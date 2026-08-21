using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Builds a walkable spherical planet (Outer Wilds style).
/// Optional heightmap-driven VisualShell for mountains/valleys,
/// or an authored FBX/prefab assigned as Custom Visual Model.
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

    [Header("Custom Visual Model")]
    [Tooltip("Optional authored planet mesh (FBX/prefab). Replaces the procedural visual shell.")]
    [SerializeField] GameObject customVisualModel;
    [Tooltip("Uniform-scale the custom model so its bounds match the gameplay radius.")]
    [SerializeField] bool fitCustomVisualToRadius = true;

    const string CustomVisualName = "CustomVisual";

    MeshFilter _filter;
    MeshRenderer _renderer;
    SphereCollider _collider;
    Material _runtimeMaterial;
    Mesh _runtimeMesh;
    Transform _shellRoot;
    MeshFilter _shellFilter;
    MeshRenderer _shellRenderer;
    Transform _customVisualInstance;
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

    bool HasVisualShell => customVisualModel != null || useVisualShell;

    public void SetVisualShellVisible(bool visible)
    {
        _shellVisible = visible;
        bool show = visible && HasVisualShell;

        if (_customVisualInstance != null)
            _customVisualInstance.gameObject.SetActive(show);

        if (_shellRenderer == null)
            return;

#if UNITY_EDITOR
        // Avoid SendMessage-during-OnValidate when toggling renderer.enabled.
        if (!Application.isPlaying && UnityEditor.EditorApplication.isUpdating)
            return;
#endif
        _shellRenderer.enabled = show && customVisualModel == null && _runtimeShellMaterial != null;
    }

    void OnEnable()
    {
        if (Application.isPlaying)
        {
            Instance = this;
            if (GetComponent<PlanetParticleGravity>() == null)
                gameObject.AddComponent<PlanetParticleGravity>();
        }

        if (IsEditorBusy)
        {
            QueueBuild();
            return;
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
        EditorApplication.delayCall += FlushQueuedBuild;
#else
        BuildPlanet();
#endif
    }

#if UNITY_EDITOR
    void FlushQueuedBuild()
    {
        if (this == null)
        {
            _buildQueued = false;
            return;
        }

        if (IsEditorBusy)
        {
            EditorApplication.delayCall += FlushQueuedBuild;
            return;
        }

        _buildQueued = false;
        BuildPlanet();
    }
#endif

    static bool IsEditorBusy
    {
        get
        {
#if UNITY_EDITOR
            return !Application.isPlaying
                && (EditorApplication.isUpdating || EditorApplication.isCompiling);
#else
            return false;
#endif
        }
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
        if (IsEditorBusy)
        {
            QueueBuild();
            return;
        }

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

        if (customVisualModel != null)
        {
            ClearProceduralShell();
            SpawnOrRefreshCustomVisual();
            return;
        }

        ClearCustomVisual();

        if (!useVisualShell)
        {
            ClearProceduralShell();
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

    void ClearProceduralShell()
    {
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

        _shellFilter.sharedMesh = null;
        _shellRenderer.sharedMaterial = null;
        _shellRenderer.enabled = false;
    }

    void SpawnOrRefreshCustomVisual()
    {
        if (_customVisualInstance == null && _shellRoot != null)
        {
            Transform existing = _shellRoot.Find(CustomVisualName);
            if (existing != null)
                _customVisualInstance = existing;
        }

        if (_customVisualInstance == null)
        {
            var instance = Instantiate(customVisualModel, _shellRoot);
            instance.name = CustomVisualName;
            instance.hideFlags = HideFlags.DontSave;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            _customVisualInstance = instance.transform;
        }

        ApplyCustomVisualSettings();
    }

    void ApplyCustomVisualSettings()
    {
        if (_customVisualInstance == null)
            return;

        _customVisualInstance.gameObject.hideFlags = HideFlags.DontSave;
        _customVisualInstance.gameObject.SetActive(_shellVisible);
        ApplyLayerRecursive(_customVisualInstance, gameObject.layer);

        foreach (var col in _customVisualInstance.GetComponentsInChildren<Collider>(true))
            col.enabled = false;

        foreach (var animator in _customVisualInstance.GetComponentsInChildren<Animator>(true))
            animator.enabled = false;

        var shadowMode = castShellShadows
            ? UnityEngine.Rendering.ShadowCastingMode.On
            : UnityEngine.Rendering.ShadowCastingMode.Off;
        foreach (var meshRenderer in _customVisualInstance.GetComponentsInChildren<Renderer>(true))
        {
            meshRenderer.shadowCastingMode = shadowMode;
            meshRenderer.receiveShadows = true;
        }

        if (fitCustomVisualToRadius)
            FitCustomVisualToRadius();
    }

    void FitCustomVisualToRadius()
    {
        Transform model = _customVisualInstance;
        if (model == null)
            return;

        model.localScale = Vector3.one;
        model.localPosition = Vector3.zero;

        if (!TryGetLocalMeshBounds(model, out Bounds local))
            return;

        float currentRadius = Mathf.Max(local.extents.x, Mathf.Max(local.extents.y, local.extents.z));
        if (currentRadius < 0.0001f)
            return;

        float targetRadius = Mathf.Max(0.01f, radius + shellRadiusOffset);
        float scale = targetRadius / currentRadius;
        model.localScale = Vector3.one * scale;
        model.localPosition = -local.center * scale;
    }

    static bool TryGetLocalMeshBounds(Transform model, out Bounds local)
    {
        local = new Bounds();
        bool has = false;

        var meshFilters = model.GetComponentsInChildren<MeshFilter>(true);
        for (int i = 0; i < meshFilters.Length; i++)
        {
            MeshFilter filter = meshFilters[i];
            if (filter.sharedMesh == null)
                continue;
            EncapsulateLocalBounds(model, filter.transform, filter.sharedMesh.bounds, ref local, ref has);
        }

        var skinned = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
        for (int i = 0; i < skinned.Length; i++)
        {
            SkinnedMeshRenderer renderer = skinned[i];
            if (renderer.sharedMesh == null)
                continue;
            EncapsulateLocalBounds(model, renderer.transform, renderer.localBounds, ref local, ref has);
        }

        return has;
    }

    static void EncapsulateLocalBounds(
        Transform model,
        Transform meshTransform,
        Bounds meshLocalBounds,
        ref Bounds local,
        ref bool has)
    {
        Vector3 extents = meshLocalBounds.extents;
        Vector3 center = meshLocalBounds.center;
        for (int x = -1; x <= 1; x += 2)
        {
            for (int y = -1; y <= 1; y += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    var corner = new Vector3(
                        center.x + extents.x * x,
                        center.y + extents.y * y,
                        center.z + extents.z * z);
                    Vector3 inModel = model.InverseTransformPoint(meshTransform.TransformPoint(corner));
                    if (!has)
                    {
                        local = new Bounds(inModel, Vector3.zero);
                        has = true;
                    }
                    else
                    {
                        local.Encapsulate(inModel);
                    }
                }
            }
        }
    }

    static void ApplyLayerRecursive(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++)
            ApplyLayerRecursive(root.GetChild(i), layer);
    }

    void ClearCustomVisual()
    {
        if (_customVisualInstance != null)
        {
            DestroyOwned(_customVisualInstance.gameObject);
            _customVisualInstance = null;
        }

        if (_shellRoot == null)
            return;

        Transform existing = _shellRoot.Find(CustomVisualName);
        if (existing != null)
            DestroyOwned(existing.gameObject);
    }

    void EnsureShellObjects()
    {
        // Never mutate hierarchy while this object is the Prefab Asset itself.
        if (IsPrefabAssetContext(gameObject))
        {
            _shellRoot = null;
            _shellFilter = null;
            _shellRenderer = null;
            _customVisualInstance = null;
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
