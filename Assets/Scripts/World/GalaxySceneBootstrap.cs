using UnityEngine;

/// <summary>
/// Spawns Player + UI from prefabs when a Galaxy/Planet scene starts.
/// CameraFollow stays on Main Camera — only the target is wired at runtime.
/// Planet walking is handled by PlanetWalker on the Player prefab.
/// </summary>
public class GalaxySceneBootstrap : MonoBehaviour
{
    [SerializeField] GameObject playerPrefab;
    [SerializeField] GameObject uiPrefab;
    [SerializeField] Vector3 spawnPosition = new Vector3(0f, 2f, 0f);

    void Awake()
    {
        if (FindFirstObjectByType<TochController>() != null)
            return;

        Transform player = null;
        if (playerPrefab != null)
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

        if (player != null)
            BindCameraTarget(player);
    }

    static void BindCameraTarget(Transform player)
    {
        CameraFollow follow = Object.FindFirstObjectByType<CameraFollow>();
        if (follow == null && Camera.main != null)
            follow = Camera.main.GetComponent<CameraFollow>();

        if (follow != null)
            follow.SetTarget(player);
        else
            Debug.LogWarning("GalaxySceneBootstrap: add CameraFollow on Main Camera to tune offsets per scene.");
    }
}
