using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PlanetTileMapPreset))]
public class PlanetTileMapPresetEditor : Editor
{
    public const string DefaultFolder = "Assets/Resources/Galaxy/Nyxara/TilePresets";

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var preset = (PlanetTileMapPreset)target;
        if (preset == null)
            return;

        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("displayName"),
            new GUIContent("Name"));
        EditorGUILayout.PropertyField(
            serializedObject.FindProperty("tileset"),
            new GUIContent("Tileset", "Tileset this snapshot was painted with. Used only as a warning on load."));
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Snapshot", EditorStyles.boldLabel);
        if (!preset.HasValidData())
        {
            EditorGUILayout.HelpBox(
                "Empty preset. Load a planet, paint it, then use Save Preset in the PlanetTileMap inspector.",
                MessageType.Info);
            return;
        }

        EditorGUILayout.LabelField("Grid", $"{preset.LongitudeBands} × {preset.LatitudeBands}");
        EditorGUILayout.LabelField("Tiles around equator", preset.TilesAroundEquator.ToString());
        EditorGUILayout.LabelField("Cells", preset.CellCount.ToString("N0"));
        DrawTerrainMix(preset);
        EditorGUILayout.HelpBox(
            "This is a painted map snapshot. Apply it from the planet's Terrain Painting inspector.",
            MessageType.None);
    }

    static void DrawTerrainMix(PlanetTileMapPreset preset)
    {
        if (!preset.TryCopyData(out _, out _, out _, out int[] terrains, out _, out _))
            return;

        PlanetTileset tileset = preset.Tileset;
        int terrainCount = tileset != null ? Mathf.Max(1, tileset.TerrainCount) : 4;
        var counts = new int[terrainCount];
        for (int i = 0; i < terrains.Length; i++)
        {
            int t = terrains[i];
            if (t < 0 || t >= counts.Length)
                t = 0;
            counts[t]++;
        }

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Terrain mix", EditorStyles.miniBoldLabel);
        int cells = terrains.Length;
        for (int i = 0; i < counts.Length; i++)
        {
            if (counts[i] <= 0)
                continue;
            string label = tileset != null && tileset.GetTerrain(i) != null
                ? tileset.GetTerrain(i).displayName
                : "Terrain " + i;
            float pct = cells > 0 ? 100f * counts[i] / cells : 0f;
            EditorGUILayout.LabelField(label, $"{counts[i]:N0}  ({pct:0.#}%)");
        }
    }

    public static void EnsureDefaultFolder()
    {
        if (AssetDatabase.IsValidFolder(DefaultFolder))
            return;

        string[] parts = DefaultFolder.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
