// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using ClassicUO.Assets;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using UOA.Assets;

namespace UOA.World
{
    /// <summary>
    /// The shared world-data layer: reads land tiles and statics straight from
    /// the mul/uop map + statics files (per 8x8 block, like
    /// ClassicUO.Client's Game/Map/Chunk.cs) and answers both rendering
    /// queries (TileRenderer) and gameplay queries (walkability, surface Z for
    /// PlayerEntity). Blocks are cached persistently - the map data never
    /// changes, so once read a block is kept, which also means revisited areas
    /// aren't re-read. Far-away blocks are evicted (see EvictFarBlocks) so a
    /// long roam doesn't grow memory unbounded; a block simply gets re-read
    /// from disk (cheap - a raw seek+read, no parsing beyond struct layout)
    /// if the player wanders back.
    /// </summary>
    public sealed class WorldMap
    {
        public struct StaticTile
        {
            public ushort Graphic;
            public ushort Hue;
            public sbyte Z;
            public short PriorityZ;

            // The Z fed to the GPU depth key (Tier 4.6): a de-duplicated
            // PriorityZ, strictly increasing within a tile's sorted stack, so
            // two statics at the same PriorityZ (e.g. a rug over a floor) get
            // distinct, depth-buffer-resolvable depths instead of tying and
            // letting the mesh's texture-bucket draw order pick the winner
            // arbitrarily. Set in GetBlockStatics after the stack sort; for
            // entity-projected tiles it's just PriorityZ (see EntityRenderSystem).
            public short DepthZ;

            public int ReadOrder; // tiebreaker for a stable sort - see GetBlockStatics

            // 0 for a real map static; the owning entity id when this entry
            // was projected from an entity (EntityRenderSystem). Lets the
            // shared render/merge path stay identical while mouse-picking can
            // still tell "clicked an entity" from "clicked a map static".
            public int EntityId;

            // Baked in once (at block-load time for map statics; at
            // construction for entity-projected tiles) instead of recomputed
            // every frame in TileRenderer - see CanDrawStatic/ComputeHueVector.
            // Real per-frame cost measured at ~13k static draws/frame in a
            // dense area; re-deriving these per static per frame was pure
            // waste since neither ever changes for a given map static.
            public bool Drawable;
            public Vector3 HueVector;
        }

        public const int BlockSize = 8;

        // Vertical model for walkability, in UO z units.
        private const int PlayerHeight = 16; // Constants.DEFAULT_CHARACTER_HEIGHT - clearance a body needs above a surface
        private const int MaxStepUp = 6;     // how far up you can climb onto a surface in one tile (tunable; UO's stair/bridge logic is subtler)

        private readonly GameAssets _assets;
        private readonly int _mapIndex;
        private readonly Dictionary<long, MapBlock?> _blockCache = new();
        private readonly Dictionary<long, List<StaticTile>[]> _staticCache = new();

        // Tier 4 #13 - persistent, texture-bucketed land vertex data per
        // block; see BlockMesh's own doc comment. Same key scheme as the
        // caches above.
        private readonly Dictionary<long, BlockMesh> _blockMeshes = new();

        // Tier 4.7: per-tile mutation overlay applied on top of the read-only
        // disk map data whenever a block's static list is (re)built - statics
        // ADDED by the game (built walls, planted gardens) and disk statics
        // SUPPRESSED (chopped trees, mined veins, cleared walls). Lives OUTSIDE
        // the block/static/mesh caches, so edits survive cache eviction +
        // re-read; a mutation marks the owning block dirty so its cached static
        // list and BlockMesh rebuild with the overlay applied. (Persisting these
        // across save/load is a game-level SaveManager participant - not wired
        // here yet.)
        private readonly Dictionary<(int X, int Y), TileOverride> _tileOverrides = new();

        // Added statics get a read-order well past any disk block's, so they
        // tie-break ABOVE disk statics at the same PriorityZ (placed on top).
        private int _nextOverrideReadOrder = 1_000_000;

