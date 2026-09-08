using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared south-cliff profile: authored lip latitudes and radial drop toward the planet
/// center. Used by both <see cref="PlanetTileMap"/> (lower tiles in the pit) and the cliff mesh
/// so they share the same boundary. A2 is a 20°–50° sector; full-ring mode follows every
/// south wall and leaves meridians without a wall open.
/// </summary>
public static class NyxaraA2CliffProfile
{
    public const float DefaultSpanDegrees = 18f;
    public const float LipBlendDegrees = 0.2f;
    public const float SouthReturnDegrees = 2.4f;
    public const float LonFadeFraction = 0.08f;
    public const float MissingLipLatitude = 999f;

    /// <summary>
    /// Fill missing lip samples between two authored walls if the hole is this small.
    /// Stops Cube (2)/Cube (1)-style joints from becoming walk-through cracks. Does not
    /// invent a wall across a long stretch with no cubes.
    /// </summary>
    public const float MaxLipBridgeDegrees = 16f;

    /// <summary>Authored south-wall inner latitudes, study lon 20 … 50 step 0.5° (stage 3).</summary>
    public static readonly float[] FallbackAuthoredSouthLatitudes =
    {
        -19f, -19.25f, -19.5f, -19.75f, -20.25f, -20.5f, -20.75f, -21f, -21.25f, -21.5f,
        -21.75f, -22.25f, -22.5f, -22.5f, -22.25f, -22f, -21.75f, -21.5f, -21.5f, -21.25f,
        -21f, -20.75f, -20.5f, -20.25f, -19.75f, -19.5f, -19.25f, -19f, -18.75f, -18.5f,
        -18.25f, -17.75f, -17.5f, -17.25f, -16.75f, -16.5f, -16.25f, -15.75f, -15.5f, -15.25f,
        -14.75f, -14.5f, -14f, -13.75f, -13.25f, -12.75f, -12.5f, -12f, -11.5f, -11.25f,
        -10.75f, -10.25f, -9.75f, -9.5f, -9f, -8.5f, -8f, -7.5f, -7f, -6.5f,
        -6f
    };

    public static bool HasLip(float latitude) => latitude < 90f;

    public static bool PlanApplies(PlanetTileMap.TerrainWorkPlan plan)
    {
        if (plan == null || !plan.enabled)
            return false;
        if (plan.coverFullRing)
            return true;
        return string.IsNullOrEmpty(plan.sectorId) ||
               string.Equals(plan.sectorId, "A2", StringComparison.Ordinal);
    }

    public static float WrapStudyLon(float lon)
    {
        return Mathf.Repeat(lon + 180f, 360f) - 180f;
    }

    public static void CaptureLip(PlanetTileMap.TerrainWorkPlan plan, NyxaraA2BoundaryOverlay overlay)
    {
        if (plan == null)
            return;

        if (overlay == null || !overlay.HasSamples)
        {
            if (plan.coverFullRing)
                return;
            if (plan.cliffLipStudyLongitudes == null || plan.cliffLipStudyLongitudes.Length < 2)
                FillFallbackLip(plan);
            return;
        }

        if (plan.coverFullRing)
        {
            if (overlay.wrapLongitude)
                CaptureRingLips(plan, overlay);
            return;
        }

        CaptureSectorLips(plan, overlay, north: false);
        CaptureSectorLips(plan, overlay, north: true);
        if (plan.cliffSpanDegrees < 8f)
            plan.cliffSpanDegrees = DefaultSpanDegrees;
    }

    static void CaptureRingLips(PlanetTileMap.TerrainWorkPlan plan, NyxaraA2BoundaryOverlay overlay)
    {
        int n = overlay.meridians.Length;
        var lons = new float[n];
        var south = new float[n];
        var north = new float[n];
        bool optional = plan.useOptionalSouthCliffShift;
        for (int i = 0; i < n; i++)
        {
            var sample = overlay.meridians[i];
            lons[i] = sample.studyLongitude;
            south[i] = sample.hasSouthWall
                ? (optional ? sample.optionalSouthLatitude : sample.southWallLatitude)
                : MissingLipLatitude;
            north[i] = sample.hasNorthWall ? sample.northWallLatitude : MissingLipLatitude;
        }

        plan.cliffLipStudyLongitudes = lons;
        plan.cliffLipLatitudes = south;
        plan.northLipStudyLongitudes = (float[])lons.Clone();
        plan.northLipLatitudes = north;
        BridgeLipGaps(plan.cliffLipStudyLongitudes, plan.cliffLipLatitudes, wrap: true);
        BridgeLipGaps(plan.northLipStudyLongitudes, plan.northLipLatitudes, wrap: true);
        if (plan.cliffSpanDegrees < 8f)
            plan.cliffSpanDegrees = DefaultSpanDegrees;
    }

