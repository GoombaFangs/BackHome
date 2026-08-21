using UnityEngine;

/// <summary>
/// Shared definition for an inventory item (icon, name).
/// Create one asset per loot type.
/// </summary>
    [CreateAssetMenu(menuName = "BackHome/Item Definition", fileName = "Item")]
public class ItemDefinition : ScriptableObject
{
    [SerializeField] string displayName = "Item";
    [SerializeField] Sprite icon;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public Sprite Icon => icon;
}
