using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// On-hit proc that bounces a hit from the struck creature to the nearest other creature,
/// repeating for a fixed number of jumps (see <see cref="WeaponChainSettings.MaxJumps"/>).
/// Before every jump the nearest not-yet-hit creature within <see cref="WeaponChainSettings.JumpRadius"/>
/// of the current one is looked up; if none exists the chain stops immediately.
/// </summary>
public static class ChainHitEffect
{
    public static void Trigger(EquippedWeapon sourceWeapon, Creature initialTarget, WeaponChainSettings settings, float hitDamage)
    {
        if (initialTarget == null || settings == null)
            return;
        if (settings.MaxJumps <= 0 || settings.JumpRadius <= 0f)
            return;

        float jumpDamage = hitDamage * settings.JumpDamageMultiplier;
        if (jumpDamage <= 0f)
            return;

        HashSet<Creature> visited = new HashSet<Creature> { initialTarget };
        Creature current = initialTarget;

        for (int i = 0; i < settings.MaxJumps; i++)
        {
            Creature next = FindNearestCreature(current.transform.position, settings.JumpRadius, visited);
            if (next == null)
                break;

            visited.Add(next);
            Vector3 sourcePosition = current.transform.position;
            Vector3 delta = next.transform.position - sourcePosition;
            Vector3 dir = delta.sqrMagnitude > 0.0001f ? delta.normalized : Vector3.forward;

            SpawnBeam(sourceWeapon, settings.BeamVFX, current, next);
            sourceWeapon?.PlayHitEffectVfx(next, dir);
            next.TakeDamage(jumpDamage, sourcePosition);

            if (!next.IsAlive)
                break;

            current = next;
        }
    }

    static Creature FindNearestCreature(Vector3 origin, float radius, HashSet<Creature> excluded)
    {
        Creature[] creatures = Object.FindObjectsByType<Creature>(FindObjectsInactive.Exclude);
        Vector3? planetCenter = SphericalPlanet.Instance != null ? SphericalPlanet.Instance.Center : (Vector3?)null;

        Creature best = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < creatures.Length; i++)
        {
            Creature candidate = creatures[i];
            if (candidate == null || !candidate.IsAlive || excluded.Contains(candidate))
                continue;

            float distance = planetCenter.HasValue
                ? PlanetSurfacePose.GetSurfaceDistance(planetCenter.Value, origin, candidate.transform.position)
                : Vector3.Distance(origin, candidate.transform.position);

            if (distance > radius || distance >= bestDistance)
                continue;

            bestDistance = distance;
            best = candidate;
        }

        return best;
    }

    static void SpawnBeam(EquippedWeapon sourceWeapon, GameObject beamPrefab, Creature from, Creature to)
    {
        if (beamPrefab == null)
            return;

        Vector3 start = sourceWeapon != null ? sourceWeapon.GetAimPoint(from) : from.transform.position;
        Vector3 end = sourceWeapon != null ? sourceWeapon.GetAimPoint(to) : to.transform.position;

        GameObject beam = Object.Instantiate(beamPrefab, start, Quaternion.identity);
        beam.name = beamPrefab.name;

        LaserBeamVfx laser = beam.GetComponent<LaserBeamVfx>();
        if (laser != null)
            laser.Init(start, end);
        else
            Object.Destroy(beam, 1f);
    }
}
