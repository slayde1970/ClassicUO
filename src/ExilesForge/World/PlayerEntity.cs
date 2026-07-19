// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Assets;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using UOA.Assets;
using UOA.Core;
using UOA.Input;
using UOA.World;
using TEF.Input;

namespace TEF.World
{
    /// <summary>
    /// A minimal stand-in for ClassicUO's Game/GameObjects/PlayerMobile.cs:
    /// no stats, equipment, or network sync, just enough to drive a body
    /// animation off input. The player is always drawn at the world origin -
    /// same convention UO itself uses, where the camera never pans and the
    /// world/map scrolls under a fixed, screen-centered player instead. Note
    /// that ClassicUO.Renderer.Camera only handles zoom/peek, not centering -
    /// its view matrix maps world (0,0) to screen (0,0), the viewport's
    /// top-left corner. Centering is the caller's job (in ClassicUO.Client,
    /// GameScene computes it from the player's own screen position); here,
    /// WorldScene passes the viewport's center in as `screenCenterOffset`.
    /// `WorldPosition` is in fractional UO tile coordinates (not pixels) so
    /// it lines up directly with TileRenderer's map lookups and iso
    /// projection; it does not affect where the sprite itself is drawn.
    /// </summary>
    public sealed class PlayerEntity : IWorldPlayer
    {
        private const ushort BodyMaleHuman = 0x0190;

        // Per-frame animation delay: run cycles faster than walk (Tier 4.6).
        // Idle/stand uses the walk delay (harmless - stand is ~static).
        private const int WalkMillisecondsPerFrame = 120;
        private const int RunMillisecondsPerFrame = 80;

        private const float ZOffsetDecayRate = 18f; // higher = faster ease-out
        private const float ZOffsetSnapThreshold = 0.05f; // pixels - avoids perpetual sub-pixel jitter

        private float _frameTimeAccumulator;
        private int _frameIndex;

        // Purely cosmetic pixel offset on the player's OWN sprite draw
        // position - never read by TileRenderer, which always uses the true,
        // instant Z for the world's vertical offset (see Z's own doc
        // comment for why an earlier attempt that eased Z itself, shared
        // with the world, caused misalignment). On a height change, this is
        // set to the same delta the ground just visually jumped by
        // (TileRenderer's isoOrigin shifts by Z*4 px), so the sprite starts
        // the transition looking exactly as it did before the step, then
        // decays to 0 over a fraction of a second - easing the step in
        // instead of popping, without the two ever disagreeing about where
        // the ground actually is.
        private float _visualZOffset;

        public ushort Graphic { get; set; } = BodyMaleHuman;

        /// <summary>Body/skin hue applied to the whole sprite (0 = the art's own default grey body). Defaults to a skin tone; overrides the animation's default hue when non-zero. Future character customization sets this.</summary>
        public ushort Hue { get; set; } = 0x83EA;

        public Vector2 WorldPosition { get; private set; }
        public Direction Facing { get; private set; } = Direction.South;
        public bool IsMoving { get; private set; }

        /// <summary>True while moving at sprint/run speed - drives the run animation group + faster frame rate (Tier 4.6).</summary>
        public bool IsRunning { get; private set; }

        /// <summary>
        /// Surface Z the player is standing on (UO z units), snapped per tile.
        /// Used as-is (not eased) for the world's vertical draw offset in
        /// TileRenderer.Draw - an earlier version eased this visually, which
        /// caused the ground to briefly render at the wrong height relative
        /// to the player while walking across a height boundary (invisible
        /// once the ease caught up at rest, so it read as intermittent
        /// clipping). Z itself still snaps instantly for this reason; a
        /// purely cosmetic ease-in on top now lives entirely in
        /// _visualZOffset (applied only to the player's own sprite draw
        /// position, never read by TileRenderer) so a step still LOOKS
        /// smooth without the two ever disagreeing about where the ground
        /// actually is.
        /// </summary>
        public sbyte Z { get; private set; }

        public void Spawn(WorldMap map, Vector2 tilePosition)
        {
            WorldPosition = tilePosition;
            Z = map.ResolveSpawnZ((int)MathF.Floor(tilePosition.X), (int)MathF.Floor(tilePosition.Y));
            _visualZOffset = 0f;
        }

        /// <summary>Restores exact saved state (see Persistence/SaveManager) - unlike Spawn, does not recompute Z via WorldMap.ResolveSpawnZ since the exact value is already known.</summary>
        public void RestoreState(Vector2 worldPosition, sbyte z, Direction facing)
        {
            WorldPosition = worldPosition;
            Z = z;
            Facing = facing;
            _visualZOffset = 0f;
        }

