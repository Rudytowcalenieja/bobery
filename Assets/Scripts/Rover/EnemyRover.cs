using System.Collections.Generic;
using UnityEngine;

namespace Bobery.Rover
{
    /// <summary>
    /// A small hostile walker (the rover's model, scaled down and tinted red). It locks onto one target —
    /// the rover or a deployed beacon, whichever is closest when it picks (beacons count as a bit closer,
    /// so they work as decoys) — and keeps that target until it's gone. It walks into range and shoots:
    /// every shot hits, the rover loses <see cref="RoverHealth.damagePerHit"/> of its hull, a beacon one of its hit points.
    /// Created by <see cref="EnemySpawner"/>.
    /// </summary>
    public class EnemyRover : MonoBehaviour
    {
        public static readonly List<EnemyRover> All = new List<EnemyRover>();

        [Header("Movement")]
        public float speed = 6f;
        public float turnSpeed = 140f;
        [Header("Combat")]
        [Tooltip("Shoots from this far away (metres).")]
        public float fireRange = 26f;
        [Tooltip("Seconds between shots.")]
        public float fireInterval = 6f;
        public float projectileSpeed = 40f;
        [Tooltip("Beacons look this much closer than they are when picking a target (0.5 = half the distance).")]
        [Range(0.1f, 1f)] public float decoyBias = 0.7f;

        /// <summary>The locked target: a <see cref="RoverController"/> or a <see cref="Beacon"/>.</summary>
        public Component Target { get; private set; }
        public bool TargetsBeacon => Target is Beacon;
        public bool IsFiring => _inRange;
        /// <summary>Outpost this enemy came from (null if it walked in from the wild).</summary>
        public Bobery.Terrain.Outpost Home { get; set; }

        RoverController _rover;
        Bobery.Terrain.TerrainGenerator _terrain;
        Animator _animator;
        Transform _muzzle;
        Light _eye;
        float _yaw, _cooldown, _currentSpeed, _animValue;
        bool _inRange;
        static readonly int SpeedHash = Animator.StringToHash(RoverAnimator.SpeedParam);

