using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor tools for baking/repairing the player capsule crash VFX prefabs used by
/// <see cref="PlayerCrashIntro"/>:
/// - <see cref="CreateCapsuleImpactPrefab"/> creates/fills the "ImpactVfx" prefab (Impact
///   Flash/Dust/Sparks/Debris) from PlayerCrashImpact's procedural builder.
/// - <see cref="FixPlayerDiveDownCapsuleMesh"/> is a one-off repair for PlayerDiveDownCapsule's
///   root, which lost its MeshFilter/MeshRenderer at some point.
///
/// (The one-time migration that merged the two legacy source prefabs into PlayerDiveDownCapsule
/// and then deleted them has been removed now that it's done - there's nothing left to merge.)
/// </summary>
static class PlayerCapsuleVfxBaker
{
    const string ImpactPrefabPath = PlayerDiveDownCapsulePaths.AssetImpactVfxPrefab;
    const string PlayerDiveDownCapsulePrefabPath = PlayerDiveDownCapsulePaths.AssetCapsulePrefab;

    /// <summary>Same placeholder metal material the capsule's builtin Capsule mesh has always
    /// used.</summary>
    const string CapsuleMaterialPath = "Assets/AssetStore/Creepy_Cat/3D Scifi Kit Starter Kit_HD/Materials/HD_Floor_02_Norm.mat";

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

    /// <summary>One-off repair: PlayerDiveDownCapsule's root is missing its MeshFilter/
    /// MeshRenderer (the actual visible capsule shape) - restores the capsule's builtin Capsule
    /// mesh + placeholder material, without touching anything else (SphereCollider/GalaxyGate/
    /// PlayerCapsuleBeacon/nested TrailVfx are untouched). Safe to re-run: no-ops once both are
    /// present.</summary>
    [MenuItem("Tools/Player VFX/Fix PlayerDiveDownCapsule Mesh")]
    public static void FixPlayerDiveDownCapsuleMesh()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogError("PlayerCapsuleVfxBaker: stop Play Mode first - refusing to run while playing.");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<GameObject>(PlayerDiveDownCapsulePrefabPath) == null)
        {
            Debug.LogError($"PlayerCapsuleVfxBaker: {PlayerDiveDownCapsulePrefabPath} not found.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PlayerDiveDownCapsulePrefabPath);
        bool changed = false;
        try
        {
            MeshFilter filter = root.GetComponent<MeshFilter>();
            if (filter == null)
            {
                filter = root.AddComponent<MeshFilter>();
                changed = true;
            }
            if (filter.sharedMesh == null)
            {
                filter.sharedMesh = AssetDatabase.GetBuiltinExtraResource<Mesh>("Capsule.fbx");
                changed = true;
            }

            MeshRenderer renderer = root.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                renderer = root.AddComponent<MeshRenderer>();
                changed = true;
            }
            if (renderer.sharedMaterial == null)
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(CapsuleMaterialPath);
                if (material != null)
                {
                    renderer.sharedMaterial = material;
                    changed = true;
                }
                else
                {
                    Debug.LogWarning($"PlayerCapsuleVfxBaker: placeholder material not found at {CapsuleMaterialPath}.");
                }
            }

            if (changed)
                PrefabUtility.SaveAsPrefabAsset(root, PlayerDiveDownCapsulePrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        if (changed)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"PlayerCapsuleVfxBaker: restored the capsule mesh/material on {PlayerDiveDownCapsulePrefabPath}.");
        }
        else
        {
            Debug.Log($"PlayerCapsuleVfxBaker: {PlayerDiveDownCapsulePrefabPath} already has its mesh/material - nothing to do.");
        }
    }
}
