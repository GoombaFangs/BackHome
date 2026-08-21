using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Itchy rapid fire: lives on the Itchy prefab. Muzzle VFX stretches from the gun to the
/// creature; Hit VFX plays at the impact; Debuff VFX is the poison aura.
/// </summary>
public class ItchyWeapon : EquippedWeapon
{
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

    [Header("Debuff")]
    [SerializeField] GameObject debuffVFX;
    [SerializeField] Vector3 debuffEuler = new Vector3(90f, 0f, 0f);

    public override void Fire(Creature target, float damage, Vector3 muzzle, Transform muzzleParent, Vector3 knockFrom)
    {
        if (target == null || !target.IsAlive || damage <= 0f)
            return;

        Vector3 hit = AimPoint(target);
        Vector3 delta = hit - muzzle;
        float distance = delta.magnitude;
        Vector3 dir = distance > 0.0001f ? delta / distance : transform.forward;

        PlayMuzzle(muzzle, muzzleParent, dir, distance);
        PlayHit(target, hit, dir);
        DealHit(target, damage, knockFrom, debuffVFX, debuffEuler);
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
