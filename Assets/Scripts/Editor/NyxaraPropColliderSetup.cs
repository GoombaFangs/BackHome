using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Adds Ground-layer MeshColliders to Nyxara prop prefabs so the player cannot walk through them.
/// Menu: BackHome → Setup Nyxara Prop Colliders
/// </summary>
public static class NyxaraPropColliderSetup
{
    const string PrefabsFolder = "Assets/Galaxy/Planets/Nyxara/Objects/Prefabs";
    const string GroundLayerName = "Ground";

    [MenuItem("BackHome/Setup Nyxara Prop Colliders")]
    public static void SetupAllMenu() => SetupAll(showDialog: true);

    public static void SetupAll(bool showDialog = true)
    {
        int ground = LayerMask.NameToLayer(GroundLayerName);
        if (ground < 0)
        {
            if (showDialog)
                EditorUtility.DisplayDialog("Prop Colliders", $"Layer '{GroundLayerName}' not found.", "OK");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabsFolder });
        if (guids.Length == 0)
        {
            if (showDialog)
                EditorUtility.DisplayDialog("Prop Colliders", $"No prefabs under:\n{PrefabsFolder}", "OK");
            return;
        }

        int ok = 0;
        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                string name = Path.GetFileNameWithoutExtension(path);
                if (showDialog)
                {
                    EditorUtility.DisplayProgressBar(
                        "Nyxara Prop Colliders",
                        $"{name} ({i + 1}/{guids.Length})",
                        (i + 0.5f) / guids.Length);
                }

                if (SetupPrefab(path, ground))
                    ok++;
            }
        }
        finally
        {
            if (showDialog)
                EditorUtility.ClearProgressBar();
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        if (showDialog)
        {
            EditorUtility.DisplayDialog(
                "Prop Colliders",
                $"Updated {ok}/{guids.Length} prefabs.\nLayer: {GroundLayerName}\nMeshCollider on meshes (Grass skipped).",
                "OK");
        }

        Debug.Log($"[BackHome] Prop colliders: {ok}/{guids.Length} ready in {PrefabsFolder}");
    }

    public static bool SetupPrefab(string prefabPath, int groundLayer)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null)
            return false;

        try
        {
            SetLayerRecursive(root, groundLayer);

            // Remove broken / tiny root box colliders from older setup.
            BoxCollider[] boxes = root.GetComponents<BoxCollider>();
            for (int i = 0; i < boxes.Length; i++)
                Undo.DestroyObjectImmediate(boxes[i]);

            bool skipSolid = IsNonBlockingProp(root.name);
            MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);

            if (skipSolid)
            {
                // Ensure no leftover solid colliders on decorative grass.
                Collider[] cols = root.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < cols.Length; i++)
                    Undo.DestroyObjectImmediate(cols[i]);
            }
            else
            {
                for (int i = 0; i < filters.Length; i++)
                {
                    MeshFilter mf = filters[i];
                    if (mf == null || mf.sharedMesh == null)
                        continue;

                    MeshCollider mc = mf.GetComponent<MeshCollider>();
                    if (mc == null)
                        mc = mf.gameObject.AddComponent<MeshCollider>();

                    mc.sharedMesh = mf.sharedMesh;
                    mc.convex = false;
                    mc.isTrigger = false;
                    mf.gameObject.layer = groundLayer;
                }

                // Fallback if FBX has no MeshFilter yet (rare): fitted box from renderers.
                if (filters.Length == 0 || root.GetComponentInChildren<MeshCollider>(true) == null)
                    AddBoundsBoxCollider(root);
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static bool IsNonBlockingProp(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        // Soft foliage — don't block the player.
        return name.IndexOf("Grass", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        Transform t = go.transform;
        for (int i = 0; i < t.childCount; i++)
            SetLayerRecursive(t.GetChild(i).gameObject, layer);
    }

    static void AddBoundsBoxCollider(GameObject root)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
            return;

        Bounds world = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
            world.Encapsulate(renderers[i].bounds);

        Vector3 localCenter = root.transform.InverseTransformPoint(world.center);
        Vector3 lossy = root.transform.lossyScale;
        Vector3 localSize = new Vector3(
            world.size.x / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
            world.size.y / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)),
            world.size.z / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)));

        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = localCenter;
        box.size = Vector3.Max(localSize, Vector3.one * 0.2f);
        box.isTrigger = false;
    }
}
