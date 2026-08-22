using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Additive combat numbers. Player base and each weapon use this.
/// A held gun fights with <c>origin + that weapon</c>, not the sum of every gun.
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
/// Loadout rules: up to <see cref="MaxWeapons"/> guns, each fighting with
/// <c>player base + that weapon</c>. Author the list on <see cref="PlayerStats"/>.
/// </summary>
public static class CombatLoadout
{
    public const int MaxWeapons = 3;

    public static CombatStats ForWeapon(CombatStats origin, WeaponDefinition weapon)
    {
        return weapon != null ? origin + weapon.Combat : origin;
    }

    public static float MaxRange(CombatStats origin, IReadOnlyList<WeaponDefinition> weapons)
    {
        float range = origin.AttackRange;
        if (weapons == null)
            return range;

        int count = 0;
        for (int i = 0; i < weapons.Count; i++)
        {
            WeaponDefinition weapon = weapons[i];
            if (weapon == null)
                continue;
            range = Mathf.Max(range, ForWeapon(origin, weapon).AttackRange);
            count++;
            if (count >= MaxWeapons)
                break;
        }

        return range;
    }

    public static void CopyClamped(IReadOnlyList<WeaponDefinition> source, List<WeaponDefinition> dest)
    {
        dest.Clear();
        if (source == null)
            return;

        for (int i = 0; i < source.Count; i++)
        {
            if (source[i] == null)
                continue;
            dest.Add(source[i]);
            if (dest.Count >= MaxWeapons)
                return;
        }
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
