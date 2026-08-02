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
    [SerializeField, Min(1f)] float maxHealth = 200f;
    [SerializeField, Min(0f)] float attackDamage = 10f;

    [Header("Survival")]
    [Tooltip("Oxygen tank capacity.")]
    [SerializeField, Min(1f)] float oxygenTank = 30f;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public float MaxHealth => maxHealth;
    public float AttackDamage => attackDamage;
    public float OxygenTank => oxygenTank;

    void OnValidate()
    {
        maxHealth = Mathf.Max(1f, maxHealth);
        attackDamage = Mathf.Max(0f, attackDamage);
        oxygenTank = Mathf.Max(1f, oxygenTank);
    }
}
