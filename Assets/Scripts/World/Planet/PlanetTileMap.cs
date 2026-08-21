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

    [Header("Tile Size")]
    [Tooltip("Tiles around the planet equator. Higher = smaller tiles.")]
    [SerializeField, Range(16, 256)] int tilesAroundEquator = 72;

    [Header("Tileset")]
    [SerializeField] PlanetTileset tileset;

    [Header("Mesh")]
    [Tooltip("Slight scale to hide seams when blocks are off. Ignored while blocks are enabled.")]
    [SerializeField] float overlap = 1f;
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
    [SerializeField] bool alternateTint = true;
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
        return radius;
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
        blockGap = Mathf.Clamp(blockGap, 0f, 0.3f);
        blockHeight = Mathf.Clamp(blockHeight, 0.05f, 0.55f);
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
        CleanupRuntimeAssets();
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

                float lon0 = lon * lonStep;
                float lon1 = (lon + 1) * lonStep;
                if (inset > 0f)
                {
                    float d = lonStep * inset * 0.5f * lonInsetScale;
                    lon0 += d;
                    lon1 -= d;
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
                        southPole ? lat1 : lat0,
                        lon0,
                        lon1,
                        lift,
                        cubeH,
                        uvSW, uvSE, uvNE, uvNW,
                        topTint,
                        sideTint,
                        vertices, normals, uvs, colors, triangles);
                    continue;
                }

                if (enableBlocks)
                {
                    Vector3 bSW = LocalSurfacePoint(lat0, lon0, lift);
                    Vector3 bSE = LocalSurfacePoint(lat0, lon1, lift);
                    Vector3 bNE = LocalSurfacePoint(lat1, lon1, lift);
                    Vector3 bNW = LocalSurfacePoint(lat1, lon0, lift);
                    Vector3 tSW = LocalSurfacePoint(lat0, lon0, lift + cubeH);
                    Vector3 tSE = LocalSurfacePoint(lat0, lon1, lift + cubeH);
                    Vector3 tNE = LocalSurfacePoint(lat1, lon1, lift + cubeH);
                    Vector3 tNW = LocalSurfacePoint(lat1, lon0, lift + cubeH);

                    if (!IsUsableFace(bSW, bSE, bNE, bNW))
                        continue;

                    AddQuad(tSW, tSE, tNE, tNW, uvSW, uvSE, uvNE, uvNW, topTint, vertices, normals, uvs, colors, triangles);
                    AddQuad(bSW, bSE, tSE, tSW, uvSW, uvSE, uvSE, uvSW, sideTint, vertices, normals, uvs, colors, triangles);
                    AddQuad(bSE, bNE, tNE, tSE, uvSE, uvNE, uvNE, uvSE, sideTint, vertices, normals, uvs, colors, triangles);
                    AddQuad(bNE, bNW, tNW, tNE, uvNE, uvNW, uvNW, uvNE, sideTint, vertices, normals, uvs, colors, triangles);
                    AddQuad(bNW, bSW, tSW, tNW, uvNW, uvSW, uvSW, uvNW, sideTint, vertices, normals, uvs, colors, triangles);
                }
                else
                {
                    Vector3 sw = LocalSurfacePoint(lat0, lon0, lift);
                    Vector3 se = LocalSurfacePoint(lat0, lon1, lift);
                    Vector3 ne = LocalSurfacePoint(lat1, lon1, lift);
                    Vector3 nw = LocalSurfacePoint(lat1, lon0, lift);
                    if (meshOverlap > 1.0001f)
                    {
                        sw *= meshOverlap;
                        se *= meshOverlap;
                        ne *= meshOverlap;
                        nw *= meshOverlap;
                    }

                    if (!IsUsableFace(sw, se, ne, nw))
                        continue;

                    AddQuad(sw, se, ne, nw, uvSW, uvSE, uvNE, uvNW, topTint, vertices, normals, uvs, colors, triangles);
                }
            }
        }

        _runtimeMesh = new Mesh { name = enableBlocks ? "PlanetTiles_Cubes" : "PlanetTiles_Atlas" };
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
        Vector2 uvRing0 = northPole ? uvSW : uvNW;
        Vector2 uvRing1 = northPole ? uvSE : uvNE;
        // Prefer equator-facing edge UVs of the tile.
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

            // Top wedge
            if (northPole)
                AddTri(tPole, t0, t1, uvPole, uvRing0, uvRing1, topTint, vertices, normals, uvs, colors, triangles);
            else
                AddTri(tPole, t1, t0, uvPole, uvRing1, uvRing0, topTint, vertices, normals, uvs, colors, triangles);

            // Outer ring wall
            AddQuad(b0, b1, t1, t0, uvRing0, uvRing1, uvRing1, uvRing0, sideTint, vertices, normals, uvs, colors, triangles);
            // Radial walls
            AddQuad(bPole, b0, t0, tPole, uvPole, uvRing0, uvRing0, uvPole, sideTint, vertices, normals, uvs, colors, triangles);
            AddQuad(b1, bPole, tPole, t1, uvRing1, uvPole, uvPole, uvRing1, sideTint, vertices, normals, uvs, colors, triangles);
        }
        else
        {
            Vector3 pole = LocalSurfacePoint(poleLat, lonMid, lift);
            Vector3 r0 = LocalSurfacePoint(ringLat, lon0, lift);
            Vector3 r1 = LocalSurfacePoint(ringLat, lon1, lift);
            if ((r0 - pole).sqrMagnitude < 1e-8f || (r1 - pole).sqrMagnitude < 1e-8f)
                return;

            if (northPole)
                AddTri(pole, r0, r1, uvPole, uvRing0, uvRing1, topTint, vertices, normals, uvs, colors, triangles);
            else
                AddTri(pole, r1, r0, uvPole, uvRing1, uvRing0, topTint, vertices, normals, uvs, colors, triangles);
        }
    }

    static bool IsUsableFace(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
    {
        Vector3 n = Vector3.Cross(b - a, c - a);
        // Reject collapsed polar quads and needle-thin wedges.
        float planetScale = Mathf.Max(a.sqrMagnitude, 1f);
        return n.sqrMagnitude > planetScale * 1e-10f;
    }

    static void AddTri(
        Vector3 a,
        Vector3 b,
        Vector3 c,
        Vector2 uvA,
        Vector2 uvB,
        Vector2 uvC,
        Color color,
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
        normals.Add(n);
        normals.Add(n);
        normals.Add(n);
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
        normals.Add(n);
        normals.Add(n);
        normals.Add(n);
        normals.Add(n);
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

        var mat = new Material(shader) { name = "PlanetTiles_Cube" };
        if (mat.HasProperty("_Cull"))
            mat.SetFloat("_Cull", 2f);
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", Color.white);
        else
            mat.color = Color.white;

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
        if (_runtimeMesh != null)
        {
            if (Application.isPlaying) Destroy(_runtimeMesh);
            else DestroyImmediate(_runtimeMesh);
            _runtimeMesh = null;
        }

        if (_runtimeMaterial != null)
        {
            if (Application.isPlaying) Destroy(_runtimeMaterial);
            else DestroyImmediate(_runtimeMaterial);
            _runtimeMaterial = null;
        }
    }

    int CellIndex(int lat, int lon) => lat * longitudeBands + lon;

    static int Mod(int value, int modulus)
    {
        int m = value % modulus;
        return m < 0 ? m + modulus : m;
    }
}
