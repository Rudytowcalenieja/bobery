using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Bobery.Rover
{
    /// <summary>
    /// Tutorial tips. Each tip pauses the game, dims the screen around the thing it explains, points at it with a
    /// bobbing arrow and shows a card with the explanation. CLOSE (or ENTER) dismisses it and the game carries on
    /// (or the next tip of the same sequence shows up). SKIP TUTORIAL turns all tips off.
    ///
    /// Turned on from the main menu (<see cref="TutorialPrompt"/>) via <see cref="Enabled"/>, started by
    /// <see cref="RoverMapPanel"/>. Lives across scene loads (the interior is loaded on top of the map, restarts
    /// reload the map) so every tip is shown only once per run of the game.
    /// </summary>
    public class TutorialTips : MonoBehaviour
    {
        /// <summary>Player asked for the tutorial in the main menu.</summary>
        public static bool Enabled { get; set; }

        /// <summary>A tip is on screen: gameplay input must be ignored (also for the frame it was closed in).</summary>
        public static bool IsOpen => _instance && (_instance._current != null || Time.frameCount <= _instance._closedFrame);

        static TutorialTips _instance;
        static readonly HashSet<string> Seen = new HashSet<string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetOnPlay()
        {
            _instance = null;
            Seen.Clear();
            Enabled = false;
        }

        /// <summary>Called by the navigation panel when the map scene starts.</summary>
        public static void Begin()
        {
            if (!Enabled || _instance) return;
            var go = new GameObject("TutorialTips");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<TutorialTips>();
        }

        // ================================================================ tips

        class Tip
        {
            public string id, title, body;
            public Func<Rect?> target; // screen-pixel rect to point at, or null (centred card, no arrow)
            public int step, steps;    // position in its sequence (0 = a one-off tip)
        }

        readonly Queue<Tip> _queue = new Queue<Tip>();
        Tip _current;
        int _closedFrame = -1;
        float _savedTimeScale = 1f;
        float _shownT;
        float _outsideSince = -1f, _insideSince = -1f;

        // Cached scene objects (re-found after restarts / entering the rover).
        RoverMapPanel _panel;
        RoverController _rover;
        RoverHealth _health;
        FuelTank _tank;
        RoverBattery _battery;
        InteriorRoom _room;

        static string K(string s) => $"<color={RoverUI.Hex(RoverUI.Accent)}><b>{s}</b></color>";
        static string Y(string s) => $"<color={RoverUI.Hex(RoverUI.Yellow)}><b>{s}</b></color>";
        static string R(string s) => $"<color={RoverUI.Hex(RoverUI.Red)}><b>{s}</b></color>";

        /// <summary>Queues a sequence of tips (skipping ones already seen). Each entry: id, title, body, target.</summary>
        void Enqueue(params (string id, string title, string body, Func<Rect?> target)[] tips)
        {
            var fresh = new List<Tip>();
            foreach (var t in tips)
                if (Seen.Add(t.id)) fresh.Add(new Tip { id = t.id, title = t.title, body = t.body, target = t.target });
            for (int i = 0; i < fresh.Count; i++)
            {
                if (fresh.Count > 1) { fresh[i].step = i + 1; fresh[i].steps = fresh.Count; }
                _queue.Enqueue(fresh[i]);
            }
        }

        void QueueMapTips()
        {
            Enqueue(
                ("welcome", "WELCOME, COMMANDER",
                    "You command a lone rover in hostile wasteland. Hunt down the enemy " + Y("outposts") +
                    " while keeping the rover fuelled, charged and in one piece.\n\nThese tips pause the game and explain each part of the controls.",
                    null),
                ("map", "NAVIGATION MAP",
                    "A live top-down view around your rover.\n\n" + K("LEFT-CLICK") + " a spot to drive there; the rover finds a route around obstacles.\n" +
                    K("RIGHT-CLICK") + " stops it.   " + K("MOUSE WHEEL") + " zooms.",
                    () => UiRect(_panel ? _panel.TutorialTarget("map") : null)),
                ("fuel", "FUEL",
                    "Driving burns fuel. " + K("RANGE") + " tells you how far you can still go.\n\n" +
                    "Drive over fuel canisters on the map to collect them (" + K("CARRIED") + "), then pump them into the tank from inside the rover.",
                    () => UiRect(_panel ? _panel.TutorialTarget("fuel") : null)),
                ("battery", "BATTERY",
                    "Powers the map and your gun, and slowly drains.\n\n" +
                    "When it runs " + R("empty") + " the map goes dark and the gun stops firing. Recharge it inside a beacon's zone.",
                    () => UiRect(_panel ? _panel.TutorialTarget("battery") : null)),
                ("beacons", "CHARGING BEACONS",
                    "Click a ready slot, then click the map to deploy a beacon (costs some fuel).\n\n" +
                    "Park inside its circle to " + K("recharge the battery") + ". Select a deployed beacon to " + K("PICK UP") + " it again. Enemies can shoot beacons down.",
                    () => UiRect(_panel ? _panel.TutorialTarget("beacons") : null)),
                ("hull", "HULL",
                    "Every enemy hit damages the hull and punches a " + R("leak") + " that keeps draining it.\n\n" +
                    "Patch leaks from inside the rover. At 0% the rover is destroyed.",
                    () => UiRect(_panel ? _panel.TutorialTarget("hull") : null)),
                ("outposts", "ENEMY OUTPOSTS",
                    Y("Yellow arrows") + " on the map lead to enemy outposts, " + R("red dots") + " are enemies.\n\n" +
                    "Destroy every turret of an outpost to take it. Captured outposts stop sending enemies and can reward you with " + K("new weapons") + ".",
                    () => UiRect(_panel ? _panel.TutorialTarget("map") : null)),
                ("weapons", "YOUR GUN",
                    "The turret on your rover turns toward the mouse cursor.\n\n" +
                    K("LEFT-CLICK") + " (or hold) over the 3D view to fire. Every shot costs a bit of battery.",
                    () => WorldRect(Camera.main, _rover ? _rover.transform : null)),
                ("hatch", "GOING INSIDE",
                    "Click " + K("ENTER") + " on the roof hatch to climb into the rover.\n\n" +
                    "Inside you repair leaks, refuel the tank, swap weapons, and return to the pilot seat to drive again.",
                    () => UiRect(RoverHatch.VisiblePopup)));
        }

        void QueueInteriorTips()
        {
            var room = _room;
            Enqueue(
                ("cabin", "THE CABIN",
                    "Your crew member works in here. " + K("LEFT-CLICK") + " the floor to walk, click a station to use it.\n\n" +
                    "This panel shows the tank, hull and battery. Time keeps running outside while you're in here!",
                    () => UiRect(room ? room.SidePanel : null)),
                ("tank", "FUEL TANK",
                    "Click the tank (or " + K("REFUEL AT THE TANK") + "). In the fuel panel hold " + K("PUMP") + " or " + K("SPACE") +
                    " to move your carried fuel into the tank.",
                    () => WorldRect(room ? room.Cam : null, room && room.Fuel ? room.Fuel.Tank : null)),
                ("gunner", "GUNNER SEAT",
                    "Walk to the right-hand seat and press " + K("E") + " to change weapons: drag a weapon from storage onto the turret mount.",
                    () => WorldRect(room ? room.Cam : null, room && room.Weapons ? room.Weapons.Seat : null)),
                ("pilot", "PILOT SEAT",
                    "Sit in the left-hand seat and press " + K("STEER [E]") + " to go back outside and drive.",
                    () => WorldRect(room ? room.Cam : null, room && room.PilotSeat ? room.PilotSeat.Target : null)));
        }

        /// <summary>Starts sequences and one-off tips when their moment comes.</summary>
        void CheckTriggers()
        {
            if (GameOverScreen.IsShown || RoverInterior.IsTransitioning) { _outsideSince = _insideSince = -1f; return; }
            float now = Time.unscaledTime;

            if (RoverInterior.IsInside)
            {
                _outsideSince = -1f;
                if (!_room) _room = FindAnyObjectByType<InteriorRoom>();
                if (!_room || !_room.SidePanel) return;
                if (_insideSince < 0f) _insideSince = now;
                if (now - _insideSince < 0.6f) return;

                if (!Seen.Contains("cabin")) QueueInteriorTips();
                if (!Seen.Contains("leak_in") && _room.Repairs && _room.Repairs.LeakCount > 0)
                {
                    var room = _room;
                    Enqueue(("leak_in", "PATCH THE LEAK",
                        "That hissing hole is a " + R("leak") + ". Click it and your crew member walks over and welds it shut, giving the hull back its health.",
                        () => LeakRect(room)));
                }
                return;
            }

            _insideSince = -1f;
            if (!_panel) _panel = FindAnyObjectByType<RoverMapPanel>();
            if (!_panel || !_panel.isActiveAndEnabled || !_panel.IsOpenFully) return;
            if (!_rover)
            {
                _rover = _panel.rover ? _panel.rover : FindAnyObjectByType<RoverController>();
                if (_rover) { _health = _rover.GetComponent<RoverHealth>(); _tank = _rover.GetComponent<FuelTank>(); _battery = _rover.GetComponent<RoverBattery>(); }
            }
            if (_outsideSince < 0f) _outsideSince = now;
            if (now - _outsideSince < 0.4f) return;

            if (!Seen.Contains("welcome")) QueueMapTips();

            if (_health && _health.Leaks.Count > 0)
                Enqueue(("leak_out", "YOU'RE HIT!",
                    "The hull took damage and is " + R("leaking") + ": it keeps losing health until you patch it.\n\n" +
                    "Click " + K("ENTER") + " on the roof hatch, then click the leak inside.",
                    () => UiRect(_panel ? _panel.TutorialTarget("hull") : null)));
            if (RoverUpgrades.Engine)
                Enqueue(("engine", "BOOST ENGINE",
                    "New upgrade! Press " + K("SHIFT") + " for a burst of speed. Afterwards the engine needs to cool down before the next boost.",
                    () => UiRect(_panel ? _panel.TutorialTarget("engine") : null)));
            if (_tank && _tank.IsLow)
                Enqueue(("fuel_low", "LOW FUEL",
                    "The tank is running dry. Collect fuel canisters on the map and pump them into the tank from inside the rover.",
                    () => UiRect(_panel ? _panel.TutorialTarget("fuel") : null)));
            if (_battery && _battery.IsLow)
                Enqueue(("battery_low", "LOW BATTERY",
                    "Deploy a beacon (or drive to one) and park inside its circle to recharge before the map goes dark.",
                    () => UiRect(_panel ? _panel.TutorialTarget("beacons") : null)));
        }

        // ================================================================ targets

        /// <summary>Screen rect of a UI element (overlay canvases: world space = screen pixels).</summary>
        static Rect? UiRect(RectTransform rt)
        {
            if (!rt || !rt.gameObject.activeInHierarchy) return null;
            var c = new Vector3[4];
            rt.GetWorldCorners(c);
            return Rect.MinMaxRect(c[0].x, c[0].y, c[2].x, c[2].y);
        }

        /// <summary>Screen rect around an object's renderers as seen by <paramref name="cam"/>.</summary>
        static Rect? WorldRect(Camera cam, Transform t)
        {
            if (!cam || !t || !cam.isActiveAndEnabled) return null;
            bool any = false;
            Bounds b = new Bounds(t.position, Vector3.one * 0.6f);
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            Vector2 min = new Vector2(float.MaxValue, float.MaxValue), max = new Vector2(float.MinValue, float.MinValue);
            for (int i = 0; i < 8; i++)
            {
                Vector3 p = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 s = cam.WorldToScreenPoint(p);
                if (s.z <= 0f) return null;
                min = Vector2.Min(min, s); max = Vector2.Max(max, s);
            }
            var rect = Rect.MinMaxRect(min.x, min.y, max.x, max.y);
            return rect.Overlaps(cam.pixelRect) ? rect : (Rect?)null;
        }

        static Rect? LeakRect(InteriorRoom room)
        {
            if (!room || !room.Repairs) return null;
            foreach (var s in room.Repairs.Spots)
                if (s) return WorldRect(room.Cam, s.transform);
            return null;
        }

        // ================================================================ UI

        const float CardW = 470f, Gap = 120f, Margin = 16f, Hole = 8f;

        Canvas _canvas;
        RectTransform _root, _card, _frame, _arrow, _arrowShaft;
        RectTransform[] _dims;
        Image[] _frameEdges;
        CanvasGroup _group;
        TextMeshProUGUI _eyebrow, _title, _body, _hint;
        UIButtons.Button _closeBtn, _skipBtn;
        readonly UIButtons _buttons = new UIButtons { ignoreTutorial = true };

        void Awake() => Build();

        void Build()
        {
            _canvas = RoverUI.NewCanvas("TutorialCanvas", transform, 500);
            _root = (RectTransform)_canvas.transform;
            _group = _canvas.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;

            _dims = new RectTransform[4];
            for (int i = 0; i < 4; i++)
            {
                _dims[i] = Corner("Dim" + i, _root);
                RoverUI.Img(_dims[i], new Color(0f, 0f, 0f, 0.62f));
            }

            _frame = Corner("Highlight", _root);
            _frameEdges = RoverUI.Outline(_frame, RoverUI.Accent, 2f);

            // Arrow: pivot at the tip, drawn "pointing down" (local -Y) and rotated toward the target.
            _arrow = Corner("Arrow", _root);
            _arrow.pivot = new Vector2(0.5f, 0f);
            _arrow.sizeDelta = new Vector2(34, 34);
            var head = RoverUI.Rect("Head", _arrow);
            head.anchorMin = head.anchorMax = new Vector2(0.5f, 0f);
            head.pivot = new Vector2(0.5f, 0f);
            head.sizeDelta = new Vector2(34, 30);
            head.anchoredPosition = Vector2.zero;
            RoverUI.Img(head, RoverUI.Accent).sprite = DownTriangle();
            _arrowShaft = RoverUI.Rect("Shaft", _arrow);
            _arrowShaft.anchorMin = _arrowShaft.anchorMax = new Vector2(0.5f, 0f);
            _arrowShaft.pivot = new Vector2(0.5f, 0f);
            _arrowShaft.anchoredPosition = new Vector2(0, 26);
            RoverUI.Img(_arrowShaft, RoverUI.Accent);

            // Card
            _card = Corner("Card", _root);
            _card.sizeDelta = new Vector2(CardW, 200);
            RoverUI.Img(_card, RoverUI.Black);
            RoverUI.Outline(_card, RoverUI.Accent);
            var stripe = RoverUI.Rect("Stripe", _card);
            RoverUI.TopLeft(stripe, 0, 0, 4, 0);
            stripe.anchorMin = new Vector2(0, 0); stripe.anchorMax = new Vector2(0, 1);
            stripe.sizeDelta = new Vector2(4, 0);
            RoverUI.Img(stripe, RoverUI.Accent);

            _eyebrow = RoverUI.Label(_card, "", 12, FontStyle.Bold, RoverUI.Dim);
            RoverUI.TopLeft(_eyebrow.rectTransform, 28, 22, CardW - 56, 18);
            _title = RoverUI.Label(_card, "", 24, FontStyle.Bold, RoverUI.Accent);
            RoverUI.TopLeft(_title.rectTransform, 28, 44, CardW - 56, 32);
            _body = RoverUI.Label(_card, "", 15, FontStyle.Normal, RoverUI.White);
            _body.lineSpacing = 6f;

            _skipBtn = _buttons.Add(_card, "SKIP TUTORIAL", 28, 0, 170, 40, false, SkipAll, 12);
            _skipBtn.tone = RoverUI.Dim;
            _closeBtn = _buttons.Add(_card, "CLOSE  ✕", CardW - 28 - 150, 0, 150, 40, true, Close, 13);
            _hint = RoverUI.Label(_card, $"<color={RoverUI.Hex(RoverUI.Accent)}>[ENTER]</color> close", 11, FontStyle.Normal, RoverUI.Dim);

            _canvas.gameObject.SetActive(false);
        }

        static RectTransform Corner(string name, Transform parent)
        {
            var rt = RoverUI.Rect(name, parent);
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            return rt;
        }

        static Sprite _tri;
        static Sprite DownTriangle()
        {
            if (_tri) return _tri;
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float fy = (y + 0.5f) / n;                // 0 = tip (bottom) .. 1 = base
                float dx = Mathf.Abs((x + 0.5f) / n - 0.5f);
                float a = Mathf.Clamp01((fy * 0.5f - dx) * n * 0.7f) * Mathf.Clamp01((1f - fy) * n);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(255 * a));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return _tri = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0f));
        }

        // ================================================================ show / close

        void Show(Tip tip)
        {
            if (_current == null)
            {
                _savedTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
                Time.timeScale = 0f;
            }
            _current = tip;
            _shownT = 0f;
            _canvas.gameObject.SetActive(true);

            _eyebrow.text = tip.steps > 0
                ? $"<color={RoverUI.Hex(RoverUI.Accent)}>[TIP]</color> TUTORIAL   {tip.step} / {tip.steps}"
                : $"<color={RoverUI.Hex(RoverUI.Yellow)}>[TIP]</color> NEW MECHANIC";
            _title.text = tip.title;
            _body.text = tip.body;

            float bodyW = CardW - 56f;
            float bodyH = Mathf.Ceil(_body.GetPreferredValues(tip.body, bodyW, 0f).y);
            RoverUI.TopLeft(_body.rectTransform, 28, 88, bodyW, bodyH);
            float btnY = 88 + bodyH + 24;
            RoverUI.TopLeft(_skipBtn.rt, 28, btnY, 170, 40);
            RoverUI.TopLeft(_closeBtn.rt, CardW - 28 - 150, btnY, 150, 40);
            RoverUI.TopLeft(_hint.rectTransform, 28, btnY + 50, CardW - 56, 16);
            _hint.alignment = TextAlignmentOptions.TopRight;
            _closeBtn.label.text = _queue.Count > 0 && tip.steps > 0 && tip.step < tip.steps ? "NEXT  ›" : "CLOSE  ✕";
            _card.sizeDelta = new Vector2(CardW, btnY + 50 + 16 + 20);
            Layout();
        }

        void Close()
        {
            if (_current == null) return;
            _closedFrame = Time.frameCount;
            if (_queue.Count > 0) { Show(_queue.Dequeue()); return; }
            _current = null;
            _canvas.gameObject.SetActive(false);
            Time.timeScale = _savedTimeScale;
        }

        void SkipAll()
        {
            Enabled = false;
            _queue.Clear();
            Close();
        }

        void OnDestroy()
        {
            if (_current != null) Time.timeScale = _savedTimeScale;
            if (_instance == this) _instance = null;
        }

        // ================================================================ runtime

        void Update()
        {
            if (_current == null)
            {
                if (!Enabled) return;
                CheckTriggers();
                if (_queue.Count > 0 && !GameOverScreen.IsShown && !RoverInterior.IsTransitioning) Show(_queue.Dequeue());
                return;
            }

            if (Time.timeScale != 0f) Time.timeScale = 0f; // nothing else may unpause while a tip is up
            _shownT += Time.unscaledDeltaTime;
            _group.alpha = Mathf.Clamp01(_shownT / 0.2f);
            Layout();
            _buttons.Update();
            var kb = Keyboard.current;
            if (kb != null && _shownT > 0.15f && (kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame)) Close();
        }

        /// <summary>Positions the dim, the highlight frame, the card and the arrow around the current target.</summary>
        void Layout()
        {
            float scale = Mathf.Max(0.0001f, _canvas.scaleFactor);
            Vector2 size = _root.rect.size;
            Vector2 cs = _card.sizeDelta;

            Rect? screen = _current.target != null ? _current.target() : null;
            bool has = screen.HasValue;
            Rect t = default;
            if (has)
            {
                var s = screen.Value;
                t = Rect.MinMaxRect(s.xMin / scale - Hole, s.yMin / scale - Hole, s.xMax / scale + Hole, s.yMax / scale + Hole);
                t = Rect.MinMaxRect(Mathf.Max(0, t.xMin), Mathf.Max(0, t.yMin), Mathf.Min(size.x, t.xMax), Mathf.Min(size.y, t.yMax));
                has = t.width > 1f && t.height > 1f;
            }

            // Dim everything except the target.
            if (has)
            {
                Place(_dims[0], 0, t.yMax, size.x, size.y - t.yMax);          // above
                Place(_dims[1], 0, 0, size.x, t.yMin);                        // below
                Place(_dims[2], 0, t.yMin, t.xMin, t.height);                 // left
                Place(_dims[3], t.xMax, t.yMin, size.x - t.xMax, t.height);   // right
                for (int i = 1; i < 4; i++) _dims[i].gameObject.SetActive(true);
                Place(_frame, t.xMin, t.yMin, t.width, t.height);
                float pulse = 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 5f);
                RoverUI.SetOutline(_frameEdges, RoverUI.A(RoverUI.Accent, pulse));
            }
            else
            {
                Place(_dims[0], 0, 0, size.x, size.y);
                for (int i = 1; i < 4; i++) _dims[i].gameObject.SetActive(false);
            }
            _frame.gameObject.SetActive(has);

            // Card: beside the target where it fits (right, left, below, above), else centred.
            Vector2 pos = (size - cs) * 0.5f;
            bool arrow = false;
            if (has)
            {
                float cy = Mathf.Clamp(t.center.y - cs.y * 0.5f, Margin, size.y - Margin - cs.y);
                float cx = Mathf.Clamp(t.center.x - cs.x * 0.5f, Margin, size.x - Margin - cs.x);
                if (t.xMax + Gap + cs.x <= size.x - Margin) { pos = new Vector2(t.xMax + Gap, cy); arrow = true; }
                else if (t.xMin - Gap - cs.x >= Margin) { pos = new Vector2(t.xMin - Gap - cs.x, cy); arrow = true; }
                else if (t.yMin - Gap - cs.y >= Margin) { pos = new Vector2(cx, t.yMin - Gap - cs.y); arrow = true; }
                else if (t.yMax + Gap + cs.y <= size.y - Margin) { pos = new Vector2(cx, t.yMax + Gap); arrow = true; }
            }
            float pop = 1f - Mathf.Pow(1f - Mathf.Clamp01(_shownT / 0.25f), 3f);
            _card.anchoredPosition = pos;
            _card.localScale = Vector3.one * Mathf.Lerp(0.95f, 1f, pop);

            // Arrow from the card's edge to the target's edge, bobbing toward the target.
            if (arrow)
            {
                var card = new Rect(pos, cs);
                Vector2 from = Closest(card, t.center);
                Vector2 tip = Closest(t, from);
                Vector2 d = tip - from;
                float len = d.magnitude;
                arrow = len > 40f;
                if (arrow)
                {
                    d /= len;
                    float bob = (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f)) * 10f;
                    Vector2 end = tip - d * (6f + bob);
                    float shaft = Mathf.Max(0f, len - 6f - bob - 26f - 14f);
                    _arrow.anchoredPosition = end;
                    _arrow.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.x, -d.y) * Mathf.Rad2Deg);
                    _arrowShaft.sizeDelta = new Vector2(6f, shaft);
                }
            }
            _arrow.gameObject.SetActive(arrow);
        }

        static void Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(Mathf.Max(0, w), Mathf.Max(0, h));
        }

        static Vector2 Closest(Rect r, Vector2 p) => new Vector2(Mathf.Clamp(p.x, r.xMin, r.xMax), Mathf.Clamp(p.y, r.yMin, r.yMax));
    }
}
