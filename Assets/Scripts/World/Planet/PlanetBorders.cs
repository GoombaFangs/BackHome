using UnityEngine;

/// <summary>
/// <c>Borders</c> cubes block walking kinematically. Physics colliders stay off
/// so the boxes cannot trap the player inside their volume.
/// </summary>
public static class PlanetBorders
{
    public const string RootName = "Borders";
    public const float MaxSeamDegrees = 3.5f;

    static BoxCollider[] _boxCache = System.Array.Empty<BoxCollider>();
    static int _cacheFrame = -1;
    static Transform _cachePlanet;

    public static void InvalidateCache()
    {
        _cacheFrame = -1;
        _cachePlanet = null;
        _boxCache = System.Array.Empty<BoxCollider>();
    }

    public static Transform FindRoot(SphericalPlanet planet)
    {
        return planet != null ? planet.transform.Find(RootName) : null;
    }

    public static bool HasLayout(SphericalPlanet planet)
    {
        BoxCollider[] boxes = SolidBoxes(planet);
        return boxes != null && boxes.Length > 0;
    }

    public static BoxCollider[] SolidBoxes(SphericalPlanet planet)
    {
        Transform root = planet != null ? planet.transform : null;
        if (_boxCache != null &&
            _cacheFrame == Time.frameCount &&
            _cachePlanet == root)
            return _boxCache;

        _cacheFrame = Time.frameCount;
        _cachePlanet = root;
        Transform borders = FindRoot(planet);
        if (borders == null)
        {
            _boxCache = System.Array.Empty<BoxCollider>();
            return _boxCache;
        }

        _boxCache = borders.GetComponentsInChildren<BoxCollider>(true);
        return _boxCache;
    }

    public static bool IsBorderCollider(Collider col)
    {
        if (col == null)
            return false;

        Transform t = col.transform;
        while (t != null)
        {
            if (t.name == RootName)
                return true;
            if (t.GetComponent<SphericalPlanet>() != null)
                break;
            t = t.parent;
        }

        return false;
    }

    public static float WrapStudyLon(float lon)
    {
        return Mathf.Repeat(lon + 180f, 360f) - 180f;
    }

    public static bool IsGround(SphericalPlanet planet, Vector3 directionFromCenter)
    {
        if (!HasLayout(planet))
            return true;

        PlanetTileMap.DirectionToStudyLonLat(directionFromCenter, out float lon, out float lat);
        SampleLips(planet, lon, out bool hasNorth, out float northInner, out bool hasSouth, out float southInner);
        if (hasNorth && lat > northInner)
            return false;
        if (hasSouth && lat < southInner)
            return false;
        return true;
    }

