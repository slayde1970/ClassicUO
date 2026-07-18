// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TEF.Assets;

namespace TEF.World
{
    /// <summary>
    /// Persistent, texture-bucketed CPU-side vertex array for one 8x8
    /// block's LAND (flat + stretched) - Tier 4 #13, see
    /// Design/prd-chunk-mesh-render.md. Built once (lazily, on first Draw)
    /// and reused every frame after; TEF's map data never mutates today
    /// (see the PRD's section 4.6), so there is currently no rebuild
    /// trigger beyond the initial build.
    ///
    /// Quads are stored already grouped by their source texture (an Art
    /// atlas page or the separate Texmap atlas), which is what actually
    /// fixes the confirmed texmap/Art-atlas interleaving problem - not the
    /// caching itself. Fed to the GPU each frame via
    /// UltimaBatcher2D.DrawBatch, which copies visible entries into
    /// Batcher2D's own dynamic buffer and lets its existing same-texture-run
    /// coalescing (Batcher2D.Flush) merge them into far fewer real draws
    /// than one-call-per-static.
    ///
    /// Vertex positions are stored in ABSOLUTE world iso-space (the same
    /// planarX/flatY math TileRenderer already computes), NOT relative to
    /// the player - repositioning happens at draw time via DrawBatch's
    /// integer offsetX/offsetY, matching the real client's own approach and
    /// TileRenderer's pixel-snapped worldOffset (see its Draw method).
    /// </summary>
    /// <summary>
    /// Cached-at-build-time land info for picking (Tier 4 #13 task 8):
    /// mouse picking needs a tile's screen position and land graphic every
    /// frame, but re-deriving it via TileRenderer.TryBuildStretch would
    /// mean redoing the same up-to-11-neighbor-lookup stretch computation
    /// BlockMesh.Build already did once, purely to answer a hit test.
    /// </summary>
    public struct LandPickInfo
    {
        public bool HasLand;
        public ushort TileId;
        public float ScreenX;
        public float ScreenY;
    }

    public sealed class BlockMesh
    {
        private UltimaBatcher2D.PositionNormalTextureColor4[] _landVertices = Array.Empty<UltimaBatcher2D.PositionNormalTextureColor4>();
        private Texture2D[] _landTextures = Array.Empty<Texture2D>();
        private bool[] _landVisible = Array.Empty<bool>();
        private int _landCount;

        private readonly LandPickInfo[] _landPick = new LandPickInfo[WorldMap.BlockSize * WorldMap.BlockSize];

        // Mesh-eligible map statics only - animated/foliage/tree/rock stay
        // on TileRenderer's per-object draw path (see StaticMeshFilter),
        // matching ChunkMesh.IsStaticExcludedFromMesh.
        private UltimaBatcher2D.PositionNormalTextureColor4[] _staticVertices = Array.Empty<UltimaBatcher2D.PositionNormalTextureColor4>();
        private Texture2D[] _staticTextures = Array.Empty<Texture2D>();
        private bool[] _staticVisible = Array.Empty<bool>();
        private int _staticCount;

        public bool IsBuilt { get; private set; }

        // Debug/perf-investigation only - how many quads this block actually baked into each layer.
        public int LandQuadCount => _landCount;
        public int StaticQuadCount => _staticCount;

        /// <summary>Cached land pick info for a tile local to this block (Tier 4 #13 task 8 - see LandPickInfo).</summary>
        public LandPickInfo GetLandPick(int localX, int localY) => _landPick[localY * WorldMap.BlockSize + localX];

