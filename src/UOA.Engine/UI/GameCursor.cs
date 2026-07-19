// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using UOA.Assets;

namespace UOA.UI
{
    /// <summary>
    /// Which cursor icon to show. The active scene sets this each frame via
    /// <see cref="GameCursor.Cursor"/> in response to game state (targeting,
    /// busy, over-UI, etc.); the engine renders the matching UO art.
    /// </summary>
    public enum CursorType
    {
        /// <summary>Plain hand/pointer - used over UI, menus, and non-world scenes.</summary>
        Pointer,

        /// <summary>The 8-way hand that points away from screen centre (the player) toward the mouse - the in-world default (Tier 4.5, groundwork for click-to-move).</summary>
        Directional,

        /// <summary>Targeting cursor (selecting a tile/object for a spell, harvest, placement, ...).</summary>
        Target,

        /// <summary>Busy/wait (hourglass) while an action resolves.</summary>
        Wait,

        /// <summary>Dragging (a gump or an item).</summary>
        Drag,

        /// <summary>Text caret / I-beam over an editable field.</summary>
        Text,
    }

    /// <summary>
    /// Draws the classic UO hand cursor and its variants (Tier 4.5), ported
    /// from ClassicUO's GameCursor. The cursor art lives in the Art file
    /// (0x206A.. normal, 0x2053.. war mode); columns 0-7 are the eight
    /// directional hands (by Direction value), 8 = drag, 9 = pointer,
    /// 12 = target, 13 = wait, 14 = text. The directional hand's facing comes
    /// from GetMouseDirection (screen-centre -> mouse), matching how UO points
    /// the hand away from the centred player. Each cursor art encodes its
    /// hotspot as a green marker pixel in the first row/column, with a 1px
    /// marker border stripped by insetting the UV.
    ///
    /// Drawn in plain screen space (its own Begin/End) on top of everything by
    /// the host each frame - see GameController.Draw.
    /// </summary>
    public sealed class GameCursor
    {
        // Column layout (shared by both rows). Indices 0-7 line up with the
        // UO Direction enum so GetMouseDirection can index straight in.
        private const int ColDrag = 8;
        private const int ColPointer = 9;
        private const int ColTarget = 12;
        private const int ColWait = 13;
        private const int ColText = 14;

        private static readonly ushort[] NormalCursors =
        {
            0x206A, 0x206B, 0x206C, 0x206D, 0x206E, 0x206F, 0x2070, 0x2071,
            0x2072, 0x2073, 0x2074, 0x2075, 0x2076, 0x2077, 0x2078, 0x2079,
        };

        private static readonly ushort[] WarCursors =
        {
            0x2053, 0x2054, 0x2055, 0x2056, 0x2057, 0x2058, 0x2059, 0x205A,
            0x205B, 0x205C, 0x205D, 0x205E, 0x205F, 0x2060, 0x2061, 0x2062,
        };

        private readonly Dictionary<ushort, Point> _hotspots = new();

        // Fallback facing when the mouse sits exactly on centre (no direction).
        private int _lastDirection = 4; // South (hand pointing down)

        /// <summary>Set by the active scene each frame to pick the icon.</summary>
        public CursorType Cursor = CursorType.Pointer;

        /// <summary>Set by the game when the player is in war mode - swaps to the red war-mode cursor art.</summary>
        public bool WarMode;

        /// <param name="mouse">Current mouse position (screen space).</param>
        /// <param name="centerAnchor">The point the directional hand faces away from - screen centre, where the player is drawn.</param>
        public void Draw(UltimaBatcher2D batcher, GameAssets assets, Point mouse, Point centerAnchor)
        {
            ushort graphic = ResolveGraphic(centerAnchor, mouse);

            ref readonly var sprite = ref assets.Art.GetArt(graphic);
            if (sprite.Texture == null)
            {
                return;
            }

            Point hot = GetHotspot(assets, graphic);

            // Skip the 1px marker border baked around each cursor art (the
            // hotspot green pixel lives in it too) - matches ClassicUO's
            // BORDER_SIZE inset.
            var uv = sprite.UV;
            uv.X += 1;
            uv.Y += 1;
            uv.Width -= 2;
            uv.Height -= 2;

            batcher.Begin();
            batcher.Draw(
                sprite.Texture,
                new Vector2(mouse.X - hot.X, mouse.Y - hot.Y),
                uv,
                ShaderHueTranslator.GetHueVector(0),
                0f
            );
            batcher.End();
        }

