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
        CombatStats resolved = stats.ResolvedCombat;

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Resolved Combat", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Runtime combat = Combat fields + every weapon in Weapons.\n" +
            "Swap the base numbers or drop a different Weapon Definition in the list.",
            MessageType.Info);

        using (new EditorGUI.DisabledScope(true))
        {
            EditorGUILayout.FloatField("Attack Damage", resolved.AttackDamage);
            EditorGUILayout.FloatField("Attack Speed", resolved.AttackSpeed);
            EditorGUILayout.FloatField("Attack Range", resolved.AttackRange);
        }

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Base", FormatCombat(origin));
        var weapons = stats.Weapons;
        bool anyWeapon = false;
        for (int i = 0; i < weapons.Count; i++)
        {
            WeaponDefinition weapon = weapons[i];
            if (weapon == null)
                continue;
            anyWeapon = true;
            EditorGUILayout.LabelField($"  + {weapon.DisplayName}", FormatCombat(weapon.Combat));
        }

        if (!anyWeapon)
            EditorGUILayout.LabelField("  + Weapons", "none");
    }

    static string FormatCombat(CombatStats combat)
    {
        return $"{combat.AttackDamage:0.##} dmg  |  {combat.AttackSpeed:0.##} spd  |  {combat.AttackRange:0.##} rng";
    }
}
