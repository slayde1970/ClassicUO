// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.IO;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace TEF.UI
{
    /// <summary>
    /// A full-screen background image, scaled to fit entirely within the
    /// viewport (letterboxed, never cropped) while preserving its aspect
    /// ratio - a "cover" fit (scale up to fill/crop) can cut off artwork that
    /// has its own baked-in title text near an edge. Shared by scenes that
    /// show a static piece of art behind their UI (TitleScene,
    /// SpawnSelectScene) so the loading/contain-fit math lives in one place.
    /// </summary>
    public sealed class BackgroundImage
    {
        private Texture2D _texture;

        public void Load(GraphicsDevice device, string path)
        {
            using var stream = File.OpenRead(path);
            _texture = Texture2D.FromStream(device, stream);
        }

        /// <summary>The rect the image is actually drawn into for the given viewport size - useful for positioning UI relative to the artwork rather than the raw (possibly letterboxed) viewport.</summary>
        public Rectangle ComputeContainRect(int viewportWidth, int viewportHeight)
        {
            if (_texture == null)
            {
                return new Rectangle(0, 0, viewportWidth, viewportHeight);
            }

            float scale = Math.Min((float)viewportWidth / _texture.Width, (float)viewportHeight / _texture.Height);
            int w = (int)(_texture.Width * scale);
            int h = (int)(_texture.Height * scale);

            return new Rectangle((viewportWidth - w) / 2, (viewportHeight - h) / 2, w, h);
        }

        public void Draw(UltimaBatcher2D batcher, int viewportWidth, int viewportHeight)
        {
            if (_texture == null)
            {
                return;
            }

            batcher.Begin();
            batcher.Draw(_texture, ComputeContainRect(viewportWidth, viewportHeight), ShaderHueTranslator.GetHueVector(0), 0f);
            batcher.End();
        }
    }
}
