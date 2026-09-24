using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PlanetTileMap))]
public class PlanetTileMapEditor : Editor
{
    enum PaintLayer
    {
        Terrain,
        Ground
    }

    enum GroundTool
    {
        Raise,
        Lower,
        Set
    }

    static bool _paintMode;
    static PaintLayer _layer = PaintLayer.Terrain;
    static int _terrainBrush;
    static GroundTool _groundTool = GroundTool.Raise;
    static float _setHeight = PlanetTileMap.DefaultGroundHeight;
    static int _brushRadius = 1;
    static bool _showGrid = true;
    static bool _eraseMode;
    static bool _eyedropper;
    static bool _floodPending;
    static int _hoverLat = int.MinValue;
    static int _hoverLon = int.MinValue;
    static int _lastLat = int.MinValue;
    static int _lastLon = int.MinValue;
    static bool _strokeActive;

    void OnDisable()
    {
        if (_paintMode)
            ExitPaintMode();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var map = target as PlanetTileMap;
        if (map == null)
            return;

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Tile Size", EditorStyles.boldLabel);
        SerializedProperty tilesAroundProp = serializedObject.FindProperty("tilesAroundEquator");
        EditorGUI.BeginChangeCheck();
        EditorGUILayout.PropertyField(
            tilesAroundProp,
            new GUIContent("Tiles Around Equator", "Higher = smaller tiles."));
        if (EditorGUI.EndChangeCheck())
            serializedObject.ApplyModifiedProperties();

        EditorGUILayout.HelpBox(
            $"Approx tile width: ~{map.ApproximateTileWorldSize:0.0}\n" +
            $"Grid: {map.LongitudeBands} × {map.LatitudeBands} = {map.CellCount} cells",
            MessageType.Info);

        if (GUILayout.Button("Apply Tile Size (Rebuild Grid)", GUILayout.Height(28)))
        {
            Undo.RecordObject(map, "Apply Planet Tile Size");
            map.SetTilesAroundEquator(tilesAroundProp.intValue, refillWithBase: true);
            MarkDirty(map);
        }

        EditorGUILayout.Space(8);
        DrawPropertiesExcluding(
            serializedObject,
            "m_Script",
            "tilesAroundEquator",
            "heightStep",
            "tileIndices",
            "terrainIds",
            "waterMask",
            "integerHeights",
            "groundHeights",
            "heights",
            "lastLoadedPreset");
        serializedObject.ApplyModifiedProperties();

        if (map.Tileset == null || map.Tileset.TerrainCount == 0)
        {
            EditorGUILayout.HelpBox(
                "Missing PlanetTileset.\n" +
                "Run: BackHome → Import Nyxara Tileset",
                MessageType.Error);
            if (GUILayout.Button("Import Nyxara Tileset Now", GUILayout.Height(32)))
                NyxaraTileAtlasImporter.Import();
            return;
        }

        if (!map.Tileset.HasVisualSource)
        {
            EditorGUILayout.HelpBox(
                "Tileset has no splat textures or atlas.\n" +
                "Re-run: BackHome → Import Nyxara Tileset",
                MessageType.Error);
            if (GUILayout.Button("Reimport Tileset Now", GUILayout.Height(32)))
                NyxaraTileAtlasImporter.Import();
            return;
        }

        if (!map.HasValidMap())
        {
            EditorGUILayout.HelpBox(
                "Map data is empty — click Fill / Bake to generate tile mesh.",
                MessageType.Warning);
            if (GUILayout.Button("Fill Grass + Bake Mesh", GUILayout.Height(32)))
            {
                Undo.RecordObject(map, "Fill Grass Tiles");
                int grass = map.Tileset.IndexOfTerrainId("Grass");
                if (grass < 0)
                    grass = map.Tileset.IndexOfTerrainId("LightGrass");
                map.FillTerrain(grass >= 0 ? grass : map.Tileset.BaseTerrainIndex);
                MarkDirty(map);
            }
        }

        EditorGUILayout.Space(10);
        EditorGUI.BeginChangeCheck();
        int layer = GUILayout.Toolbar(
            (int)_layer,
            new[] { "Terrain Painting", "Ground Level" });
        if (EditorGUI.EndChangeCheck())
            _layer = (PaintLayer)layer;

        if (_layer == PaintLayer.Terrain)
            DrawTerrainPainting(map);
        else
            DrawGroundLevel(map);

        DrawPaintControls(map);
        DrawTerrainMeshes(map);
    }

