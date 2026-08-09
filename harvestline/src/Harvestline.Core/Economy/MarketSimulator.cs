using System;
using System.Collections.Generic;
using Harvestline.Core.Model;
using Harvestline.Core.Rng;

namespace Harvestline.Core.Economy
{
    /// <summary>Mutable price state for one commodity.</summary>
    public sealed class Commodity
    {
        public ItemType Item;
        public double BasePrice;
        public double Price;
        /// <summary>Accumulated downward pressure from recent player selling; decays over ticks.</summary>
        public double Pressure;
        /// <summary>Larger depth = less price impact per unit sold.</summary>
        public double MarketDepth;
    }

    /// <summary>
    /// A single global NPC market — "EVE's player-driven economy without players"
    /// (spec §6). Every commodity price follows a mean-reverting random walk with
    /// player sell pressure. Ticks every 6 hours and is simulated forward
    /// deterministically on app open using the same RNG stream persisted in the save.
    /// </summary>
    public sealed class MarketSimulator
    {
        public const double TickIntervalSeconds = 6.0 * 3600.0;
        public const double Reversion = 0.15;      // k
        public const double NoiseFraction = 0.08;  // noise sd as a fraction of base price
        public const double PressureRecovery = 0.55; // fraction of pressure remaining each tick

        private readonly Dictionary<ItemType, Commodity> _commodities = new Dictionary<ItemType, Commodity>();
        private readonly DeterministicRng _rng;

        public MarketSimulator(DeterministicRng rng) => _rng = rng;

        public IReadOnlyDictionary<ItemType, Commodity> Commodities => _commodities;

        public Commodity Register(ItemType item, double basePrice, double marketDepth)
        {
            var c = new Commodity
            {
                Item = item,
                BasePrice = basePrice,
                Price = basePrice,
                Pressure = 0,
                MarketDepth = marketDepth,
            };
            _commodities[item] = c;
            return c;
        }

        public double PriceOf(ItemType item) =>
            _commodities.TryGetValue(item, out var c) ? c.Price : 0;

        /// <summary>Advance every commodity by one 6-hour tick.</summary>
        public void Tick()
        {
            // Deterministic order by item index so the RNG stream is stable.
            foreach (var item in OrderedItems())
            {
                var c = _commodities[item];
                double noise = _rng.NextGaussian() * (NoiseFraction * c.BasePrice);
                c.Price += Reversion * (c.BasePrice - c.Price) + noise + c.Pressure;
                if (c.Price < 0.01 * c.BasePrice) c.Price = 0.01 * c.BasePrice; // never non-positive
                c.Pressure *= PressureRecovery; // pressure recovers toward zero
            }
        }

        /// <summary>Advance the market forward by an elapsed real interval (spec §6, on app open).</summary>
        public int AdvanceBy(double elapsedSeconds)
        {
            int ticks = (int)Math.Floor(elapsedSeconds / TickIntervalSeconds);
            for (int i = 0; i < ticks; i++) Tick();
            return ticks;
        }

        /// <summary>
        /// Sell <paramref name="volume"/> units of an item at the current price and
        /// register downward pressure so dumping a large lot nets less than spreading
        /// sales across sessions (spec §6). Returns Credits earned.
        /// </summary>
        public long Sell(ItemType item, double volume)
        {
            if (volume <= 0) return 0;
            if (!_commodities.TryGetValue(item, out var c)) return 0;

            // Price impact is felt progressively across the lot: revenue integrates
            // price as pressure builds, approximated at the midpoint of the added
            // pressure so a bigger lot yields a lower effective unit price.
            double addedPressure = volume / c.MarketDepth;
            double effectivePrice = c.Price - 0.5 * addedPressure;
            if (effectivePrice < 0.01 * c.BasePrice) effectivePrice = 0.01 * c.BasePrice;

            c.Pressure -= addedPressure;
            return (long)Math.Floor(effectivePrice * volume);
        }

        private IEnumerable<ItemType> OrderedItems()
        {
            for (int i = 0; i < ItemTypeExtensions.Count; i++)
            {
                var item = (ItemType)i;
                if (_commodities.ContainsKey(item)) yield return item;
            }
        }

        /// <summary>Registers default sellable commodities with sensible base prices/depths.</summary>
        public static MarketSimulator CreateDefault(DeterministicRng rng)
        {
            var m = new MarketSimulator(rng);
            // Raw and intermediate goods are the main sell targets; food can be sold too
            // (the central "sell or hold for Harvest?" tension, spec §6).
            m.Register(ItemType.Grain, 2.0, 400);
            m.Register(ItemType.Water, 1.0, 600);
            m.Register(ItemType.Stone, 3.0, 300);
            m.Register(ItemType.Timber, 2.5, 400);
            m.Register(ItemType.Flour, 5.0, 250);
            m.Register(ItemType.Brick, 8.0, 200);
            m.Register(ItemType.Plank, 7.0, 200);
            m.Register(ItemType.Egg, 4.0, 250);
            m.Register(ItemType.Bread, 10.0, 200);
            m.Register(ItemType.Milk, 9.0, 200);
            m.Register(ItemType.Preserves, 24.0, 120);
            m.Register(ItemType.Rations, 70.0, 60);
            m.Register(ItemType.Coal, 6.0, 300);
            return m;
        }
    }
}
