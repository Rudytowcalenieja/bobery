using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bobery.Rover
{
    /// <summary>
    /// Where the crew member counts as "at" a cabin station (pilot seat, gunner seat, server rack, fuel tank):
    /// anywhere within <see cref="Reach"/> metres of the station's object, measured on the floor. When the crew is
    /// near several, only the closest one is active, so two popups never show at once.
    /// </summary>
    public static class CabinZones
    {
        /// <summary>How far from the edge of a station's object the crew can stand and still use it (m).</summary>
        public const float Reach = 1.0f;

        static readonly List<Zone> _zones = new List<Zone>();

        public class Zone
        {
            public Bounds footprint; // world bounds of the station's object
        }

        /// <summary>Registers a station's object (all renderers under it) and returns its zone.</summary>
        public static Zone Add(Transform target)
        {
            var z = new Zone { footprint = Footprint(target) };
            _zones.Add(z);
            return z;
        }

        public static void Remove(Zone z) => _zones.Remove(z);

        /// <summary>Floor distance from <paramref name="p"/> to the zone's object (0 if inside its footprint).</summary>
        public static float Distance(Zone z, Vector3 p)
        {
            var b = z.footprint;
            float dx = Mathf.Max(b.min.x - p.x, 0f, p.x - b.max.x);
            float dz = Mathf.Max(b.min.z - p.z, 0f, p.z - b.max.z);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>True if <paramref name="p"/> is within reach of this zone and no other zone is closer.</summary>
        public static bool IsActive(Zone z, Vector3 p)
        {
            if (z == null) return false;
            float mine = Distance(z, p);
            if (mine > Reach) return false;
            foreach (var other in _zones)
                if (other != z && Distance(other, p) < mine) return false;
            return true;
        }

        static Bounds Footprint(Transform target)
        {
            var rs = target ? target.GetComponentsInChildren<Renderer>(true) : Array.Empty<Renderer>();
            if (rs.Length == 0) return new Bounds(target ? target.position : Vector3.zero, Vector3.zero);
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => _zones.Clear();
    }
}
