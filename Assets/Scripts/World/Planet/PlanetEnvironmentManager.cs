using UnityEngine;

/// <summary>
/// Single scene entry point for planet-wide environment streaming: owns the shared
/// <see cref="PlanetEnvironmentRegionSet"/> and keeps the Grass / Tree / Rock streamers on this
/// GameObject in sync. Per-region prefab lists and tree/grass/rock density live on the region set
/// asset — tune amounts there, global stream radii and base density on each streamer.
///
/// Put this on the root-level "EnvironmentManager" object (alongside
/// <see cref="PlanetGrassStreamer"/>, <see cref="PlanetTreeStreamer"/>, <see cref="PlanetRockStreamer"/>).
/// </summary>
[DefaultExecutionOrder(-10)]
[DisallowMultipleComponent]
public class PlanetEnvironmentManager : MonoBehaviour
{
    [Header("Planet")]
    [Tooltip("Planet to stream onto. Leave empty to auto-resolve at runtime.")]
    [SerializeField] SphericalPlanet planet;

    [Header("Regions")]
    [Tooltip("Region layout + per-region trees/grass/rocks. All three streamers read this.")]
    [SerializeField] PlanetEnvironmentRegionSet regionSet;

    [Header("Streamers")]
    [Tooltip("Filled automatically from components on this GameObject.")]
    [SerializeField] PlanetGrassStreamer grassStreamer;
    [SerializeField] PlanetTreeStreamer treeStreamer;
    [SerializeField] PlanetRockStreamer rockStreamer;

    void Awake()
    {
        ApplyConfiguration();
    }

    void OnEnable() => PlanetEnvironmentExclusion.Changed += ForceRefreshAll;

    void OnDisable() => PlanetEnvironmentExclusion.Changed -= ForceRefreshAll;

    void OnValidate()
    {
        ResolveStreamers();
    }

    /// <summary>Pushes <see cref="planet"/> and <see cref="regionSet"/> to all streamers on this object.</summary>
    public void ApplyConfiguration()
    {
        ResolveStreamers();

        SphericalPlanet resolvedPlanet = ResolvePlanet();
        if (grassStreamer != null)
            grassStreamer.ConfigureFromManager(resolvedPlanet, regionSet);
        if (treeStreamer != null)
            treeStreamer.ConfigureFromManager(resolvedPlanet, regionSet);
        if (rockStreamer != null)
            rockStreamer.ConfigureFromManager(resolvedPlanet, regionSet);
    }

    /// <summary>Queues every streamer to rescan on its next Update when landing exclusion zones appear or move.</summary>
    public void ForceRefreshAll()
    {
        grassStreamer?.ForceRefresh();
        treeStreamer?.ForceRefresh();
        rockStreamer?.ForceRefresh();
    }

    void ResolveStreamers()
    {
        if (grassStreamer == null)
            grassStreamer = GetComponent<PlanetGrassStreamer>();
        if (treeStreamer == null)
            treeStreamer = GetComponent<PlanetTreeStreamer>();
        if (rockStreamer == null)
            rockStreamer = GetComponent<PlanetRockStreamer>();
    }

    SphericalPlanet ResolvePlanet()
    {
        if (planet != null)
            return planet;

        planet = SphericalPlanet.Instance != null
            ? SphericalPlanet.Instance
            : FindAnyObjectByType<SphericalPlanet>();
        return planet;
    }
}