        public void Build(WorldMap map, GameAssets assets, int blockX, int blockY)
        {
            var landQuads = new List<(UltimaBatcher2D.PositionNormalTextureColor4 Vertex, Texture2D Texture)>(WorldMap.BlockSize * WorldMap.BlockSize);
            var staticQuads = new List<(UltimaBatcher2D.PositionNormalTextureColor4 Vertex, Texture2D Texture)>();

            int baseX = blockX * WorldMap.BlockSize;
            int baseY = blockY * WorldMap.BlockSize;

            for (int ly = 0; ly < WorldMap.BlockSize; ly++)
            {
                for (int lx = 0; lx < WorldMap.BlockSize; lx++)
                {
                    int tx = baseX + lx;
                    int ty = baseY + ly;

                    TryAddLandQuad(map, assets, tx, ty, landQuads, _landPick, ly * WorldMap.BlockSize + lx);
                    AddStaticQuads(map, assets, tx, ty, staticQuads);
                }
            }

            _landCount = FillFrom(landQuads, ref _landVertices, ref _landTextures, ref _landVisible);
            _staticCount = FillFrom(staticQuads, ref _staticVertices, ref _staticTextures, ref _staticVisible);

            IsBuilt = true;
        }

        // Group by texture so DrawBatch feeds Batcher2D's dynamic buffer
        // with same-texture quads contiguous, regardless of their original
        // tile order - this is the actual fix for the texmap/Art-atlas
        // ping-pong. GetHashCode is only used as a stable-enough grouping
        // key (not a meaningful order); Texture2D doesn't override it, so
        // it's reference-identity-based and consistent for this sort.
        private static int FillFrom(
            List<(UltimaBatcher2D.PositionNormalTextureColor4 Vertex, Texture2D Texture)> quads,
            ref UltimaBatcher2D.PositionNormalTextureColor4[] vertices,
            ref Texture2D[] textures,
            ref bool[] visible)
        {
            quads.Sort(static (a, b) => a.Texture.GetHashCode().CompareTo(b.Texture.GetHashCode()));

            int count = quads.Count;
            vertices = new UltimaBatcher2D.PositionNormalTextureColor4[count];
            textures = new Texture2D[count];
            visible = new bool[count];

            for (int i = 0; i < count; i++)
            {
                vertices[i] = quads[i].Vertex;
                textures[i] = quads[i].Texture;
                visible[i] = true;
            }

            return count;
        }

        public void Draw(UltimaBatcher2D batcher, int offsetX, int offsetY, bool drawStatics)
        {
            if (_landCount > 0)
            {
                batcher.DrawBatch(_landVertices, _landTextures, _landVisible, _landCount, offsetX, offsetY);
            }

            if (drawStatics && _staticCount > 0)
            {
                batcher.DrawBatch(_staticVertices, _staticTextures, _staticVisible, _staticCount, offsetX, offsetY);
            }
        }

        private static void AddStaticQuads(
            WorldMap map, GameAssets assets, int tx, int ty,
            List<(UltimaBatcher2D.PositionNormalTextureColor4 Vertex, Texture2D Texture)> quads)
        {
            const int TileSize = TileRenderer.TileSize;

            var statics = map.GetStaticsAt(tx, ty);
            if (statics == null)
            {
                return;
            }

            float planarX = (tx - ty) * TileSize - TileSize;
            float baseY = (tx + ty) * TileSize - TileSize;

            foreach (var s in statics)
            {
                // Map-sourced only (GetStaticsAt never returns entity-projected
                // tiles anyway) and mesh-eligible only - excluded statics
                // (animated/foliage/tree/rock) stay on TileRenderer's
                // per-object path, matching the real client.
                if (!s.Drawable || StaticMeshFilter.IsExcludedFromMesh(assets, s.Graphic))
                {
                    continue;
                }

                ref readonly var sprite = ref assets.Art.GetArt(s.Graphic);
                if (sprite.Texture == null)
                {
                    continue;
                }

                // Art anchor for statics: bottom-center of the sprite sits
                // on the tile. Matches TileRenderer.DrawStaticsAt exactly.
                int offX = (sprite.UV.Width >> 1) - TileSize;
                int offY = sprite.UV.Height - (2 * TileSize);

                float drawX = planarX - offX;
                float drawY = baseY - (s.Z << 2) - offY;
                var position = new Vector2(drawX, drawY);

                var vertex = BuildFlatQuad(sprite.Texture, position, sprite.UV, s.HueVector, DepthKey.Compute(tx, ty, s.PriorityZ));
                quads.Add((vertex, sprite.Texture));
            }
        }

