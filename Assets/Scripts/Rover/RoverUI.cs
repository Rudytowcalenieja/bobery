using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Bobery.Rover
{
    /// <summary>Global HUD messages (e.g. "+25 L fuel collected"). The map panel shows them as toasts.</summary>
    public static class RoverHud
    {
        public static event Action<string> Notified;
        public static void Notify(string message) => Notified?.Invoke(message);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay() => Notified = null;
    }

    /// <summary>
    /// Shared monochrome UI kit (uGUI built from code) used by the interior, inventory and game-over screens,
    /// matching the navigation panel's look.
    /// </summary>
    public static class RoverUI
    {
        public static readonly Color Black = new Color(0.03f, 0.03f, 0.035f, 0.97f);
        public static readonly Color White = Color.white;
        public static readonly Color Dim = new Color(1f, 1f, 1f, 0.45f);
        public static readonly Color Faint = new Color(1f, 1f, 1f, 0.12f);
        public static readonly Color Ghost = new Color(1f, 1f, 1f, 0.05f);

        static Font _font, _mono;
        static Sprite _circle;

        public static Font Font => _font ? _font : _font = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Helvetica Neue", "Arial" }, 16)
                                                           ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        public static Font Mono => _mono ? _mono : _mono = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Menlo", "Courier New" }, 16) ?? Font;

        public static Canvas NewCanvas(string name, Transform parent, int sortingOrder)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler));
            if (parent) go.transform.SetParent(parent, false);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;
            return canvas;
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static Image Img(RectTransform rt, Color c)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        public static Text Label(Transform parent, string text, int size, FontStyle style, Color color, bool mono = false)
        {
            var rt = Rect("Text", parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = mono ? Mono : Font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.color = color;
            t.raycastTarget = false;
            t.supportRichText = true;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        public static void TopLeft(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        public static void Center(RectTransform rt, float w, float h, float x = 0f, float y = 0f)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(w, h);
        }

        public static void Stretch(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
        }

        public static void Outline(RectTransform rt, Color c, float t = 1f)
        {
            void Edge(string n, Vector2 aMin, Vector2 aMax, Vector2 size)
            {
                var e = Rect(n, rt);
                e.anchorMin = aMin; e.anchorMax = aMax;
                e.pivot = new Vector2(0.5f, 0.5f);
                e.sizeDelta = size; e.anchoredPosition = Vector2.zero;
                Img(e, c);
            }
            Edge("T", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, t));
            Edge("B", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, t));
            Edge("L", new Vector2(0, 0), new Vector2(0, 1), new Vector2(t, 0));
            Edge("R", new Vector2(1, 0), new Vector2(1, 1), new Vector2(t, 0));
        }

        /// <summary>Horizontal bar; returns the fill image (set its rectTransform width via SetBar).</summary>
        public static RectTransform Bar(RectTransform parent, float x, float y, float w, float h, out RectTransform fill)
        {
            var track = Rect("BarTrack", parent);
            TopLeft(track, x, y, w, h);
            Img(track, Faint);
            fill = Rect("Fill", track);
            fill.anchorMin = new Vector2(0, 0); fill.anchorMax = new Vector2(0, 1);
            fill.pivot = new Vector2(0, 0.5f); fill.sizeDelta = Vector2.zero;
            Img(fill, White);
            return track;
        }

        public static void SetBar(RectTransform track, RectTransform fill, float fraction)
        {
            fill.sizeDelta = new Vector2(track.rect.width * Mathf.Clamp01(fraction), 0f);
        }

        public static Sprite Circle
        {
            get
            {
                if (_circle) return _circle;
                const int n = 32;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
                var px = new Color32[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f)) / (n * 0.5f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(255 * Mathf.Clamp01((1f - d) * n * 0.5f / 1.5f)));
                }
                tex.SetPixels32(px); tex.Apply();
                return _circle = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
            }
        }
    }

    /// <summary>Mouse-driven buttons without an EventSystem: call <see cref="Update"/> every frame.</summary>
    public class UIButtons
    {
        public class Button
        {
            public RectTransform rt;
            public Image bg;
            public Text label;
            public Action onClick;
            public bool filled;
            public bool enabled = true;
        }

        readonly List<Button> _buttons = new List<Button>();

        public Button Add(RectTransform parent, string text, float x, float y, float w, float h, bool filled, Action onClick, int fontSize = 13)
        {
            var rt = RoverUI.Rect("Btn_" + text, parent);
            RoverUI.TopLeft(rt, x, y, w, h);
            return Wrap(rt, text, filled, onClick, fontSize);
        }

        public Button Wrap(RectTransform rt, string text, bool filled, Action onClick, int fontSize = 13)
        {
            var bg = RoverUI.Img(rt, filled ? RoverUI.White : new Color(0, 0, 0, 0.55f));
            RoverUI.Outline(rt, new Color(1, 1, 1, 0.6f));
            var label = RoverUI.Label(rt, text, fontSize, FontStyle.Bold, filled ? Color.black : RoverUI.White);
            RoverUI.Stretch(label.rectTransform);
            label.alignment = TextAnchor.MiddleCenter;
            var b = new Button { rt = rt, bg = bg, label = label, onClick = onClick, filled = filled };
            _buttons.Add(b);
            return b;
        }

        /// <summary>Updates hover colours and fires clicks. Returns true if the mouse is over any button.</summary>
        public bool Update()
        {
            var mouse = Mouse.current;
            if (mouse == null) return false;
            Vector2 screen = mouse.position.ReadValue();
            bool click = mouse.leftButton.wasPressedThisFrame;
            bool over = false;
            foreach (var b in _buttons)
            {
                if (!b.rt || !b.rt.gameObject.activeInHierarchy) continue;
                bool hover = b.enabled && RectTransformUtility.RectangleContainsScreenPoint(b.rt, screen, null);
                over |= hover;
                bool light = b.filled != hover;
                float a = b.enabled ? 1f : 0.3f;
                b.bg.color = light ? new Color(1, 1, 1, a) : new Color(0, 0, 0, 0.55f);
                b.label.color = light ? new Color(0, 0, 0, a) : new Color(1, 1, 1, a);
                if (hover && click) b.onClick?.Invoke();
            }
            return over;
        }
    }
}
