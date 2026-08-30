using System;
using UnityEngine;

/// <summary>
/// Optional pre-fire charge-up and post-fire recovery for mortar/turret-style weapons.
/// Independent of <see cref="WeaponDeliveryKind"/> — leave both times at 0 to fire instantly
/// with no recovery, which is the default for most weapons.
/// </summary>
[Serializable]
public class WeaponChargeSettings
{
    [Tooltip("Seconds this weapon must charge up before the shot actually fires. 0 fires immediately (default).")]
    [SerializeField, Min(0f)] float chargeTime;
    [Tooltip("Seconds after firing before this weapon is willing to start charging its next shot.")]
    [SerializeField, Min(0f)] float recoverTime;
    [Tooltip("Optional VFX played at the muzzle for the whole charge, then removed the instant the shot fires.")]
    [SerializeField] GameObject chargeVFX;

    public float ChargeTime => chargeTime;
    public float RecoverTime => recoverTime;
    public GameObject ChargeVFX => chargeVFX;
    public bool IsActive => chargeTime > 0f || recoverTime > 0f;

    public void OnValidate()
    {
        chargeTime = Mathf.Max(0f, chargeTime);
        recoverTime = Mathf.Max(0f, recoverTime);
    }
}
