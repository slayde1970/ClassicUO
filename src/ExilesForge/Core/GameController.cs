// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TEF.Assets;
using TEF.Audio;
using TEF.Input;
using TEF.Scenes;

namespace TEF.Core
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

        public GameController(GameSettings settings)
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

            // Show the OS cursor. FNA hides it by default; ClassicUO draws its
            // own in-game cursor instead, but TEF has no custom cursor yet, so
            // without this the mouse is invisible (can't tell what you're
            // hovering/picking). Revisit if/when a themed UO cursor is added.
            IsMouseVisible = true;

            IsFixedTimeStep = false;
            TargetElapsedTime = System.TimeSpan.FromMilliseconds(1000.0 / 250.0);
        }

        public GameSettings Settings { get; }
        public GraphicsDeviceManager GraphicsManager { get; }
        public GameAssets Assets { get; } = new GameAssets();
        public AudioManager Audio { get; } = new AudioManager();
        public InputManager Input { get; } = new InputManager();
        public SceneManager Scenes { get; } = new SceneManager();

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
            Scenes.Update(Input);
            Audio.Update();

            base.Update(gameTime);
        }

        protected override void Draw(GameTime gameTime)
        {
            GraphicsDevice.Clear(Color.Black);

            Scenes.Draw(_batcher);

            base.Draw(gameTime);
        }
    }
}
