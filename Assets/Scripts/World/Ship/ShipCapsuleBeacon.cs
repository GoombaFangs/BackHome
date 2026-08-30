using UnityEngine;

/// <summary>
/// Marks the ship capsule's world position so other systems (e.g. <see cref="CapsuleDirectionMarker"/>)
/// can find "home" without a fragile scene search or a hard reference wired per-scene.
/// </summary>
public class ShipCapsuleBeacon : MonoBehaviour
{
    public static ShipCapsuleBeacon Instance { get; private set; }

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
