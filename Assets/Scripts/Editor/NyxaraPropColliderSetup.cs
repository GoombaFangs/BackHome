using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Adds Ground-layer colliders to Nyxara prop prefabs.
/// High-poly meshes use BoxCollider (MeshCollider warns / breaks above ~2M tris).
/// Menu: BackHome → Setup Nyxara Prop Colliders
/// </summary>
public static class NyxaraPropColliderSetup
{
    const string PrefabsFolder = "Assets/Galaxy/Planets/Nyxara/Objects/Prefabs";
    const string GroundLayerName = "Ground";

    /// <summary>Above this, MeshCollider spam / Fast Midphase fails — use BoxCollider.</summary>
    const int MaxMeshColliderTriangles = 50_000;

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
                $"Updated {ok}/{guids.Length} prefabs.\nHeavy meshes → BoxCollider; light meshes → MeshCollider.",
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
            ApplyColliders(root, destroyImmediate: true);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>Shared by prefab setup + collider menu.</summary>
    public static void ApplyColliders(GameObject root, bool destroyImmediate)
    {
        // Clear existing colliders on this prop hierarchy.
        Collider[] existing = root.GetComponentsInChildren<Collider>(true);
        for (int i = existing.Length - 1; i >= 0; i--)
        {
            if (existing[i] == null)
                continue;
            if (destroyImmediate)
                Object.DestroyImmediate(existing[i]);
            else
                Undo.DestroyObjectImmediate(existing[i]);
        }

        if (IsNonBlockingProp(root.name))
            return;

        MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
        bool addedAny = false;

        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter mf = filters[i];
            if (mf == null || mf.sharedMesh == null)
                continue;

            Mesh mesh = mf.sharedMesh;
            int triCount = EstimateTriangleCount(mesh);

            if (triCount > MaxMeshColliderTriangles)
            {
                AddFittedBoxCollider(mf.gameObject, mesh.bounds);
                addedAny = true;
            }
            else
            {
                MeshCollider mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mesh;
                mc.convex = false;
                mc.isTrigger = false;
                // Avoid Fast Midphase warning / physics bugs on large meshes.
                mc.cookingOptions =
                    MeshColliderCookingOptions.CookForFasterSimulation |
                    MeshColliderCookingOptions.EnableMeshCleaning |
                    MeshColliderCookingOptions.WeldColocatedVertices;
                addedAny = true;
            }
        }

        if (!addedAny)
            AddBoundsBoxCollider(root);
    }

    static int EstimateTriangleCount(Mesh mesh)
    {
        // Works for non-readable imported meshes (unlike mesh.triangles).
        int tris = 0;
        for (int s = 0; s < mesh.subMeshCount; s++)
            tris += (int)(mesh.GetIndexCount(s) / 3);
        return tris;
    }

    static bool IsNonBlockingProp(string name)
    {
        if (string.IsNullOrEmpty(name))
            return false;
        return name.IndexOf("Grass", System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        go.layer = layer;
        Transform t = go.transform;
        for (int i = 0; i < t.childCount; i++)
            SetLayerRecursive(t.GetChild(i).gameObject, layer);
    }

    static void AddFittedBoxCollider(GameObject go, Bounds meshLocalBounds)
    {
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.center = meshLocalBounds.center;
        box.size = Vector3.Max(meshLocalBounds.size, Vector3.one * 0.05f);
        box.isTrigger = false;
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
