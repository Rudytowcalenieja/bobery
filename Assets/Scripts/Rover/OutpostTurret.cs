using System.Collections.Generic;
using UnityEngine;

namespace Bobery.Rover
{
    /// <summary>
    /// A close-in gun turret guarding an outpost: a tan pedestal, a yoke that turns, a white radome with a
    /// Gatling gun under it. It picks the nearest target in range (a beacon in range comes first, so beacons work
    /// as decoys here too), turns its gun onto it, paints it with a red laser while it locks on, then fires
    /// one burst of tracers. A burst counts as one hit: the rover loses <see cref="RoverHealth.damagePerHit"/>
    /// of its hull, a beacon one hit point. Built in code and placed by <see cref="Equip"/>
    /// (called by <see cref="EnemySpawner"/> for every outpost it finds).
    /// </summary>
    public class OutpostTurret : MonoBehaviour
    {
        public static readonly List<OutpostTurret> All = new List<OutpostTurret>();

        [Header("Combat")]
        public float range = 40f;
        [Tooltip("Seconds between the end of one burst and the start of the next lock-on.")]
        public float reloadTime = 15f;
        [Tooltip("Seconds a fresh target gets before the first lock-on starts.")]
        public float warmUp = 3f;
        [Tooltip("Share of the rover's per-hit damage one burst deals (enemy walkers deal 1).")]
        [Range(0.1f, 1f)] public float damageScale = 0.5f;
        [Tooltip("Seconds the laser paints the target before the gun opens fire.")]
        public float lockTime = 1.4f;
        public float burstTime = 1.1f;
        public int tracersPerBurst = 16;
        [Range(0.1f, 1f)] public float decoyBias = 0.7f;

        [Header("Motion")]
        public float yawSpeed = 110f;
        public float pitchSpeed = 70f;
        public float minPitch = -8f, maxPitch = 65f;

        public Component Target { get; private set; }
        public bool IsFiring => _state == State.Firing;
        public bool IsLocking => _state == State.Locking;
        public Bobery.Terrain.Outpost Home { get; private set; }

        enum State { Idle, Tracking, Locking, Firing }
        State _state;
        float _timer, _reload, _spin, _scan;
        int _fired;

        Transform _yaw, _pitch, _barrels, _muzzle;
        Light _flash;
        Transform _flashBall;
        Renderer _eye;
        LineRenderer _laser;
        RoverController _rover;

        const int TurretCount = 2;
        /// <summary>Hit points of one turret (the rover's guns can destroy it).</summary>
        public const float HitPoints = 100f;
        const float FootprintHalf = 1.3f;

        // ================================================================ placement

        /// <summary>Gives an outpost its turrets (once): two free spots near the corners of the walls, gate side first.</summary>
        /// <summary>True while at least one of the outpost's turrets is still standing.</summary>
        public static bool AnyAlive(Bobery.Terrain.Outpost outpost)
        {
            foreach (var t in All)
                if (t && t.Home == outpost && !(t.TryGetComponent<CombatHealth>(out var h) && h.IsDead)) return true;
            return false;
        }

        public static void Equip(Bobery.Terrain.Outpost outpost)
        {
            if (!outpost) return;
            // Equipped once only: when every turret is destroyed the outpost stays empty (no fresh turrets).
            if (OutpostProgress.IsKnown(outpost)) return;
            if (outpost.GetComponentInChildren<OutpostTurret>(true)) { OutpostProgress.Register(outpost); return; }

            float W = outpost.gate ? Mathf.Max(6f, -outpost.gate.localPosition.z - 1f) : Mathf.Max(8f, outpost.radius * 0.5f);
            // Every free spot on a 2 m grid inside the walls, best first: corners, then the gate side.
            var spots = new List<Vector2>();
            float lim = W - 3f;
            for (float x = -lim; x <= lim + 0.01f; x += 2f)
                for (float z = -lim; z <= lim + 0.01f; z += 2f)
                    spots.Add(new Vector2(x, z));
            spots.Sort((a, b) => Score(a, lim).CompareTo(Score(b, lim)));
            var used = new List<Vector2>();
            int placed = 0;
            foreach (var s in spots)
            {
                if (placed >= TurretCount) break;
                if (outpost.IsObstacleLocal(s.x, s.y, FootprintHalf + 0.6f)) continue;
                if (used.Exists(u => Vector2.Distance(u, s) < W)) continue; // spread them out
                used.Add(s);

                var go = new GameObject($"Turret_{placed}");
                go.transform.SetParent(outpost.transform, false);
                go.transform.localPosition = new Vector3(s.x, 0f, s.y);
                // Start facing outward, away from the centre.
                go.transform.localRotation = Quaternion.LookRotation(new Vector3(s.x, 0f, s.y).normalized);
                var t = go.AddComponent<OutpostTurret>();
                t.Home = outpost;
                t.Build();
                go.AddComponent<CombatHealth>().Setup("Outpost turret", HitPoints, new Vector3(0f, 2.6f, 0f), 1.9f, 5.2f);

                outpost.footprints.Add(new Bobery.Terrain.OutpostFootprint
                {
                    center = s,
                    halfSize = Vector2.one * FootprintHalf,
                });
                placed++;
            }
            if (placed > 0) OutpostProgress.Register(outpost); // completed once these turrets are destroyed
        }

