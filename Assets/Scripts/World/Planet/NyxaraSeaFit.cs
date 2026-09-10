using UnityEngine;

/// <summary>
/// Sizes the south-basin water mesh. Transform is authored in the scene and is
/// never moved on Play. Visual only — no collider.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[DefaultExecutionOrder(-25)]
public sealed class NyxaraSeaFit : MonoBehaviour
{
    public const string RootName = "Sea";
    const float FallbackDrop = 13f;

    [SerializeField]
    [Tooltip("Keep the walk ring dry. Waves also need this much clearance below the walk radius.")]
    float shoreBelowWalk = 3.2f;

    [SerializeField]
    [Tooltip("Keep water above the lowered tiles so the shader has depth instead of z-fighting the floor.")]
    float waterAboveFloor = 1.6f;

    [SerializeField]
    [Tooltip("Hairline north of the cliff lip, only to close a 1-pixel gap at the rock. Keep near 0 so water does not run into the land.")]
    float undercutNorthOfLipDegrees;

    MeshRenderer _renderer;
    MeshFilter _filter;
    Mesh _basinMesh;

    public bool ProvidesBasinMesh { get; private set; }

    void OnEnable()
    {
        Fit();
    }

    void OnDisable()
    {
        ReleaseBasinMesh();
        ProvidesBasinMesh = false;
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        shoreBelowWalk = Mathf.Max(2.4f, shoreBelowWalk);
        waterAboveFloor = Mathf.Max(0.2f, waterAboveFloor);
        undercutNorthOfLipDegrees = 0f;
        if (!isActiveAndEnabled)
            return;

        UnityEditor.EditorApplication.delayCall += () =>
        {
            if (this != null && isActiveAndEnabled)
                Fit();
        };
    }
#endif

    public void Fit()
    {
        var planet = GetComponentInParent<SphericalPlanet>();
        if (planet == null)
            return;

        DisablePhysics();

        PlanetTileMap tiles = planet.GetComponent<PlanetTileMap>();
        bool basin = tiles != null && NyxaraA2CliffProfile.PlanApplies(tiles.WorkPlan);
        if (_renderer == null)
            _renderer = GetComponent<MeshRenderer>();
        if (_filter == null)
            _filter = GetComponent<MeshFilter>();
        if (_renderer != null)
            _renderer.enabled = basin;
        ProvidesBasinMesh = basin;
        if (!basin)
        {
            ReleaseBasinMesh();
            return;
        }

        float walk = tiles.GetWalkSurfaceRadius(-planet.transform.up);
        float drop = Mathf.Max(0f, -NyxaraA2CliffProfile.RadialOffset(tiles.WorkPlan, 0f, -90f));
        if (drop < 0.5f)
            drop = Mathf.Max(tiles.WorkPlan.cliffDepth, tiles.WorkPlan.cliffDepthMax, FallbackDrop);

        float floorRadius = walk - drop;
        float shore = Mathf.Max(3.2f, shoreBelowWalk);
        float seaRadius = walk - shore;
        seaRadius = Mathf.Max(seaRadius, floorRadius + waterAboveFloor);
        seaRadius = Mathf.Min(seaRadius, walk - 2.8f);
        if (seaRadius < 1f)
            return;

        ReleaseBasinMesh();
        _basinMesh = NyxaraSeaMeshBuilder.Build(new NyxaraSeaMeshBuilder.Settings
        {
            plan = tiles.WorkPlan,
            walkRadius = walk,
            seaRadius = seaRadius,
            longitudeSegments = tiles.WorkPlan.coverFullRing ? 360 : 96,
            latitudeSegments = 36,
            worldScale = transform.lossyScale.x
        });
        _basinMesh.hideFlags = HideFlags.DontSave;
        if (_filter != null)
            _filter.sharedMesh = _basinMesh;
    }

    void ReleaseBasinMesh()
    {
        if (_basinMesh == null)
            return;
        if (_filter != null && _filter.sharedMesh == _basinMesh)
            _filter.sharedMesh = null;
        if (Application.isPlaying)
            Destroy(_basinMesh);
        else
            DestroyImmediate(_basinMesh);
        _basinMesh = null;
    }

    void DisablePhysics()
    {
        var sphere = GetComponent<SphereCollider>();
        if (sphere != null)
            sphere.enabled = false;
        var meshCol = GetComponent<MeshCollider>();
        if (meshCol != null)
            meshCol.enabled = false;
    }
}
