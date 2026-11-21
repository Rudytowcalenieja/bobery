using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.InputSystem;
using UnityEngine.Playables;

namespace Bobery.Rover
{
    /// <summary>
    /// Hull repairs inside the rover. Every leak in <see cref="RoverHealth.Leaks"/> shows up as a hissing hole on a
    /// cabin wall the camera can see. Click a leak and the crew member walks over and patches it (the DIY animation);
    /// when the repair finishes the leak is sealed and its health comes back. Click the floor to just walk there.
    /// Created by <see cref="InteriorRoom"/>.
    /// </summary>
    public class InteriorRepairs : MonoBehaviour
    {
        [Tooltip("Seconds to patch one leak.")]
        public float repairTime = 3.5f;
        public float walkSpeed = 1.6f;
        [Tooltip("Height the crew member is scaled to (metres).")]
        public float crewHeight = 1.75f;

        public RoverHealth Health => _health;
        /// <summary>Leak under the mouse (for the HUD hint), or null.</summary>
        public LeakSpot Hovered { get; private set; }
        public LeakSpot Repairing => _crew != null && _crew.State == RepairCrew.Mode.Repairing ? _crew.Leak : null;
        public float RepairProgress => _crew != null ? _crew.Progress : 0f;

        struct Wall { public Vector3 origin, u, v, n; }

        RoverHealth _health;
        Camera _cam;
        Transform _cabin;
        readonly List<Wall> _walls = new List<Wall>();
        readonly Dictionary<int, LeakSpot> _spots = new Dictionary<int, LeakSpot>();
        Bounds _floor;
        bool _hasFloor;
        RepairCrew _crew;
        System.Func<bool> _inputBlocked;
        readonly RaycastHit[] _hits = new RaycastHit[16];

        public void Init(RoverHealth health, Camera cam, Transform cabin, GameObject crewModel, AnimationClip walk, AnimationClip repair,
                         System.Func<bool> inputBlocked, RepairCrew sceneCrew = null)
        {
            _health = health;
            _cam = cam;
            _cabin = cabin ? cabin : transform;
            _inputBlocked = inputBlocked;
            FindWallsAndFloor();

            if (sceneCrew)
            {
                // Character placed in the scene: keep its position, rotation, model and settings.
                _crew = sceneCrew;
                _crew.Build(null, walk, repair, crewHeight);
                _crew.Place(_crew.transform.position, _crew.transform.rotation);
                _crew.Finished += OnRepairFinished;
            }
            else
            {
                var crewGo = new GameObject("RepairCrew");
                crewGo.transform.SetParent(_cabin, false);
                _crew = crewGo.AddComponent<RepairCrew>();
                _crew.Build(crewModel, walk, repair, crewHeight);
                _crew.walkSpeed = walkSpeed;
                _crew.repairTime = repairTime;
                _crew.Finished += OnRepairFinished;
                Vector3 start = _hasFloor ? new Vector3(_floor.center.x, _floor.max.y, _floor.center.z) : _cabin.position;
                _crew.Place(ClampToFloor(start), _cam ? Quaternion.LookRotation(Flat(_cam.transform.position - start)) : Quaternion.identity);
            }

            StartCoroutine(FirstSync());
        }

        /// <summary>Waits until the room has fitted the camera beside the side panel, so on-screen checks are right.</summary>
        System.Collections.IEnumerator FirstSync()
        {
            for (int i = 0; i < 3; i++) yield return null;
            if (_health) _health.Changed += SyncLeaks;
            SyncLeaks();
        }

        void OnDestroy()
        {
            if (_health) _health.Changed -= SyncLeaks;
        }

        // ================================================================ walls

