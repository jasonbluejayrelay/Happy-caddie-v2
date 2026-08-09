using System;
using Harvestline.Core.Content;
using Harvestline.Core.Model;

namespace Harvestline.Core.Progression
{
    /// <summary>Outcome of resolving a single Harvest.</summary>
    public readonly struct HarvestResult
    {
        public readonly bool Met;
        public readonly double Demand;
        public readonly double FoodAvailable;
        public readonly double Surplus;        // food beyond demand (0 on shortfall)
        public readonly double FulfillmentRatio; // available/demand, capped at 1
        public readonly int OldPopulation;
        public readonly int NewPopulation;
        public readonly long SealsAwarded;
        public readonly bool StoresTokenUsed;  // a grace token absorbed a failure

        public HarvestResult(bool met, double demand, double foodAvailable, double surplus,
            double fulfillmentRatio, int oldPop, int newPop, long seals, bool tokenUsed)
        {
            Met = met;
            Demand = demand;
            FoodAvailable = foodAvailable;
            Surplus = surplus;
            FulfillmentRatio = fulfillmentRatio;
            OldPopulation = oldPop;
            NewPopulation = newPop;
            SealsAwarded = seals;
            StoresTokenUsed = tokenUsed;
        }
    }

    /// <summary>
    /// The recurring survival deadline (spec §5). Fires every 72 real hours on a
    /// fixed clock. Checks food stock against demand, grows or shrinks population,
    /// awards Seals, and manages the Stores grace buffer that protects a casual
    /// player from a single missed weekend.
    /// </summary>
    public static class HarvestResolver
    {
        public const double HarvestIntervalSeconds = 72.0 * 3600.0;
        public const double GrowthFactor = 1.12;
        public const double FailFloor = 0.85; // pop *= 0.85 + 0.15 * fulfillment on shortfall

        /// <summary>demand = 10 * population^1.15 (spec §5).</summary>
        public static double Demand(int population) => 10.0 * Math.Pow(population, 1.15);

        /// <summary>seals = floor(sqrt(surplus / 50)) (spec §5).</summary>
        public static long SealsForSurplus(double surplus) =>
            surplus <= 0 ? 0 : (long)Math.Floor(Math.Sqrt(surplus / 50.0));

        /// <summary>
        /// Resolve a Harvest against the colony's inventory, mutating population,
        /// Seals, Stores tokens, and consuming food from <paramref name="inventory"/>.
        /// </summary>
        public static HarvestResult Resolve(ColonyState colony, Inventory inventory)
        {
            double demand = Demand(colony.Population);
            double food = Simulation.FactorySimulator.FoodStock(inventory);
            int oldPop = colony.Population;

            if (food >= demand)
            {
                // Success: consume exactly the demand, grow, award Seals, bank a token.
                ConsumeFood(inventory, demand);
                double surplus = food - demand;
                long seals = SealsForSurplus(surplus);

                int grown = (int)Math.Floor(colony.Population * GrowthFactor);
                if (grown <= colony.Population) grown = colony.Population + 1; // min +1
                colony.Population = grown;
                colony.Seals += seals;
                colony.LifetimeSeals += seals;
                if (colony.StoresTokens < ColonyState.MaxStoresTokens) colony.StoresTokens++;
                colony.HarvestsSucceeded++;

                return new HarvestResult(true, demand, food, surplus, 1.0, oldPop, colony.Population, seals, false);
            }

            // Shortfall.
            double ratio = demand <= 0 ? 1.0 : food / demand;
            ConsumeFood(inventory, food); // all food consumed

            if (colony.StoresTokens > 0)
            {
                // Grace: a token absorbs the loss entirely, population unchanged.
                colony.StoresTokens--;
                colony.HarvestsFailed++;
                return new HarvestResult(false, demand, food, 0, ratio, oldPop, colony.Population, 0, true);
            }

            int shrunk = (int)Math.Floor(colony.Population * (FailFloor + 0.15 * ratio));
            if (shrunk < 1) shrunk = 1;
            colony.Population = shrunk;
            colony.HarvestsFailed++;
            return new HarvestResult(false, demand, food, 0, ratio, oldPop, colony.Population, 0, false);
        }

        /// <summary>
        /// Remove <paramref name="foodValueToConsume"/> worth of food from the
        /// inventory, spending lowest-value food items first so high-value goods
        /// (Preserves, Rations) are preserved when only a partial amount is needed.
        /// Deterministic (fixed item order).
        /// </summary>
        internal static void ConsumeFood(Inventory inventory, double foodValueToConsume)
        {
            if (foodValueToConsume <= 0) return;
            double remaining = foodValueToConsume;

            // Ascending food value order: Egg(0.5), Bread(1), Milk(1), Preserves(3), Rations(8).
            ReadOnlySpan<ItemType> order = stackalloc ItemType[]
            {
                ItemType.Egg, ItemType.Bread, ItemType.Milk, ItemType.Preserves, ItemType.Rations
            };

            foreach (var item in order)
            {
                if (remaining <= 1e-9) break;
                double value = ContentDatabase.FoodValueOf(item);
                if (value <= 0) continue;
                double have = inventory.Get(item);
                if (have <= 0) continue;

                double neededUnits = remaining / value;
                if (neededUnits >= have)
                {
                    inventory.Set(item, 0);
                    remaining -= have * value;
                }
                else
                {
                    inventory.Set(item, have - neededUnits);
                    remaining = 0;
                }
            }
        }
    }
}
