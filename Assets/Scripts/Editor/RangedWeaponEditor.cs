using System.Collections.Generic;
using UnityEditor;

/// <summary>
/// Hides the projectile settings block unless the weapon's delivery is set to Projectile —
/// hitscan weapons no longer show unused bullet/sky-strike fields.
/// </summary>
[CustomEditor(typeof(RangedWeapon))]
public class RangedWeaponEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.UpdateIfRequiredOrScript();

        SerializedProperty delivery = serializedObject.FindProperty("delivery");
        var kind = delivery != null ? (WeaponDeliveryKind)delivery.enumValueIndex : WeaponDeliveryKind.Hitscan;

        List<string> hidden = new List<string> { "m_Script" };
        if (kind != WeaponDeliveryKind.Projectile)
            hidden.Add("projectile");

        DrawPropertiesExcluding(serializedObject, hidden.ToArray());
        serializedObject.ApplyModifiedProperties();
    }
}