        private sealed class TileOverride
        {
            public List<StaticTile> Added;
            public HashSet<(ushort Graphic, sbyte Z)> Suppressed;
        }

        // Cheap early-out for EvictFarBlocks: it only actually walks the
        // caches when the player's own block changes, so it's effectively
        // free on the vast majority of frames.
        private int _lastEvictBlockX = int.MinValue;
        private int _lastEvictBlockY = int.MinValue;

        // Scratch list reused by EvictFarBlocks so eviction doesn't allocate
        // every time it runs (dictionaries can't be modified while iterating).
        private readonly List<long> _evictScratch = new();

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

        /// <summary>Number of map blocks currently cached - for debug/verification that EvictFarBlocks keeps this bounded during a long roam, not general-purpose API.</summary>
        public int CachedBlockCount => _blockCache.Count;

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
                        Drawable = CanDrawStatic(_assets, sb.Color),
                        HueVector = ComputeHueVector(_assets, sb.Color, sb.Hue),
                    });
                }

            }

            // Tier 4.7: overlay the game's per-tile edits on top of the disk
            // data (creates cells if an empty-disk block now has added statics),
            // then sort each tile's stack + assign depth.
            cells = ApplyOverrides(blockX, blockY, cells);

            if (cells != null)
            {
                foreach (var list in cells)
                {
                    // Stable sort: ties keep read order, matching
                    // Chunk.AddGameObject appending same-priority statics in
                    // insertion order. List<T>.Sort is NOT stable, so break
                    // ties on ReadOrder explicitly (added statics carry a high
                    // ReadOrder so they land above disk statics at the same Z).
                    list?.Sort(static (a, b) =>
                    {
                        int cmp = a.PriorityZ.CompareTo(b.PriorityZ);
                        return cmp != 0 ? cmp : a.ReadOrder.CompareTo(b.ReadOrder);
                    });

                    AssignDepthZ(list);
                }
            }

            _staticCache[key] = cells;
            return cells;
        }

        // Applies the per-tile mutation overlay to a block's freshly-read disk
        // static grid (Tier 4.7): drops suppressed disk statics and appends
        // added ones. Returns the (possibly newly-allocated) cell grid.
        private List<StaticTile>[] ApplyOverrides(int blockX, int blockY, List<StaticTile>[] cells)
        {
            int baseX = blockX << 3;
            int baseY = blockY << 3;

            for (int ly = 0; ly < BlockSize; ly++)
            {
                for (int lx = 0; lx < BlockSize; lx++)
                {
                    if (!_tileOverrides.TryGetValue((baseX + lx, baseY + ly), out var over))
                    {
                        continue;
                    }

                    int pos = (ly << 3) + lx;
                    cells ??= new List<StaticTile>[BlockSize * BlockSize];

                    if (over.Suppressed != null && cells[pos] != null)
                    {
                        cells[pos].RemoveAll(s => over.Suppressed.Contains((s.Graphic, s.Z)));
                    }

                    if (over.Added != null && over.Added.Count > 0)
                    {
                        (cells[pos] ??= new List<StaticTile>()).AddRange(over.Added);
                    }
                }
            }

            return cells;
        }

        // --- Dynamic-world mutation API (Tier 4.7) -------------------------
        // Edits are stored in the persistent overlay and applied whenever the
        // affected block's static list is (re)built; each edit marks that block
        // dirty so its cached statics + mesh rebuild. Coordinates are absolute
        // tile coords.

        /// <summary>Places a new static on a tile (a built wall, planted plant, dropped item, ...) and rebuilds its block. Survives cache eviction.</summary>
        public void AddStatic(int tileX, int tileY, ushort graphic, ushort hue, sbyte z)
        {
            var over = GetOrCreateOverride(tileX, tileY);
            (over.Added ??= new List<StaticTile>()).Add(BuildStaticTile(graphic, hue, z));
            MarkTileDirty(tileX, tileY);
        }

        /// <summary>Hides a disk (map-data) static on a tile - a chopped tree, mined vein, or cleared wall - matched by graphic + Z. Reverse with <see cref="UnsuppressStatic"/>.</summary>
        public void SuppressStatic(int tileX, int tileY, ushort graphic, sbyte z)
        {
            var over = GetOrCreateOverride(tileX, tileY);
            (over.Suppressed ??= new HashSet<(ushort, sbyte)>()).Add((graphic, z));
            MarkTileDirty(tileX, tileY);
        }

        /// <summary>Un-hides a previously suppressed disk static (e.g. a harvested map resource respawning).</summary>
        public void UnsuppressStatic(int tileX, int tileY, ushort graphic, sbyte z)
        {
            if (_tileOverrides.TryGetValue((tileX, tileY), out var over)
                && over.Suppressed != null
                && over.Suppressed.Remove((graphic, z)))
            {
                MarkTileDirty(tileX, tileY);
            }
        }

        /// <summary>Drops a tile's cached static list AND its block mesh so both rebuild (with the overlay applied) on next access. Land is unaffected.</summary>
        public void MarkTileDirty(int tileX, int tileY) => MarkBlockDirty(tileX >> 3, tileY >> 3);

        /// <summary>Drops a block's cached static list and mesh so they rebuild on next access.</summary>
        public void MarkBlockDirty(int blockX, int blockY)
        {
            long key = ((long)blockX << 32) | (uint)blockY;
            _staticCache.Remove(key);
            _blockMeshes.Remove(key);
        }

        private TileOverride GetOrCreateOverride(int tileX, int tileY)
        {
            if (!_tileOverrides.TryGetValue((tileX, tileY), out var over))
            {
                over = new TileOverride();
                _tileOverrides[(tileX, tileY)] = over;
            }

            return over;
        }

        // Builds a StaticTile for an added static with the same baked-in fields
        // (PriorityZ/Drawable/HueVector) GetBlockStatics computes for disk
        // statics. DepthZ is assigned later, during the per-tile stack sort.
        private StaticTile BuildStaticTile(ushort graphic, ushort hue, sbyte z) => new StaticTile
        {
            Graphic = graphic,
            Hue = hue,
            Z = z,
            PriorityZ = ComputePriorityZ(_assets, graphic, z),
            ReadOrder = _nextOverrideReadOrder++,
            Drawable = CanDrawStatic(_assets, graphic),
            HueVector = ComputeHueVector(_assets, graphic, hue),
        };

        // Tier 4.6: give each static in a tile's back-to-front sorted stack a
        // strictly-increasing DepthZ, bumping only exact PriorityZ ties (or
        // inversions) by the minimum whole unit needed. A whole PriorityZ unit
        // is ~0.01 in the depth key - depth-buffer-resolvable at map scale,
        // where a fractional bias would be lost to float/24-bit-depth
        // precision. Genuine gaps are preserved (only entries <= the running
        // max get bumped), so a rug over a floor now gets a distinct, higher
        // depth than the floor instead of tying and letting the mesh's
        // texture-bucket draw order pick the winner.
        private static void AssignDepthZ(List<StaticTile> list)
        {
            if (list == null)
            {
                return;
            }

            int last = int.MinValue;

            for (int i = 0; i < list.Count; i++)
            {
                var s = list[i];
                int depth = s.PriorityZ > last ? s.PriorityZ : last + 1;
                s.DepthZ = (short)depth;
                list[i] = s;
                last = depth;
            }
        }

        /// <summary>Builds (once, lazily) or returns the cached BlockMesh for a block - Tier 4 #13.</summary>
        public BlockMesh GetOrBuildBlockMesh(int blockX, int blockY)
        {
            long key = ((long)blockX << 32) | (uint)blockY;

            if (_blockMeshes.TryGetValue(key, out var mesh))
            {
                return mesh;
            }

            mesh = new BlockMesh();
            mesh.Build(this, _assets, blockX, blockY);
            _blockMeshes[key] = mesh;
            return mesh;
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
        /// Roof-hiding gate (Tier 4.5): is the player standing under a covering
        /// (a roof or upper-floor surface) at tile (x, y)? Ported from the way
        /// ClassicUO's UpdateMaxDrawZ decides whether to hide roofs at all - it
        /// scans the player's own tile (and the SE-adjacent one, since an iso
        /// roof can be anchored a tile over) for a roof/surface static sitting
        /// at least <paramref name="clearance"/> above the player's feet. Only
        /// when one exists should statics above the player be hidden; out in the
        /// open this returns false so nearby buildings keep their roofs. The
        /// cutoff VALUE stays a flat player.Z + clearance (caller's choice) so
        /// it doesn't flicker as roof-tile heights vary underfoot.
        /// </summary>
        public bool IsUnderRoof(int x, int y, sbyte footZ, int clearance)
        {
            return HasCeilingAbove(x, y, footZ, clearance)
                || HasCeilingAbove(x + 1, y + 1, footZ, clearance);
        }

        private bool HasCeilingAbove(int x, int y, sbyte footZ, int clearance)
        {
            var statics = GetStaticsAt(x, y);
            if (statics == null)
            {
                return false;
            }

            var staticData = _assets.Files.TileData.StaticData;
            int threshold = footZ + clearance;

            foreach (var s in statics)
            {
                if (s.Z < threshold || s.Graphic >= staticData.Length)
                {
                    continue;
                }

                ref readonly var data = ref staticData[s.Graphic];
                if (data.IsRoof || data.IsSurface)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Multi-story detection (Tier 4.6): the Z of the lowest floor SURFACE
        /// sitting a full storey above the player - i.e. the second floor whose
        /// underside is the ceiling of the ground-floor room the player is in -
        /// on the player's OWN tile, or null if there's no upper floor above
        /// them. Mirrors the first pass of ClassicUO's UpdateMaxDrawZ (which
        /// only lets a surface on the player's own tile set the cull level).
        /// Feed the result to <see cref="TileRenderer.StaticZCutoff"/> with
        /// <see cref="StaticCutoffMode.All"/> to hide the whole upper storey -
        /// its floor, walls, AND roof - revealing the room below; ground-floor
        /// statics (anchored below the floor's Z) stay. Only the player's own
        /// tile is checked, so a raised building the player stands beside (its
        /// floor on a neighbouring tile) is NOT treated as an upper storey and
        /// keeps its floor - see IsUnderRoof for the single-storey roof gate.
        /// </summary>
        public int? FindUpperFloorZ(int x, int y, sbyte footZ, int clearance)
        {
            var statics = GetStaticsAt(x, y);
            if (statics == null)
            {
                return null;
            }

            var staticData = _assets.Files.TileData.StaticData;
            int threshold = footZ + clearance;
            int? lowest = null;

            foreach (var s in statics)
            {
                if (s.Z < threshold || s.Graphic >= staticData.Length)
                {
                    continue;
                }

                ref readonly var data = ref staticData[s.Graphic];
                if (!data.IsSurface)
                {
                    continue;
                }

                if (lowest == null || s.Z < lowest.Value)
                {
                    lowest = s.Z;
                }
            }

            return lowest;
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

        /// <summary>
        /// Evicts cached blocks (map + statics) farther than
        /// <paramref name="keepRadiusBlocks"/> (Chebyshev distance, i.e. a
        /// square region) from the block containing <paramref name="centerTileX"/>/
        /// <paramref name="centerTileY"/> - call once per frame with the
        /// player's current tile position; the caller is expected to pass a
        /// radius with enough margin over its actual view range that a block
        /// isn't evicted and immediately re-read on the very next frame
        /// (hysteresis), e.g. WorldScene.ComputeViewRange() converted to
        /// blocks plus a buffer.
        /// </summary>
        public void EvictFarBlocks(int centerTileX, int centerTileY, int keepRadiusBlocks)
        {
            int centerBlockX = centerTileX >> 3;
            int centerBlockY = centerTileY >> 3;

            if (centerBlockX == _lastEvictBlockX && centerBlockY == _lastEvictBlockY)
            {
                return;
            }

            _lastEvictBlockX = centerBlockX;
            _lastEvictBlockY = centerBlockY;

            EvictFar(_blockCache, centerBlockX, centerBlockY, keepRadiusBlocks);
            EvictFar(_staticCache, centerBlockX, centerBlockY, keepRadiusBlocks);
            // Tier 4.7: free far blocks' meshes too (the deferred Tier 4 #13
            // cleanup). BlockMesh holds only CPU arrays + borrowed atlas texture
            // refs (no owned GPU buffer), so dropping the reference is enough.
            // Any mutation overlay (see _tileOverrides) lives outside the caches,
            // so an evicted-then-revisited block rebuilds WITH its edits intact.
            EvictFar(_blockMeshes, centerBlockX, centerBlockY, keepRadiusBlocks);
        }

        private void EvictFar<T>(Dictionary<long, T> cache, int centerBlockX, int centerBlockY, int keepRadiusBlocks)
        {
            _evictScratch.Clear();

            foreach (var key in cache.Keys)
            {
                int blockX = (int)(key >> 32);
                int blockY = (int)(uint)key;

                int distance = Math.Max(Math.Abs(blockX - centerBlockX), Math.Abs(blockY - centerBlockY));
                if (distance > keepRadiusBlocks)
                {
                    _evictScratch.Add(key);
                }
            }

            foreach (var key in _evictScratch)
            {
                cache.Remove(key);
            }
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

        /// <summary>
        /// Hue vector for a static/entity graphic+hue, baked in once (see
        /// StaticTile.HueVector) instead of recomputed by TileRenderer every
        /// frame for every static in view.
        /// </summary>
        public static Vector3 ComputeHueVector(GameAssets assets, ushort graphic, ushort hue)
        {
            var staticData = assets.Files.TileData.StaticData;
            bool inRange = graphic < staticData.Length;
            bool partialHue = inRange && staticData[graphic].IsPartialHue;

            // Translucent-flagged statics (harbor water, some decorations)
            // render at ~70% opacity so whatever sits behind them - a sunken
            // harbour's seabed land/statics - shows through, matching
            // ClassicUO's AlphaHue = 178 for IsTranslucent items (alpha here is
            // opacity: 1 = opaque). These are also kept off the BlockMesh (see
            // StaticMeshFilter.IsExcludedFromMesh) so they draw on the
            // back-to-front per-object path, where alpha blending over the
            // already-drawn opaque world is correct.
            float alpha = inRange && staticData[graphic].IsTranslucent ? 178f / 255f : 1f;

            return ShaderHueTranslator.GetHueVector(hue, partialHue, alpha);
        }

        /// <summary>
        /// Whether a static graphic should be rendered at all. Ported from
        /// ClassicUO.Client's GameObject.CanBeDrawn - it filters out the
        /// "nodraw" placeholder statics (detected by their tiledata name
        /// starting with "nodraw", plus a handful of hardcoded graphic ids)
        /// that the map data uses as spacers. Client-only special cases
        /// (gargoyle race, pre-6.0.14.4 easel) are dropped since TEF has no
        /// player-race or legacy-version handling.
        ///
        /// Does NOT check the NoDiagonal flag, unlike the real client's
        /// literal logic - that flag has produced two confirmed false
        /// positives on legitimate decorative statics in this project
        /// (large tree graphics in Tier 1; fountain-plaza lamp posts found
        /// during the Tier 4 #13 chunk-mesh work, confirmed by bypassing
        /// the check and watching them reappear). The tree case was worked
        /// around narrowly (entity-sourced tiles only); the lamp-post case
        /// is a plain MAP static, so a per-instance workaround isn't
        /// possible here - dropping the check entirely is the fix, since
        /// its intended purpose (hiding invisible "nodraw" placeholder
        /// spacers) is already covered by the name-prefix check below.
        ///
        /// Baked into StaticTile.Drawable once here (at block-load time)
        /// rather than recomputed by TileRenderer every frame - this filter
        /// only ever depends on the graphic id, which never changes for a
        /// given map static.
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

            return true;
        }
    }
}
