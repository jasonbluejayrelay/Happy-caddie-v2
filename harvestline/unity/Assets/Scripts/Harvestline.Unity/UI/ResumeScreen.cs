using System.Collections.Generic;
using System.Text;
using Harvestline.Core.Progression;
using Harvestline.Core.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace Harvestline.Unity.UI
{
    /// <summary>
    /// Aggregates everything that happened while the app was closed, across all the
    /// offline integration segments, so the resume screen can explain it in one place.
    /// </summary>
    public sealed class ResumeSummary
    {
        private readonly Dictionary<int, double> _starved = new();
        private readonly Dictionary<int, double> _blocked = new();
        private readonly Dictionary<int, double> _throttled = new();
        private readonly Dictionary<int, string> _names = new();
        public readonly List<HarvestResult> Harvests = new();
        public double TotalSeconds { get; private set; }

        public void Merge(BottleneckReport report)
        {
            TotalSeconds += report.ElapsedSeconds;
            foreach (var e in report.Entries)
            {
                _names[e.InstanceId] = e.StructureId;
                Add(_starved, e.InstanceId, e.StarvedTime);
                Add(_blocked, e.InstanceId, e.BlockedTime);
                Add(_throttled, e.InstanceId, e.ThrottledTime);
            }
        }

        public void AddHarvest(HarvestResult r) => Harvests.Add(r);

        private static void Add(Dictionary<int, double> d, int k, double v)
        {
            d.TryGetValue(k, out double cur);
            d[k] = cur + v;
        }

        /// <summary>The machine that lost the most time to any non-running state, if any.</summary>
        public (string id, string reason, double frac)? WorstOffender()
        {
            string? worstId = null; double worst = 0; string reason = "";
            foreach (var kv in _names)
            {
                double s = Val(_starved, kv.Key), b = Val(_blocked, kv.Key), t = Val(_throttled, kv.Key);
                double lost = s + b + t;
                if (lost <= worst) continue;
                worst = lost; worstId = kv.Value;
                reason = s >= b && s >= t ? "starved" : b >= t ? "blocked" : "throttled";
            }
            if (worstId == null || TotalSeconds <= 0) return null;
            return (worstId, reason, worst / TotalSeconds);
        }

        private static double Val(Dictionary<int, double> d, int k) => d.TryGetValue(k, out var v) ? v : 0;
    }

    /// <summary>
    /// The "welcome back" screen shown on resume (spec §2, M3). Summarizes offline
    /// production, any Harvests that fired while away, and the single worst bottleneck —
    /// which drives the player's diagnosis step.
    /// </summary>
    public sealed class ResumeScreen : MonoBehaviour
    {
        [SerializeField] private GameObject _root = null!;
        [SerializeField] private Text _body = null!;

        private void Awake() { if (_root != null) _root.SetActive(false); }

        public void Show(ResumeSummary summary)
        {
            if (_root == null || _body == null) return;
            var sb = new StringBuilder();
            double hours = summary.TotalSeconds / 3600.0;
            sb.AppendLine($"<b>Welcome back</b> — {hours:0.#}h passed.");

            foreach (var h in summary.Harvests)
            {
                if (h.Met)
                    sb.AppendLine($"Harvest met: fed {h.Demand:0}, pop → {h.NewPopulation} (+{h.SealsAwarded} seals).");
                else if (h.StoresTokenUsed)
                    sb.AppendLine($"Harvest short by {h.Demand - h.FoodAvailable:0} — a Stores token covered it.");
                else
                    sb.AppendLine($"<color=#DB5C4C>Harvest FAILED</color> — pop fell to {h.NewPopulation}.");
            }

            var worst = summary.WorstOffender();
            if (worst.HasValue)
                sb.AppendLine($"Biggest bottleneck: <b>{worst.Value.id}</b> was {worst.Value.reason} {worst.Value.frac:0%} of the time.");
            else
                sb.AppendLine("The line ran clean — no significant bottlenecks.");

            _body.text = sb.ToString();
            _root.SetActive(true);
        }

        public void Dismiss() { if (_root != null) _root.SetActive(false); }
    }
}
