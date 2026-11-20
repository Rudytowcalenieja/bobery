using UnityEngine;

namespace Bobery.Rover
{
    /// <summary>
    /// Fixed chase camera: sits behind the target, looking down at a set angle (45° by default),
    /// and turns with the target's heading. No mouse control.
    /// Pulls in when terrain/objects block the view.
    /// </summary>
    [DisallowMultipleComponent]
    public class TankCamera : MonoBehaviour
    {
        [Header("Target")]
        public Transform target;
        [Tooltip("Point the camera looks at, relative to the target (in target's local space, yaw only).")]
        public Vector3 pivotOffset = new Vector3(0f, 1f, 0f);

        [Header("Auto fit")]
        [Tooltip("On Start, set pivotOffset and distance from the target's visible size (handles any scale).")]
        public bool autoFit = true;
        [Tooltip("Camera distance = this × the target's largest dimension.")]
        public float distanceMultiplier = 2.2f;
        [Tooltip("Where the camera aims, as a fraction of the target's height (0 = feet, 1 = top).")]
        [Range(0f, 1.5f)] public float aimHeight = 0.6f;

        [Header("Placement")]
        [Tooltip("Distance from the pivot.")]
        public float distance = 10f;
        [Tooltip("Downward angle in degrees (0 = level, 90 = straight down).")]
        [Range(0f, 89f)] public float pitch = 45f;

        [Header("Smoothing")]
        [Tooltip("How quickly the camera turns to match the target's heading (higher = snappier).")]
        public float rotationSmoothing = 6f;
        [Tooltip("How quickly the camera follows the target's position (higher = snappier).")]
        public float followSmoothing = 15f;

        [Header("Collision")]
        public float collisionRadius = 0.3f;
        public LayerMask collisionMask = ~0;

        float _yaw;
        Vector3 _pivot;
        readonly RaycastHit[] _hits = new RaycastHit[16];

        void Start()
        {
            if (autoFit) FitToTarget();
            SnapToTarget();
        }

        /// <summary>Sets pivotOffset and distance from the target's rendered size.</summary>
        [ContextMenu("Fit To Target")]
        public void FitToTarget()
        {
            if (!target) return;
            var renderers = target.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return;

            // Measure in the target's yaw-aligned space so rotation doesn't inflate the size.
            Quaternion yaw = Quaternion.Euler(0f, target.eulerAngles.y, 0f);
            Quaternion inv = Quaternion.Inverse(yaw);
            bool init = false;
            var b = new Bounds();
            foreach (var r in renderers)
            {
                Bounds wb = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = wb.center + Vector3.Scale(wb.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = inv * (corner - target.position);
                    if (!init) { b = new Bounds(p, Vector3.zero); init = true; }
                    else b.Encapsulate(p);
                }
            }

            float size = Mathf.Max(b.size.x, b.size.y, b.size.z);
            pivotOffset = new Vector3(0f, b.min.y + b.size.y * aimHeight, 0f);
            distance = size * distanceMultiplier;
            collisionRadius = Mathf.Max(0.3f, size * 0.03f);
        }

        /// <summary>Jumps straight to the final position without smoothing.</summary>
        public void SnapToTarget()
        {
            if (!target) return;
            _yaw = target.eulerAngles.y;
            _pivot = PivotFor(target);
            Place(distance);
        }

        void LateUpdate()
        {
            if (!target) return;

            float dt = Time.deltaTime;
            _yaw = Mathf.LerpAngle(_yaw, target.eulerAngles.y, 1f - Mathf.Exp(-rotationSmoothing * dt));
            _pivot = Vector3.Lerp(_pivot, PivotFor(target), 1f - Mathf.Exp(-followSmoothing * dt));

            Vector3 dir = Quaternion.Euler(pitch, _yaw, 0f) * Vector3.back;
            float dist = distance;
            int count = Physics.SphereCastNonAlloc(_pivot, collisionRadius, dir, _hits, distance, collisionMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (_hits[i].transform.IsChildOf(target)) continue; // ignore the rover itself
                dist = Mathf.Min(dist, Mathf.Max(_hits[i].distance, 0.5f));
            }

            Place(dist);
        }

        Vector3 PivotFor(Transform t) => t.position + Quaternion.Euler(0f, t.eulerAngles.y, 0f) * pivotOffset;

        void Place(float dist)
        {
            Quaternion rot = Quaternion.Euler(pitch, _yaw, 0f);
            transform.SetPositionAndRotation(_pivot + rot * Vector3.back * dist, rot);
        }
    }
}
