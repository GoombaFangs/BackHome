using UnityEngine;

/// <summary>
/// Shared stat definition for a creature type (HP, attack, …).
/// Create one asset per creature (e.g. Grimling_Stats) and reuse it on every instance.
/// </summary>
[CreateAssetMenu(menuName = "BackHome/Creature Stats", fileName = "CreatureStats_")]
public class CreatureStats : ScriptableObject
{
    [SerializeField] string displayName = "Creature";

    [Header("Combat")]
    [SerializeField, Min(1f)] float maxHealth = 50f;
    [SerializeField, Min(0f)] float attackDamage = 10f;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public float MaxHealth => maxHealth;
    public float AttackDamage => attackDamage;

    void OnValidate()
    {
        maxHealth = Mathf.Max(1f, maxHealth);
        attackDamage = Mathf.Max(0f, attackDamage);
    }
}
