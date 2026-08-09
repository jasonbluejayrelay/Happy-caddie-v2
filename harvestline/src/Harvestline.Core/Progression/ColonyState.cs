namespace Harvestline.Core.Progression
{
    /// <summary>
    /// The persistent colony meta-state that the Harvest and prestige systems read
    /// and mutate. The factory grid and inventory live in GridState; this holds
    /// everything judged across Harvests.
    /// </summary>
    public sealed class ColonyState
    {
        /// <summary>Current population. Harvest demand scales off this (spec §5).</summary>
        public int Population { get; set; } = 10;

        /// <summary>Spendable soft currency, earned by selling goods (spec §6).</summary>
        public long Credits { get; set; } = 0;

        /// <summary>Prestige currency; persists across Resettlement (spec §2).</summary>
        public long Seals { get; set; } = 0;

        /// <summary>Lifetime Seals ever earned; drives starting grid/tech scaling.</summary>
        public long LifetimeSeals { get; set; } = 0;

        /// <summary>Grace tokens ("Stores"), banked on success, spent to negate a failure. Max 2.</summary>
        public int StoresTokens { get; set; } = 0;

        public const int MaxStoresTokens = 2;

        /// <summary>Harvests successfully met this run (telemetry / milestone tracking).</summary>
        public int HarvestsSucceeded { get; set; } = 0;
        public int HarvestsFailed { get; set; } = 0;
    }
}
