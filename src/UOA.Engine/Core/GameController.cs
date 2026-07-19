// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using UOA.Assets;
using UOA.Audio;
using UOA.Input;
using UOA.Scenes;
using UOA.UI;

namespace UOA.Core
{
    /// <summary>
    /// Application root. Modeled on ClassicUO.GameController
    /// (ClassicUO.Client/GameController.cs): a single FNA Game subclass that
    /// owns the graphics device, drives the fixed-ish update loop, and hands
    /// off to whatever scene is active. Dropped relative to the original:
    /// SDL event filter (no gump/text-input layer yet - see InputManager),
    /// plugin host, network socket pump, and window-position persistence.
    /// Those get added back as TEF grows a UI and settings system.
    /// </summary>
    public sealed class GameController : Game
    {
        private UltimaBatcher2D _batcher;
        private int _frameCount;
        private double _fpsElapsedMs;

        public GameController(EngineSettings settings)
        {
            Settings = settings;

            GraphicsManager = new GraphicsDeviceManager(this)
            {
                PreferredDepthStencilFormat = DepthFormat.Depth24Stencil8,
                SynchronizeWithVerticalRetrace = false,
                PreferredBackBufferWidth = 1600,
                PreferredBackBufferHeight = 1024,
            };

            Window.AllowUserResizing = true;
            Window.Title = "The Exile's Forge";

            // Hide the OS cursor - the engine draws the UO hand cursor itself
            // (see Cursor / Draw), the same way ClassicUO does. Mouse POSITION
            // is still tracked with this off (see InputManager).
            IsMouseVisible = false;

            IsFixedTimeStep = false;
            TargetElapsedTime = System.TimeSpan.FromMilliseconds(1000.0 / 250.0);
        }

        public EngineSettings Settings { get; }
        public GraphicsDeviceManager GraphicsManager { get; }
        public GameAssets Assets { get; } = new GameAssets();
        public AudioManager Audio { get; } = new AudioManager();
        public InputManager Input { get; } = new InputManager();
        public SceneManager Scenes { get; } = new SceneManager();
        public SimulationClock Sim { get; } = new SimulationClock();
        public WorldClock World { get; } = new WorldClock();

        /// <summary>The UO hand cursor, drawn on top of everything each frame. Scenes set its <see cref="GameCursor.Cursor"/>/<see cref="GameCursor.WarMode"/> per game state (reset to Pointer each frame - see Update).</summary>
        public GameCursor Cursor { get; } = new GameCursor();

        protected override void Initialize()
        {
            if (GraphicsManager.GraphicsDevice.Adapter.IsProfileSupported(GraphicsProfile.HiDef))
            {
                GraphicsManager.GraphicsProfile = GraphicsProfile.HiDef;
            }

            GraphicsManager.ApplyChanges();

            base.Initialize();
        }

        protected override void LoadContent()
        {
            base.LoadContent();

            _batcher = new UltimaBatcher2D(GraphicsDevice);

            Fonts.Initialize(GraphicsDevice);
            SolidColorTextureCache.Initialize(GraphicsDevice);

            Assets.Load(GraphicsDevice, Settings);
            Audio.Initialize(Assets);

            // Caller wires up the first scene (typically a boot/world scene)
            // after construction via `Scenes.ChangeScene(...)`.
        }

        // Fires once on a clean Game.Exit()/window close, before shutdown -
        // see external/FNA/src/Game.cs. Lets the active scene flush any
        // unsaved session state (WorldScene overrides OnHostExiting to save)
        // so a clean exit never loses progress (a crash/force-kill can still
        // lose up to one autosave interval - see WorldScene.Update). The
        // engine host stays ignorant of any concrete game scene type.
        protected override void OnExiting(object sender, EventArgs args)
        {
            Scenes.Current?.OnHostExiting();

            base.OnExiting(sender, args);
        }

        protected override void UnloadContent()
        {
            Audio.StopMusic();
            Scenes.Current?.Dispose();
            Assets.Dispose();

            base.UnloadContent();
        }

        protected override void Update(GameTime gameTime)
        {
            Time.Ticks = (uint)gameTime.TotalGameTime.TotalMilliseconds;
            Time.Delta = (float)gameTime.ElapsedGameTime.TotalSeconds;

            Input.Update();

            // Default the cursor each frame so a scene only has to opt into a
            // non-pointer icon (and it resets cleanly on scene changes); the
            // active scene sets it during Scenes.Update.
            Cursor.Cursor = CursorType.Pointer;
            Cursor.WarMode = false;

            Scenes.Update(Input);
            Audio.Update();

            Sim.Advance(Time.Delta, fixedDelta =>
            {
                World.Advance(fixedDelta);
                Scenes.FixedUpdate(fixedDelta);
            });

            Time.SimTicks = Sim.TickCount;

            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            // Also clears the depth buffer (Tier 4 #13 - real GPU depth
            // testing for world occlusion, see Design/prd-chunk-mesh-render.md)
            // - the single-Color Clear() overload only clears the color
            // target, leaving stale depth values from the previous frame.
            GraphicsDevice.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.Black, 1f, 0);

            Scenes.Draw(_batcher);

            // Drawn last, so the cursor sits on top of the world and all UI.
            // The directional hand faces away from screen centre, where the
            // player is drawn.
            var viewport = GraphicsDevice.Viewport;
            Cursor.Draw(_batcher, Assets, Input.MousePosition, new Point(viewport.Width / 2, viewport.Height / 2));

            _frameCount++;
            _fpsElapsedMs += gameTime.ElapsedGameTime.TotalMilliseconds;
            if (_fpsElapsedMs >= 1000.0)
            {
                Time.Fps = _frameCount;
                _frameCount = 0;
                _fpsElapsedMs = 0;
            }

            base.Draw(gameTime);
        }
    }
}