        /// <summary>WASD movement. Returns true if a direction was held this frame (so the caller can fall back to mouse movement only when WASD is idle). Also advances the animation/Z-ease each frame regardless.</summary>
        /// <param name="moveSpeed">Tiles per second at normal (non-sprint) pace.</param>
        public bool Update(InputManager input, WorldMap map, float moveSpeed = 4f)
        {
            var move = Vector2.Zero;

            if (input.IsActionDown(GameAction.MoveForward)) move.Y -= 1;
            if (input.IsActionDown(GameAction.MoveBack)) move.Y += 1;
            if (input.IsActionDown(GameAction.StrafeLeft)) move.X -= 1;
            if (input.IsActionDown(GameAction.StrafeRight)) move.X += 1;

            bool moved = move != Vector2.Zero;

            if (moved)
            {
                MoveScreen(map, move, input.IsActionDown(GameAction.Sprint), moveSpeed);
            }
            else
            {
                IsMoving = false;
                IsRunning = false;
            }

            AdvanceAnimationFrame();
            DecayVisualZOffset();
            return moved;
        }

        /// <summary>
        /// Moves toward an ON-SCREEN direction (screen space: +X right, +Y
        /// down; e.g. (0,-1) = up = north) at walk or sprint speed, setting
        /// Facing. Shared by WASD (Update) and mouse movement (Tier 4.6) so both
        /// input methods run through one facing/collision path.
        /// </summary>
        public void MoveScreen(WorldMap map, Vector2 screenMove, bool sprint, float moveSpeed = 4f)
        {
            if (screenMove == Vector2.Zero)
            {
                IsMoving = false;
                IsRunning = false;
                return;
            }

            IsMoving = true;
            IsRunning = sprint;

            var move = screenMove;
            move.Normalize();

            // Facing is read straight off the on-screen direction so the
            // character looks the way it's heading.
            Facing = DirectionHelper.FromVector(move);

            TryMove(map, ToTileDelta(move) * (sprint ? moveSpeed * 1.75f : moveSpeed) * Time.Delta);
        }

        /// <summary>The Direction the player would face heading toward an on-screen direction - used to compare against current Facing for the right-click tap (Tier 4.6).</summary>
        public Direction FacingFor(Vector2 screenDir) => DirectionHelper.FromVector(screenDir);

        /// <summary>Turns to face an on-screen direction without moving (right-click tap when not already facing that way).</summary>
        public void FaceScreen(Vector2 screenDir)
        {
            if (screenDir != Vector2.Zero)
            {
                Facing = DirectionHelper.FromVector(screenDir);
                IsMoving = false;
                IsRunning = false;
            }
        }

        /// <summary>Takes a single step toward an on-screen direction (right-click tap when already facing that way).</summary>
        public void StepScreen(WorldMap map, Vector2 screenDir)
        {
            if (screenDir == Vector2.Zero)
            {
                return;
            }

            var move = screenDir;
            move.Normalize();
            Facing = DirectionHelper.FromVector(move);
            IsMoving = true;
            IsRunning = false; // a single tap-step is a walk
            TryMove(map, ToTileDelta(move));
        }

        // WorldPosition is in UO tile coords, which the renderer projects
        // isometrically: iso(x, y) = ((x - y), (x + y)). A screen direction has
        // to be converted to a tile delta with the inverse of that projection,
        // or the map would scroll diagonally relative to the facing (screen
        // "up" is a tile diagonal in iso space, not tile -Y). Inverse of
        // iso(dx, dy) = (sx, sy) is (dx, dy) = ((sx + sy), (sy - sx)).
        private static Vector2 ToTileDelta(Vector2 screenMove)
        {
            var tileDir = new Vector2(screenMove.X + screenMove.Y, screenMove.Y - screenMove.X);
            if (tileDir != Vector2.Zero)
            {
                tileDir.Normalize();
            }

            return tileDir;
        }

        private void DecayVisualZOffset()
        {
            if (_visualZOffset == 0f)
            {
                return;
            }

            _visualZOffset *= MathF.Exp(-ZOffsetDecayRate * Time.Delta);

            if (MathF.Abs(_visualZOffset) < ZOffsetSnapThreshold)
            {
                _visualZOffset = 0f;
            }
        }

        /// <summary>
        /// Attempts to move by <paramref name="delta"/> tiles, respecting
        /// walkability. If the full move would enter a blocked tile, it retries
        /// the X and Y components separately so the player slides along walls
        /// instead of sticking. Movement within the current tile is always
        /// allowed; only crossing into a new tile is tested.
        /// </summary>
        private void TryMove(WorldMap map, Vector2 delta)
        {
            if (TryStep(map, new Vector2(delta.X, delta.Y)))
            {
                return;
            }

            // Blocked diagonally - try sliding along one axis, then the other.
            if (delta.X != 0f && TryStep(map, new Vector2(delta.X, 0f)))
            {
                return;
            }

            if (delta.Y != 0f)
            {
                TryStep(map, new Vector2(0f, delta.Y));
            }
        }

        private bool TryStep(WorldMap map, Vector2 delta)
        {
            var candidate = WorldPosition + delta;

            int fromX = (int)MathF.Floor(WorldPosition.X);
            int fromY = (int)MathF.Floor(WorldPosition.Y);
            int toX = (int)MathF.Floor(candidate.X);
            int toY = (int)MathF.Floor(candidate.Y);

            // Same tile - no walkability change, just slide within it.
            if (toX == fromX && toY == fromY)
            {
                WorldPosition = candidate;
                return true;
            }

            if (map.TryGetStandZ(toX, toY, Z, out sbyte newZ))
            {
                WorldPosition = candidate;

                if (newZ != Z)
                {
                    _visualZOffset += (newZ - Z) * 4f;
                }

                Z = newZ;
                return true;
            }

            return false;
        }

