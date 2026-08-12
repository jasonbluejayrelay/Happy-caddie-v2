using System;
using Harvestline.Core;
using Harvestline.Core.Content;
using Harvestline.Core.Economy;
using Harvestline.Core.Progression;
using Harvestline.Core.Save;
using Harvestline.Core.Simulation;
using Harvestline.Unity.Input;
using Harvestline.Unity.UI;
using Harvestline.Unity.View;
using UnityEngine;

namespace Harvestline.Unity.Bootstrap
{
    /// <summary>
    /// The composition root (spec §8). On launch it loads the save, resolves offline
    /// accrual and any Harvests that fired while away, then wires the renderer, camera,
    /// input, and UI to the single <see cref="GameState"/>. During play it ticks the same
    /// simulation code path online with a small dt, and autosaves.
    ///
    /// Everything below the Core boundary is deterministic; this class only adds
    /// wall-clock time, rendering, and I/O.
    /// </summary>
    public sealed class GameBootstrap : MonoBehaviour
    {
        // Public so the scene can be wired either in the Unity Inspector or entirely from
        // code by SceneComposer (the project ships no hand-authored .unity asset).
        public GridRenderer _gridRenderer = null!;
        public CameraController _camera = null!;
        public PlacementController _placement = null!;
        public ForecastPanel? _forecast;
        public ResumeScreen? _resume;
        public float _tileSize = 1.0f;
        public float _autosaveSeconds = 15f;
        public double _onlineTickSeconds = 0.25;

        private ContentDatabase _content = null!;
        private GameState _game = null!;
        private FactorySimulator _sim = null!;
        private double _tickAccumulator;
        private float _autosaveTimer;
        private bool _booted;

        /// <summary>The live game state (null until <see cref="Boot"/> runs). Read-only for HUDs.</summary>
        public GameState? Game => _game;
        /// <summary>The current online simulator (rebuilt when the factory changes).</summary>
        public FactorySimulator? Sim => _sim;

        private static long NowUtc() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Start (not Awake) so all dependencies wired this frame — in the Inspector or by
        // SceneComposer — exist before boot. Idempotent so either path is safe.
        private void Start() => Boot();

        public void Boot()
        {
            if (_booted) return;
            _booted = true;

            _content = ContentDatabase.CreateDefault();
            long now = NowUtc();
            _game = SaveIO.LoadOrNew(_content, now, newGameSeed: (ulong)now);

            ResolveTimeAway(now);

            // Build the online simulator first so anything that reads it (the forecast
            // panel) has a valid instance from the start.
            _sim = new FactorySimulator(_game.Grid, _game.OutputMultiplier);

            _gridRenderer.Initialize(_game);
            _camera.Configure(_game.Grid.Edge * _tileSize);
            _placement.Initialize(_game, def => _content.BuildCost(def.Id));
            _placement.OnPlaced = (_, _, _) => OnFactoryChanged();
            _forecast?.Bind(_game, () => _sim);
        }

        /// <summary>
        /// Offline accrual + missed Harvests (spec §7 / §5). Rejects device-clock rollback
        /// (anti-cheat, §8), advances production over the capped elapsed time, resolves each
        /// Harvest that fired at the correct point on the fixed clock, and advances the
        /// market. Shows the resume screen with the bottleneck report.
        /// </summary>
        private void ResolveTimeAway(long now)
        {
            double elapsed = SaveSerializer.ElapsedSinceLastSim(_game, now, out bool rolledBack);
            if (rolledBack)
            {
                _game.LastSimulatedUtc = now; // distrust the clock; accrue nothing
                return;
            }
            if (elapsed <= 0) return;

            var sim = new FactorySimulator(_game.Grid, _game.OutputMultiplier);
            long interval = (long)HarvestResolver.HarvestIntervalSeconds;
            long cursor = _game.LastSimulatedUtc;
            var summary = new ResumeSummary();

            // Production is capped at 24h total (spec §7) — beyond that storage is full
            // anyway — but every Harvest that fired on the real clock is still resolved.
            double productionBudget = Math.Min(elapsed, FactorySimulator.OfflineCapSeconds);

            // Walk forward Harvest boundary by boundary so each Harvest sees the correct stock.
            while (cursor < now)
            {
                long nextBoundary = ((cursor - _game.FirstLaunchUtc) / interval + 1) * interval + _game.FirstLaunchUtc;
                long segEnd = Math.Min(nextBoundary, now);

                double dt = Math.Min(segEnd - cursor, productionBudget);
                if (dt > 0)
                {
                    summary.Merge(sim.Simulate(_game.Grid.Inventory, dt));
                    productionBudget -= dt;
                }

                if (segEnd == nextBoundary)
                    summary.AddHarvest(HarvestResolver.Resolve(_game.Colony, _game.Grid.Inventory));

                cursor = segEnd;
            }

            _game.Market.AdvanceBy(elapsed);
            _game.LastSimulatedUtc = now;
            _resume?.Show(summary);
        }

        private void Update()
        {
            // Online play uses the SAME simulation, called with a small dt (spec §7).
            _tickAccumulator += Time.deltaTime;
            if (_tickAccumulator >= _onlineTickSeconds)
            {
                var report = _sim.Simulate(_game.Grid.Inventory, _tickAccumulator);
                _gridRenderer.SetBottleneckReport(report);
                _game.LastSimulatedUtc = NowUtc();
                _tickAccumulator = 0;
            }

            _autosaveTimer += Time.deltaTime;
            if (_autosaveTimer >= _autosaveSeconds) { Autosave(); _autosaveTimer = 0; }
        }

        private void OnApplicationPause(bool paused) { if (paused) Autosave(); }
        private void OnApplicationQuit() => Autosave();

        private void Autosave()
        {
            if (_game == null) return;
            try { SaveIO.Save(_game); }
            catch (Exception e) { Debug.LogError($"[Harvestline] Autosave failed: {e.Message}"); }
        }

        // A newly placed structure changes the factory graph — rebuild the simulator.
        public void OnFactoryChanged() => _sim = new FactorySimulator(_game.Grid, _game.OutputMultiplier);
    }
}
