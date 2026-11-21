using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Bobery.Rover
{
    /// <summary>
    /// "ENTER ROVER" popup floating over the hatch on the rover's roof (the dome, <see cref="hatchName"/>) in the map view.
    /// Clicking it goes inside the rover. Added by <see cref="RoverMapPanel"/>, so it hides with the map while inside.
    /// </summary>
    public class RoverHatch : MonoBehaviour
    {
        [Tooltip("Name of the rover model part with the hatch (the roof dome).")]
        public string hatchName = "Icosphere.003";
        [Tooltip("Popup height above the top of the dome (metres).")]
        public float lift = 0.6f;

        /// <summary>Mouse is over the popup: the turret shouldn't fire on this click.</summary>
        public static bool PointerOver { get; private set; }

        RoverController _rover;
        Renderer _hatch;
        Canvas _canvas;
        RectTransform _popup;
        readonly UIButtons _buttons = new UIButtons();

        public void Init(RoverController rover)
        {
            _rover = rover;
            if (rover)
                foreach (var r in rover.GetComponentsInChildren<Renderer>(true))
                    if (r.name == hatchName) { _hatch = r; break; }
            Build();
        }

        void OnDisable() => PointerOver = false;

        void OnDestroy()
        {
            PointerOver = false;
            if (_canvas) Destroy(_canvas.gameObject);
        }

        /// <summary>Top of the hatch dome (or of the rover, if the dome isn't found).</summary>
        Vector3 Anchor()
        {
            if (_hatch) return _hatch.bounds.center + Vector3.up * (_hatch.bounds.extents.y + lift);
            var rs = _rover.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return _rover.transform.position + Vector3.up * 3f;
            Bounds b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return new Vector3(b.center.x, b.max.y + lift, b.center.z);
        }

        void LateUpdate()
        {
            PointerOver = false;
            var cam = Camera.main;
            bool show = _rover && cam && !RoverInterior.IsInside && !RoverInterior.IsTransitioning && !GameOverScreen.IsShown;
            if (show)
            {
                Vector3 s = cam.WorldToScreenPoint(Anchor());
                show = s.z > 0f && cam.pixelRect.Contains(s);
                if (show) _popup.position = new Vector3(s.x, s.y, 0f);
            }
            _popup.gameObject.SetActive(show);
            if (!show) return;

            // Hovering anywhere on the popup (not just the button) keeps the click away from the gun.
            var mouse = Mouse.current;
            bool overPopup = mouse != null && RectTransformUtility.RectangleContainsScreenPoint(_popup, mouse.position.ReadValue(), null);
            PointerOver = _buttons.Update() | overPopup;
        }

        void Build()
        {
            _canvas = RoverUI.NewCanvas("RoverHatch", transform, 15);
            var root = (RectTransform)_canvas.transform;

            // A small "ENTER" tag with a little arrow under it; the arrow's tip points at the hatch.
            const float w = 64f, h = 22f, tip = 5f;
            _popup = RoverUI.Rect("HatchPopup", root);
            _popup.anchorMin = _popup.anchorMax = Vector2.zero;
            _popup.pivot = new Vector2(0.5f, 0f);
            _popup.sizeDelta = new Vector2(w, h + tip);

            // Arrow first, so the button covers its top half and only a small triangle shows below.
            var arrow = RoverUI.Rect("Arrow", _popup);
            arrow.anchorMin = arrow.anchorMax = new Vector2(0.5f, 0f);
            float side = tip * 1.414f;
            arrow.sizeDelta = new Vector2(side, side);
            arrow.anchoredPosition = new Vector2(0f, tip);
            arrow.localRotation = Quaternion.Euler(0, 0, 45);
            RoverUI.Img(arrow, RoverUI.Accent);

            var btn = RoverUI.Rect("Btn_ENTER", _popup);
            RoverUI.TopLeft(btn, 0, 0, w, h);
            _buttons.Wrap(btn, "ENTER", true, RoverInterior.Enter, 11);
            _popup.gameObject.SetActive(false);
        }
    }
}
