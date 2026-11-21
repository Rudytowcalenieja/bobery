using UnityEngine;

namespace Bobery.Rover
{
    /// <summary>
    /// Sends <see cref="EnemyRover"/>s after the rover from the enemy outposts. When the rover comes within
    /// <see cref="activationRange"/> of an outpost, enemies march out of its gate: the first after
    /// <see cref="alertDelay"/> seconds, then one every <see cref="spawnInterval"/> seconds (a bit faster each time),
    /// up to <see cref="maxPerOutpost"/> per outpost and <see cref="maxAlive"/> in total. Far from every outpost the
    /// rover is safe. Enemies that lost track of the rover (too far away) are recalled.
    /// If the map has no outposts, enemies walk in from out of sight around the rover instead.
    /// Added to the rover automatically; tweak it in the inspector.
    /// </summary>
    [DisallowMultipleComponent]
    public class EnemySpawner : MonoBehaviour
    {
        public bool spawnEnemies = true;
        [Min(0)] public int maxAlive = 3;

        [Header("Outposts")]
        [Tooltip("An outpost sends enemies while the rover is within this distance of it (metres).")]
        public float activationRange = 60f;
        [Tooltip("Seconds between the rover coming in range and the first enemy leaving the gate.")]
        [Min(0f)] public float alertDelay = 6f;
        [Tooltip("Enemies one outpost can have out at the same time.")]
        [Min(1)] public int maxPerOutpost = 2;

        [Header("Timing")]
        [Tooltip("Seconds between enemies while an outpost is active.")]
        [Min(1f)] public float spawnInterval = 35f;
        [Tooltip("Each spawn multiplies the interval by this (difficulty ramp), down to minSpawnInterval.")]
        [Range(0.5f, 1f)] public float intervalRamp = 0.93f;
        [Min(1f)] public float minSpawnInterval = 20f;

        [Header("No outposts on the map")]
        [Tooltip("Seconds before the first enemy shows up when there are no outposts.")]
        [Min(0f)] public float firstSpawnDelay = 45f;
        [Tooltip("Spawn distance from the rover (metres) when there are no outposts.")]
        public Vector2 spawnDistance = new Vector2(60f, 90f);
        [Tooltip("Enemies chasing the rover further than this are recalled.")]
        public float despawnDistance = 170f;

        [Header("Enemy")]
        [Tooltip("Size relative to the rover.")]
        [Range(0.1f, 1f)] public float enemyScale = 0.4f;
        public float enemySpeed = 6f;
        public float fireRange = 35f; // far enough that the rover turret (min pitch -35°) can still depress onto them
        public float fireInterval = 10f;
        public float projectileSpeed = 40f;
        public Color tint = new Color(0.55f, 0.1f, 0.08f);
        [Tooltip("Hit points of one enemy walker.")]
        [Min(1f)] public float enemyHealth = 50f;

        [Header("Loot")]
        [Tooltip("Chance (0..1) that a destroyed enemy walker drops a fuel canister. Turrets never drop fuel.")]
        [Range(0f, 1f)] public float fuelDropChance = 0.5f;
        [Tooltip("Litres of carried fuel one canister gives.")]
        [Min(0f)] public float fuelDropLitres = 20f;

        [Header("Difficulty per completed outpost")]
        [Tooltip("Enemy damage (walkers and turrets) grows by this share of the base for every completed outpost (0.25 = x1, x1.25, x1.5...).")]
        [Min(0f)] public float damageRampPerOutpost = 0.25f;
        [Tooltip("Enemy count limits (per outpost and in total) grow by this share of the base for every completed outpost (0.5 = 2, 3, 4...).")]
        [Min(0f)] public float countRampPerOutpost = 0.5f;

        static EnemySpawner _instance;

        /// <summary>Multiplier on enemy damage to the rover: +<see cref="damageRampPerOutpost"/> per completed outpost.</summary>
        public static float DamageMultiplier =>
            1f + (_instance ? _instance.damageRampPerOutpost : 0.25f) * OutpostProgress.CompletedCount;

        float CountMultiplier => 1f + countRampPerOutpost * OutpostProgress.CompletedCount;
        /// <summary>Enemies one outpost can have out at once, after the per-outpost ramp.</summary>
        public int MaxPerOutpost => Mathf.CeilToInt(maxPerOutpost * CountMultiplier - 0.0001f);
        /// <summary>Enemies alive at once, after the per-outpost ramp.</summary>
        public int MaxAlive => Mathf.CeilToInt(maxAlive * CountMultiplier - 0.0001f);

        RoverController _rover;
        RoverAutoFit _fit;
        Bobery.Terrain.TerrainGenerator _terrain;
        float _timer, _interval;
        int _spawned;

        void OnEnable() => _instance = this;
        void OnDisable() { if (_instance == this) _instance = null; }

