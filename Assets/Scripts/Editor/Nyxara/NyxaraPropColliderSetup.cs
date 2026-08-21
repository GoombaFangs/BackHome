using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Builds tight-fitting Ground-layer BoxColliders for Nyxara prop prefabs.
/// Instead of one loose bounding box per prop, each mesh's local bounds are sliced along the
/// longest axis and one box is fit per slice — same idea as the hand-placed multi-box colliders
/// on the ship's SciFi kit pieces, just automated.
///
/// Deliberately uses <see cref="Mesh.bounds"/> only (never Mesh.vertices/triangles), so it never
/// needs to flip "Read/Write Enabled" on the source FBX models. That flag requires a full model
/// reimport, which was slow/heavy enough on the big tree models to hang the editor mid-batch and
/// leave prefabs with their old colliders removed but no new ones added. Bounds-based slicing is
/// slightly coarser for very asymmetric meshes but is instant and can never crash the editor.
///
/// Menu: BackHome → Setup Nyxara Prop Colliders
/// </summary>
public static class NyxaraPropColliderSetup
{
    const string PrefabsFolder = "Assets/Resources/Galaxy/Planets/Nyxara/Environment";
    const string GroundLayerName = "Ground";

    /// <summary>Max boxes generated per mesh — keeps physics cheap while still hugging the shape.</summary>
    const int MaxBoxesPerMesh = 4;
    const float SizePadding = 0.04f;

    [MenuItem("BackHome/Setup Nyxara Prop Colliders")]
    public static void SetupAllMenu() => SetupAll(showDialog: true);

    public static void SetupAllBatch() => SetupAll(showDialog: false);

    [MenuItem("BackHome/Clear Nyxara Prop Colliders")]
    public static void ClearAllMenu() => ClearAll(showDialog: true);

    public static void ClearAllBatch() => ClearAll(showDialog: false);

