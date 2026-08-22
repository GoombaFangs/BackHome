using UnityEngine;

public enum ItemCategory
{
    Material,
    Currency,
    Consumable,
    Quest
}

/// <summary>
/// Catalog entry for an inventory item. Create one asset per item type.
/// World drop presentation lives on <see cref="worldDropPrefab"/>; loot tables live on the creature.
/// </summary>
[CreateAssetMenu(menuName = "BackHome/Item Definition", fileName = "Item")]
public class ItemDefinition : ScriptableObject
{
    [SerializeField] string displayName = "Item";
    [SerializeField] Sprite icon;
    [SerializeField] ItemCategory category = ItemCategory.Material;
    [SerializeField, Min(1)] int maxStack = 999;
    [Tooltip("Pooled world pickup spawned when this item drops.")]
    [SerializeField] GameObject worldDropPrefab;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public Sprite Icon => icon;
    public ItemCategory Category => category;
    public int MaxStack => Mathf.Max(1, maxStack);
    public GameObject WorldDropPrefab => worldDropPrefab;

    void OnValidate()
    {
        maxStack = Mathf.Max(1, maxStack);
    }
}
