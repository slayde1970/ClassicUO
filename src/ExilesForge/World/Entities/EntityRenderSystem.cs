// SPDX-License-Identifier: BSD-2-Clause

using System;
using System.Collections.Generic;
using TEF.Assets;

namespace TEF.World.Entities
{
    /// <summary>
    /// Projects live entity component data into the same tuple shape
    /// TileRenderer already draws map statics from (WorldMap.StaticTile), so
    /// TileRenderer.DrawStaticsAt can do a three-way merge (map statics +
    /// entities + player) without needing to know or care whether a given
    /// tile entry came from disk or from entity state. Deliberately no
    /// IRenderable/ITileOccupant interface for this phase - components stay
    /// pure data. Revisit if/when an entity needs custom per-instance draw
    /// logic a flat (Graphic, Hue, Z) tuple can't express (e.g. an animated
    /// NPC). See Design/prd-entity-system.md section 4.6.
    ///
    /// Unlike WorldMap's static block cache (safe to keep forever - mul data
    /// never changes), this rebuilds its per-tile bucket fresh every call
    /// since entity state is mutable (harvested, spawned, despawned later).
    /// Fine at expected entity counts (tens to low hundreds); revisit if
    /// that changes.
    /// </summary>
    public sealed class EntityRenderSystem
    {
        private readonly EntityWorld _world;
        private readonly GameAssets _assets;
        private readonly Dictionary<(int X, int Y), List<WorldMap.StaticTile>> _byTile = new();

        public EntityRenderSystem(EntityWorld world, GameAssets assets)
        {
            _world = world;
            _assets = assets;
        }

        /// <summary>Rebuilds the per-tile lookup for entities within [x0, x1] x [y0, y1] (inclusive). Call once per frame before any GetAt calls.</summary>
        public void Rebuild(int x0, int y0, int x1, int y1)
        {
            _byTile.Clear();

            foreach (var (id, transform) in _world.Transforms)
            {
                int tx = (int)MathF.Floor(transform.WorldPosition.X);
                int ty = (int)MathF.Floor(transform.WorldPosition.Y);

                if (tx < x0 || tx > x1 || ty < y0 || ty > y1)
                {
                    continue;
                }

                if (!_world.Appearances.TryGetValue(id, out var appearance))
                {
                    continue;
                }

                var tile = new WorldMap.StaticTile
                {
                    Graphic = appearance.Graphic,
                    Hue = appearance.Hue,
                    Z = transform.Z,
                    // Same rule as WorldMap.ComputePriorityZ: a raised object
                    // sorts one step above a flat one at the same Z.
                    PriorityZ = (short)(transform.Z + (appearance.Height != 0 ? 1 : 0)),
                    ReadOrder = id, // stable tiebreaker, mirrors GetBlockStatics' read-order tiebreak
                    EntityId = id,  // marks this entry as entity-sourced for mouse-picking
                    // Entities are never subject to WorldMap's "nodraw
                    // placeholder" filter (that's a map-data-only heuristic -
                    // see WorldMap.CanDrawStatic), so always drawable.
                    Drawable = true,
                    HueVector = WorldMap.ComputeHueVector(_assets, appearance.Graphic, appearance.Hue),
                };

                var key = (tx, ty);
                if (!_byTile.TryGetValue(key, out var list))
                {
                    list = new List<WorldMap.StaticTile>();
                    _byTile[key] = list;
                }

                list.Add(tile);
            }

            foreach (var list in _byTile.Values)
            {
                if (list.Count > 1)
                {
                    list.Sort(static (a, b) =>
                    {
                        int cmp = a.PriorityZ.CompareTo(b.PriorityZ);
                        return cmp != 0 ? cmp : a.ReadOrder.CompareTo(b.ReadOrder);
                    });
                }
            }
        }

        public List<WorldMap.StaticTile> GetAt(int tx, int ty)
        {
            return _byTile.TryGetValue((tx, ty), out var list) ? list : null;
        }
    }
}
