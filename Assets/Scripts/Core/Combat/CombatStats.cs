using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Additive combat numbers. Player base, each weapon, and the resolved loadout all use this.
/// Future bonuses (armor, relics, buffs) can add the same way: <c>origin + extra</c>.
/// </summary>
[Serializable]
public struct CombatStats
{
    [SerializeField] float attackDamage;
    [SerializeField] float attackSpeed;
    [SerializeField] float attackRange;

    public float AttackDamage => attackDamage;
    public float AttackSpeed => attackSpeed;
    public float AttackRange => attackRange;

    public static CombatStats Zero => default;

    public CombatStats(float attackDamage, float attackSpeed, float attackRange)
    {
        this.attackDamage = attackDamage;
        this.attackSpeed = attackSpeed;
        this.attackRange = attackRange;
    }

    public static CombatStats operator +(CombatStats a, CombatStats b)
    {
        return new CombatStats(
            a.attackDamage + b.attackDamage,
            a.attackSpeed + b.attackSpeed,
            a.attackRange + b.attackRange);
    }
}

/// <summary>
/// Resolves a loadout: <c>base combat + every weapon in the list</c>.
/// This is the only combat-stat calculation — author the list on <see cref="PlayerStats"/>.
/// </summary>
public static class CombatLoadout
{
    public static CombatStats Combine(CombatStats origin, IReadOnlyList<WeaponDefinition> weapons)
    {
        CombatStats total = origin;
        if (weapons == null)
            return total;

        for (int i = 0; i < weapons.Count; i++)
        {
            WeaponDefinition weapon = weapons[i];
            if (weapon != null)
                total += weapon.Combat;
        }

        return total;
    }

    public static WeaponDefinition Primary(IReadOnlyList<WeaponDefinition> weapons)
    {
        if (weapons == null)
            return null;

        for (int i = 0; i < weapons.Count; i++)
        {
            if (weapons[i] != null)
                return weapons[i];
        }

        return null;
    }
}
