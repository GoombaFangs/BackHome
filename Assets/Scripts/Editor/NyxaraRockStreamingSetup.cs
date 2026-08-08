using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor tooling for Nyxara rock streaming — wires <see cref="PlanetRockStreamer"/> with
/// Rock / Rock2 / Rock3 / Rock4 only (excludes Rock_Blue_Ridge_Formation and other variants).
/// Menu: BackHome → Setup Nyxara Rock Streaming (Recommended)
/// </summary>
public static class NyxaraRockStreamingSetup
{
    const string ScenePath = "Assets/Scenes/Galaxy/PlanetNyxara.unity";
    const string RocksFolder = "Assets/Galaxy/Planets/Nyxara/Environment/Rock";
    const string Rock1PrefabPath = RocksFolder + "/Rock.prefab";
    const string Rock2PrefabPath = RocksFolder + "/Rock2.prefab";
    const string Rock3PrefabPath = RocksFolder + "/Rock3.prefab";
    const string Rock4PrefabPath = RocksFolder + "/Rock4.prefab";

    [MenuItem("BackHome/Setup Nyxara Rock Streaming (Recommended)")]
    public static void SetupStreamingMenu()
    {
        string message = SetupStreaming();
        if (message != null)
            EditorUtility.DisplayDialog("Rock Streaming", message, "OK");
    }

    /// <summary>Unity batchmode: -executeMethod NyxaraRockStreamingSetup.SetupStreamingBatch</summary>
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
            Debug.LogError("[BackHome] Rock Streaming: no SphericalPlanet found in the scene.");
            return "No SphericalPlanet found in the scene.";
        }

        GameObject rock1 = AssetDatabase.LoadAssetAtPath<GameObject>(Rock1PrefabPath);
        GameObject rock2 = AssetDatabase.LoadAssetAtPath<GameObject>(Rock2PrefabPath);
        GameObject rock3 = AssetDatabase.LoadAssetAtPath<GameObject>(Rock3PrefabPath);
        GameObject rock4 = AssetDatabase.LoadAssetAtPath<GameObject>(Rock4PrefabPath);
        if (rock1 == null || rock2 == null || rock3 == null || rock4 == null)
        {
            string missingMessage =
                "Missing prefab(s).\n"
                + $"Rock: {(rock1 != null ? "OK" : "MISSING")}\n"
                + $"Rock2: {(rock2 != null ? "OK" : "MISSING")}\n"
                + $"Rock3: {(rock3 != null ? "OK" : "MISSING")}\n"
                + $"Rock4: {(rock4 != null ? "OK" : "MISSING")}";
            Debug.LogError($"[BackHome] Rock Streaming: {missingMessage}");
            return missingMessage;
        }

        Transform streamersRoot = NyxaraStreamersRoot.FindOrCreate();
        PlanetRockStreamer streamer = streamersRoot.GetComponent<PlanetRockStreamer>();
        bool isNew = streamer == null;
        if (isNew)
            streamer = Undo.AddComponent<PlanetRockStreamer>(streamersRoot.gameObject);

        var so = new SerializedObject(streamer);
        so.FindProperty("planet").objectReferenceValue = planet;
        so.FindProperty("rock1Prefab").objectReferenceValue = rock1;
        so.FindProperty("rock2Prefab").objectReferenceValue = rock2;
        so.FindProperty("rock3Prefab").objectReferenceValue = rock3;
        so.FindProperty("rock4Prefab").objectReferenceValue = rock4;
        so.ApplyModifiedPropertiesWithoutUndo();

        EditorUtility.SetDirty(streamer);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        string resultMessage = (isNew ? "Added" : "Updated")
                                + " PlanetRockStreamer on 'Streamers' (Rock/Rock2/Rock3/Rock4) — rocks now stream in near the player at runtime.";
        Debug.Log($"[BackHome] {resultMessage}");
        return resultMessage;
    }

    static void OpenSceneIfNeeded()
    {
        if (!ScenePath.Equals(SceneManager.GetActiveScene().path, StringComparison.Ordinal))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }
}