        /// <summary>Lower is better: near a corner, and the gate (-Z) side a little ahead of the back.</summary>
        static float Score(Vector2 p, float lim)
        {
            float corner = Mathf.Min(
                Mathf.Min(Vector2.Distance(p, new Vector2(-lim, -lim)), Vector2.Distance(p, new Vector2(lim, -lim))),
                Mathf.Min(Vector2.Distance(p, new Vector2(-lim, lim)), Vector2.Distance(p, new Vector2(lim, lim))) + 3f);
            return corner;
        }

        // ================================================================ model

        static Material _paint, _white, _dark, _steel, _eyeMat, _tracerMat;

        void Build()
        {
            var tan = Paint();
            var white = White();
            var dark = Dark();
            var steel = Steel();

            // Pedestal.
            Prim(PrimitiveType.Cube, transform, new Vector3(0f, 0.08f, 0f), new Vector3(2.1f, 0.16f, 2.1f), dark);
            Prim(PrimitiveType.Cube, transform, new Vector3(0f, 0.85f, 0f), new Vector3(1.6f, 1.4f, 1.6f), tan);
            Prim(PrimitiveType.Cube, transform, new Vector3(0f, 1.6f, 0f), new Vector3(1.75f, 0.12f, 1.75f), tan);
            // Brackets down the sides, like the photo's stacked clamps.
            for (int side = 0; side < 4; side++)
            {
                var rot = Quaternion.Euler(0f, side * 90f, 0f);
                for (int i = 0; i < 3; i++)
                {
                    var b = Prim(PrimitiveType.Cube, transform, rot * new Vector3(0.45f, 0.45f + i * 0.38f, 0.83f), new Vector3(0.28f, 0.14f, 0.12f), tan);
                    b.localRotation = rot;
                    var b2 = Prim(PrimitiveType.Cube, transform, rot * new Vector3(-0.45f, 0.45f + i * 0.38f, 0.83f), new Vector3(0.28f, 0.14f, 0.12f), tan);
                    b2.localRotation = rot;
                }
            }

            // Turning yoke.
            _yaw = new GameObject("Yaw").transform;
            _yaw.SetParent(transform, false);
            _yaw.localPosition = new Vector3(0f, 1.66f, 0f);
            Prim(PrimitiveType.Cylinder, _yaw, new Vector3(0f, 0.08f, 0f), new Vector3(1.5f, 0.08f, 1.5f), dark);
            Prim(PrimitiveType.Cube, _yaw, new Vector3(0.78f, 0.75f, 0f), new Vector3(0.22f, 1.35f, 0.95f), steel);
            Prim(PrimitiveType.Cube, _yaw, new Vector3(-0.78f, 0.75f, 0f), new Vector3(0.22f, 1.35f, 0.95f), steel);
            Prim(PrimitiveType.Cube, _yaw, new Vector3(0f, 0.25f, -0.35f), new Vector3(1.3f, 0.35f, 0.5f), dark); // ammo drum housing

            // Elevating mass: radome, gun, barrels.
            _pitch = new GameObject("Pitch").transform;
            _pitch.SetParent(_yaw, false);
            _pitch.localPosition = new Vector3(0f, 1.15f, 0f);
            Prim(PrimitiveType.Cylinder, _pitch, new Vector3(0f, 0.75f, -0.15f), new Vector3(1.3f, 0.75f, 1.3f), white);
            Prim(PrimitiveType.Sphere, _pitch, new Vector3(0f, 1.5f, -0.15f), new Vector3(1.3f, 0.8f, 1.3f), white);
            Prim(PrimitiveType.Cube, _pitch, new Vector3(0.72f, 0.95f, -0.1f), new Vector3(0.3f, 0.55f, 0.55f), white); // electronics box
            Prim(PrimitiveType.Cube, _pitch, new Vector3(0.9f, 0.95f, -0.1f), new Vector3(0.08f, 0.35f, 0.4f), dark);
            Prim(PrimitiveType.Cylinder, _pitch, new Vector3(0f, 0f, 0f), new Vector3(0.5f, 0.85f, 0.5f), steel)
                .localRotation = Quaternion.Euler(0f, 0f, 90f); // trunnion axle
            Prim(PrimitiveType.Cube, _pitch, new Vector3(0f, -0.05f, 0.35f), new Vector3(0.55f, 0.5f, 1.0f), dark); // gun body

            var eye = Prim(PrimitiveType.Sphere, _pitch, new Vector3(0f, 1.0f, 0.5f), Vector3.one * 0.16f, EyeMat());
            _eye = eye.GetComponent<Renderer>();

            _barrels = new GameObject("Barrels").transform;
            _barrels.SetParent(_pitch, false);
            _barrels.localPosition = new Vector3(0f, -0.05f, 0.85f);
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                var bar = Prim(PrimitiveType.Cylinder, _barrels, new Vector3(Mathf.Cos(a) * 0.11f, Mathf.Sin(a) * 0.11f, 0.85f),
                    new Vector3(0.07f, 0.85f, 0.07f), steel);
                bar.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            foreach (float z in new[] { 0.15f, 1.0f, 1.62f })
                Prim(PrimitiveType.Cylinder, _barrels, new Vector3(0f, 0f, z), new Vector3(0.34f, 0.04f, 0.34f), dark)
                    .localRotation = Quaternion.Euler(90f, 0f, 0f);

            _muzzle = new GameObject("Muzzle").transform;
            _muzzle.SetParent(_pitch, false);
            _muzzle.localPosition = new Vector3(0f, -0.05f, 2.6f);

            _flashBall = Prim(PrimitiveType.Sphere, _muzzle, Vector3.zero, Vector3.one * 0.5f, TracerMat());
            _flashBall.gameObject.layer = HiddenLayer;
            _flashBall.gameObject.SetActive(false);
            _flash = _muzzle.gameObject.AddComponent<Light>();
            _flash.type = LightType.Point;
            _flash.color = new Color(1f, 0.7f, 0.3f);
            _flash.range = 9f;
            _flash.intensity = 0f;
            _flash.shadows = LightShadows.None;

            var laserGo = new GameObject("Laser");
            laserGo.transform.SetParent(transform, false);
            laserGo.layer = HiddenLayer;
            _laser = laserGo.AddComponent<LineRenderer>();
            _laser.positionCount = 2;
            _laser.useWorldSpace = true;
            _laser.widthMultiplier = 0.05f;
            _laser.sharedMaterial = EnemyShot.TrailMaterial();
            _laser.startColor = new Color(1f, 0.15f, 0.1f, 0.9f);
            _laser.endColor = new Color(1f, 0.15f, 0.1f, 0.35f);
            _laser.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _laser.enabled = false;

            // Face the stored outward direction with the yoke, keep the base square to the walls.
            var facing = transform.rotation;
            transform.localRotation = Quaternion.identity;
            _yaw.rotation = facing;
            _reload = Random.Range(0f, reloadTime);
        }

