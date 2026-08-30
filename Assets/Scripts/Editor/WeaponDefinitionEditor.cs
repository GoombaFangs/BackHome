using System.Collections.Generic;
using UnityEditor;

/// <summary>
/// Shows only the on-hit-effect settings block that matches the selected <see cref="WeaponHitEffectKind"/>,
/// instead of every effect's fields at once.
/// </summary>
[CustomEditor(typeof(WeaponDefinition))]
public class WeaponDefinitionEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.UpdateIfRequiredOrScript();

        SerializedProperty hitEffect = serializedObject.FindProperty("hitEffect");
        var kind = hitEffect != null ? (WeaponHitEffectKind)hitEffect.enumValueIndex : WeaponHitEffectKind.None;

        List<string> hidden = new List<string> { "m_Script" };
        if (kind != WeaponHitEffectKind.Dot)
            hidden.Add("dotSettings");
        if (kind != WeaponHitEffectKind.ChainJump)
            hidden.Add("chainSettings");
        if (kind != WeaponHitEffectKind.AreaBlast)
            hidden.Add("areaBlastSettings");

        DrawPropertiesExcluding(serializedObject, hidden.ToArray());
        serializedObject.ApplyModifiedProperties();
    }
}