    public static void BridgeLipGaps(float[] lons, float[] lats, bool wrap)
    {
        if (lons == null || lats == null || lons.Length != lats.Length || lats.Length < 3)
            return;

        int n = lats.Length;
        var valid = new List<int>();
        for (int i = 0; i < n; i++)
        {
            if (HasLip(lats[i]))
                valid.Add(i);
        }

        if (valid.Count < 2)
            return;

        int pairs = wrap ? valid.Count : valid.Count - 1;
        for (int p = 0; p < pairs; p++)
        {
            int i0 = valid[p];
            int i1 = valid[(p + 1) % valid.Count];
            float lon0 = lons[i0];
            float lon1 = lons[i1];
            float span = wrap
                ? Mathf.Repeat(lon1 - lon0 + 360f, 360f)
                : lon1 - lon0;
            if (span <= 0.0001f || span > MaxLipBridgeDegrees)
                continue;

            int k = i0 + 1;
            if (wrap)
                k = (i0 + 1) % n;
            while (k != i1)
            {
                float lonK = lons[k];
                float t;
                if (wrap && lon1 < lon0 - 0.0001f)
                {
                    float pos = lonK < lon0 - 0.0001f ? lonK + 360f : lonK;
                    t = Mathf.InverseLerp(lon0, lon1 + 360f, pos);
                }
                else
                    t = Mathf.InverseLerp(lon0, lon1, lonK);

                lats[k] = Mathf.Lerp(lats[i0], lats[i1], Mathf.Clamp01(t));
                if (!wrap)
                {
                    k++;
                    if (k >= n)
                        break;
                }
                else
                {
                    k = (k + 1) % n;
                    if (k == i0)
                        break;
                }
            }
        }
    }

    static void CaptureSectorLips(PlanetTileMap.TerrainWorkPlan plan, NyxaraA2BoundaryOverlay overlay, bool north)
    {
        int n = 0;
        var lons = new float[overlay.meridians.Length];
        var lats = new float[overlay.meridians.Length];
        bool optional = plan.useOptionalSouthCliffShift;
        for (int i = 0; i < overlay.meridians.Length; i++)
        {
            var sample = overlay.meridians[i];
            if (north)
            {
                if (!sample.hasNorthWall)
                    continue;
                lons[n] = sample.studyLongitude;
                lats[n] = sample.northWallLatitude;
            }
            else
            {
                if (!sample.hasSouthWall)
                    continue;
                lons[n] = sample.studyLongitude;
                lats[n] = optional ? sample.optionalSouthLatitude : sample.southWallLatitude;
            }

            n++;
        }

        if (n < 2)
        {
            if (!north)
                FillFallbackLip(plan);
            return;
        }

        Array.Resize(ref lons, n);
        Array.Resize(ref lats, n);
        if (north)
        {
            plan.northLipStudyLongitudes = lons;
            plan.northLipLatitudes = lats;
        }
        else
        {
            plan.cliffLipStudyLongitudes = lons;
            plan.cliffLipLatitudes = lats;
        }
    }

    public static void FillFallbackLip(PlanetTileMap.TerrainWorkPlan plan)
    {
        if (plan == null || plan.coverFullRing)
            return;
        int n = FallbackAuthoredSouthLatitudes.Length;
        var lons = new float[n];
        for (int i = 0; i < n; i++)
            lons[i] = 20f + i * 0.5f;
        plan.cliffLipStudyLongitudes = lons;
        plan.cliffLipLatitudes = (float[])FallbackAuthoredSouthLatitudes.Clone();
        if (plan.cliffSpanDegrees < 8f)
            plan.cliffSpanDegrees = DefaultSpanDegrees;
    }

    public static float SampleLipLatitude(PlanetTileMap.TerrainWorkPlan plan, float studyLon)
    {
        if (plan != null &&
            plan.cliffLipStudyLongitudes != null &&
            plan.cliffLipLatitudes != null &&
            plan.cliffLipStudyLongitudes.Length == plan.cliffLipLatitudes.Length &&
            plan.cliffLipStudyLongitudes.Length >= 2)
        {
            if (TrySampleLipLatitude(
                    plan.cliffLipStudyLongitudes,
                    plan.cliffLipLatitudes,
                    studyLon,
                    plan.coverFullRing,
                    out float lip))
                return lip;
            if (plan.coverFullRing)
                return 0f;
        }

        float t = (studyLon - 20f) / 30f * (FallbackAuthoredSouthLatitudes.Length - 1);
        int i = Mathf.Clamp(Mathf.FloorToInt(t), 0, FallbackAuthoredSouthLatitudes.Length - 2);
        float f = t - i;
        return Mathf.Lerp(FallbackAuthoredSouthLatitudes[i], FallbackAuthoredSouthLatitudes[i + 1], f);
    }

