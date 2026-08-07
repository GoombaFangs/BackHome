using UnityEngine;

/// <summary>
/// Shared surface placement math for spherical planets.
/// Prefer collider stick (tile mesh / blocks), fall back to analytic walk/terrain radius.
/// </summary>
public static class PlanetSurfacePose
{
    public const float DefaultHover = 0.05f;
    const float GroundProbeDistance = 12f;
    static readonly LayerMask GroundLayer = 1 << 3; // Ground

    public static Transform GetOrCreateObjectsRoot(SphericalPlanet planet)
    {
        if (planet == null)
            return null;

        Transform existing = planet.transform.Find("Objects");
        if (existing != null)
            return existing;

        var go = new GameObject("Objects");
        go.transform.SetParent(planet.transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        return go.transform;
    }

    public static bool TryResolvePlanet(
        Transform from,
        out SphericalPlanet planet,
        out PlanetTileMap tiles)
    {
        planet = null;
        tiles = null;
        if (from == null)
            return false;

        planet = from.GetComponentInParent<SphericalPlanet>();
        if (planet == null)
            planet = SphericalPlanet.Instance;
        if (planet == null)
            planet = Object.FindFirstObjectByType<SphericalPlanet>();

        if (planet == null)
            return false;

        tiles = planet.GetComponent<PlanetTileMap>();
        return true;
    }

    public static bool TryGetPoseFromWorldPoint(
        SphericalPlanet planet,
        PlanetTileMap tiles,
        Vector3 worldPoint,
        float yawDegrees,
        float hover,
        out Vector3 position,
        out Quaternion rotation,
        out Vector3 up)
    {
        position = default;
        rotation = Quaternion.identity;
        up = Vector3.up;

        if (planet == null)
            return false;

        Vector3 dir = worldPoint - planet.Center;
        if (dir.sqrMagnitude < 0.0001f)
            dir = planet.transform.up;
        return TryGetPose(planet, tiles, dir, yawDegrees, hover, out position, out rotation, out up);
    }

    public static bool TryGetPose(
        SphericalPlanet planet,
        PlanetTileMap tiles,
        Vector3 directionFromCenter,
        float yawDegrees,
        float hover,
        out Vector3 position,
        out Quaternion rotation,
        out Vector3 up)
    {
        position = default;
        rotation = Quaternion.identity;
        up = Vector3.up;

        if (planet == null)
            return false;

        Vector3 radial = directionFromCenter.sqrMagnitude > 0.0001f
            ? directionFromCenter.normalized
            : Vector3.up;

        tiles?.EnsureWalkColliders();

        up = radial;
        if (!TryStickToCollider(planet, tiles, radial, hover, out position, out up))
        {
            position = GetAnalyticSurfacePoint(planet, tiles, radial, hover);
            up = tiles != null && tiles.ProvidesWalkSurface
                ? tiles.GetWalkSurfaceNormal(radial)
                : planet.GetTerrainNormal(radial);
        }

        if (Vector3.Dot(up, radial) < 0f)
            up = -up;

        rotation = RotationFromUp(up, yawDegrees);
        return true;
    }

    public static Quaternion RotationFromUp(Vector3 up, float yawDegrees)
    {
        if (up.sqrMagnitude < 0.0001f)
            up = Vector3.up;
        up.Normalize();

        Vector3 forward = Vector3.ProjectOnPlane(Vector3.forward, up);
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.ProjectOnPlane(Vector3.right, up);
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.Cross(up, Vector3.right);

        Quaternion baseRot = Quaternion.LookRotation(forward.normalized, up);
        if (Mathf.Abs(yawDegrees) < 0.0001f)
            return baseRot;

        return Quaternion.AngleAxis(yawDegrees, up) * baseRot;
    }

    public static float ExtractYaw(Quaternion rotation, Vector3 up)
    {
        if (up.sqrMagnitude < 0.0001f)
            up = Vector3.up;
        up.Normalize();

        Vector3 forward = Vector3.ProjectOnPlane(rotation * Vector3.forward, up);
        if (forward.sqrMagnitude < 0.001f)
            return 0f;

        Vector3 reference = Vector3.ProjectOnPlane(Vector3.forward, up);
        if (reference.sqrMagnitude < 0.001f)
            reference = Vector3.ProjectOnPlane(Vector3.right, up);
        if (reference.sqrMagnitude < 0.001f)
            return 0f;

        return Vector3.SignedAngle(reference.normalized, forward.normalized, up);
    }

    static Vector3 GetAnalyticSurfacePoint(
        SphericalPlanet planet,
        PlanetTileMap tiles,
        Vector3 directionFromCenter,
        float hover)
    {
        if (tiles != null && tiles.ProvidesWalkSurface)
            return tiles.GetWalkSurfacePoint(directionFromCenter, hover);

        return planet.GetSurfacePoint(directionFromCenter, hover);
    }

    static float GetFallbackSurfaceRadius(SphericalPlanet planet, PlanetTileMap tiles, Vector3 radial)
    {
        if (tiles != null && tiles.ProvidesWalkSurface)
            return tiles.GetWalkSurfaceRadius(radial);
        return planet.GetTerrainRadius(radial);
    }

    static bool TryStickToCollider(
        SphericalPlanet planet,
        PlanetTileMap tiles,
        Vector3 radial,
        float hover,
        out Vector3 feetPosition,
        out Vector3 normal)
    {
        feetPosition = default;
        normal = radial;

        float castStart = GetFallbackSurfaceRadius(planet, tiles, radial)
                          + Mathf.Max(4f, GroundProbeDistance * 0.5f);
        Vector3 origin = planet.Center + radial * castStart;
        float maxDist = castStart + 2f;

        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            -radial,
            maxDist,
            GroundLayer,
            QueryTriggerInteraction.Ignore);

        if (hits == null || hits.Length == 0)
            return false;

        float bestDist = float.MaxValue;
        bool found = false;
        RaycastHit best = default;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i].collider;
            if (col == null)
                continue;

            if (col.transform != planet.transform && !col.transform.IsChildOf(planet.transform))
                continue;

            if (hits[i].distance < bestDist)
            {
                bestDist = hits[i].distance;
                best = hits[i];
                found = true;
            }
        }

        if (!found)
            return false;

        normal = best.normal.sqrMagnitude > 0.001f ? best.normal.normalized : radial;
        if (Vector3.Dot(normal, radial) < 0f)
            normal = -normal;

        feetPosition = best.point + normal * hover;
        return true;
    }
}
