using System.Collections.Generic;
using UnityEngine;

namespace Bobery.Terrain
{
    /// <summary>Small procedural meshes used by the outpost generator (no UVs; shaders use positions).</summary>
    public static class ProcMeshes
    {
        /// <summary>
        /// Half-cylinder shell along Z (Quonset hut / truck canopy), base at y = 0, centred on Z.
        /// Optional half-disc end caps.
        /// </summary>
        public static Mesh Arch(float radius, float length, int segments = 20, bool caps = true)
        {
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var t = new List<int>();
            float hz = length * 0.5f;

            // Shell: two vertices per arc step (front/back).
            for (int i = 0; i <= segments; i++)
            {
                float a = Mathf.PI * i / segments;
                var dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f);
                v.Add(new Vector3(dir.x * radius, dir.y * radius, -hz)); n.Add(dir);
                v.Add(new Vector3(dir.x * radius, dir.y * radius, hz)); n.Add(dir);
            }
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                // Wound so the outside faces outward (Unity: clockwise = front).
                t.Add(a); t.Add(c); t.Add(b);
                t.Add(c); t.Add(d); t.Add(b);
            }

            if (caps)
            {
                AddCap(v, n, t, radius, -hz, Vector3.back, segments);
                AddCap(v, n, t, radius, hz, Vector3.forward, segments);
            }

            var m = new Mesh { name = "Arch", hideFlags = HideFlags.DontSave };
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }

        static void AddCap(List<Vector3> v, List<Vector3> n, List<int> t, float r, float z, Vector3 normal, int segments)
        {
            int center = v.Count;
            v.Add(new Vector3(0f, 0f, z)); n.Add(normal);
            for (int i = 0; i <= segments; i++)
            {
                float a = Mathf.PI * i / segments;
                v.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, z)); n.Add(normal);
            }
            for (int i = 0; i < segments; i++)
            {
                int a = center + 1 + i, b = a + 1;
                if (normal.z < 0f) { t.Add(center); t.Add(b); t.Add(a); }
                else { t.Add(center); t.Add(a); t.Add(b); }
            }
        }

        /// <summary>
        /// Coiled wire (concertina / razor wire) along X from -length/2 to +length/2,
        /// coil centre at y = 0. Thin triangular tube.
        /// </summary>
        public static Mesh Coil(float length, float coilRadius = 0.3f, float turnsPerMetre = 3f, float wireRadius = 0.012f)
        {
            int turns = Mathf.Max(1, Mathf.RoundToInt(length * turnsPerMetre));
            const int stepsPerTurn = 14;
            const int sides = 3;
            int steps = turns * stepsPerTurn;

            var v = new List<Vector3>((steps + 1) * sides);
            var n = new List<Vector3>((steps + 1) * sides);
            var t = new List<int>(steps * sides * 6);

            Vector3 Point(float s)
            {
                float a = s * Mathf.PI * 2f * turns;
                // Slightly irregular radius so it looks hand-strung.
                float r = coilRadius * (1f + 0.08f * Mathf.Sin(s * 37f));
                return new Vector3(-length * 0.5f + s * length, Mathf.Sin(a) * r, Mathf.Cos(a) * r);
            }

            for (int i = 0; i <= steps; i++)
            {
                float s = (float)i / steps;
                Vector3 p = Point(s);
                Vector3 tangent = (Point(Mathf.Min(1f, s + 0.001f)) - Point(Mathf.Max(0f, s - 0.001f))).normalized;
                Vector3 side = Vector3.Cross(tangent, Vector3.right);
                if (side.sqrMagnitude < 1e-6f) side = Vector3.up;
                side.Normalize();
                Vector3 up = Vector3.Cross(side, tangent);
                for (int k = 0; k < sides; k++)
                {
                    float a = k * Mathf.PI * 2f / sides;
                    Vector3 dir = side * Mathf.Cos(a) + up * Mathf.Sin(a);
                    v.Add(p + dir * wireRadius);
                    n.Add(dir);
                }
            }
            for (int i = 0; i < steps; i++)
            {
                for (int k = 0; k < sides; k++)
                {
                    int a = i * sides + k;
                    int b = i * sides + (k + 1) % sides;
                    int c = a + sides, d = b + sides;
                    t.Add(a); t.Add(c); t.Add(b);
                    t.Add(b); t.Add(c); t.Add(d);
                }
            }

            var m = new Mesh { name = "Coil", hideFlags = HideFlags.DontSave };
            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v);
            m.SetNormals(n);
            m.SetTriangles(t, 0);
            m.RecalculateBounds();
            return m;
        }
    }
}
