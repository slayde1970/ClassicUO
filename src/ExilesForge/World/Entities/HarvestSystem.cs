// SPDX-License-Identifier: BSD-2-Clause

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TEF.World.Entities
{
    /// <summary>
    /// Owns the harvest action and the respawn countdown tick. Stateless -
    /// operates entirely on whatever EntityWorld is passed in. The renderer
    /// never inspects Harvestable at all; it only ever reads whatever
    /// Appearance.Graphic currently says, so this system writes Appearance
    /// directly when a resource's depleted state flips.
    /// </summary>
    public static class HarvestSystem
    {
        /// <summary>Ticks every depleted Harvestable's respawn countdown; respawns any that reach zero.</summary>
        public static void Update(EntityWorld world, float deltaSeconds)
        {
            foreach (int id in world.Harvestables.Keys)
            {
                ref var harvestable = ref CollectionsMarshal.GetValueRefOrNullRef(world.Harvestables, id);
                if (Unsafe.IsNullRef(ref harvestable) || !harvestable.IsDepleted)
                {
                    continue;
                }

                harvestable.RespawnCountdown -= deltaSeconds;
                if (harvestable.RespawnCountdown > 0f)
                {
                    continue;
                }

                harvestable.IsDepleted = false;
                harvestable.YieldRemaining = harvestable.YieldMax;

                ref var appearance = ref CollectionsMarshal.GetValueRefOrNullRef(world.Appearances, id);
                if (!Unsafe.IsNullRef(ref appearance))
                {
                    appearance.Graphic = harvestable.AvailableGraphic;
                }
            }
        }

        /// <summary>
        /// Takes one unit of yield from a Harvestable entity, depleting it
        /// (and swapping its Appearance to DepletedGraphic) when it runs out.
        /// Returns false if the entity isn't Harvestable or is already
        /// depleted.
        /// </summary>
        public static bool TryHarvest(EntityWorld world, int entityId)
        {
            ref var harvestable = ref CollectionsMarshal.GetValueRefOrNullRef(world.Harvestables, entityId);
            if (Unsafe.IsNullRef(ref harvestable) || harvestable.IsDepleted || harvestable.YieldRemaining <= 0)
            {
                return false;
            }

            harvestable.YieldRemaining--;

            if (harvestable.YieldRemaining <= 0)
            {
                harvestable.IsDepleted = true;
                harvestable.RespawnCountdown = harvestable.RespawnDuration;

                ref var appearance = ref CollectionsMarshal.GetValueRefOrNullRef(world.Appearances, entityId);
                if (!Unsafe.IsNullRef(ref appearance))
                {
                    appearance.Graphic = harvestable.DepletedGraphic;
                }
            }

            return true;
        }
    }
}
