using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// One-shot tool that swaps the Player character's visible model (and its running
/// animation) for the new model placed at Assets/Resources/Player/Model/Player.fbx.
///
/// Run it once from the menu below while the project is open in the Editor. It:
///  1) Configures the new Player.fbx as a Humanoid model with an auto-generated Avatar.
///  2) Reads its embedded animation take and sets it up as a looping "Run" clip.
///  3) Replaces the old Geometry/Skeleton hierarchy inside Player.prefab with an
///     instance of the new model, and re-points the Animator's Avatar to it.
///  4) Swaps the running motion inside the shared AnimatorController's locomotion
///     blend tree from the old Locomotion--Run_N clip to the new embedded clip.
///
/// Nothing is written to disk until the very end of each step (LoadPrefabContents
/// only edits an in-memory copy), so if something looks wrong, you can always revert
/// via git before saving/committing.
/// </summary>
public static class PlayerModelSwapTool
{
    const string PlayerPrefabPath = "Assets/Resources/Player/Player.prefab";
    const string NewModelPath = "Assets/Resources/Player/Model/Player.fbx";
    const string ControllerPath = "Assets/Resources/Player/Animator/StarterAssetsThirdPerson.controller";
    const string AlbedoPath = "Assets/Resources/Player/Model/Player_texture.png";
    const string NormalMapPath = "Assets/Resources/Player/Model/Player_normal.png";
    const string MetallicPath = "Assets/Resources/Player/Model/Player_metallic.png";
    const string RoughnessPath = "Assets/Resources/Player/Model/Player_roughness.png";
    const string CombinedMetallicSmoothnessPath = "Assets/Resources/Player/Model/Player_MetallicSmoothness.png";
    const string LitShaderGuid = "e4f8a21c7b6d9053c8e1f4a63720d5b9"; // same URP/Lit shader used by Frosty.mat elsewhere in the project

    // Extra mocap/animation library (same rig as Player.fbx) that holds a custom "running on a
    // spherical planet" take alongside several other one-shot clips (Alert, Fall, etc.). The user
    // moved/renamed this file to Player_Running.fbx and confirmed the take literally named
    // "Running" (not the earlier GUID-named take) is the correct animation to use.
    const string AnimLibraryPath = "Assets/Resources/Player/Animations/Player_Running.fbx";
    const string PlanetRunTakeNameFragment = "rigify_clip"; // the actual 89-frame run cycle; "Armature|clip0|baselayer" is just a 1-frame bind pose
    const string PlanetRunClipName = "RunOnPlanet";

    // Current model/animation set: individual Meshy AI exports, each bundling the full skinned
    // mesh + one baked-in animation take (the old shared Player.fbx base model and
    // Player_Animations.fbx library have since been deleted from the project). This one is used
    // directly as the Player's model since it already contains the "running on a spherical
    // planet" take we want wired up as the running motion.
    const string RunOnPlanetModelPath = "Assets/Resources/Player/Model/Meshy_AI_Little_Starbot_biped_Animation_Run_On_Planet.fbx";
    const string RunOnPlanetClipName = "Run";
    const string RunInPlaceClipPath = "Assets/Resources/Player/Animator/RunInPlace.anim";

    // Two idle variants (same "Little Starbot" rig/skeleton as the run model above) that the
    // Animator alternates between while the player stands still: 50/50 random pick of which one
    // starts, then swap to the other every 4 loops, back and forth indefinitely.
    //
    // These two FBX files were exported with their skeleton root bone named "Armature" instead of
    // "target_character" (the name the Run On Planet model - and therefore the live Player
    // hierarchy - uses for that same bone), even though every bone *below* the root matches
    // exactly (Hips, LeftUpLeg, Spine02, etc.). Mecanim's Generic-avatar retargeting binds curves
    // by walking this exact name chain from wherever it finds "target_character" under the
    // Animator, so a clip rooted at "Armature" silently matches zero bones and plays nothing
    // visible - see RemapClipRootBoneName, applied to both baked clips below.
    const string IdleSourceRootBoneName = "Armature";
    const string IdleTargetRootBoneName = "target_character";
    const string Idle1ModelPath = "Assets/Resources/Player/Model/Starbot_Animation_Idel1.fbx";
    const string Idle2ModelPath = "Assets/Resources/Player/Model/Starbot_Animation_Idel2.fbx";
    const string Idle1ClipName = "Idle1";
    const string Idle2ClipName = "Idle2";
    const string Idle1InPlaceClipPath = "Assets/Resources/Player/Animator/Idle1InPlace.anim";
    const string Idle2InPlaceClipPath = "Assets/Resources/Player/Animator/Idle2InPlace.anim";

    // Material shared by every Meshy AI export in Model/ (they were all authored with a single
    // "Material_1" slot). The shader wired into it isn't actually URP/Lit despite the guid's
    // original comment - it's the project's own custom "BackHome/CasualToon" shader (same one
    // Frosty.mat etc. use), which only understands _BaseMap/_BaseColor and _BumpMap/_BumpScale;
    // it has no metallic/smoothness input at all, so Player_metallic.png/Player_roughness.png/
    // the combined map built for the old URP/Lit path are unused by this shader.
    const string CharacterMaterialPath = "Assets/Resources/Player/Model/Material_1.mat";
    static readonly string SetupRunOnPlanetTriggerFilePath = System.IO.Path.Combine(Application.dataPath, "..", "Temp", "BackHomeSetupRunOnPlanet.trigger");
    static readonly string SetupIdleAnimationsTriggerFilePath = System.IO.Path.Combine(Application.dataPath, "..", "Temp", "BackHomeSetupIdleAnimations.trigger");
    static readonly string DumpLivePlayerHierarchyTriggerFilePath = System.IO.Path.Combine(Application.dataPath, "..", "Temp", "BackHomeDumpLivePlayerHierarchy.trigger");
    static readonly string DumpBoneTransformsTriggerFilePath = System.IO.Path.Combine(Application.dataPath, "..", "Temp", "BackHomeDumpBoneTransforms.trigger");
    static readonly string CloseIsolationStageTriggerFilePath = System.IO.Path.Combine(Application.dataPath, "..", "Temp", "BackHomeCloseIsolationStage.trigger");
    static readonly string FixCharacterTextureTriggerFilePath = System.IO.Path.Combine(Application.dataPath, "..", "Temp", "BackHomeFixCharacterTexture.trigger");

    static readonly string TriggerFilePath = System.IO.Path.Combine(Application.dataPath, "..", "Temp", "BackHomeSwapPlayerModel.trigger");
    static readonly string PreviewTriggerFilePath = System.IO.Path.Combine(Application.dataPath, "..", "Temp", "BackHomePreviewPlayerModel.trigger");
    static readonly string FixMaterialTriggerFilePath = System.IO.Path.Combine(Application.dataPath, "..", "Temp", "BackHomeFixPlayerMaterial.trigger");
    static readonly string SwapPlanetRunTriggerFilePath = System.IO.Path.Combine(Application.dataPath, "..", "Temp", "BackHomeSwapPlanetRunAnimation.trigger");
    static readonly string RebuildAvatarTriggerFilePath = System.IO.Path.Combine(Application.dataPath, "..", "Temp", "BackHomeRebuildPlayerAvatar.trigger");
    static readonly string DiagnoseAvatarsTriggerFilePath = System.IO.Path.Combine(Application.dataPath, "..", "Temp", "BackHomeDiagnoseAvatars.trigger");
    static readonly string EnterPlayModeTriggerFilePath = System.IO.Path.Combine(Application.dataPath, "..", "Temp", "BackHomeEnterPlayMode.trigger");
    static readonly string ExitPlayModeTriggerFilePath = System.IO.Path.Combine(Application.dataPath, "..", "Temp", "BackHomeExitPlayMode.trigger");
    static readonly string PreviewClipTriggerFilePath = System.IO.Path.Combine(Application.dataPath, "..", "Temp", "BackHomePreviewClip.trigger");
    static readonly string PreviewClipTimeFilePath = System.IO.Path.Combine(Application.dataPath, "..", "Temp", "BackHomePreviewClipTime.txt");
    static readonly string PreviewClipStopTriggerFilePath = System.IO.Path.Combine(Application.dataPath, "..", "Temp", "BackHomePreviewClipStop.trigger");
    static readonly string DumpRunCurvesTriggerFilePath = System.IO.Path.Combine(Application.dataPath, "..", "Temp", "BackHomeDumpRunCurves.trigger");
    static readonly string DumpLoopSeamTriggerFilePath = System.IO.Path.Combine(Application.dataPath, "..", "Temp", "BackHomeDumpLoopSeam.trigger");
    static readonly string DumpFrameDeltasTriggerFilePath = System.IO.Path.Combine(Application.dataPath, "..", "Temp", "BackHomeDumpFrameDeltas.trigger");
    static readonly string DumpRawSeamAnglesTriggerFilePath = System.IO.Path.Combine(Application.dataPath, "..", "Temp", "BackHomeDumpRawSeamAngles.trigger");
    static readonly string FindBestLoopEndFrameTriggerFilePath = System.IO.Path.Combine(Application.dataPath, "..", "Temp", "BackHomeFindBestLoopEndFrame.trigger");
    static readonly string FrameSelectedTriggerFilePath = System.IO.Path.Combine(Application.dataPath, "..", "Temp", "BackHomeFrameSelected.trigger");
    static readonly string OpenSceneTriggerFilePath = System.IO.Path.Combine(Application.dataPath, "..", "Temp", "BackHomeOpenScene.trigger");
    static readonly string OpenScenePathFilePath = System.IO.Path.Combine(Application.dataPath, "..", "Temp", "BackHomeOpenScenePath.txt");

    [InitializeOnLoadMethod]
    static void WatchForTriggerFile()
    {
        EditorApplication.update += CheckTrigger;
    }

