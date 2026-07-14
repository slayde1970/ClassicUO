// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using ClassicUO.Assets;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using TEF.Assets;

namespace TEF.World
{
    /// <summary>
    /// Draws the world floor under the player: land tiles first, then the
    /// statics (walls, trees, floors, deco) that sit on top of them - both
    /// read straight from the mul/uop map + statics files, mirroring what
    /// ClassicUO.Client's Game/Map/Chunk.cs loads per 8x8 block.
    ///
    /// Land is drawn flat (each tile's own Z baked into its screen Y, like
    /// LandView.cs's non-stretched branch). Statics are positioned with the
    /// same art anchor math as View.DrawStatic ((w/2 - 22, h - 44) offset)
    /// and drawn back-to-front by screen row so they overlap each other
    /// correctly.
    ///
    /// KNOWN GAPS:
    ///  - No height-stretched land. A first pass used DrawStretchedLand
    ///    (ported from Land.ApplyStretch) to blend tile corners with their
    ///    neighbors' heights; it produced a dense checkerboard of missing
    ///    tiles - a bug in that stretched path, not the tile reads - so it's
    ///    shelved. Slopes show a per-tile Z stair-step until that's fixed.
    ///  - No animated-static frames, no light sources, no per-object depth
    ///    vs. the player (the player always draws on top - see WorldScene).
    ///  - No real chunk cache: every block in view is re-read from disk each
    ///    frame (cleared below), unlike ClassicUO.Client's Map.cs + Chunk.cs
    ///    which load a block once and keep it until the player moves away.
    /// </summary>
    public sealed class TileRenderer
    {
        private const int TileSize = 22; // half-width/height of the 44x44 iso diamond, see GameObject.UpdateRealScreenPosition
        private const int BlockSize = 8;

        private struct StaticTile
        {
            public ushort Graphic;
            public ushort Hue;
            public sbyte Z;
        }

        private readonly int _mapIndex;
        private readonly Dictionary<long, MapBlock?> _blockCache = new();

        // Per-block statics, grouped by local cell index ((localY << 3) + localX),
        // each cell sorted low-to-high Z. Rebuilt every frame alongside _blockCache.
        private readonly Dictionary<long, List<StaticTile>[]> _staticCache = new();

        public TileRenderer(int mapIndex = 0)
        {
            _mapIndex = mapIndex;
        }

        /// <param name="playerTilePosition">Player position in fractional tile coordinates (not pixels).</param>
        /// <param name="viewRangeInTiles">How many tiles out from the player to draw in each direction.</param>
        /// <param name="screenCenterOffset">Viewport center - Camera only handles zoom/peek, not centering, so the caller supplies this (see PlayerEntity.Draw for the same convention).</param>
        /// <param name="drawStatics">When false, skips the statics pass (debug toggle to inspect the bare land layer).</param>
        public void Draw(UltimaBatcher2D batcher, GameAssets assets, Vector2 playerTilePosition, int viewRangeInTiles, Vector2 screenCenterOffset, bool drawStatics = true)
        {
            _blockCache.Clear();
            _staticCache.Clear();

            int mapIndex = _mapIndex;
            var maps = assets.Files.Maps;
            maps.SanitizeMapIndex(ref mapIndex);
            assets.EnsureMapLoaded(mapIndex);

            int centerX = (int)Math.Floor(playerTilePosition.X);
            int centerY = (int)Math.Floor(playerTilePosition.Y);

            // The player is always drawn at the screen origin (see
            // PlayerEntity.Draw), so every tile's screen position must be
            // relative to the player's exact (fractional) iso position, not
            // just their tile cell, or movement between tiles would snap
            // instead of scroll smoothly.
            float playerIsoX = (playerTilePosition.X - playerTilePosition.Y) * TileSize;
            float playerIsoY = (playerTilePosition.X + playerTilePosition.Y) * TileSize;
            var isoOrigin = new Vector2(playerIsoX, playerIsoY);

            int x0 = centerX - viewRangeInTiles;
            int x1 = centerX + viewRangeInTiles;
            int y0 = centerY - viewRangeInTiles;
            int y1 = centerY + viewRangeInTiles;

            DrawLand(batcher, assets, maps, mapIndex, x0, y0, x1, y1, isoOrigin, screenCenterOffset);

            if (drawStatics)
            {
                DrawStatics(batcher, assets, maps, mapIndex, x0, y0, x1, y1, isoOrigin, screenCenterOffset);
            }
        }

