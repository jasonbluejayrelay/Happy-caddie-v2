using UnityEngine;

namespace Harvestline.Unity.Input
{
    /// <summary>
    /// Fixed isometric-ish 3D camera (spec §3): drag to pan, pinch to zoom. The pitch is
    /// fixed; only position and orthographic size change, keeping the readable low-poly
    /// silhouette. Works with touch (1 finger pan, 2 finger pinch) and falls back to
    /// mouse in the editor.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class CameraController : MonoBehaviour
    {
        [SerializeField] private float _pitch = 50f;
        [SerializeField] private float _yaw = 45f;
        [SerializeField] private float _panSpeed = 0.02f;
        [SerializeField] private float _minZoom = 4f;
        [SerializeField] private float _maxZoom = 22f;
        [SerializeField] private float _zoomSpeed = 0.02f;

        private Camera _cam = null!;
        private Vector3 _target;      // world point the camera looks at
        private float _zoom = 10f;
        private Vector2 _lastPan;
        private float _lastPinchDist;

        public void Configure(float gridWorldSize)
        {
            _cam = GetComponent<Camera>();
            _cam.orthographic = true;
            _target = Vector3.zero;
            _zoom = Mathf.Clamp(gridWorldSize * 0.7f, _minZoom, _maxZoom);
            Apply();
        }

        private void Update()
        {
            if (_cam == null) return;
#if UNITY_EDITOR || UNITY_STANDALONE
            HandleMouse();
#endif
            HandleTouch();
            Apply();
        }

        private void HandleTouch()
        {
            int touches = UnityEngine.Input.touchCount;
            if (touches == 1)
            {
                var t = UnityEngine.Input.GetTouch(0);
                if (t.phase == TouchPhase.Moved) Pan(t.deltaPosition);
            }
            else if (touches == 2)
            {
                var a = UnityEngine.Input.GetTouch(0);
                var b = UnityEngine.Input.GetTouch(1);
                float dist = (a.position - b.position).magnitude;
                if (a.phase == TouchPhase.Began || b.phase == TouchPhase.Began) _lastPinchDist = dist;
                Zoom((_lastPinchDist - dist) * _zoomSpeed);
                _lastPinchDist = dist;
            }
        }

        private void HandleMouse()
        {
            if (UnityEngine.Input.GetMouseButtonDown(1)) _lastPan = UnityEngine.Input.mousePosition;
            if (UnityEngine.Input.GetMouseButton(1))
            {
                Vector2 now = UnityEngine.Input.mousePosition;
                Pan(now - _lastPan);
                _lastPan = now;
            }
            float scroll = UnityEngine.Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.01f) Zoom(-scroll);
        }

        private void Pan(Vector2 deltaPixels)
        {
            // Move the target on the ground plane, relative to camera yaw.
            float s = _panSpeed * _zoom;
            Vector3 right = Quaternion.Euler(0, _yaw, 0) * Vector3.right;
            Vector3 fwd = Quaternion.Euler(0, _yaw, 0) * Vector3.forward;
            _target -= (right * deltaPixels.x + fwd * deltaPixels.y) * s;
        }

        private void Zoom(float delta)
        {
            _zoom = Mathf.Clamp(_zoom + delta, _minZoom, _maxZoom);
        }

        private void Apply()
        {
            _cam.orthographicSize = _zoom;
            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0);
            transform.rotation = rot;
            transform.position = _target - rot * Vector3.forward * 40f;
        }
    }
}
