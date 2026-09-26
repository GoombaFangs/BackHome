using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(PlanetEnvironmentBrush))]
public class PlanetEnvironmentBrushEditor : Editor
{
    const string LibraryRoot = "Assets/Resources/Galaxy/Nyxara/Environment";

    static readonly string[] LibraryFolders =
    {
        LibraryRoot + "/Grass",
        LibraryRoot + "/Rock",
        LibraryRoot + "/Trees",
        LibraryRoot + "/Camp"
    };

    static bool _paintMode;
    static bool _eraseMode;
    static int _category;
    static int _specificIndex = -1;
    static bool _strokeActive;
    static bool _hasSample;
    static Vector3 _lastSample;
    static int _undoGroup;

    readonly List<Vector3> _occupied = new();
    GameObject _preview;
    GameObject _previewSource;

    void OnDisable()
    {
        DestroyPreview();
        if (_paintMode)
            ExitPaintMode();
    }

    public override void OnInspectorGUI()
    {
        var brush = target as PlanetEnvironmentBrush;
        if (brush == null)
            return;

        serializedObject.Update();
        EnsureDefaultCategories();

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Environment Brush", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Pick Grass, Rocks, or Trees. Random drops a mix from that list; a specific prefab paints only that one.\n" +
            "Start Paint Mode, then drag on the planet. Instances are parented under this object.\n" +
            "LMB paint · RMB or Shift erase · [ ] brush size · 0 random · 1-9 prefab · Esc stop · Alt orbit",
            MessageType.Info);

        DrawCategoryToolbar(brush);

        SerializedProperty categoryProp = serializedObject.FindProperty("categories").GetArrayElementAtIndex(_category);
        DrawPalette(categoryProp);
        DrawBrushSettings(categoryProp);

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Placement", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty("hover"));
        SerializedProperty randomYaw = serializedObject.FindProperty("randomYaw");
        EditorGUILayout.PropertyField(randomYaw);
        using (new EditorGUI.DisabledScope(randomYaw.boolValue))
            EditorGUILayout.PropertyField(serializedObject.FindProperty("yaw"));

        serializedObject.ApplyModifiedProperties();

        EditorGUILayout.Space(8);
        _eraseMode = GUILayout.Toggle(
            _eraseMode,
            new GUIContent("Erase", "Drag removes instances of this category (RMB and Shift do this too)"),
            "Button");

        Color prev = GUI.backgroundColor;
        GUI.backgroundColor = _paintMode ? new Color(0.35f, 0.85f, 0.45f) : new Color(0.95f, 0.75f, 0.25f);
        string label = _paintMode ? "PAINT MODE ON — Esc to stop" : "START PAINT MODE";
        if (GUILayout.Button(label, GUILayout.Height(40)))
        {
            if (_paintMode)
                ExitPaintMode();
            else
                EnterPaintMode();
        }

        GUI.backgroundColor = prev;

        if (Application.isPlaying)
            EditorGUILayout.HelpBox("Paint Mode is for the editor. Exit Play mode to paint.", MessageType.Warning);
        else if (_paintMode && brush.CountAssignedPrefabs(_category) == 0 && !_eraseMode)
            EditorGUILayout.HelpBox("This category has no prefabs yet.", MessageType.Warning);

