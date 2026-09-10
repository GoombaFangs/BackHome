using UnityEngine;

/// <summary>
/// Kinematic north/south bounds for the Nyxara walk band. Lips come from the inner
/// face of each standing Borders cube at that longitude — not the cube's chord
/// through the planet — so only the mountain and cliff block, not the open tiles.
/// </summary>
public static class NyxaraRouteBounds
{
    public const float WalkSkinDegrees = 0.28f;
    public const float ComfortInsideDegrees = 1.4f;
    public const float OffRoutePastDegrees = 1.4f;
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
    /// Hidden Border cubes and leftover rock colliders. The player must never be
    /// blocked or trapped by these while the route band is active.
    /// </summary>
    public static bool IsInvisibleTrap(Collider col, PlanetTileMap tiles)
    {
        if (col == null || col.isTrigger)
            return false;
        if (ShouldIgnorePhysicsWall(col, tiles))
            return true;
        if (!NyxaraTerrainCollision.IsBlockingWall(col))
            return false;
        var renderer = col.GetComponent<MeshRenderer>();
        return renderer != null && !renderer.enabled;
    }

    /// <summary>
    /// Turn off solid Border boxes (and any cube whose renderer is already hidden) so
    /// placeholder walls cannot pin the capsule. Triggers stay on.
    /// </summary>
    public static void EnsureRouteWallsCannotTrap(SphericalPlanet planet, PlanetTileMap tiles)
    {
        if (!Application.isPlaying || planet == null)
            return;

        bool route = IsActive(tiles);
        Transform borders = planet.transform.Find("Borders");
        if (borders != null)
        {
            Collider[] boxes = borders.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < boxes.Length; i++)
            {
                Collider col = boxes[i];
                if (col == null || col.isTrigger)
                    continue;
                var renderer = col.GetComponent<MeshRenderer>();
                bool hidden = renderer != null && !renderer.enabled;
                if (hidden || route)
                    col.enabled = false;
            }
        }

        if (!route)
            return;

