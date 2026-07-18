// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Renderer;
using Microsoft.Xna.Framework;
using UOA.Assets;

namespace UOA.World
{
    /// <summary>
    /// How <see cref="TileRenderer"/> pulls live, game-owned entities into its
    /// isometric draw/pick pass without depending on any concrete game type
    /// (Tier 4.5). A game's entity system implements this; the world module
    /// only knows "something can hand me the entities on a tile, in the same
    /// <see cref="WorldMap.StaticTile"/> shape map statics already use."
    /// </summary>
    public interface IWorldEntitySource
    {
        /// <summary>Refresh the per-tile entity projection for the given tile-range window (called once per frame before the draw walk).</summary>
        void Rebuild(int x0, int y0, int x1, int y1);

        /// <summary>Entities occupying a tile, as StaticTile-shaped rows, or null/empty if none.</summary>
        List<WorldMap.StaticTile> GetAt(int tx, int ty);
    }

    /// <summary>
    /// The single player-like actor <see cref="TileRenderer"/> interleaves into
    /// the back-to-front pass so statics on nearer tiles can occlude it (Tier
    /// 4.5). Kept minimal - only what the render/pick pass actually needs - so
    /// the world module never depends on the game's concrete player class.
    /// </summary>
    public interface IWorldPlayer
    {
        ushort Graphic { get; }
        Vector2 WorldPosition { get; }
        sbyte Z { get; }

        void Draw(UltimaBatcher2D batcher, GameAssets assets, Vector2 screenCenterOffset);
        bool TryPick(GameAssets assets, Point cursorPosition, Vector2 screenCenterOffset);
    }
}
