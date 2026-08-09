using System.Collections.Generic;

namespace Harvestline.Core.Simulation
{
    /// <summary>
    /// Per-machine accounting of where elapsed time went: fraction of the interval
    /// each machine spent starved, blocked, throttled, or running. Drives the
    /// "Diagnose" step of the core loop (spec §2) and is explicitly not optional
    /// (spec §7). Deterministic and derived purely from the integration.
    /// </summary>
    public sealed class BottleneckReport
    {
        public sealed class Entry
        {
            public int InstanceId;
            public string StructureId = "";
            public double RunningTime;
            public double StarvedTime;
            public double BlockedTime;
            public double ThrottledTime;
            public double IdleTime;

            public double Total => RunningTime + StarvedTime + BlockedTime + ThrottledTime + IdleTime;

            public double StarvedFraction => Frac(StarvedTime);
            public double BlockedFraction => Frac(BlockedTime);
            public double ThrottledFraction => Frac(ThrottledTime);
            public double RunningFraction => Frac(RunningTime);

            private double Frac(double t) { double tot = Total; return tot <= 0 ? 0 : t / tot; }

            /// <summary>The condition that consumed the most time (excluding running).</summary>
            public LimitReason WorstReason
            {
                get
                {
                    double s = StarvedTime, b = BlockedTime, t = ThrottledTime;
                    if (s <= 0 && b <= 0 && t <= 0) return LimitReason.None;
                    if (s >= b && s >= t) return LimitReason.Starved;
                    if (b >= s && b >= t) return LimitReason.Blocked;
                    return LimitReason.Throttled;
                }
            }
        }

        private readonly Entry[] _entries;
        private readonly Dictionary<int, Entry> _byInstance;

        public double ElapsedSeconds { get; internal set; }
        public int EventsResolved { get; internal set; }
        /// <summary>True if the 200-event cap was hit and the remainder integrated at final rate (spec §7).</summary>
        public bool HitEventCap { get; internal set; }

        public IReadOnlyList<Entry> Entries => _entries;

        internal BottleneckReport(SimGraph graph)
        {
            var nodes = graph.Nodes;
            _entries = new Entry[nodes.Length];
            _byInstance = new Dictionary<int, Entry>(nodes.Length);
            for (int i = 0; i < nodes.Length; i++)
            {
                var e = new Entry { InstanceId = nodes[i].InstanceId, StructureId = nodes[i].Id };
                _entries[i] = e;
                _byInstance[e.InstanceId] = e;
            }
        }

        internal void Accumulate(int nodeIndex, LimitReason reason, double dt)
        {
            var e = _entries[nodeIndex];
            switch (reason)
            {
                case LimitReason.Starved: e.StarvedTime += dt; break;
                case LimitReason.Blocked: e.BlockedTime += dt; break;
                case LimitReason.Throttled: e.ThrottledTime += dt; break;
                case LimitReason.Idle: e.IdleTime += dt; break;
                default: e.RunningTime += dt; break;
            }
        }

        public Entry ForInstance(int instanceId) => _byInstance[instanceId];

        /// <summary>The single most starved/blocked/throttled machine, or null if the factory ran clean.</summary>
        public Entry? WorstOffender()
        {
            Entry? worst = null;
            double worstLost = 0;
            foreach (var e in _entries)
            {
                double lost = e.StarvedTime + e.BlockedTime + e.ThrottledTime;
                if (lost > worstLost) { worstLost = lost; worst = e; }
            }
            return worst;
        }
    }
}
