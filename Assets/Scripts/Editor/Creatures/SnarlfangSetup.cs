using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds Snarlfang: CasualToon material, creature components, A1 spawn,
/// and a 50/50 Idle1/Idle2 animator.
/// </summary>
public static class SnarlfangSetup
{
    const string ModelPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang/Models/Snarlfang_Animation_Idel1.fbx";
    const string Idle2ModelPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang/Models/Snarlfang_Animation_Idel2.fbx";
    const string Idle1ClipPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang/Animations/Idle1.anim";
    const string Idle2ClipPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang/Animations/Idle2.anim";
    const string ControllerPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang/Animations/Snarlfang.controller";
    const string AlbedoPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang/Textures&Materials/Meshy_AI_Verdant_Crystal_Eleme_biped_texture_0.png";
    const string NormalPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang/Textures&Materials/Meshy_AI_Verdant_Crystal_Eleme_biped_texture_0_normal.png";
    const string MetallicPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang/Textures&Materials/Meshy_AI_Verdant_Crystal_Eleme_biped_texture_0_metallic.png";
    const string RoughnessPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang/Textures&Materials/Meshy_AI_Verdant_Crystal_Eleme_biped_texture_0_roughness.png";
    const string MaterialPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang/Materials/Snarlfang.mat";
    const string StatsPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang/SnarlfangStats.asset";
    const string GrimlingStatsPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Grimling/GrimlingStats.asset";
    const string PrefabPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang/Snarlfang.prefab";
    const string VitalsPrefabPath = "Assets/Resources/HUD/VitalsBars/CreaturesVitalsBars.prefab";
    const string DamagePopupPath = "Assets/Resources/HUD/DamageNumber.prefab";
    const string HitFlashPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Shaders/HitFlash.mat";
    const string AreaId = "A1";
    const int SpawnCount = 4;
    const float TargetHeight = 1.8f;

    static readonly string TriggerPath = Path.Combine(Application.dataPath, "..", "Temp", "BackHomeSetupSnarlfang.trigger");
    static readonly string IdleTriggerPath = Path.Combine(Application.dataPath, "..", "Temp", "BackHomeSetupSnarlfangIdle.trigger");
    static readonly string ResultPath = Path.Combine(Application.dataPath, "..", "Temp", "SnarlfangSetup.result.txt");

    static bool _autoStarted;

    [InitializeOnLoadMethod]
    static void Boot()
    {
        EditorApplication.update += WatchTrigger;
        EditorApplication.update += WatchIdleTrigger;
        EditorApplication.delayCall += AutoSetup;
    }

    static void WatchTrigger()
    {
        if (!File.Exists(TriggerPath))
            return;

        try { File.Delete(TriggerPath); } catch { /* ignore */ }
        Setup(force: true);
    }

    static void WatchIdleTrigger()
    {
        if (!File.Exists(IdleTriggerPath))
            return;

        try { File.Delete(IdleTriggerPath); } catch { /* ignore */ }
        SetupIdles(force: true);
    }

    static void AutoSetup()
    {
        if (_autoStarted || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += AutoSetup;
            return;
        }

        _autoStarted = true;
        Setup(force: false);
        SetupIdles(force: false);
    }

    [MenuItem("BackHome/Creatures/Setup Snarlfang Idle")]
    public static void SetupIdlesFromMenu()
    {
        SetupIdles(force: true);
    }

    [MenuItem("BackHome/Creatures/Setup Snarlfang")]
    public static void SetupFromMenu()
    {
        Setup(force: true);
    }

    static void Setup(bool force)
    {
        try
        {
            bool hasPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
            bool spawned = HasSpawnEntry();
            if (!force && hasPrefab && spawned)
            {
                WriteResult("already set up");
                return;
            }

            EnsureTextures();
            Material material = CreateMaterial();
            CreatureStats stats = CreateStats();

            if (force || !hasPrefab)
                BuildPrefab(material, stats);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            EnsureSpawn(prefab);
            AssetDatabase.SaveAssets();
            WriteResult("ok");
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            WriteResult("FAILED: " + e);
        }
    }

    static void EnsureTextures()
    {
        EnsureNormalMap(NormalPath);
        EnsureLinearData(MetallicPath);
        EnsureLinearData(RoughnessPath);
    }

