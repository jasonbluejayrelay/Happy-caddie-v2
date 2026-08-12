using Harvestline.Core.Progression;
using Harvestline.Core.Simulation;
using Harvestline.Unity.Input;
using Harvestline.Unity.View;
using UnityEngine;
using UnityEngine.UI;

namespace Harvestline.Unity.Bootstrap
{
    /// <summary>
    /// Builds the entire playable scene from code (spec §9 ethos: everything authored in
    /// C#, no imported assets). The project ships no hand-authored `.unity` file — the
    /// batchmode build creates an empty scene with a single object carrying this
    /// component, and it wires up the camera, light, renderer, input, and a HUD at
    /// runtime. This keeps the Unity project tiny and diffable.
    /// </summary>
    public sealed class SceneComposer : MonoBehaviour
    {
        [SerializeField] private float _tileSize = 1.0f;

        private void Awake()
        {
            // --- Palette + shader (procedural, one material for the scene) ---
            var palette = ScriptableObject.CreateInstance<Palette>();
            var shader = Shader.Find("Harvestline/Palette");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");

            // --- Lighting ---
            var lightGo = new GameObject("Sun");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.97f, 0.9f);
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // --- Camera + controller ---
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.19f, 0.24f);
            var camController = camGo.AddComponent<CameraController>();

            // --- Grid renderer ---
            var gridGo = new GameObject("GridRenderer");
            var grid = gridGo.AddComponent<GridRenderer>();
            grid._palette = palette;
            grid._shader = shader;
            grid._tileSize = _tileSize;

            // --- Placement input ---
            var placeGo = new GameObject("Placement");
            var placement = placeGo.AddComponent<PlacementController>();
            placement._camera = cam;
            placement._grid = grid;

            // --- Bootstrap (owns the GameState + offline accrual) ---
            var bootGo = new GameObject("GameBootstrap");
            var boot = bootGo.AddComponent<GameBootstrap>();
            boot._gridRenderer = grid;
            boot._camera = camController;
            boot._placement = placement;
            boot._tileSize = _tileSize;

            // --- HUD ---
            var hud = BuildHud();
            hud.Bind(boot);
        }

        private SceneHud BuildHud()
        {
            var canvasGo = new GameObject("HUD");
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGo.AddComponent<GraphicRaycaster>();

            var textGo = new GameObject("Status");
            textGo.transform.SetParent(canvasGo.transform, false);
            var text = textGo.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 28;
            text.color = Color.white;
            text.alignment = TextAnchor.UpperLeft;
            var rt = text.rectTransform;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(24, -24);
            rt.sizeDelta = new Vector2(-48, 200);

            var hud = canvasGo.AddComponent<SceneHud>();
            hud.Text = text;
            return hud;
        }
    }

    /// <summary>A minimal always-on status readout, polled from the running game.</summary>
    public sealed class SceneHud : MonoBehaviour
    {
        public Text Text = null!;
        private GameBootstrap _boot = null!;
        private float _timer;

        public void Bind(GameBootstrap boot) => _boot = boot;

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer < 0.5f) return;
            _timer = 0;

            var game = _boot != null ? _boot.Game : null;
            if (game == null || Text == null) return;

            double demand = HarvestResolver.Demand(game.Colony.Population);
            double food = FactorySimulator.FoodStock(game.Grid.Inventory);
            long interval = (long)HarvestResolver.HarvestIntervalSeconds;
            double toHarvest = (interval - (game.LastSimulatedUtc - game.FirstLaunchUtc) % interval) / 3600.0;

            Text.text =
                $"Pop {game.Colony.Population}   Credits {game.Colony.Credits}   Seals {game.Colony.Seals}\n" +
                $"Next Harvest in {toHarvest:0.#}h — need {demand:0} food, have {food:0} " +
                (food >= demand ? "OK" : "SHORT");
        }
    }
}
