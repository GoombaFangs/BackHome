using UnityEngine;

/// <summary>
/// Shared stat definition for a creature type (HP, attack, range, …).
/// Create one asset per creature (e.g. Grimling_Stats) and reuse it on every instance.
/// </summary>
[CreateAssetMenu(menuName = "BackHome/Creature Stats", fileName = "CreatureStats_")]
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

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public float MaxHealth => maxHealth;
    public float VisionRange => visionRange;
    public float AttackDamage => attackDamage;
    public float AttackSpeed => attackSpeed;
    public float AttackRange => attackRange;

    void OnValidate()
    {
        maxHealth = Mathf.Max(1f, maxHealth);
        visionRange = Mathf.Max(0f, visionRange);
        attackDamage = Mathf.Max(0f, attackDamage);
        attackSpeed = Mathf.Max(0.01f, attackSpeed);
        attackRange = Mathf.Max(0.05f, attackRange);
    }
}
