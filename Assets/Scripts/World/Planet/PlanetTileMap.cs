using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Spherical terrain tilemap: paint terrain ids, autotile to tileset UVs, one material mesh.
/// </summary>
[ExecuteAlways]
[DefaultExecutionOrder(-50)]
[RequireComponent(typeof(SphericalPlanet))]
public class PlanetTileMap : MonoBehaviour
{
    [Serializable]
    public struct TileSample
    {
        public int tileIndex;
        public int terrainIndex;
        public string tileId;
        public bool walkable;
        public string zoneId;
    }

    /// <summary>
    /// Longitude convention used by sector bounds. Do not mix frames without converting.
    /// </summary>
    public enum LongitudeFrame
    {
        /// <summary>atan2(z, x), 0° at local +X, + toward +Z. Same as WorldToCell / LocalSurfacePoint.</summary>
        PlanetTileMap = 0,
        /// <summary>atan2(x, z), 0° at local +Z, + toward +X. Used by the A2 fit-study JSON.</summary>
        StudyFromPositiveZ = 1
    }

    /// <summary>
    /// Editable ridge/cliff planning. When enabled, tile generation lowers cells south of
    /// the serialized south lip so the cliff pit is not covered. Production planets leave this off.
    /// </summary>
    [Serializable]
    public class TerrainWorkPlan
    {
        [Tooltip("When off, tile generation ignores the cliff pit. Leave off on production planets.")]
        public bool enabled;

        [Tooltip("If true, ridge/cliff follow every north/south wall around the planet. Gaps without walls stay open.")]
        public bool coverFullRing;

        [Tooltip("Layout sector this plan is limited to. A2, or Ring when coverFullRing is on.")]
        public string sectorId = "A2";

        [Tooltip("Frame for sectorLongitudeMin/Max. Production math uses PlanetTileMap.")]
        public LongitudeFrame longitudeFrame = LongitudeFrame.PlanetTileMap;

        [Tooltip("Inclusive sector longitude in the frame above (PlanetTileMap: A2 ≈ 40°–70°).")]
        public float sectorLongitudeMin = 40f;

        [Tooltip("Inclusive sector longitude in the frame above (PlanetTileMap: A2 ≈ 40°–70°).")]
        public float sectorLongitudeMax = 70f;

        [Tooltip("Inclusive sector latitude. Same in both frames. +latitude is local +Y (north).")]
        public float sectorLatitudeMin = -28f;

        [Tooltip("Inclusive sector latitude. Same in both frames. +latitude is local +Y (north).")]
        public float sectorLatitudeMax = 36f;

        [Tooltip("Same A2 sector in the fit-study frame (atan2(x,z), 0° at +Z). Documentation only.")]
        public float studyLongitudeMin = 20f;

        [Tooltip("Same A2 sector in the fit-study frame (atan2(x,z), 0° at +Z). Documentation only.")]
        public float studyLongitudeMax = 50f;

        [Tooltip("Local radial ridge height above the planet radius. A2 test proposal, not final.")]
        public float ridgeHeight = 24f;

        [Tooltip("Local radial cliff depth below the planet radius. A2 test value inside 12–14.")]
        public float cliffDepth = 13f;

        [Tooltip("Proposed cliff depth range minimum (local units).")]
        public float cliffDepthMin = 12f;

        [Tooltip("Proposed cliff depth range maximum (local units).")]
        public float cliffDepthMax = 14f;

        [Tooltip("Keep this many local units of walkable surface inside the authored walls.")]
        public float playableMargin = 1f;

        [Tooltip("Mesh subdivision density for later ridge/cliff builds. 1 = coarse, 4 = fine.")]
        [Range(1, 4)]
        public int detailLevel = 2;

        [Tooltip("If true, later stages may use the optional ~7.49 south-cliff shift. Default is authored walls.")]
        public bool useOptionalSouthCliffShift;

        [Tooltip("How far south of the lip the pit extends, in latitude degrees.")]
        public float cliffSpanDegrees = 18f;

        [Tooltip("Study-frame longitudes of the serialized south lip (authored wall). Filled by the study session.")]
        public float[] cliffLipStudyLongitudes;

        [Tooltip("Matching south-lip latitudes. Playable floor stays north of this polyline.")]
        public float[] cliffLipLatitudes;

        [Tooltip("Study-frame longitudes of the serialized north lip (authored wall). Filled by the study session.")]
        public float[] northLipStudyLongitudes;

        [Tooltip("Matching north-lip latitudes. Ridge starts on the blocked (north) side.")]
        public float[] northLipLatitudes;
    }

    [Header("Tile Size")]
    [Tooltip("Tiles around the planet equator. Higher = smaller tiles.")]
    [SerializeField, Range(16, 256)] int tilesAroundEquator = 72;

    [Header("Tileset")]
    [SerializeField] PlanetTileset tileset;

    [Header("Mesh")]
    [Tooltip("Slight radial scale to hide seams when blocks are off. Ignored while blocks are enabled.")]
    [SerializeField] float overlap = 1f;
    [Tooltip("How far neighboring shell tiles overlap, as a fraction of cell size. Closes cracks on the sphere.")]
    [SerializeField, Range(0f, 0.05f)] float seamOverlap = 0.012f;
    [Tooltip("Split each shell tile so it follows the planet curve. 1 = one flat quad.")]
    [SerializeField, Range(1, 4)] int cellSubdivisions = 2;
    [Tooltip("Lift the tile mesh above the planet surface.")]
    [SerializeField] float surfaceLift = 0.08f;
    [Tooltip("Hide the planet MeshRenderer while tiles are shown.")]
    [SerializeField] bool hidePlanetBaseMesh = true;
    [Tooltip("Show the painted tile mesh.")]
    [SerializeField] bool showTileVisuals = true;
    [Tooltip("Hide the planet visual shell while tiles are shown.")]
    [SerializeField] bool hideShellWhileShowingTiles = true;
    [Tooltip("Use the tile mesh as the walk collider.")]
    [SerializeField] bool useTileMeshCollider = true;
    [Tooltip("Disable the base SphereCollider when the tile collider is active.")]
    [SerializeField] bool disableBaseSphereCollider = true;
    [Tooltip("Cast shadows from the tile mesh.")]
    [SerializeField] bool castTileShadows = true;

    [Header("Block Tiles")]
    [Tooltip("Extrude each cell into a raised block with visible sides.")]
    [FormerlySerializedAs("cubeBlocks")]
    [SerializeField] bool enableBlocks = true;
    [Tooltip("Block height relative to tile width.")]
    [FormerlySerializedAs("cubeHeightFactor")]
    [SerializeField, Range(0.05f, 0.55f)] float blockHeight = 0.28f;
    [Tooltip("Gap between neighboring blocks (0 = flush).")]
    [FormerlySerializedAs("cubeInset")]
    [SerializeField, Range(0f, 0.3f)] float blockGap = 0.1f;
    [Tooltip("Alternate cell tint for a clearer grid read.")]
    [FormerlySerializedAs("checkerTint")]
    [SerializeField] bool alternateTint;
    [Tooltip("Tint for even cells (lat + lon even).")]
    [FormerlySerializedAs("checkerA")]
    [SerializeField] Color tintEven = Color.white;
    [Tooltip("Tint for odd cells (lat + lon odd).")]
    [FormerlySerializedAs("checkerB")]
    [SerializeField] Color tintOdd = new Color(0.82f, 0.9f, 0.72f, 1f);
    [Tooltip("Darken multiplier applied to block side faces.")]
    [FormerlySerializedAs("sideShade")]
    [SerializeField] Color sideDarken = new Color(0.72f, 0.72f, 0.72f, 1f);

