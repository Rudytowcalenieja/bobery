using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bobery.Rover
{
    /// <summary>
    /// Hit points of something the rover can shoot (enemy walkers, outpost turrets). Enemies are built
    /// without colliders, so shots test against a sphere (<see cref="Center"/>, <see cref="radius"/>) instead.
    /// At 0 HP it explodes and the GameObject is destroyed.
    /// </summary>
    [DisallowMultipleComponent]
    public class CombatHealth : MonoBehaviour
    {
        public static readonly List<CombatHealth> All = new List<CombatHealth>();
        /// <summary>Raised for every target that dies.</summary>
        public static event Action<CombatHealth> AnyDied;

        public string displayName = "Enemy";
        public float maxHealth = 50f;
        [Tooltip("Centre of the hit sphere, relative to the object (rotates with it, not scaled).")]
        public Vector3 centerOffset = Vector3.up;
        [Tooltip("Hit sphere radius in metres.")]
        public float radius = 1.5f;
        [Tooltip("Health bar height above the object's position (metres).")]
        public float barHeight = 3f;

        public float Health { get; private set; }
        public float Fraction => maxHealth > 0f ? Mathf.Clamp01(Health / maxHealth) : 0f;
        public bool IsDead { get; private set; }
        public float LastHitTime { get; private set; } = -100f;
        public Vector3 Center => transform.position + transform.rotation * centerOffset;
        public Vector3 BarPosition => transform.position + Vector3.up * barHeight;

        public event Action<CombatHealth> Died;

        public void Setup(string name, float hp, Vector3 center, float hitRadius, float bar)
        {
            displayName = name;
            maxHealth = hp;
            Health = hp;
            centerOffset = center;
            radius = hitRadius;
            barHeight = bar;
        }

        void Awake() { if (Health <= 0f) Health = maxHealth; }
        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public void TakeDamage(float amount, Vector3 at)
        {
            if (IsDead || amount <= 0f) return;
            Health = Mathf.Max(0f, Health - amount);
            LastHitTime = Time.time;
            if (Health <= 0f) Die();
        }

        void Die()
        {
            IsDead = true;
            WeaponFx.Explosion(Center, radius);
            RoverHud.Notify($"{displayName} destroyed");
            Died?.Invoke(this);
            AnyDied?.Invoke(this);
            Destroy(gameObject);
        }

        /// <summary>Ray against the hit sphere. Distance along the ray to the surface.</summary>
        public bool Raycast(Ray ray, float maxDistance, out float distance)
        {
            distance = 0f;
            Vector3 oc = ray.origin - Center;
            float b = Vector3.Dot(oc, ray.direction);
            float c = oc.sqrMagnitude - radius * radius;
            float disc = b * b - c;
            if (disc < 0f) return false;
            float s = Mathf.Sqrt(disc);
            float t = -b - s;
            if (t < 0f) t = -b + s;      // starting inside the sphere
            if (t < 0f || t > maxDistance) return false;
            distance = t;
            return true;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.6f);
            Gizmos.DrawWireSphere(Center, radius);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() { All.Clear(); AnyDied = null; }
    }
}
