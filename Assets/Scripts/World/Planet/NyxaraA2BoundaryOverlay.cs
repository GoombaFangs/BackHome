using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stage 3 A2 bound analysis. Samples the real walk surface against authored BoxColliders
/// (full parent TRS). Does not move walls, triggers, or enemies.
/// </summary>
[Serializable]
public class NyxaraA2BoundaryOverlay
{
    public enum SouthBoundaryChoice
    {
        AuthoredWalls = 0,
        OptionalTriggerClearance = 1
    }

    [Serializable]
    public class MeridianSample
    {
        public float studyLongitude;
        public float tileMapLongitude;
        public float walkRadius;
        public bool hasNorthWall;
        public bool hasSouthWall;
        public bool hasTrigger;
        public float northWallLatitude;
        public float southWallLatitude;
        public float triggerNorthLatitude;
        public float triggerSouthLatitude;
        public float optionalSouthLatitude;
        public float northMarginLatitude;
        public float southMarginLatitude;
        public float southOverlapArcUnits;
        public string northWallName;
        public string southWallName;
    }

    [Serializable]
    public class OpeningReport
    {
        public string label;
        public float studyLongitude;
        public float northInnerLatitude;
        public float southInnerLatitude;
        public float openingArcUnits;
        public float openingArcUnitsInsideMargin;
        public string northWallName;
        public string southWallName;
        public bool clear;
    }

    [Serializable]
    public class CorridorReport
    {
        public string towardArea;
        public float startStudyLongitude;
        public float endStudyLongitude;
        public float midLatitude;
        public float surfaceArcUnits;
        public bool reachedAdjacentTrigger;
        public bool blockedByWall;
        public string blockedByWallName;
        public string notes;
    }

    [Serializable]
    public class SummaryReport
    {
        public string groundSource;
        public bool usedMeshCollider;
        public bool heightmapAffectsRadius;
        public float minWalkRadius;
        public float maxWalkRadius;
        public int meridianCount;
        public int triggerSouthOfWallCount;
        public float maxSouthOverlapArcUnits;
        public float maxSouthOverlapDegrees;
        public float fitPackProposedShiftUnits = 7.487f;
        public float combatMarginUnits;
        public string activeSouthBoundary;
        public string recommendation;
        public string wallVerdict;
    }

    [Header("Choice")]
    [Tooltip("Gameplay south lip for later stages. Authored walls is the default. Optional clearance is visualization only and does not move colliders.")]
    public SouthBoundaryChoice activeSouthBoundary = SouthBoundaryChoice.AuthoredWalls;

    [Header("Visibility")]
    public bool showWalkable = true;
    public bool showTrigger = true;
    public bool showNorthBoundary = true;
    public bool showSouthBoundary = true;
    public bool showOptionalSouth = true;
    public bool showOpenings = true;
    public bool showCombatMargin = true;
    public bool showLabels = true;
    public bool showWorkPlanFrame = false;

    [HideInInspector] public bool wrapLongitude;

    [Header("Sampling")]
    [Range(16, 361)] public int longitudeSteps = 61;
    [Range(120, 1201)] public int latitudeSteps = 801;
    public float latitudeScanMin = -50f;
    public float latitudeScanMax = 50f;
    public float gizmoLift = 0.45f;

    [HideInInspector] public MeridianSample[] meridians = Array.Empty<MeridianSample>();
    [HideInInspector] public OpeningReport westOpening = new OpeningReport();
    [HideInInspector] public OpeningReport eastOpening = new OpeningReport();
    [HideInInspector] public CorridorReport westCorridor = new CorridorReport();
    [HideInInspector] public CorridorReport eastCorridor = new CorridorReport();
    [HideInInspector] public SummaryReport summary = new SummaryReport();

    public bool HasSamples => meridians != null && meridians.Length > 0;

