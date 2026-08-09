using System;

namespace Harvestline.Core.Model
{
    /// <summary>Broad role a structure plays, used by UI grouping and the solver's phasing.</summary>
    public enum StructureCategory
    {
        Extractor,
        Processor,
        Generator,
        Storage,
        Modifier,
    }

    /// <summary>
    /// Immutable design-time definition of a placeable structure. This is the plain
    /// record that a Unity ScriptableObject deserializes into at load (spec §8:
    /// "all content is data, not code"). The solver only reads these generic fields.
    /// </summary>
    public sealed class StructureDef
    {
        /// <summary>Stable identifier used in save files and content references.</summary>
        public string Id { get; }
        public string DisplayName { get; }
        public StructureCategory Category { get; }

        /// <summary>Footprint edge length in tiles: 1 (1×1) or 2 (2×2).</summary>
        public int Size { get; }

        /// <summary>Recipe this structure runs, or null for pure storage/modifier structures.</summary>
        public Recipe? Recipe { get; }

        /// <summary>Steady-state power draw (units) at full activity. 0 for generators/passive.</summary>
        public int PowerDraw { get; }

        // --- Generator fields (Category == Generator) ---
        /// <summary>Fuel item burned by a generator. None if not a generator.</summary>
        public ItemType FuelItem { get; }
        /// <summary>Fuel units consumed per burn cycle.</summary>
        public int FuelPerCycle { get; }
        /// <summary>Seconds per burn cycle.</summary>
        public double FuelSecondsPerCycle { get; }
        /// <summary>Power capacity contributed while active (fuel available).</summary>
        public int PowerOutput { get; }

        // --- Storage fields (Category == Storage) ---
        /// <summary>Extra storage capacity granted to its assigned item type (e.g. Silo = 200).</summary>
        public int StorageBonus { get; }

        // --- Modifier fields (Category == Modifier, e.g. Greenhouse) ---
        /// <summary>Fractional output boost applied to affected adjacent structures (0.40 = +40%).</summary>
        public double AdjacencyBoost { get; }
        /// <summary>Item consumed to keep the modifier active (e.g. Greenhouse burns Fertilizer).</summary>
        public ItemType ModifierUpkeepItem { get; }
        public int ModifierUpkeepPerCycle { get; }
        public double ModifierSecondsPerCycle { get; }
        /// <summary>Which structure id this modifier boosts when adjacent (e.g. "soil_plot").</summary>
        public string? BoostsStructureId { get; }

        public int Footprint => Size * Size;

        private StructureDef(
            string id, string displayName, StructureCategory category, int size,
            Recipe? recipe, int powerDraw,
            ItemType fuelItem, int fuelPerCycle, double fuelSecondsPerCycle, int powerOutput,
            int storageBonus,
            double adjacencyBoost, ItemType modifierUpkeepItem, int modifierUpkeepPerCycle,
            double modifierSecondsPerCycle, string? boostsStructureId)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("id required", nameof(id));
            if (size != 1 && size != 2) throw new ArgumentOutOfRangeException(nameof(size), "size must be 1 or 2");
            Id = id;
            DisplayName = displayName;
            Category = category;
            Size = size;
            Recipe = recipe;
            PowerDraw = powerDraw;
            FuelItem = fuelItem;
            FuelPerCycle = fuelPerCycle;
            FuelSecondsPerCycle = fuelSecondsPerCycle;
            PowerOutput = powerOutput;
            StorageBonus = storageBonus;
            AdjacencyBoost = adjacencyBoost;
            ModifierUpkeepItem = modifierUpkeepItem;
            ModifierUpkeepPerCycle = modifierUpkeepPerCycle;
            ModifierSecondsPerCycle = modifierSecondsPerCycle;
            BoostsStructureId = boostsStructureId;
        }

        public static StructureDef Extractor(string id, string name, int size, Recipe recipe, int powerDraw) =>
            new StructureDef(id, name, StructureCategory.Extractor, size, recipe, powerDraw,
                ItemType.None, 0, 0, 0, 0, 0, ItemType.None, 0, 0, null);

        public static StructureDef Processor(string id, string name, int size, Recipe recipe, int powerDraw) =>
            new StructureDef(id, name, StructureCategory.Processor, size, recipe, powerDraw,
                ItemType.None, 0, 0, 0, 0, 0, ItemType.None, 0, 0, null);

        public static StructureDef Generator(string id, string name, int size, ItemType fuel,
            int fuelPerCycle, double fuelSecondsPerCycle, int powerOutput) =>
            new StructureDef(id, name, StructureCategory.Generator, size, null, 0,
                fuel, fuelPerCycle, fuelSecondsPerCycle, powerOutput, 0, 0, ItemType.None, 0, 0, null);

        public static StructureDef Storage(string id, string name, int size, int storageBonus) =>
            new StructureDef(id, name, StructureCategory.Storage, size, null, 0,
                ItemType.None, 0, 0, 0, storageBonus, 0, ItemType.None, 0, 0, null);

        public static StructureDef Modifier(string id, string name, int size, double boost,
            ItemType upkeepItem, int upkeepPerCycle, double upkeepSecondsPerCycle, string boostsStructureId) =>
            new StructureDef(id, name, StructureCategory.Modifier, size, null, 0,
                ItemType.None, 0, 0, 0, 0, boost, upkeepItem, upkeepPerCycle, upkeepSecondsPerCycle, boostsStructureId);
    }
}
