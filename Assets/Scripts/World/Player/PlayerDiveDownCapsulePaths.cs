/// <summary>
/// Canonical Resources and asset paths for the crash-landing dive-down capsule VFX kit.
/// Keep every loader and editor tool pointed here so moves under Assets/Resources/Player/VFX/
/// only need updating in one place.
/// </summary>
public static class PlayerDiveDownCapsulePaths
{
    public const string ResourcesImpactVfx = "Player/VFX/DiveDownCapsule/ImpactVfx";
    public const string ResourcesReentryFlameMaterial = "Player/VFX/DiveDownCapsule/Materials/ReentryFlame";

#if UNITY_EDITOR
    public const string AssetImpactVfxPrefab = "Assets/Resources/Player/VFX/DiveDownCapsule/ImpactVfx.prefab";
    public const string AssetCapsulePrefab = "Assets/Resources/Player/VFX/DiveDownCapsule/PlayerDiveDownCapsule.prefab";
#endif
}
