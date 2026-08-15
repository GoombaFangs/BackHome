using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Builds a walkable spherical planet (Outer Wilds style).
/// Optional heightmap-driven VisualShell for mountains/valleys.
/// Also builds in the Scene view (Edit Mode) so you can see it without Play.
/// </summary>
[ExecuteAlways]
[DefaultExecutionOrder(-100)]
public class SphericalPlanet : MonoBehaviour
{
    public static SphericalPlanet Instance { get; private set; }

    [Header("Shape")]
    [SerializeField] float radius = 40f;
    [SerializeField] int latitudeSegments = 20;
    [SerializeField] int longitudeSegments = 28;
    [SerializeField] string groundLayerName = "Ground";

    [Header("Look")]
    [SerializeField] Texture2D albedoTexture;
    [SerializeField] Color tint = Color.white;
    [SerializeField] float textureTiling = 4f;

    [Header("Hybrid Visual Shell")]
    [SerializeField] bool useVisualShell = false;
    [SerializeField] Texture2D shellColorMap;
    [SerializeField] Texture2D shellNormalMap;
    [SerializeField] Texture2D shellHeightMap;
    [SerializeField] float shellRadiusOffset = 0f;
    [SerializeField] float shellHeightAmplitude = 1.6f;
    [SerializeField] float shellSmoothness = 0.05f;
    [SerializeField] float shellNormalStrength = 0f;
    [SerializeField] int shellLatitudeSegments = 24;
    [SerializeField] int shellLongitudeSegments = 32;
    [SerializeField] bool castShellShadows = true;

    MeshFilter _filter;
    MeshRenderer _renderer;
    SphereCollider _collider;
    Material _runtimeMaterial;
    Mesh _runtimeMesh;
    Transform _shellRoot;
    MeshFilter _shellFilter;
    MeshRenderer _shellRenderer;
    Material _runtimeShellMaterial;
    Mesh _runtimeShellMesh;
    bool _buildQueued;
    bool _shellVisible = true;

    public Vector3 Center => transform.position;
    public float Radius => radius;
    public bool HasHeightTerrain =>
        useVisualShell
        && shellHeightMap != null
        && shellHeightAmplitude > 0.0001f;

    public void SetVisualShellVisible(bool visible)
    {
        _shellVisible = visible;
        if (_shellRenderer == null)
            return;

#if UNITY_EDITOR
        // Avoid SendMessage-during-OnValidate when toggling renderer.enabled.
        if (!Application.isPlaying && UnityEditor.EditorApplication.isUpdating)
            return;
#endif
        _shellRenderer.enabled = visible && useVisualShell && _runtimeShellMaterial != null;
    }

    void OnEnable()
    {
        if (Application.isPlaying)
        {
            Instance = this;
            if (GetComponent<PlanetParticleGravity>() == null)
                gameObject.AddComponent<PlanetParticleGravity>();
        }

        BuildPlanet();
    }

    void OnDisable()
    {
        if (Instance == this)
            Instance = null;
    }

    void OnDestroy()
    {
        if (Instance == this)
            Instance = null;

        CleanupOwnedAssets();
    }

    void OnValidate()
    {
        shellHeightAmplitude = Mathf.Max(0f, shellHeightAmplitude);
        shellLatitudeSegments = Mathf.Max(16, shellLatitudeSegments);
        shellLongitudeSegments = Mathf.Max(24, shellLongitudeSegments);
        QueueBuild();
    }

    void QueueBuild()
    {
#if UNITY_EDITOR
        if (_buildQueued)
            return;

        _buildQueued = true;
        EditorApplication.delayCall += () =>
        {
            _buildQueued = false;
            if (this != null)
                BuildPlanet();
        };
#else
        BuildPlanet();
#endif
    }

    /// <summary>
    /// Radial distance from planet center to the heightmapped surface (no ride offset).
    /// </summary>
    public float GetTerrainRadius(Vector3 directionFromCenter)
    {
        Vector3 dir = directionFromCenter.sqrMagnitude > 0.0001f
            ? directionFromCenter.normalized
            : Vector3.up;

        float terrainRadius = radius + shellRadiusOffset;
        if (!HasHeightTerrain)
            return Mathf.Max(0.01f, terrainRadius);

        DirectionToUv(dir, out float u, out float v);
        terrainRadius += SampleHeight01(u, v) * shellHeightAmplitude;
        return Mathf.Max(0.01f, terrainRadius);
    }

