using UnityEditor;
using UnityEngine;

/// <summary>
/// Bakes the "DustTrail" prefab (see <see cref="DustTrailVfx"/>) into a real, editable prefab
/// asset, and wires an instance under Player.prefab's feet. Safe to re-run: re-baking refreshes
/// the particle modules from whatever fields are set on the DustTrailVfx component, and
/// attaching is a no-op if Player.prefab already has a "DustTrail" child.
/// </summary>
static class DustTrailVfxBaker
{
    const string PrefabPath = "Assets/Resources/Player/DustTrail/DustTrail.prefab";
    const string PlayerPrefabPath = "Assets/Resources/Player/Player.prefab";

    [MenuItem("Tools/Player VFX/Build Dust Trail (Create + Attach To Player)")]
    public static void BuildAll()
    {
        CreatePrefab();
        AttachToPlayer();
    }

    [MenuItem("Tools/Player VFX/Create DustTrail Prefab")]
    public static void CreatePrefab()
    {
        bool prefabExists = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
        GameObject root = prefabExists
            ? PrefabUtility.LoadPrefabContents(PrefabPath)
            : new GameObject("DustTrail");

        try
        {
            DustTrailVfx vfx = root.GetComponent<DustTrailVfx>();
            if (vfx == null)
                vfx = root.AddComponent<DustTrailVfx>(); // RequireComponent adds the ParticleSystem too; Awake() configures every module
            else
                vfx.Configure(); // already authored - just refresh modules from the current inspector fields

            ParticleSystem ps = root.GetComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Clear(true);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log($"DustTrailVfxBaker: {(prefabExists ? "updated" : "created")} {PrefabPath}");
        }
        finally
        {
            if (prefabExists)
                PrefabUtility.UnloadPrefabContents(root);
            else
                Object.DestroyImmediate(root);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    [MenuItem("Tools/Player VFX/Attach DustTrail To Player")]
    public static void AttachToPlayer()
    {
        GameObject dustPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (dustPrefab == null)
        {
            Debug.LogError($"DustTrailVfxBaker: run 'Create DustTrail Prefab' first - {PrefabPath} doesn't exist.");
            return;
        }

        GameObject playerRoot = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            if (playerRoot.transform.Find("DustTrail") != null)
            {
                Debug.Log("DustTrailVfxBaker: Player.prefab already has a DustTrail child - skipping.");
                return;
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(dustPrefab, playerRoot.transform);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            instance.name = "DustTrail";

            PrefabUtility.SaveAsPrefabAsset(playerRoot, PlayerPrefabPath);
            Debug.Log("DustTrailVfxBaker: attached DustTrail under Player.prefab.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(playerRoot);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}
