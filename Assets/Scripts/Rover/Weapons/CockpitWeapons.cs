using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Bobery.Rover
{
    /// <summary>
    /// Weapon swapping in the rover cabin. Walk the crew member to the cockpit (click the floor between the
    /// seats, or the dashboard) and a "CHANGE WEAPONS" popup appears. The menu has the turret mount and the
    /// storage of unlocked weapons: drag a weapon from storage onto the mount to mount it, or from the mount
    /// into storage to take it off. Weapons are heavy, so a dragged card trails the cursor slowly
    /// (about <see cref="dragSeconds"/> to cross from one side to the other).
    /// Added by <see cref="InteriorRoom"/>.
    /// </summary>
    public class CockpitWeapons : MonoBehaviour
    {
        [Tooltip("Where the crew stands at the cockpit (cabin space, between the seats).")]
        public Vector3 standLocal = new Vector3(0f, 0f, 1.75f);
        [Tooltip("Crew within this distance (metres) of the stand point is 'at the cockpit'.")]
        public float zoneRadius = 1.4f;
        [Tooltip("Seconds a dragged weapon needs to travel from storage to the mount.")]
        public float dragSeconds = 1f;

        public bool IsOpen { get; private set; }
        /// <summary>Mouse is over this component's UI (popup or menu): the cabin shouldn't react to clicks.</summary>
        public bool PointerOverUi { get; private set; }
        public bool CrewAtCockpit { get; private set; }

        InteriorRepairs _repairs;
        Camera _cam;
        Transform _cabin, _dashboard;
        Canvas _canvas;
        RectTransform _root;

        // popup / marker
        RectTransform _popup, _marker;
        readonly UIButtons _popupButtons = new UIButtons();

        // menu
        CanvasGroup _menuGroup;
        RectTransform _menu, _mountZone, _storageZone, _storageGrid, _ghostLayer;
        Image[] _mountEdges, _storageEdges;
        TextMeshProUGUI _detailTitle, _detailText, _status;
        readonly UIButtons _menuButtons = new UIButtons();
        float _menuT, _statusTimer;
        readonly List<(RectTransform rt, string id, bool mounted)> _cards = new List<(RectTransform, string, bool)>();

        // drag
        string _dragId;
        bool _dragFromMount, _returning;
        RectTransform _ghost, _dragSource;
        Vector3 _returnTo;
        float _dragSpeed;

        static readonly RaycastHit[] _hits = new RaycastHit[8];
        const float CardW = 190f, CardH = 150f;

        public void Init(InteriorRepairs repairs, Camera cam, Transform cabin)
        {
            _repairs = repairs;
            _cam = cam;
            _cabin = cabin ? cabin : transform;
            foreach (var t in _cabin.GetComponentsInChildren<Transform>(true))
                if (t.name == "Dashboard") { _dashboard = t; break; }
            Build();
            WeaponLoadout.Changed += RebuildCards;
        }

        void OnDestroy()
        {
            WeaponLoadout.Changed -= RebuildCards;
            if (_canvas) Destroy(_canvas.gameObject);
        }

        Vector3 StandPoint => _cabin.TransformPoint(standLocal);

        // ================================================================ open / close

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            RebuildCards();
            SetStatus("");
        }

        public void Close()
        {
            if (!IsOpen) return;
            CancelDrag();
            IsOpen = false;
        }

        // ================================================================ update

        void Update()
        {
            var crew = _repairs ? _repairs.Crew : null;
            var mouse = Mouse.current;
            var kb = Keyboard.current;
            Vector2 sp = mouse != null ? mouse.position.ReadValue() : Vector2.zero;

            Vector3 stand = StandPoint;
            CrewAtCockpit = crew && crew.State != RepairCrew.Mode.Repairing && Flat(crew.transform.position, stand) <= zoneRadius;
            if (!CrewAtCockpit && IsOpen) Close();

            // Popup above the crew at the cockpit, a small marker over the cockpit otherwise.
            bool popup = CrewAtCockpit && !IsOpen && !GameOverScreen.IsShown;
            _popup.gameObject.SetActive(popup && Place(_popup, crew.transform.position + Vector3.up * 2.15f));
            _marker.gameObject.SetActive(!CrewAtCockpit && !IsOpen && Place(_marker, stand + Vector3.up * 1.2f));

            PointerOverUi = false;
            if (popup)
            {
                PointerOverUi = _popupButtons.Update();
                if (kb != null && kb.eKey.wasPressedThisFrame) Open();
            }
            else if (IsOpen && kb != null && kb.eKey.wasPressedThisFrame && _dragId == null) Close();

            // Menu
            _menuT = Mathf.MoveTowards(_menuT, IsOpen ? 1f : 0f, Time.unscaledDeltaTime / 0.18f);
            _menuGroup.alpha = _menuT;
            _menu.localScale = Vector3.one * Mathf.Lerp(0.96f, 1f, _menuT);
            _menu.gameObject.SetActive(_menuT > 0.001f);
            if (IsOpen)
            {
                PointerOverUi = true; // the whole view is the menu while it's open
                _menuButtons.Update();
                UpdateDrag(mouse, sp);
                UpdateDetails(sp);
            }
            else if (_ghost && !_returning) CancelDrag();
            if (_returning) UpdateReturn();

            if (_statusTimer > 0f) { _statusTimer -= Time.unscaledDeltaTime; if (_statusTimer <= 0f) _status.text = ""; }
        }

        /// <summary>Clicking the dashboard walks the crew to the cockpit (after the cabin's own click handling).</summary>
        void LateUpdate()
        {
            var crew = _repairs ? _repairs.Crew : null;
            var mouse = Mouse.current;
            if (!crew || !_cam || !_dashboard || mouse == null || IsOpen || PointerOverUi || GameOverScreen.IsShown) return;
            if (!mouse.leftButton.wasPressedThisFrame) return;
            Vector2 sp = mouse.position.ReadValue();
            if (!_cam.pixelRect.Contains(sp) || sp.x < 400f * (_canvas ? _canvas.scaleFactor : 1f)) return;
            int n = Physics.RaycastNonAlloc(_cam.ScreenPointToRay(sp), _hits, 50f, ~0, QueryTriggerInteraction.Collide);
            float best = float.MaxValue;
            Transform hit = null;
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].transform.IsChildOf(crew.transform)) continue;
                if (_hits[i].distance < best) { best = _hits[i].distance; hit = _hits[i].transform; }
            }
            if (hit && hit.IsChildOf(_dashboard))
            {
                Vector3 p = StandPoint;
                p.y = crew.transform.position.y;
                crew.WalkTo(p);
            }
        }

        bool Place(RectTransform rt, Vector3 world)
        {
            if (!_cam) return false;
            Vector3 s = _cam.WorldToScreenPoint(world);
            if (s.z <= 0f || !_cam.pixelRect.Contains(s)) return false;
            rt.position = new Vector3(s.x, s.y, 0f);
            return true;
        }

        // ================================================================ drag

        void UpdateDrag(Mouse mouse, Vector2 sp)
        {
            if (mouse == null) return;
            bool overMount = RectTransformUtility.RectangleContainsScreenPoint(_mountZone, sp, null);

            if (_dragId == null && !_returning && mouse.leftButton.wasPressedThisFrame)
            {
                foreach (var (rt, id, mounted) in _cards)
                {
                    if (!rt || !RectTransformUtility.RectangleContainsScreenPoint(rt, sp, null)) continue;
                    BeginDrag(rt, id, mounted);
                    break;
                }
            }

            bool dragging = _dragId != null;
            Vector3 ghostPos = dragging ? _ghost.position : Vector3.zero;
            bool overTarget = dragging && (_dragFromMount
                ? RectTransformUtility.RectangleContainsScreenPoint(_storageZone, ghostPos, null)
                : RectTransformUtility.RectangleContainsScreenPoint(_mountZone, ghostPos, null));

            // Drop zones light up while dragging.
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f);
            Color idle = RoverUI.Faint;
            Color want = Color.Lerp(RoverUI.A(RoverUI.Accent, 0.35f), RoverUI.Accent, pulse);
            RoverUI.SetOutline(_mountEdges, dragging && !_dragFromMount ? (overTarget ? RoverUI.Green : want) : idle);
            RoverUI.SetOutline(_storageEdges, dragging && _dragFromMount ? (overTarget ? RoverUI.Green : want) : idle);
            if (!dragging) return;

            // Heavy: the card trails the cursor at a capped speed.
            float k = _canvas.scaleFactor;
            Vector3 target = new Vector3(sp.x, sp.y, 0f);
            _ghost.position = Vector3.MoveTowards(_ghost.position, target, _dragSpeed * Time.unscaledDeltaTime);
            float lag = Vector3.Distance(_ghost.position, target) / Mathf.Max(1f, k);
            _ghost.localRotation = Quaternion.Euler(0f, 0f, Mathf.Clamp((target.x - _ghost.position.x) / k * -0.04f, -8f, 8f));
            var label = _ghost.Find("Hint")?.GetComponent<TextMeshProUGUI>();
            if (label) label.text = overTarget ? (_dragFromMount ? "RELEASE TO STORE" : "RELEASE TO MOUNT") : lag > 30f ? "HAULING..." : "";

            if (!mouse.leftButton.isPressed)
            {
                string id = _dragId;
                if (overTarget)
                {
                    var def = WeaponDatabase.Instance.Get(id);
                    string name = def != null ? def.displayName : id;
                    Destroy(_ghost.gameObject);
                    _ghost = null; _dragId = null;
                    if (_dragFromMount) { WeaponLoadout.Unmount(); SetStatus($"{name} moved to storage. The turret is empty."); }
                    else { WeaponLoadout.Mount(id); SetStatus($"{name} mounted on the turret."); }
                }
                else
                {
                    _dragId = null;
                    _returning = true;
                    _returnTo = _dragSource ? Mid(_dragSource) : _ghost.position;
                }
            }
        }

        void BeginDrag(RectTransform source, string id, bool fromMount)
        {
            _dragId = id;
            _dragFromMount = fromMount;
            _dragSource = source;
            _ghost = BuildCard(_ghostLayer, id, fromMount);
            _ghost.position = Mid(source);
            _ghost.gameObject.AddComponent<CanvasGroup>().alpha = 0.95f;
            var hint = RoverUI.Label(_ghost, "", 11, FontStyle.Bold, RoverUI.Accent, mono: true);
            hint.name = "Hint";
            hint.rectTransform.anchorMin = new Vector2(0, 0); hint.rectTransform.anchorMax = new Vector2(1, 0);
            hint.rectTransform.pivot = new Vector2(0.5f, 1f);
            hint.rectTransform.sizeDelta = new Vector2(0, 18);
            hint.rectTransform.anchoredPosition = new Vector2(0, -6);
            hint.alignment = TextAlignmentOptions.Center;
            var cg = source.GetComponent<CanvasGroup>();
            if (!cg) cg = source.gameObject.AddComponent<CanvasGroup>();
            cg.alpha = 0.25f;
            // Speed so that crossing from one zone to the other takes dragSeconds.
            float span = Vector3.Distance(Mid(_mountZone), Mid(_storageZone));
            _dragSpeed = Mathf.Max(50f, span / Mathf.Max(0.05f, dragSeconds));
        }

        void UpdateReturn()
        {
            if (!_ghost) { _returning = false; return; }
            _ghost.position = Vector3.MoveTowards(_ghost.position, _returnTo, _dragSpeed * 3f * Time.unscaledDeltaTime);
            _ghost.localRotation = Quaternion.identity;
            if (Vector3.Distance(_ghost.position, _returnTo) < 1f) CancelDrag();
        }

        void CancelDrag()
        {
            if (_ghost) Destroy(_ghost.gameObject);
            _ghost = null;
            _dragId = null;
            _returning = false;
            if (_dragSource)
            {
                var cg = _dragSource.GetComponent<CanvasGroup>();
                if (cg) cg.alpha = 1f;
            }
            _dragSource = null;
        }

        void UpdateDetails(Vector2 sp)
        {
            string id = _dragId;
            if (id == null)
                foreach (var (rt, cid, _) in _cards)
                    if (rt && RectTransformUtility.RectangleContainsScreenPoint(rt, sp, null)) { id = cid; break; }
            if (id == null) id = WeaponLoadout.Mounted;
            var def = WeaponDatabase.Instance.Get(id);
            if (def == null)
            {
                _detailTitle.text = "TURRET EMPTY";
                _detailText.text = "Drag a weapon from storage onto the mount.";
                return;
            }
            _detailTitle.text = def.displayName + (id == WeaponLoadout.Mounted ? "  <color=#4ADE80>[MOUNTED]</color>" : "");
            _detailText.text = $"{def.description}\n" +
                               $"<color=#8A9099>DMG</color> {def.damage:0.#}   <color=#8A9099>RATE</color> {def.fireRate:0.#}/s   " +
                               $"<color=#8A9099>DPS</color> {def.damage * def.fireRate:0.#}   <color=#8A9099>RANGE</color> {def.range:0} m   " +
                               $"<color=#8A9099>POWER</color> {def.batteryPerShot:0.##}/shot";
        }

        void SetStatus(string msg)
        {
            _status.text = msg;
            _statusTimer = string.IsNullOrEmpty(msg) ? 0f : 3f;
        }

        // ================================================================ UI

        void Build()
        {
            _canvas = RoverUI.NewCanvas("CockpitWeapons", transform, 25);
            _root = (RectTransform)_canvas.transform;

            // Popup above the crew.
            _popup = RoverUI.Rect("WeaponsPopup", _root);
            _popup.anchorMin = _popup.anchorMax = Vector2.zero;
            _popup.pivot = new Vector2(0.5f, 0f);
            _popup.sizeDelta = new Vector2(250, 64);
            RoverUI.Img(_popup, RoverUI.A(RoverUI.Black, 0.92f));
            RoverUI.Outline(_popup, RoverUI.Accent);
            var pt = RoverUI.Label(_popup, "COCKPIT", 11, FontStyle.Bold, RoverUI.Dim, mono: true);
            RoverUI.TopLeft(pt.rectTransform, 10, 6, 230, 14);
            _popupButtons.Add(_popup, "CHANGE WEAPONS  [ E ]", 10, 24, 230, 32, true, Open, 13);
            var arrow = RoverUI.Rect("Arrow", _popup);
            arrow.anchorMin = arrow.anchorMax = new Vector2(0.5f, 0f);
            RoverUI.Img(arrow, RoverUI.Accent);
            arrow.sizeDelta = new Vector2(10, 10);
            arrow.anchoredPosition = new Vector2(0, -2);
            arrow.localRotation = Quaternion.Euler(0, 0, 45);

            // Marker over the cockpit when the crew is elsewhere.
            _marker = RoverUI.Rect("CockpitMarker", _root);
            _marker.anchorMin = _marker.anchorMax = Vector2.zero;
            _marker.pivot = new Vector2(0.5f, 0f);
            _marker.sizeDelta = new Vector2(120, 22);
            var marrow = RoverUI.Rect("Arrow", _marker);
            marrow.anchorMin = marrow.anchorMax = new Vector2(0.5f, 0f);
            RoverUI.Img(marrow, RoverUI.A(RoverUI.Accent, 0.7f));
            marrow.sizeDelta = new Vector2(8, 8);
            marrow.anchoredPosition = new Vector2(0, -2);
            marrow.localRotation = Quaternion.Euler(0, 0, 45);
            RoverUI.Img(_marker, RoverUI.A(RoverUI.Black, 0.7f));
            RoverUI.Outline(_marker, RoverUI.A(RoverUI.Accent, 0.5f));
            var mt = RoverUI.Label(_marker, "WEAPONS", 11, FontStyle.Bold, RoverUI.Accent, mono: true);
            RoverUI.Stretch(mt.rectTransform);
            mt.alignment = TextAlignmentOptions.Center;

            BuildMenu();
        }

        void BuildMenu()
        {
            const float W = 900f, H = 600f, pad = 28f;
            _menu = RoverUI.Rect("WeaponsMenu", _root);
            RoverUI.Center(_menu, W, H, 200f, 0f); // centred in the 3D view (right of the 400 px side panel)
            RoverUI.Img(_menu, RoverUI.Black);
            RoverUI.Outline(_menu, RoverUI.Accent);
            _menuGroup = _menu.gameObject.AddComponent<CanvasGroup>();

            var crumbs = RoverUI.Label(_menu, RoverUI.Crumbs("ROOT", "ROVER", "COCKPIT", "WEAPONS"), 12, FontStyle.Bold, RoverUI.Dim);
            RoverUI.TopLeft(crumbs.rectTransform, pad, pad, W - pad * 2, 18);
            var title = RoverUI.Label(_menu, "WEAPON SYSTEMS", 30, FontStyle.Bold, RoverUI.Accent);
            RoverUI.TopLeft(title.rectTransform, pad, pad + 22, W - pad * 2, 40);
            var sub = RoverUI.Label(_menu, "Drag a weapon onto the turret mount to arm it, or into storage to take it off. Weapons are heavy, hauling one takes a moment.", 13, FontStyle.Normal, RoverUI.Dim);
            RoverUI.TopLeft(sub.rectTransform, pad, pad + 66, W - pad * 2, 36);

            float top = pad + 116f, zoneH = 260f;
            // Mount
            RoverUI.Header(_menu, "TURRET_MOUNT", pad, top, 280f, "1 SLOT");
            _mountZone = RoverUI.Rect("MountZone", _menu);
            RoverUI.TopLeft(_mountZone, pad, top + 44f, 280f, zoneH - 44f);
            RoverUI.Img(_mountZone, RoverUI.Ghost);
            _mountEdges = RoverUI.Outline(_mountZone, RoverUI.Faint, 2f);

            // Storage
            float sx = pad + 320f, sw = W - sx - pad;
            RoverUI.Header(_menu, "STORAGE", sx, top, sw, "UNLOCKED WEAPONS");
            _storageZone = RoverUI.Rect("StorageZone", _menu);
            RoverUI.TopLeft(_storageZone, sx, top + 44f, sw, zoneH - 44f);
            RoverUI.Img(_storageZone, RoverUI.Ghost);
            _storageEdges = RoverUI.Outline(_storageZone, RoverUI.Faint, 2f);
            _storageGrid = RoverUI.Rect("Grid", _storageZone);
            RoverUI.Stretch(_storageGrid, 16f);

            // Arrow between them.
            var swap = RoverUI.Label(_menu, "< >", 22, FontStyle.Bold, RoverUI.Faint);
            RoverUI.TopLeft(swap.rectTransform, pad + 280f, top + 44f + (zoneH - 44f) * 0.5f - 16f, 40f, 32f);
            swap.alignment = TextAlignmentOptions.Center;

            // Details + status + close.
            float dy = top + zoneH + 20f;
            var rule = RoverUI.Rect("Rule", _menu);
            RoverUI.TopLeft(rule, pad, dy, W - pad * 2, 1);
            RoverUI.Img(rule, RoverUI.Faint);
            _detailTitle = RoverUI.Label(_menu, "", 16, FontStyle.Bold, RoverUI.White);
            RoverUI.TopLeft(_detailTitle.rectTransform, pad, dy + 14f, W - pad * 2, 22);
            _detailText = RoverUI.Label(_menu, "", 13, FontStyle.Normal, RoverUI.White, mono: true);
            RoverUI.TopLeft(_detailText.rectTransform, pad, dy + 40f, W - pad * 2, 60);

            _status = RoverUI.Label(_menu, "", 13, FontStyle.Bold, RoverUI.Green);
            _status.rectTransform.anchorMin = _status.rectTransform.anchorMax = new Vector2(0, 0);
            _status.rectTransform.pivot = new Vector2(0, 0);
            _status.rectTransform.anchoredPosition = new Vector2(pad, pad + 12f);
            _status.rectTransform.sizeDelta = new Vector2(W - pad * 2 - 230f, 20);

            var close = RoverUI.Rect("Close", _menu);
            close.anchorMin = close.anchorMax = new Vector2(1, 0);
            close.pivot = new Vector2(1, 0);
            close.anchoredPosition = new Vector2(-pad, pad);
            close.sizeDelta = new Vector2(210, 42);
            _menuButtons.Wrap(close, "CLOSE  [ ESC ]", false, Close);

            // Dragged cards float above everything, positioned in screen pixels.
            _ghostLayer = RoverUI.Rect("DragLayer", _root);
            RoverUI.Stretch(_ghostLayer);

            _menu.gameObject.SetActive(false);
            _menuGroup.alpha = 0f;
        }

        void RebuildCards()
        {
            if (!_mountZone) return;
            CancelDrag();
            foreach (var (rt, _, _) in _cards) if (rt) Destroy(rt.gameObject);
            _cards.Clear();

            string mounted = WeaponLoadout.Mounted;
            if (mounted != null)
            {
                var card = BuildCard(_mountZone, mounted, true);
                RoverUI.Center(card, CardW, CardH);
                _cards.Add((card, mounted, true));
            }
            else
            {
                var empty = RoverUI.Label(_mountZone, "EMPTY\n<size=11>drop a weapon here</size>", 16, FontStyle.Bold, RoverUI.Faint);
                RoverUI.Stretch(empty.rectTransform);
                empty.alignment = TextAlignmentOptions.Center;
                _cards.Add((empty.rectTransform, null, true));
            }

            var stored = WeaponLoadout.Stored;
            for (int i = 0; i < stored.Count; i++)
            {
                var card = BuildCard(_storageGrid, stored[i], false);
                RoverUI.TopLeft(card, (i % 2) * (CardW + 14f), (i / 2) * (CardH + 14f), CardW, CardH);
                _cards.Add((card, stored[i], false));
            }
            if (stored.Count == 0)
            {
                var empty = RoverUI.Label(_storageGrid, "STORAGE EMPTY", 14, FontStyle.Bold, RoverUI.Faint);
                RoverUI.Stretch(empty.rectTransform);
                empty.alignment = TextAlignmentOptions.Center;
                _cards.Add((empty.rectTransform, null, false));
            }
            _cards.RemoveAll(c => c.id == null && c.rt && c.rt.GetComponent<TextMeshProUGUI>()); // labels aren't draggable
        }

        RectTransform BuildCard(RectTransform parent, string id, bool mounted)
        {
            var def = WeaponDatabase.Instance.Get(id);
            var card = RoverUI.Rect("Card_" + id, parent);
            card.sizeDelta = new Vector2(CardW, CardH);
            RoverUI.Img(card, RoverUI.Black);
            RoverUI.Outline(card, mounted ? RoverUI.Green : RoverUI.Accent);

            // Simple gun silhouette.
            var icon = RoverUI.Rect("Icon", card);
            RoverUI.TopLeft(icon, 14, 14, CardW - 28, 54);
            Color ic = mounted ? RoverUI.Green : RoverUI.Accent;
            bool heavy = def != null && def.damage >= 10f;
            Silhouette(icon, ic, heavy);

            var name = RoverUI.Label(card, def != null ? def.displayName : id, 15, FontStyle.Bold, RoverUI.White);
            RoverUI.TopLeft(name.rectTransform, 14, 76, CardW - 28, 20);
            string stats = def != null ? $"{def.damage:0.#} DMG / {def.fireRate:0.#} PER S" : "";
            var st = RoverUI.Label(card, stats, 11, FontStyle.Normal, RoverUI.Dim, mono: true);
            RoverUI.TopLeft(st.rectTransform, 14, 98, CardW - 28, 16);
            var tag = RoverUI.Label(card, mounted ? "[MOUNTED]" : "[STORED]", 10, FontStyle.Bold, mounted ? RoverUI.Green : RoverUI.Dim, mono: true);
            RoverUI.TopLeft(tag.rectTransform, 14, 120, CardW - 28, 14);
            return card;
        }

        static void Silhouette(RectTransform icon, Color c, bool heavy)
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
            Box(20, 0, 46, 8);          // base
            Box(30, 8, 26, 8);          // neck
            if (heavy)
            {
                Box(14, 16, 64, 22);    // body
                Box(78, 22, 70, 10);    // long rail
                Box(78, 34, 70, 3);
                Box(78, 19, 70, 3);
            }
            else
            {
                Box(24, 16, 44, 20);    // body
                Box(68, 26, 60, 5);     // barrel
                Box(68, 19, 52, 4);     // second barrel
                Box(124, 24, 8, 9);     // muzzle brake
            }
        }

        /// <summary>Screen position of a rect's centre (whatever its pivot).</summary>
        static Vector3 Mid(RectTransform rt) => rt.TransformPoint(rt.rect.center);

        static float Flat(Vector3 a, Vector3 b) { a.y = 0f; b.y = 0f; return Vector3.Distance(a, b); }
    }
}
