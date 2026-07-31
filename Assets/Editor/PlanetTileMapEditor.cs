using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PlanetTileMap))]
public class PlanetTileMapEditor : Editor
{
    int _paintIndex;
    bool _painting = true;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        DrawDefaultInspector();

        var map = (PlanetTileMap)target;
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Tile Painting", EditorStyles.boldLabel);

        PlanetTilePalette palette = map.Palette;
        if (palette == null || palette.Count == 0)
        {
            EditorGUILayout.HelpBox("Assign a PlanetTilePalette with at least one tile prefab.", MessageType.Warning);
            serializedObject.ApplyModifiedProperties();
            return;
        }

        string[] names = new string[palette.Count];
        for (int i = 0; i < palette.Count; i++)
        {
            var entry = palette.GetEntry(i);
            names[i] = entry != null
                ? $"{i}: {(string.IsNullOrEmpty(entry.displayName) ? entry.id : entry.displayName)}"
                : $"{i}: <null>";
        }

        _paintIndex = Mathf.Clamp(_paintIndex, 0, palette.Count - 1);
        _paintIndex = EditorGUILayout.Popup("Brush Tile", _paintIndex, names);
        _painting = EditorGUILayout.Toggle("Paint On Click (Scene View)", _painting);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Rebuild Grid Size From Equator"))
        {
            Undo.RecordObject(map, "Rebuild Grid Size");
            map.EnsureGridDimensionsFromEquator();
            map.FillAll(map.FillTileIndex);
            EditorUtility.SetDirty(map);
        }

        if (GUILayout.Button("Fill All With Brush"))
        {
            Undo.RecordObject(map, "Fill Planet Tiles");
            map.FillAll(_paintIndex);
            EditorUtility.SetDirty(map);
        }
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("Bake Combined Mesh"))
        {
            Undo.RecordObject(map, "Bake Planet Tile Mesh");
            map.RebuildVisuals();
            EditorUtility.SetDirty(map);
        }

        serializedObject.ApplyModifiedProperties();
    }

    void OnSceneGUI()
    {
        if (!_painting)
            return;

        var map = (PlanetTileMap)target;
        if (map == null || map.Palette == null)
            return;

        Event e = Event.current;
        if (e.type != EventType.MouseDown || e.button != 0 || e.alt)
            return;

        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        if (!Physics.Raycast(ray, out RaycastHit hit, 10000f))
            return;

        if (!hit.collider || hit.collider.GetComponentInParent<PlanetTileMap>() != map)
            return;

        if (!map.WorldToCell(hit.point, out int lat, out int lon))
            return;

        Undo.RecordObject(map, "Paint Planet Tile");
        map.SetTile(lat, lon, _paintIndex);
        EditorUtility.SetDirty(map);
        e.Use();
    }
}
