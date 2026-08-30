using System;
using UnityEngine;

/// <summary>
/// Tuning for <see cref="WeaponHitEffectKind.AreaBlast"/>: replaces the weapon's normal instant
/// damage with a telegraphed blast — everyone caught in <see cref="Radius"/> (impact point
/// included) only takes damage once the VFX's impact moment hits, after <see cref="DamageDelay"/>.
/// Applied at runtime by <see cref="AreaBlastEffect"/>.
/// </summary>
[Serializable]
public class WeaponAreaBlastSettings
{
    [Tooltip("Meters around the impact point that also take damage (surface distance on spherical planets).")]
    [SerializeField, Min(0f)] float radius = 4f;
    [Tooltip("Damage dealt to every creature caught in the blast, as a multiplier of the original hit damage.")]
    [SerializeField, Min(0f)] float damageMultiplier = 1f;
    [Tooltip("Optional explosion VFX spawned once at the impact point.")]
    [SerializeField] GameObject blastVFX;
    [SerializeField, Min(0.1f)] float vfxLifetime = 2f;
    [Tooltip("Meters the blast VFX visually covers at its authored scale (1x). The VFX is rescaled so its ring always matches Radius exactly — raise Radius and the ring grows to match, so anyone shown inside it always takes damage.")]
    [SerializeField, Min(0.1f)] float vfxRadius = 4f;
    [Tooltip("Seconds after the VFX starts before damage actually lands — match this to when the VFX's core/impact visually connects, instead of waiting for the whole VFX to finish.")]
    [SerializeField, Min(0f)] float damageDelay = 0.8f;

    public float Radius => radius;
    public float DamageMultiplier => damageMultiplier;
    public GameObject BlastVFX => blastVFX;
    public float VfxLifetime => vfxLifetime;
    public float VfxRadius => vfxRadius;
    public float DamageDelay => damageDelay;

    public void OnValidate()
    {
        radius = Mathf.Max(0f, radius);
        damageMultiplier = Mathf.Max(0f, damageMultiplier);
        vfxLifetime = Mathf.Max(0.1f, vfxLifetime);
        vfxRadius = Mathf.Max(0.1f, vfxRadius);
        damageDelay = Mathf.Max(0f, damageDelay);
    }
}
