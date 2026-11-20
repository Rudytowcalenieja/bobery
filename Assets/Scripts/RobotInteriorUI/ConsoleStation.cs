using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Konsola główna: stąd gracz wraca do sceny z grą.
/// Scena musi być dodana w File -> Build Settings -> Scenes In Build.
/// </summary>
public class ConsoleStation : Station
{
    [Header("Konsola")]
    public string gameSceneName = "Game";

    [Tooltip("Minimalna ilość paliwa w zbiorniku, by można było wyruszyć (0 = bez wymagań).")]
    public int minFuelToLaunch = 0;

    TextMeshProUGUI infoText, feedbackText;
    Button launchButton;

    protected override void BuildContent(RectTransform content)
    {
        infoText = UIKit.CreateText(content, "", 30f, TextAlignmentOptions.Center, 44f);
        UIKit.CreateSpacer(content, 20f);
        launchButton = UIKit.CreateButton(content, "Wyrusz do gry", Launch, 72f, 32f);
        feedbackText = UIKit.CreateText(content, "", 24f, TextAlignmentOptions.Center, 60f);
    }

    protected override void OnOpened() => Refresh();

    void Refresh()
    {
        infoText.text = $"Paliwo statku: {GameData.ShipFuel} / {GameData.ShipFuelCapacity}";

        bool enoughFuel = GameData.ShipFuel >= minFuelToLaunch;
        launchButton.interactable = enoughFuel;
        feedbackText.text = enoughFuel ? "" : $"Potrzebujesz co najmniej {minFuelToLaunch} paliwa w zbiorniku.";
    }

    void Launch()
    {
        if (!Application.CanStreamedLevelBeLoaded(gameSceneName))
        {
            feedbackText.text = $"Scena \"{gameSceneName}\" nie jest dodana do Build Settings.";
            return;
        }

        Close();
        SceneManager.LoadScene(gameSceneName);
    }
}
