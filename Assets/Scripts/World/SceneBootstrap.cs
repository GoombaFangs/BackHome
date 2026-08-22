using StarterAssets;
using UnityEngine;

/// <summary>
/// Spawns Player when a playable scene starts. HUD lives in the scene (Hud prefab instance)
/// so the full Canvas is visible in edit mode; this only instantiates it if the scene has none.
/// CameraFollow stays on Main Camera — only the target is wired at runtime.
/// Per-scene defaults (spawn point, vitals bar size) live here because cameras differ per world.
/// </summary>
public class SceneBootstrap : MonoBehaviour
{
    [SerializeField] GameObject playerPrefab;
    [Tooltip("Fallback only. Prefer a Hud instance already placed in the scene.")]
    [SerializeField] GameObject uiPrefab;
    [SerializeField] Vector3 spawnPosition = new Vector3(0f, 2f, 0f);

    [Header("Vitals Bars")]
    [Tooltip("World size of HP/Oxygen bars for this scene. Raise on distant/high cameras (planets).")]
    [SerializeField, Min(0.1f)] float vitalsBarsScale = 1f;
    [Tooltip("Local offset of the bars relative to the player (XYZ).")]
    [SerializeField] Vector3 vitalsBarsOffset = new Vector3(0f, 2.15f, 0f);

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
        ShipCrashIntro crashIntro = FindAnyObjectByType<ShipCrashIntro>();
        if (crashIntro != null)
            crashIntro.OnLanded += SpawnPlayerAndBindCamera;
        else
            SpawnPlayerAndBindCamera();
    }

    void SpawnPlayerAndBindCamera()
    {
        Transform player = FindExistingPlayer();
        if (player == null && playerPrefab != null)
        {
            GameObject playerObject = Instantiate(playerPrefab, spawnPosition, Quaternion.identity);
            playerObject.name = playerPrefab.name;
            player = playerObject.transform;
        }

        if (player == null)
            return;

        BindCameraTarget(player);
        ApplyVitalsBarsSettings(player);
        BindMobileInput(player);
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
