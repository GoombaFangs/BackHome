using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PlanetTileMap))]
public class PlanetTileMapEditor : Editor
{
    static bool _paintMode;
    static int _paintIndex;
    static int _brushRadius;
    static bool _showGrid = true;
    static int _lastLat = int.MinValue;
    static int _lastLon = int.MinValue;

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
            new GUIContent(
                "Tiles Around Equator",
                "Higher = smaller tiles. 24 = big blocks, 72 = medium, 128+ = fine."));
        if (EditorGUI.EndChangeCheck())
            serializedObject.ApplyModifiedProperties();

        EditorGUILayout.HelpBox(
            $"Approx tile width: ~{map.ApproximateTileWorldSize:0.0} world units\n" +
            $"Grid: {map.LongitudeBands} × {map.LatitudeBands} = {map.CellCount} cells\n" +
            "After changing size, press Apply Tile Size (rebuilds the map).",
            MessageType.Info);

        if (GUILayout.Button("Apply Tile Size (Rebuild Grid)", GUILayout.Height(28)))
        {
            Undo.RecordObject(map, "Apply Planet Tile Size");
            map.SetTilesAroundEquator(tilesAroundProp.intValue, refillWithFillTile: true);
            MarkDirty(map);
        }

        EditorGUILayout.Space(8);
        DrawPropertiesExcluding(serializedObject, "m_Script", "tilesAroundEquator");
        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Tilemap Painting", EditorStyles.boldLabel);

        PlanetTilePalette palette = map.Palette;
        if (palette == null || palette.Count == 0)
        {
            EditorGUILayout.HelpBox("Assign a PlanetTilePalette first.", MessageType.Warning);
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
        _brushRadius = EditorGUILayout.IntSlider("Brush Radius (cells)", _brushRadius, 0, 6);
        _showGrid = EditorGUILayout.Toggle("Show Cell Grid", _showGrid);

        if (!map.ShowTileVisuals)
            EditorGUILayout.HelpBox("Enable Show Tile Visuals so you can see Grass / Dirt.", MessageType.Warning);

        EditorGUILayout.Space(4);
        Color prev = GUI.backgroundColor;
        GUI.backgroundColor = _paintMode ? new Color(0.35f, 0.85f, 0.45f) : new Color(0.95f, 0.75f, 0.25f);
        string label = _paintMode
            ? "PAINT MODE ON — click planet to paint (Esc / button to stop)"
            : "START PAINT MODE";
        if (GUILayout.Button(label, GUILayout.Height(36)))
        {
            if (_paintMode)
                ExitPaintMode();
            else
                EnterPaintMode();
        }
        GUI.backgroundColor = prev;

        if (_paintMode)
        {
            EditorGUILayout.HelpBox(
                "Paint mode captures left-click:\n" +
                "• LMB drag = paint\n" +
                "• Alt + LMB = orbit camera\n" +
                "• MMB / RMB = pan / orbit as usual\n" +
                "• Esc = exit paint mode",
                MessageType.Info);
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Rebuild Grid From Equator"))
        {
            Undo.RecordObject(map, "Rebuild Grid Size");
            map.SetTilesAroundEquator(map.TilesAroundEquator, refillWithFillTile: true);
            MarkDirty(map);
        }

        if (GUILayout.Button("Fill All With Brush"))
        {
            Undo.RecordObject(map, "Fill Planet Tiles");
            map.FillAll(_paintIndex);
            MarkDirty(map);
        }
        EditorGUILayout.EndHorizontal();

        if (GUILayout.Button("Bake / Refresh Tile Mesh"))
        {
            Undo.RecordObject(map, "Bake Planet Tile Mesh");
            map.RebuildVisuals();
            MarkDirty(map);
        }

        serializedObject.ApplyModifiedProperties();
        SceneView.RepaintAll();
    }

    static void EnterPaintMode()
    {
        _paintMode = true;
        Tools.current = Tool.None;
        Tools.viewTool = ViewTool.None;
        _lastLat = int.MinValue;
        _lastLon = int.MinValue;

        // Make sure the tile surface is the one you see/paint.
        var maps = Object.FindObjectsByType<PlanetTileMap>(FindObjectsSortMode.None);
        for (int i = 0; i < maps.Length; i++)
        {
            if (maps[i] != null)
                maps[i].RebuildVisuals();
        }

        SceneView.RepaintAll();
    }

    static void ExitPaintMode()
    {
        _paintMode = false;
        _lastLat = int.MinValue;
        _lastLon = int.MinValue;
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

        if (!_paintMode)
            return;

        // Keep View tool from stealing LMB while painting.
        if (Tools.current != Tool.None)
            Tools.current = Tool.None;

        Event e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        {
            ExitPaintMode();
            e.Use();
            return;
        }

        int controlId = GUIUtility.GetControlID(FocusType.Passive);

        // CRITICAL: register on Layout so Unity won't use LMB for camera orbit.
        if (e.type == EventType.Layout || e.type == EventType.MouseMove)
            HandleUtility.AddDefaultControl(controlId);

        switch (e.GetTypeForControl(controlId))
        {
            case EventType.MouseDown:
                if (e.button == 0 && !e.alt)
                {
                    GUIUtility.hotControl = controlId;
                    TryPaint(map, e.mousePosition, force: true);
                    e.Use();
                }
                break;

            case EventType.MouseDrag:
                if (GUIUtility.hotControl == controlId && e.button == 0 && !e.alt)
                {
                    TryPaint(map, e.mousePosition, force: false);
                    e.Use();
                }
                break;

            case EventType.MouseUp:
                if (GUIUtility.hotControl == controlId && e.button == 0)
                {
                    GUIUtility.hotControl = 0;
                    _lastLat = int.MinValue;
                    _lastLon = int.MinValue;
                    e.Use();
                }
                break;
        }
    }

    static void TryPaint(PlanetTileMap map, Vector2 guiPoint, bool force)
    {
        if (!TryPickPlanetPoint(map, guiPoint, out Vector3 worldPoint))
            return;

        if (!map.WorldToCell(worldPoint, out int lat, out int lon))
            return;

        if (!force && lat == _lastLat && lon == _lastLon)
            return;

        Undo.RecordObject(map, "Paint Planet Tile");
        if (map.PaintBrush(lat, lon, _paintIndex, _brushRadius))
            MarkDirty(map);

        _lastLat = lat;
        _lastLon = lon;
    }

    static void DrawSceneOverlay(PlanetTileMap map)
    {
        Handles.BeginGUI();
        Rect rect = new Rect(12f, 12f, 320f, _paintMode ? 78f : 52f);
        GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
        GUILayout.BeginArea(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, rect.height - 10f));

        if (_paintMode)
        {
            string brush = map.Palette != null && map.Palette.GetEntry(_paintIndex) != null
                ? map.Palette.GetEntry(_paintIndex).displayName
                : _paintIndex.ToString();
            GUILayout.Label($"PAINT MODE — brush: {brush}  radius: {_brushRadius}", EditorStyles.boldLabel);
            GUILayout.Label("LMB paint · Alt+LMB orbit · Esc stop");
        }
        else
        {
            GUILayout.Label("Planet Tilemap", EditorStyles.boldLabel);
            GUILayout.Label("Select planet → press START PAINT MODE");
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
        // Project onto planet surface direction for better cell picking with hills.
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

    static void MarkDirty(PlanetTileMap map)
    {
        EditorUtility.SetDirty(map);
        if (PrefabUtility.IsPartOfPrefabInstance(map))
            PrefabUtility.RecordPrefabInstancePropertyModifications(map);
    }
}
