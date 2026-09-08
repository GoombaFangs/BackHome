using UnityEngine;

/// <summary>
/// Optional lip mesh kept for bake/debug. Collision is off — the walk band is kinematic
/// (<see cref="NyxaraRouteBounds"/>). Visual ridge/cliff stay render-only.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshCollider))]
public class NyxaraA2LipWalls : MonoBehaviour
{
    public const string RootName = "NyxaraA2LipWalls";
    public const string MeshAssetPath = "Assets/Resources/Galaxy/Nyxara/Terrain/A2/NyxaraA2LipWalls.asset";

    [SerializeField] Mesh bakedMesh;
    MeshFilter _filter;
    MeshCollider _collider;
    MeshRenderer _renderer;
    Mesh _runtimeMesh;

    public void RebuildFromPlan(PlanetTileMap.TerrainWorkPlan plan, float walkRadius)
    {
        ReleaseRuntimeMesh();
        _runtimeMesh = NyxaraA2LipWallMeshBuilder.Build(plan, walkRadius);
        Assign(_runtimeMesh);
    }

    public void SetBaked(Mesh mesh)
    {
        bakedMesh = mesh;
        Assign(mesh);
    }

    void OnEnable()
    {
        SnapIdentity();
        EnsureMesh();
    }

    void OnDisable() => ReleaseRuntimeMesh();
    void OnDestroy() => ReleaseRuntimeMesh();

    void SnapIdentity()
    {
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;
    }

    void EnsureMesh()
    {
        if (bakedMesh != null)
        {
            Assign(bakedMesh);
            return;
        }

        var session = GetComponentInParent<NyxaraTerrainStudySession>();
        var planet = GetComponentInParent<SphericalPlanet>();
        PlanetTileMap tiles = planet != null ? planet.GetComponent<PlanetTileMap>() : null;
        var plan = session != null ? session.Plan : (tiles != null ? tiles.WorkPlan : null);
        float walk = tiles != null
            ? tiles.GetWalkSurfaceRadius(Vector3.up)
            : (planet != null ? planet.Radius : 75f);
        ReleaseRuntimeMesh();
        _runtimeMesh = NyxaraA2LipWallMeshBuilder.Build(plan, walk);
        Assign(_runtimeMesh);
    }

    void Assign(Mesh mesh)
    {
        if (_filter == null)
            _filter = GetComponent<MeshFilter>();
        if (_collider == null)
            _collider = GetComponent<MeshCollider>();
        if (_renderer == null)
            _renderer = GetComponent<MeshRenderer>();
        if (_filter != null)
            _filter.sharedMesh = mesh;
        if (_renderer != null)
            _renderer.enabled = false;
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
