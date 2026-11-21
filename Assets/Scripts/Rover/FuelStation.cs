using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Bobery.Rover
{
    /// <summary>
    /// Refuelling at the cabin's fuel tank. Click the tank and the crew member walks over; a "REFUEL" popup
    /// appears above them (like the cockpit's weapons popup). The fuel panel shows the tank and the fuel you
    /// carry: hold the pump button (or SPACE) and the pump spins up, pushing fuel from your jerrycans into
    /// the tank; let go and it winds down. The crew works the pump and the tank's level bar rises as it fills.
    /// Added by <see cref="InteriorRoom"/>.
    /// </summary>
    public class FuelStation : MonoBehaviour
    {
        [Tooltip("Litres per second at full pump speed.")]
        public float flowRate = 8f;
        [Tooltip("Seconds for the pump to reach full speed while held.")]
        public float spinUp = 1.2f;
        [Tooltip("Seconds for the pump to stop after letting go.")]
        public float spinDown = 0.35f;
        [Tooltip("Distance (m) in front of the tank's gauge where the crew stands to pump.")]
        public float standDistance = 1.0f;

        public bool IsOpen { get; private set; }
        /// <summary>Mouse is over this component's UI: the cabin shouldn't react to clicks.</summary>
        public bool PointerOverUi { get; private set; }
        public bool CrewAtTank { get; private set; }
        public bool Pumping => _flow > 0.01f;

        InteriorRepairs _repairs;
        Camera _cam;
        FuelTank _tank;
        Transform _tankRoot;
        /// <summary>The fuel tank in the cabin.</summary>
        public Transform Tank => _tankRoot;
        Renderer _levelRenderer;
        MaterialPropertyBlock _mpb;
        Color _levelEmission;
        Vector3 _stand;
        Canvas _canvas;
        RectTransform _root;

        RectTransform _popup, _marker;
        TextMeshProUGUI _markerText;
        readonly UIButtons _popupButtons = new UIButtons();

        CanvasGroup _panelGroup;
        RectTransform _panel, _gaugeFill, _gaugeBox, _flowTrack, _flowFill, _pumpBtn, _pumpFill;
        Image[] _pumpEdges;
        TextMeshProUGUI _tankText, _carriedText, _flowText, _pumpedText, _pumpLabel, _status;
        readonly UIButtons _panelButtons = new UIButtons();
        float _panelT, _flow, _pumped, _gaugeShown;

        static readonly RaycastHit[] _hits = new RaycastHit[8];
        CabinZones.Zone _zone;
        const float PanelW = 460f, PanelH = 540f;

        public void Init(InteriorRepairs repairs, Camera cam, FuelTank tank, Transform level)
        {
            _repairs = repairs;
            _cam = cam;
            _tank = tank;
            _tankRoot = level ? level.parent : null;
            if (level)
            {
                _levelRenderer = level.GetComponent<Renderer>();
                _mpb = new MaterialPropertyBlock();
                var mat = _levelRenderer ? _levelRenderer.sharedMaterial : null;
                _levelEmission = mat && mat.HasProperty("_EmissionColor") ? mat.GetColor("_EmissionColor") : Color.white;
            }
            // Stand in front of the gauge (the tank's local -Z faces the camera), on walkable floor.
            if (_tankRoot)
            {
                Vector3 front = -_tankRoot.forward; front.y = 0f;
                _stand = _tankRoot.position + front.normalized * standDistance;
                if (repairs && repairs.Nav != null) _stand = repairs.Nav.Nearest(_stand);
            }
            if (_tankRoot) _zone = CabinZones.Add(_tankRoot);
            Build();
        }

        void OnDestroy()
        {
            if (_zone != null) CabinZones.Remove(_zone);
            if (_canvas) Destroy(_canvas.gameObject);
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            _pumped = 0f;
            SetStatus("");
        }

        public void Close()
        {
            IsOpen = false;
        }

        /// <summary>Send the crew to the tank.</summary>
        public void GoToTank()
        {
            var crew = _repairs ? _repairs.Crew : null;
            if (crew && _tankRoot) crew.WalkTo(_stand);
        }

        // ================================================================ update

        void Update()
        {
            var crew = _repairs ? _repairs.Crew : null;
            var kb = Keyboard.current;
            var mouse = Mouse.current;
            Vector2 sp = mouse != null ? mouse.position.ReadValue() : Vector2.zero;
            float dt = Time.deltaTime;

            // Anywhere near the tank counts (the closest station wins, see CabinZones).
            CrewAtTank = crew && _tankRoot && crew.State == RepairCrew.Mode.Idle && CabinZones.IsActive(_zone, crew.transform.position);
            if (!CrewAtTank && IsOpen) Close();

            // Popup above the crew at the tank; a marker over the tank otherwise.
            bool popup = CrewAtTank && !IsOpen && !GameOverScreen.IsShown;
            _popup.gameObject.SetActive(popup && Place(_popup, crew.transform.position + Vector3.up * 2.15f));
            bool marker = _tankRoot && !CrewAtTank && !IsOpen && !GameOverScreen.IsShown;
            _marker.gameObject.SetActive(marker && Place(_marker, _tankRoot.position + Vector3.up * 2.35f));
            if (marker && _tank)
            {
                bool low = _tank.IsLow || _tank.IsEmpty;
                _markerText.text = low ? "REFUEL!" : "FUEL";
                _markerText.color = low ? RoverUI.Yellow : RoverUI.Accent;
            }

            PointerOverUi = false;
            if (popup)
            {
                PointerOverUi = _popupButtons.Update();
                if (kb != null && kb.eKey.wasPressedThisFrame && !TutorialTips.IsOpen) Open();
            }
            else if (IsOpen && kb != null && kb.eKey.wasPressedThisFrame && !TutorialTips.IsOpen) Close();

            // Pump: held → spins up to full flow; released → winds down.
            bool held = false;
            if (IsOpen && !TutorialTips.IsOpen)
            {
                PointerOverUi = RectTransformUtility.RectangleContainsScreenPoint(_panel, sp, null) | _panelButtons.Update();
                bool overPump = RectTransformUtility.RectangleContainsScreenPoint(_pumpBtn, sp, null);
                held = (mouse != null && mouse.leftButton.isPressed && overPump) || (kb != null && kb.spaceKey.isPressed);
            }
            bool canPump = _tank && FuelTank.Reserve > 0.0001f && _tank.Fuel < _tank.capacity - 0.01f;
            float target = held && canPump ? flowRate : 0f;
            float rate = target > _flow ? flowRate / Mathf.Max(0.05f, spinUp) : flowRate / Mathf.Max(0.05f, spinDown);
            _flow = Mathf.MoveTowards(_flow, target, rate * dt);
            if (_flow > 0f && _tank)
            {
                float added = _tank.RefuelFromInventory(_flow * dt);
                _pumped += added;
                if (added <= 0f) _flow = 0f;
            }
            if (held && !canPump && _tank)
                SetStatus(_tank.Fuel >= _tank.capacity - 0.01f ? "Tank is full." : "No fuel left to pump. Collect fuel on the map.");
            else if (held) SetStatus("");

            if (crew)
            {
                crew.Working = CrewAtTank && IsOpen && _flow > 0.05f;
                if (_tankRoot) crew.WorkFacing = _tankRoot.position - crew.transform.position;
            }

            UpdatePanel(held, canPump);
            UpdateLevelGlow();
        }

        /// <summary>Clicking the tank walks the crew to it (after the cabin's own click handling).</summary>
        void LateUpdate()
        {
            var crew = _repairs ? _repairs.Crew : null;
            var mouse = Mouse.current;
            if (!crew || !_cam || !_tankRoot || mouse == null || IsOpen || PointerOverUi || GameOverScreen.IsShown || TutorialTips.IsOpen) return;
            if (!mouse.leftButton.wasPressedThisFrame) return;
            Vector2 sp = mouse.position.ReadValue();
            if (!_cam.pixelRect.Contains(sp) || sp.x < 400f * (_canvas ? _canvas.scaleFactor : 1f)) return;
            int n = Physics.RaycastNonAlloc(_cam.ScreenPointToRay(sp), _hits, 50f, ~(1 << 2), QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            Transform hit = null;
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].transform.IsChildOf(crew.transform)) continue;
                if (_hits[i].distance < best) { best = _hits[i].distance; hit = _hits[i].transform; }
            }
            if (hit && hit.IsChildOf(_tankRoot)) GoToTank();
        }

        /// <summary>The level bar on the tank pulses brighter while fuel flows in.</summary>
        void UpdateLevelGlow()
        {
            if (!_levelRenderer) return;
            float k = flowRate > 0f ? _flow / flowRate : 0f;
            float pulse = 1f + k * (0.8f + 0.6f * Mathf.Sin(Time.time * 14f));
            _levelRenderer.GetPropertyBlock(_mpb);
            _mpb.SetColor("_EmissionColor", _levelEmission * pulse);
            _levelRenderer.SetPropertyBlock(_mpb);
        }

        void UpdatePanel(bool held, bool canPump)
        {
            _panelT = Mathf.MoveTowards(_panelT, IsOpen ? 1f : 0f, Time.unscaledDeltaTime / 0.18f);
            _panelGroup.alpha = _panelT;
            _panel.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, _panelT);
            _panel.gameObject.SetActive(_panelT > 0.001f);
            if (_panelT <= 0.001f) return;

            float fuel = _tank ? _tank.Fuel : 0f, cap = _tank ? _tank.capacity : 1f;
            _gaugeShown = Mathf.Lerp(_gaugeShown, fuel / cap, 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
            _gaugeFill.sizeDelta = new Vector2(0f, _gaugeBox.rect.height * Mathf.Clamp01(_gaugeShown));
            Color c = !_tank ? RoverUI.Faint : _tank.IsLow || _tank.IsEmpty ? RoverUI.Red : fuel / cap < 0.5f ? RoverUI.Yellow : RoverUI.Accent;
            _gaugeFill.GetComponent<Image>().color = Pumping ? Color.Lerp(c, Color.white, 0.25f + 0.25f * Mathf.Sin(Time.time * 14f)) : c;

            _tankText.text = _tank ? $"{fuel:0.0} <color=#8A9099>/ {cap:0} L</color>" : "NO ROVER";
            _carriedText.text = $"{FuelTank.Reserve:0.0} <color=#8A9099>L</color>";
            _flowText.text = $"{_flow:0.0} <color=#8A9099>L/s</color>";
            RoverUI.SetBar(_flowTrack, _flowFill, flowRate > 0f ? _flow / flowRate : 0f);
            _pumpedText.text = _pumped > 0.05f ? $"+{_pumped:0.0} L pumped" : "";

            // Pump button: fills with the pump speed while held.
            float k = flowRate > 0f ? _flow / flowRate : 0f;
            _pumpFill.sizeDelta = new Vector2(_pumpBtn.rect.width * k, 0f);
            Color edge = !canPump ? RoverUI.Faint : held ? RoverUI.Green : RoverUI.Accent;
            RoverUI.SetOutline(_pumpEdges, edge);
            _pumpLabel.color = canPump ? (k > 0.5f ? RoverUI.OnAccent : RoverUI.White) : RoverUI.Dim;
            _pumpLabel.text = !canPump ? (_tank && _tank.Fuel >= _tank.capacity - 0.01f ? "TANK FULL" : "NO FUEL CARRIED")
                            : held ? (k < 0.999f ? "SPINNING UP..." : "PUMPING") : "HOLD TO PUMP  [ SPACE ]";
        }

        void SetStatus(string msg)
        {
            if (_status) _status.text = msg;
        }

        bool Place(RectTransform rt, Vector3 world)
        {
            if (!_cam) return false;
            Vector3 s = _cam.WorldToScreenPoint(world);
            if (s.z <= 0f || !_cam.pixelRect.Contains(s)) return false;
            rt.position = new Vector3(s.x, s.y, 0f);
            return true;
        }

        // ================================================================ UI

        void Build()
        {
            _canvas = RoverUI.NewCanvas("FuelStation", transform, 24);
            _root = (RectTransform)_canvas.transform;

            // Popup above the crew at the tank.
            _popup = RoverUI.Rect("FuelPopup", _root);
            _popup.anchorMin = _popup.anchorMax = Vector2.zero;
            _popup.pivot = new Vector2(0.5f, 0f);
            _popup.sizeDelta = new Vector2(250, 64);
            RoverUI.Img(_popup, RoverUI.A(RoverUI.Black, 0.92f));
            RoverUI.Outline(_popup, RoverUI.Accent);
            var pt = RoverUI.Label(_popup, "FUEL TANK", 11, FontStyle.Bold, RoverUI.Dim, mono: true);
            RoverUI.TopLeft(pt.rectTransform, 10, 6, 230, 14);
            _popupButtons.Add(_popup, "REFUEL  [ E ]", 10, 24, 230, 32, true, Open, 13);
            Arrow(_popup, RoverUI.Accent, 10);

            // Marker over the tank when the crew is elsewhere.
            _marker = RoverUI.Rect("FuelMarker", _root);
            _marker.anchorMin = _marker.anchorMax = Vector2.zero;
            _marker.pivot = new Vector2(0.5f, 0f);
            _marker.sizeDelta = new Vector2(96, 22);
            Arrow(_marker, RoverUI.A(RoverUI.Accent, 0.7f), 8);
            RoverUI.Img(_marker, RoverUI.A(RoverUI.Black, 0.7f));
            RoverUI.Outline(_marker, RoverUI.A(RoverUI.Accent, 0.5f));
            _markerText = RoverUI.Label(_marker, "FUEL", 11, FontStyle.Bold, RoverUI.Accent, mono: true);
            RoverUI.Stretch(_markerText.rectTransform);
            _markerText.alignment = TextAlignmentOptions.Center;

            BuildPanel();
        }

        static void Arrow(RectTransform parent, Color c, float size)
        {
            var arrow = RoverUI.Rect("Arrow", parent);
            arrow.anchorMin = arrow.anchorMax = new Vector2(0.5f, 0f);
            RoverUI.Img(arrow, c);
            arrow.sizeDelta = new Vector2(size, size);
            arrow.anchoredPosition = new Vector2(0, -2);
            arrow.localRotation = Quaternion.Euler(0, 0, 45);
        }

        void BuildPanel()
        {
            const float pad = 28f;
            _panel = RoverUI.Rect("FuelPanel", _root);
            // Left part of the 3D view (right of the 400 px side panel), so the tank stays in sight.
            _panel.anchorMin = _panel.anchorMax = new Vector2(0f, 0.5f);
            _panel.pivot = new Vector2(0f, 0.5f);
            _panel.anchoredPosition = new Vector2(440f, 0f);
            _panel.sizeDelta = new Vector2(PanelW, PanelH);
            RoverUI.Img(_panel, RoverUI.Black);
            RoverUI.Outline(_panel, RoverUI.Accent);
            _panelGroup = _panel.gameObject.AddComponent<CanvasGroup>();

            var crumbs = RoverUI.Label(_panel, RoverUI.Crumbs("ROOT", "ROVER", "CABIN", "FUEL_TANK"), 12, FontStyle.Bold, RoverUI.Dim);
            RoverUI.TopLeft(crumbs.rectTransform, pad, pad, PanelW - pad * 2, 18);
            var title = RoverUI.Label(_panel, "FUEL TRANSFER", 30, FontStyle.Bold, RoverUI.Accent);
            RoverUI.TopLeft(title.rectTransform, pad, pad + 22, PanelW - pad * 2, 40);
            var sub = RoverUI.Label(_panel, "Hold the pump to move fuel from your jerrycans into the rover's tank.", 13, FontStyle.Normal, RoverUI.Dim);
            RoverUI.TopLeft(sub.rectTransform, pad, pad + 66, PanelW - pad * 2, 36);

            // Big vertical tank gauge on the left.
            float top = pad + 116f, gaugeH = 210f;
            _gaugeBox = RoverUI.Rect("TankGauge", _panel);
            RoverUI.TopLeft(_gaugeBox, pad, top, 90f, gaugeH);
            RoverUI.Img(_gaugeBox, RoverUI.Ghost);
            RoverUI.Outline(_gaugeBox, RoverUI.Faint, 2f);
            _gaugeFill = RoverUI.Rect("Fill", _gaugeBox);
            _gaugeFill.anchorMin = new Vector2(0, 0); _gaugeFill.anchorMax = new Vector2(1, 0);
            _gaugeFill.pivot = new Vector2(0.5f, 0f);
            _gaugeFill.sizeDelta = Vector2.zero;
            RoverUI.Img(_gaugeFill, RoverUI.Accent);
            for (int i = 1; i < 4; i++)
            {
                var tick = RoverUI.Rect("Tick", _gaugeBox);
                tick.anchorMin = new Vector2(0, i / 4f); tick.anchorMax = new Vector2(0.3f, i / 4f);
                tick.sizeDelta = new Vector2(0, 2);
                RoverUI.Img(tick, RoverUI.A(RoverUI.White, 0.35f));
            }

            // Readouts on the right.
            float rx = pad + 120f, rw = PanelW - rx - pad;
            float y = top;
            Readout("ROVER_TANK", rx, ref y, rw, out _tankText);
            Readout("CARRIED_FUEL", rx, ref y, rw, out _carriedText);
            Readout("PUMP_FLOW", rx, ref y, rw, out _flowText);
            _flowTrack = RoverUI.Bar(_panel, rx, y - 10f, rw, 6, out _flowFill);
            _pumpedText = RoverUI.Label(_panel, "", 13, FontStyle.Bold, RoverUI.Green, mono: true);
            RoverUI.TopLeft(_pumpedText.rectTransform, rx, y + 8f, rw, 18);

            // Hold-to-pump button.
            float by = top + gaugeH + 22f;
            _pumpBtn = RoverUI.Rect("PumpButton", _panel);
            RoverUI.TopLeft(_pumpBtn, pad, by, PanelW - pad * 2, 52);
            RoverUI.Img(_pumpBtn, RoverUI.A(RoverUI.Black, 0.85f));
            _pumpFill = RoverUI.Rect("Fill", _pumpBtn);
            _pumpFill.anchorMin = new Vector2(0, 0); _pumpFill.anchorMax = new Vector2(0, 1);
            _pumpFill.pivot = new Vector2(0, 0.5f);
            _pumpFill.sizeDelta = Vector2.zero;
            RoverUI.Img(_pumpFill, RoverUI.Accent);
            _pumpEdges = RoverUI.Outline(_pumpBtn, RoverUI.Accent, 2f);
            _pumpLabel = RoverUI.Label(_pumpBtn, "HOLD TO PUMP  [ SPACE ]", 15, FontStyle.Bold, RoverUI.White);
            RoverUI.Stretch(_pumpLabel.rectTransform);
            _pumpLabel.alignment = TextAlignmentOptions.Center;

            _status = RoverUI.Label(_panel, "", 12, FontStyle.Bold, RoverUI.Yellow);
            RoverUI.TopLeft(_status.rectTransform, pad, by + 60f, PanelW - pad * 2, 18);

            var close = RoverUI.Rect("Close", _panel);
            close.anchorMin = close.anchorMax = new Vector2(1, 0);
            close.pivot = new Vector2(1, 0);
            close.anchoredPosition = new Vector2(-pad, pad);
            close.sizeDelta = new Vector2(190, 40);
            _panelButtons.Wrap(close, "CLOSE  [ ESC ]", false, Close);

            _panel.gameObject.SetActive(false);
            _panelGroup.alpha = 0f;
        }

        void Readout(string label, float x, ref float y, float w, out TextMeshProUGUI value)
        {
            var l = RoverUI.Label(_panel, label, 11, FontStyle.Bold, RoverUI.Accent, mono: true);
            RoverUI.TopLeft(l.rectTransform, x, y, w, 14);
            value = RoverUI.Label(_panel, "", 22, FontStyle.Bold, RoverUI.White, mono: true);
            RoverUI.TopLeft(value.rectTransform, x, y + 16f, w, 28);
            y += 64f;
        }

        static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
    }
}
