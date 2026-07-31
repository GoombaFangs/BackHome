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
    [SerializeField] int tilesAroundEquator = 24;
    [SerializeField] int fillTileIndex = 0;
    [SerializeField] float overlap = 1.03f;
    [SerializeField] float surfaceLift = 0.15f;
    [SerializeField] bool hidePlanetBaseMesh = true;
    [SerializeField] bool showTileVisuals = true;
    [Tooltip("When tiles are shown, hide the VisualShell so they don't z-fight.")]
    [SerializeField] bool hideShellWhileShowingTiles = true;
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
    public bool ShowTileVisuals => showTileVisuals;

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

        if (_planet == null)
            _planet = GetComponent<SphericalPlanet>();

        if (_tilesRenderer != null)
            _tilesRenderer.enabled = showTileVisuals;
        ApplyBaseMeshVisibility();
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

    /// <summary>
    /// Paints a circular brush around a cell. Rebuilds visuals once.
    /// </summary>
    public bool PaintBrush(int centerLat, int centerLon, int tileIndex, int radiusCells)
    {
        if (!HasValidMap())
            FillAll(fillTileIndex);

        radiusCells = Mathf.Max(0, radiusCells);
        tileIndex = Mathf.Clamp(tileIndex, 0, palette != null ? Mathf.Max(0, palette.Count - 1) : 0);

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
                int cell = CellIndex(lat, lon);
                if (tileIndices[cell] == tileIndex)
                    continue;

                tileIndices[cell] = tileIndex;
                changed = true;
            }
        }

        if (changed)
            RebuildVisuals();
        return changed;
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
        // Keep tiles clearly above heightmapped shell / base sphere.
        float lift = Mathf.Max(surfaceLift, _planet.Radius * 0.003f);

        for (int lat = 0; lat < latitudeBands; lat++)
        {
            float lat0 = -90f + lat * latStep;
            float lat1 = -90f + (lat + 1) * latStep;

            for (int lon = 0; lon < longitudeBands; lon++)
            {
                int tileIndex = Mathf.Clamp(GetTileIndex(lat, lon), 0, materialCount - 1);
                float lon0 = lon * lonStep;
                float lon1 = (lon + 1) * lonStep;

                // Corners in LOCAL space (mesh lives under the planet transform).
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

                int start = vertices.Count;
                // SW, SE, NE, NW — then pick winding that faces outward.
                Vector3 sw = p00;
                Vector3 se = p01;
                Vector3 ne = p11;
                Vector3 nw = p10;

                Vector3 centerLocal = (sw + se + ne + nw) * 0.25f;
                Vector3 outward = centerLocal.sqrMagnitude > 0.0001f
                    ? centerLocal.normalized
                    : Vector3.up;

                // Candidate: SW -> SE -> NE -> NW (often CCW from outside in Unity).
                Vector3 n = Vector3.Cross(se - sw, ne - sw);
                bool flip = Vector3.Dot(n, outward) < 0f;

                if (!flip)
                {
                    AddVertexLocal(sw, new Vector2(0f, 0f), ref vertices, ref normals, ref uvs);
                    AddVertexLocal(se, new Vector2(1f, 0f), ref vertices, ref normals, ref uvs);
                    AddVertexLocal(ne, new Vector2(1f, 1f), ref vertices, ref normals, ref uvs);
                    AddVertexLocal(nw, new Vector2(0f, 1f), ref vertices, ref normals, ref uvs);
                }
                else
                {
                    AddVertexLocal(sw, new Vector2(0f, 0f), ref vertices, ref normals, ref uvs);
                    AddVertexLocal(nw, new Vector2(0f, 1f), ref vertices, ref normals, ref uvs);
                    AddVertexLocal(ne, new Vector2(1f, 1f), ref vertices, ref normals, ref uvs);
                    AddVertexLocal(se, new Vector2(1f, 0f), ref vertices, ref normals, ref uvs);
                }

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
            if (mat.HasProperty("_Cull"))
                mat.SetFloat("_Cull", 0f); // Off — avoid missing tiles from winding issues
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

    /// <summary>Local-space point on the (optionally heightmapped) planet surface.</summary>
    Vector3 LocalSurfacePoint(float latDeg, float lonDeg, float lift)
    {
        float lat = latDeg * Mathf.Deg2Rad;
        float lon = lonDeg * Mathf.Deg2Rad;
        Vector3 up = new Vector3(
            Mathf.Cos(lat) * Mathf.Cos(lon),
            Mathf.Sin(lat),
            Mathf.Cos(lat) * Mathf.Sin(lon));
        float terrainRadius = _planet.GetTerrainRadius(up);
        // GetTerrainRadius is in world units along direction; with uniform scale this matches local.
        float scale = Mathf.Max(transform.lossyScale.x, 0.0001f);
        return up * ((terrainRadius + lift) / scale);
    }

    void AddVertexLocal(Vector3 localPoint, Vector2 uv, ref List<Vector3> vertices, ref List<Vector3> normals, ref List<Vector2> uvs)
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

        // Tiles replace the painted surface — hide shell to prevent diamond z-fighting artifacts.
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
