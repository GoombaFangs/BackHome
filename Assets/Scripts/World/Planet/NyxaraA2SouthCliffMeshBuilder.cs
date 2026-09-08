using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Builds the south-cliff mesh in planet-local space. Lip and pit radii match
/// <see cref="NyxaraA2CliffProfile"/> so tiles and rock share a boundary.
/// Full-ring mode wraps and omits columns where there is no south wall.
/// </summary>
public static class NyxaraA2SouthCliffMeshBuilder
{
    public const string MeshName = "NyxaraA2SouthCliff";

    public struct Settings
    {
        public PlanetTileMap.TerrainWorkPlan plan;
        public float walkRadius;
        public float blockedSideInsetDegrees;
        public float buryDepth;
        public float faceBulge;
        public float uvMeters;
        public int detailLevel;
        public bool wrapRing;
    }

    public static Settings FromPlan(PlanetTileMap.TerrainWorkPlan plan, float walkRadius)
    {
        if (plan == null)
            plan = new PlanetTileMap.TerrainWorkPlan();
        plan.enabled = true;
        if (string.IsNullOrEmpty(plan.sectorId))
            plan.sectorId = plan.coverFullRing ? "Ring" : "A2";
        NyxaraA2CliffProfile.CaptureLip(plan, null);
        return new Settings
        {
            plan = plan,
            walkRadius = Mathf.Max(1f, walkRadius),
            blockedSideInsetDegrees = 0.18f,
            buryDepth = 0.08f,
            faceBulge = 0.16f,
            uvMeters = 8f,
            detailLevel = Mathf.Clamp(plan.detailLevel, 1, 4),
            wrapRing = plan.coverFullRing
        };
    }

    public static Mesh Build(Settings settings)
    {
        var plan = settings.plan ?? new PlanetTileMap.TerrainWorkPlan();
        int detail = Mathf.Clamp(settings.detailLevel, 1, 4);
        bool wrap = settings.wrapRing;
        int lonCount = wrap ? 64 + detail * 32 : 17 + detail * 12;
        int latCount = 7 + detail * 3;
        float radius = Mathf.Max(1f, settings.walkRadius);
        float lon0 = plan.studyLongitudeMin;
        float lon1 = plan.studyLongitudeMax;
        if (!wrap && lon1 - lon0 < 1f)
        {
            lon0 = 20f;
            lon1 = 50f;
        }

        if (wrap)
        {
            lon0 = -180f;
            lon1 = 180f;
        }

        float span = Mathf.Max(8f, plan.cliffSpanDegrees);
        float inset = settings.blockedSideInsetDegrees;
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
            presence[i] = wrap
                ? NyxaraA2CliffProfile.LipPresence(
                    plan.cliffLipStudyLongitudes, plan.cliffLipLatitudes, studyLon, true)
                : NyxaraA2CliffProfile.LonEdgeFade(studyLon, lon0, lon1);
            float lip = NyxaraA2CliffProfile.SampleLipLatitude(plan, studyLon);
            float baseLat = lip - inset;

            for (int j = 0; j < latCount; j++)
            {
                float u = j / (float)(latCount - 1);
                float tLat = u;
                float lat = baseLat - tLat * span;
                if (tLat > 0.78f)
                {
                    float skirt = (tLat - 0.78f) / 0.22f;
                    lat -= skirt * 0.45f * Mathf.Sin(studyLon * 0.13f + 1.1f);
                }

                float offset = NyxaraA2CliffProfile.RadialOffset(plan, studyLon, lat);
                if (j == 0)
                    offset -= Mathf.Abs(settings.buryDepth) * presence[i];
                else
                {
                    float face = Mathf.Sin(Mathf.PI * Mathf.Clamp01(tLat / 0.72f));
                    if (tLat > 0.72f)
                        face *= 1f - (tLat - 0.72f) / 0.28f;
                    offset += Mathf.Max(0.08f, settings.faceBulge) * 0.55f * Mathf.Max(0f, face) * presence[i];
                }

                Vector3 dir = PlanetTileMap.StudyLonLatToDirection(studyLon, lat);
                int index = j * lonCount + i;
                verts[index] = dir * (radius + offset);
                float lonArc = (wrap ? Mathf.Repeat(studyLon + 180f, 360f) : (studyLon - lon0))
                    * Mathf.Deg2Rad * radius;
                float slopeArc = tLat * span * Mathf.Deg2Rad * radius;
                uvs[index] = new Vector2(lonArc / uvM, slopeArc / uvM);
                float depth01 = radius > 0.001f ? Mathf.Clamp01(-offset / 14f) : 0f;
                colors[index] = CliffVertexColor(tLon, tLat, depth01);
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
        float depth = settings.plan != null ? settings.plan.cliffDepth : 13f;
        if (settings.wrapRing)
        {
            return
                $"Ring south cliff: {verts} verts, {tris} tris, drop {depth:0.#}–{settings.plan?.cliffDepthMax:0.#} local units toward center. " +
                "Lip is each authored south wall. Tiles in the pit use the same profile. Gaps without a south wall stay at full radius. " +
                "Ground MeshCollider blocks the face like a wall. Playable floor stays north of the lip.";
        }

        return
            $"A2 south cliff: {verts} verts, {tris} tris, drop {depth:0.#}–{settings.plan?.cliffDepthMax:0.#} local units toward center. " +
            "Lip is the authored south wall (Cube 2 / Cube 7). Tiles in the pit use the same profile. " +
            "Ground MeshCollider blocks the face like a wall. Playable floor and openings are north of the lip. " +
            "Face follows the tile drop: lip, steep face, lower slope back to the sphere. Sector ends fade to existing ground.";
    }

    static Color CliffVertexColor(float tLon, float tLat, float depth01)
    {
        float cool = 0.10f * Mathf.Sin(tLon * 8.4f + tLat * 4.1f);
        float shade = Mathf.Lerp(1.02f, 0.68f, Mathf.Clamp01(depth01));
        return new Color(
            Mathf.Clamp01((0.84f + cool * 0.10f) * shade),
            Mathf.Clamp01((0.80f - cool * 0.05f) * shade),
            Mathf.Clamp01((0.90f + cool * 0.16f) * shade),
            1f);
    }
}
