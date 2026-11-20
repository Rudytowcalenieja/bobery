using UnityEngine;
using UnityEngine.InputSystem;

namespace Bobery.Rover
{
    /// <summary>
    /// Tank-style rover driving.
    /// W = forward, S = reverse, A/D = turn (in place when no throttle).
    /// Combine e.g. W + A to drive forward while turning left.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public class RoverController : MonoBehaviour
    {
        [Header("Speed")]
        [Tooltip("Max forward speed in m/s.")]
        public float maxForwardSpeed = 8f;
        [Tooltip("Max reverse speed in m/s.")]
        public float maxReverseSpeed = 4f;
        [Tooltip("How fast the rover reaches target speed (m/s²).")]
        public float acceleration = 6f;
        [Tooltip("How fast the rover slows down when no key is held (m/s²).")]
        public float braking = 10f;

        [Header("Turning")]
        [Tooltip("Turn speed in degrees per second.")]
        public float turnSpeed = 60f;
        [Tooltip("How fast turning ramps up/down.")]
        public float turnAcceleration = 240f;
        [Tooltip("When reversing, A/D steer like a car backing up (rear swings toward the key).")]
        public bool carLikeReverseSteering = true;

        [Header("Ground")]
        public float groundCheckDistance = 1.2f;
        public LayerMask groundMask = ~0;
        [Tooltip("Lowers the centre of mass so the rover doesn't tip easily.")]
        public Vector3 centerOfMassOffset = new Vector3(0f, -0.6f, 0f);

        [Header("Visuals (optional)")]
        public Transform[] leftWheels;
        public Transform[] rightWheels;
        public float wheelRadius = 0.4f;

        public float CurrentSpeed => _currentSpeed;
        /// <summary>Current yaw rate in degrees/second (positive = turning right).</summary>
        public float CurrentTurn => _currentTurn;
        public bool IsGrounded => _grounded;

        Rigidbody _rb;
        float _currentSpeed;
        float _currentTurn;
        bool _grounded;
        float _throttle;
        float _steer;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _rb.centerOfMass += centerOfMassOffset;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            _rb.angularDamping = 2f;
        }

        void Update()
        {
            var kb = Keyboard.current;
            _throttle = 0f;
            _steer = 0f;
            if (kb == null) return;

            if (kb.wKey.isPressed) _throttle += 1f;
            if (kb.sKey.isPressed) _throttle -= 1f;
            if (kb.dKey.isPressed) _steer += 1f;
            if (kb.aKey.isPressed) _steer -= 1f;
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            _grounded = Physics.Raycast(transform.position + transform.up * 0.1f, -transform.up,
                groundCheckDistance, groundMask, QueryTriggerInteraction.Ignore);

            // --- Throttle ---
            float targetSpeed = _throttle > 0f ? _throttle * maxForwardSpeed
                              : _throttle < 0f ? _throttle * maxReverseSpeed
                              : 0f;
            float rate = Mathf.Approximately(_throttle, 0f) || Mathf.Sign(targetSpeed) != Mathf.Sign(_currentSpeed)
                ? braking : acceleration;
            _currentSpeed = Mathf.MoveTowards(_currentSpeed, targetSpeed, rate * dt);

            // --- Steering ---
            float steer = _steer;
            if (carLikeReverseSteering && _throttle < 0f) steer = -steer;
            _currentTurn = Mathf.MoveTowards(_currentTurn, steer * turnSpeed, turnAcceleration * dt);

            if (!_grounded) return; // no traction in the air

            // Drive along the hull's forward, flattened onto the ground plane, keep gravity.
            Vector3 vel = _rb.linearVelocity;
            Vector3 up = transform.up;
            Vector3 vertical = Vector3.Project(vel, up);
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, up).normalized;
            _rb.linearVelocity = forward * _currentSpeed + vertical;

            // Yaw around the hull's up axis.
            Vector3 av = _rb.angularVelocity;
            Vector3 yawAxisComponent = Vector3.Project(av, up);
            _rb.angularVelocity = av - yawAxisComponent + up * (_currentTurn * Mathf.Deg2Rad);
        }

        void LateUpdate()
        {
            if (wheelRadius <= 0f) return;
            // Tank-style: inner side slows, outer side speeds up when turning.
            float halfTrack = 1f;
            float turnRad = _currentTurn * Mathf.Deg2Rad;
            float leftSpeed = _currentSpeed + turnRad * halfTrack;
            float rightSpeed = _currentSpeed - turnRad * halfTrack;
            SpinWheels(leftWheels, leftSpeed);
            SpinWheels(rightWheels, rightSpeed);
        }

        void SpinWheels(Transform[] wheels, float linearSpeed)
        {
            if (wheels == null) return;
            float deg = linearSpeed / wheelRadius * Mathf.Rad2Deg * Time.deltaTime;
            foreach (var w in wheels)
                if (w) w.Rotate(Vector3.right, deg, Space.Self);
        }
    }
}
