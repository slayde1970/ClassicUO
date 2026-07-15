// SPDX-License-Identifier: BSD-2-Clause

using Microsoft.Xna.Framework;

namespace TEF.World.Entities
{
    /// <summary>
    /// Components are plain data - no Draw()/Update() methods, no references
    /// to engine types (Game, TileRenderer, WorldMap). Behavior lives in
    /// systems (HarvestSystem, EntityRenderSystem) that operate on an
    /// EntityWorld's component dictionaries. See Design/prd-entity-system.md.
    /// </summary>
    public struct Transform
    {
        /// <summary>Fractional UO tile coordinates - same convention as PlayerEntity.WorldPosition.</summary>
        public Vector2 WorldPosition;
        public sbyte Z;
    }

    public struct Appearance
    {
        public ushort Graphic;
        public ushort Hue;

        /// <summary>
        /// Mirrors ClassicUO.Assets.TileDataLoader's StaticTiles.Height - used
        /// by EntityRenderSystem to compute PriorityZ the same way
        /// WorldMap.ComputePriorityZ does for map statics, rather than
        /// inventing a one-off sorting rule for entities.
        /// </summary>
        public byte Height;
    }

    public enum ResourceType
    {
        Wood,
        Stone,
    }

    public struct Harvestable
    {
        public ResourceType Resource;
        public int YieldRemaining;
        public int YieldMax;

        /// <summary>Appearance.Graphic value while not depleted.</summary>
        public ushort AvailableGraphic;

        /// <summary>Appearance.Graphic value while depleted (e.g. a stump).</summary>
        public ushort DepletedGraphic;

        public bool IsDepleted;

        /// <summary>
        /// Seconds remaining until respawn, decremented by HarvestSystem.
        /// Deliberately a countdown, not an absolute future tick: the Tier 3
        /// game clock doesn't exist yet, and a countdown stays meaningful
        /// regardless of clock source and serializes trivially later (just
        /// save the remaining duration). Whether respawn should progress
        /// while the game isn't running is an open question for the Tier 3
        /// persistence design, not this phase.
        /// </summary>
        public float RespawnCountdown;

        public float RespawnDuration;
    }

    /// <summary>Marker only, no fields yet - Has-style checks are just a dictionary lookup.</summary>
    public struct Interactable
    {
    }
}
