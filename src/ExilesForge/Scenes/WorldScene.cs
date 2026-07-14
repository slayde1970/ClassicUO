// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using TEF.Core;
using TEF.Input;

namespace TEF.Scenes
{
    /// <summary>
    /// First playable surface: proves out the asset -> renderer -> input
    /// pipeline with a static art sprite the camera can pan/zoom around.
    /// This is the seam where map/tile rendering (ported from
    /// ClassicUO.Client/Game/Scenes/GameScene.cs), entities, and the
    /// survival/crafting systems will attach as the project grows.
    /// </summary>
    public sealed class WorldScene : Scene
    {
        private const uint DemoArtId = 0x0EED; // a static art tile, just to prove the pipeline

        // Camera (see ClassicUO.Renderer.Camera) only handles zoom + a mouse
        // "peek" offset - it has no world-position concept, because in UO the
        // view always stays centered on the player and the world scrolls
        // instead. Until there's a player entity to center on, WASD just
        // shifts where the demo sprite is drawn.
        private Vector2 _panOffset;

        public WorldScene(GameController game) : base(game)
        {
        }

        public override void Update(InputManager input)
        {
            base.Update(input);

            Camera.Bounds = Game.GraphicsDevice.Viewport.Bounds;

            const float panSpeed = 200f;
            var move = Vector2.Zero;

            if (input.IsActionDown(GameAction.MoveForward)) move.Y -= 1;
            if (input.IsActionDown(GameAction.MoveBack)) move.Y += 1;
            if (input.IsActionDown(GameAction.StrafeLeft)) move.X -= 1;
            if (input.IsActionDown(GameAction.StrafeRight)) move.X += 1;

            if (move != Vector2.Zero)
            {
                move.Normalize();
                _panOffset += move * panSpeed * Core.Time.Delta;
            }

            if (input.ScrollDelta != 0)
            {
                Camera.Zoom += input.ScrollDelta > 0 ? Camera.ZoomStep : -Camera.ZoomStep;
            }

            if (input.IsActionPressed(GameAction.Interact))
            {
                Game.Audio.PlayMusic(8); // "stones2" - the classic-era login theme, ships with every client
            }

            Camera.Update(true, Core.Time.Delta, input.MousePosition);
        }

        public override void Draw(UltimaBatcher2D batcher)
        {
            base.Draw(batcher);

            // World-space content must be drawn under the camera's view
            // transform or it renders at native pixel size with no zoom -
            // see GameScene.Draw in ClassicUO.Client for the same pattern
            // (`batcher.Begin(null, Camera.ViewTransformMatrix)`).
            batcher.Begin(null, Camera.ViewTransformMatrix);

            ref readonly var sprite = ref Game.Assets.Art.GetArt(DemoArtId);
            if (sprite.Texture != null)
            {
                batcher.Draw(
                    sprite.Texture,
                    _panOffset,
                    sprite.UV,
                    ShaderHueTranslator.GetHueVector(0),
                    0f
                );
            }

            batcher.End();
        }
    }
}