    /// <summary>
    /// Approximate surface normal from heightmap slopes (falls back to radial up).
    /// </summary>
    public Vector3 GetTerrainNormal(Vector3 directionFromCenter)
    {
        Vector3 dir = directionFromCenter.sqrMagnitude > 0.0001f
            ? directionFromCenter.normalized
            : Vector3.up;

        if (!HasHeightTerrain)
            return dir;

        DirectionToUv(dir, out float u, out float v);
        const float eps = 0.0025f;
        float hC = SampleHeight01(u, v);
        float hU = SampleHeight01(u + eps, v);
        float hV = SampleHeight01(u, Mathf.Clamp01(v + eps));

        // Local tangent frame on the sphere.
        Vector3 east = Vector3.Cross(Vector3.up, dir);
        if (east.sqrMagnitude < 0.0001f)
            east = Vector3.Cross(Vector3.right, dir);
        east.Normalize();
        Vector3 north = Vector3.Cross(dir, east).normalized;

        // Arc length of one UV step near this point.
        float circumference = 2f * Mathf.PI * radius;
        float stepEast = circumference * eps;
        float stepNorth = Mathf.PI * radius * eps;
        stepEast = Mathf.Max(0.01f, stepEast);
        stepNorth = Mathf.Max(0.01f, stepNorth);

        Vector3 pC = dir * (radius + shellRadiusOffset + hC * shellHeightAmplitude);
        Vector3 pE = (dir + east * (stepEast / radius)).normalized
                     * (radius + shellRadiusOffset + hU * shellHeightAmplitude);
        Vector3 pN = (dir + north * (stepNorth / radius)).normalized
                     * (radius + shellRadiusOffset + hV * shellHeightAmplitude);

        Vector3 n = Vector3.Cross(pE - pC, pN - pC);
        if (n.sqrMagnitude < 0.0001f)
            return dir;
        n.Normalize();
        if (Vector3.Dot(n, dir) < 0f)
            n = -n;
        return n;
    }

    public Vector3 GetSurfacePoint(Vector3 directionFromCenter, float hover = 0f)
    {
        Vector3 dir = directionFromCenter.sqrMagnitude > 0.0001f
            ? directionFromCenter.normalized
            : Vector3.up;
        return Center + dir * (GetTerrainRadius(dir) + hover);
    }

    public Vector3 GetUpAt(Vector3 worldPosition)
    {
        Vector3 up = worldPosition - Center;
        return up.sqrMagnitude > 0.0001f ? up.normalized : Vector3.up;
    }

    void BuildPlanet()
    {
        _filter = gameObject.GetComponent<MeshFilter>();
        if (_filter == null)
            _filter = gameObject.AddComponent<MeshFilter>();

        _renderer = gameObject.GetComponent<MeshRenderer>();
        if (_renderer == null)
            _renderer = gameObject.AddComponent<MeshRenderer>();

        var meshCollider = gameObject.GetComponent<MeshCollider>();
        if (meshCollider != null)
            DestroyOwned(meshCollider);

        _collider = gameObject.GetComponent<SphereCollider>();
        if (_collider == null)
            _collider = gameObject.AddComponent<SphereCollider>();
        // Base collider stays spherical; walking uses heightmap sampling when available.
        _collider.radius = radius;
        _collider.center = Vector3.zero;

        int layer = LayerMask.NameToLayer(groundLayerName);
        gameObject.layer = layer >= 0 ? layer : 3;

        CleanupOwnedAssets();

        _runtimeMesh = CreateSphereMesh(radius, latitudeSegments, longitudeSegments, textureTiling);
        _runtimeMesh.name = "SphericalPlanetMesh";
        _runtimeMesh.hideFlags = HideFlags.DontSave;
        _filter.sharedMesh = _runtimeMesh;

        _runtimeMaterial = CreateMaterial();
        _runtimeMaterial.hideFlags = HideFlags.DontSave;
        _renderer.sharedMaterial = _runtimeMaterial;

        BuildVisualShell();
    }

    void CleanupOwnedAssets()
    {
        if (_runtimeMaterial != null)
        {
            DestroyOwned(_runtimeMaterial);
            _runtimeMaterial = null;
        }

        if (_runtimeMesh != null)
        {
            DestroyOwned(_runtimeMesh);
            _runtimeMesh = null;
        }

        if (_runtimeShellMaterial != null)
        {
            DestroyOwned(_runtimeShellMaterial);
            _runtimeShellMaterial = null;
        }

        if (_runtimeShellMesh != null)
        {
            DestroyOwned(_runtimeShellMesh);
            _runtimeShellMesh = null;
        }
    }

    void DestroyOwned(Object obj)
    {
        if (obj == null)
            return;

        if (Application.isPlaying)
            Destroy(obj);
        else
            DestroyImmediate(obj);
    }

