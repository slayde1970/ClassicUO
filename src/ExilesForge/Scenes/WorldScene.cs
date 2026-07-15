// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using TEF.Core;
using TEF.Input;
using TEF.World;
using TEF.World.Entities;

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

        //private static readonly Vector2 SpawnTile = new(1395f, 1409f); // original spot
        private static readonly Vector2 SpawnTile = new(1591f, 1518f); // bank

        // Iso constants: a tile steps 44px (2 * 22) diagonally per unit change
        // in the (x - y)/(x + y) axes, so the on-screen coverage of a square
        // tile region [-R, R] is a screen-space diamond of half-extent 44*R.
        private const int TileDiagonalPixels = 44;
        // A couple of extra rings so tall statics near the edge (whose art
        // extends well above their tile) don't pop in/out at the border.
        private const int ViewRangeMargin = 8;

        private const int MapIndex = 0;

        // Tree/stump graphics for the debug Harvestable spawned in Load() -
        // proves out the entity system end to end (Design/prd-entity-system.md
        // acceptance criteria) ahead of real mouse-picking/interaction.
        private const ushort DebugTreeGraphic = 0x0CCA;
        private const ushort DebugStumpGraphic = 0x0E59; // ClassicUO's Constants.TREE_REPLACE_GRAPHIC

        private readonly PlayerEntity _player = new();
        private readonly TileRenderer _tiles = new();
        private readonly EntityWorld _entities = new();
        private readonly EntityRenderSystem _entityRenderer;
        private WorldMap _map;
        private bool _drawStatics = true;
        private int _debugTreeEntityId;

        public WorldScene(GameController game) : base(game)
        {
            _entityRenderer = new EntityRenderSystem(_entities);
        }

        public override void Load()
        {
            base.Load();

            Camera.Zoom = 1f;
            _map = new WorldMap(Game.Assets, MapIndex);
            _player.Spawn(_map, SpawnTile);

            SpawnDebugTree();
        }

        private void SpawnDebugTree()
        {
            var position = SpawnTile + new Vector2(3f, 0f);
            int tx = (int)Math.Floor(position.X);
            int ty = (int)Math.Floor(position.Y);
            sbyte z = _map.ResolveSpawnZ(tx, ty);

            byte height = Game.Assets.Files.TileData.StaticData[DebugTreeGraphic].Height;

            int id = _entities.CreateEntity();
            _entities.Transforms[id] = new Transform { WorldPosition = position, Z = z };
            _entities.Appearances[id] = new Appearance { Graphic = DebugTreeGraphic, Hue = 0, Height = height };
            _entities.Harvestables[id] = new Harvestable
            {
                Resource = ResourceType.Wood,
                YieldRemaining = 3,
                YieldMax = 3,
                AvailableGraphic = DebugTreeGraphic,
                DepletedGraphic = DebugStumpGraphic,
                IsDepleted = false,
                RespawnDuration = 10f,
            };
            _entities.Interactables[id] = new Interactable();

            _debugTreeEntityId = id;
        }

        public override void Update(InputManager input)
        {
            base.Update(input);

            Camera.Bounds = Game.GraphicsDevice.Viewport.Bounds;

            _player.Update(input, _map);

            if (input.ScrollDelta != 0)
            {
                Camera.Zoom += input.ScrollDelta > 0 ? Camera.ZoomStep : -Camera.ZoomStep;
            }

            if (input.IsActionPressed(GameAction.Interact))
            {
                Game.Audio.PlayMusic(8); // "stones2" - the classic-era login theme, ships with every client
            }

            if (input.IsActionPressed(GameAction.ToggleStatics))
            {
                _drawStatics = !_drawStatics;
            }

            // TEMP debug wiring for the entity system PRD's acceptance
            // criteria (prove deplete/respawn end to end) ahead of real
            // mouse-picking/interaction (Tier 2). Left click harvests the
            // debug tree regardless of where the player/cursor actually are.
            if (input.IsMousePressed(MouseButton.Left))
            {
                HarvestSystem.TryHarvest(_entities, _debugTreeEntityId);
            }

            HarvestSystem.Update(_entities, Time.Delta);

            Camera.Update(true, Time.Delta, input.MousePosition);

            // Surface the live tile position in the title bar so it's easy to
            // cross-reference a spot against the real ClassicUO client. UO tile
            // coords are integers; WorldPosition is fractional, so floor it.
            int tileX = (int)Math.Floor(_player.WorldPosition.X);
            int tileY = (int)Math.Floor(_player.WorldPosition.Y);
            Game.Window.Title = $"The Exile's Forge  -  map {MapIndex}  ({tileX}, {tileY}, {_player.Z})";
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

            // The player is drawn inside the tile pass (interleaved on its own
            // tile) so statics in front of it can occlude it - see
            // TileRenderer.Draw.
            _tiles.Draw(batcher, _map, _entityRenderer, _player, ComputeViewRange(), screenCenter, _drawStatics);

            batcher.End();
        }

        /// <summary>
        /// Tiles to draw out from the player in each direction, sized so the
        /// rendered diamond always covers the viewport rectangle - including
        /// its corners - at the current zoom.
        ///
        /// The camera scales world pixels by 1/Zoom, so a screen half-extent
        /// of `half` px corresponds to `half * Zoom` world px. A tile region
        /// [-R, R] covers a screen diamond `|x| + |y| <= 44*R` (world px), so
        /// the demanding viewport corner (halfW + halfH away in that L1 sense)
        /// is covered when R >= Zoom * (halfW + halfH) / 44.
        /// </summary>
        private int ComputeViewRange()
        {
            float halfW = Camera.Bounds.Width / 2f;
            float halfH = Camera.Bounds.Height / 2f;

            int range = (int)Math.Ceiling(Camera.Zoom * (halfW + halfH) / TileDiagonalPixels);

            return range + ViewRangeMargin;
        }
    }
}
