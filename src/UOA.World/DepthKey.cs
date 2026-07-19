// SPDX-License-Identifier: BSD-2-Clause

using System;

namespace UOA.World
{
    /// <summary>
    /// The GPU depth-buffer sort key TEF is moving to (Tier 4 #13 - see
    /// Design/prd-chunk-mesh-render.md section 4.4), ported directly from
    /// ClassicUO.Client's View.CalculateDepthZ. (tileX + tileY) is the
    /// primary key (matches TileRenderer's existing diagonal walk order);
    /// priorityZ is the secondary key, offset by 127 (kept positive) and
    /// scaled by 0.01 (small enough to never spill into the next
    /// diagonal's integer bucket) - this is nearly the same sort key TEF
    /// already computed for its manual List.Sort, just written into a
    /// vertex Z for a real hardware depth test instead.
    ///
    /// Deliberately NOT porting the real client's sub-tile screen-offset
    /// quadrant nudge (a +1 to whichever tile coordinate a moving/bouncing
    /// object's Offset currently leans toward) - TEF has no sub-tile
    /// bounce/effect objects yet that would need it. Revisit only if a
    /// visible occlusion glitch appears at a tile boundary for a
    /// continuously-moving object (e.g. the player).
    /// </summary>
    public static class DepthKey
    {
        public static float Compute(int tileX, int tileY, int priorityZ)
        {
            return (tileX + tileY) + (127 + priorityZ) * 0.01f;
        }

        /// <summary>
        /// Depth for a continuously-moving object whose position is fractional
        /// (Tier 4.6 - the player, and any future smoothly-moving entity). Uses
        /// the ROUNDED iso diagonal (round(worldX + worldY)) rather than the
        /// floored tile, so once the object crosses a tile's centre it jumps to
        /// the diagonal it's visually entering and stays in front of the land
        /// tile ahead - instead of that tile's upper corner clipping its feet
        /// while its depth is stuck on the tile behind. This is TEF's take on
        /// ClassicUO View.CalculateDepthZ's sub-tile Offset quadrant nudge,
        /// adapted to a fractional world position. priorityZ still separates it
        /// from other objects on the same diagonal (land below, walls above).
        /// </summary>
        public static float ComputeMoving(float worldX, float worldY, int priorityZ)
        {
            // floor(v + 0.5) = round-half-up, avoiding MathF.Round's banker's
            // rounding so the flip point is consistent at exactly x.5.
            float diagonal = MathF.Floor(worldX + worldY + 0.5f);
            return diagonal + (127 + priorityZ) * 0.01f;
        }
    }
}