        void Start()
        {
            _rover = GetComponent<RoverController>();
            _fit = GetComponent<RoverAutoFit>();
            _terrain = FindAnyObjectByType<Bobery.Terrain.TerrainGenerator>();
            _timer = firstSpawnDelay;
            _interval = spawnInterval;
        }

        void Update()
        {
            if (!_rover || GameOverScreen.IsShown) return;

            // Recall enemies that wandered off after the rover got away.
            for (int i = EnemyRover.All.Count - 1; i >= 0; i--)
            {
                var e = EnemyRover.All[i];
                if (e && !e.TargetsBeacon && Flat(e.transform.position, _rover.transform.position) > despawnDistance)
                    Destroy(e.gameObject);
            }

            if (!spawnEnemies || (_fit && !_fit.IsFitted)) return;
            if (RoverInterior.IsInside) return; // a breather while you patch things up
            if (EnemyRover.All.Count >= MaxAlive) return;

            if (HasOutposts())
            {
                // Only outposts near the rover are awake; out of range the alert resets.
                var outpost = ActiveOutpost();
                if (!outpost) { _alerted = false; return; }
                if (!_alerted) { _alerted = true; _timer = alertDelay; }
                _timer -= Time.deltaTime;
                if (_timer > 0f) return;
                if (SpawnAt(outpost)) { _interval = Mathf.Max(minSpawnInterval, _interval * intervalRamp); _timer = _interval; }
                else _timer = 2f;
                return;
            }

            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            if (Spawn()) { _interval = Mathf.Max(minSpawnInterval, _interval * intervalRamp); _timer = _interval; }
            else _timer = 2f; // no free ground this time: retry soon
        }

        // ================================================================ outposts

        Bobery.Terrain.Outpost[] _outposts = new Bobery.Terrain.Outpost[0];
        float _outpostScan = -100f;
        bool _alerted;

        /// <summary>An outpost near the rover has spotted it (enemies are on their way).</summary>
        public bool Alerted => _alerted;

        /// <summary>Outposts are generated with the terrain (possibly after we start), so look again now and then.</summary>
        bool HasOutposts()
        {
            if (Time.time - _outpostScan > 5f)
            {
                _outpostScan = Time.time;
                _outposts = FindObjectsByType<Bobery.Terrain.Outpost>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                foreach (var o in _outposts) OutpostTurret.Equip(o);
            }
            return _outposts.Length > 0;
        }

        /// <summary>The nearest outpost in range of the rover that still has room for another enemy, or null.</summary>
        Bobery.Terrain.Outpost ActiveOutpost()
        {
            Bobery.Terrain.Outpost best = null;
            float bestD = activationRange;
            foreach (var o in _outposts)
            {
                if (!o || OutpostProgress.IsCompleted(o)) continue; // neutralized: no more walkers
                float d = Flat(o.Center, _rover.transform.position);
                if (d > bestD) continue;
                int count = 0;
                foreach (var e in EnemyRover.All) if (e && e.Home == o) count++;
                if (count >= MaxPerOutpost) continue;
                best = o; bestD = d;
            }
            return best;
        }

        /// <summary>Sends one enemy out of the outpost's gate. False if there is no free ground there.</summary>
        public bool SpawnAt(Bobery.Terrain.Outpost outpost)
        {
            Transform gate = outpost.gate ? outpost.gate : outpost.transform;
            Vector3 fwd = outpost.gate ? gate.forward : (_rover.transform.position - outpost.Center);
            fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;
            Vector3 side = Vector3.Cross(Vector3.up, fwd);
            for (float k = 1.5f; k <= 12f; k += 1.5f)
            {
                Vector3 p = gate.position + fwd * k + side * Random.Range(-2.5f, 2.5f);
                if (!EnemyRover.Walkable(_terrain, p)) continue;
                p.y = EnemyRover.GroundY(_terrain, p);
                var e = Build(p);
                e.Home = outpost;
                RoverHud.Notify("Enemy leaving an outpost!");
                return true;
            }
            return false;
        }

        /// <summary>Spawns one enemy on land around the rover. False if no spot was found.</summary>
        public bool Spawn()
        {
            Vector3 c = _rover.transform.position;
            for (int attempt = 0; attempt < 30; attempt++)
            {
                float a = Random.value * Mathf.PI * 2f;
                float d = Random.Range(spawnDistance.x, spawnDistance.y);
                Vector3 p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * d;
                if (!EnemyRover.Walkable(_terrain, p)) continue;
                p.y = EnemyRover.GroundY(_terrain, p);
                Build(p);
                return true;
            }
            return false;
        }

