// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using ClassicUO.Assets;
using TEF.Assets;

namespace TEF.World
{
    /// <summary>
    /// The shared world-data layer: reads land tiles and statics straight from
    /// the mul/uop map + statics files (per 8x8 block, like
    /// ClassicUO.Client's Game/Map/Chunk.cs) and answers both rendering
    /// queries (TileRenderer) and gameplay queries (walkability, surface Z for
    /// PlayerEntity). Blocks are cached persistently - the map data never
    /// changes, so once read a block is kept, which also means revisited areas
    /// aren't re-read (a partial down payment on the chunk-cache roadmap item;
    /// no eviction yet, so a very long roam grows memory unbounded - fine for
    /// now, revisit when the chunk cache lands).
    /// </summary>
    public sealed class WorldMap
    {
        public struct StaticTile
        {
            public ushort Graphic;
            public ushort Hue;
            public sbyte Z;
            public short PriorityZ;
            public int ReadOrder; // tiebreaker for a stable sort - see GetBlockStatics

            // 0 for a real map static; the owning entity id when this entry
            // was projected from an entity (EntityRenderSystem). Lets the
            // shared render/merge path stay identical while mouse-picking can
            // still tell "clicked an entity" from "clicked a map static".
            public int EntityId;
        }

        public const int BlockSize = 8;

        // Vertical model for walkability, in UO z units.
        private const int PlayerHeight = 16; // Constants.DEFAULT_CHARACTER_HEIGHT - clearance a body needs above a surface
        private const int MaxStepUp = 6;     // how far up you can climb onto a surface in one tile (tunable; UO's stair/bridge logic is subtler)

        private readonly GameAssets _assets;
        private readonly int _mapIndex;
        private readonly Dictionary<long, MapBlock?> _blockCache = new();
        private readonly Dictionary<long, List<StaticTile>[]> _staticCache = new();

        // Reused scratch buffer for TryGetStandZ so per-move collision tests
        // don't allocate. (baseZ, topZ, isSurface) per solid object on a tile.
        private readonly List<(int Base, int Top, bool Surface)> _objs = new();

        public WorldMap(GameAssets assets, int mapIndex)
        {
            _assets = assets;

            int idx = mapIndex;
            assets.Files.Maps.SanitizeMapIndex(ref idx);
            assets.EnsureMapLoaded(idx);
            _mapIndex = idx;
        }

        public GameAssets Assets => _assets;
        public int MapIndex => _mapIndex;

        public bool TryGetLand(int x, int y, out ushort tileId, out sbyte z)
        {
            tileId = 0;
            z = 0;

            if (x < 0 || y < 0)
            {
                return false;
            }

            var block = GetBlock(x >> 3, y >> 3);
            if (block == null)
            {
                return false;
            }

            var b = block.Value;
            int localX = x & (BlockSize - 1);
            int localY = y & (BlockSize - 1);
            ref readonly var cell = ref b.Cells[localY * BlockSize + localX];

            tileId = (ushort)(cell.TileID & 0x3FFF);
            z = cell.Z;
            return true;
        }

        public sbyte GetLandZ(int x, int y)
        {
            return TryGetLand(x, y, out _, out sbyte z) ? z : (sbyte)0;
        }

        /// <summary>Statics for a block, grouped by local cell index ((localY &lt;&lt; 3) + localX), each cell sorted low-to-high by PriorityZ. May be null (no statics in the block).</summary>
        public List<StaticTile>[] GetBlockStatics(int blockX, int blockY)
        {
            long key = ((long)blockX << 32) | (uint)blockY;

            if (_staticCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            List<StaticTile>[] cells = null;

            ref var indexMap = ref _assets.Files.Maps.GetIndex(_mapIndex, blockX, blockY);

            if (indexMap.IsValid() && indexMap.StaticFile != null && indexMap.StaticAddress != 0 && indexMap.StaticCount > 0)
            {
                int count = (int)indexMap.StaticCount;
                var buffer = new StaticsBlock[count];

                indexMap.StaticFile.Seek((long)indexMap.StaticAddress, SeekOrigin.Begin);
                indexMap.StaticFile.Read(MemoryMarshal.AsBytes(buffer.AsSpan()));

                int readOrder = 0;

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
                        PriorityZ = ComputePriorityZ(_assets, sb.Color, sb.Z),
                        ReadOrder = readOrder++,
                    });
                }

                if (cells != null)
                {
                    foreach (var list in cells)
                    {
                        // Stable sort: ties keep file read order, matching
                        // Chunk.AddGameObject appending same-priority statics in
                        // insertion order. List<T>.Sort is NOT stable, so break
                        // ties on ReadOrder explicitly.
                        list?.Sort(static (a, b) =>
                        {
                            int cmp = a.PriorityZ.CompareTo(b.PriorityZ);
                            return cmp != 0 ? cmp : a.ReadOrder.CompareTo(b.ReadOrder);
                        });
                    }
                }
            }