    [Header("Map Data")]
    [SerializeField] int latitudeBands = 36;
    [SerializeField] int longitudeBands = 72;
    [SerializeField] int[] terrainIds = Array.Empty<int>();
    [SerializeField] int[] tileIndices = Array.Empty<int>();

    [Header("Terrain Work Plan")]
    [SerializeField] TerrainWorkPlan workPlan = new TerrainWorkPlan();

    SphericalPlanet _planet;
    Transform _tilesRoot;
    MeshFilter _tilesFilter;
    MeshRenderer _tilesRenderer;
    MeshCollider _tilesCollider;
    Mesh _runtimeMesh;
    Material _runtimeMaterial;

    public PlanetTileset Tileset => tileset;
    public int TilesAroundEquator => tilesAroundEquator;
    public int LatitudeBands => latitudeBands;
    public int LongitudeBands => longitudeBands;
    public int CellCount => latitudeBands * longitudeBands;
    public bool ShowTileVisuals => showTileVisuals;
    public bool ProvidesWalkSurface => showTileVisuals;
    public MeshCollider WalkMeshCollider => _tilesCollider;
    public TerrainWorkPlan WorkPlan => workPlan;

    public void SetWorkPlan(TerrainWorkPlan plan)
    {
        workPlan = plan ?? new TerrainWorkPlan();
        ClampWorkPlan();
        if (isActiveAndEnabled)
            RebuildVisuals();
    }

    /// <summary>
    /// PlanetTileMap longitude (0–360, 0° at local +X) and latitude (−90–+90, +Y = north).
    /// </summary>
    public static void DirectionToTileMapLonLat(Vector3 directionFromCenter, out float longitudeDeg, out float latitudeDeg)
    {
        Vector3 dir = directionFromCenter.sqrMagnitude > 0.0001f
            ? directionFromCenter.normalized
            : Vector3.up;
        latitudeDeg = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
        longitudeDeg = Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg;
        if (longitudeDeg < 0f)
            longitudeDeg += 360f;
    }

    /// <summary>Unit direction in planet-local space from PlanetTileMap lon/lat degrees.</summary>
    public static Vector3 TileMapLonLatToDirection(float longitudeDeg, float latitudeDeg)
    {
        float lat = latitudeDeg * Mathf.Deg2Rad;
        float lon = longitudeDeg * Mathf.Deg2Rad;
        return new Vector3(
            Mathf.Cos(lat) * Mathf.Cos(lon),
            Mathf.Sin(lat),
            Mathf.Cos(lat) * Mathf.Sin(lon));
    }

    /// <summary>
    /// Fit-study longitude (0° at local +Z, + toward +X). Latitude matches the tilemap frame.
    /// </summary>
    public static void DirectionToStudyLonLat(Vector3 directionFromCenter, out float longitudeDeg, out float latitudeDeg)
    {
        Vector3 dir = directionFromCenter.sqrMagnitude > 0.0001f
            ? directionFromCenter.normalized
            : Vector3.up;
        latitudeDeg = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
        longitudeDeg = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
    }

    public static Vector3 StudyLonLatToDirection(float longitudeDeg, float latitudeDeg)
    {
        float lat = latitudeDeg * Mathf.Deg2Rad;
        float lon = longitudeDeg * Mathf.Deg2Rad;
        return new Vector3(
            Mathf.Cos(lat) * Mathf.Sin(lon),
            Mathf.Sin(lat),
            Mathf.Cos(lat) * Mathf.Cos(lon));
    }

    public float GetWalkSurfaceRadius(Vector3 directionFromCenter)
    {
        if (_planet == null)
            _planet = GetComponent<SphericalPlanet>();
        if (_planet == null)
            return 0f;

        Vector3 up = directionFromCenter.sqrMagnitude > 0.0001f
            ? directionFromCenter.normalized
            : Vector3.up;

        float lift = Mathf.Max(surfaceLift, _planet.Radius * 0.003f);
        float radius = _planet.GetTerrainRadius(up) + lift + GetCubeHeight();
        if (!enableBlocks && overlap > 1.0001f)
            radius *= overlap;
        // Do not subtract the cliff pit: PlanetWalker uses this as a floor it cannot go below.
        // Descent is blocked by the authored lip colliders, not by a fall mechanic.
        return radius;
    }

    /// <summary>
    /// True if this collider is the walk surface (tile mesh, or the planet sphere when tiles are off).
    /// Wall boxes are blockers, not floors.
    /// </summary>
    public bool IsWalkSurfaceCollider(Collider col)
    {
        if (col == null)
            return false;

        if (showTileVisuals && useTileMeshCollider && _tilesCollider != null && _tilesCollider.enabled)
            return col == _tilesCollider;

        return col is SphereCollider && col.GetComponent<SphericalPlanet>() != null;
    }

    /// <summary>
    /// Picks the outermost walk hit that is not below the analytic floor (A2 cliff pit is visual only).
    /// </summary>
    public bool TryPickWalkSurfaceHit(
        RaycastHit[] hits,
        Vector3 planetCenter,
        Vector3 inwardRayDirection,
        out RaycastHit best)
    {
        best = default;
        if (hits == null || hits.Length == 0)
            return false;

        Vector3 radial = inwardRayDirection.sqrMagnitude > 0.001f
            ? -inwardRayDirection.normalized
            : Vector3.up;
        float minRadius = GetWalkSurfaceRadius(radial) - 0.05f;
        float bestRadius = -1f;
        bool found = false;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i].collider;
            if (!IsWalkSurfaceCollider(col))
                continue;

            Vector3 normal = hits[i].normal.sqrMagnitude > 0.001f
                ? hits[i].normal.normalized
                : radial;
            if (Vector3.Dot(normal, radial) < 0.35f)
                continue;

            float surfaceRadius = (hits[i].point - planetCenter).magnitude;
            if (surfaceRadius < minRadius)
                continue;