    public static bool TryGetWalkBand(
        SphericalPlanet planet,
        float studyLon,
        float skinDegrees,
        out float latMax,
        out float latMin)
    {
        latMax = 90f;
        latMin = -90f;
        if (planet == null)
            return false;

        SampleLips(planet, studyLon, out bool hasNorth, out float northInner, out bool hasSouth, out float southInner);
        bool any = false;
        if (hasNorth)
        {
            latMax = northInner - skinDegrees;
            any = true;
        }

        if (hasSouth)
        {
            latMin = southInner + skinDegrees;
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

    public static void SampleLips(
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
        BoxCollider[] boxes = SolidBoxes(planet);
        for (int i = 0; i < boxes.Length; i++)
        {
            BoxCollider box = boxes[i];
            if (box == null)
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

    public static void SetSolidRenderersEnabled(SphericalPlanet planet, bool enabled)
    {
        BoxCollider[] boxes = SolidBoxes(planet);
        for (int i = 0; i < boxes.Length; i++)
        {
            if (boxes[i] == null)
                continue;
            var renderer = boxes[i].GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.enabled = enabled;
        }
    }

    public static void SetSolidCollidersEnabled(SphericalPlanet planet, bool enabled)
    {
        BoxCollider[] boxes = SolidBoxes(planet);
        for (int i = 0; i < boxes.Length; i++)
        {
            if (boxes[i] != null)
                boxes[i].enabled = enabled;
        }
    }

    public static void EnsureSolidWalls(SphericalPlanet planet)
    {
        BoxCollider[] boxes = SolidBoxes(planet);
        for (int i = 0; i < boxes.Length; i++)
        {
            if (boxes[i] != null)
                boxes[i].enabled = false;
        }
    }

    /// <summary>
    /// Keeps <paramref name="desired"/> outside every Borders cube, sliding along
    /// the wall. If the player is already inside a cube, they are pushed out.
    /// </summary>
    public static Vector3 ResolveAgainstWalls(
        SphericalPlanet planet,
        Vector3 from,
        Vector3 desired,
        float radius)
    {
        if (planet == null)
            return desired;

        BoxCollider[] boxes = SolidBoxes(planet);
        if (boxes == null || boxes.Length == 0)
            return desired;

        float skin = Mathf.Max(0.08f, radius);
        Vector3 freed = from;
        Depenetrate(planet, boxes, ref freed, skin);
        Vector3 dest = freed + (desired - from);
        Depenetrate(planet, boxes, ref dest, skin);
        return dest;
    }

    static void Depenetrate(
        SphericalPlanet planet,
        BoxCollider[] boxes,
        ref Vector3 worldPos,
        float radius)
    {
        for (int iter = 0; iter < 4; iter++)
        {
            bool moved = false;
            Vector3 radial = worldPos - planet.Center;
            if (radial.sqrMagnitude < 0.0001f)
                return;
            radial.Normalize();

            for (int i = 0; i < boxes.Length; i++)
            {
                Vector3 before = worldPos;
                if (!TryDepenetrateBox(boxes[i], ref worldPos, radius))
                    continue;
                worldPos = before + Vector3.ProjectOnPlane(worldPos - before, radial);
                if ((worldPos - before).sqrMagnitude > 0.0000001f)
                    moved = true;
            }

            if (!moved)
                return;
        }
    }

    static bool TryDepenetrateBox(BoxCollider box, ref Vector3 worldPos, float radius)
    {
        if (box == null)
            return false;

        Transform t = box.transform;
        Vector3 local = t.InverseTransformPoint(worldPos) - box.center;
        Vector3 half = box.size * 0.5f;
        Vector3 lossy = t.lossyScale;
        Vector3 expanded = new Vector3(
            half.x + radius / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
            half.y + radius / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)),
            half.z + radius / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)));

        if (Mathf.Abs(local.x) > expanded.x ||
            Mathf.Abs(local.y) > expanded.y ||
            Mathf.Abs(local.z) > expanded.z)
            return false;

        int axis = 0;
        if (TryThinAxis(box, out Vector3 thinLocal))
            axis = thinLocal == Vector3.right ? 0 : (thinLocal == Vector3.up ? 1 : 2);
        else
        {
            float dx = expanded.x - Mathf.Abs(local.x);
            float dy = expanded.y - Mathf.Abs(local.y);
            float dz = expanded.z - Mathf.Abs(local.z);
            axis = dx <= dy && dx <= dz ? 0 : (dy <= dz ? 1 : 2);
        }

        float sign = local[axis];
        local[axis] = (sign >= 0f ? 1f : -1f) * expanded[axis];
        worldPos = t.TransformPoint(local + box.center);
        return true;
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

        var lons = new float[4];
        var lats = new float[4];
        var surface = new bool[4];
        Vector3 surfaceMean = Vector3.zero;
        int surfaceCount = 0;
        for (int i = 0; i < 4; i++)
        {
            Vector3 p = corners[i];
            if (p.sqrMagnitude < 0.0001f)
            {
                lons[i] = 1000f;
                continue;
            }

            PlanetTileMap.DirectionToStudyLonLat(p, out lons[i], out lats[i]);
            surface[i] = p.magnitude >= surfaceRadius * 0.90f;
            if (surface[i])
            {
                surfaceMean += p;
                surfaceCount++;
            }
        }

        if (surfaceCount < 2 || surfaceMean.sqrMagnitude < 0.0001f)
            return false;

        PlanetTileMap.DirectionToStudyLonLat(surfaceMean, out float centerLon, out float centerLat);
        if (Mathf.Abs(WrapStudyLon(centerLon - studyLon)) > 80f)
            return false;

        float minDelta = 0f;
        float maxDelta = 0f;
        for (int i = 0; i < 4; i++)
        {
            if (!surface[i] || lons[i] > 900f)
                continue;
            float d = WrapStudyLon(lons[i] - centerLon);
            if (d < minDelta)
                minDelta = d;
            if (d > maxDelta)
                maxDelta = d;
        }

        float q = WrapStudyLon(studyLon - centerLon);
        if (q < minDelta - 0.45f || q > maxDelta + 0.45f)
            return false;

        Vector3 queryDir = PlanetTileMap.StudyLonLatToDirection(studyLon, centerLat);
        float minLat = 90f;
        float maxLat = -90f;
        int hits = 0;

        for (int i = 0; i < 4; i++)
        {
            if (!surface[i] || lons[i] > 900f)
                continue;
            if (Mathf.Abs(WrapStudyLon(lons[i] - studyLon)) > 0.4f)
                continue;
            if (Vector3.Dot(corners[i].normalized, queryDir) < 0.25f)
                continue;
            minLat = Mathf.Min(minLat, lats[i]);
            maxLat = Mathf.Max(maxLat, lats[i]);
            hits++;
        }

        int[,] edges = { { 0, 1 }, { 0, 2 }, { 1, 3 }, { 2, 3 } };
        for (int e = 0; e < 4; e++)
        {
            int a = edges[e, 0];
            int b = edges[e, 1];
            if (!surface[a] || !surface[b] || lons[a] > 900f || lons[b] > 900f)
                continue;
            float lonSpan = Mathf.Abs(WrapStudyLon(lons[a] - lons[b]));
            if (lonSpan > 70f)
                continue;
            float d0 = WrapStudyLon(lons[a] - studyLon);
            float d1 = WrapStudyLon(lons[b] - studyLon);
            if (d0 * d1 > 0f)
                continue;
            if (Mathf.Abs(d0) + Mathf.Abs(d1) > 70f)
                continue;
            if (Mathf.Abs(d0 - d1) < 0.0001f)
                continue;
            float t = d0 / (d0 - d1);
            if (t < -0.001f || t > 1.001f)
                continue;
            Vector3 hit = Vector3.Lerp(corners[a], corners[b], Mathf.Clamp01(t));
            if (hit.sqrMagnitude < 0.0001f)
                continue;
            if (hit.magnitude < surfaceRadius * 0.90f)
                continue;
            if (Vector3.Dot(hit.normalized, queryDir) < 0.25f)
                continue;
            PlanetTileMap.DirectionToStudyLonLat(hit, out float hitLon, out float lat);
            if (Mathf.Abs(WrapStudyLon(hitLon - studyLon)) > 1.2f)
                continue;
            minLat = Mathf.Min(minLat, lat);
            maxLat = Mathf.Max(maxLat, lat);
            hits++;
        }

        if (hits == 0)
            return false;
        if (maxLat - minLat > 8f)
            return false;

        innerLat = north ? minLat : maxLat;
        return true;
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
}