        private void DrawLand(
            UltimaBatcher2D batcher, GameAssets assets, MapLoader maps, int mapIndex,
            int x0, int y0, int x1, int y1, Vector2 isoOrigin, Vector2 screenCenterOffset)
        {
            // Land tiles tile the plane without overlapping, so a plain
            // row-by-row pass is enough - draw order doesn't matter here.
            for (int ty = y0; ty <= y1; ty++)
            {
                if (ty < 0)
                {
                    continue;
                }

                for (int tx = x0; tx <= x1; tx++)
                {
                    if (tx < 0)
                    {
                        continue;
                    }

                    if (!TryGetTile(maps, mapIndex, tx, ty, out ushort tileId, out sbyte z))
                    {
                        continue;
                    }

                    // Land graphics 0-2 are the "no-draw" void tiles (paved-
                    // over areas, cave interiors, etc.) - UO leaves them blank
                    // for statics to cover. Matches Land.AllowedToDraw (> 2).
                    if (tileId <= 2)
                    {
                        continue;
                    }

                    ref readonly var sprite = ref assets.Art.GetLand(tileId);
                    if (sprite.Texture == null)
                    {
                        continue;
                    }

                    float planarX = (tx - ty) * TileSize - TileSize;
                    float planarY = (tx + ty) * TileSize - TileSize - (z << 2);
                    var screenPos = new Vector2(planarX, planarY) - isoOrigin + screenCenterOffset;

                    batcher.Draw(
                        sprite.Texture,
                        screenPos,
                        sprite.UV,
                        ShaderHueTranslator.GetHueVector(0),
                        0f
                    );
                }
            }
        }

        private void DrawStatics(
            UltimaBatcher2D batcher, GameAssets assets, MapLoader maps, int mapIndex,
            int x0, int y0, int x1, int y1, Vector2 isoOrigin, Vector2 screenCenterOffset)
        {
            // Statics have height and overlap, so they must be painted
            // back-to-front. Screen row (and therefore paint order) is the iso
            // sum (tx + ty), so iterate diagonals of increasing sum; within a
            // cell the list is already sorted low-to-high Z.
            for (int sum = x0 + y0; sum <= x1 + y1; sum++)
            {
                int txStart = Math.Max(x0, sum - y1);
                int txEnd = Math.Min(x1, sum - y0);

                for (int tx = txStart; tx <= txEnd; tx++)
                {
                    int ty = sum - tx;
                    if (tx < 0 || ty < 0)
                    {
                        continue;
                    }

                    var cells = GetStatics(maps, mapIndex, tx >> 3, ty >> 3);
                    if (cells == null)
                    {
                        continue;
                    }

                    int localPos = ((ty & (BlockSize - 1)) << 3) + (tx & (BlockSize - 1));
                    var list = cells[localPos];
                    if (list == null)
                    {
                        continue;
                    }

                    float planarX = (tx - ty) * TileSize - TileSize;
                    float baseY = (tx + ty) * TileSize - TileSize;

                    foreach (var s in list)
                    {
                        if (!CanDrawStatic(assets, s.Graphic))
                        {
                            continue;
                        }

                        ref readonly var sprite = ref assets.Art.GetArt(s.Graphic);
                        if (sprite.Texture == null)
                        {
                            continue;
                        }

                        // Art anchor for statics: bottom-center of the sprite
                        // sits on the tile. See View.DrawStatic.
                        int offX = (sprite.UV.Width >> 1) - TileSize;
                        int offY = sprite.UV.Height - (2 * TileSize);

                        float drawX = planarX - offX;
                        float drawY = baseY - (s.Z << 2) - offY;
                        var screenPos = new Vector2(drawX, drawY) - isoOrigin + screenCenterOffset;

                        bool partialHue = assets.Files.TileData.StaticData[s.Graphic].IsPartialHue;

                        batcher.Draw(
                            sprite.Texture,
                            screenPos,
                            sprite.UV,
                            ShaderHueTranslator.GetHueVector(s.Hue, partialHue, 1f),
                            0f
                        );
                    }
                }
            }
        }

