// SPDX-License-Identifier: BSD-2-Clause

using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using TEF.Core;
using TEF.World;

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
        private const float LineGap = 4f;

        // A bright gold/yellow UO hue (commonly used for system/guild text in
        // the original client). Tune here if it doesn't read as "yellow"
        // enough - ShaderHueTranslator.GetHueVector looks this up as a
        // 1-based palette index, not a literal RGB value.
        private const int FpsHue = 0x0035;

        public bool ShowDebugInfo { get; set; } = true;
        public bool ShowFps { get; set; } = true;

        public void Draw(UltimaBatcher2D batcher, in PickResult tilePick, in PickResult entityPick)
        {
            if (!ShowDebugInfo && !ShowFps)
            {
                return;
            }

            batcher.Begin();

            float y = Margin;

            if (ShowFps)
            {
                string fpsLine = $"FPS: {Time.Fps}";

                batcher.DrawString(
                    Fonts.Bold,
                    fpsLine,
                    new Vector2(Margin, y),
                    ShaderHueTranslator.GetHueVector(FpsHue),
                    0f
                );

                y += Fonts.Bold.MeasureString(fpsLine).Y + LineGap;
            }

            if (ShowDebugInfo)
            {
                string tileLine = tilePick.Kind == PickKind.None
                    ? "Tile: -"
                    : $"Tile: {tilePick.Name} ({tilePick.TileX}, {tilePick.TileY})";

                string entityLine = entityPick.Kind == PickKind.None
                    ? "Entity: -"
                    : $"Entity: {entityPick.Name} ({entityPick.TileX}, {entityPick.TileY})";

                string block = tileLine + "\n" + entityLine;

                batcher.DrawString(
                    Fonts.Bold,
                    block,
                    new Vector2(Margin, y),
                    ShaderHueTranslator.GetHueVector(0),
                    0f
                );
            }

            batcher.End();
        }
    }
}
