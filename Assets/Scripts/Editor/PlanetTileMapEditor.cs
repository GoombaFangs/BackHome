using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PlanetTileMap))]
public class PlanetTileMapEditor : Editor
{
    static bool _paintMode;
    static int _terrainBrush;
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

    void OnEnable()
    {
        SceneView.duringSceneGui -= DuringSceneGui;
        SceneView.duringSceneGui += DuringSceneGui;
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= DuringSceneGui;
        if (_paintMode)
            ExitPaintMode();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var map = (PlanetTileMap)target;

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
        DrawPropertiesExcluding(serializedObject, "m_Script", "tilesAroundEquator", "tileIndices", "terrainIds");
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Terrain Painting", EditorStyles.boldLabel);

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

        if (map.Tileset.Texture == null || map.Tileset.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "Tileset is empty (no texture / entries).\n" +
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
                map.FillTerrain(map.Tileset.BaseTerrainIndex);
                MarkDirty(map);
            }
        }

        string[] names = new string[map.Tileset.TerrainCount];
        for (int i = 0; i < map.Tileset.TerrainCount; i++)
        {
            var t = map.Tileset.GetTerrain(i);
            names[i] = t != null ? t.displayName : $"Terrain {i}";
        }

        _terrainBrush = Mathf.Clamp(_terrainBrush, 0, map.Tileset.TerrainCount - 1);
        _terrainBrush = GUILayout.Toolbar(_terrainBrush, names);
        _brushRadius = EditorGUILayout.IntSlider("Brush Radius", _brushRadius, 0, 10);
        _showGrid = EditorGUILayout.Toggle("Show Cell Grid", _showGrid);

        EditorGUILayout.BeginHorizontal();
        _eraseMode = GUILayout.Toggle(_eraseMode, new GUIContent("Erase", "Paint base terrain (RMB also erases)"), "Button");
        _eyedropper = GUILayout.Toggle(_eyedropper, new GUIContent("Eyedropper (I)", "Click to pick terrain"), "Button");
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
                "LMB paint · RMB erase · [ ] brush size · 1-9 terrain · I eyedropper · F flood · Esc exit · Alt+LMB orbit",
                MessageType.Info);
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Fill With Terrain"))
        {
            Undo.RecordObject(map, "Fill Terrain");
            map.FillTerrain(_terrainBrush);
            MarkDirty(map);
        }
        if (GUILayout.Button("Resolve Autotile"))
        {
            Undo.RecordObject(map, "Resolve Autotile");
            PlanetBlobAutotile.ResolveAll(map);
            MarkDirty(map);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Generate Continents"))
        {
            Undo.RecordObject(map, "Generate Continents");
            map.FillTerrain(map.Tileset.BaseTerrainIndex);
            PlanetBlobAutotile.GenerateContinents(map, seed: 11);
            MarkDirty(map);
        }
        if (GUILayout.Button("Bake / Refresh Mesh"))
        {
            Undo.RecordObject(map, "Bake Tile Mesh");
            map.RebuildVisuals();
            MarkDirty(map);
        }
        EditorGUILayout.EndHorizontal();

        SceneView.RepaintAll();
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

    void DuringSceneGui(SceneView sceneView)
    {
        var map = target as PlanetTileMap;
        if (map == null)
            return;

        if (_showGrid)
            DrawGridPreview(map);

        DrawSceneOverlay(map);
        UpdateHover(map);

        if (_paintMode)
            DrawBrushPreview(map);

        if (!_paintMode)
            return;

        if (Tools.current != Tool.None)
            Tools.current = Tool.None;

        Event e = Event.current;
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
                    Undo.RecordObject(map, "Paint Planet Terrain");
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
            if (TryPickCell(map, e.mousePosition, out int lat, out int lon))
            {
                Undo.RecordObject(map, "Flood Fill Terrain");
                int terrain = _eraseMode ? map.Tileset.BaseTerrainIndex : _terrainBrush;
                PlanetBlobAutotile.FloodFill(map, lat, lon, terrain);
                MarkDirty(map);
            }
            _floodPending = false;
            e.Use();
            return;
        }

        if (map.Tileset != null && e.keyCode >= KeyCode.Alpha1 && e.keyCode <= KeyCode.Alpha9)
        {
            int idx = e.keyCode - KeyCode.Alpha1;
            if (idx < map.Tileset.TerrainCount)
            {
                _terrainBrush = idx;
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
            _terrainBrush = map.GetTerrain(lat, lon);
            _eyedropper = false;
            _eraseMode = false;
            return;
        }

        if (_floodPending)
            return;

        if (!force && lat == _lastLat && lon == _lastLon)
            return;

        int terrain = erase
            ? (map.Tileset != null ? map.Tileset.BaseTerrainIndex : 0)
            : _terrainBrush;

        PlanetBlobAutotile.PaintTerrain(map, lat, lon, terrain, _brushRadius, rebuild: true);
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
        if (_hoverLat == int.MinValue || map.Tileset == null)
            return;

        var t = map.Tileset.GetTerrain(_eraseMode ? map.Tileset.BaseTerrainIndex : _terrainBrush);
        Color c = t != null ? t.previewColor : Color.white;
        c.a = 0.35f;
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
                Handles.DrawSolidDisc(p, (p - map.transform.position).normalized, size);
            }
        }
    }

    static void DrawSceneOverlay(PlanetTileMap map)
    {
        Handles.BeginGUI();
        float h = _paintMode ? 96f : 52f;
        Rect rect = new Rect(12f, 12f, 360f, h);
        GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
        GUILayout.BeginArea(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, rect.height - 10f));

        if (_paintMode)
        {
            string terrainName = "?";
            if (map.Tileset != null)
            {
                int ti = _eraseMode ? map.Tileset.BaseTerrainIndex : _terrainBrush;
                var t = map.Tileset.GetTerrain(ti);
                terrainName = t != null ? t.displayName : ti.ToString();
            }

            string mode = _eyedropper ? "EYEDROPPER" : _eraseMode ? "ERASE" : "PAINT";
            GUILayout.Label($"{mode} — {terrainName}  r:{_brushRadius}", EditorStyles.boldLabel);
            GUILayout.Label("LMB paint · RMB erase · [ ] size · 1-9 terrain");
            GUILayout.Label("I pick · F flood · Esc stop · Alt orbit");
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

        Handles.color = new Color(1f, 1f, 1f, 0.12f);
        int latBands = map.LatitudeBands;
        int lonBands = map.LongitudeBands;
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
        int m = value % modulus;
        return m < 0 ? m + modulus : m;
    }

    static void MarkDirty(PlanetTileMap map)
    {
        EditorUtility.SetDirty(map);
        if (PrefabUtility.IsPartOfPrefabInstance(map))
            PrefabUtility.RecordPrefabInstancePropertyModifications(map);
    }
}