    public void Rebuild(SphericalPlanet planet, PlanetTileMap tileMap, PlanetTileMap.TerrainWorkPlan plan)
    {
        meridians = Array.Empty<MeridianSample>();
        summary = new SummaryReport();
        if (planet == null || plan == null)
            return;

        BoxCollider a2 = null;
        BoxCollider a1 = null;
        BoxCollider r1 = null;
        var walls = new List<BoxCollider>(24);
        CollectColliders(planet.transform, ref a2, ref a1, ref r1, walls);

        MeshCollider walkMesh = tileMap != null ? tileMap.WalkMeshCollider : null;
        bool usedMesh = walkMesh != null && walkMesh.enabled && walkMesh.sharedMesh != null;
        bool heightmap = planet.HasHeightTerrain;
        string groundSource = usedMesh
            ? "PlanetTileMap MeshCollider raycast"
            : tileMap != null
                ? "PlanetTileMap.GetWalkSurfacePoint"
                : "SphericalPlanet.GetTerrainRadius";

        int lonCount = Mathf.Max(2, longitudeSteps);
        int latCount = Mathf.Max(32, latitudeSteps);
        wrapLongitude = plan.coverFullRing;
        float lon0 = wrapLongitude ? -180f : plan.studyLongitudeMin;
        float lon1 = wrapLongitude ? 180f : plan.studyLongitudeMax;
        float margin = Mathf.Max(0f, plan.playableMargin);
        var samples = new MeridianSample[lonCount];

        float minR = float.MaxValue;
        float maxR = 0f;
        int overlapCount = 0;
        float maxOverlapArc = 0f;
        float maxOverlapDeg = 0f;

        for (int i = 0; i < lonCount; i++)
        {
            float studyLon = wrapLongitude
                ? -180f + 360f * i / lonCount
                : Mathf.Lerp(lon0, lon1, i / (float)(lonCount - 1));
            var sample = SampleMeridian(
                planet, tileMap, walkMesh, usedMesh,
                walls, a2, studyLon, latCount, margin);
            samples[i] = sample;
            minR = Mathf.Min(minR, sample.walkRadius);
            maxR = Mathf.Max(maxR, sample.walkRadius);
            if (sample.southOverlapArcUnits > 0.05f)
            {
                overlapCount++;
                if (sample.southOverlapArcUnits > maxOverlapArc)
                {
                    maxOverlapArc = sample.southOverlapArcUnits;
                    maxOverlapDeg = sample.hasTrigger && sample.hasSouthWall
                        ? sample.southWallLatitude - sample.triggerSouthLatitude
                        : 0f;
                }
            }
        }

        StampWallsOnMeridians(planet, walls, samples);
        FillShortLipGaps(samples, wrapLongitude);
        meridians = samples;
        if (wrapLongitude)
        {
            westOpening = new OpeningReport { label = "Ring (no A2 sector cut)" };
            eastOpening = new OpeningReport { label = "Ring (no A2 sector cut)" };
            westCorridor = new CorridorReport { towardArea = "Ring", notes = "Full-ring overlay: N-S corridors stay open wherever a meridian has no north or south wall." };
            eastCorridor = new CorridorReport { towardArea = "Ring", notes = "Ridge only north of a north wall. Cliff only south of a south wall." };
        }
        else
        {
            westOpening = BuildOpening("West toward R1", samples[0]);
            eastOpening = BuildOpening("East toward A1", samples[lonCount - 1]);
            westCorridor = ProbeCorridor(
                planet, tileMap, walkMesh, usedMesh, walls, r1,
                samples[0], -0.35f, 90, "R1");
            eastCorridor = ProbeCorridor(
                planet, tileMap, walkMesh, usedMesh, walls, a1,
                samples[lonCount - 1], 0.35f, 90, "A1");
        }

        bool keepAuthored = activeSouthBoundary == SouthBoundaryChoice.AuthoredWalls;
        summary.groundSource = groundSource;
        summary.usedMeshCollider = usedMesh;
        summary.heightmapAffectsRadius = heightmap;
        summary.minWalkRadius = minR < float.MaxValue ? minR : planet.Radius;
        summary.maxWalkRadius = maxR;
        summary.meridianCount = lonCount;
        summary.triggerSouthOfWallCount = overlapCount;
        summary.maxSouthOverlapArcUnits = maxOverlapArc;
        summary.maxSouthOverlapDegrees = maxOverlapDeg;
        summary.combatMarginUnits = margin;
        summary.activeSouthBoundary = keepAuthored ? "AuthoredWalls" : "OptionalTriggerClearance";
        if (wrapLongitude)
        {
            summary.wallVerdict =
                "Every Borders non-trigger BoxCollider is sampled. North vs south is the wall's planet-local latitude (center ≥ 0° = north). Inner lips are the lowest north hit and highest south hit on each meridian.";
            summary.recommendation = keepAuthored
                ? "Full ring: mountain north of each north wall, cliff south of each south wall. Meridians with no wall stay open. Do not apply the fit-pack ~7.49 unit south shift."
                : "Optional trigger-clearance south lip is visualization only. Walls stay in place.";
        }
        else
        {
            summary.wallVerdict =
                "Cube (2) and Cube (7) are Ground-layer non-trigger BoxColliders. PlanetWalker capsule-casts them, so they are the authored gameplay south edge. A2 is a named trigger volume with no gameplay script and is not the walkable floor.";
            summary.recommendation = keepAuthored
                ? "Keep the authored south wall. Do not apply the fit-pack ~7.49 unit south shift. The A2 trigger overlapping Cube (2) is a volume marker biting through the cliff wall, not evidence that the wall is misplaced. Show the optional clearance as an alternate overlay only."
                : "Optional trigger-clearance south lip is selected for later mesh tests only. Walls, triggers, and enemies stay in place until a later stage explicitly moves collision.";
            westCorridor.notes = westCorridor.reachedAdjacentTrigger
                ? "N-S corridor stays open westward into the R1 trigger."
                : westCorridor.blockedByWall
                    ? "West corridor hit a wall before R1 at the sampled mid-latitude."
                    : "West corridor stayed open for the sample limit (passage toward R1).";
            eastCorridor.notes = eastCorridor.reachedAdjacentTrigger
                ? "N-S corridor reaches the A1 trigger."
                : "East exit is the N-S opening between Cube (3) and Cube (2), not a hole at the A2 trigger center. Cube (2) sits on the A2 volume latitude.";
        }
    }

