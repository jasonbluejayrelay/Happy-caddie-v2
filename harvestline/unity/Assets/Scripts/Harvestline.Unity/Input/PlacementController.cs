using Harvestline.Core;
using Harvestline.Core.Model;
using Harvestline.Unity.View;
using UnityEngine;

namespace Harvestline.Unity.Input
{
    /// <summary>
    /// Tap-to-place and conveyor rotation (spec §3, §5 build step). Holds the currently
    /// selected structure def to place; a tap on a valid footprint places it (spending
    /// Credits), a tap on an existing conveyor rotates it, a tap-and-hold opens context.
    /// Placement is validated against Core's <see cref="GridState"/> so the view can never
    /// desync from the simulation.
    /// </summary>
    public sealed class PlacementController : MonoBehaviour
    {
        // internal so SceneComposer can wire these from code (also serialized for the Inspector).
        [SerializeField] internal Camera _camera = null!;
        [SerializeField] internal GridRenderer _grid = null!;
        [SerializeField] private float _holdSeconds = 0.4f;

        private GameState _game = null!;
        private StructureDef? _selectedDef;
        private System.Func<StructureDef, int> _costOf = null!;

        private float _pressTime;
        private bool _pressing;
        private Vector2 _pressPos;

        public System.Action<PlacedStructure>? OnStructureSelected;
        public System.Action<StructureDef, int, int>? OnPlaced;

        public void Initialize(GameState game, System.Func<StructureDef, int> costOf)
        {
            _game = game;
            _costOf = costOf;
        }

        /// <summary>Enter "place this structure" mode (called by the build UI).</summary>
        public void SelectForPlacement(StructureDef def) => _selectedDef = def;
        public void CancelPlacement() => _selectedDef = null;

        private void Update()
        {
            if (_game == null) return;
            if (!TryGetPointer(out Vector2 screen, out bool began, out bool ended)) return;

            if (began) { _pressing = true; _pressTime = Time.time; _pressPos = screen; }
            if (!ended) return;
            _pressing = false;

            bool isHold = Time.time - _pressTime >= _holdSeconds;
            if (!RaycastTile(screen, out int tx, out int ty)) return;

            if (isHold) { HandleHold(tx, ty); return; }
            HandleTap(tx, ty);
        }

        private void HandleTap(int tx, int ty)
        {
            var existing = _game.Grid.StructureAt(tx, ty);

            // Placing a new structure.
            if (_selectedDef != null && existing == null)
            {
                var def = _selectedDef;
                if (!_game.Grid.IsFootprintFree(tx, ty, def.Size)) return;
                int cost = _costOf(def);
                if (_game.Colony.Credits < cost) return;
                _game.Grid.Place(def, tx, ty);
                _game.Colony.Credits -= cost;
                OnPlaced?.Invoke(def, tx, ty);
                return;
            }

            // Tapping a conveyor rotates it (conveyors are the only rotatable structure).
            if (existing != null && existing.Def.Id == "conveyor")
            {
                RotateConveyor(existing);
                return;
            }

            // Otherwise: select for inspection.
            if (existing != null) OnStructureSelected?.Invoke(existing);
        }

        private void HandleHold(int tx, int ty)
        {
            var existing = _game.Grid.StructureAt(tx, ty);
            if (existing != null) OnStructureSelected?.Invoke(existing); // context menu opens on the selection
        }

        private void RotateConveyor(PlacedStructure conveyor)
        {
            // Replace with the same def at a rotated facing (Facing is immutable on the record).
            var next = (Facing)(((int)conveyor.Facing + 1) & 3);
            _game.Grid.Remove(conveyor.InstanceId);
            _game.Grid.Restore(conveyor.InstanceId, conveyor.Def, conveyor.X, conveyor.Y, next, conveyor.AssignedItem);
        }

        private bool RaycastTile(Vector2 screen, out int tx, out int ty)
        {
            tx = ty = -1;
            var ray = _camera.ScreenPointToRay(screen);
            // Intersect the y=0 ground plane.
            if (Mathf.Abs(ray.direction.y) < 1e-5f) return false;
            float t = -ray.origin.y / ray.direction.y;
            if (t < 0) return false;
            Vector3 hit = ray.origin + ray.direction * t;
            return _grid.WorldToTile(hit, out tx, out ty);
        }

        private bool TryGetPointer(out Vector2 screen, out bool began, out bool ended)
        {
            screen = default; began = ended = false;
            if (UnityEngine.Input.touchCount == 1)
            {
                var tch = UnityEngine.Input.GetTouch(0);
                screen = tch.position;
                began = tch.phase == TouchPhase.Began;
                ended = tch.phase == TouchPhase.Ended;
                return true;
            }
#if UNITY_EDITOR || UNITY_STANDALONE
            if (UnityEngine.Input.GetMouseButtonDown(0)) { screen = UnityEngine.Input.mousePosition; began = true; return true; }
            if (UnityEngine.Input.GetMouseButtonUp(0)) { screen = UnityEngine.Input.mousePosition; ended = true; return true; }
#endif
            return false;
        }
    }
}
