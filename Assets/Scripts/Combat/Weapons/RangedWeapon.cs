using System.Collections;
using UnityEngine;

/// <summary>
/// Shared ranged attack: muzzle flash, hit VFX, and a pluggable <see cref="WeaponDeliveryKind"/>
/// (instant hitscan or a travelling bullet). Put this on every ranged weapon prefab. Optional
/// on-hit procs live on <see cref="WeaponDefinition"/>.
/// </summary>
public class RangedWeapon : EquippedWeapon
{
    const float MuzzleFlashLength = 0.45f;

    [Header("Muzzle")]
    [SerializeField] GameObject muzzleVFX;
    [SerializeField, Min(0.05f)] float muzzleScale = 0.3f;
    [SerializeField, Min(0.05f)] float muzzleLifetime = 0.45f;
    [SerializeField] Vector3 muzzleEuler = new Vector3(90f, 0f, 0f);

    [Header("Hit")]
    [SerializeField] GameObject hitVFX;
    [SerializeField, Min(0.05f)] float hitLifetime = 0.6f;

    [Header("Delivery")]
    [Tooltip("Hitscan = instant beam straight to the target. Projectile = a physical bullet travels there (see below).")]
    [SerializeField] WeaponDeliveryKind delivery = WeaponDeliveryKind.Hitscan;
    [SerializeField] WeaponProjectileSettings projectile = new();

    [Header("Charge (optional - mortar/turret style weapons)")]
    [SerializeField] WeaponChargeSettings charge = new();

    bool _busy;

    public override bool IsReady => !_busy;

    void OnDisable()
    {
        _busy = false;
    }

    void OnValidate()
    {
        projectile?.OnValidate();
        charge?.OnValidate();
    }

    public override void Fire(Creature target, float damage, Vector3 muzzle, Transform muzzleParent, Vector3 knockFrom)
    {
        if (target == null || !target.IsAlive || damage <= 0f)
            return;
        if (_busy)
            return;

        if (!charge.IsActive)
        {
            FireNow(target, damage, muzzle, muzzleParent, knockFrom);
            return;
        }

        _busy = true;
        StartCoroutine(ChargeThenFire(target, damage, muzzle, muzzleParent, knockFrom));
    }

    IEnumerator ChargeThenFire(Creature target, float damage, Vector3 muzzle, Transform muzzleParent, Vector3 knockFrom)
    {
        GameObject chargeFx = null;
        if (charge.ChargeVFX != null)
        {
            chargeFx = Instantiate(charge.ChargeVFX, muzzle, Quaternion.identity, muzzleParent);
            chargeFx.name = charge.ChargeVFX.name;
        }

        float remainingCharge = charge.ChargeTime;
        while (remainingCharge > 0f)
        {
            remainingCharge -= Time.deltaTime;
            yield return null;
        }

        if (chargeFx != null)
            Destroy(chargeFx);

        if (target != null && target.IsAlive)
            FireNow(target, damage, ResolveMuzzle(muzzle), muzzleParent, knockFrom);

        float remainingRecovery = charge.RecoverTime;
        while (remainingRecovery > 0f)
        {
            remainingRecovery -= Time.deltaTime;
            yield return null;
        }

        _busy = false;
    }

    Vector3 ResolveMuzzle(Vector3 fallback)
    {
        return MuzzleAnchor != null ? MuzzleAnchor.position : fallback;
    }

    void FireNow(Creature target, float damage, Vector3 muzzle, Transform muzzleParent, Vector3 knockFrom)
    {
        if (target == null || !target.IsAlive || damage <= 0f)
            return;

        bool isProjectile = delivery == WeaponDeliveryKind.Projectile && projectile.BulletPrefab != null;
        Vector3 hit = AimPoint(target);

        if (isProjectile && projectile.FireFromSky)
        {
            Vector3 skyOrigin = SkyPoint(target.transform.position);
            Vector3 skyDelta = hit - skyOrigin;
            Vector3 skyDir = skyDelta.sqrMagnitude > 0.0001f ? skyDelta.normalized : -PlanetUp(target.transform.position);
            SpawnBullet(target, damage, skyOrigin, skyDir, knockFrom);
            return;
        }

        Vector3 delta = hit - muzzle;
        float distance = delta.magnitude;
        Vector3 dir = distance > 0.0001f ? delta / distance : transform.forward;

        if (isProjectile)
        {
            PlayMuzzle(muzzle, muzzleParent, dir, Mathf.Min(MuzzleFlashLength, distance));
            SpawnBullet(target, damage, muzzle, dir, knockFrom);
            return;
        }

        PlayMuzzle(muzzle, muzzleParent, dir, distance);
        PlayHit(target, hit, dir);
        DealHit(target, damage, knockFrom);
    }

