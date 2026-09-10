using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds the north-ridge heightfield in planet-local space. Call from the editor bake
/// only — not from Update or OnDrawGizmos. A2 is a sector; full-ring mode wraps and
/// drops height where there is no north wall.
/// </summary>
public static class NyxaraA2NorthRidgeMeshBuilder
{
    public const string MeshName = "NyxaraA2NorthRidge";

    /// <summary>
    /// Stage-3 north-wall inner latitudes at study lon 20, 20.5, … 50 (61 samples).
    /// Used when the live overlay has not been rebuilt yet.
    /// </summary>
    static readonly float[] FallbackNorthWallLatitudes =
    {
        19f, 19.25f, 19.75f, 20f, 20f, 20.75f, 21f, 21.5f, 21.75f, 22f,
        22.5f, 22.75f, 23f, 23.5f, 23.75f, 24f, 24.5f, 24.75f, 25f, 25.5f,
        25.75f, 26f, 26.5f, 26.75f, 27f, 27.5f, 27.75f, 28f, 28.5f, 28.75f,
        29f, 29.5f, 29.75f, 30f, 31f, 31f, 31f, 31f, 31f, 31f,
        31f, 31f, 31f, 31f, 31f, 31f, 31f, 31f, 31f, 31f,
        31.25f, 31.25f, 31.25f, 31.25f, 31.25f, 31.25f, 31.25f, 31.25f, 31.25f, 31.25f,
        31.25f
    };

    public struct Settings
    {
        public float studyLongitudeMin;
        public float studyLongitudeMax;
        public float ridgeHeight;
        public float playableMargin;
        public int detailLevel;
        public float walkRadius;
        public float blockedSideInsetDegrees;
        public float ridgeSpanDegrees;
        public float buryDepth;
        public float uvMeters;
        public bool wrapRing;
        public float[] studyLongitudes;
        public float[] northWallLatitudes;
    }

    public static Settings FromPlan(
        PlanetTileMap.TerrainWorkPlan plan,
        NyxaraA2BoundaryOverlay overlay,
        float walkRadius)
    {
        var settings = new Settings
        {
            studyLongitudeMin = plan != null ? plan.studyLongitudeMin : 20f,
            studyLongitudeMax = plan != null ? plan.studyLongitudeMax : 50f,
            ridgeHeight = plan != null ? Mathf.Max(1f, plan.ridgeHeight) : 24f,
            playableMargin = plan != null ? Mathf.Max(0f, plan.playableMargin) : 1f,
            detailLevel = plan != null ? Mathf.Clamp(plan.detailLevel, 1, 4) : 2,
            walkRadius = Mathf.Max(1f, walkRadius),
            blockedSideInsetDegrees = 0.35f,
            ridgeSpanDegrees = 30f,
            buryDepth = 0.08f,
            uvMeters = 8f,
            wrapRing = plan != null && plan.coverFullRing
        };

        if (settings.wrapRing)
        {
            settings.studyLongitudeMin = -180f;
            settings.studyLongitudeMax = 180f;
        }

        if (plan != null &&
            plan.northLipStudyLongitudes != null &&
            plan.northLipLatitudes != null &&
            plan.northLipStudyLongitudes.Length == plan.northLipLatitudes.Length &&
            plan.northLipStudyLongitudes.Length >= 2)
        {
            settings.studyLongitudes = plan.northLipStudyLongitudes;
            settings.northWallLatitudes = plan.northLipLatitudes;
            return settings;
        }

        if (overlay != null && overlay.HasSamples)
        {
            bool keepGaps = settings.wrapRing;
            var lons = new float[overlay.meridians.Length];
            var lats = new float[overlay.meridians.Length];
            int n = 0;
            for (int i = 0; i < overlay.meridians.Length; i++)
            {
                var sample = overlay.meridians[i];
                if (!keepGaps && !sample.hasNorthWall)
                    continue;
                lons[n] = sample.studyLongitude;
                lats[n] = sample.hasNorthWall
                    ? sample.northWallLatitude
                    : NyxaraA2CliffProfile.MissingLipLatitude;
                n++;
            }

            if (n >= 2)
            {
                Array.Resize(ref lons, n);
                Array.Resize(ref lats, n);
                settings.studyLongitudes = lons;
                settings.northWallLatitudes = lats;
            }
        }

        return settings;
    }

