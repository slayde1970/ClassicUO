// SPDX-License-Identifier: BSD-2-Clause

using System;
using Microsoft.Xna.Framework;
using UOA.Input;

namespace UOA.UI.Controls
{
    /// <summary>A clickable Panel with a centered-by-padding label. Sizes itself from the label text once at construction - if the text can change later, rebuild the button rather than mutating Text in place (keeps padding/sizing simple for this first pass).</summary>
    public sealed class Button : Panel
    {
        private const int PaddingX = 12;
        private const int PaddingY = 6;

        private readonly Color _normalColor;
        private readonly Color _hoverColor;

        public event Action Clicked;

        public Button(string text, Color? normalColor = null, Color? hoverColor = null)
        {
            _normalColor = normalColor ?? new Color(60, 60, 60, 230);
            _hoverColor = hoverColor ?? new Color(95, 95, 95, 230);
            BackgroundColor = _normalColor;

            var label = new Label { Text = text, X = PaddingX, Y = PaddingY };
            Children.Add(label);

            var size = ClassicUO.Renderer.Fonts.Bold.MeasureString(text);
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
