/// <summary>
/// Canonical Resources and asset paths for the crash-landing dive-down capsule VFX kit.
/// Keep every loader and editor tool pointed here so moves under Assets/Resources/Player/VFX/
/// only need updating in one place.
/// </summary>
public static class PlayerDiveDownCapsulePaths
{
    public const string ResourcesImpactVfx = "Player/VFX/DiveDownCapsule/ImpactVfx";
    public const string ResourcesReentryFlameMaterial = "Player/VFX/DiveDownCapsule/Materials/ReentryFlame";
    public const string ResourcesReentryTrailMaterial = "Player/VFX/DiveDownCapsule/Materials/ReentryTrail";
    public const string ResourcesDiveClip = "Player/Animator/DiveDownClip";
    public const string ResourcesLandClip = "Player/Animator/DiveDownAndLandClip";
    public const string ResourcesDiveDownModel = "Player/Models/Starbot_Animation_Dive_Down";
    public const string ResourcesDiveModel = "Player/Models/Starbot_Animation_Dive_Down_and_Land";
    public const string DiveModelChildName = "Starbot_Animation_Dive_Down_and_Land";
    public const string DiveClipAssetName = "DiveDownClip";
    public const string LandClipAssetName = "DiveDownAndLandClip";

#if UNITY_EDITOR
    public const string AssetImpactVfxPrefab = "Assets/Resources/Player/VFX/DiveDownCapsule/ImpactVfx.prefab";
    public const string AssetCapsulePrefab = "Assets/Resources/Player/VFX/DiveDownCapsule/PlayerDiveDownCapsule.prefab";
    public const string AssetDiveDownModel = "Assets/Resources/Player/Models/Starbot_Animation_Dive_Down.fbx";
    public const string AssetLandModel = "Assets/Resources/Player/Models/Starbot_Animation_Dive_Down_and_Land.fbx";
    public const string AssetDiveClip = "Assets/Resources/Player/Animator/DiveDownClip.anim";
    public const string AssetLandClip = "Assets/Resources/Player/Animator/DiveDownAndLandClip.anim";
#endif
}