        private ushort ResolveGraphic(Point center, Point mouse)
        {
            ushort[] row = WarMode ? WarCursors : NormalCursors;

            int column = Cursor switch
            {
                CursorType.Directional => _lastDirection = GetMouseDirection(center.X, center.Y, mouse.X, mouse.Y, _lastDirection),
                CursorType.Drag => ColDrag,
                CursorType.Target => ColTarget,
                CursorType.Wait => ColWait,
                CursorType.Text => ColText,
                _ => ColPointer,
            };

            return row[column];
        }

        private Point GetHotspot(GameAssets assets, ushort graphic)
        {
            if (_hotspots.TryGetValue(graphic, out var cached))
            {
                return cached;
            }

            int hotX = 0;
            int hotY = 0;

            // Cursor art is static art (+0x4000, same as GetArt does). Its
            // hotspot is a green marker (0xFF00FF00, RGBA) sitting in the first
            // row (gives hotX) and first column (gives hotY).
            var art = assets.Files.Arts.GetArt((uint)(graphic + 0x4000));
            if (!art.Pixels.IsEmpty)
            {
                int w = art.Width;
                int h = art.Height;
                var pixels = art.Pixels;

                for (int x = 0; x < w; x++)
                {
                    if (pixels[x] == 0xFF00FF00) // first row (y == 0)
                    {
                        hotX = x;
                        break;
                    }
                }

                for (int y = 0; y < h; y++)
                {
                    if (pixels[y * w] == 0xFF00FF00) // first column (x == 0)
                    {
                        hotY = y;
                        break;
                    }
                }
            }

            var hotspot = new Point(hotX, hotY);
            _hotspots[graphic] = hotspot;
            return hotspot;
        }

        // Direct port of ClassicUO GameCursor.GetMouseDirection: returns a UO
        // Direction value (0-7) from (x1,y1) toward (to_x,to_y), used straight
        // as the directional-hand column. Falls back to current_facing when the
        // mouse is exactly on centre.
        public static int GetMouseDirection(int x1, int y1, int to_x, int to_y, int current_facing)
        {
            int shiftX = to_x - x1;
            int shiftY = to_y - y1;
            int hashf = 100 * (Sgn(shiftX) + 2) + 10 * (Sgn(shiftY) + 2);

            if (shiftX != 0 && shiftY != 0)
            {
                shiftX = Math.Abs(shiftX);
                shiftY = Math.Abs(shiftY);

                if (shiftY * 5 <= shiftX * 2)
                {
                    hashf += 1;
                }
                else if (shiftY * 2 >= shiftX * 5)
                {
                    hashf += 3;
                }
                else
                {
                    hashf += 2;
                }
            }
            else if (shiftX == 0 && shiftY == 0)
            {
                return current_facing;
            }

            // Direction values: North=0, Right(NE)=1, East=2, Down(SE)=3,
            // South=4, Left(SW)=5, West=6, Up(NW)=7.
            return hashf switch
            {
                111 => 6, // W
                112 => 7, // NW
                113 => 0, // N
                120 => 6, // W
                131 => 6, // W
                132 => 5, // SW
                133 => 4, // S
                210 => 0, // N
                230 => 4, // S
                311 => 2, // E
                312 => 1, // NE
                313 => 0, // N
                320 => 2, // E
                331 => 2, // E
                332 => 3, // SE
                333 => 4, // S
                _ => current_facing,
            };
        }

        private static int Sgn(int val) => (0 < val ? 1 : 0) - (val < 0 ? 1 : 0);
    }
}
