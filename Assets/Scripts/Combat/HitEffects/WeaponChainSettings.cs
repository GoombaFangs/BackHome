using System;
using UnityEngine;

/// <summary>
/// Tuning for <see cref="WeaponHitEffectKind.ChainJump"/>: the hit bounces from the struck
/// creature to the nearest other creature, repeating up to <see cref="MaxJumps"/> times.
/// Applied at runtime by <see cref="ChainHitEffect"/>.
/// </summary>
[Serializable]
public class WeaponChainSettings
{
    [Tooltip("How many times the hit jumps to the nearest creature before stopping.")]
    [SerializeField, Min(0)] int maxJumps = 4;
    [Tooltip("Meters a jump can travel to reach the next creature. If none is in range, the chain stops.")]
    [SerializeField, Min(0f)] float jumpRadius = 5f;
    [Tooltip("Damage dealt on each chain jump, as a multiplier of the original hit damage.")]
    [SerializeField, Min(0f)] float jumpDamageMultiplier = 1f;
    [Tooltip("Optional beam effect (for example a LaserBeamVfx prefab) drawn between the two creatures on every chain jump.")]
    [SerializeField] GameObject beamVFX;

    public int MaxJumps => maxJumps;
    public float JumpRadius => jumpRadius;
    public float JumpDamageMultiplier => jumpDamageMultiplier;
    public GameObject BeamVFX => beamVFX;

    public void OnValidate()
    {
        maxJumps = Mathf.Max(0, maxJumps);
        jumpRadius = Mathf.Max(0f, jumpRadius);
        jumpDamageMultiplier = Mathf.Max(0f, jumpDamageMultiplier);
    }
}
