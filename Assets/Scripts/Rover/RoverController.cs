using System.Collections.Generic;
using UnityEngine;

namespace Bobery.Rover
{
    /// <summary>
    /// Point-and-go rover driving with pathfinding.
    /// SetDestination plans a route around water / steep ground (RoverPathfinder), then the rover
    /// follows the waypoints at a constant speed: it drives at full speed while roughly facing the
    /// next waypoint and turns in place when it needs a sharper turn.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class RoverController : MonoBehaviour
    {
        [Header("Speed")]
        [Tooltip("Multiplies speed and turn rate. Quick way to make the rover faster/slower.")]
        [Range(0.1f, 5f)] public float speedMultiplier = 1.75f;
        [Tooltip("Cruise speed in m/s (before the multiplier). The rover always drives at this speed.")]
        public float maxForwardSpeed = 10f;
        [Tooltip("Kept for the animator (reverse is not used by auto-driving).")]
        public float maxReverseSpeed = 4f;
        [Tooltip("Seconds to get from standstill to cruise speed (and back). Small = constant speed.")]
        [Range(0.01f, 2f)] public float speedRampTime = 0.15f;

        [Header("Turning")]
        [Tooltip("Turn speed in degrees per second.")]
        public float turnSpeed = 70f;
        [Tooltip("How fast turning ramps up/down.")]
        public float turnAcceleration = 300f;
        [Tooltip("If the next waypoint is further off than this angle, stop and turn in place first.")]
        [Range(5f, 90f)] public float pivotAngle = 35f;
        [Tooltip("Heading error (degrees) at which steering is at full lock.")]
        public float fullSteerAngle = 20f;

        [Header("Auto-drive")]
        [Tooltip("Stop when this close to the destination (metres, flat distance).")]
        public float arriveDistance = 3f;
        [Tooltip("Move on to the next waypoint when this close to the current one.")]
        public float waypointRadius = 3f;

        [Header("Pathfinding")]
        [Tooltip("Grid cell size in metres. Smaller = more precise, slower.")]
        public float pathCellSize = 2f;
        [Tooltip("Steepest slope (degrees) the rover will drive on.")]
        public float maxSlope = 35f;
        [Tooltip("Keep this far from water. 0 = use the rover's half-width automatically.")]
        public float waterClearance = 0f;

        [Header("Ground following")]
        [Tooltip("The rover walks on the terrain surface: every physics step it is placed on the ground under its "
               + "feet and tilted to match the slope. Holes, craters and bumps can't trap it.")]
        public bool followTerrain = true;
        [Tooltip("How far the foot sample points sit from the centre, as a fraction of the body's size.")]
        [Range(0.1f, 0.6f)] public float footSpread = 0.4f;
        [Tooltip("How quickly height follows the ground (higher = snappier).")]
        public float heightSmoothing = 12f;
        [Tooltip("How quickly the body tilts to match the ground (higher = snappier).")]
        public float tiltSmoothing = 6f;
        [Tooltip("Maximum body tilt in degrees.")]
        [Range(0f, 45f)] public float maxTilt = 20f;
        public LayerMask groundMask = ~0;

        [HideInInspector] public float groundCheckDistance = 1.2f;  // used only when followTerrain is off
        [HideInInspector] public Vector3 centerOfMassOffset = new Vector3(0f, -0.6f, 0f);

        public float CurrentSpeed => _currentSpeed;
        public float TopSpeed => maxForwardSpeed * speedMultiplier;
        public float TopTurnRate => turnSpeed * Mathf.Sqrt(speedMultiplier);
        /// <summary>Current yaw rate in degrees/second (positive = turning right).</summary>
        public float CurrentTurn => _currentTurn;
        public bool IsGrounded => _grounded;
        public bool HasDestination => _hasDestination;
        /// <summary>Final destination (end of the path).</summary>
        public Vector3 Destination => _destination;
        /// <summary>Remaining waypoints, current one first, final destination last.</summary>
        public IReadOnlyList<Vector3> Path => _path;
        public int WaypointIndex => _wp;
        /// <summary>Remaining distance along the path.</summary>
        public float DistanceToDestination
        {
            get
            {
                if (!_hasDestination) return 0f;
                float d = 0f;
                Vector3 p = transform.position;
                for (int i = _wp; i < _path.Count; i++) { d += FlatDistance(p, _path[i]); p = _path[i]; }
                return d;
            }
        }

        /// <summary>Raised when the rover reaches its destination.</summary>
        public event System.Action Arrived;

        Rigidbody _rb;
        RoverAutoFit _fit;
        Bobery.Terrain.TerrainGenerator _terrain;
        float _yaw;
        bool _placed;
        readonly RaycastHit[] _groundHits = new RaycastHit[8];
        RoverPathfinder _pathfinder;
        readonly List<Vector3> _path = new List<Vector3>();
        int _wp;
        float _currentSpeed;
        float _currentTurn;
        bool _grounded;
        float _throttle;
        float _steer;
        bool _hasDestination;
        Vector3 _destination;

        /// <summary>Plans a route and starts driving. Returns false when there's no way to get there.</summary>
        public bool SetDestination(Vector3 worldPoint)
        {
            _pathfinder ??= new RoverPathfinder();
            _pathfinder.cellSize = pathCellSize;
            _pathfinder.maxSlope = maxSlope;
            _pathfinder.clearance = waterClearance > 0f ? waterClearance : HalfWidth();
            _pathfinder.groundMask = groundMask;
            _pathfinder.ignore = transform;

            var tmp = new List<Vector3>();
            if (!_pathfinder.FindPath(transform.position, worldPoint, tmp) || tmp.Count == 0) return false;

            _path.Clear();
            _path.AddRange(tmp);
            _wp = 0;
            _destination = _path[_path.Count - 1];
            _hasDestination = true;
            return true;
        }

        public void Stop()
        {
            _hasDestination = false;
            _path.Clear();
        }

        /// <summary>Distance from the centre to the body's corner (+ a small margin), so the whole
        /// footprint stays on land whichever way the rover is facing.</summary>
        float HalfWidth()
        {
            const float margin = 1.5f;
            var fit = GetComponent<RoverAutoFit>();
            if (fit && fit.IsFitted)
                return new Vector2(fit.WorldSize.x, fit.WorldSize.z).magnitude * 0.5f + margin;
            var box = GetComponent<BoxCollider>();
            if (box)
            {
                Vector3 s = transform.lossyScale;
                return new Vector2(box.size.x * s.x, box.size.z * s.z).magnitude * 0.5f + margin;
            }
            return 3f;
        }

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            if (followTerrain)
            {
                // Moved by code, not pushed by physics: the box can't snag on crater rims.
                _rb.isKinematic = true;
                _rb.useGravity = false;
                _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            }
            else
            {
                _rb.centerOfMass += centerOfMassOffset;
                _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
                _rb.angularDamping = 2f;
            }

            // Fit the collider to the real animated model at runtime.
            _fit = GetComponent<RoverAutoFit>();
            if (!_fit) _fit = gameObject.AddComponent<RoverAutoFit>();
            _terrain = FindAnyObjectByType<Bobery.Terrain.TerrainGenerator>();
        }