    public static void ClearAll(bool showDialog = true)
    {
        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabsFolder });
        if (guids.Length == 0)
        {
            if (showDialog)
                EditorUtility.DisplayDialog("Clear Prop Colliders", $"No prefabs under:\n{PrefabsFolder}", "OK");
            return;
        }

        int cleared = 0;
        int removed = 0;
        try
        {
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                string name = Path.GetFileNameWithoutExtension(path);
                if (showDialog)
                {
                    EditorUtility.DisplayProgressBar(
                        "Clear Nyxara Prop Colliders",
                        $"{name} ({i + 1}/{guids.Length})",
                        (i + 0.5f) / guids.Length);
                }

                if (ClearPrefab(path, out int count))
                {
                    cleared++;
                    removed += count;
                }
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
                "Clear Prop Colliders",
                $"Removed {removed} colliders from {cleared}/{guids.Length} prefabs.",
                "OK");
        }

        Debug.Log($"[BackHome] Cleared {removed} prop colliders from {cleared}/{guids.Length} prefabs in {PrefabsFolder}");
    }

    public static bool ClearPrefab(string prefabPath, out int removedCount)
    {
        removedCount = 0;
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null)
            return false;

        try
        {
            Collider[] existing = root.GetComponentsInChildren<Collider>(true);
            removedCount = existing.Length;
            for (int i = existing.Length - 1; i >= 0; i--)
            {
                if (existing[i] != null)
                    UnityEngine.Object.DestroyImmediate(existing[i]);
            }

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            return removedCount > 0;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

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
        int failed = 0;
        var failedNames = new List<string>();
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

                try
                {
                    if (SetupPrefab(path, ground))
                        ok++;
                    else
                    {
                        failed++;
                        failedNames.Add(name);
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    failedNames.Add(name);
                    Debug.LogError($"[BackHome] Failed to set up colliders for '{name}': {ex}");
                }
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
            string message = $"Updated {ok}/{guids.Length} prefabs with tight multi-box colliders.";
            if (failed > 0)
                message += $"\n\nFailed: {string.Join(", ", failedNames)}";
            EditorUtility.DisplayDialog("Prop Colliders", message, "OK");
        }

        Debug.Log($"[BackHome] Prop colliders: {ok}/{guids.Length} ready in {PrefabsFolder}"
            + (failed > 0 ? $" ({failed} failed: {string.Join(", ", failedNames)})" : string.Empty));
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

            // Guarantee every prop keeps at least one collider on disk — never leave a prefab
            // in the "colliders removed, none added" state even if slicing found nothing.
            if (root.GetComponentInChildren<Collider>(true) == null && !IsNonBlockingProp(root.name))
                AddFallbackBox(root);

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>Shared by prefab setup + collider menu. Rebuilds root-level BoxColliders.</summary>
    public static void ApplyColliders(GameObject root, bool destroyImmediate)
    {
        Collider[] existing = root.GetComponentsInChildren<Collider>(true);
        for (int i = existing.Length - 1; i >= 0; i--)
        {
            if (existing[i] == null)
                continue;
            if (destroyImmediate)
                UnityEngine.Object.DestroyImmediate(existing[i]);
            else
                Undo.DestroyObjectImmediate(existing[i]);
        }

        if (IsNonBlockingProp(root.name))
            return;

        MeshFilter[] filters = root.GetComponentsInChildren<MeshFilter>(true);
        int added = 0;

        for (int i = 0; i < filters.Length; i++)
        {
            MeshFilter mf = filters[i];
            if (mf == null || mf.sharedMesh == null)
                continue;

            List<BoxFit> boxes = BuildBoxes(mf, root.transform);
            for (int b = 0; b < boxes.Count; b++)
            {
                BoxCollider box = root.AddComponent<BoxCollider>();
                box.center = boxes[b].Center;
                box.size = boxes[b].Size;
                box.isTrigger = false;
                added++;
            }
        }

        if (added == 0)
            AddFallbackBox(root);
    }

    struct BoxFit
    {
        public Vector3 Center;
        public Vector3 Size;
    }

    /// <summary>
    /// Slices a mesh's local bounds along its longest axis (relative to root) and fits one box
    /// per slice. Round/blocky props naturally collapse to a single box; elongated props (logs,
    /// tall trees) get several boxes instead of one oversized one. Uses only Mesh.bounds (always
    /// available, no Read/Write Enabled required) — safe and instant even for huge meshes.
    /// </summary>
    static List<BoxFit> BuildBoxes(MeshFilter mf, Transform root)
    {
        Mesh mesh = mf.sharedMesh;
        Bounds localBounds = mesh.bounds;
        if (localBounds.size.sqrMagnitude < 1e-8f)
            return FallbackFromRenderer(mf, root);

        Transform meshT = mf.transform;
        Bounds overall = TransformBoundsToRoot(localBounds, meshT, root);

        int axis = PrimaryAxis(overall.size);
        float primaryExtent = overall.size[axis];
        float crossMax = Mathf.Max(
            overall.size[(axis + 1) % 3],
            overall.size[(axis + 2) % 3],
            0.001f);

        int slices = Mathf.Clamp(Mathf.RoundToInt(primaryExtent / (crossMax * 0.75f)), 1, MaxBoxesPerMesh);

        if (slices <= 1)
        {
            return new List<BoxFit>
            {
                new BoxFit
                {
                    Center = overall.center,
                    Size = Vector3.Max(overall.size, Vector3.one * 0.05f) * (1f + SizePadding)
                }
            };
        }

        float min = overall.min[axis];
        float max = overall.max[axis];
        float step = (max - min) / slices;
        float overlap = step * 0.15f;

        var result = new List<BoxFit>(slices);
        for (int s = 0; s < slices; s++)
        {
            Vector3 center = overall.center;
            center[axis] = min + step * (s + 0.5f);

            Vector3 size = overall.size;
            size[axis] = step + overlap;
            size = Vector3.Max(size, Vector3.one * 0.05f) * (1f + SizePadding);

            result.Add(new BoxFit { Center = center, Size = size });
        }

        return result;
    }

    /// <summary>Transforms a local-space AABB (all 8 corners) into root-local space and re-encloses it.</summary>
    static Bounds TransformBoundsToRoot(Bounds local, Transform meshTransform, Transform root)
    {
        Vector3 c = local.center;
        Vector3 e = local.extents;
        Bounds result = new Bounds(root.InverseTransformPoint(meshTransform.TransformPoint(c)), Vector3.zero);
        for (int xs = -1; xs <= 1; xs += 2)
        for (int ys = -1; ys <= 1; ys += 2)
        for (int zs = -1; zs <= 1; zs += 2)
        {
            Vector3 corner = c + new Vector3(e.x * xs, e.y * ys, e.z * zs);
            result.Encapsulate(root.InverseTransformPoint(meshTransform.TransformPoint(corner)));
        }
        return result;
    }

    static int PrimaryAxis(Vector3 size)
    {
        if (size.x >= size.y && size.x >= size.z)
            return 0;
        return size.y >= size.z ? 1 : 2;
    }

    static List<BoxFit> FallbackFromRenderer(MeshFilter mf, Transform root)
    {
        Renderer renderer = mf.GetComponent<Renderer>();
        if (renderer == null)
            return new List<BoxFit>();

        Bounds world = renderer.bounds;
        Vector3 center = root.InverseTransformPoint(world.center);
        Vector3 lossy = root.lossyScale;
        Vector3 size = new Vector3(
            world.size.x / Mathf.Max(0.0001f, Mathf.Abs(lossy.x)),
            world.size.y / Mathf.Max(0.0001f, Mathf.Abs(lossy.y)),
            world.size.z / Mathf.Max(0.0001f, Mathf.Abs(lossy.z)));

        return new List<BoxFit> { new BoxFit { Center = center, Size = Vector3.Max(size, Vector3.one * 0.1f) } };
    }

    static void AddFallbackBox(GameObject root)
    {
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, 0.5f, 0f);
        box.size = new Vector3(1f, 1f, 1f);
        box.isTrigger = false;
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
}
