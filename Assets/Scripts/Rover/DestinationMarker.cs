using UnityEngine;
using UnityEngine.Rendering;

namespace Bobery.Rover
{
    /// <summary>
    /// In-world destination marker: a ring hugging the terrain, an expanding pulse ring
    /// and a floating, bobbing arrow pointing down at the spot. Built from code, no assets needed.
    /// </summary>
    public class DestinationMarker : MonoBehaviour
    {
        public Color color = Color.white;
        [Tooltip("Outer radius of the ground ring (metres).")]
        public float radius = 2f;
        [Tooltip("Colliders under this transform are ignored when hugging the ground (the rover).")]
        public Transform ignore;
        public LayerMask groundMask = ~0;

        const int Segments = 64;
        const float PulsePeriod = 1.4f;

        Mesh _ringMesh, _pulseMesh;
        Material _ringMat, _pulseMat, _arrowMat;
        Transform _ring, _pulse, _arrow;
        Vector3 _point;
        float _shownAt;
        bool _visible;
        readonly RaycastHit[] _hits = new RaycastHit[8];
        Vector3[] _ringVerts;

        public bool Visible => _visible;

        void Awake() => Build();

        void Build()
        {
            _ringMat = MakeMaterial(color, transparent: true);
            _pulseMat = MakeMaterial(color, transparent: true);
            _arrowMat = MakeMaterial(color, transparent: false);

            _ringMesh = new Mesh { name = "MarkerRing" };
            _pulseMesh = new Mesh { name = "MarkerPulse" };
            _ring = MakeChild("Ring", _ringMesh, _ringMat);
            _pulse = MakeChild("Pulse", _pulseMesh, _pulseMat);
            _arrow = MakeChild("Arrow", MakeArrowMesh(), _arrowMat);

            SetVisible(false);
        }

        public void Show(Vector3 point)
        {
            _point = point;
            _shownAt = Time.time;
            // Ring meshes are built in world space, so the marker stays at the origin.
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            BuildConformedRing(_ringMesh, _point, radius * 0.78f, radius);
            SetVisible(true);
        }

        public void Hide() => SetVisible(false);

        void SetVisible(bool v)
        {
            _visible = v;
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.enabled = v;
        }

        void Update()
        {
            if (!_visible) return;
            float t = Time.time - _shownAt;

            // Pop in.
            float pop = Mathf.Clamp01(t / 0.25f);
            float popScale = 1f + 0.4f * (1f - pop) * (1f - pop);

            // Ground ring breathes a little.
            SetAlpha(_ringMat, 0.75f + 0.2f * Mathf.Sin(t * 4f));

            // Pulse expands outward and fades.
            float p = (t % PulsePeriod) / PulsePeriod;
            float pr = Mathf.Lerp(radius * 0.3f, radius * 1.8f, p) * popScale;
            BuildConformedRing(_pulseMesh, _point, pr * 0.88f, pr);
            SetAlpha(_pulseMat, 0.7f * (1f - p) * (1f - p));

            // Arrow bobs and spins above the spot.
            float bob = Mathf.Sin(t * 3f) * radius * 0.15f;
            float h = GroundHeight(_point) + radius * 1.4f + bob;
            _arrow.position = new Vector3(_point.x, h, _point.z);
            _arrow.rotation = Quaternion.Euler(0f, t * 90f, 0f);
            _arrow.localScale = Vector3.one * radius * 0.6f * popScale;
        }

        // ---------------------------------------------------------------- geometry

