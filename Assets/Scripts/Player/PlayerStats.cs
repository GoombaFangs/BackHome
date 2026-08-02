using UnityEngine;

/// <summary>
/// Shared player stats (HP, attack, oxygen tank, …).
/// Same pattern as <see cref="CreatureStats"/> — tune values in the asset, not on every prefab.
/// </summary>
[CreateAssetMenu(menuName = "BackHome/Player Stats", fileName = "PlayerStats_")]
public class PlayerStats : ScriptableObject
{
    [SerializeField] string displayName = "Player";

    [Header("Combat")]
    [SerializeField, Min(0f)] float attackDamage = 10f;
    [Tooltip("Attacks per second. 1 = deal attack damage once every second.")]
    [SerializeField, Min(0.01f)] float attackSpeed = 1f;
    [Tooltip("World-space radius of the attack ring around the player.")]
    [SerializeField, Min(0.05f)] float attackRange = 5f;

    [Header("Survival")]
    [SerializeField, Min(1f)] float maxHealth = 200f;
    [Tooltip("Oxygen tank capacity.")]
    [SerializeField, Min(1f)] float oxygenTank = 30f;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public float MaxHealth => maxHealth;
    public float AttackDamage => attackDamage;
    public float AttackSpeed => attackSpeed;
    public float AttackRange => attackRange;
    public float OxygenTank => oxygenTank;

    void OnValidate()
    {
        maxHealth = Mathf.Max(1f, maxHealth);
        attackDamage = Mathf.Max(0f, attackDamage);
        attackSpeed = Mathf.Max(0.01f, attackSpeed);
        attackRange = Mathf.Max(0.05f, attackRange);
        oxygenTank = Mathf.Max(1f, oxygenTank);
    }
}
