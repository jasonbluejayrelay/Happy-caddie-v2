using System;
using System.Collections.Generic;
using System.Linq;

namespace Harvestline.Core.Model
{
    /// <summary>A quantity of a single item type (an input line or output line of a recipe).</summary>
    public readonly struct ItemStack
    {
        public readonly ItemType Item;
        public readonly int Quantity;

        public ItemStack(ItemType item, int quantity)
        {
            Item = item;
            Quantity = quantity;
        }

        public override string ToString() => $"{Quantity} {Item}";
    }

    /// <summary>
    /// A production recipe: consume <see cref="Inputs"/> and yield <see cref="Outputs"/>
    /// once per <see cref="SecondsPerCycle"/>. Extractors have no inputs.
    ///
    /// The simulation works in continuous rates rather than discrete crafts, so a
    /// recipe's rate = 1 / SecondsPerCycle cycles per second. Consumption rate of
    /// an input = inputQty * cycleRate; production rate of an output = outputQty *
    /// cycleRate. The discrete "craft complete" pulse is a view-only concern (M2).
    /// </summary>
    public sealed class Recipe
    {
        public IReadOnlyList<ItemStack> Inputs { get; }
        public IReadOnlyList<ItemStack> Outputs { get; }
        public double SecondsPerCycle { get; }

        public Recipe(IReadOnlyList<ItemStack>? inputs, IReadOnlyList<ItemStack> outputs, double secondsPerCycle)
        {
            if (secondsPerCycle <= 0) throw new ArgumentOutOfRangeException(nameof(secondsPerCycle));
            Inputs = inputs ?? Array.Empty<ItemStack>();
            Outputs = outputs ?? Array.Empty<ItemStack>();
            SecondsPerCycle = secondsPerCycle;
        }

        /// <summary>Cycles per second at full activity.</summary>
        public double CycleRate => 1.0 / SecondsPerCycle;

        public bool IsExtractor => Inputs.Count == 0;

        public override string ToString()
        {
            string ins = Inputs.Count == 0 ? "∅" : string.Join(" + ", Inputs.Select(i => i.ToString()));
            string outs = Outputs.Count == 0 ? "∅" : string.Join(" + ", Outputs.Select(o => o.ToString()));
            return $"{ins} -> {outs} @ {SecondsPerCycle}s";
        }
    }
}
