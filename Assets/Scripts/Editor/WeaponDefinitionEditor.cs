using UnityEditor;

[CustomEditor(typeof(WeaponDefinition))]
public class WeaponDefinitionEditor : Editor
{
    static readonly string[] AlwaysHidden = { "m_Script" };
    static readonly string[] HiddenWithoutDot =
    {
        "m_Script",
        "hitsToApplyDot",
        "hitWindow",
        "dotDuration",
        "dotDamagePerSecond",
        "dotTickInterval",
        "debuffVFX",
        "debuffEuler"
    };

    public override void OnInspectorGUI()
    {
        serializedObject.UpdateIfRequiredOrScript();

        SerializedProperty hitEffect = serializedObject.FindProperty("hitEffect");
        bool showDot = hitEffect != null
            && hitEffect.enumValueIndex == (int)WeaponHitEffectKind.RepeatedHitDoT;

        DrawPropertiesExcluding(serializedObject, showDot ? AlwaysHidden : HiddenWithoutDot);
        serializedObject.ApplyModifiedProperties();
    }
}