        /// <summary>Flat annulus whose vertices are dropped onto the ground, so it hugs hills.</summary>
        void BuildConformedRing(Mesh mesh, Vector3 center, float inner, float outer)
        {
            int n = Segments;
            if (_ringVerts == null || _ringVerts.Length != (n + 1) * 2) _ringVerts = new Vector3[(n + 1) * 2];
            var verts = _ringVerts;
            float lift = Mathf.Max(0.05f, radius * 0.04f);
            for (int i = 0; i <= n; i++)
            {
                float a = i / (float)n * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 pi = center + dir * inner;
                Vector3 po = center + dir * outer;
                pi.y = GroundHeight(pi) + lift;
                po.y = GroundHeight(po) + lift;
                verts[i * 2] = pi;
                verts[i * 2 + 1] = po;
            }

            mesh.vertices = verts;
            if (mesh.triangles.Length == 0)
            {
                var tris = new int[n * 6];
                for (int i = 0; i < n; i++)
                {
                    int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                    tris[i * 6 + 0] = a; tris[i * 6 + 1] = c; tris[i * 6 + 2] = b;
                    tris[i * 6 + 3] = b; tris[i * 6 + 4] = c; tris[i * 6 + 5] = d;
                }
                mesh.triangles = tris;
            }
            mesh.RecalculateBounds();
        }

        float GroundHeight(Vector3 p)
        {
            Vector3 from = new Vector3(p.x, _point.y + radius * 20f + 50f, p.z);
            int count = Physics.RaycastNonAlloc(from, Vector3.down, _hits, radius * 40f + 200f, groundMask, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            float bestDist = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (ignore && _hits[i].transform.IsChildOf(ignore)) continue;
                if (_hits[i].distance < bestDist) { bestDist = _hits[i].distance; best = _hits[i].point.y; }
            }
            return float.IsNegativeInfinity(best) ? _point.y : best;
        }

        /// <summary>Downward-pointing four-sided arrow (pyramid head + short shaft), pivot at the tip.</summary>
        static Mesh MakeArrowMesh()
        {
            var m = new Mesh { name = "MarkerArrow" };
            // Head: tip at y=0, base square at y=1. Shaft: thin box from y=1 to y=1.8.
            Vector3 tip = Vector3.zero;
            float w = 0.6f, s = 0.2f;
            Vector3[] baseSq = { new(-w, 1, -w), new(w, 1, -w), new(w, 1, w), new(-w, 1, w) };
            var verts = new System.Collections.Generic.List<Vector3>();
            var tris = new System.Collections.Generic.List<int>();

            void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                int i = verts.Count;
                verts.Add(a); verts.Add(b); verts.Add(c);
                tris.Add(i); tris.Add(i + 1); tris.Add(i + 2);
            }
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { Tri(a, b, c); Tri(a, c, d); }

            for (int i = 0; i < 4; i++) Tri(tip, baseSq[(i + 1) % 4], baseSq[i]);
            Quad(baseSq[0], baseSq[1], baseSq[2], baseSq[3]);

            Vector3[] lo = { new(-s, 1, -s), new(s, 1, -s), new(s, 1, s), new(-s, 1, s) };
            Vector3[] hi = { new(-s, 1.8f, -s), new(s, 1.8f, -s), new(s, 1.8f, s), new(-s, 1.8f, s) };
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                Quad(lo[i], lo[j], hi[j], hi[i]);
            }
            Quad(hi[3], hi[2], hi[1], hi[0]);

            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        // ---------------------------------------------------------------- helpers

        Transform MakeChild(string name, Mesh mesh, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        static Material MakeMaterial(Color c, bool transparent)
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var m = new Material(shader) { name = transparent ? "MarkerTransparent" : "MarkerSolid" };
            if (transparent)
            {
                m.SetFloat("_Surface", 1f);   // Transparent
                m.SetFloat("_Blend", 0f);     // Alpha
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)RenderQueue.Transparent;
            }
            if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f); // double-sided
            SetColor(m, c);
            return m;
        }

        static void SetColor(Material m, Color c)
        {
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        }

        void SetAlpha(Material m, float a)
        {
            var c = color;
            c.a = Mathf.Clamp01(a);
            SetColor(m, c);
        }

        void OnDestroy()
        {
            Destroy(_ringMesh); Destroy(_pulseMesh);
            Destroy(_ringMat); Destroy(_pulseMat); Destroy(_arrowMat);
        }
    }
}
