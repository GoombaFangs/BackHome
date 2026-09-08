using UnityEngine;

/// <summary>
/// Helpers for Nyxara ground colliders. Study walking uses <see cref="NyxaraRouteBounds"/>
/// (kinematic band). Lip/ridge/cliff MeshColliders stay disabled. Border boxes stay in
/// the prefab for layout compare; the study walker ignores them in Play.
/// </summary>
public static class NyxaraTerrainCollision
{
    public const string GroundLayerName = "Ground";
    public const float ComfortInsideDegrees = 1.8f;
    public const float OffRoutePastDegrees = 1.1f;
    public const float PitOffRouteUnits = 2.8f;

    public enum RouteState
    {
        Open = 0,
        Comfort = 1,
        NearLip = 2,
        OffRoute = 3
    }

    public static int GroundLayerIndex
    {
        get
        {
            int layer = LayerMask.NameToLayer(GroundLayerName);
            return layer >= 0 ? layer : 3;
        }
    }

    public static void ApplyBlockingMesh(GameObject go, Mesh mesh)
    {
        if (go == null)
            return;

        go.layer = GroundLayerIndex;
        var collider = go.GetComponent<MeshCollider>();
        if (mesh == null)
        {
            if (collider != null)
                collider.enabled = false;
            return;
        }

        if (collider == null)
            collider = go.AddComponent<MeshCollider>();
        collider.convex = false;
        collider.isTrigger = false;
        collider.sharedMesh = null;
        collider.sharedMesh = mesh;
        collider.enabled = true;
    }

    public static void ClearBlockingMesh(GameObject go)
    {
        if (go == null)
            return;
        var collider = go.GetComponent<MeshCollider>();
        if (collider != null)
        {
            collider.sharedMesh = null;
            collider.enabled = false;
        }
    }

    /// <summary>Invisible lip curtains and authored Border cubes. Not the sloped rock meshes.</summary>
    public static bool IsBlockingWall(Collider col)
    {
        if (col == null)
            return false;

        if (col.GetComponent<NyxaraA2LipWalls>() != null)
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

    public static RouteState GetRouteState(
        SphericalPlanet planet,
        PlanetTileMap tiles,
        Vector3 worldPosition)
    {
        if (planet == null || tiles == null)
            return RouteState.Open;

        PlanetTileMap.TerrainWorkPlan plan = tiles.WorkPlan;
        if (!NyxaraA2CliffProfile.PlanApplies(plan))
            return RouteState.Open;

        Vector3 local = planet.WorldToPlanetLocal.MultiplyPoint3x4(worldPosition);
        if (local.sqrMagnitude < 0.0001f)
            return RouteState.Open;

        PlanetTileMap.DirectionToStudyLonLat(local, out float studyLon, out float studyLat);
        bool wrap = plan.coverFullRing;
        float northPresence = wrap
            ? NyxaraA2CliffProfile.LipPresence(
                plan.northLipStudyLongitudes, plan.northLipLatitudes, studyLon, true)
            : NyxaraA2CliffProfile.LonEdgeFade(studyLon, plan.studyLongitudeMin, plan.studyLongitudeMax);
        float southPresence = wrap
            ? NyxaraA2CliffProfile.LipPresence(
                plan.cliffLipStudyLongitudes, plan.cliffLipLatitudes, studyLon, true)
            : northPresence;

        Vector3 dir = local.normalized;
        float walk = tiles.GetWalkSurfaceRadius(dir);
        if (local.magnitude < walk - PitOffRouteUnits)
            return RouteState.OffRoute;

        bool near = false;
        if (northPresence > 0.12f &&
            NyxaraA2CliffProfile.TrySampleLipLatitude(
                plan.northLipStudyLongitudes, plan.northLipLatitudes, studyLon, wrap, out float northLip))
        {
            if (studyLat > northLip + OffRoutePastDegrees)
                return RouteState.OffRoute;
            if (studyLat > northLip - ComfortInsideDegrees)
                near = true;
        }

        if (southPresence > 0.12f &&
            NyxaraA2CliffProfile.TrySampleLipLatitude(
                plan.cliffLipStudyLongitudes, plan.cliffLipLatitudes, studyLon, wrap, out float southLip))
        {
            if (studyLat < southLip - OffRoutePastDegrees)
                return RouteState.OffRoute;
            if (studyLat < southLip + ComfortInsideDegrees)
                near = true;
        }

        if (northPresence <= 0.12f && southPresence <= 0.12f)
            return RouteState.Open;

        return near ? RouteState.NearLip : RouteState.Comfort;
    }

    public static bool TryRecoverOffRoute(
        SphericalPlanet planet,
        PlanetTileMap tiles,
        Vector3 worldPosition,
        float extraHover,
        bool hasAnchor,
        Vector3 anchor,
        out Vector3 recovered)
    {
        recovered = worldPosition;
        if (GetRouteState(planet, tiles, worldPosition) != RouteState.OffRoute)
            return false;

        if (hasAnchor && GetRouteState(planet, tiles, anchor) == RouteState.Comfort)
        {
            recovered = anchor;
            return true;
        }

        return TrySnapToCorridorCenter(planet, tiles, worldPosition, extraHover, out recovered);
    }

    public static bool TrySnapToCorridorCenter(
        SphericalPlanet planet,
        PlanetTileMap tiles,
        Vector3 worldPosition,
        float extraHover,
        out Vector3 clampedPosition)
    {
        clampedPosition = worldPosition;
        if (planet == null || tiles == null)
            return false;

        PlanetTileMap.TerrainWorkPlan plan = tiles.WorkPlan;
        if (!NyxaraA2CliffProfile.PlanApplies(plan))
            return false;

        Vector3 local = planet.WorldToPlanetLocal.MultiplyPoint3x4(worldPosition);
        if (local.sqrMagnitude < 0.0001f)
            return false;

        PlanetTileMap.DirectionToStudyLonLat(local, out float studyLon, out _);
        bool wrap = plan.coverFullRing;
        float northPresence = wrap
            ? NyxaraA2CliffProfile.LipPresence(
                plan.northLipStudyLongitudes, plan.northLipLatitudes, studyLon, true)
            : NyxaraA2CliffProfile.LonEdgeFade(studyLon, plan.studyLongitudeMin, plan.studyLongitudeMax);
        float southPresence = wrap
            ? NyxaraA2CliffProfile.LipPresence(
                plan.cliffLipStudyLongitudes, plan.cliffLipLatitudes, studyLon, true)
            : northPresence;

        float northLip = 90f;
        float southLip = -90f;
        bool haveN = northPresence > 0.12f &&
                     NyxaraA2CliffProfile.TrySampleLipLatitude(
                         plan.northLipStudyLongitudes, plan.northLipLatitudes, studyLon, wrap, out northLip);
        bool haveS = southPresence > 0.12f &&
                     NyxaraA2CliffProfile.TrySampleLipLatitude(
                         plan.cliffLipStudyLongitudes, plan.cliffLipLatitudes, studyLon, wrap, out southLip);
        if (!haveN && !haveS)
            return false;

        float lat;
        if (haveN && haveS)
            lat = 0.5f * (northLip + southLip);
        else if (haveN)
            lat = northLip - ComfortInsideDegrees;
        else
            lat = southLip + ComfortInsideDegrees;

        Vector3 dir = PlanetTileMap.StudyLonLatToDirection(studyLon, lat);
        float walk = tiles.GetWalkSurfaceRadius(dir) + Mathf.Max(0f, extraHover);
        clampedPosition = planet.PlanetLocalToWorld.MultiplyPoint3x4(dir * walk);
        return true;
    }
}
