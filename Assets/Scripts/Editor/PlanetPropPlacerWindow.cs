using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Scene tool: click a spherical planet to place decoration prefabs aligned to curvature.
/// Menu: BackHome → Planet Prop Placer
/// </summary>
public class PlanetPropPlacerWindow : EditorWindow
{
    const string PrefabsFolder = "Assets/Resources/Galaxy/Planets/Nyxara/Environment";
    const string PrefKeyPlaceMode = "BackHome.PlanetPropPlacer.PlaceMode";
    const string PrefKeyRandomYaw = "BackHome.PlanetPropPlacer.RandomYaw";
    const string PrefKeyScale = "BackHome.PlanetPropPlacer.Scale";
    const string PrefKeyHover = "BackHome.PlanetPropPlacer.Hover";
    const string PrefKeyYaw = "BackHome.PlanetPropPlacer.Yaw";

    SphericalPlanet _planet;
    GameObject _prefab;
    Vector2 _prefabScroll;
    readonly List<GameObject> _prefabCache = new();
    double _nextPrefabRefresh;

    bool _placeMode;
    bool _randomYaw = true;
    float _scale = 1f;
    float _hover = PlanetSurfacePose.DefaultHover;
    float _yaw;

    GameObject _preview;
    Material _previewMat;
    Vector3 _hoverPoint;
    bool _hasHover;
    float _previewYaw;

    [MenuItem("BackHome/Planet Prop Placer")]
    public static void Open()
    {
        var window = GetWindow<PlanetPropPlacerWindow>("Planet Props");
        window.minSize = new Vector2(280, 360);
        window.Show();
    }

    void OnEnable()
    {
        _placeMode = EditorPrefs.GetBool(PrefKeyPlaceMode, false);
        _randomYaw = EditorPrefs.GetBool(PrefKeyRandomYaw, true);
        _scale = EditorPrefs.GetFloat(PrefKeyScale, 1f);
        _hover = EditorPrefs.GetFloat(PrefKeyHover, PlanetSurfacePose.DefaultHover);
        _yaw = EditorPrefs.GetFloat(PrefKeyYaw, 0f);

        SceneView.duringSceneGui += OnSceneGUI;
        RefreshPrefabList(force: true);
        AutoPickPlanet();
        wantsMouseMove = true;
    }

    void OnDisable()
    {
        SceneView.duringSceneGui -= OnSceneGUI;
        DestroyPreview();
        if (_previewMat != null)
        {
            DestroyImmediate(_previewMat);
            _previewMat = null;
        }

        EditorPrefs.SetBool(PrefKeyPlaceMode, _placeMode);
        EditorPrefs.SetBool(PrefKeyRandomYaw, _randomYaw);
        EditorPrefs.SetFloat(PrefKeyScale, _scale);
        EditorPrefs.SetFloat(PrefKeyHover, _hover);
        EditorPrefs.SetFloat(PrefKeyYaw, _yaw);

        if (Tools.current == Tool.None)
            Tools.current = Tool.Move;
    }

    void OnGUI()
    {
        EditorGUILayout.LabelField("Planet Prop Placer", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Enable Place Mode, pick a prefab, then left-click the planet in the Scene view.\n" +
            "Shift+Click an existing prop under Objects to delete it.\n" +
            "Props get PlanetSurfaceAlign so Move keeps them on the curve.",
            MessageType.Info);

        EditorGUI.BeginChangeCheck();
        _planet = (SphericalPlanet)EditorGUILayout.ObjectField("Planet", _planet, typeof(SphericalPlanet), true);
        if (EditorGUI.EndChangeCheck() || _planet == null)
            AutoPickPlanet();

        EditorGUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUI.BeginChangeCheck();
            bool place = GUILayout.Toggle(_placeMode, "Place Mode", "Button", GUILayout.Height(32));
            if (EditorGUI.EndChangeCheck())
            {
                _placeMode = place;
                if (_placeMode)
                {
                    Tools.current = Tool.None;
                    _previewYaw = _randomYaw ? Random.Range(0f, 360f) : _yaw;
                }
                else
                {
                    DestroyPreview();
                    if (Tools.current == Tool.None)
                        Tools.current = Tool.Move;
                }

                SceneView.RepaintAll();
            }

            if (GUILayout.Button("Refresh Prefabs", GUILayout.Height(32), GUILayout.Width(110)))
                RefreshPrefabList(force: true);
        }

