using UnityEngine;

/// <summary>
/// Kinematic north/south bounds for the Nyxara walk band. When the study work plan is on,
/// the player is kept between the authored lips in code — no physics walls on those edges,
/// so the capsule cannot snag on cubes or rock slopes.
/// </summary>
public static class NyxaraRouteBounds
{
    public const float WalkSkinDegrees = 0.55f;
    public const float ComfortInsideDegrees = 1.4f;
    public const float OffRoutePastDegrees = 2.6f;
    public const float PitOffRouteUnits = 2.8f;

    public static bool IsActive(PlanetTileMap tiles)
    {
        return tiles != null && NyxaraA2CliffProfile.PlanApplies(tiles.WorkPlan);
    }

    /// <summary>
    /// Border cubes and leftover rock colliders. Ignored by the walker while the route
    /// band is active so those colliders cannot pin the player to a seam.
    /// </summary>
    public static bool ShouldIgnorePhysicsWall(Collider col, PlanetTileMap tiles)
    {
        if (!IsActive(tiles) || col == null)
            return false;
        if (col.GetComponent<NyxaraA2NorthRidge>() != null)
            return true;
        if (col.GetComponent<NyxaraA2SouthCliff>() != null)
            return true;

        Transform t = col.transform;
        while (t != null)
        {
            if (t.name == "Borders")
                return true;
            if (t.GetComponent<SphericalPlanet>() != null)
                break;
            t = t.parent;
        }

        return false;
    }

    /// <summary>
    /// Ignore physical contact with Borders / leftover rock colliders so a kinematic body
    /// cannot snag. Trigger volumes on Borders stay active. Call again with IsActive false
    /// to restore.
    /// </summary>
    public static void ApplyIgnoreCollisions(Collider player, SphericalPlanet planet, PlanetTileMap tiles)
    {
        if (player == null || planet == null)
            return;

        bool ignore = IsActive(tiles);
        Transform borders = planet.transform.Find("Borders");
        if (borders != null)
        {
            Collider[] boxes = borders.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < boxes.Length; i++)
            {
                Collider col = boxes[i];
                if (col == null || col == player || col.isTrigger)
                    continue;
                Physics.IgnoreCollision(player, col, ignore);
            }
        }

