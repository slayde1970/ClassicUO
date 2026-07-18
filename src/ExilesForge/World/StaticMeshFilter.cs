// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using TEF.Assets;

namespace TEF.World
{
    /// <summary>
    /// Decides which map statics stay on the per-object draw path (never
    /// baked into a BlockMesh) for Tier 4 #13 - see
    /// Design/prd-chunk-mesh-render.md sections 3/4.1. Ported directly from
    /// ClassicUO.Client's ChunkMesh.IsStaticExcludedFromMesh: animated
    /// statics need per-frame UV changes a fixed mesh slot can't cheaply
    /// express (TEF's AnimatedStatics system stays on this path), and
    /// foliage/trees/rocks are excluded in the real client too (likely for
    /// similar visual-variety/no-caching reasons - not investigated further,
    /// just matched).
    /// </summary>
    public static class StaticMeshFilter
    {
        // Ported from ClassicUO.Client's Game/Data/StaticFilters.cs IsRock -
        // a plain id-range/switch check, no external data file needed.
        public static bool IsRock(ushort graphic)
        {
            switch (graphic)
            {
                case 4945:
                case 4948:
                case 4950:
                case 4953:
                case 4955:
                case 4958:
                case 4959:
                case 4960:
                case 4962:
                    return true;

                default:
                    return graphic >= 6001 && graphic <= 6012;
            }
        }

        // Ported from ClassicUO.Client's Game/Data/StaticFilters.cs - the
        // real client seeds this from an editable tree.txt on first run;
        // TEF just hardcodes the same graphic ids directly (extend this set
        // if a tree/stump graphic turns out to be missing).
        private static readonly HashSet<ushort> TreeGraphics = new()
        {
            0x0C95, 0x0C96, 0x0C99, 0x0C9B, 0x0C9C, 0x0C9D, 0x0C9E, 0x0CA6, 0x0CA8, 0x0CAA, 0x0CAB,
            0x0CC9, 0x0CCA, 0x0CCB, 0x0CCC, 0x0CCD, 0x0CD0, 0x0CD3, 0x0CD6, 0x0CD8, 0x0CDA, 0x0CDD,
            0x0CE0, 0x0CE3, 0x0CE6, 0x0CF8, 0x0CFB, 0x0CFE, 0x0D01, 0x0D37, 0x0D38, 0x0D41, 0x0D42,
            0x0D43, 0x0D44, 0x0D57, 0x0D58, 0x0D59, 0x0D5A, 0x0D5B, 0x0D6E, 0x0D6F, 0x0D70, 0x0D71,
            0x0D72, 0x0D84, 0x0D85, 0x0D86, 0x0D94, 0x0D98, 0x0D9C, 0x0DA0, 0x0DA4, 0x0DA8, 0x12B6,
            0x12B7, 0x12B8, 0x12B9, 0x12BA, 0x12BB, 0x12BC, 0x12BD,
        };

        public static bool IsTree(ushort graphic) => TreeGraphics.Contains(graphic);

        public static bool IsExcludedFromMesh(GameAssets assets, ushort graphic)
        {
            if (graphic >= assets.Files.TileData.StaticData.Length)
            {
                return true;
            }

            ref readonly var data = ref assets.Files.TileData.StaticData[graphic];

            return data.IsInternal
                || data.IsAnimated
                || data.IsFoliage
                || IsTree(graphic)
                || IsRock(graphic);
        }
    }
}
