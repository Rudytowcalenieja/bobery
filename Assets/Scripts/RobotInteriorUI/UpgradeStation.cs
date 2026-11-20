using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Panel ulepszeń. Użyj tego samego skryptu na dwóch stacjach i nadaj im inne nazwy
/// oraz inne listy Items. Pusta lista = komunikat "brak ulepszeń".
/// </summary>
public class UpgradeStation : Station
{
    [Header("Ulepszenia")]
    public List<UpgradeItem> items = new List<UpgradeItem>();

    [Tooltip("Kredyty dodawane raz na start - do testów. Ustaw 0, gdy waluta będzie pochodzić z gry.")]
    public int testCredits = 100;

    TextMeshProUGUI creditsText;
    RectTransform listRoot;

    void Start()
    {
        if (testCredits > 0 && !GameData.TestCreditsGranted)
        {
            GameData.Credits += testCredits;
            GameData.TestCreditsGranted = true;
        }
    }

    protected override void BuildContent(RectTransform content)
    {
        creditsText = UIKit.CreateText(content, "", 28f, TextAlignmentOptions.Center, 40f);
        listRoot = UIKit.CreateScrollList(content);
    }

    protected override void OnOpened() => Refresh();

    void Refresh()
    {
        creditsText.text = $"Kredyty: {GameData.Credits}";

        // Wyczyść starą listę (SetActive(false), żeby layout od razu ją pominął)
        foreach (Transform child in listRoot)
        {
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }

        if (items == null || items.Count == 0)
        {
            UIKit.CreateText(listRoot, "Brak dostępnych ulepszeń.", 26f, TextAlignmentOptions.Center, 80f);
            return;
        }

        foreach (UpgradeItem item in items)
            if (item != null) BuildRow(item);
    }

    void BuildRow(UpgradeItem item)
    {
        int level = GameData.GetUpgradeLevel(item.Id);
        bool maxed = level >= item.maxLevel;

        Image row = UIKit.CreateImage(listRoot, "Item_" + item.Id, UIKit.SubtleColor);
        UIKit.AddHorizontal(row.gameObject, 12f, 12, false);
        row.gameObject.AddComponent<LayoutElement>().preferredHeight = 96f;

        RectTransform info = UIKit.CreateRect("Info", row.transform);
        UIKit.AddVertical(info.gameObject, 2f, 0, TextAnchor.MiddleLeft);
        info.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        UIKit.CreateText(info, $"{item.displayName}  (poz. {level}/{item.maxLevel})", 26f,
            TextAlignmentOptions.Left, 34f);
        UIKit.CreateText(info, item.description, 20f, TextAlignmentOptions.Left, 30f);

        Button buy = UIKit.CreateButton(row.transform, maxed ? "MAKS" : $"Kup ({item.cost})",
            () => Buy(item), 60f, 24f);
        var le = buy.GetComponent<LayoutElement>();
        le.preferredWidth = 180f;
        le.flexibleWidth = 0f;
        buy.interactable = !maxed && GameData.Credits >= item.cost;
    }

    void Buy(UpgradeItem item)
    {
        GameData.TryBuyUpgrade(item);
        Refresh();
    }
}
