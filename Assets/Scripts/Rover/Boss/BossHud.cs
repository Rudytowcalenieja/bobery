using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Bobery.Rover
{
    /// <summary>
    /// Screen overlay for the <see cref="BossFight"/>: cinematic letterbox and title cards, the boss bar
    /// (one segment per cycle) with the current phase and what to do, the Shift-dodge meter, short flash
    /// messages and a bracket marker on the weak point telling the player to use the railgun.
    /// </summary>
    public class BossHud : MonoBehaviour
    {
        BossFight _fight;
        Canvas _canvas;
        RectTransform _root;

        RectTransform _top, _bottom;
        float _letter, _letterTarget;

        CanvasGroup _titleGroup;
        TextMeshProUGUI _titleEyebrow, _titleText;
        float _titleLeft, _titleLength = 1f;

        CanvasGroup _barGroup;
        TextMeshProUGUI _name, _cycle, _phase, _objective, _hint;
        readonly List<Image> _segments = new List<Image>();

        CanvasGroup _dodgeGroup;
        TextMeshProUGUI _dodgeLabel;
        RectTransform _dodgeTrack, _dodgeFill;
        Image _dodgeFillImg;

        TextMeshProUGUI _flash;
        float _flashLeft;
        const float FlashTime = 1.8f;

        RectTransform _mark;
        readonly List<Image> _markParts = new List<Image>();
        TextMeshProUGUI _markLabel;

        // Centred on the game view (the camera's viewport, right of the navigation panel), not the whole screen.
        readonly List<(RectTransform rt, Vector2 pos)> _centred = new List<(RectTransform, Vector2)>();

        public static BossHud Create(BossFight fight)
        {
            var go = new GameObject("BossHud");
            var hud = go.AddComponent<BossHud>();
            hud._fight = fight;
            hud.Build();
            return hud;
        }

        public void Letterbox(bool on) => _letterTarget = on ? 1f : 0f;

        public void Title(string eyebrow, string title, Color color, float seconds)
        {
            _titleEyebrow.text = eyebrow;
            _titleEyebrow.color = RoverUI.A(color, 0.9f);
            _titleText.text = title;
            _titleText.color = color;
            _titleLeft = _titleLength = Mathf.Max(0.3f, seconds);
        }

        public void Flash(string text, Color color)
        {
            _flash.text = text;
            _flash.color = color;
            _flashLeft = FlashTime;
        }

        // ================================================================ build

        void Build()
        {
            _canvas = RoverUI.NewCanvas("BossHudCanvas", transform, 40);
            _root = (RectTransform)_canvas.transform;

            // Letterbox bars.
            _top = Bar("Top", new Vector2(0f, 1f));
            _bottom = Bar("Bottom", new Vector2(0f, 0f));

            // Title card.
            var title = RoverUI.Rect("Title", _root);
            RoverUI.Center(title, 1400, 200, 0, 60);
            _titleGroup = title.gameObject.AddComponent<CanvasGroup>();
            _titleGroup.alpha = 0f;
            _titleEyebrow = RoverUI.Label(title, "", 18, FontStyle.Bold, RoverUI.Red, mono: true);
            RoverUI.TopLeft(_titleEyebrow.rectTransform, 0, 30, 1400, 30);
            _titleEyebrow.alignment = TextAlignmentOptions.Top;
            _titleText = RoverUI.Label(title, "", 72, FontStyle.Bold, RoverUI.Red);
            RoverUI.TopLeft(_titleText.rectTransform, 0, 64, 1400, 100);
            _titleText.alignment = TextAlignmentOptions.Top;
            RoverUI.Shadowed(_titleText);

            // Boss bar (top centre).
            var bar = RoverUI.Rect("BossBar", _root);
            bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0.5f, 1f);
            bar.anchoredPosition = new Vector2(0f, -22f);
            bar.sizeDelta = new Vector2(780f, 150f);
            _barGroup = bar.gameObject.AddComponent<CanvasGroup>();
            _barGroup.alpha = 0f;
            var bg = RoverUI.Rect("Bg", bar);
            RoverUI.TopLeft(bg, -16, -10, 812, 122);
            RoverUI.Img(bg, RoverUI.A(RoverUI.Black, 0.72f));
            RoverUI.Outline(bg, RoverUI.A(RoverUI.Red, 0.6f));
            _name = RoverUI.Label(bar, _fight.DisplayName, 22, FontStyle.Bold, RoverUI.Red);
            RoverUI.TopLeft(_name.rectTransform, 0, 0, 520, 30);
            RoverUI.Shadowed(_name);
            _cycle = RoverUI.Label(bar, "", 13, FontStyle.Bold, RoverUI.Dim, mono: true);
            RoverUI.TopLeft(_cycle.rectTransform, 520, 6, 260, 20);
            _cycle.alignment = TextAlignmentOptions.TopRight;
            int n = Mathf.Max(1, _fight.Cycles);
            const float gap = 8f, w = 780f;
            float segW = (w - gap * (n - 1)) / n;
            for (int i = 0; i < n; i++)
            {
                var seg = RoverUI.Rect("Segment", bar);
                RoverUI.TopLeft(seg, i * (segW + gap), 36, segW, 14);
                _segments.Add(RoverUI.Img(seg, RoverUI.Red));
            }
            _phase = RoverUI.Label(bar, "", 15, FontStyle.Bold, RoverUI.Yellow, mono: true);
            RoverUI.TopLeft(_phase.rectTransform, 0, 60, 780, 22);
            _objective = RoverUI.Label(bar, "", 17, FontStyle.Normal, RoverUI.White);
            RoverUI.TopLeft(_objective.rectTransform, 0, 84, 780, 26);
            RoverUI.Shadowed(_objective);

            _hint = RoverUI.Label(_root, "", 24, FontStyle.Bold, RoverUI.Yellow);
            var hr = _hint.rectTransform;
            hr.anchorMin = hr.anchorMax = hr.pivot = new Vector2(0.5f, 1f);
            hr.anchoredPosition = new Vector2(0f, -168f);
            hr.sizeDelta = new Vector2(1200f, 34f);
            _hint.alignment = TextAlignmentOptions.Top;
            RoverUI.Shadowed(_hint);

            // Dodge meter (bottom centre).
            var dodge = RoverUI.Rect("Dodge", _root);
            dodge.anchorMin = dodge.anchorMax = dodge.pivot = new Vector2(0.5f, 0f);
            dodge.anchoredPosition = new Vector2(0f, 150f);
            dodge.sizeDelta = new Vector2(360f, 44f);
            _dodgeGroup = dodge.gameObject.AddComponent<CanvasGroup>();
            _dodgeGroup.alpha = 0f;
            _dodgeLabel = RoverUI.Label(dodge, "", 15, FontStyle.Bold, RoverUI.Accent, mono: true);
            RoverUI.TopLeft(_dodgeLabel.rectTransform, 0, 0, 360, 22);
            _dodgeLabel.alignment = TextAlignmentOptions.Top;
            RoverUI.Shadowed(_dodgeLabel);
            _dodgeTrack = RoverUI.Bar(dodge, 60, 28, 240, 6, out _dodgeFill);
            _dodgeFillImg = _dodgeFill.GetComponent<Image>();

            // Flash message.
            _flash = RoverUI.Label(_root, "", 34, FontStyle.Bold, RoverUI.White);
            RoverUI.Center(_flash.rectTransform, 1400, 50, 0, 230);
            _flash.alignment = TextAlignmentOptions.Center;
            RoverUI.Shadowed(_flash);
            _flash.alpha = 0f;

            // Weak point marker: four brackets + label.
            _mark = RoverUI.Rect("WeakPointMarker", _root);
            _mark.anchorMin = _mark.anchorMax = Vector2.zero;
            _mark.pivot = new Vector2(0.5f, 0.5f);
            foreach (float sx in new[] { -1f, 1f })
                foreach (float sy in new[] { -1f, 1f })
                {
                    _markParts.Add(Corner(sx, sy, true));
                    _markParts.Add(Corner(sx, sy, false));
                }
            _markLabel = RoverUI.Label(_mark, "RAILGUN  >  SHOOT HERE", 16, FontStyle.Bold, RoverUI.Accent, mono: true);
            var ml = _markLabel.rectTransform;
            ml.anchorMin = ml.anchorMax = new Vector2(0.5f, 1f);
            ml.pivot = new Vector2(0.5f, 0f);
            ml.anchoredPosition = new Vector2(0f, 10f);
            ml.sizeDelta = new Vector2(420f, 24f);
            _markLabel.alignment = TextAlignmentOptions.Bottom;
            RoverUI.Shadowed(_markLabel);
            _mark.gameObject.SetActive(false);

            foreach (var rt in new[] { title, bar, _hint.rectTransform, dodge, _flash.rectTransform })
                _centred.Add((rt, rt.anchoredPosition));
        }

        /// <summary>Keeps the texts centred on the game view and the letterbox bars off the navigation panel.</summary>
        void FitToGameView()
        {
            var cam = _fight.FightCamera ? _fight.FightCamera : Camera.main;
            float k = Mathf.Max(0.0001f, _canvas.scaleFactor);
            Rect view = cam ? cam.pixelRect : new Rect(0f, 0f, Screen.width, Screen.height);
            float left = view.xMin / k, right = (Screen.width - view.xMax) / k;
            float shift = (left - right) * 0.5f;
            foreach (var (rt, pos) in _centred) rt.anchoredPosition = new Vector2(pos.x + shift, pos.y);
            foreach (var bar in new[] { _top, _bottom })
            {
                bar.anchoredPosition = new Vector2(shift, 0f);
                bar.sizeDelta = new Vector2(-(left + right), bar.sizeDelta.y);
            }
        }

        RectTransform Bar(string name, Vector2 anchorY)
        {
            var rt = RoverUI.Rect(name, _root);
            rt.anchorMin = new Vector2(0f, anchorY.y);
            rt.anchorMax = new Vector2(1f, anchorY.y);
            rt.pivot = new Vector2(0.5f, anchorY.y);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, 0f);
            RoverUI.Img(rt, Color.black);
            return rt;
        }

        Image Corner(float sx, float sy, bool horizontal)
        {
            var rt = RoverUI.Rect("Corner", _mark);
            rt.anchorMin = rt.anchorMax = new Vector2(sx > 0 ? 1f : 0f, sy > 0 ? 1f : 0f);
            rt.pivot = new Vector2(sx > 0 ? 1f : 0f, sy > 0 ? 1f : 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = horizontal ? new Vector2(22f, 4f) : new Vector2(4f, 22f);
            return RoverUI.Img(rt, RoverUI.Accent);
        }

        // ================================================================ update

        void Update()
        {
            if (!_fight) { Destroy(gameObject); return; }
            bool show = !RoverInterior.IsInside && !GameOverScreen.IsShown;
            _canvas.enabled = show;
            if (!show) return;
            float dt = Time.unscaledDeltaTime;
            var phase = _fight.CurrentPhase;

            // Letterbox
            _letter = Mathf.MoveTowards(_letter, _letterTarget, dt / 0.5f);
            float h = 120f * Mathf.SmoothStep(0f, 1f, _letter);
            _top.sizeDelta = new Vector2(_top.sizeDelta.x, h);
            _bottom.sizeDelta = new Vector2(_bottom.sizeDelta.x, h);
            FitToGameView();

            // Title card: fade in, hold, fade out.
            if (_titleLeft > 0f) _titleLeft = Mathf.Max(0f, _titleLeft - dt);
            float age = _titleLength - _titleLeft;
            _titleGroup.alpha = _titleLeft <= 0f ? 0f : Mathf.Min(Mathf.Clamp01(age / 0.25f), Mathf.Clamp01(_titleLeft / 0.4f));
            _titleText.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.12f, 1f, Mathf.Clamp01(age / 0.35f));

            // Boss bar
            bool fighting = phase >= BossFight.Phase.Escorts && phase <= BossFight.Phase.Recover;
            _barGroup.alpha = Mathf.MoveTowards(_barGroup.alpha, fighting ? 1f : 0f, dt / 0.4f);
            int done = _fight.CyclesDone, total = _fight.Cycles;
            for (int i = 0; i < _segments.Count; i++)
            {
                bool alive = i < total - done;
                bool hot = alive && i == total - done - 1 && phase == BossFight.Phase.Exposed;
                _segments[i].color = !alive ? RoverUI.Faint
                    : hot ? Color.Lerp(RoverUI.Red, RoverUI.White, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 8f))
                    : RoverUI.Red;
            }
            _cycle.text = $"CYCLE {Mathf.Min(done + 1, total)}/{total}";
            switch (phase)
            {
                case BossFight.Phase.Escorts:
                    _phase.text = "PHASE 1 // ESCORTS";
                    int escortsLeft = _fight.EscortsAlive, escorts = _fight.EscortsTotal;
                    _objective.text = $"Destroy the escort walkers  ({escorts - escortsLeft}/{escorts})";
                    break;
                case BossFight.Phase.Barrage:
                    _phase.text = "PHASE 2 // BARRAGE";
                    _objective.text = $"Dodge the cannon balls with [SHIFT]  -  volley {_fight.Volley}/{BossConfig.Instance.volleys}";
                    break;
                case BossFight.Phase.Exposed:
                    _phase.text = "PHASE 3 // EXPOSED";
                    _objective.text = "It's stunned! Hit the glowing weak point with the RAILGUN";
                    break;
                case BossFight.Phase.Recover:
                    _phase.text = "RECOVERING";
                    _objective.text = "Get ready...";
                    break;
            }

            // Railgun hint (phase 3)
            string hint = "";
            if (phase == BossFight.Phase.Exposed && _fight.WeakPoint)
            {
                bool rail = WeaponLoadout.Mounted == WeaponDef.RailGunId;
                hint = rail ? "RAILGUN READY - AIM AT THE MARKER, CLICK [LMB]"
                            : "RAILGUN NEEDED - ENTER THE ROVER AND MOUNT IT AT THE COCKPIT";
                _hint.color = rail ? RoverUI.Accent : RoverUI.Yellow;
                _hint.alpha = rail ? 1f : 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 4f));
            }
            _hint.text = hint;

            // Dodge meter
            bool dodgeShown = _fight.CanDodge;
            _dodgeGroup.alpha = Mathf.MoveTowards(_dodgeGroup.alpha, dodgeShown ? 1f : 0f, dt / 0.3f);
            if (dodgeShown)
            {
                bool ready = _fight.DodgeReady;
                bool danger = _fight.IncomingLocked > 0;
                _dodgeLabel.text = danger && ready ? "INCOMING!  [SHIFT] DODGE" : ready ? "[SHIFT] DODGE  READY" : "[SHIFT] DODGE  CHARGING";
                Color c = danger ? Color.Lerp(RoverUI.Red, RoverUI.White, 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 14f))
                        : ready ? RoverUI.Accent : RoverUI.Dim;
                _dodgeLabel.color = c;
                _dodgeFillImg.color = ready ? RoverUI.Accent : RoverUI.Dim;
                RoverUI.SetBar(_dodgeTrack, _dodgeFill, _fight.DodgeCharge);
            }

            // Flash
            if (_flashLeft > 0f) _flashLeft = Mathf.Max(0f, _flashLeft - dt);
            float fa = FlashTime - _flashLeft;
            _flash.alpha = _flashLeft <= 0f ? 0f : Mathf.Min(Mathf.Clamp01(fa / 0.12f), Mathf.Clamp01(_flashLeft / 0.5f));
            _flash.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.25f, 1f, Mathf.Clamp01(fa / 0.2f));

            UpdateMarker();
        }

        void UpdateMarker()
        {
            var weak = _fight.WeakPoint;
            var cam = _fight.FightCamera;
            bool on = weak && !weak.IsDead && cam && _fight.CurrentPhase == BossFight.Phase.Exposed;
            Vector3 s = on ? cam.WorldToScreenPoint(weak.Center) : Vector3.zero;
            on &= s.z > 0f;
            _mark.gameObject.SetActive(on);
            if (!on) return;
            float k = Mathf.Max(0.0001f, _canvas.scaleFactor);
            Vector3 edge = cam.WorldToScreenPoint(weak.Center + cam.transform.right * weak.radius);
            float r = Mathf.Max(34f, Vector2.Distance(s, edge) / k * 1.5f);
            float pulse = 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 6f);
            _mark.anchoredPosition = (Vector2)s / k;
            _mark.sizeDelta = Vector2.one * (r * 2f * pulse);
            bool rail = WeaponLoadout.Mounted == WeaponDef.RailGunId;
            Color c = rail ? RoverUI.Accent : RoverUI.Yellow;
            foreach (var p in _markParts) p.color = c;
            _markLabel.color = c;
            _markLabel.text = rail ? "RAILGUN  >  SHOOT HERE" : "RAILGUN ONLY";
        }
    }
}
