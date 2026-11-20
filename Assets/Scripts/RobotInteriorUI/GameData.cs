using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dane, które przetrwają zmianę sceny (zwykła klasa statyczna, bez obiektu na scenie).
/// </summary>
public static class GameData
{
    public static int PlayerFuel;               // paliwo niesione przez gracza (zbierane w grze)
    public static int ShipFuel;                 // paliwo wrzucone do zbiornika statku
    public static int ShipFuelCapacity = 100;   // pojemność zbiornika
    public static int Credits;                  // waluta na ulepszenia (na razie placeholder)

    public static bool TestFuelGranted;
    public static bool TestCreditsGranted;

    static readonly Dictionary<string, int> upgradeLevels = new Dictionary<string, int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ResetAll()
    {
        PlayerFuel = 0;
        ShipFuel = 0;
        ShipFuelCapacity = 100;
        Credits = 0;
        TestFuelGranted = false;
        TestCreditsGranted = false;
        upgradeLevels.Clear();
        GameState.UiOpen = false;
    }

    /// <summary>Przenosi paliwo od gracza do zbiornika. Zwraca ile faktycznie wrzucono.</summary>
    public static int DepositFuel(int amount)
    {
        amount = Mathf.Clamp(amount, 0, Mathf.Min(PlayerFuel, ShipFuelCapacity - ShipFuel));
        PlayerFuel -= amount;
        ShipFuel += amount;
        return amount;
    }

    public static int GetUpgradeLevel(string id)
    {
        return upgradeLevels.TryGetValue(id, out int level) ? level : 0;
    }

    public static bool TryBuyUpgrade(UpgradeItem item)
    {
        int level = GetUpgradeLevel(item.Id);
        if (level >= item.maxLevel || Credits < item.cost) return false;

        Credits -= item.cost;
        upgradeLevels[item.Id] = level + 1;
        return true;
    }
}

/// <summary>Stan interfejsu - gdy jest otwarty panel, gracz stoi w miejscu.</summary>
public static class GameState
{
    public static bool UiOpen;
}
