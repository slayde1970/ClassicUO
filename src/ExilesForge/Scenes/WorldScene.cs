// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using TEF.Core;
using TEF.Input;
using TEF.World;

namespace TEF.Scenes
{
    /// <summary>
    /// First playable surface: proves out the asset -> renderer -> input ->
    /// map pipeline with a player entity standing on real UO land tiles. The
    /// seam where statics, height-blended tile corners (see TileRenderer),
    /// and the survival/crafting systems will attach as the project grows.
    /// </summary>
    public sealed class WorldScene : Scene
    {
        // Britain Bank, Felucca - an arbitrary but well-known, always-valid
        // spawn point. Stand-in until there's real character-select/spawn
        // logic to pick this from.
        private static readonly Vector2 SpawnTile = new(1436f, 1443f);
        private const int TileViewRange = 15;

        private readonly PlayerEntity _player = new();
        private readonly TileRenderer _tiles = new(mapIndex: 0);

        public WorldScene(GameController game) : base(game)
        {
        }

        public override void Load()
        {
            base.Load();

            Camera.Zoom = 1f;
            _player.Teleport(SpawnTile);
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

            // Camera only handles zoom/peek, not centering (world (0,0) maps
            // to screen (0,0), the viewport's top-left) - so every world-space
            // draw needs the viewport's center added explicitly to appear
            // centered on the player.
            var screenCenter = new Vector2(Camera.Bounds.Width / 2f, Camera.Bounds.Height / 2f);

            _tiles.Draw(batcher, Game.Assets, _player.WorldPosition, TileViewRange, screenCenter);
            _player.Draw(batcher, Game.Assets, screenCenter);

            batcher.End();
        }
    }
}
