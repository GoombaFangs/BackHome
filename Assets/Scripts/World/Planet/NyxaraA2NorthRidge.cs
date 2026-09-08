using UnityEngine;

/// <summary>
/// Scene visual for the baked north ridge. Walk collision is kinematic
/// (<see cref="NyxaraRouteBounds"/>). This mesh has no collider.
/// Mesh is assigned by an editor bake, not rebuilt every frame.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class NyxaraA2NorthRidge : MonoBehaviour
{
    public const string RootName = "NyxaraA2NorthRidge";
    public const string MeshAssetPath = "Assets/Resources/Galaxy/Nyxara/Terrain/A2/NyxaraA2NorthRidge.asset";
    public const string MaterialAssetPath = "Assets/Resources/Galaxy/Nyxara/Terrain/A2/NyxaraA2NorthRidge.mat";

    [SerializeField] Mesh bakedMesh;
    [SerializeField] Material ridgeMaterial;
    [SerializeField] [TextArea(2, 6)] string bakeReport;

    MeshFilter _filter;
    MeshRenderer _renderer;
    Mesh _runtimeMesh;

    public Mesh BakedMesh => bakedMesh;
    public string BakeReport => bakeReport;

    public void SetBaked(Mesh mesh, Material material, string report)
    {
        bakedMesh = mesh;
        ridgeMaterial = material;
        bakeReport = report;
        Assign(mesh, material);
    }

    void OnEnable()
    {
        SnapIdentityUnderPlanet();
        EnsureVisual();
    }

    void OnDisable()
    {
        ReleaseRuntimeMesh();
    }

    void OnDestroy()
    {
        ReleaseRuntimeMesh();
    }

    void SnapIdentityUnderPlanet()
    {
        var planet = GetComponentInParent<SphericalPlanet>();
        if (planet == null)
            return;
        if (transform.parent == planet.transform)
        {
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            return;
        }

        var session = GetComponentInParent<NyxaraTerrainStudySession>();
        if (session != null && transform.parent == session.transform)
        {
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
        }
    }

    void EnsureVisual()
    {
        if (_filter == null)
            _filter = GetComponent<MeshFilter>();
        if (_renderer == null)
            _renderer = GetComponent<MeshRenderer>();

        if (bakedMesh != null)
        {
            Assign(bakedMesh, ridgeMaterial);
            return;
        }

#if UNITY_EDITOR
        Mesh asset = UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>(MeshAssetPath);
        Material mat = ridgeMaterial != null
            ? ridgeMaterial
            : UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(MaterialAssetPath);
        if (asset != null)
        {
            bakedMesh = asset;
            ridgeMaterial = mat;
            Assign(asset, mat);
            return;
        }

        // First open before a disk bake: build once in memory so the Scene View has geometry.
        var session = GetComponentInParent<NyxaraTerrainStudySession>();
        var planet = GetComponentInParent<SphericalPlanet>();
        if (planet == null)
            return;
        var tileMap = planet.GetComponent<PlanetTileMap>();
        var plan = session != null ? session.Plan : (tileMap != null ? tileMap.WorkPlan : null);
        var overlay = session != null ? session.BoundaryOverlay : null;
        float walk = tileMap != null
            ? tileMap.GetWalkSurfaceRadius(PlanetTileMap.StudyLonLatToDirection(35f, 25f))
            : planet.Radius;
        var settings = NyxaraA2NorthRidgeMeshBuilder.FromPlan(plan, overlay, walk);
        ReleaseRuntimeMesh();
        _runtimeMesh = NyxaraA2NorthRidgeMeshBuilder.Build(settings);
        bakeReport = NyxaraA2NorthRidgeMeshBuilder.Describe(settings, _runtimeMesh) +
                     " (in-memory preview — use Bake A2 North Ridge to save the asset).";
        Assign(_runtimeMesh, mat);
#endif
    }

    void Assign(Mesh mesh, Material material)
    {
        if (_filter == null)
            _filter = GetComponent<MeshFilter>();
        if (_renderer == null)
            _renderer = GetComponent<MeshRenderer>();
        if (_filter != null)
            _filter.sharedMesh = mesh;
        if (_renderer != null && material != null)
            _renderer.sharedMaterial = material;
        NyxaraTerrainCollision.ClearBlockingMesh(gameObject);
    }

    void ReleaseRuntimeMesh()
    {
        if (_runtimeMesh == null)
            return;
        if (Application.isPlaying)
            Destroy(_runtimeMesh);
        else
            DestroyImmediate(_runtimeMesh);
        _runtimeMesh = null;
    }
}
