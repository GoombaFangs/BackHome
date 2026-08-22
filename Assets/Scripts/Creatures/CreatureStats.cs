using System;
using UnityEngine;

[Serializable]
public struct LootEntry
{
    public ItemDefinition item;
    [Min(1)] public int minAmount;
    [Min(1)] public int maxAmount;
    [Tooltip("0 = never, 1 = always.")]
    [Range(0f, 1f)] public float chance;
}

/// <summary>
/// Shared stat definition for a creature type (HP, attack, range, loot).
/// Create one asset per creature (e.g. GrimlingStats) and reuse it on every instance.
/// </summary>
[CreateAssetMenu(menuName = "BackHome/Creature Stats", fileName = "CreatureStats")]
public class CreatureStats : ScriptableObject
{
    [SerializeField] string displayName = "Creature";

    [Header("Combat")]
    [SerializeField, Min(0f)] float attackDamage = 10f;
    [Tooltip("Attacks per second. 1 = deal attack damage once every second.")]
    [SerializeField, Min(0.01f)] float attackSpeed = 1f;
    [Tooltip("World-space radius in which this creature can hit the player.")]
    [SerializeField, Min(0.05f)] float attackRange = 2f;

    [Header("Vitality")]
    [SerializeField, Min(1f)] float maxHealth = 50f;
    [Tooltip("World-space radius in which this creature detects the player and starts chasing.")]
    [SerializeField, Min(0f)] float visionRange = 10f;

    [Header("Loot")]
    [Tooltip("Rolled independently on death. Chance 0 = never, 1 = always.")]
    [SerializeField] LootEntry[] loot = Array.Empty<LootEntry>();

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public float MaxHealth => maxHealth;
    public float VisionRange => visionRange;
    public float AttackDamage => attackDamage;
    public float AttackSpeed => attackSpeed;
    public float AttackRange => attackRange;
    public LootEntry[] Loot => loot;

    public bool TryRollLoot(int index, out ItemDefinition item, out int amount)
    {
        item = null;
        amount = 0;
        if (loot == null || index < 0 || index >= loot.Length)
            return false;

        LootEntry entry = loot[index];
        if (entry.item == null || entry.chance <= 0f)
            return false;

        if (entry.chance < 1f && UnityEngine.Random.value > entry.chance)
            return false;

        int min = Mathf.Max(1, entry.minAmount);
        int max = Mathf.Max(min, entry.maxAmount);
        amount = min == max ? min : UnityEngine.Random.Range(min, max + 1);
        item = entry.item;
        return true;
    }

    void OnValidate()
    {
        maxHealth = Mathf.Max(1f, maxHealth);
        visionRange = Mathf.Max(0f, visionRange);
        attackDamage = Mathf.Max(0f, attackDamage);
        attackSpeed = Mathf.Max(0.01f, attackSpeed);
        attackRange = Mathf.Max(0.05f, attackRange);

        if (loot == null)
            return;

        for (int i = 0; i < loot.Length; i++)
        {
            LootEntry entry = loot[i];
            bool newRow = entry.minAmount <= 0 && entry.maxAmount <= 0;
            entry.minAmount = Mathf.Max(1, entry.minAmount);
            entry.maxAmount = Mathf.Max(entry.minAmount, entry.maxAmount);
            entry.chance = newRow ? 1f : Mathf.Clamp01(entry.chance);
            loot[i] = entry;
        }
    }
}