        private static void TryAddLandQuad(
            WorldMap map, GameAssets assets, int tx, int ty,
            List<(UltimaBatcher2D.PositionNormalTextureColor4 Vertex, Texture2D Texture)> quads,
            LandPickInfo[] pickInfo, int pickIndex)
        {
            const int TileSize = TileRenderer.TileSize;

            if (!map.TryGetLand(tx, ty, out ushort tileId, out sbyte z))
            {
                return;
            }

            // Matches TileRenderer.DrawLandTile's "no-draw void tile" skip.
            if (tileId <= 2)
            {
                return;
            }

            float planarX = (tx - ty) * TileSize - TileSize;
            ushort texId = assets.Files.TileData.LandData[tileId].TexID;

            if (texId != 0
                && assets.Files.Texmaps.File.GetValidRefEntry(texId).Length > 0
                && TileRenderer.TryBuildStretch(map, tx, ty, z,
                    out var yOffsets, out var nTop, out var nRight, out var nLeft, out var nBottom, out sbyte averageZ))
            {
                ref readonly var texmap = ref assets.Texmaps.GetTexmap(texId);
                if (texmap.Texture != null)
                {
                    float stretchedY = (tx + ty) * TileSize - TileSize;
                    var position = new Vector2(planarX, stretchedY);
                    var landHue = new Vector3(0f, ShaderHueTranslator.SHADER_LAND, 1f);

                    // Matches Chunk.AddGameObject's Land case: priorityZ starts
                    // at AverageZ - 1 (vs. Z - 1 for flat land below), then an
                    // extra -1 is applied to ALL land regardless of stretch.
                    // This guarantees land always sorts behind every static on
                    // its own tile, even a Background-flagged one (priorityZ
                    // z-1) - without it, ground-level decals like stone pavers
                    // (Background, Height==0, so priorityZ == z - 1) tie or
                    // lose against land using a bare z depth key and vanish.
                    float depth = DepthKey.Compute(tx, ty, averageZ - 2);

                    var vertex = BuildStretchedQuad(
                        texmap.Texture, position, texmap.UV,
                        yOffsets, nTop, nRight, nLeft, nBottom,
                        landHue, depth
                    );

                    quads.Add((vertex, texmap.Texture));

                    pickInfo[pickIndex] = new LandPickInfo
                    {
                        HasLand = true,
                        TileId = tileId,
                        ScreenX = planarX,
                        ScreenY = stretchedY,
                    };
                    return;
                }
            }

            ref readonly var sprite = ref assets.Art.GetLand(tileId);
            if (sprite.Texture == null)
            {
                return;
            }

            float flatY = (tx + ty) * TileSize - TileSize - (z << 2);
            var flatPosition = new Vector2(planarX, flatY);

            // Matches Chunk.AddGameObject's Land case for the non-stretched
            // branch: priorityZ = z - 1, then the extra -1 applied to all
            // land - see the stretched branch above for why.
            var flatVertex = BuildFlatQuad(
                sprite.Texture, flatPosition, sprite.UV,
                ShaderHueTranslator.GetHueVector(0), DepthKey.Compute(tx, ty, z - 2)
            );

            quads.Add((flatVertex, sprite.Texture));

            pickInfo[pickIndex] = new LandPickInfo
            {
                HasLand = true,
                TileId = tileId,
                ScreenX = planarX,
                ScreenY = flatY,
            };
        }

