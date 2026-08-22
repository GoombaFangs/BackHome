using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PlayerStats))]
public class PlayerStatsEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var stats = (PlayerStats)target;
        CombatStats origin = stats.BaseCombat;

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Resolved Combat", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Each weapon floats on its own and fights with Combat + that weapon (up to 3).\n" +
            "The range ring uses the longest weapon range.",
            MessageType.Info);

        EditorGUILayout.LabelField("Base", FormatCombat(origin));
        var weapons = stats.Weapons;
        bool anyWeapon = false;
        int shown = 0;
        for (int i = 0; i < weapons.Count; i++)
        {
            WeaponDefinition weapon = weapons[i];
            if (weapon == null)
                continue;
            anyWeapon = true;
            if (shown >= CombatLoadout.MaxWeapons)
            {
                EditorGUILayout.HelpBox($"Only the first {CombatLoadout.MaxWeapons} weapons are used.", MessageType.Warning);
                break;
            }

            CombatStats resolved = stats.CombatFor(weapon);
            EditorGUILayout.LabelField($"  {weapon.DisplayName}", FormatCombat(resolved));
            shown++;
        }

        if (!anyWeapon)
            EditorGUILayout.LabelField("  Weapons", "none — base combat only");

        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.FloatField("Range Ring", stats.MaxAttackRange);
    }

    static string FormatCombat(CombatStats combat)
    {
        return $"{combat.AttackDamage:0.##} dmg  |  {combat.AttackSpeed:0.##} spd  |  {combat.AttackRange:0.##} rng";
    }
}
