using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Bobery.Rover
{
    /// <summary>
    /// Rover interior (scene #2): a cabin with a fuel tank, its own camera and a HUD.
    ///   LMB – walk / repair a leak / use the fuel tank (<see cref="FuelStation"/>) or the cockpit (<see cref="CockpitWeapons"/>)
    ///   Esc – close an open menu (leave at the pilot seat)
    /// The cabin geometry lives in the scene (Bobery > Build Rover Interior Geometry) so it can be
    /// edited by hand; only the HUD is built at runtime. If the scene has no geometry yet
    /// (no camera assigned), a placeholder cabin is built at runtime instead.
    /// Works standalone too (open the scene directly) — then there's just no rover tank to fill.
    /// </summary>
    public class InteriorRoom : MonoBehaviour
    {
        [Header("Scene references")]
        [Tooltip("Camera that renders the cabin. Its rotation is the resting view; the mouse adds a little parallax.")]
        public Camera interiorCamera;
        [Tooltip("Glowing fuel level bar. Its size/position in the scene = a FULL tank; it shrinks from the bottom as fuel drops.")]
        public Transform fuelLevel;

        [Header("Repairs")]
        [Tooltip("Crew character placed in the scene (RepairCrew with its model as the first child). Empty → spawned from crewModel at runtime.")]
        public RepairCrew crew;
        [Tooltip("Crew member model (Assets/Models/player-walking.fbx). Empty → a capsule stand-in.")]
        public GameObject crewModel;
        [Tooltip("Walk animation (from player-walking.fbx).")]
        public AnimationClip crewWalkClip;
        [Tooltip("Repair animation (from player-DIY.fbx).")]
        public AnimationClip crewRepairClip;
        public const string CrewWalkPath = "Assets/Models/player-walking.fbx";
        public const string CrewRepairPath = "Assets/Models/player-DIY.fbx";

#if UNITY_EDITOR
        void Reset() => FindCrewAssets();
        void OnValidate() => FindCrewAssets();
        /// <summary>Fills in the crew model and clips from Assets/Models if they're empty.</summary>
        void FindCrewAssets()
        {
            bool changed = false;
            if (!crewModel) { crewModel = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(CrewWalkPath); changed |= (bool)crewModel; }
            if (!crewWalkClip) { crewWalkClip = FirstClip(CrewWalkPath); changed |= (bool)crewWalkClip; }
            if (!crewRepairClip) { crewRepairClip = FirstClip(CrewRepairPath); changed |= (bool)crewRepairClip; }
            if (changed && !Application.isPlaying && gameObject.scene.IsValid())
                UnityEditor.EditorApplication.delayCall += () =>
                {
                    if (this) UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(gameObject.scene);
                };
        }

        static AnimationClip FirstClip(string path)
        {
            foreach (var o in UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path))
                if (o is AnimationClip c && !c.name.StartsWith("__preview__")) return c;
            return null;
        }
#endif

        FuelTank _tank;
        RoverHealth _health;
        InteriorRepairs _repairs;
        CockpitWeapons _weapons;
        FuelStation _fuel;
        CabinStation _steer;
        RoverBattery _battery;
        Vector3 _camBasePos;
        float _shake;
        Camera _cam;
        Quaternion _camBaseRot;
        Canvas _canvasScaler;
        Transform _fuelLevel;
        float _fuelLevelMaxHeight, _fuelLevelBottom;
        Vector3 _fuelLevelFullScale;

        // HUD
        readonly UIButtons _hudButtons = new UIButtons();
        RectTransform _gaugeTrack, _gaugeFill;
        TextMeshProUGUI _gaugeText, _reserveText, _statusText, _toastText;
        RectTransform _toast;
        float _toastTimer;
        RectTransform _hullTrack, _hullFill, _cursorHint;
        TextMeshProUGUI _hullText, _hullStatus, _cursorHintText;
        RectTransform _batTrack, _batFill;
        TextMeshProUGUI _batText, _powerText;

        void Start()
        {
            var rover = FindAnyObjectByType<RoverController>();
            if (rover) _tank = rover.GetComponent<FuelTank>();
            if (rover) _health = rover.GetComponent<RoverHealth>();
            if (rover) _battery = rover.GetComponent<RoverBattery>();
#if UNITY_EDITOR
            FindCrewAssets();
#endif

            if (!interiorCamera)
            {
                var built = InteriorCabin.Build(transform, InteriorCabin.CreateMaterials());
                interiorCamera = built.cam;
                fuelLevel = built.level;
            }
            _cam = interiorCamera;
            if (_cam) { _camBaseRot = _cam.transform.localRotation; _camBasePos = _cam.transform.localPosition; }
            _fuelLevel = fuelLevel;
            if (_fuelLevel)
            {
                _fuelLevelFullScale = _fuelLevel.localScale;
                _fuelLevelMaxHeight = _fuelLevelFullScale.y;
                _fuelLevelBottom = _fuelLevel.localPosition.y - _fuelLevelMaxHeight * 0.5f;
            }

            BuildHud();

            // Leaks on the walls + the crew member who patches them.
            _repairs = gameObject.AddComponent<InteriorRepairs>();
            var cabin = _cam && _cam.transform.parent ? _cam.transform.parent : transform.Find("Cabin");
            _repairs.Init(_health, _cam, cabin, crewModel, crewWalkClip, crewRepairClip,
                () => PointerOverHud() || (_weapons && _weapons.PointerOverUi) || (_fuel && _fuel.PointerOverUi)
                      || (_steer && _steer.PointerOverUi), crew);

            // Weapon swapping at the cockpit.
            _weapons = gameObject.AddComponent<CockpitWeapons>();
            _weapons.Init(_repairs, _cam, cabin);

            // Refuelling at the fuel tank.
            _fuel = gameObject.AddComponent<FuelStation>();
            _fuel.Init(_repairs, _cam, _tank, fuelLevel);

            // Steering at the left-hand seat (the right-hand one is the gunner's: CockpitWeapons).
            System.Func<bool> menuOpen = () => (_weapons && _weapons.IsOpen) || (_fuel && _fuel.IsOpen);
            var leftSeat = FindInCabin(cabin, "Seat", t => cabin.InverseTransformPoint(t.position).x < 0f);
            if (leftSeat)
            {
                Vector3 seatLocal = cabin.InverseTransformPoint(leftSeat.position);
                _steer = gameObject.AddComponent<CabinStation>();
                _steer.Blocked = menuOpen;
                _steer.Init(_repairs, _cam, leftSeat, cabin.TransformPoint(new Vector3(seatLocal.x, 0f, seatLocal.z - 0.65f)),
                            "PILOT SEAT", "STEER", () => "STEER THE ROVER  [ E ]", Leave);
            }

            if (_health) _health.Damaged += OnHullHit;
        }

        static Transform FindInCabin(Transform cabin, string name, System.Func<Transform, bool> where)
        {
            if (!cabin) return null;
            foreach (var t in cabin.GetComponentsInChildren<Transform>(true))
                if (t.name == name && (where == null || where(t))) return t;
            return null;
        }

        void OnDestroy()
        {
            if (_health) _health.Damaged -= OnHullHit;
        }

        void OnHullHit(RoverHealth.Leak leak)
        {
            _shake = 0.5f;
            Toast("HULL BREACH! Click the leak to repair it");
        }

        /// <summary>Mouse over the side panel (the 3D view starts right of it).</summary>
        bool PointerOverHud()
        {
            var m = Mouse.current;
            if (m == null || !_canvasScaler) return false;
            return m.position.ReadValue().x < 400f * _canvasScaler.scaleFactor;
        }

        // Cabin geometry: see InteriorCabin.

        // ================================================================ HUD

        void BuildHud()
        {
            var canvas = RoverUI.NewCanvas("InteriorHud", transform, 20);
            _canvasScaler = canvas;
            var root = canvas.transform;

            // Left column (same width/style as the navigation panel)
            var side = RoverUI.Rect("Side", root);
            side.anchorMin = new Vector2(0, 0); side.anchorMax = new Vector2(0, 1);
            side.pivot = new Vector2(0, 0.5f); side.sizeDelta = new Vector2(400, 0);
            RoverUI.Img(side, RoverUI.Black);
            var sideEdge = RoverUI.Rect("Edge", side);
            sideEdge.anchorMin = new Vector2(1, 0); sideEdge.anchorMax = new Vector2(1, 1);
            sideEdge.pivot = new Vector2(1, 0.5f); sideEdge.sizeDelta = new Vector2(1, 0);
            RoverUI.Img(sideEdge, RoverUI.Faint);
            const float pad = 24f, inner = 352f;

            var eyebrow = RoverUI.Label(side, RoverUI.Crumbs("ROOT", "ROVER", "INTERIOR"), 12, FontStyle.Bold, RoverUI.Dim);
            RoverUI.TopLeft(eyebrow.rectTransform, pad, pad, inner, 18);
            var title = RoverUI.Label(side, "CABIN", 34, FontStyle.Bold, RoverUI.Accent);
            RoverUI.TopLeft(title.rectTransform, pad, pad + 24, inner, 44);
            var sub = RoverUI.Label(side, "Left seat: steer.  Right seat: weapons.\nTank: refuel.", 13, FontStyle.Normal, RoverUI.Dim);
            RoverUI.TopLeft(sub.rectTransform, pad, pad + 70, inner, 36);

            float y = pad + 112;
            var lbl = RoverUI.Label(side, "ROVER_TANK", 13, FontStyle.Bold, RoverUI.Accent);
            RoverUI.TopLeft(lbl.rectTransform, pad, y, inner, 16);
            _gaugeText = RoverUI.Label(side, "", 12, FontStyle.Bold, RoverUI.White, mono: true);
            RoverUI.TopLeft(_gaugeText.rectTransform, pad, y, inner, 16);
            _gaugeText.alignment = TextAlignmentOptions.TopRight;
            _gaugeTrack = RoverUI.Bar(side, pad, y + 24, inner, 10, out _gaugeFill);
            y += 50;
            _reserveText = RoverUI.Label(side, "", 13, FontStyle.Normal, RoverUI.Dim, mono: true);
            RoverUI.TopLeft(_reserveText.rectTransform, pad, y, inner, 18);
            y += 34;
            _statusText = RoverUI.Label(side, "", 14, FontStyle.Bold, RoverUI.White);
            RoverUI.TopLeft(_statusText.rectTransform, pad, y, inner, 44);
            y += 60;

            var hl = RoverUI.Label(side, "ROVER_HULL", 13, FontStyle.Bold, RoverUI.Accent);
            RoverUI.TopLeft(hl.rectTransform, pad, y, inner, 16);
            _hullText = RoverUI.Label(side, "", 12, FontStyle.Bold, RoverUI.White, mono: true);
            RoverUI.TopLeft(_hullText.rectTransform, pad, y, inner, 16);
            _hullText.alignment = TextAlignmentOptions.TopRight;
            _hullTrack = RoverUI.Bar(side, pad, y + 24, inner, 10, out _hullFill);
            y += 50;
            _hullStatus = RoverUI.Label(side, "", 14, FontStyle.Bold, RoverUI.White);
            RoverUI.TopLeft(_hullStatus.rectTransform, pad, y, inner, 44);
            y += 60;

            var bl = RoverUI.Label(side, "ROVER_BATTERY", 13, FontStyle.Bold, RoverUI.Accent);
            RoverUI.TopLeft(bl.rectTransform, pad, y, inner, 16);
            _batText = RoverUI.Label(side, "", 12, FontStyle.Bold, RoverUI.White, mono: true);
            RoverUI.TopLeft(_batText.rectTransform, pad, y, inner, 16);
            _batText.alignment = TextAlignmentOptions.TopRight;
            _batTrack = RoverUI.Bar(side, pad, y + 24, inner, 10, out _batFill);
            y += 50;
            _powerText = RoverUI.Label(side, "", 14, FontStyle.Bold, RoverUI.White);
            RoverUI.TopLeft(_powerText.rectTransform, pad, y, inner, 44);
            y += 60;

            // (Leaving the rover: the pilot seat's STEER popup.)
            _hudButtons.Add(side, "REFUEL AT THE TANK", pad, y, inner, 46, true, () => { if (_fuel) _fuel.GoToTank(); });

            // Footer key hints
            string acc = RoverUI.Hex(RoverUI.Accent);
            var hint = RoverUI.Label(side, $"<color={acc}><b>[LMB]</b></color> Walk / repair / use   <color={acc}><b>[E]</b></color> Open   <color={acc}><b>[ESC]</b></color> Close", 12, FontStyle.Normal, RoverUI.Dim, mono: true);
            hint.rectTransform.anchorMin = new Vector2(0, 0); hint.rectTransform.anchorMax = new Vector2(1, 0);
            hint.rectTransform.pivot = new Vector2(0, 0);
            hint.rectTransform.offsetMin = new Vector2(pad, pad); hint.rectTransform.offsetMax = new Vector2(-pad, pad + 20);

            // Toast (bottom centre of the view)
            _toast = RoverUI.Rect("Toast", root);
            _toast.anchorMin = _toast.anchorMax = new Vector2(0.5f, 0);
            _toast.pivot = new Vector2(0.5f, 0);
            _toast.anchoredPosition = new Vector2(200, 60);
            _toast.sizeDelta = new Vector2(320, 36);
            RoverUI.Img(_toast, RoverUI.Black);
            RoverUI.Outline(_toast, RoverUI.Accent);
            _toastText = RoverUI.Label(_toast, "", 14, FontStyle.Bold, RoverUI.Accent);
            RoverUI.Stretch(_toastText.rectTransform);
            _toastText.alignment = TextAlignmentOptions.Center;
            _toast.gameObject.SetActive(false);

            // Hint next to the cursor over a leak / repair progress over the crew.
            _cursorHint = RoverUI.Rect("CursorHint", root);
            _cursorHint.anchorMin = _cursorHint.anchorMax = Vector2.zero;
            _cursorHint.pivot = new Vector2(0, 0.5f);
            _cursorHint.sizeDelta = new Vector2(190, 26);
            RoverUI.Img(_cursorHint, RoverUI.Black);
            RoverUI.Outline(_cursorHint, RoverUI.Red);
            _cursorHintText = RoverUI.Label(_cursorHint, "", 12, FontStyle.Bold, RoverUI.Red, mono: true);
            RoverUI.Stretch(_cursorHintText.rectTransform);
            _cursorHintText.alignment = TextAlignmentOptions.Center;
            _cursorHint.gameObject.SetActive(false);
        }

        // ================================================================ runtime

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && !GameOverScreen.IsShown)
            {
                if (kb.escapeKey.wasPressedThisFrame)
                {
                    if (_weapons && _weapons.IsOpen) _weapons.Close();
                    else if (_fuel && _fuel.IsOpen) _fuel.Close();
                    // Leaving is done at the pilot seat (or the LEAVE ROVER button), not with Esc.
                }
            }

            if (!(_weapons && _weapons.IsOpen)) _hudButtons.Update();

            UpdateGauges();
            UpdateHull();
            UpdatePower();
            AnimateRoom();

            if (_toastTimer > 0f)
            {
                _toastTimer -= Time.unscaledDeltaTime;
                if (_toastTimer <= 0f) _toast.gameObject.SetActive(false);
            }
        }

        void UpdateGauges()
        {
            float fuel = _tank ? _tank.Fuel : 0f, cap = _tank ? _tank.capacity : 1f;
            string g = _tank ? $"{fuel:0.0} / {cap:0} L" : "NO ROVER";
            _gaugeText.text = g;
            RoverUI.SetBar(_gaugeTrack, _gaugeFill, fuel / cap);
            _reserveText.text = $"CARRIED FUEL   {FuelTank.Reserve:0.#} L";
            Color barC = !_tank ? RoverUI.Faint : _tank.IsLow || _tank.IsEmpty ? RoverUI.Red : fuel / cap < 0.5f ? RoverUI.Yellow : RoverUI.Accent;
            _gaugeFill.GetComponent<Image>().color = barC;

            if (!_tank) _statusText.text = "";
            else if (_fuel && _fuel.Pumping) _statusText.text = Tag("FUEL", RoverUI.Accent, "Pumping fuel into the tank...");
            else if (_tank.IsEmpty && FuelTank.Reserve > 0f) _statusText.text = Tag("ERR ", RoverUI.Red, "Tank empty. Click the fuel tank and pump fuel in.");
            else if (_tank.IsLow) _statusText.text = Tag("WARN", RoverUI.Yellow, "Fuel low. Click the fuel tank to refuel.");
            else _statusText.text = Tag("OKAY", RoverUI.Green, "Ready to go.");
        }

        void UpdateHull()
        {
            if (!_hullTrack) return;
            if (!_health)
            {
                _hullText.text = "NO ROVER";
                RoverUI.SetBar(_hullTrack, _hullFill, 0f);
                _hullStatus.text = "";
            }
            else
            {
                int leaks = _health.Leaks.Count;
                float drain = _health.LeakRate / _health.maxHealth * 100f;
                _hullText.text = $"{_health.Fraction * 100f:0}%" + (leaks > 0 ? $"  -{drain:0.#}%/s  ·  {leaks} LEAK{(leaks == 1 ? "" : "S")}"
                                 : _health.IsRegenerating ? $"  +{_health.regenPerSecond * 100f:0.#}%/s" : "");
                RoverUI.SetBar(_hullTrack, _hullFill, _health.Fraction);
                bool hit = _health.SinceHit < 0.6f;
                _hullFill.GetComponent<Image>().color = hit ? RoverUI.White : _health.IsLow ? RoverUI.Red : leaks > 0 ? RoverUI.Yellow : RoverUI.Accent;
                if (_repairs && _repairs.Repairing)
                    _hullStatus.text = Tag("FIX ", RoverUI.Accent, $"Patching a leak... {_repairs.RepairProgress * 100f:0}%");
                else if (_health.IsLow) _hullStatus.text = Tag("ERR ", RoverUI.Red, "Hull critical! Click the leaks on the walls to repair.");
                else if (leaks > 0) _hullStatus.text = Tag("WARN", RoverUI.Yellow, "Hull leaking. Click a leak to send the crew.");
                else _hullStatus.text = Tag("OKAY", RoverUI.Green, "Hull intact.");
            }

            // Cursor hint over a leak.
            var m = Mouse.current;
            bool show = _repairs && _repairs.Hovered && m != null && !(_fuel && _fuel.IsOpen);
            _cursorHint.gameObject.SetActive(show);
            if (show)
            {
                bool busy = _repairs.Repairing == _repairs.Hovered;
                _cursorHintText.text = busy ? $"REPAIRING {_repairs.RepairProgress * 100f:0}%" : "[LMB] REPAIR LEAK";
                float s = _canvasScaler ? _canvasScaler.scaleFactor : 1f;
                _cursorHint.anchoredPosition = m.position.ReadValue() / s + new Vector2(18, -18);
            }
        }

        /// <summary>Battery level. It recharges by itself whenever the rover is parked in a beacon's zone.</summary>
        void UpdatePower()
        {
            if (!_batTrack) return;
            float f = _battery ? _battery.Fraction : 0f;
            bool charging = _battery && _battery.IsCharging && f < 0.999f;
            _batText.text = _battery ? $"{f * 100f:0}%" + (charging ? "  ·  CHARGING" : "") : "NO ROVER";
            RoverUI.SetBar(_batTrack, _batFill, f);
            _batFill.GetComponent<Image>().color = !_battery ? RoverUI.Faint : charging ? RoverUI.Green : _battery.IsLow ? RoverUI.Red : RoverUI.Accent;
            _powerText.text = charging
                ? Tag("CHRG", RoverUI.Green, "Beacon in range: charging.")
                : !_battery || _battery.IsEmpty ? Tag("EMPT", RoverUI.Red, "Battery flat. Park in a beacon's zone to recharge.")
                : Tag("INFO", RoverUI.Dim, "Recharges inside a beacon's zone.");
        }

        static string Tag(string tag, Color c, string msg) => $"<color={RoverUI.Hex(c)}>[{tag}]</color> {msg}";

        void AnimateRoom()
        {
            if (_fuelLevel)
            {
                float f = _tank ? _tank.Fraction : 0f;
                float h = Mathf.Max(0.001f, _fuelLevelMaxHeight * f);
                var target = new Vector3(_fuelLevelFullScale.x, h, _fuelLevelFullScale.z);
                _fuelLevel.localScale = Vector3.Lerp(_fuelLevel.localScale, target, 1f - Mathf.Exp(-6f * Time.deltaTime));
                var p = _fuelLevel.localPosition;
                _fuelLevel.localPosition = new Vector3(p.x, _fuelLevelBottom + _fuelLevel.localScale.y * 0.5f, p.z);
            }
            if (_cam)
            {
                // Keep the 3D view to the right of the 400 px side panel.
                if (_canvasScaler)
                {
                    float frac = Mathf.Clamp01(400f * _canvasScaler.scaleFactor / Mathf.Max(1, Screen.width));
                    _cam.rect = new Rect(frac, 0f, 1f - frac, 1f);
                }
                // Subtle mouse parallax.
                var m = Mouse.current;
                Vector2 p = m != null ? m.position.ReadValue() : new Vector2(Screen.width, Screen.height) * 0.5f;
                Vector2 n = new Vector2(p.x / Mathf.Max(1, Screen.width) - 0.5f, p.y / Mathf.Max(1, Screen.height) - 0.5f);
                var target = _camBaseRot * Quaternion.Euler(-n.y * 6f, n.x * 10f, 0f);
                _cam.transform.localRotation = Quaternion.Slerp(_cam.transform.localRotation, target, 1f - Mathf.Exp(-4f * Time.deltaTime));
                // Shake when the hull takes a hit.
                _shake = Mathf.Max(0f, _shake - Time.deltaTime);
                _cam.transform.localPosition = _camBasePos + Random.insideUnitSphere * (0.08f * _shake * _shake * 4f);
            }
        }

        void Leave()
        {
            if (_tank && _tank.IsEmpty)
            {
                Toast(FuelTank.Reserve > 0f ? "Tank is empty. Refuel before leaving." : "No fuel left");
                return;
            }
            RoverInterior.Exit();
        }

        void Toast(string msg)
        {
            _toast.gameObject.SetActive(true);
            _toastText.text = msg;
            _toast.sizeDelta = new Vector2(Mathf.Max(220f, msg.Length * 9f + 40f), 36);
            _toastTimer = 2.2f;
        }
    }
}