        private bool TryGetTile(MapLoader maps, int mapIndex, int x, int y, out ushort tileId, out sbyte z)
        {
            tileId = 0;
            z = 0;

            if (x < 0 || y < 0)
            {
                return false;
            }

            var blockOrNull = GetBlock(maps, mapIndex, x >> 3, y >> 3);
            if (blockOrNull == null)
            {
                return false;
            }

            var block = blockOrNull.Value;
            int localX = x & (BlockSize - 1);
            int localY = y & (BlockSize - 1);
            ref readonly var cell = ref block.Cells[localY * BlockSize + localX];

            tileId = (ushort)(cell.TileID & 0x3FFF);
            z = cell.Z;
            return true;
        }

        private MapBlock? GetBlock(MapLoader maps, int mapIndex, int blockX, int blockY)
        {
            long key = ((long)blockX << 32) | (uint)blockY;

            if (_blockCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            ref var indexMap = ref maps.GetIndex(mapIndex, blockX, blockY);
            MapBlock? block = null;

            if (indexMap.IsValid())
            {
                indexMap.MapFile.Seek((long)indexMap.MapAddress, SeekOrigin.Begin);
                block = indexMap.MapFile.Read<MapBlock>();
            }

            _blockCache[key] = block;
            return block;
        }

        /// <summary>
        /// Whether a static graphic should be rendered at all. Ported from
        /// ClassicUO.Client's GameObject.CanBeDrawn - it filters out the
        /// "nodraw" placeholder statics (detected mainly by their tiledata
        /// name starting with "nodraw", plus a handful of hardcoded graphic
        /// ids and the NoDiagonal flag) that the map data uses as spacers.
        /// Client-only special cases (gargoyle race, pre-6.0.14.4 easel) are
        /// dropped since TEF has no player-race or legacy-version handling.
        /// </summary>
        private static bool CanDrawStatic(GameAssets assets, ushort graphic)
        {
            var staticData = assets.Files.TileData.StaticData;

            switch (graphic)
            {
                case 0x0001:
                case 0x21BC:
                case 0xA1FE:
                case 0xA1FF:
                case 0xA200:
                case 0xA201:
                    return false;

                case 0x9E4C:
                case 0x9E64:
                case 0x9E65:
                case 0x9E7D:
                {
                    ref readonly var d = ref staticData[graphic];
                    return !d.IsBackground && !d.IsSurface;
                }
            }

            if (graphic == 0x63D3 || (graphic >= 0x2198 && graphic <= 0x21A4))
            {
                return false;
            }

            if (graphic >= staticData.Length)
            {
                return false;
            }

            ref readonly var data = ref staticData[graphic];

            if (!string.IsNullOrEmpty(data.Name) && data.Name.StartsWith("nodraw", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return !data.IsNoDiagonal;
        }

        private List<StaticTile>[] GetStatics(MapLoader maps, int mapIndex, int blockX, int blockY)
        {
            long key = ((long)blockX << 32) | (uint)blockY;

            if (_staticCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            List<StaticTile>[] cells = null;

            ref var indexMap = ref maps.GetIndex(mapIndex, blockX, blockY);

            if (indexMap.IsValid() && indexMap.StaticFile != null && indexMap.StaticAddress != 0 && indexMap.StaticCount > 0)
            {
                int count = (int)indexMap.StaticCount;
                var buffer = new StaticsBlock[count];

                indexMap.StaticFile.Seek((long)indexMap.StaticAddress, SeekOrigin.Begin);
                indexMap.StaticFile.Read(MemoryMarshal.AsBytes(buffer.AsSpan()));

                foreach (ref readonly var sb in buffer.AsSpan())
                {
                    // 0 / 0xFFFF are "no tile" sentinels - same filter as Chunk.Load.
                    if (sb.Color == 0 || sb.Color == 0xFFFF)
                    {
                        continue;
                    }

                    if (sb.X >= BlockSize || sb.Y >= BlockSize)
                    {
                        continue;
                    }

                    cells ??= new List<StaticTile>[BlockSize * BlockSize];

                    int pos = (sb.Y << 3) + sb.X;
                    (cells[pos] ??= new List<StaticTile>()).Add(new StaticTile
                    {
                        Graphic = sb.Color,
                        Hue = sb.Hue,
                        Z = sb.Z,
                    });
                }

                if (cells != null)
                {
                    foreach (var list in cells)
                    {
                        list?.Sort(static (a, b) => a.Z.CompareTo(b.Z));
                    }
                }
            }

            _staticCache[key] = cells;
            return cells;
        }
    }
}
