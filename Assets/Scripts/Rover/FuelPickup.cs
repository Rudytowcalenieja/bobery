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

        void BuildPlaceholder()
        {
            var root = new GameObject("Canister").transform;
            root.SetParent(transform, false);
            root.localPosition = Vector3.up * 1.2f;
            _visual = root;

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(root, false);
            body.transform.localScale = new Vector3(1.2f, 1.6f, 0.7f);
            var cap = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(cap.GetComponent<Collider>());
            cap.transform.SetParent(root, false);
            cap.transform.localPosition = new Vector3(0.3f, 0.95f, 0f);
            cap.transform.localScale = new Vector3(0.3f, 0.15f, 0.3f);

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var mat = new Material(shader) { color = new Color(0.95f, 0.95f, 0.95f) };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.95f, 0.95f, 0.95f));
            body.GetComponent<Renderer>().sharedMaterial = mat;
            cap.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }
}
