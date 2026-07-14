// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Renderer;
using TEF.Core;
using TEF.Input;
using TEF.World;

namespace TEF.Scenes
{
    /// <summary>
    /// First playable surface: proves out the asset -> renderer -> input
    /// pipeline with a player entity the camera zooms around. This is the
    /// seam where map/tile rendering (ported from
    /// ClassicUO.Client/Game/Scenes/GameScene.cs) and the survival/crafting
    /// systems will attach as the project grows.
    /// </summary>
    public sealed class WorldScene : Scene
    {
        private readonly PlayerEntity _player = new();

        public WorldScene(GameController game) : base(game)
        {
        }

        public override void Update(InputManager input)
        {
            base.Update(input);

            Camera.Bounds = Game.GraphicsDevice.Viewport.Bounds;

            _player.Update(input);

            if (input.ScrollDelta != 0)
            {
                Camera.Zoom += input.ScrollDelta > 0 ? Camera.ZoomStep : -Camera.ZoomStep;
            }

            if (input.IsActionPressed(GameAction.Interact))
            {
                Game.Audio.PlayMusic(8); // "stones2" - the classic-era login theme, ships with every client
            }

            Camera.Update(true, Time.Delta, input.MousePosition);
        }

        public override void Draw(UltimaBatcher2D batcher)
        {
            base.Draw(batcher);

            // World-space content must be drawn under the camera's view
            // transform or it renders at native pixel size with no zoom -
            // see GameScene.Draw in ClassicUO.Client for the same pattern
            // (`batcher.Begin(null, Camera.ViewTransformMatrix)`).
            batcher.Begin(null, Camera.ViewTransformMatrix);

            _player.Draw(batcher, Game.Assets);

            batcher.End();
        }
    }
}
