using UnityEditor;
using UnityEngine;

/// <summary>
/// Ensures Nyxara prop colliders are applied after domain reload.
/// </summary>
[InitializeOnLoad]
static class NyxaraPropColliderAutoRun
{
    const string PrefKey = "BackHome.PropColliders.Applied.v4";
    static int _frames;

    static NyxaraPropColliderAutoRun()
    {
        if (EditorPrefs.GetBool(PrefKey, false))
            return;

        _frames = 30; // wait for AssetDatabase to settle
        EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            return;
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        if (--_frames > 0)
            return;

        EditorApplication.update -= Tick;

        if (!AssetDatabase.IsValidFolder("Assets/Galaxy/Planets/Nyxara/Objects/Prefabs"))
            return;

        try
        {
            NyxaraPropColliderSetup.SetupAll(showDialog: false);
            EditorPrefs.SetBool(PrefKey, true);
        }
        catch (System.Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    [MenuItem("BackHome/Reset Prop Collider Auto Flag")]
    static void ResetFlag()
    {
        EditorPrefs.DeleteKey(PrefKey);
        Debug.Log("[BackHome] Prop collider auto flag cleared. Recompile or run Setup Nyxara Prop Colliders.");
    }
}
