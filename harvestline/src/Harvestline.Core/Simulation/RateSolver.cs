using System;

namespace Harvestline.Core.Simulation
{
    /// <summary>Why a machine is not running at full activity during an interval.</summary>
    public enum LimitReason : byte
    {
        None = 0,     // running at full rate
        Starved,      // an input item is at zero stock and under-produced
        Blocked,      // an output item is at full storage and over-produced
        Throttled,    // limited by the global power budget
        Idle,         // no recipe / nothing to do
    }

    /// <summary>
    /// Resolves the factory into a steady-state activity vector for the current
    /// stock levels (spec §7 steps 1–2):
    ///   1. Fixed-point relaxation of per-machine activity under material
    ///      starvation (empty input) and blockage (full output) constraints,
    ///      capped at <see cref="MaxPasses"/> passes so the Composter→Greenhouse→
    ///      Soil Plot feedback cycle terminates.
    ///   2. A single global power throttle applied on top.
    /// The result is valid until the next discontinuity event; the EventIntegrator
    /// re-solves after each one.
    ///
    /// Reused across events: all working buffers are preallocated and cleared per
    /// call, so a Solve does not allocate. Deterministic: every loop walks arrays in
    /// index order.
    /// </summary>
    public sealed class RateSolver
    {
        public const int MaxPasses = 20;
        private const double Eps = 1e-12;
        private const double ConvergeEps = 1e-10;
        private const double Big = double.MaxValue;

        private readonly SimGraph _graph;
        private readonly int _n;
        private readonly int _items;

        private readonly double[] _activity;
        private readonly double[] _activityNext;
        private readonly double[] _boost;
        private readonly LimitReason[] _reason;
        private readonly double[] _net;

        private readonly double[] _prodCur;
        private readonly double[] _consCur;
        private readonly double[] _prodFull;
        private readonly double[] _consFull;
        private readonly double[] _supplyRatio;
        private readonly double[] _acceptRatio;

        public RateSolver(SimGraph graph)
        {
            _graph = graph;
            _n = graph.Nodes.Length;
            _items = graph.ItemCount;

            _activity = new double[_n];
            _activityNext = new double[_n];
            _boost = new double[_n];
            _reason = new LimitReason[_n];
            _net = new double[_items];

            _prodCur = new double[_items];
            _consCur = new double[_items];
            _prodFull = new double[_items];
            _consFull = new double[_items];
            _supplyRatio = new double[_items];
            _acceptRatio = new double[_items];
        }

        /// <summary>Per-machine activity in [0,1] after the last <see cref="Solve"/>.</summary>
        public double[] Activity => _activity;
        /// <summary>Per-machine limiting reason after the last <see cref="Solve"/>.</summary>
        public LimitReason[] Reason => _reason;
        /// <summary>Net production rate (units/sec) per item after the last <see cref="Solve"/>.</summary>
        public double[] Net => _net;
        public PowerBudget Power { get; private set; }

