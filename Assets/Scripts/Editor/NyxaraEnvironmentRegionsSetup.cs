using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor tooling for Nyxara's region-based environment: creates/updates the example
/// <see cref="PlanetEnvironmentRegionSet"/> asset (the two starter regions from the original
/// request) and wires it into the Grass/Tree/Rock streamers — and <see cref="CreatureSpawner"/>,
/// if one exists in the scene — on the "Streamers" root.
///
/// Region A: Tree1 / Grass3 + Grass_Luminous_Toadstool / Rock3 — plus a dense Grimling pocket
/// (<see cref="DenseRegionCreatureCount"/>). Every scene marker whose name starts with
/// <see cref="SpawnPointNamePrefix"/> (e.g. "Spawn Point", "Spawn Point (1)") gets its own tight
/// cluster (<see cref="CreatureSpawner.SpawnPoint"/>, <see cref="DenseSpawnPointRadius"/>); if none
/// exist, it falls back to spawning anywhere within Region A's procedural area.
/// Region B: Tree3 + Tree5 / Grass2 / Rock — no region-specific creatures.
/// The CreatureSpawner's old flat, planet-wide spawnEntries row is cleared — Grimlings now spawn
/// exclusively from the region/spawn-point pocket above, not planet-wide too.
///
/// Menu: BackHome → Setup Nyxara Environment Regions (Recommended)
/// </summary>
public static class NyxaraEnvironmentRegionsSetup
{
    const string ScenePath = "Assets/Scenes/PlanetNyxara.unity";
    const string EnvironmentFolder = "Assets/Galaxy/Planets/Nyxara/Environment";
    const string RegionSetAssetPath = EnvironmentFolder + "/NyxaraEnvironmentRegions.asset";

    const string TreesFolder = EnvironmentFolder + "/Trees";
    const string GrassFolder = EnvironmentFolder + "/Grass";
    const string RocksFolder = EnvironmentFolder + "/Rock";
    const string CreaturesFolder = "Assets/Galaxy/Planets/Nyxara/Creatures";

    const string Tree1PrefabPath = TreesFolder + "/Tree1.prefab";
    const string Tree3PrefabPath = TreesFolder + "/Tree3.prefab";
    const string Tree5PrefabPath = TreesFolder + "/Tree5.prefab";
    const string Grass2PrefabPath = GrassFolder + "/Grass2.prefab";
    const string Grass3PrefabPath = GrassFolder + "/Grass3.prefab";
    const string GrassLuminousToadstoolPrefabPath = GrassFolder + "/Grass_Luminous_Toadstool.prefab";
    const string Rock1PrefabPath = RocksFolder + "/Rock.prefab";
    const string Rock3PrefabPath = RocksFolder + "/Rock3.prefab";
    const string GrimlingPrefabPath = CreaturesFolder + "/Grimling/Prefabs/Grimling.prefab";
    const string GrimlingLootDropPrefabPath = CreaturesFolder + "/Grimling/Loot/GrimlingLootDrop.prefab";

    /// <summary>Grimlings confined to the region/spawn-point pocket (the CreatureSpawner's old
    /// flat, planet-wide spawnEntries row is cleared) — makes it read as a single, distinctly dense
    /// "Grimling Den" (packed shoulder-to-shoulder, reference: mobile-game mob clusters) instead of
    /// scattered thinly across the whole planet.</summary>
    const int DenseRegionCreatureCount = 220;
    const float DenseRegionRespawnSeconds = 4f;
    const float DenseRegionMinSeparationDegrees = 1.5f;

    /// <summary>Scene markers whose Transform name starts with this prefix each receive their own
    /// dense Grimling cluster (e.g. "Spawn Point", "Spawn Point (1)", "Spawn Point (2)").</summary>
    const string SpawnPointNamePrefix = "Spawn Point";
    const float DenseSpawnPointRadius = 12f;

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
        GameObject tree3 = AssetDatabase.LoadAssetAtPath<GameObject>(Tree3PrefabPath);
        GameObject tree5 = AssetDatabase.LoadAssetAtPath<GameObject>(Tree5PrefabPath);
        GameObject grass2 = AssetDatabase.LoadAssetAtPath<GameObject>(Grass2PrefabPath);
        GameObject grass3 = AssetDatabase.LoadAssetAtPath<GameObject>(Grass3PrefabPath);
        GameObject grassToadstool = AssetDatabase.LoadAssetAtPath<GameObject>(GrassLuminousToadstoolPrefabPath);
        GameObject rock1 = AssetDatabase.LoadAssetAtPath<GameObject>(Rock1PrefabPath);
        GameObject rock3 = AssetDatabase.LoadAssetAtPath<GameObject>(Rock3PrefabPath);
        GameObject grimling = AssetDatabase.LoadAssetAtPath<GameObject>(GrimlingPrefabPath);
        GameObject grimlingLoot = AssetDatabase.LoadAssetAtPath<GameObject>(GrimlingLootDropPrefabPath);

