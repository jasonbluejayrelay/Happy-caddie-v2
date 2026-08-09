using System;
using Harvestline.Core.Model;

namespace Harvestline.Core.Simulation
{
    /// <summary>
    /// Piecewise-linear rate integrator (spec §7). This is the single simulation
    /// implementation used by BOTH callers:
    ///   * offline accrual — call <see cref="Simulate"/> with the elapsed horizon;
    ///     it resolves in a handful of discontinuity events, never millions of ticks;
    ///   * online play — call it every frame with a small dt; it does one event and
    ///     returns.
    /// Same code path, so offline catch-up is provably identical to online behavior.
    ///
    /// The loop:
    ///   1. Solve steady-state rates for the current stocks (RateSolver).
    ///   2. Find the earliest discontinuity: a storage fills, a storage empties, or a
    ///      generator's fuel runs out (fuel is just an item that empties).
    ///   3. Integrate all rates linearly to that event; apply the state change.
    ///   4. Recompute and repeat, up to <see cref="MaxEvents"/>.
    /// </summary>
    public sealed class FactorySimulator
    {
        public const int MaxEvents = 200;

        /// <summary>Offline accrual is capped at this many seconds of production (spec §7: 24h).</summary>
        public const double OfflineCapSeconds = 24.0 * 3600.0;

        private const double Eps = 1e-9;

        private readonly SimGraph _graph;
        private readonly RateSolver _solver;

        public FactorySimulator(GridState grid, double outputMultiplier = 1.0)
        {
            _graph = SimGraph.Build(grid, outputMultiplier);
            _solver = new RateSolver(_graph);
        }

        /// <summary>
        /// Advance <paramref name="inventory"/> forward by <paramref name="horizonSeconds"/>,
        /// mutating stocks in place. Returns a bottleneck report for the interval.
        /// Deterministic: identical inputs yield identical outputs.
        /// </summary>
        public BottleneckReport Simulate(Inventory inventory, double horizonSeconds)
        {
            if (horizonSeconds < 0) throw new ArgumentOutOfRangeException(nameof(horizonSeconds));

            var report = new BottleneckReport(_graph);
            double[] stock = inventory.StockArray;
            double[] cap = inventory.CapacityArray;
            var nodes = _graph.Nodes;

            double remaining = horizonSeconds;
            int events = 0;

            while (remaining > Eps)
            {
                _solver.Solve(stock, cap);
                double[] net = _solver.Net;

                // Time to the next discontinuity across all items.
                double dt = remaining;
                for (int it = 0; it < stock.Length; it++)
                {
                    double r = net[it];
                    if (r > Eps)
                    {
                        double room = cap[it] - stock[it];
                        if (room > Eps)
                        {
                            double t = room / r;
                            if (t < dt) dt = t;
                        }
                    }
                    else if (r < -Eps)
                    {
                        double s = stock[it];
                        if (s > Eps)
                        {
                            double t = s / (-r);
                            if (t < dt) dt = t;
                        }
                    }
                }

                if (events >= MaxEvents)
                {
                    // Cap hit: integrate the whole remainder at the final steady-state rate.
                    dt = remaining;
                    report.HitEventCap = true;
                }

                if (dt <= 0) dt = remaining; // no finite event: fast-forward to the end

                // Integrate stocks linearly across dt, clamping to [0, cap].
                for (int it = 0; it < stock.Length; it++)
                {
                    double v = stock[it] + net[it] * dt;
                    if (v < 0) v = 0;
                    else if (v > cap[it]) v = cap[it];
                    stock[it] = v;
                }

                // Accrue bottleneck time for this interval.
                var reasons = _solver.Reason;
                for (int i = 0; i < nodes.Length; i++)
                    report.Accumulate(i, reasons[i], dt);

                remaining -= dt;
                events++;

                if (report.HitEventCap) break;
            }

            report.ElapsedSeconds = horizonSeconds;
            report.EventsResolved = events;
            return report;
        }

        /// <summary>
        /// Convenience wrapper for offline accrual: clamps the elapsed real time to the
        /// 24-hour cap before simulating (spec §7).
        /// </summary>
        public BottleneckReport SimulateOffline(Inventory inventory, double elapsedRealSeconds)
        {
            double clamped = Math.Min(Math.Max(elapsedRealSeconds, 0), OfflineCapSeconds);
            return Simulate(inventory, clamped);
        }

        /// <summary>
        /// Steady-state food production in food-value units per hour, used by the
        /// forecast panel (spec §2 "Project") and the balance bot's greedy heuristic
        /// (spec §11). Warms intermediate buffers for one hour on a clone, then reads
        /// the resolved net production rate of every food item.
        /// </summary>
        public double ProjectedFoodPerHour(Inventory inventory)
        {
            double[] net = ResolveSteadyRates(inventory, ItemType.None);
            double perSecond = 0.0;
            for (int i = 0; i < ItemTypeExtensions.Count; i++)
            {
                double v = Content.ContentDatabase.FoodValueOf((ItemType)i);
                if (v > 0 && net[i] > 0) perSecond += v * net[i];
            }
            return perSecond * 3600.0;
        }

        /// <summary>Steady-state production of a single item in units/hour (its buffer treated as non-binding).</summary>
        public double ProjectedItemPerHour(Inventory inventory, ItemType item)
        {
            double[] net = ResolveSteadyRates(inventory, item);
            double r = net[item.Index()];
            return r > 0 ? r * 3600.0 : 0.0;
        }

        /// <summary>Grain production in units/hour — used to verify the greenhouse boost.</summary>
        public double ProjectedGrainPerHour(Inventory inventory) => ProjectedItemPerHour(inventory, ItemType.Grain);

        // Warm the factory with food storage — and any explicitly measured item — treated
        // as a bottomless sink (the Harvest / a sale, not the warehouse, drains output),
        // then return the resolved net rate vector. This reveals true production capacity
        // instead of the zero net rate a full buffer would report.
        private double[] ResolveSteadyRates(Inventory inventory, ItemType extraSink)
        {
            var clone = inventory.Clone();
            const double sink = 1e18;
            for (int i = 0; i < ItemTypeExtensions.Count; i++)
            {
                var item = (ItemType)i;
                if (Content.ContentDatabase.FoodValueOf(item) > 0 || item == extraSink)
                {
                    clone.SetCapacity(item, sink);
                    clone.Set(item, 0);
                }
            }
            Simulate(clone, 3600.0);
            _solver.Solve(clone.StockArray, clone.CapacityArray);
            return _solver.Net;
        }

        /// <summary>Total food value currently in an inventory (spec §4 food values).</summary>
        public static double FoodStock(Inventory inventory)
        {
            double total = 0;
            for (int i = 0; i < ItemTypeExtensions.Count; i++)
            {
                var item = (ItemType)i;
                double v = Content.ContentDatabase.FoodValueOf(item);
                if (v > 0) total += v * inventory.Get(item);
            }
            return total;
        }
    }
}