        public void Init(RoverController rover, Bobery.Terrain.TerrainGenerator terrain, Transform muzzle, Light eye)
        {
            _rover = rover;
            _terrain = terrain;
            _muzzle = muzzle;
            _eye = eye;
            _animator = GetComponentInChildren<Animator>();
            _yaw = transform.eulerAngles.y;
            _cooldown = fireInterval * Random.Range(0.4f, 1f);
            PickTarget();
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        void OnDestroy()
        {
            // Tinted model materials and turret materials are per-enemy copies.
            foreach (var r in GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m) Destroy(m);
        }

        // ================================================================ targeting

        bool TargetValid()
        {
            if (!Target) return false;
            if (Target is Beacon b) return b.IsActive;
            if (Target is RoverController r)
            {
                var h = r.GetComponent<RoverHealth>();
                return !h || !h.IsDestroyed;
            }
            return false;
        }

        /// <summary>Locks onto the nearest target (beacons weighted by <see cref="decoyBias"/>).</summary>
        void PickTarget()
        {
            Target = null;
            float best = float.MaxValue;
            if (_rover)
            {
                var h = _rover.GetComponent<RoverHealth>();
                if (!h || !h.IsDestroyed) { Target = _rover; best = Flat(_rover.transform.position); }
            }
            foreach (var b in BeaconNetwork.Deployed)
            {
                if (!b || !b.IsActive) continue;
                float d = Flat(b.transform.position) * decoyBias;
                if (d < best) { best = d; Target = b; }
            }
        }

        // ================================================================ update

        void Update()
        {
            if (GameOverScreen.IsShown) { Animate(0f); return; }
            if (!TargetValid()) PickTarget();
            if (!Target) { _inRange = false; Drive(Vector3.zero, false); return; }

            Vector3 to = Target.transform.position - transform.position;
            to.y = 0f;
            float dist = to.magnitude;
            // Stop a bit inside the range so small target moves don't make it shuffle back and forth.
            if (_inRange) _inRange = dist <= fireRange;
            else _inRange = dist <= fireRange * 0.85f;

            Drive(_inRange ? to : PathDirection(to), !_inRange);

            _cooldown -= Time.deltaTime;
            float facing = Vector3.Angle(Forward(), to);
            if (_inRange && facing < 25f && _cooldown <= 0f)
            {
                _cooldown = fireInterval;
                Fire();
            }

            if (_eye) _eye.intensity = Mathf.Lerp(_eye.intensity, _inRange ? 3f : 1.2f, Time.deltaTime * 4f);
        }

        Vector3 Forward() => Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;

        // ================================================================ pathing

        // One grid shared by all enemies (same rules as the rover: water, steep ground, outpost walls).
        static RoverPathfinder _pathfinder;
        readonly List<Vector3> _path = new List<Vector3>();
        int _wp;
        float _repathAt;
        Vector3 _pathGoal = new Vector3(float.NaN, 0f, 0f);

        /// <summary>Direction to walk: along a planned route around walls and water, re-planned every few seconds
        /// or when the target has moved. Falls back to straight at the target if there's no route.</summary>
        Vector3 PathDirection(Vector3 toTarget)
        {
            Vector3 goal = Target.transform.position;
            bool moved = float.IsNaN(_pathGoal.x) || Flat2(goal, _pathGoal) > 8f;
            if (moved || Time.time >= _repathAt)
            {
                _repathAt = Time.time + Random.Range(2.5f, 4f); // staggered so enemies don't all plan on one frame
                _pathGoal = goal;
                _pathfinder ??= new RoverPathfinder { cellSize = 2f, clearance = 1.5f, maxSlope = 40f, cacheGrid = true };
                _pathfinder.ignore = transform;
                if (!_pathfinder.FindPath(transform.position, goal, _path)) _path.Clear();
                _wp = 0;
            }
            while (_wp < _path.Count - 1 && Flat2(transform.position, _path[_wp]) < 2.5f) _wp++;
            if (_wp >= _path.Count) return toTarget;
            Vector3 d = _path[_wp] - transform.position;
            d.y = 0f;
            return d.sqrMagnitude > 0.01f ? d : toTarget;
        }

        static float Flat2(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        void Drive(Vector3 toTarget, bool move)
        {
            float dt = Time.deltaTime;
            Vector3 desired = toTarget;

            // Keep some room from other enemies.
            foreach (var e in All)
            {
                if (e == this) continue;
                Vector3 away = transform.position - e.transform.position;
                away.y = 0f;
                float d = away.magnitude;
                if (d > 0.01f && d < 6f) desired += away.normalized * (6f - d) * 3f;
            }

            if (desired.sqrMagnitude > 0.0001f)
            {
                // Find a heading toward the target that isn't water or a building.
                float wanted = Mathf.Atan2(desired.x, desired.z) * Mathf.Rad2Deg;
                float heading = wanted;
                if (move)
                {
                    bool found = false;
                    foreach (float off in new[] { 0f, 30f, -30f, 60f, -60f, 95f, -95f, 130f, -130f })
                    {
                        Vector3 dir = Quaternion.Euler(0f, wanted + off, 0f) * Vector3.forward;
                        if (Walkable(transform.position + dir * 4f)) { heading = wanted + off; found = true; break; }
                    }
                    if (!found) move = false;
                }
                _yaw = Mathf.MoveTowardsAngle(_yaw, heading, turnSpeed * dt);
                if (move && Mathf.Abs(Mathf.DeltaAngle(_yaw, heading)) > 50f) move = false; // turn in place first
            }
            else move = false;

            _currentSpeed = Mathf.MoveTowards(_currentSpeed, move ? speed : 0f, speed * 3f * dt);
            Vector3 pos = transform.position + Forward() * (_currentSpeed * dt);
            if (_currentSpeed > 0f && !Walkable(pos)) { pos = transform.position; _currentSpeed = 0f; }
            pos.y = GroundY(pos);
            transform.SetPositionAndRotation(pos, Quaternion.Euler(0f, _yaw, 0f));
            Animate(_currentSpeed / Mathf.Max(0.01f, speed));
        }

        void Animate(float drive)
        {
            if (!_animator) return;
            _animValue = Mathf.Lerp(_animValue, drive, 1f - Mathf.Exp(-8f * Time.deltaTime));
            _animator.SetFloat(SpeedHash, _animValue * 2.2f); // small legs move faster
        }

        void Fire()
        {
            Vector3 from = _muzzle ? _muzzle.position : transform.position + Vector3.up;
            EnemyShot.Launch(from, Target, projectileSpeed);
        }

        // ================================================================ ground

        bool Walkable(Vector3 world) => Walkable(_terrain, world);
        float GroundY(Vector3 p) => GroundY(_terrain, p);

        /// <summary>Land inside the map that isn't water or an outpost building.</summary>
        public static bool Walkable(Bobery.Terrain.TerrainGenerator terrain, Vector3 world)
        {
            if (!terrain) return true;
            var t = terrain.transform;
            Vector3 l = t.InverseTransformPoint(world);
            float half = terrain.mapSize * 0.5f - 3f;
            if (Mathf.Abs(l.x) > half || Mathf.Abs(l.z) > half) return false;
            if (terrain.IsObstacle(l.x, l.z, 1f)) return false;
            float waterY = t.position.y + terrain.waterLevel * t.lossyScale.y;
            return GroundY(terrain, world) > waterY + 0.2f;
        }

        public static float GroundY(Bobery.Terrain.TerrainGenerator terrain, Vector3 p)
        {
            if (terrain)
            {
                var t = terrain.transform;
                Vector3 l = t.InverseTransformPoint(p);
                return t.TransformPoint(new Vector3(l.x, terrain.SampleHeight(l.x, l.z), l.z)).y;
            }
            if (Physics.Raycast(new Vector3(p.x, p.y + 200f, p.z), Vector3.down, out var hit, 400f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point.y;
            return p.y;
        }

        float Flat(Vector3 p)
        {
            Vector3 a = transform.position; a.y = 0f; p.y = 0f;
            return Vector3.Distance(a, p);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() { All.Clear(); _pathfinder = null; }
    }

    /// <summary>Enemy bolt: flies to its target and always hits (unless the target is gone by then).</summary>
    public class EnemyShot : MonoBehaviour
    {
        Component _target;
        Vector3 _aim;
        float _speed, _life;
        Light _light;
        static Material _boltMat, _trailMat;

        public static readonly Color BoltColor = new Color(1f, 0.25f, 0.15f);

        public static void Launch(Vector3 from, Component target, float speed)
        {
            var go = new GameObject("EnemyShot");
            go.transform.position = from;
            var s = go.AddComponent<EnemyShot>();
            s._target = target;
            s._aim = AimOf(target);
            s._speed = speed;
            s.Build();
        }

        void Build()
        {
            int layer = HiddenLayer;
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(ball.GetComponent<Collider>());
            ball.transform.SetParent(transform, false);
            ball.transform.localScale = Vector3.one * 0.45f;
            ball.GetComponent<Renderer>().sharedMaterial = BoltMaterial();
            ball.layer = layer;

            var trail = gameObject.AddComponent<TrailRenderer>();
            trail.time = 0.18f;
            trail.startWidth = 0.35f;
            trail.endWidth = 0f;
            trail.sharedMaterial = TrailMaterial();
            trail.startColor = BoltColor;
            trail.endColor = new Color(BoltColor.r, BoltColor.g, BoltColor.b, 0f);
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            gameObject.layer = layer;

            _light = gameObject.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = BoltColor;
            _light.range = 6f;
            _light.intensity = 3f;
            _light.shadows = LightShadows.None;
        }

        void Update()
        {
            _life += Time.deltaTime;
            if (_target) _aim = AimOf(_target);
            Vector3 to = _aim - transform.position;
            float step = _speed * Time.deltaTime;
            if (to.magnitude <= step + 0.3f || _life > 6f)
            {
                transform.position = _aim;
                Impact();
                return;
            }
            transform.position += to.normalized * step;
        }

        static Vector3 AimOf(Component t)
        {
            if (t is Beacon) return t.transform.position + Vector3.up * 2.5f;
            var fit = t.GetComponent<RoverAutoFit>();
            float h = fit && fit.IsFitted ? fit.WorldSize.y * 0.55f : 1.5f;
            return t.transform.position + Vector3.up * h;
        }

        void Impact()
        {
            if (_target && !GameOverScreen.IsShown)
            {
                if (_target is Beacon b) b.TakeHit();
                else if (_target.TryGetComponent<RoverHealth>(out var h)) h.Hit();
            }
            ImpactFlash.Spawn(transform.position);
            Destroy(gameObject);
        }

        static int HiddenLayer
        {
            get { int l = LayerMask.NameToLayer("TransparentFX"); return l < 0 ? 1 : l; }
        }

        internal static Material BoltMaterial()
        {
            if (_boltMat) return _boltMat;
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            _boltMat = new Material(sh) { name = "EnemyBolt", color = BoltColor };
            if (_boltMat.HasProperty("_BaseColor")) _boltMat.SetColor("_BaseColor", new Color(1f, 0.55f, 0.4f));
            return _boltMat;
        }

        internal static Material TrailMaterial()
        {
            if (_trailMat) return _trailMat;
            _trailMat = new Material(Shader.Find("Sprites/Default")) { name = "EnemyTrail" };
            return _trailMat;
        }
    }

    /// <summary>Short expanding flash where a shot lands.</summary>
    public class ImpactFlash : MonoBehaviour
    {
        Transform _ball;
        Light _light;
        Material _mat;
        float _t;

        public static void Spawn(Vector3 at)
        {
            var go = new GameObject("ImpactFlash");
            go.transform.position = at;
            go.AddComponent<ImpactFlash>();
        }

        void Start()
        {
            int layer = LayerMask.NameToLayer("TransparentFX"); if (layer < 0) layer = 1;
            var ball = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(ball.GetComponent<Collider>());
            ball.transform.SetParent(transform, false);
            ball.layer = layer;
            _ball = ball.transform;
            _mat = new Material(EnemyShot.BoltMaterial());
            ball.GetComponent<Renderer>().sharedMaterial = _mat;
            _light = gameObject.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = EnemyShot.BoltColor;
            _light.range = 10f;
            _light.shadows = LightShadows.None;
        }

        void Update()
        {
            _t += Time.deltaTime;
            float k = Mathf.Clamp01(_t / 0.35f);
            _ball.localScale = Vector3.one * Mathf.Lerp(0.4f, 2.4f, k) * (1f - k * 0.6f);
            _light.intensity = 8f * (1f - k);
            if (k >= 1f) Destroy(gameObject);
        }

        void OnDestroy() { if (_mat) Destroy(_mat); }
    }
}
