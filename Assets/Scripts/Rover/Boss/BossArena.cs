using UnityEngine;

namespace Bobery.Rover
{
    /// <summary>
    /// The ring the boss fight takes place in. While <see cref="Active"/>, the rover (see
    /// <see cref="RoverController"/>) and enemy walkers (<see cref="EnemyRover.Walkable"/>) are kept inside the
    /// ring and out of the boss's footprint. The visible wall is <see cref="BossArenaWall"/>.
    /// </summary>
    public static class BossArena
    {
        public static bool Active { get; private set; }
        public static Vector3 Center { get; private set; }
        public static float Radius { get; private set; }
        public static Vector3 BossPosition { get; private set; }
        public static float BossRadius { get; private set; }

        public static void Open(Vector3 center, float radius, Vector3 boss, float bossRadius)
        {
            Center = center;
            Radius = radius;
            BossPosition = boss;
            BossRadius = bossRadius;
            Active = true;
        }

        public static void Close() => Active = false;

        /// <summary>Inside the ring (at least <paramref name="margin"/> from the wall) and outside the boss's footprint.</summary>
        public static bool Contains(Vector3 p, float margin = 0f)
        {
            if (!Active) return true;
            if (Flat(p, Center) > Radius - margin) return false;
            return Flat(p, BossPosition) >= BossRadius + margin;
        }

        /// <summary>Pushes a point back inside the ring and out of the boss's footprint (height unchanged).</summary>
        public static Vector3 Clamp(Vector3 p, float margin)
        {
            if (!Active) return p;
            float y = p.y;
            Vector3 d = p - Center; d.y = 0f;
            float max = Mathf.Max(0f, Radius - margin);
            if (d.magnitude > max) p = Center + d.normalized * max;
            Vector3 b = p - BossPosition; b.y = 0f;
            float min = BossRadius + margin;
            if (b.magnitude < min)
            {
                Vector3 dir = b.sqrMagnitude > 0.0001f ? b.normalized : (Center - BossPosition).normalized;
                p = BossPosition + dir * min;
            }
            p.y = y;
            return p;
        }

        static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => Active = false;
    }
}
