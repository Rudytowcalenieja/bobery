using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Bazowa klasa stacji: zasięg interakcji + okno UI z tytułem i przyciskiem zamknięcia.
/// Konkretne stacje dziedziczą po niej i budują zawartość okna w BuildContent().
/// </summary>
public abstract class Station : MonoBehaviour
{
    public static readonly List<Station> All = new List<Station>();

    [Header("Stacja")]
    public string stationName = "Stacja";
    public float interactRadius = 2.5f;
    public Vector2 windowSize = new Vector2(760f, 640f);

    public bool IsOpen { get; private set; }
    public string PromptText => $"[E]  {stationName}";

    Canvas canvas;

    protected virtual void OnEnable() { All.Add(this); }

    protected virtual void OnDisable()
    {
        All.Remove(this);
        Close();
    }

    protected virtual void OnDestroy()
    {
        if (canvas != null) Destroy(canvas.gameObject);
    }

    public void Open()
    {
        if (IsOpen) return;
        if (canvas == null) BuildWindow();

        canvas.gameObject.SetActive(true);
        IsOpen = true;
        GameState.UiOpen = true;
        OnOpened();
    }

    public void Close()
    {
        if (!IsOpen) return;

        IsOpen = false;
        GameState.UiOpen = false;
        if (canvas != null) canvas.gameObject.SetActive(false);
        OnClosed();
    }

    protected abstract void BuildContent(RectTransform content);
    protected virtual void OnOpened() { }
    protected virtual void OnClosed() { }

    void BuildWindow()
    {
        UIKit.EnsureEventSystem();

        canvas = UIKit.CreateCanvas("UI_" + stationName);

        Image dim = UIKit.CreateImage(canvas.transform, "Dim", UIKit.DimColor);
        UIKit.Stretch(dim.rectTransform);

        Image window = UIKit.CreateImage(canvas.transform, "Window", UIKit.PanelColor);
        RectTransform wrt = window.rectTransform;
        wrt.anchorMin = wrt.anchorMax = wrt.pivot = new Vector2(0.5f, 0.5f);
        wrt.sizeDelta = windowSize;
        UIKit.AddVertical(window.gameObject, 14f, 28);

        TextMeshProUGUI title = UIKit.CreateText(window.transform, stationName, 40f,
            TextAlignmentOptions.Center, 60f);
        title.fontStyle = FontStyles.Bold;

        RectTransform content = UIKit.CreateRect("Content", window.transform);
        UIKit.AddVertical(content.gameObject, 12f);
        content.gameObject.AddComponent<LayoutElement>().flexibleHeight = 1f;
        BuildContent(content);

        UIKit.CreateButton(window.transform, "Zamknij (Esc)", Close, 56f, 24f);

        canvas.gameObject.SetActive(false);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.8f);
        Gizmos.DrawWireSphere(transform.position, interactRadius);
    }
}
