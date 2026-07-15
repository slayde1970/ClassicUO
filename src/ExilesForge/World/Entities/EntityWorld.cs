// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;

namespace TEF.World.Entities
{
    /// <summary>
    /// Hand-rolled entity registry: a plain int id per entity, and one
    /// dictionary per component type (not a dictionary-of-dictionaries) so
    /// adding a new component type never touches existing storage. This is
    /// deliberately not a third-party ECS (DefaultECS etc.) - the shape here
    /// (id-based entities, data-only components, plain systems) is the same
    /// shape those libraries expose, so adopting one later is additive, not
    /// a rewrite. See Design/prd-entity-system.md.
    /// </summary>
    public sealed class EntityWorld
    {
        private int _nextId = 1;

        public Dictionary<int, Transform> Transforms { get; } = new();
        public Dictionary<int, Appearance> Appearances { get; } = new();
        public Dictionary<int, Harvestable> Harvestables { get; } = new();
        public Dictionary<int, Interactable> Interactables { get; } = new();

        public int CreateEntity()
        {
            return _nextId++;
        }

        public void Destroy(int id)
        {
            Transforms.Remove(id);
            Appearances.Remove(id);
            Harvestables.Remove(id);
            Interactables.Remove(id);
        }
    }
}
