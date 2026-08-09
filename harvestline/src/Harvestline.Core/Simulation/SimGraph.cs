using System.Collections.Generic;
using Harvestline.Core.Model;

namespace Harvestline.Core.Simulation
{
    /// <summary>
    /// A flat, array-of-structs view of a <see cref="GridState"/> prepared once per
    /// simulation run. The structure set does not change while a horizon is being
    /// integrated (only stocks do), so this is built once and reused across every
    /// discontinuity event — keeping the hot loop allocation-free and deterministic
    /// (iteration is always in placement order).
    /// </summary>
    public sealed class SimGraph
    {
        public readonly struct Line
        {
            public readonly int Item;         // ItemType index
            public readonly double RateFull;  // units/sec at full activity (qty / secondsPerCycle)
            public Line(int item, double rateFull) { Item = item; RateFull = rateFull; }
        }

        public sealed class Node
        {
            public int InstanceId;
            public string Id = "";
            public StructureCategory Category;
            public Line[] Inputs = System.Array.Empty<Line>();
            public Line[] Outputs = System.Array.Empty<Line>();
            public double PowerDraw;
            public double PowerOutputFull;      // generators only
            public double BoostPerSource;       // if boosted: fractional boost each active source grants
            public int[] BoostSources = System.Array.Empty<int>(); // node indices that boost this node
            public bool IsBoosted => BoostSources.Length > 0;
        }

        public Node[] Nodes { get; }
        public int ItemCount => ItemTypeExtensions.Count;

        private SimGraph(Node[] nodes) { Nodes = nodes; }

        /// <summary>
        /// Build the flow graph. <paramref name="outputMultiplier"/> is the permanent
        /// prestige yield bonus (spec §2): it scales every machine's item output while
        /// leaving inputs and power draw unchanged — a "+X% output" buff bought with
        /// Seals. Default 1.0 (no bonus).
        /// </summary>
        public static SimGraph Build(GridState grid, double outputMultiplier = 1.0)
        {
            var structures = grid.Structures;
            int n = structures.Count;
            var nodes = new Node[n];

            // Map instanceId -> node index for adjacency wiring.
            var indexById = new Dictionary<int, int>(n);
            for (int i = 0; i < n; i++) indexById[structures[i].InstanceId] = i;

            for (int i = 0; i < n; i++)
            {
                var s = structures[i];
                var def = s.Def;
                var node = new Node
                {
                    InstanceId = s.InstanceId,
                    Id = def.Id,
                    Category = def.Category,
                };

                var inputs = new List<Line>();
                var outputs = new List<Line>();

                if (def.Recipe != null)
                {
                    double cr = def.Recipe.CycleRate;
                    foreach (var stack in def.Recipe.Inputs)
                        inputs.Add(new Line(stack.Item.Index(), stack.Quantity * cr));
                    foreach (var stack in def.Recipe.Outputs)
                        outputs.Add(new Line(stack.Item.Index(), stack.Quantity * cr * outputMultiplier));
                    node.PowerDraw = def.PowerDraw;
                }

                if (def.Category == StructureCategory.Generator)
                {
                    // Fuel modeled as an input line so starvation logic is uniform.
                    double fuelRate = def.FuelPerCycle / def.FuelSecondsPerCycle;
                    inputs.Add(new Line(def.FuelItem.Index(), fuelRate));
                    node.PowerOutputFull = def.PowerOutput;
                }

                if (def.Category == StructureCategory.Modifier)
                {
                    // Upkeep modeled as an input line; the modifier's activity is
                    // limited by its upkeep supply, and that activity scales its boost.
                    if (def.ModifierUpkeepItem != ItemType.None)
                    {
                        double upkeepRate = def.ModifierUpkeepPerCycle / def.ModifierSecondsPerCycle;
                        inputs.Add(new Line(def.ModifierUpkeepItem.Index(), upkeepRate));
                    }
                }

                node.Inputs = inputs.ToArray();
                node.Outputs = outputs.ToArray();
                nodes[i] = node;
            }

            // Wire adjacency boosts: for each modifier, find adjacent structures it boosts.
            for (int i = 0; i < n; i++)
            {
                var s = structures[i];
                if (s.Def.Category != StructureCategory.Modifier) continue;
                if (s.Def.BoostsStructureId == null) continue;

                for (int j = 0; j < n; j++)
                {
                    if (j == i) continue;
                    var t = structures[j];
                    if (t.Def.Id != s.Def.BoostsStructureId) continue;
                    if (!s.IsAdjacentTo(t)) continue;

                    var target = nodes[j];
                    var srcList = new List<int>(target.BoostSources) { i };
                    target.BoostSources = srcList.ToArray();
                    target.BoostPerSource = s.Def.AdjacencyBoost; // uniform per boost type
                }
            }

            return new SimGraph(nodes);
        }
    }
}