    void DrawTerrainPainting(PlanetTileMap map)
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Terrain Painting", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Handpainted splat blend. Keys 1–4 pick Grass, Dirt, Clay, Dark Grass.\n" +
            "Neighbors mix smoothly. Save a preset when the map looks right.",
            MessageType.Info);

        int count = map.Tileset.TerrainCount;
        _terrainBrush = Mathf.Clamp(_terrainBrush, 0, Mathf.Max(0, count - 1));
        int columns = Mathf.Min(4, Mathf.Max(1, count));
        EditorGUILayout.BeginVertical();
        Color prevBg = GUI.backgroundColor;
        for (int i = 0; i < count; i++)
        {
            if (i % columns == 0)
            {
                if (i > 0)
                    EditorGUILayout.EndHorizontal();
                EditorGUILayout.BeginHorizontal();
            }

            var t = map.Tileset.GetTerrain(i);
            string label = t != null ? t.displayName : $"Terrain {i}";
            Color preview = t != null ? t.previewColor : Color.gray;
            GUI.backgroundColor = i == _terrainBrush
                ? preview
                : Color.Lerp(preview, Color.gray, 0.45f);
            if (GUILayout.Toggle(i == _terrainBrush, label, "Button", GUILayout.Height(28)))
            {
                _terrainBrush = i;
                _layer = PaintLayer.Terrain;
            }
        }
        GUI.backgroundColor = prevBg;
        if (count > 0)
            EditorGUILayout.EndHorizontal();
        EditorGUILayout.EndVertical();

        DrawMapActions(map, includeFill: true);
    }

    void DrawMapActions(PlanetTileMap map, bool includeFill)
    {
        PlanetTileMapPreset bound = map.LastLoadedPreset;
        bool dirty = bound != null && map.ComputeContentHash() != bound.ComputeContentHash();
        string dropdownLabel = bound != null ? bound.DisplayName : "Presets";
        if (dirty)
            dropdownLabel += " *";

        EditorGUILayout.BeginHorizontal();
        if (includeFill && GUILayout.Button("Fill This Terrain"))
        {
            Undo.RecordObject(map, "Fill Terrain");
            map.FillTerrain(_terrainBrush);
            MarkDirty(map);
        }

        if (EditorGUILayout.DropdownButton(
                new GUIContent(dropdownLabel, "Load a saved painted map."),
                FocusType.Keyboard))
            ShowPresetMenu(map, bound);

        if (GUILayout.Button(
                new GUIContent("Save Preset", "Store the current painted map so you can load it later."),
                GUILayout.Width(96f)))
            SavePreset(map, bound, dirty);
        EditorGUILayout.EndHorizontal();

        if (dirty)
        {
            EditorGUILayout.HelpBox(
                $"Unsaved changes on \"{bound.DisplayName}\". Save Preset to keep this version.",
                MessageType.Info);
        }
    }

    static void ShowPresetMenu(PlanetTileMap map, PlanetTileMapPreset current)
    {
        var menu = new GenericMenu();
        PlanetTileMapPreset[] presets = FindPresets();
        if (presets.Length == 0)
        {
            menu.AddDisabledItem(new GUIContent("No presets yet — paint, then Save Preset"));
            menu.ShowAsContext();
            return;
        }

        for (int i = 0; i < presets.Length; i++)
        {
            PlanetTileMapPreset preset = presets[i];
            string label = PresetMenuPath(preset);
            menu.AddItem(new GUIContent(label), preset == current, () => ApplyPreset(map, preset));
        }

        menu.ShowAsContext();
    }

    static void ApplyPreset(PlanetTileMap map, PlanetTileMapPreset preset)
    {
        if (map == null || preset == null)
            return;

        if (!preset.HasValidData())
        {
            EditorUtility.DisplayDialog(
                "Tile Map Preset",
                $"\"{preset.DisplayName}\" has no map data. Paint a planet and Save Preset to fill it.",
                "OK");
            return;
        }

        if (preset.Tileset != null && map.Tileset != null && preset.Tileset != map.Tileset)
        {
            if (!EditorUtility.DisplayDialog(
                    "Different Tileset",
                    "This preset was saved with a different tileset. Terrain colors may not match.\n\nApply anyway?",
                    "Apply",
                    "Cancel"))
                return;
        }

        Undo.RecordObject(map, "Load Tile Map Preset");
        if (!map.ApplyPreset(preset))
        {
            EditorUtility.DisplayDialog("Tile Map Preset", "Could not apply this preset.", "OK");
            return;
        }

        MarkDirty(map);
        SceneView.RepaintAll();
    }

    static void SavePreset(PlanetTileMap map, PlanetTileMapPreset bound, bool dirty)
    {
        if (map == null || !map.HasValidMap())
        {
            EditorUtility.DisplayDialog(
                "Save Preset",
                "The map is empty. Paint some terrain first.",
                "OK");
            return;
        }

        if (bound != null)
        {
            var menu = new GenericMenu();
            string overwrite = dirty
                ? $"Overwrite \"{bound.DisplayName}\""
                : $"Overwrite \"{bound.DisplayName}\" (no changes)";
            menu.AddItem(new GUIContent(overwrite), false, () => OverwritePreset(map, bound));
            menu.AddItem(new GUIContent("Save As New…"), false, () => SavePresetAs(map, bound));
            menu.ShowAsContext();
            return;
        }

        SavePresetAs(map, null);
    }

    static void OverwritePreset(PlanetTileMap map, PlanetTileMapPreset preset)
    {
        if (map == null || preset == null)
            return;

        if (!EditorUtility.DisplayDialog(
                "Overwrite Preset",
                $"Replace \"{preset.DisplayName}\" with the current painted map?",
                "Overwrite",
                "Cancel"))
            return;

        Undo.RecordObject(preset, "Update Tile Map Preset");
        Undo.RecordObject(map, "Bind Tile Map Preset");
        map.CaptureToPreset(preset);
        EditorUtility.SetDirty(preset);
        MarkDirty(map);
        AssetDatabase.SaveAssets();
    }

    static void SavePresetAs(PlanetTileMap map, PlanetTileMapPreset bound)
    {
        if (map == null)
            return;

        EnsurePresetFolder();

        string folder = PlanetTileMapPresetEditor.DefaultFolder;
        string fileName = "New Tile Preset";
        if (bound != null)
        {
            string existing = AssetDatabase.GetAssetPath(bound);
            if (!string.IsNullOrEmpty(existing))
            {
                folder = System.IO.Path.GetDirectoryName(existing).Replace('\\', '/');
                fileName = bound.DisplayName + " Copy";
            }
        }

        string path = EditorUtility.SaveFilePanelInProject(
            "Save Tile Map Preset",
            fileName,
            "asset",
            "Save the current painted terrain and ground heights as a reusable preset.",
            folder);
        if (string.IsNullOrEmpty(path))
            return;

        var existingPreset = AssetDatabase.LoadAssetAtPath<PlanetTileMapPreset>(path);
        if (existingPreset != null)
        {
            OverwritePreset(map, existingPreset);
            return;
        }

        var preset = ScriptableObject.CreateInstance<PlanetTileMapPreset>();
        AssetDatabase.CreateAsset(preset, path);
        Undo.RecordObject(map, "Save Tile Map Preset");
        map.CaptureToPreset(preset);
        preset.SetDisplayName(System.IO.Path.GetFileNameWithoutExtension(path));
        EditorUtility.SetDirty(preset);
        MarkDirty(map);
        AssetDatabase.SaveAssets();
        EditorGUIUtility.PingObject(preset);
    }

    static PlanetTileMapPreset[] FindPresets()
    {
        string[] guids = AssetDatabase.FindAssets("t:PlanetTileMapPreset");
        var list = new System.Collections.Generic.List<PlanetTileMapPreset>(guids.Length);
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            var preset = AssetDatabase.LoadAssetAtPath<PlanetTileMapPreset>(path);
            if (preset != null)
                list.Add(preset);
        }

        list.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, System.StringComparison.OrdinalIgnoreCase));
        return list.ToArray();
    }

    static string PresetMenuPath(PlanetTileMapPreset preset)
    {
        string path = AssetDatabase.GetAssetPath(preset);
        if (!string.IsNullOrEmpty(path) && path.StartsWith(PlanetTileMapPresetEditor.DefaultFolder + "/", System.StringComparison.Ordinal))
            return preset.DisplayName;

        if (string.IsNullOrEmpty(path))
            return preset.DisplayName;

        string folder = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        if (folder.StartsWith("Assets/", System.StringComparison.Ordinal))
            folder = folder.Substring("Assets/".Length);
        return folder + "/" + preset.DisplayName;
    }

    static void EnsurePresetFolder()
    {
        PlanetTileMapPresetEditor.EnsureDefaultFolder();
    }

    void DrawGroundLevel(PlanetTileMap map)
    {
        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Ground Level", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Sculpt hills and pits. Neighbors blend so the surface stays connected.",
            MessageType.Info);

        SerializedProperty heightStepProp = serializedObject.FindProperty("heightStep");
        if (heightStepProp != null)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(
                heightStepProp,
                new GUIContent("Height Step", "World units per raise / lower."));
            if (EditorGUI.EndChangeCheck())
                serializedObject.ApplyModifiedProperties();
        }

        string[] tools = { "Raise", "Lower", "Set Height" };
        EditorGUI.BeginChangeCheck();
        int picked = GUILayout.Toolbar((int)_groundTool, tools);
        if (EditorGUI.EndChangeCheck())
        {
            _groundTool = (GroundTool)picked;
            _layer = PaintLayer.Ground;
        }

        _setHeight = EditorGUILayout.Slider(
            "Set Height",
            _setHeight,
            PlanetTileMap.MinHeight,
            PlanetTileMap.MaxHeight);

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Flatten (height " + PlanetTileMap.DefaultGroundHeight.ToString("0.#") + ")"))
        {
            Undo.RecordObject(map, "Flatten Ground");
            map.FillHeight(PlanetTileMap.DefaultGroundHeight);
            MarkDirty(map);
        }
        if (GUILayout.Button("Fill Set Height"))
        {
            Undo.RecordObject(map, "Fill Ground Height");
            map.FillHeight(_setHeight);
            MarkDirty(map);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);
        DrawMapActions(map, includeFill: false);
    }

    void DrawPaintControls(PlanetTileMap map)
    {
        EditorGUILayout.Space(8);
        _brushRadius = EditorGUILayout.IntSlider("Brush Radius", _brushRadius, 0, 10);
        _showGrid = EditorGUILayout.Toggle("Show Cell Grid", _showGrid);

        EditorGUILayout.BeginHorizontal();
        _eraseMode = GUILayout.Toggle(_eraseMode, new GUIContent("Erase", "Paint base terrain / opposite height (RMB also erases)"), "Button");
        _eyedropper = GUILayout.Toggle(_eyedropper, new GUIContent("Eyedropper (I)", "Click to pick terrain or height"), "Button");
        EditorGUILayout.EndHorizontal();

        if (!map.ShowTileVisuals)
            EditorGUILayout.HelpBox("Enable Show Tile Visuals to see painted tiles.", MessageType.Warning);

        Color prev = GUI.backgroundColor;
        GUI.backgroundColor = _paintMode ? new Color(0.35f, 0.85f, 0.45f) : new Color(0.95f, 0.75f, 0.25f);
        string label = _paintMode
            ? "PAINT MODE ON — Esc to stop"
            : "START PAINT MODE";
        if (GUILayout.Button(label, GUILayout.Height(40)))
        {
            if (_paintMode) ExitPaintMode();
            else EnterPaintMode(map);
        }
        GUI.backgroundColor = prev;

        if (_paintMode)
        {
            EditorGUILayout.HelpBox(
                "LMB paint · RMB opposite · Tab layer · [ ] size · 1-9 terrain · R Raise · L Lower · I eyedropper · F flood · Esc exit · Alt+LMB orbit",
                MessageType.Info);
        }

        if (GUILayout.Button("Bake / Refresh Mesh"))
        {
            Undo.RecordObject(map, "Bake Tile Mesh");
            map.RebuildVisuals();
            MarkDirty(map);
        }
    }

    void DrawTerrainMeshes(PlanetTileMap map)
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Terrain Meshes", EditorStyles.boldLabel);
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Attach Ridge / Cliff"))
        {
            SphericalPlanet planet = map.GetComponent<SphericalPlanet>();
            if (planet != null)
            {
                Undo.RegisterFullObjectHierarchyUndo(planet.gameObject, "Attach Terrain Meshes");
                int added = PlanetTestTerrain.Attach(planet);
                Debug.Log("[BackHome] Attached " + added + " terrain mesh(es).");
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(planet.gameObject.scene);
            }
        }
        if (GUILayout.Button("Remove Terrain Meshes"))
        {
            SphericalPlanet planet = map.GetComponent<SphericalPlanet>();
            if (planet != null)
            {
                Undo.RegisterFullObjectHierarchyUndo(planet.gameObject, "Remove Terrain Meshes");
                PlanetTestTerrain.Remove(planet);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(planet.gameObject.scene);
            }
        }
        EditorGUILayout.EndHorizontal();
    }

    static void EnterPaintMode(PlanetTileMap map)
    {
        _paintMode = true;
        Tools.current = Tool.None;
        Tools.viewTool = ViewTool.None;
        _lastLat = int.MinValue;
        _lastLon = int.MinValue;
        _strokeActive = false;
        if (map != null)
        {
            if (map.Tileset != null && !map.HasValidMap())
                map.FillTerrain(map.Tileset.BaseTerrainIndex);
            else
                map.RebuildVisuals();
        }
        SceneView.RepaintAll();
    }

    static void ExitPaintMode()
    {
        _paintMode = false;
        _strokeActive = false;
        _hoverLat = int.MinValue;
        _lastLat = int.MinValue;
        if (Tools.current == Tool.None)
            Tools.current = Tool.Move;
        SceneView.RepaintAll();
    }

    void OnSceneGUI()
    {
        var map = target as PlanetTileMap;
        if (map == null)
            return;

        SceneView sceneView = SceneView.currentDrawingSceneView;
        if (sceneView == null || sceneView.camera == null)
            return;

        Event e = Event.current;
        if (e == null)
            return;

        try
        {
            DrawSceneOverlay(map);

            if (e.type == EventType.Repaint)
            {
                if (_showGrid)
                    DrawGridPreview(map);
                if (_paintMode)
                    DrawBrushPreview(map);
            }

            UpdateHover(map);

            if (!_paintMode)
                return;

            if (Tools.current != Tool.None)
                Tools.current = Tool.None;

            HandleHotkeys(map, e);

            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            if (e.type == EventType.Layout || e.type == EventType.MouseMove)
                HandleUtility.AddDefaultControl(controlId);

            switch (e.GetTypeForControl(controlId))
            {
                case EventType.MouseDown:
                    if (e.alt)
                        break;
                    if (e.button == 0 || e.button == 1)
                    {
                        GUIUtility.hotControl = controlId;
                        _strokeActive = true;
                        Undo.RecordObject(map, _layer == PaintLayer.Ground ? "Sculpt Planet Ground" : "Paint Planet Terrain");
                        TryPaint(map, e.mousePosition, force: true, erase: e.button == 1 || _eraseMode);
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (GUIUtility.hotControl == controlId && _strokeActive && (e.button == 0 || e.button == 1) && !e.alt)
                    {
                        TryPaint(map, e.mousePosition, force: false, erase: e.button == 1 || _eraseMode);
                        e.Use();
                    }
                    break;

                case EventType.MouseUp:
                    if (GUIUtility.hotControl == controlId)
                    {
                        GUIUtility.hotControl = 0;
                        _strokeActive = false;
                        _lastLat = int.MinValue;
                        _lastLon = int.MinValue;
                        MarkDirty(map);
                        e.Use();
                    }
                    break;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    static void HandleHotkeys(PlanetTileMap map, Event e)
    {
        if (e.type != EventType.KeyDown)
            return;

        if (e.keyCode == KeyCode.Escape)
        {
            ExitPaintMode();
            e.Use();
            return;
        }

        if (e.keyCode == KeyCode.Tab)
        {
            _layer = _layer == PaintLayer.Terrain ? PaintLayer.Ground : PaintLayer.Terrain;
            e.Use();
            SceneView.RepaintAll();
            return;
        }

        if (e.keyCode == KeyCode.LeftBracket)
        {
            _brushRadius = Mathf.Max(0, _brushRadius - 1);
            e.Use();
            SceneView.RepaintAll();
            return;
        }

        if (e.keyCode == KeyCode.RightBracket)
        {
            _brushRadius = Mathf.Min(10, _brushRadius + 1);
            e.Use();
            SceneView.RepaintAll();
            return;
        }

        if (e.keyCode == KeyCode.I)
        {
            _eyedropper = !_eyedropper;
            e.Use();
            return;
        }

        if (e.keyCode == KeyCode.E)
        {
            _eraseMode = !_eraseMode;
            e.Use();
            return;
        }

        if (e.keyCode == KeyCode.F)
        {
            _floodPending = true;
            if (map.Tileset != null && TryPickCell(map, e.mousePosition, out int lat, out int lon))
            {
                Undo.RecordObject(map, "Flood Fill");
                if (_layer == PaintLayer.Ground)
                {
                    if (_groundTool == GroundTool.Set)
                        PlanetBlobAutotile.FloodFillHeight(map, lat, lon, _eraseMode ? PlanetTileMap.DefaultGroundHeight : _setHeight);
                    else if (_groundTool == GroundTool.Raise)
                        map.PaintHeightDeltaBrush(lat, lon, _eraseMode ? -1 : 1, 0, rebuild: true);
                    else
                        map.PaintHeightDeltaBrush(lat, lon, _eraseMode ? 1 : -1, 0, rebuild: true);
                }
                else
                    PlanetBlobAutotile.FloodFill(map, lat, lon, _terrainBrush);
                MarkDirty(map);
            }
            _floodPending = false;
            e.Use();
            return;
        }

        if (e.keyCode == KeyCode.R || e.keyCode == KeyCode.Equals || e.keyCode == KeyCode.Plus)
        {
            _layer = PaintLayer.Ground;
            _groundTool = GroundTool.Raise;
            _eraseMode = false;
            e.Use();
            return;
        }

        if (e.keyCode == KeyCode.L || e.keyCode == KeyCode.Minus)
        {
            _layer = PaintLayer.Ground;
            _groundTool = GroundTool.Lower;
            _eraseMode = false;
            e.Use();
            return;
        }

        if (_layer == PaintLayer.Ground
            && e.keyCode >= KeyCode.Alpha0
            && e.keyCode <= KeyCode.Alpha4)
        {
            int value = e.keyCode - KeyCode.Alpha0;
            if (value >= PlanetTileMap.MinHeight)
            {
                _setHeight = value;
                _groundTool = GroundTool.Set;
                _eraseMode = false;
                e.Use();
                return;
            }
        }

        if (map.Tileset != null && e.keyCode >= KeyCode.Alpha1 && e.keyCode <= KeyCode.Alpha9)
        {
            int idx = e.keyCode - KeyCode.Alpha1;
            if (idx < map.Tileset.TerrainCount)
            {
                _terrainBrush = idx;
                _layer = PaintLayer.Terrain;
                _eraseMode = false;
                e.Use();
            }
        }
    }

    static void UpdateHover(PlanetTileMap map)
    {
        if (!_paintMode)
        {
            _hoverLat = int.MinValue;
            return;
        }

        Event e = Event.current;
        if (e == null)
            return;

        if (TryPickCell(map, e.mousePosition, out int lat, out int lon))
        {
            _hoverLat = lat;
            _hoverLon = lon;
        }
        else
        {
            _hoverLat = int.MinValue;
        }
    }

    static void TryPaint(PlanetTileMap map, Vector2 guiPoint, bool force, bool erase)
    {
        if (!TryPickCell(map, guiPoint, out int lat, out int lon))
            return;

        if (_eyedropper)
        {
            if (_layer == PaintLayer.Ground)
            {
                _setHeight = map.GetHeight(lat, lon);
                _groundTool = GroundTool.Set;
            }
            else
            {
                _terrainBrush = map.GetTerrain(lat, lon);
            }
            _eyedropper = false;
            _eraseMode = false;
            return;
        }

        if (_floodPending)
            return;

        if (!force && lat == _lastLat && lon == _lastLon)
            return;

        if (_layer == PaintLayer.Ground)
        {
            if (_groundTool == GroundTool.Raise)
                map.PaintHeightDeltaBrush(lat, lon, erase ? -1 : 1, _brushRadius, rebuild: true);
            else if (_groundTool == GroundTool.Lower)
                map.PaintHeightDeltaBrush(lat, lon, erase ? 1 : -1, _brushRadius, rebuild: true);
            else
            {
                float height = erase ? PlanetTileMap.DefaultGroundHeight : _setHeight;
                map.PaintHeightBrush(lat, lon, height, _brushRadius, rebuild: true);
            }
        }
        else
        {
            int terrain = erase && map.Tileset != null
                ? map.Tileset.BaseTerrainIndex
                : _terrainBrush;
            PlanetBlobAutotile.PaintTerrain(map, lat, lon, terrain, _brushRadius, rebuild: true);
        }

        _lastLat = lat;
        _lastLon = lon;
    }

    static bool TryPickCell(PlanetTileMap map, Vector2 guiPoint, out int lat, out int lon)
    {
        lat = 0;
        lon = 0;
        if (!TryPickPlanetPoint(map, guiPoint, out Vector3 worldPoint))
            return false;
        return map.WorldToCell(worldPoint, out lat, out lon);
    }

    static void DrawBrushPreview(PlanetTileMap map)
    {
        if (_hoverLat == int.MinValue || map.Tileset == null || map.LongitudeBands <= 0)
            return;

        Color c;
        if (_layer == PaintLayer.Ground)
        {
            bool lifting = (_groundTool == GroundTool.Raise && !_eraseMode)
                || (_groundTool == GroundTool.Lower && _eraseMode)
                || (_groundTool == GroundTool.Set && !_eraseMode && _setHeight > PlanetTileMap.DefaultGroundHeight);
            c = lifting
                ? new Color(0.85f, 0.45f, 0.2f, 0.4f)
                : new Color(0.35f, 0.7f, 0.35f, 0.4f);
        }
        else
        {
            var t = map.Tileset.GetTerrain(_terrainBrush);
            c = t != null ? t.previewColor : Color.white;
            c.a = 0.35f;
        }
        Handles.color = c;

        for (int dLat = -_brushRadius; dLat <= _brushRadius; dLat++)
        {
            int lat = _hoverLat + dLat;
            if (lat < 0 || lat >= map.LatitudeBands)
                continue;
            for (int dLon = -_brushRadius; dLon <= _brushRadius; dLon++)
            {
                if (dLat * dLat + dLon * dLon > _brushRadius * _brushRadius)
                    continue;
                int lon = Mod(_hoverLon + dLon, map.LongitudeBands);
                if (!map.TryGetCellCenter(lat, lon, out Vector3 p))
                    continue;
                float size = map.ApproximateTileWorldSize * 0.35f;
                Vector3 normal = p - map.transform.position;
                if (normal.sqrMagnitude < 0.0001f)
                    continue;
                Handles.DrawSolidDisc(p, normal.normalized, size);
            }
        }
    }

    static void DrawSceneOverlay(PlanetTileMap map)
    {
        Handles.BeginGUI();
        float h = _paintMode ? 120f : 52f;
        Rect rect = new Rect(12f, 12f, 360f, h);
        GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
        GUILayout.BeginArea(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, rect.height - 10f));

        if (_paintMode)
        {
            string brushName;
            string mode;
            if (_layer == PaintLayer.Ground)
            {
                mode = "GROUND";
                if (_groundTool == GroundTool.Raise)
                    brushName = _eraseMode ? "Lower" : "Raise";
                else if (_groundTool == GroundTool.Lower)
                    brushName = _eraseMode ? "Raise" : "Lower";
                else
                    brushName = _eraseMode ? "Flatten" : "Set " + _setHeight.ToString("0.##");
            }
            else
            {
                var t = map.Tileset != null ? map.Tileset.GetTerrain(_terrainBrush) : null;
                brushName = t != null ? t.displayName : _terrainBrush.ToString();
                mode = "TERRAIN";
            }

            if (_eyedropper)
                mode = "EYEDROPPER";
            float hoverH = _hoverLat != int.MinValue ? map.GetHeight(_hoverLat, _hoverLon) : 0f;
            GUILayout.Label($"{mode} — {brushName}  r:{_brushRadius}  h:{hoverH:0.##}", EditorStyles.boldLabel);
            GUILayout.Label("LMB paint · RMB opposite · Tab layer · [ ] size");
            GUILayout.Label("R raise · L lower · I pick · F flood · Esc stop");
        }
        else
        {
            GUILayout.Label("Planet Tilemap", EditorStyles.boldLabel);
            GUILayout.Label("Select planet → START PAINT MODE");
        }

        GUILayout.EndArea();
        Handles.EndGUI();
    }

    static bool TryPickPlanetPoint(PlanetTileMap map, Vector2 guiPoint, out Vector3 worldPoint)
    {
        worldPoint = default;
        Ray ray = HandleUtility.GUIPointToWorldRay(guiPoint);

        RaycastHit[] hits = Physics.RaycastAll(ray, 10000f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider != null
                && hits[i].collider.GetComponentInParent<PlanetTileMap>() == map)
            {
                worldPoint = hits[i].point;
                return true;
            }
        }

        SphericalPlanet planet = map.GetComponent<SphericalPlanet>();
        if (planet == null)
            return false;

        if (!RaySphere(ray, planet.Center, Mathf.Max(0.01f, planet.Radius + 2f), out float t))
            return false;

        worldPoint = ray.GetPoint(t);
        Vector3 dir = (worldPoint - planet.Center).normalized;
        worldPoint = planet.Center + dir * planet.GetTerrainRadius(dir);
        return true;
    }

    static bool RaySphere(Ray ray, Vector3 center, float radius, out float t)
    {
        t = 0f;
        Vector3 oc = ray.origin - center;
        float b = Vector3.Dot(oc, ray.direction);
        float c = Vector3.Dot(oc, oc) - radius * radius;
        float disc = b * b - c;
        if (disc < 0f)
            return false;
        float s = Mathf.Sqrt(disc);
        float t0 = -b - s;
        float t1 = -b + s;
        t = t0 >= 0f ? t0 : t1;
        return t >= 0f;
    }

    static void DrawGridPreview(PlanetTileMap map)
    {
        SphericalPlanet planet = map.GetComponent<SphericalPlanet>();
        if (planet == null || !map.HasValidMap())
            return;

        int latBands = map.LatitudeBands;
        int lonBands = map.LongitudeBands;
        if (latBands <= 0 || lonBands <= 0)
            return;

        Handles.color = new Color(1f, 1f, 1f, 0.12f);
        float latStep = 180f / latBands;
        float lonStep = 360f / lonBands;
        int latStepDraw = Mathf.Max(1, latBands / 12);
        int lonStepDraw = Mathf.Max(1, lonBands / 16);

        for (int lat = 0; lat <= latBands; lat += latStepDraw)
        {
            float latDeg = -90f + lat * latStep;
            Vector3 prev = Vector3.zero;
            bool hasPrev = false;
            for (int lon = 0; lon <= lonBands; lon++)
            {
                Vector3 p = SurfacePoint(planet, latDeg, lon * lonStep);
                if (hasPrev)
                    Handles.DrawLine(prev, p);
                prev = p;
                hasPrev = true;
            }
        }

        for (int lon = 0; lon < lonBands; lon += lonStepDraw)
        {
            float lonDeg = lon * lonStep;
            Vector3 prev = Vector3.zero;
            bool hasPrev = false;
            for (int lat = 0; lat <= latBands; lat++)
            {
                Vector3 p = SurfacePoint(planet, -90f + lat * latStep, lonDeg);
                if (hasPrev)
                    Handles.DrawLine(prev, p);
                prev = p;
                hasPrev = true;
            }
        }
    }

    static Vector3 SurfacePoint(SphericalPlanet planet, float latDeg, float lonDeg)
    {
        float lat = latDeg * Mathf.Deg2Rad;
        float lon = lonDeg * Mathf.Deg2Rad;
        Vector3 up = new Vector3(
            Mathf.Cos(lat) * Mathf.Cos(lon),
            Mathf.Sin(lat),
            Mathf.Cos(lat) * Mathf.Sin(lon));
        return planet.Center + up * (planet.GetTerrainRadius(up) + 0.05f);
    }

    static int Mod(int value, int modulus)
    {
        if (modulus <= 0)
            return 0;
        int m = value % modulus;
        return m < 0 ? m + modulus : m;
    }

    static void MarkDirty(PlanetTileMap map)
    {
        if (map == null)
            return;
        EditorUtility.SetDirty(map);
        if (PrefabUtility.IsPartOfPrefabInstance(map))
            PrefabUtility.RecordPrefabInstancePropertyModifications(map);
    }
}
