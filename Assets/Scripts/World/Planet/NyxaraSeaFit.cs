using UnityEngine;

/// <summary>
/// Concentric ocean sphere, independent of <see cref="PlanetTileMap"/>.
/// Visual only — no collider. Mesh comes from <see cref="CasualWaterSphereMesh"/>.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
[DefaultExecutionOrder(-25)]
public sealed class NyxaraSeaFit : MonoBehaviour
{
    public const string RootName = "Sea";

    [SerializeField, Tooltip("World units above the planet surface so ground at height 0 sits just under the water.")]
    float clearance = 0.45f;

    MeshRenderer _renderer;

    void OnEnable()
    {
        var planet = GetComponentInParent<SphericalPlanet>();
        PlanetLayout.StripLegacyArt(planet);
        Fit();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
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

        if (_renderer == null)
            _renderer = GetComponent<MeshRenderer>();
        if (_renderer != null)
            _renderer.enabled = true;

        float worldRadius = planet.GetTerrainRadius(Vector3.up) + Mathf.Max(0f, clearance);
        float parentScale = Mathf.Max(0.0001f, planet.transform.lossyScale.x);
        float localRadius = worldRadius / parentScale;
        float localScale = localRadius / CasualWaterSphereMesh.UnitySphereRadius;

        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one * localScale;

        var sphere = GetComponent<CasualWaterSphereMesh>();
        if (sphere != null)
            sphere.Refresh();
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