        EnemyRover Build(Vector3 at)
        {
            var animator = _rover.GetComponentInChildren<Animator>();
            var source = animator ? animator.gameObject : (_rover.transform.childCount > 0 ? _rover.transform.GetChild(0).gameObject : null);

            var root = new GameObject($"Enemy{++_spawned}");
            Vector3 look = _rover.transform.position - at; look.y = 0f;
            root.transform.SetPositionAndRotation(at, look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look) : Quaternion.identity);

            // Body: the rover's own model, scaled down (keeps the rover's model offset so the feet touch the ground).
            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);
            body.localScale = _rover.transform.lossyScale * enemyScale;
            if (source)
            {
                var model = Instantiate(source, body, false);
                model.name = "Model";
                foreach (var t in model.GetComponentsInChildren<Transform>(true))
                    if (t && t.name == RoverTurret.GunRootName) DestroyImmediate(t.gameObject); // the rover's own gun
                foreach (var col in model.GetComponentsInChildren<Collider>(true)) Destroy(col);
                foreach (var mb in model.GetComponentsInChildren<MonoBehaviour>(true)) Destroy(mb);
                foreach (var r in model.GetComponentsInChildren<Renderer>(true))
                {
                    var mats = r.materials; // instances, destroyed with the enemy
                    foreach (var m in mats) Tint(m);
                }
            }

            // Gun turret + glowing eye on top, sized from the rover's fitted body.
            Vector3 size = (_fit && _fit.IsFitted ? _fit.WorldSize : new Vector3(3f, 2f, 4f)) * enemyScale;
            var dark = NewMat(Lit(), new Color(0.12f, 0.1f, 0.1f));
            var turret = Prim(PrimitiveType.Cube, root.transform, new Vector3(0f, size.y * 0.95f, 0f), new Vector3(size.x * 0.35f, size.y * 0.18f, size.z * 0.35f), dark);
            var barrel = Prim(PrimitiveType.Cylinder, root.transform, new Vector3(0f, size.y * 0.97f, size.z * 0.35f), new Vector3(0.12f, size.z * 0.22f, 0.12f), dark);
            barrel.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(root.transform, false);
            muzzle.localPosition = new Vector3(0f, size.y * 0.97f, size.z * 0.6f);

            var eyeMat = NewMat(Unlit(), EnemyShot.BoltColor);
            Prim(PrimitiveType.Sphere, turret, new Vector3(0f, 0.55f, 0.35f), new Vector3(0.35f, 0.6f, 0.35f), eyeMat);
            var eyeGo = new GameObject("EyeLight");
            eyeGo.transform.SetParent(root.transform, false);
            eyeGo.transform.localPosition = new Vector3(0f, size.y * 1.2f, 0f);
            var eye = eyeGo.AddComponent<Light>();
            eye.type = LightType.Point;
            eye.color = EnemyShot.BoltColor;
            eye.range = 8f;
            eye.intensity = 1.2f;
            eye.shadows = LightShadows.None;

            // The navigation map draws its own enemy icons.
            int layer = LayerMask.NameToLayer("TransparentFX"); if (layer < 0) layer = 1;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.gameObject.layer = layer;

            // Can be shot: 50 HP, hit sphere around the body.
            var health = root.AddComponent<CombatHealth>();
            health.Setup("Enemy walker", enemyHealth,
                new Vector3(0f, size.y * 0.55f, 0f), Mathf.Max(size.x, size.z) * 0.5f, size.y * 1.35f);
            health.Died += OnWalkerDied;

            var enemy = root.AddComponent<EnemyRover>();
            enemy.speed = enemySpeed;
            enemy.fireRange = fireRange;
            enemy.fireInterval = fireInterval;
            enemy.projectileSpeed = projectileSpeed;
            enemy.Init(_rover, _terrain, muzzle, eye);
            return enemy;
        }

        /// <summary>A destroyed walker may leave a fuel canister behind (picked up into carried fuel).</summary>
        void OnWalkerDied(CombatHealth h)
        {
            if (!this || !h || Random.value >= fuelDropChance || fuelDropLitres <= 0f) return;
            Vector3 p = h.transform.position;
            p.y = EnemyRover.GroundY(_terrain, p);
            var go = new GameObject("FuelCanister");
            go.transform.position = p;
            var pickup = go.AddComponent<FuelPickup>();
            pickup.litres = fuelDropLitres;
            pickup.intoTank = false;
            RoverHud.Notify("Enemy dropped a fuel canister");
        }

        void Tint(Material m)
        {
            if (!m) return;
            Color c = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : m.color;
            Color t = Color.Lerp(c, tint, 0.65f);
            t.a = c.a;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", t);
            m.color = t;
        }

        static Shader Lit() => Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        static Shader Unlit() => Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");

        static Material NewMat(Shader s, Color c)
        {
            var m = new Material(s) { name = "Enemy", color = c };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }

        static Transform Prim(PrimitiveType type, Transform parent, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go.transform;
        }

        static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
    }
}
