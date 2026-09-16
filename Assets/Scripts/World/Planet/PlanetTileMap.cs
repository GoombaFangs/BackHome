using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

/// <summary>
/// Spherical terrain tilemap: paint terrains, sculpt ground height, splat-blend or autotile visuals.
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
    [Tooltip("Split each cell so the ground follows the planet curve, like the authored terrain meshes.")]
    [SerializeField, Range(2, 4)] int cellSubdivisions = 3;
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
    [HideInInspector, FormerlySerializedAs("cubeBlocks")]
    [SerializeField] bool enableBlocks;
    [HideInInspector, FormerlySerializedAs("cubeHeightFactor")]
    [SerializeField] float blockHeight = 0.28f;
    [HideInInspector, FormerlySerializedAs("cubeInset")]
    [SerializeField] float blockGap = 0.1f;
    [Tooltip("Alternate cell tint for a clearer grid read.")]
    [FormerlySerializedAs("checkerTint")]
    [SerializeField] bool alternateTint;
    [Tooltip("Tint for even cells (lat + lon even).")]
    [FormerlySerializedAs("checkerA")]
    [SerializeField] Color tintEven = Color.white;
    [Tooltip("Tint for odd cells (lat + lon odd).")]
    [FormerlySerializedAs("checkerB")]
    [SerializeField] Color tintOdd = new Color(0.82f, 0.9f, 0.72f, 1f);
    [HideInInspector, FormerlySerializedAs("sideShade")]
    [SerializeField] Color sideDarken = new Color(0.72f, 0.72f, 0.72f, 1f);

    [Header("Ground Level")]
    [Tooltip("World units per height step. Raise/Lower moves the ground by this amount.")]
    [FormerlySerializedAs("seaDrop")]
    [SerializeField, Range(1f, 24f)] float heightStep = 12f;

    [Header("Map Data")]
    [SerializeField] int latitudeBands = 36;
    [SerializeField] int longitudeBands = 72;
    [SerializeField] int[] terrainIds = Array.Empty<int>();
    [SerializeField] int[] tileIndices = Array.Empty<int>();
    [HideInInspector, FormerlySerializedAs("waterMask")]
    [SerializeField] byte[] waterMask = Array.Empty<byte>();
    [HideInInspector, FormerlySerializedAs("heights")]
    [SerializeField] int[] integerHeights = Array.Empty<int>();
    [Tooltip("Ground height in steps. Fractional values like 0.5 are allowed.")]
    [SerializeField] float[] groundHeights = Array.Empty<float>();

    public const float DefaultGroundHeight = 1f;
    public const float MinHeight = -2f;
    public const float MaxHeight = 4f;
    const float HeightEpsilon = 0.001f;

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

        return _planet.GetTerrainRadius(up) + SampleSurfaceLift(up);
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
    /// Picks the outermost walk hit that is not below the analytic floor.
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
            if (Vector3.Dot(normal, radial) < 0.55f)
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

    public float GetHeightStep() => Mathf.Max(1f, heightStep);

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
        cellSubdivisions = Mathf.Clamp(cellSubdivisions, 2, 4);
        blockGap = Mathf.Clamp(blockGap, 0f, 0.3f);
        blockHeight = Mathf.Clamp(blockHeight, 0.05f, 0.55f);
        heightStep = Mathf.Clamp(heightStep, 1f, 24f);
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
        EnsureHeights();
    }

    void EnsureHeights()
    {
        int cells = latitudeBands * longitudeBands;
        if (cells <= 0)
            return;
        if (groundHeights != null && groundHeights.Length == cells)
        {
            for (int i = 0; i < cells; i++)
                groundHeights[i] = Mathf.Clamp(groundHeights[i], MinHeight, MaxHeight);
            return;
        }

        var next = new float[cells];
        if (integerHeights != null && integerHeights.Length == cells)
        {
            for (int i = 0; i < cells; i++)
                next[i] = Mathf.Clamp(integerHeights[i], MinHeight, MaxHeight);
        }
        else if (waterMask != null && waterMask.Length == cells)
        {
            for (int i = 0; i < cells; i++)
                next[i] = waterMask[i] != 0 ? 0f : DefaultGroundHeight;
        }
        else
        {
            for (int i = 0; i < cells; i++)
                next[i] = DefaultGroundHeight;
        }

        groundHeights = next;
        integerHeights = Array.Empty<int>();
#if UNITY_EDITOR
        if (!Application.isPlaying)
            UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    float HeightToLift(float height)
    {
        float lift = 0.08f;
        if (_planet != null)
            lift = Mathf.Max(surfaceLift, _planet.Radius * 0.003f);
        return lift + height * GetHeightStep();
    }

    float CornerLift(int vertexLat, int vertexLon)
    {
        EnsureHeights();
        vertexLon = Mod(vertexLon, longitudeBands);
        float sum = 0f;
        int n = 0;
        for (int dLat = -1; dLat <= 0; dLat++)
        {
            int cellLat = vertexLat + dLat;
            if (cellLat < 0 || cellLat >= latitudeBands)
                continue;
            for (int dLon = -1; dLon <= 0; dLon++)
            {
                int cellLon = Mod(vertexLon + dLon, longitudeBands);
                sum += HeightToLift(GetHeight(cellLat, cellLon));
                n++;
            }
        }

        return n > 0 ? sum / n : HeightToLift(DefaultGroundHeight);
    }

    float SampleSurfaceLift(Vector3 directionFromCenter)
    {
        if (!HasValidMap())
            return HeightToLift(DefaultGroundHeight);

        DirectionToTileMapLonLat(directionFromCenter, out float lonDeg, out float latDeg);
        float vLat = Mathf.Clamp((latDeg + 90f) / 180f * latitudeBands, 0f, latitudeBands);
        float vLon = lonDeg / 360f * longitudeBands;
        int i0 = Mathf.Clamp(Mathf.FloorToInt(vLat), 0, latitudeBands);
        int i1 = Mathf.Min(i0 + 1, latitudeBands);
        int j0 = Mathf.FloorToInt(vLon);
        float fy = Mathf.Clamp01(vLat - i0);
        float fx = vLon - Mathf.Floor(vLon);
        float sw = CornerLift(i0, j0);
        float se = CornerLift(i0, j0 + 1);
        float nw = CornerLift(i1, j0);
        float ne = CornerLift(i1, j0 + 1);
        float south = Mathf.Lerp(sw, se, fx);
        float north = Mathf.Lerp(nw, ne, fx);
        return Mathf.Lerp(south, north, fy);
    }

    public float GetHeight(int lat, int lon)
    {
        if (!HasValidMap())
            return DefaultGroundHeight;
        lon = Mod(lon, longitudeBands);
        if (lat < 0 || lat >= latitudeBands)
            return DefaultGroundHeight;
        EnsureHeights();
        return groundHeights[CellIndex(lat, lon)];
    }

    public bool SetHeightSilent(int lat, int lon, float height)
    {
        if (!HasValidMap())
            FillTerrain(tileset != null ? tileset.BaseTerrainIndex : 0);

        lon = Mod(lon, longitudeBands);
        if (lat < 0 || lat >= latitudeBands)
            return false;

        EnsureHeights();
        height = Mathf.Clamp(height, MinHeight, MaxHeight);
        int cell = CellIndex(lat, lon);
        if (Mathf.Abs(groundHeights[cell] - height) <= HeightEpsilon)
            return false;
        groundHeights[cell] = height;
        return true;
    }

    public void FillHeight(float height, bool rebuild = true)
    {
        EnsureMapArrays();
        height = Mathf.Clamp(height, MinHeight, MaxHeight);
        for (int i = 0; i < groundHeights.Length; i++)
            groundHeights[i] = height;
        if (rebuild)
            RebuildVisuals();
    }

    public bool PaintHeightBrush(int centerLat, int centerLon, float height, int radiusCells, bool rebuild)
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
                if (SetHeightSilent(lat, lon, height))
                    changed = true;
            }
        }

        if (changed && rebuild)
            RebuildVisuals();
        return changed;
    }

    public bool PaintHeightDeltaBrush(int centerLat, int centerLon, float delta, int radiusCells, bool rebuild)
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
                float next = GetHeight(lat, lon) + delta;
                if (SetHeightSilent(lat, lon, next))
                    changed = true;
            }
        }

        if (changed && rebuild)
            RebuildVisuals();
        return changed;
    }

    public void FillTerrain(int terrainIndex)
    {
        EnsureGridDimensionsFromEquator();
        int cells = latitudeBands * longitudeBands;
        bool keepHeights = groundHeights != null && groundHeights.Length == cells;
        terrainIds = new int[cells];
        tileIndices = new int[cells];
        if (!keepHeights)
        {
            groundHeights = new float[cells];
            for (int i = 0; i < cells; i++)
                groundHeights[i] = DefaultGroundHeight;
        }
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
        int entryIndex = visual;
        if (tileset != null)
            PlanetTileset.UnpackVisual(visual, out entryIndex, out _);
        var a = tileset != null ? tileset.GetEntry(entryIndex) : null;
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
        if (!HasValidMap() || tileset == null || !tileset.HasVisualSource)
        {
            if (_tilesFilter != null)
                _tilesFilter.sharedMesh = null;
            return;
        }

        int vertsPerCell = 20;
        int trisPerCell = 30;
        var vertices = new List<Vector3>(CellCount * vertsPerCell);
        var normals = new List<Vector3>(CellCount * vertsPerCell);
        var uvs = new List<Vector2>(CellCount * vertsPerCell);
        var colors = new List<Color>(CellCount * vertsPerCell);
        var triangles = new List<int>(CellCount * trisPerCell);

        EnsureHeights();
        float latStep = 180f / latitudeBands;
        float lonStep = 360f / longitudeBands;
        int subdiv = Mathf.Max(2, cellSubdivisions);
        int fallback = tileset.DefaultVisualIndex();
        bool splat = tileset.UsesSplatBlending;
        float tiling = tileset.SplatTiling;

        for (int lat = 0; lat < latitudeBands; lat++)
        {
            bool southPole = lat == 0;
            bool northPole = lat == latitudeBands - 1;
            float lat0 = -90f + lat * latStep;
            float lat1 = -90f + (lat + 1) * latStep;

            for (int lon = 0; lon < longitudeBands; lon++)
            {
                Vector2 uvSW;
                Vector2 uvSE;
                Vector2 uvNE;
                Vector2 uvNW;
                Color cSW;
                Color cSE;
                Color cNE;
                Color cNW;

                if (splat)
                {
                    uvSW = new Vector2(lon * tiling, lat * tiling);
                    uvSE = new Vector2((lon + 1) * tiling, lat * tiling);
                    uvNE = new Vector2((lon + 1) * tiling, (lat + 1) * tiling);
                    uvNW = new Vector2(lon * tiling, (lat + 1) * tiling);
                    cSW = tileset.SplatWeight(TerrainAtClamped(lat, lon));
                    cSE = tileset.SplatWeight(TerrainAtClamped(lat, lon + 1));
                    cNW = tileset.SplatWeight(TerrainAtClamped(lat + 1, lon));
                    cNE = tileset.SplatWeight(TerrainAtClamped(lat + 1, lon + 1));
                }
                else
                {
                    int tileIndex = GetTileIndex(lat, lon);
                    if (!tileset.IsValidVisual(tileIndex))
                        tileIndex = fallback;
                    if (!tileset.TryGetCornerUvs(tileIndex, out uvSW, out uvSE, out uvNE, out uvNW))
                    {
                        uvSW = new Vector2(0f, 0f);
                        uvSE = new Vector2(1f, 0f);
                        uvNE = new Vector2(1f, 1f);
                        uvNW = new Vector2(0f, 1f);
                    }

                    Color topTint = Color.white;
                    if (alternateTint && ((lat + lon) & 1) == 1)
                        topTint = tintOdd;
                    else if (alternateTint)
                        topTint = tintEven;
                    cSW = cSE = cNE = cNW = topTint;
                }

                float lon0 = lon * lonStep;
                float lon1 = (lon + 1) * lonStep;
                float liftSW = CornerLift(lat, lon);
                float liftSE = CornerLift(lat, lon + 1);
                float liftNW = CornerLift(lat + 1, lon);
                float liftNE = CornerLift(lat + 1, lon + 1);

                if (southPole)
                {
                    AddPolarHeightfield(
                        false, lat1, lon0, lon1,
                        liftNW, liftNE, 0.5f * (liftSW + liftSE),
                        uvSW, uvSE, uvNE, uvNW,
                        cSW, cSE, cNE, cNW,
                        vertices, normals, uvs, colors, triangles);
                    continue;
                }

                if (northPole)
                {
                    AddPolarHeightfield(
                        true, lat0, lon0, lon1,
                        liftSW, liftSE, 0.5f * (liftNW + liftNE),
                        uvSW, uvSE, uvNE, uvNW,
                        cSW, cSE, cNE, cNW,
                        vertices, normals, uvs, colors, triangles);
                    continue;
                }

                AddHeightfieldCell(
                    lat0, lat1, lon0, lon1,
                    liftSW, liftSE, liftNE, liftNW,
                    uvSW, uvSE, uvNE, uvNW,
                    cSW, cSE, cNE, cNW, subdiv,
                    vertices, normals, uvs, colors, triangles);
            }
        }

        if (_runtimeMesh == null)
            _runtimeMesh = new Mesh();
        else
            _runtimeMesh.Clear();
        _runtimeMesh.name = "PlanetTiles_Heightfield";
        _runtimeMesh.indexFormat = vertices.Count > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        _runtimeMesh.SetVertices(vertices);
        _runtimeMesh.SetUVs(0, uvs);
        _runtimeMesh.SetColors(colors);
        _runtimeMesh.SetTriangles(triangles, 0, true);
        _runtimeMesh.RecalculateBounds();
        _runtimeMesh.RecalculateNormals();

        _tilesFilter.sharedMesh = _runtimeMesh;
        _runtimeMaterial = BuildTileMaterial();
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

    int TerrainAtClamped(int lat, int lon)
    {
        if (lat < 0)
            lat = 0;
        else if (lat >= latitudeBands)
            lat = latitudeBands - 1;
        return GetTerrain(lat, lon);
    }

    void AddHeightfieldCell(
        float lat0,
        float lat1,
        float lon0,
        float lon1,
        float liftSW,
        float liftSE,
        float liftNE,
        float liftNW,
        Vector2 uvSW,
        Vector2 uvSE,
        Vector2 uvNE,
        Vector2 uvNW,
        Color cSW,
        Color cSE,
        Color cNE,
        Color cNW,
        int subdiv,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<Color> colors,
        List<int> triangles)
    {
        subdiv = Mathf.Max(2, subdiv);
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
            Color colW0 = Color.Lerp(cSW, cNW, ty0);
            Color colE0 = Color.Lerp(cSE, cNE, ty0);
            Color colW1 = Color.Lerp(cSW, cNW, ty1);
            Color colE1 = Color.Lerp(cSE, cNE, ty1);

            for (int x = 0; x < subdiv; x++)
            {
                float tx0 = x / (float)subdiv;
                float tx1 = (x + 1) / (float)subdiv;
                float lift00 = Mathf.Lerp(Mathf.Lerp(liftSW, liftSE, tx0), Mathf.Lerp(liftNW, liftNE, tx0), ty0);
                float lift10 = Mathf.Lerp(Mathf.Lerp(liftSW, liftSE, tx1), Mathf.Lerp(liftNW, liftNE, tx1), ty0);
                float lift11 = Mathf.Lerp(Mathf.Lerp(liftSW, liftSE, tx1), Mathf.Lerp(liftNW, liftNE, tx1), ty1);
                float lift01 = Mathf.Lerp(Mathf.Lerp(liftSW, liftSE, tx0), Mathf.Lerp(liftNW, liftNE, tx0), ty1);
                Vector3 sw = LocalSurfacePoint(la0, Mathf.Lerp(lon0, lon1, tx0), lift00);
                Vector3 se = LocalSurfacePoint(la0, Mathf.Lerp(lon0, lon1, tx1), lift10);
                Vector3 ne = LocalSurfacePoint(la1, Mathf.Lerp(lon0, lon1, tx1), lift11);
                Vector3 nw = LocalSurfacePoint(la1, Mathf.Lerp(lon0, lon1, tx0), lift01);
                if (!IsUsableFace(sw, se, ne, nw))
                    continue;

                AddQuad(
                    sw, se, ne, nw,
                    Vector2.Lerp(uvW0, uvE0, tx0),
                    Vector2.Lerp(uvW0, uvE0, tx1),
                    Vector2.Lerp(uvW1, uvE1, tx1),
                    Vector2.Lerp(uvW1, uvE1, tx0),
                    Color.Lerp(colW0, colE0, tx0),
                    Color.Lerp(colW0, colE0, tx1),
                    Color.Lerp(colW1, colE1, tx1),
                    Color.Lerp(colW1, colE1, tx0),
                    true,
                    vertices, normals, uvs, colors, triangles);
            }
        }
    }

    void AddPolarHeightfield(
        bool northPole,
        float ringLat,
        float lon0,
        float lon1,
        float lift0,
        float lift1,
        float poleLift,
        Vector2 uvSW,
        Vector2 uvSE,
        Vector2 uvNE,
        Vector2 uvNW,
        Color cSW,
        Color cSE,
        Color cNE,
        Color cNW,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs,
        List<Color> colors,
        List<int> triangles)
    {
        float poleLat = northPole ? 90f : -90f;
        float lonMid = 0.5f * (lon0 + lon1);
        Vector2 uvPole = (uvSW + uvSE + uvNE + uvNW) * 0.25f;
        Vector2 uvRing0 = northPole ? uvSW : uvNW;
        Vector2 uvRing1 = northPole ? uvSE : uvNE;
        Color cRing0 = northPole ? cSW : cNW;
        Color cRing1 = northPole ? cSE : cNE;
        Color cPole = Color.Lerp(cRing0, cRing1, 0.5f);
        Vector3 pole = LocalSurfacePoint(poleLat, lonMid, poleLift);
        Vector3 r0 = LocalSurfacePoint(ringLat, lon0, lift0);
        Vector3 r1 = LocalSurfacePoint(ringLat, lon1, lift1);
        if ((r0 - pole).sqrMagnitude < 1e-8f || (r1 - pole).sqrMagnitude < 1e-8f)
            return;

        if (northPole)
            AddTri(pole, r0, r1, uvPole, uvRing0, uvRing1, cPole, cRing0, cRing1, true, vertices, normals, uvs, colors, triangles);
        else
            AddTri(pole, r1, r0, uvPole, uvRing1, uvRing0, cPole, cRing1, cRing0, true, vertices, normals, uvs, colors, triangles);
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
        Color colorA,
        Color colorB,
        Color colorC,
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
            Color tmpC = colorB;
            colorB = colorC;
            colorC = tmpC;
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
        colors.Add(colorA);
        colors.Add(colorB);
        colors.Add(colorC);
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
        Color colorA,
        Color colorB,
        Color colorC,
        Color colorD,
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
            Color tmpC = colorB;
            colorB = colorD;
            colorD = tmpC;
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
        colors.Add(colorA);
        colors.Add(colorB);
        colors.Add(colorC);
        colors.Add(colorD);
        triangles.Add(start + 0);
        triangles.Add(start + 1);
        triangles.Add(start + 2);
        triangles.Add(start + 0);
        triangles.Add(start + 2);
        triangles.Add(start + 3);
    }

    Material BuildTileMaterial()
    {
        bool splat = tileset != null && tileset.UsesSplatBlending;
        Shader shader = splat
            ? Shader.Find("BackHome/PlanetTilesSplat")
            : Shader.Find("BackHome/PlanetTilesCube");
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
            : new Material(shader) { name = "PlanetTiles_Heightfield" };
        if (mat.HasProperty("_Cull"))
            mat.SetFloat("_Cull", 2f);
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", Color.white);
        else
            mat.color = Color.white;

        if (splat)
        {
            if (mat.HasProperty("_ShadeFloor"))
                mat.SetFloat("_ShadeFloor", 0.88f);
            if (mat.HasProperty("_ShadeCeil"))
                mat.SetFloat("_ShadeCeil", 1.06f);
            if (mat.HasProperty("_BlendSharpness"))
                mat.SetFloat("_BlendSharpness", tileset.SplatBlendSharpness);

            Texture2D s0 = tileset.GetSplatAlbedo(0);
            Texture2D s1 = tileset.GetSplatAlbedo(1);
            Texture2D s2 = tileset.GetSplatAlbedo(2);
            Texture2D s3 = tileset.GetSplatAlbedo(3);
            if (mat.HasProperty("_Splat0"))
                mat.SetTexture("_Splat0", s0);
            if (mat.HasProperty("_Splat1"))
                mat.SetTexture("_Splat1", s1);
            if (mat.HasProperty("_Splat2"))
                mat.SetTexture("_Splat2", s2);
            if (mat.HasProperty("_Splat3"))
                mat.SetTexture("_Splat3", s3);
            if (s0 != null)
                mat.mainTexture = s0;
        }
        else
        {
            if (mat.HasProperty("_ShadeFloor"))
                mat.SetFloat("_ShadeFloor", 0.82f);
            if (mat.HasProperty("_ShadeCeil"))
                mat.SetFloat("_ShadeCeil", 1.02f);

            Texture2D tex = tileset.Texture;
            if (tex != null)
            {
                mat.mainTexture = tex;
                if (mat.HasProperty("_BaseMap"))
                    mat.SetTexture("_BaseMap", tex);
                if (mat.HasProperty("_MainTex"))
                    mat.SetTexture("_MainTex", tex);
            }
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
        float scale = Mathf.Max(transform.lossyScale.x, 0.0001f);
        return up * ((terrainRadius + lift) / scale);
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
}