        void FindWallsAndFloor()
        {
            _walls.Clear();
            var renderers = _cabin.GetComponentsInChildren<MeshRenderer>(true);
            Bounds room = default;
            bool any = false;
            foreach (var r in renderers)
            {
                if (!any) { room = r.bounds; any = true; } else room.Encapsulate(r.bounds);
                if (r.name.StartsWith("Floor"))
                {
                    if (!_hasFloor) { _floor = r.bounds; _hasFloor = true; } else _floor.Encapsulate(r.bounds);
                }
            }
            if (!any) return;

            foreach (var r in renderers)
            {
                if (!r.name.StartsWith("Wall")) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (!mf || !mf.sharedMesh) continue;
                var t = r.transform;
                var b = mf.sharedMesh.bounds;
                Vector3 c = t.TransformPoint(b.center);
                Vector3[] axes = { t.TransformVector(Vector3.right * b.size.x), t.TransformVector(Vector3.up * b.size.y), t.TransformVector(Vector3.forward * b.size.z) };
                int thin = 0;
                for (int i = 1; i < 3; i++) if (axes[i].sqrMagnitude < axes[thin].sqrMagnitude) thin = i;
                int a = (thin + 1) % 3, d = (thin + 2) % 3;
                // v = the axis closest to world up, u = the other one
                int vi = Mathf.Abs(Vector3.Dot(axes[a].normalized, Vector3.up)) > Mathf.Abs(Vector3.Dot(axes[d].normalized, Vector3.up)) ? a : d;
                int ui = vi == a ? d : a;
                Vector3 n = axes[thin].normalized;
                if (Vector3.Dot(room.center - c, n) < 0f) n = -n;
                // The camera can't see this face (it faces away from it): no leaks there.
                if (_cam && Vector3.Dot(n, _cam.transform.forward) > 0.2f) continue;
                Vector3 u = axes[ui], v = axes[vi];
                if (Vector3.Dot(v, Vector3.up) < 0f) v = -v;
                Vector3 face = c + n * (axes[thin].magnitude * 0.5f);
                _walls.Add(new Wall { origin = face - u * 0.5f - v * 0.5f, u = u, v = v, n = n });
            }

            if (_walls.Count == 0) // no "Wall*" objects: use the sides of the cabin's bounds
            {
                Vector3 e = room.extents, c = room.center;
                void Side(Vector3 n, Vector3 u)
                {
                    if (_cam && Vector3.Dot(n, _cam.transform.forward) > 0.2f) return;
                    Vector3 face = c - Vector3.Scale(n, e);
                    Vector3 uu = Vector3.Scale(u, e) * 2f, vv = Vector3.up * e.y * 2f;
                    _walls.Add(new Wall { origin = face - uu * 0.5f - vv * 0.5f, u = uu, v = vv, n = n });
                }
                Side(Vector3.forward, Vector3.right); Side(Vector3.back, Vector3.right);
                Side(Vector3.right, Vector3.forward); Side(Vector3.left, Vector3.forward);
            }
        }

        /// <summary>World position + inward normal for a leak; avoids spots hidden behind props.</summary>
        bool Locate(RoverHealth.Leak leak, out Vector3 pos, out Vector3 normal)
        {
            pos = normal = default;
            if (_walls.Count == 0) return false;
            int first = Mathf.Clamp((int)(leak.wall * _walls.Count), 0, _walls.Count - 1);
            // Prefer the stored spot; if it's off-screen or a console/tank hides it, slide along the wall,
            // then try the other walls, until it's visible.
            for (int k = 0; k < _walls.Count; k++)
            {
                var w = _walls[(first + k) % _walls.Count];
                for (int i = 0; i < 24; i++)
                {
                    float u = i == 0 ? leak.u : Mathf.Repeat(leak.u - 0.1f + i * 0.137f, 0.8f) + 0.1f;
                    float v = i < 12 ? leak.v : Mathf.Repeat(leak.v + 0.17f, 0.35f) + 0.2f;
                    Vector3 p = w.origin + w.u * u + w.v * v + w.n * 0.02f;
                    if (k == 0 && i == 0) { pos = p; normal = w.n; } // fallback: the stored spot
                    if ((!_cam || Visible(p)) && !Crowded(p)) { pos = p; normal = w.n; return true; }
                }
            }
            return true;
        }

        bool Visible(Vector3 p)
        {
            Vector3 vp = _cam.WorldToViewportPoint(p);
            if (vp.z <= 0.3f || vp.x < 0.06f || vp.x > 0.94f || vp.y < 0.08f || vp.y > 0.92f) return false;
            Vector3 from = _cam.transform.position;
            Vector3 d = p - from;
            int n = Physics.RaycastNonAlloc(from, d.normalized, _hits, d.magnitude - 0.03f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (_crew && _hits[i].transform.IsChildOf(_crew.transform)) continue;
                if (_hits[i].transform.GetComponentInParent<LeakSpot>()) continue;
                return false;
            }
            return true;
        }

        /// <summary>Too close to another leak (keeps them apart so each is easy to click).</summary>
        bool Crowded(Vector3 p)
        {
            foreach (var s in _spots.Values)
                if (s && !s.Sealing && Vector3.Distance(s.transform.position, p) < 0.8f) return true;
            return false;
        }