            _staticCache[key] = cells;
            return cells;
        }

        public List<StaticTile> GetStaticsAt(int x, int y)
        {
            if (x < 0 || y < 0)
            {
                return null;
            }

            var cells = GetBlockStatics(x >> 3, y >> 3);
            if (cells == null)
            {
                return null;
            }

            int localPos = ((y & (BlockSize - 1)) << 3) + (x & (BlockSize - 1));
            return cells[localPos];
        }

        /// <summary>
        /// Resolves the Z the player would stand at on tile (x, y) coming from
        /// height <paramref name="fromZ"/>, and whether the tile is walkable
        /// at all. A tile is walkable if it has a standable surface (passable
        /// land, or a Surface/Bridge static top) that is reachable (not more
        /// than MaxStepUp above fromZ) and has PlayerHeight of clearance above
        /// it. Impassable land (water, mountains) and walls/blocking statics
        /// leave no valid surface, so they return false.
        ///
        /// This is a deliberately simplified stand-in for ClassicUO's full
        /// Pathfinder.CalculateNewZ (which handles multi-level bridges, corner
        /// cutting, flying, etc.) - enough to keep the player on the ground and
        /// out of water/walls, to be refined as movement needs grow.
        /// </summary>
        public bool TryGetStandZ(int x, int y, sbyte fromZ, out sbyte standZ)
        {
            standZ = fromZ;

            if (!TryGetLand(x, y, out ushort landId, out sbyte landZ))
            {
                return false;
            }

            var tileData = _assets.Files.TileData;
            _objs.Clear();

            // Land: graphics 0-2 are no-draw void, and Impassable flags water /
            // mountains. A passable land tile is a zero-thickness floor whose
            // top is landZ.
            bool landPassable = landId > 2 && !tileData.LandData[landId].IsImpassable;
            if (landPassable)
            {
                _objs.Add((landZ, landZ, true));
            }

            var statics = GetStaticsAt(x, y);
            if (statics != null)
            {
                var staticData = tileData.StaticData;

                foreach (var s in statics)
                {
                    if (s.Graphic >= staticData.Length)
                    {
                        continue;
                    }

                    ref readonly var sd = ref staticData[s.Graphic];
                    bool surface = sd.IsSurface || sd.IsBridge;
                    bool solid = surface || sd.IsImpassable;

                    // Purely decorative / passable statics (grass tufts, most
                    // deco) don't participate in collision.
                    if (!solid)
                    {
                        continue;
                    }

                    _objs.Add((s.Z, s.Z + sd.Height, surface));
                }
            }

            // Pick the highest surface reachable from fromZ (so stairs climb
            // rather than snapping to the ground beneath them), that also has
            // clearance for the body above it.
            int best = int.MinValue;
            bool found = false;

            foreach (var o in _objs)
            {
                if (!o.Surface)
                {
                    continue;
                }

                int top = o.Top;

                // Too high to step onto (a wall ledge, a table). Stepping down
                // is unrestricted for now.
                if (top - fromZ > MaxStepUp)
                {
                    continue;
                }

                // Clearance: nothing solid may intrude the band (top, top+PlayerHeight).
                // A solid strictly above `top` that starts below head height blocks.
                bool blocked = false;
                foreach (var b in _objs)
                {
                    if (b.Top > top && b.Base < top + PlayerHeight)
                    {
                        blocked = true;
                        break;
                    }
                }

                if (blocked)
                {
                    continue;
                }

                if (!found || top > best)
                {
                    best = top;
                    found = true;
                }
            }

            if (!found)
            {
                return false;
            }

            standZ = (sbyte)best;
            return true;
        }

        /// <summary>
        /// Resolves the surface Z at a tile ignoring any step-up limit - used
        /// to seed the player's Z on spawn/teleport, where there is no "from"
        /// height to climb from. Falls back to the land Z if nothing stands.
        /// </summary>
        public sbyte ResolveSpawnZ(int x, int y)
        {
            sbyte landZ = GetLandZ(x, y);
            return TryGetStandZ(x, y, landZ, out sbyte z) ? z : landZ;
        }

        private MapBlock? GetBlock(int blockX, int blockY)
        {
            long key = ((long)blockX << 32) | (uint)blockY;

            if (_blockCache.TryGetValue(key, out var cached))
            {
                return cached;
            }

            ref var indexMap = ref _assets.Files.Maps.GetIndex(_mapIndex, blockX, blockY);
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
        /// Draw-order priority for a static at a given Z, ported from
        /// Chunk.AddGameObject's default (plain item/static) case. Raw Z alone
        /// isn't enough to sort correctly: e.g. a fountain's water surface is
        /// flagged Background and sits at the same Z as the stone rim around
        /// it, but must still paint behind the rim - one step earlier despite
        /// the tied Z.
        /// </summary>
        private static short ComputePriorityZ(GameAssets assets, ushort graphic, sbyte z)
        {
            short priorityZ = z;

            if (graphic < assets.Files.TileData.StaticData.Length)
            {
                ref readonly var data = ref assets.Files.TileData.StaticData[graphic];

                if (data.IsBackground)
                {
                    priorityZ--;
                }

                if (data.Height != 0)
                {
                    priorityZ++;
                }

                if (data.IsMultiMovable)
                {
                    priorityZ++;
                }
            }

            return priorityZ;
        }
    }
}
