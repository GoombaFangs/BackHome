using UnityEngine;

/// <summary>
/// Sizes the Sea sphere to flood the south basin the cliff lowers. A concentric
/// sphere only shows where terrain is below the water (the pit) and stays hidden
/// under the walk band. No collider — water is visual only.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[DefaultExecutionOrder(-25)]
public sealed class NyxaraSeaFit : MonoBehaviour
{
    public const string RootName = "Sea";
    const float UnitySphereRadius = 0.5f;
    const float FallbackDrop = 13f;

    [SerializeField]
    [Tooltip("Keep the walk ring dry. Waves also need this much clearance below the walk radius.")]
    float shoreBelowWalk = 2.1f;

    [SerializeField]
    [Tooltip("Keep water above the lowered tiles so the shader has depth instead of z-fighting the floor.")]
    float waterAboveFloor = 1.6f;

    MeshRenderer _renderer;

    void OnEnable()
    {
        Fit();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        shoreBelowWalk = Mathf.Max(0.4f, shoreBelowWalk);
        waterAboveFloor = Mathf.Max(0.2f, waterAboveFloor);
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

        if (transform.parent != planet.transform)
            transform.SetParent(planet.transform, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;

        DisablePhysics();

        PlanetTileMap tiles = planet.GetComponent<PlanetTileMap>();
        bool basin = tiles != null && NyxaraA2CliffProfile.PlanApplies(tiles.WorkPlan);
        if (_renderer == null)
            _renderer = GetComponent<MeshRenderer>();
        if (_renderer != null)
            _renderer.enabled = basin;
        if (!basin)
            return;

        float walk = tiles.GetWalkSurfaceRadius(-planet.transform.up);
        float drop = Mathf.Max(0f, -NyxaraA2CliffProfile.RadialOffset(tiles.WorkPlan, 0f, -90f));
        if (drop < 0.5f)
            drop = Mathf.Max(tiles.WorkPlan.cliffDepth, tiles.WorkPlan.cliffDepthMax, FallbackDrop);

        float floorRadius = walk - drop;
        float seaRadius = walk - shoreBelowWalk;
        seaRadius = Mathf.Max(seaRadius, floorRadius + waterAboveFloor);
        seaRadius = Mathf.Min(seaRadius, walk - 0.35f);
        if (seaRadius < 1f)
            return;

        float parentScale = transform.parent != null
            ? Mathf.Max(0.0001f, transform.parent.lossyScale.x)
            : 1f;
        float local = (seaRadius / UnitySphereRadius) / parentScale;
        Vector3 scale = Vector3.one * local;
        if ((transform.localScale - scale).sqrMagnitude > 0.0001f)
            transform.localScale = scale;

        var mesh = GetComponent<CasualWaterSphereMesh>();
        if (mesh != null)
            mesh.Refresh();
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
