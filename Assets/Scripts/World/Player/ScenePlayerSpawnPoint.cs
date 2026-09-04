using UnityEngine;

/// <summary>
/// Fixed spawn point for scenes without a crash landing or planet portal (e.g. SpaceShip).
/// Place one in the scene at the position/rotation where the player should appear.
/// </summary>
[DisallowMultipleComponent]
public class ScenePlayerSpawnPoint : MonoBehaviour
{
    public static bool TryGetPose(out Vector3 position, out Quaternion rotation)
    {
        ScenePlayerSpawnPoint point = FindAnyObjectByType<ScenePlayerSpawnPoint>();
        if (point == null)
        {
            position = default;
            rotation = default;
            return false;
        }

        position = point.transform.position;
        rotation = point.transform.rotation;
        return true;
    }
}
