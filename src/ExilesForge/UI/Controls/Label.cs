// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Renderer;
using Microsoft.Xna.Framework;

namespace TEF.UI.Controls
{
    /// <summary>Plain text control, auto-sized from its current Text each frame.</summary>
    public sealed class Label : Control
    {
        public string Text = string.Empty;

        // Null = use Fonts.Bold. NOT defaulted eagerly to Fonts.Bold here -
        // a field initializer would capture whatever Fonts.Bold is AT
        // CONSTRUCTION time (null, if this Label is itself built via a field
        // initializer on something constructed before GameController.
        // LoadContent runs Fonts.Initialize - e.g. a scene's own fields).
        // Resolving it lazily on every use sidesteps that ordering trap
        // entirely.
        public SpriteFont Font;
        public Vector3 Hue = ShaderHueTranslator.GetHueVector(0);

        public Label()
        {
            // Plain text has no hover/click behavior of its own - without
            // this, hovering the word "Reset" over a Button would hit this
            // Label instead of the Button underneath it, dropping the
            // button's hover highlight.
            HitTestVisible = false;
        }

        public override void Update(int parentScreenX, int parentScreenY)
        {
            var size = (Font ?? Fonts.Bold).MeasureString(Text);
            Width = (int)size.X;
            Height = (int)size.Y;

            base.Update(parentScreenX, parentScreenY);
        }

        public override void Draw(UltimaBatcher2D batcher)
        {
            if (!Visible || string.IsNullOrEmpty(Text))
            {
                return;
            }

            batcher.DrawString(Font ?? Fonts.Bold, Text, new Vector2(ScreenX, ScreenY), Hue, 0f);

            base.Draw(batcher);
        }
    }
}
