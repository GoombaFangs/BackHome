using System;
using UnityEngine;

/// <summary>
/// Tuning for <see cref="WeaponDeliveryKind.Projectile"/>: a physical bullet is spawned and
/// travels to the target instead of an instant hitscan beam. Only used while the owning
/// <see cref="RangedWeapon"/>'s delivery is set to Projectile.
/// </summary>
[Serializable]
public class WeaponProjectileSettings
{
    [SerializeField] GameObject bulletPrefab;
    [SerializeField, Min(1f)] float bulletSpeed = 22f;
    [SerializeField, Min(0.05f)] float bulletHitRadius = 0.5f;
    [SerializeField, Min(0.1f)] float bulletLifetime = 1.5f;
    [SerializeField] Vector3 bulletEuler = Vector3.zero;

    [Header("Sky Strike (optional)")]
    [Tooltip("When enabled, the bullet drops from high above the target instead of leaving this weapon's muzzle — like a bolt of lightning striking from the sky.")]
    [SerializeField] bool fireFromSky;
    [Tooltip("Meters above the target the bullet starts falling from.")]
    [SerializeField, Min(0.5f)] float skyDropHeight = 15f;

    public GameObject BulletPrefab => bulletPrefab;
    public float BulletSpeed => bulletSpeed;
    public float BulletHitRadius => bulletHitRadius;
    public float BulletLifetime => bulletLifetime;
    public Vector3 BulletEuler => bulletEuler;
    public bool FireFromSky => fireFromSky;
    public float SkyDropHeight => skyDropHeight;

    public void OnValidate()
    {
        bulletSpeed = Mathf.Max(1f, bulletSpeed);
        bulletHitRadius = Mathf.Max(0.05f, bulletHitRadius);
        bulletLifetime = Mathf.Max(0.1f, bulletLifetime);
        skyDropHeight = Mathf.Max(0.5f, skyDropHeight);
    }
}