    static void CheckTrigger()
    {
        if (System.IO.File.Exists(TriggerFilePath))
        {
            try { System.IO.File.Delete(TriggerFilePath); } catch { /* ignore */ }
            Debug.Log("[BackHome] Trigger file detected - running Swap Player Model automatically.");
            SwapPlayerModel();
        }

        if (System.IO.File.Exists(PreviewTriggerFilePath))
        {
            try { System.IO.File.Delete(PreviewTriggerFilePath); } catch { /* ignore */ }
            Debug.Log("[BackHome] Preview trigger detected - opening Player prefab in isolation.");
            PreviewPlayerPrefab();
        }

        if (System.IO.File.Exists(FixMaterialTriggerFilePath))
        {
            try { System.IO.File.Delete(FixMaterialTriggerFilePath); } catch { /* ignore */ }
            Debug.Log("[BackHome] Fix-material trigger detected - wiring textures into the Player model material.");
            FixPlayerMaterial();
        }

        if (System.IO.File.Exists(SwapPlanetRunTriggerFilePath))
        {
            try { System.IO.File.Delete(SwapPlanetRunTriggerFilePath); } catch { /* ignore */ }
            Debug.Log("[BackHome] Swap-planet-run trigger detected - swapping in the planet running animation.");
            SwapRunAnimationToPlanetClip();
        }

        if (System.IO.File.Exists(SetupIdleAnimationsTriggerFilePath))
        {
            try { System.IO.File.Delete(SetupIdleAnimationsTriggerFilePath); } catch { /* ignore */ }
            Debug.Log("[BackHome] Setup-idle-animations trigger detected - wiring up the Idle1/Idle2 idle animations.");
            SetupPlayerIdleAnimations();
        }

        if (System.IO.File.Exists(DumpLivePlayerHierarchyTriggerFilePath))
        {
            try { System.IO.File.Delete(DumpLivePlayerHierarchyTriggerFilePath); } catch { /* ignore */ }
            Debug.Log("[BackHome] Dump-live-player-hierarchy trigger detected - dumping the live Player's transform hierarchy.");
            DumpLivePlayerHierarchy();
        }

        if (System.IO.File.Exists(DumpBoneTransformsTriggerFilePath))
        {
            try { System.IO.File.Delete(DumpBoneTransformsTriggerFilePath); } catch { /* ignore */ }
            Debug.Log("[BackHome] Dump-bone-transforms trigger detected - comparing rig scales.");
            DumpModelBoneTransforms(RunOnPlanetModelPath);
            DumpModelBoneTransforms(Idle1ModelPath);
            DumpModelBoneTransforms(Idle2ModelPath);
        }

        if (System.IO.File.Exists(RebuildAvatarTriggerFilePath))
        {
            try { System.IO.File.Delete(RebuildAvatarTriggerFilePath); } catch { /* ignore */ }
            Debug.Log("[BackHome] Rebuild-avatar trigger detected - rebuilding the Player Humanoid Avatar.");
            RebuildPlayerAvatar();
        }

        if (System.IO.File.Exists(DiagnoseAvatarsTriggerFilePath))
        {
            try { System.IO.File.Delete(DiagnoseAvatarsTriggerFilePath); } catch { /* ignore */ }
            Debug.Log("[BackHome] Diagnose-avatars trigger detected - dumping bone hierarchies and human bone maps.");
            DiagnoseAvatars();
        }

        if (System.IO.File.Exists(DumpRunCurvesTriggerFilePath))
        {
            try { System.IO.File.Delete(DumpRunCurvesTriggerFilePath); } catch { /* ignore */ }
            Debug.Log("[BackHome] Dump-run-curves trigger detected.");
            DumpRunClipCurves();
        }

        if (System.IO.File.Exists(DumpLoopSeamTriggerFilePath))
        {
            try { System.IO.File.Delete(DumpLoopSeamTriggerFilePath); } catch { /* ignore */ }
            Debug.Log("[BackHome] Dump-loop-seam trigger detected.");
            DumpLoopSeam();
        }

        if (System.IO.File.Exists(FrameSelectedTriggerFilePath))
        {
            try { System.IO.File.Delete(FrameSelectedTriggerFilePath); } catch { /* ignore */ }
            var sv = SceneView.lastActiveSceneView;
            var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            if (sv != null && stage != null && stage.prefabContentsRoot != null)
            {
                var renderers = stage.prefabContentsRoot.GetComponentsInChildren<Renderer>();
                if (renderers.Length > 0)
                {
                    var bounds = renderers[0].bounds;
                    foreach (var r in renderers) bounds.Encapsulate(r.bounds);
                    sv.pivot = bounds.center;
                    sv.size = Mathf.Max(bounds.extents.magnitude, 0.5f);
                }
                else
                {
                    sv.pivot = stage.prefabContentsRoot.transform.position;
                    sv.size = 3f;
                }
                sv.Repaint();
            }
        }

        if (System.IO.File.Exists(DumpFrameDeltasTriggerFilePath))
        {
            try { System.IO.File.Delete(DumpFrameDeltasTriggerFilePath); } catch { /* ignore */ }
            Debug.Log("[BackHome] Dump-frame-deltas trigger detected.");
            DumpFrameByFrameRotationDeltas();
        }

        if (System.IO.File.Exists(DumpRawSeamAnglesTriggerFilePath))
        {
            try { System.IO.File.Delete(DumpRawSeamAnglesTriggerFilePath); } catch { /* ignore */ }
            Debug.Log("[BackHome] Dump-raw-seam-angles trigger detected.");
            DumpRawSeamAngles();
        }

        if (System.IO.File.Exists(FindBestLoopEndFrameTriggerFilePath))
        {
            try { System.IO.File.Delete(FindBestLoopEndFrameTriggerFilePath); } catch { /* ignore */ }
            Debug.Log("[BackHome] Find-best-loop-end-frame trigger detected.");
            FindBestLoopEndFrame();
        }

        if (System.IO.File.Exists(OpenSceneTriggerFilePath))
        {
            try { System.IO.File.Delete(OpenSceneTriggerFilePath); } catch { /* ignore */ }
            string scenePath = null;
            try { scenePath = System.IO.File.ReadAllText(OpenScenePathFilePath).Trim(); } catch { /* ignore */ }
            if (!string.IsNullOrEmpty(scenePath))
            {
                Debug.Log($"[BackHome] Open-scene trigger detected - opening {scenePath}.");
                UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath);
            }
        }

        if (System.IO.File.Exists(EnterPlayModeTriggerFilePath))
        {
            try { System.IO.File.Delete(EnterPlayModeTriggerFilePath); } catch { /* ignore */ }
            Debug.Log("[BackHome] Enter-play-mode trigger detected.");
            EditorApplication.isPlaying = true;
        }

        if (System.IO.File.Exists(ExitPlayModeTriggerFilePath))
        {
            try { System.IO.File.Delete(ExitPlayModeTriggerFilePath); } catch { /* ignore */ }
            Debug.Log("[BackHome] Exit-play-mode trigger detected.");
            EditorApplication.isPlaying = false;
        }

        if (System.IO.File.Exists(PreviewClipTriggerFilePath))
        {
            try { System.IO.File.Delete(PreviewClipTriggerFilePath); } catch { /* ignore */ }
            float t = 0f;
            try
            {
                if (System.IO.File.Exists(PreviewClipTimeFilePath))
                    float.TryParse(System.IO.File.ReadAllText(PreviewClipTimeFilePath).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out t);
            }
            catch { /* ignore, default to 0 */ }
            Debug.Log($"[BackHome] Preview-clip trigger detected - sampling RunOnPlanet at t={t:0.00}s.");
            PreviewRunOnPlanetClipAtTime(t);
        }

        if (System.IO.File.Exists(PreviewClipStopTriggerFilePath))
        {
            try { System.IO.File.Delete(PreviewClipStopTriggerFilePath); } catch { /* ignore */ }
            Debug.Log("[BackHome] Preview-clip-stop trigger detected - exiting animation preview mode.");
            if (AnimationMode.InAnimationMode())
                AnimationMode.StopAnimationMode();
        }

        if (System.IO.File.Exists(SetupRunOnPlanetTriggerFilePath))
        {
            try { System.IO.File.Delete(SetupRunOnPlanetTriggerFilePath); } catch { /* ignore */ }
            Debug.Log("[BackHome] Setup-run-on-planet trigger detected - rebuilding the Player model/controller around the Run On Planet clip.");
            SetupPlayerRunOnPlanet();
        }

        if (System.IO.File.Exists(CloseIsolationStageTriggerFilePath))
        {
            try { System.IO.File.Delete(CloseIsolationStageTriggerFilePath); } catch { /* ignore */ }
            Debug.Log("[BackHome] Close-isolation-stage trigger detected - returning to the main scene stage.");
            Selection.activeObject = null;
            UnityEditor.SceneManagement.StageUtility.GoToMainStage();
        }

        if (System.IO.File.Exists(FixCharacterTextureTriggerFilePath))
        {
            try { System.IO.File.Delete(FixCharacterTextureTriggerFilePath); } catch { /* ignore */ }
            Debug.Log("[BackHome] Fix-character-texture trigger detected - wiring Player_texture/Player_normal into Material_1 and forcing it onto the Player's renderers.");
            FixPlayerCharacterTexture();
        }

