using UnityEngine;

/// <summary>
/// Attach to a Portal instance (see Resources/Portal/Portal.prefab) in a planet scene. When the
/// scene loads, <see cref="SceneBootstrap"/> looks for this component - only on planet scenes,
/// see <see cref="SceneRoles.IsPlanetScene"/> - and spawns the player at a random point within
/// <see cref="SpawnRadius"/> of the portal instead of exactly on top of it. Since it lives on the
/// shared Portal prefab, every future planet scene gets this for free just by placing the portal -
/// no per-scene wiring required beyond tuning the radius if a designer wants to.
/// </summary>
[DisallowMultipleComponent]
public class PortalPlayerSpawn : MonoBehaviour
{
    [Tooltip("Player spawns at a random point within this distance of the portal, in world units.")]
    [SerializeField, Min(0f)] float spawnRadius = 4f;

    public float SpawnRadius => spawnRadius;

    /// <summary>
    /// Picks a random point within <see cref="SpawnRadius"/> of this portal - never closer than
    /// this portal's own re-teleport trigger radius (see <see cref="GetSafeMinRadius"/>) - and
    /// snaps it onto the planet's surface with an upright rotation and a random facing. Returns
    /// false if there's no <see cref="SphericalPlanet"/> in the scene to project onto.
    /// </summary>
    public bool TryGetRandomSpawnPose(out Vector3 position, out Quaternion rotation)
    {
        position = transform.position;
        rotation = transform.rotation;

        if (!PlanetSurfacePose.TryResolvePlanet(transform, out SphericalPlanet planet, out PlanetTileMap tiles))
            return false;

        float minRadius = GetSafeMinRadius();
        if (!PlanetRadialSampling.TryGetRandomPointNear(planet, transform.position, minRadius, spawnRadius, out Vector3 direction))
            return false;

        float yaw = Random.Range(0f, 360f);
        return PlanetSurfacePose.TryGetPose(planet, tiles, direction, yaw, PlanetSurfacePose.DefaultHover, out position, out rotation, out _);
    }

    float GetSafeMinRadius() => GalaxyGate.GetSafeMinSpawnRadius(GetComponent<GalaxyGate>(), spawnRadius);
}