            if (surfaceRadius > bestRadius)
            {
                bestRadius = surfaceRadius;
                best = hits[i];
                found = true;
            }
        }

        return found;
    }

    /// <summary>
    /// Light clearing stays the tileset green. A thin dark-green soil band hugs the A2 lips only —
    /// alien dirt, not a leaf layer. Openings (lon fade 0) stay untinted.
    /// </summary>
    Color GetA2ClearingTint(float latDeg, float lonDeg, Color baseTint)
    {
        if (workPlan == null || !workPlan.enabled)
            return baseTint;

        Vector3 dir = TileMapLonLatToDirection(lonDeg, latDeg);
        DirectionToStudyLonLat(dir, out float studyLon, out float studyLat);
        bool wrap = workPlan.coverFullRing;
        float southPresence = wrap
            ? NyxaraA2CliffProfile.LipPresence(
                workPlan.cliffLipStudyLongitudes, workPlan.cliffLipLatitudes, studyLon, true)
            : NyxaraA2CliffProfile.LonEdgeFade(studyLon, workPlan.studyLongitudeMin, workPlan.studyLongitudeMax);
        float northPresence = wrap
            ? NyxaraA2CliffProfile.LipPresence(
                workPlan.northLipStudyLongitudes, workPlan.northLipLatitudes, studyLon, true)
            : southPresence;
        if (southPresence <= 0.02f && northPresence <= 0.02f)
            return baseTint;

        const float stripDeg = 2.2f;
        float soil = 0f;
        if (southPresence > 0.02f)
        {
            float southLip = NyxaraA2CliffProfile.SampleLipLatitude(workPlan, studyLon);
            if (studyLat >= southLip && studyLat < southLip + stripDeg)
                soil = Mathf.Max(soil, (1f - (studyLat - southLip) / stripDeg) * southPresence);
        }

        if (northPresence > 0.02f)
        {
            float northLip = NyxaraA2CliffProfile.SampleNorthLipLatitude(workPlan, studyLon);
            if (studyLat <= northLip && studyLat > northLip - stripDeg)
                soil = Mathf.Max(soil, (1f - (northLip - studyLat) / stripDeg) * northPresence);
        }
        if (soil <= 0.001f)
            return baseTint;

        soil = soil * soil;
        var earth = new Color(0.46f, 0.50f, 0.34f, 1f);
        return Color.Lerp(baseTint, earth, soil * 0.52f);
    }

    /// <summary>
    /// Radial drop (≤ 0) applied to tile vertices in the A2 cliff pit. Zero on the playable floor.
    /// </summary>
    public float GetCliffRadialOffset(Vector3 directionFromCenter)
    {
        if (workPlan == null || !workPlan.enabled)
            return 0f;
        Vector3 dir = directionFromCenter.sqrMagnitude > 0.0001f
            ? directionFromCenter.normalized
            : Vector3.up;
        DirectionToStudyLonLat(dir, out float studyLon, out float studyLat);
        return NyxaraA2CliffProfile.RadialOffset(workPlan, studyLon, studyLat);
    }

    float GetCubeHeight()
    {
        if (!enableBlocks)
            return 0f;
        return ApproximateTileWorldSize * Mathf.Clamp(blockHeight, 0.05f, 0.55f);
    }

    public Vector3 GetWalkSurfacePoint(Vector3 directionFromCenter, float hover = 0f)
    {
        if (_planet == null)
            _planet = GetComponent<SphericalPlanet>();
        if (_planet == null)
            return directionFromCenter;

        Vector3 up = directionFromCenter.sqrMagnitude > 0.0001f
            ? directionFromCenter.normalized
            : Vector3.up;
        return _planet.Center + up * (GetWalkSurfaceRadius(up) + hover);
    }

    public Vector3 GetWalkSurfaceNormal(Vector3 directionFromCenter)
    {
        if (_planet == null)
            _planet = GetComponent<SphericalPlanet>();
        if (_planet == null)
            return Vector3.up;
        return _planet.GetTerrainNormal(directionFromCenter);
    }

    public float ApproximateTileWorldSize
    {
        get
        {
            if (_planet == null)
                _planet = GetComponent<SphericalPlanet>();
            float radius = _planet != null ? _planet.Radius : 40f;
            int count = Mathf.Max(1, tilesAroundEquator);
            return (2f * Mathf.PI * radius) / count;
        }
    }

    public void SetTileset(PlanetTileset newTileset, bool refillBase)
    {
        tileset = newTileset;
        if (refillBase)
            FillTerrain(tileset != null ? tileset.BaseTerrainIndex : 0);
        else
        {
            EnsureMapArrays();
            PlanetBlobAutotile.ResolveAll(this);
        }
    }

    void OnEnable()
    {
        _planet = GetComponent<SphericalPlanet>();
        EnsureRenderObjects();
        ApplyBaseMeshVisibility();

        if (tileset != null && tileset.TerrainCount > 0)
        {
            if (!HasValidMap())
                FillTerrain(tileset.BaseTerrainIndex);
            else
                RebuildVisuals();
        }

        EnsureWalkColliders();
    }

    void OnDisable() => CleanupRuntimeAssets();
    void OnDestroy() => CleanupRuntimeAssets();

    void OnValidate()
    {
        tilesAroundEquator = Mathf.Clamp(tilesAroundEquator, 16, 256);
        overlap = Mathf.Max(1f, overlap);
        seamOverlap = Mathf.Clamp(seamOverlap, 0f, 0.05f);
        cellSubdivisions = Mathf.Clamp(cellSubdivisions, 1, 4);
        blockGap = Mathf.Clamp(blockGap, 0f, 0.3f);
        blockHeight = Mathf.Clamp(blockHeight, 0.05f, 0.55f);
        ClampWorkPlan();
        if (_planet == null)
            _planet = GetComponent<SphericalPlanet>();

        // Defer renderer toggles — Unity forbids SendMessage during OnValidate.
#if UNITY_EDITOR
        if (!_visibilityQueued)
        {
            _visibilityQueued = true;
            UnityEditor.EditorApplication.delayCall += ApplyVisibilityDeferred;
        }
#else
        if (_tilesRenderer != null)
            _tilesRenderer.enabled = showTileVisuals;
        ApplyBaseMeshVisibility();
#endif
    }

#if UNITY_EDITOR
    bool _visibilityQueued;

    void ApplyVisibilityDeferred()
    {
        _visibilityQueued = false;
        if (this == null)
            return;
        if (_tilesRenderer != null)
            _tilesRenderer.enabled = showTileVisuals;
        ApplyBaseMeshVisibility();
    }
