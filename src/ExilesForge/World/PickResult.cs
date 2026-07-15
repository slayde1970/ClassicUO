// SPDX-License-Identifier: BSD-2-Clause

namespace TEF.World
{
    public enum PickKind
    {
        None,
        Land,
        Static,
        Entity,
        Player,
    }

    /// <summary>
    /// What the mouse cursor is over, resolved as a side effect of the render
    /// pass (see TileRenderer.Draw). Mirrors how ClassicUO computes
    /// SelectedObject during drawing rather than as a separate spatial query:
    /// every drawn candidate under the cursor is pixel-tested, and because the
    /// pass is back-to-front, the last (frontmost) hit wins. Produced by Draw
    /// for the current frame and typically consumed in the *next* frame's
    /// Update (a one-frame lag, same as the real client).
    /// </summary>
    public struct PickResult
    {
        public PickKind Kind;
        public int TileX;
        public int TileY;
        public ushort Graphic;

        /// <summary>The tiledata name for the graphic (LandTiles.Name / StaticTiles.Name), or null.</summary>
        public string Name;

        /// <summary>Valid only when <see cref="Kind"/> is <see cref="PickKind.Entity"/>.</summary>
        public int EntityId;
    }
}
