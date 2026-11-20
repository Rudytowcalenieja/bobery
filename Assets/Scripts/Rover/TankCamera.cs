using UnityEngine;
using UnityEngine.InputSystem;

namespace Bobery.Rover
{
    /// <summary>
    /// World of Tanks–style third-person camera.
    /// Mouse orbits freely around the target (independent of the hull's rotation),
    /// scroll wheel zooms, camera pulls in when blocked by terrain/objects.
    /// Esc frees the cursor, left click locks it again.
    /// </summary>
    [DisallowMultipleComponent]
    public class TankCamera : MonoBehaviour
    {
        [Header("Target")]
        public Transform target;
        [Tooltip("Orbit pivot offset above the target's position.")]
        public Vector3 pivotOffset = new Vector3(0f, 2f, 0f);

        [Header("Orbit")]
        public float mouseSensitivity = 0.12f;
        public float minPitch = -10f;
        public float maxPitch = 60f;
        public bool invertY = false;

        [Header("Zoom")]
        public float distance = 10f;
        public float minDistance = 4f;
        public float maxDistance = 22f;
        public float zoomStep = 1.5f;
        public float zoomSmoothing = 10f;

        [Header("Smoothing")]
        [Tooltip("How quickly the pivot follows the target (higher = snappier).")]
        public float followSmoothing = 15f;

        [Header("Collision")]
        public float collisionRadius = 0.3f;
        public LayerMask collisionMask = ~0;

        [Header("Crosshair")]
        public bool showCrosshair = true;

        float _yaw;
        float _pitch = 15f;
        float _targetDistance;
        float _currentDistance;
        Vector3 _pivot;
        readonly RaycastHit[] _hits = new RaycastHit[16];

        void Start()
        {
            _targetDistance = _currentDistance = distance;
            if (target)
            {
                _yaw = target.eulerAngles.y;
                _pivot = target.position + pivotOffset;
            }
            LockCursor(true);
        }

        void Update()
        {
            var mouse = Mouse.current;
            var kb = Keyboard.current;

            if (kb != null && kb.escapeKey.wasPressedThisFrame) LockCursor(false);
            if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
                LockCursor(true);

            if (mouse == null) return;

            if (Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 delta = mouse.delta.ReadValue() * mouseSensitivity;
                _yaw += delta.x;
                _pitch += invertY ? delta.y : -delta.y;
                _pitch = Mathf.Clamp(_pitch, minPitch, maxPitch);
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f)
            {
                _targetDistance -= Mathf.Sign(scroll) * zoomStep;
                _targetDistance = Mathf.Clamp(_targetDistance, minDistance, maxDistance);
            }
        }

        void LateUpdate()
        {
            if (!target) return;

            Vector3 desiredPivot = target.position + pivotOffset;
            _pivot = Vector3.Lerp(_pivot, desiredPivot, 1f - Mathf.Exp(-followSmoothing * Time.deltaTime));

            _currentDistance = Mathf.Lerp(_currentDistance, _targetDistance, 1f - Mathf.Exp(-zoomSmoothing * Time.deltaTime));

            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 dir = rot * Vector3.back;

            float dist = _currentDistance;
            int count = Physics.SphereCastNonAlloc(_pivot, collisionRadius, dir, _hits, dist, collisionMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (_hits[i].transform.IsChildOf(target)) continue; // ignore the rover itself
                dist = Mathf.Min(dist, Mathf.Max(_hits[i].distance, 0.5f));
            }

            transform.SetPositionAndRotation(_pivot + dir * dist, rot);
        }

        static void LockCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        void OnGUI()
        {
            if (!showCrosshair) return;
            const float size = 6f;
            var r = new Rect(Screen.width * 0.5f - size * 0.5f, Screen.height * 0.5f - size * 0.5f, size, size);
            GUI.color = new Color(1f, 1f, 1f, 0.85f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
        }
    }
}
