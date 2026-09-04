using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marks a world position where streamed planet environment (grass, trees, rocks) must not appear.
/// Attach to portals — the cleared disk defaults to <see cref="PortalPlayerSpawn.SpawnRadius"/> plus
/// <see cref="clearanceMargin"/> so the landing site stays open for the portal mesh and the player
/// spawn scatter.
/// </summary>
[DisallowMultipleComponent]
public class PlanetEnvironmentExclusionZone : MonoBehaviour
{
    [Tooltip("Extra world-space clearance beyond the player spawn radius and portal trigger.")]
    [SerializeField, Min(0f)] float clearanceMargin = 3f;

    [Tooltip("When enabled, the exclusion disk always covers PortalPlayerSpawn's configured radius.")]
    [SerializeField] bool includePlayerSpawnRadius = true;

    public float EffectiveRadius
    {
        get
        {
            float radius = clearanceMargin;
            if (includePlayerSpawnRadius)
            {
                PortalPlayerSpawn spawn = GetComponent<PortalPlayerSpawn>();
                if (spawn != null)
                    radius += spawn.SpawnRadius;
            }

            return radius;
        }
    }

    void OnEnable() => PlanetEnvironmentExclusion.Register(this);

    void OnDisable() => PlanetEnvironmentExclusion.Unregister(this);

#if UNITY_EDITOR
    void OnValidate() => PlanetEnvironmentExclusion.NotifyChanged();
#endif
}

/// <summary>Registry queried by the grass/tree/rock streamers to skip spawning inside landing zones.</summary>
public static class PlanetEnvironmentExclusion
{
    static readonly List<PlanetEnvironmentExclusionZone> s_Zones = new();

    public static event Action Changed;

    public static void Register(PlanetEnvironmentExclusionZone zone)
    {
        if (zone == null || s_Zones.Contains(zone))
            return;

        s_Zones.Add(zone);
        NotifyChanged();
    }

    public static void Unregister(PlanetEnvironmentExclusionZone zone)
    {
        if (zone == null || !s_Zones.Remove(zone))
            return;

        NotifyChanged();
    }

    public static void NotifyChanged() => Changed?.Invoke();

    public static bool IsExcluded(SphericalPlanet planet, Vector3 worldPoint)
    {
        if (planet == null || s_Zones.Count == 0)
            return false;

        Vector3 center = planet.Center;
        for (int i = s_Zones.Count - 1; i >= 0; i--)
        {
            PlanetEnvironmentExclusionZone zone = s_Zones[i];
            if (zone == null)
            {
                s_Zones.RemoveAt(i);
                continue;
            }

            if (!zone.isActiveAndEnabled)
                continue;

            float surfaceDistance = PlanetSurfacePose.GetSurfaceDistance(center, zone.transform.position, worldPoint);
            if (surfaceDistance <= zone.EffectiveRadius)
                return true;
        }

        return false;
    }
}
