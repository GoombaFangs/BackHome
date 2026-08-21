using System;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor tooling for Nyxara's region-based environment: creates/updates the example
/// <see cref="PlanetEnvironmentRegionSet"/> asset (four procedural regions) and wires it into the
/// global Grass/Tree/Rock streamers on "EnvironmentManager" (via <see cref="PlanetEnvironmentManager"/>).
///
/// Does NOT touch <see cref="CreatureSpawner"/> — creatures are configured separately in the
/// Inspector. Environment and creatures are fully decoupled.
///
/// Flora streams across the whole planet via noise-seeded region blobs
/// (<see cref="PlanetEnvironmentRegionSet.GetRegionIndex"/>). No hand-placed Area markers needed.
///
/// Region A: Tree1 / Grass3 + GrassLuminousToadstool / Rock3.
/// Region B: Tree3 + Tree5 / Grass2 / Rock.
/// Region C: Tree2 / Grass / Rock2.
/// Region D: Tree4 / HollowLog / Rock4.
///
/// TreeEmeraldCanopy and RockBlueRidgeFormation are scene props only — not streamed via regions.
///
/// Menu: BackHome → Setup Nyxara Environment Regions (Recommended)
/// </summary>
public static class NyxaraEnvironmentRegionsSetup
{
    const string ScenePath = "Assets/Scenes/PlanetNyxara.unity";
    const string EnvironmentFolder = "Assets/Resources/Galaxy/Planets/Nyxara/Environment";
    const string RegionSetAssetPath = EnvironmentFolder + "/NyxaraEnvironmentRegions.asset";

    const string TreesFolder = EnvironmentFolder + "/Trees";
    const string GrassFolder = EnvironmentFolder + "/Grass";
    const string RocksFolder = EnvironmentFolder + "/Rock";

    const string Tree1PrefabPath = TreesFolder + "/Tree1.prefab";
    const string Tree2PrefabPath = TreesFolder + "/Tree2.prefab";
    const string Tree3PrefabPath = TreesFolder + "/Tree3.prefab";
    const string Tree4PrefabPath = TreesFolder + "/Tree4.prefab";
    const string Tree5PrefabPath = TreesFolder + "/Tree5.prefab";
    const string Grass1PrefabPath = GrassFolder + "/Grass.prefab";
    const string Grass2PrefabPath = GrassFolder + "/Grass2.prefab";
    const string Grass3PrefabPath = GrassFolder + "/Grass3.prefab";
    const string GrassLuminousToadstoolPrefabPath = GrassFolder + "/GrassLuminousToadstool.prefab";
    const string HollowLogPrefabPath = GrassFolder + "/HollowLog.prefab";
    const string Rock1PrefabPath = RocksFolder + "/Rock.prefab";
    const string Rock2PrefabPath = RocksFolder + "/Rock2.prefab";
    const string Rock3PrefabPath = RocksFolder + "/Rock3.prefab";
    const string Rock4PrefabPath = RocksFolder + "/Rock4.prefab";

    /// <summary>Retired hierarchy root from an old per-Area streaming experiment — removed on setup.</summary>
    const string EnvironmentRegionsRootName = "EnvironmentRegions";

    [MenuItem("BackHome/Setup Nyxara Environment Regions (Recommended)")]
    public static void SetupRegionsMenu()
    {
        string message = SetupRegions();
        if (message != null)
            EditorUtility.DisplayDialog("Environment Regions", message, "OK");
    }

    /// <summary>Unity batchmode: -executeMethod NyxaraEnvironmentRegionsSetup.SetupRegionsBatch</summary>
    public static void SetupRegionsBatch()
    {
        try
        {
            SetupRegions();
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
            EditorApplication.Exit(1);
            return;
        }

        EditorApplication.Exit(0);
    }

