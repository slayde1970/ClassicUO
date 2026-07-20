// SPDX-License-Identifier: BSD-2-Clause

using System;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace UOA.UI.Controls
{
    /// <summary>
    /// A single-line editable text field (Tier 4.7). Gains keyboard focus when
    /// clicked (see UIManager focus), then receives typed characters and the
    /// editing keys (backspace/delete/arrows/home/end) plus Enter (fires
    /// <see cref="Submitted"/>). Draws its text and a caret while focused.
    /// Set <see cref="Control.Width"/>/<see cref="Control.Height"/> explicitly.
    /// </summary>
    public sealed class TextBox : Panel
    {
        private const int PaddingX = 5;
        private const int PaddingY = 4;

        public string Text = string.Empty;
        public int MaxLength = 64;
        public Vector3 TextHue = ShaderHueTranslator.GetHueVector(0);

        /// <summary>Raised when Enter is pressed while focused.</summary>
        public event Action Submitted;

        private int _caret;

        public TextBox()
        {
            Focusable = true;
            BackgroundColor = new Color(15, 15, 15, 235);
        }

        public override void OnFocus() => _caret = Text.Length;

        public override void OnTextInput(char c)
        {
            if (Text.Length >= MaxLength)
            {
                return;
            }

            Text = Text.Insert(_caret, c.ToString());
            _caret++;
        }

        public override void OnKeyDown(Keys key)
        {
            switch (key)
            {
                case Keys.Back:
                    if (_caret > 0)
                    {
                        Text = Text.Remove(_caret - 1, 1);
                        _caret--;
                    }
                    break;

                case Keys.Delete:
                    if (_caret < Text.Length)
                    {
                        Text = Text.Remove(_caret, 1);
                    }
                    break;

                case Keys.Left:
                    if (_caret > 0) _caret--;
                    break;

                case Keys.Right:
                    if (_caret < Text.Length) _caret++;
                    break;

                case Keys.Home:
                    _caret = 0;
                    break;

                case Keys.End:
                    _caret = Text.Length;
                    break;

                case Keys.Enter:
                    Submitted?.Invoke();
                    break;
            }
        }

        public override void Draw(UltimaBatcher2D batcher)
        {
            base.Draw(batcher); // panel background

            var font = Fonts.Bold;
            var textPos = new Vector2(ScreenX + PaddingX, ScreenY + PaddingY);
            batcher.DrawString(font, Text, textPos, TextHue, 0f);

            if (IsFocused)
            {
                float caretX = ScreenX + PaddingX + font.MeasureString(Text.Substring(0, _caret)).X;
                int caretH = (int)font.MeasureString("W").Y;
                var caret = new Rectangle((int)caretX, ScreenY + PaddingY, 1, caretH);
                batcher.Draw(SolidColorTextureCache.GetTexture(Color.White), caret, ShaderHueTranslator.GetHueVector(0), 0f);
            }
        }
    }
}
