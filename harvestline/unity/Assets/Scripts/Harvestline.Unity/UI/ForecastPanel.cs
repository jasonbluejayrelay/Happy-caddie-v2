using System;
using Harvestline.Core;
using Harvestline.Core.Progression;
using Harvestline.Core.Simulation;
using UnityEngine;
using UnityEngine.UI;

namespace Harvestline.Unity.UI
{
    /// <summary>
    /// The forecast panel (spec §2 "Project", §5) — the game's single most important
    /// retention surface. It projects food output over the time until the next Harvest,
    /// compares it to demand, and shows green when the player is on track to survive.
    /// The player closes the app having made a bet, not emptied a queue.
    /// </summary>
    public sealed class ForecastPanel : MonoBehaviour
    {
        [SerializeField] private Text _demandText = null!;
        [SerializeField] private Text _projectedText = null!;
        [SerializeField] private Text _verdictText = null!;
        [SerializeField] private Image _statusBar = null!;
        [SerializeField] private Color _green = new(0.35f, 0.72f, 0.42f);
        [SerializeField] private Color _red = new(0.82f, 0.35f, 0.30f);
        [SerializeField] private float _refreshSeconds = 1f;

        private GameState _game = null!;
        private Func<FactorySimulator> _sim = null!;
        private float _timer;

        public void Bind(GameState game, Func<FactorySimulator> sim)
        {
            _game = game;
            _sim = sim;
            Refresh();
        }

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer < _refreshSeconds) return;
            _timer = 0;
            Refresh();
        }

        private void Refresh()
        {
            if (_game == null || _sim == null) return;

            double demand = HarvestResolver.Demand(_game.Colony.Population);
            double secondsToHarvest = SecondsToNextHarvest();
            double hours = secondsToHarvest / 3600.0;

            double onHand = FactorySimulator.FoodStock(_game.Grid.Inventory);
            double perHour = _sim().ProjectedFoodPerHour(_game.Grid.Inventory);
            double storageCap = FoodStorageCapacity();

            // Projected food at Harvest = what's on hand + accrual, capped by storage.
            double projected = Math.Min(storageCap, onHand + perHour * hours);
            bool onTrack = projected >= demand;

            if (_demandText != null) _demandText.text = $"Demand: {demand:0} in {hours:0.#}h";
            if (_projectedText != null) _projectedText.text = $"Projected food: {projected:0} (cap {storageCap:0})";
            if (_verdictText != null)
                _verdictText.text = onTrack
                    ? $"On track — surplus {projected - demand:0}"
                    : $"SHORT by {demand - projected:0} — build storage or output";
            if (_statusBar != null) _statusBar.color = onTrack ? _green : _red;
        }

        private double SecondsToNextHarvest()
        {
            long interval = (long)HarvestResolver.HarvestIntervalSeconds;
            long since = _game.LastSimulatedUtc - _game.FirstLaunchUtc;
            if (since < 0) return interval;
            long intoCycle = since % interval;
            return interval - intoCycle;
        }

        private double FoodStorageCapacity()
        {
            double total = 0;
            for (int i = 0; i < Harvestline.Core.Model.ItemTypeExtensions.Count; i++)
            {
                var item = (Harvestline.Core.Model.ItemType)i;
                double v = Harvestline.Core.Content.ContentDatabase.FoodValueOf(item);
                if (v > 0) total += v * _game.Grid.Inventory.Capacity(item);
            }
            return total;
        }
    }
}