    static string SetupRegions()
    {
        OpenSceneIfNeeded();

        GameObject tree1 = AssetDatabase.LoadAssetAtPath<GameObject>(Tree1PrefabPath);
        GameObject tree2 = AssetDatabase.LoadAssetAtPath<GameObject>(Tree2PrefabPath);
        GameObject tree3 = AssetDatabase.LoadAssetAtPath<GameObject>(Tree3PrefabPath);
        GameObject tree4 = AssetDatabase.LoadAssetAtPath<GameObject>(Tree4PrefabPath);
        GameObject tree5 = AssetDatabase.LoadAssetAtPath<GameObject>(Tree5PrefabPath);
        GameObject grass1 = AssetDatabase.LoadAssetAtPath<GameObject>(Grass1PrefabPath);
        GameObject grass2 = AssetDatabase.LoadAssetAtPath<GameObject>(Grass2PrefabPath);
        GameObject grass3 = AssetDatabase.LoadAssetAtPath<GameObject>(Grass3PrefabPath);
        GameObject grassToadstool = AssetDatabase.LoadAssetAtPath<GameObject>(GrassLuminousToadstoolPrefabPath);
        GameObject hollowLog = AssetDatabase.LoadAssetAtPath<GameObject>(HollowLogPrefabPath);
        GameObject rock1 = AssetDatabase.LoadAssetAtPath<GameObject>(Rock1PrefabPath);
        GameObject rock2 = AssetDatabase.LoadAssetAtPath<GameObject>(Rock2PrefabPath);
        GameObject rock3 = AssetDatabase.LoadAssetAtPath<GameObject>(Rock3PrefabPath);
        GameObject rock4 = AssetDatabase.LoadAssetAtPath<GameObject>(Rock4PrefabPath);

        if (tree1 == null || tree2 == null || tree3 == null || tree4 == null || tree5 == null
            || grass1 == null || grass2 == null || grass3 == null || grassToadstool == null || hollowLog == null
            || rock1 == null || rock2 == null || rock3 == null || rock4 == null)
        {
            string missingMessage =
                "Missing prefab(s).\n"
                + $"Tree1: {(tree1 != null ? "OK" : "MISSING")}\n"
                + $"Tree2: {(tree2 != null ? "OK" : "MISSING")}\n"
                + $"Tree3: {(tree3 != null ? "OK" : "MISSING")}\n"
                + $"Tree4: {(tree4 != null ? "OK" : "MISSING")}\n"
                + $"Tree5: {(tree5 != null ? "OK" : "MISSING")}\n"
                + $"Grass: {(grass1 != null ? "OK" : "MISSING")}\n"
                + $"Grass2: {(grass2 != null ? "OK" : "MISSING")}\n"
                + $"Grass3: {(grass3 != null ? "OK" : "MISSING")}\n"
                + $"GrassLuminousToadstool: {(grassToadstool != null ? "OK" : "MISSING")}\n"
                + $"HollowLog: {(hollowLog != null ? "OK" : "MISSING")}\n"
                + $"Rock: {(rock1 != null ? "OK" : "MISSING")}\n"
                + $"Rock2: {(rock2 != null ? "OK" : "MISSING")}\n"
                + $"Rock3: {(rock3 != null ? "OK" : "MISSING")}\n"
                + $"Rock4: {(rock4 != null ? "OK" : "MISSING")}";
            Debug.LogError($"[BackHome] Environment Regions: {missingMessage}");
            return missingMessage;
        }

        PlanetEnvironmentRegionSet regionSet = AssetDatabase.LoadAssetAtPath<PlanetEnvironmentRegionSet>(RegionSetAssetPath);
        bool isNewAsset = regionSet == null;
        if (isNewAsset)
        {
            regionSet = ScriptableObject.CreateInstance<PlanetEnvironmentRegionSet>();
            AssetDatabase.CreateAsset(regionSet, RegionSetAssetPath);
        }

        var regionA = new PlanetEnvironmentRegionSet.Region
        {
            name = "Region A",
            trees = new[] { new PlanetEnvironmentRegionSet.WeightedPrefab { prefab = tree1, weight = 1f } },
            grass = new[]
            {
                new PlanetEnvironmentRegionSet.WeightedPrefab { prefab = grass3, weight = 1f },
                new PlanetEnvironmentRegionSet.WeightedPrefab { prefab = grassToadstool, weight = 1f },
            },
            rocks = new[] { new PlanetEnvironmentRegionSet.WeightedPrefab { prefab = rock3, weight = 1f } },
        };

        var regionB = new PlanetEnvironmentRegionSet.Region
        {
            name = "Region B",
            trees = new[]
            {
                new PlanetEnvironmentRegionSet.WeightedPrefab { prefab = tree3, weight = 1f },
                new PlanetEnvironmentRegionSet.WeightedPrefab { prefab = tree5, weight = 1f },
            },
            grass = new[] { new PlanetEnvironmentRegionSet.WeightedPrefab { prefab = grass2, weight = 1f } },
            rocks = new[] { new PlanetEnvironmentRegionSet.WeightedPrefab { prefab = rock1, weight = 1f } },
        };

        var regionC = new PlanetEnvironmentRegionSet.Region
        {
            name = "Region C",
            trees = new[] { new PlanetEnvironmentRegionSet.WeightedPrefab { prefab = tree2, weight = 1f } },
            grass = new[] { new PlanetEnvironmentRegionSet.WeightedPrefab { prefab = grass1, weight = 1f } },
            rocks = new[] { new PlanetEnvironmentRegionSet.WeightedPrefab { prefab = rock2, weight = 1f } },
        };

        var regionD = new PlanetEnvironmentRegionSet.Region
        {
            name = "Region D",
            trees = new[] { new PlanetEnvironmentRegionSet.WeightedPrefab { prefab = tree4, weight = 1f } },
            grass = new[] { new PlanetEnvironmentRegionSet.WeightedPrefab { prefab = hollowLog, weight = 1f } },
            rocks = new[] { new PlanetEnvironmentRegionSet.WeightedPrefab { prefab = rock4, weight = 1f } },
        };

        PlanetEnvironmentRegionSet.Region[] regionTemplates = { regionA, regionB, regionC, regionD };
        SetRegions(regionSet, regionTemplates);
        EditorUtility.SetDirty(regionSet);

        var report = new StringBuilder();
        Transform streamersRoot = NyxaraStreamersRoot.FindOrCreate();
        SphericalPlanet planet = UnityEngine.Object.FindAnyObjectByType<SphericalPlanet>();

        if (RemoveEnvironmentRegionsHierarchy())
            report.AppendLine($"Removed retired '{EnvironmentRegionsRootName}' hierarchy (Area markers are no longer used).");

        int wired = WireEnvironmentManager(streamersRoot, planet, regionSet, report);
        wired += TryAssignRegionSet<PlanetGrassStreamer>(streamersRoot, regionSet, "Grass", report);
        wired += TryAssignRegionSet<PlanetTreeStreamer>(streamersRoot, regionSet, "Tree", report);
        wired += TryAssignRegionSet<PlanetRockStreamer>(streamersRoot, regionSet, "Rock", report);
        SetGlobalStreamersEnabled(streamersRoot, enabled: true, report);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        string resultMessage = (isNewAsset ? "Created" : "Updated")
            + $" {RegionSetAssetPath} (flora streams across the whole planet via EnvironmentManager — Region A: Tree1 / Grass3+GrassLuminousToadstool / Rock3 — Region B: Tree3+Tree5 / Grass2 / Rock — Region C: Tree2 / Grass / Rock2 — Region D: Tree4 / HollowLog / Rock4) and wired {wired} component(s). CreatureSpawner was NOT modified.";
        if (report.Length > 0)
            resultMessage += "\n\n" + report.ToString().TrimEnd();

        Debug.Log($"[BackHome] {resultMessage}");
        return resultMessage;
    }