    Material CreateMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Texture");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        var mat = new Material(shader);
        mat.name = "PlanetSurface_Unlit";
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", tint);
        else
            mat.color = tint;

        if (albedoTexture != null)
        {
            mat.mainTexture = albedoTexture;
            if (mat.HasProperty("_BaseMap"))
                mat.SetTexture("_BaseMap", albedoTexture);
            if (mat.HasProperty("_MainTex"))
                mat.SetTexture("_MainTex", albedoTexture);
        }

        return mat;
    }

    void BuildVisualShell()
    {
        EnsureShellObjects();

        if (_shellRoot == null || _shellFilter == null || _shellRenderer == null)
            return;

        if (!useVisualShell)
        {
            _shellFilter.sharedMesh = null;
            _shellRenderer.sharedMaterial = null;
            _shellRenderer.enabled = false;
            return;
        }

        if (_runtimeShellMaterial != null)
        {
            DestroyOwned(_runtimeShellMaterial);
            _runtimeShellMaterial = null;
        }

        if (_runtimeShellMesh != null)
        {
            DestroyOwned(_runtimeShellMesh);
            _runtimeShellMesh = null;
        }

        int latSegments = Mathf.Max(latitudeSegments, shellLatitudeSegments);
        int lonSegments = Mathf.Max(longitudeSegments, shellLongitudeSegments);
        _runtimeShellMesh = CreateSphereMesh(
            radius + shellRadiusOffset,
            latSegments,
            lonSegments,
            textureTiling,
            shellHeightMap,
            shellHeightAmplitude);
        _runtimeShellMesh.name = "PlanetVisualShellMesh";
        _runtimeShellMesh.hideFlags = HideFlags.DontSave;
        _shellFilter.sharedMesh = _runtimeShellMesh;

        _runtimeShellMaterial = CreateShellMaterial();
        if (_runtimeShellMaterial != null)
            _runtimeShellMaterial.hideFlags = HideFlags.DontSave;

        _shellRenderer.sharedMaterial = _runtimeShellMaterial;
        _shellRenderer.enabled = _shellVisible && _runtimeShellMaterial != null;
        _shellRenderer.shadowCastingMode = castShellShadows
            ? UnityEngine.Rendering.ShadowCastingMode.On
            : UnityEngine.Rendering.ShadowCastingMode.Off;
        _shellRenderer.receiveShadows = true;
    }

    void EnsureShellObjects()
    {
        // Never mutate hierarchy while this object is the Prefab Asset itself.
        if (IsPrefabAssetContext(gameObject))
        {
            _shellRoot = null;
            _shellFilter = null;
            _shellRenderer = null;
            return;
        }

        CleanupDuplicateShells();

        if (_shellRoot == null)
        {
            Transform existing = transform.Find("VisualShell");
            if (existing != null)
                _shellRoot = existing;
        }

        if (_shellRoot == null)
        {
            var root = new GameObject("VisualShell");
            root.hideFlags = HideFlags.DontSave;
            root.transform.SetParent(transform, false);
            root.transform.localPosition = Vector3.zero;
            root.transform.localRotation = Quaternion.identity;
            root.transform.localScale = Vector3.one;
            _shellRoot = root.transform;
        }

        _shellRoot.gameObject.layer = gameObject.layer;
        _shellRoot.gameObject.hideFlags = HideFlags.DontSave;

        _shellFilter = _shellRoot.GetComponent<MeshFilter>();
        if (_shellFilter == null)
            _shellFilter = _shellRoot.gameObject.AddComponent<MeshFilter>();

        _shellRenderer = _shellRoot.GetComponent<MeshRenderer>();
        if (_shellRenderer == null)
            _shellRenderer = _shellRoot.gameObject.AddComponent<MeshRenderer>();
    }

    void CleanupDuplicateShells()
    {
        Transform keep = _shellRoot != null ? _shellRoot : transform.Find("VisualShell");
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child == null || child.name != "VisualShell")
                continue;

            if (keep == null)
            {
                keep = child;
                continue;
            }

            if (child == keep)
                continue;

            DestroyOwned(child.gameObject);
        }

        _shellRoot = keep;
    }

    static bool IsPrefabAssetContext(GameObject go)
    {
#if UNITY_EDITOR
        return PrefabUtility.IsPartOfPrefabAsset(go);
#else
        return false;
#endif
    }

    Material CreateShellMaterial()
    {
        // Handpainted casual look: unlit when no normal map, soft lit otherwise.
        Shader shader = shellNormalMap != null
            ? Shader.Find("Universal Render Pipeline/Lit")
            : Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (shader == null)
            return null;

        var mat = new Material(shader) { name = "PlanetVisualShell" };
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", Color.white);
        else if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", Color.white);

        if (shellColorMap != null)
        {
            mat.mainTexture = shellColorMap;
            if (mat.HasProperty("_BaseMap"))
                mat.SetTexture("_BaseMap", shellColorMap);
            if (mat.HasProperty("_MainTex"))
                mat.SetTexture("_MainTex", shellColorMap);
        }

        if (shellNormalMap != null)
        {
            if (mat.HasProperty("_BumpMap"))
                mat.SetTexture("_BumpMap", shellNormalMap);
            if (mat.HasProperty("_NormalMap"))
                mat.SetTexture("_NormalMap", shellNormalMap);
            if (mat.HasProperty("_BumpScale"))
                mat.SetFloat("_BumpScale", shellNormalStrength);
            mat.EnableKeyword("_NORMALMAP");
        }

        if (shellHeightMap != null)
        {
            if (mat.HasProperty("_ParallaxMap"))
                mat.SetTexture("_ParallaxMap", shellHeightMap);
            if (mat.HasProperty("_Parallax"))
                mat.SetFloat("_Parallax", Mathf.Clamp(shellHeightAmplitude * 0.004f, 0f, 0.08f));
        }

        if (mat.HasProperty("_Smoothness"))
            mat.SetFloat("_Smoothness", shellSmoothness);

        return mat;
    }

    float SampleHeight01(float u, float v)
    {
        if (shellHeightMap == null)
            return 0f;

        u = u - Mathf.Floor(u);
        v = Mathf.Clamp01(v);

        if (shellHeightMap.isReadable)
            return shellHeightMap.GetPixelBilinear(u, v).grayscale;

        // Non-readable import: coarse sample via GPU-less fallback (no displacement).
        return 0f;
    }

    static void DirectionToUv(Vector3 dir, out float u, out float v)
    {
        v = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) / Mathf.PI + 0.5f;
        u = Mathf.Atan2(dir.z, dir.x) / (Mathf.PI * 2f);
        if (u < 0f)
            u += 1f;
    }

    static Mesh CreateSphereMesh(
        float radius,
        int latSegments,
        int lonSegments,
        float uvTiles,
        Texture2D heightMap = null,
        float heightAmplitude = 0f)
    {
        latSegments = Mathf.Max(3, latSegments);
        lonSegments = Mathf.Max(3, lonSegments);

        int ringVerts = lonSegments + 1;
        int vertCount = (latSegments + 1) * ringVerts;
        var vertices = new Vector3[vertCount];
        var normals = new Vector3[vertCount];
        var uvs = new Vector2[vertCount];
        bool displace = heightMap != null && heightMap.isReadable && heightAmplitude > 0f;

        for (int lat = 0; lat <= latSegments; lat++)
        {
            float v = lat / (float)latSegments;
            float pitch = Mathf.PI * (-0.5f + v); // -90..+90
            float y = Mathf.Sin(pitch);
            float ringRadius = Mathf.Cos(pitch);

            for (int lon = 0; lon <= lonSegments; lon++)
            {
                float u = lon / (float)lonSegments;
                float yaw = u * Mathf.PI * 2f;
                var normal = new Vector3(
                    Mathf.Cos(yaw) * ringRadius,
                    y,
                    Mathf.Sin(yaw) * ringRadius);

                int index = lat * ringVerts + lon;
                float displacedRadius = radius;
                if (displace)
                    displacedRadius += heightMap.GetPixelBilinear(u, v).grayscale * heightAmplitude;

                vertices[index] = normal * displacedRadius;
                normals[index] = normal;
                uvs[index] = new Vector2(u * uvTiles, v * uvTiles);
            }
        }

        int triCount = latSegments * lonSegments * 6;
        var triangles = new int[triCount];
        int t = 0;
        for (int lat = 0; lat < latSegments; lat++)
        {
            for (int lon = 0; lon < lonSegments; lon++)
            {
                int current = lat * ringVerts + lon;
                int next = current + ringVerts;

                triangles[t++] = current;
                triangles[t++] = next;
                triangles[t++] = current + 1;

                triangles[t++] = current + 1;
                triangles[t++] = next;
                triangles[t++] = next + 1;
            }
        }

        var mesh = new Mesh();
        mesh.indexFormat = vertCount > 65535
            ? UnityEngine.Rendering.IndexFormat.UInt32
            : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.vertices = vertices;
        mesh.normals = normals;
        mesh.uv = uvs;
        mesh.triangles = triangles;
        if (displace)
            mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
