using System.Collections.Generic;
using Harvestline.Core.Content;
using Harvestline.Core.Model;
using Harvestline.Core.Progression;
using Harvestline.Core.Simulation;

namespace Harvestline.Core.Samples
{
    /// <summary>One day's snapshot from a balance-bot run.</summary>
    public readonly struct BotDay
    {
        public readonly int Day;
        public readonly int Population;
        public readonly long Credits;
        public readonly long Seals;
        public readonly double FoodCapacity;
        public readonly double NextDemand;
        public readonly int Structures;
        public readonly string Note;

        public BotDay(int day, int pop, long credits, long seals, double foodCapacity,
            double nextDemand, int structures, string note)
        {
            Day = day; Population = pop; Credits = credits; Seals = seals;
            FoodCapacity = foodCapacity; NextDemand = nextDemand; Structures = structures; Note = note;
        }
    }

    /// <summary>Summary of a full balance-bot run — the numbers §11 says to validate every change against.</summary>
    public sealed class BotReport
    {
        public List<BotDay> Days { get; } = new List<BotDay>();
        /// <summary>First Harvest that came up short — even if a Stores token absorbed it (the "deadline bit" day).</summary>
        public int FirstShortfallDay { get; set; } = -1;
        /// <summary>First Harvest that actually cost population (no grace token left).</summary>
        public int FirstFailureDay { get; set; } = -1;
        public int FirstResettleDay { get; set; } = -1;
        public int FinalPopulation { get; set; }
        public long FinalSeals { get; set; }
        public int TotalResettlements { get; set; }
    }

    /// <summary>
    /// The headless "reasonable player" bot (spec §11), kept alive from M1 so every
    /// balance change is validated before it ships. It models an ordinary — not
    /// optimal — player who logs in once a day and:
    ///   1. sells surplus non-food goods for Credits;
    ///   2. buys just enough food storage to survive the NEXT Harvest with headroom,
    ///      climbing the food-value ladder (Bread → Preserves → Rations) because
    ///      higher-value food is far more storage- and space-efficient (spec §4);
    ///   3. keeps production able to fill that storage, and keeps the lights on;
    ///   4. banks the rest and Resettles once prestige is clearly worthwhile.
    /// It fails a Harvest when it simply cannot afford the storage the growing
    /// population demands — which is the intended pressure. Fully deterministic.
    /// </summary>
    public sealed class BalanceBot
    {
        private readonly ContentDatabase _db;
        private readonly long _startUtc;
        private double _mult = 1.0; // current prestige output multiplier

        /// <summary>Fractional food-storage headroom a cautious player keeps over demand.</summary>
        public double Headroom { get; set; } = 1.15;
        /// <summary>Credits a fresh colony starts each run with.</summary>
        public long StartingCredits { get; set; } = 60;
        /// <summary>
        /// Model a naive new player who builds production but neglects storage and tech —
        /// used to demonstrate that the first Harvest failure lands on the intended
        /// day 5–7 teaching window (spec §11). A diligent player (the default) instead
        /// validates that survival and Resettlement are achievable.
        /// </summary>
        public bool Naive { get; set; }

        public BalanceBot(ContentDatabase db, long startUtc = 0)
        {
            _db = db;
            _startUtc = startUtc;
        }

