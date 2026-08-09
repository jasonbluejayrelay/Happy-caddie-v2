using System;

namespace Harvestline.Core.Model
{
    /// <summary>
    /// The colony's material stores. Modeled as a single shared warehouse keyed by
    /// item type. This is the deliberate M1 simplification: the rate solver reasons
    /// about item *flows* (spec §7: "clamp by input availability propagated from
    /// extractors forward"), not tile-by-tile transport. Spatial routing
    /// (adjacency / conveyors) determines the production graph's edges and is layered
    /// on in M2/M3; it does not change the steady-state balance the solver computes.
    ///
    /// Stocks are doubles because the integrator advances them by (rate * dt) over
    /// continuous intervals.
    /// </summary>
    public sealed class Inventory
    {
        private readonly double[] _stock = new double[ItemTypeExtensions.Count];
        private readonly double[] _capacity = new double[ItemTypeExtensions.Count];

        /// <summary>
        /// Base per-item capacity before any silo bonuses. Sized so a starting colony
        /// (population 10, demand ≈141) clears its first Harvest on bare storage but is
        /// caught by its second (demand ≈158) unless the player expands — the intended
        /// day-5–7 teaching moment (spec §11). Balance data, not a spec constant.
        /// </summary>
        public const double BaseCapacity = 150.0;

        public Inventory()
        {
            for (int i = 0; i < _capacity.Length; i++) _capacity[i] = BaseCapacity;
        }

        public double Get(ItemType item) => _stock[item.Index()];
        public double Capacity(ItemType item) => _capacity[item.Index()];

        public void Set(ItemType item, double amount) =>
            _stock[item.Index()] = Clamp(amount, 0, _capacity[item.Index()]);

        public void SetCapacity(ItemType item, double capacity)
        {
            int i = item.Index();
            _capacity[i] = capacity < BaseCapacity ? BaseCapacity : capacity;
            if (_stock[i] > _capacity[i]) _stock[i] = _capacity[i];
        }

        public void Add(ItemType item, double amount) => Set(item, Get(item) + amount);

        /// <summary>Directly read the backing arrays (used by the integrator hot loop).</summary>
        public double[] StockArray => _stock;
        public double[] CapacityArray => _capacity;

        public Inventory Clone()
        {
            var copy = new Inventory();
            Array.Copy(_stock, copy._stock, _stock.Length);
            Array.Copy(_capacity, copy._capacity, _capacity.Length);
            return copy;
        }

        private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);
    }
}
