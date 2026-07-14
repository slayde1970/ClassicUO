// SPDX-License-Identifier: BSD-2-Clause

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
    /// world/map scrolls under a fixed, screen-centered player instead (see
    /// ClassicUO.Renderer.Camera, which has no position field at all).
    /// `WorldPosition` accumulates where the player "is" for whenever map/tile
    /// streaming is added; it does not affect where the sprite is drawn.
    /// </summary>
    public sealed class PlayerEntity
    {
        private const ushort BodyMaleHuman = 0x0190;
        private const int MillisecondsPerFrame = 150; // matches ClassicUO's WALKING_DELAY

        private float _frameTimeAccumulator;
        private int _frameIndex;

        public ushort Graphic { get; set; } = BodyMaleHuman;
        public Vector2 WorldPosition { get; private set; }
        public Direction Facing { get; private set; } = Direction.South;
        public bool IsMoving { get; private set; }

        public void Update(InputManager input, float moveSpeed = 120f)
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
                Facing = DirectionHelper.FromVector(move);

                if (input.IsActionDown(GameAction.Sprint))
                {
                    move *= 1.75f;
                }

                WorldPosition += move * moveSpeed * Time.Delta;
            }

            AdvanceAnimationFrame();
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

        public void Draw(UltimaBatcher2D batcher, GameAssets assets)
        {
            byte action = (byte)(IsMoving ? PeopleAnimationGroup.WalkUnarmed : PeopleAnimationGroup.Stand);
            byte dir = (byte)Facing;
            bool mirror = false;

            assets.Animations.GetAnimDirection(ref dir, ref mirror);

            var frames = assets.Animations.GetAnimationFrames(Graphic, action, dir, out ushort hue, out _);
            if (frames.IsEmpty)
            {
                return;
            }

            ref readonly var sprite = ref frames[_frameIndex % frames.Length];
            if (sprite.Texture == null)
            {
                return;
            }

            // Always anchored at the world origin - the camera keeps the
            // player centered on screen, so this is really "screen center,
            // feet-aligned", not the player's actual world coordinates.
            float x = mirror
                ? -(sprite.UV.Width - sprite.Center.X)
                : -sprite.Center.X;
            float y = -(sprite.UV.Height + sprite.Center.Y);

            batcher.Draw(
                sprite.Texture,
                new Vector2(x, y),
                sprite.UV,
                ShaderHueTranslator.GetHueVector(hue),
                0f,
                Vector2.Zero,
                1f,
                mirror ? SpriteEffects.FlipHorizontally : SpriteEffects.None,
                0f
            );
        }
    }
}