    public void Draw(SphericalPlanet planet, PlanetTileMap.TerrainWorkPlan plan)
    {
        if (planet == null || !HasSamples)
            return;

#if UNITY_EDITOR
        float lift = gizmoLift;
        Color walk = new Color(0.35f, 0.78f, 0.38f, 0.95f);
        Color trigger = new Color(0.25f, 0.7f, 1f, 0.95f);
        Color north = new Color(0.78f, 0.45f, 0.95f, 0.95f);
        Color south = new Color(0.95f, 0.48f, 0.18f, 0.95f);
        Color optional = new Color(1f, 1f, 1f, 0.9f);
        Color overlap = new Color(0.95f, 0.2f, 0.25f, 0.85f);
        Color opening = new Color(0.55f, 1f, 0.35f, 1f);
        Color marginCol = new Color(0.98f, 0.86f, 0.25f, 0.95f);

        bool authoredActive = activeSouthBoundary == SouthBoundaryChoice.AuthoredWalls;

        if (showWalkable)
            DrawBand(planet, s => s.hasNorthWall && s.hasSouthWall, s => s.northMarginLatitude, s => s.southMarginLatitude, walk, lift, 3);

        if (showTrigger)
            DrawBand(planet, s => s.hasTrigger, s => s.triggerNorthLatitude, s => s.triggerSouthLatitude, trigger, lift + 0.12f, 2);

        if (showCombatMargin)
        {
            DrawPolyline(planet, s => s.hasNorthWall, s => s.northMarginLatitude, marginCol, lift + 0.2f, dotted: true);
            DrawPolyline(planet, s => s.hasSouthWall, s => s.southMarginLatitude, marginCol, lift + 0.2f, dotted: true);
        }

        if (showNorthBoundary)
            DrawPolyline(planet, s => s.hasNorthWall, s => s.northWallLatitude, north, lift + 0.25f, dotted: false);

        if (showSouthBoundary)
            DrawPolyline(planet, s => s.hasSouthWall, s => s.southWallLatitude, south, lift + 0.25f, dotted: !authoredActive);

        if (showOptionalSouth)
            DrawPolyline(planet, s => s.hasSouthWall, s => s.optionalSouthLatitude, optional, lift + 0.3f, dotted: authoredActive);

        if (showSouthBoundary)
            DrawOverlapHatch(planet, overlap, lift + 0.18f);

        if (showOpenings)
        {
            DrawOpeningMeridian(planet, westOpening, opening, lift + 0.35f);
            DrawOpeningMeridian(planet, eastOpening, opening, lift + 0.35f);
        }

        if (showWorkPlanFrame && plan != null)
            PlanetTileMap.DrawWorkPlanGizmos(planet, plan);

        UnityEditor.Handles.color = new Color(0.35f, 0.9f, 0.45f, 0.95f);
        Vector3 northAxis = planet.PlanetLocalToWorld.MultiplyPoint3x4(
            Vector3.up * (planet.Radius + Mathf.Max(4f, plan != null ? plan.ridgeHeight : 4f)));
        UnityEditor.Handles.DrawLine(planet.Center, northAxis);

        if (showLabels)
            DrawLabels(planet, lift + 0.6f);
#endif
    }

