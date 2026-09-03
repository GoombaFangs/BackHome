using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Player kit: vitality, base combat, and up to <see cref="CombatLoadout.MaxWeapons"/> weapons.
/// Each gun fights with <c>base + that weapon</c> and floats on its own.
/// </summary>
[CreateAssetMenu(menuName = "BackHome/Player Stats", fileName = "PlayerStats")]
public class PlayerStats : ScriptableObject
{
    [SerializeField] string displayName = "Player";

    [Header("Combat")]
    [SerializeField, Min(0f)] float attackDamage = 10f;
    [Tooltip("Attacks per second. 1 = deal attack damage once every second.")]
    [SerializeField, Min(0f)] float attackSpeed = 1f;
    [Tooltip("World-space radius of the attack ring around the player.")]
    [SerializeField, Min(0f)] float attackRange = 5f;

    [Header("Weapons")]
    [Tooltip("Up to 3. Each floats beside the player and fights with Combat + this weapon.")]
    [SerializeField] WeaponDefinition[] weapons;

    [Header("Movement")]
    [Tooltip("Multiplies the player's walk/run speed. 1 = normal speed.")]
    [SerializeField, Min(0.1f)] float moveSpeedMultiplier = 1f;

    [Header("Vitality")]
    [SerializeField, Min(1f)] float maxHealth = 200f;
    [Tooltip("Oxygen tank capacity.")]
    [SerializeField, Min(1f)] float oxygenTank = 30f;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public float MaxHealth => maxHealth;
    public float OxygenTank => oxygenTank;
    public CombatStats BaseCombat => new CombatStats(attackDamage, attackSpeed, attackRange);
    public IReadOnlyList<WeaponDefinition> Weapons => weapons ?? Array.Empty<WeaponDefinition>();
    public float MaxAttackRange => CombatLoadout.MaxRange(BaseCombat, Weapons);
    public float MoveSpeedMultiplier => moveSpeedMultiplier;

    public CombatStats CombatFor(WeaponDefinition weapon)
    {
        return CombatLoadout.ForWeapon(BaseCombat, weapon);
    }

    void OnValidate()
    {
        maxHealth = Mathf.Max(1f, maxHealth);
        attackDamage = Mathf.Max(0f, attackDamage);
        attackSpeed = Mathf.Max(0f, attackSpeed);
        attackRange = Mathf.Max(0f, attackRange);
        moveSpeedMultiplier = Mathf.Max(0.1f, moveSpeedMultiplier);
        oxygenTank = Mathf.Max(1f, oxygenTank);
        if (weapons != null && weapons.Length > CombatLoadout.MaxWeapons)
            Array.Resize(ref weapons, CombatLoadout.MaxWeapons);
    }
}
