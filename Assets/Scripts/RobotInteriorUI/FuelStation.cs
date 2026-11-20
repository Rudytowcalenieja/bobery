using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Stacja paliwowa: gracz wybiera ile paliwa (z tego, które niesie) wrzuci do zbiornika statku.
/// </summary>
public class FuelStation : Station
{
    [Header("Paliwo")]
    [Tooltip("Paliwo dodawane graczowi raz na start - do testów. Ustaw 0, gdy paliwo będzie pochodzić z gry.")]
    public int testFuelOnStart = 60;

    int selectedAmount;
    TextMeshProUGUI carriedText, tankText, amountText, feedbackText;
    RectTransform barFill;
    Button depositButton;

    void Start()
    {
        if (testFuelOnStart > 0 && !GameData.TestFuelGranted)
        {
            GameData.PlayerFuel += testFuelOnStart;
            GameData.TestFuelGranted = true;
        }
    }

    int MaxDepositable =>
        Mathf.Max(0, Mathf.Min(GameData.PlayerFuel, GameData.ShipFuelCapacity - GameData.ShipFuel));

    protected override void BuildContent(RectTransform content)
    {
        carriedText = UIKit.CreateText(content, "", 30f, TextAlignmentOptions.Center, 44f);
        tankText = UIKit.CreateText(content, "", 30f, TextAlignmentOptions.Center, 44f);
        barFill = UIKit.CreateBar(content, 30f).fill;

        UIKit.CreateSpacer(content, 10f);
        UIKit.CreateText(content, "Ilość paliwa do wrzucenia:", 26f, TextAlignmentOptions.Center, 36f);

        RectTransform amountRow = UIKit.CreateRow(content, 64f);
        UIKit.CreateButton(amountRow, "-10", () => Change(-10), 64f, 28f);
        UIKit.CreateButton(amountRow, "-1", () => Change(-1), 64f, 28f);
        amountText = UIKit.CreateText(amountRow, "0", 42f, TextAlignmentOptions.Center, 64f, 2f);
        UIKit.CreateButton(amountRow, "+1", () => Change(1), 64f, 28f);
        UIKit.CreateButton(amountRow, "+10", () => Change(10), 64f, 28f);

        RectTransform actionRow = UIKit.CreateRow(content, 64f);
        UIKit.CreateButton(actionRow, "Maks", () => SetAmount(MaxDepositable), 64f, 28f);
        depositButton = UIKit.CreateButton(actionRow, "Wrzuć paliwo", Deposit, 64f, 28f);

        feedbackText = UIKit.CreateText(content, "", 24f, TextAlignmentOptions.Center, 40f);
    }

    protected override void OnOpened()
    {
        if (GameData.PlayerFuel <= 0) feedbackText.text = "Nie masz paliwa do wrzucenia.";
        else if (MaxDepositable == 0) feedbackText.text = "Zbiornik jest pełny.";
        else feedbackText.text = "";

        SetAmount(MaxDepositable); // domyślnie całe paliwo, które gracz może wrzucić
    }

    void Change(int delta) => SetAmount(selectedAmount + delta);

    void SetAmount(int value)
    {
        selectedAmount = Mathf.Clamp(value, 0, MaxDepositable);
        Refresh();
    }

    void Deposit()
    {
        int moved = GameData.DepositFuel(selectedAmount);
        feedbackText.text = moved > 0 ? $"Wrzucono {moved} paliwa." : "Nie ma co wrzucać.";
        SetAmount(selectedAmount);
    }

    void Refresh()
    {
        carriedText.text = $"Masz przy sobie: {GameData.PlayerFuel}";
        tankText.text = $"Zbiornik statku: {GameData.ShipFuel} / {GameData.ShipFuelCapacity}";
        UIKit.SetBar(barFill, (float)GameData.ShipFuel / Mathf.Max(1, GameData.ShipFuelCapacity));
        amountText.text = selectedAmount.ToString();
        depositButton.interactable = selectedAmount > 0;
    }
}
