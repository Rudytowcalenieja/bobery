using System;
using System.Collections.Generic;
using TMPro;
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
        // Terminal palette: near-black panels, thin grey rules, cyan accent, status colours.
        public static readonly Color Black = new Color32(10, 11, 13, 250);    // panel background
        public static readonly Color White = new Color32(228, 231, 235, 255); // primary text
        public static readonly Color Dim = new Color32(138, 144, 153, 255);   // muted text
        public static readonly Color Faint = new Color32(46, 50, 56, 255);    // borders / rules / tracks
        public static readonly Color Ghost = new Color32(18, 20, 24, 255);    // raised surface (cards, slots)
        public static readonly Color Accent = new Color32(125, 211, 252, 255); // cyan
        public static readonly Color Green = new Color32(74, 222, 128, 255);
        public static readonly Color Yellow = new Color32(232, 240, 106, 255);
        public static readonly Color Red = new Color32(240, 82, 82, 255);
        public static readonly Color Purple = new Color32(167, 139, 250, 255);
        public static readonly Color OnAccent = new Color32(6, 8, 10, 255);   // text on a filled accent

        static Font _font, _mono;
        static Sprite _circle;

        static readonly string[] MonoFonts = { "Consolas", "Menlo", "Courier New" };

        // Terminal look: everything is monospace (Consolas).
        public static Font Mono => _mono ? _mono : _mono = LoadMono();
        public static Font Font => _font ? _font : _font = Mono;

        /// <summary>First installed font from <see cref="MonoFonts"/>, loaded on its own (no fallback list) so Unity can't pick another one.</summary>
        static Font LoadMono()
        {
            var installed = new HashSet<string>(Font.GetOSInstalledFontNames(), StringComparer.OrdinalIgnoreCase);
            foreach (var name in MonoFonts)
            {
                if (!installed.Contains(name)) continue;
                var f = Font.CreateDynamicFontFromOSFont(name, 16);
                if (f) { f.name = name; Debug.Log($"[RoverUI] UI font: {name}"); return f; }
            }
            Debug.LogWarning("[RoverUI] No monospace OS font found, using Unity's default font.");
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        }

        static TMP_FontAsset _tmp;
        static Material _shadowMat;

        /// <summary>
        /// Consolas as a TextMesh Pro signed-distance-field font, built at runtime from the OS font file.
        /// SDF text stays sharp at any canvas scale (the legacy Text component blurs small text).
        /// </summary>
        public static TMP_FontAsset Tmp => _tmp ? _tmp : _tmp = LoadTmp();

        static TMP_FontAsset LoadTmp()
        {
            TMP_FontAsset fa = null;
            foreach (var name in MonoFonts)
            {
                fa = TMP_FontAsset.CreateFontAsset(name, "Regular");
                if (!fa) continue;
                fa.name = name + " SDF";
                var bold = TMP_FontAsset.CreateFontAsset(name, "Bold");
                if (bold)
                {
                    bold.name = name + " Bold SDF";
                    fa.fontWeightTable[7].regularTypeface = bold; // real bold face instead of faux bold
                }
                Debug.Log($"[RoverUI] UI font: {name} (TextMesh Pro SDF)");
                break;
            }
            if (!fa)
            {
                Debug.LogWarning("[RoverUI] No monospace OS font found, using TextMesh Pro's default font.");
                return TMP_Settings.defaultFontAsset;
            }

            // Symbols Consolas lacks (arrows, ✕ ...) come from Windows' symbol fonts.
            var fallbacks = new List<TMP_FontAsset>();
            foreach (var fb in new[] { "Segoe UI Symbol", "Segoe UI" })
            {
                var f = TMP_FontAsset.CreateFontAsset(fb, "Regular");
                if (f) fallbacks.Add(f);
            }
            if (TMP_Settings.defaultFontAsset) fallbacks.Add(TMP_Settings.defaultFontAsset);
            fa.fallbackFontAssetTable = fallbacks;
            return fa;
        }

        /// <summary>Dark drop shadow under the text (for labels drawn over the map).</summary>
        public static void Shadowed(TMP_Text t)
        {
            if (!_shadowMat)
            {
                _shadowMat = new Material(Tmp.material) { name = Tmp.name + " Shadow" };
                _shadowMat.EnableKeyword(ShaderUtilities.Keyword_Underlay);
                _shadowMat.SetColor(ShaderUtilities.ID_UnderlayColor, new Color(0f, 0f, 0f, 0.9f));
                _shadowMat.SetFloat(ShaderUtilities.ID_UnderlayOffsetX, 0.5f);
                _shadowMat.SetFloat(ShaderUtilities.ID_UnderlayOffsetY, -0.5f);
                _shadowMat.SetFloat(ShaderUtilities.ID_UnderlaySoftness, 0.3f);
            }
            if (t.font == Tmp) t.fontSharedMaterial = _shadowMat;
        }

        // Domain reload is disabled in this project, so statics survive between play sessions: reset the font cache.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetFonts() { _font = null; _mono = null; }

        /// <summary>Colour with a different alpha.</summary>
        public static Color A(Color c, float a) { c.a = a; return c; }

        /// <summary>"#rrggbb" for rich-text tags.</summary>
        public static string Hex(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);

        /// <summary>Breadcrumb like "ROOT / ROVER / NAVIGATION" with the last part highlighted.</summary>
        public static string Crumbs(params string[] parts)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                if (i > 0) sb.Append(" <color=").Append(Hex(Faint)).Append(">/</color> ");
                sb.Append(i == parts.Length - 1 ? $"<color={Hex(White)}>{parts[i]}</color>" : parts[i]);
            }
            return sb.ToString();
        }

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

        public static TextMeshProUGUI Label(Transform parent, string text, int size, FontStyle style, Color color, bool mono = false)
        {
            var rt = Rect("Text", parent);
            var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
            t.font = Tmp;
            t.fontSize = size;
            t.fontStyle = style == FontStyle.Bold ? FontStyles.Bold
                        : style == FontStyle.Italic ? FontStyles.Italic
                        : style == FontStyle.BoldAndItalic ? FontStyles.Bold | FontStyles.Italic : FontStyles.Normal;
            t.color = color;
            t.raycastTarget = false;
            t.richText = true;
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow;
            t.text = text;
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

        public static Image[] Outline(RectTransform rt, Color c, float t = 1f)
        {
            Image Edge(string n, Vector2 aMin, Vector2 aMax, Vector2 size)
            {
                var e = Rect(n, rt);
                e.anchorMin = aMin; e.anchorMax = aMax;
                e.pivot = new Vector2(0.5f, 0.5f);
                e.sizeDelta = size; e.anchoredPosition = Vector2.zero;
                return Img(e, c);
            }
            return new[]
            {
                Edge("T", new Vector2(0, 1), new Vector2(1, 1), new Vector2(0, t)),
                Edge("B", new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, t)),
                Edge("L", new Vector2(0, 0), new Vector2(0, 1), new Vector2(t, 0)),
                Edge("R", new Vector2(1, 0), new Vector2(1, 1), new Vector2(t, 0)),
            };
        }

        public static void SetOutline(Image[] edges, Color c)
        {
            if (edges == null) return;
            foreach (var e in edges) if (e) e.color = c;
        }

        /// <summary>Panel header: accent title (e.g. "ROVER_TANK") with a rule underneath. Returns the y below it.</summary>
        public static float Header(RectTransform parent, string title, float x, float y, float w, string right = null)
        {
            var t = Label(parent, title, 14, FontStyle.Bold, Accent);
            TopLeft(t.rectTransform, x, y, w, 20);
            if (right != null)
            {
                var r = Label(parent, right, 11, FontStyle.Normal, Dim);
                TopLeft(r.rectTransform, x, y + 2, w, 18);
                r.alignment = TextAlignmentOptions.TopRight;
            }
            var rule = Rect("Rule", parent);
            TopLeft(rule, x, y + 28, w, 1);
            Img(rule, Faint);
            return y + 40;
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
            Img(fill, Accent);
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
            public Image[] edges;
            public TextMeshProUGUI label;
            public Action onClick;
            public bool filled;
            public bool enabled = true;
            public Color tone = RoverUI.Accent; // fill colour of a filled button, highlight on hover
        }

        readonly List<Button> _buttons = new List<Button>();

        /// <summary>These buttons keep working while a tutorial tip is open (the tip's own buttons).</summary>
        public bool ignoreTutorial;

        public Button Add(RectTransform parent, string text, float x, float y, float w, float h, bool filled, Action onClick, int fontSize = 13)
        {
            var rt = RoverUI.Rect("Btn_" + text, parent);
            RoverUI.TopLeft(rt, x, y, w, h);
            return Wrap(rt, text, filled, onClick, fontSize);
        }

        public Button Wrap(RectTransform rt, string text, bool filled, Action onClick, int fontSize = 13)
        {
            var bg = RoverUI.Img(rt, Color.clear);
            var edges = RoverUI.Outline(rt, RoverUI.Faint);
            var label = RoverUI.Label(rt, text, fontSize, FontStyle.Bold, RoverUI.White);
            RoverUI.Stretch(label.rectTransform);
            label.alignment = TextAlignmentOptions.Center;
            var b = new Button { rt = rt, bg = bg, edges = edges, label = label, onClick = onClick, filled = filled };
            Paint(b, false);
            _buttons.Add(b);
            return b;
        }

        public static void Paint(Button b, bool hover) => Paint(b.bg, b.edges, b.label, b.filled, b.enabled, hover, b.tone);

        /// <summary>Terminal button look. Filled: solid tone with dark text. Outline: dark with a grey border; hover lights it in the tone.</summary>
        public static void Paint(Image bg, Image[] edges, TextMeshProUGUI label, bool filled, bool enabled, bool hover, Color tone)
        {
            float a = enabled ? 1f : 0.3f;
            hover &= enabled;
            if (filled)
            {
                Color c = RoverUI.A(hover ? Color.Lerp(tone, Color.white, 0.35f) : tone, a);
                bg.color = c;
                RoverUI.SetOutline(edges, c);
                label.color = RoverUI.A(RoverUI.OnAccent, a);
            }
            else
            {
                bg.color = hover ? RoverUI.A(tone, 0.12f) : RoverUI.A(RoverUI.Black, 0.85f * a);
                RoverUI.SetOutline(edges, hover ? tone : RoverUI.A(RoverUI.Faint, a));
                label.color = hover ? tone : RoverUI.A(RoverUI.White, a);
            }
        }

        /// <summary>Updates hover colours and fires clicks. Returns true if the mouse is over any button.</summary>
        public bool Update()
        {
            var mouse = Mouse.current;
            if (mouse == null) return false;
            if (TutorialTips.IsOpen && !ignoreTutorial)
            {
                foreach (var b in _buttons) if (b.rt && b.rt.gameObject.activeInHierarchy) Paint(b, false);
                return false;
            }
            Vector2 screen = mouse.position.ReadValue();
            bool click = mouse.leftButton.wasPressedThisFrame;
            bool over = false;
            Action clicked = null;
            foreach (var b in _buttons)
            {
                if (!b.rt || !b.rt.gameObject.activeInHierarchy) continue;
                bool hover = b.enabled && RectTransformUtility.RectangleContainsScreenPoint(b.rt, screen, null);
                over |= hover;
                Paint(b, hover);
                if (hover && click) clicked = b.onClick;
            }
            clicked?.Invoke(); // after the loop: a click may add/remove buttons
            return over;
        }
    }
}
