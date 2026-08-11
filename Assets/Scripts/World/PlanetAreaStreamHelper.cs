using UnityEngine;

/// <summary>Shared disk-on-sphere checks for area-confined environment streaming (same geometry
/// spirit as <see cref="CreatureSpawner"/>'s spawn-point sampling).</summary>
public static class PlanetAreaStreamHelper
{
    public static bool IsWithinDisk(SphericalPlanet planet, Vector3 anchorPosition, float worldRadius, Vector3 worldPoint)
    {
        if (planet == null || worldRadius <= 0.01f)
            return false;

        Vector3 up = anchorPosition - planet.Center;
        if (up.sqrMagnitude < 0.0001f)
            return false;
        up.Normalize();

        Vector3 planar = Vector3.ProjectOnPlane(worldPoint - anchorPosition, up);
        return planar.sqrMagnitude <= worldRadius * worldRadius;
    }

    public static bool IsPlayerNearArea(SphericalPlanet planet, Vector3 playerPosition, Vector3 areaAnchorPosition, float activationRadius)
    {
        if (planet == null || activationRadius <= 0.01f)
            return true;

        Vector3 up = areaAnchorPosition - planet.Center;
        if (up.sqrMagnitude < 0.0001f)
            return true;
        up.Normalize();

        Vector3 planar = Vector3.ProjectOnPlane(playerPosition - areaAnchorPosition, up);
        return planar.sqrMagnitude <= activationRadius * activationRadius;
    }

    public static bool HasAnyPrefab(PlanetEnvironmentRegionSet.WeightedPrefab[] entries)
    {
        if (entries == null || entries.Length == 0)
            return false;

        for (int i = 0; i < entries.Length; i++)
        {
            if (entries[i] != null && entries[i].prefab != null)
                return true;
        }

        return false;
    }
}
