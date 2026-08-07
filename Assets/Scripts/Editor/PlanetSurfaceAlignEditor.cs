using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PlanetSurfaceAlign))]
[CanEditMultipleObjects]
public class PlanetSurfaceAlignEditor : Editor
{
    bool _wasDragging;
    SerializedProperty _planet;
    SerializedProperty _hover;
    SerializedProperty _yaw;
    SerializedProperty _alignEveryFrame;

    void OnEnable()
    {
        _planet = serializedObject.FindProperty("planet");
        _hover = serializedObject.FindProperty("hover");
        _yaw = serializedObject.FindProperty("yaw");
        _alignEveryFrame = serializedObject.FindProperty("alignEveryFrame");
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(_planet);
        EditorGUILayout.PropertyField(_hover);
        EditorGUILayout.PropertyField(_yaw);
        EditorGUILayout.PropertyField(_alignEveryFrame);
        bool changed = EditorGUI.EndChangeCheck();

        serializedObject.ApplyModifiedProperties();

        // Only re-position when the user edits hover/yaw/planet in the Inspector — never on Play.
        if (changed && !Application.isPlaying)
        {
            for (int i = 0; i < targets.Length; i++)
            {
                var align = targets[i] as PlanetSurfaceAlign;
                if (align == null)
                    continue;
                Undo.RecordObject(align.transform, "Adjust Planet Surface Align");
                align.SnapToSurface(recordUndo: false);
                EditorUtility.SetDirty(align);
            }
        }

        EditorGUILayout.Space(6);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Snap To Surface", GUILayout.Height(28)))
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    var align = targets[i] as PlanetSurfaceAlign;
                    if (align == null)
                        continue;
                    Undo.RecordObject(align.transform, "Snap Prop To Planet Surface");
                    align.SnapToSurface(recordUndo: false);
                    EditorUtility.SetDirty(align);
                }
            }

            if (GUILayout.Button("Align Rotation Only", GUILayout.Height(28)))
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    var align = targets[i] as PlanetSurfaceAlign;
                    if (align == null)
                        continue;
                    Undo.RecordObject(align.transform, "Align Prop Rotation");
                    align.AlignRotationOnly(recordUndo: false);
                    EditorUtility.SetDirty(align);
                }
            }
        }

        EditorGUILayout.HelpBox(
            "Position is saved as-is. Play mode will not move the prop unless Align Every Frame is on.",
            MessageType.Info);
    }

    void OnSceneGUI()
    {
        var align = target as PlanetSurfaceAlign;
        if (align == null || Application.isPlaying)
            return;

        Event e = Event.current;
        if (e == null)
            return;

        if (e.type == EventType.MouseDrag && e.button == 0 && !e.alt)
            _wasDragging = true;

        if (_wasDragging && (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp))
        {
            _wasDragging = false;
            align.EditorHandleMoved();
            EditorUtility.SetDirty(align);
        }

        if (_wasDragging && e.type == EventType.MouseDrag && Tools.current == Tool.Move)
            align.EditorHandleMoved();
    }
}
