using UnityEngine;

/// <summary>
/// Shared combat stats for the player (HP, attack, …).
/// Same pattern as <see cref="CreatureStats"/> — tune values in the asset, not on every prefab.
/// </summary>
[CreateAssetMenu(menuName = "BackHome/Player Stats", fileName = "PlayerStats_")]
public class PlayerStats : ScriptableObject
{
    [SerializeField] string displayName = "Player";

    [Header("Combat")]
    [SerializeField, Min(1f)] float maxHealth = 200f;
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