        void SyncLeaks()
        {
            var alive = new HashSet<int>();
            if (_health)
                foreach (var leak in _health.Leaks)
                {
                    alive.Add(leak.id);
                    if (_spots.ContainsKey(leak.id)) continue;
                    if (!Locate(leak, out var pos, out var n)) continue;
                    var go = new GameObject($"Leak{leak.id}");
                    go.transform.SetParent(_cabin, true);
                    go.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(n));
                    var spot = go.AddComponent<LeakSpot>();
                    spot.leakId = leak.id;
                    _spots[leak.id] = spot;
                }
            var gone = new List<int>();
            foreach (var kv in _spots) if (!alive.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (int id in gone)
            {
                if (_spots[id]) _spots[id].Seal();
                _spots.Remove(id);
            }
        }

        public int LeakCount => _spots.Count;

        // ================================================================ input

        void Update()
        {
            Hovered = null;
            foreach (var s in _spots.Values) if (s) s.Highlight = false;
            if (!_cam || GameOverScreen.IsShown) return;
            var mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 sp = mouse.position.ReadValue();
            if (!_cam.pixelRect.Contains(sp) || (_inputBlocked != null && _inputBlocked())) return;

            Ray ray = _cam.ScreenPointToRay(sp);
            int n = Physics.RaycastNonAlloc(ray, _hits, 50f, ~0, QueryTriggerInteraction.Collide);
            RaycastHit best = default;
            bool found = false;
            for (int i = 0; i < n; i++)
            {
                if (_crew && _hits[i].transform.IsChildOf(_crew.transform)) continue;
                if (!found || _hits[i].distance < best.distance) { best = _hits[i]; found = true; }
            }
            if (!found) return;

            var leak = best.collider.GetComponentInParent<LeakSpot>();
            if (leak && !leak.Sealing)
            {
                Hovered = leak;
                leak.Highlight = true;
            }
            if (!mouse.leftButton.wasPressedThisFrame) return;

            if (Hovered)
            {
                Vector3 stand = Hovered.transform.position + Flat(Hovered.transform.forward).normalized * 0.55f;
                stand.y = _hasFloor ? _floor.max.y : stand.y - 1f;
                _crew.RepairAt(ClampToFloor(stand), Hovered);
            }
            else if (best.normal.y > 0.7f) // floor: just walk there
                _crew.WalkTo(ClampToFloor(best.point));
        }

        void OnRepairFinished(LeakSpot spot)
        {
            if (!spot || !_health) return;
            if (_health.Repair(spot.leakId)) RoverHud.Notify("Leak sealed");
        }

        Vector3 ClampToFloor(Vector3 p)
        {
            if (!_hasFloor) return p;
            const float m = 0.35f;
            p.x = Mathf.Clamp(p.x, _floor.min.x + m, _floor.max.x - m);
            p.z = Mathf.Clamp(p.z, _floor.min.z + m, _floor.max.z - m);
            p.y = _floor.max.y;
            return p;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v.sqrMagnitude > 0.0001f ? v : Vector3.forward; }
    }

    /// <summary>A hole in the cabin wall: dark crack, glowing rim, steam and sparks. Clickable (has a collider).</summary>
    public class LeakSpot : MonoBehaviour
    {
        public int leakId;
        public bool Highlight { get; set; }
        public bool Sealing { get; private set; }
        /// <summary>0..1 while the crew works on it (calms the steam down).</summary>
        public float Progress { get; set; }

        Transform _visual;
        Light _light;
        ParticleSystem _steam, _sparks;
        Material _rimMat;
        readonly List<Object> _owned = new List<Object>();
        float _t, _seal;
        static Texture2D _soft;

        void Start()
        {
            _visual = new GameObject("Visual").transform;
            _visual.SetParent(transform, false);

            var dark = Mat(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"), new Color(0.02f, 0.02f, 0.02f));
            _rimMat = Mat(Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color"), new Color(1f, 0.45f, 0.1f));

            // Glowing rim (slightly bigger, behind) + dark hole on top.
            Disc(_rimMat, 0.42f, 0.004f);
            Disc(dark, 0.3f, 0.01f);
            // Cracks radiating out along the wall.
            int cracks = Random.Range(4, 7);
            for (int i = 0; i < cracks; i++)
            {
                var c = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(c.GetComponent<Collider>());
                c.transform.SetParent(_visual, false);
                float ang = i * 360f / cracks + Random.Range(-20f, 20f);
                float len = Random.Range(0.18f, 0.38f);
                c.transform.localRotation = Quaternion.Euler(0f, 0f, ang);
                c.transform.localPosition = c.transform.localRotation * new Vector3(0.15f + len * 0.5f, 0f, 0f) + new Vector3(0, 0, 0.008f);
                c.transform.localScale = new Vector3(len, Random.Range(0.015f, 0.03f), 0.01f);
                c.GetComponent<Renderer>().sharedMaterial = dark;
            }

            var col = gameObject.AddComponent<SphereCollider>();
            col.radius = 0.4f;
            col.isTrigger = true;

            var lg = new GameObject("Light");
            lg.transform.SetParent(transform, false);
            lg.transform.localPosition = new Vector3(0f, 0f, 0.3f);
            _light = lg.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = new Color(1f, 0.4f, 0.15f);
            _light.range = 2.2f;
            _light.shadows = LightShadows.None;

            _steam = Particles("Steam", new Color(0.85f, 0.88f, 0.9f, 0.45f), 30f, new Vector2(0.8f, 1.6f), new Vector2(0.6f, 1.1f), new Vector2(0.12f, 0.32f), -0.06f, 14f, 1.6f);
            _sparks = Particles("Sparks", new Color(1f, 0.65f, 0.2f, 1f), 0f, new Vector2(2f, 4f), new Vector2(0.25f, 0.5f), new Vector2(0.025f, 0.05f), 1f, 35f, 0f);
            var em = _sparks.emission;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 6, 12, -1, 0.9f) { probability = 0.6f } });
        }