        PlanetEnvironmentBrush.Category category = brush.GetCategory(_category);
        if (category != null)
        {
            Transform folder = FindCategoryFolder(brush.transform, FolderName(category));
            int placed = folder != null ? folder.childCount : 0;
            EditorGUILayout.LabelField("Placed in " + FolderName(category), placed.ToString());
        }
    }

    void DrawCategoryToolbar(PlanetEnvironmentBrush brush)
    {
        int count = brush.CategoryCount;
        _category = Mathf.Clamp(_category, 0, Mathf.Max(0, count - 1));

        EditorGUILayout.BeginHorizontal();
        Color prev = GUI.backgroundColor;
        for (int i = 0; i < count; i++)
        {
            PlanetEnvironmentBrush.Category category = brush.GetCategory(i);
            string name = category != null && !string.IsNullOrEmpty(category.displayName)
                ? category.displayName
                : "Category " + i;
            Color tint = category != null ? category.previewColor : Color.gray;
            GUI.backgroundColor = i == _category ? tint : Color.Lerp(tint, Color.gray, 0.45f);
            if (GUILayout.Toggle(i == _category, name, "Button", GUILayout.Height(28)))
            {
                if (_category != i)
                {
                    _category = i;
                    _specificIndex = -1;
                    DestroyPreview();
                }
            }
        }

        GUI.backgroundColor = prev;
        EditorGUILayout.EndHorizontal();
    }

    void DrawPalette(SerializedProperty categoryProp)
    {
        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Prefabs", EditorStyles.boldLabel);

        SerializedProperty prefabs = categoryProp.FindPropertyRelative("prefabs");
        int removeAt = -1;
        for (int i = 0; i < prefabs.arraySize; i++)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(prefabs.GetArrayElementAtIndex(i), GUIContent.none);
            if (GUILayout.Button("X", GUILayout.Width(22)))
                removeAt = i;
            EditorGUILayout.EndHorizontal();
        }

        if (removeAt >= 0)
        {
            prefabs.DeleteArrayElementAtIndex(removeAt);
            if (removeAt < prefabs.arraySize && prefabs.GetArrayElementAtIndex(removeAt).objectReferenceValue == null)
                prefabs.DeleteArrayElementAtIndex(removeAt);
            if (_specificIndex >= prefabs.arraySize)
                _specificIndex = -1;
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add Prefab"))
        {
            int next = prefabs.arraySize;
            prefabs.arraySize = next + 1;
            prefabs.GetArrayElementAtIndex(next).objectReferenceValue = null;
        }

        if (GUILayout.Button("Load Nyxara Prefabs"))
            LoadAllFromLibrary();
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Paint With", EditorStyles.boldLabel);
        DrawPrefabPicker(prefabs);
    }

    void DrawPrefabPicker(SerializedProperty prefabs)
    {
        const int columns = 3;
        Color prev = GUI.backgroundColor;
        bool open = false;
        int shown = 0;

        GUI.backgroundColor = _specificIndex < 0
            ? new Color(0.45f, 0.75f, 1f)
            : new Color(0.75f, 0.75f, 0.75f);
        if (GUILayout.Toggle(_specificIndex < 0, "Random", "Button", GUILayout.Height(24)))
            _specificIndex = -1;
        GUI.backgroundColor = prev;

        for (int i = 0; i < prefabs.arraySize; i++)
        {
            var prefab = prefabs.GetArrayElementAtIndex(i).objectReferenceValue as GameObject;
            if (prefab == null)
                continue;

            if (!open)
            {
                EditorGUILayout.BeginHorizontal();
                open = true;
            }

            bool selected = _specificIndex == i;
            GUI.backgroundColor = selected ? new Color(0.45f, 0.75f, 1f) : Color.white;
            if (GUILayout.Toggle(selected, prefab.name, "Button", GUILayout.Height(24)))
                _specificIndex = i;

            shown++;
            if (shown % columns == 0)
            {
                EditorGUILayout.EndHorizontal();
                open = false;
            }
        }

        GUI.backgroundColor = prev;
        if (open)
            EditorGUILayout.EndHorizontal();
    }

    static void DrawBrushSettings(SerializedProperty categoryProp)
    {
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("Brush", EditorStyles.boldLabel);

        SerializedProperty radius = categoryProp.FindPropertyRelative("brushRadius");
        SerializedProperty spacing = categoryProp.FindPropertyRelative("spacing");
        SerializedProperty amount = categoryProp.FindPropertyRelative("amount");
        SerializedProperty scaleMin = categoryProp.FindPropertyRelative("scaleMin");
        SerializedProperty scaleMax = categoryProp.FindPropertyRelative("scaleMax");

        radius.floatValue = EditorGUILayout.Slider("Radius", radius.floatValue, 0f, 25f);
        spacing.floatValue = EditorGUILayout.Slider("Spacing", spacing.floatValue, 0.2f, 20f);
        amount.intValue = EditorGUILayout.IntSlider("Amount", amount.intValue, 1, 12);
        scaleMin.floatValue = EditorGUILayout.Slider("Scale Min", scaleMin.floatValue, 0.1f, 4f);
        scaleMax.floatValue = EditorGUILayout.Slider("Scale Max", scaleMax.floatValue, 0.1f, 4f);
        if (scaleMax.floatValue < scaleMin.floatValue)
            scaleMax.floatValue = scaleMin.floatValue;
    }

    void LoadAllFromLibrary()
    {
        if (!EditorUtility.DisplayDialog(
                "Environment Brush",
                "Replace the Grass, Rocks, Trees, and Camp prefab lists with the Nyxara library?",
                "Load",
                "Cancel"))
        {
            return;
        }

        SerializedProperty categories = serializedObject.FindProperty("categories");
        int count = Mathf.Min(categories.arraySize, LibraryFolders.Length);
        for (int c = 0; c < count; c++)
        {
            if (!AssetDatabase.IsValidFolder(LibraryFolders[c]))
                continue;

            List<GameObject> found = FindPrefabs(LibraryFolders[c]);
            SerializedProperty prefabs = categories.GetArrayElementAtIndex(c).FindPropertyRelative("prefabs");
            prefabs.arraySize = found.Count;
            for (int i = 0; i < found.Count; i++)
                prefabs.GetArrayElementAtIndex(i).objectReferenceValue = found[i];
        }

        _specificIndex = -1;
    }

    static List<GameObject> FindPrefabs(string folder)
    {
        var list = new List<GameObject>();
        if (!AssetDatabase.IsValidFolder(folder))
            return list;

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { folder });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            if (path.Contains("/Models/"))
                continue;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
                list.Add(prefab);
        }

        list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        return list;
    }

    void EnsureDefaultCategories()
    {
        SerializedProperty categories = serializedObject.FindProperty("categories");
        if (HasCategory(categories, "Camp") && categories.arraySize >= 3)
            return;

        string[] names = { "Grass", "Rocks", "Trees", "Camp" };
        Color[] colors =
        {
            new Color(0.45f, 0.78f, 0.32f, 1f),
            new Color(0.62f, 0.58f, 0.52f, 1f),
            new Color(0.22f, 0.55f, 0.28f, 1f),
            new Color(0.72f, 0.58f, 0.42f, 1f)
        };
        float[] radii = { 4f, 5f, 7f, 5f };
        float[] spacings = { 1.2f, 2.8f, 5.5f, 3.2f };
        int[] amounts = { 4, 1, 1, 1 };

        int previous = categories.arraySize;
        categories.arraySize = 4;
        for (int i = previous; i < 4; i++)
        {
            SerializedProperty element = categories.GetArrayElementAtIndex(i);
            element.FindPropertyRelative("displayName").stringValue = names[i];
            element.FindPropertyRelative("previewColor").colorValue = colors[i];
            element.FindPropertyRelative("brushRadius").floatValue = radii[i];
            element.FindPropertyRelative("spacing").floatValue = spacings[i];
            element.FindPropertyRelative("amount").intValue = amounts[i];
            element.FindPropertyRelative("scaleMin").floatValue = 0.85f;
            element.FindPropertyRelative("scaleMax").floatValue = 1.15f;
        }
    }

    static bool HasCategory(SerializedProperty categories, string displayName)
    {
        for (int i = 0; i < categories.arraySize; i++)
        {
            string name = categories.GetArrayElementAtIndex(i).FindPropertyRelative("displayName").stringValue;
            if (string.Equals(name, displayName, System.StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    static void EnterPaintMode()
    {
        _paintMode = true;
        _strokeActive = false;
        _hasSample = false;
        Tools.current = Tool.None;
        Tools.viewTool = ViewTool.None;
        SceneView.RepaintAll();
    }

    static void ExitPaintMode()
    {
        _paintMode = false;
        _strokeActive = false;
        _hasSample = false;
        if (Tools.current == Tool.None)
            Tools.current = Tool.Move;
        SceneView.RepaintAll();
    }

    void OnSceneGUI()
    {
        var brush = target as PlanetEnvironmentBrush;
        if (brush == null)
            return;

        Event e = Event.current;
        if (e == null)
            return;

        DrawOverlay(brush);

        if (!_paintMode || Application.isPlaying)
        {
            DestroyPreview();
            return;
        }

        if (Tools.current != Tool.None)
            Tools.current = Tool.None;

        int controlId = GUIUtility.GetControlID(FocusType.Passive);
        if (e.type == EventType.Layout || e.type == EventType.MouseMove)
            HandleUtility.AddDefaultControl(controlId);

        HandleHotkeys(brush, e);

        bool hit = TryPickSurface(brush, e.mousePosition, out Vector3 worldPoint);
        PlanetEnvironmentBrush.Category category = brush.GetCategory(_category);
        if (hit && category != null && e.type == EventType.Repaint)
            DrawBrushDisc(brush, category, worldPoint, _eraseMode);

        if (hit && !_eraseMode && !_strokeActive)
            UpdatePreview(brush, worldPoint);
        else if (!hit || _eraseMode)
            DestroyPreview();

        switch (e.GetTypeForControl(controlId))
        {
            case EventType.MouseMove:
                SceneView.RepaintAll();
                break;

            case EventType.MouseDown:
                if (e.alt || (e.button != 0 && e.button != 1))
                    break;

                GUIUtility.hotControl = controlId;
                _strokeActive = true;
                _hasSample = false;
                _undoGroup = Undo.GetCurrentGroup();
                bool erase = e.button == 1 || e.shift || _eraseMode;
                Undo.SetCurrentGroupName(erase ? "Erase Environment" : "Paint Environment");
                RebuildOccupied(brush);
                ApplyStroke(brush, worldPoint, hit, erase);
                e.Use();
                break;

            case EventType.MouseDrag:
                if (GUIUtility.hotControl == controlId && _strokeActive && !e.alt && (e.button == 0 || e.button == 1))
                {
                    bool eraseDrag = e.button == 1 || e.shift || _eraseMode;
                    ApplyStroke(brush, worldPoint, hit, eraseDrag);
                    e.Use();
                }

                break;

            case EventType.MouseUp:
                if (GUIUtility.hotControl == controlId)
                {
                    GUIUtility.hotControl = 0;
                    _strokeActive = false;
                    _hasSample = false;
                    Undo.CollapseUndoOperations(_undoGroup);
                    e.Use();
                    DestroyPreview();
                }

                break;
        }
    }

    void HandleHotkeys(PlanetEnvironmentBrush brush, Event e)
    {
        if (e.type != EventType.KeyDown || e.alt)
            return;

        if (e.keyCode == KeyCode.Escape)
        {
            ExitPaintMode();
            DestroyPreview();
            e.Use();
            return;
        }

        PlanetEnvironmentBrush.Category category = brush.GetCategory(_category);
        if (category == null)
            return;

        if (e.keyCode == KeyCode.LeftBracket)
        {
            Undo.RecordObject(brush, "Environment Brush Size");
            category.brushRadius = Mathf.Max(0f, category.brushRadius - 0.5f);
            EditorUtility.SetDirty(brush);
            e.Use();
            return;
        }

        if (e.keyCode == KeyCode.RightBracket)
        {
            Undo.RecordObject(brush, "Environment Brush Size");
            category.brushRadius = Mathf.Min(25f, category.brushRadius + 0.5f);
            EditorUtility.SetDirty(brush);
            e.Use();
            return;
        }

        if (e.keyCode == KeyCode.E)
        {
            _eraseMode = !_eraseMode;
            e.Use();
            return;
        }

        if (e.keyCode == KeyCode.Alpha0 || e.keyCode == KeyCode.R)
        {
            _specificIndex = -1;
            DestroyPreview();
            e.Use();
            return;
        }

        if (e.keyCode >= KeyCode.Alpha1 && e.keyCode <= KeyCode.Alpha9)
        {
            int slot = e.keyCode - KeyCode.Alpha1;
            if (brush.GetPrefab(_category, slot) != null)
            {
                _specificIndex = slot;
                DestroyPreview();
                e.Use();
            }
        }
    }

    void ApplyStroke(PlanetEnvironmentBrush brush, Vector3 worldPoint, bool hit, bool erase)
    {
        if (!hit)
            return;

        PlanetEnvironmentBrush.Category category = brush.GetCategory(_category);
        if (category == null)
            return;

        float spacing = Mathf.Max(0.2f, category.spacing);
        if (_hasSample && Vector3.Distance(_lastSample, worldPoint) < spacing * 0.45f)
            return;

        _lastSample = worldPoint;
        _hasSample = true;

        if (erase)
            Erase(brush, category, worldPoint);
        else
            Scatter(brush, category, worldPoint);

        SceneView.RepaintAll();
    }

    void Scatter(PlanetEnvironmentBrush brush, PlanetEnvironmentBrush.Category category, Vector3 worldPoint)
    {
        SphericalPlanet planet = brush.GetComponentInParent<SphericalPlanet>();
        if (planet == null)
            return;

        int amount = Mathf.Clamp(category.amount, 1, 12);
        float radius = Mathf.Max(0f, category.brushRadius);
        if (radius < 0.05f)
            amount = 1;

        for (int i = 0; i < amount; i++)
        {
            Vector3 sample = i == 0 && radius < 0.05f
                ? worldPoint
                : RandomPointInDisc(planet, worldPoint, radius);
            TryPlace(brush, planet, category, sample);
        }
    }

    void TryPlace(
        PlanetEnvironmentBrush brush,
        SphericalPlanet planet,
        PlanetEnvironmentBrush.Category category,
        Vector3 worldPoint)
    {
        GameObject prefab = _specificIndex < 0
            ? brush.PickRandomPrefab(_category)
            : brush.GetPrefab(_category, _specificIndex);
        if (prefab == null)
            return;

        float spacing = Mathf.Max(0.2f, category.spacing);
        float minSqr = spacing * spacing;
        for (int i = 0; i < _occupied.Count; i++)
        {
            if ((_occupied[i] - worldPoint).sqrMagnitude < minSqr)
                return;
        }

        PlanetTileMap tiles = planet.GetComponent<PlanetTileMap>();
        float yaw = brush.RandomYaw ? Random.Range(0f, 360f) : brush.Yaw;
        if (!PlanetSurfacePose.TryGetPoseFromWorldPoint(
                planet,
                tiles,
                worldPoint,
                yaw,
                brush.Hover,
                out Vector3 position,
                out Quaternion rotation,
                out _))
        {
            return;
        }

        Transform parent = GetOrCreateFolder(brush, category);
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        if (instance == null)
            return;

        Undo.RegisterCreatedObjectUndo(instance, "Paint Environment");
        instance.transform.SetPositionAndRotation(position, rotation);

        float scaleMin = Mathf.Max(0.05f, category.scaleMin);
        float scaleMax = Mathf.Max(scaleMin, category.scaleMax);
        instance.transform.localScale *= Random.Range(scaleMin, scaleMax);

        var align = instance.GetComponent<PlanetSurfaceAlign>();
        if (align == null)
            align = Undo.AddComponent<PlanetSurfaceAlign>(instance);
        align.Configure(planet, yaw, brush.Hover);

        _occupied.Add(instance.transform.position);
    }

    void Erase(PlanetEnvironmentBrush brush, PlanetEnvironmentBrush.Category category, Vector3 worldPoint)
    {
        Transform folder = FindCategoryFolder(brush.transform, FolderName(category));
        if (folder == null)
            return;

        float radius = Mathf.Max(category.brushRadius, Mathf.Max(0.75f, category.spacing * 0.65f));
        float radiusSqr = radius * radius;
        var doomed = new List<GameObject>();
        for (int i = 0; i < folder.childCount; i++)
        {
            Transform child = folder.GetChild(i);
            if (child == null)
                continue;
            if ((child.position - worldPoint).sqrMagnitude <= radiusSqr)
                doomed.Add(child.gameObject);
        }

        for (int i = 0; i < doomed.Count; i++)
        {
            Vector3 position = doomed[i].transform.position;
            Undo.DestroyObjectImmediate(doomed[i]);
            for (int o = _occupied.Count - 1; o >= 0; o--)
            {
                if ((_occupied[o] - position).sqrMagnitude < 0.01f)
                    _occupied.RemoveAt(o);
            }
        }
    }

    void RebuildOccupied(PlanetEnvironmentBrush brush)
    {
        _occupied.Clear();
        PlanetEnvironmentBrush.Category category = brush.GetCategory(_category);
        if (category == null)
            return;

        Transform folder = FindCategoryFolder(brush.transform, FolderName(category));
        if (folder == null)
            return;

        for (int i = 0; i < folder.childCount; i++)
        {
            Transform child = folder.GetChild(i);
            if (child != null)
                _occupied.Add(child.position);
        }
    }

    static Transform GetOrCreateFolder(PlanetEnvironmentBrush brush, PlanetEnvironmentBrush.Category category)
    {
        string folderName = FolderName(category);
        Transform existing = FindCategoryFolder(brush.transform, folderName);
        if (existing != null)
            return existing;

        var go = new GameObject(folderName);
        go.transform.SetParent(brush.transform, false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        Undo.RegisterCreatedObjectUndo(go, "Create " + folderName);
        return go.transform;
    }

    static Transform FindCategoryFolder(Transform root, string folderName)
    {
        if (root == null)
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (!string.Equals(child.name, folderName, System.StringComparison.Ordinal))
                continue;
            // A placed clump can share the category name. Only empty organizers count as the folder.
            if (PrefabUtility.IsAnyPrefabInstanceRoot(child.gameObject))
                continue;
            if (child.GetComponent<Renderer>() != null || child.GetComponent<MeshFilter>() != null)
                continue;
            return child;
        }

        return null;
    }

    static string FolderName(PlanetEnvironmentBrush.Category category)
    {
        if (category == null || string.IsNullOrWhiteSpace(category.displayName))
            return "Props";
        return category.displayName.Trim();
    }

    static Vector3 RandomPointInDisc(SphericalPlanet planet, Vector3 worldPoint, float radius)
    {
        if (radius <= 0.01f)
            return worldPoint;

        Vector3 fromCenter = worldPoint - planet.Center;
        float surfaceRadius = fromCenter.magnitude;
        if (surfaceRadius < 0.01f)
            return worldPoint;

        Vector3 radial = fromCenter / surfaceRadius;
        Vector3 tangent = Vector3.Cross(radial, Mathf.Abs(radial.y) > 0.9f ? Vector3.right : Vector3.up);
        if (tangent.sqrMagnitude < 0.0001f)
            tangent = Vector3.right;
        tangent.Normalize();
        Vector3 bitangent = Vector3.Cross(radial, tangent);

        float ang = Random.Range(0f, Mathf.PI * 2f);
        float dist = radius * Mathf.Sqrt(Random.value);
        float arc = dist / Mathf.Max(0.01f, planet.Radius);
        Vector3 offset = tangent * Mathf.Cos(ang) + bitangent * Mathf.Sin(ang);
        Vector3 dir = (radial * Mathf.Cos(arc) + offset * Mathf.Sin(arc)).normalized;
        return planet.Center + dir * surfaceRadius;
    }

    void UpdatePreview(PlanetEnvironmentBrush brush, Vector3 worldPoint)
    {
        GameObject prefab;
        if (_specificIndex >= 0)
            prefab = brush.GetPrefab(_category, _specificIndex);
        else if (_previewSource != null)
            prefab = _previewSource;
        else
            prefab = brush.PickRandomPrefab(_category);

        if (prefab == null)
        {
            DestroyPreview();
            return;
        }

        SphericalPlanet planet = brush.GetComponentInParent<SphericalPlanet>();
        if (planet == null)
        {
            DestroyPreview();
            return;
        }

        PlanetTileMap tiles = planet.GetComponent<PlanetTileMap>();
        float yaw = brush.RandomYaw ? 0f : brush.Yaw;
        if (!PlanetSurfacePose.TryGetPoseFromWorldPoint(
                planet,
                tiles,
                worldPoint,
                yaw,
                brush.Hover,
                out Vector3 position,
                out Quaternion rotation,
                out _))
        {
            DestroyPreview();
            return;
        }

        if (_preview == null || _previewSource != prefab)
        {
            DestroyPreview();
            _preview = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            _previewSource = prefab;
            if (_preview == null)
                return;
            _preview.name = "__EnvBrushPreview";
            _preview.hideFlags = HideFlags.HideAndDontSave;
            foreach (Transform t in _preview.GetComponentsInChildren<Transform>(true))
                t.gameObject.hideFlags = HideFlags.HideAndDontSave;
            var cols = _preview.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < cols.Length; i++)
                cols[i].enabled = false;
        }

        _preview.transform.SetPositionAndRotation(position, rotation);
        PlanetEnvironmentBrush.Category category = brush.GetCategory(_category);
        if (category != null)
        {
            float scale = (category.scaleMin + category.scaleMax) * 0.5f;
            _preview.transform.localScale = prefab.transform.localScale * scale;
        }
    }

    void DestroyPreview()
    {
        if (_preview != null)
            DestroyImmediate(_preview);
        _preview = null;
        _previewSource = null;
    }

    static void DrawBrushDisc(
        PlanetEnvironmentBrush brush,
        PlanetEnvironmentBrush.Category category,
        Vector3 worldPoint,
        bool erase)
    {
        SphericalPlanet planet = brush.GetComponentInParent<SphericalPlanet>();
        if (planet == null)
            return;

        Vector3 normal = worldPoint - planet.Center;
        if (normal.sqrMagnitude < 0.0001f)
            return;
        normal.Normalize();

        float radius = Mathf.Max(0.15f, category.brushRadius);
        Color color = erase
            ? new Color(0.9f, 0.25f, 0.2f, 0.9f)
            : category.previewColor;
        color.a = 0.9f;
        Handles.color = color;
        Handles.DrawWireDisc(worldPoint, normal, radius);
        Color fill = color;
        fill.a = 0.12f;
        Handles.color = fill;
        Handles.DrawSolidDisc(worldPoint, normal, radius);
    }

    void DrawOverlay(PlanetEnvironmentBrush brush)
    {
        PlanetEnvironmentBrush.Category category = brush.GetCategory(_category);
        string categoryName = category != null ? category.displayName : "—";
        string paintWith = _specificIndex < 0
            ? "Random"
            : (brush.GetPrefab(_category, _specificIndex) != null
                ? brush.GetPrefab(_category, _specificIndex).name
                : "Random");

        Handles.BeginGUI();
        float height = _paintMode ? 78f : 40f;
        var rect = new Rect(12f, 12f, 340f, height);
        GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);
        GUILayout.BeginArea(new Rect(rect.x + 8f, rect.y + 6f, rect.width - 16f, rect.height - 10f));
        if (_paintMode)
        {
            string mode = _eraseMode ? "ERASE" : "PAINT";
            float radius = category != null ? category.brushRadius : 0f;
            GUILayout.Label($"{mode}  {categoryName}  ·  {paintWith}  ·  r {radius:0.0}", EditorStyles.boldLabel);
            GUILayout.Label("LMB paint · Shift/RMB erase · [ ] size · 0 random · 1-9 prefab · Esc");
        }
        else
        {
            GUILayout.Label($"Environment Brush — {categoryName}", EditorStyles.boldLabel);
        }

        GUILayout.EndArea();
        Handles.EndGUI();
    }

    static bool TryPickSurface(PlanetEnvironmentBrush brush, Vector2 guiPoint, out Vector3 worldPoint)
    {
        worldPoint = default;
        SphericalPlanet planet = brush.GetComponentInParent<SphericalPlanet>();
        if (planet == null)
            return false;

        Ray ray = HandleUtility.GUIPointToWorldRay(guiPoint);
        RaycastHit[] hits = Physics.RaycastAll(ray, 10000f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i].collider;
            if (col == null)
                continue;
            if (col.transform != planet.transform && !col.transform.IsChildOf(planet.transform))
                continue;
            if (col.transform == brush.transform || col.transform.IsChildOf(brush.transform))
                continue;

            worldPoint = hits[i].point;
            return true;
        }

        PlanetTileMap map = planet.GetComponent<PlanetTileMap>();
        float radius = Mathf.Max(0.01f, planet.Radius + 2f);
        if (map != null)
            radius = Mathf.Max(radius, map.GetWalkSurfaceRadius(Vector3.up) + 2f);
        if (!RaySphere(ray, planet.Center, radius, out float t))
            return false;

        worldPoint = ray.GetPoint(t);
        Vector3 dir = (worldPoint - planet.Center).normalized;
        if (map != null && map.ProvidesWalkSurface)
            worldPoint = map.GetWalkSurfacePoint(dir, 0f);
        else
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
}
