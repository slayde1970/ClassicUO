// SPDX-License-Identifier: BSD-2-Clause

using System.IO;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using UOA.Core;
using UOA.Input;
using UOA.Scenes;
using UOA.UI;
using UOA.UI.Controls;

namespace TEF.Scenes
{
    /// <summary>
    /// Character-spawn location picker (Tier 4 #12), shown after New Game
    /// instead of always dropping the player at WorldScene.DefaultSpawnTile.
    /// A handful of hand-picked, known-good tile coordinates rather than a
    /// map click or a generated one - this is a stand-in for real character
    /// creation, not a full location browser.
    /// </summary>
    public sealed class SpawnSelectScene : Scene
    {
        private readonly struct SpawnLocation
        {
            public readonly string Name;
            public readonly Vector2 Tile;

            public SpawnLocation(string name, Vector2 tile)
            {
                Name = name;
                Tile = tile;
            }
        }

        // Both hand-verified in earlier sessions (see WorldScene's history) -
        // deliberately not adding unverified coordinates here, since a bad
        // one could spawn the player in water or inside a wall.
        private static readonly SpawnLocation[] Locations =
        {
            new("Britain Bank", WorldScene.DefaultSpawnTile),
            new("Britain (Alt.)", new Vector2(1395f, 1409f)),
        };

        private static readonly string BackgroundPath = Path.Combine(System.AppContext.BaseDirectory, "Content", "title-bg.png");

        private const int ButtonPaddingX = 10;
        private const int ButtonGapY = 8;
        private const float ButtonBandTopFraction = 0.63f;

        private readonly BackgroundImage _background = new();
        private readonly UIManager _ui = new();
        private readonly Panel _menuPanel = new() { Width = 220, Height = 90 };

        public SpawnSelectScene(GameController game) : base(game)
        {
        }

        public override void Load()
        {
            base.Load();

            BuildMenu();
            _background.Load(Game.GraphicsDevice, BackgroundPath);
        }

        private void BuildMenu()
        {
            _menuPanel.Children.Add(new Label { Text = "Choose a starting location", X = 0, Y = 0 });

            int y = 30;
            int maxWidth = 0;

            foreach (var location in Locations)
            {
                var button = new Button(location.Name) { Y = y };
                var tile = location.Tile;
                button.Clicked += () => Game.Scenes.ChangeScene(new WorldScene(Game, spawnTile: tile));
                _menuPanel.Children.Add(button);

                maxWidth = System.Math.Max(maxWidth, button.Width);
                y += button.Height + ButtonGapY;
            }

            var backButton = new Button("Back") { Y = y };
            backButton.Clicked += () => Game.Scenes.ChangeScene(new TitleScene(Game));
            _menuPanel.Children.Add(backButton);
            maxWidth = System.Math.Max(maxWidth, backButton.Width);
            y += backButton.Height;

            _menuPanel.Width = maxWidth + ButtonPaddingX * 2;
            _menuPanel.Height = y;

            // Center every child horizontally now that the panel's final
            // width is known (labels/buttons vary in size) - matches
            // TitleScene's BuildMenu convention.
            foreach (var child in _menuPanel.Children)
            {
                child.X = (_menuPanel.Width - child.Width) / 2;
            }

            _ui.Add(_menuPanel);
        }

        public override void Update(InputManager input)
        {
            base.Update(input);

            Camera.Bounds = Game.GraphicsDevice.Viewport.Bounds;

            var bgRect = _background.ComputeContainRect(Camera.Bounds.Width, Camera.Bounds.Height);
            _menuPanel.X = bgRect.X + (bgRect.Width - _menuPanel.Width) / 2;
            _menuPanel.Y = bgRect.Y + (int)(bgRect.Height * ButtonBandTopFraction);

            _ui.Update(input);
        }

        public override void Draw(UltimaBatcher2D batcher)
        {
            base.Draw(batcher);

            _background.Draw(batcher, Camera.Bounds.Width, Camera.Bounds.Height);

            _ui.Draw(batcher);
        }
    }
}