        void Disc(Material m, float d, float depth)
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(g.GetComponent<Collider>());
            g.transform.SetParent(_visual, false);
            g.transform.localRotation = Quaternion.Euler(90f, 0f, 0f); // cylinder axis → wall normal
            g.transform.localPosition = new Vector3(0f, 0f, depth);
            g.transform.localScale = new Vector3(d, 0.003f, d * Random.Range(0.6f, 0.85f));
            g.GetComponent<Renderer>().sharedMaterial = m;
        }

        ParticleSystem Particles(string name, Color c, float rate, Vector2 speed, Vector2 life, Vector2 size, float gravity, float angle, float growth)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, 0.03f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startColor = c;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;
            var em = ps.emission;
            em.rateOverTime = rate;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = angle;
            shape.radius = 0.05f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            if (growth > 0f)
            {
                var sol = ps.sizeOverLifetime;
                sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, growth));
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit") ?? Shader.Find("Particles/Standard Unlit") ?? Shader.Find("Sprites/Default");
            var m = new Material(sh) { name = "Leak" + name };
            var tex = SoftDot();
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            m.mainTexture = tex;
            if (m.HasProperty("_Surface")) // URP particles: transparent, alpha blended
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            }
            r.sharedMaterial = m;
            _owned.Add(m);
            ps.Play();
            return ps;
        }

        static Texture2D SoftDot()
        {
            if (_soft) return _soft;
            const int n = 32;
            _soft = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "SoftDot" };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f)) / (n * 0.5f);
                byte a = (byte)(Mathf.Clamp01(1f - d) * Mathf.Clamp01(1f - d) * 255f);
                px[y * n + x] = new Color32(255, 255, 255, a);
            }
            _soft.SetPixels32(px);
            _soft.Apply();
            return _soft;
        }

        Material Mat(Shader s, Color c)
        {
            var m = new Material(s) { name = "Leak", color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            _owned.Add(m);
            return m;
        }

        void Update()
        {
            if (!_visual) return;
            _t += Time.deltaTime;
            float calm = 1f - Progress;
            if (Sealing)
            {
                _seal += Time.deltaTime / 0.5f;
                _visual.localScale = Vector3.one * Mathf.Max(0f, 1f - _seal);
                if (_light) _light.intensity = 3f * Mathf.Max(0f, 1f - _seal);
                if (_seal >= 1f) Destroy(gameObject);
                return;
            }
            float flicker = 0.7f + 0.3f * Mathf.PerlinNoise(_t * 8f, leakId);
            float hl = Highlight ? 0.5f + 0.5f * Mathf.Sin(_t * 12f) : 0f;
            if (_light) _light.intensity = (1.2f + 1.6f * flicker) * Mathf.Lerp(0.3f, 1f, calm) + hl * 2.5f;
            _visual.localScale = Vector3.one * (1f + 0.12f * hl);
            var rim = Color.Lerp(new Color(1f, 0.45f, 0.1f) * flicker, Color.white, hl * 0.6f);
            _rimMat.color = rim;
            if (_rimMat.HasProperty("_BaseColor")) _rimMat.SetColor("_BaseColor", rim);
            if (_steam) { var em = _steam.emission; em.rateOverTime = 30f * Mathf.Lerp(0.15f, 1f, calm); }
        }

        /// <summary>Repaired: shrink away and stop steaming.</summary>
        public void Seal()
        {
            if (Sealing) return;
            Sealing = true;
            var col = GetComponent<Collider>();
            if (col) col.enabled = false;
            if (_steam) _steam.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (_sparks) _sparks.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        void OnDestroy()
        {
            foreach (var o in _owned) if (o) Destroy(o);
        }
    }
}
