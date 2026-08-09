using System.Collections.Generic;
using Harvestline.Core.Model;
using Harvestline.Core.Rng;

namespace Harvestline.Core.Economy
{
    /// <summary>A standing offer: deliver a quantity of an item by a deadline for above-market pay.</summary>
    public sealed class Contract
    {
        public ItemType Item;
        public int Quantity;
        public long Reward;         // total Credits on fulfillment
        public long Penalty;        // Credits lost if the deadline passes unfulfilled
        public double DeadlineUtcSeconds;
    }

    /// <summary>
    /// Generates the two active contracts (spec §6). Contracts pay above market for a
    /// committed quantity by a deadline; accepting one is a bet. Deterministic given
    /// the shared RNG.
    /// </summary>
    public sealed class ContractGenerator
    {
        private readonly DeterministicRng _rng;
        private readonly MarketSimulator _market;

        public const int MaxActive = 2;
        public const double DefaultDurationSeconds = 48.0 * 3600.0;

        public ContractGenerator(DeterministicRng rng, MarketSimulator market)
        {
            _rng = rng;
            _market = market;
        }

        /// <summary>Generate a fresh set of active contracts from currently traded goods.</summary>
        public List<Contract> Generate(double nowUtcSeconds)
        {
            var sellable = new List<ItemType>();
            for (int i = 0; i < ItemTypeExtensions.Count; i++)
            {
                var item = (ItemType)i;
                if (_market.Commodities.ContainsKey(item)) sellable.Add(item);
            }

            var contracts = new List<Contract>(MaxActive);
            if (sellable.Count == 0) return contracts;

            for (int n = 0; n < MaxActive; n++)
            {
                var item = sellable[_rng.NextInt(0, sellable.Count)];
                double price = _market.PriceOf(item);
                int qty = _rng.NextInt(20, 80);
                double bonus = _rng.NextDouble(1.20, 1.60); // 20–60% above market
                long reward = (long)System.Math.Floor(price * qty * bonus);
                contracts.Add(new Contract
                {
                    Item = item,
                    Quantity = qty,
                    Reward = reward,
                    Penalty = reward / 4,
                    DeadlineUtcSeconds = nowUtcSeconds + DefaultDurationSeconds,
                });
            }
            return contracts;
        }
    }
}
