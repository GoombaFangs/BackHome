using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Shared ranged attack: muzzle flash, flying bullet, and hit VFX.
/// Put this on every ranged weapon prefab. Optional on-hit procs live on <see cref="WeaponDefinition"/>.
/// </summary>
public class RangedWeapon : EquippedWeapon
{
    const float MuzzleFlashLength = 0.45f;

    [Header("Muzzle")]
    [FormerlySerializedAs("shotPrefab")]
    [SerializeField] GameObject muzzleVFX;
    [FormerlySerializedAs("shotScale")]
    [SerializeField, Min(0.05f)] float muzzleScale = 0.3f;
    [FormerlySerializedAs("lifetime")]
    [SerializeField, Min(0.05f)] float muzzleLifetime = 0.45f;
    [FormerlySerializedAs("emitEuler")]
    [SerializeField] Vector3 muzzleEuler = new Vector3(90f, 0f, 0f);

    [Header("Hit")]
    [SerializeField] GameObject hitVFX;
    [SerializeField, Min(0.05f)] float hitLifetime = 0.6f;

    [Header("Bullets")]
    [SerializeField] GameObject bullets;
    [SerializeField, Min(1f)] float bulletSpeed = 22f;
    [SerializeField, Min(0.05f)] float bulletHitRadius = 0.5f;
    [SerializeField, Min(0.1f)] float bulletLifetime = 1.5f;
    [SerializeField] Vector3 bulletEuler = Vector3.zero;

    [Header("Sky Strike (optional)")]
    [Tooltip("When enabled, the bullet drops from high above the target instead of leaving this weapon's muzzle — like a bolt of lightning striking from the sky.")]
    [SerializeField] bool bulletFromSky = false;
    [Tooltip("Meters above the target the bullet starts falling from.")]
    [SerializeField, Min(0.5f)] float skyDropHeight = 15f;

    [Header("Charge (optional - mortar/turret style weapons)")]
    [Tooltip("Seconds this weapon must charge up before the shot actually fires. 0 fires immediately (default).")]
    [SerializeField, Min(0f)] float chargeTime = 0f;
    [Tooltip("Seconds after firing before this weapon is willing to start charging its next shot.")]
    [SerializeField, Min(0f)] float recoverTime = 0f;
    [Tooltip("Optional VFX played at the muzzle for the whole charge, then removed the instant the shot fires.")]
    [SerializeField] GameObject chargeVFX;

    bool _busy;

    public override bool IsReady => !_busy;

    void OnDisable()
    {
        _busy = false;
    }

    public override void Fire(Creature target, float damage, Vector3 muzzle, Transform muzzleParent, Vector3 knockFrom)
    {
        if (target == null || !target.IsAlive || damage <= 0f)
            return;
        if (_busy)
            return;

        if (chargeTime <= 0f && recoverTime <= 0f)
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
        if (chargeVFX != null)
        {
            chargeFx = Instantiate(chargeVFX, muzzle, Quaternion.identity, muzzleParent);
            chargeFx.name = chargeVFX.name;
        }

        float charge = chargeTime;
        while (charge > 0f)
        {
            charge -= Time.deltaTime;
            yield return null;
        }

        if (chargeFx != null)
            Destroy(chargeFx);

        if (target != null && target.IsAlive)
            FireNow(target, damage, ResolveMuzzle(muzzle), muzzleParent, knockFrom);

        float recover = recoverTime;
        while (recover > 0f)
        {
            recover -= Time.deltaTime;
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

        Vector3 hit = AimPoint(target);

        if (bullets != null && bulletFromSky)
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

        if (bullets != null)
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
        return groundPosition + PlanetUp(groundPosition) * skyDropHeight;
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
        Vector3 up = ResolveUp(origin, dir);
        GameObject instance = Instantiate(
            bullets,
            origin,
            Quaternion.LookRotation(dir, up) * Quaternion.Euler(bulletEuler));
        instance.name = bullets.name;
        StripFlightPhysics(instance);
        PlayBurst(instance);

        RangedBullet projectile = instance.GetComponent<RangedBullet>();
        if (projectile == null)
            projectile = instance.AddComponent<RangedBullet>();

        projectile.Launch(this, target, damage, knockFrom, bulletSpeed, bulletHitRadius, bulletLifetime, bulletEuler);
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
