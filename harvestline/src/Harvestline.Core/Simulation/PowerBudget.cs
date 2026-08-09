namespace Harvestline.Core.Simulation
{
    /// <summary>
    /// The global power budget for a single solve. Generators add capacity; powered
    /// machines add demand. If demand exceeds capacity, every powered machine
    /// throttles by the same factor — soft failure, no blackout, no cascade
    /// (spec §3). Correct behavior for a game the player is not watching.
    /// </summary>
    public readonly struct PowerBudget
    {
        /// <summary>Total power capacity from active generators.</summary>
        public double Capacity { get; }
        /// <summary>Total power demanded by active powered machines (pre-throttle).</summary>
        public double Demand { get; }

        public PowerBudget(double capacity, double demand)
        {
            Capacity = capacity;
            Demand = demand;
        }

        /// <summary>
        /// Uniform throttle factor in (0,1]. 1 means demand is met; below 1 every
        /// powered machine runs at this fraction.
        /// </summary>
        public double ThrottleFactor
        {
            get
            {
                const double eps = 1e-12;
                if (Demand <= eps) return 1.0;
                if (Demand <= Capacity) return 1.0;
                return Capacity / Demand;
            }
        }

        public bool IsThrottled => ThrottleFactor < 1.0 - 1e-9;
    }
}
