using UnityEngine;

/// <summary>
/// Marks the player capsule's world position so other systems (e.g. <see cref="CapsuleDirectionMarker"/>)
/// can find "home" without a fragile scene search or a hard reference wired per-scene.
/// </summary>
public class PlayerCapsuleBeacon : MonoBehaviour
{
    public static PlayerCapsuleBeacon Instance { get; private set; }

    void OnEnable()
    {
        Instance = this;
    }

    void OnDisable()
    {
        if (Instance == this)
            Instance = null;
    }
}
