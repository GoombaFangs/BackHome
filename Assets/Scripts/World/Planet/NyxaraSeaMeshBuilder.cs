using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Level spherical water in the south basin. One continuous cap to the south pole;
/// the north edge follows the waterline south of the rim so waves stay off the walk tiles.
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
            hasShore[i] = NyxaraA2CliffProfile.TryBasinWaterlineLatitude(
                plan, studyLon, wrap, walk, sea, WaveSpillDegrees, out northLat[i]);
            if (!hasShore[i])
                northLat[i] = -90f;
        }

        if (wrap)
            FillShoreGaps(northLat, hasShore);

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
                if (northLat[i] <= -89.5f && northLat[i1] <= -89.5f)
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

    static void FillShoreGaps(float[] northLat, bool[] hasShore)
    {
        int n = northLat.Length;
        if (n < 3)
            return;

        var filled = (float[])northLat.Clone();
        var filledShore = (bool[])hasShore.Clone();
        for (int i = 0; i < n; i++)
        {
            if (hasShore[i])
                continue;

            int left = -1;
            int right = -1;
            for (int d = 1; d < n; d++)
            {
                int li = (i - d + n) % n;
                if (left < 0 && hasShore[li])
                    left = li;
                int ri = (i + d) % n;
                if (right < 0 && hasShore[ri])
                    right = ri;
                if (left >= 0 && right >= 0)
                    break;
            }

            if (left < 0 && right < 0)
                continue;
            if (left < 0)
                filled[i] = northLat[right];
            else if (right < 0)
                filled[i] = northLat[left];
            else
            {
                float span = Mathf.Min(n - 1, ((i - left + n) % n) + ((right - i + n) % n));
                float t = span > 0.0001f ? ((i - left + n) % n) / span : 0f;
                filled[i] = Mathf.Lerp(northLat[left], northLat[right], t);
            }

            filledShore[i] = true;
        }

        for (int i = 0; i < n; i++)
        {
            northLat[i] = filled[i];
            hasShore[i] = filledShore[i];
        }
    }
}
