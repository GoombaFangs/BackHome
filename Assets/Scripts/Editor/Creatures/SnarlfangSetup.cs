using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Builds Snarlfang: CasualToon material, creature components, A1 spawn,
/// a 50/50 Idle1/Idle2 animator, and a Run state driven by IsMoving.
/// </summary>
public static class SnarlfangSetup
{
    const string ModelPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang/Models/Snarlfang_Animation_Idel1.fbx";
    const string Idle2ModelPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang/Models/Snarlfang_Animation_Idel2.fbx";
    const string RunningModelPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang/Models/Snarlfang_Animation_Running.fbx";
    const string Idle1ClipPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang/Animations/Idle1.anim";
    const string Idle2ClipPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang/Animations/Idle2.anim";
    const string RunningClipPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang/Animations/Running.anim";
    const string LightModelPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang/Models/Snarlfang_Animation_Light_Attack.fbx";
    const string HeavyModelPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang/Models/Snarlfang_Animation_Heavy_Attack.fbx";
    const string LightClipPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang/Animations/LightAttack.anim";
    const string HeavyClipPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang/Animations/HeavyAttack.anim";
    const string DeathModelPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang/Models/Snarlfang_Animation_Death.fbx";
    const string DeathClipPath = "Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang/Animations/Death.anim";
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
    static readonly string DiagTriggerPath = Path.Combine(Application.dataPath, "..", "Temp", "BackHomeSnarlfangDiag.trigger");
    static readonly string DiagPath = Path.Combine(Application.dataPath, "..", "Temp", "SnarlfangDiag.txt");

    static bool _autoStarted;

    [InitializeOnLoadMethod]
    static void Boot()
    {
        EditorApplication.update += WatchTrigger;
        EditorApplication.update += WatchIdleTrigger;
        EditorApplication.delayCall += AutoSetup;
        // A refresh/reload may be the only editor tick we get. Run a pending setup
        // immediately instead of waiting for a later update that never arrives.
        if (!EditorApplication.isCompiling && !EditorApplication.isUpdating && !EditorApplication.isPlayingOrWillChangePlaymode)
            TryRunPendingSetup();
    }

    public static void TryRunPendingSetup()
    {
        if (File.Exists(IdleTriggerPath))
        {
            try
            {
                File.Delete(IdleTriggerPath);
                SetupIdles(force: true);
            }
            catch { /* ignore */ }
        }

        if (File.Exists(DiagTriggerPath))
        {
            try { File.Delete(DiagTriggerPath); } catch { /* ignore */ }
            Diagnose();
        }
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
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        TryRunPendingSetup();
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
        EnsureAttacks();
        EnsureDeath();
    }

