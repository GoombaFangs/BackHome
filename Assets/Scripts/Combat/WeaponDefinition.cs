using UnityEngine;

public enum WeaponHitEffectKind
{
    None,
    RepeatedHitDoT,
    ChainJump,
    AreaBlast
}

/// <summary>
/// Catalog entry for any player weapon: identity, held prefab, and combat contribution.
/// Optional on-hit procs (for example Itchy's poison) are chosen per asset — leave as None when unused.
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
    [Tooltip("Hits on the same target, inside the window, before the DoT starts.")]
    [SerializeField, Min(1)] int hitsToApplyDot = 3;
    [Tooltip("Seconds allowed between hits while building the DoT. Waiting longer resets the count.")]
    [SerializeField, Min(0.05f)] float hitWindow = 2f;
    [SerializeField, Min(0.05f)] float dotDuration = 2f;
    [SerializeField, Min(0f)] float dotDamagePerSecond = 10f;
    [SerializeField, Min(0.05f)] float dotTickInterval = 0.5f;
    [SerializeField] GameObject debuffVFX;
    [SerializeField] Vector3 debuffEuler = new Vector3(90f, 0f, 0f);

    [Tooltip("How many times the hit jumps to the nearest creature before stopping.")]
    [SerializeField, Min(0)] int chainJumps = 4;
    [Tooltip("Meters a jump can travel to reach the next creature. If none is in range, the chain stops.")]
    [SerializeField, Min(0f)] float chainRadius = 5f;
    [Tooltip("Damage dealt on each chain jump, as a multiplier of the original hit damage.")]
    [SerializeField, Min(0f)] float chainDamageMultiplier = 1f;
    [Tooltip("Optional beam effect (for example a LaserBeamVfx prefab) drawn between the two creatures on every chain jump.")]
    [SerializeField] GameObject chainVFX;

    [Tooltip("Meters around the impact point that also take damage (surface distance on spherical planets).")]
    [SerializeField, Min(0f)] float areaBlastRadius = 4f;
    [Tooltip("Damage dealt to every creature caught in the blast, as a multiplier of the original hit damage.")]
    [SerializeField, Min(0f)] float areaBlastDamageMultiplier = 1f;
    [Tooltip("Optional explosion VFX spawned once at the impact point.")]
    [SerializeField] GameObject areaBlastVFX;
    [SerializeField, Min(0.1f)] float areaBlastVfxLifetime = 2f;
    [Tooltip("Meters the blast VFX visually covers at its authored scale (1x). The VFX is rescaled so its ring always matches Area Blast Radius exactly — raise Area Blast Radius and the ring grows to match, so anyone shown inside it always takes damage.")]
    [SerializeField, Min(0.1f)] float areaBlastVfxRadius = 4f;
    [Tooltip("Seconds after the VFX starts before damage actually lands — match this to when the VFX's core/impact visually connects, instead of waiting for the whole VFX to finish.")]
    [SerializeField, Min(0f)] float areaBlastDamageDelay = 0.8f;

    public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
    public Sprite Icon => icon;
    public GameObject Prefab => prefab;
    public CombatStats Combat => new CombatStats(attackDamage, attackSpeed, attackRange);
    public WeaponHitEffectKind HitEffect => hitEffect;
    public int HitsToApplyDot => hitsToApplyDot;
    public float HitWindow => hitWindow;
    public float DotDuration => dotDuration;
    public float DotDamagePerSecond => dotDamagePerSecond;
    public float DotTickInterval => dotTickInterval;
    public int ChainJumps => chainJumps;
    public float ChainRadius => chainRadius;
    public float ChainDamageMultiplier => chainDamageMultiplier;
    public GameObject ChainVFX => chainVFX;
    public float AreaBlastRadius => areaBlastRadius;
    public float AreaBlastDamageMultiplier => areaBlastDamageMultiplier;
    public GameObject AreaBlastVFX => areaBlastVFX;
    public float AreaBlastVfxLifetime => areaBlastVfxLifetime;
    public float AreaBlastVfxRadius => areaBlastVfxRadius;
    public float AreaBlastDamageDelay => areaBlastDamageDelay;

    public void ApplyHitEffect(EquippedWeapon sourceWeapon, Creature target, float hitDamage)
    {
        if (target == null || !target.IsAlive || hitEffect == WeaponHitEffectKind.None)
            return;

        if (hitEffect == WeaponHitEffectKind.RepeatedHitDoT)
            CreatureAilments.RegisterRepeatedHit(target, this, hitDamage, debuffVFX, debuffEuler);
        else if (hitEffect == WeaponHitEffectKind.ChainJump)
            ChainHitEffect.Trigger(sourceWeapon, target, this, hitDamage);
        // AreaBlast is handled directly by EquippedWeapon.DealHit — see AreaBlastEffect.
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
        chainJumps = Mathf.Max(0, chainJumps);
        chainRadius = Mathf.Max(0f, chainRadius);
        chainDamageMultiplier = Mathf.Max(0f, chainDamageMultiplier);
        areaBlastRadius = Mathf.Max(0f, areaBlastRadius);
        areaBlastDamageMultiplier = Mathf.Max(0f, areaBlastDamageMultiplier);
        areaBlastVfxLifetime = Mathf.Max(0.1f, areaBlastVfxLifetime);
        areaBlastVfxRadius = Mathf.Max(0.1f, areaBlastVfxRadius);
        areaBlastDamageDelay = Mathf.Max(0f, areaBlastDamageDelay);
    }
}
