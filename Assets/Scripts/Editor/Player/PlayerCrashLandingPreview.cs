using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only Scene view aid for <see cref="PlayerCrashIntro"/>: hides each crash cinematic's
/// authored capsule (PlayerDiveDownCapsule) from the Scene view - via Unity's built-in Scene
/// Visibility, so it never touches saved scene data and has zero effect on Play Mode/builds -
/// and, the first time it sees a PlayerCrashIntro with no <c>portalAnchor</c> wired up yet,
/// creates one: a real, permanently-placed Portal instance seeded at the capsule's current
/// landing pose and wired straight into PlayerCrashIntro.portalAnchor.
///
/// From that point on this tool never touches the anchor's transform again - it's a normal,
/// fully user-owned scene object. Drag it anywhere and PlayerCrashIntro.LandingAnchor (see
/// TryComputeLandingSite) follows it automatically, both for the crash cinematic itself and for
/// where the ground portal ends up - so the portal and the crash site can never drift apart, no
/// matter where the portal gets moved.
/// </summary>
[InitializeOnLoad]
static class PlayerCrashLandingPreview
{
    const string AnchorName = "Portal";

    static bool _refreshing;

    static PlayerCrashLandingPreview()
    {
        EditorApplication.hierarchyChanged += RefreshAll;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.delayCall += RefreshAll;
    }

    [MenuItem("BackHome/Refresh Crash Landing Previews")]
    static void RefreshFromMenu() => RefreshAllInternal(snapExistingAnchors: true);

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.EnteredEditMode)
            RefreshAll();
    }

    static void RefreshAll() => RefreshAllInternal(snapExistingAnchors: false);

    static void RefreshAllInternal(bool snapExistingAnchors)
    {
        if (_refreshing)
            return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;

        _refreshing = true;
        try
        {
            PlayerCrashIntro[] found = Object.FindObjectsByType<PlayerCrashIntro>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (PlayerCrashIntro intro in found)
                EnsureAnchor(intro, snapExistingAnchors);
        }
        finally
        {
            _refreshing = false;
        }
    }

    static void EnsureAnchor(PlayerCrashIntro intro, bool snapExistingAnchor = false)
    {
        Transform capsule = intro.PlayerCapsule;
        if (capsule == null)
            return;

        // Purely cosmetic and never saved to the scene file - safe to reapply every pass.
        SceneVisibilityManager.instance.Hide(capsule.gameObject, true);

        if (intro.PortalAnchor != null)
        {
            if (snapExistingAnchor)
                intro.EditorApplyGroundPortalPose(intro.PortalAnchor, intro.PortalAnchor.position, intro.PortalAnchor.rotation);
            return;
        }

        GameObject prefab = intro.EditorResolvePortalPrefab();
        if (prefab == null)
            return;

        if (!intro.TryComputePortalLandingSite(out Vector3 position, out Quaternion rotation))
            return;

        GameObject marker = (GameObject)PrefabUtility.InstantiatePrefab(prefab, capsule.gameObject.scene);
        if (marker == null)
            return;

        marker.name = AnchorName;
        intro.EditorApplyGroundPortalPose(marker.transform, position, rotation);
        StripInteractivity(marker);
        Undo.RegisterCreatedObjectUndo(marker, "Create Portal Landing Anchor");

        Undo.RecordObject(intro, "Assign Portal Landing Anchor");
        var so = new SerializedObject(intro);
        SerializedProperty prop = so.FindProperty("portalAnchor");
        if (prop != null)
        {
            prop.objectReferenceValue = marker.transform;
            so.ApplyModifiedProperties();
        }
    }

    static void StripInteractivity(GameObject marker)
    {
        foreach (Collider collider in marker.GetComponentsInChildren<Collider>(true))
            collider.enabled = false;
        foreach (GalaxyGate gate in marker.GetComponentsInChildren<GalaxyGate>(true))
            gate.enabled = false;
    }
}
