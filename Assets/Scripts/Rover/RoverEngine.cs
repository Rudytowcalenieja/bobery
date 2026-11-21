using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Bobery.Rover
{
    /// <summary>
    /// Non-weapon upgrades the rover has earned. Static, so it survives loading the interior scene on top of the map.
    /// </summary>
    public static class RoverUpgrades
    {
        /// <summary>Reward id used by <see cref="OutpostRewardBanner"/> for the boost engine.</summary>
        public const string EngineId = "engine";

        /// <summary>Boost engine installed (reward from the n-th outpost, see <see cref="WeaponDatabase.engineOutpost"/>).</summary>
        public static bool Engine { get; private set; }

        public static event Action Changed;

        public static void UnlockEngine()
        {
            if (Engine) return;
            Engine = true;
            Changed?.Invoke();
        }

        /// <summary>Back to no upgrades (used on restart).</summary>
        public static void ResetAll()
        {
            Engine = false;
            Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            Engine = false;
            Changed = null;
        }
    }

    /// <summary>
    /// Boost engine upgrade. Nothing to mount: once unlocked (<see cref="RoverUpgrades.Engine"/>) it is always on,
    /// and Shift gives a short speed burst while driving, followed by a cooldown.
    /// </summary>
    [RequireComponent(typeof(RoverController))]
    [DisallowMultipleComponent]
    public class RoverEngine : MonoBehaviour
    {
        [Tooltip("Speed multiplier while boosting.")]
        [Range(1f, 4f)] public float boostMultiplier = 1.8f;
        [Tooltip("How long one boost lasts (seconds).")]
        public float boostDuration = 2f;
        [Tooltip("Seconds after a boost ends until the next one is ready.")]
        public float cooldown = 10f;

        public bool Unlocked => RoverUpgrades.Engine;
        public bool IsBoosting => _boostLeft > 0f;
        public bool IsCoolingDown => !IsBoosting && _cooldownLeft > 0f;
        public bool IsReady => Unlocked && !IsBoosting && _cooldownLeft <= 0f;
        public float BoostLeft => _boostLeft;
        public float CooldownLeft => _cooldownLeft;

        /// <summary>Gauge value 0..1: remaining burn while boosting, recharge progress while cooling down, 1 when ready.</summary>
        public float Charge => IsBoosting ? _boostLeft / Mathf.Max(0.01f, boostDuration)
                             : _cooldownLeft > 0f ? 1f - _cooldownLeft / Mathf.Max(0.01f, cooldown) : 1f;

        /// <summary>Raised when a boost starts.</summary>
        public event Action Boosted;

        RoverController _rover;
        FuelTank _tank;
        float _boostLeft, _cooldownLeft;

        void Awake()
        {
            _rover = GetComponent<RoverController>();
            _tank = GetComponent<FuelTank>();
        }

        void OnEnable() => RoverUpgrades.Changed += OnUpgradesChanged;

        void OnDisable()
        {
            RoverUpgrades.Changed -= OnUpgradesChanged;
            if (_rover) _rover.SpeedBoost = 1f;
        }

        void OnUpgradesChanged()
        {
            if (!Unlocked) _boostLeft = _cooldownLeft = 0f;
        }

        void Update()
        {
            if (!Unlocked) { _rover.SpeedBoost = 1f; return; }

            float dt = Time.deltaTime;
            if (_boostLeft > 0f)
            {
                _boostLeft -= dt;
                if (_boostLeft <= 0f) { _boostLeft = 0f; _cooldownLeft = cooldown; }
            }
            else if (_cooldownLeft > 0f) _cooldownLeft = Mathf.Max(0f, _cooldownLeft - dt);

            var kb = Keyboard.current;
            if (kb != null && !TutorialTips.IsOpen && (kb.leftShiftKey.wasPressedThisFrame || kb.rightShiftKey.wasPressedThisFrame)) TryBoost();

            // Ease the speed in/out a little so the burst doesn't snap.
            float target = IsBoosting ? boostMultiplier : 1f;
            _rover.SpeedBoost = Mathf.MoveTowards(_rover.SpeedBoost, target, dt * (boostMultiplier - 1f) * 6f);
        }

        /// <summary>Starts a boost if the engine is ready and the rover is driving.</summary>
        public bool TryBoost()
        {
            if (!IsReady || RoverInterior.IsInside || GameOverScreen.IsShown) return false;
            if (!_rover.HasDestination || (_tank && _tank.IsEmpty)) return false;
            _boostLeft = boostDuration;
            Boosted?.Invoke();
            return true;
        }
    }
}
