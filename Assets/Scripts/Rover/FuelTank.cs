using System;
using UnityEngine;

namespace Bobery.Rover
{
    /// <summary>
    /// The rover's fuel tank. Walking burns fuel in proportion to the distance covered.
    ///  - Tank empty, but the player carries fuel  → the player must go inside the rover and refuel.
    ///  - Tank empty and the player carries none   → game over.
    /// </summary>
    [DisallowMultipleComponent]
    public class FuelTank : MonoBehaviour
    {
        [Header("Tank")]
        [Min(1f)] public float capacity = 100f;
        [Tooltip("Fuel in the tank when the game starts (litres).")]
        [Min(0f)] public float startingFuel = 100f;
        [Tooltip("Litres burned per metre walked.")]
        [Min(0f)] public float litresPerMetre = 0.25f;

        [Header("Player")]
        [Tooltip("Fuel the player carries in their inventory at the start (litres).")]
        [Min(0f)] public float startingReserve = 40f;

        [Header("Warnings")]
        [Range(0f, 1f)] public float lowFuelFraction = 0.2f;

        public float Fuel { get; private set; }
        public float Fraction => capacity > 0f ? Fuel / capacity : 0f;
        public bool IsEmpty => Fuel <= 0.0001f;
        public bool IsLow => Fraction <= lowFuelFraction;
        /// <summary>How far the rover can walk on what's in the tank (metres).</summary>
        public float Range => litresPerMetre > 0f ? Fuel / litresPerMetre : float.PositiveInfinity;
        /// <summary>Fuel the player carries (inventory).</summary>
        public static float Reserve => PlayerInventory.Get(PlayerInventory.Fuel);
        public bool IsGameOver { get; private set; }

        /// <summary>Tank just ran dry.</summary>
        public event Action RanOut;
        /// <summary>Tank and inventory are both empty.</summary>
        public event Action GameOver;
        public event Action Changed;

        void Awake()
        {
            Fuel = Mathf.Clamp(startingFuel, 0f, capacity);
            if (startingReserve > 0f) PlayerInventory.Add(PlayerInventory.Fuel, startingReserve);
        }

        void OnEnable() => PlayerInventory.Changed += CheckGameOver;
        void OnDisable() => PlayerInventory.Changed -= CheckGameOver;

        /// <summary>Burns fuel for <paramref name="metres"/> walked. Returns the fraction of that distance the fuel covered (0..1).</summary>
        public float Burn(float metres)
        {
            if (metres <= 0f || litresPerMetre <= 0f) return 1f;
            if (IsEmpty) return 0f;
            float need = metres * litresPerMetre;
            float used = Mathf.Min(need, Fuel);
            Fuel -= used;
            Changed?.Invoke();
            if (IsEmpty)
            {
                Fuel = 0f;
                RanOut?.Invoke();
                CheckGameOver();
            }
            return used / need;
        }

        /// <summary>Takes up to <paramref name="litres"/> straight out of the tank (e.g. to build something). Returns litres used.</summary>
        public float Drain(float litres)
        {
            if (litres <= 0f || IsEmpty) return 0f;
            float used = Mathf.Min(litres, Fuel);
            Fuel -= used;
            Changed?.Invoke();
            if (IsEmpty)
            {
                Fuel = 0f;
                RanOut?.Invoke();
                CheckGameOver();
            }
            return used;
        }

        /// <summary>Pours up to <paramref name="litres"/> from the player's inventory into the tank. Returns litres added.</summary>
        public float RefuelFromInventory(float litres)
        {
            float room = capacity - Fuel;
            // Taking from the inventory fires its Changed event; don't judge "game over"
            // in the instant between the fuel leaving the inventory and entering the tank.
            _transferring = true;
            float taken;
            try { taken = PlayerInventory.Take(PlayerInventory.Fuel, Mathf.Min(litres, room)); }
            finally { _transferring = false; }
            if (taken > 0f)
            {
                Fuel += taken;
                Changed?.Invoke();
            }
            CheckGameOver();
            return taken;
        }

        bool _transferring;

        /// <summary>Adds fuel straight into the tank (e.g. a refuel station). Returns litres added.</summary>
        public float AddDirect(float litres)
        {
            float add = Mathf.Clamp(litres, 0f, capacity - Fuel);
            Fuel += add;
            if (add > 0f) Changed?.Invoke();
            return add;
        }

        void CheckGameOver()
        {
            if (_transferring || IsGameOver || !IsEmpty || Reserve > 0.0001f) return;
            IsGameOver = true;
            GameOver?.Invoke();
            GameOverScreen.Show("Out of fuel",
                "The rover's tank is empty and you have no fuel left to refill it.");
        }
    }
}
