// SPDX-License-Identifier: BSD-2-Clause

namespace TEF.World
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
    }
}
