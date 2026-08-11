using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor tooling for Nyxara tree streaming — mirrors <see cref="NyxaraGrassScatter"/>'s streaming
/// setup but wires up <see cref="PlanetTreeStreamer"/> with Tree1..Tree5. Tree_Emerald_Canopy and
/// Tree_Verdant_Crown are intentionally excluded (kept as hand-placed decoration, not part of the
/// streamed mix).
/// Menu: BackHome → Setup Nyxara Tree Streaming (Recommended)
/// </summary>
public static class NyxaraTreeStreamingSetup
{
    const string ScenePath = "Assets/Scenes/PlanetNyxara.unity";
    const string TreesFolder = "Assets/Galaxy/Planets/Nyxara/Environment/Trees";
    const string Tree1PrefabPath = TreesFolder + "/Tree1.prefab";
    const string Tree2PrefabPath = TreesFolder + "/Tree2.prefab";
    const string Tree3PrefabPath = TreesFolder + "/Tree3.prefab";
    const string Tree4PrefabPath = TreesFolder + "/Tree4.prefab";
    const string Tree5PrefabPath = TreesFolder + "/Tree5.prefab";

    [MenuItem("BackHome/Setup Nyxara Tree Streaming (Recommended)")]
    public static void SetupStreamingMenu()
    {
        string message = SetupStreaming();
        if (message != null)
            EditorUtility.DisplayDialog("Tree Streaming", message, "OK");
    }

    /// <summary>Unity batchmode: -executeMethod NyxaraTreeStreamingSetup.SetupStreamingBatch</summary>
    public static void SetupStreamingBatch()
    {
        try
        {
            SetupStreaming();
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorApplication.Exit(1);
            return;
        }

        EditorApplication.Exit(0);
    }

    static string SetupStreaming()
    {
        OpenSceneIfNeeded();

        SphericalPlanet planet = UnityEngine.Object.FindFirstObjectByType<SphericalPlanet>();
        if (planet == null)
        {
            Debug.LogError("[BackHome] Tree Streaming: no SphericalPlanet found in the scene.");
            return "No SphericalPlanet found in the scene.";
        }

        GameObject tree1 = AssetDatabase.LoadAssetAtPath<GameObject>(Tree1PrefabPath);
        GameObject tree2 = AssetDatabase.LoadAssetAtPath<GameObject>(Tree2PrefabPath);
        GameObject tree3 = AssetDatabase.LoadAssetAtPath<GameObject>(Tree3PrefabPath);
        GameObject tree4 = AssetDatabase.LoadAssetAtPath<GameObject>(Tree4PrefabPath);
        GameObject tree5 = AssetDatabase.LoadAssetAtPath<GameObject>(Tree5PrefabPath);
        if (tree1 == null || tree2 == null || tree3 == null || tree4 == null || tree5 == null)
        {
            string missingMessage =
                "Missing prefab(s).\n"
                + $"Tree1: {(tree1 != null ? "OK" : "MISSING")}\n"
                + $"Tree2: {(tree2 != null ? "OK" : "MISSING")}\n"
                + $"Tree3: {(tree3 != null ? "OK" : "MISSING")}\n"
                + $"Tree4: {(tree4 != null ? "OK" : "MISSING")}\n"
                + $"Tree5: {(tree5 != null ? "OK" : "MISSING")}";
            Debug.LogError($"[BackHome] Tree Streaming: {missingMessage}");
            return missingMessage;
        }

        Transform streamersRoot = NyxaraStreamersRoot.FindOrCreate();
        PlanetTreeStreamer streamer = streamersRoot.GetComponent<PlanetTreeStreamer>();
        bool isNew = streamer == null;
        if (isNew)
            streamer = Undo.AddComponent<PlanetTreeStreamer>(streamersRoot.gameObject);

        var so = new SerializedObject(streamer);
        so.FindProperty("planet").objectReferenceValue = planet;
        so.FindProperty("tree1Prefab").objectReferenceValue = tree1;
        so.FindProperty("tree2Prefab").objectReferenceValue = tree2;
        so.FindProperty("tree3Prefab").objectReferenceValue = tree3;
        so.FindProperty("tree4Prefab").objectReferenceValue = tree4;
        so.FindProperty("tree5Prefab").objectReferenceValue = tree5;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(streamer);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        string resultMessage = (isNew ? "Added" : "Updated")
                                + " PlanetTreeStreamer on 'Streamers' (Tree1/Tree2/Tree3/Tree4/Tree5) — trees now stream in near the player at runtime.";
        Debug.Log($"[BackHome] {resultMessage}");
        return resultMessage;
    }

    static void OpenSceneIfNeeded()
    {
        if (!ScenePath.Equals(SceneManager.GetActiveScene().path, StringComparison.Ordinal))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }
}
