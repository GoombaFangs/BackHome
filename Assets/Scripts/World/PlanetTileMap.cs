using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mobile-optimized spherical tilemap.
/// Stores tiles as data and renders as one combined mesh (submeshes by tile type).
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
        public string tileId;
        public bool walkable;
        public string zoneId;
    }

    [Header("Grid")]
    [SerializeField] PlanetTilePalette palette;
    [SerializeField] int tilesAroundEquator = 48;
    [SerializeField] int fillTileIndex = 0;
    [SerializeField] float overlap = 1f;
    [SerializeField] float surfaceLift = 0.002f;
    [SerializeField] bool hidePlanetBaseMesh = true;
    [SerializeField] bool showTileVisuals = false;
    [SerializeField] bool useTileMeshCollider = true;
    [SerializeField] bool disableBaseSphereCollider = true;
    [SerializeField] bool castTileShadows = false;

    [Header("Serialized Map")]
    [SerializeField] int latitudeBands = 24;
    [SerializeField] int longitudeBands = 48;
    [SerializeField] int[] tileIndices = Array.Empty<int>();

    SphericalPlanet _planet;
    Transform _tilesRoot;
    MeshFilter _tilesFilter;
    MeshRenderer _tilesRenderer;
    MeshCollider _tilesCollider;
    Mesh _runtimeMesh;
    Material[] _runtimeMaterials;

    public PlanetTilePalette Palette => palette;
    public int LatitudeBands => latitudeBands;
    public int LongitudeBands => longitudeBands;
    public int FillTileIndex => fillTileIndex;
    public int CellCount => latitudeBands * longitudeBands;

    void OnEnable()
    {
        _planet = GetComponent<SphericalPlanet>();
        EnsureRenderObjects();
        ApplyBaseMeshVisibility();
        if (!HasValidMap())
            FillAll(fillTileIndex);
        else
            RebuildVisuals();
    }

    void OnDisable()
    {
        CleanupRuntimeAssets();
    }

    void OnDestroy()
    {
        CleanupRuntimeAssets();
    }

    void OnValidate()
    {
        tilesAroundEquator = Mathf.Max(8, tilesAroundEquator);
        overlap = Mathf.Max(1f, overlap);
        if (palette != null)
            fillTileIndex = Mathf.Clamp(fillTileIndex, 0, Mathf.Max(0, palette.Count - 1));
    }

    public void EnsureGridDimensionsFromEquator()
    {
        longitudeBands = Mathf.Max(8, tilesAroundEquator);
        latitudeBands = Mathf.Max(4, tilesAroundEquator / 2);
    }

    public bool HasValidMap()
    {
        return tileIndices != null
               && latitudeBands > 0
               && longitudeBands > 0
               && tileIndices.Length == latitudeBands * longitudeBands;
    }

    public void FillAll(int tileIndex)
    {
        EnsureGridDimensionsFromEquator();
        tileIndices = new int[latitudeBands * longitudeBands];
        for (int i = 0; i < tileIndices.Length; i++)
            tileIndices[i] = tileIndex;
        RebuildVisuals();
    }

    public void SetTile(int lat, int lon, int tileIndex)
    {
        if (!HasValidMap())
            FillAll(fillTileIndex);

        lon = Mod(lon, longitudeBands);
        if (lat < 0 || lat >= latitudeBands)
            return;

        int cell = CellIndex(lat, lon);
        if (tileIndices[cell] == tileIndex)
            return;

        tileIndices[cell] = tileIndex;
        RebuildVisuals();
    }

    public int GetTileIndex(int lat, int lon)
    {
        if (!HasValidMap())
            return fillTileIndex;
        lon = Mod(lon, longitudeBands);
        if (lat < 0 || lat >= latitudeBands)
            return fillTileIndex;
        return tileIndices[CellIndex(lat, lon)];
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

        int index = GetTileIndex(lat, lon);
        var entry = palette != null ? palette.GetEntry(index) : null;
        sample.tileIndex = index;
        sample.tileId = entry != null ? entry.id : string.Empty;
        sample.walkable = entry == null || entry.walkable;
        sample.zoneId = entry != null ? entry.zoneId : string.Empty;
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

    public void RebuildVisuals()
    {
        if (_planet == null)
            _planet = GetComponent<SphericalPlanet>();
        if (_planet == null)
            return;

        EnsureRenderObjects();
        ApplyBaseMeshVisibility();
        BuildCombinedMesh();
    }

    void BuildCombinedMesh()
    {
        CleanupRuntimeAssets();
        if (!HasValidMap() || palette == null || palette.Count == 0)
        {
            if (_tilesFilter != null)
                _tilesFilter.sharedMesh = null;
            return;
        }

        int materialCount = palette.Count;
        var vertices = new List<Vector3>(CellCount * 4);
        var normals = new List<Vector3>(CellCount * 4);
        var uvs = new List<Vector2>(CellCount * 4);
        var trianglesByMat = new List<int>[materialCount];
        for (int i = 0; i < materialCount; i++)
            trianglesByMat[i] = new List<int>();

        float latStep = 180f / latitudeBands;
        float lonStep = 360f / longitudeBands;

        for (int lat = 0; lat < latitudeBands; lat++)
        {
            float lat0 = -90f + lat * latStep;
            float lat1 = -90f + (lat + 1) * latStep;

            for (int lon = 0; lon < longitudeBands; lon++)
            {
                int tileIndex = Mathf.Clamp(GetTileIndex(lat, lon), 0, materialCount - 1);
                float lon0 = lon * lonStep;
                float lon1 = (lon + 1) * lonStep;

                Vector3 p00 = SurfacePoint(lat0, lon0);
                Vector3 p01 = SurfacePoint(lat0, lon1);
                Vector3 p10 = SurfacePoint(lat1, lon0);
                Vector3 p11 = SurfacePoint(lat1, lon1);
                p00 = ExpandFromCenter(p00);
                p01 = ExpandFromCenter(p01);
                p10 = ExpandFromCenter(p10);
                p11 = ExpandFromCenter(p11);

                int start = vertices.Count;
                AddVertex(p00, new Vector2(0f, 0f), ref vertices, ref normals, ref uvs);
                AddVertex(p10, new Vector2(0f, 1f), ref vertices, ref normals, ref uvs);
                AddVertex(p11, new Vector2(1f, 1f), ref vertices, ref normals, ref uvs);
                AddVertex(p01, new Vector2(1f, 0f), ref vertices, ref normals, ref uvs);

                // Clockwise outward.
                var tris = trianglesByMat[tileIndex];
                tris.Add(start + 0);
                tris.Add(start + 1);
                tris.Add(start + 2);
                tris.Add(start + 0);
                tris.Add(start + 2);
                tris.Add(start + 3);
            }
        }

        _runtimeMesh = new Mesh { name = "PlanetTiles_Combined" };
        _runtimeMesh.indexFormat = vertices.Count > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        _runtimeMesh.SetVertices(vertices);
        _runtimeMesh.SetNormals(normals);
        _runtimeMesh.SetUVs(0, uvs);
        _runtimeMesh.subMeshCount = materialCount;
        for (int i = 0; i < materialCount; i++)
            _runtimeMesh.SetTriangles(trianglesByMat[i], i, true);
        _runtimeMesh.RecalculateBounds();

        _tilesFilter.sharedMesh = _runtimeMesh;
        _runtimeMaterials = BuildMaterials();
        _tilesRenderer.sharedMaterials = _runtimeMaterials;
        if (_tilesCollider != null)
        {
            _tilesCollider.sharedMesh = null;
            _tilesCollider.sharedMesh = _runtimeMesh;
            _tilesCollider.enabled = useTileMeshCollider;
        }
    }

    Material[] BuildMaterials()
    {
        var mats = new Material[palette.Count];
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Texture");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        for (int i = 0; i < mats.Length; i++)
        {
            Material mat = new Material(shader);
            mat.name = $"PlanetTile_{i}_Unlit";
            // If there's no texture assigned (albedo == null), keep a visible fallback color.
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", Color.white);
            else
                mat.color = Color.white;
            if (palette.TryGetAlbedo(i, out Texture2D tex) && tex != null)
            {
                mat.mainTexture = tex;
                if (mat.HasProperty("_BaseMap"))
                    mat.SetTexture("_BaseMap", tex);
                if (mat.HasProperty("_MainTex"))
                    mat.SetTexture("_MainTex", tex);
            }

            mats[i] = mat;
        }

        return mats;
    }

    Vector3 SurfacePoint(float latDeg, float lonDeg)
    {
        float lat = latDeg * Mathf.Deg2Rad;
        float lon = lonDeg * Mathf.Deg2Rad;
        Vector3 up = new Vector3(
            Mathf.Cos(lat) * Mathf.Cos(lon),
            Mathf.Sin(lat),
            Mathf.Cos(lat) * Mathf.Sin(lon));
        return _planet.Center + up * (_planet.Radius + surfaceLift);
    }

    Vector3 ExpandFromCenter(Vector3 point)
    {
        if (Mathf.Abs(overlap - 1f) < 0.0001f)
            return point;

        Vector3 dir = (point - _planet.Center).normalized;
        float dist = (point - _planet.Center).magnitude;
        return _planet.Center + dir * (dist * overlap);
    }

    void AddVertex(Vector3 point, Vector2 uv, ref List<Vector3> vertices, ref List<Vector3> normals, ref List<Vector2> uvs)
    {
        Vector3 normal = (point - _planet.Center).normalized;
        vertices.Add(point);
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
    }

    void CleanupRuntimeAssets()
    {
        if (_runtimeMesh != null)
        {
            if (Application.isPlaying) Destroy(_runtimeMesh);
            else DestroyImmediate(_runtimeMesh);
            _runtimeMesh = null;
        }

        if (_runtimeMaterials != null)
        {
            for (int i = 0; i < _runtimeMaterials.Length; i++)
            {
                if (_runtimeMaterials[i] == null)
                    continue;
                if (Application.isPlaying) Destroy(_runtimeMaterials[i]);
                else DestroyImmediate(_runtimeMaterials[i]);
            }
            _runtimeMaterials = null;
        }
    }

    int CellIndex(int lat, int lon) => lat * longitudeBands + lon;

    static int Mod(int value, int modulus)
    {
        int m = value % modulus;
        return m < 0 ? m + modulus : m;
    }
}