        /// <summary>
        /// Matches Batcher2D's plain Draw(texture, position, sourceRect,
        /// color, depth) overload exactly (position/rect/color/depth, no
        /// rotation/origin/scale/flip) - see its SetVertex/CalculateUVs.
        /// Default (0,0,1) normal, matching SetDefaultNormals.
        /// </summary>
        private static UltimaBatcher2D.PositionNormalTextureColor4 BuildFlatQuad(
            Texture2D texture, Vector2 position, Rectangle sourceRect, Vector3 hue, float depth)
        {
            float invW = 1f / texture.Width;
            float invH = 1f / texture.Height;
            float sourceX = sourceRect.X * invW;
            float sourceY = sourceRect.Y * invH;
            float sourceW = sourceRect.Width * invW;
            float sourceH = sourceRect.Height * invH;

            float destW = sourceRect.Width;
            float destH = sourceRect.Height;

            UltimaBatcher2D.PositionNormalTextureColor4 vertex = default;

            vertex.Position0 = new Vector3(position.X, position.Y, depth);
            vertex.Position1 = new Vector3(position.X + destW, position.Y, depth);
            vertex.Position2 = new Vector3(position.X, position.Y + destH, depth);
            vertex.Position3 = new Vector3(position.X + destW, position.Y + destH, depth);

            vertex.TextureCoordinate0 = new Vector3(sourceX, sourceY, 0f);
            vertex.TextureCoordinate1 = new Vector3(sourceX + sourceW, sourceY, 0f);
            vertex.TextureCoordinate2 = new Vector3(sourceX, sourceY + sourceH, 0f);
            vertex.TextureCoordinate3 = new Vector3(sourceX + sourceW, sourceY + sourceH, 0f);

            vertex.Hue0 = hue;
            vertex.Hue1 = hue;
            vertex.Hue2 = hue;
            vertex.Hue3 = hue;

            vertex.Normal0 = new Vector3(0f, 0f, 1f);
            vertex.Normal1 = new Vector3(0f, 0f, 1f);
            vertex.Normal2 = new Vector3(0f, 0f, 1f);
            vertex.Normal3 = new Vector3(0f, 0f, 1f);

            return vertex;
        }

        /// <summary>
        /// Matches Batcher2D.DrawStretchedLand's quad exactly (diamond
        /// corners offset by yOffsets, half-pixel UV inset, per-corner
        /// normals) - see its implementation for the source of this port.
        /// </summary>
        private static UltimaBatcher2D.PositionNormalTextureColor4 BuildStretchedQuad(
            Texture2D texture, Vector2 position, Rectangle sourceRect,
            in UltimaBatcher2D.YOffsets yOffsets,
            Vector3 normalTop, Vector3 normalRight, Vector3 normalLeft, Vector3 normalBottom,
            Vector3 hue, float depth)
        {
            float invW = 1f / texture.Width;
            float invH = 1f / texture.Height;
            float sourceX = (sourceRect.X + 0.5f) * invW;
            float sourceY = (sourceRect.Y + 0.5f) * invH;
            float sourceW = (sourceRect.Width - 1f) * invW;
            float sourceH = (sourceRect.Height - 1f) * invH;

            UltimaBatcher2D.PositionNormalTextureColor4 vertex = default;

            vertex.TextureCoordinate0 = new Vector3(sourceX, sourceY, 0f);
            vertex.TextureCoordinate1 = new Vector3(sourceX + sourceW, sourceY, 0f);
            vertex.TextureCoordinate2 = new Vector3(sourceX, sourceY + sourceH, 0f);
            vertex.TextureCoordinate3 = new Vector3(sourceX + sourceW, sourceY + sourceH, 0f);

            vertex.Normal0 = normalTop;
            vertex.Normal1 = normalRight;
            vertex.Normal2 = normalLeft;
            vertex.Normal3 = normalBottom;

            vertex.Position0 = new Vector3(position.X + 22, position.Y - yOffsets.Top, depth);
            vertex.Position1 = new Vector3(position.X + 44, position.Y + (22 - yOffsets.Right), depth);
            vertex.Position2 = new Vector3(position.X, position.Y + (22 - yOffsets.Left), depth);
            vertex.Position3 = new Vector3(position.X + 22, position.Y + (44 - yOffsets.Bottom), depth);

            vertex.Hue0 = hue;
            vertex.Hue1 = hue;
            vertex.Hue2 = hue;
            vertex.Hue3 = hue;

            return vertex;
        }
    }
}
