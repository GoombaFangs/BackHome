using UnityEngine;

/// <summary>
/// Spawns Player + UI from prefabs when a playable scene starts.
/// CameraFollow stays on Main Camera — only the target is wired at runtime.
/// Per-scene defaults (spawn point, vitals bar size) live here because cameras differ per world.
/// </summary>
public class SceneBootstrap : MonoBehaviour
{
    [SerializeField] GameObject playerPrefab;
    [SerializeField] GameObject uiPrefab;
    [SerializeField] Vector3 spawnPosition = new Vector3(0f, 2f, 0f);

    [Header("Vitals HUD")]
    [Tooltip("World size of HP/Oxygen bars for this scene. Raise on distant/high cameras (planets).")]
    [SerializeField, Min(0.1f)] float vitalsBarsScale = 1f;
    [Tooltip("Local offset of the bars relative to the player (XYZ).")]
    [SerializeField] Vector3 vitalsBarsOffset = new Vector3(0f, 2.15f, 0f);

    void Awake()
    {
        Transform player = FindExistingPlayer();
        if (player == null && playerPrefab != null)
        {
            GameObject playerObject = Instantiate(playerPrefab, spawnPosition, Quaternion.identity);
            playerObject.name = playerPrefab.name;
            player = playerObject.transform;
        }

        if (uiPrefab != null && GameObject.Find("UI") == null)
        {
            GameObject uiObject = Instantiate(uiPrefab);
            uiObject.name = uiPrefab.name;
        }

        if (player == null)
            return;

        BindCameraTarget(player);
        ApplyVitalsBarsSettings(player);
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
        WorldVitalsBars vitals = player.GetComponent<WorldVitalsBars>();
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