    public static float SampleNorthLipLatitude(PlanetTileMap.TerrainWorkPlan plan, float studyLon)
    {
        if (plan != null &&
            plan.northLipStudyLongitudes != null &&
            plan.northLipLatitudes != null &&
            plan.northLipStudyLongitudes.Length == plan.northLipLatitudes.Length &&
            plan.northLipStudyLongitudes.Length >= 2)
        {
            if (TrySampleLipLatitude(
                    plan.northLipStudyLongitudes,
                    plan.northLipLatitudes,
                    studyLon,
                    plan.coverFullRing,
                    out float lip))
                return lip;
            if (plan.coverFullRing)
                return 0f;
        }

        return NyxaraA2NorthRidgeMeshBuilder.SampleNorthLipLatitude(studyLon);
    }

    public static float LipPresence(float[] lons, float[] lats, float studyLon, bool wrap)
    {
        if (lons == null || lats == null || lons.Length != lats.Length || lons.Length == 0)
            return 0f;
        if (lons.Length == 1)
            return HasLip(lats[0]) ? 1f : 0f;

        FindBracket(lons, studyLon, wrap, out int i0, out int i1, out float t);
        float p0 = HasLip(lats[i0]) ? 1f : 0f;
        float p1 = HasLip(lats[i1]) ? 1f : 0f;
        return Mathf.Lerp(p0, p1, t);
    }

    public static bool TrySampleLipLatitude(
        float[] lons,
        float[] lats,
        float studyLon,
        bool wrap,
        out float latitude)
    {
        latitude = 0f;
        if (lons == null || lats == null || lons.Length != lats.Length || lons.Length == 0)
            return false;

        FindBracket(lons, studyLon, wrap, out int i0, out int i1, out float t);
        bool h0 = HasLip(lats[i0]);
        bool h1 = HasLip(lats[i1]);
        if (h0 && h1)
        {
            latitude = Mathf.Lerp(lats[i0], lats[i1], t);
            return true;
        }

        if (h0)
        {
            latitude = lats[i0];
            return true;
        }

        if (h1)
        {
            latitude = lats[i1];
            return true;
        }

        return false;
    }

    /// <summary>
    /// Negative radial offset (toward planet center) for the south basin. Zero on the
    /// playable side of the south rim. In full-ring mode the floor stays at pit depth
    /// from the steep drop all the way to the south pole (one southern region). A2-only
    /// still returns to the sphere south of <see cref="PlanetTileMap.TerrainWorkPlan.sectorLatitudeMin"/>.
    /// </summary>
    public static float RadialOffset(PlanetTileMap.TerrainWorkPlan plan, float studyLon, float studyLat)
    {
        if (!PlanApplies(plan))
            return 0f;

        float fade;
        float lip;
        if (plan.coverFullRing)
        {
            if (!TryInterpolateValidLip(
                    plan.cliffLipStudyLongitudes,
                    plan.cliffLipLatitudes,
                    studyLon,
                    wrap: true,
                    out lip))
                return 0f;
            fade = 1f;
        }
        else
        {
            fade = LonEdgeFade(studyLon, plan.studyLongitudeMin, plan.studyLongitudeMax);
            if (fade <= 0.0001f)
                return 0f;
            lip = SampleLipLatitude(plan, studyLon);
        }

        if (studyLat >= lip - LipBlendDegrees)
            return 0f;

        float span = Mathf.Max(8f, plan.cliffSpanDegrees);
        float southOfLip = (lip - LipBlendDegrees) - studyLat;
        float t = southOfLip <= 0f ? 0f : Mathf.Clamp01(southOfLip / span);
        float depth = Mathf.Lerp(
            plan.cliffDepth,
            Mathf.Max(plan.cliffDepth, plan.cliffDepthMax),
            Smooth01((t - 0.45f) / 0.4f));
        float drop = depth * DropShape(t);

        if (!plan.coverFullRing)
        {
            float southLimit = plan.sectorLatitudeMin;
            if (studyLat <= southLimit - SouthReturnDegrees)
                return 0f;
            if (studyLat < southLimit)
            {
                float back = Smooth01((southLimit - studyLat) / SouthReturnDegrees);
                drop *= 1f - back;
            }
        }

        return -drop * fade;
    }