        public BotReport Run(int days, ulong seed = 12345)
        {
            var game = GameState.NewGame(_db, _startUtc, seed);
            SeedStartingEngine(game);
            game.Colony.Credits = StartingCredits;
            _mult = game.OutputMultiplier;
            var report = new BotReport();

            const double stepSeconds = 6.0 * 3600.0; // 6h steps; a session once per day
            long now = _startUtc;

            for (int day = 1; day <= days; day++)
            {
                string note = "";
                for (int step = 0; step < 4; step++) // 4 × 6h = 24h
                {
                    now += (long)stepSeconds;
                    new FactorySimulator(game.Grid, _mult).Simulate(game.Grid.Inventory, stepSeconds);
                    game.LastSimulatedUtc = now;

                    int due = HarvestsDue(game, now);
                    for (int h = 0; h < due; h++)
                    {
                        var result = HarvestResolver.Resolve(game.Colony, game.Grid.Inventory);
                        if (!result.Met && report.FirstShortfallDay < 0)
                            report.FirstShortfallDay = day;
                        if (!result.Met && !result.StoresTokenUsed && report.FirstFailureDay < 0)
                            report.FirstFailureDay = day;
                        note = result.Met ? $"harvest OK → pop {result.NewPopulation} (+{result.SealsAwarded} seal)"
                            : result.StoresTokenUsed ? "harvest short — Stores token spent"
                            : $"harvest FAIL → pop {result.NewPopulation}";
                    }
                }

                // --- Daily session ---
                SellSurplus(game);
                if (!Naive)
                    SecureNextHarvest(game);   // storage first — this is survival
                EnsureProduction(game);        // keep production able to fill the storage
                EnsurePower(game);
                if (!Naive)
                {
                    InvestSurplus(game);       // climb the food ladder with spare credits
                    note = MaybeResettle(game, report, day, note);
                }

                report.Days.Add(new BotDay(day, game.Colony.Population, game.Colony.Credits,
                    game.Colony.Seals, FoodCapacity(game), HarvestResolver.Demand(game.Colony.Population),
                    game.Grid.Structures.Count, note));
            }

            report.FinalPopulation = game.Colony.Population;
            report.FinalSeals = game.Colony.Seals;
            report.TotalResettlements = game.Colony.Resettlements;
            return report;
        }

        // ---------------------------------------------------------------- sessions

        private void SellSurplus(GameState game)
        {
            for (int i = 0; i < ItemTypeExtensions.Count; i++)
            {
                var item = (ItemType)i;
                if (ContentDatabase.FoodValueOf(item) > 0) continue;      // never sell food
                if (!game.Market.Commodities.ContainsKey(item)) continue;
                double qty = game.Grid.Inventory.Get(item);
                if (qty <= 0) continue;
                game.Colony.Credits += game.Market.Sell(item, qty);
                game.Grid.Inventory.Set(item, 0);
            }
        }

        /// <summary>Buy food storage (silos on the best produced food) until the next Harvest is covered.</summary>
        private void SecureNextHarvest(GameState game)
        {
            int siloCost = _db.BuildCost("silo");
            while (game.Colony.Credits >= siloCost)
            {
                double demand = HarvestResolver.Demand(game.Colony.Population);
                if (FoodCapacity(game) >= demand * Headroom) break;
                var food = BestProducedFood(game);
                if (food == ItemType.None) break;                 // nothing to store yet
                if (!FindFreeCell(game.Grid, 1, out int x, out int y)) break; // out of space → may fail
                var silo = game.Grid.Place(_db.Get("silo"), x, y);
                silo.AssignedItem = food;
                game.Grid.RecomputeStorageCapacity();
                game.Colony.Credits -= siloCost;
            }
        }

        /// <summary>Add producers until projected output can fill storage before the next Harvest.</summary>
        private void EnsureProduction(GameState game)
        {
            double demand = HarvestResolver.Demand(game.Colony.Population);
            double target = demand * Headroom;
            int safety = 60;
            while (safety-- > 0)
            {
                double foodPerCycle = new FactorySimulator(game.Grid, _mult)
                    .ProjectedFoodPerHour(game.Grid.Inventory) * 72.0; // food produced over one 72h Harvest
                if (foodPerCycle >= target) break;
                if (!BuyBestProducer(game)) break;
            }
        }