        void Update()
        {
            _throttle = 0f;
            _steer = 0f;
            if (!_hasDestination) return;

            // Advance through waypoints.
            while (_wp < _path.Count - 1 && FlatDistance(transform.position, _path[_wp]) <= waypointRadius) _wp++;

            if (_wp >= _path.Count - 1 && FlatDistance(transform.position, _destination) <= arriveDistance)
            {
                Stop();
                Arrived?.Invoke();
                return;
            }

            Vector3 to = _path[_wp] - transform.position;
            to.y = 0f;
            Vector3 fwd = transform.forward;
            fwd.y = 0f;
            float angle = Vector3.SignedAngle(fwd, to, Vector3.up); // + = target is to the right

            _steer = Mathf.Clamp(angle / Mathf.Max(1f, fullSteerAngle), -1f, 1f);
            _throttle = Mathf.Abs(angle) < pivotAngle ? 1f : 0f; // constant speed, or pivot in place
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            float targetSpeed = _throttle * TopSpeed;
            float rate = TopSpeed / Mathf.Max(0.01f, speedRampTime);
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, rate * dt);
            _currentTurn = Mathf.MoveTowards(_currentTurn, _steer * TopTurnRate, turnAcceleration * speedMultiplier * dt);

            if (followTerrain) FollowTerrain(dt);
            else PhysicsDrive();
        }

        /// <summary>Walk on the surface: move/turn on the flat, then sit on the ground under the feet, tilted to match it.</summary>
        void FollowTerrain(float dt)
        {
            // Wait until the runtime collider fit is done (it briefly rotates the rover while measuring).
            if (_fit && !_fit.IsFitted) return;
            bool first = !_placed;
            if (first)
            {
                _yaw = transform.eulerAngles.y;
                _placed = true;
            }

            _yaw += _currentTurn * dt;
            Quaternion yawRot = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 fwd = yawRot * Vector3.forward;
            Vector3 right = yawRot * Vector3.right;

            Vector3 pos = _rb.position + fwd * (_currentSpeed * dt);

            // Four foot points around the body + the centre.
            Vector3 size = _fit && _fit.IsFitted ? _fit.WorldSize : new Vector3(3f, 2f, 4f);
            float hx = Mathf.Max(0.1f, size.x * footSpread), hz = Mathf.Max(0.1f, size.z * footSpread);
            float fl = GroundY(pos + fwd * hz - right * hx);
            float fr = GroundY(pos + fwd * hz + right * hx);
            float bl = GroundY(pos - fwd * hz - right * hx);
            float br = GroundY(pos - fwd * hz + right * hx);
            float c = GroundY(pos);

            // Height: average of the feet, but never let the centre sink into a bump.
            float targetY = Mathf.Max((fl + fr + bl + br) * 0.25f, c);
            pos.y = first ? targetY : Mathf.Lerp(_rb.position.y, targetY, 1f - Mathf.Exp(-heightSmoothing * dt));

            // Tilt from the feet: pitch nose-down when the front is lower, roll toward the lower side.
            float pitch = Mathf.Atan2(((bl + br) - (fl + fr)) * 0.5f, hz * 2f) * Mathf.Rad2Deg;
            float roll = Mathf.Atan2(((fr + br) - (fl + bl)) * 0.5f, hx * 2f) * Mathf.Rad2Deg; // +Z roll lifts the right side
            pitch = Mathf.Clamp(pitch, -maxTilt, maxTilt);
            roll = Mathf.Clamp(roll, -maxTilt, maxTilt);
            Quaternion target = yawRot * Quaternion.Euler(pitch, 0f, roll);
            Quaternion rot = first ? target : Quaternion.Slerp(_rb.rotation, target, 1f - Mathf.Exp(-tiltSmoothing * dt));
            // Keep the heading exact; the slerp only smooths the tilt.
            Vector3 up = rot * Vector3.up;
            rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(fwd, up).normalized, up);

            _rb.MovePosition(pos);
            _rb.MoveRotation(rot);
            _grounded = true;
        }

        /// <summary>Ground height under a world XZ point (terrain function if available, otherwise a raycast that skips the rover).</summary>
        float GroundY(Vector3 p)
        {
            if (_terrain)
            {
                var t = _terrain.transform;
                Vector3 l = t.InverseTransformPoint(p);
                return t.TransformPoint(new Vector3(l.x, _terrain.SampleHeight(l.x, l.z), l.z)).y;
            }
            int n = Physics.RaycastNonAlloc(new Vector3(p.x, p.y + 500f, p.z), Vector3.down, _groundHits, 1000f,
                groundMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue, y = p.y;
            for (int i = 0; i < n; i++)
            {
                if (_groundHits[i].transform.IsChildOf(transform)) continue;
                if (_groundHits[i].distance < best) { best = _groundHits[i].distance; y = _groundHits[i].point.y; }
            }
            return y;
        }

        /// <summary>Old physics-based driving (followTerrain off).</summary>
        void PhysicsDrive()
        {
            _grounded = Physics.Raycast(transform.position + transform.up * 0.1f, -transform.up,
                groundCheckDistance, groundMask, QueryTriggerInteraction.Ignore);
            if (!_grounded || _rb.isKinematic) return;

            Vector3 vel = _rb.linearVelocity;
            Vector3 up = transform.up;
            Vector3 vertical = Vector3.Project(vel, up);
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, up).normalized;
            _rb.linearVelocity = forward * _currentSpeed + vertical;

            Vector3 av = _rb.angularVelocity;
            _rb.angularVelocity = av - Vector3.Project(av, up) + up * (_currentTurn * Mathf.Deg2Rad);
        }

        static float FlatDistance(Vector3 a, Vector3 b)
        {
            a.y = 0f; b.y = 0f;
            return Vector3.Distance(a, b);
        }
    }
}