        NyxaraA2NorthRidge ridge = planet.GetComponentInChildren<NyxaraA2NorthRidge>(true);
        if (ridge != null)
            IgnoreIfPresent(player, ridge.GetComponent<Collider>(), ignore);
        NyxaraA2SouthCliff cliff = planet.GetComponentInChildren<NyxaraA2SouthCliff>(true);
        if (cliff != null)
            IgnoreIfPresent(player, cliff.GetComponent<Collider>(), ignore);
    }

    static void IgnoreIfPresent(Collider player, Collider other, bool ignore)
    {
        if (other == null || other == player)
            return;
        Physics.IgnoreCollision(player, other, ignore);
    }

    public static Vector3 FilterMove(
        SphericalPlanet planet,
        PlanetTileMap tiles,
        Vector3 from,
        Vector3 delta,
        float extraHover)
    {
        if (delta.sqrMagnitude < 0.0000001f || !IsActive(tiles) || planet == null)
            return delta;

        Vector3 to = from + delta;
        Vector3 clamped = ClampPosition(planet, tiles, to, extraHover);
        return clamped - from;
    }

    public static Vector3 ClampPosition(
        SphericalPlanet planet,
        PlanetTileMap tiles,
        Vector3 worldPosition,
        float extraHover)
    {
        if (planet == null || tiles == null || !IsActive(tiles))
            return worldPosition;

        Vector3 local = planet.WorldToPlanetLocal.MultiplyPoint3x4(worldPosition);
        if (local.sqrMagnitude < 0.0001f)
            return worldPosition;

        PlanetTileMap.DirectionToStudyLonLat(local, out float lon, out float lat);
        if (!TryGetWalkBand(tiles.WorkPlan, lon, out float latMax, out float latMin))
            return worldPosition;

        float clampedLat = Mathf.Clamp(lat, latMin, latMax);
        if (Mathf.Abs(clampedLat - lat) < 0.0001f)
            return worldPosition;

        Vector3 dir = PlanetTileMap.StudyLonLatToDirection(lon, clampedLat);
        float walk = tiles.GetWalkSurfaceRadius(dir) + Mathf.Max(0f, extraHover);
        return planet.PlanetLocalToWorld.MultiplyPoint3x4(dir * walk);
    }

    public static bool IsOffRoute(SphericalPlanet planet, PlanetTileMap tiles, Vector3 worldPosition)
    {
        if (planet == null || tiles == null || !IsActive(tiles))
            return false;

        Vector3 local = planet.WorldToPlanetLocal.MultiplyPoint3x4(worldPosition);
        if (local.sqrMagnitude < 0.0001f)
            return false;

        PlanetTileMap.DirectionToStudyLonLat(local, out float lon, out float lat);
        Vector3 dir = local.normalized;
        if (local.magnitude < tiles.GetWalkSurfaceRadius(dir) - PitOffRouteUnits)
            return true;

        PlanetTileMap.TerrainWorkPlan plan = tiles.WorkPlan;
        bool wrap = plan.coverFullRing;
        float northP = NorthPresence(plan, lon);
        float southP = SouthPresence(plan, lon);

        if (northP > 0.12f &&
            NyxaraA2CliffProfile.TrySampleLipLatitude(
                plan.northLipStudyLongitudes, plan.northLipLatitudes, lon, wrap, out float northLip) &&
            lat > northLip + OffRoutePastDegrees)
            return true;

        if (southP > 0.12f &&
            NyxaraA2CliffProfile.TrySampleLipLatitude(
                plan.cliffLipStudyLongitudes, plan.cliffLipLatitudes, lon, wrap, out float southLip) &&
            lat < southLip - OffRoutePastDegrees)
            return true;

        return false;
    }

    public static bool IsComfortable(SphericalPlanet planet, PlanetTileMap tiles, Vector3 worldPosition)
    {
        if (planet == null || tiles == null || !IsActive(tiles))
            return false;

        Vector3 local = planet.WorldToPlanetLocal.MultiplyPoint3x4(worldPosition);
        if (local.sqrMagnitude < 0.0001f)
            return false;

        PlanetTileMap.DirectionToStudyLonLat(local, out float lon, out float lat);
        if (!TryGetWalkBand(tiles.WorkPlan, lon, out float latMax, out float latMin))
            return true;

        float extra = ComfortInsideDegrees - WalkSkinDegrees;
        return lat <= latMax - extra && lat >= latMin + extra;
    }

    public static bool TryRecover(
        SphericalPlanet planet,
        PlanetTileMap tiles,
        Vector3 worldPosition,
        float extraHover,
        bool hasAnchor,
        Vector3 anchor,
        out Vector3 recovered)
    {
        recovered = worldPosition;
        if (!IsOffRoute(planet, tiles, worldPosition))
            return false;

        if (hasAnchor && IsComfortable(planet, tiles, anchor))
        {
            recovered = anchor;
            return true;
        }

        return TrySnapToCenter(planet, tiles, worldPosition, extraHover, out recovered);
    }

    public static bool TrySnapToCenter(
        SphericalPlanet planet,
        PlanetTileMap tiles,
        Vector3 worldPosition,
        float extraHover,
        out Vector3 center)
    {
        center = worldPosition;
        if (planet == null || tiles == null || !IsActive(tiles))
            return false;

        Vector3 local = planet.WorldToPlanetLocal.MultiplyPoint3x4(worldPosition);
        if (local.sqrMagnitude < 0.0001f)
            return false;

        PlanetTileMap.DirectionToStudyLonLat(local, out float lon, out _);
        if (!TryGetWalkBand(tiles.WorkPlan, lon, out float latMax, out float latMin))
            return false;

        float lat = 0.5f * (latMax + latMin);
        Vector3 dir = PlanetTileMap.StudyLonLatToDirection(lon, lat);
        float walk = tiles.GetWalkSurfaceRadius(dir) + Mathf.Max(0f, extraHover);
        center = planet.PlanetLocalToWorld.MultiplyPoint3x4(dir * walk);
        return true;
    }

    public static bool TryGetWalkBand(
        PlanetTileMap.TerrainWorkPlan plan,
        float studyLon,
        out float latMax,
        out float latMin)
    {
        latMax = 90f;
        latMin = -90f;
        if (plan == null)
            return false;

        bool wrap = plan.coverFullRing;
        bool any = false;
        if (NorthPresence(plan, studyLon) > 0.12f &&
            NyxaraA2CliffProfile.TrySampleLipLatitude(
                plan.northLipStudyLongitudes, plan.northLipLatitudes, studyLon, wrap, out float northLip))
        {
            latMax = northLip - WalkSkinDegrees;
            any = true;
        }

        if (SouthPresence(plan, studyLon) > 0.12f &&
            NyxaraA2CliffProfile.TrySampleLipLatitude(
                plan.cliffLipStudyLongitudes, plan.cliffLipLatitudes, studyLon, wrap, out float southLip))
        {
            latMin = southLip + WalkSkinDegrees;
            any = true;
        }

        if (any && latMax < latMin)
        {
            float mid = 0.5f * (latMax + latMin);
            latMax = mid;
            latMin = mid;
        }

        return any;
    }

    static float NorthPresence(PlanetTileMap.TerrainWorkPlan plan, float studyLon)
    {
        if (plan.coverFullRing)
            return NyxaraA2CliffProfile.LipPresence(
                plan.northLipStudyLongitudes, plan.northLipLatitudes, studyLon, true);
        return NyxaraA2CliffProfile.LonEdgeFade(studyLon, plan.studyLongitudeMin, plan.studyLongitudeMax);
    }

    static float SouthPresence(PlanetTileMap.TerrainWorkPlan plan, float studyLon)
    {
        if (plan.coverFullRing)
            return NyxaraA2CliffProfile.LipPresence(
                plan.cliffLipStudyLongitudes, plan.cliffLipLatitudes, studyLon, true);
        return NorthPresence(plan, studyLon);
    }
}
