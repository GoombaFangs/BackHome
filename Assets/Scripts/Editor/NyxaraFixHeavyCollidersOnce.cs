using UnityEditor;
using UnityEngine;

/// <summary>
/// One-shot: refit prop BoxColliders from mesh bounds after heavy MeshCollider removal.
/// </summary>
[InitializeOnLoad]
static class NyxaraFixHeavyCollidersOnce
{
    const string PrefKey = "BackHome.NyxaraFixHeavyColliders.v2";
    static int _frames;

    static NyxaraFixHeavyCollidersOnce()
    {
        if (EditorPrefs.GetBool(PrefKey, false))
            return;
        _frames = 25;
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
        try
        {
            NyxaraPropColliderSetup.SetupAll(showDialog: false);
            EditorPrefs.SetBool(PrefKey, true);
            Debug.Log("[BackHome] Rebuilt prop colliders (BoxCollider for high-poly meshes).");
        }
        catch (System.Exception ex)
        {
            Debug.LogException(ex);
        }
    }
}
