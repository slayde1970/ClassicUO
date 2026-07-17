// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using TEF.Core;
using TEF.Input;
using TEF.Persistence;
using TEF.UI;
using TEF.UI.Controls;
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

        // Extra blocks kept beyond the current view range before
        // WorldMap.EvictFarBlocks lets a block go - hysteresis so a block
        // just out of sight isn't evicted and immediately re-read the
        // moment the player nudges back toward it.
        private const int EvictionMarginBlocks = 4;

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
        private readonly AnimatedStatics _animatedStatics = new();
        private WorldMap _map;
        private bool _drawStatics = true;
        private bool _drawMapStatics = true; // F7 - independent of F6, entities always draw regardless of either

        // What the cursor was over, produced by Draw and consumed on the next
        // frame's Update (one-frame lag - see PickResult). Tracked separately
        // so game code can ask for either independently - an entity can be
        // standing on any tile, and scripting needs both (e.g. "chop this
        // tree" wants the entity; "walk here" wants the ground tile
        // underneath it, entity or not).
        private PickResult _entityPick;
        private PickResult _tilePick;

        private readonly DebugHud _hud = new();

        // First real (non-debug) piece of UI built on the control system -
        // a "Resources" panel showing wood harvested this session, with a
        // Reset button. _woodCollected is a placeholder counter, not a real
        // inventory system (that's later gameplay-design scope) - just
        // enough live state to prove Panel+Label+Button+click routing all
        // work together end to end.
        private readonly UIManager _ui = new();
        private readonly Panel _resourcePanel = new() { Width = 170, Height = 78 };
        private readonly Label _woodLabel = new() { X = 10, Y = 30 };
        private int _woodCollected;

        // See Design/prd-persistence.md 4.4 - autosave uses render Delta (not
        // the fixed sim tick) since its timing has no gameplay-determinism
        // requirement; a simple accumulator is enough.
        private const float AutosaveIntervalSeconds = 60f;
        private float _autosaveTimer;

        public bool PlayMusicOnStart = true;
        const int DEFAULT_MUSIC = 8;

        // Null = fresh session (today's spawn tile + debug trees). Non-null =
        // restore this exact state instead - see Load()/RestoreFromSave().
        private readonly SaveData _saveData;

        public WorldScene(GameController game, SaveData saveData = null) : base(game)
        {
            _entityRenderer = new EntityRenderSystem(_entities, game.Assets);
            _saveData = saveData;
        }

        private void BuildResourcePanel()
        {
            _resourcePanel.Children.Add(new Label { Text = "Resources", X = 10, Y = 8 });
            _resourcePanel.Children.Add(_woodLabel);

            var resetButton = new Button("Reset") { X = 10, Y = 52 };
            resetButton.Clicked += () => _woodCollected = 0;
            _resourcePanel.Children.Add(resetButton);

            _ui.Add(_resourcePanel);
        }

        public override void Load()
        {
            base.Load();

            // Controls that measure text (Button, Label) need Fonts already
            // initialized (GameController.LoadContent) - the scene
            // constructor runs before that, so building the UI has to wait
            // until here.
            BuildResourcePanel();

            Camera.Zoom = 1f;
            _map = new WorldMap(Game.Assets, MapIndex);
            _animatedStatics.Initialize(Game.Assets);

            if (_saveData != null)
            {
                RestoreFromSave(_saveData);
            }
            else
            {
                _player.Spawn(_map, SpawnTile);
                SpawnDebugTrees();
            }
        }

        private void RestoreFromSave(SaveData data)
        {
            _player.RestoreState(new Vector2(data.Player.X, data.Player.Y), data.Player.Z, (Direction)data.Player.Facing);
            Game.World.Restore(data.Clock.Day, data.Clock.TimeOfDay);
            _woodCollected = data.WoodCollected;

            foreach (var entity in data.Entities)
            {
                int id = _entities.CreateEntity();

                _entities.Transforms[id] = new Transform { WorldPosition = new Vector2(entity.X, entity.Y), Z = entity.Z };
                _entities.Appearances[id] = new Appearance { Graphic = entity.Graphic, Hue = entity.Hue, Height = entity.Height };

                if (entity.HasHarvestable)
                {
                    _entities.Harvestables[id] = new Harvestable
                    {
                        Resource = entity.Resource,
                        YieldRemaining = entity.YieldRemaining,
                        YieldMax = entity.YieldMax,
                        AvailableGraphic = entity.AvailableGraphic,
                        DepletedGraphic = entity.DepletedGraphic,
                        IsDepleted = entity.IsDepleted,
                        RespawnCountdown = entity.RespawnCountdown,
                        RespawnDuration = entity.RespawnDuration,
                    };
                }

                if (entity.HasInteractable)
                {
                    _entities.Interactables[id] = new Interactable();
                }
            }
        }

        /// <summary>Builds a SaveData snapshot of current runtime state and writes it - called by the autosave timer (Update) and GameController.OnExiting.</summary>
        public void SaveGame()
        {
            var data = new SaveData
            {
                MapIndex = MapIndex,
                Player = new PlayerSaveData
                {
                    X = _player.WorldPosition.X,
                    Y = _player.WorldPosition.Y,
                    Z = _player.Z,
                    Facing = (byte)_player.Facing,
                },
                Clock = new WorldClockSaveData
                {
                    Day = Game.World.Day,
                    TimeOfDay = Game.World.TimeOfDay,
                },
                WoodCollected = _woodCollected,
                Entities = new List<EntitySaveData>(),
            };

            foreach (var (id, transform) in _entities.Transforms)
            {
                var appearance = _entities.Appearances[id];

                var entityData = new EntitySaveData
                {
                    X = transform.WorldPosition.X,
                    Y = transform.WorldPosition.Y,
                    Z = transform.Z,
                    Graphic = appearance.Graphic,
                    Hue = appearance.Hue,
                    Height = appearance.Height,
                };

                if (_entities.Harvestables.TryGetValue(id, out var harvestable))
                {
                    entityData.HasHarvestable = true;
                    entityData.Resource = harvestable.Resource;
                    entityData.YieldRemaining = harvestable.YieldRemaining;
                    entityData.YieldMax = harvestable.YieldMax;
                    entityData.AvailableGraphic = harvestable.AvailableGraphic;
                    entityData.DepletedGraphic = harvestable.DepletedGraphic;
                    entityData.IsDepleted = harvestable.IsDepleted;
                    entityData.RespawnCountdown = harvestable.RespawnCountdown;
                    entityData.RespawnDuration = harvestable.RespawnDuration;
                }

                entityData.HasInteractable = _entities.Interactables.ContainsKey(id);

                data.Entities.Add(entityData);
            }

            SaveManager.Save(data);
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

            // Matches ClassicUO.Client's GameScene.Update calling
            // AnimatedStaticsManager.Process() every frame (not the fixed
            // sim tick) - it's internally time-gated (Time.Ticks) so this
            // is cheap on frames where nothing is actually due to advance.
            _animatedStatics.Update(Game.Assets);

            _player.Update(input, _map);

            if (input.ScrollDelta != 0)
            {
                Camera.Zoom += input.ScrollDelta > 0 ? Camera.ZoomStep : -Camera.ZoomStep;
            }

            if (PlayMusicOnStart || input.IsActionPressed(GameAction.Interact))
            {
                Game.Audio.PlayMusic(DEFAULT_MUSIC); // "stones2" - the classic-era login theme, ships with every client
            }

            if (input.IsActionPressed(GameAction.ToggleStatics))
            {
                _drawStatics = !_drawStatics;
            }

            if (input.IsActionPressed(GameAction.ToggleMapStatics))
            {
                _drawMapStatics = !_drawMapStatics;
            }

            if (input.IsActionPressed(GameAction.ToggleDebugInfo))
            {
                _hud.ShowDebugInfo = !_hud.ShowDebugInfo;
            }

            if (input.IsActionPressed(GameAction.ToggleFpsCounter))
            {
                _hud.ShowFps = !_hud.ShowFps;
            }

            // Anchor the resource panel to the top-right corner (recomputed
            // every frame so a window resize doesn't leave it stranded).
            _resourcePanel.X = Camera.Bounds.Width - _resourcePanel.Width - 5;
            _resourcePanel.Y = 10;
            _woodLabel.Text = $"Wood: {_woodCollected}";

            _ui.Update(input);

            // Left-click harvests whatever entity the cursor is actually over
            // (resolved by the previous frame's Draw via mouse-picking) -
            // unless the click actually landed on a UI panel (e.g. the
            // Reset button), which should never also chop a tree behind it.
            if (!_ui.IsMouseOverUI
                && input.IsMousePressed(MouseButton.Left)
                && _entityPick.Kind == PickKind.Entity
                && _entities.Harvestables.ContainsKey(_entityPick.EntityId)
                && HarvestSystem.TryHarvest(_entities, _entityPick.EntityId))
            {
                _woodCollected++;
            }

            _autosaveTimer += Time.Delta;
            if (_autosaveTimer >= AutosaveIntervalSeconds)
            {
                _autosaveTimer = 0f;
                SaveGame();
            }

            Camera.Update(true, Time.Delta, input.MousePosition);

            // Surface the live tile position in the title bar so it's easy to
            // cross-reference a spot against the real ClassicUO client. UO tile
            // coords are integers; WorldPosition is fractional, so floor it.
            int tileX = (int)Math.Floor(_player.WorldPosition.X);
            int tileY = (int)Math.Floor(_player.WorldPosition.Y);

            // Chunk cache eviction (Tier 3 #8) - keep radius is the current
            // view range (in blocks) plus a margin, so a block on screen is
            // never evicted, and one just out of view isn't immediately
            // re-read the moment the player nudges back toward it.
            int keepRadiusBlocks = (ComputeViewRange() / WorldMap.BlockSize) + EvictionMarginBlocks;
            _map.EvictFarBlocks(tileX, tileY, keepRadiusBlocks);

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

        // Resource respawn hangs off the fixed simulation tick (Tier 3 #6),
        // not Update's variable render Delta, so timers behave the same
        // regardless of framerate.
        public override void FixedUpdate(float fixedDelta)
        {
            HarvestSystem.Update(_entities, fixedDelta);
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
                Camera.MouseToWorldPosition(), out _entityPick, out _tilePick,
                _animatedStatics, _drawStatics && _drawMapStatics);

            batcher.End();

            // Screen-space HUD/UI - each has its own Begin/End (no camera
            // matrix), drawn after the world so they always sit on top.
            _hud.Draw(batcher, _tilePick, _entityPick, Game.World, _map, _tiles);
            _ui.Draw(batcher);
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
