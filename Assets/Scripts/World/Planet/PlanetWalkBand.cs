using UnityEngine;

/// <summary>
/// Leftover Borders cubes are disabled in Play so they cannot trap the player.
/// The tile mesh owns the walk surface; latitude is not clamped to Borders.
/// </summary>
public static class PlanetWalkBand
{
    public const float WalkSkinDegrees = 0.28f;
    public const float ComfortInsideDegrees = 1.4f;
    public const float OffRoutePastDegrees = 1.4f;

    static readonly Collider[] TrapOverlap = new Collider[24];

    public static bool IsActive(SphericalPlanet planet)
    {
        return false;
    }

    public static bool IsActive(PlanetTileMap tiles)
    {
        return tiles != null && IsActive(tiles.GetComponent<SphericalPlanet>());
    }

    public static bool ShouldIgnorePhysicsWall(Collider col, SphericalPlanet planet)
    {
        return PlanetBorders.IsBorderCollider(col);
    }

    public static bool IsInvisibleTrap(Collider col, SphericalPlanet planet)
    {
        if (col == null || col.isTrigger)
            return false;
        if (ShouldIgnorePhysicsWall(col, planet))
            return true;
        if (!PlanetBorders.IsBorderCollider(col))
            return false;
        var renderer = col.GetComponent<MeshRenderer>();
        return renderer != null && !renderer.enabled;
    }

    public static void EnsureWallsCannotTrap(SphericalPlanet planet)
    {
        if (!Application.isPlaying || planet == null)
            return;
        PlanetBorders.SetSolidCollidersEnabled(planet, false);
    }

    public static void ApplyIgnoreCollisions(GameObject player, SphericalPlanet planet, bool ignore)
    {
        if (player == null || planet == null)
            return;

        EnsureWallsCannotTrap(planet);

        Collider[] playerCols = player.GetComponents<Collider>();
        for (int p = 0; p < playerCols.Length; p++)
            ApplyIgnoreCollisions(playerCols[p], planet, ignore);
    }

    public static void ApplyIgnoreCollisions(Collider player, SphericalPlanet planet, bool ignore)
    {
        if (player == null || planet == null)
            return;

        BoxCollider[] boxes = PlanetBorders.SolidBoxes(planet);
        for (int i = 0; i < boxes.Length; i++)
        {
            Collider col = boxes[i];
            if (col == null || col == player || col.isTrigger)
                continue;
            Physics.IgnoreCollision(player, col, ignore);
        }
    }

    public static Vector3 FilterMove(
        SphericalPlanet planet,
        PlanetTileMap tiles,
        Vector3 from,
        Vector3 delta,
        float extraHover)
    {
        if (delta.sqrMagnitude < 0.0000001f || !IsActive(planet))
            return delta;

        Vector3 clamped = ClampPosition(planet, tiles, from + delta, extraHover);
        return clamped - from;
    }

    public static Vector3 ClampPosition(
        SphericalPlanet planet,
        PlanetTileMap tiles,
        Vector3 worldPosition,
        float extraHover)
    {
        if (planet == null || !IsActive(planet))
            return worldPosition;

        Vector3 local = planet.WorldToPlanetLocal.MultiplyPoint3x4(worldPosition);
        if (local.sqrMagnitude < 0.0001f)
            return worldPosition;

        PlanetTileMap.DirectionToStudyLonLat(local, out float lon, out float lat);
        if (!PlanetBorders.TryGetWalkBand(planet, lon, WalkSkinDegrees, out float latMax, out float latMin))
            return worldPosition;

        float clampedLat = Mathf.Clamp(lat, latMin, latMax);
        if (Mathf.Abs(clampedLat - lat) < 0.0001f)
            return worldPosition;

        Vector3 dir = PlanetTileMap.StudyLonLatToDirection(lon, clampedLat);
        float walk = WalkRadius(tiles, planet, dir) + Mathf.Max(0f, extraHover);
        return planet.PlanetLocalToWorld.MultiplyPoint3x4(dir * walk);
    }

    public static bool IsOffRoute(SphericalPlanet planet, Vector3 worldPosition)
    {
        if (planet == null || !IsActive(planet))
            return false;

        Vector3 local = planet.WorldToPlanetLocal.MultiplyPoint3x4(worldPosition);
        if (local.sqrMagnitude < 0.0001f)
            return false;

        PlanetTileMap.DirectionToStudyLonLat(local, out float lon, out float lat);
        if (!PlanetBorders.TryGetWalkBand(planet, lon, WalkSkinDegrees, out float latMax, out float latMin))
            return false;

        return lat > latMax + OffRoutePastDegrees || lat < latMin - OffRoutePastDegrees;
    }

    public static bool IsComfortable(SphericalPlanet planet, Vector3 worldPosition)
    {
        if (planet == null || !IsActive(planet))
            return false;

        Vector3 local = planet.WorldToPlanetLocal.MultiplyPoint3x4(worldPosition);
        if (local.sqrMagnitude < 0.0001f)
            return false;

        PlanetTileMap.DirectionToStudyLonLat(local, out float lon, out float lat);
        if (!PlanetBorders.TryGetWalkBand(planet, lon, WalkSkinDegrees, out float latMax, out float latMin))
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
        if (!IsOffRoute(planet, worldPosition))
            return false;

        if (hasAnchor && IsComfortable(planet, anchor))
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
        if (planet == null || !IsActive(planet))
            return false;

        Vector3 local = planet.WorldToPlanetLocal.MultiplyPoint3x4(worldPosition);
        if (local.sqrMagnitude < 0.0001f)
            return false;

        PlanetTileMap.DirectionToStudyLonLat(local, out float lon, out _);
        if (!PlanetBorders.TryGetWalkBand(planet, lon, WalkSkinDegrees, out float latMax, out float latMin))
            return false;

        float lat = 0.5f * (latMax + latMin);
        Vector3 dir = PlanetTileMap.StudyLonLatToDirection(lon, lat);
        float walk = WalkRadius(tiles, planet, dir) + Mathf.Max(0f, extraHover);
        center = planet.PlanetLocalToWorld.MultiplyPoint3x4(dir * walk);
        return true;
    }

    public static bool TryUnstickFromInvisibleWall(
        SphericalPlanet planet,
        PlanetTileMap tiles,
        Vector3 worldPosition,
        Vector3 capsuleBottom,
        Vector3 capsuleTop,
        float radius,
        float extraHover,
        bool hasAnchor,
        Vector3 anchor,
        out Vector3 freed)
    {
        freed = worldPosition;
        if (!IsActive(planet) || planet == null)
            return false;

        int count = Physics.OverlapCapsuleNonAlloc(
            capsuleBottom,
            capsuleTop,
            Mathf.Max(0.05f, radius),
            TrapOverlap,
            ~0,
            QueryTriggerInteraction.Ignore);
        bool trapped = false;
        int n = Mathf.Min(count, TrapOverlap.Length);
        for (int i = 0; i < n; i++)
        {
            if (IsInvisibleTrap(TrapOverlap[i], planet))
            {
                trapped = true;
                break;
            }
        }

        if (!trapped)
            return false;

        if (hasAnchor && IsComfortable(planet, anchor))
        {
            freed = anchor;
            return true;
        }

        return TrySnapToCenter(planet, tiles, worldPosition, extraHover, out freed);
    }

    static float WalkRadius(PlanetTileMap tiles, SphericalPlanet planet, Vector3 dir)
    {
        if (tiles != null)
            return tiles.GetWalkSurfaceRadius(dir);
        return planet != null ? planet.Radius : 0f;
    }
}
