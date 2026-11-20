using UnityEngine;

/// <summary>
/// Pojedyncze ulepszenie. Tworzysz: prawy klik w Project -> Create -> Game -> Upgrade Item.
/// </summary>
[CreateAssetMenu(menuName = "Game/Upgrade Item", fileName = "NewUpgrade")]
public class UpgradeItem : ScriptableObject
{
    [Tooltip("Unikalne ID (jeśli puste, użyta zostanie nazwa assetu)")]
    public string id;
    public string displayName = "Ulepszenie";
    [TextArea] public string description;
    public int cost = 10;
    public int maxLevel = 1;

    public string Id => string.IsNullOrEmpty(id) ? name : id;
}
