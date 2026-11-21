using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bobery.Rover
{
    /// <summary>
    /// The rover's hull. Every enemy hit takes <see cref="damagePerHit"/> of the maximum health and punches a
    /// leak into a random spot on the cabin walls. Leaks are repaired from the inside (<see cref="InteriorRepairs"/>):
    /// each repaired leak gives back the health it cost. Health 0 → game over.
    /// Leaks live here (not in the interior scene), so they survive entering/leaving the rover.
    /// </summary>
    [DisallowMultipleComponent]
    public class RoverHealth : MonoBehaviour
    {
        [Header("Hull")]
        [Min(1f)] public float maxHealth = 100f;
        [Tooltip("Share (0..1) of the maximum health one enemy shot takes.")]
        [Range(0.01f, 1f)] public float damagePerHit = 0.1f;
        [Range(0f, 1f)] public float lowFraction = 0.3f;
        [Tooltip("Share (0..1) of the maximum health each open leak drains per second.")]
        [Range(0f, 0.05f)] public float leakDamagePerSecond = 0.005f;
        [Tooltip("Cap (0..1 of max health per second) on the total leak drain, so piled-up leaks can't snowball.")]
        [Range(0f, 0.1f)] public float maxLeakDrainPerSecond = 0.0075f;

        /// <summary>A hole in the cabin wall. Position is abstract (0..1 values) so the interior can map it onto
        /// whatever walls the cabin has; <see cref="id"/> stays stable while the leak exists.</summary>
        [Serializable]
        public class Leak
        {
            public int id;
            /// <summary>Picks a wall (scaled onto the cabin's wall count).</summary>
            public float wall;
            /// <summary>Position across the wall and up the wall, 0..1.</summary>
            public float u, v;
            /// <summary>Health given back when repaired.</summary>
            public float damage;
        }

        public float Health { get; private set; }
        public float Fraction => maxHealth > 0f ? Health / maxHealth : 0f;
        public bool IsLow => Fraction <= lowFraction;
        public bool IsDestroyed { get; private set; }
        public IReadOnlyList<Leak> Leaks => _leaks;
        /// <summary>Seconds since the last hit (for HUD flashes).</summary>
        public float SinceHit => Time.time - _lastHit;
        /// <summary>Health lost per second right now through the open leaks (capped at <see cref="maxLeakDrainPerSecond"/>).</summary>
        public float LeakRate => maxHealth * Mathf.Min(_leaks.Count * leakDamagePerSecond, maxLeakDrainPerSecond);

        /// <summary>Any change: hit, repair.</summary>
        public event Action Changed;
        /// <summary>A shot landed (the new leak is passed along).</summary>
        public event Action<Leak> Damaged;
        public event Action<Leak> Repaired;

        readonly List<Leak> _leaks = new List<Leak>();
        int _nextId = 1;
        float _lastHit = -100f;

        void Awake() => Health = maxHealth;

        void Update()
        {
            // Open leaks bleed the hull until they're patched (inside or outside the rover).
            if (IsDestroyed || GameOverScreen.IsShown || _leaks.Count == 0) return;
            Health = Mathf.Max(0f, Health - LeakRate * Time.deltaTime);
            if (Health <= 0.0001f) Die("The leaks drained the hull before they could be patched.");
        }

        void Die(string reason)
        {
            Health = 0f;
            IsDestroyed = true;
            GameOverScreen.Show("Rover destroyed", reason);
        }

        /// <summary>One enemy shot: loses <see cref="damagePerHit"/> of max health and opens a leak.</summary>
        public void Hit() => TakeDamage(maxHealth * damagePerHit);

        public void TakeDamage(float amount)
        {
            if (IsDestroyed || amount <= 0f) return;
            float dealt = Mathf.Min(amount, Health);
            Health -= dealt;
            _lastHit = Time.time;

            var leak = new Leak
            {
                id = _nextId++,
                wall = UnityEngine.Random.value,
                u = UnityEngine.Random.Range(0.12f, 0.88f),
                v = UnityEngine.Random.Range(0.2f, 0.55f), // within the crew's reach
                damage = dealt,
            };
            _leaks.Add(leak);
            Damaged?.Invoke(leak);
            Changed?.Invoke();
            RoverHud.Notify(RoverInterior.IsInside ? "Hull breach! New leak in the cabin" : "Rover hit! Hull breached");

            if (Health <= 0.0001f) Die("Enemy fire tore the hull apart.");
        }

        /// <summary>Seals a leak and gives its health back. False if it's already gone.</summary>
        public bool Repair(int leakId)
        {
            if (IsDestroyed) return false;
            int i = _leaks.FindIndex(l => l.id == leakId);
            if (i < 0) return false;
            var leak = _leaks[i];
            _leaks.RemoveAt(i);
            Health = Mathf.Min(maxHealth, Health + leak.damage);
            Repaired?.Invoke(leak);
            Changed?.Invoke();
            return true;
        }

        public bool HasLeak(int leakId) => _leaks.Exists(l => l.id == leakId);
    }
}
