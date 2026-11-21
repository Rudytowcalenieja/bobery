#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Bobery.Rover;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Bobery.Trailer
{
    /// <summary>
    /// Plays and records the ~1 minute game trailer (Bobery > Trailer > Record Trailer).
    ///
    ///  0.0  cabin: pilot in the seat, camera on his right shoulder, turbulence, sad music, narrator starts
    ///  2.0  camera glides behind the seat
    ///  3.5  1 s of black
    ///  4.5  outside: camera starts inside the rover and rises to the 45° chase view as the narration ends
    ///  +3s  hold
    ///       key mechanics (route, hull breaches, weapon swap, outpost raid), 3 boss clips, end card.
    ///
    /// The game runs normally; this only scripts it (camera, rover orders, aiming, dodges) and keeps the
    /// rover alive. Shots are separated by hard cuts: the game keeps running between them, unrecorded.
    /// </summary>
    [DefaultExecutionOrder(32000)]
    public class TrailerDirector : MonoBehaviour
    {
        public const string RecordKey = "Bobery.Trailer.Record";
        public const string VoicePath = "Assets/Sounds/lector.mp3";
        public const string SitterPath = "Assets/Models/player-sitting.fbx";
        public static string OutputFile => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Recordings", "Dead_Reckoning_Trailer.mp4"));
        public static string StillsFolder => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Recordings", "TrailerStills"));

        const float VoiceStart = 0.4f;
        const float HoldSeconds = 3f;
        const float NarratorGain = 3f; // AudioSource.volume can't go above 1

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (!SessionState.GetBool(RecordKey, false)) return;
            SessionState.SetBool(RecordKey, false);
            if (!FindAnyObjectByType<RoverController>())
            {
                Debug.LogError("[Trailer] Open the game scene (Sebastian) before recording.");
                return;
            }
            new GameObject("TrailerDirector").AddComponent<TrailerDirector>();
        }

        // ================================================================ state

        RoverController _rover;
        RoverAutoFit _fit;
        RoverHealth _health;
        RoverBattery _battery;
        FuelTank _tank;
        EnemySpawner _spawner;
        Camera _mainCam;
        TankCamera _tankCam;
        TrailerCapture _cap;

        AudioSource _voice, _sad, _fight;
        readonly List<AudioSource> _music = new List<AudioSource>();

        // Overlay
        Canvas _canvas;
        Image _black, _barTop, _barBottom;
        RectTransform _caption;
        TextMeshProUGUI _capTitle, _capSub, _endTitle, _endSub;
        CanvasGroup _capGroup, _endGroup;
        float _blackAlpha, _bars;

        // Per-frame jobs that must run after the game's own cameras (LateUpdate).
        Action _late;
        bool _cinematic;                                   // hide the game's HUD
        readonly HashSet<Canvas> _hiddenCanvases = new HashSet<Canvas>();

        // Scripted aim
        Func<Vector3?> _aimAt;
        Vector2 _aimScreen;
        bool _aimFire, _aimLocked;

        // Camera blend (hand-over from a scripted pose to the game's camera)
        Vector3 _blendFromPos;
        Quaternion _blendFromRot;
        float _blendT = 1f, _blendTime = 1f;

        static readonly PropertyInfo HealthProp = typeof(RoverHealth).GetProperty("Health");
        static readonly FieldInfo RoverYaw = typeof(RoverController).GetField("_yaw", BindingFlags.NonPublic | BindingFlags.Instance);
        static readonly FieldInfo RoverSpeed = typeof(RoverController).GetField("_currentSpeed", BindingFlags.NonPublic | BindingFlags.Instance);

        float _origTopSpeed;

        // ================================================================ lifecycle

        void Start()
        {
            _rover = FindAnyObjectByType<RoverController>();
            _fit = _rover.GetComponent<RoverAutoFit>();
            _health = _rover.GetComponent<RoverHealth>();
            _battery = _rover.GetComponent<RoverBattery>();
            _tank = _rover.GetComponent<FuelTank>();
            _spawner = _rover.GetComponent<EnemySpawner>();
            _mainCam = Camera.main;
            _tankCam = _mainCam ? _mainCam.GetComponent<TankCamera>() : null;
            _origTopSpeed = _rover.maxForwardSpeed;

            _cap = gameObject.AddComponent<TrailerCapture>();
            _cap.StillsFolder = StillsFolder;
            if (Directory.Exists(StillsFolder)) foreach (var f in Directory.GetFiles(StillsFolder)) File.Delete(f);

            BuildAudio();
            BuildOverlay();
            RoverTurret.AimOverride = AimOverride;
            StartCoroutine(Run());
        }

        void OnDestroy()
        {
            RoverTurret.AimOverride = null;
            Time.timeScale = 1f;
        }

        void Update()
        {
            KeepAlive();
            UpdateAim();
        }

        void LateUpdate()
        {
            if (_cinematic) HideHud(); else ShowHud();
            HideSidePanels();
            _late?.Invoke();
            ApplyBlend();
            ApplyOverlay();
        }

        /// <summary>The rover can't die, run dry or run flat while filming.</summary>
        void KeepAlive()
        {
            if (_health && _health.Health < _health.maxHealth * 0.45f) HealthProp.SetValue(_health, _health.maxHealth * 0.45f);
            if (_battery && _battery.Fraction < 0.9f) _battery.Recharge(_battery.capacity);
            if (_tank && _tank.Fraction < 0.8f) _tank.AddDirect(_tank.capacity);
        }

        // ================================================================ timeline

        IEnumerator Run()
        {
            while (!_fit.IsFitted || Time.frameCount < 30) yield return null;
            yield return Frames(20);

            MuteGameMusic();
            SetSpawning(false);
            PrepareIntroWalk();
            _cap.Begin(OutputFile);
            Debug.Log($"[Trailer] Recording to {OutputFile}");

            // ---------------------------------------------------------------- 1. cabin
            RoverInterior.Enter();
            while (!RoverInterior.IsInside) yield return null;
            yield return Frames(3);
            var room = FindAnyObjectByType<InteriorRoom>();
            var icam = room.Cam;
            var cabin = icam.transform.parent;
            var camHome = (icam.transform.localPosition, icam.transform.localRotation, icam.rect, icam.fieldOfView);
            var crewRenderers = room.Repairs.Crew ? room.Repairs.Crew.GetComponentsInChildren<Renderer>() : new Renderer[0];
            foreach (var r in crewRenderers) r.enabled = false;
            Transform seat = cabin.GetComponentsInChildren<Transform>().Where(t => t.name == "Seat").OrderBy(t => t.localPosition.x).First();
            var sitter = SpawnSitter(seat, room);
            var lights = cabin.GetComponentsInChildren<Light>().Select(l => (l, l.intensity)).ToArray();

            _cinematic = true;
            _bars = 1f;
            _blackAlpha = 1f;
            _sad.volume = 0f;
            _sad.Play();
            bool voiceOn = false;

            // Points on the seated model (its own units: head top ~2.2, right shoulder at +x).
            Vector3 headP = sitter.transform.TransformPoint(new Vector3(0f, 2.0f, 0f));
            Vector3 shoulderP = sitter.transform.TransformPoint(new Vector3(0.62f, 1.45f, -0.1f));
            Vector3 fwd = seat.forward, right = seat.right;
            // Over the right shoulder, looking out of the windshield.
            Vector3 shPos = shoulderP + right * 0.24f + Vector3.up * 0.16f - fwd * 0.5f;
            Quaternion shRot = Quaternion.LookRotation((headP + fwd * 6f - Vector3.up * 0.35f + right * 0.3f) - shPos);
            // Behind the seat.
            Vector3 bkPos = headP + Vector3.up * 0.42f - fwd * 1.55f + right * 0.15f;
            Quaternion bkRot = Quaternion.LookRotation((headP + fwd * 4f - Vector3.up * 0.4f) - bkPos);

            float t0 = _cap.VideoTime;
            _late = () =>
            {
                float t = _cap.VideoTime - t0;
                icam.rect = new Rect(0, 0, 1, 1);
                icam.fieldOfView = 55f;
                float k = Smooth(Mathf.InverseLerp(2.0f, 3.5f, t));
                Vector3 p = Vector3.Lerp(shPos, bkPos, k);
                Quaternion q = Quaternion.Slerp(shRot, bkRot, k);
                Turbulence(t, out Vector3 dp, out Vector3 dr);
                icam.transform.SetPositionAndRotation(p + dp, q * Quaternion.Euler(dr));
                foreach (var (l, i) in lights) if (l) l.intensity = i * (0.75f + 0.35f * Mathf.PerlinNoise(t * 9f, l.name.Length * 0.37f));
            };
            yield return Record(3.5f, t =>
            {
                _blackAlpha = 1f - Mathf.InverseLerp(0f, 0.6f, t);
                _sad.volume = Mathf.Lerp(0f, 0.55f, t / 1.2f);
                if (!voiceOn && t >= VoiceStart) { _voice.Play(); voiceOn = true; }
                if (t > 3.15f) _blackAlpha = Mathf.InverseLerp(3.15f, 3.45f, t);
            });

            // ---------------------------------------------------------------- 2. one second of black
            _blackAlpha = 1f;
            float voiceEnd = VoiceStart + (_voice.clip ? _voice.clip.length : 12.4f);
            bool exited = false;
            yield return Record(1f, t =>
            {
                if (exited) return;
                exited = true;
                _late = null;
                foreach (var (l, i) in lights) if (l) l.intensity = i;
                Destroy(sitter);
                foreach (var r in crewRenderers) if (r) r.enabled = true;
                icam.transform.SetLocalPositionAndRotation(camHome.Item1, camHome.Item2);
                icam.fieldOfView = camHome.Item4;
                RoverInterior.Exit();
                StartIntroWalk();
                if (_tankCam) _tankCam.enabled = false;
            });

            // ---------------------------------------------------------------- 3. out of the rover, up to the chase view
            float moveStart = _cap.VideoTime, moveLen = Mathf.Max(4f, voiceEnd - moveStart);
            _late = () =>
            {
                float t = Mathf.Clamp01((_cap.VideoTime - moveStart) / moveLen);
                FullViewport();
                IntroCamera(t);
            };
            yield return Record(moveLen, t => _blackAlpha = 1f - Mathf.InverseLerp(0f, 0.5f, t));

            // ---------------------------------------------------------------- 4. hold
            yield return Record(HoldSeconds, t =>
            {
                _sad.volume = Mathf.Lerp(0.55f, 0f, Mathf.InverseLerp(HoldSeconds - 1.8f, HoldSeconds, t));
                _bars = 1f - Smooth(Mathf.InverseLerp(HoldSeconds - 0.6f, HoldSeconds, t));
            });
            _sad.Stop();

            // ---------------------------------------------------------------- 5. mechanics: route
            _late = null;
            HandOverCamera(1.2f);
            _cinematic = false;
            _bars = 0f;
            _rover.maxForwardSpeed = _origTopSpeed;
            _fight.volume = 0.5f;
            _fight.Play();
            TurnTowardsOutpost();
            yield return Record(6f, t => Caption("PLOT YOUR ROUTE", "Across a dead, scorched world", t, 6f));

            // ---------------------------------------------------------------- 6. mechanics: hull breaches (cut)
            yield return Off(EnterCabin());
            room = FindAnyObjectByType<InteriorRoom>();
            _health.Hit();
            _health.Hit();
            yield return Frames(2);
            bool sent = false;
            yield return Record(6f, t =>
            {
                Caption("PATCH HULL BREACHES", "Every hit opens a leak — send the crew", t, 6f);
                if (t > 0.4f && !sent) { sent = true; RepairNearestLeak(room); }
                if (t > 3.3f && t < 3.35f) _health.Hit();
            });

            // ---------------------------------------------------------------- 7. mechanics: weapons
            WeaponLoadout.Unlock(WeaponDef.RailGunId);
            WeaponLoadout.Unlock(WeaponDef.RocketId);
            var weapons = room.Weapons;
            // The menu only stays open with the crew idle at the gunner's seat: put them there between shots.
            var crewNow = room.Repairs.Crew;
            if (crewNow)
            {
                var cabinNow = room.Cam.transform.parent;
                Vector3 stand = cabinNow.TransformPoint(weapons.standLocal);
                stand.y = crewNow.transform.position.y;
                crewNow.Place(stand, Quaternion.LookRotation(cabinNow.forward));
            }
            for (int i = 0; i < 60 && !weapons.CrewAtCockpit; i++) yield return Frames(1);
            weapons.Open();
            bool swapped = false;
            yield return Record(5f, t =>
            {
                Caption("SWAP YOUR ARSENAL", "Autocannon · Railgun · Rocket strike", t, 5f);
                if (t > 2.2f && !swapped) { swapped = true; WeaponLoadout.Mount(WeaponDef.RailGunId); }
            });
            weapons.Close();

            // ---------------------------------------------------------------- 8. mechanics: outpost raid (cut)
            yield return Off(ExitCabin());
            WeaponLoadout.Mount(WeaponDef.AutoCannonId);
            var outpost = NearestOutpost();
            if (outpost)
            {
                Vector3 dir = (_rover.transform.position - outpost.Center); dir.y = 0f;
                if (dir.sqrMagnitude < 1f) dir = Vector3.back;
                Teleport(outpost.Center + dir.normalized * (outpost.radius + 30f), -dir);
                SetSpawning(true);
                for (int i = 0; i < 3; i++) _spawner.SpawnAt(outpost);
                _rover.SetDestination(outpost.Center + dir.normalized * (outpost.radius + 12f));
            }
            if (_tankCam) _tankCam.SnapToTarget();
            yield return Frames(45, record: false);
            _aimAt = NearestEnemy;
            yield return Record(8f, t =>
            {
                Caption("RAID ENEMY OUTPOSTS", "Strip them for fuel, parts and weapons", t, 8f);
                if (EnemyRover.All.Count == 0 && t < 5f && outpost) _spawner.SpawnAt(outpost);
            });
            _aimAt = null;

            // ---------------------------------------------------------------- 9. boss: it falls (cut)
            SetSpawning(false);
            KillEnemies();
            WeaponLoadout.Mount(WeaponDef.RailGunId);
            Teleport(_introStart, _introDir);
            _rover.Stop();
            if (_tankCam) { _tankCam.enabled = true; _tankCam.SnapToTarget(); }
            var boss = BossFight.Instance;
            boss.StartNow();
            while (boss.CurrentPhase != BossFight.Phase.Intro) { KillEnemies(); yield return null; }
            yield return Frames(Mathf.RoundToInt(1.4f * TrailerCapture.Fps), record: false);
            yield return Record(5.6f, t => { });

            // ---------------------------------------------------------------- 10. boss: barrage + dodges (cut)
            yield return Off(Until(() => boss.CurrentPhase == BossFight.Phase.Barrage, killEscorts: true));
            yield return Frames(Mathf.RoundToInt(1.0f * TrailerCapture.Fps), record: false);
            _aimAt = () => boss.WeakPoint ? boss.WeakPoint.transform.position : BossArena.BossPosition + Vector3.up * 8f;
            yield return Record(4.6f, t =>
            {
                Caption("SURVIVE THE COLOSSUS", "Dash out of its barrage", t, 4.6f);
                AutoDodge(boss);
            });

            // ---------------------------------------------------------------- 11. boss: the kill shot (cut)
            _aimAt = null;
            yield return Off(SkipToFinalExposed(boss));
            yield return Frames(Mathf.RoundToInt(0.5f * TrailerCapture.Fps), record: false);
            _aimAt = () => boss.WeakPoint ? boss.WeakPoint.Center : (Vector3?)null;
            yield return Record(6f, t =>
            {
                if (t > 3.0f && boss.WeakPoint) boss.WeakPoint.TakeDamage(100f, boss.WeakPoint.Center, WeaponDatabase.Instance.Get(WeaponDef.RailGunId));
                _fight.volume = Mathf.Lerp(0.5f, 0.25f, Mathf.InverseLerp(3f, 6f, t));
            });
            _aimAt = null;

            // ---------------------------------------------------------------- 12. end card
            yield return Record(4.5f, t =>
            {
                _blackAlpha = Mathf.InverseLerp(0f, 0.5f, t);
                _endGroup.alpha = Mathf.InverseLerp(0.6f, 1.4f, t) * (1f - Mathf.InverseLerp(4.0f, 4.5f, t));
                _fight.volume = Mathf.Lerp(0.25f, 0f, Mathf.InverseLerp(1.5f, 4.4f, t));
            });

            _cap.End();
            Debug.Log($"[Trailer] Done: {_cap.FramesWritten} frames ({_cap.FramesWritten / (float)TrailerCapture.Fps:0.0} s) → {OutputFile}");
            EditorApplication.isPlaying = false;
        }

        // ================================================================ recording helpers

        /// <summary>Records <paramref name="seconds"/> of video, calling <paramref name="each"/> with the shot-local time.</summary>
        IEnumerator Record(float seconds, Action<float> each)
        {
            int start = _cap.FramesWritten, frames = Mathf.RoundToInt(seconds * TrailerCapture.Fps);
            SetMusicPaused(false);
            Time.timeScale = 1f;
            _cap.Recording = true;
            while (_cap.FramesWritten - start < frames)
            {
                each((_cap.FramesWritten - start) / (float)TrailerCapture.Fps);
                yield return null;
            }
            _cap.Recording = false;
            _capGroup.alpha = 0f;
        }

        /// <summary>Runs a setup step off camera (music paused so it carries on seamlessly after the cut).</summary>
        IEnumerator Off(IEnumerator step)
        {
            _cap.Recording = false;
            SetMusicPaused(true);
            yield return step;
        }

        IEnumerator Frames(int n, bool record = false)
        {
            if (!record) { _cap.Recording = false; SetMusicPaused(true); }
            for (int i = 0; i < n; i++) yield return null;
        }

        IEnumerator Until(Func<bool> done, bool killEscorts = false)
        {
            Time.timeScale = 2f;
            float guard = Time.time + 120f;
            while (!done() && Time.time < guard)
            {
                if (killEscorts) KillEnemies();
                yield return null;
            }
            Time.timeScale = 1f;
        }

        IEnumerator SkipToFinalExposed(BossFight boss)
        {
            Time.timeScale = 2f;
            var rail = WeaponDatabase.Instance.Get(WeaponDef.RailGunId);
            float guard = Time.time + 240f;
            while (Time.time < guard)
            {
                KillEnemies();
                AutoDodge(boss);
                if (boss.CurrentPhase == BossFight.Phase.Exposed && boss.WeakPoint && !boss.WeakPoint.IsDead)
                {
                    if (boss.CyclesDone >= boss.Cycles - 1) break;
                    boss.WeakPoint.TakeDamage(100f, boss.WeakPoint.Center, rail);
                }
                yield return null;
            }
            Time.timeScale = 1f;
        }

        IEnumerator EnterCabin()
        {
            _rover.Stop();
            RoverInterior.Enter();
            while (!RoverInterior.IsInside) yield return null;
            yield return null;
        }

        IEnumerator ExitCabin()
        {
            RoverInterior.Exit();
            while (RoverInterior.IsTransitioning) yield return null;
            if (_tankCam) { _tankCam.enabled = true; _tankCam.SnapToTarget(); }
            yield return null;
        }

        // ================================================================ intro walk + camera

        Vector3 _introStart, _introDir;

        /// <summary>Faces the rover along open ground (away from outposts) before anything is filmed.</summary>
        void PrepareIntroWalk()
        {
            Vector3 p = _rover.transform.position;
            var outposts = FindObjectsByType<Bobery.Terrain.Outpost>();
            Vector3 best = _rover.transform.forward;
            float bestScore = float.MinValue;
            for (int i = 0; i < 24; i++)
            {
                Vector3 d = Quaternion.Euler(0f, i * 15f, 0f) * Vector3.forward;
                float clear = float.MaxValue;
                for (float s = 0f; s <= 90f; s += 15f)
                    foreach (var o in outposts) clear = Mathf.Min(clear, Vector3.Distance(Flat(p + d * s), Flat(o.Center)) - o.radius);
                float score = Mathf.Min(clear, 120f) + Vector3.Dot(d, _rover.transform.forward) * 5f;
                if (score > bestScore) { bestScore = score; best = d; }
            }
            _introStart = p;
            _introDir = best;
            Teleport(p, best);
        }

        void StartIntroWalk()
        {
            _rover.maxForwardSpeed = _origTopSpeed * 0.45f;
            _rover.SetDestination(_rover.transform.position + _introDir * 140f);
        }

        void TurnTowardsOutpost()
        {
            var o = NearestOutpost();
            Vector3 p = _rover.transform.position;
            Vector3 aim = o ? o.Center : p + _rover.transform.forward * 100f;
            Vector3 d = Flat(aim - p).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, _rover.transform.forward);
            // A bend in the route, so the map shows a path rather than a straight line.
            _rover.SetDestination(p + (_rover.transform.forward * 0.6f + d * 0.4f + side * 0.3f).normalized * 70f);
        }

        /// <summary>t = 0: inside the hull looking ahead; t = 1: the 45° chase view behind and above.</summary>
        float _roofTop = -1f;

        void IntroCamera(float t)
        {
            var rt = _rover.transform;
            Quaternion yaw = Quaternion.Euler(0f, rt.eulerAngles.y, 0f);
            Vector3 size = _fit.WorldSize;
            Vector3 pivotLocal = _tankCam ? _tankCam.pivotOffset : new Vector3(0f, size.y * 0.6f, 0f);
            float dist = _tankCam ? _tankCam.distance : Mathf.Max(size.x, size.z) * 2.2f;
            const float pitch = 45f;

            // Starts just above the roof at the front (the pilot's eye-line, roof edge in frame) and pulls back and up.
            // Both ends sit above the highest part of the rover, so the path never cuts through the hull.
            if (_roofTop < 0f)
            {
                _roofTop = 0f;
                foreach (var r in _rover.GetComponentsInChildren<Renderer>())
                    if (r.enabled && !(r is ParticleSystemRenderer) && !(r is LineRenderer) && !(r is TrailRenderer))
                        _roofTop = Mathf.Max(_roofTop, r.bounds.max.y - rt.position.y);
            }
            Vector3 startLocal = new Vector3(0f, _roofTop + 0.8f, size.z * 0.15f);
            Vector3 endLocal = pivotLocal + Quaternion.Euler(pitch, 0f, 0f) * Vector3.back * dist;
            Vector3 ctrlLocal = new Vector3(0f, Mathf.Max(startLocal.y, endLocal.y * 0.7f), startLocal.z * 0.2f);

            float k = EaseInOut(t);
            Vector3 local = Bezier(startLocal, ctrlLocal, endLocal, k);
            Quaternion startRot = Quaternion.Euler(12f, 0f, 0f), endRot = Quaternion.Euler(pitch, 0f, 0f);
            Quaternion rot = Quaternion.Slerp(startRot, endRot, Smooth(Mathf.InverseLerp(0.1f, 0.95f, k)));
            // A gentle sway on the way out.
            rot *= Quaternion.Euler(0f, Mathf.Sin(k * Mathf.PI) * -8f, Mathf.Sin(k * Mathf.PI) * 2f);
            _mainCam.transform.SetPositionAndRotation(rt.position + yaw * local, yaw * rot);
            _mainCam.fieldOfView = Mathf.Lerp(70f, 60f, k);
        }

        void HandOverCamera(float seconds)
        {
            _blendFromPos = _mainCam.transform.position;
            _blendFromRot = _mainCam.transform.rotation;
            _blendT = 0f;
            _blendTime = seconds;
            if (_tankCam) { _tankCam.enabled = true; _tankCam.SnapToTarget(); }
        }

        void ApplyBlend()
        {
            if (_blendT >= 1f || !_mainCam) return;
            _blendT = Mathf.Min(1f, _blendT + Time.deltaTime / Mathf.Max(0.01f, _blendTime));
            float k = Smooth(_blendT);
            var tr = _mainCam.transform;
            tr.SetPositionAndRotation(Vector3.Lerp(_blendFromPos, tr.position, k), Quaternion.Slerp(_blendFromRot, tr.rotation, k));
        }

        static readonly string[] SidePanels = { "MapCanvas", "InteriorHud", "RoverHatch" };

        /// <summary>The navigation / cabin side panels (and the hatch label) never appear in the trailer,
        /// and every on-screen camera gets the whole frame.</summary>
        void HideSidePanels()
        {
            foreach (var c in FindObjectsByType<Canvas>())
                if (c.enabled && c.isRootCanvas && Array.IndexOf(SidePanels, c.name) >= 0) c.enabled = false;
            foreach (var cam in Camera.allCameras)
                if (!cam.targetTexture && cam.rect != new Rect(0, 0, 1, 1)) cam.rect = new Rect(0, 0, 1, 1);
        }

        void FullViewport()
        {
            if (_mainCam) _mainCam.rect = new Rect(0, 0, 1, 1);
        }

        static void Turbulence(float t, out Vector3 pos, out Vector3 rot)
        {
            float n(float s, float o) => Mathf.PerlinNoise(t * s, o) * 2f - 1f;
            // Constant rumble + two hard jolts.
            float jolt = Jolt(t, 0.9f) + Jolt(t, 2.55f) * 0.8f;
            float a = 0.35f + jolt * 2.2f;
            rot = new Vector3(n(7f, 0.1f) * 0.9f, n(6f, 3.3f) * 0.6f, n(5f, 7.7f) * 1.2f) * a;
            rot.x += Mathf.Sin(t * 1.3f) * 0.6f;
            pos = new Vector3(n(9f, 11f), n(10f, 17f), 0f) * 0.012f * (1f + jolt * 3f);
        }

        static float Jolt(float t, float at) => t < at ? 0f : Mathf.Exp(-(t - at) * 4f) * Mathf.Abs(Mathf.Sin((t - at) * 30f));

        // ================================================================ cabin helpers

        GameObject SpawnSitter(Transform seat, InteriorRoom room)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SitterPath);
            var clip = AssetDatabase.LoadAllAssetsAtPath(SitterPath).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
            var go = prefab ? Instantiate(prefab) : GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = "TrailerPilot";
            SceneManager.MoveGameObjectToScene(go, seat.gameObject.scene);
            foreach (var a in go.GetComponentsInChildren<Animator>()) Destroy(a);
            foreach (var c in go.GetComponentsInChildren<Collider>()) Destroy(c);
            foreach (var r in go.GetComponentsInChildren<SkinnedMeshRenderer>()) r.updateWhenOffscreen = true;

            var crewModel = room.Repairs.Crew && room.Repairs.Crew.transform.childCount > 0 ? room.Repairs.Crew.transform.GetChild(0) : null;
            go.transform.localScale = crewModel ? crewModel.lossyScale : Vector3.one;
            go.transform.SetPositionAndRotation(seat.position, seat.rotation);
            if (clip) clip.SampleAnimation(go, clip.length * 0.5f);

            // The pose is baked into the mesh with its origin at the seat of the pants: put that on the cushion.
            go.transform.position = seat.TransformPoint(new Vector3(0f, 0.58f, 0.02f));
            return go;
        }

        void RepairNearestLeak(InteriorRoom room)
        {
            var crew = room.Repairs.Crew;
            if (!crew) return;
            var leak = room.Repairs.Spots.Where(s => s && !s.Sealing).OrderBy(s => Vector3.Distance(s.transform.position, crew.transform.position)).FirstOrDefault();
            if (!leak) return;
            Vector3 stand = leak.transform.position + Flat(leak.transform.forward).normalized * 0.55f;
            stand.y = crew.transform.position.y;
            crew.RepairAt(stand, leak);
        }

        // ================================================================ combat helpers

        (Vector2 screen, bool fire)? AimOverride()
        {
            if (_aimAt == null || !_aimLocked) return null;
            return (_aimScreen, _aimFire);
        }

        void UpdateAim()
        {
            _aimLocked = false;
            var cam = Camera.main;
            if (_aimAt == null || !cam) return;
            var p = _aimAt();
            if (!p.HasValue) return;
            Vector3 s = cam.WorldToScreenPoint(p.Value);
            if (s.z <= 0f) return;
            Vector2 want = s;
            if ((_aimScreen - want).sqrMagnitude > 600f * 600f) _aimScreen = cam.pixelRect.center;
            _aimScreen = Vector2.Lerp(_aimScreen, want, 1f - Mathf.Exp(-9f * Time.deltaTime));
            _aimLocked = cam.pixelRect.Contains(_aimScreen);
            _aimFire = (_aimScreen - want).magnitude < 45f;
        }

        Vector3? NearestEnemy()
        {
            Vector3 p = _rover.transform.position;
            CombatHealth best = null;
            float bd = 75f;
            foreach (var h in CombatHealth.All)
            {
                if (!h || h.IsDead || h.invulnerable || !string.IsNullOrEmpty(h.onlyWeapon)) continue;
                float d = Vector3.Distance(h.Center, p);
                if (d < bd) { bd = d; best = h; }
            }
            return best ? best.Center : (Vector3?)null;
        }

        void KillEnemies()
        {
            foreach (var e in EnemyRover.All.ToArray())
            {
                if (!e) continue;
                var h = e.GetComponentInChildren<CombatHealth>();
                if (h && !h.IsDead) h.TakeDamage(1e6f, h.Center);
                else Destroy(e.gameObject);
            }
        }

        float _nextDodge;

        void AutoDodge(BossFight boss)
        {
            if (!boss || !boss.CanDodge || Time.time < _nextDodge) return;
            float reach = Mathf.Max(_fit.WorldSize.x, _fit.WorldSize.z) * 2.2f;
            foreach (var b in BossCannonBall.InFlight)
            {
                if (!b || !b.Locked) continue;
                if (Vector3.Distance(b.transform.position, _rover.transform.position) < reach && boss.TryDodge())
                {
                    _nextDodge = Time.time + 0.25f;
                    return;
                }
            }
        }

        Bobery.Terrain.Outpost NearestOutpost()
        {
            Vector3 p = _rover.transform.position;
            return FindObjectsByType<Bobery.Terrain.Outpost>().OrderBy(o => Vector3.Distance(Flat(o.Center), Flat(p))).FirstOrDefault();
        }

        void SetSpawning(bool on)
        {
            if (!_spawner) return;
            _spawner.spawnEnemies = on;
            _spawner.spawnRoamers = false;
        }

        void Teleport(Vector3 pos, Vector3 facing)
        {
            facing = Flat(facing);
            if (facing.sqrMagnitude < 0.001f) facing = _rover.transform.forward;
            var rot = Quaternion.LookRotation(facing.normalized, Vector3.up);
            _rover.Stop();
            var rb = _rover.GetComponent<Rigidbody>();
            if (rb) { rb.position = pos; rb.rotation = rot; }
            _rover.transform.SetPositionAndRotation(pos, rot);
            RoverYaw?.SetValue(_rover, rot.eulerAngles.y);
            RoverSpeed?.SetValue(_rover, 0f);
        }

        // ================================================================ audio

        void BuildAudio()
        {
            var bank = Bobery.Audio.SoundBank.Instance;
            AudioSource Src(string n, AudioClip clip, bool loop)
            {
                var go = new GameObject(n);
                go.transform.SetParent(transform, false);
                var s = go.AddComponent<AudioSource>();
                s.clip = clip;
                s.loop = loop;
                s.playOnAwake = false;
                s.spatialBlend = 0f;
                s.priority = 0;
                s.ignoreListenerPause = true;
                return s;
            }
            _voice = Src("Narrator", AssetDatabase.LoadAssetAtPath<AudioClip>(VoicePath), false);
            _voice.volume = 1f;
            _voice.gameObject.AddComponent<TrailerGain>().gain = NarratorGain;
            _sad = Src("SadMusic", bank ? bank.menuMusic : null, true);
            _sad.pitch = 0.82f; // slower and lower: the menu theme turned melancholy
            var lp = _sad.gameObject.AddComponent<AudioLowPassFilter>();
            lp.cutoffFrequency = 2600f;
            _fight = Src("FightMusic", bank ? bank.fightMusic : null, true);
            _music.Add(_voice);
            _music.Add(_sad);
            _music.Add(_fight);
        }

        readonly Dictionary<AudioSource, bool> _wasPlaying = new Dictionary<AudioSource, bool>();

        void SetMusicPaused(bool paused)
        {
            foreach (var s in _music)
            {
                if (!s) continue;
                if (paused)
                {
                    if (s.isPlaying) { _wasPlaying[s] = true; s.Pause(); }
                }
                else if (_wasPlaying.TryGetValue(s, out bool was) && was)
                {
                    _wasPlaying[s] = false;
                    s.UnPause();
                }
            }
        }

        static void MuteGameMusic()
        {
            var ga = FindAnyObjectByType<Bobery.Audio.GameAudio>();
            if (!ga) return;
            ga.enabled = false;
            foreach (var s in ga.GetComponentsInChildren<AudioSource>()) { s.volume = 0f; s.Stop(); }
        }

        // ================================================================ overlay

        void BuildOverlay()
        {
            _canvas = RoverUI.NewCanvas("TrailerOverlay", transform, 500);
            var root = (RectTransform)_canvas.transform;

            _barTop = Bar("BarTop", root, top: true);
            _barBottom = Bar("BarBottom", root, top: false);

            _caption = RoverUI.Rect("Caption", root);
            _caption.anchorMin = _caption.anchorMax = _caption.pivot = new Vector2(0f, 0f);
            _caption.anchoredPosition = new Vector2(110f, 120f);
            _caption.sizeDelta = new Vector2(1200f, 160f);
            _capGroup = _caption.gameObject.AddComponent<CanvasGroup>();
            _capGroup.alpha = 0f;
            var accent = RoverUI.Rect("Accent", _caption);
            accent.anchorMin = new Vector2(0f, 0f); accent.anchorMax = new Vector2(0f, 1f); accent.pivot = new Vector2(0f, 0.5f);
            accent.sizeDelta = new Vector2(8f, -20f);
            accent.anchoredPosition = new Vector2(-28f, 0f);
            RoverUI.Img(accent, RoverUI.Accent);
            _capTitle = RoverUI.Label(_caption, "", 72, FontStyle.Bold, Color.white);
            Place(_capTitle.rectTransform, 86f, 58f);
            _capSub = RoverUI.Label(_caption, "", 34, FontStyle.Normal, RoverUI.Accent, mono: true);
            Place(_capSub.rectTransform, 44f, 0f);
            foreach (var t in new[] { _capTitle, _capSub }) { t.alignment = TextAlignmentOptions.BottomLeft; RoverUI.Shadowed(t); t.textWrappingMode = TextWrappingModes.NoWrap; }

            var blackRt = RoverUI.Rect("Black", root);
            RoverUI.Stretch(blackRt);
            _black = RoverUI.Img(blackRt, Color.black);
            _black.raycastTarget = false;

            var end = RoverUI.Rect("EndCard", root);
            RoverUI.Stretch(end);
            _endGroup = end.gameObject.AddComponent<CanvasGroup>();
            _endGroup.alpha = 0f;
            _endTitle = RoverUI.Label(end, "DEAD RECKONING", 150, FontStyle.Bold, Color.white);
            RoverUI.Center(_endTitle.rectTransform, 1600f, 240f, 0f, 50f);
            _endTitle.alignment = TextAlignmentOptions.Center;
            _endTitle.characterSpacing = 18f;
            _endSub = RoverUI.Label(end, "DRIVE.  PATCH.  FIGHT.  SURVIVE.", 40, FontStyle.Normal, RoverUI.Accent, mono: true);
            RoverUI.Center(_endSub.rectTransform, 1600f, 60f, 0f, -95f);
            _endSub.alignment = TextAlignmentOptions.Center;
            _endSub.characterSpacing = 8f;
        }

        static void Place(RectTransform rt, float h, float y)
        {
            rt.anchorMin = new Vector2(0f, 0f); rt.anchorMax = new Vector2(1f, 0f); rt.pivot = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(0f, h);
            rt.anchoredPosition = new Vector2(0f, y);
        }

        static Image Bar(string name, RectTransform root, bool top)
        {
            var rt = RoverUI.Rect(name, root);
            rt.anchorMin = new Vector2(0f, top ? 1f : 0f);
            rt.anchorMax = new Vector2(1f, top ? 1f : 0f);
            rt.pivot = new Vector2(0.5f, top ? 1f : 0f);
            rt.sizeDelta = new Vector2(0f, 0f);
            var img = RoverUI.Img(rt, Color.black);
            img.raycastTarget = false;
            return img;
        }

        void Caption(string title, string sub, float t, float length)
        {
            _capTitle.text = title;
            _capSub.text = sub;
            float a = Mathf.InverseLerp(0.15f, 0.55f, t) * (1f - Mathf.InverseLerp(length - 0.5f, length - 0.1f, t));
            _capGroup.alpha = a;
            _caption.anchoredPosition = new Vector2(110f - (1f - Smooth(Mathf.InverseLerp(0.15f, 0.7f, t))) * 40f, 120f);
        }

        void ApplyOverlay()
        {
            var c = _black.color; c.a = Mathf.Clamp01(_blackAlpha); _black.color = c;
            float h = 1080f * 0.11f * Smooth(Mathf.Clamp01(_bars));
            _barTop.rectTransform.sizeDelta = new Vector2(0f, h);
            _barBottom.rectTransform.sizeDelta = new Vector2(0f, h);
        }

        void HideHud()
        {
            foreach (var c in FindObjectsByType<Canvas>())
            {
                if (c == _canvas || !c.enabled || !c.isRootCanvas) continue;
                c.enabled = false;
                _hiddenCanvases.Add(c);
            }
        }

        void ShowHud()
        {
            if (_hiddenCanvases.Count == 0) return;
            foreach (var c in _hiddenCanvases) if (c) c.enabled = true;
            _hiddenCanvases.Clear();
        }

        // ================================================================ math

        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * x * (x * (x * 6f - 15f) + 10f); }
        static float EaseInOut(float x) { x = Mathf.Clamp01(x); return x < 0.5f ? 4f * x * x * x : 1f - Mathf.Pow(-2f * x + 2f, 3f) / 2f; }
        static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float t) => Vector3.Lerp(Vector3.Lerp(a, b, t), Vector3.Lerp(b, c, t), t);
        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }

    /// <summary>Boosts the AudioSource on this object past volume 1, with a soft limiter (tanh) so peaks
    /// round off instead of clipping hard.</summary>
    public class TrailerGain : MonoBehaviour
    {
        public float gain = 1f;

        void OnAudioFilterRead(float[] data, int channels)
        {
            float g = gain;
            for (int i = 0; i < data.Length; i++) data[i] = (float)System.Math.Tanh(data[i] * g);
        }
    }
}
#endif