        // ================================================================ behaviour

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void Update()
        {
            if (!_yaw) return;
            if (!_rover) _rover = FindAnyObjectByType<RoverController>();

            _scan -= Time.deltaTime;
            if (_scan <= 0f || !Valid(Target))
            {
                _scan = 0.5f;
                var t = PickTarget();
                if (t != Target)
                {
                    Target = t;
                    if (t) _reload = Mathf.Max(_reload, warmUp + Random.Range(0f, 2f)); // turrets don't fire in lockstep
                    if (_state == State.Locking) _state = State.Tracking; // re-acquire before firing
                }
            }

            if (!Valid(Target) || GameOverScreen.IsShown)
            {
                Target = null;
                if (_state != State.Firing) _state = State.Idle;
            }

            Vector3 aim = Target ? AimOf(Target) : Vector3.zero;
            bool onTarget = Target && Turn(aim);

            switch (_state)
            {
                case State.Idle:
                    _reload -= Time.deltaTime;
                    if (Target) _state = State.Tracking;
                    else _yaw.Rotate(0f, 12f * Time.deltaTime, 0f, Space.Self); // slow sweep
                    break;
                case State.Tracking:
                    _reload -= Time.deltaTime;
                    if (onTarget && _reload <= 0f) { _state = State.Locking; _timer = 0f; }
                    break;
                case State.Locking:
                    _timer += Time.deltaTime;
                    if (_timer >= lockTime) { _state = State.Firing; _timer = 0f; _fired = 0; }
                    break;
                case State.Firing:
                    _timer += Time.deltaTime;
                    int due = Mathf.Min(tracersPerBurst, Mathf.CeilToInt(_timer / burstTime * tracersPerBurst));
                    while (_fired < due)
                    {
                        _fired++;
                        bool last = _fired == tracersPerBurst;
                        TurretTracer.Launch(_muzzle.position, Target ? aim : _muzzle.position + _muzzle.forward * range,
                            last ? Target : null, TracerMat(), damageScale);
                    }
                    if (_fired >= tracersPerBurst) { _state = Target ? State.Tracking : State.Idle; _reload = reloadTime; }
                    break;
            }

            // Barrels spin up while locking and firing, wind down otherwise.
            float spinTarget = _state == State.Firing ? 1800f : _state == State.Locking ? 900f : 0f;
            _spin = Mathf.MoveTowards(_spin, spinTarget, 1500f * Time.deltaTime);
            _barrels.Rotate(0f, 0f, _spin * Time.deltaTime, Space.Self);

            bool firing = _state == State.Firing;
            _flash.intensity = firing ? Random.Range(3f, 7f) : 0f;
            _flashBall.gameObject.SetActive(firing && Random.value > 0.35f);
            if (firing) _flashBall.localScale = Vector3.one * Random.Range(0.35f, 0.6f);

            bool painting = (_state == State.Locking || firing) && Target;
            _laser.enabled = painting;
            if (painting)
            {
                _laser.SetPosition(0, _muzzle.position);
                _laser.SetPosition(1, aim);
                float a = _state == State.Locking ? 0.45f + 0.45f * Mathf.Sin(Time.time * 30f) : 0.9f;
                _laser.startColor = new Color(1f, 0.15f, 0.1f, a);
            }

            Color ec = painting ? new Color(1f, 0.1f, 0.05f) * 4f : Target ? new Color(1f, 0.6f, 0.1f) * 2f : new Color(0.2f, 0.9f, 0.3f) * 1.5f;
            _eye.material.SetColor("_BaseColor", ec);
        }

