using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bobery.Rover
{
    /// <summary>
    /// The player's stock of charging beacons: how many exist, what one costs to drop, and which are deployed.
    /// Static so any script can query it; configured by <see cref="RoverMapPanel"/> when the map scene starts.
    /// </summary>
    public static class BeaconNetwork
    {
        /// <summary>Total beacons the player owns (deployed + in stock).</summary>
        public static int Capacity { get; private set; } = 2;
        /// <summary>Share (0..1) of the rover's tank capacity and battery capacity spent to drop a beacon.</summary>
        public static float FuelCostFraction { get; private set; } = 0.1f;
        public static float BatteryCostFraction { get; private set; } = 0f;
        public static float Radius { get; private set; } = 15f;
        public static float ChargePerSecond { get; private set; } = 4f;

        static readonly List<Beacon> _deployed = new List<Beacon>();
        public static IReadOnlyList<Beacon> Deployed => _deployed;
        /// <summary>Beacons lost for good.</summary>
        public static int Destroyed { get; private set; }
        /// <summary>Beacons ready to be dropped.</summary>
        public static int Available => Mathf.Max(0, Capacity - _deployed.Count - Destroyed);

        public static event Action Changed;

        // The rover's own tank and battery pay for the drop.
        static FuelTank _tank;
        static RoverBattery _battery;
        static void FindRover()
        {
            if (_tank && _battery) return;
            var rover = UnityEngine.Object.FindAnyObjectByType<RoverController>();
            if (!rover) return;
            _tank = rover.GetComponent<FuelTank>();
            _battery = rover.GetComponent<RoverBattery>();
        }

        public static float FuelCost { get { FindRover(); return _tank ? _tank.capacity * FuelCostFraction : 0f; } }
        public static float BatteryCost { get { FindRover(); return _battery ? _battery.capacity * BatteryCostFraction : 0f; } }
        public static bool HasFuel { get { FindRover(); return !_tank || _tank.Fuel + 0.0001f >= FuelCost; } }
        public static bool HasBattery { get { FindRover(); return !_battery || _battery.Charge + 0.0001f >= BatteryCost; } }
        public static bool CanAfford => HasFuel && HasBattery;
        public static string CostText => BatteryCostFraction > 0f
            ? $"FUEL -{FuelCostFraction * 100f:0}%  BATT -{BatteryCostFraction * 100f:0}%"
            : $"FUEL -{FuelCostFraction * 100f:0}%";
        /// <summary>Why the player can't afford a beacon right now, or null.</summary>
        public static string CostProblem =>
            !HasFuel && !HasBattery ? $"Not enough fuel and battery: need {CostText}"
            : !HasFuel ? $"Not enough fuel in the tank: need {FuelCostFraction * 100f:0}% ({FuelCost:0.#} L)"
            : !HasBattery ? $"Not enough battery: need {BatteryCostFraction * 100f:0}%"
            : null;

        public static float Cooldown { get; private set; } = 120f;
        /// <summary>Seconds it takes to pick a beacon up (the rover has to stay in its zone). 0 = instant.</summary>
        public static float PickUpTime { get; set; } = 10f;
        /// <summary>Enemy shots a beacon survives.</summary>
        public static int MaxHits { get; set; } = 5;
        /// <summary>Seconds the rover is busy deploying a beacon (nothing else can be done meanwhile).</summary>
        public static float CastTime { get; set; } = 3f;
        /// <summary>3D model used for placed beacons (Models/beacon). Null → a primitive stand-in is built.</summary>
        public static GameObject Model { get; set; }

        public static void Configure(int capacity, float fuelCostFraction, float batteryCostFraction, float radius, float chargePerSecond, float cooldown)
        {
            _tank = null; _battery = null; // rover may have been rebuilt (scene reload)
            Cooldown = Mathf.Max(0f, cooldown);
            Capacity = Mathf.Max(0, capacity);
            FuelCostFraction = Mathf.Clamp01(fuelCostFraction);
            BatteryCostFraction = Mathf.Clamp01(batteryCostFraction);
            Radius = Mathf.Max(1f, radius);
            ChargePerSecond = Mathf.Max(0f, chargePerSecond);
            _deployed.RemoveAll(b => !b);
            Destroyed = 0;
            Changed?.Invoke();
        }

        /// <summary>Spends the cost and drops a beacon from the sky onto <paramref name="groundPoint"/>.</summary>
        public static bool TryPlace(Vector3 groundPoint, out Beacon beacon, out string error)
        {
            beacon = null;
            if (Available <= 0) { error = "No beacons left"; return false; }
            if (!CanAfford) { error = CostProblem; return false; }

            float fuel = FuelCost, charge = BatteryCost;
            if (_battery) _battery.Drain(charge);
            if (_tank) _tank.Drain(fuel);
            var go = new GameObject("Beacon");
            go.transform.position = groundPoint;
            beacon = go.AddComponent<Beacon>();
            beacon.radius = Radius;
            beacon.chargePerSecond = ChargePerSecond;
            beacon.cooldown = Cooldown;
            beacon.pickUpTime = PickUpTime;
            beacon.maxHits = MaxHits;
            _deployed.Add(beacon);
            Changed?.Invoke();
            error = null;
            return true;
        }

        /// <summary>Starts the timed pick-up of <paramref name="b"/>; it returns to stock when the countdown ends.</summary>
        public static void BeginPickUp(Beacon b)
        {
            if (!b || !_deployed.Contains(b)) return;
            if (b.BeginPickUp()) Changed?.Invoke();
        }

        public static void CancelPickUp(Beacon b)
        {
            if (!b || !b.IsPickingUp) return;
            b.CancelPickUp("Beacon pick-up cancelled");
            Changed?.Invoke();
        }

        /// <summary>Recovers a beacon back into stock right away (no refund of its cost).</summary>
        public static void PickUp(Beacon b)
        {
            if (!b || !_deployed.Remove(b)) return;
            b.Retract();
            Changed?.Invoke();
        }

        /// <summary>Destroys a beacon: it's gone for good.</summary>
        public static void DestroyBeacon(Beacon b)
        {
            if (!b || !_deployed.Remove(b)) return;
            Destroyed++;
            b.Explode();
            Changed?.Invoke();
        }

        internal static void Forget(Beacon b)
        {
            if (_deployed.Remove(b)) Changed?.Invoke();
        }

        /// <summary>The beacon whose zone contains <paramref name="worldPoint"/> (nearest first), or null.</summary>
        public static Beacon ZoneAt(Vector3 worldPoint)
        {
            Beacon best = null;
            float bestD = float.MaxValue;
            foreach (var b in _deployed)
            {
                if (!b || !b.IsActive) continue;
                float d = b.FlatDistance(worldPoint);
                if (d <= b.radius && d < bestD) { best = b; bestD = d; }
            }
            return best;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            _deployed.Clear();
            Destroyed = 0;
            Capacity = 2;
            MaxHits = 5;
            CastTime = 3f;
            Changed = null;
            Model = null;
        }
    }

    /// <summary>
    /// A charging beacon. Falls from the sky, pins itself into the ground and projects a charging zone
    /// (transparent disc hugging the terrain). While the rover stands inside the zone its battery recharges.
    /// Uses the Models/beacon model (<see cref="BeaconNetwork.Model"/>), or primitives if none is assigned. Create through <see cref="BeaconNetwork.TryPlace"/>.
    /// </summary>
    public class Beacon : MonoBehaviour
    {
        public float radius = 15f;
        public float chargePerSecond = 4f;
        public float dropHeight = 70f;
        public float dropTime = 1.1f;
        public Color color = Color.white;
        [Tooltip("Height the beacon model is scaled to (metres). 0 keeps the model's own size.")]
        public float modelHeight = 4.5f;

        public enum State { Falling, Active, Retracting, Exploding }
        public State Phase { get; private set; } = State.Falling;
        public bool IsActive => Phase == State.Active;
        /// <summary>0..1 while the zone opens after landing.</summary>
        public float ZoneOpen { get; private set; }
        public bool RoverInside { get; private set; }

        [Tooltip("Seconds the beacon needs after a charging session before it can charge again.")]
        public float cooldown = 120f;
        public float CooldownLeft { get; private set; }
        public bool IsCoolingDown => CooldownLeft > 0f;

        [Tooltip("Seconds it takes to pick the beacon up. The rover must stay inside the zone meanwhile.")]
        public float pickUpTime = 10f;
        public float PickUpLeft { get; private set; }
        public bool IsPickingUp => PickUpLeft > 0f;

        /// <summary>Starts the pick-up countdown (instant if <see cref="pickUpTime"/> is 0). False if it can't start.</summary>
        public bool BeginPickUp()
        {
            if (!IsActive || IsPickingUp) return false;
            if (pickUpTime <= 0f) { BeaconNetwork.PickUp(this); return true; }
            PickUpLeft = pickUpTime;
            RoverHud.Notify($"Picking up beacon: {Clock(PickUpLeft)} · stay in the zone");
            return true;
        }

        public void CancelPickUp(string why)
        {
            if (!IsPickingUp) return;
            PickUpLeft = 0f;
            RoverHud.Notify(why);
        }
        [Tooltip("Enemy shots this beacon survives.")]
        public int maxHits = 5;
        public int Hits { get; private set; }
        public int HitsLeft => Mathf.Max(0, maxHits - Hits);
        public bool IsDamaged => Hits > 0;
        float _hitFlash;

        /// <summary>An enemy shot landed. The beacon is destroyed for good after <see cref="maxHits"/> hits.</summary>
        public void TakeHit()
        {
            if (!IsActive) return;
            Hits++;
            _hitFlash = 1f;
            if (Hits >= maxHits)
            {
                RoverHud.Notify("Beacon destroyed by enemy fire");
                BeaconNetwork.DestroyBeacon(this);
                return;
            }
            RoverHud.Notify($"Beacon under attack: {HitsLeft}/{maxHits} HP");
        }

        float _delivered;
        TextMesh _timer, _hp;

        float PinDepth = 0.6f; // how far the body sinks into the ground on landing
        const int Rings = 10, Segments = 72;

        RoverController _rover;
        RoverBattery _battery;
        Transform _body, _head, _zone, _edge, _pulse;
        Light _light;
        Material _bodyMat, _headMat, _zoneMat, _edgeMat, _pulseMat;
        Mesh _zoneMesh, _edgeMesh, _pulseMesh;
        float _t;
        Vector3 _ground;
        readonly RaycastHit[] _hits = new RaycastHit[8];
        readonly List<Debris> _debris = new List<Debris>();
        readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();

        struct Debris { public Transform t; public Vector3 v, spin; }

        void Awake()
        {
            _rover = FindAnyObjectByType<RoverController>();
            if (_rover) _battery = _rover.GetComponent<RoverBattery>();
        }

        void Start()
        {
            _ground = transform.position;
            Build();
            HideFromMapCamera();
            _body.localPosition = Vector3.up * dropHeight;
        }

        void OnDestroy()
        {
            BeaconNetwork.Forget(this);
            foreach (var o in new UnityEngine.Object[] { _bodyMat, _headMat, _zoneMat, _edgeMat, _pulseMat, _zoneMesh, _edgeMesh, _pulseMesh })
                if (o) Destroy(o);
            foreach (var o in _owned) if (o) Destroy(o);
        }

        public float FlatDistance(Vector3 p)
        {
            Vector3 a = transform.position; a.y = 0f; p.y = 0f;
            return Vector3.Distance(a, p);
        }

        // ================================================================ lifecycle

        void Update()
        {
            _t += Time.deltaTime;
            switch (Phase)
            {
                case State.Falling: UpdateFalling(); break;
                case State.Active: UpdateActive(); break;
                case State.Retracting: UpdateRetracting(); break;
                case State.Exploding: UpdateExploding(); break;
            }
        }

        void UpdateFalling()
        {
            float k = Mathf.Clamp01(_t / dropTime);
            // accelerate like it's falling, a bit of spin on the way down
            _body.localPosition = Vector3.up * Mathf.Lerp(dropHeight, -PinDepth, k * k);
            _body.localRotation = Quaternion.Euler(0f, (1f - k) * 540f, 0f);
            SetHeadGlow(0.6f + 0.4f * Mathf.Sin(_t * 30f));
            if (k < 1f) return;

            Phase = State.Active;
            _t = 0f;
            _body.localRotation = Quaternion.identity;
            SpawnDebris(10, 4f, 0.25f, new Color(0.5f, 0.5f, 0.5f)); // dirt kicked up on impact
        }

        void UpdateActive()
        {
            // Impact squash, then settle.
            float s = 1f + 0.25f * Mathf.Exp(-_t * 8f) * Mathf.Sin(_t * 30f);
            _body.localScale = new Vector3(1f / Mathf.Sqrt(s), s, 1f / Mathf.Sqrt(s));

            // Zone opens out from the pin.
            ZoneOpen = Mathf.Clamp01(_t / 0.7f);
            float e = 1f - Mathf.Pow(1f - ZoneOpen, 3f);
            if (IsPickingUp && pickUpTime > 0f) e *= Mathf.Lerp(0.25f, 1f, PickUpLeft / pickUpTime);
            _zone.localScale = _edge.localScale = new Vector3(e, 1f, e);

            float breathe = 0.5f + 0.5f * Mathf.Sin(Time.time * 2.2f);

            // Charging pulses run outward while the rover is inside.
            bool inside = _rover && FlatDistance(_rover.transform.position) <= radius;
            if (inside && !RoverInside)
                RoverHud.Notify(IsCoolingDown ? $"Beacon cooling down: {Clock(CooldownLeft)}"
                              : _battery && _battery.Fraction < 0.999f ? "Beacon in range: charging" : "Beacon in range");
            RoverInside = inside;

            // Timed pick-up: the rover has to wait in the zone; the beacon stops charging meanwhile.
            if (IsPickingUp)
            {
                if (!inside) CancelPickUp("Beacon pick-up cancelled: rover left the zone");
                else
                {
                    PickUpLeft = Mathf.Max(0f, PickUpLeft - Time.deltaTime);
                    if (!IsPickingUp)
                    {
                        RoverHud.Notify("Beacon recovered");
                        BeaconNetwork.PickUp(this);
                        return;
                    }
                }
            }

            // Cooldown: after a charging session the beacon needs time before it can charge again.
            if (IsCoolingDown)
            {
                CooldownLeft = Mathf.Max(0f, CooldownLeft - Time.deltaTime);
                if (!IsCoolingDown) RoverHud.Notify("Beacon ready");
            }

            bool charging = false;
            if (!IsCoolingDown && !IsPickingUp && inside && _battery && _battery.Fraction < 0.999f)
            {
                _delivered += _battery.Recharge(chargePerSecond * Time.deltaTime);
                charging = true;
            }
            // Session over (rover left, or battery full) → start the cooldown.
            if (!charging && _delivered > 0.01f)
            {
                _delivered = 0f;
                if (cooldown > 0f)
                {
                    CooldownLeft = cooldown;
                    RoverHud.Notify($"Beacon cooling down: {Clock(CooldownLeft)}");
                }
            }

            float dim = IsCoolingDown ? 0.35f : 1f;
            SetAlpha(_zoneMat, (0.10f + 0.04f * breathe) * e * dim);
            SetAlpha(_edgeMat, (0.55f + 0.25f * breathe) * e * (IsCoolingDown ? 0.5f : 1f));

            float p = (Time.time * (charging ? 0.8f : 0.35f)) % 1f;
            _pulse.localScale = new Vector3(p, 1f, p) * e;
            SetAlpha(_pulseMat, IsCoolingDown ? 0f : (charging ? 0.6f : 0.25f) * (1f - p) * e);

            SetHeadGlow(IsPickingUp ? 0.5f + 0.5f * Mathf.Sin(Time.time * 14f) : IsCoolingDown ? 0.1f : charging ? 0.7f + 0.3f * Mathf.Sin(Time.time * 10f) : 0.55f + 0.2f * breathe);
            _head.localRotation = Quaternion.Euler(0f, Time.time * (IsPickingUp ? 400f : charging ? 240f : IsCoolingDown ? 10f : 60f), 0f);

            UpdateTimerLabel();
            UpdateHpLabel();
        }

        /// <summary>Hit points floating above the beacon once it has taken damage; the head flashes red on each hit.</summary>
        void UpdateHpLabel()
        {
            if (_hitFlash > 0f)
            {
                _hitFlash = Mathf.Max(0f, _hitFlash - Time.deltaTime * 2.5f);
                SetColor(_headMat, Color.Lerp(_headMat.color, new Color(1f, 0.2f, 0.15f), _hitFlash));
            }
            if (!_hp) return;
            bool show = IsDamaged && Phase == State.Active;
            if (_hp.gameObject.activeSelf != show) _hp.gameObject.SetActive(show);
            if (!show) return;
            _hp.text = new string('|', HitsLeft) + "<color=#40404080>" + new string('|', Hits) + "</color>";
            var cam = Camera.main;
            if (cam)
            {
                _hp.transform.rotation = Quaternion.LookRotation(_hp.transform.position - cam.transform.position, Vector3.up);
                float d = Vector3.Distance(cam.transform.position, _hp.transform.position);
                _hp.transform.localScale = Vector3.one * Mathf.Clamp(d / 30f, 0.6f, 3f);
            }
        }

        /// <summary>Countdown floating above the beacon, facing the camera. Hidden when the beacon is ready.</summary>
        void UpdateTimerLabel()
        {
            if (!_timer) return;
            bool show = (IsCoolingDown || IsPickingUp) && Phase == State.Active;
            if (_timer.gameObject.activeSelf != show) _timer.gameObject.SetActive(show);
            if (!show) return;
            // Pick-up countdown wins over the charging cooldown while both run.
            _timer.text = IsPickingUp ? $"PICK-UP {Clock(PickUpLeft)}" : Clock(CooldownLeft);
            var cam = Camera.main;
            if (cam)
            {
                _timer.transform.rotation = Quaternion.LookRotation(_timer.transform.position - cam.transform.position, Vector3.up);
                // keep it readable from the far-away chase camera
                float d = Vector3.Distance(cam.transform.position, _timer.transform.position);
                _timer.transform.localScale = Vector3.one * Mathf.Clamp(d / 30f, 0.6f, 3f);
            }
        }

        public static string Clock(float secs)
        {
            int s = Mathf.CeilToInt(secs);
            return $"{s / 60}:{s % 60:00}";
        }

        void UpdateRetracting()
        {
            float k = Mathf.Clamp01(_t / 0.9f);
            float close = 1f - Mathf.Clamp01(_t / 0.35f);
            _zone.localScale = _edge.localScale = _pulse.localScale = new Vector3(close, 1f, close);
            SetAlpha(_zoneMat, 0.12f * close);
            SetAlpha(_edgeMat, 0.7f * close);
            SetAlpha(_pulseMat, 0f);
            if (_timer) _timer.gameObject.SetActive(false);
            if (_hp) _hp.gameObject.SetActive(false);
            _body.localPosition = Vector3.up * Mathf.Lerp(-PinDepth, dropHeight, k * k);
            _body.localRotation = Quaternion.Euler(0f, k * 540f, 0f);
            SetHeadGlow(1f);
            if (k >= 1f) Destroy(gameObject);
        }

        void UpdateExploding()
        {
            float fade = 1f - Mathf.Clamp01(_t / 0.4f);
            _zone.localScale = _edge.localScale = _pulse.localScale = new Vector3(1f + (1f - fade) * 0.3f, 1f, 1f + (1f - fade) * 0.3f);
            SetAlpha(_zoneMat, 0.25f * fade);
            SetAlpha(_edgeMat, 0.9f * fade);
            SetAlpha(_pulseMat, 0f);
            if (_light) _light.intensity = 8f * fade;

            for (int i = 0; i < _debris.Count; i++)
            {
                var d = _debris[i];
                if (!d.t) continue;
                d.v += Physics.gravity * Time.deltaTime;
                d.t.position += d.v * Time.deltaTime;
                d.t.Rotate(d.spin * Time.deltaTime, Space.Self);
                d.t.localScale *= 1f - Time.deltaTime * 0.6f;
                _debris[i] = d;
            }
            if (_t > 2.2f) Destroy(gameObject);
        }

        /// <summary>Called by the network: lift off and disappear.</summary>
        public void Retract()
        {
            if (Phase == State.Retracting || Phase == State.Exploding) return;
            Phase = State.Retracting;
            _t = 0f;
            _body.localScale = Vector3.one;
        }

        /// <summary>Called by the network: blow apart.</summary>
        public void Explode()
        {
            if (Phase == State.Exploding) return;
            Phase = State.Exploding;
            _t = 0f;
            if (_timer) _timer.gameObject.SetActive(false);
            if (_hp) _hp.gameObject.SetActive(false);
            Vector3 at = _body.position;
            // The beacon itself becomes debris.
            foreach (Transform part in _body.GetComponentsInChildren<Transform>())
            {
                if (part == _body || !part.GetComponent<Renderer>()) continue;
                var v = (UnityEngine.Random.onUnitSphere + Vector3.up * 1.2f).normalized * UnityEngine.Random.Range(6f, 14f);
                _debris.Add(new Debris { t = part, v = v, spin = UnityEngine.Random.insideUnitSphere * 720f });
            }
            foreach (var d in _debris) d.t.SetParent(transform, true);
            SpawnDebris(14, 10f, 0.35f, new Color(0.25f, 0.25f, 0.25f), at);
            SetHeadGlow(1f);
        }

        void SpawnDebris(int count, float speed, float size, Color c, Vector3? at = null)
        {
            var mat = MakeMaterial(c, lit: true);
            _owned.Add(mat);
            for (int i = 0; i < count; i++)
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Destroy(cube.GetComponent<Collider>());
                cube.GetComponent<Renderer>().sharedMaterial = mat;
                cube.transform.SetParent(transform, false);
                cube.transform.position = (at ?? _ground) + UnityEngine.Random.insideUnitSphere * 0.4f;
                cube.transform.localScale = Vector3.one * size * UnityEngine.Random.Range(0.6f, 1.4f);
                var dir = UnityEngine.Random.insideUnitSphere; dir.y = Mathf.Abs(dir.y) + 0.8f;
                _debris.Add(new Debris { t = cube.transform, v = dir.normalized * speed * UnityEngine.Random.Range(0.5f, 1f), spin = UnityEngine.Random.insideUnitSphere * 500f });
                Destroy(cube, 1.2f);
            }
            HideFromMapCamera();
            if (Phase != State.Exploding) StartCoroutine(AnimateDebris(1.2f));
        }

        System.Collections.IEnumerator AnimateDebris(float time)
        {
            for (float t = 0f; t < time; t += Time.deltaTime)
            {
                if (Phase == State.Exploding) yield break; // UpdateExploding takes over
                for (int i = 0; i < _debris.Count; i++)
                {
                    var d = _debris[i];
                    if (!d.t) continue;
                    d.v += Physics.gravity * Time.deltaTime;
                    d.t.position += d.v * Time.deltaTime;
                    d.t.Rotate(d.spin * Time.deltaTime, Space.Self);
                    _debris[i] = d;
                }
                yield return null;
            }
            _debris.RemoveAll(d => !d.t);
        }

        // ================================================================ build

        void Build()
        {
            _bodyMat = MakeMaterial(new Color(0.16f, 0.16f, 0.17f), lit: true);
            _headMat = MakeMaterial(color, lit: false);
            _zoneMat = MakeMaterial(new Color(color.r, color.g, color.b, 0f), lit: false, transparent: true);
            _edgeMat = MakeMaterial(new Color(color.r, color.g, color.b, 0f), lit: false, transparent: true);
            _pulseMat = MakeMaterial(new Color(color.r, color.g, color.b, 0f), lit: false, transparent: true);

            // ---- body (pivot at ground level; the spike goes below)
            _body = new GameObject("Body").transform;
            _body.SetParent(transform, false);

            float top = BeaconNetwork.Model ? BuildFromModel(BeaconNetwork.Model) : BuildPrimitive();
            if (BeaconNetwork.Model) PinDepth = 0.15f; // the model has no spike: just settle it into the dirt

            var lightGo = new GameObject("Light");
            lightGo.transform.SetParent(_head, false);
            _light = lightGo.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.range = 10f;
            _light.color = color;
            _light.shadows = LightShadows.None;

            // ---- zone (meshes are in local space around the pin, conformed to the terrain once)
            _zoneMesh = BuildDisc(radius);
            _edgeMesh = BuildRing(radius * 0.97f, radius);
            _pulseMesh = BuildRing(radius * 0.9f, radius);
            _zone = MeshChild("Zone", _zoneMesh, _zoneMat);
            _edge = MeshChild("Edge", _edgeMesh, _edgeMat);
            _pulse = MeshChild("Pulse", _pulseMesh, _pulseMat);

            // ---- cooldown countdown, floating above the head
            var timerGo = new GameObject("CooldownTimer");
            timerGo.transform.SetParent(transform, false);
            timerGo.transform.localPosition = new Vector3(0f, top + 1.6f, 0f);
            _timer = timerGo.AddComponent<TextMesh>();
            _timer.font = RoverUI.Mono;
            timerGo.GetComponent<MeshRenderer>().sharedMaterial = _timer.font.material;
            _timer.fontSize = 64;
            _timer.characterSize = 0.06f;
            _timer.anchor = TextAnchor.LowerCenter;
            _timer.alignment = TextAlignment.Center;
            _timer.fontStyle = FontStyle.Bold;
            _timer.color = color;
            timerGo.SetActive(false);

            // ---- hit points, just under the countdown
            var hpGo = new GameObject("HitPoints");
            hpGo.transform.SetParent(transform, false);
            hpGo.transform.localPosition = new Vector3(0f, top + 0.9f, 0f);
            _hp = hpGo.AddComponent<TextMesh>();
            _hp.font = RoverUI.Mono;
            hpGo.GetComponent<MeshRenderer>().sharedMaterial = _hp.font.material;
            _hp.fontSize = 64;
            _hp.characterSize = 0.07f;
            _hp.anchor = TextAnchor.LowerCenter;
            _hp.alignment = TextAlignment.Center;
            _hp.fontStyle = FontStyle.Bold;
            _hp.richText = true;
            _hp.color = new Color(1f, 0.32f, 0.32f);
            hpGo.SetActive(false);
            _zone.localScale = _edge.localScale = _pulse.localScale = new Vector3(0f, 1f, 0f);
        }

        /// <summary>Instantiates the beacon model under the body, sized to <see cref="modelHeight"/>.
        /// Its sphere becomes the spinning head. Returns the model's top height.</summary>
        float BuildFromModel(GameObject prefab)
        {
            var model = Instantiate(prefab, _body, false);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) Destroy(c); // the rover drives through it
            foreach (var a in model.GetComponentsInChildren<Animator>(true)) Destroy(a);

            var renderers = model.GetComponentsInChildren<Renderer>(true);
            Bounds b = LocalBounds(renderers);
            if (modelHeight > 0f && b.size.y > 0.001f)
            {
                model.transform.localScale *= modelHeight / b.size.y;
                b = LocalBounds(renderers);
            }
            // stand it on the pin: bottom at ground level, centred on the pin
            model.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
            b = LocalBounds(renderers);

            // The sphere on top spins: give it a pivot at its own centre.
            Transform sphere = null;
            foreach (var r in renderers)
                if (r.name.IndexOf("sphere", StringComparison.OrdinalIgnoreCase) >= 0) { sphere = r.transform; break; }
            _head = new GameObject("Head").transform;
            _head.SetParent(_body, false);
            if (sphere)
            {
                var sb = sphere.GetComponent<Renderer>().bounds;
                _head.position = sb.center;
                sphere.SetParent(_head, true);
            }
            else _head.localPosition = new Vector3(0f, b.max.y, 0f);
            return b.max.y;
        }

        /// <summary>Bounds of <paramref name="renderers"/> in this beacon's local space (body at rest).</summary>
        Bounds LocalBounds(Renderer[] renderers)
        {
            bool any = false;
            var bounds = new Bounds();
            foreach (var r in renderers)
            {
                var wb = r.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = wb.center + Vector3.Scale(wb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var local = _body.InverseTransformPoint(corner);
                    if (!any) { bounds = new Bounds(local, Vector3.zero); any = true; }
                    else bounds.Encapsulate(local);
                }
            }
            return bounds;
        }

        /// <summary>Stand-in built from primitives, used when no beacon model is assigned. Returns its top height.</summary>
        float BuildPrimitive()
        {
            Part(PrimitiveType.Cylinder, _body, new Vector3(0f, -0.4f, 0f), new Vector3(0.18f, 0.9f, 0.18f), _bodyMat);   // spike
            Part(PrimitiveType.Cylinder, _body, new Vector3(0f, 0.6f, 0f), new Vector3(1.6f, 0.08f, 1.6f), _bodyMat);    // base plate
            Part(PrimitiveType.Cylinder, _body, new Vector3(0f, 2.2f, 0f), new Vector3(0.3f, 1.6f, 0.3f), _bodyMat);     // mast
            for (int i = 0; i < 3; i++)                                                                                   // fins
            {
                var fin = Part(PrimitiveType.Cube, _body, Vector3.zero, new Vector3(0.08f, 1.4f, 0.7f), _bodyMat);
                fin.localRotation = Quaternion.Euler(0f, i * 120f, 0f);
                fin.localPosition = fin.localRotation * new Vector3(0f, 1.3f, 0.4f);
            }
            Part(PrimitiveType.Cylinder, _body, new Vector3(0f, 3.85f, 0f), new Vector3(0.7f, 0.06f, 0.7f), _bodyMat);   // collar

            _head = new GameObject("Head").transform;
            _head.SetParent(_body, false);
            _head.localPosition = new Vector3(0f, 4.4f, 0f);
            Part(PrimitiveType.Sphere, _head, Vector3.zero, Vector3.one * 0.6f, _headMat);
            Part(PrimitiveType.Cube, _head, Vector3.zero, new Vector3(1.3f, 0.06f, 0.06f), _headMat);                     // spinning bars
            Part(PrimitiveType.Cube, _head, Vector3.zero, new Vector3(0.06f, 0.06f, 1.3f), _headMat);
            return 4.7f;
        }

        /// <summary>Filled disc made of rings; each vertex sits on the ground. Mesh is scaled in XZ to open the zone,
        /// so heights are stored as-is and the scale only shrinks the footprint.</summary>
        Mesh BuildDisc(float r)
        {
            var verts = new List<Vector3> { new Vector3(0f, Lift(Height(Vector3.zero)), 0f) };
            var tris = new List<int>();
            for (int ring = 1; ring <= Rings; ring++)
            {
                float rr = r * ring / Rings;
                for (int s = 0; s < Segments; s++)
                {
                    float a = s / (float)Segments * Mathf.PI * 2f;
                    var p = new Vector3(Mathf.Cos(a) * rr, 0f, Mathf.Sin(a) * rr);
                    p.y = Lift(Height(p));
                    verts.Add(p);
                }
            }
            for (int s = 0; s < Segments; s++)
            {
                int a = 1 + s, b = 1 + (s + 1) % Segments;
                tris.Add(0); tris.Add(b); tris.Add(a);
            }
            for (int ring = 1; ring < Rings; ring++)
            {
                int i0 = 1 + (ring - 1) * Segments, i1 = 1 + ring * Segments;
                for (int s = 0; s < Segments; s++)
                {
                    int s1 = (s + 1) % Segments;
                    tris.Add(i0 + s); tris.Add(i0 + s1); tris.Add(i1 + s);
                    tris.Add(i0 + s1); tris.Add(i1 + s1); tris.Add(i1 + s);
                }
            }
            var m = new Mesh { name = "BeaconZone" };
            m.SetVertices(verts);
            m.SetTriangles(tris, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        Mesh BuildRing(float inner, float outer)
        {
            var verts = new Vector3[(Segments + 1) * 2];
            for (int i = 0; i <= Segments; i++)
            {
                float a = i / (float)Segments * Mathf.PI * 2f;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var pi = d * inner; pi.y = Lift(Height(pi)) + 0.02f;
                var po = d * outer; po.y = Lift(Height(po)) + 0.02f;
                verts[i * 2] = pi; verts[i * 2 + 1] = po;
            }
            var tris = new int[Segments * 6];
            for (int i = 0; i < Segments; i++)
            {
                int a = i * 2, b = a + 1, c = a + 2, d = a + 3;
                tris[i * 6 + 0] = a; tris[i * 6 + 1] = c; tris[i * 6 + 2] = b;
                tris[i * 6 + 3] = b; tris[i * 6 + 4] = c; tris[i * 6 + 5] = d;
            }
            var m = new Mesh { name = "BeaconRing", vertices = verts, triangles = tris };
            m.RecalculateBounds();
            return m;
        }

        static float Lift(float h) => h + 0.12f;

        /// <summary>Ground height (relative to the pin) at a local XZ offset. Ignores the rover.</summary>
        float Height(Vector3 local)
        {
            Vector3 from = _ground + new Vector3(local.x, 300f, local.z);
            int count = Physics.RaycastNonAlloc(from, Vector3.down, _hits, 700f, ~0, QueryTriggerInteraction.Ignore);
            float bestDist = float.MaxValue, best = 0f;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                if (_rover && _hits[i].transform.IsChildOf(_rover.transform)) continue;
                if (_hits[i].transform.IsChildOf(transform)) continue;
                if (_hits[i].distance < bestDist) { bestDist = _hits[i].distance; best = _hits[i].point.y - _ground.y; found = true; }
            }
            return found ? best : 0f;
        }

        // ================================================================ helpers

        Transform Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>()); // no colliders: the rover drives through and ground raycasts ignore it
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            return go.transform;
        }

        Transform MeshChild(string name, Mesh mesh, Material mat)
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

        /// <summary>The navigation map draws its own beacon icons, so the 3D model stays off the map camera.</summary>
        void HideFromMapCamera()
        {
            int layer = LayerMask.NameToLayer("TransparentFX");
            if (layer < 0) layer = 1;
            foreach (var r in GetComponentsInChildren<Renderer>(true)) r.gameObject.layer = layer;
        }

        void SetHeadGlow(float k)
        {
            var c = Color.Lerp(new Color(0.35f, 0.35f, 0.35f), color, Mathf.Clamp01(k));
            SetColor(_headMat, c);
            if (_light && Phase != State.Exploding) _light.intensity = 1.5f + 3f * k;
        }

        static Material MakeMaterial(Color c, bool lit, bool transparent = false)
        {
            var shader = lit
                ? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard")
                : Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var m = new Material(shader) { name = "Beacon" };
            if (transparent)
            {
                m.SetFloat("_Surface", 1f);
                m.SetFloat("_Blend", 0f);
                m.SetOverrideTag("RenderType", "Transparent");
                m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                m.SetInt("_ZWrite", 0);
                m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                m.renderQueue = (int)RenderQueue.Transparent;
                if (m.HasProperty("_Cull")) m.SetFloat("_Cull", 0f);
            }
            SetColor(m, c);
            return m;
        }

        static void SetColor(Material m, Color c)
        {
            if (!m) return;
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
        }

        void SetAlpha(Material m, float a)
        {
            var c = color; c.a = Mathf.Clamp01(a);
            SetColor(m, c);
        }
    }
}