    static void EnsureNormalMap(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null || importer.textureType == TextureImporterType.NormalMap)
            return;

        importer.textureType = TextureImporterType.NormalMap;
        importer.SaveAndReimport();
    }

    static void EnsureLinearData(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null || !importer.sRGBTexture)
            return;

        importer.sRGBTexture = false;
        importer.SaveAndReimport();
    }

    static Material CreateMaterial()
    {
        EnsureFolder("Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang", "Materials");

        Shader shader = Shader.Find("BackHome/CasualToon");
        if (shader == null)
            throw new System.InvalidOperationException("BackHome/CasualToon shader was not found.");

        var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath);
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath);
        if (albedo == null)
            throw new System.InvalidOperationException("Snarlfang albedo texture was not found.");

        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(shader) { name = "Snarlfang" };
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        else
        {
            material.shader = shader;
        }

        material.SetTexture("_BaseMap", albedo);
        material.SetTexture("_BumpMap", normal);
        material.SetFloat("_BumpScale", 1f);
        material.SetFloat("_Cull", 2f);
        material.shaderKeywords = System.Array.Empty<string>();
        EditorUtility.SetDirty(material);
        return material;
    }

    static CreatureStats CreateStats()
    {
        if (AssetDatabase.LoadAssetAtPath<CreatureStats>(StatsPath) == null)
        {
            if (!AssetDatabase.CopyAsset(GrimlingStatsPath, StatsPath))
                throw new System.InvalidOperationException("Could not copy Grimling stats for Snarlfang.");
        }

        var stats = AssetDatabase.LoadAssetAtPath<CreatureStats>(StatsPath);
        var so = new SerializedObject(stats);
        so.FindProperty("displayName").stringValue = "Snarlfang";
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(stats);
        return stats;
    }

    static void BuildPrefab(Material material, CreatureStats stats)
    {
        var importer = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
        if (importer == null)
            throw new System.InvalidOperationException("Snarlfang idle FBX was not found.");

        bool reimport = false;
        if (importer.animationType != ModelImporterAnimationType.Generic)
        {
            importer.animationType = ModelImporterAnimationType.Generic;
            reimport = true;
        }

        if (!importer.importAnimation)
        {
            importer.importAnimation = true;
            reimport = true;
        }

        if (importer.importCameras || importer.importLights)
        {
            importer.importCameras = false;
            importer.importLights = false;
            reimport = true;
        }

        if (reimport)
            importer.SaveAndReimport();

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (model == null)
            throw new System.InvalidOperationException("Could not load Snarlfang idle model.");

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
        try
        {
            instance.name = "Snarlfang";
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            AnimationClip clip = AssetDatabase.LoadAllAssetsAtPath(ModelPath)
                .OfType<AnimationClip>()
                .Where(c => c != null && !c.name.StartsWith("__preview"))
                .OrderByDescending(c => c.length)
                .FirstOrDefault();

            if (clip != null)
                clip.SampleAnimation(instance, clip.length * 0.5f);

            foreach (Animator animator in instance.GetComponentsInChildren<Animator>(true))
                Object.DestroyImmediate(animator);

            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                var slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                    slots[i] = material;
                if (slots.Length == 0)
                    slots = new[] { material };
                renderer.sharedMaterials = slots;
            }

            float height = MeasureHeight(instance);
            if (height > 0.05f && (height < 0.8f || height > 3.2f))
            {
                float factor = TargetHeight / height;
                instance.transform.localScale = Vector3.one * factor;
                height = MeasureHeight(instance);
            }

            float barHeight = height > 0.05f ? height + 0.25f : 2.2f;
            AddCreatureComponents(instance, stats, barHeight);

            PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            Debug.Log($"[BackHome] Snarlfang prefab saved. clip={(clip != null ? clip.name : "none")} height={height:0.00}");
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    static float MeasureHeight(GameObject instance)
    {
        float top = float.NegativeInfinity;
        float bottom = float.PositiveInfinity;
        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer is not SkinnedMeshRenderer && renderer is not MeshRenderer)
                continue;

            top = Mathf.Max(top, renderer.bounds.max.y);
            bottom = Mathf.Min(bottom, renderer.bounds.min.y);
        }

        if (float.IsNegativeInfinity(top) || float.IsPositiveInfinity(bottom))
            return 0f;

        return top - bottom;
    }

    static void AddCreatureComponents(GameObject instance, CreatureStats stats, float barHeight)
    {
        Creature creature = instance.GetComponent<Creature>();
        if (creature == null)
            creature = instance.AddComponent<Creature>();

        var creatureSo = new SerializedObject(creature);
        creatureSo.FindProperty("stats").objectReferenceValue = stats;
        creatureSo.FindProperty("destroyDelay").floatValue = 0.1f;
        creatureSo.ApplyModifiedPropertiesWithoutUndo();

        VitalsBars bars = instance.GetComponent<VitalsBars>();
        if (bars == null)
            bars = instance.AddComponent<VitalsBars>();

        var barsPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VitalsPrefabPath);
        var barsSo = new SerializedObject(bars);
        barsSo.FindProperty("vitalsBarsPrefab").objectReferenceValue =
            barsPrefab != null ? barsPrefab.GetComponent<VitalsBarsView>() : null;
        barsSo.FindProperty("localOffset").vector3Value = new Vector3(0f, barHeight, 0f);
        barsSo.FindProperty("worldScale").floatValue = 1.6f;
        barsSo.FindProperty("hideWhenDead").boolValue = true;
        barsSo.FindProperty("hideUntilDamaged").boolValue = true;
        barsSo.FindProperty("visibleAfterDamage").floatValue = 2.5f;
        barsSo.FindProperty("fadeDuration").floatValue = 0.2f;
        barsSo.ApplyModifiedPropertiesWithoutUndo();

        if (instance.GetComponent<CreatureRangeCombat>() == null)
            instance.AddComponent<CreatureRangeCombat>();

        DamageNumbers numbers = instance.GetComponent<DamageNumbers>();
        if (numbers == null)
            numbers = instance.AddComponent<DamageNumbers>();

        var popupPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DamagePopupPath);
        var numbersSo = new SerializedObject(numbers);
        numbersSo.FindProperty("popupPrefab").objectReferenceValue =
            popupPrefab != null ? popupPrefab.GetComponent<DamagePopup>() : null;
        numbersSo.FindProperty("localOffset").vector3Value = new Vector3(0f, barHeight + 0.2f, 0f);
        numbersSo.ApplyModifiedPropertiesWithoutUndo();

        CreatureChase chase = instance.GetComponent<CreatureChase>();
        if (chase == null)
            chase = instance.AddComponent<CreatureChase>();

        var chaseSo = new SerializedObject(chase);
        chaseSo.FindProperty("moveSpeed").floatValue = 4f;
        chaseSo.FindProperty("alignSpeed").floatValue = 10f;
        chaseSo.FindProperty("footOffset").floatValue = 0.05f;
        chaseSo.FindProperty("groundProbeDistance").floatValue = 12f;
        chaseSo.FindProperty("groundLayer").intValue = 1 << 3;
        chaseSo.FindProperty("loseVisionDelay").floatValue = 3f;
        chaseSo.FindProperty("homeArriveDistance").floatValue = 0.35f;
        chaseSo.FindProperty("knockbackDistance").floatValue = 0.2f;
        chaseSo.FindProperty("knockbackDuration").floatValue = 0.16f;
        chaseSo.FindProperty("knockbackCooldown").floatValue = 0.5f;
        chaseSo.ApplyModifiedPropertiesWithoutUndo();

        CreatureHitFlash flash = instance.GetComponent<CreatureHitFlash>();
        if (flash == null)
            flash = instance.AddComponent<CreatureHitFlash>();

        var flashSo = new SerializedObject(flash);
        flashSo.FindProperty("overlayMaterial").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<Material>(HitFlashPath);
        flashSo.FindProperty("hitColor").colorValue = Color.white;
        flashSo.FindProperty("flashDuration").floatValue = 0.1f;
        flashSo.ApplyModifiedPropertiesWithoutUndo();
    }

    static bool HasSpawnEntry()
    {
        CreatureSpawner spawner = Object.FindAnyObjectByType<CreatureSpawner>(FindObjectsInactive.Include);
        if (spawner == null)
            return false;

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
            return false;

        var so = new SerializedObject(spawner);
        SerializedProperty points = so.FindProperty("spawnPoints");
        for (int i = 0; i < points.arraySize; i++)
        {
            SerializedProperty point = points.GetArrayElementAtIndex(i);
            if (point.FindPropertyRelative("areaId").stringValue != AreaId)
                continue;

            SerializedProperty creatures = point.FindPropertyRelative("creatures");
            for (int c = 0; c < creatures.arraySize; c++)
            {
                if (creatures.GetArrayElementAtIndex(c).FindPropertyRelative("prefab").objectReferenceValue == prefab)
                    return true;
            }
        }

        return false;
    }

    static void EnsureSpawn(GameObject prefab)
    {
        if (prefab == null)
            throw new System.InvalidOperationException("Snarlfang prefab is missing.");

        CreatureSpawner spawner = Object.FindAnyObjectByType<CreatureSpawner>(FindObjectsInactive.Include);
        if (spawner == null)
            throw new System.InvalidOperationException("CreatureSpawner was not found in the open scene.");

        var so = new SerializedObject(spawner);
        SerializedProperty points = so.FindProperty("spawnPoints");
        SerializedProperty a1 = null;
        for (int i = 0; i < points.arraySize; i++)
        {
            SerializedProperty point = points.GetArrayElementAtIndex(i);
            if (point.FindPropertyRelative("areaId").stringValue == AreaId)
            {
                a1 = point;
                break;
            }
        }

        if (a1 == null)
            throw new System.InvalidOperationException("Spawn point A1 was not found.");

        SerializedProperty creatures = a1.FindPropertyRelative("creatures");
        for (int c = 0; c < creatures.arraySize; c++)
        {
            SerializedProperty existing = creatures.GetArrayElementAtIndex(c);
            if (existing.FindPropertyRelative("prefab").objectReferenceValue == prefab)
            {
                existing.FindPropertyRelative("count").intValue = SpawnCount;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(spawner.gameObject.scene);
                EditorSceneManager.SaveScene(spawner.gameObject.scene);
                return;
            }
        }

        int index = creatures.arraySize;
        creatures.InsertArrayElementAtIndex(index);
        SerializedProperty entry = creatures.GetArrayElementAtIndex(index);
        entry.FindPropertyRelative("prefab").objectReferenceValue = prefab;
        entry.FindPropertyRelative("count").intValue = SpawnCount;
        entry.FindPropertyRelative("respawnTime").floatValue = 4f;
        entry.FindPropertyRelative("minSeparationDegrees").floatValue = 1.5f;
        so.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(spawner.gameObject.scene);
        EditorSceneManager.SaveScene(spawner.gameObject.scene);
    }

    static void SetupIdles(bool force)
    {
        try
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            bool wired = controller != null && prefab != null && prefab.GetComponent<Animator>() != null
                && prefab.GetComponent<Animator>().runtimeAnimatorController == controller;
            if (!force && wired)
            {
                WriteResult("idle already set up");
                return;
            }

            AnimationClip idle1 = ExtractLoopingClip(ModelPath, "Idle1", Idle1ClipPath);
            AnimationClip idle2 = ExtractLoopingClip(Idle2ModelPath, "Idle2", Idle2ClipPath);
            controller = CreateIdleController(idle1, idle2);
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
            WireAnimator(controller, avatar);

            AssetDatabase.SaveAssets();
            string paths = string.Join(" | ", SamplePaths(idle1).Take(4));
            WriteResult($"idle ok. layers={controller.layers.Length} states={controller.layers[0].stateMachine.states.Length} idle1={idle1.length:0.00}s idle2={idle2.length:0.00}s paths={paths}");
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            WriteResult("IDLE FAILED: " + e);
        }
    }

    static AnimationClip ExtractLoopingClip(string modelPath, string clipName, string outputPath)
    {
        var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        if (importer == null)
            throw new System.InvalidOperationException("Missing model at " + modelPath);

        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.optimizeGameObjects = false;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.importAnimation = true;
        importer.SaveAndReimport();

        importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        ModelImporterClipAnimation[] takes = importer.importedTakeInfos == null
            ? null
            : importer.importedTakeInfos.Select(take => new ModelImporterClipAnimation
            {
                name = clipName,
                takeName = take.name,
                firstFrame = take.startTime * take.sampleRate,
                lastFrame = take.stopTime * take.sampleRate,
                loopTime = true,
                loopPose = false,
                wrapMode = WrapMode.Loop
            }).Take(1).ToArray();

        if (takes == null || takes.Length == 0)
            throw new System.InvalidOperationException(modelPath + " has no animation take.");

        importer.clipAnimations = takes;
        importer.SaveAndReimport();

        AnimationClip source = AssetDatabase.LoadAllAssetsAtPath(modelPath)
            .OfType<AnimationClip>()
            .FirstOrDefault(c => c.name == clipName && !c.name.StartsWith("__preview"));
        if (source == null)
            throw new System.InvalidOperationException("Could not read clip " + clipName + " from " + modelPath);

        return BakeInPlaceClip(source, outputPath);
    }

    static AnimationClip BakeInPlaceClip(AnimationClip source, string outputPath)
    {
        var clip = new AnimationClip { name = Path.GetFileNameWithoutExtension(outputPath) };
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(source);
        settings.loopTime = true;
        settings.loopBlend = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        foreach (EditorCurveBinding binding in AnimationUtility.GetCurveBindings(source))
        {
            if (binding.type == typeof(Transform) && binding.propertyName.StartsWith("m_LocalScale."))
                continue;

            string path = RemapBonePath(binding.path);
            if (path == "target_character")
                continue;

            AnimationCurve curve = AnimationUtility.GetEditorCurve(source, binding);
            bool lockHips = path.EndsWith("Hips", System.StringComparison.Ordinal)
                && (binding.propertyName == "m_LocalPosition.x" || binding.propertyName == "m_LocalPosition.z");
            if (lockHips && curve.keys.Length > 0)
            {
                float locked = curve.keys[0].value;
                curve = AnimationCurve.Constant(curve.keys[0].time, curve.keys[curve.keys.Length - 1].time, locked);
            }

            var remapped = binding;
            remapped.path = path;
            AnimationUtility.SetEditorCurve(clip, remapped, curve);
        }

        EnsureFolder("Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang", "Animations");
        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(outputPath) != null)
            AssetDatabase.DeleteAsset(outputPath);
        AssetDatabase.CreateAsset(clip, outputPath);
        return clip;
    }

    static string RemapBonePath(string path)
    {
        if (string.IsNullOrEmpty(path))
            return path;

        const string liveRoot = "target_character";
        int slash = path.IndexOf('/');
        string head = slash < 0 ? path : path.Substring(0, slash);
        if (head == liveRoot)
            return path;
        if (head.StartsWith("mixamorig:", System.StringComparison.Ordinal))
            return liveRoot + "/" + path;
        if (slash >= 0)
            return liveRoot + path.Substring(slash);
        return path;
    }

    static IEnumerable<string> SamplePaths(AnimationClip clip)
    {
        return AnimationUtility.GetCurveBindings(clip).Select(b => b.path).Distinct();
    }

    static AnimatorController CreateIdleController(AnimationClip idle1, AnimationClip idle2)
    {
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath) != null)
            AssetDatabase.DeleteAsset(ControllerPath);

        AnimatorController controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        AnimatorStateMachine machine = controller.layers[0].stateMachine;

        AnimatorState state1 = machine.AddState("Idle1", new Vector3(280, 0, 0));
        state1.motion = idle1;
        AnimatorState state2 = machine.AddState("Idle2", new Vector3(280, 140, 0));
        state2.motion = idle2;
        machine.defaultState = state1;

        const float crossfade = 0.25f;
        AnimatorStateTransition to2 = state1.AddTransition(state2);
        to2.hasExitTime = true;
        to2.exitTime = 1f;
        to2.hasFixedDuration = true;
        to2.duration = crossfade;
        AnimatorStateTransition to1 = state2.AddTransition(state1);
        to1.hasExitTime = true;
        to1.exitTime = 1f;
        to1.hasFixedDuration = true;
        to1.duration = crossfade;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    static void WireAnimator(AnimatorController controller, Avatar avatar)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Animator animator = root.GetComponent<Animator>();
            if (animator == null)
                animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.avatar = avatar;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            if (root.GetComponent<CreatureAnimator>() == null)
                root.AddComponent<CreatureAnimator>();

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void EnsureFolder(string parent, string child)
    {
        string folder = parent + "/" + child;
        if (AssetDatabase.IsValidFolder(folder))
            return;

        AssetDatabase.CreateFolder(parent, child);
    }

    static void WriteResult(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ResultPath));
            File.WriteAllText(ResultPath, message);
        }
        catch { /* ignore */ }

        Debug.Log("[BackHome] Snarlfang setup: " + message);
    }
}
