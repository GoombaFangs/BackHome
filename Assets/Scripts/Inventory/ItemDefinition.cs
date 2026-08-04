using UnityEngine;

/// <summary>
/// Shared definition for an inventory item (icon, name, id).
/// Create one asset per loot type.
/// </summary>
[CreateAssetMenu(menuName = "BackHome/Item Definition", fileName = "Item_")]
public class ItemDefinition : ScriptableObject
{
    [SerializeField] string id = "item";
    [SerializeField] string displayName = "Item";
    [SerializeField] Sprite icon;

    public string Id => string.IsNullOrWhiteSpace(id) ? name : id;
    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public Sprite Icon => icon;
}
