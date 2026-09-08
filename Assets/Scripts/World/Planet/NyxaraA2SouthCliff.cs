using UnityEngine;

/// <summary>
/// Scene visual for the baked south cliff. Walk collision is kinematic
/// (<see cref="NyxaraRouteBounds"/>). This mesh has no collider.
/// Mesh is assigned by an editor bake, not rebuilt every frame.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class NyxaraA2SouthCliff : MonoBehaviour
{
    public const string RootName = "NyxaraA2SouthCliff";
    public const string MeshAssetPath = "Assets/Resources/Galaxy/Nyxara/Terrain/A2/NyxaraA2SouthCliff.asset";
    public const string MaterialAssetPath = "Assets/Resources/Galaxy/Nyxara/Terrain/A2/NyxaraA2SouthCliff.mat";

    [SerializeField] Mesh bakedMesh;
    [SerializeField] Material cliffMaterial;
    [SerializeField] [TextArea(2, 6)] string bakeReport;

    MeshFilter _filter;
    MeshRenderer _renderer;
    Mesh _runtimeMesh;

    public Mesh BakedMesh => bakedMesh;
    public string BakeReport => bakeReport;

    public void SetBaked(Mesh mesh, Material material, string report)
    {
        bakedMesh = mesh;
        cliffMaterial = material;
        bakeReport = report;
        Assign(mesh, material);
    }

    void OnEnable()
    {
        SnapIdentityUnderPlanet();
        EnsureVisual();
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

    void OnDisable() => ReleaseRuntimeMesh();
    void OnDestroy() => ReleaseRuntimeMesh();

    void EnsureVisual()
    {
        if (_filter == null)
            _filter = GetComponent<MeshFilter>();
        if (_renderer == null)
            _renderer = GetComponent<MeshRenderer>();

        if (bakedMesh != null)
        {
            Assign(bakedMesh, cliffMaterial);
            return;
        }

#if UNITY_EDITOR
        Mesh asset = UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>(MeshAssetPath);
        Material mat = cliffMaterial != null
            ? cliffMaterial
            : UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(MaterialAssetPath);
        if (asset != null)
        {
            bakedMesh = asset;
            cliffMaterial = mat;
            Assign(asset, mat);
            return;
        }

        var session = GetComponentInParent<NyxaraTerrainStudySession>();
        var planet = GetComponentInParent<SphericalPlanet>();
        if (planet == null)
            return;
        var tileMap = planet.GetComponent<PlanetTileMap>();
        var plan = session != null ? session.Plan : (tileMap != null ? tileMap.WorkPlan : null);
        float walk = tileMap != null
            ? tileMap.GetWalkSurfaceRadius(PlanetTileMap.StudyLonLatToDirection(35f, -12f))
            : planet.Radius;
        var settings = NyxaraA2SouthCliffMeshBuilder.FromPlan(plan, walk);
        ReleaseRuntimeMesh();
        _runtimeMesh = NyxaraA2SouthCliffMeshBuilder.Build(settings);
        bakeReport = NyxaraA2SouthCliffMeshBuilder.Describe(settings, _runtimeMesh) +
                     " (in-memory preview — use Bake A2 South Cliff to save the asset).";
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