    /// <summary>
    /// South-rim latitude at this longitude. Uses authored walls when present, otherwise
    /// interpolates across gaps so the southern basin is one continuous floor.
    /// </summary>
    public static bool TryInterpolateValidLip(
        float[] lons,
        float[] lats,
        float studyLon,
        bool wrap,
        out float latitude)
    {
        if (TrySampleLipLatitude(lons, lats, studyLon, wrap, out latitude))
            return true;

        latitude = 0f;
        if (!wrap || lons == null || lats == null || lons.Length != lats.Length || lons.Length == 0)
            return false;

        studyLon = WrapStudyLon(studyLon);
        int n = lons.Length;
        int iLeft = -1;
        int iRight = -1;
        float bestLeft = 1e9f;
        float bestRight = 1e9f;
        for (int i = 0; i < n; i++)
        {
            if (!HasLip(lats[i]))
                continue;
            float east = Mathf.Repeat(lons[i] - studyLon, 360f);
            float west = Mathf.Repeat(studyLon - lons[i], 360f);
            if (east < bestRight)
            {
                bestRight = east;
                iRight = i;
            }

            if (west < bestLeft)
            {
                bestLeft = west;
                iLeft = i;
            }
        }

        if (iLeft < 0 && iRight < 0)
            return false;
        if (iLeft < 0)
        {
            latitude = lats[iRight];
            return true;
        }

        if (iRight < 0)
        {
            latitude = lats[iLeft];
            return true;
        }

        float span = bestLeft + bestRight;
        float t = span > 0.0001f ? bestLeft / span : 0f;
        latitude = Mathf.Lerp(lats[iLeft], lats[iRight], t);
        return true;
    }

    public static float LonEdgeFade(float studyLon, float lon0, float lon1)
    {
        float span = lon1 - lon0;
        if (span <= 0.0001f)
            return 0f;
        float t = (studyLon - lon0) / span;
        if (t <= 0f || t >= 1f)
            return 0f;
        float edge = LonFadeFraction;
        return Smooth01(t / edge) * Smooth01((1f - t) / edge);
    }

    /// <summary>0 at the lip, 1 at full depth. Steep drop just south of the wall, then a floor.</summary>
    public static float DropShape(float t)
    {
        t = Mathf.Clamp01(t);
        if (t < 0.02f)
            return 0f;
        if (t < 0.07f)
            return Mathf.Lerp(0f, 0.62f, (t - 0.02f) / 0.05f);
        if (t < 0.16f)
            return Mathf.Lerp(0.62f, 0.90f, (t - 0.07f) / 0.09f);
        if (t < 0.32f)
            return Mathf.Lerp(0.90f, 0.97f, (t - 0.16f) / 0.16f);
        return Mathf.Lerp(0.97f, 1f, (t - 0.32f) / 0.68f);
    }

    static void FindBracket(float[] xs, float x, bool wrap, out int i0, out int i1, out float t)
    {
        int n = xs.Length;
        if (n == 1)
        {
            i0 = 0;
            i1 = 0;
            t = 0f;
            return;
        }

        if (wrap)
        {
            x = WrapStudyLon(x);
            if (x <= xs[0] || x >= xs[n - 1])
            {
                i0 = n - 1;
                i1 = 0;
                float span = (xs[0] + 360f) - xs[n - 1];
                float pos = x <= xs[0] ? (x + 360f) - xs[n - 1] : x - xs[n - 1];
                t = span > 0.0001f ? Mathf.Clamp01(pos / span) : 0f;
                return;
            }
        }
        else
        {
            if (x <= xs[0])
            {
                i0 = 0;
                i1 = 0;
                t = 0f;
                return;
            }

            if (x >= xs[n - 1])
            {
                i0 = n - 1;
                i1 = n - 1;
                t = 0f;
                return;
            }
        }

        for (int i = 0; i < n - 1; i++)
        {
            if (x <= xs[i + 1])
            {
                i0 = i;
                i1 = i + 1;
                float span = xs[i + 1] - xs[i];
                t = span > 0.0001f ? Mathf.Clamp01((x - xs[i]) / span) : 0f;
                return;
            }
        }

        i0 = n - 1;
        i1 = wrap ? 0 : n - 1;
        t = 0f;
    }

    public static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }
}
