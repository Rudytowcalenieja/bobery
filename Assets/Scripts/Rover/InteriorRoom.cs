using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Bobery.Rover
{
    /// <summary>
    /// Placeholder rover interior (scene #2). Builds a simple cabin with a fuel tank, its own camera,
    /// a HUD, and the player inventory window:
    ///   I   – open / close inventory (pour fuel into the tank from there)
    ///   Esc – close inventory, or leave the rover
    /// Works standalone too (open the scene directly) — then there's just no rover tank to fill.
    /// </summary>
    public class InteriorRoom : MonoBehaviour
    {
        [Tooltip("Litres poured per click of the small pour button.")]
        public float pourStep = 10f;

        FuelTank _tank;
        Camera _cam;
        Canvas _canvasScaler;
        Transform _fuelLevel;
        float _fuelLevelMaxHeight;

        // HUD
        readonly UIButtons _hudButtons = new UIButtons();
        RectTransform _gaugeTrack, _gaugeFill;
        Text _gaugeText, _reserveText, _statusText, _toastText;
        RectTransform _toast;
        float _toastTimer;

        // Inventory
        readonly UIButtons _invButtons = new UIButtons();
        RectTransform _inv;
        CanvasGroup _invGroup;
        RectTransform _slotGrid;
        Text _detailName, _detailAmount, _detailDesc;
        RectTransform _invGaugeTrack, _invGaugeFill;
        Text _invGaugeText;
        UIButtons.Button _pourBtn, _fillBtn;
        string _selected;
        readonly System.Collections.Generic.List<(RectTransform rt, string id)> _slotRects =
            new System.Collections.Generic.List<(RectTransform, string)>();
        bool _invOpen;
        float _invT;
        const int Columns = 4, Rows = 3;
        const float SlotSize = 96f, SlotGap = 10f;

        void Start()
        {
            var rover = FindAnyObjectByType<RoverController>();
            if (rover) _tank = rover.GetComponent<FuelTank>();

            BuildRoom();
            BuildHud();
            BuildInventory();
            PlayerInventory.Changed += RebuildSlots;
            RebuildSlots();
            SetInventoryOpen(false, instant: true);
        }

        void OnDestroy() => PlayerInventory.Changed -= RebuildSlots;

        // ================================================================ room (placeholder geometry)

        void BuildRoom()
        {
            var lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material Mat(Color c)
            {
                var m = new Material(lit) { color = c };
                if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
                return m;
            }
            var wall = Mat(new Color(0.16f, 0.16f, 0.17f));
            var floor = Mat(new Color(0.1f, 0.1f, 0.11f));
            var panel = Mat(new Color(0.85f, 0.85f, 0.85f));
            var dark = Mat(new Color(0.05f, 0.05f, 0.05f));
            var glow = Mat(Color.white);
            if (glow.HasProperty("_EmissionColor")) { glow.EnableKeyword("_EMISSION"); glow.SetColor("_EmissionColor", Color.white * 1.5f); }

            Transform Box(string n, Vector3 pos, Vector3 size, Material m)
            {
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
                g.name = n;
                g.transform.SetParent(transform, false);
                g.transform.localPosition = pos;
                g.transform.localScale = size;
                g.GetComponent<Renderer>().sharedMaterial = m;
                return g.transform;
            }

            // Cabin 8 x 3 x 6 m
            Box("Floor", new Vector3(0, -0.05f, 0), new Vector3(8, 0.1f, 6), floor);
            Box("Ceiling", new Vector3(0, 3.05f, 0), new Vector3(8, 0.1f, 6), wall);
            Box("WallBack", new Vector3(0, 1.5f, 3), new Vector3(8, 3, 0.1f), wall);
            Box("WallFront", new Vector3(0, 1.5f, -3), new Vector3(8, 3, 0.1f), wall);
            Box("WallLeft", new Vector3(-4, 1.5f, 0), new Vector3(0.1f, 3, 6), wall);
            Box("WallRight", new Vector3(4, 1.5f, 0), new Vector3(0.1f, 3, 6), wall);

            // Light strips
            Box("Strip1", new Vector3(0, 2.98f, -1.2f), new Vector3(6, 0.04f, 0.12f), glow);
            Box("Strip2", new Vector3(0, 2.98f, 1.2f), new Vector3(6, 0.04f, 0.12f), glow);

            // Console + screen on the back wall
            Box("Console", new Vector3(-1.6f, 0.5f, 2.5f), new Vector3(3, 1, 0.8f), panel);
            Box("Screen", new Vector3(-1.6f, 1.7f, 2.92f), new Vector3(2.6f, 1.2f, 0.05f), dark);
            Box("Seat", new Vector3(-1.6f, 0.45f, 1.2f), new Vector3(0.9f, 0.9f, 0.9f), dark);

            // Fuel tank (glass-ish frame with a level column inside)
            var tankRoot = new GameObject("FuelTank").transform;
            tankRoot.SetParent(transform, false);
            tankRoot.localPosition = new Vector3(2.5f, 0f, 2f);
            var shell = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            shell.name = "Shell";
            shell.transform.SetParent(tankRoot, false);
            shell.transform.localPosition = new Vector3(0, 1.3f, 0);
            shell.transform.localScale = new Vector3(1.2f, 1.25f, 1.2f);
            shell.GetComponent<Renderer>().sharedMaterial = dark;
            _fuelLevelMaxHeight = 2.3f;
            // Level gauge: a glowing strip on the front of the tank, facing the camera.
            var gaugeBack = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gaugeBack.name = "GaugeBack";
            gaugeBack.transform.SetParent(tankRoot, false);
            gaugeBack.transform.localPosition = new Vector3(0, 0.15f + _fuelLevelMaxHeight * 0.5f, -0.6f);
            gaugeBack.transform.localScale = new Vector3(0.34f, _fuelLevelMaxHeight + 0.08f, 0.04f);
            gaugeBack.GetComponent<Renderer>().sharedMaterial = wall;
            var level = GameObject.CreatePrimitive(PrimitiveType.Cube);
            level.name = "Level";
            level.transform.SetParent(tankRoot, false);
            level.GetComponent<Renderer>().sharedMaterial = glow;
            _fuelLevel = level.transform;
            Box("Pipe", new Vector3(2.5f, 2.75f, 1.2f), new Vector3(0.15f, 0.15f, 1.8f), panel);

            // Lights
            var lightGo = new GameObject("CabinLight");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.localPosition = new Vector3(0, 2.6f, 0);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.range = 12f;
            l.intensity = 6f;
            l.color = new Color(1f, 0.97f, 0.92f);

            // Camera (in the doorway, looking in)
            var camGo = new GameObject("InteriorCamera");
            camGo.transform.SetParent(transform, false);
            camGo.transform.localPosition = new Vector3(0, 1.7f, -2.6f);
            camGo.transform.localRotation = Quaternion.Euler(12f, 0, 0);
            _cam = camGo.AddComponent<Camera>();
            _cam.fieldOfView = 70f;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = Color.black;
            _cam.nearClipPlane = 0.05f;
            camGo.AddComponent<AudioListener>();
            camGo.tag = "MainCamera";
        }

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
            const float pad = 24f, inner = 352f;

            var eyebrow = RoverUI.Label(side, "ROVER  /  INTERIOR", 12, FontStyle.Bold, RoverUI.Dim);
            RoverUI.TopLeft(eyebrow.rectTransform, pad, pad, inner, 18);
            var title = RoverUI.Label(side, "Cabin", 34, FontStyle.Bold, RoverUI.White);
            RoverUI.TopLeft(title.rectTransform, pad, pad + 24, inner, 44);
            var sub = RoverUI.Label(side, "Refuel the rover from your inventory.", 14, FontStyle.Normal, RoverUI.Dim);
            RoverUI.TopLeft(sub.rectTransform, pad, pad + 72, inner, 20);

            float y = pad + 112;
            var lbl = RoverUI.Label(side, "ROVER TANK", 12, FontStyle.Bold, RoverUI.Dim);
            RoverUI.TopLeft(lbl.rectTransform, pad, y, inner, 16);
            _gaugeText = RoverUI.Label(side, "", 12, FontStyle.Bold, RoverUI.White, mono: true);
            RoverUI.TopLeft(_gaugeText.rectTransform, pad, y, inner, 16);
            _gaugeText.alignment = TextAnchor.UpperRight;
            _gaugeTrack = RoverUI.Bar(side, pad, y + 24, inner, 10, out _gaugeFill);
            y += 50;
            _reserveText = RoverUI.Label(side, "", 13, FontStyle.Normal, RoverUI.Dim, mono: true);
            RoverUI.TopLeft(_reserveText.rectTransform, pad, y, inner, 18);
            y += 34;
            _statusText = RoverUI.Label(side, "", 15, FontStyle.Bold, RoverUI.White);
            RoverUI.TopLeft(_statusText.rectTransform, pad, y, inner, 44);
            y += 60;

            _hudButtons.Add(side, "INVENTORY  [ I ]", pad, y, inner, 46, true, () => SetInventoryOpen(!_invOpen));
            y += 56;
            _hudButtons.Add(side, "LEAVE ROVER  [ ESC ]", pad, y, inner, 46, false, Leave);

            // Footer key hints
            var hint = RoverUI.Label(side, "<b>I</b>  Inventory     <b>ESC</b>  Leave / close", 12, FontStyle.Normal, RoverUI.Dim, mono: true);
            hint.rectTransform.anchorMin = new Vector2(0, 0); hint.rectTransform.anchorMax = new Vector2(1, 0);
            hint.rectTransform.pivot = new Vector2(0, 0);
            hint.rectTransform.offsetMin = new Vector2(pad, pad); hint.rectTransform.offsetMax = new Vector2(-pad, pad + 20);

            // Toast (bottom centre of the view)
            _toast = RoverUI.Rect("Toast", root);
            _toast.anchorMin = _toast.anchorMax = new Vector2(0.5f, 0);
            _toast.pivot = new Vector2(0.5f, 0);
            _toast.anchoredPosition = new Vector2(200, 60);
            _toast.sizeDelta = new Vector2(320, 36);
            RoverUI.Img(_toast, new Color(0, 0, 0, 0.85f));
            RoverUI.Outline(_toast, new Color(1, 1, 1, 0.25f));
            _toastText = RoverUI.Label(_toast, "", 14, FontStyle.Bold, RoverUI.White);
            RoverUI.Stretch(_toastText.rectTransform);
            _toastText.alignment = TextAnchor.MiddleCenter;
            _toast.gameObject.SetActive(false);

            // Inventory lives on the same canvas, above everything.
            _inv = RoverUI.Rect("Inventory", root);
        }

        // ================================================================ inventory window

        void BuildInventory()
        {
            float gridW = Columns * SlotSize + (Columns - 1) * SlotGap;
            float gridH = Rows * SlotSize + (Rows - 1) * SlotGap;
            float detailW = 300f;
            float w = 40 + gridW + 30 + detailW + 40;
            float h = 110 + gridH + 40;

            RoverUI.Stretch(_inv);
            _invGroup = _inv.gameObject.AddComponent<CanvasGroup>();
            var dim = RoverUI.Rect("Dim", _inv);
            RoverUI.Stretch(dim);
            RoverUI.Img(dim, new Color(0, 0, 0, 0.6f));

            var card = RoverUI.Rect("Card", _inv);
            RoverUI.Center(card, w, h, 200f, 0f);
            RoverUI.Img(card, RoverUI.Black);
            RoverUI.Outline(card, RoverUI.Faint);

            var eyebrow = RoverUI.Label(card, "PLAYER", 12, FontStyle.Bold, RoverUI.Dim);
            RoverUI.TopLeft(eyebrow.rectTransform, 40, 32, 300, 16);
            var title = RoverUI.Label(card, "Inventory", 30, FontStyle.Bold, RoverUI.White);
            RoverUI.TopLeft(title.rectTransform, 40, 50, 300, 40);
            var close = RoverUI.Rect("Close", card);
            close.anchorMin = close.anchorMax = new Vector2(1, 1);
            close.pivot = new Vector2(1, 1);
            close.anchoredPosition = new Vector2(-30, -30);
            close.sizeDelta = new Vector2(36, 36);
            _invButtons.Wrap(close, "✕", false, () => SetInventoryOpen(false), 16);

            _slotGrid = RoverUI.Rect("Slots", card);
            RoverUI.TopLeft(_slotGrid, 40, 110, gridW, gridH);

            // Details
            float dx = 40 + gridW + 30;
            var div = RoverUI.Rect("Divider", card);
            RoverUI.TopLeft(div, dx - 15, 110, 1, gridH);
            RoverUI.Img(div, RoverUI.Faint);

            _detailName = RoverUI.Label(card, "", 22, FontStyle.Bold, RoverUI.White);
            RoverUI.TopLeft(_detailName.rectTransform, dx, 110, detailW, 30);
            _detailAmount = RoverUI.Label(card, "", 14, FontStyle.Bold, RoverUI.Dim, mono: true);
            RoverUI.TopLeft(_detailAmount.rectTransform, dx, 142, detailW, 20);
            _detailDesc = RoverUI.Label(card, "", 13, FontStyle.Normal, RoverUI.Dim);
            RoverUI.TopLeft(_detailDesc.rectTransform, dx, 172, detailW, 60);

            var tl = RoverUI.Label(card, "ROVER TANK", 11, FontStyle.Bold, RoverUI.Dim);
            RoverUI.TopLeft(tl.rectTransform, dx, 240, detailW, 14);
            _invGaugeText = RoverUI.Label(card, "", 11, FontStyle.Bold, RoverUI.White, mono: true);
            RoverUI.TopLeft(_invGaugeText.rectTransform, dx, 240, detailW, 14);
            _invGaugeText.alignment = TextAnchor.UpperRight;
            _invGaugeTrack = RoverUI.Bar(card, dx, 260, detailW, 8, out _invGaugeFill);

            float bw = (detailW - 10f) / 2f;
            _pourBtn = _invButtons.Add(card, $"POUR {pourStep:0} L", dx, 286, bw, 44, false, () => Pour(pourStep));
            _fillBtn = _invButtons.Add(card, "FILL TANK", dx + bw + 10, 286, bw, 44, true, () => Pour(float.MaxValue));
        }

        void RebuildSlots()
        {
            if (!_slotGrid) return;
            for (int i = _slotGrid.childCount - 1; i >= 0; i--) Destroy(_slotGrid.GetChild(i).gameObject);
            _slotRects.Clear();

            var ids = PlayerInventory.ItemIds;
            if (_selected == null || PlayerInventory.Get(_selected) <= 0f) _selected = ids.Count > 0 ? ids[0] : null;

            for (int i = 0; i < Columns * Rows; i++)
            {
                int cx = i % Columns, cy = i / Columns;
                var slot = RoverUI.Rect("Slot" + i, _slotGrid);
                RoverUI.TopLeft(slot, cx * (SlotSize + SlotGap), cy * (SlotSize + SlotGap), SlotSize, SlotSize);
                bool has = i < ids.Count;
                bool sel = has && ids[i] == _selected;
                RoverUI.Img(slot, sel ? new Color(1, 1, 1, 0.14f) : RoverUI.Ghost);
                RoverUI.Outline(slot, sel ? RoverUI.White : RoverUI.Faint, sel ? 2f : 1f);
                if (!has) continue;

                string id = ids[i];
                var icon = RoverUI.Rect("Icon", slot);
                RoverUI.Center(icon, 30, 40, 0, 10);
                RoverUI.Img(icon, RoverUI.White);
                var capRt = RoverUI.Rect("Cap", icon);
                capRt.anchorMin = capRt.anchorMax = new Vector2(0.7f, 1f);
                capRt.pivot = new Vector2(0.5f, 0f);
                capRt.sizeDelta = new Vector2(8, 6);
                RoverUI.Img(capRt, RoverUI.White);

                var name = RoverUI.Label(slot, PlayerInventory.NameOf(id).ToUpperInvariant(), 10, FontStyle.Bold, RoverUI.Dim);
                RoverUI.TopLeft(name.rectTransform, 8, 6, SlotSize - 16, 14);
                var amt = RoverUI.Label(slot, $"{PlayerInventory.Get(id):0.#} {PlayerInventory.UnitOf(id)}", 12, FontStyle.Bold, RoverUI.White, mono: true);
                amt.rectTransform.anchorMin = new Vector2(0, 0); amt.rectTransform.anchorMax = new Vector2(1, 0);
                amt.rectTransform.pivot = new Vector2(0.5f, 0);
                amt.rectTransform.offsetMin = new Vector2(6, 6); amt.rectTransform.offsetMax = new Vector2(-6, 22);
                amt.alignment = TextAnchor.LowerCenter;

                _slotRects.Add((slot, id));
            }
            UpdateDetails();
        }

        void UpdateDetails()
        {
            if (_selected == null)
            {
                _detailName.text = "Empty";
                _detailAmount.text = "";
                _detailDesc.text = "Nothing in your inventory. Collect fuel on the map.";
            }
            else
            {
                _detailName.text = PlayerInventory.NameOf(_selected);
                _detailAmount.text = $"{PlayerInventory.Get(_selected):0.#} {PlayerInventory.UnitOf(_selected)}";
                _detailDesc.text = PlayerInventory.DescriptionOf(_selected);
            }
            bool fuelSel = _selected == PlayerInventory.Fuel;
            bool canPour = fuelSel && _tank && FuelTank.Reserve > 0f && _tank.Fuel < _tank.capacity - 0.01f;
            _pourBtn.enabled = canPour;
            _fillBtn.enabled = canPour;
            _pourBtn.rt.gameObject.SetActive(fuelSel);
            _fillBtn.rt.gameObject.SetActive(fuelSel);
        }

        void Pour(float litres)
        {
            if (!_tank) { Toast("No rover tank found"); return; }
            float added = _tank.RefuelFromInventory(litres);
            Toast(added > 0f ? $"+{added:0.#} L in the tank" : _tank.Fuel >= _tank.capacity - 0.01f ? "Tank is full" : "No fuel to pour");
        }

        // ================================================================ runtime

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && !GameOverScreen.IsShown)
            {
                if (kb.iKey.wasPressedThisFrame) SetInventoryOpen(!_invOpen);
                if (kb.escapeKey.wasPressedThisFrame)
                {
                    if (_invOpen) SetInventoryOpen(false);
                    else Leave();
                }
            }

            if (_invOpen)
            {
                _invButtons.Update();
                var mouse = Mouse.current;
                if (mouse != null && mouse.leftButton.wasPressedThisFrame)
                {
                    Vector2 sp = mouse.position.ReadValue();
                    foreach (var (rt, id) in _slotRects)
                        if (rt && RectTransformUtility.RectangleContainsScreenPoint(rt, sp, null)) { _selected = id; RebuildSlots(); break; }
                }
            }
            else _hudButtons.Update();

            // Inventory fade/scale
            _invT = Mathf.MoveTowards(_invT, _invOpen ? 1f : 0f, Time.unscaledDeltaTime / 0.18f);
            _invGroup.alpha = _invT;
            _inv.gameObject.SetActive(_invT > 0.001f);

            UpdateGauges();
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
            _invGaugeText.text = g;
            RoverUI.SetBar(_gaugeTrack, _gaugeFill, fuel / cap);
            RoverUI.SetBar(_invGaugeTrack, _invGaugeFill, fuel / cap);
            _reserveText.text = $"Carried fuel   {FuelTank.Reserve:0.#} L";

            if (!_tank) _statusText.text = "";
            else if (_tank.IsEmpty && FuelTank.Reserve > 0f) _statusText.text = "Tank empty. Open the inventory (I) and pour fuel in.";
            else if (_tank.IsLow) _statusText.text = "Fuel low.";
            else _statusText.text = "Ready to go.";

            if (_invOpen) UpdateDetails();
        }

        void AnimateRoom()
        {
            if (_fuelLevel)
            {
                float f = _tank ? _tank.Fraction : 0f;
                float h = Mathf.Max(0.001f, _fuelLevelMaxHeight * f);
                var target = new Vector3(0.26f, h, 0.06f);
                _fuelLevel.localScale = Vector3.Lerp(_fuelLevel.localScale, target, 1f - Mathf.Exp(-6f * Time.deltaTime));
                _fuelLevel.localPosition = new Vector3(0, 0.15f + _fuelLevel.localScale.y * 0.5f, -0.63f);
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
                var target = Quaternion.Euler(12f - n.y * 6f, n.x * 10f, 0f);
                _cam.transform.localRotation = Quaternion.Slerp(_cam.transform.localRotation, target, 1f - Mathf.Exp(-4f * Time.deltaTime));
            }
        }

        void SetInventoryOpen(bool open, bool instant = false)
        {
            _invOpen = open;
            if (instant) { _invT = open ? 1f : 0f; _invGroup.alpha = _invT; _inv.gameObject.SetActive(open); }
            if (open) RebuildSlots();
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
