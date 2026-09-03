using UnityEditor;
using UnityEngine;

/// <summary>
/// One-time structural edit: replaces the single centered "DustTrail" child under Player.prefab
/// with two instances - "DustTrail_LeftFoot" and "DustTrail_RightFoot" - each pinned to one foot
/// bone via <see cref="DustTrailFootFollower"/>, so running kicks up dust from both feet instead
/// of one puff trailing the character's center. Safe to re-run (skips feet that already exist).
///
/// Both instances stay real nested-prefab instances of DustTrail.prefab, so any hand-tuning done
/// there (mesh, material, size/shape/color curves, ...) is still shared and editable normally.
///
/// IMPORTANT: both must live as DIRECT children of Player.prefab's root, NOT nested inside the
/// model's bone hierarchy (e.g. under LeftFoot/RightFoot themselves). DustTrailFootFollower already
/// tracks the foot bone by reference (Animator.GetBoneTransform), so literal bone-parenting buys
/// nothing - and it's actively dangerous here, because PlayerModelSwapTool's model/animation swap
/// helpers (SwapPrefabModelTo, SwapPrefabModel, ...) Object.DestroyImmediate the entire "Model"/
/// "Geometry"/"Skeleton" hierarchy and rebuild it from a fresh prefab instance whenever the model or
/// run animation is swapped. Anything nested under a bone inside that hierarchy - like a DustTrail
/// accidentally dragged onto LeftFoot/RightFoot in the Hierarchy window - gets destroyed right along
/// with it, silently. This method re-parents back to the root if it finds either one nested deeper.
/// </summary>
static class DustTrailFeetSetup
{
    const string PlayerPrefabPath = "Assets/Resources/Player/Player.prefab";
    const string DustPrefabPath = "Assets/Resources/Player/DustTrail/DustTrail.prefab";

    [MenuItem("Tools/Player VFX/Split DustTrail Into Left+Right Feet")]
    public static void Split()
    {
        GameObject dustPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DustPrefabPath);
        if (dustPrefab == null)
        {
            Debug.LogError($"DustTrailFeetSetup: {DustPrefabPath} doesn't exist.");
            return;
        }

        GameObject playerRoot = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            Transform existing = playerRoot.transform.Find("DustTrail");
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            bool changedLeft = EnsureFootTrail(playerRoot.transform, dustPrefab, "DustTrail_LeftFoot", HumanBodyBones.LeftFoot);
            bool changedRight = EnsureFootTrail(playerRoot.transform, dustPrefab, "DustTrail_RightFoot", HumanBodyBones.RightFoot);

            if (existing == null && !changedLeft && !changedRight)
            {
                Debug.Log("DustTrailFeetSetup: both foot trails already exist and are correctly parented - nothing to do.");
                return;
            }

            PrefabUtility.SaveAsPrefabAsset(playerRoot, PlayerPrefabPath);
            Debug.Log("DustTrailFeetSetup: Player.prefab now has DustTrail_LeftFoot/DustTrail_RightFoot as direct children of the root.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(playerRoot);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    /// <summary>Ensures a foot trail exists as a direct child of <paramref name="root"/>. If one already
    /// exists but is nested deeper in the hierarchy (e.g. dragged onto a foot bone), it's moved back to
    /// the root instead of being duplicated. Returns true if anything was created or moved.</summary>
    static bool EnsureFootTrail(Transform root, GameObject dustPrefab, string name, HumanBodyBones foot)
    {
        Transform found = FindDeep(root, name);
        if (found != null)
        {
            if (found.parent == root)
                return false; // already set up correctly - leave it alone

            found.SetParent(root, false);
            found.localPosition = Vector3.zero;
            found.localRotation = Quaternion.identity;
            found.localScale = Vector3.one;
            Debug.LogWarning($"DustTrailFeetSetup: '{name}' was nested inside the model's bone hierarchy " +
                "(PlayerModelSwapTool would destroy it on the next model/animation swap) - moved it back " +
                "to be a direct child of Player.prefab's root.");
            return true;
        }

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(dustPrefab, root);
        instance.name = name;
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;

        DustTrailFootFollower follower = instance.AddComponent<DustTrailFootFollower>();
        follower.SetFoot(foot);
        return true;
    }

    static Transform FindDeep(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t != root && t.name == name)
                return t;
        return null;
    }
}