        /// <summary>
        /// Solve for the given stock/capacity snapshot. Arrays are indexed by item.
        /// </summary>
        public void Solve(double[] stock, double[] capacity)
        {
            var nodes = _graph.Nodes;

            for (int i = 0; i < _n; i++) _activity[i] = 1.0;

            // ---- Phase 1: material fixed point ----
            for (int pass = 0; pass < MaxPasses; pass++)
            {
                RecomputeBoost();

                Array.Clear(_prodCur, 0, _items);
                Array.Clear(_consCur, 0, _items);
                Array.Clear(_prodFull, 0, _items);
                Array.Clear(_consFull, 0, _items);

                for (int i = 0; i < _n; i++)
                {
                    var node = nodes[i];
                    double m = _boost[i];
                    double a = _activity[i];
                    var outs = node.Outputs;
                    for (int k = 0; k < outs.Length; k++)
                    {
                        double full = outs[k].RateFull * m;
                        _prodFull[outs[k].Item] += full;
                        _prodCur[outs[k].Item] += full * a;
                    }
                    var ins = node.Inputs;
                    for (int k = 0; k < ins.Length; k++)
                    {
                        double full = ins[k].RateFull * m;
                        _consFull[ins[k].Item] += full;
                        _consCur[ins[k].Item] += full * a;
                    }
                }

                for (int it = 0; it < _items; it++)
                {
                    // Starvation: an empty input can only supply what is produced right now.
                    _supplyRatio[it] = stock[it] > Eps
                        ? Big
                        : (_consFull[it] <= Eps ? Big : _prodCur[it] / _consFull[it]);

                    // Blockage: a full output can only accept what is drained right now.
                    _acceptRatio[it] = stock[it] < capacity[it] - Eps
                        ? Big
                        : (_prodFull[it] <= Eps ? Big : _consCur[it] / _prodFull[it]);
                }

                double maxDelta = 0.0;
                for (int i = 0; i < _n; i++)
                {
                    var node = nodes[i];
                    double factor = 1.0;
                    var ins = node.Inputs;
                    for (int k = 0; k < ins.Length; k++)
                    {
                        double r = _supplyRatio[ins[k].Item];
                        if (r < factor) factor = r;
                    }
                    var outs = node.Outputs;
                    for (int k = 0; k < outs.Length; k++)
                    {
                        double r = _acceptRatio[outs[k].Item];
                        if (r < factor) factor = r;
                    }
                    if (factor < 0.0) factor = 0.0;
                    else if (factor > 1.0) factor = 1.0;

                    _activityNext[i] = factor;
                    double d = factor - _activity[i];
                    if (d < 0) d = -d;
                    if (d > maxDelta) maxDelta = d;
                }

                var swap = _activityNext;
                Array.Copy(swap, _activity, _n);

                if (maxDelta < ConvergeEps) break;
            }

            // Classify the material limiting reason (pre-power) for the report.
            ClassifyMaterialReasons(stock, capacity);

            // ---- Phase 2: global power throttle ----
            double capacityPow = 0.0, demandPow = 0.0;
            for (int i = 0; i < _n; i++)
            {
                var node = nodes[i];
                if (node.PowerOutputFull > 0) capacityPow += node.PowerOutputFull * _activity[i];
                if (node.PowerDraw > 0) demandPow += node.PowerDraw * _activity[i];
            }
            Power = new PowerBudget(capacityPow, demandPow);
            double g = Power.ThrottleFactor;
            if (g < 1.0 - 1e-9)
            {
                for (int i = 0; i < _n; i++)
                {
                    if (nodes[i].PowerDraw > 0)
                    {
                        // Power throttle dominates the report classification when it binds.
                        if (_reason[i] == LimitReason.None) _reason[i] = LimitReason.Throttled;
                        else _reason[i] = LimitReason.Throttled; // power is the tighter, whole-grid constraint
                        _activity[i] *= g;
                    }
                }
            }

            // ---- Net rates for the integrator (final activities + boost) ----
            RecomputeBoost();
            Array.Clear(_net, 0, _items);
            for (int i = 0; i < _n; i++)
            {
                var node = nodes[i];
                double m = _boost[i];
                double a = _activity[i];
                var outs = node.Outputs;
                for (int k = 0; k < outs.Length; k++) _net[outs[k].Item] += outs[k].RateFull * m * a;
                var ins = node.Inputs;
                for (int k = 0; k < ins.Length; k++) _net[ins[k].Item] -= ins[k].RateFull * m * a;
            }
        }

        private void RecomputeBoost()
        {
            var nodes = _graph.Nodes;
            for (int i = 0; i < _n; i++)
            {
                var node = nodes[i];
                if (!node.IsBoosted) { _boost[i] = 1.0; continue; }
                double b = 1.0;
                var src = node.BoostSources;
                for (int k = 0; k < src.Length; k++)
                    b += node.BoostPerSource * _activity[src[k]];
                _boost[i] = b;
            }
        }

        private void ClassifyMaterialReasons(double[] stock, double[] capacity)
        {
            var nodes = _graph.Nodes;
            for (int i = 0; i < _n; i++)
            {
                var node = nodes[i];
                if (node.Inputs.Length == 0 && node.Outputs.Length == 0)
                {
                    _reason[i] = LimitReason.Idle;
                    continue;
                }
                if (_activity[i] >= 1.0 - 1e-6)
                {
                    _reason[i] = LimitReason.None;
                    continue;
                }

                // Find the binding constraint: the input/output whose ratio is lowest.
                double best = Big;
                LimitReason reason = LimitReason.None;
                var ins = node.Inputs;
                for (int k = 0; k < ins.Length; k++)
                {
                    double r = _supplyRatio[ins[k].Item];
                    if (r < best) { best = r; reason = LimitReason.Starved; }
                }
                var outs = node.Outputs;
                for (int k = 0; k < outs.Length; k++)
                {
                    double r = _acceptRatio[outs[k].Item];
                    if (r < best) { best = r; reason = LimitReason.Blocked; }
                }
                _reason[i] = reason == LimitReason.None ? LimitReason.Starved : reason;
            }
        }
    }
}
