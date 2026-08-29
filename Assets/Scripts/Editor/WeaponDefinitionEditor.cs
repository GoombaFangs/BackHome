using System.Collections.Generic;
using UnityEditor;

[CustomEditor(typeof(WeaponDefinition))]
public class WeaponDefinitionEditor : Editor
{
    static readonly string[] DotFields =
    {
        "hitsToApplyDot",
        "hitWindow",
        "dotDuration",
        "dotDamagePerSecond",
        "dotTickInterval",
        "debuffVFX",
        "debuffEuler"
    };
    static readonly string[] ChainFields =
    {
        "chainJumps",
        "chainRadius",
        "chainDamageMultiplier",
        "chainVFX"
    };
    static readonly string[] AreaBlastFields =
    {
        "areaBlastRadius",
        "areaBlastDamageMultiplier",
        "areaBlastVFX",
        "areaBlastVfxLifetime",
        "areaBlastVfxRadius",
        "areaBlastDamageDelay"
    };

    public override void OnInspectorGUI()
    {
        serializedObject.UpdateIfRequiredOrScript();

        SerializedProperty hitEffect = serializedObject.FindProperty("hitEffect");
        bool showDot = hitEffect != null
            && hitEffect.enumValueIndex == (int)WeaponHitEffectKind.RepeatedHitDoT;
        bool showChain = hitEffect != null
            && hitEffect.enumValueIndex == (int)WeaponHitEffectKind.ChainJump;
        bool showAreaBlast = hitEffect != null
            && hitEffect.enumValueIndex == (int)WeaponHitEffectKind.AreaBlast;

        List<string> hidden = new List<string> { "m_Script" };
        if (!showDot)
            hidden.AddRange(DotFields);
        if (!showChain)
            hidden.AddRange(ChainFields);
        if (!showAreaBlast)
            hidden.AddRange(AreaBlastFields);

        DrawPropertiesExcluding(serializedObject, hidden.ToArray());
        serializedObject.ApplyModifiedProperties();
    }
}
