using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Player kit: vitality, base combat, and the weapons that stack on top of it.
/// Runtime combat is <see cref="ResolvedCombat"/> — <c>base + Weapons</c> via <see cref="CombatLoadout"/>.
/// Swap this asset, change the base numbers, or drop a different <see cref="WeaponDefinition"/> in the list.
/// </summary>
[CreateAssetMenu(menuName = "BackHome/Player Stats", fileName = "PlayerStats_")]
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
    [Tooltip("Equipped weapons. Resolved combat is base + every entry in this list.")]
    [SerializeField] WeaponDefinition[] weapons;

    [Header("Vitality")]
    [SerializeField, Min(1f)] float maxHealth = 200f;
    [Tooltip("Oxygen tank capacity.")]
    [SerializeField, Min(1f)] float oxygenTank = 30f;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public float MaxHealth => maxHealth;
    public float OxygenTank => oxygenTank;
    public CombatStats BaseCombat => new CombatStats(attackDamage, attackSpeed, attackRange);
    public IReadOnlyList<WeaponDefinition> Weapons => weapons ?? Array.Empty<WeaponDefinition>();
    public WeaponDefinition PrimaryWeapon => CombatLoadout.Primary(Weapons);
    public CombatStats ResolvedCombat => CombatLoadout.Combine(BaseCombat, Weapons);

    void OnValidate()
    {
        maxHealth = Mathf.Max(1f, maxHealth);
        attackDamage = Mathf.Max(0f, attackDamage);
        attackSpeed = Mathf.Max(0f, attackSpeed);
        attackRange = Mathf.Max(0f, attackRange);
        oxygenTank = Mathf.Max(1f, oxygenTank);
    }
}
