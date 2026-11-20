using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bobery.Terrain
{
    /// <summary>
    /// Procedural 3D terrain: gentle rolling hills with a few small lakes.
    /// Mostly land – lakes are carved as separate basins below the water level.
    /// Regenerates in the editor when values change (and on Play).
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class TerrainGenerator : MonoBehaviour
    {
        [Header("Size")]
        [Tooltip("World size of the map (X and Z) in metres.")]
        [Min(10f)] public float mapSize = 240f;
        [Tooltip("Vertices per side. Higher = smoother.")]
        [Range(32, 512)] public int resolution = 241;

        [Header("Seed")]
        public int seed = 1337;
        public bool randomSeedOnPlay = false;

        [Header("Hills")]
        [Tooltip("Base ground height above the water level.")]
        public float baseHeight = 2.5f;
        [Tooltip("Max height of the small hills.")]
        public float hillHeight = 7f;
        [Tooltip("Smaller = wider hills.")]
        public float hillFrequency = 0.012f;
        [Range(1, 8)] public int octaves = 5;
        [Range(0.1f, 0.9f)] public float persistence = 0.45f;
        [Range(1.5f, 3f)] public float lacunarity = 2.1f;
        [Tooltip("Pushes low areas flatter, keeps hills rounded (1 = no change).")]
        [Range(1f, 3f)] public float hillSharpness = 1.6f;
        [Tooltip("Tiny surface bumps.")]
        public float detailHeight = 0.25f;

        [Header("Lakes")]
        [Range(0, 20)] public int lakeCount = 5;
        public Vector2 lakeRadius = new Vector2(9f, 18f);
        [Tooltip("How deep below the water surface a lake bottom goes.")]
        public float lakeDepth = 3f;
        [Tooltip("Irregularity of lake shorelines (0 = circles).")]
        [Range(0f, 0.6f)] public float shoreNoise = 0.35f;
        [Tooltip("Keep lakes this far from the map edge.")]
        public float lakeEdgeMargin = 25f;

        [Header("Craters")]
        [Range(0, 40)] public int craterCount = 0;
        public Vector2 craterRadius = new Vector2(4f, 12f);
        [Tooltip("Crater depth as a fraction of its radius.")]
        [Range(0f, 0.6f)] public float craterDepth = 0.25f;
        [Tooltip("Height of the raised rim as a fraction of its radius.")]
        [Range(0f, 0.3f)] public float craterRim = 0.08f;
        [Tooltip("Keep crater floors this far above the water so they stay dry.")]
        public float craterMinAboveWater = 0.3f;

        [Header("Water")]
        public float waterLevel = 0f;
        public bool generateWater = true;

        [Header("Materials")]
        public Material terrainMaterial;
        public Material waterMaterial;

        [Header("Physics")]
        public bool generateCollider = true;

        const string TerrainChildName = "Terrain Mesh";
        const string WaterChildName = "Water";

        struct Lake
        {
            public Vector2 center;
            public float radius;
            public float phase;
        }

        readonly List<Lake> _lakes = new List<Lake>();
        readonly List<Lake> _craters = new List<Lake>();
        Vector2 _noiseOffset;
        Mesh _terrainMesh;
        Mesh _waterMesh;
        bool _dirty;

        /// <summary>Lake centres in world space (useful for spawning, AI, etc.).</summary>
        public IReadOnlyList<Vector3> LakeCenters
        {
            get
            {
                var list = new List<Vector3>(_lakes.Count);
                foreach (var l in _lakes)
                    list.Add(transform.TransformPoint(new Vector3(l.center.x, waterLevel, l.center.y)));
                return list;
            }
        }

        void OnEnable()
        {
            if (Application.isPlaying && randomSeedOnPlay)
                seed = Random.Range(int.MinValue, int.MaxValue);
            Generate();
        }

        void OnValidate()
        {
            // Mesh changes are not allowed inside OnValidate – defer to Update.
            _dirty = true;
#if UNITY_EDITOR
            if (!Application.isPlaying) UnityEditor.EditorApplication.QueuePlayerLoopUpdate();
#endif
        }

        void Update()
        {
            if (_dirty)
            {
                _dirty = false;
                Generate();
            }
        }

        void OnDestroy()
        {
            DestroySafe(_terrainMesh);
            DestroySafe(_waterMesh);
        }

        [ContextMenu("Generate")]
        public void Generate()
        {
            SetupLakes();
            BuildTerrain();
            BuildWater();
        }

        [ContextMenu("Randomize Seed")]
        public void RandomizeSeed()
        {
            seed = Random.Range(int.MinValue, int.MaxValue);
            Generate();
        }

        // ------------------------------------------------------------------ height

        void SetupLakes()
        {
            var rng = new System.Random(seed);
            _noiseOffset = new Vector2((float)rng.NextDouble() * 10000f, (float)rng.NextDouble() * 10000f);

            _lakes.Clear();
            float half = mapSize * 0.5f;
            float min = -half + lakeEdgeMargin;
            float max = half - lakeEdgeMargin;
            if (max <= min) { min = -half * 0.5f; max = half * 0.5f; }

            int attempts = 0;
            while (_lakes.Count < lakeCount && attempts < lakeCount * 50)
            {
                attempts++;
                float r = Mathf.Lerp(lakeRadius.x, lakeRadius.y, (float)rng.NextDouble());
                var c = new Vector2(Mathf.Lerp(min, max, (float)rng.NextDouble()),
                                    Mathf.Lerp(min, max, (float)rng.NextDouble()));

                // Keep lakes apart so they stay small and separate.
                bool ok = true;
                foreach (var other in _lakes)
                {
                    if (Vector2.Distance(c, other.center) < (r + other.radius) * 1.6f) { ok = false; break; }
                }
                if (!ok) continue;

                _lakes.Add(new Lake { center = c, radius = r, phase = (float)rng.NextDouble() * 100f });
            }

            // Craters: may overlap each other (looks like repeated impacts) but avoid lakes.
            _craters.Clear();
            attempts = 0;
            while (_craters.Count < craterCount && attempts < craterCount * 50)
            {
                attempts++;
                float r = Mathf.Lerp(craterRadius.x, craterRadius.y, Mathf.Pow((float)rng.NextDouble(), 2f)); // more small ones
                var c = new Vector2(Mathf.Lerp(-half + r * 2f, half - r * 2f, (float)rng.NextDouble()),
                                    Mathf.Lerp(-half + r * 2f, half - r * 2f, (float)rng.NextDouble()));
                bool ok = true;
                foreach (var lake in _lakes)
                {
                    if (Vector2.Distance(c, lake.center) < (lake.radius * 1.9f + r * 2f)) { ok = false; break; }
                }
                if (!ok) continue;
                _craters.Add(new Lake { center = c, radius = r, phase = (float)rng.NextDouble() * 100f });
            }
        }

        /// <summary>Crater height offset: bowl below 0, raised rim, fades out by 2x radius.</summary>
        float CraterOffset(Lake crater, float x, float z)
        {
            Vector2 dv = new Vector2(x, z) - crater.center;
            float angle = Mathf.Atan2(dv.y, dv.x);
            float wobble = 1f + 0.15f * (Mathf.PerlinNoise(Mathf.Cos(angle) + crater.phase, Mathf.Sin(angle) + crater.phase) - 0.5f) * 2f;
            float r = crater.radius * wobble;
            float d = dv.magnitude / r;
            if (d >= 2f) return 0f;

            float depth = craterDepth * r;
            float rim = craterRim * r;
            if (d < 1f)
                return depth * (d * d - 1f) + rim * d * d * d; // bowl rising into the rim
            float o = 1f - (d - 1f);                             // 1 at rim -> 0 at 2r
            return rim * o * o;
        }

        float Fbm(float x, float z)
        {
            float amp = 1f, freq = hillFrequency, sum = 0f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                float n = Mathf.PerlinNoise(x * freq + _noiseOffset.x + i * 31.7f, z * freq + _noiseOffset.y - i * 17.3f);
                sum += n * amp;
                norm += amp;
                amp *= persistence;
                freq *= lacunarity;
            }
            return sum / norm; // 0..1
        }

        /// <summary>Terrain height (local space) at local XZ.</summary>
        public float SampleHeight(float x, float z)
        {
            // Rolling hills – always above water.
            float n = Mathf.Clamp01(Fbm(x, z));
            n = Mathf.Pow(n, hillSharpness) * Mathf.Pow(2f, hillSharpness - 1f); // flatten lows, keep 0.5 ~ 0.5
            float h = waterLevel + baseHeight + n * hillHeight;

            // Small bumps.
            h += (Mathf.PerlinNoise(x * 0.15f + _noiseOffset.y, z * 0.15f + _noiseOffset.x) - 0.5f) * detailHeight * 2f;

            // Impact craters.
            if (_craters.Count > 0)
            {
                float before = h;
                foreach (var crater in _craters)
                    h += CraterOffset(crater, x, z);
                if (h < before) h = Mathf.Max(h, waterLevel + craterMinAboveWater);
            }

            // Carve lakes: smooth basin that blends into the surrounding land.
            foreach (var lake in _lakes)
            {
                Vector2 d = new Vector2(x, z) - lake.center;
                float dist = d.magnitude;
                float angle = Mathf.Atan2(d.y, d.x);
                float wobble = 1f + shoreNoise * (Mathf.PerlinNoise(Mathf.Cos(angle) * 1.5f + lake.phase,
                                                                    Mathf.Sin(angle) * 1.5f + lake.phase) - 0.5f) * 2f;
                float r = lake.radius * wobble;
                float outer = r * 1.9f; // shore slope reaches back up to the hills here
                if (dist >= outer) continue;

                float lakeH;
                if (dist < r)
                {
                    // Bowl: flat-ish bottom, sloped edge, crosses the water line exactly at r.
                    float u = dist / r;
                    lakeH = Mathf.Lerp(waterLevel - lakeDepth, waterLevel, u * u);
                }
                else
                {
                    // Shore: rises from the water line and eases into the surrounding land.
                    float v = (dist - r) / (outer - r);
                    v = 1f - (1f - v) * (1f - v);
                    lakeH = Mathf.Lerp(waterLevel, h, v);
                }
                h = Mathf.Min(h, lakeH);
            }

            return h;
        }

        // ------------------------------------------------------------------ meshes

        void BuildTerrain()
        {
            var go = GetOrCreateChild(TerrainChildName);
            var mf = GetOrAdd<MeshFilter>(go);
            var mr = GetOrAdd<MeshRenderer>(go);
            if (terrainMaterial) mr.sharedMaterial = terrainMaterial;
            mr.shadowCastingMode = ShadowCastingMode.On;
            mr.receiveShadows = true;

            if (_terrainMesh == null)
            {
                _terrainMesh = new Mesh { name = "Procedural Terrain", hideFlags = HideFlags.DontSave };
                _terrainMesh.indexFormat = IndexFormat.UInt32;
            }

            int res = Mathf.Max(2, resolution);
            float step = mapSize / (res - 1);
            float half = mapSize * 0.5f;

            var verts = new Vector3[res * res];
            var uvs = new Vector2[res * res];
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                {
                    float px = -half + x * step;
                    float pz = -half + z * step;
                    int i = z * res + x;
                    verts[i] = new Vector3(px, SampleHeight(px, pz), pz);
                    uvs[i] = new Vector2((float)x / (res - 1), (float)z / (res - 1));
                }
            }

            var tris = new int[(res - 1) * (res - 1) * 6];
            int t = 0;
            for (int z = 0; z < res - 1; z++)
            {
                for (int x = 0; x < res - 1; x++)
                {
                    int i = z * res + x;
                    tris[t++] = i;
                    tris[t++] = i + res;
                    tris[t++] = i + 1;
                    tris[t++] = i + 1;
                    tris[t++] = i + res;
                    tris[t++] = i + res + 1;
                }
            }

            _terrainMesh.Clear();
            _terrainMesh.vertices = verts;
            _terrainMesh.uv = uvs;
            _terrainMesh.triangles = tris;
            _terrainMesh.RecalculateNormals();
            _terrainMesh.RecalculateTangents();
            _terrainMesh.RecalculateBounds();
            mf.sharedMesh = _terrainMesh;

            if (generateCollider)
            {
                var mc = GetOrAdd<MeshCollider>(go);
                mc.sharedMesh = null;
                mc.sharedMesh = _terrainMesh;
            }
            else
            {
                var mc = go.GetComponent<MeshCollider>();
                if (mc) DestroySafe(mc);
            }

            if (terrainMaterial)
            {
                terrainMaterial.SetFloat("_WaterLevel", transform.position.y + waterLevel);
            }
        }

        void BuildWater()
        {
            var existing = transform.Find(WaterChildName);
            if (!generateWater)
            {
                if (existing) DestroySafe(existing.gameObject);
                return;
            }

            var go = GetOrCreateChild(WaterChildName);
            go.transform.localPosition = new Vector3(0f, waterLevel, 0f);
            var mf = GetOrAdd<MeshFilter>(go);
            var mr = GetOrAdd<MeshRenderer>(go);
            if (waterMaterial) mr.sharedMaterial = waterMaterial;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = true;

            if (_waterMesh == null)
                _waterMesh = new Mesh { name = "Procedural Water", hideFlags = HideFlags.DontSave };

            // A subdivided plane covering the map (subdivided for vertex waves).
            int res = 96;
            float half = mapSize * 0.5f;
            float step = mapSize / (res - 1);
            var verts = new Vector3[res * res];
            var uvs = new Vector2[res * res];
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                {
                    int i = z * res + x;
                    verts[i] = new Vector3(-half + x * step, 0f, -half + z * step);
                    uvs[i] = new Vector2((float)x / (res - 1), (float)z / (res - 1));
                }

            var tris = new int[(res - 1) * (res - 1) * 6];
            int t = 0;
            for (int z = 0; z < res - 1; z++)
                for (int x = 0; x < res - 1; x++)
                {
                    int i = z * res + x;
                    tris[t++] = i; tris[t++] = i + res; tris[t++] = i + 1;
                    tris[t++] = i + 1; tris[t++] = i + res; tris[t++] = i + res + 1;
                }

            _waterMesh.Clear();
            _waterMesh.vertices = verts;
            _waterMesh.uv = uvs;
            _waterMesh.triangles = tris;
            _waterMesh.RecalculateNormals();
            _waterMesh.RecalculateBounds();
            // Waves move vertices a little – pad bounds so it isn't culled.
            var b = _waterMesh.bounds; b.Expand(new Vector3(0f, 2f, 0f)); _waterMesh.bounds = b;
            mf.sharedMesh = _waterMesh;
        }

        // ------------------------------------------------------------------ helpers

        GameObject GetOrCreateChild(string childName)
        {
            var tr = transform.Find(childName);
            if (tr) return tr.gameObject;
            var go = new GameObject(childName);
            go.transform.SetParent(transform, false);
            go.layer = gameObject.layer;
            go.isStatic = false;
            return go;
        }

        static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var c = go.GetComponent<T>();
            return c ? c : go.AddComponent<T>();
        }

        static void DestroySafe(Object o)
        {
            if (!o) return;
            if (Application.isPlaying) Destroy(o);
            else DestroyImmediate(o);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.6f, 1f, 0.8f);
            foreach (var l in _lakes)
                Gizmos.DrawWireSphere(transform.TransformPoint(new Vector3(l.center.x, waterLevel, l.center.y)), l.radius);
        }
    }
}