        if (tree1 == null || tree3 == null || tree5 == null
            || grass2 == null || grass3 == null || grassToadstool == null
            || rock1 == null || rock3 == null || grimling == null)
        {
            string missingMessage =
                "Missing prefab(s).\n"
                + $"Tree1: {(tree1 != null ? "OK" : "MISSING")}\n"
                + $"Tree3: {(tree3 != null ? "OK" : "MISSING")}\n"
                + $"Tree5: {(tree5 != null ? "OK" : "MISSING")}\n"
                + $"Grass2: {(grass2 != null ? "OK" : "MISSING")}\n"
                + $"Grass3: {(grass3 != null ? "OK" : "MISSING")}\n"
                + $"Grass_Luminous_Toadstool: {(grassToadstool != null ? "OK" : "MISSING")}\n"
                + $"Rock: {(rock1 != null ? "OK" : "MISSING")}\n"
                + $"Rock3: {(rock3 != null ? "OK" : "MISSING")}\n"
                + $"Grimling: {(grimling != null ? "OK" : "MISSING")}";
            Debug.LogError($"[BackHome] Environment Regions: {missingMessage}");
            return missingMessage;
        }

        if (grimlingLoot == null)
            Debug.LogWarning($"[BackHome] Environment Regions: GrimlingLootDrop prefab not found at {GrimlingLootDropPrefabPath} — the dense Region A pocket will spawn without a loot drop.");

        PlanetEnvironmentRegionSet regionSet = AssetDatabase.LoadAssetAtPath<PlanetEnvironmentRegionSet>(RegionSetAssetPath);
        bool isNewAsset = regionSet == null;
        if (isNewAsset)
        {
            regionSet = ScriptableObject.CreateInstance<PlanetEnvironmentRegionSet>();
            AssetDatabase.CreateAsset(regionSet, RegionSetAssetPath);
        }

        List<Transform> spawnPointAnchors = FindSpawnPointAnchors();
        bool useSpawnPoints = spawnPointAnchors.Count > 0;