        EditorGUILayout.Space(6);
        _scale = EditorGUILayout.Slider("Scale", _scale, 0.1f, 5f);
        _hover = EditorGUILayout.Slider("Hover", _hover, 0f, 2f);
        _randomYaw = EditorGUILayout.Toggle("Random Yaw", _randomYaw);
        using (new EditorGUI.DisabledScope(_randomYaw))
            _yaw = EditorGUILayout.Slider("Yaw", _yaw, 0f, 360f);

        EditorGUILayout.Space(8);
        EditorGUILayout.LabelField("Prefab", EditorStyles.boldLabel);
        _prefab = (GameObject)EditorGUILayout.ObjectField("Selected", _prefab, typeof(GameObject), false);

        RefreshPrefabList(force: false);
        _prefabScroll = EditorGUILayout.BeginScrollView(_prefabScroll, GUILayout.MinHeight(160));
        for (int i = 0; i < _prefabCache.Count; i++)
        {
            GameObject p = _prefabCache[i];
            if (p == null)
                continue;

            bool selected = _prefab == p;
            Color prev = GUI.backgroundColor;
            if (selected)
                GUI.backgroundColor = new Color(0.45f, 0.75f, 1f);
            if (GUILayout.Button(p.name, GUILayout.Height(22)))
            {
                _prefab = p;
                Selection.activeObject = p;
            }

            GUI.backgroundColor = prev;
        }

        EditorGUILayout.EndScrollView();

