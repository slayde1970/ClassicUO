// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using TEF.World.Entities;

namespace TEF.Persistence
{
    /// <summary>
    /// Per-section save DTOs (Tier 4.5). Each maps to one
    /// <c>ISaveParticipant</c> section registered by WorldScene - deliberately
    /// independent of runtime types (EntityWorld's dictionaries, PlayerEntity,
    /// WorldClock) so the save format doesn't reshape every time an internal
    /// class does. See Design/prd-persistence.md and prd-uoa-engine.md 4.4.
    /// </summary>
    public sealed class GameStateSaveData
    {
        public int SaveVersion { get; set; } = 1;
        public int MapIndex { get; set; }
        public int WoodCollected { get; set; }
    }

    public sealed class PlayerSaveData
    {
        public float X { get; set; }
        public float Y { get; set; }
        public sbyte Z { get; set; }

        /// <summary>Direction enum, stored as its underlying byte value.</summary>
        public byte Facing { get; set; }
    }

    public sealed class WorldClockSaveData
    {
        public uint Day { get; set; }
        public float TimeOfDay { get; set; }
    }

    /// <summary>
    /// Flat "has-component" shape mirroring EntityWorld's per-type
    /// dictionaries, rather than a polymorphic component list - simplest
    /// thing that works while there are only a few component types (see
    /// Design/prd-persistence.md 4.2). Transform/Appearance are assumed
    /// present on every saved entity; Harvestable/Interactable are optional
    /// per entity via the Has* flags.
    /// </summary>
    public sealed class EntitySaveData
    {
        public float X { get; set; }
        public float Y { get; set; }
        public sbyte Z { get; set; }
        public ushort Graphic { get; set; }
        public ushort Hue { get; set; }
        public byte Height { get; set; }

        public bool HasHarvestable { get; set; }
        public ResourceType Resource { get; set; }
        public int YieldRemaining { get; set; }
        public int YieldMax { get; set; }
        public ushort AvailableGraphic { get; set; }
        public ushort DepletedGraphic { get; set; }
        public bool IsDepleted { get; set; }
        public float RespawnCountdown { get; set; }
        public float RespawnDuration { get; set; }

        public bool HasInteractable { get; set; }
    }
}