        NyxaraA2NorthRidge ridge = planet.GetComponentInChildren<NyxaraA2NorthRidge>(true);
        if (ridge != null)
            NyxaraTerrainCollision.ClearBlockingMesh(ridge.gameObject);
    }

    /// <summary>
    /// Ignore physical contact with Borders / leftover rock colliders so a kinematic body
    /// cannot snag. Trigger volumes on Borders stay active. Call again with IsActive false
    /// to restore ignore flags (does not re-enable disabled Border boxes).
    /// </summary>
    public static void ApplyIgnoreCollisions(GameObject player, SphericalPlanet planet, PlanetTileMap tiles)
    {
        if (player == null || planet == null)
            return;

        bool ignore = IsActive(tiles);
        if (ignore)
            EnsureRouteWallsCannotTrap(planet, tiles);

        Collider[] playerCols = player.GetComponents<Collider>();
        for (int p = 0; p < playerCols.Length; p++)
            ApplyIgnoreCollisions(playerCols[p], planet, tiles);
    }

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
    }

    static readonly Collider[] TrapOverlap = new Collider[24];

    /// <summary>
    /// If the capsule is already inside a hidden Border cube or leftover rock collider,
    /// snap back onto the walk band so the player cannot stay trapped.
    /// </summary>
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
        if (!IsActive(tiles) || planet == null)
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
            if (IsInvisibleTrap(TrapOverlap[i], tiles))
            {
                trapped = true;
                break;
            }
        }

        if (!trapped)
            return false;

        if (hasAnchor && IsComfortable(planet, tiles, anchor))
        {
            freed = anchor;
            return true;
        }

        return TrySnapToCenter(planet, tiles, worldPosition, extraHover, out freed);
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
        if (!TryGetWalkBand(planet, tiles.WorkPlan, lon, out float latMax, out float latMin))
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

        if (!TryGetWalkBand(planet, tiles.WorkPlan, lon, out float latMax, out float latMin))
            return false;

        if (lat > latMax + OffRoutePastDegrees)
            return true;
        if (lat < latMin - OffRoutePastDegrees)
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
        if (!TryGetWalkBand(planet, tiles.WorkPlan, lon, out float latMax, out float latMin))
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
        if (!TryGetWalkBand(planet, tiles.WorkPlan, lon, out float latMax, out float latMin))
            return false;

        float lat = 0.5f * (latMax + latMin);
        Vector3 dir = PlanetTileMap.StudyLonLatToDirection(lon, lat);
        float walk = tiles.GetWalkSurfaceRadius(dir) + Mathf.Max(0f, extraHover);
        center = planet.PlanetLocalToWorld.MultiplyPoint3x4(dir * walk);
        return true;
    }

    public static bool TryGetWalkBand(
        SphericalPlanet planet,
        PlanetTileMap.TerrainWorkPlan plan,
        float studyLon,
        out float latMax,
        out float latMin)
    {
        latMax = 90f;
        latMin = -90f;
        if (TryGetWalkBandFromCubes(planet, studyLon, out latMax, out latMin))
            return true;
        return TryGetWalkBandFromPlan(plan, studyLon, out latMax, out latMin);
    }

    static bool TryGetWalkBandFromPlan(
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

    static BoxCollider[] _borderBoxCache;
    static int _cubeCacheFrame = -1;
    static Transform _cubeCachePlanet;

    static BoxCollider[] BorderBoxes(SphericalPlanet planet)
    {
        Transform root = planet != null ? planet.transform : null;
        if (_borderBoxCache != null &&
            _cubeCacheFrame == Time.frameCount &&
            _cubeCachePlanet == root)
            return _borderBoxCache;

        _cubeCacheFrame = Time.frameCount;
        _cubeCachePlanet = root;
        Transform borders = root != null ? root.Find("Borders") : null;
        _borderBoxCache = borders != null
            ? borders.GetComponentsInChildren<BoxCollider>(true)
            : System.Array.Empty<BoxCollider>();
        return _borderBoxCache;
    }

    static bool TryGetWalkBandFromCubes(SphericalPlanet planet, float studyLon, out float latMax, out float latMin)
    {
        latMax = 90f;
        latMin = -90f;
        if (planet == null)
            return false;

        BoxCollider[] boxes = BorderBoxes(planet);
        bool hasNorth = false;
        bool hasSouth = false;
        float northInner = 90f;
        float southInner = -90f;
        SampleCubeLips(boxes, planet, studyLon, ref hasNorth, ref northInner, ref hasSouth, ref southInner);

        bool any = false;
        if (hasNorth)
        {
            latMax = northInner - WalkSkinDegrees;
            any = true;
        }

        if (hasSouth)
        {
            latMin = southInner + WalkSkinDegrees;
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

    public static void SampleCubeLips(
        SphericalPlanet planet,
        float studyLon,
        out bool hasNorth,
        out float northInner,
        out bool hasSouth,
        out float southInner)
    {
        hasNorth = false;
        hasSouth = false;
        northInner = 90f;
        southInner = -90f;
        if (planet == null)
            return;
        SampleCubeLips(BorderBoxes(planet), planet, studyLon, ref hasNorth, ref northInner, ref hasSouth, ref southInner);
    }

    static void SampleCubeLips(
        BoxCollider[] boxes,
        SphericalPlanet planet,
        float studyLon,
        ref bool hasNorth,
        ref float northInner,
        ref bool hasSouth,
        ref float southInner)
    {
        if (boxes == null)
            return;
        for (int i = 0; i < boxes.Length; i++)
        {
            BoxCollider box = boxes[i];
            if (box == null || box.isTrigger)
                continue;
            if (!TryInnerLatitudeAtLon(box, planet, studyLon, out float inner, out bool north))
                continue;
            if (north)
            {
                northInner = Mathf.Min(northInner, inner);
                hasNorth = true;
            }
            else
            {
                southInner = Mathf.Max(southInner, inner);
                hasSouth = true;
            }
        }
    }

    public static bool TryInnerLatitudeAtLon(
        BoxCollider box,
        SphericalPlanet planet,
        float studyLon,
        bool north,
        out float innerLat)
    {
        innerLat = 0f;
        if (box == null || planet == null)
            return false;
        if (!IsLatitudeWall(box, planet))
            return false;
        if (!TryInnerFaceCorners(box, planet, north, out Vector3[] corners, out float surfaceRadius))
            return false;

        float minLat = 90f;
        float maxLat = -90f;
        int hits = 0;
        var lons = new float[4];
        var lats = new float[4];
        var radii = new float[4];
        for (int i = 0; i < 4; i++)
        {
            Vector3 p = corners[i];
            radii[i] = p.magnitude;
            if (p.sqrMagnitude < 0.0001f)
            {
                lons[i] = 1000f;
                lats[i] = 0f;
                continue;
            }

            PlanetTileMap.DirectionToStudyLonLat(p, out lons[i], out lats[i]);
            if (radii[i] < surfaceRadius * 0.88f)
                continue;
            if (Mathf.Abs(NyxaraA2CliffProfile.WrapStudyLon(lons[i] - studyLon)) <= 0.35f)
            {
                minLat = Mathf.Min(minLat, lats[i]);
                maxLat = Mathf.Max(maxLat, lats[i]);
                hits++;
            }
        }

        int[,] edges = { { 0, 1 }, { 0, 2 }, { 1, 3 }, { 2, 3 } };
        for (int e = 0; e < 4; e++)
        {
            int a = edges[e, 0];
            int b = edges[e, 1];
            if (lons[a] > 900f || lons[b] > 900f)
                continue;
            float lonSpan = Mathf.Abs(NyxaraA2CliffProfile.WrapStudyLon(lons[a] - lons[b]));
            if (lonSpan > 90f)
                continue;
            float d0 = NyxaraA2CliffProfile.WrapStudyLon(lons[a] - studyLon);
            float d1 = NyxaraA2CliffProfile.WrapStudyLon(lons[b] - studyLon);
            if (d0 * d1 > 0f)
                continue;
            if (Mathf.Abs(d0 - d1) < 0.0001f)
                continue;
            float t = d0 / (d0 - d1);
            if (t < -0.001f || t > 1.001f)
                continue;
            Vector3 hit = Vector3.Lerp(corners[a], corners[b], Mathf.Clamp01(t));
            if (hit.magnitude < surfaceRadius * 0.88f)
                continue;
            PlanetTileMap.DirectionToStudyLonLat(hit, out _, out float lat);
            minLat = Mathf.Min(minLat, lat);
            maxLat = Mathf.Max(maxLat, lat);
            hits++;
        }

        if (hits == 0)
            return false;

        // A standing N/S wall's inner face is nearly constant latitude. A tall meridian
        // slice here means the cube is a side wall that only grazed this longitude.
        if (maxLat - minLat > 8f)
            return false;

        innerLat = north ? minLat : maxLat;
        return true;
    }

    static bool IsLatitudeWall(BoxCollider box, SphericalPlanet planet)
    {
        if (!TryThinAxis(box, out Vector3 thinLocal))
            return false;

        Vector3 world = box.transform.TransformPoint(box.center);
        Vector3 local = planet.WorldToPlanetLocal.MultiplyPoint3x4(world);
        if (local.sqrMagnitude < 0.0001f)
            return false;

        Vector3 radial = local.normalized;
        Vector3 north = Vector3.ProjectOnPlane(Vector3.up, radial);
        if (north.sqrMagnitude < 0.0001f)
            north = Vector3.ProjectOnPlane(Vector3.forward, radial);
        north.Normalize();
        Vector3 east = Vector3.Cross(radial, north).normalized;
        Vector3 thin = planet.WorldToPlanetLocal.MultiplyVector(
            box.transform.TransformDirection(thinLocal)).normalized;

        float dNorth = Mathf.Abs(Vector3.Dot(thin, north));
        float dEast = Mathf.Abs(Vector3.Dot(thin, east));
        float dRad = Mathf.Abs(Vector3.Dot(thin, radial));
        if (dEast > dNorth && dEast > dRad)
            return false;
        if (dRad > dNorth && dRad > dEast)
            return false;
        return true;
    }

    static bool TryThinAxis(BoxCollider box, out Vector3 thinLocal)
    {
        thinLocal = Vector3.forward;
        if (box == null)
            return false;

        Vector3 lossy = box.transform.lossyScale;
        Vector3 worldSize = new Vector3(
            Mathf.Abs(box.size.x * lossy.x),
            Mathf.Abs(box.size.y * lossy.y),
            Mathf.Abs(box.size.z * lossy.z));
        int axis = 0;
        if (worldSize.y < worldSize.x)
            axis = 1;
        if (worldSize.z < worldSize[axis])
            axis = 2;

        float thin = worldSize[axis];
        float longest = Mathf.Max(worldSize.x, Mathf.Max(worldSize.y, worldSize.z));
        if (longest < 0.01f || thin > longest * 0.35f)
            return false;

        thinLocal = axis == 0 ? Vector3.right : (axis == 1 ? Vector3.up : Vector3.forward);
        return true;
    }

    static bool TryInnerFaceCorners(
        BoxCollider box,
        SphericalPlanet planet,
        bool north,
        out Vector3[] corners,
        out float surfaceRadius)
    {
        corners = null;
        surfaceRadius = 0f;
        if (!TryThinAxis(box, out Vector3 thinLocal))
            return false;

        Vector3 half = box.size * 0.5f;
        Vector3 c = box.center;
        int axis = thinLocal == Vector3.right ? 0 : (thinLocal == Vector3.up ? 1 : 2);
        int uAxis = (axis + 1) % 3;
        int vAxis = (axis + 2) % 3;

        Vector3[] plus = FaceCorners(box, axis, uAxis, vAxis, half, c, 1f, planet);
        Vector3[] minus = FaceCorners(box, axis, uAxis, vAxis, half, c, -1f, planet);
        float plusLat = MeanLatitude(plus);
        float minusLat = MeanLatitude(minus);
        corners = north
            ? (plusLat < minusLat ? plus : minus)
            : (plusLat > minusLat ? plus : minus);

        for (int i = 0; i < 4; i++)
            surfaceRadius = Mathf.Max(surfaceRadius, corners[i].magnitude);
        return surfaceRadius > 0.01f;
    }

    static Vector3[] FaceCorners(
        BoxCollider box,
        int axis,
        int uAxis,
        int vAxis,
        Vector3 half,
        Vector3 center,
        float sign,
        SphericalPlanet planet)
    {
        var corners = new Vector3[4];
        int n = 0;
        for (int u = -1; u <= 1; u += 2)
        {
            for (int v = -1; v <= 1; v += 2)
            {
                Vector3 local = center;
                local[axis] = center[axis] + sign * half[axis];
                local[uAxis] = center[uAxis] + u * half[uAxis];
                local[vAxis] = center[vAxis] + v * half[vAxis];
                corners[n++] = planet.WorldToPlanetLocal.MultiplyPoint3x4(
                    box.transform.TransformPoint(local));
            }
        }

        return corners;
    }

    static float MeanLatitude(Vector3[] corners)
    {
        float sum = 0f;
        int n = 0;
        for (int i = 0; i < corners.Length; i++)
        {
            if (corners[i].sqrMagnitude < 0.0001f)
                continue;
            PlanetTileMap.DirectionToStudyLonLat(corners[i], out _, out float lat);
            sum += lat;
            n++;
        }

        return n > 0 ? sum / n : 0f;
    }

    static bool TryInnerLatitudeAtLon(
        BoxCollider box,
        SphericalPlanet planet,
        float studyLon,
        out float innerLat,
        out bool north)
    {
        north = IsNorthSideCube(box, planet);
        return TryInnerLatitudeAtLon(box, planet, studyLon, north, out innerLat);
    }

    static bool IsNorthSideCube(BoxCollider box, SphericalPlanet planet)
    {
        if (box == null || planet == null)
            return false;
        Vector3 world = box.transform.TransformPoint(box.center);
        Vector3 local = planet.WorldToPlanetLocal.MultiplyPoint3x4(world);
        if (local.sqrMagnitude < 0.0001f)
            return true;
        return local.normalized.y >= 0f;
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
