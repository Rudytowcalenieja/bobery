using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Małe narzędzie do budowania interfejsu z kodu (Canvas, panele, przyciski, teksty).
/// Dzięki temu stacje nie wymagają ręcznego składania UI w edytorze.
/// </summary>
public static class UIKit
{
    public static readonly Color DimColor = new Color(0f, 0f, 0f, 0.6f);
    public static readonly Color PanelColor = new Color(0.09f, 0.11f, 0.15f, 0.97f);
    public static readonly Color ButtonColor = new Color(0.20f, 0.45f, 0.75f, 1f);
    public static readonly Color AccentColor = new Color(0.95f, 0.65f, 0.15f, 1f);
    public static readonly Color SubtleColor = new Color(1f, 1f, 1f, 0.08f);

    // ---------- EventSystem (wymagany, żeby działały przyciski) ----------

    public static void EnsureEventSystem()
    {
        EventSystem es = EventSystem.current;
        if (es == null)
            es = new GameObject("EventSystem", typeof(EventSystem)).GetComponent<EventSystem>();

        // Stary moduł nie działa z nowym Input System
        var legacy = es.GetComponent<StandaloneInputModule>();
        if (legacy != null) UnityEngine.Object.Destroy(legacy);

        if (es.GetComponent<InputSystemUIInputModule>() == null)
        {
            var module = es.gameObject.AddComponent<InputSystemUIInputModule>();
            module.AssignDefaultActions();
        }
    }

    // ---------- Podstawy ----------

    public static Canvas CreateCanvas(string name, int sortingOrder = 100)
    {
        var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        var canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        return canvas;
    }

    public static RectTransform CreateRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    public static Image CreateImage(Transform parent, string name, Color color)
    {
        RectTransform rt = CreateRect(name, parent);
        var img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        return img;
    }

    public static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    // ---------- Layouty ----------

    public static VerticalLayoutGroup AddVertical(GameObject go, float spacing = 12f, int padding = 0,
        TextAnchor align = TextAnchor.UpperCenter)
    {
        var v = go.AddComponent<VerticalLayoutGroup>();
        v.spacing = spacing;
        v.padding = new RectOffset(padding, padding, padding, padding);
        v.childAlignment = align;
        v.childControlWidth = true;
        v.childControlHeight = true;
        v.childForceExpandWidth = true;
        v.childForceExpandHeight = false;
        return v;
    }

    public static HorizontalLayoutGroup AddHorizontal(GameObject go, float spacing = 10f, int padding = 0,
        bool forceExpandWidth = true)
    {
        var h = go.AddComponent<HorizontalLayoutGroup>();
        h.spacing = spacing;
        h.padding = new RectOffset(padding, padding, padding, padding);
        h.childAlignment = TextAnchor.MiddleCenter;
        h.childControlWidth = true;
        h.childControlHeight = true;
        h.childForceExpandWidth = forceExpandWidth;
        h.childForceExpandHeight = true;
        return h;
    }

    public static RectTransform CreateRow(Transform parent, float height = 56f, float spacing = 10f)
    {
        RectTransform rt = CreateRect("Row", parent);
        AddHorizontal(rt.gameObject, spacing);
        rt.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
        return rt;
    }

    public static void CreateSpacer(Transform parent, float height)
    {
        RectTransform rt = CreateRect("Spacer", parent);
        rt.gameObject.AddComponent<LayoutElement>().preferredHeight = height;
    }

    // ---------- Elementy ----------

    public static TextMeshProUGUI CreateText(Transform parent, string text, float fontSize,
        TextAlignmentOptions align = TextAlignmentOptions.Center, float height = 40f, float flexibleWidth = 0f)
    {
        RectTransform rt = CreateRect("Text", parent);
        var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.alignment = align;
        tmp.color = Color.white;
        tmp.raycastTarget = false;

        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        if (flexibleWidth > 0f) le.flexibleWidth = flexibleWidth;
        return tmp;
    }

    public static Button CreateButton(Transform parent, string label, Action onClick,
        float height = 56f, float fontSize = 26f)
    {
        Image img = CreateImage(parent, "Button_" + label, ButtonColor);
        var button = img.gameObject.AddComponent<Button>();
        button.targetGraphic = img;

        var le = img.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = height;
        le.flexibleWidth = 1f;

        TextMeshProUGUI t = CreateText(img.transform, label, fontSize);
        Stretch(t.rectTransform);

        if (onClick != null) button.onClick.AddListener(() => onClick());
        return button;
    }

    public static void SetLabel(Button button, string label)
    {
        var t = button.GetComponentInChildren<TextMeshProUGUI>();
        if (t != null) t.text = label;
    }

    /// <summary>Pasek postępu. Wypełnienie ustawiasz przez SetBar(fill, 0..1).</summary>
    public static (RectTransform root, RectTransform fill) CreateBar(Transform parent, float height = 30f)
    {
        Image bg = CreateImage(parent, "Bar", new Color(1f, 1f, 1f, 0.12f));
        bg.gameObject.AddComponent<LayoutElement>().preferredHeight = height;

        Image fill = CreateImage(bg.transform, "Fill", AccentColor);
        RectTransform rt = fill.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = new Vector2(0f, 1f);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        return (bg.rectTransform, rt);
    }

    public static void SetBar(RectTransform fill, float t)
    {
        fill.anchorMax = new Vector2(Mathf.Clamp01(t), 1f);
    }

    /// <summary>Przewijana lista. Zwraca obiekt Content, do którego dodajesz wiersze.</summary>
    public static RectTransform CreateScrollList(Transform parent)
    {
        Image bg = CreateImage(parent, "ScrollList", new Color(0f, 0f, 0f, 0.25f));
        var le = bg.gameObject.AddComponent<LayoutElement>();
        le.flexibleHeight = 1f;
        le.preferredHeight = 240f;

        RectTransform viewport = CreateRect("Viewport", bg.transform);
        Stretch(viewport);
        viewport.gameObject.AddComponent<RectMask2D>();

        RectTransform content = CreateRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot = new Vector2(0.5f, 1f);
        content.anchoredPosition = Vector2.zero;
        content.sizeDelta = Vector2.zero;
        AddVertical(content.gameObject, 8f, 8);
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = bg.gameObject.AddComponent<ScrollRect>();
        scroll.viewport = viewport;
        scroll.content = content;
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 30f;
        return content;
    }
}
