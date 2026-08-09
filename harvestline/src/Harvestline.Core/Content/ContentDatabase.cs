using System;
using System.Collections.Generic;
using Harvestline.Core.Model;

namespace Harvestline.Core.Content
{
    /// <summary>
    /// The catalog of every structure, recipe and balance constant, plus item food
    /// values and build costs. In Unity this is populated from ScriptableObjects at
    /// load; here it is authored in code so the headless core and balance bot can run
    /// standalone. Either way the solver only ever reads the resulting data records,
    /// so adding a machine never touches the solver (spec §8).
    ///
    /// Cycle times are balance data, not part of the spec's tables (which give only
    /// inputs/outputs/power); they are chosen here and are the primary tuning knob.
    /// </summary>
    public sealed class ContentDatabase
    {
        private readonly Dictionary<string, StructureDef> _byId;
        private readonly Dictionary<string, int> _buildCost;

        public IReadOnlyDictionary<string, StructureDef> Structures => _byId;

        /// <summary>Credits value counted toward Harvest demand per unit of a food item (spec §4).</summary>
        private static readonly double[] FoodValue = BuildFoodValues();

        private ContentDatabase(Dictionary<string, StructureDef> byId, Dictionary<string, int> buildCost)
        {
            _byId = byId;
            _buildCost = buildCost;
        }

        public StructureDef Get(string id) =>
            _byId.TryGetValue(id, out var d) ? d : throw new KeyNotFoundException($"Unknown structure id '{id}'.");

        public bool TryGet(string id, out StructureDef def) => _byId.TryGetValue(id, out def!);

        /// <summary>Credits to build a structure (spec §5 build step; values are balance data).</summary>
        public int BuildCost(string id) =>
            _buildCost.TryGetValue(id, out var c) ? c : throw new KeyNotFoundException($"No build cost for '{id}'.");

        public static double FoodValueOf(ItemType item) => FoodValue[item.Index()];

        private static double[] BuildFoodValues()
        {
            var v = new double[ItemTypeExtensions.Count];
            v[ItemType.Bread.Index()] = 1.0;
            v[ItemType.Egg.Index()] = 0.5;
            v[ItemType.Milk.Index()] = 1.0;
            v[ItemType.Preserves.Index()] = 3.0;
            v[ItemType.Rations.Index()] = 8.0;
            return v;
        }

        private static ItemStack[] Stacks(params (ItemType item, int qty)[] items)
        {
            var arr = new ItemStack[items.Length];
            for (int i = 0; i < items.Length; i++) arr[i] = new ItemStack(items[i].item, items[i].qty);
            return arr;
        }

        /// <summary>Builds the default v1 content set (all structures from spec §4).</summary>
        public static ContentDatabase CreateDefault()
        {
            var defs = new List<StructureDef>();
            var cost = new Dictionary<string, int>(StringComparer.Ordinal);

            void Add(StructureDef def, int buildCost)
            {
                defs.Add(def);
                cost[def.Id] = buildCost;
            }

            // ---- Tier 0 — Extraction ----
            Add(StructureDef.Extractor("soil_plot", "Soil Plot", 1,
                new Recipe(null, Stacks((ItemType.Grain, 1)), 3.0), powerDraw: 0), 15);
            Add(StructureDef.Extractor("water_pump", "Water Pump", 1,
                new Recipe(null, Stacks((ItemType.Water, 1)), 2.0), powerDraw: 2), 20);
            Add(StructureDef.Extractor("quarry", "Quarry", 2,
                new Recipe(null, Stacks((ItemType.Stone, 1)), 6.0), powerDraw: 4), 60);
            Add(StructureDef.Extractor("woodlot", "Woodlot", 1,
                new Recipe(null, Stacks((ItemType.Timber, 1)), 4.0), powerDraw: 0), 20);

            // ---- Tier 1 — Basic processing ----
            Add(StructureDef.Processor("mill", "Mill", 1,
                new Recipe(Stacks((ItemType.Grain, 2)), Stacks((ItemType.Flour, 1)), 4.0), powerDraw: 3), 45);
            Add(StructureDef.Processor("coop", "Coop", 2,
                new Recipe(Stacks((ItemType.Grain, 1), (ItemType.Water, 1)), Stacks((ItemType.Egg, 2)), 6.0), powerDraw: 2), 70);
            Add(StructureDef.Processor("kiln", "Kiln", 1,
                new Recipe(Stacks((ItemType.Stone, 2)), Stacks((ItemType.Brick, 1)), 6.0), powerDraw: 5), 55);
            Add(StructureDef.Processor("sawmill", "Sawmill", 1,
                new Recipe(Stacks((ItemType.Timber, 2)), Stacks((ItemType.Plank, 1)), 4.0), powerDraw: 3), 45);

            // ---- Tier 2 — Food goods ----
            Add(StructureDef.Processor("bakery", "Bakery", 2,
                new Recipe(Stacks((ItemType.Flour, 2), (ItemType.Water, 1)), Stacks((ItemType.Bread, 3)), 8.0), powerDraw: 6), 120);
            Add(StructureDef.Processor("dairy", "Dairy", 2,
                new Recipe(Stacks((ItemType.Grain, 2), (ItemType.Water, 2)), Stacks((ItemType.Milk, 2)), 8.0), powerDraw: 5), 110);
            Add(StructureDef.Processor("cannery", "Cannery", 2,
                new Recipe(Stacks((ItemType.Bread, 1), (ItemType.Egg, 1)), Stacks((ItemType.Preserves, 2)), 10.0), powerDraw: 8), 180);

            // ---- Tier 3 — Compound / high value ----
            Add(StructureDef.Processor("composter", "Composter", 1,
                new Recipe(Stacks((ItemType.Timber, 2), (ItemType.Water, 1)), Stacks((ItemType.Fertilizer, 1)), 8.0), powerDraw: 4), 90);
            Add(StructureDef.Processor("ration_line", "Ration Line", 2,
                new Recipe(Stacks((ItemType.Preserves, 2), (ItemType.Milk, 1)), Stacks((ItemType.Rations, 4)), 15.0), powerDraw: 12), 300);

            // ---- Support ----
            Add(StructureDef.Storage("silo", "Silo", 1, storageBonus: 200), 50);
            Add(StructureDef.Generator("biomass_generator", "Biomass Generator", 1,
                fuel: ItemType.Timber, fuelPerCycle: 1, fuelSecondsPerCycle: 6.0, powerOutput: 15), 80);
            Add(StructureDef.Generator("coal_generator", "Coal Generator", 2,
                fuel: ItemType.Coal, fuelPerCycle: 1, fuelSecondsPerCycle: 6.0, powerOutput: 60), 200);
            Add(StructureDef.Modifier("greenhouse", "Greenhouse", 2,
                boost: 0.40, upkeepItem: ItemType.Fertilizer, upkeepPerCycle: 1, upkeepSecondsPerCycle: 20.0,
                boostsStructureId: "soil_plot"), 150);

            var byId = new Dictionary<string, StructureDef>(StringComparer.Ordinal);
            foreach (var d in defs) byId[d.Id] = d;
            return new ContentDatabase(byId, cost);
        }
    }
}