    static void SetGlobalStreamersEnabled(Transform streamersRoot, bool enabled, StringBuilder report)
    {
        SetStreamerEnabled<PlanetGrassStreamer>(streamersRoot, enabled);
        SetStreamerEnabled<PlanetTreeStreamer>(streamersRoot, enabled);
        SetStreamerEnabled<PlanetRockStreamer>(streamersRoot, enabled);

        if (enabled)
            report.AppendLine("Enabled global Grass/Tree/Rock streamers on 'EnvironmentManager'.");
        else
            report.AppendLine("Disabled global Grass/Tree/Rock streamers on 'EnvironmentManager'.");
    }

    static void SetStreamerEnabled<T>(Transform streamersRoot, bool enabled) where T : Behaviour
    {
        T streamer = streamersRoot.GetComponent<T>();
        if (streamer == null)
            return;

        streamer.enabled = enabled;
        EditorUtility.SetDirty(streamer);
    }

    static int WireEnvironmentManager(Transform streamersRoot, SphericalPlanet planet, PlanetEnvironmentRegionSet regionSet, StringBuilder report)
    {
        PlanetEnvironmentManager manager = streamersRoot.GetComponent<PlanetEnvironmentManager>();
        if (manager == null)
            manager = Undo.AddComponent<PlanetEnvironmentManager>(streamersRoot.gameObject);

        var so = new SerializedObject(manager);
        so.FindProperty("regionSet").objectReferenceValue = regionSet;
        if (planet != null)
            so.FindProperty("planet").objectReferenceValue = planet;
        so.ApplyModifiedPropertiesWithoutUndo();

        manager.ApplyConfiguration();
        EditorUtility.SetDirty(manager);
        MarkStreamerDirty<PlanetGrassStreamer>(streamersRoot);
        MarkStreamerDirty<PlanetTreeStreamer>(streamersRoot);
        MarkStreamerDirty<PlanetRockStreamer>(streamersRoot);
        report.AppendLine("Wired NyxaraEnvironmentRegions into PlanetEnvironmentManager (single regionSet reference for all streamers).");
        return 1;
    }

