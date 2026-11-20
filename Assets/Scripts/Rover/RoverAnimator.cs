using UnityEngine;

namespace Bobery.Rover
{
    /// <summary>
    /// Drives the model's Animator from the rover's movement.
    /// The "Drive" state's playback speed follows the "Speed" float:
    /// 0 = paused, 1 = normal speed at max forward speed, negative = plays backwards when reversing.
    /// Turning in place also animates.
    /// </summary>
    [RequireComponent(typeof(RoverController))]
    [DisallowMultipleComponent]
    public class RoverAnimator : MonoBehaviour
    {
        public const string SpeedParam = "Speed";

        [Tooltip("Animator on the model. Found in children if empty.")]
        public Animator animator;
        [Tooltip("Animation playback speed at full forward speed.")]
        public float animationSpeedAtMax = 1f;
        [Tooltip("Animation playback speed when turning in place at full turn rate.")]
        public float animationSpeedWhenTurning = 0.6f;
        [Tooltip("How quickly playback speed follows movement.")]
        public float smoothing = 8f;

        static readonly int SpeedHash = Animator.StringToHash(SpeedParam);

        RoverController _rover;
        float _value;

        void Awake()
        {
            _rover = GetComponent<RoverController>();
            if (!animator) animator = GetComponentInChildren<Animator>();
        }

        void Update()
        {
            if (!animator) return;

            float drive = _rover.CurrentSpeed / Mathf.Max(0.01f, _rover.maxForwardSpeed);
            float turn = Mathf.Abs(_rover.CurrentTurn) / Mathf.Max(0.01f, _rover.turnSpeed);

            float target = drive * animationSpeedAtMax;
            // Turning in place still moves the legs/wheels.
            float turnAnim = turn * animationSpeedWhenTurning;
            if (Mathf.Abs(target) < turnAnim) target = turnAnim * (drive < 0f ? -1f : 1f);

            _value = Mathf.Lerp(_value, target, 1f - Mathf.Exp(-smoothing * Time.deltaTime));
            animator.SetFloat(SpeedHash, _value);
        }
    }
}
