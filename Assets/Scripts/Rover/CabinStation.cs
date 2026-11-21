using System;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Bobery.Rover
{
    /// <summary>
    /// A simple spot in the rover cabin the crew can use: click its object (a seat, the server rack...) and the crew
    /// walks over; a popup appears above them with a status line and one action button ([E] or click). When the crew
    /// is elsewhere, a small marker floats over the spot. Same look as the cockpit's weapons popup.
    /// Added by <see cref="InteriorRoom"/> (steering seat, power switch).
    /// </summary>
    public class CabinStation : MonoBehaviour
    {
        public bool CrewAtStation { get; private set; }
        /// <summary>Mouse is over this station's popup: the cabin shouldn't react to clicks.</summary>
        public bool PointerOverUi { get; private set; }
        /// <summary>Another menu is open: hide the popup and ignore input.</summary>
        public Func<bool> Blocked;

        InteriorRepairs _repairs;
        Camera _cam;
        Transform _target;
        /// <summary>The station's object (seat, rack...).</summary>
        public Transform Target => _target;
        Vector3 _stand, _markerAt;
        Func<string> _button;
        Func<(string text, Color color)> _state;
        Action _use;
        Canvas _canvas;
        RectTransform _popup, _marker;
        TextMeshProUGUI _stateText, _markerText;
        UIButtons.Button _btn;
        readonly UIButtons _buttons = new UIButtons();
        static readonly RaycastHit[] _hits = new RaycastHit[8];
        CabinZones.Zone _zone;

        /// <param name="target">Clicking anything under this walks the crew here.</param>
        /// <param name="stand">Where the crew stands (snapped to walkable floor).</param>
        /// <param name="title">Small heading of the popup (e.g. "PILOT SEAT").</param>
        /// <param name="marker">Text of the marker shown when the crew is elsewhere.</param>
        /// <param name="button">Action button text (re-read every frame).</param>
        /// <param name="use">What the button / [E] does.</param>
        /// <param name="state">Optional status line above the button (re-read every frame).</param>
        public void Init(InteriorRepairs repairs, Camera cam, Transform target, Vector3 stand, string title, string marker,
                         Func<string> button, Action use, Func<(string, Color)> state = null)
        {
            _repairs = repairs;
            _cam = cam;
            _target = target;
            _stand = repairs && repairs.Nav != null ? repairs.Nav.Nearest(stand) : stand;
            _markerAt = target ? target.position + Vector3.up * 1.6f : _stand + Vector3.up * 1.4f;
            _button = button;
            _use = use;
            _state = state;
            if (target) _zone = CabinZones.Add(target);
            Build(title, marker);
        }

        void OnDestroy()
        {
            if (_zone != null) CabinZones.Remove(_zone);
            if (_canvas) Destroy(_canvas.gameObject);
        }

        /// <summary>Send the crew here.</summary>
        public void GoTo()
        {
            var crew = _repairs ? _repairs.Crew : null;
            if (crew) crew.WalkTo(_stand);
        }

        void Update()
        {
            var crew = _repairs ? _repairs.Crew : null;
            bool blocked = GameOverScreen.IsShown || (Blocked != null && Blocked());
            // Anywhere near the station's object counts (the closest station wins, see CabinZones).
            CrewAtStation = crew && crew.State == RepairCrew.Mode.Idle && CabinZones.IsActive(_zone, crew.transform.position);

            bool popup = CrewAtStation && !blocked;
            _popup.gameObject.SetActive(popup && Place(_popup, crew.transform.position + Vector3.up * 2.15f));
            _marker.gameObject.SetActive(!CrewAtStation && !blocked && Place(_marker, _markerAt));

            PointerOverUi = false;
            if (!popup) return;
            _btn.label.text = _button != null ? _button() : "USE";
            if (_state != null)
            {
                var (text, color) = _state();
                _stateText.text = text;
                _stateText.color = color;
            }
            PointerOverUi = _buttons.Update();
            var kb = Keyboard.current;
            if (kb != null && kb.eKey.wasPressedThisFrame && !TutorialTips.IsOpen) _use?.Invoke();
        }

        /// <summary>Clicking the station's object walks the crew to it (after the cabin's own click handling).</summary>
        void LateUpdate()
        {
            var crew = _repairs ? _repairs.Crew : null;
            var mouse = Mouse.current;
            if (!crew || !_cam || !_target || mouse == null || PointerOverUi || GameOverScreen.IsShown || TutorialTips.IsOpen) return;
            if (Blocked != null && Blocked()) return;
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
            if (hit && hit.IsChildOf(_target)) GoTo();
        }

        bool Place(RectTransform rt, Vector3 world)
        {
            if (!_cam) return false;
            Vector3 s = _cam.WorldToScreenPoint(world);
            if (s.z <= 0f || !_cam.pixelRect.Contains(s)) return false;
            rt.position = new Vector3(s.x, s.y, 0f);
            return true;
        }

        void Build(string title, string marker)
        {
            _canvas = RoverUI.NewCanvas("Station_" + title, transform, 24);
            var root = (RectTransform)_canvas.transform;
            bool hasState = _state != null;

            _popup = RoverUI.Rect("Popup", root);
            _popup.anchorMin = _popup.anchorMax = Vector2.zero;
            _popup.pivot = new Vector2(0.5f, 0f);
            _popup.sizeDelta = new Vector2(270, hasState ? 86 : 64);
            RoverUI.Img(_popup, RoverUI.A(RoverUI.Black, 0.92f));
            RoverUI.Outline(_popup, RoverUI.Accent);
            var pt = RoverUI.Label(_popup, title, 11, FontStyle.Bold, RoverUI.Dim, mono: true);
            RoverUI.TopLeft(pt.rectTransform, 10, 6, 250, 14);
            float y = 24;
            if (hasState)
            {
                _stateText = RoverUI.Label(_popup, "", 12, FontStyle.Bold, RoverUI.White, mono: true);
                RoverUI.TopLeft(_stateText.rectTransform, 10, 22, 250, 16);
                y = 46;
            }
            _btn = _buttons.Add(_popup, "", 10, y, 250, 32, true, () => _use?.Invoke(), 13);
            Arrow(_popup, RoverUI.Accent, 10);

            _marker = RoverUI.Rect("Marker", root);
            _marker.anchorMin = _marker.anchorMax = Vector2.zero;
            _marker.pivot = new Vector2(0.5f, 0f);
            _marker.sizeDelta = new Vector2(Mathf.Max(80, marker.Length * 9 + 24), 22);
            Arrow(_marker, RoverUI.A(RoverUI.Accent, 0.7f), 8);
            RoverUI.Img(_marker, RoverUI.A(RoverUI.Black, 0.7f));
            RoverUI.Outline(_marker, RoverUI.A(RoverUI.Accent, 0.5f));
            _markerText = RoverUI.Label(_marker, marker, 11, FontStyle.Bold, RoverUI.Accent, mono: true);
            RoverUI.Stretch(_markerText.rectTransform);
            _markerText.alignment = TextAlignmentOptions.Center;
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

        static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
    }
}