        if (_placeMode && _prefab == null)
            EditorGUILayout.HelpBox("Select a prefab to place.", MessageType.Warning);
        if (_placeMode && _planet == null)
            EditorGUILayout.HelpBox("No SphericalPlanet in the scene.", MessageType.Error);
    }

    void AutoPickPlanet()
    {
        if (_planet != null)
            return;

        if (Selection.activeGameObject != null)
        {
            var fromSel = Selection.activeGameObject.GetComponentInParent<SphericalPlanet>();
            if (fromSel != null)
            {
                _planet = fromSel;
                return;
            }
        }

        _planet = Object.FindAnyObjectByType<SphericalPlanet>();
    }

    void RefreshPrefabList(bool force)
    {
        if (!force && EditorApplication.timeSinceStartup < _nextPrefabRefresh)
            return;

        _nextPrefabRefresh = EditorApplication.timeSinceStartup + 2.0;
        _prefabCache.Clear();

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { PrefabsFolder });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (go != null)
                _prefabCache.Add(go);
        }

        _prefabCache.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
    }

    void OnSceneGUI(SceneView sceneView)
    {
        if (!_placeMode || _planet == null)
        {
            DestroyPreview();
            return;
        }

        Event e = Event.current;
        if (e == null)
            return;

        if (Tools.current != Tool.None)
            Tools.current = Tool.None;

        int controlId = GUIUtility.GetControlID(FocusType.Passive);
        if (e.type == EventType.Layout || e.type == EventType.MouseMove)
            HandleUtility.AddDefaultControl(controlId);

        bool hit = TryPickPlanetPoint(_planet, e.mousePosition, out Vector3 worldPoint);
        _hasHover = hit;
        if (hit)
            _hoverPoint = worldPoint;

        float yawNow = _randomYaw ? _previewYaw : _yaw;
        if (hit && _prefab != null)
            UpdatePreview(worldPoint, yawNow);
        else
            DestroyPreview();

        switch (e.GetTypeForControl(controlId))
        {
            case EventType.MouseMove:
                sceneView.Repaint();
                break;

            case EventType.MouseDown:
                if (e.alt || e.button != 0)
                    break;

                GUIUtility.hotControl = controlId;
                if (e.shift)
                {
                    if (TryDeletePropUnderMouse(e.mousePosition))
                        e.Use();
                }
                else if (_prefab != null && hit)
                {
                    float yaw = _randomYaw ? Random.Range(0f, 360f) : _yaw;
                    PlacePrefab(worldPoint, yaw);
                    if (_randomYaw)
                        _previewYaw = Random.Range(0f, 360f);
                    e.Use();
                }

                break;

            case EventType.MouseUp:
                if (GUIUtility.hotControl == controlId)
                {
                    GUIUtility.hotControl = 0;
                    e.Use();
                }

                break;
        }

        Handles.BeginGUI();
        GUILayout.BeginArea(new Rect(12, 12, 260, 70));
        GUILayout.Label("Planet Prop Placer", EditorStyles.boldLabel);
        GUILayout.Label(_prefab != null ? $"Place: {_prefab.name}" : "No prefab selected");
        GUILayout.Label("LMB place · Shift+LMB delete · Alt orbit");
        GUILayout.EndArea();
        Handles.EndGUI();
    }

    void PlacePrefab(Vector3 worldPoint, float yawDegrees)
    {
        PlanetTileMap tiles = _planet.GetComponent<PlanetTileMap>();
        if (!PlanetSurfacePose.TryGetPoseFromWorldPoint(
                _planet,
                tiles,
                worldPoint,
                yawDegrees,
                _hover,
                out Vector3 position,
                out Quaternion rotation,
                out _))
        {
            return;
        }

        Transform parent = PlanetSurfacePose.GetOrCreateObjectsRoot(_planet);
        if (parent == null)
            return;

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(_prefab, parent);
        if (instance == null)
            return;

        Undo.RegisterCreatedObjectUndo(instance, $"Place {_prefab.name}");
        instance.transform.SetPositionAndRotation(position, rotation);
        instance.transform.localScale = Vector3.one * _scale;

        var align = instance.GetComponent<PlanetSurfaceAlign>();
        if (align == null)
            align = Undo.AddComponent<PlanetSurfaceAlign>(instance);
        align.Configure(_planet, yawDegrees, _hover);
        EditorUtility.SetDirty(align);
        EditorUtility.SetDirty(instance);

        Selection.activeGameObject = instance;
    }

    bool TryDeletePropUnderMouse(Vector2 guiPoint)
    {
        Ray ray = HandleUtility.GUIPointToWorldRay(guiPoint);
        if (!Physics.Raycast(ray, out RaycastHit hit, 10000f, ~0, QueryTriggerInteraction.Ignore))
            return false;

        Transform objects = _planet.transform.Find("Objects");
        if (objects == null)
            return false;

        Transform t = hit.collider != null ? hit.collider.transform : null;
        while (t != null && t != objects && t.parent != objects)
            t = t.parent;

        if (t == null || t.parent != objects)
            return false;

        Undo.DestroyObjectImmediate(t.gameObject);
        return true;
    }

    void UpdatePreview(Vector3 worldPoint, float yawDegrees)
    {
        if (_prefab == null)
        {
            DestroyPreview();
            return;
        }

        PlanetTileMap tiles = _planet.GetComponent<PlanetTileMap>();
        if (!PlanetSurfacePose.TryGetPoseFromWorldPoint(
                _planet,
                tiles,
                worldPoint,
                yawDegrees,
                _hover,
                out Vector3 position,
                out Quaternion rotation,
                out _))
        {
            DestroyPreview();
            return;
        }

        if (_preview == null || _preview.name != $"__Preview_{_prefab.name}")
        {
            DestroyPreview();
            _preview = (GameObject)PrefabUtility.InstantiatePrefab(_prefab);
            _preview.name = $"__Preview_{_prefab.name}";
            _preview.hideFlags = HideFlags.HideAndDontSave;
            SetPreviewMaterials(_preview);
        }

        _preview.transform.SetPositionAndRotation(position, rotation);
        _preview.transform.localScale = Vector3.one * _scale;
    }

    void SetPreviewMaterials(GameObject root)
    {
        if (_previewMat == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Standard");
            _previewMat = new Material(shader)
            {
                color = new Color(0.4f, 0.85f, 1f, 0.35f),
                hideFlags = HideFlags.HideAndDontSave
            };
            if (_previewMat.HasProperty("_BaseColor"))
                _previewMat.SetColor("_BaseColor", new Color(0.4f, 0.85f, 1f, 0.35f));
        }

        var renderers = root.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var mats = new Material[renderers[i].sharedMaterials.Length];
            for (int m = 0; m < mats.Length; m++)
                mats[m] = _previewMat;
            renderers[i].sharedMaterials = mats;
            renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderers[i].receiveShadows = false;
        }

        var cols = root.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
            cols[i].enabled = false;
    }

    void DestroyPreview()
    {
        if (_preview != null)
        {
            DestroyImmediate(_preview);
            _preview = null;
        }
    }

    static bool TryPickPlanetPoint(SphericalPlanet planet, Vector2 guiPoint, out Vector3 worldPoint)
    {
        worldPoint = default;
        if (planet == null)
            return false;

        Ray ray = HandleUtility.GUIPointToWorldRay(guiPoint);
        PlanetTileMap map = planet.GetComponent<PlanetTileMap>();

        RaycastHit[] hits = Physics.RaycastAll(ray, 10000f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i].collider;
            if (col == null)
                continue;

            if (col.transform == planet.transform || col.transform.IsChildOf(planet.transform))
            {
                // Ignore props under Objects for placement hit (place on terrain, not on other props)
                Transform objects = planet.transform.Find("Objects");
                if (objects != null && (col.transform == objects || col.transform.IsChildOf(objects)))
                    continue;

                worldPoint = hits[i].point;
                return true;
            }
        }

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