    static void CollectColliders(
        Transform root,
        ref BoxCollider a2,
        ref BoxCollider a1,
        ref BoxCollider r1,
        List<BoxCollider> walls)
    {
        var boxes = root.GetComponentsInChildren<BoxCollider>(true);
        for (int i = 0; i < boxes.Length; i++)
        {
            BoxCollider box = boxes[i];
            if (box == null || !box.enabled)
                continue;
            string n = box.gameObject.name;
            if (n == "A2")
                a2 = box;
            else if (n == "A1")
                a1 = box;
            else if (n == "R1")
                r1 = box;
            else if (!box.isTrigger && HasAncestorNamed(box.transform, "Borders"))
                walls.Add(box);
        }
    }

    static bool HasAncestorNamed(Transform t, string name)
    {
        Transform p = t.parent;
        while (p != null)
        {
            if (p.name == name)
                return true;
            p = p.parent;
        }
        return false;
    }

    MeridianSample SampleMeridian(
        SphericalPlanet planet,
        PlanetTileMap tileMap,
        MeshCollider walkMesh,
        bool usedMesh,
        List<BoxCollider> walls,
        BoxCollider a2,
        float studyLon,
        int latCount,
        float margin)
    {
        var sample = new MeridianSample
        {
            studyLongitude = studyLon,
            northWallName = "",
            southWallName = ""
        };
        PlanetTileMap.DirectionToTileMapLonLat(
            PlanetTileMap.StudyLonLatToDirection(studyLon, 0f),
            out sample.tileMapLongitude,
            out _);

        float northMin = 90f;
        float southMax = -90f;
        float trigNorth = -90f;
        float trigSouth = 90f;
        string northName = "";
        string southName = "";
        float radiusSum = 0f;
        int radiusHits = 0;

        for (int j = 0; j < latCount; j++)
        {
            float u = j / (float)(latCount - 1);
            float lat = Mathf.Lerp(latitudeScanMin, latitudeScanMax, u);
            Vector3 world = SurfacePoint(planet, tileMap, walkMesh, usedMesh, studyLon, lat, out float radius);
            radiusSum += radius;
            radiusHits++;

            if (a2 != null && ContainsBox(a2, world))
            {
                sample.hasTrigger = true;
                trigNorth = Mathf.Max(trigNorth, lat);
                trigSouth = Mathf.Min(trigSouth, lat);
            }

            for (int w = 0; w < walls.Count; w++)
            {
                BoxCollider wall = walls[w];
                if (!ContainsBox(wall, world))
                    continue;
                string name = wall.gameObject.name;
                if (IsNorthSideWall(wall, planet))
                {
                    sample.hasNorthWall = true;
                    if (lat < northMin)
                    {
                        northMin = lat;
                        northName = name;
                    }
                }
                else
                {
                    sample.hasSouthWall = true;
                    if (lat > southMax)
                    {
                        southMax = lat;
                        southName = name;
                    }
                }
            }
        }

        sample.walkRadius = radiusHits > 0 ? radiusSum / radiusHits : planet.Radius;
        float marginDeg = margin / Mathf.Max(0.01f, sample.walkRadius) * Mathf.Rad2Deg;

        if (sample.hasNorthWall)
        {
            sample.northWallLatitude = northMin;
            sample.northWallName = northName;
            sample.northMarginLatitude = northMin - marginDeg;
        }

        if (sample.hasSouthWall)
        {
            sample.southWallLatitude = southMax;
            sample.southWallName = southName;
            sample.southMarginLatitude = southMax + marginDeg;
        }

        if (sample.hasTrigger)
        {
            sample.triggerNorthLatitude = trigNorth;
            sample.triggerSouthLatitude = trigSouth;
        }

        sample.optionalSouthLatitude = sample.southWallLatitude;
        if (sample.hasTrigger && sample.hasSouthWall && trigSouth < southMax - 0.02f)
        {
            sample.southOverlapArcUnits = (southMax - trigSouth) * Mathf.Deg2Rad * sample.walkRadius;
            sample.optionalSouthLatitude = trigSouth - marginDeg;
        }
        else if (sample.hasSouthWall)
        {
            sample.southOverlapArcUnits = 0f;
        }

        return sample;
    }

