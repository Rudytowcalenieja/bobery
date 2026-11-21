using UnityEngine;

namespace Bobery.Rover
{
    /// <summary>
    /// Fuel lying on the map. When the rover walks close enough, the fuel goes into the
    /// player's inventory (refuel the tank from inside the rover) — or straight into the tank
    /// if <see cref="intoTank"/> is ticked.
    /// Drop it on any object; with no renderer of its own it shows a simple placeholder canister.
    /// </summary>
    public class FuelPickup : MonoBehaviour
    {
        [Min(0f)] public float litres = 25f;
        [Tooltip("Pick up when the rover's centre is this close (metres, flat). 0 = use the rover's size.")]
        public float pickupRadius = 0f;
        [Tooltip("Put the fuel straight into the rover's tank instead of the player's inventory.")]
        public bool intoTank = false;
        [Tooltip("Spin/bob the placeholder so it's easy to spot.")]
        public bool animate = true;

        RoverController _rover;
        FuelTank _tank;
        Transform _visual;
        Vector3 _basePos;

        void Start()
        {
            _rover = FindAnyObjectByType<RoverController>();
            if (_rover) _tank = _rover.GetComponent<FuelTank>();

            if (!GetComponentInChildren<Renderer>()) BuildPlaceholder();
            _basePos = _visual ? _visual.localPosition : Vector3.zero;
        }

        void Update()
        {
            if (_visual && animate)
            {
                _visual.localRotation = Quaternion.Euler(0f, Time.time * 60f, 0f);
                _visual.localPosition = _basePos + Vector3.up * (Mathf.Sin(Time.time * 2f) * 0.25f);
            }

            if (!_rover) return;
            float r = pickupRadius > 0f ? pickupRadius : RoverReach();
            Vector3 a = transform.position, b = _rover.transform.position;
            a.y = 0f; b.y = 0f;
            if ((a - b).sqrMagnitude > r * r) return;

            if (intoTank && _tank) _tank.AddDirect(litres);
            else PlayerInventory.Add(PlayerInventory.Fuel, litres);
            RoverHud.Notify($"+{litres:0.#} L fuel {(intoTank ? "in tank" : "collected")}");
            Destroy(gameObject);
        }

        float RoverReach()
        {
            var fit = _rover.GetComponent<RoverAutoFit>();
            if (fit && fit.IsFitted) return Mathf.Max(fit.WorldSize.x, fit.WorldSize.z) * 0.6f;
            return 4f;
        }

        // Shared between all canisters (never destroyed: a handful of materials for the whole game).
        static Material _paint, _metal, _brass;

