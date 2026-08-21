using UnityEngine;

public enum WeaponHitEffectKind
{
    None,
    RepeatedHitDoT
}

/// <summary>
/// Catalog entry for a player weapon: identity, held prefab, combat contribution, and on-hit effect.
/// Drop this asset into <see cref="PlayerStats"/>.<c>Weapons</c> to include it in resolved combat.
/// Attack behaviour lives on the prefab (<see cref="EquippedWeapon"/>).
/// </summary>
[CreateAssetMenu(menuName = "BackHome/Weapon Definition", fileName = "Weapon_")]
public class WeaponDefinition : ScriptableObject
{
    [SerializeField] string displayName = "Weapon";
    [SerializeField] string archetype = "Weapon";
    [SerializeField] GameObject prefab;

    [Header("Combat")]
    [SerializeField, Min(0f)] float attackDamage = 4f;
    [Tooltip("Attacks per second added to the player's base attack speed.")]
    [SerializeField, Min(0f)] float attackSpeed = 4f;
    [Tooltip("World-space meters added to the player's base attack range.")]
    [SerializeField, Min(0f)] float attackRange = 4f;

    [Header("Special Effect")]
    [SerializeField] WeaponHitEffectKind hitEffect;
    [Tooltip("Hits on the same target, inside the window, before the DoT starts.")]
    [SerializeField, Min(1)] int hitsToApplyDot = 3;
    [Tooltip("Seconds allowed between hits while building the DoT. Waiting longer resets the count.")]
    [SerializeField, Min(0.05f)] float hitWindow = 2f;
    [SerializeField, Min(0.05f)] float dotDuration = 2f;
    [SerializeField, Min(0f)] float dotDamagePerSecond = 10f;
    [SerializeField, Min(0.05f)] float dotTickInterval = 0.5f;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public string Archetype => string.IsNullOrWhiteSpace(archetype) ? "Weapon" : archetype;
    public GameObject Prefab => prefab;
    public CombatStats Combat => new CombatStats(attackDamage, attackSpeed, attackRange);
    public WeaponHitEffectKind HitEffect => hitEffect;
    public int HitsToApplyDot => hitsToApplyDot;
    public float HitWindow => hitWindow;
    public float DotDuration => dotDuration;
    public float DotDamagePerSecond => dotDamagePerSecond;
    public float DotTickInterval => dotTickInterval;

    public void ApplyHitEffect(Creature target, float hitDamage, GameObject debuffVfx = null, Vector3 debuffEuler = default)
    {
        if (target == null || !target.IsAlive || hitEffect == WeaponHitEffectKind.None)
            return;

        if (hitEffect == WeaponHitEffectKind.RepeatedHitDoT)
            CreatureAilments.RegisterRepeatedHit(target, this, hitDamage, debuffVfx, debuffEuler);
    }

    void OnValidate()
    {
        attackDamage = Mathf.Max(0f, attackDamage);
        attackSpeed = Mathf.Max(0f, attackSpeed);
        attackRange = Mathf.Max(0f, attackRange);
        hitsToApplyDot = Mathf.Max(1, hitsToApplyDot);
        hitWindow = Mathf.Max(0.05f, hitWindow);
        dotDuration = Mathf.Max(0.05f, dotDuration);
        dotDamagePerSecond = Mathf.Max(0f, dotDamagePerSecond);
        dotTickInterval = Mathf.Max(0.05f, dotTickInterval);
    }
}
