using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Invisible vertical curtains along the north and south lips. Sliding along the path
/// is smooth; the sloped rock meshes stay visual-only.
/// </summary>
public static class NyxaraA2LipWallMeshBuilder
{
    public const string MeshName = "NyxaraA2LipWalls";
    public const float WallHeight = 12f;
    public const float Bury = 1.4f;
    public const float BlockedInsetDegrees = 0.22f;
    public const float ThicknessDegrees = 0.7f;

    public static Mesh Build(PlanetTileMap.TerrainWorkPlan plan, float walkRadius)
    {
        bool wrap = plan != null && plan.coverFullRing;
        int detail = plan != null ? Mathf.Clamp(plan.detailLevel, 1, 4) : 2;
        int lonCount = wrap ? 72 + detail * 24 : 17 + detail * 12;
        float radius = Mathf.Max(1f, walkRadius);

        var verts = new List<Vector3>(lonCount * 8);
        var tris = new List<int>(lonCount * 24);
        var presenceN = new float[lonCount];
        var presenceS = new float[lonCount];
        var northLat = new float[lonCount];
        var southLat = new float[lonCount];
        var lons = new float[lonCount];

        for (int i = 0; i < lonCount; i++)
        {
            float studyLon = wrap
                ? -180f + 360f * i / lonCount
                : Mathf.Lerp(
                    plan != null ? plan.studyLongitudeMin : 20f,
                    plan != null ? plan.studyLongitudeMax : 50f,
                    i / (float)Mathf.Max(1, lonCount - 1));
            lons[i] = studyLon;
            presenceN[i] = wrap
                ? NyxaraA2CliffProfile.LipPresence(
                    plan.northLipStudyLongitudes, plan.northLipLatitudes, studyLon, true)
                : NyxaraA2CliffProfile.LonEdgeFade(
                    studyLon,
                    plan != null ? plan.studyLongitudeMin : 20f,
                    plan != null ? plan.studyLongitudeMax : 50f);
            presenceS[i] = wrap
                ? NyxaraA2CliffProfile.LipPresence(
                    plan.cliffLipStudyLongitudes, plan.cliffLipLatitudes, studyLon, true)
                : presenceN[i];
            northLat[i] = NyxaraA2CliffProfile.SampleNorthLipLatitude(plan, studyLon);
            southLat[i] = NyxaraA2CliffProfile.SampleLipLatitude(plan, studyLon);
        }

        AppendStrip(verts, tris, lons, northLat, presenceN, wrap, north: true, radius);
        AppendStrip(verts, tris, lons, southLat, presenceS, wrap, north: false, radius);

        var mesh = new Mesh { name = MeshName };
        if (verts.Count > 65535)
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts);
        mesh.SetTriangles(tris, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    static void AppendStrip(
        List<Vector3> verts,
        List<int> tris,
        float[] lons,
        float[] lipLat,
        float[] presence,
        bool wrap,
        bool north,
        float radius)
    {
        int lonCount = lons.Length;
        int start = verts.Count;
        float r0 = radius - Bury;
        float r1 = radius + WallHeight;
        float sign = north ? 1f : -1f;

        for (int i = 0; i < lonCount; i++)
        {
            float lat0 = lipLat[i] + sign * BlockedInsetDegrees;
            float lat1 = lat0 + sign * ThicknessDegrees;
            Vector3 d0 = PlanetTileMap.StudyLonLatToDirection(lons[i], lat0);
            Vector3 d1 = PlanetTileMap.StudyLonLatToDirection(lons[i], lat1);
            verts.Add(d0 * r0);
            verts.Add(d0 * r1);
            verts.Add(d1 * r0);
            verts.Add(d1 * r1);
        }

        int lonSteps = wrap ? lonCount : lonCount - 1;
        for (int i = 0; i < lonSteps; i++)
        {
            int i1 = wrap ? (i + 1) % lonCount : i + 1;
            if (presence[i] <= 0.08f && presence[i1] <= 0.08f)
                continue;

            int a = start + i * 4;
            int b = start + i1 * 4;
            AddQuad(tris, a + 0, a + 1, b + 1, b + 0);
            AddQuad(tris, a + 2, a + 3, b + 3, b + 2);
            AddQuad(tris, a + 0, a + 2, b + 2, b + 0);
            AddQuad(tris, a + 1, a + 3, b + 3, b + 1);
        }
    }

    static void AddQuad(List<int> tris, int a, int b, int c, int d)
    {
        tris.Add(a);
        tris.Add(b);
        tris.Add(c);
        tris.Add(a);
        tris.Add(c);
        tris.Add(d);
    }
}
