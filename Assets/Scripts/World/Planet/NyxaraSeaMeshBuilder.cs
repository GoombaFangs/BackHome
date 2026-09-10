using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Level spherical water in the south basin. The north edge is the waterline on
/// the cliff face — south of the brown rim — so waves cannot spill onto the walk tiles.
/// Vertices are in Unity-sphere space (radius 0.5); <see cref="NyxaraSeaFit"/> scales it.
/// </summary>
public static class NyxaraSeaMeshBuilder
{
    public const string MeshName = "NyxaraSeaBasin";
    public const float UnitySphereRadius = 0.5f;
    public const float WaveSpillDegrees = 0.85f;

    public struct Settings
    {
        public PlanetTileMap.TerrainWorkPlan plan;
        public float walkRadius;
        public float seaRadius;
        public int longitudeSegments;
        public int latitudeSegments;
        public float worldScale;
    }

    public static Mesh Build(Settings settings)
    {
        var plan = settings.plan ?? new PlanetTileMap.TerrainWorkPlan();
        bool wrap = plan.coverFullRing;
        int lonCount = Mathf.Max(48, settings.longitudeSegments);
        int latCount = Mathf.Max(12, settings.latitudeSegments);
        float walk = Mathf.Max(1f, settings.walkRadius);
        float sea = settings.seaRadius > 1f ? settings.seaRadius : walk - 3.2f;

        float lon0 = wrap ? -180f : plan.studyLongitudeMin;
        float lon1 = wrap ? 180f : plan.studyLongitudeMax;
        if (!wrap && lon1 - lon0 < 1f)
        {
            lon0 = 20f;
            lon1 = 50f;
        }

        var northLat = new float[lonCount];
        var hasShore = new bool[lonCount];
        for (int i = 0; i < lonCount; i++)
        {
            float tLon = wrap ? i / (float)lonCount : i / (float)(lonCount - 1);
            float studyLon = wrap
                ? -180f + 360f * i / lonCount
                : Mathf.Lerp(lon0, lon1, tLon);
            hasShore[i] = NyxaraA2CliffProfile.TryWaterlineLatitude(
                plan, studyLon, wrap, walk, sea, WaveSpillDegrees, out northLat[i]);
            if (!hasShore[i])
                northLat[i] = -90f;
        }

        int vertCount = lonCount * latCount;
        var verts = new Vector3[vertCount];
        var uvs = new Vector2[vertCount];
        var colors = new Color[vertCount];
        float uvScale = Mathf.Max(1f, settings.worldScale);

        for (int i = 0; i < lonCount; i++)
        {
            float tLon = wrap ? i / (float)lonCount : i / (float)(lonCount - 1);
            float studyLon = wrap
                ? -180f + 360f * i / lonCount
                : Mathf.Lerp(lon0, lon1, tLon);
            float north = northLat[i];

            for (int j = 0; j < latCount; j++)
            {
                float v = j / (float)(latCount - 1);
                float lat = Mathf.Lerp(north, -90f, v * v);
                Vector3 dir = PlanetTileMap.StudyLonLatToDirection(studyLon, lat);
                int index = j * lonCount + i;
                verts[index] = dir * UnitySphereRadius;
                uvs[index] = new Vector2(verts[index].x, verts[index].z) * uvScale;
                float foam = hasShore[i] ? Mathf.Clamp01(1f - v * 7f) : 0f;
                colors[index] = new Color(foam, 0f, 0f, 1f);
            }
        }

        var triList = new List<int>((wrap ? lonCount : lonCount - 1) * (latCount - 1) * 6);
        int lonSteps = wrap ? lonCount : lonCount - 1;
        for (int j = 0; j < latCount - 1; j++)
        {
            for (int i = 0; i < lonSteps; i++)
            {
                int i1 = wrap ? (i + 1) % lonCount : i + 1;
                if (!hasShore[i] || !hasShore[i1])
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
        if (vertCount > 65535)
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
}