        /// <summary>Add a biomass generator whenever the grid is power-throttled and it's affordable.</summary>
        private void EnsurePower(GameState game)
        {
            int cost = _db.BuildCost("biomass_generator");
            while (game.Colony.Credits >= cost)
            {
                var solver = new RateSolver(SimGraph.Build(game.Grid, _mult));
                solver.Solve(game.Grid.Inventory.StockArray, game.Grid.Inventory.CapacityArray);
                if (!solver.Power.IsThrottled) break;
                if (!FindFreeCell(game.Grid, 1, out int x, out int y)) break;
                game.Grid.Place(_db.Get("biomass_generator"), x, y);
                game.Colony.Credits -= cost;
            }
        }

        /// <summary>
        /// With storage/production secure, invest spare Credits in climbing the food
        /// ladder — unlocking Preserves then Rations makes each future silo hold far
        /// more food per tile, which is how a player keeps ahead of the curve (§4).
        /// A cautious player keeps a Credit reserve rather than spending to zero.
        /// </summary>
        private void InvestSurplus(GameState game)
        {
            long reserve = _db.BuildCost("silo") * 2; // keep enough for storage next session
            if (game.Colony.Credits <= reserve) return;

            // Preserves need a Coop (eggs) + Cannery, fed by the existing bread line.
            if (!HasProducerOf(game.Grid, ItemType.Preserves))
            {
                TryBuildChain(game, reserve, new[] { "coop", "cannery" });
                return;
            }
            // Rations need a Dairy (milk) + Ration Line, fed by preserves.
            if (!HasProducerOf(game.Grid, ItemType.Rations))
            {
                TryBuildChain(game, reserve, new[] { "dairy", "ration_line" });
            }
        }

        private string MaybeResettle(GameState game, BotReport report, int day, string note)
        {
            long lifetimeAtStart = game.Colony.LifetimeSeals - game.Colony.Seals;
            if (!PrestigeCalculator.ShouldResettle(game.Colony.Seals, lifetimeAtStart)) return note;

            game.SpendSealsOnOutput(game.Colony.Seals);
            game.Resettle();
            SeedStartingEngine(game);
            game.Colony.Credits = StartingCredits;
            _mult = game.OutputMultiplier;
            if (report.FirstResettleDay < 0) report.FirstResettleDay = day;
            return $"RESETTLE #{game.Colony.Resettlements} (×{_mult:0.00} output, {game.Grid.Edge}² grid)";
        }

        // ---------------------------------------------------------------- helpers

        private void SeedStartingEngine(GameState game)
        {
            var g = game.Grid;
            g.Place(_db.Get("soil_plot"), 0, 0);
            g.Place(_db.Get("soil_plot"), 1, 0);
            g.Place(_db.Get("water_pump"), 2, 0);
            g.Place(_db.Get("mill"), 3, 0);
            g.Place(_db.Get("bakery"), 4, 0);
            g.Place(_db.Get("woodlot"), 0, 2);
            g.Place(_db.Get("biomass_generator"), 1, 2);
        }

        /// <summary>Sum of storable food value across food items the colony can actually produce.</summary>
        private double FoodCapacity(GameState game)
        {
            double total = 0;
            for (int i = 0; i < ItemTypeExtensions.Count; i++)
            {
                var item = (ItemType)i;
                double v = ContentDatabase.FoodValueOf(item);
                if (v > 0 && HasProducerOf(game.Grid, item))
                    total += v * game.Grid.Inventory.Capacity(item);
            }
            return total;
        }

        /// <summary>The highest-food-value item the colony currently produces (silo target).</summary>
        private ItemType BestProducedFood(GameState game)
        {
            var order = new[] { ItemType.Rations, ItemType.Preserves, ItemType.Milk, ItemType.Bread, ItemType.Egg };
            foreach (var item in order)
                if (HasProducerOf(game.Grid, item)) return item;
            return ItemType.None;
        }

        private static bool HasProducerOf(GridState grid, ItemType item)
        {
            foreach (var s in grid.Structures)
            {
                if (s.Def.Recipe == null) continue;
                foreach (var o in s.Def.Recipe.Outputs)
                    if (o.Item == item) return true;
            }
            return false;
        }

