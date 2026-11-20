using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Bobery.Rover
{
    /// <summary>
    /// Monochrome navigation panel docked on the left: live top-down map that follows the rover,
    /// telemetry, route progress and controls.
    ///   Map: left click = go there, right click = stop, mouse wheel = zoom.
    /// Everything (UI, sprites, map camera) is built from code at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    public class RoverMapPanel : MonoBehaviour
    {
        [Header("References")]
        public RoverController rover;
        [Tooltip("Terrain (used for the water level). Found automatically if empty.")]
        public Bobery.Terrain.TerrainGenerator terrain;
        [Tooltip("Water level when no TerrainGenerator is found.")]
        public float fallbackWaterLevel = float.NegativeInfinity;

        [Header("Map camera")]
        [Tooltip("Height of the map camera above the rover (metres).")]
        public float mapHeight = 50f;
        public float minMapHeight = 20f;
        public float maxMapHeight = 160f;
        [Range(10f, 120f)] public float mapFieldOfView = 60f;
        [Tooltip("Rotate the map so the rover always points up. Off = north (+Z) is always up.")]
        public bool rotateWithRover = false;

        [Header("Layout (reference resolution 1920x1080)")]
        public float panelWidth = 400f;
        public int mapTextureSize = 1024;
        [Tooltip("Shrink the main camera's view so the rover isn't hidden behind the panel.")]
        public bool shiftMainCameraViewport = true;

        [Header("World marker")]
        public Color markerColor = Color.white;
        [Tooltip("Marker size relative to the rover's footprint.")]
        [Range(0.05f, 1f)] public float markerSize = 0.3f;

        [Header("Ground")]
        public LayerMask groundMask = ~0;

        // ---- theme
        static readonly Color Black = new Color(0.03f, 0.03f, 0.035f, 0.97f);
        static readonly Color White = Color.white;
        static readonly Color Dim = new Color(1f, 1f, 1f, 0.45f);
        static readonly Color Faint = new Color(1f, 1f, 1f, 0.12f);
        static readonly Color Ghost = new Color(1f, 1f, 1f, 0.05f);

        const float Pad = 24f;

        // ---- built UI
        Canvas _canvas;
        RectTransform _panel, _map, _roverIcon, _destIcon, _hoverIcon, _pathRoot, _noGo;
        readonly List<RectTransform> _segs = new List<RectTransform>();
        readonly List<RawImage> _segImgs = new List<RawImage>();
        readonly List<RectTransform> _joints = new List<RectTransform>();
        Texture2D _dashTex;
        Sprite _jointSprite;
        Text _stateText, _hoverText, _toastText, _coordText, _scaleText, _progressText;
        Image _stateDot, _toastBg, _progressFill;
        RectTransform _scaleBar, _toast;
        Text _speedVal, _headingVal, _distVal, _etaVal;
        readonly List<Btn> _buttons = new List<Btn>();
        Font _font, _mono;

        // ---- map
        Camera _mapCam;
        RenderTexture _rt;
        DestinationMarker _marker;
        int _hiddenLayer;
        Vector3 _markerPoint = new Vector3(float.NaN, 0, 0);
        float _routeStart = 1f;
        float _waterY;
        readonly RaycastHit[] _hits = new RaycastHit[16];

        // ---- fuel
        FuelTank _tank;
        RectTransform _fuelTrack, _fuelFill;
        Image _fuelFillImg;
        Text _fuelValue, _fuelInfo, _fuelLabel;
        float _fuelFlash;

        // ---- state
        string _lastState = "";
        float _toastTimer;
        float _openT;

        class Btn
        {
            public RectTransform rt;
            public Image bg;
            public Text label;
            public Action onClick;
            public bool filled;   // filled = white button with black text
        }

        // ================================================================ lifecycle

        void Start()
        {
            if (!rover) rover = FindAnyObjectByType<RoverController>();
            if (!terrain) terrain = FindAnyObjectByType<Bobery.Terrain.TerrainGenerator>();
            _waterY = terrain
                ? terrain.transform.position.y + terrain.waterLevel * terrain.transform.lossyScale.y
                : fallbackWaterLevel;

            _font = Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Helvetica Neue", "Arial" }, 16)
                    ?? Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _mono = Font.CreateDynamicFontFromOSFont(new[] { "Consolas", "Menlo", "Courier New" }, 16) ?? _font;

            // Renderers on this layer are skipped by the map camera (rover model + 3D marker).
            _hiddenLayer = LayerMask.NameToLayer("TransparentFX");
            if (_hiddenLayer < 0) _hiddenLayer = 1;

            BuildMapCamera();
            BuildUI();
            BuildWorldMarker();
            HideFromMap(rover ? rover.gameObject : null);

            if (rover) rover.Arrived += OnArrived;
            if (rover) _tank = rover.GetComponent<FuelTank>();
            if (_tank) _tank.RanOut += OnRanOut;
            RoverHud.Notified += Toast;
        }

        void OnDestroy()
        {
            if (rover) rover.Arrived -= OnArrived;
            if (_tank) _tank.RanOut -= OnRanOut;
            RoverHud.Notified -= Toast;
            if (_rt) _rt.Release();
            if (_marker) Destroy(_marker.gameObject);
            var main = Camera.main;
            if (shiftMainCameraViewport && main) main.rect = new Rect(0, 0, 1, 1);
        }

        // ================================================================ map camera / marker

        void BuildMapCamera()
        {
            _rt = new RenderTexture(mapTextureSize, mapTextureSize, 24) { name = "RoverMapRT", antiAliasing = 4 };
            var go = new GameObject("MapCamera");
            go.transform.SetParent(transform, false);
            _mapCam = go.AddComponent<Camera>();
            _mapCam.orthographic = false;
            _mapCam.aspect = 1f;
            _mapCam.nearClipPlane = 0.5f;
            _mapCam.clearFlags = CameraClearFlags.SolidColor;
            _mapCam.backgroundColor = Color.black;
            _mapCam.targetTexture = _rt;
            _mapCam.depth = -10;
            _mapCam.cullingMask = ~(1 << _hiddenLayer);
            FollowRover();
        }

        void FollowRover()
        {
            if (!_mapCam) return;
            Vector3 focus = rover ? rover.transform.position : Vector3.zero;
            float yaw = rotateWithRover && rover ? rover.transform.eulerAngles.y : 0f;
            _mapCam.fieldOfView = mapFieldOfView;
            _mapCam.farClipPlane = mapHeight * 4f + 100f;
            _mapCam.transform.SetPositionAndRotation(focus + Vector3.up * mapHeight, Quaternion.Euler(90f, yaw, 0f));
        }

        void BuildWorldMarker()
        {
            var go = new GameObject("DestinationMarker");
            go.SetActive(false); // configure before Awake builds it
            _marker = go.AddComponent<DestinationMarker>();
            _marker.color = markerColor;
            _marker.groundMask = groundMask;
            if (rover)
            {
                _marker.ignore = rover.transform;
                var box = rover.GetComponent<BoxCollider>();
                if (box)
                {
                    Vector3 s = rover.transform.lossyScale;
                    _marker.radius = Mathf.Max(0.5f, Mathf.Max(box.size.x * s.x, box.size.z * s.z) * markerSize);
                }
            }
            go.SetActive(true);
            HideFromMap(go);
        }

        /// <summary>Moves every renderer under <paramref name="root"/> to the layer the map camera skips.
        /// Only renderer objects change layer, so colliders/physics are untouched.</summary>
        void HideFromMap(GameObject root)
        {
            if (!root) return;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (!r.GetComponent<Collider>()) r.gameObject.layer = _hiddenLayer;
        }

        // ================================================================ UI build

        void BuildUI()
        {
            var canvasGo = new GameObject("MapCanvas", typeof(Canvas), typeof(CanvasScaler));
            canvasGo.transform.SetParent(transform, false);
            _canvas = canvasGo.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1f;

            // Panel
            _panel = NewRect("Panel", canvasGo.transform);
            _panel.anchorMin = new Vector2(0, 0);
            _panel.anchorMax = new Vector2(0, 1);
            _panel.pivot = new Vector2(0, 0.5f);
            _panel.sizeDelta = new Vector2(panelWidth, 0);
            Img(_panel, Black);
            var edge = NewRect("Edge", _panel);
            edge.anchorMin = new Vector2(1, 0); edge.anchorMax = new Vector2(1, 1);
            edge.pivot = new Vector2(1, 0.5f); edge.sizeDelta = new Vector2(1, 0);
            Img(edge, Faint);

            float inner = panelWidth - Pad * 2f;
            float y = Pad;

            // ---- Header
            var eyebrow = Label(_panel, "ROVER  /  NAVIGATION", 12, FontStyle.Bold, Dim);
            TopLeft(eyebrow.rectTransform, Pad, y, inner, 18);

            var pill = NewRect("StatePill", _panel);
            TopRight(pill, Pad, y - 4, 118, 26);
            Img(pill, Ghost);
            Outline(pill, Faint, 1);
            _stateDot = Img(NewRect("Dot", pill), White);
            _stateDot.sprite = Circle(32, 0f);
            var dotRt = _stateDot.rectTransform;
            dotRt.anchorMin = dotRt.anchorMax = new Vector2(0, 0.5f);
            dotRt.anchoredPosition = new Vector2(14, 0); dotRt.sizeDelta = new Vector2(8, 8);
            _stateText = Label(pill, "IDLE", 12, FontStyle.Bold, White, _mono);
            var stRt = _stateText.rectTransform;
            stRt.anchorMin = new Vector2(0, 0); stRt.anchorMax = new Vector2(1, 1);
            stRt.offsetMin = new Vector2(26, 0); stRt.offsetMax = new Vector2(-8, 0);
            _stateText.alignment = TextAnchor.MiddleLeft;

            y += 24;
            var title = Label(_panel, "Navigation", 34, FontStyle.Bold, White);
            TopLeft(title.rectTransform, Pad, y, inner, 44);
            y += 48;
            var sub = Label(_panel, "Select a point on the map to dispatch the rover.", 14, FontStyle.Normal, Dim);
            TopLeft(sub.rectTransform, Pad, y, inner, 20);
            y += 34;

            // ---- Map
            BuildMap(Pad, y, inner);
            y += inner + 22;

            // ---- Telemetry
            Section("TELEMETRY", y, inner);
            y += 26;
            float gap = 10f, cw = (inner - gap) / 2f, ch = 74f;
            _speedVal = Card("SPEED", Pad, y, cw, ch);
            _headingVal = Card("HEADING", Pad + cw + gap, y, cw, ch);
            y += ch + gap;
            _distVal = Card("DISTANCE", Pad, y, cw, ch);
            _etaVal = Card("ETA", Pad + cw + gap, y, cw, ch);
            y += ch + 22;

            // ---- Route progress
            Section("ROUTE", y, inner);
            _progressText = Label(_panel, "—", 12, FontStyle.Bold, Dim, _mono);
            TopLeft(_progressText.rectTransform, Pad, y, inner, 16);
            _progressText.alignment = TextAnchor.UpperRight;
            y += 24;
            var track = NewRect("ProgressTrack", _panel);
            TopLeft(track, Pad, y, inner, 4);
            Img(track, Faint);
            _progressFill = Img(NewRect("Fill", track), White);
            var fRt = _progressFill.rectTransform;
            fRt.anchorMin = new Vector2(0, 0); fRt.anchorMax = new Vector2(0, 1);
            fRt.pivot = new Vector2(0, 0.5f); fRt.sizeDelta = Vector2.zero;
            y += 22;

            // ---- Actions
            float bw = (inner - gap) / 2f;
            AddButton("STOP", Pad, y, bw, 46, filled: true, () =>
            {
                if (rover && rover.HasDestination) { rover.Stop(); Toast("Rover stopped"); }
            });
            AddButton("ENTER ROVER  [E]", Pad + bw + gap, y, bw, 46, filled: false, RoverInterior.Enter);

            // ---- Footer (pinned to bottom)
            var footer = NewRect("Footer", _panel);
            footer.anchorMin = new Vector2(0, 0); footer.anchorMax = new Vector2(1, 0);
            footer.pivot = new Vector2(0.5f, 0);
            footer.offsetMin = new Vector2(Pad, Pad); footer.offsetMax = new Vector2(-Pad, Pad + 40);
            var line = NewRect("Rule", footer);
            line.anchorMin = new Vector2(0, 1); line.anchorMax = new Vector2(1, 1);
            line.pivot = new Vector2(0.5f, 1); line.sizeDelta = new Vector2(0, 1);
            Img(line, Faint);
            float kx = 0f;
            kx = KeyHint(footer, kx, "LMB", "Go");
            kx = KeyHint(footer, kx, "RMB", "Stop");
            kx = KeyHint(footer, kx, "WHEEL", "Zoom");
            KeyHint(footer, kx, "E", "Enter");

            BuildFuelBlock(inner);
        }

        /// <summary>Fuel gauge pinned to the bottom-left of the panel, just above the key hints.</summary>
        void BuildFuelBlock(float inner)
        {
            var block = NewRect("Fuel", _panel);
            block.anchorMin = new Vector2(0, 0); block.anchorMax = new Vector2(0, 0);
            block.pivot = new Vector2(0, 0);
            block.anchoredPosition = new Vector2(Pad, Pad + 56);
            block.sizeDelta = new Vector2(inner, 70);

            _fuelLabel = Label(block, "FUEL", 12, FontStyle.Bold, Dim);
            TopLeft(_fuelLabel.rectTransform, 0, 0, inner, 16);
            _fuelValue = Label(block, "", 13, FontStyle.Bold, White, _mono);
            TopLeft(_fuelValue.rectTransform, 0, -2, inner, 18);
            _fuelValue.alignment = TextAnchor.UpperRight;

            _fuelTrack = NewRect("Track", block);
            TopLeft(_fuelTrack, 0, 24, inner, 14);
            Img(_fuelTrack, Faint);
            Outline(_fuelTrack, new Color(1, 1, 1, 0.25f), 1);
            // quarter ticks
            for (int i = 1; i < 4; i++)
            {
                var t = NewRect("Tick", _fuelTrack);
                t.anchorMin = t.anchorMax = new Vector2(i / 4f, 0.5f);
                t.sizeDelta = new Vector2(1, 14);
                Img(t, new Color(0, 0, 0, 0.6f));
            }
            _fuelFill = NewRect("Fill", _fuelTrack);
            _fuelFill.anchorMin = new Vector2(0, 0); _fuelFill.anchorMax = new Vector2(0, 1);
            _fuelFill.pivot = new Vector2(0, 0.5f);
            _fuelFill.offsetMin = new Vector2(2, 2); _fuelFill.offsetMax = new Vector2(2, -2);
            _fuelFillImg = Img(_fuelFill, White);
            _fuelFill.SetAsFirstSibling(); // under the ticks and the outline

            _fuelInfo = Label(block, "", 12, FontStyle.Normal, Dim, _mono);
            TopLeft(_fuelInfo.rectTransform, 0, 46, inner, 18);
        }

        void UpdateFuel()
        {
            if (!_fuelTrack) return;
            if (!_tank)
            {
                _fuelValue.text = "—";
                _fuelInfo.text = "No fuel tank";
                return;
            }

            float w = _fuelTrack.rect.width - 4f;
            _fuelFill.sizeDelta = new Vector2(w * _tank.Fraction, _fuelFill.sizeDelta.y);
            _fuelValue.text = $"{_tank.Fuel:0.0} / {_tank.capacity:0} L";
            string range = float.IsInfinity(_tank.Range) ? "∞" : $"{_tank.Range:0} m";
            _fuelInfo.text = $"RANGE {range}   ·   CARRIED {FuelTank.Reserve:0.#} L";

            // Low fuel: blink. Empty: label says so. Flash after a refused order.
            _fuelFlash = Mathf.Max(0f, _fuelFlash - Time.deltaTime);
            float blink = 0.5f + 0.5f * Mathf.Sin(Time.time * 8f);
            bool warn = _tank.IsLow || _fuelFlash > 0f;
            _fuelFillImg.color = warn ? new Color(1, 1, 1, Mathf.Lerp(0.35f, 1f, blink)) : White;
            _fuelLabel.text = _tank.IsEmpty ? "FUEL  —  EMPTY" : _tank.IsLow ? "FUEL  —  LOW" : "FUEL";
            _fuelLabel.color = warn || _tank.IsEmpty ? White : Dim;
        }

        void OnRanOut()
        {
            _fuelFlash = 3f;
            if (FuelTank.Reserve > 0f) Toast("Out of fuel. Press E to refuel inside");
        }

        void BuildMap(float x, float y, float side)
        {
            // Frame + image
            var frame = NewRect("MapFrame", _panel);
            TopLeft(frame, x, y, side, side);
            Img(frame, Color.black);

            _map = NewRect("Map", frame);
            Stretch(_map, 1);
            _map.gameObject.AddComponent<RawImage>().texture = _rt;
            _map.gameObject.AddComponent<RectMask2D>(); // clip route lines that run off the map
            Outline(frame, Faint, 1);
            Brackets(frame, 16, 2, White);

            // Subtle grid + vignette
            var grid = NewRect("Grid", _map);
            Stretch(grid, 0);
            var gridImg = grid.gameObject.AddComponent<RawImage>();
            gridImg.texture = GridTexture(64);
            gridImg.uvRect = new Rect(0, 0, side / 48f, side / 48f);
            gridImg.color = new Color(1, 1, 1, 0.07f);
            gridImg.raycastTarget = false;

            var vig = NewRect("Vignette", _map);
            Stretch(vig, 0);
            var vigImg = vig.gameObject.AddComponent<RawImage>();
            vigImg.texture = VignetteTexture(128);
            vigImg.raycastTarget = false;

            // Centre crosshair (faint)
            for (int i = 0; i < 2; i++)
            {
                var c = NewRect("Cross", _map);
                c.sizeDelta = i == 0 ? new Vector2(14, 1) : new Vector2(1, 14);
                Img(c, new Color(1, 1, 1, 0.25f)).raycastTarget = false;
            }

            // Route (dashed, flowing polyline, built on demand), destination dot, hover ghost, rover arrow
            _pathRoot = NewRect("Route", _map);
            Stretch(_pathRoot, 0);
            _dashTex = DashTexture();
            _jointSprite = Circle(32, 0f);

            _destIcon = NewRect("Destination", _map);
            _destIcon.sizeDelta = new Vector2(12, 12);
            var dImg = Img(_destIcon, White);
            dImg.sprite = Circle(64, 0.18f); // white dot with dark outline
            dImg.raycastTarget = false;
            _destIcon.gameObject.SetActive(false);

            _hoverIcon = NewRect("Hover", _map);
            _hoverIcon.sizeDelta = new Vector2(18, 18);
            var hImg = Img(_hoverIcon, new Color(1, 1, 1, 0.6f));
            hImg.sprite = Ring(64, 0.78f);
            hImg.raycastTarget = false;
            _hoverText = Label(_hoverIcon, "", 12, FontStyle.Bold, White, _mono);
            var htRt = _hoverText.rectTransform;
            htRt.anchorMin = htRt.anchorMax = new Vector2(0.5f, 0.5f);
            htRt.pivot = new Vector2(0, 0.5f);
            htRt.anchoredPosition = new Vector2(14, 0);
            htRt.sizeDelta = new Vector2(80, 18);
            _hoverText.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.9f);
            _hoverIcon.gameObject.SetActive(false);

            // Area around the rover that's too close to drive to (= arrive distance).
            _noGo = NewRect("NoGoZone", _map);
            var ngFill = Img(_noGo, new Color(1, 1, 1, 0.07f));
            ngFill.sprite = Circle(128, 0f);
            var ngRing = NewRect("Ring", _noGo);
            Stretch(ngRing, 0);
            Img(ngRing, new Color(1, 1, 1, 0.55f)).sprite = Ring(256, 0.965f);

            _roverIcon = NewRect("RoverIcon", _map);
            _roverIcon.sizeDelta = new Vector2(24, 24);
            var rImg = Img(_roverIcon, White);
            rImg.sprite = Arrow(64);
            rImg.raycastTarget = false;

            // Overlays: north, zoom, scale bar, coordinates, toast
            var north = Label(frame, "N", 13, FontStyle.Bold, White, _mono);
            var nRt = north.rectTransform;
            nRt.anchorMin = nRt.anchorMax = new Vector2(0.5f, 1);
            nRt.pivot = new Vector2(0.5f, 1);
            nRt.anchoredPosition = new Vector2(0, -8);
            nRt.sizeDelta = new Vector2(30, 18);
            north.alignment = TextAnchor.UpperCenter;
            north.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.9f);
            north.gameObject.SetActive(!rotateWithRover);

            AddButton("+", 0, 0, 30, 30, filled: false, () => Zoom(-1f), frame, topRight: new Vector2(10, 10));
            AddButton("−", 0, 0, 30, 30, filled: false, () => Zoom(1f), frame, topRight: new Vector2(10, 44));

            _scaleBar = NewRect("ScaleBar", frame);
            _scaleBar.anchorMin = _scaleBar.anchorMax = new Vector2(0, 0);
            _scaleBar.pivot = new Vector2(0, 0);
            _scaleBar.anchoredPosition = new Vector2(12, 12);
            _scaleBar.sizeDelta = new Vector2(80, 6);
            // bar = bottom line + two end ticks
            var bl = NewRect("Line", _scaleBar); bl.anchorMin = new Vector2(0, 0); bl.anchorMax = new Vector2(1, 0);
            bl.pivot = new Vector2(0.5f, 0); bl.sizeDelta = new Vector2(0, 2); Img(bl, White);
            var t0 = NewRect("T0", _scaleBar); t0.anchorMin = new Vector2(0, 0); t0.anchorMax = new Vector2(0, 1);
            t0.pivot = new Vector2(0, 0.5f); t0.sizeDelta = new Vector2(2, 0); Img(t0, White);
            var t1 = NewRect("T1", _scaleBar); t1.anchorMin = new Vector2(1, 0); t1.anchorMax = new Vector2(1, 1);
            t1.pivot = new Vector2(1, 0.5f); t1.sizeDelta = new Vector2(2, 0); Img(t1, White);
            _scaleText = Label(_scaleBar, "", 11, FontStyle.Bold, White, _mono);
            var sRt = _scaleText.rectTransform;
            sRt.anchorMin = new Vector2(0, 1); sRt.anchorMax = new Vector2(0, 1);
            sRt.pivot = new Vector2(0, 0); sRt.anchoredPosition = new Vector2(0, 3); sRt.sizeDelta = new Vector2(80, 14);
            _scaleText.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.9f);

            _coordText = Label(frame, "", 11, FontStyle.Bold, White, _mono);
            var cRt = _coordText.rectTransform;
            cRt.anchorMin = cRt.anchorMax = new Vector2(1, 0);
            cRt.pivot = new Vector2(1, 0);
            cRt.anchoredPosition = new Vector2(-12, 10);
            cRt.sizeDelta = new Vector2(180, 14);
            _coordText.alignment = TextAnchor.LowerRight;
            _coordText.gameObject.AddComponent<Shadow>().effectColor = new Color(0, 0, 0, 0.9f);

            _toast = NewRect("Toast", frame);
            _toast.anchorMin = _toast.anchorMax = new Vector2(0.5f, 0);
            _toast.pivot = new Vector2(0.5f, 0);
            _toast.anchoredPosition = new Vector2(0, 36);
            _toast.sizeDelta = new Vector2(220, 28);
            _toastBg = Img(_toast, new Color(0, 0, 0, 0.85f));
            Outline(_toast, new Color(1, 1, 1, 0.25f), 1);
            _toastText = Label(_toast, "", 13, FontStyle.Bold, White);
            Stretch(_toastText.rectTransform, 0);
            _toastText.alignment = TextAnchor.MiddleCenter;
            _toast.gameObject.SetActive(false);
        }

        // ================================================================ runtime

        void Update()
        {
            HandleInput();

            if (_toastTimer > 0f)
            {
                _toastTimer -= Time.deltaTime;
                float a = Mathf.Clamp01(_toastTimer / 0.3f);
                _toastBg.color = new Color(0, 0, 0, 0.85f * a);
                _toastText.color = new Color(1, 1, 1, a);
                if (_toastTimer <= 0f) _toast.gameObject.SetActive(false);
            }
        }

        void LateUpdate()
        {
            FollowRover();
            UpdateMapOverlay();
            UpdateTelemetry();
            UpdateFuel();

            // Slide-in on start.
            _openT = Mathf.Min(1f, _openT + Time.unscaledDeltaTime / 0.45f);
            float e = 1f - Mathf.Pow(1f - _openT, 3f);
            _panel.anchoredPosition = new Vector2(Mathf.Lerp(-panelWidth, 0f, e), 0f);

            if (!shiftMainCameraViewport || !_canvas) return;
            var main = Camera.main;
            if (!main) return;
            float frac = Mathf.Clamp01(panelWidth * _canvas.scaleFactor * e / Mathf.Max(1, Screen.width));
            main.rect = new Rect(frac, 0f, 1f - frac, 1f);
        }

        void HandleInput()
        {
            if (GameOverScreen.IsShown) return;
            var kb = Keyboard.current;
            if (kb != null && kb.eKey.wasPressedThisFrame) { RoverInterior.Enter(); return; }

            var mouse = Mouse.current;
            if (mouse == null) return;
            Vector2 screen = mouse.position.ReadValue();
            bool click = mouse.leftButton.wasPressedThisFrame;

            // Buttons (hover + click)
            bool overButton = false;
            foreach (var b in _buttons)
            {
                bool hover = RectTransformUtility.RectangleContainsScreenPoint(b.rt, screen, null);
                overButton |= hover;
                bool light = b.filled != hover;      // hover inverts the colours
                b.bg.color = light ? White : new Color(0, 0, 0, 0.55f);
                b.label.color = light ? Color.black : White;
                if (hover && click) b.onClick?.Invoke();
            }

            bool overMap = _map && RectTransformUtility.RectangleContainsScreenPoint(_map, screen, null) && !overButton;

            // Hover ghost
            if (overMap && TryMapPoint(screen, out RaycastHit hoverHit, out Vector2 local))
            {
                _hoverIcon.gameObject.SetActive(true);
                _hoverIcon.anchoredPosition = local;
                bool water = hoverHit.point.y < _waterY - 0.05f;
                float d = rover ? Flat(rover.transform.position, hoverHit.point) : 0f;
                bool tooClose = rover && d <= rover.arriveDistance;
                _hoverText.text = water ? "WATER" : tooClose ? "TOO CLOSE" : $"{d:0} m";
            }
            else _hoverIcon.gameObject.SetActive(false);

            if (!overMap || !rover) return;

            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f) Zoom(-Mathf.Sign(scroll) * 0.5f);

            if (mouse.rightButton.wasPressedThisFrame)
            {
                if (rover.HasDestination) { rover.Stop(); Toast("Rover stopped"); }
                return;
            }
            if (!click) return;

            if (_tank && _tank.IsEmpty)
            {
                _fuelFlash = 2f;
                Toast(FuelTank.Reserve > 0f ? "No fuel. Press E to refuel inside" : "No fuel");
                return;
            }
            if (!TryMapPoint(screen, out RaycastHit hit, out _)) { Toast("No ground there"); return; }
            if (hit.point.y < _waterY - 0.05f) { Toast("Can't drive into water"); return; }
            if (Flat(rover.transform.position, hit.point) <= rover.arriveDistance) { Toast("Too close to the rover"); return; }

            if (!rover.SetDestination(hit.point)) { Toast("No route there"); return; }
            _routeStart = Mathf.Max(0.01f, rover.DistanceToDestination);
            if (_tank && rover.DistanceToDestination > _tank.Range)
            {
                _fuelFlash = 2.5f;
                Toast($"Not enough fuel: route {rover.DistanceToDestination:0} m, range {_tank.Range:0} m");
            }
            else if (rover.Path.Count > 1) Toast($"Route: {rover.DistanceToDestination:0} m around obstacles");
        }

        /// <summary>Screen point on the map → ground hit (skipping the rover) + local map position.</summary>
        bool TryMapPoint(Vector2 screen, out RaycastHit hit, out Vector2 local)
        {
            hit = default;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_map, screen, null, out local);
            Rect r = _map.rect;
            float u = (local.x - r.xMin) / r.width;
            float v = (local.y - r.yMin) / r.height;

            Ray ray = _mapCam.ViewportPointToRay(new Vector3(u, v, 0f));
            int count = Physics.RaycastNonAlloc(ray, _hits, _mapCam.farClipPlane, groundMask, QueryTriggerInteraction.Ignore);
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                if (rover && _hits[i].transform.IsChildOf(rover.transform)) continue;
                if (!found || _hits[i].distance < hit.distance) { hit = _hits[i]; found = true; }
            }
            return found;
        }

        void Zoom(float steps)
        {
            mapHeight = Mathf.Clamp(mapHeight * Mathf.Pow(1.2f, steps), minMapHeight, maxMapHeight);
        }

        void UpdateMapOverlay()
        {
            if (!rover || !_map) return;

            Vector2 rp = WorldToMap(rover.transform.position);
            _roverIcon.anchoredPosition = rp;

            // No-go circle: project the arrive distance onto the map (perspective-correct at ground level).
            Vector3 rpos = rover.transform.position;
            Vector2 edge = WorldToMapUnclamped(rpos + Vector3.right * rover.arriveDistance);
            float radiusPx = Mathf.Abs(edge.x - WorldToMapUnclamped(rpos).x);
            _noGo.anchoredPosition = rp;
            _noGo.sizeDelta = Vector2.one * (radiusPx * 2f);
            float mapYaw = _mapCam.transform.eulerAngles.y;
            _roverIcon.localRotation = Quaternion.Euler(0, 0, -(rover.transform.eulerAngles.y - mapYaw));

            bool has = rover.HasDestination;
            _destIcon.gameObject.SetActive(has);
            _pathRoot.gameObject.SetActive(has);
            if (!has)
            {
                if (_marker && _marker.Visible) _marker.Hide();
                _markerPoint.x = float.NaN;
            }
            else
            {
                Vector3 dest = rover.Destination;
                if (_marker && dest != _markerPoint)
                {
                    var fit = rover.GetComponent<RoverAutoFit>();
                    if (fit && fit.IsFitted)
                        _marker.radius = Mathf.Max(0.5f, Mathf.Max(fit.WorldSize.x, fit.WorldSize.z) * markerSize);
                    _marker.Show(dest);
                    _markerPoint = dest;
                }
                Vector2 dp = WorldToMap(dest);
                _destIcon.anchoredPosition = dp;
                DrawRoute(rp);
            }

            // Scale bar: pick a round length close to 80 px.
            float metresAcross = 2f * mapHeight * Mathf.Tan(mapFieldOfView * 0.5f * Mathf.Deg2Rad);
            float mPerPx = metresAcross / Mathf.Max(1f, _map.rect.width);
            float target = 80f * mPerPx;
            float[] nice = { 1, 2, 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000 };
            float best = nice[0];
            foreach (var n in nice) if (Mathf.Abs(n - target) < Mathf.Abs(best - target)) best = n;
            _scaleBar.sizeDelta = new Vector2(best / mPerPx, 6);
            _scaleText.text = $"{best:0} m";

            Vector3 p = rover.transform.position;
            _coordText.text = $"X {p.x:0}   Z {p.z:0}";
        }

        /// <summary>Dashed polyline: rover → each remaining waypoint → destination, with small joints at the corners.</summary>
        void DrawRoute(Vector2 roverPos)
        {
            var path = rover.Path;
            int segCount = 0, jointCount = 0;
            Vector2 a = roverPos;
            float travelled = 0f;
            for (int i = rover.WaypointIndex; i < path.Count; i++)
            {
                Vector2 b = WorldToMapUnclampedSafe(path[i]);
                Vector2 d = b - a;
                float len = d.magnitude;
                if (len > 0.5f)
                {
                    var seg = Seg(segCount++);
                    seg.anchoredPosition = a;
                    seg.sizeDelta = new Vector2(len, 2f);
                    seg.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg);
                    // continuous dash phase along the whole route, flowing toward the target
                    _segImgs[segCount - 1].uvRect = new Rect(travelled / 12f - Time.time * 1.5f, 0, len / 12f, 1);
                    travelled += len;
                }
                if (i < path.Count - 1)
                {
                    var j = Joint(jointCount++);
                    j.anchoredPosition = b;
                }
                a = b;
            }
            for (int i = segCount; i < _segs.Count; i++) _segs[i].gameObject.SetActive(false);
            for (int i = jointCount; i < _joints.Count; i++) _joints[i].gameObject.SetActive(false);
        }

        RectTransform Seg(int i)
        {
            while (_segs.Count <= i)
            {
                var rt = NewRect("Seg", _pathRoot);
                rt.pivot = new Vector2(0, 0.5f);
                var img = rt.gameObject.AddComponent<RawImage>();
                img.texture = _dashTex;
                img.color = new Color(1, 1, 1, 0.85f);
                img.raycastTarget = false;
                _segs.Add(rt);
                _segImgs.Add(img);
            }
            _segs[i].gameObject.SetActive(true);
            return _segs[i];
        }

        RectTransform Joint(int i)
        {
            while (_joints.Count <= i)
            {
                var rt = NewRect("Joint", _pathRoot);
                rt.sizeDelta = new Vector2(5, 5);
                var img = Img(rt, new Color(1, 1, 1, 0.85f));
                img.sprite = _jointSprite;
                _joints.Add(rt);
            }
            _joints[i].gameObject.SetActive(true);
            return _joints[i];
        }

        /// <summary>Like WorldToMap but doesn't clamp, except far outside (keeps UI sizes sane).</summary>
        Vector2 WorldToMapUnclampedSafe(Vector3 world)
        {
            Vector2 p = WorldToMapUnclamped(world);
            Rect r = _map.rect;
            float lim = Mathf.Max(r.width, r.height) * 4f;
            return new Vector2(Mathf.Clamp(p.x, -lim, lim), Mathf.Clamp(p.y, -lim, lim));
        }

        void UpdateTelemetry()
        {
            if (!rover) return;

            float speed = Mathf.Abs(rover.CurrentSpeed);
            _speedVal.text = $"{speed * 3.6f:0}<size=14> km/h</size>";

            float yaw = (rover.transform.eulerAngles.y + 360f) % 360f;
            _headingVal.text = $"{yaw:000}°<size=14> {Cardinal(yaw)}</size>";

            bool has = rover.HasDestination;
            float dist = rover.DistanceToDestination;
            _distVal.text = has ? $"{dist:0}<size=14> m</size>" : "—";

            if (has && speed > 0.3f)
            {
                int secs = Mathf.CeilToInt(dist / speed);
                _etaVal.text = $"{secs / 60}:{secs % 60:00}";
            }
            else _etaVal.text = "—";

            float prog = has ? Mathf.Clamp01(1f - dist / _routeStart) : 0f;
            _progressFill.rectTransform.sizeDelta = new Vector2((panelWidth - Pad * 2f) * prog, 0);
            _progressText.text = has ? $"{prog * 100f:0}%" : "—";

            string state = _tank && _tank.IsEmpty ? "NO FUEL" : has ? (speed > 0.3f ? "EN ROUTE" : "TURNING") : "IDLE";
            if (state != _lastState) { _stateText.text = state; _lastState = state; }
            float blink = has ? 0.35f + 0.65f * Mathf.Abs(Mathf.Sin(Time.time * 3f)) : 0.45f;
            _stateDot.color = new Color(1, 1, 1, blink);
        }

        void OnArrived() => Toast("Destination reached");

        void Toast(string msg)
        {
            if (!_toast) return;
            _toast.gameObject.SetActive(true);
            _toastText.text = msg;
            _toast.sizeDelta = new Vector2(Mathf.Max(160f, msg.Length * 8.5f + 40f), 28);
            _toastTimer = 2.2f;
        }

        Vector2 WorldToMap(Vector3 world)
        {
            Rect r = _map.rect;
            Vector3 vp = _mapCam.WorldToViewportPoint(world);
            float m = 6f / Mathf.Max(1f, r.width); // keep icons a few px inside the edge
            float u = Mathf.Clamp(vp.x, m, 1f - m);
            float v = Mathf.Clamp(vp.y, m, 1f - m);
            return new Vector2((u - 0.5f) * r.width, (v - 0.5f) * r.height);
        }

        Vector2 WorldToMapUnclamped(Vector3 world)
        {
            Rect r = _map.rect;
            Vector3 vp = _mapCam.WorldToViewportPoint(world);
            return new Vector2((vp.x - 0.5f) * r.width, (vp.y - 0.5f) * r.height);
        }

        static float Flat(Vector3 a, Vector3 b) { a.y = 0; b.y = 0; return Vector3.Distance(a, b); }

        static string Cardinal(float yaw)
        {
            string[] c = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
            return c[Mathf.RoundToInt(yaw / 45f) % 8];
        }

        // ================================================================ UI helpers

        void Section(string name, float y, float inner)
        {
            var t = Label(_panel, name, 12, FontStyle.Bold, Dim);
            TopLeft(t.rectTransform, Pad, y, inner, 16);
            var rule = NewRect("Rule", _panel);
            TopLeft(rule, Pad + name.Length * 9f + 10f, y + 8, inner - (name.Length * 9f + 10f) - 44f, 1);
            Img(rule, Faint);
        }

        Text Card(string name, float x, float y, float w, float h)
        {
            var card = NewRect(name, _panel);
            TopLeft(card, x, y, w, h);
            Img(card, Ghost);
            Outline(card, Faint, 1);
            var lbl = Label(card, name, 11, FontStyle.Bold, Dim);
            TopLeft(lbl.rectTransform, 14, 12, w - 28, 14);
            var val = Label(card, "—", 26, FontStyle.Bold, White, _mono);
            TopLeft(val.rectTransform, 14, 30, w - 28, 34);
            val.supportRichText = true;
            return val;
        }

        void AddButton(string text, float x, float y, float w, float h, bool filled, Action onClick,
                       Transform parent = null, Vector2? topRight = null)
        {
            var rt = NewRect("Btn_" + text, parent ? parent : _panel);
            if (topRight.HasValue) TopRight(rt, topRight.Value.x, topRight.Value.y, w, h);
            else TopLeft(rt, x, y, w, h);
            var bg = Img(rt, filled ? White : new Color(0, 0, 0, 0.55f));
            Outline(rt, new Color(1, 1, 1, 0.6f), 1);
            var label = Label(rt, text, text.Length <= 1 ? 18 : 13, FontStyle.Bold, filled ? Color.black : White);
            Stretch(label.rectTransform, 0);
            label.alignment = TextAnchor.MiddleCenter;
            _buttons.Add(new Btn { rt = rt, bg = bg, label = label, onClick = onClick, filled = filled });
        }

        float KeyHint(RectTransform parent, float x, string key, string action)
        {
            float kw = key.Length * 8f + 16f;
            var cap = NewRect("Key_" + key, parent);
            cap.anchorMin = cap.anchorMax = new Vector2(0, 0);
            cap.pivot = new Vector2(0, 0);
            cap.anchoredPosition = new Vector2(x, 4);
            cap.sizeDelta = new Vector2(kw, 22);
            Img(cap, Ghost);
            Outline(cap, new Color(1, 1, 1, 0.3f), 1);
            var k = Label(cap, key, 11, FontStyle.Bold, White, _mono);
            Stretch(k.rectTransform, 0);
            k.alignment = TextAnchor.MiddleCenter;

            float aw = action.Length * 7f + 12f;
            var a = Label(parent, action, 12, FontStyle.Normal, Dim);
            var art = a.rectTransform;
            art.anchorMin = art.anchorMax = new Vector2(0, 0);
            art.pivot = new Vector2(0, 0);
            art.anchoredPosition = new Vector2(x + kw + 8, 4);
            art.sizeDelta = new Vector2(aw, 22);
            a.alignment = TextAnchor.MiddleLeft;
            return x + kw + 8 + aw;
        }

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static Image Img(RectTransform rt, Color c)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        static void TopLeft(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        static void TopRight(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(1, 1);
            rt.anchoredPosition = new Vector2(-x, -y);
            rt.sizeDelta = new Vector2(w, h);
        }

        static void Stretch(RectTransform rt, float inset)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(inset, inset); rt.offsetMax = new Vector2(-inset, -inset);
        }

        /// <summary>1px-style border made of four thin images.</summary>
        static void Outline(RectTransform rt, Color c, float t)
        {
            void Edge(string n, Vector2 aMin, Vector2 aMax, Vector2 size)
            {
                var e = NewRect(n, rt);
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

        /// <summary>L-shaped corner brackets just outside the rect.</summary>
        static void Brackets(RectTransform rt, float len, float t, Color c)
        {
            for (int i = 0; i < 4; i++)
            {
                Vector2 a = new Vector2(i % 2, i / 2);              // corner anchor
                Vector2 dir = new Vector2(a.x == 0 ? 1 : -1, a.y == 0 ? 1 : -1);
                Vector2 off = -dir * 4f;                             // push outside
                var h = NewRect("BrH", rt);
                h.anchorMin = h.anchorMax = a;
                h.pivot = new Vector2(a.x, a.y);
                h.anchoredPosition = off; h.sizeDelta = new Vector2(len, t);
                Img(h, c);
                var v = NewRect("BrV", rt);
                v.anchorMin = v.anchorMax = a;
                v.pivot = new Vector2(a.x, a.y);
                v.anchoredPosition = off; v.sizeDelta = new Vector2(t, len);
                Img(v, c);
            }
        }

        Text Label(Transform parent, string text, int size, FontStyle style, Color color, Font font = null)
        {
            var rt = NewRect("Text", parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = font ? font : _font;
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

        // ================================================================ generated textures

        static Texture2D NewTex(int w, int h, TextureWrapMode wrap = TextureWrapMode.Clamp) =>
            new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = wrap };

        /// <summary>Upward arrow, white with a black outline.</summary>
        static Sprite Arrow(int n)
        {
            var tex = NewTex(n, n);
            var px = new Color32[n * n];
            Vector2 a = new(0.5f, 0.94f), b = new(0.14f, 0.08f), c = new(0.86f, 0.08f), notch = new(0.5f, 0.32f);
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                var p = new Vector2((x + 0.5f) / n, (y + 0.5f) / n);
                bool inner = InTri(p, a, b, notch) || InTri(p, a, notch, c);
                bool outline = false;
                if (!inner)
                    for (int k = 0; k < 8 && !outline; k++)
                    {
                        float ang = k * Mathf.PI / 4f;
                        var q = p + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * 0.06f;
                        outline = InTri(q, a, b, notch) || InTri(q, a, notch, c);
                    }
                px[y * n + x] = inner ? new Color32(255, 255, 255, 255)
                              : outline ? new Color32(0, 0, 0, 230) : new Color32(0, 0, 0, 0);
            }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        }

        /// <summary>Solid circle, optional dark outline (fraction of radius). Antialiased.</summary>
        static Sprite Circle(int n, float outline)
        {
            var tex = NewTex(n, n);
            var px = new Color32[n * n];
            float aa = 1.5f / (n * 0.5f);
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f)) / (n * 0.5f);
                float alpha = Mathf.Clamp01((1f - d) / aa);
                float white = Mathf.Clamp01((1f - outline - d) / aa);
                byte v = (byte)(255 * white);
                px[y * n + x] = new Color32(v, v, v, (byte)(255 * alpha));
            }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        }

        static Sprite Ring(int n, float inner)
        {
            var tex = NewTex(n, n);
            var px = new Color32[n * n];
            float aa = 1.5f / (n * 0.5f);
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(n * 0.5f, n * 0.5f)) / (n * 0.5f);
                float a = Mathf.Clamp01((1f - d) / aa) * Mathf.Clamp01((d - inner) / aa);
                px[y * n + x] = new Color32(255, 255, 255, (byte)(255 * a));
            }
            tex.SetPixels32(px); tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f));
        }

        static Texture2D GridTexture(int n)
        {
            var tex = NewTex(n, n, TextureWrapMode.Repeat);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
                px[y * n + x] = (x == 0 || y == 0) ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            tex.SetPixels32(px); tex.Apply();
            tex.filterMode = FilterMode.Point;
            return tex;
        }

        static Texture2D VignetteTexture(int n)
        {
            var tex = NewTex(n, n);
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                float d = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));
                float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.65f, 1f, d)) * 0.55f;
                px[y * n + x] = new Color32(0, 0, 0, (byte)(255 * a));
            }
            tex.SetPixels32(px); tex.Apply();
            return tex;
        }

        static Texture2D DashTexture()
        {
            var tex = NewTex(16, 1, TextureWrapMode.Repeat);
            var px = new Color32[16];
            for (int x = 0; x < 16; x++) px[x] = x < 9 ? new Color32(255, 255, 255, 255) : new Color32(255, 255, 255, 0);
            tex.SetPixels32(px); tex.Apply();
            tex.filterMode = FilterMode.Point;
            return tex;
        }

        static bool InTri(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float s1 = Cross(b - a, p - a), s2 = Cross(c - b, p - b), s3 = Cross(a - c, p - c);
            bool neg = s1 < 0 || s2 < 0 || s3 < 0, pos = s1 > 0 || s2 > 0 || s3 > 0;
            return !(neg && pos);
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;
    }
}
