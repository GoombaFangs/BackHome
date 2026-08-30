using UnityEngine;

/// <summary>
/// Catalog entry for any player weapon: identity, held prefab, and combat contribution.
/// Optional on-hit procs (for example Itchy's poison) are chosen per asset via <see cref="HitEffect"/> —
/// leave as None when unused. Only the settings block matching that choice is used; see
/// <see cref="WeaponDotSettings"/>, <see cref="WeaponChainSettings"/> and <see cref="WeaponAreaBlastSettings"/>.
/// Attack behaviour lives on the prefab (<see cref="EquippedWeapon"/> / <see cref="RangedWeapon"/>).
/// </summary>
[CreateAssetMenu(menuName = "BackHome/Weapon Definition", fileName = "Weapon")]
public class WeaponDefinition : ScriptableObject
{
    [SerializeField] string displayName = "Weapon";
    [SerializeField] Sprite icon;
    [SerializeField] GameObject prefab;

    [Header("Combat")]
    [SerializeField, Min(0f)] float attackDamage = 4f;
    [Tooltip("Attacks per second added to the player's base attack speed.")]
    [SerializeField, Min(0f)] float attackSpeed = 4f;
    [Tooltip("World-space meters added to the player's base attack range.")]
    [SerializeField, Min(0f)] float attackRange = 4f;

    [Header("On-Hit Effect")]
    [Tooltip("Optional proc. None for weapons that only deal their shot damage.")]
    [SerializeField] WeaponHitEffectKind hitEffect;
    [SerializeField] WeaponDotSettings dotSettings = new();
    [SerializeField] WeaponChainSettings chainSettings = new();
    [SerializeField] WeaponAreaBlastSettings areaBlastSettings = new();

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public Sprite Icon => icon;
    public GameObject Prefab => prefab;
    public CombatStats Combat => new CombatStats(attackDamage, attackSpeed, attackRange);
    public WeaponHitEffectKind HitEffect => hitEffect;
    public WeaponDotSettings DotSettings => dotSettings;
    public WeaponChainSettings ChainSettings => chainSettings;
    public WeaponAreaBlastSettings AreaBlastSettings => areaBlastSettings;

    /// <summary>
    /// Applies this weapon's on-hit proc, if any. Not called for <see cref="WeaponHitEffectKind.AreaBlast"/> —
    /// that one replaces the direct damage entirely, so <see cref="EquippedWeapon.DealHit"/> handles it up front.
    /// </summary>
    public void ApplyHitEffect(EquippedWeapon sourceWeapon, Creature target, float hitDamage)
    {
        if (target == null || !target.IsAlive || hitEffect == WeaponHitEffectKind.None)
            return;

        if (hitEffect == WeaponHitEffectKind.Dot)
            CreatureAilments.RegisterRepeatedHit(target, dotSettings, hitDamage);
        else if (hitEffect == WeaponHitEffectKind.ChainJump)
            ChainHitEffect.Trigger(sourceWeapon, target, chainSettings, hitDamage);
    }

    void OnValidate()
    {
        attackDamage = Mathf.Max(0f, attackDamage);
        attackSpeed = Mathf.Max(0f, attackSpeed);
        attackRange = Mathf.Max(0f, attackRange);
        dotSettings?.OnValidate();
        chainSettings?.OnValidate();
        areaBlastSettings?.OnValidate();
    }
}
