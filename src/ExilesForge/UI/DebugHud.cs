// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using UOA.Core;
using UOA.World;

namespace TEF.UI
{
    /// <summary>
    /// Top-left debug overlay: an FPS counter (always the topmost line when
    /// shown) and a small "what's under the cursor" readout below it (tile
    /// name/coords, entity name/coords), each independently toggleable.
    /// Deliberately minimal - this is a first,
    /// narrowly-scoped step into Tier 2 #5 (UI/HUD), not the general
    /// control/gump system that item still needs for inventory, crafting
    /// menus, survival bars, etc. Draws in plain screen space (its own
    /// batcher.Begin()/End() with no camera matrix), unlike TileRenderer's
    /// world-space pass.
    /// </summary>
    public sealed class DebugHud
    {
        private const int Margin = 10;
        private const int Padding = 8;
        private const float LineGap = 4f;

        // A bright gold/yellow UO hue (commonly used for system/guild text in
        // the original client). Tune here if it doesn't read as "yellow"
        // enough - ShaderHueTranslator.GetHueVector looks this up as a
        // 1-based palette index, not a literal RGB value.
        private const int FpsHue = 0x0035;

        private static readonly Color BackgroundColor = new(20, 20, 20, 200);

        public bool ShowDebugInfo { get; set; } = true;
        public bool ShowFps { get; set; } = true;

        public void Draw(UltimaBatcher2D batcher, in PickResult tilePick, in PickResult entityPick, WorldClock clock, WorldMap map, TileRenderer tiles, int worldFlushesDone, int worldTextureSwitches)
        {
            if (!ShowDebugInfo && !ShowFps)
            {
                return;
            }

            string fpsLine = ShowFps ? $"FPS: {Time.Fps}" : null;

            string block = null;

            if (ShowDebugInfo)
            {
                string tileLine = tilePick.Kind == PickKind.None
                    ? "Tile: -"
                    : $"Tile: {tilePick.Name} ({tilePick.TileX}, {tilePick.TileY})";

                string entityLine = entityPick.Kind == PickKind.None
                    ? "Entity: -"
                    : $"Entity: {entityPick.Name} ({entityPick.TileX}, {entityPick.TileY})";

                string timeLine = $"Time: Day {clock.Day}  {clock.Hour12:D2}:{clock.Minute:D2} {clock.MeridiemTag}";
                string cacheLine = $"Blocks cached: {map.CachedBlockCount}";
                string drawLine = $"Draws: static={tiles.StaticDrawCalls} meshedBlocks={tiles.MeshedBlockDraws} meshLand={tiles.MeshedLandQuads} meshStatic={tiles.MeshedStaticQuads}";
                string gpuLine = $"GPU: flushes={worldFlushesDone} texSwitches={worldTextureSwitches}";

                block = tileLine + "\n" + entityLine + "\n" + timeLine + "\n" + cacheLine + "\n" + drawLine + "\n" + gpuLine;
            }

            // Measure everything up front so the background panel can be
            // sized to fit before any text is drawn on top of it.
            Vector2 fpsSize = fpsLine != null ? Fonts.Bold.MeasureString(fpsLine) : Vector2.Zero;
            Vector2 blockSize = block != null ? Fonts.Bold.MeasureString(block) : Vector2.Zero;

            float contentWidth = System.Math.Max(fpsSize.X, blockSize.X);
            float contentHeight = fpsSize.Y + blockSize.Y + (fpsLine != null && block != null ? LineGap : 0f);

            batcher.Begin();

            var backgroundRect = new Rectangle(
                Margin,
                Margin,
                (int)contentWidth + Padding * 2,
                (int)contentHeight + Padding * 2
            );

            batcher.Draw(
                SolidColorTextureCache.GetTexture(BackgroundColor),
                backgroundRect,
                ShaderHueTranslator.GetHueVector(0),
                0f
            );

            float x = Margin + Padding;
            float y = Margin + Padding;

            if (fpsLine != null)
            {
                batcher.DrawString(
                    Fonts.Bold,
                    fpsLine,
                    new Vector2(x, y),
                    ShaderHueTranslator.GetHueVector(FpsHue),
                    0f
                );

                y += fpsSize.Y + LineGap;
            }

            if (block != null)
            {
                batcher.DrawString(
                    Fonts.Bold,
                    block,
                    new Vector2(x, y),
                    ShaderHueTranslator.GetHueVector(0),
                    0f
                );
            }

            batcher.End();
        }
    }
}