        var denseGrimlingEntry = new CreatureSpawner.SpawnEntry
        {
            prefab = grimling,
            count = DenseRegionCreatureCount,
            respawnTime = DenseRegionRespawnSeconds,
            lootDropPrefab = grimlingLoot,
            minSeparationDegrees = DenseRegionMinSeparationDegrees,
        };

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
            // Prefer hand-placed spawn-point anchors when any exist — falls back to spawning
            // anywhere within Region A's (much larger) procedural area otherwise.
            creatures = useSpawnPoints ? Array.Empty<CreatureSpawner.SpawnEntry>() : new[] { denseGrimlingEntry },
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
            creatures = Array.Empty<CreatureSpawner.SpawnEntry>(),
        };

        SetRegions(regionSet, new[] { regionA, regionB });
        EditorUtility.SetDirty(regionSet);

        var report = new StringBuilder();
        Transform streamersRoot = NyxaraStreamersRoot.FindOrCreate();

        int wired = 0;
        wired += TryAssignRegionSet<PlanetGrassStreamer>(streamersRoot, regionSet, "Grass", report);
        wired += TryAssignRegionSet<PlanetTreeStreamer>(streamersRoot, regionSet, "Tree", report);
        wired += TryAssignRegionSet<PlanetRockStreamer>(streamersRoot, regionSet, "Rock", report);

        CreatureSpawner creatureSpawner = UnityEngine.Object.FindAnyObjectByType<CreatureSpawner>();
        if (creatureSpawner != null)
        {
            var so = new SerializedObject(creatureSpawner);
            so.FindProperty("regionSet").objectReferenceValue = regionSet;

            // Grimlings now come exclusively from the region/spawn-point pocket below — clear the
            // old flat, planet-wide spawnEntries row so it's not spawning an extra, undesired batch.
            SerializedProperty spawnEntriesProp = so.FindProperty("spawnEntries");
            int clearedSpawnEntries = spawnEntriesProp.arraySize;
            spawnEntriesProp.ClearArray();

            so.ApplyModifiedPropertiesWithoutUndo();

            if (useSpawnPoints)
            {
                var points = new CreatureSpawner.SpawnPoint[spawnPointAnchors.Count];
                for (int i = 0; i < spawnPointAnchors.Count; i++)
                {
                    points[i] = new CreatureSpawner.SpawnPoint
                    {
                        anchor = spawnPointAnchors[i],
                        radius = DenseSpawnPointRadius,
                        creatures = new[] { denseGrimlingEntry },
                    };
                }

                SetSpawnPoints(creatureSpawner, points);
                report.AppendLine(
                    $"Found {spawnPointAnchors.Count} '{SpawnPointNamePrefix}*' marker(s) — "
                    + $"{DenseRegionCreatureCount} dense Grimlings each @ {DenseSpawnPointRadius:0}-unit radius "
                    + $"({DenseRegionMinSeparationDegrees}° spacing).");
            }

            if (clearedSpawnEntries > 0)
                report.AppendLine($"Cleared {clearedSpawnEntries} planet-wide spawnEntries row(s) — Grimlings now only come from the region/spawn-point pocket.");

            EditorUtility.SetDirty(creatureSpawner);
            wired++;
        }
        else
        {
            report.AppendLine("No CreatureSpawner found in the scene — skipped (both example regions start with an empty creature list anyway).");
        }

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        AssetDatabase.SaveAssets();

        string denseDescription = useSpawnPoints
            ? $"{spawnPointAnchors.Count}×{DenseRegionCreatureCount} Grimlings packed @ {DenseRegionMinSeparationDegrees}° spacing (one cluster per '{SpawnPointNamePrefix}*' marker)"
            : $"+{DenseRegionCreatureCount} tightly-packed Grimlings @ {DenseRegionMinSeparationDegrees}° spacing in Region A";
        string resultMessage = (isNewAsset ? "Created" : "Updated")
            + $" {RegionSetAssetPath} (Region A: Tree1 / Grass3+Grass_Luminous_Toadstool / Rock3 / {denseDescription} — Region B: Tree3+Tree5 / Grass2 / Rock) and wired it into {wired} component(s).";
        if (report.Length > 0)
            resultMessage += "\n\n" + report.ToString().TrimEnd();

        Debug.Log($"[BackHome] {resultMessage}");
        return resultMessage;
    }

    static int TryAssignRegionSet<T>(Transform streamersRoot, PlanetEnvironmentRegionSet regionSet, string label, StringBuilder report) where T : Component
    {
        T streamer = streamersRoot.GetComponent<T>();
        if (streamer == null)
        {
            report.AppendLine($"No {typeof(T).Name} found on 'Streamers' — run BackHome → Setup Nyxara {label} Streaming first.");
            return 0;
        }

        var so = new SerializedObject(streamer);
        so.FindProperty("regionSet").objectReferenceValue = regionSet;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(streamer);
        return 1;
    }

    static void SetRegions(PlanetEnvironmentRegionSet regionSet, PlanetEnvironmentRegionSet.Region[] regions)
    {
        FieldInfo field = typeof(PlanetEnvironmentRegionSet).GetField("regions", BindingFlags.NonPublic | BindingFlags.Instance);
        field.SetValue(regionSet, regions);
    }

    static void SetSpawnPoints(CreatureSpawner spawner, CreatureSpawner.SpawnPoint[] spawnPoints)
    {
        FieldInfo field = typeof(CreatureSpawner).GetField("spawnPoints", BindingFlags.NonPublic | BindingFlags.Instance);
        field.SetValue(spawner, spawnPoints);
    }

    /// <summary>Collects every Transform in the active scene whose name starts with
    /// <see cref="SpawnPointNamePrefix"/> (depth-first, stable name sort).</summary>
    static List<Transform> FindSpawnPointAnchors()
    {
        var results = new List<Transform>();
        Scene scene = SceneManager.GetActiveScene();
        GameObject[] roots = scene.GetRootGameObjects();
        for (int i = 0; i < roots.Length; i++)
            CollectSpawnPointAnchors(roots[i].transform, results);

        results.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));
        return results;
    }

    static void CollectSpawnPointAnchors(Transform t, List<Transform> results)
    {
        if (t.name.StartsWith(SpawnPointNamePrefix, StringComparison.Ordinal))
            results.Add(t);

        for (int i = 0; i < t.childCount; i++)
            CollectSpawnPointAnchors(t.GetChild(i), results);
    }

    static void OpenSceneIfNeeded()
    {
        if (!ScenePath.Equals(SceneManager.GetActiveScene().path, StringComparison.Ordinal))
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }
}