    static bool IsNorthSideWall(BoxCollider wall, SphericalPlanet planet)
    {
        if (wall == null || planet == null)
            return false;
        Vector3 world = wall.transform.TransformPoint(wall.center);
        Vector3 local = planet.WorldToPlanetLocal.MultiplyPoint3x4(world);
        if (local.sqrMagnitude < 0.0001f)
            return true;
        return local.normalized.y >= 0f;
    }

    static void StampWallsOnMeridians(
        SphericalPlanet planet,
        List<BoxCollider> walls,
        MeridianSample[] samples)
    {
        if (planet == null || walls == null || samples == null || samples.Length == 0)
            return;

        for (int i = 0; i < samples.Length; i++)
        {
            MeridianSample sample = samples[i];
            float lon = sample.studyLongitude;
            float northInner = 90f;
            float southInner = -90f;
            for (int w = 0; w < walls.Count; w++)
            {
                BoxCollider box = walls[w];
                if (box == null)
                    continue;
                bool north = IsNorthSideWall(box, planet);
                if (!NyxaraRouteBounds.TryInnerLatitudeAtLon(box, planet, lon, north, out float inner))
                    continue;
                if (north)
                {
                    if (!sample.hasNorthWall || inner < northInner)
                    {
                        sample.hasNorthWall = true;
                        northInner = inner;
                        sample.northWallLatitude = inner;
                        sample.northWallName = box.gameObject.name;
                    }
                }
                else if (!sample.hasSouthWall || inner > southInner)
                {
                    sample.hasSouthWall = true;
                    southInner = inner;
                    sample.southWallLatitude = inner;
                    sample.southWallName = box.gameObject.name;
                    sample.optionalSouthLatitude = inner;
                }
            }

            samples[i] = sample;
        }
    }

    static void FillShortLipGaps(MeridianSample[] samples, bool wrap)
    {
        if (samples == null || samples.Length < 3)
            return;
        FillShortLipGapsOneSide(samples, wrap, north: true);
        FillShortLipGapsOneSide(samples, wrap, north: false);
    }

