using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Bobery.Rover
{
    /// <summary>
    /// The final boss. Once enough outposts are completed (<see cref="BossConfig.outpostsBeforeBoss"/>) the boss
    /// slams down from the sky next to the rover: the camera stops following the rover and holds a fixed view in
    /// front of the boss, and a ring of raised ground (<see cref="BossArenaWall"/>) traps the rover in the arena.
    /// Then it runs <see cref="BossConfig.cycles"/> cycles of three phases:
    ///  1. Escorts - two walkers drop in; kill both.
    ///  2. Barrage - volleys of cannon balls that home in on the rover; Shift dashes aside and breaks their lock.
    ///  3. Exposed - the boss slumps, helpless, and a weak point lights up on its chest; only a railgun hit counts.
    /// After the last cycle it falls over (death clip) and the victory screen comes up.
    /// Added to the rover automatically by <see cref="RoverController"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class BossFight : MonoBehaviour
    {
        public enum Phase { Idle, Incoming, Intro, Escorts, Barrage, Exposed, Recover, Dying, Defeated }

        public static BossFight Instance { get; private set; }
        /// <summary>From the boss landing until it's dead: normal enemy spawns stop, Shift dodges instead of boosting.</summary>
        public static bool IsActive => Instance && Instance._phase >= Phase.Intro && Instance._phase <= Phase.Dying;
        /// <summary>Landing / death scenes: the rover holds still and can't shoot.</summary>
        public static bool InCutscene => Instance && (Instance._phase == Phase.Intro || Instance._phase == Phase.Dying);

        public Phase CurrentPhase => _phase;
        public string DisplayName => _cfg ? _cfg.displayName : "BOSS";
        public int CyclesDone => _cyclesDone;
        public int Cycles => _cfg ? _cfg.cycles : 3;
        public int Volley => _volley;
        public int EscortsTotal => _escortsTotal;
        public int EscortsAlive
        {
            get
            {
                int n = 0;
                foreach (var e in _escorts) if (e && e.TryGetComponent<CombatHealth>(out var h) && !h.IsDead) n++;
                return n;
            }
        }
        public CombatHealth WeakPoint => _weak;
        public Camera FightCamera => _cam;
        public bool DodgeReady => _dodgeCooldown <= 0f && _dodgeLeft <= 0f;
        /// <summary>0..1, 1 = ready.</summary>
        public float DodgeCharge => _cfg ? 1f - Mathf.Clamp01(_dodgeCooldown / Mathf.Max(0.01f, _cfg.dodgeCooldown + _cfg.dodgeDuration)) : 1f;
        public bool CanDodge => _phase >= Phase.Escorts && _phase <= Phase.Recover && !Paused;
        /// <summary>Cannon balls still locked onto the rover.</summary>
        public int IncomingLocked
        {
            get { int n = 0; foreach (var b in BossCannonBall.InFlight) if (b && b.Locked) n++; return n; }
        }

        BossConfig _cfg;
        RoverController _rover;
        RoverAutoFit _fit;
        RoverBattery _battery;
        EnemySpawner _spawner;
        Bobery.Terrain.TerrainGenerator _terrain;
        BossHud _hud;
        Phase _phase;
        float _incomingAt;
        int _cyclesDone, _volley, _escortsTotal;
        readonly List<EnemyRover> _escorts = new List<EnemyRover>();

        // Sizes (metres)
        float _roverSize, _roverWidth, _arenaRadius, _bossHeight, _bossRadius, _ballRadius;
        Vector3 _facing; // flat: from the boss towards the camera

        // Boss: _root (on the ground, facing the camera) > _pose (procedural motion, feet pivot) > _model (scaled)
        Transform _root, _pose, _model;
        Transform[] _muzzles = new Transform[0];
        Light[] _muzzleLights = new Light[0];
        Transform _chest;
        CombatHealth _armor, _weak;
        Transform _weakVisual;
        LineRenderer _weakRing;
        Light _weakLight;
        Vector3 _lastWeakPos;
        BossArenaWall _wall;

        // Procedural pose
        float _fallY, _recoil, _charge, _slump, _slumpTarget, _impactAge = 99f, _shootClipT = 99f;
        bool _poseFrozen; // death clip is playing

        // Camera
        Camera _cam;
        TankCamera _tankCam;
        Vector3 _camPos, _camFromPos;
        Quaternion _camRot, _camFromRot;
        float _camBlend = 1f;
        static float _shakeStrength, _shakeLeft, _shakeLength = 1f;

        // Dodge
        float _dodgeLeft, _dodgeCooldown;
        Vector3 _dodgeDir;
        float _dodgeSpeed;
        int _lastSide = 1;
        bool _powerNotified;
        float _deflectMsgAt = -10f;

        // Clips (measured once in BuildBoss)
        Transform _armature, _fallAnchor;
        Transform[] _bones = new Transform[0];
        Vector3[] _boneP, _boneS, _bindP, _bindS;
        Quaternion[] _boneR, _bindR;
        float _slamImpact = 1.3f, _slamDrop, _clipYOffset; // seconds; metres (landed pose -> feet on the ground)
        bool _slamFalls;                                   // the landing clip brings the boss down from the sky itself
        float[] _muzzleShotTime = new float[0];            // when each barrel kicks back in the attack clip
        Vector3 _flameOffset;                              // fall anchor -> chest, root space

        static bool Paused => RoverInterior.IsInside || RoverInterior.IsTransitioning || GameOverScreen.IsShown;

        // ================================================================ lifecycle

        void Awake()
        {
            _rover = GetComponent<RoverController>();
            _fit = GetComponent<RoverAutoFit>();
        }

        void OnEnable()
        {
            Instance = this;
            OutpostProgress.Completed += OnOutpostCompleted;
            BossCannonBall.Dodged += OnBallDodged;
        }

        void OnDisable()
        {
            OutpostProgress.Completed -= OnOutpostCompleted;
            BossCannonBall.Dodged -= OnBallDodged;
            if (Instance == this) Instance = null;
        }

        void OnDestroy()
        {
            BossArena.Close();
            if (_hud) Destroy(_hud.gameObject);
        }

        void Start()
        {
            _cfg = BossConfig.Instance;
            _battery = GetComponent<RoverBattery>();
            _spawner = GetComponent<EnemySpawner>();
            _terrain = FindAnyObjectByType<Bobery.Terrain.TerrainGenerator>();
        }

        void OnOutpostCompleted(Bobery.Terrain.Outpost o, string reward, int count)
        {
            if (_phase != Phase.Idle || !_cfg) return;
            int known = OutpostProgress.KnownCount;
            int needed = known > 0 ? Mathf.Min(_cfg.outpostsBeforeBoss, known) : _cfg.outpostsBeforeBoss;
            if (count < needed) return;
            _phase = Phase.Incoming;
            _incomingAt = Time.time + _cfg.arrivalDelay;
            RoverHud.Notify("Massive object falling from orbit!");
        }

        /// <summary>Skips the outpost requirement and brings the boss in right away (testing).</summary>
        [ContextMenu("Start Boss Fight Now")]
        public void StartNow()
        {
            if (_phase != Phase.Idle && _phase != Phase.Incoming) return;
            if (!_cfg) _cfg = BossConfig.Instance;
            _phase = Phase.Incoming;
            _incomingAt = Time.time;
        }

        /// <summary>Cheat (Ctrl + \): every weapon, the boost engine, and the boss right away.</summary>
        void CheatShortcut()
        {
            var kb = Keyboard.current;
            if (kb == null || !kb.backslashKey.wasPressedThisFrame || !kb.ctrlKey.isPressed) return;
            foreach (var w in WeaponDatabase.Instance.weapons) if (w != null) WeaponLoadout.Unlock(w.id);
            RoverUpgrades.UnlockEngine();
            if (_phase == Phase.Idle)
            {
                StartNow();
                RoverHud.Notify("Cheat: all weapons + boost unlocked, boss incoming");
            }
            else RoverHud.Notify("Cheat: all weapons + boost unlocked");
        }

        void Update()
        {
            CheatShortcut();
            if (_phase == Phase.Idle || _phase == Phase.Defeated) return;
            if (_phase == Phase.Incoming)
            {
                if (CanBegin()) Begin();
                return;
            }
            if (GameOverScreen.IsShown) { _rover.ExtraVelocity = Vector3.zero; return; }

            Bobery.Audio.GameAudio.MarkCombat();
            if (InCutscene) _rover.Stop();
            UpdateDodge();
            UpdatePose();
            if (_phase == Phase.Exposed) UpdateExposed();
        }

        bool CanBegin()
        {
            if (Time.time < _incomingAt || Paused || TutorialTips.IsOpen) return false;
            if (_fit && !_fit.IsFitted) return false;
            var cam = Camera.main;
            return cam && cam.GetComponent<TankCamera>();
        }

        // ================================================================ setup

        void Begin()
        {
            _phase = Phase.Intro;
            _rover.Stop();
            foreach (var e in EnemyRover.All.ToArray()) if (e) Destroy(e.gameObject); // the boss brings its own

            Vector3 size = _fit && _fit.IsFitted ? _fit.WorldSize : new Vector3(4f, 3f, 5f);
            _roverSize = Mathf.Max(size.x, size.y, size.z);
            _roverWidth = Mathf.Max(size.x, size.z);
            _arenaRadius = _roverSize * _cfg.arenaVsRover;
            _ballRadius = _roverSize * 0.085f;

            BuildBoss();
            PickArena(out Vector3 center, out Vector3 bossPos);
            _root.SetPositionAndRotation(new Vector3(bossPos.x, GroundY(bossPos), bossPos.z), Quaternion.LookRotation(_facing));
            BossArena.Open(center, _arenaRadius, _root.position, _bossRadius);
            _wall = BossArenaWall.Build(center, _arenaRadius, size.y * _cfg.wallVsRover, _roverSize * 0.9f);

            SetupCamera(center);
            _hud = BossHud.Create(this);
            StartCoroutine(Fight());
        }

        /// <summary>Instantiates the model, scales it to <see cref="BossConfig.sizeVsRover"/> and finds the cannons and chest.</summary>
        void BuildBoss()
        {
            _root = new GameObject("Boss").transform;
            _pose = new GameObject("Pose").transform;
            _pose.SetParent(_root, false);

            GameObject model = _cfg.model ? Instantiate(_cfg.model) : Placeholder();
            model.name = "Model";
            foreach (var c in model.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var a in model.GetComponentsInChildren<Animator>(true)) Destroy(a); // clips are sampled by hand
            _model = model.transform;
            _model.SetParent(_pose, false);
            _model.localPosition = Vector3.zero;
            _model.localRotation = Quaternion.identity;
            _model.localScale = Vector3.one;
            foreach (var r in model.GetComponentsInChildren<SkinnedMeshRenderer>(true)) r.updateWhenOffscreen = true;
            _armature = FindDeep(_model, "Armature");
            if (_armature)
            {
                _bones = _armature.GetComponentsInChildren<Transform>(true);
                _fallAnchor = _armature.childCount > 0 ? _armature.GetChild(0) : _armature;
            }
            _boneP = new Vector3[_bones.Length];
            _boneS = new Vector3[_bones.Length];
            _boneR = new Quaternion[_bones.Length];
            CaptureBones(); // bind pose, the standing pose when there's no death clip
            _bindP = (Vector3[])_boneP.Clone();
            _bindR = (Quaternion[])_boneR.Clone();
            _bindS = (Vector3[])_boneS.Clone();
            RestPose();

            // Measure at scale 1, in the root's space.
            Bounds all = MeasureAll(null, out bool any);
            if (!any) all = new Bounds(Vector3.up * 5f, new Vector3(6f, 10f, 6f));
            float s = _cfg.sizeVsRover * _roverSize / Mathf.Max(0.01f, all.size.y);
            _bossHeight = all.size.y * s;
            Bounds lower = MeasureAll(b => b.center.y < all.min.y + all.size.y * 0.55f, out bool anyLower);
            if (!anyLower) lower = all;
            _bossRadius = Mathf.Max(Mathf.Abs(lower.min.x), Mathf.Abs(lower.max.x), Mathf.Abs(lower.min.z), Mathf.Abs(lower.max.z)) * s * 0.85f;
            // Cannons: the two long barrels on the arms ("Circle.002" / "Circle.004"); chest: the round torso ("Circle").
            var barrels = new List<Bounds>();
            var shotTimes = new List<float>();
            foreach (var part in new[] { "Circle.002", "Circle.004" })
            {
                Bounds barrel = MeasurePart(part, out bool found);
                if (!found) continue;
                barrels.Add(barrel);
                shotTimes.Add(_cfg.shootClip ? RecoilTime(part) : 0f);
            }
            RestPose();
            Bounds chest = MeasurePart("Circle", out bool hasChest);
            if (_cfg.slamClip) AnalyseSlam(all);
            RestPose();

            // Now scale it up (everything above was measured at scale 1).
            _model.localScale = Vector3.one * s;
            _model.localPosition = new Vector3(0f, -all.min.y * s - 0.15f, 0f); // feet on the ground
            Vector3 ToPose(Vector3 p) => new Vector3(p.x * s, (p.y - all.min.y) * s - 0.15f, p.z * s);
            _slamDrop *= s;

            // Muzzles in firing order (the barrel that kicks first in the attack clip comes first).
            var order = new List<int>();
            for (int i = 0; i < barrels.Count; i++) order.Add(i);
            order.Sort((x, y) => shotTimes[x].CompareTo(shotTimes[y]));
            var muzzles = new List<Transform>();
            var times = new List<float>();
            foreach (int i in order)
            {
                Bounds b = barrels[i];
                muzzles.Add(Marker("Muzzle", ToPose(new Vector3(b.center.x, b.center.y, b.max.z))));
                times.Add(shotTimes[i]);
            }
            if (muzzles.Count == 0)
                foreach (float side in new[] { -1f, 1f })
                {
                    muzzles.Add(Marker("Muzzle", ToPose(new Vector3(all.center.x + side * all.extents.x * 0.6f, all.min.y + all.size.y * 0.6f, all.max.z))));
                    times.Add(0f);
                }
            _muzzles = muzzles.ToArray();
            _muzzleShotTime = times.ToArray();
            _muzzleLights = new Light[_muzzles.Length];
            for (int i = 0; i < _muzzles.Length; i++)
            {
                var l = _muzzles[i].gameObject.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(1f, 0.45f, 0.12f);
                l.range = _roverSize * 1.5f;
                l.intensity = 0f;
                l.shadows = LightShadows.None;
                _muzzleLights[i] = l;
            }

            // Chest: front of the round torso; the weak point opens here.
            Vector3 chestPos = hasChest ? new Vector3(chest.center.x, chest.center.y, chest.max.z)
                                        : new Vector3(all.center.x, all.min.y + all.size.y * 0.62f, all.center.z + all.extents.z * 0.4f);
            _chest = Marker("Chest", ToPose(chestPos));

            // Armour: hits on the torso spark off and do nothing (until the weak point opens).
            var armorGo = new GameObject("Armour");
            armorGo.transform.SetParent(_pose, false);
            Vector3 torso = hasChest ? chest.center : new Vector3(all.center.x, all.min.y + all.size.y * 0.6f, all.center.z);
            armorGo.transform.localPosition = ToPose(torso);
            _armor = armorGo.AddComponent<CombatHealth>();
            float armorR = hasChest ? Mathf.Max(chest.extents.x, chest.extents.y) * s : _bossHeight * 0.15f;
            _armor.Setup(_cfg.displayName, 1000f, Vector3.zero, armorR, 0f);
            _armor.invulnerable = true;
            _armor.Deflected += OnArmorDeflected;

            // Flames on the way down ride along with the root bone (the landing clip moves the whole skeleton).
            if (!_fallAnchor) _fallAnchor = _model;
            _flameOffset = _root.InverseTransformVector(_chest.position - _fallAnchor.position);

            _model.gameObject.SetActive(false); // appears when it falls
        }

        static Transform FindDeep(Transform t, string name)
        {
            foreach (var c in t.GetComponentsInChildren<Transform>(true)) if (c.name == name) return c;
            return null;
        }

        /// <summary>When the barrel <paramref name="part"/> kicks back hardest in the attack clip (= when its ball leaves).</summary>
        float RecoilTime(string part)
        {
            var clip = _cfg.shootClip;
            float fps = clip.frameRate > 0f ? clip.frameRate : 24f;
            int frames = Mathf.Max(1, Mathf.RoundToInt(clip.length * fps));
            float prev = float.NaN, bestDrop = 0f, best = 0f;
            for (int f = 0; f <= frames; f++)
            {
                float t = Mathf.Min(clip.length, f / fps);
                clip.SampleAnimation(_model.gameObject, t);
                Bounds b = MeasurePart(part, out bool found);
                if (!found) break;
                if (!float.IsNaN(prev) && prev - b.max.z > bestDrop + 0.0001f) { bestDrop = prev - b.max.z; best = Mathf.Max(0f, (f - 1) / fps); }
                prev = b.max.z;
            }
            return best;
        }

        /// <summary>Landing clip: finds the touchdown moment and how far the landed pose's feet sit from the standing pose's.</summary>
        void AnalyseSlam(Bounds rest)
        {
            var clip = _cfg.slamClip;
            float fps = clip.frameRate > 0f ? clip.frameRate : 24f;
            int frames = Mathf.Max(1, Mathf.RoundToInt(clip.length * fps));
            var low = new float[frames + 1];
            float top = float.MinValue;
            for (int f = 0; f <= frames; f++)
            {
                clip.SampleAnimation(_model.gameObject, Mathf.Min(clip.length, f / fps));
                float y = float.MaxValue;
                foreach (var b in _bones) y = Mathf.Min(y, _root.InverseTransformPoint(b.position).y);
                low[f] = _bones.Length > 0 ? y : 0f;
                top = Mathf.Max(top, low[f]);
            }
            float end = low[frames];
            _slamFalls = top - end > rest.size.y * 0.5f;
            int impact = frames;
            if (_slamFalls)
                for (int f = 0; f <= frames; f++)
                    if (low[f] - end <= (top - end) * 0.02f) { impact = f; break; }
            _slamImpact = _slamFalls ? impact / fps : clip.length * _cfg.slamImpactAt;
            clip.SampleAnimation(_model.gameObject, _slamImpact);
            Bounds landed = MeasureAll(null, out bool any);
            _slamDrop = any ? rest.min.y - landed.min.y : 0f; // model units, scaled by the caller
        }

        void CaptureBones()
        {
            for (int i = 0; i < _bones.Length; i++)
            {
                _boneP[i] = _bones[i].localPosition;
                _boneR[i] = _bones[i].localRotation;
                _boneS[i] = _bones[i].localScale;
            }
        }

        /// <summary>Blends from the captured pose (w = 0) to the pose the skeleton is in now (w = 1).</summary>
        void BlendFromCaptured(float w)
        {
            for (int i = 0; i < _bones.Length; i++)
            {
                var b = _bones[i];
                b.localPosition = Vector3.Lerp(_boneP[i], b.localPosition, w);
                b.localRotation = Quaternion.Slerp(_boneR[i], b.localRotation, w);
                b.localScale = Vector3.Lerp(_boneS[i], b.localScale, w);
            }
        }

        Transform Marker(string name, Vector3 local)
        {
            var t = new GameObject(name).transform;
            t.SetParent(_pose, false);
            t.localPosition = local;
            return t;
        }

        /// <summary>Bounds (root space, current model scale) of every renderer whose own bounds pass <paramref name="filter"/> (null = all).</summary>
        Bounds MeasureAll(System.Func<Bounds, bool> filter, out bool any) => MeasureWhere(null, filter, out any);

        /// <summary>Bounds (root space, current model scale) of the renderer(s) called <paramref name="name"/>.</summary>
        Bounds MeasurePart(string name, out bool found) => MeasureWhere(r => r.name == name, null, out found);

        Bounds MeasureWhere(System.Func<Renderer, bool> pick, System.Func<Bounds, bool> filter, out bool any)
        {
            any = false;
            var result = new Bounds();
            var mesh = new Mesh();
            foreach (var r in _model.GetComponentsInChildren<Renderer>(true))
            {
                if (pick != null && !pick(r)) continue;
                if (!PartBounds(r, mesh, out Bounds b)) continue;
                if (filter != null && !filter(b)) continue;
                if (!any) { result = b; any = true; } else result.Encapsulate(b);
            }
            Destroy(mesh);
            return result;
        }

        /// <summary>One renderer's bounds in root space: exact (baked) for skinned meshes when the mesh is readable.</summary>
        bool PartBounds(Renderer r, Mesh scratch, out Bounds b)
        {
            b = default;
            Vector3[] verts = null;
            Matrix4x4 m = _root.worldToLocalMatrix * r.transform.localToWorldMatrix;
            try
            {
                if (r is SkinnedMeshRenderer smr && smr.sharedMesh)
                {
                    if (smr.sharedMesh.isReadable) { smr.BakeMesh(scratch, true); verts = scratch.vertices; }
                }
                else if (r is MeshRenderer && r.TryGetComponent<MeshFilter>(out var mf) && mf.sharedMesh && mf.sharedMesh.isReadable)
                    verts = mf.sharedMesh.vertices;
            }
            catch { verts = null; }

            if (verts != null && verts.Length > 0)
            {
                b = new Bounds(m.MultiplyPoint3x4(verts[0]), Vector3.zero);
                for (int i = 1; i < verts.Length; i++) b.Encapsulate(m.MultiplyPoint3x4(verts[i]));
                return true;
            }
            // Fallback: the renderer's world box.
            Bounds w = r.bounds;
            if (w.size.sqrMagnitude < 1e-6f) return false;
            b = new Bounds(_root.InverseTransformPoint(w.center), Vector3.zero);
            for (int i = 0; i < 8; i++)
                b.Encapsulate(_root.InverseTransformPoint(w.center + Vector3.Scale(w.extents,
                    new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1))));
            return true;
        }

        static GameObject Placeholder()
        {
            var root = new GameObject("BossPlaceholder");
            var dark = WeaponFx.Unlit(new Color(0.2f, 0.12f, 0.1f));
            void Prim(PrimitiveType t, Vector3 p, Vector3 s, string name)
            {
                var go = GameObject.CreatePrimitive(t);
                go.name = name;
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = p;
                go.transform.localScale = s;
                go.GetComponent<Renderer>().sharedMaterial = dark;
            }
            Prim(PrimitiveType.Capsule, new Vector3(0f, 5f, 0f), new Vector3(6f, 5f, 6f), "Body");
            Prim(PrimitiveType.Sphere, new Vector3(0f, 9f, 0f), new Vector3(4.5f, 4f, 4f), "Circle");
            Prim(PrimitiveType.Cylinder, new Vector3(-3.5f, 8f, 3f), new Vector3(1f, 2f, 1f), "Circle.002");
            Prim(PrimitiveType.Cylinder, new Vector3(3.5f, 8f, 3f), new Vector3(1f, 2f, 1f), "Circle.004");
            foreach (var t in root.GetComponentsInChildren<Transform>())
                if (t.name.StartsWith("Circle.00")) t.localRotation = Quaternion.Euler(90f, 0f, 0f);
            return root;
        }

        /// <summary>Standing pose: the death clip's first frame (or the model's bind pose without one).</summary>
        void RestPose()
        {
            if (_cfg.deathClip) { _cfg.deathClip.SampleAnimation(_model.gameObject, 0f); return; }
            if (_bindP == null) return;
            for (int i = 0; i < _bones.Length; i++)
            {
                _bones[i].localPosition = _bindP[i];
                _bones[i].localRotation = _bindR[i];
                _bones[i].localScale = _bindS[i];
            }
        }

        // ================================================================ arena + camera

        /// <summary>Puts the boss in front of the rover where the ring is mostly open, flat land (tries 16 headings).</summary>
        void PickArena(out Vector3 center, out Vector3 bossPos)
        {
            Vector3 rp = _rover.transform.position;
            Vector3 fwd = _rover.transform.forward; fwd.y = 0f;
            fwd = fwd.sqrMagnitude > 0.001f ? fwd.normalized : Vector3.forward;
            float R = _arenaRadius;
            float d = R * 0.65f; // rover -> boss

            float best = float.MinValue;
            center = rp; bossPos = rp + fwd * d; _facing = -fwd;
            for (int i = 0; i < 16; i++)
            {
                float yaw = i * 22.5f;
                Vector3 dir = Quaternion.Euler(0f, yaw, 0f) * fwd;
                Vector3 b = rp + dir * d;
                Vector3 f = -dir;                 // the boss faces the rover and the camera behind it
                Vector3 c = b + f * (R * 0.3f);
                float score = ArenaScore(c, b, f) - Mathf.Min(yaw, 360f - yaw) / 180f * 0.15f; // prefer straight ahead
                if (score > best) { best = score; center = c; bossPos = b; _facing = f; }
            }
        }

        float ArenaScore(Vector3 c, Vector3 boss, Vector3 f)
        {
            float R = _arenaRadius;
            int ok = 0, total = 0;
            float hMin = float.MaxValue, hMax = float.MinValue;
            foreach (float ring in new[] { 0.15f, 0.4f, 0.65f, 0.9f })
                for (int k = 0; k < 16; k++)
                {
                    float a = k * Mathf.PI * 2f / 16f;
                    Vector3 p = c + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (R * ring);
                    total++;
                    if (EnemyRover.Walkable(_terrain, p)) ok++;
                    float h = GroundY(p);
                    hMin = Mathf.Min(hMin, h); hMax = Mathf.Max(hMax, h);
                }
            float score = (float)ok / total;
            if (!EnemyRover.Walkable(_terrain, boss)) score -= 0.3f;
            if (!InsideMap(CameraSpot(c, f), 0f)) score -= 0.5f;
            if (!InsideMap(c, R)) score -= 0.3f;
            foreach (var o in OutpostProgress.Unfinished)
                if (o && Flat(o.Center, c) < R + 70f) score -= 0.5f; // its turrets would join in
            score -= Mathf.Clamp01((hMax - hMin) / (R * 0.5f)) * 0.3f;
            return score;
        }

        Vector3 CameraSpot(Vector3 c, Vector3 f)
        {
            Vector3 p = c + f * (_arenaRadius * _cfg.cameraDistance);
            p.y = GroundY(c) + _arenaRadius * _cfg.cameraHeight;
            return p;
        }

        bool InsideMap(Vector3 p, float margin)
        {
            if (!_terrain) return true;
            Vector3 l = _terrain.transform.InverseTransformPoint(p);
            float half = _terrain.mapSize * 0.5f - margin;
            return Mathf.Abs(l.x) <= half && Mathf.Abs(l.z) <= half;
        }

        /// <summary>Static camera in front of the boss looking at it (raised until hills don't block the view).</summary>
        void SetupCamera(Vector3 center)
        {
            _cam = Camera.main;
            _tankCam = _cam ? _cam.GetComponent<TankCamera>() : null;
            if (_tankCam) _tankCam.enabled = false;
            if (!_cam) return;

            // Aim between the arena centre and the boss, low enough that the near wall stays in frame.
            Vector3 look = Vector3.Lerp(center, _root.position, 0.3f);
            look.y = GroundY(center) + _bossHeight * 0.15f;
            Vector3 pos = CameraSpot(center, _facing);
            for (int tries = 0; tries < 12 && Blocked(pos, look); tries++) pos.y += _arenaRadius * 0.08f;
            pos.y = Mathf.Max(pos.y, GroundY(pos) + _roverSize);

            _camPos = pos;
            _camRot = Quaternion.LookRotation(look - pos);
            _camFromPos = _cam.transform.position;
            _camFromRot = _cam.transform.rotation;
            _camBlend = 0f;
        }

        bool Blocked(Vector3 from, Vector3 to)
        {
            for (int i = 1; i < 20; i++)
            {
                Vector3 p = Vector3.Lerp(from, to, i / 20f);
                if (GroundY(p) > p.y - 2f) return true;
            }
            return false;
        }

        void LateUpdate()
        {
            if (!_cam || _phase < Phase.Intro) return;
            float dt = Time.deltaTime;
            _camBlend = Mathf.MoveTowards(_camBlend, 1f, dt / 1.6f);
            float e = Mathf.SmoothStep(0f, 1f, _camBlend);
            Vector3 pos = Vector3.Lerp(_camFromPos, _camPos, e);
            Quaternion rot = Quaternion.Slerp(_camFromRot, _camRot, e);

            if (_shakeLeft > 0f)
            {
                _shakeLeft = Mathf.Max(0f, _shakeLeft - dt);
                float k = _shakeStrength * (_shakeLeft / _shakeLength) * _arenaRadius * 0.035f;
                float t = Time.time * 22f;
                pos += rot * new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, t) - 0.5f, 0f) * (2f * k);
            }
            _cam.transform.SetPositionAndRotation(pos, rot);

            // The weak point's ring always faces the camera.
            if (_weakRing) _weakRing.transform.rotation = Quaternion.LookRotation(_cam.transform.forward);
        }

        /// <summary>Camera shake (strength 0..1).</summary>
        public static void Shake(float strength, float seconds)
        {
            if (_shakeLeft > 0f && _shakeStrength * (_shakeLeft / _shakeLength) > strength) return;
            _shakeStrength = strength;
            _shakeLeft = _shakeLength = Mathf.Max(0.05f, seconds);
        }

        // ================================================================ the fight

        IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                if (!Paused) t += Time.deltaTime;
                yield return null;
            }
        }

        IEnumerator Fight()
        {
            yield return Intro();
            while (true)
            {
                yield return Escorts();
                yield return Barrage();
                yield return Exposed();
                if (_cyclesDone >= _cfg.cycles) break;
                yield return Recover();
            }
            yield return Death();
        }

        IEnumerator Intro()
        {
            _phase = Phase.Intro;
            _hud.Letterbox(true);
            Bobery.Audio.GameAudio.PlayBossAlarm();
            _hud.Title("[WARN] ORBITAL IMPACT", "INCOMING", RoverUI.Red, 2.6f);
            yield return Wait(2f);

            // Falls out of the sky. The landing clip brings it down by itself; without one it's dropped in code.
            var slam = _cfg.slamClip;
            _model.gameObject.SetActive(true);
            float fallHeight = Mathf.Max(220f, _bossHeight * 6f);
            float impact = slam ? Mathf.Max(0.3f, _slamImpact) : 1.3f;
            bool dropInCode = !slam || !_slamFalls;
            _clipYOffset = slam ? _slamDrop : 0f; // landed pose: feet on the ground
            var fx = new GameObject("BossFallFx").transform;
            var flame = WeaponFx.ExhaustFlame(fx, Vector3.zero, _bossHeight * 0.12f);
            var smoke = WeaponFx.ExhaustSmoke(fx, Vector3.zero, _bossHeight * 0.1f);
            for (float t = 0f; t < impact; t += Time.deltaTime)
            {
                float k = t / impact;
                _fallY = dropInCode ? fallHeight * (1f - k * k) : 0f;
                if (slam) slam.SampleAnimation(_model.gameObject, t);
                fx.position = _fallAnchor.position + _root.TransformVector(_flameOffset);
                yield return null;
            }
            _fallY = 0f;
            if (slam) slam.SampleAnimation(_model.gameObject, impact);
            if (flame) Destroy(flame.gameObject);
            if (smoke) { var em = smoke.emission; em.enabled = false; }
            Destroy(fx.gameObject, 3f);

            // Slam.
            Vector3 feet = _root.position;
            if (!slam) _impactAge = 0f; // procedural squash only when there's no landing clip
            WeaponFx.Explosion(feet + Vector3.up * _bossHeight * 0.1f, _bossRadius * 0.7f);
            WeaponFx.Shockwave(feet, _arenaRadius * 0.9f);
            for (int i = 0; i < 10; i++)
            {
                float a = i * Mathf.PI * 2f / 10f;
                Vector3 p = feet + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * _bossRadius * 1.2f;
                WeaponFx.Dust(new Vector3(p.x, GroundY(p), p.z), Vector3.up, _bossRadius * 0.35f);
            }
            Shake(1f, 1.1f);

            // The rest of the landing clip plays out while the ground heaves up into a ring around the arena.
            float end = slam ? slam.length : impact;
            bool wallUp = false;
            for (float t = impact; t < end || !wallUp; t += Time.deltaTime)
            {
                if (slam) slam.SampleAnimation(_model.gameObject, Mathf.Min(t, end));
                if (!wallUp && t >= impact + 0.35f) { wallUp = true; RaiseWall(); }
                yield return null;
            }
            if (slam) yield return BlendToRest(0.5f); // ease from the landing's last pose into the standing pose
            yield return Wait(1.2f);

            _hud.Title("[BOSS]", _cfg.displayName, RoverUI.Red, 2.6f);
            _recoil = 1.3f; // rears up
            Shake(0.3f, 0.8f);
            yield return Wait(2.4f);
            _hud.Letterbox(false);
        }

        void RaiseWall()
        {
            _wall.Rise(1.4f);
            for (int i = 0; i < 24; i++)
            {
                float a = i * Mathf.PI * 2f / 24f;
                Vector3 p = BossArena.Center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * (_arenaRadius + _roverSize * 0.4f);
                WeaponFx.Dust(new Vector3(p.x, GroundY(p), p.z), Vector3.up, _roverSize * 0.35f);
            }
            Shake(0.6f, 1.4f);
        }

        /// <summary>Crossfades from the skeleton's current pose (and landing height offset) into the standing pose.</summary>
        IEnumerator BlendToRest(float seconds)
        {
            CaptureBones();
            float from = _clipYOffset;
            for (float t = 0f; t < seconds; t += Time.deltaTime)
            {
                float w = Mathf.SmoothStep(0f, 1f, t / seconds);
                RestPose();
                BlendFromCaptured(w);
                _clipYOffset = Mathf.Lerp(from, 0f, w);
                yield return null;
            }
            RestPose();
            _clipYOffset = 0f;
        }

        IEnumerator Escorts()
        {
            _phase = Phase.Escorts;
            _escorts.Clear();
            _escortsTotal = 0;
            _hud.Flash("ESCORTS INBOUND", RoverUI.Red);
            yield return Wait(0.8f);
            for (int i = 0; i < _cfg.escorts; i++)
            {
                Vector3 p = EscortSpot(i);
                var e = _spawner ? _spawner.SpawnEscort(p) : null;
                if (e)
                {
                    _escorts.Add(e);
                    _escortsTotal++;
                    WeaponFx.Explosion(e.transform.position + Vector3.up, _roverSize * 0.25f);
                    WeaponFx.Dust(e.transform.position, Vector3.up, _roverSize * 0.3f);
                }
                yield return Wait(0.35f);
            }
            while (EscortsAlive > 0) yield return null;
            yield return Wait(1f);
        }

        /// <summary>Escorts drop in at the boss's feet, alternating left / right.</summary>
        Vector3 EscortSpot(int i)
        {
            Vector3 right = Vector3.Cross(Vector3.up, _facing);
            float side = i % 2 == 0 ? 1f : -1f;
            Vector3 p = _root.position + right * side * (_bossRadius + _roverSize * 0.9f) + _facing * (_roverSize * (0.8f + i / 2));
            if (BossArena.Contains(p, _roverSize * 0.5f) && EnemyRover.Walkable(_terrain, p)) return p;
            for (int tries = 0; tries < 30; tries++)
            {
                Vector2 r = Random.insideUnitCircle * _arenaRadius * 0.8f;
                Vector3 q = BossArena.Center + new Vector3(r.x, 0f, r.y);
                if (BossArena.Contains(q, _roverSize * 0.5f) && EnemyRover.Walkable(_terrain, q) && Flat(q, _rover.transform.position) > _roverSize * 2f)
                    return q;
            }
            return p;
        }

        IEnumerator Barrage()
        {
            _phase = Phase.Barrage;
            for (_volley = 1; _volley <= _cfg.volleys; _volley++)
            {
                // Wind up: the cannons glow.
                _hud.Flash(_volley == 1 ? "INCOMING FIRE - [SHIFT] TO DODGE" : "INCOMING!", RoverUI.Yellow);
                for (float t = 0f; t < 0.9f; t += Paused ? 0f : Time.deltaTime) { _charge = t / 0.9f; yield return null; }

                // One shot = one play of the attack clip; the ball leaves when that arm's barrel kicks back
                // (arms take turns). Without a clip: procedural recoil, a ball every shotSpacing seconds.
                var clip = _cfg.shootClip;
                float spacing = clip ? Mathf.Max(_cfg.shotSpacing, clip.length) : _cfg.shotSpacing;
                for (int s = 0; s < _cfg.shotsPerVolley; s++)
                {
                    int arm = _muzzles.Length > 0 ? s % _muzzles.Length : 0;
                    float at = clip && arm < _muzzleShotTime.Length ? Mathf.Clamp(_muzzleShotTime[arm], 0f, clip.length) : 0f;
                    if (clip) _shootClipT = 0f;
                    if (at > 0f) yield return Wait(at);
                    Fire(arm);
                    yield return Wait(Mathf.Max(0.05f, spacing - at));
                }
                _charge = 0f;
                if (_volley < _cfg.volleys) yield return Wait(_cfg.volleyPause);
            }
            _volley = _cfg.volleys;
            while (BossCannonBall.InFlight.Count > 0) yield return null;
            yield return Wait(0.8f);
        }

        void Fire(int arm)
        {
            if (_muzzles.Length == 0) return;
            Transform m = _muzzles[arm % _muzzles.Length];
            Vector3 from = m.position;
            float dist = Flat(from, _rover.transform.position);
            float k = Mathf.InverseLerp(_arenaRadius * 0.3f, _arenaRadius * 1.6f, dist);
            float flight = Mathf.Lerp(_cfg.ballFlightTime.x, _cfg.ballFlightTime.y, k);
            BossCannonBall.Launch(from, _rover, flight, _ballRadius, _cfg.ballDamage);

            Vector3 dir = (_rover.transform.position - from).normalized;
            WeaponFx.MuzzleFlash(from, dir, _ballRadius * 2f, new Color(1f, 0.5f, 0.15f));
            Bobery.Audio.GameAudio.PlayBossShot(from);
            if (!_cfg.shootClip) _recoil = 1f; // the attack clip has its own kick
            Shake(0.25f, 0.2f);
        }

        IEnumerator Exposed()
        {
            _phase = Phase.Exposed;
            _slumpTarget = 1f;
            _armor.gameObject.SetActive(false);
            WeaponFx.Explosion(_chest.position, _roverSize * 0.35f);
            Shake(0.5f, 0.6f);
            yield return Wait(0.9f);

            WeaponLoadout.Unlock(WeaponDef.RailGunId);
            SpawnWeakPoint();
            _hud.Flash("WEAK POINT EXPOSED - USE THE RAILGUN", RoverUI.Accent);
            _powerNotified = false;
            while (_weak) yield return null;

            _cyclesDone++;
            WeaponFx.Explosion(_lastWeakPos, _roverSize * 0.6f);
            WeaponFx.Shockwave(_root.position, _arenaRadius * 0.5f);
            Shake(0.9f, 0.8f);
            _recoil = 1.6f; // knocked back
            int left = _cfg.cycles - _cyclesDone;
            _hud.Flash(left > 0 ? $"CORE HIT!  {left} MORE TO GO" : "CORE DESTROYED!", RoverUI.Green);
            yield return Wait(1.4f);
        }

        void SpawnWeakPoint()
        {
            var go = new GameObject("WeakPoint");
            go.transform.SetParent(_pose, false);
            float r = Mathf.Max(_roverSize * 0.2f, _bossHeight * 0.05f);
            go.transform.localPosition = _chest.localPosition + Vector3.forward * (r * 0.5f);
            _weak = go.AddComponent<CombatHealth>();
            _weak.Setup("Weak point", 1f, Vector3.zero, r, 0f);
            _weak.onlyWeapon = WeaponDef.RailGunId;
            _weak.Deflected += OnWeakDeflected;
            _lastWeakPos = go.transform.position;

            int layer = WeaponFx.HiddenLayer;
            var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(core.GetComponent<Collider>());
            core.name = "Core";
            core.layer = layer;
            core.transform.SetParent(go.transform, false);
            core.transform.localScale = Vector3.one * r * 0.9f / Mathf.Max(0.0001f, go.transform.lossyScale.x);
            core.GetComponent<Renderer>().sharedMaterial = WeaponFx.Unlit(new Color(0.55f, 0.95f, 1f));
            _weakVisual = core.transform;

            var ringGo = new GameObject("Ring") { layer = layer };
            ringGo.transform.SetParent(go.transform, false);
            _weakRing = ringGo.AddComponent<LineRenderer>();
            _weakRing.useWorldSpace = false;
            _weakRing.loop = true;
            _weakRing.positionCount = 40;
            _weakRing.sharedMaterial = WeaponFx.LineMaterial();
            _weakRing.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _weakRing.startColor = _weakRing.endColor = RoverUI.Accent;
            _weakRing.widthMultiplier = r * 0.12f;
            for (int i = 0; i < 40; i++)
            {
                float a = i * Mathf.PI * 2f / 40f;
                _weakRing.SetPosition(i, new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * r * 1.6f);
            }

            _weakLight = go.AddComponent<Light>();
            _weakLight.type = LightType.Point;
            _weakLight.color = RoverUI.Accent;
            _weakLight.range = r * 8f;
            _weakLight.intensity = 4f;
            _weakLight.shadows = LightShadows.None;
        }

        void UpdateExposed()
        {
            if (_weak)
            {
                _lastWeakPos = _weak.transform.position;
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 6f);
                if (_weakVisual) _weakVisual.localScale = Vector3.one * Mathf.Lerp(0.8f, 1.1f, pulse) * _weak.radius * 0.9f / Mathf.Max(0.0001f, _weak.transform.lossyScale.x);
                if (_weakRing) _weakRing.transform.localScale = Vector3.one * Mathf.Lerp(1f, 1.25f, 1f - pulse);
                if (_weakLight) _weakLight.intensity = Mathf.Lerp(2f, 6f, pulse);
                EnsureRailgunPower();
            }

            // Sparks crackle over the stunned boss.
            if (!Paused && Random.value < Time.deltaTime * 2.5f)
            {
                Vector3 p = _chest.position + Random.insideUnitSphere * _bossHeight * 0.25f;
                WeaponFx.Sparks(p, (p - _root.position).normalized, _roverSize * 0.2f);
            }
        }

        /// <summary>The fight can't be won without a railgun shot, so keep at least one shot of charge in the battery.</summary>
        void EnsureRailgunPower()
        {
            if (!_battery) return;
            var def = WeaponDatabase.Instance.Get(WeaponDef.RailGunId);
            if (def == null) return;
            float cost = def.ShotCost(_battery.capacity);
            if (_battery.Charge + 0.0001f >= cost) return;
            _battery.Recharge(cost * 1.05f - _battery.Charge);
            if (!_powerNotified) { _powerNotified = true; RoverHud.Notify("Emergency power routed to the railgun"); }
        }

        IEnumerator Recover()
        {
            _phase = Phase.Recover;
            _slumpTarget = 0f;
            _hud.Flash("IT'S GETTING BACK UP", RoverUI.Yellow);
            yield return Wait(2.2f);
            _armor.gameObject.SetActive(true);
        }

        IEnumerator Death()
        {
            _phase = Phase.Dying;
            _hud.Letterbox(true);
            _slumpTarget = 0f;
            _armor.gameObject.SetActive(false);

            // Chain of blasts across the body.
            for (int i = 0; i < 7; i++)
            {
                Vector3 p = _chest.position + Random.insideUnitSphere * _bossHeight * 0.3f;
                WeaponFx.Explosion(p, _roverSize * Random.Range(0.3f, 0.6f));
                Shake(0.5f, 0.3f);
                yield return Wait(0.2f);
            }

            // Fall over.
            _poseFrozen = true;
            _pose.localPosition = Vector3.zero;
            _pose.localRotation = Quaternion.identity;
            _pose.localScale = Vector3.one;
            var clip = _cfg.deathClip;
            float speed = Mathf.Max(0.05f, _cfg.deathClipSpeed);
            float length = clip ? clip.length : 1.6f;
            bool landed = false;
            for (float t = 0f; t < length; t += Time.deltaTime * speed)
            {
                if (clip) clip.SampleAnimation(_model.gameObject, t);
                else _pose.localRotation = Quaternion.Euler(-90f * Mathf.Pow(t / length, 2f), 0f, 0f);
                if (!landed && t >= length * 0.85f) { landed = true; Landed(); }
                if (Random.value < Time.deltaTime * 4f)
                    WeaponFx.Explosion(_chest.position + Random.insideUnitSphere * _bossHeight * 0.2f, _roverSize * 0.4f);
                yield return null;
            }
            if (clip) clip.SampleAnimation(_model.gameObject, length);
            if (!landed) Landed();

            _hud.Title("[ OK ] TARGET DOWN", _cfg.displayName + " DESTROYED", RoverUI.Green, 3f);
            yield return Wait(3.2f);
            _phase = Phase.Defeated;
            _hud.Letterbox(false);
            BossArena.Close();
            GameOverScreen.ShowVictory("Colossus destroyed",
                "The war machine from orbit lies in pieces. The wasteland is yours.");
        }

        void Landed()
        {
            Vector3 back = _root.position - _facing * (_bossHeight * 0.55f);
            back.y = GroundY(back);
            WeaponFx.Shockwave(back, _arenaRadius * 0.9f);
            WeaponFx.Explosion(back + Vector3.up * _roverSize * 0.3f, _bossRadius * 0.6f);
            for (int i = 0; i < 6; i++)
            {
                Vector3 p = Vector3.Lerp(_root.position, back, i / 5f) + Random.insideUnitSphere * _bossRadius * 0.4f;
                WeaponFx.Dust(new Vector3(p.x, GroundY(p), p.z), Vector3.up, _bossRadius * 0.4f);
            }
            Shake(1f, 1.3f);
        }

        // ================================================================ procedural motion

        void UpdatePose()
        {
            if (!_pose || _poseFrozen) return;
            float dt = Paused ? 0f : Time.deltaTime;
            _recoil = Mathf.MoveTowards(_recoil, 0f, dt * 3.5f);
            _slump = Mathf.MoveTowards(_slump, _slumpTarget, dt * 0.9f);
            _impactAge += dt;
            float h = _bossHeight;

            float breath = Mathf.Sin(Time.time * 1.4f) * 0.006f * h * (1f - _slump);
            float shiver = _phase == Phase.Exposed ? Mathf.Sin(Time.time * 37f) * 0.15f : 0f;
            float slumpE = Mathf.SmoothStep(0f, 1f, _slump);
            _pose.localPosition = new Vector3(0f, _fallY + _clipYOffset + breath - slumpE * h * 0.08f, -_recoil * h * 0.02f);
            _pose.localRotation = Quaternion.Euler(-_recoil * 6f + slumpE * 14f + shiver, 0f, Mathf.Sin(Time.time * 0.7f) * 0.6f);

            // Landing squash: flattens, springs back.
            float k = _impactAge < 2f ? Mathf.Exp(-5f * _impactAge) * Mathf.Cos(_impactAge * 13f) : 0f;
            _pose.localScale = new Vector3(1f + 0.09f * k, 1f - 0.15f * k, 1f + 0.09f * k);

            float glow = _phase == Phase.Barrage ? Mathf.Max(_charge, Mathf.Clamp01(_recoil)) : 0f;
            foreach (var l in _muzzleLights) if (l) l.intensity = glow * 8f;

            if (_cfg.shootClip)
            {
                if (_shootClipT < _cfg.shootClip.length)
                {
                    _shootClipT += dt;
                    _cfg.shootClip.SampleAnimation(_model.gameObject, Mathf.Min(_shootClipT, _cfg.shootClip.length));
                    if (_shootClipT >= _cfg.shootClip.length) RestPose();
                }
            }
        }

        // ================================================================ dodge

        void UpdateDodge()
        {
            float dt = Time.deltaTime;
            if (_dodgeLeft > 0f)
            {
                _dodgeLeft = Mathf.Max(0f, _dodgeLeft - dt);
                float t = 1f - _dodgeLeft / Mathf.Max(0.01f, _cfg.dodgeDuration);
                _rover.ExtraVelocity = _dodgeDir * (_dodgeSpeed * Mathf.Sin(Mathf.PI * t));
                if (_dodgeLeft <= 0f) _rover.ExtraVelocity = Vector3.zero;
            }
            if (_dodgeCooldown > 0f) _dodgeCooldown = Mathf.Max(0f, _dodgeCooldown - dt);

            var kb = Keyboard.current;
            if (kb == null || !CanDodge || TutorialTips.IsOpen) return;
            if (kb.leftShiftKey.wasPressedThisFrame || kb.rightShiftKey.wasPressedThisFrame) TryDodge();
        }

        /// <summary>Dashes sideways (relative to the boss) and breaks every cannon ball's lock.</summary>
        public bool TryDodge()
        {
            if (!DodgeReady) return false;
            Vector3 p = _rover.transform.position;
            Vector3 toBoss = _root.position - p; toBoss.y = 0f;
            toBoss = toBoss.sqrMagnitude > 0.01f ? toBoss.normalized : -_facing;
            Vector3 side = Vector3.Cross(Vector3.up, toBoss);
            float dist = _roverWidth * _cfg.dodgeDistance;

            // Alternate sides; if that's blocked (against the wall) try the other side, the diagonals, back / forward,
            // and finally a shorter hop.
            Vector3 a = side * -_lastSide, b = side * _lastSide;
            var options = new[]
            {
                a, b, (a + toBoss).normalized, (b + toBoss).normalized, (a - toBoss).normalized, (b - toBoss).normalized,
                toBoss, -toBoss,
            };
            for (int i = 0; i < options.Length * 2; i++)
            {
                if (i == options.Length) dist *= 0.55f;
                Vector3 dir = options[i % options.Length];
                if (!Clear(p, dir, dist)) continue;
                int o = i % options.Length;
                if (o == 0 || o == 2 || o == 4) _lastSide = -_lastSide; // went to side 'a'
                _dodgeDir = dir;
                _dodgeSpeed = dist * Mathf.PI / (2f * Mathf.Max(0.05f, _cfg.dodgeDuration)); // sine profile covers 'dist'
                _dodgeLeft = _cfg.dodgeDuration;
                _dodgeCooldown = _cfg.dodgeCooldown + _cfg.dodgeDuration;
                foreach (var ball in BossCannonBall.InFlight) if (ball) ball.BreakLock();

                float hgt = _fit && _fit.IsFitted ? _fit.WorldSize.y : 3f;
                WeaponFx.MuzzleFlash(p + Vector3.up * hgt * 0.35f - _dodgeDir * _roverWidth * 0.5f, -_dodgeDir, _roverSize * 0.25f, RoverUI.Accent);
                WeaponFx.Dust(new Vector3(p.x, GroundY(p), p.z), Vector3.up, _roverSize * 0.25f);
                return true;
            }
            _hud.Flash("NO ROOM TO DODGE", RoverUI.Red);
            _dodgeCooldown = 0.25f;
            return false;
        }

        bool Clear(Vector3 from, Vector3 dir, float dist)
        {
            float margin = _roverWidth * 0.5f;
            for (int k = 1; k <= 4; k++)
            {
                Vector3 q = from + dir * (dist * k / 4f);
                if (!BossArena.Contains(q, margin) || !EnemyRover.Walkable(_terrain, q)) return false;
            }
            return true;
        }

        void OnBallDodged(Vector3 at) { if (_hud) _hud.Flash("DODGED!", RoverUI.Green); }

        void OnArmorDeflected(CombatHealth h)
        {
            if (Time.time - _deflectMsgAt < 3f || !_hud) return;
            _deflectMsgAt = Time.time;
            _hud.Flash("ARMOUR TOO THICK - WAIT FOR AN OPENING", RoverUI.Yellow);
        }

        void OnWeakDeflected(CombatHealth h)
        {
            if (Time.time - _deflectMsgAt < 1.5f || !_hud) return;
            _deflectMsgAt = Time.time;
            _hud.Flash("NO EFFECT - ONLY THE RAILGUN PIERCES IT", RoverUI.Red);
        }

        // ================================================================ helpers

        float GroundY(Vector3 p) => EnemyRover.GroundY(_terrain, p);
        static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() { Instance = null; _shakeLeft = 0f; }
    }
}