    static Vector3 PlanetUp(Vector3 position)
    {
        return SphericalPlanet.Instance != null
            ? SphericalPlanet.Instance.GetUpAt(position)
            : Vector3.up;
    }

    Vector3 SkyPoint(Vector3 groundPosition)
    {
        return groundPosition + PlanetUp(groundPosition) * projectile.SkyDropHeight;
    }

    internal Vector3 GetBulletAim(Creature creature)
    {
        return AimPoint(creature);
    }

    internal static Vector3 GetBulletUp(Vector3 origin, Vector3 dir)
    {
        return ResolveUp(origin, dir);
    }

    internal void ResolveBulletHit(Creature target, float damage, Vector3 knockFrom, Vector3 hitPoint, Vector3 dir)
    {
        if (target == null || !target.IsAlive)
            return;

        PlayHit(target, hitPoint, dir);
        DealHit(target, damage, knockFrom);
    }

    void SpawnBullet(Creature target, float damage, Vector3 origin, Vector3 dir, Vector3 knockFrom)
    {
        GameObject bulletPrefab = projectile.BulletPrefab;
        if (bulletPrefab == null)
            return;

        Vector3 up = ResolveUp(origin, dir);
        GameObject instance = Instantiate(
            bulletPrefab,
            origin,
            Quaternion.LookRotation(dir, up) * Quaternion.Euler(projectile.BulletEuler));
        instance.name = bulletPrefab.name;
        StripFlightPhysics(instance);
        PlayBurst(instance);

        RangedBullet bullet = instance.GetComponent<RangedBullet>();
        if (bullet == null)
            bullet = instance.AddComponent<RangedBullet>();

        bullet.Launch(this, target, damage, knockFrom, projectile.BulletSpeed, projectile.BulletHitRadius, projectile.BulletLifetime, projectile.BulletEuler);
    }

    void PlayMuzzle(Vector3 origin, Transform parent, Vector3 dir, float distance)
    {
        if (muzzleVFX == null || distance <= 0.05f)
            return;

        Vector3 up = ResolveUp(origin, dir);
        GameObject fx = Instantiate(
            muzzleVFX,
            origin,
            Quaternion.LookRotation(dir, up) * Quaternion.Euler(muzzleEuler));
        fx.name = muzzleVFX.name;
        fx.transform.localScale = Vector3.one;
        if (parent != null)
            fx.transform.SetParent(parent, true);

        SetBeamLength(fx, dir, distance, muzzleScale);
        PlayBurst(fx);
        Destroy(fx, muzzleLifetime);
    }

    void PlayHit(Creature target, Vector3 position, Vector3 dir)
    {
        if (hitVFX == null)
            return;

        Vector3 up = ResolveUp(position, dir);
        GameObject fx = Instantiate(hitVFX, position, Quaternion.LookRotation(dir, up));
        fx.name = hitVFX.name;
        fx.transform.localScale = Vector3.one;
        if (target != null)
            fx.transform.SetParent(target.transform, true);

        PlayBurst(fx);
        Destroy(fx, hitLifetime);
    }

    protected override void PlayHitVfxCore(Creature target, Vector3 position, Vector3 dir)
    {
        PlayHit(target, position, dir);
    }

    static void StripFlightPhysics(GameObject root)
    {
        Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = false;

        Rigidbody[] bodies = root.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < bodies.Length; i++)
        {
            bodies[i].isKinematic = true;
            bodies[i].useGravity = false;
            bodies[i].detectCollisions = false;
        }
    }

    static void SetBeamLength(GameObject fx, Vector3 dir, float distance, float thickness)
    {
        ParticleSystem[] systems = fx.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem ps = systems[i];
            if (ps.name != "Shoot")
                continue;

            ParticleSystem.MainModule main = ps.main;
            main.startSize3D = true;
            main.startSizeX = thickness;
            main.startSizeY = distance;
            main.startSizeZ = thickness;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.loop = false;

            ps.transform.position = fx.transform.position + dir * (distance * 0.5f);

            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Play(true);
        }
    }

    static void PlayBurst(GameObject fx)
    {
        ParticleSystem[] systems = fx.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem.MainModule main = systems[i].main;
            main.loop = false;
            if (!systems[i].isPlaying)
                systems[i].Play(true);
        }
    }
}