    public static Mesh Build(Settings settings)
    {
        int detail = Mathf.Clamp(settings.detailLevel, 1, 4);
        bool wrap = settings.wrapRing;
        int lonCount = wrap ? 181 : 19 + detail * 10;
        int latCount = 11 + detail * 4;
        float radius = Mathf.Max(1f, settings.walkRadius);
        float maxH = Mathf.Max(1f, settings.ridgeHeight);
        float lon0 = settings.studyLongitudeMin;
        float lon1 = settings.studyLongitudeMax;
        float inset = Mathf.Max(
            settings.blockedSideInsetDegrees,
            settings.playableMargin / radius * Mathf.Rad2Deg * 0.25f);
        float span = Mathf.Max(8f, settings.ridgeSpanDegrees);
        float uvM = Mathf.Max(1f, settings.uvMeters);

        var verts = new Vector3[lonCount * latCount];
        var uvs = new Vector2[verts.Length];
        var colors = new Color[verts.Length];
        var presence = new float[lonCount];

        for (int i = 0; i < lonCount; i++)
        {
            float tLon = wrap
                ? i / (float)lonCount
                : i / (float)(lonCount - 1);
            float studyLon = wrap
                ? -180f + 360f * i / lonCount
                : Mathf.Lerp(lon0, lon1, tLon);
            presence[i] = NorthPresence(settings, studyLon);
            float wallLat = NorthWallLatitude(settings, studyLon);
            float baseLat = wallLat + inset;

            for (int j = 0; j < latCount; j++)
            {
                float u = j / (float)(latCount - 1);
                float tLat = u;
                float lat = baseLat + tLat * span;
                if (tLat > 0.78f)
                {
                    float skirt = (tLat - 0.78f) / 0.22f;
                    lat += skirt * 0.55f * Mathf.Sin(studyLon * 0.14f + 0.4f);
                }

                float height = RidgeHeight(tLat, tLon, studyLon, maxH, presence[i], wrap);
                if (j == 0)
                    height = -Mathf.Abs(settings.buryDepth) * presence[i];

                Vector3 dir = PlanetTileMap.StudyLonLatToDirection(studyLon, lat);
                int index = j * lonCount + i;
                verts[index] = dir * (radius + height);

                float lonArc = (wrap ? Mathf.Repeat(studyLon + 180f, 360f) : (studyLon - lon0))
                    * Mathf.Deg2Rad * radius;
                float slopeArc = tLat * span * Mathf.Deg2Rad * radius;
                uvs[index] = new Vector2(lonArc / uvM, slopeArc / uvM);
                colors[index] = RockVertexColor(tLon, tLat, maxH > 0.001f ? height / maxH : 0f);
            }
        }

        var triList = new List<int>((wrap ? lonCount : lonCount - 1) * (latCount - 1) * 6);
        int lonSteps = wrap ? lonCount : lonCount - 1;
        for (int j = 0; j < latCount - 1; j++)
        {
            for (int i = 0; i < lonSteps; i++)
            {
                int i1 = wrap ? (i + 1) % lonCount : i + 1;
                if (presence[i] <= 0.02f && presence[i1] <= 0.02f)
                    continue;

                int a = j * lonCount + i;
                int b = j * lonCount + i1;
                int c = a + lonCount;
                int d = b + lonCount;
                Vector3 radial = verts[a].sqrMagnitude > 0.0001f ? verts[a].normalized : Vector3.up;
                Vector3 n = Vector3.Cross(verts[c] - verts[a], verts[b] - verts[a]);
                if (Vector3.Dot(n, radial) < 0f)
                {
                    triList.Add(a); triList.Add(b); triList.Add(c);
                    triList.Add(b); triList.Add(d); triList.Add(c);
                }
                else
                {
                    triList.Add(a); triList.Add(c); triList.Add(b);
                    triList.Add(b); triList.Add(c); triList.Add(d);
                }
            }
        }

        var mesh = new Mesh { name = MeshName };
        if (verts.Length > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.vertices = verts;
        mesh.uv = uvs;
        mesh.colors = colors;
        mesh.triangles = triList.ToArray();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        mesh.RecalculateBounds();
        return mesh;
    }

    public static string Describe(Settings settings, Mesh mesh)
    {
        int tris = mesh != null ? mesh.triangles.Length / 3 : 0;
        int verts = mesh != null ? mesh.vertexCount : 0;
        if (settings.wrapRing)
        {
            return
                $"Ring north mountain: {verts} verts, {tris} tris, max height {settings.ridgeHeight:0.#} local units. " +
                "Mass sits on the blocked (north) side of every north wall. Gaps without a north wall stay open. " +
                "Visual only; walk band is kinematic. " +
                "Smooth base / slope / repeating lobes / back to the sphere.";
        }

        return
            $"A2 north mountain: {verts} verts, {tris} tris, max height {settings.ridgeHeight:0.#} local units. " +
            "Wide mass on the blocked (north) side of Cube (3)/(4). Cube (5) is shared with R1 and is not meshed over. " +
            "No collider. West/east openings stay south of the wall. Smooth base / slope / two lobes / back to the sphere. Visual only; walk band is kinematic.";
    }

    static float NorthPresence(Settings settings, float studyLon)
    {
        if (settings.studyLongitudes == null ||
            settings.northWallLatitudes == null ||
            settings.studyLongitudes.Length != settings.northWallLatitudes.Length)
            return settings.wrapRing ? 0f : 1f;

        if (settings.wrapRing)
            return NyxaraA2CliffProfile.LipPresence(
                settings.studyLongitudes, settings.northWallLatitudes, studyLon, wrap: true);

        if (NyxaraA2CliffProfile.TrySampleLipLatitude(
                settings.studyLongitudes, settings.northWallLatitudes, studyLon, wrap: false, out _))
            return 1f;
        return 1f;
    }

    static float NorthWallLatitude(Settings settings, float studyLon)
    {
        if (settings.studyLongitudes != null &&
            settings.northWallLatitudes != null &&
            settings.studyLongitudes.Length == settings.northWallLatitudes.Length &&
            settings.studyLongitudes.Length >= 2)
        {
            if (NyxaraA2CliffProfile.TrySampleLipLatitude(
                    settings.studyLongitudes,
                    settings.northWallLatitudes,
                    studyLon,
                    settings.wrapRing,
                    out float lip))
                return lip;
            if (settings.wrapRing)
                return 0f;
        }

        float t = (studyLon - 20f) / 30f * (FallbackNorthWallLatitudes.Length - 1);
        int i = Mathf.Clamp(Mathf.FloorToInt(t), 0, FallbackNorthWallLatitudes.Length - 2);
        float f = t - i;
        return Mathf.Lerp(FallbackNorthWallLatitudes[i], FallbackNorthWallLatitudes[i + 1], f);
    }

    /// <summary>
    /// Wide mountain mass north of the authored wall: broad base, slope, lobes, back to the sphere.
    /// Ring mode repeats the A2-scale lobes along longitude and uses wall presence instead of sector fade.
    /// </summary>
    static float RidgeHeight(float tLat, float tLon, float studyLon, float maxH, float presence, bool wrap)
    {
        if (presence <= 0.0001f)
            return 0f;

        float edge = wrap
            ? 1f
            : Smooth01(tLon / 0.12f) * Smooth01((1f - tLon) / 0.12f);
        float rise = Smooth01(tLat / 0.22f);
        float back = 1f - Smooth01((tLat - 0.58f) / 0.42f);
        float envelope = rise * back;

        float lon01 = wrap ? Mathf.Repeat(studyLon / 32f, 1f) : tLon;
        float west = Smooth01(1f - Mathf.Abs(lon01 - 0.33f) / 0.44f);
        float east = Smooth01(1f - Mathf.Abs(lon01 - 0.67f) / 0.40f);
        float saddle = 0.64f * Smooth01(1f - Mathf.Abs(lon01 - 0.50f) / 0.30f);
        float planform = west;
        if (east > planform)
            planform = east;
        if (saddle > planform)
            planform = saddle;

        float crest = Smooth01((tLat - 0.16f) / 0.18f) * (1f - Smooth01((tLat - 0.40f) / 0.30f));
        float hNorm = envelope * Mathf.Lerp(0.52f, 1f, planform) * Mathf.Lerp(0.80f, 1f, crest);

        float n = 0.03f * Mathf.Sin(lon01 * 5.6f + tLat * 3.8f) * Mathf.Cos(lon01 * 3.2f);
        hNorm = Mathf.Max(0f, hNorm + n * envelope);
        return hNorm * maxH * edge * presence;
    }

    public static float SampleNorthLipLatitude(float studyLon)
    {
        return NorthWallLatitude(default, studyLon);
    }

    static Color RockVertexColor(float tLon, float tLat, float h01)
    {
        float cool = 0.10f * Mathf.Sin(tLon * 9.1f + tLat * 3.2f);
        float shade = Mathf.Lerp(0.70f, 1.04f, Mathf.Clamp01(h01));
        return new Color(
            Mathf.Clamp01((0.88f + cool * 0.12f) * shade),
            Mathf.Clamp01((0.84f - cool * 0.06f) * shade),
            Mathf.Clamp01((0.93f + cool * 0.18f) * shade),
            1f);
    }

    static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }
}