#endif

    public void SetTilesAroundEquator(int count, bool refillWithBase = true)
    {
        tilesAroundEquator = Mathf.Clamp(count, 16, 256);
        EnsureGridDimensionsFromEquator();
        if (refillWithBase || !HasValidMap())
            FillTerrain(tileset != null ? tileset.BaseTerrainIndex : 0);
        else
        {
            EnsureMapArrays();
            PlanetBlobAutotile.ResolveAll(this);
        }
    }

    void EnsureGridDimensionsFromEquator()
    {
        longitudeBands = Mathf.Max(16, tilesAroundEquator);
        latitudeBands = Mathf.Max(8, tilesAroundEquator / 2);
    }

    public bool HasValidMap()
    {
        int cells = latitudeBands * longitudeBands;
        return latitudeBands > 0
               && longitudeBands > 0
               && terrainIds != null
               && tileIndices != null
               && terrainIds.Length == cells
               && tileIndices.Length == cells;
    }

    void EnsureMapArrays()
    {
        EnsureGridDimensionsFromEquator();
        int cells = latitudeBands * longitudeBands;
        if (terrainIds == null || terrainIds.Length != cells)
            terrainIds = new int[cells];
        if (tileIndices == null || tileIndices.Length != cells)
            tileIndices = new int[cells];
    }

    public void FillTerrain(int terrainIndex)
    {
        EnsureGridDimensionsFromEquator();
        int cells = latitudeBands * longitudeBands;
        terrainIds = new int[cells];
        tileIndices = new int[cells];
        for (int i = 0; i < cells; i++)
            terrainIds[i] = terrainIndex;
        PlanetBlobAutotile.ResolveAll(this);
    }

    public int GetTerrain(int lat, int lon)
    {
        if (!HasValidMap())
            return tileset != null ? tileset.BaseTerrainIndex : 0;
        lon = Mod(lon, longitudeBands);
        if (lat < 0 || lat >= latitudeBands)
            return tileset != null ? tileset.BaseTerrainIndex : 0;
        return terrainIds[CellIndex(lat, lon)];
    }

    public bool SetTerrainSilent(int lat, int lon, int terrainIndex)
    {
        if (!HasValidMap())
            FillTerrain(tileset != null ? tileset.BaseTerrainIndex : 0);

        lon = Mod(lon, longitudeBands);
        if (lat < 0 || lat >= latitudeBands)
            return false;

        int maxT = tileset != null ? Mathf.Max(0, tileset.TerrainCount - 1) : 0;
        terrainIndex = Mathf.Clamp(terrainIndex, 0, maxT);
        int cell = CellIndex(lat, lon);
        if (terrainIds[cell] == terrainIndex)
            return false;
        terrainIds[cell] = terrainIndex;
        return true;
    }

    public bool PaintTerrainBrush(int centerLat, int centerLon, int terrainIndex, int radiusCells, bool rebuild)
    {
        if (!HasValidMap())
            FillTerrain(tileset != null ? tileset.BaseTerrainIndex : 0);

        radiusCells = Mathf.Max(0, radiusCells);
        bool changed = false;
        for (int dLat = -radiusCells; dLat <= radiusCells; dLat++)
        {
            int lat = centerLat + dLat;
            if (lat < 0 || lat >= latitudeBands)
                continue;
            for (int dLon = -radiusCells; dLon <= radiusCells; dLon++)
            {
                if (dLat * dLat + dLon * dLon > radiusCells * radiusCells)
                    continue;
                int lon = Mod(centerLon + dLon, longitudeBands);
                if (SetTerrainSilent(lat, lon, terrainIndex))
                    changed = true;
            }
        }

        if (changed && rebuild)
            PlanetBlobAutotile.ResolveRegion(this, centerLat, centerLon, radiusCells + 1);
        return changed;
    }

    public int GetTileIndex(int lat, int lon)
    {
        if (!HasValidMap())
            return 0;
        lon = Mod(lon, longitudeBands);
        if (lat < 0 || lat >= latitudeBands)
            return 0;
        return tileIndices[CellIndex(lat, lon)];
    }

    public bool SetVisualSilent(int lat, int lon, int visualIndex)
    {
        if (!HasValidMap())
            return false;
        lon = Mod(lon, longitudeBands);
        if (lat < 0 || lat >= latitudeBands)
            return false;
        int cell = CellIndex(lat, lon);
        if (tileIndices[cell] == visualIndex)
            return false;
        tileIndices[cell] = visualIndex;
        return true;
    }

    public void SetVisualTiles(int[] visuals, bool rebuild)
    {
        if (visuals == null || visuals.Length != CellCount)
            return;
        EnsureMapArrays();
        tileIndices = (int[])visuals.Clone();
        if (rebuild)
            RebuildVisuals();
    }

    public bool TryGetTile(Vector3 worldPosition, out TileSample sample)
    {
        sample = default;
        if (_planet == null)
            _planet = GetComponent<SphericalPlanet>();
        if (_planet == null || !HasValidMap())
            return false;
        if (!WorldToCell(worldPosition, out int lat, out int lon))
            return false;

        int terrain = GetTerrain(lat, lon);
        int visual = GetTileIndex(lat, lon);
        sample.terrainIndex = terrain;
        sample.tileIndex = visual;

        var t = tileset != null ? tileset.GetTerrain(terrain) : null;
        var a = tileset != null ? tileset.GetEntry(visual) : null;
        sample.tileId = a != null ? a.id : (t != null ? t.id : string.Empty);
        sample.walkable = t == null || t.walkable;
        sample.zoneId = t != null ? t.zoneId : (a != null ? a.zoneId : string.Empty);
        return true;
    }

    public bool WorldToCell(Vector3 worldPosition, out int lat, out int lon)
    {
        lat = 0;
        lon = 0;
        if (_planet == null)
            _planet = GetComponent<SphericalPlanet>();
        if (_planet == null)
            return false;

        Vector3 dir = (worldPosition - _planet.Center).normalized;
        float latitude = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
        float longitude = Mathf.Atan2(dir.z, dir.x) * Mathf.Rad2Deg;
        if (longitude < 0f)
            longitude += 360f;

        float lat01 = (latitude + 90f) / 180f;
        float lon01 = longitude / 360f;
        lat = Mathf.Clamp(Mathf.FloorToInt(lat01 * latitudeBands), 0, latitudeBands - 1);
        lon = Mathf.Clamp(Mathf.FloorToInt(lon01 * longitudeBands), 0, longitudeBands - 1);
        return true;
    }

    public bool TryGetCellCenter(int lat, int lon, out Vector3 worldPoint)
    {
        worldPoint = default;
        if (_planet == null)
            _planet = GetComponent<SphericalPlanet>();
        if (_planet == null || lat < 0 || lat >= latitudeBands)
            return false;

        lon = Mod(lon, longitudeBands);
        float latStep = 180f / latitudeBands;
        float lonStep = 360f / longitudeBands;
        float latMid = -90f + (lat + 0.5f) * latStep;
        float lonMid = (lon + 0.5f) * lonStep;
        float latR = latMid * Mathf.Deg2Rad;
        float lonR = lonMid * Mathf.Deg2Rad;
        Vector3 up = new Vector3(
            Mathf.Cos(latR) * Mathf.Cos(lonR),
            Mathf.Sin(latR),
            Mathf.Cos(latR) * Mathf.Sin(lonR));
        worldPoint = _planet.Center + up * (GetWalkSurfaceRadius(up) + 0.02f);
        return true;
    }

    public void RebuildVisuals()
    {
        if (_planet == null)
            _planet = GetComponent<SphericalPlanet>();
        if (_planet == null)
            return;
        EnsureRenderObjects();
        ApplyBaseMeshVisibility();
        BuildCombinedMesh();
        EnsureWalkColliders();
    }

    public void EnsureWalkColliders()
    {
        if (_planet == null)
            _planet = GetComponent<SphericalPlanet>();
        EnsureRenderObjects();

        if (_tilesCollider != null)
        {
            if (_runtimeMesh != null && _tilesCollider.sharedMesh != _runtimeMesh)
            {
                _tilesCollider.sharedMesh = null;
                _tilesCollider.sharedMesh = _runtimeMesh;
            }
            _tilesCollider.convex = false;
            _tilesCollider.enabled = useTileMeshCollider && _tilesCollider.sharedMesh != null;
        }

        var sphere = GetComponent<SphereCollider>();
        if (sphere != null)
            sphere.enabled = !(useTileMeshCollider && _tilesCollider != null && _tilesCollider.enabled);

        ApplyBaseMeshVisibility();
    }

    void BuildCombinedMesh()
    {
        if (!HasValidMap() || tileset == null || tileset.Texture == null || tileset.Count == 0)
        {
            if (_tilesFilter != null)
                _tilesFilter.sharedMesh = null;
            return;
        }

        int vertsPerCell = enableBlocks ? 20 : 4;
        int trisPerCell = enableBlocks ? 30 : 6;
        var vertices = new List<Vector3>(CellCount * vertsPerCell);
        var normals = new List<Vector3>(CellCount * vertsPerCell);
        var uvs = new List<Vector2>(CellCount * vertsPerCell);
        var colors = new List<Color>(CellCount * vertsPerCell);
        var triangles = new List<int>(CellCount * trisPerCell);

        float latStep = 180f / latitudeBands;
        float lonStep = 360f / longitudeBands;
        float lift = Mathf.Max(surfaceLift, _planet.Radius * 0.003f);
        float cubeH = GetCubeHeight();
        float inset = enableBlocks ? Mathf.Clamp01(blockGap) : 0f;
        float meshOverlap = enableBlocks ? 1f : overlap;
        float shellSeam = !enableBlocks ? seamOverlap : 0f;
        int subdiv = enableBlocks ? 1 : Mathf.Max(1, cellSubdivisions);
        int fallback = Mathf.Max(0, tileset.IndexOfId("Fill_Grass"));

        for (int lat = 0; lat < latitudeBands; lat++)
        {
            bool southPole = lat == 0;
            bool northPole = lat == latitudeBands - 1;

            float lat0 = -90f + lat * latStep;
            float lat1 = -90f + (lat + 1) * latStep;

            // Keep polar cells as wedges into the true pole (no collapsed quad edge).
            if (!southPole && !northPole && inset > 0f)
            {
                float d = latStep * inset * 0.5f;
                lat0 += d;
                lat1 -= d;
            }
            else if (southPole && inset > 0f)
            {
                lat1 -= latStep * inset * 0.5f;
            }
            else if (northPole && inset > 0f)
            {
                lat0 += latStep * inset * 0.5f;
            }

            // Near poles, shrink longitude inset so wedges don't vanish.
            float midLatRad = 0.5f * (lat0 + lat1) * Mathf.Deg2Rad;
            float cosLat = Mathf.Max(0.12f, Mathf.Abs(Mathf.Cos(midLatRad)));
            float lonInsetScale = southPole || northPole ? 0.25f : Mathf.Lerp(0.35f, 1f, cosLat);

            for (int lon = 0; lon < longitudeBands; lon++)
            {
                int tileIndex = GetTileIndex(lat, lon);
                if (tileIndex < 0 || tileIndex >= tileset.Count)
                    tileIndex = fallback;
                if (!tileset.TryGetCornerUvs(tileIndex, out Vector2 uvSW, out Vector2 uvSE, out Vector2 uvNE, out Vector2 uvNW))
                {
                    uvSW = new Vector2(0f, 0f);
                    uvSE = new Vector2(1f, 0f);
                    uvNE = new Vector2(1f, 1f);
                    uvNW = new Vector2(0f, 1f);
                }

                float cellLat0 = lat0;
                float cellLat1 = lat1;
                float lon0 = lon * lonStep;
                float lon1 = (lon + 1) * lonStep;
                if (inset > 0f)
                {
                    float d = lonStep * inset * 0.5f * lonInsetScale;
                    lon0 += d;
                    lon1 -= d;
                }
                else if (shellSeam > 0f)
                {
                    float dLat = latStep * shellSeam * 0.5f;
                    float dLon = lonStep * shellSeam * 0.5f * lonInsetScale;
                    if (!southPole && !northPole)
                    {
                        cellLat0 -= dLat;
                        cellLat1 += dLat;
                    }
                    lon0 -= dLon;
                    lon1 += dLon;
                }

                Color topTint = Color.white;
                if (alternateTint && ((lat + lon) & 1) == 1)
                    topTint = tintOdd;
                else if (alternateTint)
                    topTint = tintEven;
                Color sideTint = new Color(
                    topTint.r * sideDarken.r,
                    topTint.g * sideDarken.g,
                    topTint.b * sideDarken.b,
                    1f);

                if (southPole || northPole)
                {
                    AddPolarCell(
                        northPole,
                        southPole ? cellLat1 : cellLat0,
                        lon0,
                        lon1,
                        lift,
                        cubeH,
                        uvSW, uvSE, uvNE, uvNW,
                        topTint,
                        sideTint,
                        !enableBlocks,
                        vertices, normals, uvs, colors, triangles);
                    continue;
                }

                if (enableBlocks)
                {
                    Vector3 bSW = LocalSurfacePoint(cellLat0, lon0, lift);
                    Vector3 bSE = LocalSurfacePoint(cellLat0, lon1, lift);
                    Vector3 bNE = LocalSurfacePoint(cellLat1, lon1, lift);
                    Vector3 bNW = LocalSurfacePoint(cellLat1, lon0, lift);
                    Vector3 tSW = LocalSurfacePoint(cellLat0, lon0, lift + cubeH);
                    Vector3 tSE = LocalSurfacePoint(cellLat0, lon1, lift + cubeH);
                    Vector3 tNE = LocalSurfacePoint(cellLat1, lon1, lift + cubeH);
                    Vector3 tNW = LocalSurfacePoint(cellLat1, lon0, lift + cubeH);

                    if (!IsUsableFace(bSW, bSE, bNE, bNW))
                        continue;

                    AddQuad(tSW, tSE, tNE, tNW, uvSW, uvSE, uvNE, uvNW, topTint, true, vertices, normals, uvs, colors, triangles);
                    AddQuad(bSW, bSE, tSE, tSW, uvSW, uvSE, uvSE, uvSW, sideTint, false, vertices, normals, uvs, colors, triangles);
                    AddQuad(bSE, bNE, tNE, tSE, uvSE, uvNE, uvNE, uvSE, sideTint, false, vertices, normals, uvs, colors, triangles);
                    AddQuad(bNE, bNW, tNW, tNE, uvNE, uvNW, uvNW, uvNE, sideTint, false, vertices, normals, uvs, colors, triangles);
                    AddQuad(bNW, bSW, tSW, tNW, uvNW, uvSW, uvSW, uvNW, sideTint, false, vertices, normals, uvs, colors, triangles);
                }
                else
                {
                    AddShellCell(
                        cellLat0, cellLat1, lon0, lon1,
                        uvSW, uvSE, uvNE, uvNW,
                        topTint, lift, meshOverlap, subdiv,
                        vertices, normals, uvs, colors, triangles);
                }
            }
        }

        if (_runtimeMesh == null)
            _runtimeMesh = new Mesh();
        else
            _runtimeMesh.Clear();
        _runtimeMesh.name = enableBlocks ? "PlanetTiles_Cubes" : "PlanetTiles_Atlas";
        _runtimeMesh.indexFormat = vertices.Count > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        _runtimeMesh.SetVertices(vertices);
        _runtimeMesh.SetNormals(normals);
        _runtimeMesh.SetUVs(0, uvs);
        _runtimeMesh.SetColors(colors);
        _runtimeMesh.SetTriangles(triangles, 0, true);
        _runtimeMesh.RecalculateBounds();

        _tilesFilter.sharedMesh = _runtimeMesh;
        _runtimeMaterial = BuildAtlasMaterial();
        _tilesRenderer.sharedMaterials = new[] { _runtimeMaterial };

        if (_tilesCollider != null)
        {
            _tilesCollider.sharedMesh = null;
            _tilesCollider.sharedMesh = _runtimeMesh;
            _tilesCollider.convex = false;
            _tilesCollider.enabled = useTileMeshCollider;
        }

        EnsureWalkColliders();
    }

    void AddShellCell(
        float lat0,
        float lat1,
        float lon0,
        float lon1,
        Vector2 uvSW,
        Vector2 uvSE,
        Vector2 uvNE,
        Vector2 uvNW,
        Color tint,
        float lift,
        float meshOverlap,
        int subdiv,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<Color> colors,
        List<int> triangles)
    {
        subdiv = Mathf.Max(1, subdiv);
        for (int y = 0; y < subdiv; y++)
        {
            float ty0 = y / (float)subdiv;
            float ty1 = (y + 1) / (float)subdiv;
            float la0 = Mathf.Lerp(lat0, lat1, ty0);
            float la1 = Mathf.Lerp(lat0, lat1, ty1);
            Vector2 uvW0 = Vector2.Lerp(uvSW, uvNW, ty0);
            Vector2 uvE0 = Vector2.Lerp(uvSE, uvNE, ty0);
            Vector2 uvW1 = Vector2.Lerp(uvSW, uvNW, ty1);
            Vector2 uvE1 = Vector2.Lerp(uvSE, uvNE, ty1);

            for (int x = 0; x < subdiv; x++)
            {
                float tx0 = x / (float)subdiv;
                float tx1 = (x + 1) / (float)subdiv;
                Vector3 sw = LocalSurfacePoint(la0, Mathf.Lerp(lon0, lon1, tx0), lift);
                Vector3 se = LocalSurfacePoint(la0, Mathf.Lerp(lon0, lon1, tx1), lift);
                Vector3 ne = LocalSurfacePoint(la1, Mathf.Lerp(lon0, lon1, tx1), lift);
                Vector3 nw = LocalSurfacePoint(la1, Mathf.Lerp(lon0, lon1, tx0), lift);
                if (meshOverlap > 1.0001f)
                {
                    sw *= meshOverlap;
                    se *= meshOverlap;
                    ne *= meshOverlap;
                    nw *= meshOverlap;
                }

                if (!IsUsableFace(sw, se, ne, nw))
                    continue;

                AddQuad(
                    sw, se, ne, nw,
                    Vector2.Lerp(uvW0, uvE0, tx0),
                    Vector2.Lerp(uvW0, uvE0, tx1),
                    Vector2.Lerp(uvW1, uvE1, tx1),
                    Vector2.Lerp(uvW1, uvE1, tx0),
                    GetA2ClearingTint(0.5f * (la0 + la1), Mathf.Lerp(lon0, lon1, 0.5f * (tx0 + tx1)), tint),
                    true,
                    vertices, normals, uvs, colors, triangles);
            }
        }
    }

    void AddPolarCell(
        bool northPole,
        float ringLat,
        float lon0,
        float lon1,
        float lift,
        float cubeH,
        Vector2 uvSW,
        Vector2 uvSE,
        Vector2 uvNE,
        Vector2 uvNW,
        Color topTint,
        Color sideTint,
        bool sphericalNormals,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<Color> colors,
        List<int> triangles)
    {
        float poleLat = northPole ? 90f : -90f;
        float lonMid = 0.5f * (lon0 + lon1);

        // Average UVs toward tile center so the pole tip doesn't stretch a corner texel.
        Vector2 uvPole = (uvSW + uvSE + uvNE + uvNW) * 0.25f;
        Vector2 uvRing0;
        Vector2 uvRing1;
        if (northPole)
        {
            uvRing0 = uvSW;
            uvRing1 = uvSE;
        }
        else
        {
            uvRing0 = uvNW;
            uvRing1 = uvNE;
        }

        if (enableBlocks)
        {
            Vector3 bPole = LocalSurfacePoint(poleLat, lonMid, lift);
            Vector3 b0 = LocalSurfacePoint(ringLat, lon0, lift);
            Vector3 b1 = LocalSurfacePoint(ringLat, lon1, lift);
            Vector3 tPole = LocalSurfacePoint(poleLat, lonMid, lift + cubeH);
            Vector3 t0 = LocalSurfacePoint(ringLat, lon0, lift + cubeH);
            Vector3 t1 = LocalSurfacePoint(ringLat, lon1, lift + cubeH);

            if ((b0 - bPole).sqrMagnitude < 1e-8f || (b1 - bPole).sqrMagnitude < 1e-8f)
                return;

            if (northPole)
                AddTri(tPole, t0, t1, uvPole, uvRing0, uvRing1, topTint, true, vertices, normals, uvs, colors, triangles);
            else
                AddTri(tPole, t1, t0, uvPole, uvRing1, uvRing0, topTint, true, vertices, normals, uvs, colors, triangles);

            AddQuad(b0, b1, t1, t0, uvRing0, uvRing1, uvRing1, uvRing0, sideTint, false, vertices, normals, uvs, colors, triangles);
            AddQuad(bPole, b0, t0, tPole, uvPole, uvRing0, uvRing0, uvPole, sideTint, false, vertices, normals, uvs, colors, triangles);
            AddQuad(b1, bPole, tPole, t1, uvRing1, uvPole, uvPole, uvRing1, sideTint, false, vertices, normals, uvs, colors, triangles);
            return;
        }

        Vector3 pole = LocalSurfacePoint(poleLat, lonMid, lift);
        Vector3 r0 = LocalSurfacePoint(ringLat, lon0, lift);
        Vector3 r1 = LocalSurfacePoint(ringLat, lon1, lift);
        if ((r0 - pole).sqrMagnitude < 1e-8f || (r1 - pole).sqrMagnitude < 1e-8f)
            return;

        if (northPole)
            AddTri(pole, r0, r1, uvPole, uvRing0, uvRing1, topTint, sphericalNormals, vertices, normals, uvs, colors, triangles);
        else
            AddTri(pole, r1, r0, uvPole, uvRing1, uvRing0, topTint, sphericalNormals, vertices, normals, uvs, colors, triangles);
    }

    static bool IsUsableFace(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        Vector3 n = Vector3.Cross(b - a, c - a);
        // Reject collapsed polar quads and needle-thin wedges.
        float planetScale = Mathf.Max(a.sqrMagnitude, 1f);
        return n.sqrMagnitude > planetScale * 1e-10f;
    }

    static Vector3 RadialNormal(Vector3 point, Vector3 fallback)
    {
        return point.sqrMagnitude > 1e-8f ? point.normalized : fallback;
    }

    static void AddTri(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector2 uvA,
        Vector2 uvB,
        Vector2 uvC,
        Color color,
        bool sphericalNormals,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<Color> colors,
        List<int> triangles)
    {
        Vector3 center = (a + b + c) / 3f;
        Vector3 outward = center.sqrMagnitude > 0.0001f ? center.normalized : Vector3.up;
        Vector3 n = Vector3.Cross(b - a, c - a);
        if (n.sqrMagnitude < 1e-10f)
            return;
        n.Normalize();
        if (Vector3.Dot(n, outward) < 0f)
        {
            Vector3 tmpV = b;
            b = c;
            c = tmpV;
            Vector2 tmpUv = uvB;
            uvB = uvC;
            uvC = tmpUv;
            n = -n;
        }

        int start = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        if (sphericalNormals)
        {
            normals.Add(RadialNormal(a, n));
            normals.Add(RadialNormal(b, n));
            normals.Add(RadialNormal(c, n));
        }
        else
        {
            normals.Add(n);
            normals.Add(n);
            normals.Add(n);
        }
        uvs.Add(uvA);
        uvs.Add(uvB);
        uvs.Add(uvC);
        colors.Add(color);
        colors.Add(color);
        colors.Add(color);
        triangles.Add(start + 0);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
    }

    static void AddQuad(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector3 d,
        Vector2 uvA,
        Vector2 uvB,
        Vector2 uvC,
        Vector2 uvD,
        Color color,
        bool sphericalNormals,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<Color> colors,
        List<int> triangles)
    {
        if (!IsUsableFace(a, b, c, d))
            return;

        Vector3 center = (a + b + c + d) * 0.25f;
        Vector3 outward = center.sqrMagnitude > 0.0001f ? center.normalized : Vector3.up;
        Vector3 n = Vector3.Cross(b - a, c - a);
        if (n.sqrMagnitude < 1e-10f)
            n = outward;
        else
            n.Normalize();
        if (Vector3.Dot(n, outward) < 0f)
        {
            Vector3 tmpV = b;
            b = d;
            d = tmpV;
            Vector2 tmpUv = uvB;
            uvB = uvD;
            uvD = tmpUv;
            n = -n;
        }

        int start = vertices.Count;
        vertices.Add(a);
        vertices.Add(b);
        vertices.Add(c);
        vertices.Add(d);
        if (sphericalNormals)
        {
            normals.Add(RadialNormal(a, n));
            normals.Add(RadialNormal(b, n));
            normals.Add(RadialNormal(c, n));
            normals.Add(RadialNormal(d, n));
        }
        else
        {
            normals.Add(n);
            normals.Add(n);
            normals.Add(n);
            normals.Add(n);
        }
        uvs.Add(uvA);
        uvs.Add(uvB);
        uvs.Add(uvC);
        uvs.Add(uvD);
        colors.Add(color);
        colors.Add(color);
        colors.Add(color);
        colors.Add(color);
        triangles.Add(start + 0);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
        triangles.Add(start + 0);
        triangles.Add(start + 2);
        triangles.Add(start + 3);
    }

    Material BuildAtlasMaterial()
    {
        Shader shader = Shader.Find("BackHome/PlanetTilesCube");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Texture");

        if (_runtimeMaterial != null && _runtimeMaterial.shader != shader)
        {
            DestroyRuntimeAsset(_runtimeMaterial);
            _runtimeMaterial = null;
        }

        var mat = _runtimeMaterial != null
            ? _runtimeMaterial
            : new Material(shader) { name = "PlanetTiles_Cube" };
        if (mat.HasProperty("_Cull"))
            mat.SetFloat("_Cull", 2f);
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", Color.white);
        else
            mat.color = Color.white;
        if (mat.HasProperty("_ShadeFloor"))
            mat.SetFloat("_ShadeFloor", enableBlocks ? 0.55f : 0.82f);
        if (mat.HasProperty("_ShadeCeil"))
            mat.SetFloat("_ShadeCeil", enableBlocks ? 1.05f : 1.02f);

        Texture2D tex = tileset.Texture;
        if (tex != null)
        {
            mat.mainTexture = tex;
            if (mat.HasProperty("_BaseMap"))
                mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex"))
                mat.SetTexture("_MainTex", tex);
        }

        return mat;
    }

    Vector3 LocalSurfacePoint(float latDeg, float lonDeg, float lift)
    {
        float lat = latDeg * Mathf.Deg2Rad;
        float lon = lonDeg * Mathf.Deg2Rad;
        Vector3 up = new Vector3(
            Mathf.Cos(lat) * Mathf.Cos(lon),
            Mathf.Sin(lat),
            Mathf.Cos(lat) * Mathf.Sin(lon));
        float terrainRadius = _planet.GetTerrainRadius(up);
        DirectionToStudyLonLat(up, out float studyLon, out float studyLat);
        float cliff = NyxaraA2CliffProfile.RadialOffset(workPlan, studyLon, studyLat);
        float scale = Mathf.Max(transform.lossyScale.x, 0.0001f);
        return up * ((terrainRadius + lift + cliff) / scale);
    }

    void EnsureRenderObjects()
    {
        if (_tilesRoot == null)
        {
            Transform existing = transform.Find("Tiles");
            if (existing != null)
                _tilesRoot = existing;
        }

        if (_tilesRoot == null)
        {
            var root = new GameObject("Tiles");
            root.transform.SetParent(transform, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            _tilesRoot = root.transform;
        }

        _tilesFilter = _tilesRoot.GetComponent<MeshFilter>();
        if (_tilesFilter == null)
            _tilesFilter = _tilesRoot.gameObject.AddComponent<MeshFilter>();

        _tilesRenderer = _tilesRoot.GetComponent<MeshRenderer>();
        if (_tilesRenderer == null)
            _tilesRenderer = _tilesRoot.gameObject.AddComponent<MeshRenderer>();
        _tilesRenderer.enabled = showTileVisuals;
        _tilesRenderer.shadowCastingMode = castTileShadows
            ? UnityEngine.Rendering.ShadowCastingMode.On
            : UnityEngine.Rendering.ShadowCastingMode.Off;
        _tilesRenderer.receiveShadows = true;
        _tilesRoot.gameObject.layer = gameObject.layer;

        _tilesCollider = _tilesRoot.GetComponent<MeshCollider>();
        if (_tilesCollider == null)
            _tilesCollider = _tilesRoot.gameObject.AddComponent<MeshCollider>();
        _tilesCollider.convex = false;
        _tilesCollider.enabled = useTileMeshCollider;
    }

    void ApplyBaseMeshVisibility()
    {
        var renderer = GetComponent<MeshRenderer>();
        if (renderer != null)
            renderer.enabled = !hidePlanetBaseMesh;

        if (disableBaseSphereCollider)
        {
            var sphere = GetComponent<SphereCollider>();
            if (sphere != null)
                sphere.enabled = !useTileMeshCollider;
        }

        bool showShell = !(showTileVisuals && hideShellWhileShowingTiles);
        if (_planet != null)
            _planet.SetVisualShellVisible(showShell);
    }

    void CleanupRuntimeAssets()
    {
        if (_tilesFilter != null && _tilesFilter.sharedMesh == _runtimeMesh)
            _tilesFilter.sharedMesh = null;
        if (_tilesCollider != null && _tilesCollider.sharedMesh == _runtimeMesh)
            _tilesCollider.sharedMesh = null;
        if (_tilesRenderer != null && _runtimeMaterial != null)
            _tilesRenderer.sharedMaterials = System.Array.Empty<Material>();

        DestroyRuntimeAsset(_runtimeMesh);
        _runtimeMesh = null;
        DestroyRuntimeAsset(_runtimeMaterial);
        _runtimeMaterial = null;
    }

    static void DestroyRuntimeAsset(UnityEngine.Object asset)
    {
        if (asset == null)
            return;
        if (Application.isPlaying)
        {
            Destroy(asset);
            return;
        }

#if UNITY_EDITOR
        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (asset != null)
                DestroyImmediate(asset);
        };
#else
        DestroyImmediate(asset);
#endif
    }

    int CellIndex(int lat, int lon) => lat * longitudeBands + lon;

    static int Mod(int value, int modulus)
    {
        int m = value % modulus;
        return m < 0 ? m + modulus : m;
    }

    void ClampWorkPlan()
    {
        if (workPlan == null)
            workPlan = new TerrainWorkPlan();

        if (string.IsNullOrWhiteSpace(workPlan.sectorId))
            workPlan.sectorId = workPlan.coverFullRing ? "Ring" : "A2";

        workPlan.ridgeHeight = Mathf.Max(0f, workPlan.ridgeHeight);
        workPlan.cliffDepthMin = Mathf.Max(0f, workPlan.cliffDepthMin);
        workPlan.cliffDepthMax = Mathf.Max(workPlan.cliffDepthMin, workPlan.cliffDepthMax);
        workPlan.cliffDepth = Mathf.Clamp(workPlan.cliffDepth, workPlan.cliffDepthMin, workPlan.cliffDepthMax);
        workPlan.playableMargin = Mathf.Max(0f, workPlan.playableMargin);
        workPlan.detailLevel = Mathf.Clamp(workPlan.detailLevel, 1, 4);
        workPlan.cliffSpanDegrees = Mathf.Max(8f, workPlan.cliffSpanDegrees);
        workPlan.sectorLatitudeMin = Mathf.Clamp(workPlan.sectorLatitudeMin, -90f, 90f);
        workPlan.sectorLatitudeMax = Mathf.Clamp(workPlan.sectorLatitudeMax, workPlan.sectorLatitudeMin, 90f);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (workPlan == null || !workPlan.enabled)
            return;

        if (_planet == null)
            _planet = GetComponent<SphericalPlanet>();
        if (_planet == null)
            return;

        DrawWorkPlanGizmos(_planet, workPlan);
    }

    public static void DrawWorkPlanGizmos(SphericalPlanet planet, TerrainWorkPlan plan)
    {
        if (planet == null || plan == null || !plan.enabled)
            return;

        float radius = Mathf.Max(0.01f, planet.Radius);
        int steps = plan.coverFullRing ? 48 : 24;
        bool tileMapFrame = plan.longitudeFrame == LongitudeFrame.PlanetTileMap;
        float lon0 = tileMapFrame ? plan.sectorLongitudeMin : plan.studyLongitudeMin;
        float lon1 = tileMapFrame ? plan.sectorLongitudeMax : plan.studyLongitudeMax;

        Vector3 LocalToWorld(Vector3 local) => planet.PlanetLocalToWorld.MultiplyPoint3x4(local);

        Vector3 Dir(float lonDeg, float latDeg) =>
            plan.longitudeFrame == LongitudeFrame.PlanetTileMap
                ? TileMapLonLatToDirection(lonDeg, latDeg)
                : StudyLonLatToDirection(lonDeg, latDeg);

        void DrawParallel(float latDeg, Color color, float radialOffset)
        {
            Gizmos.color = color;
            Vector3 prev = Vector3.zero;
            for (int i = 0; i <= steps; i++)
            {
                float t = i / (float)steps;
                float lon = Mathf.Lerp(lon0, lon1, t);
                Vector3 p = LocalToWorld(Dir(lon, latDeg) * (radius + radialOffset));
                if (i > 0)
                    Gizmos.DrawLine(prev, p);
                prev = p;
            }
        }

        DrawParallel(plan.sectorLatitudeMin, new Color(0.2f, 0.85f, 1f, 0.9f), 0.3f);
        DrawParallel(plan.sectorLatitudeMax, new Color(0.2f, 0.85f, 1f, 0.9f), 0.3f);
        DrawParallel(plan.sectorLatitudeMax, new Color(0.85f, 0.55f, 1f, 0.85f), plan.ridgeHeight);
        DrawParallel(plan.sectorLatitudeMin, new Color(0.95f, 0.45f, 0.25f, 0.85f), -plan.cliffDepth);

        if (!plan.coverFullRing)
        {
            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.9f);
            Vector3 westMin = LocalToWorld(Dir(lon0, plan.sectorLatitudeMin) * (radius + 0.3f));
            Vector3 westMax = LocalToWorld(Dir(lon0, plan.sectorLatitudeMax) * (radius + 0.3f));
            Vector3 eastMin = LocalToWorld(Dir(lon1, plan.sectorLatitudeMin) * (radius + 0.3f));
            Vector3 eastMax = LocalToWorld(Dir(lon1, plan.sectorLatitudeMax) * (radius + 0.3f));
            Gizmos.DrawLine(westMin, westMax);
            Gizmos.DrawLine(eastMin, eastMax);
        }

        Gizmos.color = new Color(0.35f, 0.9f, 0.45f, 0.95f);
        Vector3 north = LocalToWorld(Vector3.up * (radius + Mathf.Max(4f, plan.ridgeHeight)));
        Gizmos.DrawLine(planet.Center, north);
    }
#endif
}
