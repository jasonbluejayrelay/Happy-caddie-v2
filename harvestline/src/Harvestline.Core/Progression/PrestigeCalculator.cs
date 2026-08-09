using System;
using Harvestline.Core.Model;

namespace Harvestline.Core.Progression
{
    /// <summary>
    /// Resettlement / prestige math (spec §2). On Resettlement the grid, Credits, and
    /// tech wipe; Seals persist and buy permanent global multipliers, and lifetime
    /// Seals raise the starting grid size and tech tier. All functions are pure.
    /// </summary>
    public static class PrestigeCalculator
    {
        /// <summary>
        /// Starting grid edge as a function of lifetime Seals: every doubling of Seals
        /// adds a ring (edge +2), clamped to the grid maximum. A fresh player starts at
        /// the minimum 8×8.
        /// </summary>
        public static int StartingEdge(long lifetimeSeals)
        {
            if (lifetimeSeals <= 0) return GridState.MinEdge;
            int rings = (int)Math.Floor(Math.Log(lifetimeSeals + 1, 2.0)); // 0 at 0, grows slowly
            int edge = GridState.MinEdge + 2 * rings;
            return Math.Min(edge, GridState.MaxEdge);
        }

        /// <summary>
        /// Starting tech tier (0..3) unlocked at settle time, one tier per threshold of
        /// lifetime Seals. Lets veterans skip re-grinding the early chain.
        /// </summary>
        public static int StartingTechTier(long lifetimeSeals)
        {
            if (lifetimeSeals >= 400) return 3;
            if (lifetimeSeals >= 100) return 2;
            if (lifetimeSeals >= 20) return 1;
            return 0;
        }

        /// <summary>
        /// Permanent global production multiplier bought with Seals spent on upgrades.
        /// Each Seal spent adds 2%, with diminishing returns baked into how expensive
        /// upgrades get (handled by the shop, not here). Pure mapping for the solver
        /// to apply uniformly.
        /// </summary>
        public static double GlobalOutputMultiplier(long sealsSpentOnOutput) =>
            1.0 + 0.02 * sealsSpentOnOutput;

        /// <summary>Minimum Seals a first run must bank before Resettlement is worthwhile.</summary>
        public const long FirstResettleSeals = 20;

        /// <summary>
        /// Whether a Resettlement is worthwhile: the current run must have banked a real
        /// haul of Seals — at least <see cref="FirstResettleSeals"/>, and no less than the
        /// player's prior lifetime total so each run out-earns all previous progress
        /// before wiping. This prevents the degenerate "reset every couple of harvests"
        /// loop and makes prestige a milestone, not a reflex. Used by the balance bot.
        /// </summary>
        public static bool ShouldResettle(long currentSeals, long lifetimeSealsAtRunStart) =>
            currentSeals >= FirstResettleSeals && currentSeals >= lifetimeSealsAtRunStart;
    }
}