        /// <summary>Turns yoke and gun toward a point. True once both are close enough to shoot.</summary>
        bool Turn(Vector3 aim)
        {
            Vector3 flat = aim - _yaw.position;
            flat.y = 0f;
            if (flat.sqrMagnitude > 0.01f)
            {
                var want = Quaternion.LookRotation(flat);
                _yaw.rotation = Quaternion.RotateTowards(_yaw.rotation, want, yawSpeed * Time.deltaTime);
            }
            Vector3 local = _yaw.InverseTransformPoint(aim) - _pitch.localPosition;
            float pitch = Mathf.Clamp(Mathf.Atan2(local.y, new Vector2(local.x, local.z).magnitude) * Mathf.Rad2Deg, minPitch, maxPitch);
            float cur = Mathf.DeltaAngle(0f, -_pitch.localEulerAngles.x);
            cur = Mathf.MoveTowards(cur, pitch, pitchSpeed * Time.deltaTime);
            _pitch.localRotation = Quaternion.Euler(-cur, 0f, 0f);

            Vector3 dir = aim - _muzzle.position;
            return Vector3.Angle(_muzzle.forward, dir) < 4f || Vector3.Angle(_pitch.forward, aim - _pitch.position) < 4f;
        }

        Component PickTarget()
        {
            // Keep a locked target while it stays valid and in range.
            if (Valid(Target) && InRange(Target)) return Target;

            Component best = null;
            float bestScore = float.MaxValue;
            foreach (var b in BeaconNetwork.Deployed)
            {
                if (!Valid(b) || !InRange(b)) continue;
                float s = Vector3.Distance(transform.position, b.transform.position) * decoyBias;
                if (s < bestScore) { bestScore = s; best = b; }
            }
            if (_rover && Valid(_rover) && InRange(_rover))
            {
                float s = Vector3.Distance(transform.position, _rover.transform.position);
                if (s < bestScore) best = _rover;
            }
            return best;
        }

