using System;
using UnityEngine;

/// <summary>
/// Tuning for <see cref="WeaponHitEffectKind.Dot"/>: repeated hits on the same target build up
/// a stack, and once enough land inside the window a damage-over-time debuff starts.
/// Applied at runtime by <see cref="CreatureAilments"/>.
/// </summary>
[Serializable]
public class WeaponDotSettings
{
    [Tooltip("Hits on the same target, inside the window, before the DoT starts.")]
    [SerializeField, Min(1)] int hitsToApply = 3;
    [Tooltip("Seconds allowed between hits while building the DoT. Waiting longer resets the count.")]
    [SerializeField, Min(0.05f)] float hitWindow = 2f;
    [SerializeField, Min(0.05f)] float duration = 2f;
    [SerializeField, Min(0f)] float damagePerSecond = 10f;
    [SerializeField, Min(0.05f)] float tickInterval = 0.5f;
    [SerializeField] GameObject debuffVFX;
    [SerializeField] Vector3 debuffEuler = new Vector3(90f, 0f, 0f);

    public int HitsToApply => hitsToApply;
    public float HitWindow => hitWindow;
    public float Duration => duration;
    public float DamagePerSecond => damagePerSecond;
    public float TickInterval => tickInterval;
    public GameObject DebuffVFX => debuffVFX;
    public Vector3 DebuffEuler => debuffEuler;

    public void OnValidate()
    {
        hitsToApply = Mathf.Max(1, hitsToApply);
        hitWindow = Mathf.Max(0.05f, hitWindow);
        duration = Mathf.Max(0.05f, duration);
        damagePerSecond = Mathf.Max(0f, damagePerSecond);
        tickInterval = Mathf.Max(0.05f, tickInterval);
    }
}
