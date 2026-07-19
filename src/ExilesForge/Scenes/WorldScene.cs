// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using System.Text.Json;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using UOA.Core;
using UOA.Input;
using UOA.Persistence;
using UOA.Scenes;
using UOA.Scripting;
using TEF.Input;
using TEF.Persistence;
using UOA.UI;
using UOA.UI.Controls;
using TEF.UI;
using UOA.World;
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
        // Britain Bank, Felucca - the default spawn point when no location
        // was explicitly chosen (e.g. restoring a save doesn't need one).
        // See Scenes/SpawnSelectScene.cs for the actual character-spawn
        // location picker (Tier 4 #12).
        public static readonly Vector2 DefaultSpawnTile = new(1591f, 1518f);

        private readonly Vector2 _spawnTile;

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

        // See the SetBrightlight call in Draw() - 0f..1f, how strongly
        // stretched land's per-corner normals bump-shade sloped terrain.
        private const float TerrainShadowIntensity = 1f;

        // Extra multiplier on top of TerrainShadowIntensity's effect (see
        // IsometricWorld.fx's LightContrastBoost) - the stock shader's
        // slope-shading is subtle even at max Brightlight, so this
        // exaggerates the contrast further. 0 = stock ClassicUO look;
        // tune freely (config-file exposure is Tier 4 #11, app-shell
        // completeness - a plain constant for now).
        private const float TerrainLightContrastBoost = 0.3f;

        private const int MapIndex = 0;

        // Roof-hiding (Tier 4.5): map statics at or above (player.Z + this) are
        // hidden so the player can see into whatever they've walked under. A
        // fixed clearance above the player, not the overhead object's own Z, so
        // it doesn't flicker as roof-tile heights vary across a building.
        private const int RoofHideHeight = 10;

        // Multi-storey (Tier 4.6): a floor SURFACE at least this far above the
        // player's feet counts as an upper storey (its underside is the room's
        // ceiling), triggering the hide-whole-storey path. Above typical
        // furniture-surface heights (~8-12) but below a full UO storey (~20) so
        // it catches the second floor without mistaking a tall table for one.
        private const int UpperFloorClearance = 16;

        // Right-click movement (Tier 4.6), ClassicUO-style. Distances are the
        // cursor's pixel offset from screen centre (the player). Inside the
        // dead zone nothing happens (no clear direction); between it and the run
        // radius the player walks; beyond, it runs.
        private const float MouseMoveDeadZone = 18f;
        private const float MouseRunRadius = 120f;

        // Set when a right-click starts over UI or an interactable entity, so
        // the whole hold is suppressed (that click is reserved - a future
        // interact/context action). Cleared when the button is released.
        private bool _mouseMoveSuppressed;

        // Tier 4 #13 - tried CompareFunction.GreaterEqual here (reasoning
        // that DepthKey.Compute assigns LARGER values to things meant to
        // draw later/in front) to explain a ground-level-static (stone
        // pavers) rendering bug under the default LessEqual - but
        // GreaterEqual made EVERYTHING fail to render (worse, not better),
        // so that reasoning was wrong somewhere. Reverted to Default
        // (LessEqual) - see the still-open paver investigation in
        // next-steps.md/PRD notes for the real root cause, not yet found.
        private static readonly DepthStencilState WorldDepthStencilState = DepthStencilState.Default;

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
        private readonly DayNightOverlay _dayNight = new();
        private WorldMap _map;
        private bool _drawStatics = true;
        private bool _drawMapStatics = true; // F7 - independent of F6, entities always draw regardless of either
        private bool _terrainLightingEnabled = true; // Ctrl+L - for comparing stretched-land shading on/off
        private bool _depthTestEnabled = true; // F11 - A/B toggle for the Tier 4 #13 GPU depth-buffer work
        private bool _roofHideEnabled;  // F12 - auto-hide roofs when the player is under them; enabled on scene load (Tier 4.5)

        // What the cursor was over, produced by Draw and consumed on the next
        // frame's Update (one-frame lag - see PickResult). Tracked separately
        // so game code can ask for either independently - an entity can be
        // standing on any tile, and scripting needs both (e.g. "chop this
        // tree" wants the entity; "walk here" wants the ground tile
        // underneath it, entity or not).
        private PickResult _entityPick;
        private PickResult _tilePick;

        // See the FlushesDone/TextureSwitches capture in Draw() - debug/perf
        // investigation only.
        private int _lastWorldFlushesDone;
        private int _lastWorldTextureSwitches;

        private readonly DebugHud _hud = new();

        // First real (non-debug) piece of UI built on the control system -
        // a "Resources" panel showing wood harvested this session, with a
        // Reset button. _woodCollected is a placeholder counter, not a real
        // inventory system (that's later gameplay-design scope) - just
        // enough live state to prove Panel+Label+Button+click routing all
        // work together end to end.
        // Uses the base Scene's shared Ui layer (Tier 4.5). Named + draggable:
        // it drops into the top-right corner on first frame, then the player
        // can drag it anywhere (dragging also raises it to the front).
        private readonly Panel _resourcePanel = new() { Width = 170, Height = 78, Name = "resources", Draggable = true };
        private readonly Label _woodLabel = new() { X = 10, Y = 30 };
        private int _woodCollected;
        private bool _resourcePanelPositioned;

        // Script-readiness seam (Tier 4.5): the resource panel's controls are
        // built via the type-name factory + string-property setter, and the
        // Reset button dispatches through this host by action name - the exact
        // path a future markup/Lua loader will use, exercised here with no
        // interpreter.
        private readonly ControlFactory _controls = ControlFactory.CreateDefault();
        private readonly DelegateScriptHost _scriptHost = new();

        // See Design/prd-persistence.md 4.4 - autosave uses render Delta (not
        // the fixed sim tick) since its timing has no gameplay-determinism
        // requirement; a simple accumulator is enough.
        private const float AutosaveIntervalSeconds = 60f;
        private float _autosaveTimer;

        public bool PlayMusicOnStart = true;
        const int DEFAULT_MUSIC = 15;

        // The engine save system (Tier 4.5): this scene registers its own
        // sections (player/clock/game/entities) as ISaveParticipants, so the
        // engine persists them without knowing their shape. Same %AppData%/
        // ExilesForge/save.json slot as before, keyed by TefApp.AppId.
        private readonly SaveManager _saves = new(TefApp.AppId);

        // true = restore the existing save on Load(); false = fresh session
        // (spawn tile + debug trees).
        private readonly bool _continueFromSave;

        public WorldScene(GameController game, bool continueFromSave = false, Vector2? spawnTile = null) : base(game)
        {
            _entityRenderer = new EntityRenderSystem(_entities, game.Assets);
            _continueFromSave = continueFromSave;
            _spawnTile = spawnTile ?? DefaultSpawnTile;
        }

        private void BuildResourcePanel()
        {
            // Named action the "Reset" button dispatches to (a markup loader
            // would read onClick="resetWood" and wire the same way).
            _scriptHost.Register("resetWood", () => _woodCollected = 0);

            // Title label: created by type-name, properties set by name.
            var title = _controls.Create("Label");
            ControlProperties.Set(title, "Text", "Resources");
            ControlProperties.Set(title, "X", 10);
            ControlProperties.Set(title, "Y", 8);
            _resourcePanel.Children.Add(title);

            _resourcePanel.Children.Add(_woodLabel);

            // Reset button: same data-driven construction, click dispatched by
            // action name through the script host.
            var resetButton = (Button)_controls.Create("Button");
            ControlProperties.Set(resetButton, "Text", "Reset");
            ControlProperties.Set(resetButton, "X", 10);
            ControlProperties.Set(resetButton, "Y", 52);
            resetButton.Clicked += () => _scriptHost.Invoke("resetWood");
            _resourcePanel.Children.Add(resetButton);

            Ui.Add(_resourcePanel);
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

            // Roof-hiding is on from the moment the world scene loads (F12
            // toggles it thereafter) - Tier 4.5.
            _roofHideEnabled = true;

            RegisterSaveSections();

            // Restore only if we were asked to AND a save actually loaded;
            // otherwise start fresh. (A "Continue" with a missing/corrupt file
            // falls through to a fresh session rather than erroring.)
            if (!(_continueFromSave && _saves.Load()))
            {
                _player.Spawn(_map, _spawnTile);
                SpawnDebugTrees();
            }
        }

        // Registers each system's save section with the engine SaveManager
        // (Tier 4.5). Adding a new persistent system later is just one more
        // Register call here - no engine change. Section keys are stable
        // strings; order only affects on-disk key order, not correctness.
        private void RegisterSaveSections()
        {
            _saves.Register(new SaveSection("game", CaptureGameState, RestoreGameState));
            _saves.Register(new SaveSection("player", CapturePlayer, RestorePlayer));
            _saves.Register(new SaveSection("clock", CaptureClock, RestoreClock));
            _saves.Register(new SaveSection("entities", CaptureEntities, RestoreEntities));
        }

        private object CaptureGameState() =>
            new GameStateSaveData { MapIndex = MapIndex, WoodCollected = _woodCollected };

        private void RestoreGameState(JsonElement section)
        {
            var data = section.Deserialize<GameStateSaveData>();
            _woodCollected = data.WoodCollected;
            // MapIndex is fixed at 0 for now; restored value is informational.
        }

        private object CapturePlayer() => new PlayerSaveData
        {
            X = _player.WorldPosition.X,
            Y = _player.WorldPosition.Y,
            Z = _player.Z,
            Facing = (byte)_player.Facing,
        };

        private void RestorePlayer(JsonElement section)
        {
            var data = section.Deserialize<PlayerSaveData>();
            _player.RestoreState(new Vector2(data.X, data.Y), data.Z, (Direction)data.Facing);
        }

        private object CaptureClock() =>
            new WorldClockSaveData { Day = Game.World.Day, TimeOfDay = Game.World.TimeOfDay };

        private void RestoreClock(JsonElement section)
        {
            var data = section.Deserialize<WorldClockSaveData>();
            Game.World.Restore(data.Day, data.TimeOfDay);
        }

        private object CaptureEntities()
        {
            var list = new List<EntitySaveData>();

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

                list.Add(entityData);
            }

            return list;
        }

        private void RestoreEntities(JsonElement section)
        {
            var list = section.Deserialize<List<EntitySaveData>>();
            if (list == null)
            {
                return;
            }

            foreach (var entity in list)
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

        /// <summary>Snapshots current runtime state into the save file via the registered participants - called by the autosave timer (Update) and GameController.OnExiting (via OnHostExiting).</summary>
        public void SaveGame() => _saves.Save();

        public override void OnHostExiting() => SaveGame();

        private void SpawnDebugTrees()
        {
            // A small cluster near the spawn point so mouse-picking can be
            // tested against several adjacent, overlapping trees (pick the
            // right one, per-pixel through the branches, etc.).
            Span<Vector2> offsets = [new(3f, 0f), new(4f, 1f), new(2f, 2f)];

            byte height = Game.Assets.Files.TileData.StaticData[DebugTreeGraphic].Height;

            foreach (var offset in offsets)
            {
                var position = _spawnTile + offset;
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

            bool wasdMoved = _player.Update(input, _map);
            HandleMouseMovement(input, wasdMoved);

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

            if (input.IsActionPressed(GameAction.ToggleTerrainLighting))
            {
                _terrainLightingEnabled = !_terrainLightingEnabled;
            }

            if (input.IsActionPressed(GameAction.DebugToggleDepthTest))
            {
                _depthTestEnabled = !_depthTestEnabled;
            }

            if (input.IsActionPressed(GameAction.ToggleRoofHide))
            {
                _roofHideEnabled = !_roofHideEnabled;
            }

            // Roof / upper-storey hiding (Tier 4.5 + 4.6), ported from
            // ClassicUO's UpdateMaxDrawZ. Two cases, both gated on the player
            // being under a covering on their OWN tile so buildings they're
            // merely standing next to keep their roofs:
            //  - Multi-storey: if a floor SURFACE sits a storey above the player
            //    (the ceiling of the room they're in), cut at that floor's Z and
            //    hide EVERYTHING above it (floor, upper walls, roof) so the whole
            //    upper storey lifts off. The floor is flat, so the cut is stable.
            //  - Single-storey: otherwise, if just a roof is overhead, hide only
            //    roofs at a flat player.Z + RoofHideHeight (stable, keeps the
            //    walls/floor/decor, no flicker as sloped roof heights vary).
            int playerTileX = (int)MathF.Floor(_player.WorldPosition.X);
            int playerTileY = (int)MathF.Floor(_player.WorldPosition.Y);

            if (_roofHideEnabled && _map.FindUpperFloorZ(playerTileX, playerTileY, _player.Z, UpperFloorClearance) is int upperFloorZ)
            {
                _tiles.StaticZCutoff = upperFloorZ;
                _tiles.StaticCutoffFilter = StaticCutoffMode.All;
            }
            else if (_roofHideEnabled && _map.IsUnderRoof(playerTileX, playerTileY, _player.Z, RoofHideHeight))
            {
                _tiles.StaticZCutoff = _player.Z + RoofHideHeight;
                _tiles.StaticCutoffFilter = StaticCutoffMode.Roofs;
            }
            else
            {
                _tiles.StaticZCutoff = null;
            }

            if (input.IsActionPressed(GameAction.ToggleDebugInfo))
            {
                _hud.ShowDebugInfo = !_hud.ShowDebugInfo;
            }

            if (input.IsActionPressed(GameAction.ToggleFpsCounter))
            {
                _hud.ShowFps = !_hud.ShowFps;
            }

            // Debug-only: jump the world clock forward 1 in-game hour per
            // press, to check day/night ambient darkening without waiting
            // through a full real-time day cycle.
            if (input.IsActionPressed(GameAction.DebugAdvanceTime))
            {
                Game.World.Advance(Game.World.DayLengthSeconds / 24f);
            }

            // Drop the resource panel into the top-right corner once, then
            // leave it alone so the player can drag it (Tier 4.5) without it
            // snapping back every frame.
            if (!_resourcePanelPositioned)
            {
                _resourcePanel.X = Camera.Bounds.Width - _resourcePanel.Width - 5;
                _resourcePanel.Y = 10;
                _resourcePanelPositioned = true;
            }
            _woodLabel.Text = $"Wood: {_woodCollected}";

            Ui.Update(input);

            // Cursor icon (Tier 4.5): the 8-way directional hand over the
            // world (groundwork for click-to-move), a plain pointer over a UI
            // panel. Later: Target while selecting, Wait during an action,
            // WarMode toggle, etc.
            Game.Cursor.Cursor = Ui.IsMouseOverUI ? CursorType.Pointer : CursorType.Directional;

            // Left-click harvests whatever entity the cursor is actually over
            // (resolved by the previous frame's Draw via mouse-picking) -
            // unless the click actually landed on a UI panel (e.g. the
            // Reset button), which should never also chop a tree behind it.
            if (!Ui.IsMouseOverUI
                && !Ui.IsDragging
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

        // Right-click movement (Tier 4.6), ClassicUO/UO-style, layered on the
        // same movement path as WASD (see PlayerEntity.MoveScreen). Direction is
        // the cursor's offset from screen centre (where the player is drawn);
        // the directional hand cursor already visualizes it. WASD takes priority.
        private void HandleMouseMovement(InputManager input, bool wasdMoved)
        {
            if (wasdMoved || !input.IsMouseDown(MouseButton.Right))
            {
                _mouseMoveSuppressed = false;
                return;
            }

            var viewport = Game.GraphicsDevice.Viewport;
            var center = new Vector2(viewport.Width / 2f, viewport.Height / 2f);
            var screenDir = new Vector2(input.MousePosition.X, input.MousePosition.Y) - center;
            float distance = screenDir.Length();

            // Snap to the 8-direction grid so mouse movement matches WASD
            // instead of floating toward the cursor at an arbitrary angle. The
            // raw distance still drives the dead-zone / walk-vs-run choice.
            var gridDir = DirectionHelper.SnapTo8(screenDir);

            if (input.IsMousePressed(MouseButton.Right))
            {
                // Initial click. Reserve a click that starts over UI or an
                // interactable entity (the latter for a future interact/context
                // action - designed later); otherwise do the UO tap: step if
                // already facing that way, else just turn to face the cursor.
                _mouseMoveSuppressed = Ui.IsMouseOverUI || _entityPick.Kind == PickKind.Entity;

                if (_mouseMoveSuppressed || distance < MouseMoveDeadZone)
                {
                    return;
                }

                if (_player.Facing == _player.FacingFor(gridDir))
                {
                    _player.StepScreen(_map, gridDir);
                }
                else
                {
                    _player.FaceScreen(gridDir);
                }

                return;
            }

            // Held (after the initial press): run/walk continuously toward the
            // cursor - walk inside the run radius, run beyond it, nothing inside
            // the dead zone.
            if (_mouseMoveSuppressed || distance < MouseMoveDeadZone)
            {
                return;
            }

            _player.MoveScreen(_map, gridDir, sprint: distance >= MouseRunRadius);
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

            // Must run before the world draw below - see DayNightOverlay.Prepare's
            // doc comment for why (render-target switching can discard the
            // backbuffer if done after something's already drawn to it).
            _dayNight.Prepare(Game.GraphicsDevice, Game.World, Camera.Bounds.Width, Camera.Bounds.Height);

            // World-space content must be drawn under the camera's view
            // transform or it renders at native pixel size with no zoom -
            // see GameScene.Draw in ClassicUO.Client for the same pattern
            // (`batcher.Begin(null, Camera.ViewTransformMatrix)`).
            batcher.Begin(null, Camera.ViewTransformMatrix);

            // Enables the terrain shader's directional bump-shading
            // (IsometricWorld.fx's get_light(), dotting each stretched-land
            // corner's already-computed normal against a fixed light
            // direction). Without this, Brightlight defaults to 0, which
            // algebraically collapses get_light() to a flat constant
            // regardless of the real normal - the per-corner normals
            // TryBuildStretch/CalculateNormal already compute and
            // DrawStretchedLand already uploads were being silently
            // discarded. 1f matches the real client's max "terrain shadows"
            // setting (full effect); there's no settings/profile system yet
            // to make this user-tunable (see Tier 4 #11, app-shell
            // completeness).
            batcher.SetBrightlight(_terrainLightingEnabled ? TerrainShadowIntensity : 0f);
            batcher.SetLightContrastBoost(_terrainLightingEnabled ? TerrainLightContrastBoost : 0f);

            // Real GPU depth-testing for world occlusion (Tier 4 #13 - see
            // Design/prd-chunk-mesh-render.md). Every world-pass draw now
            // writes a DepthKey-computed Z; this is an isolated first step
            // (draw order is UNCHANGED below) specifically to verify the
            // depth-key formula/DepthStencilState direction agrees with the
            // existing, already-correct draw-order occlusion before any
            // mesh/reordering work builds on top of it. Reset to the plain
            // 2D default after End() so HUD/UI (drawn after) aren't
            // depth-tested.
            batcher.SetStencil(_depthTestEnabled ? WorldDepthStencilState : null);

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
            batcher.SetStencil(null);

            // Snapshot right after End() flushes the world pass - Batcher2D
            // resets both counters on every Begin(), and several more
            // Begin()/End() pairs (day/night, HUD, UI) still run this frame.
            // Debug-only, to check whether Batcher2D's same-texture-run
            // coalescing (see Batcher2D.Flush) already merges most of our
            // per-static Draw() calls into far fewer real GPU submissions,
            // before assuming the chunk-mesh GPU redesign (Tier 4 #13) is
            // the right lever.
            _lastWorldFlushesDone = batcher.FlushesDone;
            _lastWorldTextureSwitches = batcher.TextureSwitches;

            // Ambient day/night darkening, composited over the world just
            // rendered - before the HUD/UI so darkening never affects them.
            _dayNight.Composite(batcher, Camera.Bounds);

            // Screen-space HUD/UI - each has its own Begin/End (no camera
            // matrix), drawn after the world so they always sit on top.
            _hud.Draw(batcher, _tilePick, _entityPick, Game.World, _map, _tiles, _lastWorldFlushesDone, _lastWorldTextureSwitches);
            Ui.Draw(batcher);
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
