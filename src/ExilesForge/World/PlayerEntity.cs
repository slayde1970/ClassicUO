// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Assets;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TEF.Assets;
using TEF.Core;
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
    public sealed class PlayerEntity
    {
        private const ushort BodyMaleHuman = 0x0190;
        private const int MillisecondsPerFrame = 150; // matches ClassicUO's WALKING_DELAY

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
        public Vector2 WorldPosition { get; private set; }
        public Direction Facing { get; private set; } = Direction.South;
        public bool IsMoving { get; private set; }

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

        /// <param name="moveSpeed">Tiles per second at normal (non-sprint) pace.</param>
        public void Update(InputManager input, WorldMap map, float moveSpeed = 4f)
        {
            var move = Vector2.Zero;

            if (input.IsActionDown(GameAction.MoveForward)) move.Y -= 1;
            if (input.IsActionDown(GameAction.MoveBack)) move.Y += 1;
            if (input.IsActionDown(GameAction.StrafeLeft)) move.X -= 1;
            if (input.IsActionDown(GameAction.StrafeRight)) move.X += 1;

            IsMoving = move != Vector2.Zero;

            if (IsMoving)
            {
                move.Normalize();

                // `move` is the desired ON-SCREEN direction (+X right, +Y
                // down): W = up, D = right, etc. Facing is read straight off
                // it so the character looks the way the player pushed.
                Facing = DirectionHelper.FromVector(move);

                // WorldPosition is in UO tile coords, which the tile renderer
                // projects isometrically: iso(x, y) = ((x - y), (x + y)). So
                // the same screen direction has to be converted into a tile
                // delta with the inverse of that projection, or the map would
                // scroll diagonally relative to the facing (screen "up" is a
                // tile diagonal in iso space, not tile -Y). Inverse of
                // iso(dx, dy) = (sx, sy) is (dx, dy) = ((sx + sy), (sy - sx)).
                var tileDir = new Vector2(move.X + move.Y, move.Y - move.X);
                if (tileDir != Vector2.Zero)
                {
                    tileDir.Normalize();
                }

                float speed = moveSpeed;
                if (input.IsActionDown(GameAction.Sprint))
                {
                    speed *= 1.75f;
                }

                TryMove(map, tileDir * speed * Time.Delta);
            }

            AdvanceAnimationFrame();
            DecayVisualZOffset();
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

            if (_frameTimeAccumulator < MillisecondsPerFrame)
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

            batcher.Draw(
                sprite.Texture,
                localOrigin + screenCenterOffset,
                sprite.UV,
                ShaderHueTranslator.GetHueVector(hue),
                0f,
                Vector2.Zero,
                1f,
                mirror ? SpriteEffects.FlipHorizontally : SpriteEffects.None,
                0f
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
            action = (byte)(IsMoving ? PeopleAnimationGroup.WalkUnarmed : PeopleAnimationGroup.Stand);
            dir = (byte)Facing;
            mirror = false;

            assets.Animations.GetAnimDirection(ref dir, ref mirror);

            var frames = assets.Animations.GetAnimationFrames(Graphic, action, dir, out hue, out useUOP);
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