        private static readonly string[] Producers =
        {
            "soil_plot", "water_pump", "woodlot", "mill", "bakery", "coop", "dairy", "cannery", "ration_line",
        };

        /// <summary>Greedy: buy the affordable producer that most raises projected food/hour per credit.</summary>
        private bool BuyBestProducer(GameState game)
        {
            double baseFood = new FactorySimulator(game.Grid, _mult).ProjectedFoodPerHour(game.Grid.Inventory);
            string? best = null;
            double bestGain = 0;
            int bestX = -1, bestY = -1;

            foreach (var id in Producers)
            {
                var def = _db.Get(id);
                int cost = _db.BuildCost(id);
                if (game.Colony.Credits < cost) continue;
                if (!FindFreeCell(game.Grid, def.Size, out int x, out int y)) continue;

                var trial = CloneGridWith(game, def, x, y);
                double food = new FactorySimulator(trial, _mult).ProjectedFoodPerHour(trial.Inventory);
                double gain = (food - baseFood) / cost;
                if (gain > bestGain) { bestGain = gain; best = id; bestX = x; bestY = y; }
            }

            if (best == null || bestGain <= 0) return false;
            game.Grid.Place(_db.Get(best), bestX, bestY);
            game.Colony.Credits -= _db.BuildCost(best);
            return true;
        }

        /// <summary>Build a set of structures if all fit and stay above the Credit reserve.</summary>
        private void TryBuildChain(GameState game, long reserve, string[] ids)
        {
            long cost = 0;
            foreach (var id in ids) cost += _db.BuildCost(id);
            if (game.Colony.Credits - cost < reserve) return;

            // Verify space for every piece before committing (all-or-nothing).
            var placements = new List<(StructureDef def, int x, int y)>();
            var footprint = new GridState(game.Grid.Edge);
            foreach (var s in game.Grid.Structures)
                footprint.Restore(s.InstanceId, s.Def, s.X, s.Y, s.Facing, s.AssignedItem);
            foreach (var id in ids)
            {
                var def = _db.Get(id);
                if (!FindFreeCell(footprint, def.Size, out int x, out int y)) return;
                footprint.Place(def, x, y);
                placements.Add((def, x, y));
            }

            foreach (var p in placements) game.Grid.Place(p.def, p.x, p.y);
            game.Colony.Credits -= cost;
        }

        private static int HarvestsDue(GameState game, long now)
        {
            long interval = (long)HarvestResolver.HarvestIntervalSeconds;
            long dueNow = (now - game.FirstLaunchUtc) / interval;
            long prev = now - (long)(6 * 3600) - game.FirstLaunchUtc;
            long prevDue = prev < 0 ? 0 : prev / interval;
            long d = dueNow - prevDue;
            return d < 0 ? 0 : (int)d;
        }

        private static bool FindFreeCell(GridState grid, int size, out int x, out int y)
        {
            for (int yy = 0; yy + size <= grid.Edge; yy++)
                for (int xx = 0; xx + size <= grid.Edge; xx++)
                    if (grid.IsFootprintFree(xx, yy, size)) { x = xx; y = yy; return true; }
            x = y = -1;
            return false;
        }

        private GridState CloneGridWith(GameState game, StructureDef newDef, int nx, int ny)
        {
            var g = new GridState(game.Grid.Edge);
            foreach (var s in game.Grid.Structures)
                g.Restore(s.InstanceId, s.Def, s.X, s.Y, s.Facing, s.AssignedItem);
            g.Place(newDef, nx, ny);
            g.RecomputeStorageCapacity();
            for (int i = 0; i < ItemTypeExtensions.Count; i++)
                g.Inventory.Set((ItemType)i, game.Grid.Inventory.Get((ItemType)i));
            return g;
        }
    }
}
