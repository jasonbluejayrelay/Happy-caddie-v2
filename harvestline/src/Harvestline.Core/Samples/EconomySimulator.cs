using System;
using System.Collections.Generic;
using Harvestline.Core.Economy;
using Harvestline.Core.Model;
using Harvestline.Core.Rng;

namespace Harvestline.Core.Samples
{
    /// <summary>Per-commodity price statistics over an economy run, as ratios of base price.</summary>
    public sealed class CommodityStats
    {
        public ItemType Item;
        public double MinRatio = double.MaxValue;
        public double MaxRatio = double.MinValue;
        public double SumRatio;
        public int Samples;
        public double AvgRatio => Samples == 0 ? 1.0 : SumRatio / Samples;
    }

    /// <summary>Result of a headless economy run — the M5 gate's evidence (spec §10).</summary>
    public sealed class EconomyReport
    {
        public List<CommodityStats> Stats { get; } = new List<CommodityStats>();
        public int Ticks { get; set; }

        /// <summary>
        /// No runaway inflation and no price collapse: every commodity stays within the
        /// band the whole run, and its long-run average does not drift far from base.
        /// </summary>
        public bool IsStable(double hardLow = 0.15, double hardHigh = 4.0,
                             double avgLow = 0.5, double avgHigh = 1.6)
        {
            foreach (var s in Stats)
            {
                if (s.MinRatio < hardLow || s.MaxRatio > hardHigh) return false;
                if (s.AvgRatio < avgLow || s.AvgRatio > avgHigh) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Drives the market forward for a number of days with a realistic pattern of player
    /// selling, to prove the mean-reverting walk plus sell pressure neither inflates
    /// away nor collapses (spec §6, M5 gate §10). Deterministic given the seed.
    /// </summary>
    public static class EconomySimulator
    {
        /// <summary>
        /// Simulate <paramref name="days"/> of trading. Each in-game day the "player"
        /// liquidates a depth-proportional volume of each tradable good — realistic
        /// throughput, since the pressure model itself scales impact by market depth
        /// (playerPressure = -volume/depth). Then the market ticks four times (every 6h)
        /// and prices are sampled. A heavy-selling stress level is also exercised.
        /// </summary>
        /// <param name="sellFractionOfDepth">
        /// Daily sell volume as a fraction of each good's market depth. 0.05 models a
        /// steady liquidator; higher values stress the market harder.
        /// </param>
        public static EconomyReport Run(int days, ulong seed = 2024, double sellFractionOfDepth = 0.06)
        {
            var rng = new DeterministicRng(seed);
            var market = MarketSimulator.CreateDefault(rng);
            var report = new EconomyReport();

            var stats = new Dictionary<ItemType, CommodityStats>();
            foreach (var kv in market.Commodities)
                stats[kv.Key] = new CommodityStats { Item = kv.Key };

            int ticksPerDay = 4; // 24h / 6h
            for (int day = 0; day < days; day++)
            {
                // A session: liquidate a depth-proportional lot of every tradable good,
                // with day-to-day variation (0.5×–1.5×) so the walk is genuinely stressed.
                foreach (var kv in market.Commodities)
                {
                    double vary = 0.5 + rng.NextDouble();
                    double volume = kv.Value.MarketDepth * sellFractionOfDepth * vary;
                    market.Sell(kv.Key, volume);
                }

                for (int t = 0; t < ticksPerDay; t++)
                {
                    market.Tick();
                    report.Ticks++;
                    foreach (var kv in market.Commodities)
                    {
                        var c = kv.Value;
                        double ratio = c.Price / c.BasePrice;
                        var s = stats[kv.Key];
                        if (ratio < s.MinRatio) s.MinRatio = ratio;
                        if (ratio > s.MaxRatio) s.MaxRatio = ratio;
                        s.SumRatio += ratio;
                        s.Samples++;
                    }
                }
            }

            // Emit stats in deterministic item order.
            for (int i = 0; i < ItemTypeExtensions.Count; i++)
            {
                var item = (ItemType)i;
                if (stats.TryGetValue(item, out var s)) report.Stats.Add(s);
            }
            return report;
        }
    }
}
