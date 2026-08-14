using UnityEditor;
using UnityEngine;

/// <summary>
/// One-off (but safe to re-run) tools that bake the ShipCapsule crash VFX into real, editable
/// prefab assets instead of leaving them as objects that only exist transiently at runtime:
/// - <see cref="Bake"/> fills the fire trail ("FireTrail": TrailRenderer + FlameBody/Smoke/Sparks/
///   FireEmbers) into the existing CapsuleParticalSystem prefab.
/// - <see cref="CreateCapsuleImpactPrefab"/> creates a brand new "CapsuleImpact" prefab (Impact
///   Flash/Dust/Sparks/Debris) from scratch.
///
/// How it works: it simply adds <see cref="ShipFireTrail"/>/<see cref="ShipCrashImpact"/> to a
/// throwaway object and lets it build everything exactly like it would at runtime - the only
/// difference is this happens once, at edit time, and gets saved into a prefab asset.
/// </summary>
static class ShipCapsuleVfxBaker
{
    const string ParticalSystemPrefabPath = "Assets/Ship/Capsule/CapsuleParticalSystem.prefab";
    const string ImpactPrefabPath = "Assets/Ship/Capsule/CapsuleImpact.prefab";

    [MenuItem("Tools/Ship VFX/Bake Fire Trail Into CapsuleParticalSystem")]
    public static void Bake()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(ParticalSystemPrefabPath);
        try
        {
            EnsureChildWithComponent<ShipFireTrail>(root.transform, "FireTrail");

            PrefabUtility.SaveAsPrefabAsset(root, ParticalSystemPrefabPath);
            Debug.Log($"ShipCapsuleVfxBaker: baked FireTrail into {ParticalSystemPrefabPath}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    /// <summary>Builds a fresh "CapsuleImpact" prefab asset (ImpactFlash/ImpactDust/ImpactSparks/
    /// ImpactDebris) from ShipCrashImpact's procedural builder. Safe to re-run: if the prefab
    /// already exists, it opens it and only fills in whatever children are missing, leaving any
    /// hand-tuned ones untouched (same idempotent lookup ShipCrashImpact itself uses).</summary>
    [MenuItem("Tools/Ship VFX/Create CapsuleImpact Prefab")]
    public static void CreateCapsuleImpactPrefab()
    {
        bool prefabExists = AssetDatabase.LoadAssetAtPath<GameObject>(ImpactPrefabPath) != null;
        GameObject root = prefabExists
            ? PrefabUtility.LoadPrefabContents(ImpactPrefabPath)
            : new GameObject("CapsuleImpact");

        GameObject builder = new GameObject("CapsuleImpact_Builder");
        try
        {
            ShipCrashImpact impact = builder.AddComponent<ShipCrashImpact>();
            // ShipCrashImpact no longer builds in Awake() (that's deliberate - see
            // ShipCrashImpact.SetEffectPrefab), so force it via Trigger() instead. It builds
            // straight onto whatever children already exist under `root` by name... except the
            // builder has no `root` reference, it only knows its own transform. So build onto the
            // builder first, then move the (missing) children over onto `root` below.
            impact.Trigger();

            // Move every child the builder just created onto `root`, skipping any name that
            // already exists there (i.e. was hand-tuned in a previous bake).
            Transform[] children = new Transform[builder.transform.childCount];
            for (int i = 0; i < children.Length; i++)
                children[i] = builder.transform.GetChild(i);

            foreach (Transform child in children)
            {
                if (root.transform.Find(child.name) != null)
                    continue; // already authored - leave it as-is
                child.SetParent(root.transform, false);
            }

            // Trigger() above actually played everything to build it - reset back to a clean,
            // never-played state before saving so the prefab asset doesn't capture mid-simulation
            // particles.
            foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Clear(true);
            }

            PrefabUtility.SaveAsPrefabAsset(root, ImpactPrefabPath);
            Debug.Log($"ShipCapsuleVfxBaker: {(prefabExists ? "updated" : "created")} {ImpactPrefabPath}");
        }
        finally
        {
            Object.DestroyImmediate(builder);
            if (prefabExists)
                PrefabUtility.UnloadPrefabContents(root);
            else
                Object.DestroyImmediate(root);
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