        bool InRange(Component c) => Vector3.Distance(transform.position, c.transform.position) <= range;

        static bool Valid(Component c)
        {
            if (!c) return false;
            if (c is Beacon) return c.gameObject.activeInHierarchy;
            return !(c.TryGetComponent<RoverHealth>(out var h) && h.IsDestroyed);
        }

        static Vector3 AimOf(Component t)
        {
            if (t is Beacon) return t.transform.position + Vector3.up * 2.5f;
            var fit = t.GetComponent<RoverAutoFit>();
            float h = fit && fit.IsFitted ? fit.WorldSize.y * 0.55f : 1.5f;
            return t.transform.position + Vector3.up * h;
        }

        // ================================================================ helpers

        static int HiddenLayer
        {
            get { int l = LayerMask.NameToLayer("TransparentFX"); return l < 0 ? 1 : l; }
        }

        static Transform Prim(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.name = type.ToString();
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go.transform;
        }

        static Material Lit(string name, Color c, float smooth, float metal)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh) { name = name, color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            return m;
        }

        static Material Paint() => _paint ? _paint : _paint = Lit("TurretTan", new Color(0.72f, 0.62f, 0.45f), 0.25f, 0f);
        static Material White() => _white ? _white : _white = Lit("TurretRadome", new Color(0.93f, 0.94f, 0.92f), 0.55f, 0f);
        static Material Dark() => _dark ? _dark : _dark = Lit("TurretDark", new Color(0.18f, 0.19f, 0.2f), 0.4f, 0.5f);
        static Material Steel() => _steel ? _steel : _steel = Lit("TurretSteel", new Color(0.42f, 0.44f, 0.45f), 0.6f, 0.8f);

        static Material EyeMat()
        {
            if (_eyeMat) return _eyeMat;
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            _eyeMat = new Material(sh) { name = "TurretEye" };
            return _eyeMat;
        }

        internal static Material TracerMat()
        {
            if (_tracerMat) return _tracerMat;
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var c = new Color(1f, 0.82f, 0.45f);
            _tracerMat = new Material(sh) { name = "TurretTracer", color = c };
            if (_tracerMat.HasProperty("_BaseColor")) _tracerMat.SetColor("_BaseColor", c);
            return _tracerMat;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => All.Clear();
    }

    /// <summary>
    /// One fast tracer round from a turret burst. Purely visual, except the burst's last round,
    /// which carries the damage to its target when it lands.
    /// </summary>
    public class TurretTracer : MonoBehaviour
    {
        Vector3 _to;
        Component _target;
        float _damageScale = 1f;
        const float Speed = 120f;

        public static void Launch(Vector3 from, Vector3 to, Component damageTarget, Material mat, float damageScale = 1f)
        {
            int layer = LayerMask.NameToLayer("TransparentFX"); if (layer < 0) layer = 1;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.name = "Tracer";
            go.layer = layer;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            go.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.localScale = new Vector3(0.07f, 0.07f, 1.4f);
            go.transform.position = from;
            var t = go.AddComponent<TurretTracer>();
            t._to = to + Random.insideUnitSphere * (damageTarget ? 0f : 0.7f);
            t._target = damageTarget;
            t._damageScale = damageScale;
            go.transform.rotation = Quaternion.LookRotation(t._to - from);
        }

        void Update()
        {
            Vector3 d = _to - transform.position;
            float step = Speed * Time.deltaTime;
            if (d.magnitude <= step)
            {
                if (_target && !GameOverScreen.IsShown)
                {
                    if (_target is Beacon b) b.TakeHit();
                    else if (_target.TryGetComponent<RoverHealth>(out var h)) h.TakeDamage(h.maxHealth * h.damagePerHit * _damageScale * EnemySpawner.DamageMultiplier);
                    ImpactFlash.Spawn(_to);
                }
                Destroy(gameObject);
                return;
            }
            transform.position += d.normalized * step;
        }
    }
}
