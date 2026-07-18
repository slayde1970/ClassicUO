// SPDX-License-Identifier: BSD-2-Clause

using System;
using Microsoft.Xna.Framework;
using UOA.Input;

namespace UOA.UI.Controls
{
    /// <summary>A clickable Panel with a centered-by-padding label, auto-sized from its <see cref="Text"/>. Setting Text re-sizes in place, so it can be built parameterless (e.g. via ControlFactory, Tier 4.5) and have its text assigned as a string property afterward.</summary>
    public sealed class Button : Panel
    {
        private const int PaddingX = 12;
        private const int PaddingY = 6;

        private readonly Color _normalColor;
        private readonly Color _hoverColor;
        private readonly Label _label;

        public event Action Clicked;

        /// <summary>Button caption. Assigning it re-measures and resizes the button.</summary>
        public string Text
        {
            get => _label.Text;
            set
            {
                _label.Text = value ?? string.Empty;
                Resize();
            }
        }

        public Button() : this(string.Empty)
        {
        }

        public Button(string text, Color? normalColor = null, Color? hoverColor = null)
        {
            _normalColor = normalColor ?? new Color(60, 60, 60, 230);
            _hoverColor = hoverColor ?? new Color(95, 95, 95, 230);
            BackgroundColor = _normalColor;

            _label = new Label { Text = text, X = PaddingX, Y = PaddingY };
            Children.Add(_label);

            Resize();
        }

        private void Resize()
        {
            var size = ClassicUO.Renderer.Fonts.Bold.MeasureString(_label.Text);
            Width = (int)size.X + PaddingX * 2;
            Height = (int)size.Y + PaddingY * 2;
        }

        public override void OnMouseEnter()
        {
            BackgroundColor = _hoverColor;
        }

        public override void OnMouseLeave()
        {
            BackgroundColor = _normalColor;
        }

        public override void OnClick(MouseButton button)
        {
            if (button == MouseButton.Left)
            {
                Clicked?.Invoke();
            }
        }
    }
}
