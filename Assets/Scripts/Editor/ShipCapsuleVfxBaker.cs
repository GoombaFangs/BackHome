using UnityEditor;
using UnityEngine;

/// <summary>
/// One-off (but safe to re-run) tool that bakes the ShipCapsule crash VFX - the fire trail
/// (FireTrail: TrailRenderer + FlameBody/Smoke/Sparks/FireEmbers) and the impact burst
/// (ImpactEffects: ImpactDust/ImpactSparks/ImpactDebris) - directly into the CapsuleParticalSystem
/// prefab as real, editable children, instead of leaving them as objects that only exist
/// transiently at runtime.
///
/// How it works: it simply adds <see cref="ShipFireTrail"/>/<see cref="ShipCrashImpact"/> to a
/// fresh child inside the prefab and lets their own Awake() build everything exactly like it
/// would at runtime - the only difference is this happens once, at edit time, and gets saved into
/// the prefab asset. Re-running this is harmless: both components already skip re-creating any
/// child that's found by name, so it only ever fills in whatever's missing.
/// </summary>
static class ShipCapsuleVfxBaker
{
    const string PrefabPath = "Assets/Ship/Prefabs/CapsuleParticalSystem.prefab";

    [MenuItem("Tools/Ship VFX/Bake Fire Trail + Impact Into CapsuleParticalSystem")]
    public static void Bake()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            EnsureChildWithComponent<ShipFireTrail>(root.transform, "FireTrail");
            EnsureChildWithComponent<ShipCrashImpact>(root.transform, "ImpactEffects");

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"ShipCapsuleVfxBaker: baked FireTrail + ImpactEffects into {PrefabPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    static void EnsureChildWithComponent<T>(Transform parent, string childName) where T : Component
    {
        Transform existing = parent.Find(childName);
        if (existing != null && existing.GetComponent<T>() != null)
            return; // already baked - leave whatever's authored there untouched

        GameObject go = existing != null ? existing.gameObject : new GameObject(childName);
        if (existing == null)
            go.transform.SetParent(parent, false);

        // Adding the component triggers Awake() immediately (LoadPrefabContents loads the prefab
        // into a real, active temporary scene), which builds all of its children exactly like it
        // does at runtime.
        go.AddComponent<T>();
    }
}
