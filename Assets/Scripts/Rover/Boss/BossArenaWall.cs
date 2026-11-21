using System.Collections.Generic;
using UnityEngine;

namespace Bobery.Rover
{
    /// <summary>
    /// A ring of raised, rocky ground around the boss arena. Built on the terrain (each vertex follows the ground)
    /// with the terrain's own material, so it reads as the land itself heaving up. Starts sunk underground and
    /// rises when <see cref="Rise"/> is called.
    /// </summary>
    public class BossArenaWall : MonoBehaviour
    {
        const int Segments = 120;

        float _height, _t = -1f, _riseTime = 1.6f;
        Mesh _mesh;

        public bool IsRaised => _t >= 1f;

        public static BossArenaWall Build(Vector3 center, float radius, float height, float thickness)
        {
            var go = new GameObject("BossArenaWall");
            var wall = go.AddComponent<BossArenaWall>();
            wall._height = height;
            wall.BuildMesh(center, radius, height, thickness);
            go.transform.position = Vector3.down * (height + 3f); // underground until it rises
            return wall;
        }

        public void Rise(float seconds)
        {
            _riseTime = Mathf.Max(0.1f, seconds);
            _t = 0f;
        }

        void Update()
        {
            if (_t < 0f || _t >= 1f) return;
            _t = Mathf.Min(1f, _t + Time.deltaTime / _riseTime);
            float e = 1f - Mathf.Pow(1f - _t, 3f);
            float jitter = _t < 1f ? Mathf.Sin(Time.time * 60f) * 0.15f * (1f - _t) : 0f;
            transform.position = Vector3.down * ((_height + 3f) * (1f - e) + jitter);
        }

        void BuildMesh(Vector3 center, float radius, float height, float thickness)
        {
            var terrain = FindAnyObjectByType<Bobery.Terrain.TerrainGenerator>();
            float seed = Random.value * 100f;

            // Profile across the wall (inside foot -> crest -> outside foot): radial offset, height share.
            var profile = new[]
            {
                new Vector2(-0.10f, -0.35f),
                new Vector2(0.18f, 0.55f),
                new Vector2(0.34f, 0.95f),
                new Vector2(0.55f, 1.00f),
                new Vector2(0.78f, 0.55f),
                new Vector2(1.05f, -0.35f),
            };
            int rows = profile.Length;
            var verts = new List<Vector3>((Segments + 1) * rows);
            var uvs = new List<Vector2>((Segments + 1) * rows);
            for (int s = 0; s <= Segments; s++)
            {
                int si = s % Segments; // the seam shares its noise with the start
                float a = si * Mathf.PI * 2f / Segments;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                // Rocky silhouette: a slow swell plus sharper crags.
                float swell = Mathf.PerlinNoise(Mathf.Cos(a) * 1.3f + seed, Mathf.Sin(a) * 1.3f + seed);
                float crag = Mathf.PerlinNoise(Mathf.Cos(a) * 5f + seed * 2f, Mathf.Sin(a) * 5f - seed);
                float h = height * (0.7f + 0.45f * swell + 0.35f * crag * crag);
                float wobble = (Mathf.PerlinNoise(Mathf.Cos(a) * 3f - seed, Mathf.Sin(a) * 3f + seed) - 0.5f) * thickness * 0.5f;
                for (int r = 0; r < rows; r++)
                {
                    Vector3 p = center + dir * (radius + wobble + profile[r].x * thickness);
                    float ground = GroundY(terrain, p);
                    float lump = r > 0 && r < rows - 1
                        ? (Mathf.PerlinNoise(p.x * 0.15f + seed, p.z * 0.15f) - 0.5f) * height * 0.25f : 0f;
                    p.y = ground + profile[r].y * h + lump;
                    verts.Add(p);
                    uvs.Add(new Vector2((float)s / Segments, (float)r / (rows - 1)));
                }
            }

            var tris = new List<int>(Segments * (rows - 1) * 6);
            for (int s = 0; s < Segments; s++)
                for (int r = 0; r < rows - 1; r++)
                {
                    int i0 = s * rows + r, i1 = i0 + 1, j0 = i0 + rows, j1 = j0 + 1;
                    // Outward-facing winding (normals point away from the wall's core).
                    tris.Add(i0); tris.Add(i1); tris.Add(j0);
                    tris.Add(i1); tris.Add(j1); tris.Add(j0);
                }

            _mesh = new Mesh { name = "BossArenaWall" };
            if (verts.Count > 65000) _mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            _mesh.SetVertices(verts);
            _mesh.SetUVs(0, uvs);
            _mesh.SetTriangles(tris, 0);
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
            // Make sure the faces point the right way (towards the camera on both sides of the ring).
            var n = _mesh.normals;
            int up = 0;
            for (int i = 0; i < n.Length; i++) if (n[i].y > 0f) up++;
            if (up < n.Length / 2)
            {
                for (int i = 0; i < tris.Count; i += 3) { int t = tris[i]; tris[i] = tris[i + 1]; tris[i + 1] = t; }
                _mesh.SetTriangles(tris, 0);
                _mesh.RecalculateNormals();
            }

            gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
            var mr = gameObject.AddComponent<MeshRenderer>();
            mr.sharedMaterial = terrain && terrain.terrainMaterial ? terrain.terrainMaterial : RockMaterial();
            gameObject.AddComponent<MeshCollider>().sharedMesh = _mesh;
        }

        static float GroundY(Bobery.Terrain.TerrainGenerator terrain, Vector3 p) => EnemyRover.GroundY(terrain, p);

        static Material _rock;
        static Material RockMaterial()
        {
            if (_rock) return _rock;
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _rock = new Material(sh) { name = "BossArenaRock", color = new Color(0.42f, 0.38f, 0.33f) };
            if (_rock.HasProperty("_BaseColor")) _rock.SetColor("_BaseColor", _rock.color);
            return _rock;
        }

        void OnDestroy() { if (_mesh) Destroy(_mesh); }
    }
}
