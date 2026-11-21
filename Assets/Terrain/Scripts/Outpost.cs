using System.Collections.Generic;
using UnityEngine;

namespace Bobery.Terrain
{
    /// <summary>Where an outpost sits on the terrain (terrain-local XZ).</summary>
    [System.Serializable]
    public struct OutpostSite
    {
        public Vector2 center;
        public float height;   // flat pad height (terrain-local Y)
        public float radius;   // flat pad radius
        public float blend;    // distance over which the pad eases back into the terrain
        public float yaw;      // degrees
        public int seed;
    }

    /// <summary>Axis-aligned rectangle in the outpost's local XZ, used for obstacle queries.</summary>
    [System.Serializable]
    public struct OutpostFootprint
    {
        public Vector2 center;
        public Vector2 halfSize;
    }

    /// <summary>
    /// One generated enemy outpost. Gameplay code can use <see cref="spawnPoints"/> to place enemies
    /// and <see cref="gate"/> as the entrance. Created by <see cref="OutpostGenerator"/>.
    /// </summary>
    public class Outpost : MonoBehaviour
    {
        public int index;
        [Tooltip("Radius of the flat pad the outpost stands on.")]
        public float radius;
        [Tooltip("Entrance of the outpost (outside the wall, facing out).")]
        public Transform gate;
        [Tooltip("Free spots inside the walls for enemies.")]
        public List<Transform> spawnPoints = new List<Transform>();

        [HideInInspector] public List<OutpostFootprint> footprints = new List<OutpostFootprint>();

        /// <summary>World centre of the outpost.</summary>
        public Vector3 Center => transform.position;

        /// <summary>True if a point in the outpost's local XZ is inside a solid piece.</summary>
        public bool IsObstacleLocal(float x, float z, float padding)
        {
            foreach (var f in footprints)
            {
                if (Mathf.Abs(x - f.center.x) <= f.halfSize.x + padding &&
                    Mathf.Abs(z - f.center.y) <= f.halfSize.y + padding)
                    return true;
            }
            return false;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.9f);
            foreach (var s in spawnPoints)
                if (s) Gizmos.DrawWireSphere(s.position + Vector3.up * 0.9f, 0.5f);
            if (gate)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(gate.position, gate.position + gate.forward * 4f);
            }
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.35f);
            Gizmos.matrix = transform.localToWorldMatrix;
            foreach (var f in footprints)
                Gizmos.DrawWireCube(new Vector3(f.center.x, 0.5f, f.center.y), new Vector3(f.halfSize.x * 2f, 1f, f.halfSize.y * 2f));
        }
    }
}