        if (System.IO.File.Exists(FrameLivePlayerTriggerFilePath))
        {
            try { System.IO.File.Delete(FrameLivePlayerTriggerFilePath); } catch { /* ignore */ }
            Debug.Log("[BackHome] Frame-live-player trigger detected - zooming the Scene view onto the live Player character.");
            FrameLivePlayerInSceneView();
        }
    }

    static readonly string FrameLivePlayerTriggerFilePath = System.IO.Path.Combine(Application.dataPath, "..", "Temp", "BackHomeFrameLivePlayer.trigger");

    /// <summary>
    /// Prints each bone's local position/scale for the given model asset's skeleton - used to
    /// compare the Idle1/Idle2 rig's bone offsets against the Run On Planet rig's to check for a
    /// unit-scale mismatch (e.g. one rig authored in centimeters, the other in meters).
    /// </summary>
    static void DumpModelBoneTransforms(string modelPath)
    {
        var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (modelAsset == null)
        {
            Debug.LogError($"[BackHome] Could not load model asset at {modelPath}");
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[BackHome] === Bone local position/scale for '{modelPath}' ===");
        AppendBoneTransforms(modelAsset.transform, sb, 0);
        Debug.Log(sb.ToString());
    }

    static void AppendBoneTransforms(Transform t, System.Text.StringBuilder sb, int depth)
    {
        sb.AppendLine($"{new string(' ', depth * 2)}{t.name}  pos={t.localPosition:F4}  scale={t.localScale:F4}");
        for (int i = 0; i < t.childCount; i++)
            AppendBoneTransforms(t.GetChild(i), sb, depth + 1);
    }

    static void DumpLivePlayerHierarchy()
    {
        GameObject playerGo = GameObject.FindWithTag("Player");
        bool fromPrefabAsset = false;
        if (playerGo == null)
        {
            // Not in Play mode (or no live Player instance) - fall back to the Player.prefab
            // source asset itself, loaded read-only, which has the exact same hierarchy the
            // Animator plays against at runtime.
            playerGo = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            fromPrefabAsset = true;
        }

        try
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine(fromPrefabAsset
                ? "[BackHome] === Player.prefab transform hierarchy (paths relative to the Animator's GameObject) ==="
                : "[BackHome] === Live Player transform hierarchy (paths relative to the Animator's GameObject) ===");
            AppendHierarchyWithPaths(playerGo.transform, playerGo.transform, sb);
            Debug.Log(sb.ToString());
        }
        finally
        {
            if (fromPrefabAsset)
                PrefabUtility.UnloadPrefabContents(playerGo);
        }
    }

    static void AppendHierarchyWithPaths(Transform root, Transform t, System.Text.StringBuilder sb)
    {
        string relativePath = t == root ? "<root>" : AnimationUtility.CalculateTransformPath(t, root);
        sb.AppendLine(relativePath);
        for (int i = 0; i < t.childCount; i++)
            AppendHierarchyWithPaths(root, t.GetChild(i), sb);
    }

    static void FrameLivePlayerInSceneView()
    {
        var playerGo = GameObject.FindWithTag("Player");
        if (playerGo == null)
        {
            Debug.LogError("[BackHome] Could not find a GameObject tagged 'Player' in the currently loaded scene.");
            return;
        }

        var renderers = playerGo.GetComponentsInChildren<Renderer>(includeInactive: false);
        var sv = SceneView.lastActiveSceneView;
        if (sv == null)
        {
            Debug.LogError("[BackHome] No active SceneView to frame.");
            return;
        }

        if (renderers.Length == 0)
        {
            sv.pivot = playerGo.transform.position;
            sv.size = 1f;
        }
        else
        {
            Bounds bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            sv.pivot = bounds.center;
            sv.size = Mathf.Max(bounds.extents.magnitude * 1.5f, 0.3f);
        }
        sv.Repaint();
        Debug.Log($"[BackHome] Framed SceneView on '{playerGo.name}' at {sv.pivot} (size {sv.size:0.00}, {renderers.Length} renderer(s)).");
    }

    static void PreviewRunOnPlanetClipAtTime(float t)
    {
        var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
        GameObject root;
        if (stage != null && stage.assetPath == PlayerPrefabPath)
        {
            root = stage.prefabContentsRoot;
        }
        else
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[BackHome] Could not load prefab at {PlayerPrefabPath}");
                return;
            }
            AssetDatabase.OpenAsset(prefab);
            stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
            root = stage != null ? stage.prefabContentsRoot : null;
        }

        if (root == null)
        {
            Debug.LogError("[BackHome] Could not open/find the Player prefab stage to preview the clip on.");
            return;
        }

        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(RunInPlaceClipPath);
        if (clip == null)
        {
            Debug.LogError($"[BackHome] Could not find the baked-in-place clip at {RunInPlaceClipPath} to preview.");
            return;
        }

        if (!AnimationMode.InAnimationMode())
            AnimationMode.StartAnimationMode();

        AnimationMode.BeginSampling();
        AnimationMode.SampleAnimationClip(root, clip, t);
        AnimationMode.EndSampling();

        SceneView.RepaintAll();
        Debug.Log($"[BackHome] Sampled '{clip.name}' at t={t:0.00}s / {clip.length:0.00}s on the Player prefab preview.");
    }

    /// <summary>
    /// For every curve in both the raw extracted 'Run' clip and our baked 'RunInPlace' copy,
    /// compares the value at t=0 to the value at the last keyframe. A large mismatch on a curve
    /// means the source take's start pose and end pose don't match on that bone/property, which
    /// is exactly what causes a visible "pop"/snap every time the AnimatorController loops back
    /// to the start of the state.
    /// </summary>
    static void DumpLoopSeam()
    {
        DumpLoopSeamFor(RunOnPlanetModelPath, RunOnPlanetClipName, isSubAsset: true);
        DumpLoopSeamFor(RunInPlaceClipPath, null, isSubAsset: false);
    }

    static void DumpLoopSeamFor(string path, string clipName, bool isSubAsset)
    {
        AnimationClip clip = isSubAsset
            ? AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => c.name == clipName)
            : AssetDatabase.LoadAssetAtPath<AnimationClip>(path);

        if (clip == null)
        {
            Debug.LogError($"[BackHome] DumpLoopSeam: could not load clip from {path}.");
            return;
        }

        var rows = new List<(string label, float delta)>();
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve.keys.Length < 2) continue;
            float startVal = curve.keys[0].value;
            float endVal = curve.keys[curve.keys.Length - 1].value;
            float delta = Mathf.Abs(endVal - startVal);
            rows.Add(($"{binding.path}::{binding.propertyName}", delta));
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[BackHome] === Loop seam for '{clip.name}' ({clip.length:0.000}s) - top 20 start/end mismatches ===");
        foreach (var row in rows.OrderByDescending(r => r.delta).Take(20))
            sb.AppendLine($"{row.label} : delta={row.delta:0.####}");
        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// For every bone's rotation in the baked 'RunInPlace' clip, walks every consecutive pair of
    /// keyframes (including the wrap-around from the last key back to the first) and reports the
    /// angular jump between them. A frame-by-frame check like this catches a mid-blend "flip" or
    /// pop that a start-vs-end-only comparison would miss.
    /// </summary>
    static void DumpFrameByFrameRotationDeltas()
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(RunInPlaceClipPath);
        if (clip == null)
        {
            Debug.LogError($"[BackHome] Could not load {RunInPlaceClipPath}.");
            return;
        }

        var bindings = AnimationUtility.GetCurveBindings(clip);
        var rows = new List<(string bone, int frameIndex, float time, float angleDelta)>();

        foreach (var group in bindings.Where(b => b.type == typeof(Transform) && b.propertyName.StartsWith("m_LocalRotation.")).GroupBy(b => b.path))
        {
            var byProp = group.ToDictionary(b => b.propertyName, b => b);
            if (!byProp.ContainsKey("m_LocalRotation.x") || !byProp.ContainsKey("m_LocalRotation.y") ||
                !byProp.ContainsKey("m_LocalRotation.z") || !byProp.ContainsKey("m_LocalRotation.w"))
                continue;

            var kx = AnimationUtility.GetEditorCurve(clip, byProp["m_LocalRotation.x"]).keys;
            var ky = AnimationUtility.GetEditorCurve(clip, byProp["m_LocalRotation.y"]).keys;
            var kz = AnimationUtility.GetEditorCurve(clip, byProp["m_LocalRotation.z"]).keys;
            var kw = AnimationUtility.GetEditorCurve(clip, byProp["m_LocalRotation.w"]).keys;
            int n = kx.Length;
            if (n < 2) continue;

            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n; // wrap the last frame back to the first
                var qa = new Quaternion(kx[i].value, ky[i].value, kz[i].value, kw[i].value).normalized;
                var qb = new Quaternion(kx[j].value, ky[j].value, kz[j].value, kw[j].value).normalized;
                float angle = Quaternion.Angle(qa, qb);
                rows.Add((group.Key, i, kx[i].time, angle));
            }
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[BackHome] === Frame-to-frame rotation jumps for '{clip.name}' - top 25 largest ===");
        foreach (var row in rows.OrderByDescending(r => r.angleDelta).Take(25))
            sb.AppendLine($"{row.bone} : frame {row.frameIndex} (t={row.time:0.000}s) -> next : {row.angleDelta:0.00} deg");
        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// Reports the true rotation angle (not raw quaternion-component delta) between the first and
    /// last frame of the raw, unmodified 'Run' take for every bone - i.e. how far out of phase the
    /// source take's end pose actually is from its start pose, before any correction is applied.
    /// </summary>
    static void DumpRawSeamAngles()
    {
        var clip = AssetDatabase.LoadAllAssetsAtPath(RunOnPlanetModelPath).OfType<AnimationClip>().FirstOrDefault(c => c.name == RunOnPlanetClipName);
        if (clip == null)
        {
            Debug.LogError($"[BackHome] Could not load '{RunOnPlanetClipName}' from {RunOnPlanetModelPath}.");
            return;
        }

        var bindings = AnimationUtility.GetCurveBindings(clip);
        var rows = new List<(string bone, float angle)>();

        foreach (var group in bindings.Where(b => b.type == typeof(Transform) && b.propertyName.StartsWith("m_LocalRotation.")).GroupBy(b => b.path))
        {
            var byProp = group.ToDictionary(b => b.propertyName, b => b);
            if (!byProp.ContainsKey("m_LocalRotation.x") || !byProp.ContainsKey("m_LocalRotation.y") ||
                !byProp.ContainsKey("m_LocalRotation.z") || !byProp.ContainsKey("m_LocalRotation.w"))
                continue;

            var kx = AnimationUtility.GetEditorCurve(clip, byProp["m_LocalRotation.x"]).keys;
            var ky = AnimationUtility.GetEditorCurve(clip, byProp["m_LocalRotation.y"]).keys;
            var kz = AnimationUtility.GetEditorCurve(clip, byProp["m_LocalRotation.z"]).keys;
            var kw = AnimationUtility.GetEditorCurve(clip, byProp["m_LocalRotation.w"]).keys;
            if (kx.Length < 2) continue;

            var qStart = new Quaternion(kx[0].value, ky[0].value, kz[0].value, kw[0].value).normalized;
            var qEnd = new Quaternion(kx[kx.Length - 1].value, ky[ky.Length - 1].value, kz[kz.Length - 1].value, kw[kw.Length - 1].value).normalized;
            rows.Add((group.Key, Quaternion.Angle(qStart, qEnd)));
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[BackHome] === Raw start-vs-end rotation angle per bone for '{clip.name}' (before correction) ===");
        foreach (var row in rows.OrderByDescending(r => r.angle))
            sb.AppendLine($"{row.bone} : {row.angle:0.0} deg");
        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// The raw take's end pose can be dozens of degrees out of phase from its start pose (not a
    /// rounding error - the captured take likely isn't an exact whole number of gait cycles), which
    /// is why forcing a big correction into just the tail looked like an unnatural "leg swap". This
    /// searches for a candidate end frame (searching the back half of the clip) whose pose is
    /// naturally closest to frame 0 across all bones, so a much shorter loop hiding inside the take
    /// can be used instead of always looping through the full authored duration.
    /// </summary>
    static void FindBestLoopEndFrame()
    {
        var clip = AssetDatabase.LoadAllAssetsAtPath(RunOnPlanetModelPath).OfType<AnimationClip>().FirstOrDefault(c => c.name == RunOnPlanetClipName);
        if (clip == null)
        {
            Debug.LogError($"[BackHome] Could not load '{RunOnPlanetClipName}' from {RunOnPlanetModelPath}.");
            return;
        }

        var candidates = ScoreLoopEndCandidates(clip, out int n, out int groupCount);
        if (candidates == null) return;

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[BackHome] === Best natural loop-end candidates for '{clip.name}' (clip has {n} frames, {clip.length:0.000}s) ===");
        sb.AppendLine($"(comparing every candidate end frame in the back half against frame 0, across {groupCount} bones)");
        foreach (var c in candidates.OrderBy(c => c.totalMismatch).Take(15))
            sb.AppendLine($"frame {c.frame} (t={c.time:0.000}s, duration if used={c.time:0.000}s) : totalMismatch={c.totalMismatch:0.0} deg, worstBone={c.maxMismatch:0.0} deg");
        sb.AppendLine("--- for comparison, the authored last frame ---");
        var last = candidates[candidates.Count - 1];
        sb.AppendLine($"frame {last.frame} (t={last.time:0.000}s) : totalMismatch={last.totalMismatch:0.0} deg, worstBone={last.maxMismatch:0.0} deg");
        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// For every candidate end frame in the back half of <paramref name="clip"/>, scores how close
    /// its pose is to frame 0 across every rotated bone (summed and max angle, in degrees).
    /// </summary>
    static List<(int frame, float time, float totalMismatch, float maxMismatch)> ScoreLoopEndCandidates(AnimationClip clip, out int frameCount, out int boneCount)
    {
        frameCount = 0;
        boneCount = 0;
        var bindings = AnimationUtility.GetCurveBindings(clip);
        var rotationGroups = new List<(Keyframe[] x, Keyframe[] y, Keyframe[] z, Keyframe[] w)>();
        foreach (var group in bindings.Where(b => b.type == typeof(Transform) && b.propertyName.StartsWith("m_LocalRotation.")).GroupBy(b => b.path))
        {
            var byProp = group.ToDictionary(b => b.propertyName, b => b);
            if (!byProp.ContainsKey("m_LocalRotation.x") || !byProp.ContainsKey("m_LocalRotation.y") ||
                !byProp.ContainsKey("m_LocalRotation.z") || !byProp.ContainsKey("m_LocalRotation.w"))
                continue;
            rotationGroups.Add((
                AnimationUtility.GetEditorCurve(clip, byProp["m_LocalRotation.x"]).keys,
                AnimationUtility.GetEditorCurve(clip, byProp["m_LocalRotation.y"]).keys,
                AnimationUtility.GetEditorCurve(clip, byProp["m_LocalRotation.z"]).keys,
                AnimationUtility.GetEditorCurve(clip, byProp["m_LocalRotation.w"]).keys));
        }

        if (rotationGroups.Count == 0 || rotationGroups[0].x.Length < 10)
            return null;

        int n = rotationGroups[0].x.Length;
        frameCount = n;
        boneCount = rotationGroups.Count;

        Quaternion QuatAt(int groupIdx, int frame)
        {
            var g = rotationGroups[groupIdx];
            return new Quaternion(g.x[frame].value, g.y[frame].value, g.z[frame].value, g.w[frame].value).normalized;
        }

        var frame0Quats = new Quaternion[rotationGroups.Count];
        for (int g = 0; g < rotationGroups.Count; g++) frame0Quats[g] = QuatAt(g, 0);

        var candidates = new List<(int frame, float time, float totalMismatch, float maxMismatch)>();
        int searchStart = Mathf.Max(1, (int)(n * 0.5f));
        for (int f = searchStart; f < n; f++)
        {
            float total = 0f, max = 0f;
            for (int g = 0; g < rotationGroups.Count; g++)
            {
                float angle = Quaternion.Angle(frame0Quats[g], QuatAt(g, f));
                total += angle;
                if (angle > max) max = angle;
            }
            candidates.Add((f, rotationGroups[0].x[f].time, total, max));
        }

        return candidates;
    }

    static void DumpRunClipCurves()
    {
        var clip = AssetDatabase.LoadAllAssetsAtPath(RunOnPlanetModelPath).OfType<AnimationClip>().FirstOrDefault(c => c.name == RunOnPlanetClipName);
        if (clip == null)
        {
            Debug.LogError($"[BackHome] Could not find '{RunOnPlanetClipName}' clip inside {RunOnPlanetModelPath}.");
            return;
        }

        var bindings = AnimationUtility.GetCurveBindings(clip);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[BackHome] === Curve bindings for '{clip.name}' ({bindings.Length} curves) ===");
        foreach (var binding in bindings)
        {
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            float min = float.MaxValue, max = float.MinValue;
            foreach (var key in curve.keys)
            {
                if (key.value < min) min = key.value;
                if (key.value > max) max = key.value;
            }
            sb.AppendLine($"path='{binding.path}' prop={binding.propertyName} type={binding.type.Name} range=[{min:0.###}, {max:0.###}] delta={(max - min):0.###} keys={curve.keys.Length}");
        }
        Debug.Log(sb.ToString());
    }

    [MenuItem("BackHome/Player/Diagnose Avatars (Debug)")]
    public static void DiagnoseAvatars()
    {
        DumpModelInfo(NewModelPath, "Player.fbx");
        DumpModelInfo(AnimLibraryPath, "Player_Running.fbx");
    }

    static void DumpModelInfo(string path, string label)
    {
        var importer = AssetImporter.GetAtPath(path) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"[BackHome] [{label}] No ModelImporter at {path}");
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[BackHome] === {label} ===");
        sb.AppendLine($"animationType={importer.animationType} avatarSetup={importer.avatarSetup}");

        var avatar = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Avatar>().FirstOrDefault();
        sb.AppendLine(avatar == null
            ? "Avatar: <null>"
            : $"Avatar: isValid={avatar.isValid} isHuman={avatar.isHuman}");

        var human = importer.humanDescription.human;
        sb.AppendLine($"Human bone map ({(human?.Length ?? 0)} entries):");
        if (human != null)
            foreach (var h in human)
                sb.AppendLine($"  {h.humanName} -> {h.boneName}");

        var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        sb.AppendLine("Transform hierarchy:");
        if (modelAsset != null)
            AppendHierarchy(modelAsset.transform, sb, 1);

        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// Wires the actual character texture (Player_texture.png as albedo, Player_normal.png as the
    /// normal map) into Material_1.mat - the material shared by every Meshy_AI_*.fbx export in
    /// Model/ - and then forces that material onto every renderer of the model that's actually
    /// active inside Player.prefab. This is separate from <see cref="FixPlayerMaterial"/> above
    /// (which targets the older, now-unused Player.fbx path and assumed a URP/Lit metallic
    /// workflow); Material_1's shader is the project's custom "BackHome/CasualToon" toon shader,
    /// which has no metallic/smoothness input, so only the albedo + normal map are meaningful here.
    /// </summary>
    [MenuItem("BackHome/Player/Fix Player Character Texture")]
    public static void FixPlayerCharacterTexture()
    {
        try
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(CharacterMaterialPath);
            if (mat == null)
            {
                Debug.LogError($"[BackHome] Could not load material at {CharacterMaterialPath}");
                return;
            }

            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath);
            if (albedo == null)
            {
                Debug.LogError($"[BackHome] Could not load albedo texture at {AlbedoPath}");
                return;
            }

            EnsureNormalMapImport(NormalMapPath);
            var normalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalMapPath);

            mat.SetTexture("_BaseMap", albedo);
            if (normalTex != null)
                mat.SetTexture("_BumpMap", normalTex);
            mat.SetFloat("_BumpScale", 1f);
            // Clear any stray keyword state left over from when this material was briefly treated
            // as URP/Lit (e.g. _NORMALMAP/_METALLICSPECGLOSSMAP) - CasualToon doesn't declare or
            // read any shader_feature keywords, so an empty keyword set is the correct state.
            mat.shaderKeywords = new string[0];
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();

            int renderersFixed = ForceMaterialOnActivePlayerModel(mat);

            AssetDatabase.Refresh();
            Debug.Log($"[BackHome] Wired Player_texture/Player_normal into {CharacterMaterialPath} and applied it to {renderersFixed} renderer(s) on the Player's active model.");
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            Debug.LogError("[BackHome] Fix-Player-Character-Texture aborted due to the exception above.");
        }
    }

    /// <summary>
    /// Opens Player.prefab's contents in memory, finds the specific nested model instance that
    /// holds the live running character (named after <see cref="RunOnPlanetModelPath"/> - Player
    /// .prefab also keeps a disabled "Model" instance and several unrelated renderers around,
    /// e.g. VitalsBars/UI icons, which must NOT be touched), and force-assigns
    /// <paramref name="mat"/> onto every slot of every Renderer under just that instance,
    /// regardless of whatever the FBX's own material import settings would otherwise resolve to.
    /// Returns how many renderers were touched.
    /// </summary>
    static int ForceMaterialOnActivePlayerModel(Material mat)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            string modelInstanceName = System.IO.Path.GetFileNameWithoutExtension(RunOnPlanetModelPath);
            Transform modelInstance = FindActiveChildByName(root.transform, modelInstanceName);
            if (modelInstance == null)
            {
                Debug.LogError($"[BackHome] Could not find an active '{modelInstanceName}' instance under Player.prefab - aborting to avoid touching unrelated renderers.");
                return 0;
            }

            var renderers = modelInstance.GetComponentsInChildren<Renderer>(includeInactive: false);
            int count = 0;
            foreach (var renderer in renderers)
            {
                if (!(renderer is SkinnedMeshRenderer) && !(renderer is MeshRenderer))
                    continue;

                var mats = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] != mat)
                    {
                        mats[i] = mat;
                        changed = true;
                    }
                }
                if (mats.Length == 0)
                {
                    mats = new[] { mat };
                    changed = true;
                }
                if (changed)
                {
                    renderer.sharedMaterials = mats;
                    count++;
                }
            }

            if (count > 0)
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);

            return count;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static Transform FindActiveChildByName(Transform root, string name)
    {
        if (!root.gameObject.activeSelf)
            return null;
        if (root.name == name)
            return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindActiveChildByName(root.GetChild(i), name);
            if (found != null)
                return found;
        }
        return null;
    }

    [MenuItem("BackHome/Player/Fix Player Model Material")]
    public static void FixPlayerMaterial()
    {
        try
        {
            string shaderPath = AssetDatabase.GUIDToAssetPath(LitShaderGuid);
            var shader = string.IsNullOrEmpty(shaderPath) ? null : AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
            if (shader == null)
            {
                Debug.LogError("[BackHome] Could not find the project's URP/Lit shader. Aborting material fix.");
                return;
            }

            EnsureNormalMapImport(NormalMapPath);

            Texture2D combined = BuildMetallicSmoothnessMap(MetallicPath, RoughnessPath);
            SaveCombinedTexture(combined, CombinedMetallicSmoothnessPath);

            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(AlbedoPath);
            var normalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(NormalMapPath);
            var metallicSmoothness = AssetDatabase.LoadAssetAtPath<Texture2D>(CombinedMetallicSmoothnessPath);

            var embeddedMaterials = AssetDatabase.LoadAllAssetsAtPath(NewModelPath).OfType<Material>().ToList();
            if (embeddedMaterials.Count == 0)
            {
                Debug.LogWarning("[BackHome] No material found on the new Player model - nothing to fix.");
                return;
            }

            var modelImporter = AssetImporter.GetAtPath(NewModelPath) as ModelImporter;
            var namesToRemap = new List<string>();

            int fixedCount = 0;
            foreach (var embedded in embeddedMaterials)
            {
                string materialName = embedded.name;
                string matPath = $"Assets/Resources/Player/Model/{materialName}.mat";
                string fullMatPath = System.IO.Path.Combine(Application.dataPath, "..", matPath);
                if (AssetDatabase.IsSubAsset(embedded) && !System.IO.File.Exists(fullMatPath))
                {
                    string error = AssetDatabase.ExtractAsset(embedded, matPath);
                    if (!string.IsNullOrEmpty(error))
                        Debug.LogWarning($"[BackHome] Could not extract material '{materialName}': {error}");
                }

                var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                if (mat == null)
                    mat = embedded; // fall back to editing the embedded one directly if extraction wasn't possible/needed
                else
                    namesToRemap.Add(materialName); // extracted to a standalone asset -> the model importer needs to be told to use it instead of regenerating an embedded one

                mat.shader = shader;
                mat.SetTexture("_BaseMap", albedo);
                mat.SetTexture("_BumpMap", normalTex);
                mat.EnableKeyword("_NORMALMAP");
                mat.SetTexture("_MetallicGlossMap", metallicSmoothness);
                mat.EnableKeyword("_METALLICSPECGLOSSMAP");
                mat.SetFloat("_Metallic", 1f);
                mat.SetFloat("_Smoothness", 1f);
                EditorUtility.SetDirty(mat);
                fixedCount++;
            }

            AssetDatabase.SaveAssets();

            if (modelImporter != null && namesToRemap.Count > 0)
            {
                foreach (var materialName in namesToRemap)
                {
                    string matPath = $"Assets/Resources/Player/Model/{materialName}.mat";
                    var extractedMat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
                    if (extractedMat == null) continue;

                    var identifier = new AssetImporter.SourceAssetIdentifier(typeof(Material), materialName);
                    modelImporter.AddRemap(identifier, extractedMat);
                }
                modelImporter.SaveAndReimport();
                Debug.Log("[BackHome] Remapped the model importer to use the extracted material(s) instead of regenerating embedded ones.");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[BackHome] Wired albedo/normal/metallic-smoothness textures into {fixedCount} material(s) on the new Player model.");
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
        }
    }

    static void EnsureNormalMapImport(string path)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer == null) return;
        if (importer.textureType != TextureImporterType.NormalMap)
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
        }
    }

    static Texture2D LoadReadableLinear(string path)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer != null && (!importer.isReadable || importer.sRGBTexture))
        {
            importer.isReadable = true;
            importer.sRGBTexture = false;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    static Texture2D BuildMetallicSmoothnessMap(string metallicPath, string roughnessPath)
    {
        var metallicTex = LoadReadableLinear(metallicPath);
        var roughnessTex = LoadReadableLinear(roughnessPath);

        int width = Mathf.Max(metallicTex.width, roughnessTex.width);
        int height = Mathf.Max(metallicTex.height, roughnessTex.height);

        var result = new Texture2D(width, height, TextureFormat.RGBA32, false);
        for (int y = 0; y < height; y++)
        {
            float v = (y + 0.5f) / height;
            for (int x = 0; x < width; x++)
            {
                float u = (x + 0.5f) / width;
                float metallic = metallicTex.GetPixelBilinear(u, v).r;
                float roughness = roughnessTex.GetPixelBilinear(u, v).r;
                float smoothness = 1f - roughness;
                result.SetPixel(x, y, new Color(metallic, metallic, metallic, smoothness));
            }
        }
        result.Apply();
        return result;
    }

    static void SaveCombinedTexture(Texture2D tex, string assetPath)
    {
        byte[] png = tex.EncodeToPNG();
        string fullPath = System.IO.Path.Combine(Application.dataPath, "..", assetPath);
        System.IO.File.WriteAllBytes(fullPath, png);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

        var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
        if (importer != null)
        {
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = false;
            importer.SaveAndReimport();
        }
    }

    [MenuItem("BackHome/Player/Preview Player Prefab")]
    public static void PreviewPlayerPrefab()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"[BackHome] Could not load prefab at {PlayerPrefabPath}");
            return;
        }
        AssetDatabase.OpenAsset(prefab);
        EditorApplication.delayCall += () => EditorApplication.delayCall += FrameStageDelayed;
    }

    [MenuItem("BackHome/Player/Swap Player Model To New Model")]
    public static void SwapPlayerModel()
    {
        try
        {
            AnimationClip newRunClip = ConfigureNewModelAsHumanoidAndGetRunClip();

            if (!SwapPrefabModel())
                return;

            if (newRunClip != null)
                SwapRunAnimation(newRunClip);

            AssetDatabase.SaveAssets();
            Debug.Log("[BackHome] Player model swap finished. Open Player.prefab and eyeball the mesh/materials, then test Play mode.");
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            Debug.LogError("[BackHome] Player model swap aborted due to the exception above. No prefab changes were saved if the error happened before the save step.");
        }
    }

    /// <summary>
    /// Swaps the running motion in the locomotion blend tree for the custom "running on a
    /// spherical planet" take that lives inside Player_Running.fbx (alongside Alert, Fall,
    /// Walking, etc.), replacing whatever clip is currently wired up as "Run" (either the
    /// original asset-store clip or the one previously extracted from Player.fbx).
    /// </summary>
    [MenuItem("BackHome/Player/Swap Run Animation To Planet Clip")]
    public static void SwapRunAnimationToPlanetClip()
    {
        try
        {
            AnimationClip newRunClip = ConfigureAnimLibraryAsHumanoidAndGetClip(PlanetRunTakeNameFragment, PlanetRunClipName);
            if (newRunClip == null)
                return;

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                Debug.LogError($"[BackHome] Could not load AnimatorController at {ControllerPath}");
                return;
            }

            // Whatever is currently sitting in the "Run" slot of the main locomotion blend tree -
            // could be the clip previously swapped in from Player.fbx, or (if this is being re-run)
            // the RunOnPlanet clip itself, which gets updated in place via the reimport above.
            var candidateOldClips = new List<AnimationClip>();
            var fromNewModel = AssetDatabase.LoadAllAssetsAtPath(NewModelPath).OfType<AnimationClip>().FirstOrDefault(c => c.name == "Run");
            if (fromNewModel != null)
                candidateOldClips.Add(fromNewModel);

            bool replaced = false;
            foreach (var layer in controller.layers)
                foreach (var oldClip in candidateOldClips)
                    replaced |= ReplaceInStateMachine(layer.stateMachine, oldClip, newRunClip);

            if (replaced)
            {
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssets();
                Debug.Log($"[BackHome] Replaced the running motion in the locomotion blend tree with the '{PlanetRunClipName}' clip ({newRunClip.length:0.00}s).");
            }
            else
            {
                Debug.LogWarning("[BackHome] Could not find the currently-wired running clip inside the AnimatorController's blend tree - it was not replaced automatically.");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            Debug.LogError("[BackHome] Planet run animation swap aborted due to the exception above.");
        }
    }

    /// <summary>
    /// The old shared Player.fbx model and StarterAssetsThirdPerson.controller were deleted from
    /// the project (the Player.prefab's "Model" nested-prefab reference and Animator.controller
    /// reference are now broken/missing). This rebuilds the Player around
    /// Meshy_AI_Little_Starbot_biped_Animation_Run_On_Planet.fbx instead: configures it as a
    /// Humanoid model with its own Avatar, extracts its baked-in take as a looping "Run" clip,
    /// swaps it in as the prefab's Model, and creates a fresh AnimatorController whose sole state
    /// plays that clip (its speed driven by the "MotionSpeed" parameter, same as PlanetWalker
    /// already feeds it) so the character runs using this animation.
    /// </summary>
    [MenuItem("BackHome/Player/Setup Player To Run On Planet Model")]
    public static void SetupPlayerRunOnPlanet()
    {
        try
        {
            AnimationClip runClip = ConfigureModelAsHumanoidAndExtractLoopingClip(RunOnPlanetModelPath, RunOnPlanetClipName, RunInPlaceClipPath);
            if (runClip == null)
                return;

            if (!SwapPrefabModelTo(RunOnPlanetModelPath))
                return;

            if (!RebuildLocomotionController(runClip))
                return;

            AssetDatabase.SaveAssets();
            Debug.Log("[BackHome] Player now uses the Run On Planet model and its baked-in animation as the running motion. Test Play mode.");
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            Debug.LogError("[BackHome] Setup-Player-Run-On-Planet aborted due to the exception above.");
        }
    }

    /// <summary>
    /// Extracts looping in-place clips from Starbot_Animation_Idel1.fbx / Idel2.fbx (same rig as
    /// the Run On Planet model, same treatment: Generic rig, uncompressed, hips-locked, seam-
    /// smoothed - see <see cref="ConfigureModelAsHumanoidAndExtractLoopingClip"/>) and wires them
    /// into the shared AnimatorController as two new states, "Idle1" and "Idle2", that play while
    /// the player is standing still: a 50/50 random pick of which one starts, then the state
    /// machine itself alternates to the other one every 4 loops, back and forth forever, purely
    /// via built-in "Exit Time" transitions (no runtime scripting needed for the alternation).
    /// PlanetWalker/TouchController set the "Moving" bool each frame and roll a fresh 50/50
    /// "IdleVariant" (0 or 1) every time the player comes to a stop.
    /// </summary>
    [MenuItem("BackHome/Player/Setup Player Idle Animations")]
    public static void SetupPlayerIdleAnimations()
    {
        try
        {
            AnimationClip idle1Clip = ConfigureModelAsHumanoidAndExtractLoopingClip(Idle1ModelPath, Idle1ClipName, Idle1InPlaceClipPath, IdleSourceRootBoneName);
            if (idle1Clip == null)
                return;

            AnimationClip idle2Clip = ConfigureModelAsHumanoidAndExtractLoopingClip(Idle2ModelPath, Idle2ClipName, Idle2InPlaceClipPath, IdleSourceRootBoneName);
            if (idle2Clip == null)
                return;

            RemapClipRootBoneName(idle1Clip, IdleSourceRootBoneName, IdleTargetRootBoneName);
            RemapClipRootBoneName(idle2Clip, IdleSourceRootBoneName, IdleTargetRootBoneName);

            if (!WireIdleStatesIntoController(idle1Clip, idle2Clip))
                return;

            AssetDatabase.SaveAssets();
            Debug.Log("[BackHome] Player idle animations wired up: 50/50 random start between Idle1/Idle2, alternating every 4 loops while standing still.");
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            Debug.LogError("[BackHome] Setup-Player-Idle-Animations aborted due to the exception above.");
        }
    }

    /// <summary>
    /// Rewrites every curve (and object-reference curve) in <paramref name="clip"/> whose path is
    /// exactly <paramref name="oldRootName"/> or starts with "<paramref name="oldRootName"/>/" so
    /// that leading path segment becomes <paramref name="newRootName"/> instead - i.e. re-roots
    /// the whole clip onto a differently-named top bone without touching anything below it.
    /// </summary>
    static void RemapClipRootBoneName(AnimationClip clip, string oldRootName, string newRootName)
    {
        string oldPrefix = oldRootName + "/";

        string Remap(string path) => path == oldRootName
            ? newRootName
            : newRootName + path.Substring(oldRootName.Length);

        bool IsRootItself(string path) => path == oldRootName;
        bool IsChildOfRoot(string path) => path.StartsWith(oldPrefix, System.StringComparison.Ordinal);

        // Curves keyed directly on the root bone (not its children) encode this source rig's own
        // export-time axis/position convention (e.g. a baked -90 degree tilt some exporters add to
        // the armature object). The live "target_character" root is never animated by the Run clip -
        // its orientation/position is owned by the Player's own transform (which PlanetWalker aligns
        // to the planet surface) - so keeping the source rig's root curves here would fight that and
        // visibly tip/sink the character into the ground. Drop them entirely; only remap the children.
        int remapped = 0;
        int dropped = 0;
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            if (IsRootItself(binding.path))
            {
                AnimationUtility.SetEditorCurve(clip, binding, null);
                dropped++;
                continue;
            }
            if (!IsChildOfRoot(binding.path))
                continue;

            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            var newBinding = binding;
            newBinding.path = Remap(binding.path);

            AnimationUtility.SetEditorCurve(clip, binding, null);
            AnimationUtility.SetEditorCurve(clip, newBinding, curve);
            remapped++;
        }

        foreach (var binding in AnimationUtility.GetObjectReferenceCurveBindings(clip))
        {
            if (IsRootItself(binding.path))
            {
                AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
                dropped++;
                continue;
            }
            if (!IsChildOfRoot(binding.path))
                continue;

            var curve = AnimationUtility.GetObjectReferenceCurve(clip, binding);
            var newBinding = binding;
            newBinding.path = Remap(binding.path);

            AnimationUtility.SetObjectReferenceCurve(clip, binding, null);
            AnimationUtility.SetObjectReferenceCurve(clip, newBinding, curve);
            remapped++;
        }

        EditorUtility.SetDirty(clip);
        Debug.Log($"[BackHome] Re-rooted {remapped} curve(s) in '{clip.name}' from '{oldRootName}' to '{newRootName}' (dropped {dropped} root-level curve(s) to avoid fighting the Player's own orientation/position).");
    }

    /// <summary>
    /// Adds/refreshes the "Idle1"/"Idle2" states and the "Moving"/"IdleVariant" parameters on the
    /// shared locomotion AnimatorController. Safe to re-run: existing Idle1/Idle2 states and any
    /// transitions touching Run/Idle1/Idle2 are rebuilt from scratch each time.
    /// </summary>
    static bool WireIdleStatesIntoController(AnimationClip idle1Clip, AnimationClip idle2Clip)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError($"[BackHome] Could not load AnimatorController at {ControllerPath}");
            return false;
        }

        AnimatorStateMachine rootStateMachine = controller.layers[0].stateMachine;

        AnimatorState runState = rootStateMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == "Run");
        if (runState == null)
        {
            Debug.LogError("[BackHome] Could not find the 'Run' state in the AnimatorController - run 'Setup Player To Run On Planet Model' first.");
            return false;
        }

        if (!controller.parameters.Any(p => p.name == "Moving"))
            controller.AddParameter("Moving", AnimatorControllerParameterType.Bool);
        if (!controller.parameters.Any(p => p.name == "IdleVariant"))
            controller.AddParameter("IdleVariant", AnimatorControllerParameterType.Int);

        AnimatorState idle1State = rootStateMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == "Idle1")
            ?? rootStateMachine.AddState("Idle1", new Vector3(400, -60, 0));
        idle1State.motion = idle1Clip;
        idle1State.speedParameterActive = false;
        idle1State.speed = 1f;

        AnimatorState idle2State = rootStateMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == "Idle2")
            ?? rootStateMachine.AddState("Idle2", new Vector3(400, 160, 0));
        idle2State.motion = idle2Clip;
        idle2State.speedParameterActive = false;
        idle2State.speed = 1f;

        // Rebuild every transition touching Run/Idle1/Idle2 from scratch so re-running this tool
        // never leaves stale/duplicate transitions behind.
        foreach (var t in rootStateMachine.anyStateTransitions
                     .Where(t => t.destinationState == runState || t.destinationState == idle1State || t.destinationState == idle2State)
                     .ToList())
            rootStateMachine.RemoveAnyStateTransition(t);
        foreach (var t in idle1State.transitions.ToList())
            idle1State.RemoveTransition(t);
        foreach (var t in idle2State.transitions.ToList())
            idle2State.RemoveTransition(t);

        const float switchDuration = 0.15f;

        AnimatorStateTransition toIdle1 = rootStateMachine.AddAnyStateTransition(idle1State);
        toIdle1.hasExitTime = false;
        toIdle1.hasFixedDuration = true;
        toIdle1.duration = switchDuration;
        toIdle1.canTransitionToSelf = false;
        toIdle1.AddCondition(AnimatorConditionMode.IfNot, 0, "Moving");
        toIdle1.AddCondition(AnimatorConditionMode.Equals, 0, "IdleVariant");

        AnimatorStateTransition toIdle2 = rootStateMachine.AddAnyStateTransition(idle2State);
        toIdle2.hasExitTime = false;
        toIdle2.hasFixedDuration = true;
        toIdle2.duration = switchDuration;
        toIdle2.canTransitionToSelf = false;
        toIdle2.AddCondition(AnimatorConditionMode.IfNot, 0, "Moving");
        toIdle2.AddCondition(AnimatorConditionMode.Equals, 1, "IdleVariant");

        AnimatorStateTransition toRun = rootStateMachine.AddAnyStateTransition(runState);
        toRun.hasExitTime = false;
        toRun.hasFixedDuration = true;
        toRun.duration = switchDuration;
        toRun.canTransitionToSelf = false;
        toRun.AddCondition(AnimatorConditionMode.If, 0, "Moving");

        // The actual "loop N times, then swap to the other idle" alternation - no scripting
        // needed, an exit time > 1 on a looping state fires after that many full loops (integer
        // part = loop count, .0 = right at the loop boundary). Idle2 is the "rare" variant, so
        // even when it does get picked it only plays once before falling back to Idle1 - it never
        // repeats 4 times in a row the way Idle1 does.
        AnimatorStateTransition idle1ToIdle2 = idle1State.AddTransition(idle2State);
        idle1ToIdle2.hasExitTime = true;
        idle1ToIdle2.exitTime = 4f;
        idle1ToIdle2.hasFixedDuration = true;
        idle1ToIdle2.duration = switchDuration;

        AnimatorStateTransition idle2ToIdle1 = idle2State.AddTransition(idle1State);
        idle2ToIdle1.hasExitTime = true;
        idle2ToIdle1.exitTime = 1f;
        idle2ToIdle1.hasFixedDuration = true;
        idle2ToIdle1.duration = switchDuration;

        EditorUtility.SetDirty(controller);
        Debug.Log("[BackHome] Added/refreshed Idle1 <-> Idle2 states, Moving/IdleVariant parameters, and their transitions in the AnimatorController.");
        return true;
    }

    /// <summary>
    /// Configures the model at <paramref name="modelPath"/> as a Generic-rig model (its own
    /// skeleton, no Humanoid muscle retargeting - Humanoid retargeting was visibly distorting the
    /// pose away from how Meshy's own preview renders it) with animation compression disabled (so
    /// Unity doesn't lossily reduce keyframes on import), then extracts its (single) embedded take
    /// as a looping AnimationClip named <paramref name="clipName"/>, baked in-place at
    /// <paramref name="inPlaceOutputPath"/> (see <see cref="MakeInPlaceCopy"/>).
    /// </summary>
    static AnimationClip ConfigureModelAsHumanoidAndExtractLoopingClip(string modelPath, string clipName, string inPlaceOutputPath, string rootBoneNameForRotationAbsorb = null)
    {
        var importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"[BackHome] Could not find a ModelImporter at {modelPath}");
            return null;
        }

        importer.animationType = ModelImporterAnimationType.Generic;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.optimizeGameObjects = false;
        importer.animationCompression = ModelImporterAnimationCompression.Off;
        importer.SaveAndReimport();

        var avatar = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().FirstOrDefault();
        if (avatar == null || !avatar.isValid)
        {
            Debug.LogError($"[BackHome] Unity could not generate a valid Avatar for {modelPath}. " +
                "Select it, open the Rig tab, and check for errors there, then re-run this tool.");
            return null;
        }

        var takes = importer.importedTakeInfos;
        if (takes == null || takes.Length == 0)
        {
            Debug.LogError($"[BackHome] {modelPath} has no embedded animation take.");
            return null;
        }

        var take = takes[0];
        var clipAnim = new ModelImporterClipAnimation
        {
            name = clipName,
            takeName = take.name,
            firstFrame = take.startTime * take.sampleRate,
            lastFrame = take.stopTime * take.sampleRate,
            loopTime = true,
            loopPose = true,
            wrapMode = WrapMode.Loop,
        };
        importer.clipAnimations = new[] { clipAnim };
        importer.SaveAndReimport();

        var newClip = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<AnimationClip>().FirstOrDefault(c => c.name == clipName);
        if (newClip == null)
        {
            Debug.LogError($"[BackHome] Could not locate the extracted '{clipName}' clip after reimport.");
            return null;
        }

        Debug.Log($"[BackHome] Extracted '{clipName}' clip ({newClip.length:0.00}s) from {modelPath} (Generic rig, uncompressed).");

        if (!string.IsNullOrEmpty(rootBoneNameForRotationAbsorb))
        {
            // This source rig's bones are authored at "real" scale (its own root has no
            // compensating scale), but the live Player skeleton's root ("target_character") has a
            // small local scale baked into its bind pose to normalize a different rig convention
            // (see FindTargetCharacterScale). Every bone in this clip needs its position curves
            // scaled up by the inverse of that so they land at the correct size once the live
            // root's scale is applied on top at playback - otherwise the whole pose collapses
            // toward the root's origin (the character appears to sink into the ground).
            float liveRootScale = FindTargetCharacterScale(IdleTargetRootBoneName);
            if (liveRootScale > 0f && Mathf.Abs(liveRootScale - 1f) > 0.0001f)
                ScaleAllPositionCurves(newClip, 1f / liveRootScale);

            AbsorbRootRotationIntoHips(newClip, rootBoneNameForRotationAbsorb);
        }

        return MakeInPlaceCopy(newClip, inPlaceOutputPath);
    }

    /// <summary>
    /// Reads the live Player skeleton's root bone local scale (e.g. "target_character"), used to
    /// figure out how much a differently-scaled source rig's position curves need to be corrected
    /// by before they're transplanted onto that skeleton. Returns 1 (no-op) if it can't be found.
    /// </summary>
    static float FindTargetCharacterScale(string rootBoneName)
    {
        GameObject playerGo = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            Transform found = FindDescendant(playerGo.transform, rootBoneName);
            if (found == null)
            {
                Debug.LogWarning($"[BackHome] Could not find '{rootBoneName}' under {PlayerPrefabPath} to read its scale - skipping position rescale.");
                return 1f;
            }
            return found.localScale.x;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(playerGo);
        }
    }

    static Transform FindDescendant(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindDescendant(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }

    // Multiplies every m_LocalPosition.x/y/z curve in the clip (regardless of bone path) by a
    // fixed factor. Uniform scaling is linear, so tangents scale the same way values do.
    static void ScaleAllPositionCurves(AnimationClip clip, float factor)
    {
        int scaled = 0;
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            if (binding.type != typeof(Transform) || !binding.propertyName.StartsWith("m_LocalPosition."))
                continue;

            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            var keys = curve.keys;
            for (int i = 0; i < keys.Length; i++)
            {
                keys[i].value *= factor;
                keys[i].inTangent *= factor;
                keys[i].outTangent *= factor;
            }
            curve.keys = keys;
            AnimationUtility.SetEditorCurve(clip, binding, curve);
            scaled++;
        }
        Debug.Log($"[BackHome] Rescaled {scaled} position curve(s) in '{clip.name}' by x{factor:0.###} to match the live skeleton's scale.");
    }

    /// <summary>
    /// Some source rigs (this project's Idle1/Idle2 FBX exports) bake a static reorientation
    /// (a -90 degree tilt, likely a Z-up-to-Y-up compensation from the authoring tool) directly
    /// onto the root bone itself instead of into the mesh's bind pose. Left as a live curve on the
    /// shared "target_character" root, that rotation would tip the whole shared skeleton/mesh over
    /// every time the clip plays - the Run clip never touches the root at all, so the shared bind
    /// pose assumes an identity root rotation, and the root's own orientation is otherwise owned by
    /// the Player's transform (which aligns it to the planet surface). This absorbs that fixed
    /// rotation into the single bone directly beneath the root (this project's rigs are all
    /// single-rooted at "Hips") by pre-rotating its position/rotation keys with the same transform,
    /// then discards the root's own curves - the resulting pose is identical, but the shared root
    /// is left untouched. No-ops if the root isn't actually animated (e.g. when called on Run).
    /// </summary>
    static void AbsorbRootRotationIntoHips(AnimationClip clip, string rootBoneName, string childBoneName = "Hips")
    {
        var bindings = AnimationUtility.GetCurveBindings(clip).ToList();

        EditorCurveBinding? Find(string path, string prop) =>
            bindings.Where(b => b.path == path && b.propertyName == prop)
                    .Select(b => (EditorCurveBinding?)b)
                    .FirstOrDefault();

        var rxB = Find(rootBoneName, "m_LocalRotation.x");
        var ryB = Find(rootBoneName, "m_LocalRotation.y");
        var rzB = Find(rootBoneName, "m_LocalRotation.z");
        var rwB = Find(rootBoneName, "m_LocalRotation.w");
        if (rxB == null || ryB == null || rzB == null || rwB == null)
            return; // Root isn't animated at all (e.g. Run's rig) - nothing to absorb.

        var rxCurve = AnimationUtility.GetEditorCurve(clip, rxB.Value);
        var ryCurve = AnimationUtility.GetEditorCurve(clip, ryB.Value);
        var rzCurve = AnimationUtility.GetEditorCurve(clip, rzB.Value);
        var rwCurve = AnimationUtility.GetEditorCurve(clip, rwB.Value);
        var rootRotation = new Quaternion(rxCurve.keys[0].value, ryCurve.keys[0].value, rzCurve.keys[0].value, rwCurve.keys[0].value).normalized;

        if (Quaternion.Angle(Quaternion.identity, rootRotation) < 0.01f)
            return; // Effectively identity - nothing to do.

        string childPath = rootBoneName + "/" + childBoneName;

        var posX = Find(childPath, "m_LocalPosition.x");
        var posY = Find(childPath, "m_LocalPosition.y");
        var posZ = Find(childPath, "m_LocalPosition.z");
        if (posX != null && posY != null && posZ != null)
        {
            var cx = AnimationUtility.GetEditorCurve(clip, posX.Value);
            var cy = AnimationUtility.GetEditorCurve(clip, posY.Value);
            var cz = AnimationUtility.GetEditorCurve(clip, posZ.Value);
            RotatePositionCurves(cx, cy, cz, rootRotation);
            AnimationUtility.SetEditorCurve(clip, posX.Value, cx);
            AnimationUtility.SetEditorCurve(clip, posY.Value, cy);
            AnimationUtility.SetEditorCurve(clip, posZ.Value, cz);
        }

        var rotX = Find(childPath, "m_LocalRotation.x");
        var rotY = Find(childPath, "m_LocalRotation.y");
        var rotZ = Find(childPath, "m_LocalRotation.z");
        var rotW = Find(childPath, "m_LocalRotation.w");
        if (rotX != null && rotY != null && rotZ != null && rotW != null)
        {
            var cx = AnimationUtility.GetEditorCurve(clip, rotX.Value);
            var cy = AnimationUtility.GetEditorCurve(clip, rotY.Value);
            var cz = AnimationUtility.GetEditorCurve(clip, rotZ.Value);
            var cw = AnimationUtility.GetEditorCurve(clip, rotW.Value);
            PreMultiplyRotationCurves(cx, cy, cz, cw, rootRotation);
            AnimationUtility.SetEditorCurve(clip, rotX.Value, cx);
            AnimationUtility.SetEditorCurve(clip, rotY.Value, cy);
            AnimationUtility.SetEditorCurve(clip, rotZ.Value, cz);
            AnimationUtility.SetEditorCurve(clip, rotW.Value, cw);
        }

        foreach (var b in bindings.Where(b => b.path == rootBoneName))
            AnimationUtility.SetEditorCurve(clip, b, null);

        Debug.Log($"[BackHome] Absorbed a {Quaternion.Angle(Quaternion.identity, rootRotation):0} degree root rotation baked onto " +
            $"'{rootBoneName}' into '{childPath}' instead, so the shared skeleton's root stays untouched during '{clip.name}'.");
    }

    // Rotates a Vector3 curve (split across 3 float curves) by a fixed rotation. Since the rotation
    // is constant (not time-varying), tangents transform the same linear way as the values do.
    static void RotatePositionCurves(AnimationCurve cx, AnimationCurve cy, AnimationCurve cz, Quaternion rotation)
    {
        var kx = cx.keys;
        var ky = cy.keys;
        var kz = cz.keys;
        int n = kx.Length;
        if (ky.Length != n || kz.Length != n) return;

        for (int i = 0; i < n; i++)
        {
            var v = rotation * new Vector3(kx[i].value, ky[i].value, kz[i].value);
            var tIn = rotation * new Vector3(kx[i].inTangent, ky[i].inTangent, kz[i].inTangent);
            var tOut = rotation * new Vector3(kx[i].outTangent, ky[i].outTangent, kz[i].outTangent);

            kx[i].value = v.x; ky[i].value = v.y; kz[i].value = v.z;
            kx[i].inTangent = tIn.x; ky[i].inTangent = tIn.y; kz[i].inTangent = tIn.z;
            kx[i].outTangent = tOut.x; ky[i].outTangent = tOut.y; kz[i].outTangent = tOut.z;
        }

        cx.keys = kx;
        cy.keys = ky;
        cz.keys = kz;
    }

    // Pre-multiplies a quaternion curve (split across 4 float curves) by a fixed rotation.
    // Left-multiplication by a constant quaternion is a linear map on the 4 components, so
    // tangents transform the same way the values do (same reasoning as RotatePositionCurves).
    static void PreMultiplyRotationCurves(AnimationCurve cx, AnimationCurve cy, AnimationCurve cz, AnimationCurve cw, Quaternion rotation)
    {
        var kx = cx.keys;
        var ky = cy.keys;
        var kz = cz.keys;
        var kw = cw.keys;
        int n = kx.Length;
        if (ky.Length != n || kz.Length != n || kw.Length != n) return;

        for (int i = 0; i < n; i++)
        {
            var q = rotation * new Quaternion(kx[i].value, ky[i].value, kz[i].value, kw[i].value);
            var tIn = rotation * new Quaternion(kx[i].inTangent, ky[i].inTangent, kz[i].inTangent, kw[i].inTangent);
            var tOut = rotation * new Quaternion(kx[i].outTangent, ky[i].outTangent, kz[i].outTangent, kw[i].outTangent);

            kx[i].value = q.x; ky[i].value = q.y; kz[i].value = q.z; kw[i].value = q.w;
            kx[i].inTangent = tIn.x; ky[i].inTangent = tIn.y; kz[i].inTangent = tIn.z; kw[i].inTangent = tIn.w;
            kx[i].outTangent = tOut.x; ky[i].outTangent = tOut.y; kz[i].outTangent = tOut.z; kw[i].outTangent = tOut.w;
        }

        cx.keys = kx;
        cy.keys = ky;
        cz.keys = kz;
        cw.keys = kw;
    }

    /// <summary>
    /// Meshy's baked-in "run" take moves the Hips bone hundreds of units forward/sideways over
    /// the clip (real root motion baked directly onto a child bone, not a dedicated root node),
    /// so simply playing the raw clip made the character visibly fly away from the Player's pivot
    /// every loop instead of running in place - Player.prefab already drives world movement itself
    /// via PlanetWalker, so the visual mesh only needs to loop in place. This copies every curve
    /// from <paramref name="sourceClip"/> into a new standalone .anim asset, except it flattens the
    /// Hips bone's horizontal (X/Z) position curves to their first-frame value while keeping the
    /// vertical (Y) bob, so the body still has natural up/down motion but never drifts sideways.
    /// </summary>
    // Fraction of the clip's tail used to blend the pose back to the start pose (see SmoothLoopSeam).
    const float SeamBlendFraction = 0.2f;

    /// <summary>
    /// Meshy's AI-generated take doesn't end in exactly the pose it started in (several leg/arm
    /// rotations differ noticeably between the first and last frame), so simply looping the raw
    /// clip produced a visible "pop"/snap every cycle. This ramps a correction in over the last
    /// <see cref="SeamBlendFraction"/> of the clip (smoothstep-eased) so the curve approaches its
    /// starting value by the final key, spreading the fix-up across the tail instead of snapping
    /// instantly - then forces the last key to be a bit-exact copy of the first key (same value,
    /// same tangents) so the clip wraps with zero positional or velocity discontinuity: the first
    /// and last frame are literally the same frame. Mutates <paramref name="curve"/> in place.
    /// </summary>
    static void SmoothLoopSeam(AnimationCurve curve)
    {
        var keys = curve.keys;
        if (keys.Length < 3) return;

        float startVal = keys[0].value;
        float endVal = keys[keys.Length - 1].value;
        float delta = endVal - startVal;

        if (Mathf.Abs(delta) > 0f)
        {
            float tStart = keys[0].time;
            float tEnd = keys[keys.Length - 1].time;
            float blendStartTime = tEnd - (tEnd - tStart) * SeamBlendFraction;

            for (int i = 0; i < keys.Length; i++)
            {
                if (keys[i].time < blendStartTime) continue;
                float t = Mathf.InverseLerp(blendStartTime, tEnd, keys[i].time);
                float weight = t * t * (3f - 2f * t); // smoothstep easing
                keys[i].value -= weight * delta;
            }
        }

        // Force bit-exact equality with the first key (value + tangents), regardless of how small
        // the residual mismatch from the blend above is - "same frame" means exactly the same,
        // not just close.
        var firstKey = keys[0];
        var lastKey = keys[keys.Length - 1];
        lastKey.value = firstKey.value;
        lastKey.inTangent = firstKey.inTangent;
        lastKey.outTangent = firstKey.outTangent;
        keys[keys.Length - 1] = lastKey;

        curve.keys = keys;
        for (int i = 1; i < keys.Length - 1; i++)
            curve.SmoothTangents(i, 0f);
    }

    /// <summary>
    /// Rotation-aware version of <see cref="SmoothLoopSeam"/>: treats the 4 component curves of a
    /// single bone's local rotation as one quaternion per key, Slerps each key inside the tail
    /// blend window toward the starting rotation (picking whichever hemisphere of the double-cover
    /// is closest to the original so it never spins the "long way around"), then forces the last
    /// key to be a bit-exact copy of the first key on all 4 curves. Mutates the curves in place.
    /// </summary>
    static void SmoothRotationLoopSeam(AnimationCurve curveX, AnimationCurve curveY, AnimationCurve curveZ, AnimationCurve curveW)
    {
        var keysX = curveX.keys;
        var keysY = curveY.keys;
        var keysZ = curveZ.keys;
        var keysW = curveW.keys;
        int n = keysX.Length;
        if (n < 3 || keysY.Length != n || keysZ.Length != n || keysW.Length != n)
        {
            SmoothLoopSeam(curveX);
            SmoothLoopSeam(curveY);
            SmoothLoopSeam(curveZ);
            SmoothLoopSeam(curveW);
            return;
        }

        var startQuat = new Quaternion(keysX[0].value, keysY[0].value, keysZ[0].value, keysW[0].value).normalized;

        float tStart = keysX[0].time;
        float tEnd = keysX[n - 1].time;
        float blendStartTime = tEnd - (tEnd - tStart) * SeamBlendFraction;

        for (int i = 0; i < n; i++)
        {
            if (keysX[i].time < blendStartTime) continue;

            var original = new Quaternion(keysX[i].value, keysY[i].value, keysZ[i].value, keysW[i].value).normalized;
            if (Quaternion.Dot(original, startQuat) < 0f)
                original = new Quaternion(-original.x, -original.y, -original.z, -original.w);

            float t = Mathf.InverseLerp(blendStartTime, tEnd, keysX[i].time);
            float weight = t * t * (3f - 2f * t); // smoothstep easing
            var corrected = Quaternion.Slerp(original, startQuat, weight);

            keysX[i].value = corrected.x;
            keysY[i].value = corrected.y;
            keysZ[i].value = corrected.z;
            keysW[i].value = corrected.w;
        }

        CopyValueAndTangentsFromFirstKey(ref keysX);
        CopyValueAndTangentsFromFirstKey(ref keysY);
        CopyValueAndTangentsFromFirstKey(ref keysZ);
        CopyValueAndTangentsFromFirstKey(ref keysW);

        curveX.keys = keysX;
        curveY.keys = keysY;
        curveZ.keys = keysZ;
        curveW.keys = keysW;

        for (int i = 1; i < n - 1; i++)
        {
            curveX.SmoothTangents(i, 0f);
            curveY.SmoothTangents(i, 0f);
            curveZ.SmoothTangents(i, 0f);
            curveW.SmoothTangents(i, 0f);
        }
    }

    static void CopyValueAndTangentsFromFirstKey(ref Keyframe[] keys)
    {
        var first = keys[0];
        var last = keys[keys.Length - 1];
        last.value = first.value;
        last.inTangent = first.inTangent;
        last.outTangent = first.outTangent;
        keys[keys.Length - 1] = last;
    }

    static AnimationClip MakeInPlaceCopy(AnimationClip sourceClip, string outputPath)
    {
        var clip = new AnimationClip { name = System.IO.Path.GetFileNameWithoutExtension(outputPath) };
        AnimationUtility.SetAnimationClipSettings(clip, AnimationUtility.GetAnimationClipSettings(sourceClip));

        // The take's authored end pose can be dozens of degrees out of phase from its start pose
        // (it isn't necessarily an exact whole number of gait cycles), which is far too much to
        // hide by blending just the tail - it looked like an unnatural "leg swap" right at the
        // loop point. Search the back half of the clip for whichever frame's pose is naturally
        // closest to frame 0 across every bone, and just discard whatever comes after it - that
        // gives a shorter but far more honestly-looping cycle to work with before any correction.
        int truncateAtFrame = -1;
        var candidates = ScoreLoopEndCandidates(sourceClip, out _, out _);
        if (candidates != null && candidates.Count > 0)
        {
            var best = candidates.OrderBy(c => c.totalMismatch).First();
            truncateAtFrame = best.frame;
            Debug.Log($"[BackHome] Best natural loop point found at frame {best.frame} (t={best.time:0.00}s, was {sourceClip.length:0.00}s) - " +
                $"worst-bone mismatch there is {best.maxMismatch:0.0} deg vs {candidates.Last().maxMismatch:0.0} deg at the authored last frame. Trimming to it.");
        }

        var allBindings = AnimationUtility.GetCurveBindings(sourceClip);
        var handled = new HashSet<EditorCurveBinding>();

        AnimationCurve GetTruncatedCurve(EditorCurveBinding binding)
        {
            var curve = AnimationUtility.GetEditorCurve(sourceClip, binding);
            if (truncateAtFrame < 0 || truncateAtFrame >= curve.keys.Length - 1) return curve;
            var truncatedKeys = curve.keys.Take(truncateAtFrame + 1).ToArray();
            return new AnimationCurve(truncatedKeys);
        }

        // Rotations are stored as 4 independent scalar curves (x/y/z/w), but a rotation is not 4
        // independent numbers - blending/locking each component separately (as a previous version
        // of this tool did) can produce quaternions that aren't unit-length or that interpolate
        // through a completely different orientation than intended, which showed up as a visible
        // "leg swap" glitch right at the loop point. Group each bone's 4 rotation curves back into
        // a genuine quaternion and fix the seam with Slerp instead.
        foreach (var group in allBindings.Where(b => b.type == typeof(Transform) && b.propertyName.StartsWith("m_LocalRotation.")).GroupBy(b => b.path))
        {
            var byProp = group.ToDictionary(b => b.propertyName, b => b);
            if (!byProp.ContainsKey("m_LocalRotation.x") || !byProp.ContainsKey("m_LocalRotation.y") ||
                !byProp.ContainsKey("m_LocalRotation.z") || !byProp.ContainsKey("m_LocalRotation.w"))
                continue;

            var curveX = GetTruncatedCurve(byProp["m_LocalRotation.x"]);
            var curveY = GetTruncatedCurve(byProp["m_LocalRotation.y"]);
            var curveZ = GetTruncatedCurve(byProp["m_LocalRotation.z"]);
            var curveW = GetTruncatedCurve(byProp["m_LocalRotation.w"]);

            SmoothRotationLoopSeam(curveX, curveY, curveZ, curveW);

            AnimationUtility.SetEditorCurve(clip, byProp["m_LocalRotation.x"], curveX);
            AnimationUtility.SetEditorCurve(clip, byProp["m_LocalRotation.y"], curveY);
            AnimationUtility.SetEditorCurve(clip, byProp["m_LocalRotation.z"], curveZ);
            AnimationUtility.SetEditorCurve(clip, byProp["m_LocalRotation.w"], curveW);

            handled.Add(byProp["m_LocalRotation.x"]);
            handled.Add(byProp["m_LocalRotation.y"]);
            handled.Add(byProp["m_LocalRotation.z"]);
            handled.Add(byProp["m_LocalRotation.w"]);
        }

        foreach (var binding in allBindings)
        {
            if (handled.Contains(binding)) continue;

            // Some source rigs bake a near-constant ~1.0 scale track onto every bone (floating
            // point noise from the authoring tool, not an intentional squash/stretch). Left in,
            // that overrides the bind pose's actual rest scale on playback - most importantly the
            // rig-normalizing scale baked onto the skeleton's root bone (e.g. 0.01 to convert a
            // centimeter-authored rig down to meters) - making the whole character balloon up to
            // ~100x size for as long as the clip is active. Scale isn't meant to be animated here,
            // so drop these curves entirely and let the bind pose's scale stand untouched.
            if (binding.type == typeof(Transform) && binding.propertyName.StartsWith("m_LocalScale."))
                continue;

            var curve = GetTruncatedCurve(binding);
            bool isHipsHorizontalPosition = binding.path.EndsWith("Hips", System.StringComparison.Ordinal)
                && binding.propertyName is "m_LocalPosition.x" or "m_LocalPosition.z";

            if (isHipsHorizontalPosition && curve.keys.Length > 0)
            {
                float lockedValue = curve.keys[0].value;
                curve = AnimationCurve.Constant(curve.keys[0].time, curve.keys[curve.keys.Length - 1].time, lockedValue);
            }
            else
            {
                SmoothLoopSeam(curve);
            }

            AnimationUtility.SetEditorCurve(clip, binding, curve);
        }

        foreach (var objBinding in AnimationUtility.GetObjectReferenceCurveBindings(sourceClip))
            AnimationUtility.SetObjectReferenceCurve(clip, objBinding, AnimationUtility.GetObjectReferenceCurve(sourceClip, objBinding));

        EnsureFolderExists(System.IO.Path.GetDirectoryName(outputPath));
        var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(outputPath);
        if (existing != null)
            AssetDatabase.DeleteAsset(outputPath);
        AssetDatabase.CreateAsset(clip, outputPath);
        AssetDatabase.SaveAssets();

        Debug.Log($"[BackHome] Baked in-place copy of '{sourceClip.name}' at {outputPath} (Hips X/Z locked, Y bob kept).");
        return clip;
    }

    /// <summary>
    /// Replaces whatever is currently parented under Player.prefab's root as "Geometry"/"Skeleton"/
    /// "Model" (including a broken/missing nested-prefab "Model") with a fresh instance of the
    /// model at <paramref name="modelPath"/>, and re-points the Animator's Avatar to match.
    /// </summary>
    static bool SwapPrefabModelTo(string modelPath)
    {
        var newModelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
        if (newModelAsset == null)
        {
            Debug.LogError($"[BackHome] Could not load the model asset at {modelPath}");
            return false;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            Transform geometry = root.transform.Find("Geometry");
            if (geometry != null) Object.DestroyImmediate(geometry.gameObject);

            Transform skeleton = root.transform.Find("Skeleton");
            if (skeleton != null) Object.DestroyImmediate(skeleton.gameObject);

            Transform existingModel = root.transform.Find("Model");
            if (existingModel != null) Object.DestroyImmediate(existingModel.gameObject);

            GameObject modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(newModelAsset, root.transform.gameObject.scene);
            modelInstance.transform.SetParent(root.transform, false);
            modelInstance.name = "Model";
            modelInstance.transform.localPosition = Vector3.zero;
            modelInstance.transform.localRotation = Quaternion.identity;
            modelInstance.transform.localScale = Vector3.one;

            var animator = root.GetComponent<Animator>();
            if (animator == null)
            {
                Debug.LogError("[BackHome] Player.prefab root has no Animator component. Aborting before saving.");
                return false;
            }

            var newAvatar = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Avatar>().FirstOrDefault();
            animator.avatar = newAvatar;

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Debug.Log($"[BackHome] Player.prefab now references the model at {modelPath} and its Avatar.");
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// Creates (or replaces the contents of) the AnimatorController at <see cref="ControllerPath"/>
    /// with the standard StarterAssets parameter set PlanetWalker/TouchController already drive
    /// (Speed, Grounded, Jump, FreeFall, MotionSpeed) and a single default state that plays
    /// <paramref name="runClip"/>, its playback rate tied to the "MotionSpeed" parameter. That
    /// parameter is fed by PlanetWalker as roughly proportional to move-input magnitude, so the
    /// character runs at normal speed while moving and eases to a near-standstill pose otherwise.
    /// </summary>
    static bool RebuildLocomotionController(AnimationClip runClip)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            EnsureFolderExists(System.IO.Path.GetDirectoryName(ControllerPath).Replace('\\', '/'));
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        }

        if (controller == null)
        {
            Debug.LogError($"[BackHome] Could not create/load an AnimatorController at {ControllerPath}");
            return false;
        }

        foreach (var param in controller.parameters.ToList())
            controller.RemoveParameter(param);

        AnimatorStateMachine rootStateMachine = controller.layers[0].stateMachine;
        foreach (var childState in rootStateMachine.states.ToList())
            rootStateMachine.RemoveState(childState.state);

        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("MotionSpeed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Grounded", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Jump", AnimatorControllerParameterType.Bool);
        controller.AddParameter("FreeFall", AnimatorControllerParameterType.Bool);

        AnimatorState runState = rootStateMachine.AddState("Run");
        runState.motion = runClip;
        runState.speedParameterActive = true;
        runState.speedParameter = "MotionSpeed";
        rootStateMachine.defaultState = runState;

        EditorUtility.SetDirty(controller);
        Debug.Log($"[BackHome] Rebuilt {ControllerPath} with a single Run state playing '{runClip.name}'.");
        return true;
    }

    static void EnsureFolderExists(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        string parent = System.IO.Path.GetDirectoryName(folderPath).Replace('\\', '/');
        string leaf = System.IO.Path.GetFileName(folderPath);
        EnsureFolderExists(parent);
        AssetDatabase.CreateFolder(parent, leaf);
    }

    /// <summary>
    /// Re-derives Player.fbx's Humanoid Avatar from scratch. Use this whenever the FBX has been
    /// re-exported/edited and the console shows a "Rig Error: Avatar creation failed" for it
    /// (e.g. "Transform 'X' not found in HumanDescription") - that means the bone mapping cached
    /// in the .meta file no longer matches the model's current hierarchy, the Avatar fails to
    /// build, Animator.avatar becomes invalid, and the character renders stuck in T-pose with no
    /// animation playing at all.
    /// </summary>
    [MenuItem("BackHome/Player/Rebuild Player Avatar")]
    public static void RebuildPlayerAvatar()
    {
        try
        {
            var importer = AssetImporter.GetAtPath(NewModelPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogError($"[BackHome] Could not find a ModelImporter at {NewModelPath}");
                return;
            }

            // Wipe the stale human/skeleton mapping so CreateFromThisModel re-derives it fresh
            // against the model's current hierarchy instead of reusing paths that no longer exist.
            importer.humanDescription = new HumanDescription();
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.SaveAndReimport();

            var avatar = AssetDatabase.LoadAllAssetsAtPath(NewModelPath).OfType<Avatar>().FirstOrDefault();
            if (avatar == null || !avatar.isValid || !avatar.isHuman)
            {
                Debug.LogError("[BackHome] Rebuilding the Avatar still failed - Unity could not auto-map bone names from the " +
                    "current Player.fbx hierarchy. Dumping the model's transform hierarchy below so bones can be matched manually " +
                    "(select Player.fbx, open the Rig tab, click Configure..., and map any bones marked red).");
                LogModelHierarchy();
                return;
            }

            Debug.Log("[BackHome] Player.fbx Avatar rebuilt successfully and is valid Humanoid.");

            // The Animator on Player.prefab references this same Avatar by guid, so it doesn't
            // need any changes - it will pick up the rebuilt Avatar automatically.
            AssetDatabase.SaveAssets();
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            Debug.LogError("[BackHome] Rebuilding the Player Avatar aborted due to the exception above.");
        }
    }

    static void LogModelHierarchy()
    {
        var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(NewModelPath);
        if (modelAsset == null)
        {
            Debug.LogError($"[BackHome] Could not load model asset at {NewModelPath} to dump its hierarchy.");
            return;
        }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"[BackHome] Player.fbx transform hierarchy:");
        AppendHierarchy(modelAsset.transform, sb, 0);
        Debug.Log(sb.ToString());
    }

    static void AppendHierarchy(Transform t, System.Text.StringBuilder sb, int depth)
    {
        sb.AppendLine(new string(' ', depth * 2) + t.name);
        for (int i = 0; i < t.childCount; i++)
            AppendHierarchy(t.GetChild(i), sb, depth + 1);
    }

    static AnimationClip ConfigureAnimLibraryAsHumanoidAndGetClip(string takeNameFragment, string friendlyClipName)
    {
        var importer = AssetImporter.GetAtPath(AnimLibraryPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"[BackHome] Could not find a ModelImporter at {AnimLibraryPath}");
            return null;
        }

        // Player_Running.fbx's hierarchy doesn't line up exactly with Player.fbx's (CopyFromOther
        // failed - "bone names likely don't match exactly"), so auto-map its own Humanoid Avatar
        // from scratch instead of trying to reuse Player.fbx's. Unity's retargeting only needs the
        // human bone names to match at animation-playback time, not the raw hierarchy/avatar identity.
        importer.humanDescription = new HumanDescription();
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.optimizeGameObjects = false;
        importer.SaveAndReimport();

        var avatar = AssetDatabase.LoadAllAssetsAtPath(AnimLibraryPath).OfType<Avatar>().FirstOrDefault();
        if (avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            Debug.LogError("[BackHome] Unity could not auto-map a valid Humanoid Avatar for Player_Running.fbx from its bone names. " +
                "Select Assets/Resources/Player/Animations/Player_Running.fbx, open the Rig tab, click Configure..., and fix any bones marked red, then re-run this tool.");
            return null;
        }

        var takes = importer.importedTakeInfos;
        if (takes == null || takes.Length == 0)
        {
            Debug.LogError("[BackHome] Player_Running.fbx has no embedded animation takes.");
            return null;
        }

        Debug.Log("[BackHome] Player_Running.fbx takes found: " + string.Join(", ", takes.Select(t =>
            $"{t.name} ({(t.stopTime - t.startTime) * t.sampleRate:0} frames @ {t.sampleRate:0}fps)")));

        var targetTake = takes.FirstOrDefault(t => t.name.IndexOf(takeNameFragment, System.StringComparison.OrdinalIgnoreCase) >= 0);
        if (targetTake.name == null)
        {
            Debug.LogError($"[BackHome] Could not find a take matching '{takeNameFragment}' inside Player_Running.fbx. " +
                "See the take list logged above and adjust PlanetRunTakeNameFragment if the name differs.");
            return null;
        }

        // Keep every take available as its own clip (so Alert/Fall/Walking etc. stay usable later),
        // only marking the planet-run take as a looping clip.
        var clipAnimations = new ModelImporterClipAnimation[takes.Length];
        for (int i = 0; i < takes.Length; i++)
        {
            var take = takes[i];
            bool isTarget = take.name == targetTake.name;
            clipAnimations[i] = new ModelImporterClipAnimation
            {
                name = isTarget ? friendlyClipName : take.name,
                takeName = take.name,
                firstFrame = take.startTime * take.sampleRate,
                lastFrame = take.stopTime * take.sampleRate,
                wrapMode = isTarget ? WrapMode.Loop : WrapMode.Default,
                loopTime = isTarget,
                loopPose = isTarget,
            };
        }
        importer.clipAnimations = clipAnimations;
        importer.SaveAndReimport();

        var newClip = AssetDatabase.LoadAllAssetsAtPath(AnimLibraryPath).OfType<AnimationClip>().FirstOrDefault(c => c.name == friendlyClipName);
        if (newClip == null)
        {
            Debug.LogError($"[BackHome] Could not locate the extracted '{friendlyClipName}' clip after reimport.");
            return null;
        }

        Debug.Log($"[BackHome] Extracted '{friendlyClipName}' clip ({newClip.length:0.00}s) from take '{targetTake.name}'.");
        return newClip;
    }

    static AnimationClip ConfigureNewModelAsHumanoidAndGetRunClip()
    {
        var importer = AssetImporter.GetAtPath(NewModelPath) as ModelImporter;
        if (importer == null)
        {
            Debug.LogError($"[BackHome] Could not find a ModelImporter at {NewModelPath}");
            return null;
        }

        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.optimizeGameObjects = false;
        importer.SaveAndReimport();

        var avatar = AssetDatabase.LoadAllAssetsAtPath(NewModelPath).OfType<Avatar>().FirstOrDefault();
        if (avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            Debug.LogError("[BackHome] Unity could not auto-map a valid Humanoid Avatar for the new model from its bone names. " +
                "Select Assets/Resources/Player/Model/Player.fbx, open the Rig tab, click Configure..., and fix the bone mapping manually, then re-run this tool.");
            return null;
        }

        Debug.Log("[BackHome] New Player model is configured as Humanoid with a valid Avatar.");

        var takes = importer.importedTakeInfos;
        if (takes == null || takes.Length == 0)
        {
            Debug.LogWarning("[BackHome] The new model has no embedded animation take. Skipping the running-animation swap; only the model itself will be replaced.");
            return null;
        }

        var take = takes[0];
        var clipAnim = new ModelImporterClipAnimation
        {
            name = "Run",
            takeName = take.name,
            firstFrame = take.startTime * take.sampleRate,
            lastFrame = take.stopTime * take.sampleRate,
            loopTime = true,
            loopPose = true,
            wrapMode = WrapMode.Loop,
        };
        importer.clipAnimations = new[] { clipAnim };
        importer.SaveAndReimport();

        var newClip = AssetDatabase.LoadAllAssetsAtPath(NewModelPath).OfType<AnimationClip>().FirstOrDefault(c => c.name == "Run");
        if (newClip == null)
        {
            Debug.LogWarning("[BackHome] Could not locate the extracted 'Run' clip after reimport. Skipping the running-animation swap.");
            return null;
        }

        Debug.Log($"[BackHome] Extracted new running animation clip 'Run' ({newClip.length:0.00}s).");
        if (newClip.length < 0.1f)
            Debug.LogWarning("[BackHome] The extracted 'Run' clip is very short - double check it's really the running animation.");

        return newClip;
    }

    static bool SwapPrefabModel()
    {
        var newModelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(NewModelPath);
        if (newModelAsset == null)
        {
            Debug.LogError($"[BackHome] Could not load the new model prefab asset at {NewModelPath}");
            return false;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            Transform geometry = root.transform.Find("Geometry");
            if (geometry != null) Object.DestroyImmediate(geometry.gameObject);

            Transform skeleton = root.transform.Find("Skeleton");
            if (skeleton != null) Object.DestroyImmediate(skeleton.gameObject);

            Transform existingModel = root.transform.Find("Model");
            if (existingModel != null) Object.DestroyImmediate(existingModel.gameObject);

            GameObject modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(newModelAsset, root.transform.gameObject.scene);
            modelInstance.transform.SetParent(root.transform, false);
            modelInstance.name = "Model";
            modelInstance.transform.localPosition = Vector3.zero;
            modelInstance.transform.localRotation = Quaternion.identity;
            modelInstance.transform.localScale = Vector3.one;

            var animator = root.GetComponent<Animator>();
            if (animator == null)
            {
                Debug.LogError("[BackHome] Player.prefab root has no Animator component. Aborting before saving.");
                return false;
            }

            var newAvatar = AssetDatabase.LoadAllAssetsAtPath(NewModelPath).OfType<Avatar>().FirstOrDefault();
            animator.avatar = newAvatar;

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Debug.Log("[BackHome] Player.prefab now references the new model and its Avatar.");
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void SwapRunAnimation(AnimationClip newRunClip)
    {
        // The original asset-store "Run_N" clip this once replaced has since been deleted (its
        // whole SpaceRobotKyle folder is gone) - this is now a no-op unless SwapPlayerModel is
        // re-run against whatever clip currently occupies the Run slot (e.g. RunOnPlanet).
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError($"[BackHome] Could not load AnimatorController at {ControllerPath}");
            return;
        }

        var runOnPlanetClip = AssetDatabase.LoadAllAssetsAtPath(AnimLibraryPath).OfType<AnimationClip>().FirstOrDefault(c => c.name == PlanetRunClipName);
        if (runOnPlanetClip == null)
        {
            Debug.LogWarning($"[BackHome] Could not find the '{PlanetRunClipName}' clip to replace. Skipping animator update.");
            return;
        }

        bool replaced = false;
        foreach (var layer in controller.layers)
            replaced |= ReplaceInStateMachine(layer.stateMachine, runOnPlanetClip, newRunClip);

        if (replaced)
        {
            EditorUtility.SetDirty(controller);
            Debug.Log("[BackHome] Replaced the running motion in the locomotion blend tree with the new clip.");
        }
        else
        {
            Debug.LogWarning("[BackHome] Could not find the old running clip inside the AnimatorController's blend tree - it was not replaced automatically.");
        }
    }

    static void FrameStageDelayed()
    {
        var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage();
        var sceneView = SceneView.lastActiveSceneView;
        if (stage == null || sceneView == null)
            return;

        var renderers = stage.prefabContentsRoot.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            Selection.activeObject = stage.prefabContentsRoot;
            sceneView.FrameSelected(false, true);
            return;
        }

        Bounds bounds = renderers[0].bounds;
        foreach (var r in renderers)
            bounds.Encapsulate(r.bounds);
        bounds.Expand(bounds.size.magnitude * 0.3f);

        Selection.activeObject = stage.prefabContentsRoot;
        sceneView.Frame(bounds, false);
    }

    static bool ReplaceInStateMachine(AnimatorStateMachine sm, AnimationClip oldClip, AnimationClip newClip)
    {
        bool replaced = false;

        foreach (var childState in sm.states)
        {
            if (childState.state.motion is BlendTree bt)
                replaced |= ReplaceInBlendTree(bt, oldClip, newClip);
            else if (childState.state.motion == oldClip)
            {
                childState.state.motion = newClip;
                replaced = true;
            }
        }

        foreach (var sub in sm.stateMachines)
            replaced |= ReplaceInStateMachine(sub.stateMachine, oldClip, newClip);

        return replaced;
    }

    static bool ReplaceInBlendTree(BlendTree bt, AnimationClip oldClip, AnimationClip newClip)
    {
        bool replaced = false;
        var children = bt.children;
        for (int i = 0; i < children.Length; i++)
        {
            if (children[i].motion == oldClip)
            {
                children[i].motion = newClip;
                replaced = true;
            }
            else if (children[i].motion is BlendTree nested)
            {
                replaced |= ReplaceInBlendTree(nested, oldClip, newClip);
            }
        }
        if (replaced)
            bt.children = children;
        return replaced;
    }
}
