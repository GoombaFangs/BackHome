using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Replaces a weapon's normal instant damage with a telegraphed area blast: the VFX plays
/// first, and everyone caught in the radius (the original target included) only actually
/// loses health once the VFX's impact moment hits — see <see cref="WeaponDefinition.AreaBlastDamageDelay"/>
/// and <see cref="WeaponDefinition.AreaBlastRadius"/>. The VFX itself spawns slightly ahead of the
/// primary target, along its current movement, so it lands where the target will actually be
/// once that delay elapses instead of where it stood when the shot fired.
/// </summary>
public static class AreaBlastEffect
{
    struct BlastHit
    {
        public Creature Creature;
        public Vector3 KnockFrom;
        public Vector3 Dir;
        public bool PlayHitVfx;
    }

    public static void Trigger(EquippedWeapon sourceWeapon, Creature primaryTarget, WeaponDefinition weapon, float hitDamage, Vector3 knockFrom)
    {
        if (primaryTarget == null || weapon == null)
            return;
        if (weapon.AreaBlastRadius <= 0f)
            return;

        float blastDamage = hitDamage * weapon.AreaBlastDamageMultiplier;
        Vector3 impactPoint = sourceWeapon != null ? sourceWeapon.GetAimPoint(primaryTarget) : primaryTarget.transform.position;

        // The target keeps moving during the delay between the VFX spawning and damage actually
        // landing — lead the blast toward where it's heading so the impact lines up with the target
        // instead of the empty ground it already walked past.
        impactPoint += primaryTarget.Velocity * weapon.AreaBlastDamageDelay;

        GameObject vfx = SpawnBlastVfx(weapon, impactPoint, weapon.AreaBlastRadius);
        float delay = vfx != null ? weapon.AreaBlastDamageDelay : 0f;

        if (blastDamage <= 0f)
            return;

        List<BlastHit> hits = CollectHits(impactPoint, weapon.AreaBlastRadius, primaryTarget, knockFrom);

        if (delay <= 0f || sourceWeapon == null)
        {
            ApplyHits(sourceWeapon, hits, blastDamage);
            return;
        }

        sourceWeapon.StartCoroutine(DelayedApply(sourceWeapon, hits, blastDamage, delay));
    }

    static List<BlastHit> CollectHits(Vector3 impactPoint, float radius, Creature primaryTarget, Vector3 knockFrom)
    {
        List<BlastHit> hits = new List<BlastHit>
        {
            new BlastHit { Creature = primaryTarget, KnockFrom = knockFrom, Dir = Vector3.forward, PlayHitVfx = false }
        };

        Creature[] creatures = Object.FindObjectsByType<Creature>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        Vector3? planetCenter = SphericalPlanet.Instance != null ? SphericalPlanet.Instance.Center : (Vector3?)null;

        for (int i = 0; i < creatures.Length; i++)
        {
            Creature candidate = creatures[i];
            if (candidate == null || !candidate.IsAlive || candidate == primaryTarget)
                continue;

            float distance = planetCenter.HasValue
                ? PlanetSurfacePose.GetSurfaceDistance(planetCenter.Value, impactPoint, candidate.transform.position)
                : Vector3.Distance(impactPoint, candidate.transform.position);

            if (distance > radius)
                continue;

            Vector3 delta = candidate.transform.position - impactPoint;
            Vector3 dir = delta.sqrMagnitude > 0.0001f ? delta.normalized : Vector3.forward;

            hits.Add(new BlastHit { Creature = candidate, KnockFrom = impactPoint, Dir = dir, PlayHitVfx = true });
        }

        return hits;
    }

    static IEnumerator DelayedApply(EquippedWeapon sourceWeapon, List<BlastHit> hits, float damage, float delay)
    {
        yield return new WaitForSeconds(delay);
        ApplyHits(sourceWeapon, hits, damage);
    }

    static void ApplyHits(EquippedWeapon sourceWeapon, List<BlastHit> hits, float damage)
    {
        for (int i = 0; i < hits.Count; i++)
        {
            BlastHit hit = hits[i];
            if (hit.Creature == null || !hit.Creature.IsAlive)
                continue;

            if (hit.PlayHitVfx)
                sourceWeapon?.PlayHitEffectVfx(hit.Creature, hit.Dir);

            hit.Creature.TakeDamage(damage, hit.KnockFrom);
        }
    }

    static GameObject SpawnBlastVfx(WeaponDefinition weapon, Vector3 position, float radius)
    {
        GameObject prefab = weapon.AreaBlastVFX;
        if (prefab == null)
            return null;

        Vector3 up = SphericalPlanet.Instance != null
            ? SphericalPlanet.Instance.GetUpAt(position)
            : Vector3.up;
        Quaternion rotation = Quaternion.FromToRotation(Vector3.up, up);

        GameObject fx = Object.Instantiate(prefab, position, rotation);
        fx.name = prefab.name;

        // Scale the VFX so its ring always matches the actual hit radius — whatever it visually
        // covers is exactly what takes damage, instead of a fixed size unrelated to the radius.
        float referenceRadius = weapon.AreaBlastVfxRadius > 0f ? weapon.AreaBlastVfxRadius : 1f;
        float scale = radius / referenceRadius;
        fx.transform.localScale = Vector3.one * scale;

        ParticleSystem[] systems = fx.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < systems.Length; i++)
        {
            ParticleSystem.MainModule main = systems[i].main;
            main.loop = false;
            if (!systems[i].isPlaying)
                systems[i].Play(true);
        }

        Object.Destroy(fx, weapon.AreaBlastVfxLifetime);
        return fx;
    }
}
