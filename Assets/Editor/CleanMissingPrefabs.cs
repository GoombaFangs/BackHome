using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Removes Missing Prefab instances left behind after deleting assets (e.g. GrondPlanet).
/// </summary>
public static class CleanMissingPrefabs
{
    const string AutoCleanKey = "BackHome.CleanMissingPrefabs.Done";

    [MenuItem("BackHome/Clean Missing Prefabs In Open Scenes")]
    public static void CleanOpenScenes()
    {
        int removed = CleanAllLoadedScenes();
        if (removed > 0)
        {
            EditorSceneManager.MarkAllScenesDirty();
            Debug.Log($"[BackHome] Removed {removed} missing prefab instance(s). Save the scene (Ctrl+S).");
        }
        else
        {
            Debug.Log("[BackHome] No missing prefab instances found in open scenes.");
        }
    }

    [InitializeOnLoadMethod]
    static void AutoCleanOnLoad()
    {
        if (SessionState.GetBool(AutoCleanKey, false))
            return;

        EditorApplication.delayCall += () =>
        {
            if (SessionState.GetBool(AutoCleanKey, false))
                return;

            int removed = CleanAllLoadedScenes();
            SessionState.SetBool(AutoCleanKey, true);

            if (removed <= 0)
                return;

            EditorSceneManager.MarkAllScenesDirty();
            Debug.Log($"[BackHome] Auto-removed {removed} missing prefab instance(s). Save PlanetA (Ctrl+S).");
        };
    }

    static int CleanAllLoadedScenes()
    {
        int removed = 0;
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            Scene scene = SceneManager.GetSceneAt(i);
            if (!scene.IsValid() || !scene.isLoaded)
                continue;

            foreach (GameObject root in scene.GetRootGameObjects())
                removed += CleanRecursive(root);
        }

        return removed;
    }

    static int CleanRecursive(GameObject go)
    {
        if (go == null)
            return 0;

        int removed = 0;
        var children = new List<GameObject>();
        foreach (Transform child in go.transform)
            children.Add(child.gameObject);

        foreach (GameObject child in children)
            removed += CleanRecursive(child);

        if (go != null && PrefabUtility.IsPrefabAssetMissing(go))
        {
            Object.DestroyImmediate(go);
            removed++;
        }

        return removed;
    }
}
