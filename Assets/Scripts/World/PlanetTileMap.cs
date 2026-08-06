using System;
using System.Collections.Generic;
using UnityEngine;

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
    [Tooltip("Legacy palette — unused when tileset is assigned.")]
    [SerializeField] PlanetTilePalette palette;

    [Header("Mesh")]
    [SerializeField] float overlap = 1.03f;
    [SerializeField] float surfaceLift = 0.08f;
    [SerializeField] bool hidePlanetBaseMesh = true;
    [SerializeField] bool showTileVisuals = true;
    [SerializeField] bool hideShellWhileShowingTiles = true;
    [SerializeField] bool useTileMeshCollider = true;
    [SerializeField] bool disableBaseSphereCollider = true;
    [SerializeField] bool castTileShadows = false;

    [Header("Serialized Map")]
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
    public PlanetTilePalette Palette => palette;
    public int TilesAroundEquator => tilesAroundEquator;
    public int LatitudeBands => latitudeBands;
    public int LongitudeBands => longitudeBands;
    public int CellCount => latitudeBands * longitudeBands;
    public bool ShowTileVisuals => showTileVisuals;
    public bool ProvidesWalkSurface => showTileVisuals;

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
        float radius = _planet.GetTerrainRadius(up) + lift;
        if (overlap > 1.0001f)
            radius *= overlap;
        return radius;
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
        if (_planet == null)
            _planet = GetComponent<SphericalPlanet>();
        if (_tilesRenderer != null)
            _tilesRenderer.enabled = showTileVisuals;
        ApplyBaseMeshVisibility();
    }

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

    public void EnsureGridDimensionsFromEquator()
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

    /// <summary>Legacy helper — fills base terrain.</summary>
    public void FillAll(int ignoredTileIndex = 0)
    {
        FillTerrain(tileset != null ? tileset.BaseTerrainIndex : 0);
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

    public void SetTerrain(int lat, int lon, int terrainIndex)
    {
        if (SetTerrainSilent(lat, lon, terrainIndex))
            PlanetBlobAutotile.ResolveRegion(this, lat, lon, 1);
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

        var vertices = new List<Vector3>(CellCount * 4);
        var normals = new List<Vector3>(CellCount * 4);
        var uvs = new List<Vector2>(CellCount * 4);
        var triangles = new List<int>(CellCount * 6);

        float latStep = 180f / latitudeBands;
        float lonStep = 360f / longitudeBands;
        float lift = Mathf.Max(surfaceLift, _planet.Radius * 0.003f);
        int fallback = Mathf.Max(0, tileset.IndexOfId("Fill_Grass"));

        for (int lat = 0; lat < latitudeBands; lat++)
        {
            float lat0 = -90f + lat * latStep;
            float lat1 = -90f + (lat + 1) * latStep;

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

                Vector3 p00 = LocalSurfacePoint(lat0, lon0, lift);
                Vector3 p01 = LocalSurfacePoint(lat0, lon1, lift);
                Vector3 p10 = LocalSurfacePoint(lat1, lon0, lift);
                Vector3 p11 = LocalSurfacePoint(lat1, lon1, lift);

                if (overlap > 1.0001f)
                {
                    p00 *= overlap;
                    p01 *= overlap;
                    p10 *= overlap;
                    p11 *= overlap;
                }

                Vector3 sw = p00;
                Vector3 se = p01;
                Vector3 ne = p11;
                Vector3 nw = p10;

                Vector3 centerLocal = (sw + se + ne + nw) * 0.25f;
                Vector3 outward = centerLocal.sqrMagnitude > 0.0001f
                    ? centerLocal.normalized
                    : Vector3.up;
                Vector3 n = Vector3.Cross(se - sw, ne - sw);
                bool flip = Vector3.Dot(n, outward) < 0f;

                int start = vertices.Count;

                if (!flip)
                {
                    AddVertexLocal(sw, uvSW, vertices, normals, uvs);
                    AddVertexLocal(se, uvSE, vertices, normals, uvs);
                    AddVertexLocal(ne, uvNE, vertices, normals, uvs);
                    AddVertexLocal(nw, uvNW, vertices, normals, uvs);
                }
                else
                {
                    AddVertexLocal(sw, uvSW, vertices, normals, uvs);
                    AddVertexLocal(nw, uvNW, vertices, normals, uvs);
                    AddVertexLocal(ne, uvNE, vertices, normals, uvs);
                    AddVertexLocal(se, uvSE, vertices, normals, uvs);
                }

                triangles.Add(start + 0);
                triangles.Add(start + 1);
                triangles.Add(start + 2);
                triangles.Add(start + 0);
                triangles.Add(start + 2);
                triangles.Add(start + 3);
            }
        }

        _runtimeMesh = new Mesh { name = "PlanetTiles_Atlas" };
        _runtimeMesh.indexFormat = vertices.Count > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        _runtimeMesh.SetVertices(vertices);
        _runtimeMesh.SetNormals(normals);
        _runtimeMesh.SetUVs(0, uvs);
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

    Material BuildAtlasMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Texture");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        var mat = new Material(shader) { name = "PlanetTiles_Atlas_Unlit" };
        if (mat.HasProperty("_Cull"))
            mat.SetFloat("_Cull", 0f);
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

    static void AddVertexLocal(
        Vector3 localPoint,
        Vector2 uv,
        List<Vector3> vertices,
        List<Vector3> normals,
        List<Vector2> uvs)
    {
        Vector3 normal = localPoint.sqrMagnitude > 0.0001f ? localPoint.normalized : Vector3.up;
        vertices.Add(localPoint);
        normals.Add(normal);
        uvs.Add(uv);
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
