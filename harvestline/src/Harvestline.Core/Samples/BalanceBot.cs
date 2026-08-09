using System;
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
        public readonly double FoodPerHour;
        public readonly double NextDemand;
        public readonly int Structures;
        public readonly string Note;

        public BotDay(int day, int pop, long credits, long seals, double foodPerHour,
            double nextDemand, int structures, string note)
        {
            Day = day; Population = pop; Credits = credits; Seals = seals;
            FoodPerHour = foodPerHour; NextDemand = nextDemand; Structures = structures; Note = note;
        }
    }

    /// <summary>Summary of a full balance-bot run — the numbers §11 says to validate every change against.</summary>
    public sealed class BotReport
    {
        public List<BotDay> Days { get; } = new List<BotDay>();
        public int FirstFailureDay { get; set; } = -1;
        public int FirstResettleDay { get; set; } = -1;
        public int FinalPopulation { get; set; }
        public long FinalSeals { get; set; }
    }

    /// <summary>
    /// The headless "reasonable player" bot (spec §11). It plays greedily under one
    /// heuristic — each session, buy the affordable structure that most increases
    /// projected food/hour — and reports days-to-milestone. Kept alive from M1 so every
    /// balance change can be validated before it ships. Fully deterministic.
    /// </summary>
    public sealed class BalanceBot
    {
        private readonly ContentDatabase _db;
        private readonly long _startUtc;
        private double _mult = 1.0; // current prestige output multiplier

        // Producer structures the greedy heuristic ranks by food-gain-per-credit.
        private static readonly string[] Buildable =
        {
            "soil_plot", "water_pump", "woodlot",
            "mill", "bakery", "coop", "dairy",
        };

        public BalanceBot(ContentDatabase db, long startUtc = 0)
        {
            _db = db;
            _startUtc = startUtc;
        }

        public BotReport Run(int days, ulong seed = 12345)
        {
            var game = GameState.NewGame(_db, _startUtc, seed);
            // A fresh colony starts with a minimal working bread engine (the tutorial
            // grid) plus seed Credits — a lone machine produces no food and no power, so
            // the greedy heuristic needs a running chain to measure marginal gains against.
            SeedStartingEngine(game);
            game.Colony.Credits = 40;
            _mult = game.OutputMultiplier;
            var report = new BotReport();

            const double stepSeconds = 6.0 * 3600.0; // simulate in 6h steps; a "session" once/day
            long now = _startUtc;

            for (int day = 1; day <= days; day++)
            {
                string note = "";
                for (int step = 0; step < 4; step++) // 4 × 6h = 24h
                {
                    long next = now + (long)stepSeconds;

                    // Advance production for this step.
                    var sim = new FactorySimulator(game.Grid, _mult);
                    sim.Simulate(game.Grid.Inventory, stepSeconds);
                    now = next;
                    game.LastSimulatedUtc = now;

                    // Resolve any Harvest that fired.
                    int due = HarvestsDue(game, now);
                    for (int h = 0; h < due; h++)
                    {
                        var result = HarvestResolver.Resolve(game.Colony, game.Grid.Inventory);
                        if (!result.Met && !result.StoresTokenUsed && report.FirstFailureDay < 0)
                            report.FirstFailureDay = day;
                        note = result.Met ? $"harvest+ pop {result.NewPopulation}"
                                           : (result.StoresTokenUsed ? "harvest saved by Stores" : $"harvest FAIL pop {result.NewPopulation}");
                    }
                }

                // --- Session: sell surplus, ensure storage/power, then build greedily ---
                SellSurplus(game);
                EnsureFoodStorage(game);
                EnsurePower(game);
                BuildGreedily(game);

                // Prestige: when a Resettlement is worthwhile, spend banked Seals on the
                // permanent output multiplier, wipe, and restart on a larger grid.
                long lifetimeAtStart = game.Colony.LifetimeSeals - game.Colony.Seals;
                if (PrestigeCalculator.ShouldResettle(game.Colony.Seals, lifetimeAtStart))
                {
                    game.SpendSealsOnOutput(game.Colony.Seals);
                    game.Resettle();
                    SeedStartingEngine(game);
                    game.Colony.Credits = 40;
                    _mult = game.OutputMultiplier;
                    if (report.FirstResettleDay < 0) report.FirstResettleDay = day;
                    note = $"RESETTLE #{game.Colony.Resettlements} (×{_mult:0.00}, edge {game.Grid.Edge})";
                }

                var simFore = new FactorySimulator(game.Grid, _mult);
                double foodPerHour = simFore.ProjectedFoodPerHour(game.Grid.Inventory);
                double nextDemand = HarvestResolver.Demand(game.Colony.Population);
                report.Days.Add(new BotDay(day, game.Colony.Population, game.Colony.Credits,
                    game.Colony.Seals, foodPerHour, nextDemand, game.Grid.Structures.Count, note));
            }

            report.FinalPopulation = game.Colony.Population;
            report.FinalSeals = game.Colony.Seals;
            return report;
        }

        private static int HarvestsDue(GameState game, long now)
        {
            long interval = (long)HarvestResolver.HarvestIntervalSeconds;
            long dueNow = (now - game.FirstLaunchUtc) / interval;
            long dueBefore = (game.LastSimulatedUtc - (long)(6 * 3600) - game.FirstLaunchUtc) / interval;
            // Count boundaries crossed in the last step.
            long prev = (now - (long)(6 * 3600) - game.FirstLaunchUtc);
            long prevDue = prev < 0 ? 0 : prev / interval;
            long d = dueNow - prevDue;
            return d < 0 ? 0 : (int)d;
        }

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

        /// <summary>Buy Bread silos until food storage can hold the next Harvest's demand.</summary>
        private void EnsureFoodStorage(GameState game)
        {
            // Bread is value 1, so its storage capacity in units == its food value.
            while (game.Colony.Credits >= _db.BuildCost("silo"))
            {
                double demand = HarvestResolver.Demand(game.Colony.Population);
                double breadCap = game.Grid.Inventory.Capacity(ItemType.Bread);
                if (breadCap >= demand * 1.1) break; // small headroom
                if (!FindFreeCell(game.Grid, 1, out int x, out int y)) break;
                var silo = game.Grid.Place(_db.Get("silo"), x, y);
                silo.AssignedItem = ItemType.Bread;
                game.Grid.RecomputeStorageCapacity();
                game.Colony.Credits -= _db.BuildCost("silo");
            }
        }

        /// <summary>Add a biomass generator whenever the grid is power-throttled.</summary>
        private void EnsurePower(GameState game)
        {
            while (game.Colony.Credits >= _db.BuildCost("biomass_generator"))
            {
                var sim = new FactorySimulator(game.Grid, _mult);
                sim.Simulate(game.Grid.Inventory.Clone(), 60); // one solve to read the power budget
                var solver = new RateSolver(SimGraph.Build(game.Grid));
                var inv = game.Grid.Inventory;
                solver.Solve(inv.StockArray, inv.CapacityArray);
                if (!solver.Power.IsThrottled) break;
                if (!FindFreeCell(game.Grid, 1, out int x, out int y)) break;
                game.Grid.Place(_db.Get("biomass_generator"), x, y);
                game.Colony.Credits -= _db.BuildCost("biomass_generator");
            }
        }

        private void SellSurplus(GameState game)
        {
            // Sell everything that is not food-valued, to fund building.
            for (int i = 0; i < ItemTypeExtensions.Count; i++)
            {
                var item = (ItemType)i;
                if (ContentDatabase.FoodValueOf(item) > 0) continue;
                if (!game.Market.Commodities.ContainsKey(item)) continue;
                double qty = game.Grid.Inventory.Get(item);
                if (qty <= 0) continue;
                game.Colony.Credits += game.Market.Sell(item, qty);
                game.Grid.Inventory.Set(item, 0);
            }
        }

        /// <summary>Greedy build: repeatedly buy the affordable structure that most raises projected food/hour.</summary>
        private void BuildGreedily(GameState game)
        {
            bool bought = true;
            int safety = 200;
            while (bought && safety-- > 0)
            {
                bought = false;
                double baseFood = new FactorySimulator(game.Grid, _mult).ProjectedFoodPerHour(game.Grid.Inventory);

                string? best = null;
                double bestGain = 0;
                int bestX = -1, bestY = -1;

                foreach (var id in Buildable)
                {
                    var def = _db.Get(id);
                    int cost = _db.BuildCost(id);
                    if (game.Colony.Credits < cost) continue;
                    if (!FindFreeCell(game.Grid, def.Size, out int x, out int y)) continue;

                    // Trial placement on a scratch grid.
                    var trial = CloneGridWith(game, def, x, y);
                    double food = new FactorySimulator(trial, _mult).ProjectedFoodPerHour(trial.Inventory);
                    double gain = (food - baseFood) / cost; // gain per credit
                    if (gain > bestGain)
                    {
                        bestGain = gain; best = id; bestX = x; bestY = y;
                    }
                }

                if (best != null && bestGain > 0)
                {
                    var def = _db.Get(best);
                    game.Grid.Place(def, bestX, bestY);
                    game.Colony.Credits -= _db.BuildCost(best);
                    bought = true;
                }
            }
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
            // Copy current inventory so projection warms from the real buffer state.
            for (int i = 0; i < ItemTypeExtensions.Count; i++)
                g.Inventory.Set((ItemType)i, game.Grid.Inventory.Get((ItemType)i));
            return g;
        }
    }
}
