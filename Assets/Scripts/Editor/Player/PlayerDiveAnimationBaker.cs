using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Extracts the dive and land takes into standalone clips (hashed FBX sub-clip fileIDs cannot
/// be referenced reliably) and wires them onto PlayerDiveDownCapsule.
/// </summary>
static class PlayerDiveAnimationBaker
{
    const string CapsulePrefabPath = PlayerDiveDownCapsulePaths.AssetCapsulePrefab;

    static readonly string TriggerFilePath = Path.Combine(Application.dataPath, "..", "Temp", "BackHomeBakeDiveAnimation.trigger");
    static readonly string ResultLogPath = Path.Combine(Application.dataPath, "..", "Temp", "DiveAnimBake.log");

    [InitializeOnLoadMethod]
    static void WatchForTriggerFile()
    {
        EditorApplication.update -= PollTrigger;
        EditorApplication.update += PollTrigger;
        TryRunFromTrigger();
    }

    static void PollTrigger() => TryRunFromTrigger();

    static void TryRunFromTrigger()
    {
        if (!File.Exists(TriggerFilePath))
            return;
        if (EditorApplication.isPlaying || EditorApplication.isCompiling)
            return;

        try { File.Delete(TriggerFilePath); } catch { return; }
        EditorApplication.update -= PollTrigger;
        EditorApplication.delayCall += Run;
    }

    [MenuItem("Tools/Player VFX/Bake Dive Down Animation")]
    public static void Run()
    {
        if (EditorApplication.isPlaying)
        {
            Debug.LogWarning("[PlayerDiveAnimationBaker] stop Play Mode first.");
            return;
        }

        var log = new StringBuilder();
        void Line(string message)
        {
            log.AppendLine(message);
            Debug.Log("[PlayerDiveAnimationBaker] " + message);
        }

        try
        {
            AnimationClip landClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(PlayerDiveDownCapsulePaths.AssetLandClip);
            if (landClip == null)
            {
                landClip = ExtractClip(
                    PlayerDiveDownCapsulePaths.AssetLandModel,
                    PlayerDiveDownCapsulePaths.AssetLandClip,
                    PlayerDiveDownCapsulePaths.LandClipAssetName,
                    Line);
            }
            else
            {
                Line("Land clip already at " + PlayerDiveDownCapsulePaths.AssetLandClip + " length=" + landClip.length.ToString("0.000") + "s");
            }

            AnimationClip diveClip = ExtractClip(
                PlayerDiveDownCapsulePaths.AssetDiveDownModel,
                PlayerDiveDownCapsulePaths.AssetDiveClip,
                PlayerDiveDownCapsulePaths.DiveClipAssetName,
                Line);

            AnimationClip landInPlace = BakePlayerLandClip(landClip, Line);

            if (diveClip == null && landClip == null && landInPlace == null)
            {
                WriteLog(log);
                return;
            }

            GameObject prefab = PrefabUtility.LoadPrefabContents(CapsulePrefabPath);
            try
            {
                Transform model = prefab.transform.Find(PlayerDiveDownCapsulePaths.DiveModelChildName);
                if (model == null)
                {
                    SkinnedMeshRenderer skin = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
                    model = skin != null ? skin.transform : null;
                    while (model != null && model.parent != prefab.transform)
                        model = model.parent;
                }

                if (model == null)
                {
                    Line("FAIL: nested dive model not found on prefab.");
                    WriteLog(log);
                    return;
                }

                Animator animator = model.GetComponent<Animator>();
                if (animator == null)
                    animator = model.GetComponentInChildren<Animator>(true);
                if (animator == null)
                    animator = model.gameObject.AddComponent<Animator>();

                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.updateMode = AnimatorUpdateMode.Normal;
                animator.enabled = true;

                PlayerDiveAnimation dive = prefab.GetComponent<PlayerDiveAnimation>();
                if (dive == null)
                    dive = prefab.AddComponent<PlayerDiveAnimation>();

                SerializedObject so = new SerializedObject(dive);
                so.FindProperty("modelRoot").objectReferenceValue = model;
                if (diveClip != null)
                    so.FindProperty("diveClip").objectReferenceValue = diveClip;
                so.ApplyModifiedPropertiesWithoutUndo();

                PrefabUtility.SaveAsPrefabAsset(prefab, CapsulePrefabPath);
                Line("Saved " + CapsulePrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(prefab);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Line("OK");
        }
        catch (System.Exception ex)
        {
            Line("EXCEPTION: " + ex);
        }

        WriteLog(log);
    }

    static AnimationClip BakePlayerLandClip(AnimationClip source, System.Action<string> line)
    {
        if (source == null)
        {
            line("FAIL: no land source clip to remap onto the Player.");
            return null;
        }

        const string oldRoot = "Armature";
        const string newRoot = "target_character";
        string oldPrefix = oldRoot + "/";
        string outputPath = PlayerDiveDownCapsulePaths.AssetLandInPlaceClip;

        AnimationClip clip = new AnimationClip { name = "DiveDownAndLandInPlace" };
        var settings = AnimationUtility.GetAnimationClipSettings(source);
        settings.loopTime = false;
        settings.loopBlend = false;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        int remapped = 0;
        int dropped = 0;
        foreach (var binding in AnimationUtility.GetCurveBindings(source))
        {
            if (binding.type == typeof(Transform) && binding.propertyName.StartsWith("m_LocalScale."))
            {
                dropped++;
                continue;
            }

            bool isRoot = binding.path == oldRoot;
            bool isChild = binding.path.StartsWith(oldPrefix, System.StringComparison.Ordinal);
            if (isRoot)
            {
                dropped++;
                continue;
            }

            bool isHipsPosition = binding.path.EndsWith("Hips", System.StringComparison.Ordinal)
                && binding.propertyName.StartsWith("m_LocalPosition.");
            if (isHipsPosition)
            {
                dropped++;
                continue;
            }

            var curve = AnimationUtility.GetEditorCurve(source, binding);
            var newBinding = binding;
            if (isChild)
                newBinding.path = newRoot + binding.path.Substring(oldRoot.Length);

            AnimationUtility.SetEditorCurve(clip, newBinding, curve);
            remapped++;
        }

        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(outputPath) != null)
            AssetDatabase.DeleteAsset(outputPath);
        AssetDatabase.CreateAsset(clip, outputPath);
        line("Player land clip: " + remapped + " curves remapped " + oldRoot + " -> " + newRoot +
             ", dropped " + dropped + " root/hips-position/scale tracks -> " + outputPath);

        GameObject player = PrefabUtility.LoadPrefabContents("Assets/Resources/Player/Player.prefab");
        try
        {
            PlayerLandIntro land = player.GetComponent<PlayerLandIntro>();
            if (land == null)
                land = player.AddComponent<PlayerLandIntro>();
            SerializedObject so = new SerializedObject(land);
            so.FindProperty("landClip").objectReferenceValue = clip;
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(player, "Assets/Resources/Player/Player.prefab");
            line("Wired PlayerLandIntro on Player.prefab");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(player);
        }

        return clip;
    }

    static AnimationClip ExtractClip(string fbxPath, string clipAssetPath, string clipName, System.Action<string> line)
    {
        var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null)
        {
            line("FAIL: no ModelImporter at " + fbxPath);
            return null;
        }

        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.optimizeGameObjects = false;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.SaveAndReimport();

        var takes = importer.importedTakeInfos;
        if (takes == null || takes.Length == 0)
        {
            line("FAIL: " + fbxPath + " has no embedded animation take.");
            return null;
        }

        for (int i = 0; i < takes.Length; i++)
            line("Take[" + i + "] " + fbxPath + ": '" + takes[i].name + "' " + takes[i].startTime + "s -> " + takes[i].stopTime + "s");

        var take = takes[0];
        var clipAnim = new ModelImporterClipAnimation
        {
            name = clipName,
            takeName = take.name,
            firstFrame = take.startTime * take.sampleRate,
            lastFrame = take.stopTime * take.sampleRate,
            loopTime = false,
            loopPose = false,
            wrapMode = WrapMode.ClampForever,
            keepOriginalOrientation = true,
            keepOriginalPositionY = true,
            keepOriginalPositionXZ = true,
        };
        importer.clipAnimations = new[] { clipAnim };
        importer.SaveAndReimport();

        var sourceClip = AssetDatabase.LoadAllAssetsAtPath(fbxPath)
            .OfType<AnimationClip>()
            .FirstOrDefault(c => c.name == clipName)
            ?? AssetDatabase.LoadAllAssetsAtPath(fbxPath)
                .OfType<AnimationClip>()
                .FirstOrDefault(c => c.name.IndexOf("__preview__", System.StringComparison.OrdinalIgnoreCase) < 0);

        if (sourceClip == null)
        {
            line("FAIL: could not load an AnimationClip from " + fbxPath);
            return null;
        }

        line("Source clip: '" + sourceClip.name + "' length=" + sourceClip.length.ToString("0.000") + "s from " + fbxPath);

        AnimationClip standalone = Object.Instantiate(sourceClip);
        standalone.name = clipName;
        standalone.legacy = false;
        var settings = AnimationUtility.GetAnimationClipSettings(sourceClip);
        settings.loopTime = false;
        settings.loopBlend = false;
        AnimationUtility.SetAnimationClipSettings(standalone, settings);

        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(clipAssetPath) != null)
            AssetDatabase.DeleteAsset(clipAssetPath);
        AssetDatabase.CreateAsset(standalone, clipAssetPath);
        line("Wrote " + clipAssetPath);
        return standalone;
    }

    static void WriteLog(StringBuilder log)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ResultLogPath));
            File.WriteAllText(ResultLogPath, log.ToString());
        }
        catch
        {
            // ignore
        }
    }
}
