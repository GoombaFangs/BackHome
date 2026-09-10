using UnityEngine;

/// <summary>
/// Scene visual for the north ridge. Walk collision is kinematic
/// (<see cref="NyxaraRouteBounds"/>). This mesh has no collider.
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

    public void SetBaked(Mesh mesh, Material fallbackMaterial, string report)
    {
        bakedMesh = mesh;
        bakeReport = report;
        Assign(mesh, fallbackMaterial);
    }

    public void RebuildFromPlan(
        PlanetTileMap.TerrainWorkPlan plan,
        NyxaraA2BoundaryOverlay overlay,
        float walkRadius)
    {
        ReleaseRuntimeMesh();
        var settings = NyxaraA2NorthRidgeMeshBuilder.FromPlan(plan, overlay, walkRadius);
        _runtimeMesh = NyxaraA2NorthRidgeMeshBuilder.Build(settings);
        bakeReport = NyxaraA2NorthRidgeMeshBuilder.Describe(settings, _runtimeMesh);
        Assign(_runtimeMesh, ridgeMaterial);
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

        var session = GetComponentInParent<NyxaraTerrainStudySession>();
        var planet = GetComponentInParent<SphericalPlanet>();
        PlanetTileMap tileMap = planet != null ? planet.GetComponent<PlanetTileMap>() : null;
        if (session != null && NyxaraA2CliffProfile.PlanApplies(session.Plan))
        {
            float walk = tileMap != null
                ? tileMap.GetWalkSurfaceRadius(PlanetTileMap.StudyLonLatToDirection(35f, 25f))
                : (planet != null ? planet.Radius : 75f);
            RebuildFromPlan(session.Plan, session.BoundaryOverlay, walk);
            return;
        }

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

        if (planet == null)
            return;
        var plan = tileMap != null ? tileMap.WorkPlan : null;
        float previewWalk = tileMap != null
            ? tileMap.GetWalkSurfaceRadius(PlanetTileMap.StudyLonLatToDirection(35f, 25f))
            : planet.Radius;
        RebuildFromPlan(plan, session != null ? session.BoundaryOverlay : null, previewWalk);
#endif
    }

    void Assign(Mesh mesh, Material fallbackMaterial)
    {
        if (_filter == null)
            _filter = GetComponent<MeshFilter>();
        if (_renderer == null)
            _renderer = GetComponent<MeshRenderer>();
        if (_filter != null)
            _filter.sharedMesh = mesh;
        KeepOrApplyMaterials(fallbackMaterial);
        NyxaraTerrainCollision.ClearBlockingMesh(gameObject);
    }

    void KeepOrApplyMaterials(Material fallbackMaterial)
    {
        if (_renderer == null)
            return;

        if (HasAnyMaterial(_renderer))
        {
            Material[] current = _renderer.sharedMaterials;
            for (int i = 0; i < current.Length; i++)
            {
                if (current[i] != null)
                {
                    ridgeMaterial = current[i];
                    break;
                }
            }

            return;
        }

        Material mat = ridgeMaterial != null ? ridgeMaterial : fallbackMaterial;
#if UNITY_EDITOR
        if (mat == null)
            mat = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(MaterialAssetPath);
#endif
        if (mat == null)
            return;
        ridgeMaterial = mat;
        _renderer.sharedMaterial = mat;
    }

    static bool HasAnyMaterial(MeshRenderer renderer)
    {
        if (renderer == null)
            return false;
        Material[] mats = renderer.sharedMaterials;
        if (mats == null)
            return false;
        for (int i = 0; i < mats.Length; i++)
        {
            if (mats[i] != null)
                return true;
        }

        return false;
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
