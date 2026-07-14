// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using System.IO;
using ClassicUO.Assets;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using TEF.Assets;

namespace TEF.World
{
    /// <summary>
    /// Reads raw land tiles straight from the mul/uop map files and draws
    /// them under the player as flat (non-height-stretched) tiles - each
    /// tile's own Z is baked into its screen Y, like ClassicUO.Client's
    /// Game/GameObjects/Views/LandView.cs non-stretched fallback branch.
    ///
    /// KNOWN GAP: a first pass used UltimaBatcher2D.DrawStretchedLand (ported
    /// from Land.ApplyStretch) to blend each tile's corners with its
    /// neighbors' heights, which is what the real client does to avoid
    /// seeing gaps/steps on slopes. That produced a dense checkerboard of
    /// missing tiles - confirmed (by swapping back to this flat draw) to be
    /// a bug in that stretched-corner path, not in the tile data reading
    /// below. Needs debugging before re-introducing height blending; until
    /// then, slopes will show a stair-step Z jump per tile instead of a
    /// smooth ramp.
    ///
    /// Also missing: statics, and a real chunk cache - every block touched by
    /// the view window is re-read from disk every frame instead of being
    /// loaded once and kept around like ClassicUO.Client's Game/Map/Map.cs +
    /// Chunk.cs do.
    /// </summary>
    public sealed class TileRenderer
    {
        private const int TileSize = 22; // half-width/height of the 44x44 iso diamond, see GameObject.UpdateRealScreenPosition
        private const int BlockSize = 8;

        private readonly int _mapIndex;
        private readonly Dictionary<long, MapBlock?> _blockCache = new();

        public TileRenderer(int mapIndex = 0)
        {
            _mapIndex = mapIndex;
        }

        /// <param name="playerTilePosition">Player position in fractional tile coordinates (not pixels).</param>
        /// <param name="viewRangeInTiles">How many tiles out from the player to draw in each direction.</param>
        /// <param name="screenCenterOffset">Viewport center - Camera only handles zoom/peek, not centering, so the caller supplies this (see PlayerEntity.Draw for the same convention).</param>
        public void Draw(UltimaBatcher2D batcher, GameAssets assets, Vector2 playerTilePosition, int viewRangeInTiles, Vector2 screenCenterOffset)
        {
            _blockCache.Clear();

            int mapIndex = _mapIndex;
            var maps = assets.Files.Maps;
            maps.SanitizeMapIndex(ref mapIndex);
            assets.EnsureMapLoaded(mapIndex);

            int centerX = (int)System.Math.Floor(playerTilePosition.X);
            int centerY = (int)System.Math.Floor(playerTilePosition.Y);

            // The player is always drawn at the screen origin (see
            // PlayerEntity.Draw), so every tile's screen position must be
            // relative to the player's exact (fractional) iso position, not
            // just their tile cell, or movement between tiles would snap
            // instead of scroll smoothly.
            float playerIsoX = (playerTilePosition.X - playerTilePosition.Y) * TileSize;
            float playerIsoY = (playerTilePosition.X + playerTilePosition.Y) * TileSize;

            for (int ty = centerY - viewRangeInTiles; ty <= centerY + viewRangeInTiles; ty++)
            {
                if (ty < 0)
                {
                    continue;
                }

                for (int tx = centerX - viewRangeInTiles; tx <= centerX + viewRangeInTiles; tx++)
                {
                    if (tx < 0)
                    {
                        continue;
                    }

                    if (!TryGetTile(maps, mapIndex, tx, ty, out ushort tileId, out sbyte z))
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
                    var screenPos = new Vector2(planarX - playerIsoX, planarY - playerIsoY) + screenCenterOffset;

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
    }
}