    static void Diagnose()
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            var direct = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Object mainAsset = AssetDatabase.LoadMainAssetAtPath(ControllerPath);
            sb.AppendLine($"direct={(direct != null ? direct.name : "null")} main={(mainAsset != null ? mainAsset.GetType().Name : "null")} guid={AssetDatabase.AssetPathToGUID(ControllerPath)} subs={AssetDatabase.LoadAllAssetsAtPath(ControllerPath).Length}");

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Animator pa = prefab != null ? prefab.GetComponent<Animator>() : null;
            if (pa != null)
            {
                var so = new SerializedObject(pa);
                SerializedProperty ctrl = so.FindProperty("m_Controller");
                sb.AppendLine($"serialized m_Controller={(ctrl != null && ctrl.objectReferenceValue != null ? ctrl.objectReferenceValue.name : "null")}");
            }
            sb.AppendLine($"prefab={(prefab != null)} animator={(pa != null)} enabled={(pa != null && pa.enabled)}");
            if (pa != null)
            {
                Avatar av = pa.avatar;
                sb.AppendLine($"avatar={(av != null ? av.name : "null")} valid={(av != null && av.isValid)} human={(av != null && av.isHuman)}");
                RuntimeAnimatorController rc = pa.runtimeAnimatorController;
                sb.AppendLine($"controller={(rc != null ? rc.name : "null")} clips={(rc != null ? string.Join(",", rc.animationClips.Select(c => c != null ? c.name + ":" + c.length.ToString("0.00") : "null")) : "-")}");
                if (rc is AnimatorController ac)
                {
                    sb.AppendLine($"params={string.Join(",", ac.parameters.Select(p => p.name))} layers={ac.layers.Length}");
                    foreach (ChildAnimatorState s in ac.layers[0].stateMachine.states)
                        sb.AppendLine($"  state {s.state.name} motion={(s.state.motion != null ? s.state.motion.name : "null")} transitions={s.state.transitions.Length}");
                }
            }

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            try
            {
                Animator a = instance.GetComponent<Animator>();
                a.Rebind();
                a.Update(0f);
                sb.AppendLine($"instance initialized={a.isInitialized} hasController={(a.runtimeAnimatorController != null)} paramCount={a.parameterCount} hasRun={(a.isInitialized && a.HasState(0, Animator.StringToHash("Run")))}");
                Transform hips = instance.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "mixamorig:Hips");
                sb.AppendLine($"hips path={(hips != null ? AnimationUtility.CalculateTransformPath(hips, instance.transform) : "missing")}");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }
        catch (System.Exception e)
        {
            sb.AppendLine("DIAG FAILED: " + e);
        }

        try { File.WriteAllText(DiagPath, sb.ToString()); } catch { /* ignore */ }
        Debug.Log("[BackHome] Snarlfang diag:\n" + sb);
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
        so.FindProperty("specialAbility").intValue = (int)CreatureSpecialAbility.Stealth;
        so.FindProperty("abilityDuration").floatValue = 2f;
        so.FindProperty("stealthOpacity").floatValue = 0.22f;
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
            bool hasRun = HasRunState(controller);
            bool wired = controller != null && prefab != null && prefab.GetComponent<Animator>() != null
                && prefab.GetComponent<Animator>().runtimeAnimatorController == controller
                && hasRun;
            if (!force && wired)
            {
                WriteResult("idle already set up");
                return;
            }

            AnimationClip idle1 = AssetDatabase.LoadAssetAtPath<AnimationClip>(Idle1ClipPath)
                ?? ExtractLoopingClip(ModelPath, "Idle1", Idle1ClipPath);
            AnimationClip idle2 = AssetDatabase.LoadAssetAtPath<AnimationClip>(Idle2ClipPath)
                ?? ExtractLoopingClip(Idle2ModelPath, "Idle2", Idle2ClipPath);
            AnimationClip running = ExtractLoopingClip(RunningModelPath, "Running", RunningClipPath);
            controller = CreateIdleController(idle1, idle2, running);
            Avatar avatar = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Avatar>().FirstOrDefault();
            WireAnimator(controller, avatar);

            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(RunningClipPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(ControllerPath, ImportAssetOptions.ForceUpdate);
            AssetDatabase.ImportAsset(PrefabPath, ImportAssetOptions.ForceUpdate);
            string paths = string.Join(" | ", SamplePaths(running).Take(4));
            WriteResult($"idle ok. layers={controller.layers.Length} states={controller.layers[0].stateMachine.states.Length} idle1={idle1.length:0.00}s idle2={idle2.length:0.00}s run={running.length:0.00}s paths={paths}");
            EnsureAttacks();
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            WriteResult("IDLE FAILED: " + e);
        }
    }

    static void EnsureAttacks()
    {
        try
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
                return;

            AnimationClip light = AssetDatabase.LoadAssetAtPath<AnimationClip>(LightClipPath)
                ?? ExtractLoopingClip(LightModelPath, "LightAttack", LightClipPath, loop: true);
            AnimationClip heavy = AssetDatabase.LoadAssetAtPath<AnimationClip>(HeavyClipPath)
                ?? ExtractLoopingClip(HeavyModelPath, "HeavyAttack", HeavyClipPath, loop: false);
            AddAttackStates(controller, light, heavy);
            AssetDatabase.SaveAssets();
            WriteResult($"attacks ok light={light.length:0.00}s heavy={heavy.length:0.00}s");
            EnsureDeath();
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            WriteResult("ATTACK FAILED: " + e);
        }
    }

    static void EnsureDeath()
    {
        try
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null || controller.layers == null || controller.layers.Length == 0)
                return;

            AnimationClip death = AssetDatabase.LoadAssetAtPath<AnimationClip>(DeathClipPath)
                ?? ExtractLoopingClip(DeathModelPath, "Death", DeathClipPath, loop: false);
            if (death == null)
                return;

            EnsureParameter(controller, "Die", AnimatorControllerParameterType.Trigger);

            AnimatorStateMachine machine = controller.layers[0].stateMachine;
            AnimatorState state = FindState(machine, "Death") ?? machine.AddState("Death", new Vector3(540, 420, 0));
            state.motion = death;
            state.speed = 1f;
            state.speedParameterActive = false;

            if (!HasAnyStateTransitionTo(machine, state))
            {
                AnimatorStateTransition transition = machine.AddAnyStateTransition(state);
                transition.hasExitTime = false;
                transition.exitTime = 0f;
                transition.hasFixedDuration = true;
                transition.duration = 0.05f;
                transition.canTransitionToSelf = false;
                transition.interruptionSource = TransitionInterruptionSource.None;
                transition.AddCondition(AnimatorConditionMode.If, 0, "Die");
            }

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            WriteResult($"death ok length={death.length:0.00}s loop=false");
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            WriteResult("DEATH FAILED: " + e);
        }
    }

    static bool HasAnyStateTransitionTo(AnimatorStateMachine machine, AnimatorState to)
    {
        if (machine == null || to == null)
            return true;

        foreach (AnimatorStateTransition transition in machine.anyStateTransitions)
        {
            if (transition.destinationState == to)
                return true;
        }

        return false;
    }

    static AnimatorState FindState(AnimatorStateMachine machine, string stateName)
    {
        foreach (ChildAnimatorState child in machine.states)
        {
            if (child.state != null && child.state.name == stateName)
                return child.state;
        }

        return null;
    }

    static void EnsureParameter(AnimatorController controller, string name, AnimatorControllerParameterType type)
    {
        foreach (AnimatorControllerParameter parameter in controller.parameters)
        {
            if (parameter.name == name && parameter.type == type)
                return;
        }

        controller.AddParameter(name, type);
    }

    static bool HasTransitionTo(AnimatorState from, AnimatorState to)
    {
        if (from == null || to == null)
            return true;

        foreach (AnimatorStateTransition transition in from.transitions)
        {
            if (transition.destinationState == to)
                return true;
        }

        return false;
    }

    static void Unlink(AnimatorState from, AnimatorState to)
    {
        if (from == null || to == null)
            return;

        AnimatorStateTransition[] transitions = from.transitions;
        for (int i = transitions.Length - 1; i >= 0; i--)
        {
            if (transitions[i] != null && transitions[i].destinationState == to)
                from.RemoveTransition(transitions[i]);
        }
    }

    static void AddAttackStates(AnimatorController controller, AnimationClip lightClip, AnimationClip heavyClip)
    {
        EnsureParameter(controller, "IsAttacking", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "AttackKind", AnimatorControllerParameterType.Int);
        EnsureParameter(controller, "AttackAnimSpeed", AnimatorControllerParameterType.Float);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        AnimatorState light = FindState(machine, "LightAttack") ?? machine.AddState("LightAttack", new Vector3(540, 210, 0));
        AnimatorState heavy = FindState(machine, "HeavyAttack") ?? machine.AddState("HeavyAttack", new Vector3(540, -70, 0));
        light.motion = lightClip;
        heavy.motion = heavyClip;
        light.speedParameterActive = true;
        heavy.speedParameterActive = true;
        light.speedParameter = "AttackAnimSpeed";
        heavy.speedParameter = "AttackAnimSpeed";

        AnimatorState idle1 = FindState(machine, "Idle1");
        AnimatorState idle2 = FindState(machine, "Idle2");
        AnimatorState run = FindState(machine, "Run");

        LinkAttack(idle1, light, CreatureAttackKind.Light);
        LinkAttack(idle2, light, CreatureAttackKind.Light);
        LinkAttack(run, light, CreatureAttackKind.Light);
        LinkAttack(heavy, light, CreatureAttackKind.Light);

        // Heavy Attack is only the Stealth exit. Nothing in the controller may enter it on its own.
        Unlink(idle1, heavy);
        Unlink(idle2, heavy);
        Unlink(run, heavy);
        Unlink(light, heavy);

        LinkAttackExit(light, run, moving: true);
        LinkAttackExit(heavy, run, moving: true);
        LinkAttackExit(light, idle1, moving: false);
        LinkAttackExit(heavy, idle1, moving: false);

        EditorUtility.SetDirty(controller);
    }

    static void LinkAttack(AnimatorState from, AnimatorState to, CreatureAttackKind kind)
    {
        if (from == null || to == null || HasTransitionTo(from, to))
            return;

        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.exitTime = 0f;
        transition.hasFixedDuration = true;
        transition.duration = 0.08f;
        transition.canTransitionToSelf = false;
        transition.AddCondition(AnimatorConditionMode.If, 0, "IsAttacking");
        transition.AddCondition(AnimatorConditionMode.Equals, (int)kind, "AttackKind");
    }

    static void LinkAttackExit(AnimatorState from, AnimatorState to, bool moving)
    {
        if (from == null || to == null || HasTransitionTo(from, to))
            return;

        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.exitTime = 0f;
        transition.hasFixedDuration = true;
        transition.duration = 0.1f;
        transition.canTransitionToSelf = false;
        transition.AddCondition(AnimatorConditionMode.IfNot, 0, "IsAttacking");
        transition.AddCondition(moving ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, "IsMoving");
    }

    static bool HasRunState(AnimatorController controller)
    {
        if (controller == null || controller.layers == null || controller.layers.Length == 0)
            return false;

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        if (machine == null)
            return false;

        bool hasParam = false;
        foreach (AnimatorControllerParameter parameter in controller.parameters)
        {
            if (parameter.name == "IsMoving" && parameter.type == AnimatorControllerParameterType.Bool)
            {
                hasParam = true;
                break;
            }
        }

        if (!hasParam)
            return false;

        foreach (ChildAnimatorState child in machine.states)
        {
            if (child.state != null && child.state.name == "Run" && child.state.motion != null)
                return true;
        }

        return false;
    }

    static AnimationClip ExtractLoopingClip(string modelPath, string clipName, string outputPath, bool loop = true)
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
        if (importer.importedTakeInfos == null || importer.importedTakeInfos.Length == 0)
            throw new System.InvalidOperationException(modelPath + " has no animation take.");

        TakeInfo selected = SelectLongestTake(importer.importedTakeInfos);
        ModelImporterClipAnimation[] takes = new[]
        {
            new ModelImporterClipAnimation
            {
                name = clipName,
                takeName = selected.name,
                firstFrame = selected.startTime * selected.sampleRate,
                lastFrame = selected.stopTime * selected.sampleRate,
                loopTime = loop,
                loopPose = false,
                wrapMode = WrapMode.Loop
            }
        };

        Debug.Log($"[BackHome] {clipName} take '{selected.name}' ({selected.stopTime - selected.startTime:0.00}s) from {modelPath}");

        importer.clipAnimations = takes;
        importer.SaveAndReimport();

        AnimationClip source = AssetDatabase.LoadAllAssetsAtPath(modelPath)
            .OfType<AnimationClip>()
            .FirstOrDefault(c => c.name == clipName && !c.name.StartsWith("__preview"));
        if (source == null)
            throw new System.InvalidOperationException("Could not read clip " + clipName + " from " + modelPath);

        return BakeInPlaceClip(source, outputPath, loop);
    }

    static AnimationClip BakeInPlaceClip(AnimationClip source, string outputPath, bool loop)
    {
        var clip = new AnimationClip { name = Path.GetFileNameWithoutExtension(outputPath) };
        AnimationClipSettings settings = AnimationUtility.GetAnimationClipSettings(source);
        settings.loopTime = loop;
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

        return SaveClipInPlace(clip, outputPath);
    }

    static AnimationClip SaveClipInPlace(AnimationClip clip, string outputPath)
    {
        EnsureFolder("Assets/Resources/Galaxy/Nyxara/Creatures/Snarlfang", "Animations");
        AnimationClip existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(outputPath);
        if (existing == null)
        {
            AssetDatabase.CreateAsset(clip, outputPath);
            return clip;
        }

        string name = existing.name;
        EditorUtility.CopySerialized(clip, existing);
        existing.name = name;
        EditorUtility.SetDirty(existing);
        AssetDatabase.SaveAssetIfDirty(existing);
        Object.DestroyImmediate(clip);
        return existing;
    }

    static TakeInfo SelectLongestTake(TakeInfo[] takes)
    {
        TakeInfo best = takes[0];
        float bestLength = best.stopTime - best.startTime;
        foreach (TakeInfo take in takes)
        {
            float length = take.stopTime - take.startTime;
            if (length > bestLength)
            {
                best = take;
                bestLength = length;
            }
        }

        return best;
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

    static AnimatorController CreateIdleController(AnimationClip idle1, AnimationClip idle2, AnimationClip running)
    {
        // Rebuild in place: deleting and recreating at the same path leaves the prefab's
        // Animator pointing at the destroyed controller until the editor restarts.
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath)
            ?? AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);

        foreach (AnimatorControllerParameter param in controller.parameters.ToList())
            controller.RemoveParameter(param);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;
        foreach (ChildAnimatorState child in machine.states.ToList())
            machine.RemoveState(child.state);
        foreach (AnimatorStateTransition any in machine.anyStateTransitions.ToList())
            machine.RemoveAnyStateTransition(any);

        controller.AddParameter("IsMoving", AnimatorControllerParameterType.Bool);
        controller.AddParameter("IsAttacking", AnimatorControllerParameterType.Bool);

        AnimatorState state1 = machine.AddState("Idle1", new Vector3(280, 0, 0));
        state1.motion = idle1;
        AnimatorState state2 = machine.AddState("Idle2", new Vector3(280, 140, 0));
        state2.motion = idle2;
        AnimatorState run = machine.AddState("Run", new Vector3(540, 70, 0));
        run.motion = running;
        machine.defaultState = state1;

        const float idleCrossfade = 0.25f;
        const float moveCrossfade = 0.12f;
        // Moving transitions are added first so they win over the idle swap at the loop point,
        // and so they can interrupt an idle crossfade that already started.
        AddMoveTransition(state1, run, moving: true, moveCrossfade);
        AddMoveTransition(state2, run, moving: true, moveCrossfade);
        AddMoveTransition(run, state1, moving: false, moveCrossfade);

        AnimatorStateTransition to2 = state1.AddTransition(state2);
        to2.hasExitTime = true;
        to2.exitTime = 1f;
        to2.hasFixedDuration = true;
        to2.duration = idleCrossfade;
        to2.interruptionSource = TransitionInterruptionSource.Source;
        to2.orderedInterruption = true;
        to2.AddCondition(AnimatorConditionMode.IfNot, 0, "IsMoving");

        AnimatorStateTransition to1 = state2.AddTransition(state1);
        to1.hasExitTime = true;
        to1.exitTime = 1f;
        to1.hasFixedDuration = true;
        to1.duration = idleCrossfade;
        to1.interruptionSource = TransitionInterruptionSource.Source;
        to1.orderedInterruption = true;
        to1.AddCondition(AnimatorConditionMode.IfNot, 0, "IsMoving");

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    static void AddMoveTransition(AnimatorState from, AnimatorState to, bool moving, float duration)
    {
        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.exitTime = 0f;
        transition.hasFixedDuration = true;
        transition.duration = duration;
        transition.canTransitionToSelf = false;
        transition.AddCondition(AnimatorConditionMode.IfNot, 0, "IsAttacking");
        transition.AddCondition(moving ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0, "IsMoving");
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

public class SnarlfangRunSetupHook : AssetPostprocessor
{
    static bool _busy;

    static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets, string[] movedFromAssetPaths)
    {
        if (_busy)
            return;

        _busy = true;
        try
        {
            SnarlfangSetup.TryRunPendingSetup();
        }
        finally
        {
            _busy = false;
        }
    }
}
