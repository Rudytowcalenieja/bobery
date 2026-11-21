using System;
using UnityEngine;

namespace Bobery.Rover
{
    /// <summary>
    /// The rover's battery. Powers the navigation map and the weapons.
    ///  - Drains slowly while the map is live (outside the rover), and per shot via <see cref="TryUse"/>.
    ///  - Can't be refilled with resources: it only recharges inside a <see cref="Beacon"/>'s zone.
    ///  - Empty → the map loses its picture and firing is disabled (<see cref="CanFire"/>).
    /// </summary>
    [DisallowMultipleComponent]
    public class RoverBattery : MonoBehaviour
    {
        [Header("Battery")]
        [Min(1f)] public float capacity = 100f;
        [Tooltip("Charge when the game starts (%).")]
        [Min(0f)] public float startingCharge = 100f;
        [Tooltip("Seconds of map time on a full battery (180 = 3 min).")]
        [Min(0f)] public float mapSecondsPerCharge = 180f;
        /// <summary>Charge used per second while the map is running.</summary>
        public float mapDrainPerSecond => mapSecondsPerCharge > 0f ? capacity / mapSecondsPerCharge : 0f;
        [Tooltip("Default charge used by one shot (see TryFire).")]
        [Min(0f)] public float shotCost = 5f;

        [Header("Warnings")]
        [Range(0f, 1f)] public float lowFraction = 0.2f;

        public float Charge { get; private set; }
        public float Fraction => capacity > 0f ? Charge / capacity : 0f;
        public bool IsEmpty => Charge <= 0.0001f;
        public bool IsLow => Fraction <= lowFraction;
        /// <summary>Firing needs power, and the charging switch off.</summary>
        public bool CanFire => !IsEmpty && !ChargingEnabled;
        /// <summary>True while a beacon is charging the battery this frame.</summary>
        public bool IsCharging => Time.frameCount - _chargedFrame <= 1;
        /// <summary>Seconds of map time left on the current charge.</summary>
        public float MapTimeLeft => mapDrainPerSecond > 0f ? Charge / mapDrainPerSecond : float.PositiveInfinity;

        /// <summary>Battery just went flat.</summary>
        public event Action RanOut;
        /// <summary>Battery got power back after being flat.</summary>
        public event Action Restored;
        public event Action Changed;

        int _chargedFrame = -10;

        // ---------------------------------------------------------------- charging switch
        /// <summary>
        /// Charging mode, switched at the server rack in the cabin. On: beacons charge the battery, but the
        /// weapons are offline (power is routed to the battery). Off (default): weapons armed, beacons don't charge.
        /// </summary>
        public static bool ChargingEnabled { get; private set; }
        public static event Action ChargingToggled;

        public static void SetCharging(bool on)
        {
            if (ChargingEnabled == on) return;
            ChargingEnabled = on;
            ChargingToggled?.Invoke();
            RoverHud.Notify(on ? "Charging ON: weapons offline" : "Charging OFF: weapons armed");
        }

        public static void ToggleCharging() => SetCharging(!ChargingEnabled);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => ChargingEnabled = false;

        void Awake() => Charge = Mathf.Clamp(startingCharge, 0f, capacity);

        void Update()
        {
            // The map only runs while we're outside (the interior hides the navigation HUD).
            if (!RoverInterior.IsInside && !GameOverScreen.IsShown && mapDrainPerSecond > 0f)
                Drain(mapDrainPerSecond * Time.deltaTime);
        }

        /// <summary>Uses <paramref name="amount"/> charge if there's enough. Returns false (and uses nothing) otherwise.</summary>
        public bool TryUse(float amount)
        {
            if (amount <= 0f) return true;
            if (Charge + 0.0001f < amount) return false;
            Drain(amount);
            return true;
        }

        /// <summary>Spends one shot's worth of charge. Returns false when firing isn't possible.</summary>
        public bool TryFire() => CanFire && TryUse(shotCost);

        /// <summary>Drains up to <paramref name="amount"/> charge. Returns how much was used.</summary>
        public float Drain(float amount)
        {
            if (amount <= 0f || IsEmpty) return 0f;
            float used = Mathf.Min(amount, Charge);
            Charge -= used;
            Changed?.Invoke();
            if (IsEmpty)
            {
                Charge = 0f;
                RanOut?.Invoke();
            }
            return used;
        }

        /// <summary>Adds charge (called by beacons). Returns how much was added (nothing while charging is switched off).</summary>
        public float Recharge(float amount)
        {
            if (!ChargingEnabled) return 0f;
            _chargedFrame = Time.frameCount;
            bool wasEmpty = IsEmpty;
            float add = Mathf.Clamp(amount, 0f, capacity - Charge);
            if (add <= 0f) return 0f;
            Charge += add;
            Changed?.Invoke();
            if (wasEmpty && !IsEmpty) Restored?.Invoke();
            return add;
        }
    }
}