        /// <summary>A classic red jerrycan built from primitives: pressed body with a welded seam, stamped X on
        /// both sides, three carry handles, a spout with a brass cap, and a warm point light so it's easy to spot.</summary>
        void BuildPlaceholder()
        {
            var root = new GameObject("Jerrycan").transform;
            root.SetParent(transform, false);
            root.localPosition = Vector3.up * 1.3f;
            _visual = root;

            var model = new GameObject("Model").transform;   // slight tilt so it doesn't look like a flat box
            model.SetParent(root, false);
            model.localRotation = Quaternion.Euler(0f, 0f, 8f);
            model.localScale = Vector3.one * 1.35f;

            Materials();
            const float W = 0.9f, H = 1.2f, D = 0.42f;

            // Body: a core box plus slightly inset slabs on each face, which bevels the edges.
            Part(PrimitiveType.Cube, model, Vector3.zero, new Vector3(W - 0.08f, H - 0.08f, D), _paint);
            Part(PrimitiveType.Cube, model, Vector3.zero, new Vector3(W, H - 0.16f, D - 0.06f), _paint);
            Part(PrimitiveType.Cube, model, Vector3.zero, new Vector3(W - 0.16f, H, D - 0.06f), _paint);
            // Welded seam running around the middle.
            Part(PrimitiveType.Cube, model, Vector3.zero, new Vector3(W + 0.03f, H - 0.12f, 0.05f), _metal);
            Part(PrimitiveType.Cube, model, Vector3.zero, new Vector3(W - 0.12f, H + 0.03f, 0.05f), _metal);

            // Stamped X on both faces (raised ribs).
            float diag = Mathf.Sqrt(W * W + H * H) * 0.62f;
            float ang = Mathf.Atan2(H, W) * Mathf.Rad2Deg;
            foreach (float side in new[] { 1f, -1f })
            {
                float z = side * (D * 0.5f + 0.005f);
                foreach (float a in new[] { ang, -ang })
                {
                    var rib = Part(PrimitiveType.Cube, model, new Vector3(0f, -0.04f, z), new Vector3(diag, 0.07f, 0.03f), _paint);
                    rib.localRotation = Quaternion.Euler(0f, 0f, a);
                }
                // Frame around the X.
                Part(PrimitiveType.Cube, model, new Vector3(0f, H * 0.36f, z), new Vector3(W * 0.78f, 0.05f, 0.025f), _paint);
                Part(PrimitiveType.Cube, model, new Vector3(0f, -H * 0.44f, z), new Vector3(W * 0.78f, 0.05f, 0.025f), _paint);
            }

            // Three carry handles on top (the classic triple handle), each a bar on two posts.
            float top = H * 0.5f;
            for (int i = -1; i <= 1; i++)
            {
                float x = i * 0.2f - 0.08f;
                Part(PrimitiveType.Cube, model, new Vector3(x, top + 0.17f, 0f), new Vector3(0.06f, 0.05f, D * 0.75f), _metal);
                Part(PrimitiveType.Cube, model, new Vector3(x, top + 0.08f, D * 0.32f), new Vector3(0.06f, 0.16f, 0.05f), _metal);
                Part(PrimitiveType.Cube, model, new Vector3(x, top + 0.08f, -D * 0.32f), new Vector3(0.06f, 0.16f, 0.05f), _metal);
            }

            // Spout on the front corner, tilted outward, with a brass cap and its locking lever.
            var spout = new GameObject("Spout").transform;
            spout.SetParent(model, false);
            spout.localPosition = new Vector3(W * 0.36f, top - 0.02f, 0f);
            spout.localRotation = Quaternion.Euler(0f, 0f, -35f);
            Part(PrimitiveType.Cylinder, spout, new Vector3(0f, 0.08f, 0f), new Vector3(0.16f, 0.09f, 0.16f), _metal);
            Part(PrimitiveType.Cylinder, spout, new Vector3(0f, 0.2f, 0f), new Vector3(0.2f, 0.05f, 0.2f), _brass);
            var lever = Part(PrimitiveType.Cube, spout, new Vector3(-0.1f, 0.2f, 0f), new Vector3(0.14f, 0.035f, 0.07f), _brass);
            lever.localRotation = Quaternion.Euler(0f, 0f, 20f);

            // Warm glow around the can so it's easy to spot at night.
            var lightGo = new GameObject("Glow");
            lightGo.transform.SetParent(root, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.55f, 0.15f);
            light.range = 5f;
            light.intensity = 1.5f;
            light.shadows = LightShadows.None;
        }

        static void Materials()
        {
            if (_paint) return;
            var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            _paint = Mat(lit, new Color(0.72f, 0.07f, 0.05f), 0.35f, 0.55f);
            _metal = Mat(lit, new Color(0.16f, 0.16f, 0.17f), 0.8f, 0.45f);
            _brass = Mat(lit, new Color(0.85f, 0.65f, 0.25f), 0.9f, 0.7f);
        }

        static Material Mat(Shader s, Color c, float metallic, float smoothness)
        {
            var m = new Material(s) { name = "Jerrycan", color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metallic);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
            return m;
        }

        static Transform Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            return go.transform;
        }
    }
}