        private void AdvanceAnimationFrame()
        {
            _frameTimeAccumulator += Time.Delta * 1000f;

            if (_frameTimeAccumulator < (IsRunning ? RunMillisecondsPerFrame : WalkMillisecondsPerFrame))
            {
                return;
            }

            _frameTimeAccumulator = 0f;
            _frameIndex++;
        }

        public void Draw(UltimaBatcher2D batcher, GameAssets assets, Vector2 screenCenterOffset)
        {
            if (!TryResolveFrame(assets, out var sprite, out var localOrigin, out bool mirror, out ushort hue, out _, out _, out _))
            {
                return;
            }

            // Mobile priority (Z + 1, matching Chunk.AddGameObject) fed through
            // the moving-object depth (Tier 4.6): rounds the iso diagonal off
            // the player's FRACTIONAL position so, once past a tile's centre,
            // the player sorts in front of the land tile they're stepping onto
            // instead of that tile's top corner clipping their feet. See
            // DepthKey.ComputeMoving.
            float depth = DepthKey.ComputeMoving(WorldPosition.X, WorldPosition.Y, Z + 1);

            batcher.Draw(
                sprite.Texture,
                localOrigin + screenCenterOffset,
                sprite.UV,
                ShaderHueTranslator.GetHueVector(hue),
                0f,
                Vector2.Zero,
                1f,
                mirror ? SpriteEffects.FlipHorizontally : SpriteEffects.None,
                depth
            );
        }

        /// <summary>
        /// Bounding-box hit test for the player's currently-drawn sprite
        /// (not per-pixel, unlike TileRenderer's static/entity picking).
        /// A first attempt used Animations.PixelCheck (the same pixel-alpha
        /// mask ClassicUO's mobile mouse-selection uses), but its key is
        /// built from frame/body-conversion bookkeeping with more moving
        /// parts than Art's plain per-graphic-id key, and it never
        /// registered a hit despite the geometry matching Draw exactly -
        /// not confidently debuggable without runtime introspection. Bounding
        /// box is an acceptable simplification here: a standing humanoid is
        /// mostly opaque within its box, unlike trees/foliage where the gaps
        /// matter a lot for clicking through to what's behind them. Shares
        /// frame resolution with <see cref="Draw"/> so the hit test can
        /// never drift from what's actually on screen.
        /// </summary>
        public bool TryPick(GameAssets assets, Point cursorPosition, Vector2 screenCenterOffset)
        {
            if (!TryResolveFrame(assets, out var sprite, out var localOrigin, out _, out _, out _, out _, out _))
            {
                return false;
            }

            var drawPos = localOrigin + screenCenterOffset;
            int lx = cursorPosition.X - (int)drawPos.X;
            int ly = cursorPosition.Y - (int)drawPos.Y;

            return lx >= 0 && ly >= 0 && lx < sprite.UV.Width && ly < sprite.UV.Height;
        }

        /// <summary>
        /// Resolves the current animation frame and its screen-local draw
        /// origin (relative to the player's fixed screen anchor, before
        /// adding screenCenterOffset). Shared by Draw and TryPick.
        /// </summary>
        private bool TryResolveFrame(
            GameAssets assets,
            out SpriteInfo sprite, out Vector2 localOrigin, out bool mirror,
            out ushort hue, out bool useUOP, out byte action, out byte dir)
        {
            // Stand when idle, the run cycle when sprinting, else the walk
            // cycle (Tier 4.6 - running previously reused the walk animation).
            var group = !IsMoving ? PeopleAnimationGroup.Stand
                : IsRunning ? PeopleAnimationGroup.RunUnarmed
                : PeopleAnimationGroup.WalkUnarmed;
            action = (byte)group;
            dir = (byte)Facing;
            mirror = false;

            assets.Animations.GetAnimDirection(ref dir, ref mirror);

            var frames = assets.Animations.GetAnimationFrames(Graphic, action, dir, out hue, out useUOP);

            // The player's own hue (skin tone) overrides the animation file's
            // default when set - matches how a mobile's Hue overrides its body
            // art's default in ClassicUO.
            if (Hue != 0)
            {
                hue = Hue;
            }

            if (frames.IsEmpty)
            {
                sprite = default;
                localOrigin = default;
                return false;
            }

            _frameIndex %= frames.Length;
            sprite = frames[_frameIndex];

            if (sprite.Texture == null)
            {
                localOrigin = default;
                return false;
            }

            // Always anchored at the world origin - the camera keeps the
            // player centered on screen, so this is really "screen center,
            // feet-aligned", not the player's actual world coordinates.
            float x = mirror
                ? -(sprite.UV.Width - sprite.Center.X)
                : -sprite.Center.X;
            float y = -(sprite.UV.Height + sprite.Center.Y) + _visualZOffset;
            localOrigin = new Vector2(x, y);

            return true;
        }
    }
}
