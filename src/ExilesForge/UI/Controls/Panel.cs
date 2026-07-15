// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Renderer;
using Microsoft.Xna.Framework;

namespace TEF.UI.Controls
{
    /// <summary>Solid-color background rectangle, using the renderer's 1x1 SolidColorTextureCache stretched to fill Bounds. A plain container otherwise - add a border/gump-art backing later if a themed look is wanted.</summary>
    public class Panel : Control
    {
        public Color BackgroundColor = new(20, 20, 20, 200);

        public override void Draw(UltimaBatcher2D batcher)
        {
            if (!Visible)
            {
                return;
            }

            var texture = SolidColorTextureCache.GetTexture(BackgroundColor);
            var rect = new Rectangle(ScreenX, ScreenY, Width, Height);

            batcher.Draw(texture, rect, ShaderHueTranslator.GetHueVector(0), 0f);

            base.Draw(batcher);
        }
    }
}
