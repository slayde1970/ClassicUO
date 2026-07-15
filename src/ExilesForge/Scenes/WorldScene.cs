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

        // Tree/stump graphics for the debug Harvestables spawned in Load() -
        // a stand-in for a real world-populate step; harvested by clicking
        // (mouse-picking) them.
        private const ushort DebugTreeGraphic = 0x0CCA;
        private const ushort DebugStumpGraphic = 0x0E59; // ClassicUO's Constants.TREE_REPLACE_GRAPHIC

        private readonly PlayerEntity _player = new();
        private readonly TileRenderer _tiles = new();
        private readonly EntityWorld _entities = new();
        private readonly EntityRenderSystem _entityRenderer;
        private WorldMap _map;
        private bool _drawStatics = true;

        // What the cursor was over, produced by Draw and consumed on the next
        // frame's Update (one-frame lag - see PickResult). Tracked separately
        // so game code can ask for either independently - an entity can be
        // standing on any tile, and scripting needs both (e.g. "chop this
        // tree" wants the entity; "walk here" wants the ground tile
        // underneath it, entity or not).
        private PickResult _entityPick;
        private PickResult _tilePick;

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

            SpawnDebugTrees();
        }

        private void SpawnDebugTrees()
        {
            // A small cluster near the spawn point so mouse-picking can be
            // tested against several adjacent, overlapping trees (pick the
            // right one, per-pixel through the branches, etc.).
            Span<Vector2> offsets = [new(3f, 0f), new(4f, 1f), new(2f, 2f)];

            byte height = Game.Assets.Files.TileData.StaticData[DebugTreeGraphic].Height;

            foreach (var offset in offsets)
            {
                var position = SpawnTile + offset;
                int tx = (int)Math.Floor(position.X);
                int ty = (int)Math.Floor(position.Y);
                sbyte z = _map.ResolveSpawnZ(tx, ty);

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
            }
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

            // Left-click harvests whatever entity the cursor is actually over
            // (resolved by the previous frame's Draw via mouse-picking).
            if (input.IsMousePressed(MouseButton.Left)
                && _entityPick.Kind == PickKind.Entity
                && _entities.Harvestables.ContainsKey(_entityPick.EntityId))
            {
                HarvestSystem.TryHarvest(_entities, _entityPick.EntityId);
            }

            HarvestSystem.Update(_entities, Time.Delta);

            Camera.Update(true, Time.Delta, input.MousePosition);

            // Surface the live tile position in the title bar so it's easy to
            // cross-reference a spot against the real ClassicUO client. UO tile
            // coords are integers; WorldPosition is fractional, so floor it.
            int tileX = (int)Math.Floor(_player.WorldPosition.X);
            int tileY = (int)Math.Floor(_player.WorldPosition.Y);

            // Report the entity under the cursor when there is one; otherwise
            // fall back to the ground tile it's standing on (or the bare
            // tile, if no entity at all). Both picks stay independently
            // available on _entityPick/_tilePick for future scripting.
            var headline = _entityPick.Kind != PickKind.None ? _entityPick : _tilePick;
            string hover = headline.Kind == PickKind.None
                ? ""
                : $"  |  hover: {headline.Kind} {headline.Name} 0x{headline.Graphic:X4} @ ({headline.TileX}, {headline.TileY})";
            Game.Window.Title = $"The Exile's Forge  -  map {MapIndex}  ({tileX}, {tileY}, {_player.Z}){hover}";
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
            // TileRenderer.Draw. Mouse picking rides along the same pass: the
            // cursor is converted into pre-matrix (world-draw) space via
            // Camera.MouseToWorldPosition so it lines up with sprite positions.
            _tiles.Draw(
                batcher, _map, _entityRenderer, _player, ComputeViewRange(), screenCenter,
                Camera.MouseToWorldPosition(), out _entityPick, out _tilePick, _drawStatics);

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
