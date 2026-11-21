using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Bobery.Rover
{
    /// <summary>
    /// The rover's gun. Mounts the weapon chosen in <see cref="WeaponLoadout"/> on the rover model's
    /// <c>turret_slot</c>, turns it slowly toward the mouse and fires while the left mouse button is held
    /// (over the 3D view). Draws the crosshair, the barrel reticle and enemy health bars.
    /// Added to the rover automatically by <see cref="RoverController"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public class RoverTurret : MonoBehaviour
    {
        public const string SlotName = "turret_slot";
        public const string GunRootName = "MountedWeapon";

        [Tooltip("Lowest / highest barrel angle (degrees) when aiming at the ground or up a slope.")]
        public float minPitch = -35f, maxPitch = 40f;
        [Tooltip("Aim this far out when the mouse points at the sky.")]
        public float skyAimDistance = 120f;
        [Tooltip("Health bars are drawn for targets closer than this.")]
        public float healthBarRange = 160f;

        public WeaponDef Weapon => _def;
        public bool IsFiring { get; private set; }
        public Transform Muzzle => _muzzle;

        RoverAutoFit _fit;
        RoverBattery _battery;
        Transform _slot, _pivot, _gun, _muzzle;
        GameObject _model;
        WeaponDef _def;
        string _builtId = "\0";
        float _yaw, _cooldown, _sinceShot = 99f, _kick, _slotScale = 1f;
        Vector3 _gunRestPos;
        bool _rebuild = true, _aimOnEnemy;
        Vector3 _aimPoint, _barrelPoint;
        readonly List<AnimationClip> _clips = new List<AnimationClip>();
        float _clipLength;

        static readonly RaycastHit[] _hits = new RaycastHit[16];
        public static float LastHitTime = -100f, LastKillTime = -100f;

        void Awake()
        {
            _fit = GetComponent<RoverAutoFit>();
            _battery = GetComponent<RoverBattery>();
            WeaponLoadout.Changed += OnLoadoutChanged;
            CombatHealth.AnyDied += OnAnyDied;
        }

        void OnDestroy()
        {
            WeaponLoadout.Changed -= OnLoadoutChanged;
            CombatHealth.AnyDied -= OnAnyDied;
            if (_canvas) Destroy(_canvas.gameObject);
            Cursor.visible = true;
        }

        void OnLoadoutChanged() => _rebuild = true;
        void OnAnyDied(CombatHealth h) { if (Time.time - LastHitTime < 0.2f) LastKillTime = Time.time; }

        // ================================================================ mounting

        Transform FindSlot()
        {
            foreach (var t in GetComponentsInChildren<Transform>(true))
                if (string.Equals(t.name, SlotName, System.StringComparison.OrdinalIgnoreCase)) return t;
            // No slot in the model: make one on top of the body.
            var slot = new GameObject(SlotName).transform;
            slot.SetParent(transform, false);
            float h = _fit && _fit.IsFitted ? _fit.WorldSize.y / Mathf.Max(0.0001f, transform.lossyScale.y) : 2f;
            slot.localPosition = new Vector3(0f, h * 0.8f, 0f);
            return slot;
        }

        void Rebuild()
        {
            _rebuild = false;
            var def = WeaponLoadout.MountedDef;
            string id = def != null ? def.id : null;
            if (id == _builtId && _pivot) return;
            _builtId = id;
            _def = def;
            if (_pivot) Destroy(_pivot.gameObject);
            _pivot = null; _gun = null; _muzzle = null; _model = null;
            _clips.Clear();
            if (def == null) return;

            if (!_slot) _slot = FindSlot();
            _pivot = new GameObject(GunRootName).transform;
            _pivot.SetParent(_slot, false);

            // Model + where its mount and barrel tip are (model units).
            Vector3 euler = def.modelEuler, pivot = def.pivot, muzzle = def.muzzle;
            if (def.model)
            {
                _model = Instantiate(def.model);
                _model.name = def.id;
            }
            else
            {
                _model = BuildPlaceholder(def);
                euler = Vector3.zero; pivot = Vector3.zero; muzzle = new Vector3(0f, 0.65f, 2.3f);
            }
            foreach (var c in _model.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var a in _model.GetComponentsInChildren<Animator>(true)) Destroy(a);

            // Size from the rover's length.
            _model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _model.transform.localScale = Vector3.one;
            Bounds b = default;
            bool any = false;
            foreach (var r in _model.GetComponentsInChildren<Renderer>(true))
            {
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            float length = any ? Mathf.Max(0.01f, Mathf.Max(b.size.x, b.size.z)) : 1f;
            float roverLength = _fit && _fit.IsFitted ? _fit.WorldSize.z : 10f;
            float worldScale = roverLength * def.sizeVsRover / length;
            float slotScale = _slotScale = Mathf.Max(0.0001f, _slot.lossyScale.x);
            float s = worldScale / slotScale;

            _gun = _model.transform;
            _gun.SetParent(_pivot, false);
            var rot = Quaternion.Euler(euler);
            _gun.localRotation = rot;
            _gun.localScale = Vector3.one * s;
            _gun.localPosition = -(rot * pivot) * s;
            _gunRestPos = _gun.localPosition;

            _muzzle = new GameObject("Muzzle").transform;
            _muzzle.SetParent(_gun, false);
            _muzzle.localPosition = muzzle;
            _muzzle.localRotation = Quaternion.Inverse(rot); // +Z = pivot forward

            if (def.fireClips != null)
                foreach (var c in def.fireClips)
                    if (c) { _clips.Add(c); _clipLength = Mathf.Max(_clipLength, c.length); }
            SampleClips(0f);

            // Start facing the rover's heading.
            Vector3 f = _slot.InverseTransformDirection(transform.forward);
            _yaw = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            _pivot.localRotation = Quaternion.Euler(0f, _yaw, 0f);
            _cooldown = 0f;
            _sinceShot = 99f;
        }

        static GameObject BuildPlaceholder(WeaponDef def)
        {
            var root = new GameObject(def.id);
            var dark = WeaponFx.Unlit(new Color(0.2f, 0.21f, 0.22f));
            var steel = WeaponFx.Unlit(new Color(0.45f, 0.47f, 0.5f));
            Prim(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.15f, 0f), new Vector3(1f, 0.15f, 1f), Vector3.zero, dark);
            Prim(PrimitiveType.Cube, root.transform, new Vector3(0f, 0.6f, 0f), new Vector3(0.8f, 0.5f, 1f), Vector3.zero, steel);
            Prim(PrimitiveType.Cylinder, root.transform, new Vector3(0f, 0.65f, 1.3f), new Vector3(0.18f, 1f, 0.18f), new Vector3(90f, 0f, 0f), dark);
            return root;
        }

        static void Prim(PrimitiveType t, Transform parent, Vector3 pos, Vector3 scale, Vector3 euler, Material m)
        {
            var go = GameObject.CreatePrimitive(t);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.transform.localEulerAngles = euler;
            go.GetComponent<Renderer>().sharedMaterial = m;
        }

        void SampleClips(float time)
        {
            if (!_model) return;
            foreach (var c in _clips) c.SampleAnimation(_model, Mathf.Min(time, c.length));
        }

        // ================================================================ update

        void Update()
        {
            if (_fit && !_fit.IsFitted) return; // gun must not be part of the collider fit
            if (_rebuild) Rebuild();

            bool inside = RoverInterior.IsInside || RoverInterior.IsTransitioning;
            bool blocked = inside || GameOverScreen.IsShown || Time.timeScale <= 0f;
            var cam = Camera.main;
            var mouse = Mouse.current;
            Vector2 sp = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
            bool inView = !blocked && cam && mouse != null && cam.pixelRect.Contains(sp);

            if (_def != null && _pivot && inView) Aim(cam, sp);

            // Fire while held.
            IsFiring = false;
            _sinceShot += Time.deltaTime;
            if (_cooldown > 0f) _cooldown -= Time.deltaTime;
            bool held = inView && mouse.leftButton.isPressed;
            if (_def != null && _muzzle && held)
            {
                IsFiring = true;
                if (_cooldown <= 0f)
                {
                    _cooldown += _def.FireInterval;
                    if (_cooldown < 0f) _cooldown = 0f;
                    if (!_battery || _battery.TryUse(_def.batteryPerShot)) Shoot();
                    else _noPowerFlash = 1f;
                }
            }
            else if (_cooldown < 0f) _cooldown = 0f;

            // Shot animation + recoil.
            if (_clips.Count > 0 && _sinceShot * _def.clipSpeed <= _clipLength + 0.05f) SampleClips(_sinceShot * _def.clipSpeed);
            if (_gun)
            {
                _kick = Mathf.MoveTowards(_kick, 0f, Time.deltaTime * Mathf.Max(0.5f, _def.recoilKick * 6f));
                _gun.localPosition = _gunRestPos + Vector3.back * (_kick / _slotScale);
            }

            UpdateHud(cam, sp, inView, inside);
        }

        void Aim(Camera cam, Vector2 sp)
        {
            Ray ray = cam.ScreenPointToRay(sp);
            _aimOnEnemy = false;
            if (RaycastWorld(ray, 1000f, out float d, out CombatHealth h)) { _aimPoint = ray.GetPoint(d); _aimOnEnemy = h; if (h) _aimPoint = h.Center; }
            else _aimPoint = ray.GetPoint(skyAimDistance);

            Vector3 local = _slot.InverseTransformPoint(_aimPoint);
            if (new Vector2(local.x, local.z).sqrMagnitude > 0.0001f)
            {
                float want = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
                _yaw = Mathf.MoveTowardsAngle(_yaw, want, _def.turnSpeed * Time.deltaTime);
                _pivot.localRotation = Quaternion.Euler(0f, _yaw, 0f);
            }
        }

        /// <summary>Where the barrel points right now: its heading, pitched to reach the aimed-at distance and height.</summary>
        Vector3 ShotDirection()
        {
            Vector3 from = _muzzle.position;
            Vector3 fwd = Vector3.ProjectOnPlane(_pivot.forward, Vector3.up);
            if (fwd.sqrMagnitude < 0.0001f) fwd = transform.forward;
            fwd.Normalize();
            Vector3 flat = _aimPoint - from; flat.y = 0f;
            float dist = Mathf.Max(3f, flat.magnitude);
            float pitch = Mathf.Atan2(_aimPoint.y - from.y, dist) * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            Vector3 axis = Vector3.Cross(Vector3.up, fwd);
            return Quaternion.AngleAxis(-pitch, axis) * fwd;
        }

        void Shoot()
        {
            _sinceShot = 0f;
            Vector3 dir = ShotDirection();
            if (_def.spread > 0f)
            {
                Vector2 r = Random.insideUnitCircle * _def.spread;
                dir = Quaternion.AngleAxis(r.x, Vector3.up) * Quaternion.AngleAxis(r.y, Vector3.Cross(Vector3.up, dir)) * dir;
            }
            Vector3 from = _muzzle.position;
            CannonShell.Fire(from, dir, _def, transform);
            WeaponFx.MuzzleFlash(from, dir, _def.fxScale, _def.tracerColor);
            _kick = _def.recoilKick;
            _shotPulse = 1f;
            SampleClips(0f);
        }

        /// <summary>Closest of: an enemy hit sphere, a collider that isn't the rover or an effect.</summary>
        bool RaycastWorld(Ray ray, float max, out float dist, out CombatHealth target)
        {
            dist = max;
            target = null;
            bool found = false;
            foreach (var h in CombatHealth.All)
                if (h && !h.IsDead && h.Raycast(ray, dist, out float d)) { dist = d; target = h; found = true; }
            int mask = ~(1 << WeaponFx.HiddenLayer) & ~(1 << 2) & ~(1 << 5);
            int n = Physics.RaycastNonAlloc(ray, _hits, dist, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].transform.IsChildOf(transform) || _hits[i].distance >= dist) continue;
                dist = _hits[i].distance;
                target = _hits[i].transform.GetComponentInParent<CombatHealth>();
                found = true;
            }
            return found;
        }

        // ================================================================ HUD

        Canvas _canvas;
        RectTransform _root, _cross, _reticle, _hitMark, _barRoot;
        TextMeshProUGUI _label;
        Image[] _crossParts, _reticleParts, _hitParts;
        readonly List<(RectTransform rt, RectTransform fill, Image fillImg)> _bars = new List<(RectTransform, RectTransform, Image)>();
        float _noPowerFlash, _shotPulse;

        void BuildHud()
        {
            _canvas = RoverUI.NewCanvas("WeaponHud", null, 30);
            _root = (RectTransform)_canvas.transform;

            _barRoot = RoverUI.Rect("HealthBars", _root);
            _barRoot.anchorMin = _barRoot.anchorMax = _barRoot.pivot = Vector2.zero;

            // Crosshair at the mouse: four ticks and a dot.
            _cross = Anchor("Crosshair", 40);
            var parts = new List<Image>();
            foreach (var (pos, size) in new[] {
                (new Vector2(0, 11), new Vector2(2, 10)), (new Vector2(0, -11), new Vector2(2, 10)),
                (new Vector2(11, 0), new Vector2(10, 2)), (new Vector2(-11, 0), new Vector2(10, 2)),
                (Vector2.zero, new Vector2(3, 3)) })
                parts.Add(Piece(_cross, pos, size));
            _crossParts = parts.ToArray();

            // Barrel reticle: where the gun is actually pointing (four corner brackets).
            _reticle = Anchor("BarrelReticle", 44);
            parts = new List<Image>();
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sy in new[] { -1f, 1f })
                {
                    parts.Add(Piece(_reticle, new Vector2(sx * 16, sy * 20), new Vector2(10, 2)));
                    parts.Add(Piece(_reticle, new Vector2(sx * 20, sy * 16), new Vector2(2, 10)));
                }
            _reticleParts = parts.ToArray();

            // Hit marker (an X around the crosshair).
            _hitMark = Anchor("HitMarker", 40);
            parts = new List<Image>();
            foreach (float a in new[] { 45f, 135f, 225f, 315f })
            {
                var img = Piece(_hitMark, Quaternion.Euler(0, 0, a) * new Vector2(14, 0), new Vector2(9, 2));
                img.rectTransform.localRotation = Quaternion.Euler(0, 0, a);
                parts.Add(img);
            }
            _hitParts = parts.ToArray();

            _label = RoverUI.Label(_root, "", 11, FontStyle.Bold, RoverUI.Accent, mono: true);
            var lr = _label.rectTransform;
            lr.anchorMin = lr.anchorMax = Vector2.zero;
            lr.pivot = new Vector2(0f, 1f);
            lr.sizeDelta = new Vector2(220, 18);
            RoverUI.Shadowed(_label);
        }

        RectTransform Anchor(string name, float size)
        {
            var rt = RoverUI.Rect(name, _root);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            return rt;
        }

        static Image Piece(RectTransform parent, Vector2 pos, Vector2 size)
        {
            var rt = RoverUI.Rect("Piece", parent);
            RoverUI.Center(rt, size.x, size.y, pos.x, pos.y);
            return RoverUI.Img(rt, RoverUI.Accent);
        }

        void UpdateHud(Camera cam, Vector2 sp, bool inView, bool inside)
        {
            if (!_canvas) BuildHud();
            bool show = !inside && !GameOverScreen.IsShown;
            _canvas.enabled = show;
            Cursor.visible = !(show && inView);
            if (!show) return;

            float k = Mathf.Max(0.0001f, _canvas.scaleFactor);
            _noPowerFlash = Mathf.Max(0f, _noPowerFlash - Time.deltaTime);
            _shotPulse = Mathf.Max(0f, _shotPulse - Time.deltaTime * 8f);

            // Crosshair
            _cross.gameObject.SetActive(inView);
            _label.gameObject.SetActive(inView);
            if (inView)
            {
                _cross.anchoredPosition = sp / k;
                _cross.localScale = Vector3.one * (1f + 0.35f * _shotPulse);
                Color c = _aimOnEnemy ? RoverUI.Red : RoverUI.Accent;
                foreach (var p in _crossParts) p.color = c;

                _label.rectTransform.anchoredPosition = sp / k + new Vector2(18, -14);
                if (_def == null) { _label.text = "NO WEAPON - mount one in the cockpit"; _label.color = RoverUI.Yellow; }
                else if (_battery && _battery.Charge + 0.0001f < _def.batteryPerShot) { _label.text = "NO POWER"; _label.color = RoverUI.Red; }
                else
                {
                    _label.text = _def.displayName + (IsFiring ? "  FIRING" : "  [LMB]");
                    _label.color = _noPowerFlash > 0f ? RoverUI.Red : RoverUI.A(RoverUI.Accent, 0.85f);
                }
            }

            // Barrel reticle
            bool reticle = inView && _def != null && _muzzle && cam;
            if (reticle)
            {
                Vector3 from = _muzzle.position, dir = ShotDirection();
                float maxD = _def.range;
                bool hit = RaycastWorld(new Ray(from, dir), maxD, out float d, out CombatHealth h);
                _barrelPoint = from + dir * (hit ? d : maxD);
                Vector3 s = cam.WorldToScreenPoint(_barrelPoint);
                reticle = s.z > 0f;
                if (reticle)
                {
                    _reticle.anchoredPosition = (Vector2)s / k;
                    Color c = h ? RoverUI.Red : RoverUI.A(RoverUI.White, 0.75f);
                    foreach (var p in _reticleParts) p.color = c;
                }
            }
            _reticle.gameObject.SetActive(reticle);

            // Hit marker
            float since = Time.time - LastHitTime;
            bool kill = Time.time - LastKillTime < 0.4f;
            _hitMark.gameObject.SetActive(inView && (since < 0.15f || kill));
            if (_hitMark.gameObject.activeSelf)
            {
                _hitMark.anchoredPosition = sp / k;
                _hitMark.localScale = Vector3.one * (kill ? 1.5f : 1f);
                foreach (var p in _hitParts) p.color = kill ? RoverUI.Red : RoverUI.White;
            }

            UpdateHealthBars(cam, k);
        }

        void UpdateHealthBars(Camera cam, float k)
        {
            int used = 0;
            if (cam)
            {
                foreach (var h in CombatHealth.All)
                {
                    if (!h || h.IsDead) continue;
                    bool recent = Time.time - h.LastHitTime < 5f;
                    if (h.Fraction >= 0.999f && !recent) continue;
                    if (Vector3.Distance(cam.transform.position, h.transform.position) > healthBarRange) continue;
                    Vector3 s = cam.WorldToScreenPoint(h.BarPosition);
                    if (s.z <= 0f || !cam.pixelRect.Contains(s)) continue;

                    if (used >= _bars.Count) _bars.Add(NewBar());
                    var (rt, fill, img) = _bars[used++];
                    rt.gameObject.SetActive(true);
                    rt.anchoredPosition = (Vector2)s / k;
                    fill.anchorMax = new Vector2(h.Fraction, 1f);
                    bool flash = Time.time - h.LastHitTime < 0.08f;
                    img.color = flash ? RoverUI.White : h.Fraction < 0.35f ? RoverUI.Red : RoverUI.Yellow;
                }
            }
            for (int i = used; i < _bars.Count; i++) _bars[i].rt.gameObject.SetActive(false);
        }

        (RectTransform, RectTransform, Image) NewBar()
        {
            var rt = RoverUI.Rect("Bar", _barRoot);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0f);
            rt.sizeDelta = new Vector2(64, 7);
            RoverUI.Img(rt, RoverUI.A(RoverUI.Black, 0.85f));
            RoverUI.Outline(rt, RoverUI.Faint);
            var fill = RoverUI.Rect("Fill", rt);
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.pivot = new Vector2(0f, 0.5f);
            fill.offsetMin = new Vector2(1, 1);
            fill.offsetMax = new Vector2(-1, -1);
            var img = RoverUI.Img(fill, RoverUI.Yellow);
            return (rt, fill, img);
        }
    }
}
