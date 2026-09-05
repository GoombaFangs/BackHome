using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor tools for baking/repairing the player crash VFX prefabs used by
/// <see cref="PlayerCrashIntro"/>:
/// - <see cref="CreateCapsuleImpactPrefab"/> creates/fills the "ImpactVfx" prefab from
///   PlayerCrashImpact's procedural builder.
/// </summary>
static class PlayerCapsuleVfxBaker
{
    const string ImpactPrefabPath = PlayerDiveDownCapsulePaths.AssetImpactVfxPrefab;

    /// <summary>Builds a fresh "ImpactVfx" prefab asset (ImpactFlash/ImpactDust/ImpactSparks/
    /// ImpactDebris) from PlayerCrashImpact's procedural builder. Safe to re-run: if the prefab
    /// already exists, it opens it and only fills in whatever children are missing, leaving any
    /// hand-tuned ones untouched (same idempotent lookup PlayerCrashImpact itself uses).</summary>
    [MenuItem("Tools/Player VFX/Create ImpactVfx Prefab")]
    public static void CreateCapsuleImpactPrefab()
    {
        bool prefabExists = AssetDatabase.LoadAssetAtPath<GameObject>(ImpactPrefabPath) != null;
        GameObject root = prefabExists
            ? PrefabUtility.LoadPrefabContents(ImpactPrefabPath)
            : new GameObject("ImpactVfx");

        GameObject builder = new GameObject("ImpactVfx_Builder");
        try
        {
            PlayerCrashImpact impact = builder.AddComponent<PlayerCrashImpact>();
            // PlayerCrashImpact no longer builds in Awake() (that's deliberate - see
            // PlayerCrashImpact.SetEffectPrefab), so force it via Trigger() instead. It builds
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
            Debug.Log($"PlayerCapsuleVfxBaker: {(prefabExists ? "updated" : "created")} {ImpactPrefabPath}");
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
}