    static void FillShortLipGapsOneSide(MeridianSample[] samples, bool wrap, bool north)
    {
        int n = samples.Length;
        var valid = new List<int>();
        for (int i = 0; i < n; i++)
        {
            if (north ? samples[i].hasNorthWall : samples[i].hasSouthWall)
                valid.Add(i);
        }

        if (valid.Count < 2)
            return;

        int pairs = wrap ? valid.Count : valid.Count - 1;
        for (int p = 0; p < pairs; p++)
        {
            int i0 = valid[p];
            int i1 = valid[(p + 1) % valid.Count];
            float lon0 = samples[i0].studyLongitude;
            float lon1 = samples[i1].studyLongitude;
            float span = wrap
                ? Mathf.Repeat(lon1 - lon0 + 360f, 360f)
                : lon1 - lon0;
            if (span <= 0.0001f || span > NyxaraA2CliffProfile.MaxLipBridgeDegrees)
                continue;

            float lat0 = north ? samples[i0].northWallLatitude : samples[i0].southWallLatitude;
            float lat1 = north ? samples[i1].northWallLatitude : samples[i1].southWallLatitude;
            int k = wrap ? (i0 + 1) % n : i0 + 1;
            while (k != i1)
            {
                float lonK = samples[k].studyLongitude;
                float t;
                if (wrap && lon1 < lon0 - 0.0001f)
                {
                    float pos = lonK < lon0 - 0.0001f ? lonK + 360f : lonK;
                    t = Mathf.InverseLerp(lon0, lon1 + 360f, pos);
                }
                else
                    t = Mathf.InverseLerp(lon0, lon1, lonK);

                float lat = Mathf.Lerp(lat0, lat1, Mathf.Clamp01(t));
                MeridianSample sample = samples[k];
                if (north)
                {
                    sample.hasNorthWall = true;
                    sample.northWallLatitude = lat;
                    sample.northWallName = "bridged";
                }
                else
                {
                    sample.hasSouthWall = true;
                    sample.southWallLatitude = lat;
                    sample.optionalSouthLatitude = lat;
                    sample.southWallName = "bridged";
                }

                samples[k] = sample;
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

    CorridorReport ProbeCorridor(
        SphericalPlanet planet,
        PlanetTileMap tileMap,
        MeshCollider walkMesh,
        bool usedMesh,
        List<BoxCollider> walls,
        BoxCollider adjacent,
        MeridianSample start,
        float lonStepDeg,
        int maxSteps,
        string towardName)
    {
        var report = new CorridorReport
        {
            towardArea = towardName,
            startStudyLongitude = start.studyLongitude,
            midLatitude = start.hasNorthWall && start.hasSouthWall
                ? 0.5f * (start.northMarginLatitude + start.southMarginLatitude)
                : -5.25f,
            blockedByWallName = ""
        };

        float lon = start.studyLongitude;
        float travelledDeg = 0f;
        float radius = Mathf.Max(0.01f, start.walkRadius);
        for (int i = 0; i < maxSteps; i++)
        {
            lon += lonStepDeg;
            travelledDeg += Mathf.Abs(lonStepDeg);
            Vector3 world = SurfacePoint(planet, tileMap, walkMesh, usedMesh, lon, report.midLatitude, out radius);
            if (adjacent != null && ContainsBox(adjacent, world))
            {
                report.reachedAdjacentTrigger = true;
                report.endStudyLongitude = lon;
                report.surfaceArcUnits = travelledDeg * Mathf.Deg2Rad * radius;
                return report;
            }

            for (int w = 0; w < walls.Count; w++)
            {
                if (!ContainsBox(walls[w], world))
                    continue;
                report.blockedByWall = true;
                report.blockedByWallName = walls[w].gameObject.name;
                report.endStudyLongitude = lon;
                report.surfaceArcUnits = travelledDeg * Mathf.Deg2Rad * radius;
                return report;
            }
        }

        report.endStudyLongitude = lon;
        report.surfaceArcUnits = travelledDeg * Mathf.Deg2Rad * radius;
        return report;
    }

    static OpeningReport BuildOpening(string label, MeridianSample sample)
    {
        var report = new OpeningReport
        {
            label = label,
            studyLongitude = sample.studyLongitude,
            northInnerLatitude = sample.northWallLatitude,
            southInnerLatitude = sample.southWallLatitude,
            northWallName = sample.northWallName,
            southWallName = sample.southWallName,
            clear = sample.hasNorthWall && sample.hasSouthWall
                    && sample.northMarginLatitude > sample.southMarginLatitude
        };
        if (sample.hasNorthWall && sample.hasSouthWall)
        {
            float span = sample.northWallLatitude - sample.southWallLatitude;
            float inner = sample.northMarginLatitude - sample.southMarginLatitude;
            report.openingArcUnits = span * Mathf.Deg2Rad * sample.walkRadius;
            report.openingArcUnitsInsideMargin = Mathf.Max(0f, inner) * Mathf.Deg2Rad * sample.walkRadius;
        }

        return report;
    }

    static Vector3 SurfacePoint(
        SphericalPlanet planet,
        PlanetTileMap tileMap,
        MeshCollider walkMesh,
        bool usedMesh,
        float studyLon,
        float lat,
        out float radius)
    {
        Vector3 localDir = PlanetTileMap.StudyLonLatToDirection(studyLon, lat);
        Vector3 worldDir = planet.PlanetLocalToWorld.MultiplyVector(localDir);
        if (worldDir.sqrMagnitude < 0.0001f)
            worldDir = planet.transform.up;
        worldDir.Normalize();

        if (usedMesh)
        {
            float outer = planet.Radius + 48f;
            var ray = new Ray(planet.Center + worldDir * outer, -worldDir);
            if (walkMesh.Raycast(ray, out RaycastHit hit, outer + 48f))
            {
                radius = (hit.point - planet.Center).magnitude;
                return hit.point;
            }
        }

        Vector3 localPoint;
        if (tileMap != null)
        {
            radius = tileMap.GetWalkSurfaceRadius(localDir);
            localPoint = localDir * radius;
        }
        else
        {
            radius = planet.GetTerrainRadius(localDir);
            localPoint = localDir * radius;
        }

        return planet.PlanetLocalToWorld.MultiplyPoint3x4(localPoint);
    }

    static bool ContainsBox(BoxCollider box, Vector3 worldPoint)
    {
        Vector3 local = box.transform.InverseTransformPoint(worldPoint);
        Vector3 delta = local - box.center;
        Vector3 half = box.size * 0.5f;
        const float pad = 0.0002f;
        return Mathf.Abs(delta.x) <= half.x + pad
               && Mathf.Abs(delta.y) <= half.y + pad
               && Mathf.Abs(delta.z) <= half.z + pad;
    }

#if UNITY_EDITOR
    void DrawPolyline(
        SphericalPlanet planet,
        Func<MeridianSample, bool> mask,
        Func<MeridianSample, float> latOf,
        Color color,
        float lift,
        bool dotted)
    {
        Vector3 prev = Vector3.zero;
        bool hasPrev = false;
        for (int i = 0; i < meridians.Length; i++)
        {
            MeridianSample s = meridians[i];
            if (!mask(s))
            {
                hasPrev = false;
                continue;
            }

            Vector3 p = SurfaceLifted(planet, s.studyLongitude, latOf(s), lift);
            if (hasPrev)
            {
                UnityEditor.Handles.color = color;
                if (dotted)
                    UnityEditor.Handles.DrawDottedLine(prev, p, 3.5f);
                else
                    UnityEditor.Handles.DrawAAPolyLine(3f, prev, p);
            }

            prev = p;
            hasPrev = true;
        }

        if (wrapLongitude && meridians.Length >= 2)
        {
            MeridianSample first = meridians[0];
            MeridianSample last = meridians[meridians.Length - 1];
            if (mask(first) && mask(last))
            {
                Vector3 a = SurfaceLifted(planet, last.studyLongitude, latOf(last), lift);
                Vector3 b = SurfaceLifted(planet, first.studyLongitude, latOf(first), lift);
                UnityEditor.Handles.color = color;
                if (dotted)
                    UnityEditor.Handles.DrawDottedLine(a, b, 3.5f);
                else
                    UnityEditor.Handles.DrawAAPolyLine(3f, a, b);
            }
        }
    }

    void DrawBand(
        SphericalPlanet planet,
        Func<MeridianSample, bool> mask,
        Func<MeridianSample, float> northLat,
        Func<MeridianSample, float> southLat,
        Color color,
        float lift,
        int stride)
    {
        Color fill = new Color(color.r, color.g, color.b, 0.12f);
        int last = wrapLongitude ? meridians.Length : meridians.Length - 1;
        for (int i = 0; i < last; i += stride)
        {
            MeridianSample a = meridians[i];
            int next = wrapLongitude
                ? (i + stride) % meridians.Length
                : Mathf.Min(i + stride, meridians.Length - 1);
            MeridianSample b = meridians[next];
            if (!mask(a) || !mask(b))
                continue;
            Vector3 aN = SurfaceLifted(planet, a.studyLongitude, northLat(a), lift);
            Vector3 aS = SurfaceLifted(planet, a.studyLongitude, southLat(a), lift);
            Vector3 bN = SurfaceLifted(planet, b.studyLongitude, northLat(b), lift);
            Vector3 bS = SurfaceLifted(planet, b.studyLongitude, southLat(b), lift);
            UnityEditor.Handles.color = fill;
            UnityEditor.Handles.DrawAAConvexPolygon(aN, bN, bS, aS);
            UnityEditor.Handles.color = color;
            UnityEditor.Handles.DrawAAPolyLine(1.5f, aN, aS);
        }
    }

    void DrawOverlapHatch(SphericalPlanet planet, Color color, float lift)
    {
        for (int i = 0; i < meridians.Length; i++)
        {
            MeridianSample s = meridians[i];
            if (s.southOverlapArcUnits <= 0.05f || !s.hasTrigger || !s.hasSouthWall)
                continue;
            Vector3 wall = SurfaceLifted(planet, s.studyLongitude, s.southWallLatitude, lift);
            Vector3 trig = SurfaceLifted(planet, s.studyLongitude, s.triggerSouthLatitude, lift);
            UnityEditor.Handles.color = color;
            UnityEditor.Handles.DrawDottedLine(wall, trig, 2.5f);
        }
    }

    void DrawOpeningMeridian(SphericalPlanet planet, OpeningReport opening, Color color, float lift)
    {
        if (opening == null || !opening.clear)
            return;
        Vector3 n = SurfaceLifted(planet, opening.studyLongitude, opening.northInnerLatitude, lift);
        Vector3 s = SurfaceLifted(planet, opening.studyLongitude, opening.southInnerLatitude, lift);
        UnityEditor.Handles.color = color;
        UnityEditor.Handles.DrawAAPolyLine(4.5f, n, s);
        UnityEditor.Handles.Label(0.5f * (n + s), $"{opening.label}\n{opening.openingArcUnits:0.0} u");
    }

    void DrawLabels(SphericalPlanet planet, float lift)
    {
        MeridianSample mid = null;
        if (meridians != null)
        {
            for (int i = 0; i < meridians.Length; i++)
            {
                if (meridians[i].hasNorthWall && meridians[i].hasSouthWall)
                {
                    mid = meridians[i];
                    break;
                }
            }

            if (mid == null && meridians.Length > 0)
                mid = meridians[meridians.Length / 2];
        }

        if (mid == null)
            return;
        if (mid.hasNorthWall && showNorthBoundary)
        {
            UnityEditor.Handles.color = new Color(0.78f, 0.45f, 0.95f);
            UnityEditor.Handles.Label(
                SurfaceLifted(planet, mid.studyLongitude, mid.northWallLatitude, lift),
                "North wall (authored)");
        }

        if (mid.hasSouthWall && showSouthBoundary)
        {
            UnityEditor.Handles.color = new Color(0.95f, 0.48f, 0.18f);
            UnityEditor.Handles.Label(
                SurfaceLifted(planet, mid.studyLongitude, mid.southWallLatitude, lift),
                "South wall (authored, keep)");
        }

        if (showOptionalSouth)
        {
            for (int i = meridians.Length - 1; i >= 0; i--)
            {
                if (meridians[i].southOverlapArcUnits <= 0.05f)
                    continue;
                UnityEditor.Handles.color = Color.white;
                UnityEditor.Handles.Label(
                    SurfaceLifted(planet, meridians[i].studyLongitude, meridians[i].optionalSouthLatitude, lift),
                    $"Optional south (+{summary.maxSouthOverlapArcUnits:0.00} u, not applied)");
                break;
            }
        }

        if (mid.hasTrigger && showTrigger)
        {
            float lat = 0.5f * (mid.triggerNorthLatitude + mid.triggerSouthLatitude);
            UnityEditor.Handles.color = new Color(0.25f, 0.7f, 1f);
            UnityEditor.Handles.Label(
                SurfaceLifted(planet, mid.studyLongitude, lat, lift),
                "A2 trigger ∩ ground");
        }
    }

    Vector3 SurfaceLifted(SphericalPlanet planet, float studyLon, float lat, float lift)
    {
        Vector3 localDir = PlanetTileMap.StudyLonLatToDirection(studyLon, lat);
        float r = planet.Radius + lift;
        if (HasSamples)
        {
            // Prefer the sampled walk radius at the nearest meridian so gizmos sit on the real surface.
            float best = float.MaxValue;
            for (int i = 0; i < meridians.Length; i++)
            {
                float d = Mathf.Abs(meridians[i].studyLongitude - studyLon);
                if (d < best)
                {
                    best = d;
                    r = meridians[i].walkRadius + lift;
                }
            }
        }

        return planet.PlanetLocalToWorld.MultiplyPoint3x4(localDir * r);
    }
#endif
}