    static void MarkStreamerDirty<T>(Transform streamersRoot) where T : Component
    {
        T streamer = streamersRoot.GetComponent<T>();
        if (streamer != null)
            EditorUtility.SetDirty(streamer);
    }

    static int TryAssignRegionSet<T>(Transform streamersRoot, PlanetEnvironmentRegionSet regionSet, string label, StringBuilder report) where T : Component
    {
        T streamer = streamersRoot.GetComponent<T>();
        if (streamer == null)
        {
            report.AppendLine($"No {typeof(T).Name} found on 'EnvironmentManager' — run BackHome → Setup Nyxara {label} Streaming first.");
            return 0;
        }

        EditorUtility.SetDirty(streamer);
        return 1;
    }

    static void SetRegions(PlanetEnvironmentRegionSet regionSet, PlanetEnvironmentRegionSet.Region[] regions)
    {
        FieldInfo field = typeof(PlanetEnvironmentRegionSet).GetField("regions", BindingFlags.NonPublic | BindingFlags.Instance);
        field.SetValue(regionSet, regions);
    }

    /// <summary>Deletes the retired <see cref="EnvironmentRegionsRootName"/> root and all its children
    /// (hand-placed Area markers from an old streaming experiment).</summary>
    static bool RemoveEnvironmentRegionsHierarchy()
    {
        Scene scene = SceneManager.GetActiveScene();
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
        {
            if (!string.Equals(roots[i].name, EnvironmentRegionsRootName, StringComparison.Ordinal))
                continue;

            Undo.DestroyObjectImmediate(roots[i]);
            return true;
        }

        return false;
    }

    static void OpenSceneIfNeeded()
    {
        if (!ScenePath.Equals(SceneManager.GetActiveScene().path, StringComparison.Ordinal))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }
}
