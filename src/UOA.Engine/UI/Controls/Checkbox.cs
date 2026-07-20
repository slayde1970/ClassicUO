// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using UOA.Input;

namespace UOA.UI.Controls
{
    /// <summary>
    /// A square toggle (Tier 4.7). Left-click flips <see cref="Checked"/> and
    /// raises <see cref="Toggled"/>. Drawn as an outlined box with a filled
    /// inner square when checked (art-backed check mark can come later).
    /// </summary>
    public sealed class Checkbox : Control
    {
        private const int BoxSize = 18;

        public bool Checked;

        public Color BoxColor = new(55, 55, 55, 235);
        public Color CheckColor = new(120, 200, 120, 255);

        /// <summary>Raised on toggle, with the new checked state.</summary>
        public event Action<bool> Toggled;

        public Checkbox()
        {
            Width = BoxSize;
            Height = BoxSize;
        }

        public override void OnClick(MouseButton button)
        {
            if (button == MouseButton.Left)
            {
                Checked = !Checked;
                Toggled?.Invoke(Checked);
            }
        }

        public override void Draw(UltimaBatcher2D batcher)
        {
            if (!Visible)
            {
                return;
            }

            var box = new Rectangle(ScreenX, ScreenY, BoxSize, BoxSize);
            batcher.Draw(SolidColorTextureCache.GetTexture(BoxColor), box, ShaderHueTranslator.GetHueVector(0), 0f);

            if (Checked)
            {
                var inner = new Rectangle(ScreenX + 4, ScreenY + 4, BoxSize - 8, BoxSize - 8);
                batcher.Draw(SolidColorTextureCache.GetTexture(CheckColor), inner, ShaderHueTranslator.GetHueVector(0), 0f);
            }

            base.Draw(batcher);
        }
    }
}
