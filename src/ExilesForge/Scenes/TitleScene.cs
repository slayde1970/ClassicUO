// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.IO;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using TEF.Core;
using TEF.Input;
using TEF.Persistence;
using TEF.UI;
using TEF.UI.Controls;

namespace TEF.Scenes
{
    /// <summary>
    /// First scene shown on launch (see Program.cs). New Game/Continue,
    /// built entirely on the Tier 2 control/gump system - its first real use
    /// beyond WorldScene's demo resource panel. See
    /// Design/prd-persistence.md 4.5.
    /// </summary>
    public sealed class TitleScene : Scene
    {
        private static readonly string BackgroundPath = Path.Combine(AppContext.BaseDirectory, "Content", "title-bg.png");

        private readonly UIManager _ui = new();
        private readonly Panel _menuPanel = new() { Width = 220, Height = 90 };

        // Built lazily in the New Game handler (only needed if a save exists
        // to confirm overwriting) rather than always, since most launches
        // won't need it.
        private Panel _confirmDim;
        private Panel _confirmPanel;

        private readonly BackgroundImage _background = new();

        public TitleScene(GameController game) : base(game)
        {
        }

        public override void Load()
        {
            base.Load();

            // Button/Label construction measures text via Fonts.Bold, which
            // isn't ready until GameController.LoadContent - same ordering
            // trap as WorldScene.BuildResourcePanel, so this waits until here.
            BuildMenu();
            _background.Load(Game.GraphicsDevice, BackgroundPath);
        }

        private const int ButtonPaddingX = 10;
        private const int ButtonGapY = 8;

        // Fraction down the artwork's own height where the illustrated scene
        // ends and the "THE EXILE'S FORGE" title text begins - tuned by eye
        // against the actual artwork, not a general-purpose layout constant.
        private const float ButtonBandTopFraction = 0.63f;

        private void BuildMenu()
        {
            // No title label here - the background artwork already carries
            // "The Exile's Forge" title text, so a second text title would
            // just duplicate it.
            var newGameButton = new Button("New Game");
            newGameButton.Clicked += OnNewGameClicked;
            _menuPanel.Children.Add(newGameButton);

            Button continueButton = null;
            if (SaveManager.Exists())
            {
                continueButton = new Button("Continue");
                continueButton.Clicked += OnContinueClicked;
                _menuPanel.Children.Add(continueButton);
            }

            // Size the panel to the widest button and center each button
            // within it horizontally - buttons auto-size from their own text
            // (see Button), so a fixed panel width would otherwise leave them
            // off-center relative to the panel (and the artwork, since the
            // panel itself is centered on the artwork - see Update).
            int contentWidth = Math.Max(newGameButton.Width, continueButton?.Width ?? 0);
            _menuPanel.Width = contentWidth + ButtonPaddingX * 2;

            newGameButton.X = (_menuPanel.Width - newGameButton.Width) / 2;
            newGameButton.Y = 0;

            int height = newGameButton.Height;

            if (continueButton != null)
            {
                continueButton.X = (_menuPanel.Width - continueButton.Width) / 2;
                continueButton.Y = newGameButton.Height + ButtonGapY;
                height += ButtonGapY + continueButton.Height;
            }

            _menuPanel.Height = height;

            _ui.Add(_menuPanel);
        }

        private void OnContinueClicked()
        {
            Game.Scenes.ChangeScene(new WorldScene(Game, SaveManager.Load()));
        }

        private void OnNewGameClicked()
        {
            if (!SaveManager.Exists())
            {
                Game.Scenes.ChangeScene(new SpawnSelectScene(Game));
                return;
            }

            ShowNewGameConfirm();
        }

        private void ShowNewGameConfirm()
        {
            // Full-screen dim panel behind the dialog - added first (bottom
            // of Z order) so the small dialog panel draws on top of it, but
            // still hit-tests/blocks clicks to the title buttons underneath
            // (UIManager checks the topmost-added root first).
            _confirmDim = new Panel
            {
                X = 0,
                Y = 0,
                Width = Camera.Bounds.Width,
                Height = Camera.Bounds.Height,
                BackgroundColor = new Color(0, 0, 0, 160),
            };

            _confirmPanel = new Panel { Width = 260, Height = 110 };
            _confirmPanel.Children.Add(new Label
            {
                Text = "This will erase your saved game.",
                X = 10,
                Y = 10,
            });

            var yesButton = new Button("Yes") { X = 10, Y = 60 };
            yesButton.Clicked += OnConfirmNewGameYes;
            _confirmPanel.Children.Add(yesButton);

            var cancelButton = new Button("Cancel") { X = 100, Y = 60 };
            cancelButton.Clicked += HideNewGameConfirm;
            _confirmPanel.Children.Add(cancelButton);

            CenterOnScreen(_confirmPanel);

            _ui.Add(_confirmDim);
            _ui.Add(_confirmPanel);
        }

        private void OnConfirmNewGameYes()
        {
            SaveManager.Delete();
            Game.Scenes.ChangeScene(new SpawnSelectScene(Game));
        }

        private void HideNewGameConfirm()
        {
            _ui.Remove(_confirmPanel);
            _ui.Remove(_confirmDim);
            _confirmPanel = null;
            _confirmDim = null;
        }

        private void CenterOnScreen(Control control)
        {
            control.X = (Camera.Bounds.Width - control.Width) / 2;
            control.Y = (Camera.Bounds.Height - control.Height) / 2;
        }

        public override void Update(InputManager input)
        {
            base.Update(input);

            Camera.Bounds = Game.GraphicsDevice.Viewport.Bounds;

            // Positioned in the gap baked into the artwork itself, between
            // the illustrated scene and "THE EXILE'S FORGE" title text below
            // it - as a fraction of the rendered artwork's own rect (not the
            // raw viewport) so it stays correctly placed regardless of
            // window size/aspect, and centered on the artwork rather than
            // the full window (which may be letterboxed on the sides).
            var bgRect = _background.ComputeContainRect(Camera.Bounds.Width, Camera.Bounds.Height);
            _menuPanel.X = bgRect.X + (bgRect.Width - _menuPanel.Width) / 2;
            _menuPanel.Y = bgRect.Y + (int)(bgRect.Height * ButtonBandTopFraction);

            if (_confirmDim != null)
            {
                _confirmDim.Width = Camera.Bounds.Width;
                _confirmDim.Height = Camera.Bounds.Height;
                CenterOnScreen(_confirmPanel);
            }

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
