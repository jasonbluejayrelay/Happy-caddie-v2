using System;
using System.Collections.Generic;

namespace Harvestline.Core.Model
{
    /// <summary>
    /// The mutable state of a single colony: the grid of placed structures plus the
    /// shared inventory. This is what the simulation reads and mutates, and what the
    /// save system serializes (grid + inventories per spec §8).
    /// </summary>
    public sealed class GridState
    {
        /// <summary>Edge length in tiles. Starts 8, expands in rings up to 24 (spec §3).</summary>
        public int Edge { get; private set; }

        public const int MinEdge = 8;
        public const int MaxEdge = 24;

        private readonly List<PlacedStructure> _structures = new List<PlacedStructure>();
        private int _nextInstanceId;

        public IReadOnlyList<PlacedStructure> Structures => _structures;
        public Inventory Inventory { get; } = new Inventory();

        public GridState(int edge = MinEdge)
        {
            if (edge < MinEdge || edge > MaxEdge) throw new ArgumentOutOfRangeException(nameof(edge));
            Edge = edge;
        }

        /// <summary>Expand the grid by one ring (each side +2). Returns false at the cap.</summary>
        public bool Expand()
        {
            if (Edge + 2 > MaxEdge) return false;
            Edge += 2;
            return true;
        }

        public int TotalTiles => Edge * Edge;

        public int OccupiedTiles
        {
            get
            {
                int n = 0;
                foreach (var s in _structures) n += s.Def.Footprint;
                return n;
            }
        }

        public bool IsInBounds(int x, int y, int size) =>
            x >= 0 && y >= 0 && x + size <= Edge && y + size <= Edge;

        public bool IsFootprintFree(int x, int y, int size)
        {
            if (!IsInBounds(x, y, size)) return false;
            for (int dx = 0; dx < size; dx++)
                for (int dy = 0; dy < size; dy++)
                    if (StructureAt(x + dx, y + dy) != null)
                        return false;
            return true;
        }

        public PlacedStructure? StructureAt(int tx, int ty)
        {
            foreach (var s in _structures)
                if (s.Covers(tx, ty)) return s;
            return null;
        }

        /// <summary>Place a structure. Throws if the footprint is out of bounds or occupied.</summary>
        public PlacedStructure Place(StructureDef def, int x, int y,
            Facing facing = Facing.North, ItemType assignedItem = ItemType.None)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (!IsFootprintFree(x, y, def.Size))
                throw new InvalidOperationException($"Cannot place {def.Id} at ({x},{y}): out of bounds or occupied.");

            var placed = new PlacedStructure(_nextInstanceId++, def, x, y, facing, assignedItem);
            _structures.Add(placed);
            RecomputeStorageCapacity();
            return placed;
        }

        public bool Remove(int instanceId)
        {
            for (int i = 0; i < _structures.Count; i++)
            {
                if (_structures[i].InstanceId == instanceId)
                {
                    _structures.RemoveAt(i);
                    RecomputeStorageCapacity();
                    return true;
                }
            }
            return false;
        }

        /// <summary>Recompute per-item storage capacity from base + silo bonuses.</summary>
        public void RecomputeStorageCapacity()
        {
            // Reset all to base.
            for (int i = 0; i < ItemTypeExtensions.Count; i++)
                Inventory.SetCapacity((ItemType)i, Inventory.BaseCapacity);

            // Accumulate silo bonuses per assigned item.
            var bonus = new double[ItemTypeExtensions.Count];
            foreach (var s in _structures)
            {
                if (s.Def.Category == StructureCategory.Storage && s.AssignedItem != ItemType.None)
                    bonus[s.AssignedItem.Index()] += s.Def.StorageBonus;
            }
            for (int i = 0; i < ItemTypeExtensions.Count; i++)
                if (bonus[i] > 0)
                    Inventory.SetCapacity((ItemType)i, Inventory.BaseCapacity + bonus[i]);
        }

        /// <summary>Used by the save system to restore a structure with its original instance id.</summary>
        public PlacedStructure Restore(int instanceId, StructureDef def, int x, int y, Facing facing, ItemType assignedItem)
        {
            var placed = new PlacedStructure(instanceId, def, x, y, facing, assignedItem);
            _structures.Add(placed);
            if (instanceId >= _nextInstanceId) _nextInstanceId = instanceId + 1;
            return placed;
        }
    }
}
