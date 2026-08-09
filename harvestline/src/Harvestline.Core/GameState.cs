using Harvestline.Core.Content;
using Harvestline.Core.Economy;
using Harvestline.Core.Model;
using Harvestline.Core.Progression;
using Harvestline.Core.Rng;

namespace Harvestline.Core
{
    /// <summary>
    /// The aggregate root of a single save: everything the game persists and operates
    /// on. Kept UnityEngine-free so the whole game state can be simulated headless
    /// (offline accrual, the balance bot, and unit tests all drive this object).
    /// </summary>
    public sealed class GameState
    {
        public ContentDatabase Content { get; }
        public GridState Grid { get; }
        public ColonyState Colony { get; }
        public DeterministicRng Rng { get; }
        public MarketSimulator Market { get; }

        /// <summary>UTC unix seconds of first launch — the fixed clock the Harvest fires on (spec §5).</summary>
        public long FirstLaunchUtc { get; set; }

        /// <summary>UTC unix seconds the simulation was last advanced to. Anti-cheat anchor (spec §8).</summary>
        public long LastSimulatedUtc { get; set; }

        public GameState(ContentDatabase content, GridState grid, ColonyState colony,
            DeterministicRng rng, MarketSimulator market, long firstLaunchUtc, long lastSimulatedUtc)
        {
            Content = content;
            Grid = grid;
            Colony = colony;
            Rng = rng;
            Market = market;
            FirstLaunchUtc = firstLaunchUtc;
            LastSimulatedUtc = lastSimulatedUtc;
        }

        /// <summary>Start a fresh colony.</summary>
        public static GameState NewGame(ContentDatabase content, long nowUtc, ulong seed)
        {
            var rng = new DeterministicRng(seed);
            var grid = new GridState(GridState.MinEdge);
            var colony = new ColonyState();
            var market = MarketSimulator.CreateDefault(rng);
            return new GameState(content, grid, colony, rng, market, nowUtc, nowUtc);
        }

        /// <summary>How many Harvests have fired between two timestamps on the fixed clock.</summary>
        public int HarvestsDueBy(long nowUtc)
        {
            long sinceFirst = nowUtc - FirstLaunchUtc;
            long lastSince = LastSimulatedUtc - FirstLaunchUtc;
            if (sinceFirst < 0) return 0;
            long interval = (long)HarvestResolver.HarvestIntervalSeconds;
            long dueNow = sinceFirst / interval;
            long dueThen = lastSince < 0 ? 0 : lastSince / interval;
            long due = dueNow - dueThen;
            return due < 0 ? 0 : (int)due;
        }
    }
}
