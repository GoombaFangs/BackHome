using StarterAssets;
using UnityEngine;

/// <summary>
/// Spawns Player when a playable scene starts. HUD lives in the scene (Hud prefab instance)
/// so the full Canvas is visible in edit mode; this only instantiates it if the scene has none.
/// CameraFollow stays on Main Camera — only the target is wired at runtime.
/// Per-scene vitals bar sizing lives here because cameras differ per world.
/// Where the player spawns is resolved by <see cref="TryResolveSpawnPose"/> from, in order:
/// crash-landing portal, planet portal, or a <see cref="ScenePlayerSpawnPoint"/> marker.
/// </summary>
public class SceneBootstrap : MonoBehaviour
{
    [SerializeField] GameObject playerPrefab;
    [Tooltip("Fallback only. Prefer a Hud instance already placed in the scene.")]
    [SerializeField] GameObject uiPrefab;

    [Header("Vitals Bars")]
    [Tooltip("World size of HP/Oxygen bars for this scene. Raise on distant/high cameras (planets).")]
    [SerializeField, Min(0.1f)] float vitalsBarsScale = 1f;
    [Tooltip("Local offset of the bars relative to the player (XYZ).")]
    [SerializeField] Vector3 vitalsBarsOffset = new Vector3(0f, 2.15f, 0f);

    PlayerCrashIntro _crashIntro;

    void Awake()
    {
        if (uiPrefab != null && FindExistingHud() == null)
        {
            GameObject uiObject = Instantiate(uiPrefab);
            uiObject.name = "Hud";
        }

        PlayerInventory.EnsureExists();

        // If a crash-landing cinematic is present in the scene, wait for it to finish
        // (camera follows the capsule) before spawning the player and taking the camera back.
        _crashIntro = FindAnyObjectByType<PlayerCrashIntro>();
        if (_crashIntro != null)
            _crashIntro.OnLanded += SpawnPlayerAndBindCamera;
        else
            SpawnPlayerAndBindCamera();
    }

    void SpawnPlayerAndBindCamera()
    {
        Transform player = FindExistingPlayer();
        if (player == null && playerPrefab != null)
        {
            if (!TryResolveSpawnPose(out Vector3 position, out Quaternion rotation))
            {
                Debug.LogWarning("SceneBootstrap: no spawn pose resolved. Add PlayerCrashIntro, " +
                    "PortalPlayerSpawn, or ScenePlayerSpawnPoint to this scene.", this);
                return;
            }

            GameObject playerObject = Instantiate(playerPrefab, position, rotation);
            playerObject.name = playerPrefab.name;
            player = playerObject.transform;
        }

        if (player == null)
            return;

        BindCameraTarget(player);
        ApplyVitalsBarsSettings(player);
        BindMobileInput(player);
    }

    /// <summary>
    /// 1. Crash-landing ground portal (<see cref="PortalPlayerSpawn"/>), or the crash site itself.
    /// 2. Planet scene portal (<see cref="PortalPlayerSpawn"/>).
    /// 3. <see cref="ScenePlayerSpawnPoint"/> for fixed spawns (e.g. SpaceShip).
    /// </summary>
    bool TryResolveSpawnPose(out Vector3 position, out Quaternion rotation)
    {
        if (_crashIntro != null)
        {
            Transform groundPortal = _crashIntro.GroundPortal;
            if (groundPortal != null)
            {
                PortalPlayerSpawn portalSpawn = groundPortal.GetComponent<PortalPlayerSpawn>();
                if (portalSpawn != null && portalSpawn.TryGetRandomSpawnPose(out position, out rotation))
                    return true;
            }

            if (_crashIntro.TryComputeLandingSite(out position, out rotation))
                return true;
        }

        if (SceneRoles.IsPlanetScene())
        {
            PortalPlayerSpawn portalSpawn = FindAnyObjectByType<PortalPlayerSpawn>();
            if (portalSpawn != null && portalSpawn.TryGetRandomSpawnPose(out position, out rotation))
                return true;
        }

        if (ScenePlayerSpawnPoint.TryGetPose(out position, out rotation))
            return true;

        position = default;
        rotation = default;
        return false;
    }

    static void BindMobileInput(Transform player)
    {
        MobileInputBinder binder = FindAnyObjectByType<MobileInputBinder>();
        if (binder == null)
            return;

        binder.BindPlayer(player.GetComponent<StarterAssetsInputs>());
    }

    static GameObject FindExistingHud()
    {
        GameObject named = GameObject.Find("Hud");
        if (named != null)
            return named;

        PlayerDeathUI death = Object.FindAnyObjectByType<PlayerDeathUI>(FindObjectsInactive.Include);
        return death != null ? death.gameObject : null;
    }

    static Transform FindExistingPlayer()
    {
        TouchController motor = FindAnyObjectByType<TouchController>();
        if (motor != null)
            return motor.transform;

        GameObject tagged = GameObject.FindGameObjectWithTag("Player");
        return tagged != null ? tagged.transform : null;
    }

    void ApplyVitalsBarsSettings(Transform player)
    {
        VitalsBars vitals = player.GetComponent<VitalsBars>();
        if (vitals == null)
            return;

        vitals.SetWorldScale(vitalsBarsScale);
        vitals.SetLocalOffset(vitalsBarsOffset);
    }

    static void BindCameraTarget(Transform player)
    {
        CameraFollow follow = Object.FindAnyObjectByType<CameraFollow>();
        if (follow == null && Camera.main != null)
            follow = Camera.main.GetComponent<CameraFollow>();

        if (follow != null)
            follow.SetTarget(player);
        else
            Debug.LogWarning("SceneBootstrap: add CameraFollow on Main Camera to tune offsets per scene.");
    }
}
