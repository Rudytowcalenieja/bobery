using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Bobery.Rover
{
    /// <summary>
    /// Animated "OUTPOST NEUTRALIZED" banner shown when an outpost is completed. A banner sweeps in, then the
    /// rewarded weapon's card pops out of a burst of light (spinning rays, an expanding ring, a shine across
    /// the card, the name typed out), holds for a moment and flies off. Without a reward it says so instead.
    /// Doesn't block input; several completions in a row queue up.
    /// </summary>
    public class OutpostRewardBanner : MonoBehaviour
    {
        struct Entry { public string reward; public int done, total; }

        static OutpostRewardBanner _instance;
        readonly Queue<Entry> _queue = new Queue<Entry>();

        const float Duration = 5f;

        Canvas _canvas;
        RectTransform _root, _banner, _stripe, _card, _burst, _ring, _shine, _rays;
        CanvasGroup _group, _cardGroup;
        TextMeshProUGUI _title, _sub, _cardTop, _cardName, _cardHint;
        RectTransform _icon;
        readonly List<RectTransform> _rayBars = new List<RectTransform>();
        Entry _current;
        float _t = -1f;
        string _fullName = "";

        public static void Show(Bobery.Terrain.Outpost outpost, string reward, int done, int total)
        {
            if (!_instance)
            {
                var go = new GameObject("OutpostRewardBanner");
                _instance = go.AddComponent<OutpostRewardBanner>();
            }
            _instance._queue.Enqueue(new Entry { reward = reward, done = done, total = Mathf.Max(done, total) });
        }

        void OnDestroy() { if (_instance == this) _instance = null; }

        void Update()
        {
            if (_t < 0f)
            {
                if (_queue.Count == 0) return;
                Begin(_queue.Dequeue());
            }
            _t += Time.unscaledDeltaTime;
            Animate(_t);
            if (_t >= Duration)
            {
                _t = -1f;
                _canvas.enabled = false;
            }
        }

        void Begin(Entry e)
        {
            if (!_canvas) Build();
            _current = e;
            _t = 0f;
            _canvas.enabled = true;

            var def = WeaponDatabase.Instance.Get(e.reward);
            _title.text = "OUTPOST NEUTRALIZED";
            _sub.text = $"ALL DEFENCES DOWN  ·  {e.done} / {e.total} OUTPOSTS CLEARED";
            for (int i = _icon.childCount - 1; i >= 0; i--) Destroy(_icon.GetChild(i).gameObject);
            if (def != null)
            {
                _cardTop.text = "NEW WEAPON UNLOCKED";
                _cardTop.color = RoverUI.Yellow;
                _fullName = def.displayName;
                _cardHint.text = "Mount it at the cockpit inside the rover";
                if (def.kind == WeaponKind.RocketStrike) CockpitWeapons.RocketSilhouette(_icon, RoverUI.Yellow);
                else CockpitWeapons.Silhouette(_icon, RoverUI.Yellow, def.damage >= 10f);
            }
            else if (e.reward == RoverUpgrades.EngineId)
            {
                _cardTop.text = "UPGRADE INSTALLED";
                _cardTop.color = RoverUI.Yellow;
                _fullName = "BOOST ENGINE";
                _cardHint.text = "Press SHIFT while driving for a speed burst";
                EngineSilhouette(_icon, RoverUI.Yellow);
            }
            else
            {
                _cardTop.text = "SALVAGE";
                _cardTop.color = RoverUI.Dim;
                _fullName = "NOTHING USABLE";
                _cardHint.text = "Keep going, the next outpost may have more";
            }
            Animate(0f);
        }

        // ================================================================ animation

        void Animate(float t)
        {
            bool reward = !string.IsNullOrEmpty(_current.reward);
            Color tone = reward ? RoverUI.Yellow : RoverUI.Dim;

            // Whole thing fades out at the end.
            _group.alpha = Mathf.Clamp01(t / 0.15f) * (1f - Smooth((t - (Duration - 0.5f)) / 0.5f));
            _root.anchoredPosition = new Vector2(200f, 60f * Smooth((t - (Duration - 0.5f)) / 0.5f));

            // Banner: stripe shoots across, then the text slides in behind it.
            float b = Smooth(t / 0.35f);
            _banner.localScale = new Vector3(1f, Mathf.Lerp(0.05f, 1f, Smooth((t - 0.1f) / 0.25f)), 1f);
            _stripe.anchoredPosition = new Vector2(Mathf.Lerp(-900f, 900f, Mathf.Clamp01(t / 0.45f)), 0f);
            _title.rectTransform.anchoredPosition = new Vector2(Mathf.Lerp(-120f, 0f, Smooth((t - 0.15f) / 0.35f)), 14f);
            _title.alpha = Smooth((t - 0.15f) / 0.3f);
            _sub.alpha = Smooth((t - 0.35f) / 0.3f);
            _banner.GetComponent<Image>().color = RoverUI.A(RoverUI.Black, 0.92f * b);

            // Card pops out of a flash: overshoot scale, a little spin that settles.
            float c = (t - 0.6f) / 0.5f;
            float pop = c <= 0f ? 0f : Mathf.Max(0f, Back(Mathf.Clamp01(c)));
            _card.localScale = Vector3.one * pop;
            _card.localRotation = Quaternion.Euler(0f, 0f, (1f - Smooth(c)) * -12f);
            _cardGroup.alpha = Mathf.Clamp01(c * 3f);

            // Burst behind the card: rays spinning slowly, a ring that expands and fades.
            float burst = Mathf.Clamp01((t - 0.55f) / 0.4f);
            _burst.gameObject.SetActive(t > 0.55f);
            _rays.localRotation = Quaternion.Euler(0f, 0f, t * 25f);
            float rayLen = reward ? 260f : 140f;
            for (int i = 0; i < _rayBars.Count; i++)
            {
                float k = Smooth(burst) * (0.75f + 0.25f * Mathf.Sin(t * 3f + i * 1.7f));
                _rayBars[i].sizeDelta = new Vector2(reward ? 10f : 6f, rayLen * k);
                _rayBars[i].GetComponent<Image>().color = RoverUI.A(tone, (reward ? 0.22f : 0.1f) * (1f - Smooth((t - 3.8f) / 0.8f)));
            }
            float r = Mathf.Clamp01((t - 0.6f) / 0.9f);
            _ring.sizeDelta = Vector2.one * Mathf.Lerp(120f, 620f, Smooth(r));
            _ring.GetComponent<Image>().color = RoverUI.A(tone, 0.6f * (1f - r));

            // Shine sweeping across the card once it has landed.
            float s = Mathf.Clamp01((t - 1.15f) / 0.5f);
            _shine.anchoredPosition = new Vector2(Mathf.Lerp(-260f, 260f, s), 0f);
            _shine.gameObject.SetActive(s > 0f && s < 1f);

            // Name types itself out.
            int chars = Mathf.Clamp(Mathf.FloorToInt((t - 0.9f) / 0.05f), 0, _fullName.Length);
            _cardName.text = _fullName.Substring(0, chars) + (chars < _fullName.Length && t > 0.9f ? "_" : "");
            _cardName.color = reward ? RoverUI.White : RoverUI.Dim;
            _cardHint.alpha = Smooth((t - 1.6f) / 0.4f);
            _icon.localScale = Vector3.one * (1f + 0.06f * Mathf.Sin(t * 4f) * (reward ? 1f : 0f));
        }

        static void EngineSilhouette(RectTransform icon, Color c)
        {
            void Box(float x, float y, float w, float h)
            {
                var rt = RoverUI.Rect("Px", icon);
                rt.anchorMin = rt.anchorMax = new Vector2(0, 0);
                rt.pivot = Vector2.zero;
                rt.anchoredPosition = new Vector2(x, y);
                rt.sizeDelta = new Vector2(w, h);
                RoverUI.Img(rt, c);
            }
            Box(40, 12, 76, 32);        // block
            Box(52, 44, 10, 8);         // intake
            Box(72, 44, 10, 8);
            Box(92, 44, 10, 8);
            Box(116, 18, 14, 20);       // nozzle
            Box(130, 14, 8, 28);
            Box(14, 22, 10, 12);        // flame
            Box(26, 18, 12, 20);
            Box(46, 4, 64, 8);          // mount
        }

        static float Smooth(float x) { x = Mathf.Clamp01(x); return x * x * (3f - 2f * x); }

        /// <summary>Ease-out with overshoot.</summary>
        static float Back(float x)
        {
            const float c1 = 1.9f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(x - 1f, 3f) + c1 * Mathf.Pow(x - 1f, 2f);
        }

        // ================================================================ UI

        void Build()
        {
            _canvas = RoverUI.NewCanvas("OutpostReward", transform, 60);
            var canvasRt = (RectTransform)_canvas.transform;

            _root = RoverUI.Rect("Root", canvasRt);
            RoverUI.Center(_root, 900f, 600f, 200f, 0f);   // centred in the 3D view (right of the side panel)
            _group = _root.gameObject.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;

            // Banner across the top.
            _banner = RoverUI.Rect("Banner", _root);
            RoverUI.Center(_banner, 900f, 92f, 0f, 190f);
            RoverUI.Img(_banner, RoverUI.Black);
            _banner.gameObject.AddComponent<RectMask2D>();
            var top = RoverUI.Rect("Top", _banner);
            top.anchorMin = new Vector2(0, 1); top.anchorMax = new Vector2(1, 1); top.pivot = new Vector2(0.5f, 1f);
            top.sizeDelta = new Vector2(0, 3); RoverUI.Img(top, RoverUI.Yellow);
            var bottom = RoverUI.Rect("Bottom", _banner);
            bottom.anchorMin = new Vector2(0, 0); bottom.anchorMax = new Vector2(1, 0); bottom.pivot = new Vector2(0.5f, 0f);
            bottom.sizeDelta = new Vector2(0, 3); RoverUI.Img(bottom, RoverUI.Yellow);
            _stripe = RoverUI.Rect("Stripe", _banner);
            RoverUI.Center(_stripe, 180f, 140f);
            _stripe.localRotation = Quaternion.Euler(0, 0, -20f);
            RoverUI.Img(_stripe, RoverUI.A(RoverUI.Yellow, 0.35f));

            _title = RoverUI.Label(_banner, "", 36, FontStyle.Bold, RoverUI.Yellow);
            RoverUI.Center(_title.rectTransform, 860f, 46f, 0f, 14f);
            _title.alignment = TextAlignmentOptions.Center;
            _title.characterSpacing = 8f;
            _sub = RoverUI.Label(_banner, "", 13, FontStyle.Bold, RoverUI.White, mono: true);
            RoverUI.Center(_sub.rectTransform, 860f, 20f, 0f, -24f);
            _sub.alignment = TextAlignmentOptions.Center;

            // Burst + card.
            _burst = RoverUI.Rect("Burst", _root);
            RoverUI.Center(_burst, 10f, 10f, 0f, -40f);
            _rays = RoverUI.Rect("Rays", _burst);
            RoverUI.Center(_rays, 10f, 10f);
            for (int i = 0; i < 12; i++)
            {
                var ray = RoverUI.Rect("Ray", _rays);
                ray.anchorMin = ray.anchorMax = new Vector2(0.5f, 0.5f);
                ray.pivot = new Vector2(0.5f, 0f);
                ray.localRotation = Quaternion.Euler(0, 0, i * 30f);
                RoverUI.Img(ray, RoverUI.Yellow);
                _rayBars.Add(ray);
            }
            _ring = RoverUI.Rect("Ring", _burst);
            RoverUI.Center(_ring, 120f, 120f);
            var ringImg = RoverUI.Img(_ring, RoverUI.Yellow);
            ringImg.sprite = RingSprite();

            _card = RoverUI.Rect("Card", _root);
            RoverUI.Center(_card, 380f, 220f, 0f, -40f);
            RoverUI.Img(_card, RoverUI.Black);
            RoverUI.Outline(_card, RoverUI.Yellow, 2f);
            _card.gameObject.AddComponent<RectMask2D>();
            _cardGroup = _card.gameObject.AddComponent<CanvasGroup>();

            _cardTop = RoverUI.Label(_card, "", 12, FontStyle.Bold, RoverUI.Yellow, mono: true);
            RoverUI.TopLeft(_cardTop.rectTransform, 20f, 16f, 340f, 16f);
            _icon = RoverUI.Rect("Icon", _card);
            RoverUI.TopLeft(_icon, 110f, 44f, 160f, 56f);
            _cardName = RoverUI.Label(_card, "", 26, FontStyle.Bold, RoverUI.White);
            RoverUI.TopLeft(_cardName.rectTransform, 20f, 116f, 340f, 34f);
            _cardName.alignment = TextAlignmentOptions.Center;
            _cardHint = RoverUI.Label(_card, "", 12, FontStyle.Normal, RoverUI.Dim);
            RoverUI.TopLeft(_cardHint.rectTransform, 20f, 164f, 340f, 36f);
            _cardHint.alignment = TextAlignmentOptions.Center;
            _cardTop.alignment = TextAlignmentOptions.Center;

            _shine = RoverUI.Rect("Shine", _card);
            RoverUI.Center(_shine, 60f, 400f);
            _shine.localRotation = Quaternion.Euler(0, 0, -25f);
            RoverUI.Img(_shine, RoverUI.A(Color.white, 0.18f));

            _canvas.enabled = false;
        }

        static Sprite _ringSprite;
        static Sprite RingSprite()
        {
            if (_ringSprite) return _ringSprite;
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f)) / (n * 0.5f);
                    float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.93f) / 0.05f);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(255 * a));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return _ringSprite = Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        }
    }
}
