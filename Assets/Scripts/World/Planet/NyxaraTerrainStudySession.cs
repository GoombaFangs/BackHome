using UnityEngine;

/// <summary>
/// Scene-only A2 terrain study root. Lives as a child of PlanetNyxara with identity TRS.
/// Holds the editable work plan, applies it onto <see cref="PlanetTileMap"/> (the existing
/// terrain data owner), and is the object to delete to undo the study extras.
/// </summary>
[ExecuteAlways]
[DefaultExecutionOrder(-40)]
public class NyxaraTerrainStudySession : MonoBehaviour
{
    public const string RootName = "NyxaraA2TerrainStudy";
    public const string SnapshotAssetPath =
        "Assets/Resources/Galaxy/Nyxara/Test/Nyxara-A2-LayoutSnapshot.json";
    public const string SourceScenePath = "Assets/Scenes/PlanetNyxara.unity";
    public const string StudyScenePath = "Assets/Scenes/PlanetNyxaraTerrainStudy.unity";

    [SerializeField] SphericalPlanet planet;
    [SerializeField] PlanetTileMap tileMap;
    [SerializeField] PlanetTileMap.TerrainWorkPlan plan = new PlanetTileMap.TerrainWorkPlan
    {
        enabled = true,
        sectorId = "A2",
        longitudeFrame = PlanetTileMap.LongitudeFrame.PlanetTileMap,
        sectorLongitudeMin = 40f,
        sectorLongitudeMax = 70f,
        sectorLatitudeMin = -28f,
        sectorLatitudeMax = 36f,
        studyLongitudeMin = 20f,
        studyLongitudeMax = 50f,
        ridgeHeight = 18f,
        cliffDepth = 13f,
        cliffDepthMin = 12f,
        cliffDepthMax = 14f,
        playableMargin = 1f,
        detailLevel = 2,
        cliffSpanDegrees = 18f,
        useOptionalSouthCliffShift = false
    };

    [SerializeField] string snapshotPath = SnapshotAssetPath;
    [SerializeField] bool applyPlanToTileMap = true;
    [SerializeField] NyxaraA2BoundaryOverlay boundaryOverlay = new NyxaraA2BoundaryOverlay();
    [SerializeField] bool autoRebuildBoundaryOverlay = true;
    [SerializeField]
    [Tooltip("Hide MeshRenderer on covered Border cubes after the mountain and cliff cover them. GameObjects stay for layout compare. In Play, solid BoxColliders are off (triggers stay). Full ring hides every Borders cube; A2-only hides Cube (2)/(3)/(4)/(7) and leaves Cube (5) for R1.")]
    bool hideCoveredPlaceholderWallRenderers = true;

    public const string BoundaryOverlayAssetPath =
        "Assets/Resources/Galaxy/Nyxara/Test/Nyxara-A2-BoundaryOverlay.json";

    public static readonly string[] CoveredA2PlaceholderWallNames =
    {
        "Cube (2)", "Cube (3)", "Cube (4)", "Cube (7)"
    };

    public PlanetTileMap.TerrainWorkPlan Plan => plan;
    public SphericalPlanet Planet => planet;
    public PlanetTileMap TileMap => tileMap;
    public NyxaraA2BoundaryOverlay BoundaryOverlay => boundaryOverlay;
    public bool HideCoveredPlaceholderWallRenderers => hideCoveredPlaceholderWallRenderers;

    void OnEnable()
    {
        if (IsOriginalPlayScene())
            return;

        BindPlanet();
        SnapToPlanetLocalIdentity();
        if (autoRebuildBoundaryOverlay)
            RebuildBoundaryOverlay();
        else if (applyPlanToTileMap)
            ApplyPlanToTileMap();
        ApplyPlaceholderWallRenderers();
    }

    void OnDisable()
    {
        RestorePlaceholderWallRenderers();
    }

    void OnValidate()
    {
        if (plan == null)
            plan = new PlanetTileMap.TerrainWorkPlan();
        plan.enabled = true;
        SyncPlanScope();
        plan.ridgeHeight = Mathf.Max(0f, plan.ridgeHeight);
        plan.cliffDepthMin = Mathf.Max(0f, plan.cliffDepthMin);
        plan.cliffDepthMax = Mathf.Max(plan.cliffDepthMin, plan.cliffDepthMax);
        plan.cliffDepth = Mathf.Clamp(plan.cliffDepth, plan.cliffDepthMin, plan.cliffDepthMax);
        plan.playableMargin = Mathf.Max(0f, plan.playableMargin);
        plan.detailLevel = Mathf.Clamp(plan.detailLevel, 1, 4);
        plan.cliffSpanDegrees = Mathf.Max(8f, plan.cliffSpanDegrees);
        SyncSouthBoundaryChoiceToPlan();
        if (!isActiveAndEnabled || IsOriginalPlayScene())
            return;

        // Rebuild is deferred: DestroyImmediate on the tile mesh is illegal inside OnValidate.
#if UNITY_EDITOR
        if (!_applyQueued)
        {
            _applyQueued = true;
            UnityEditor.EditorApplication.delayCall += ApplyPlanDeferred;
        }
#else
        ApplyPlanToTileMap();
        ApplyPlaceholderWallRenderers();
#endif
    }

#if UNITY_EDITOR
    bool _applyQueued;

    void ApplyPlanDeferred()
    {
        _applyQueued = false;
        if (this == null || !isActiveAndEnabled || IsOriginalPlayScene())
            return;
        if (plan != null && plan.coverFullRing &&
            (boundaryOverlay == null || !boundaryOverlay.wrapLongitude || !boundaryOverlay.HasSamples))
            RebuildBoundaryOverlay();
        else
            ApplyPlanToTileMap();
        ApplyPlaceholderWallRenderers();
    }
#endif

    bool IsOriginalPlayScene()
    {
        if (gameObject.scene.IsValid() && gameObject.scene.path == SourceScenePath)
            return true;
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        return scene.path == SourceScenePath;
    }

    public void BindPlanet()
    {
        if (planet == null)
            planet = GetComponentInParent<SphericalPlanet>();
        if (planet == null)
            planet = SphericalPlanet.Instance != null
                ? SphericalPlanet.Instance
                : FindAnyObjectByType<SphericalPlanet>();
        if (planet != null)
            tileMap = planet.GetComponent<PlanetTileMap>();
    }

    public void SnapToPlanetLocalIdentity()
    {
        if (planet == null)
            return;
        if (transform.parent != planet.transform)
            transform.SetParent(planet.transform, false);
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        transform.localScale = Vector3.one;
    }

    public void ApplyPlanToTileMap()
    {
        BindPlanet();
        if (tileMap == null || plan == null)
            return;

        plan.enabled = true;
        SyncPlanScope();
        NyxaraA2CliffProfile.CaptureLip(plan, boundaryOverlay);
        if (plan.cliffSpanDegrees < 8f)
            plan.cliffSpanDegrees = NyxaraA2CliffProfile.DefaultSpanDegrees;
        tileMap.SetWorkPlan(ClonePlan(plan));
        RefreshSouthCliffMesh();
        DisableRoutePhysicsColliders();

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(tileMap);
        if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(tileMap))
            UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(tileMap);
#endif
    }

    public void DisableRoutePhysicsColliders()
    {
        if (IsOriginalPlayScene())
            return;

        BindPlanet();
        NyxaraA2NorthRidge ridge = GetComponentInChildren<NyxaraA2NorthRidge>(true);
        if (ridge != null)
            NyxaraTerrainCollision.ClearBlockingMesh(ridge.gameObject);

        NyxaraA2SouthCliff cliff = GetComponentInChildren<NyxaraA2SouthCliff>(true);
        if (cliff != null)
            NyxaraTerrainCollision.ClearBlockingMesh(cliff.gameObject);

        // Play only — do not persist disabled boxes into the study scene asset.
        if (Application.isPlaying)
            SetBorderSolidCollidersEnabled(false);
    }

    void RefreshSouthCliffMesh()
    {
        if (IsOriginalPlayScene())
            return;

        BindPlanet();
        NyxaraA2SouthCliff cliff = GetComponentInChildren<NyxaraA2SouthCliff>(true);
        if (cliff == null || plan == null)
            return;

        float walk = tileMap != null
            ? tileMap.GetWalkSurfaceRadius(PlanetTileMap.StudyLonLatToDirection(35f, -12f))
            : (planet != null ? planet.Radius : 75f);
        cliff.RebuildFromPlan(plan, walk);
    }

    public void ClearPlanFromTileMap()
    {
        BindPlanet();
        if (tileMap == null || tileMap.WorkPlan == null)
            return;

        var cleared = ClonePlan(tileMap.WorkPlan);
        cleared.enabled = false;
        tileMap.SetWorkPlan(cleared);

#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(tileMap);
        if (UnityEditor.PrefabUtility.IsPartOfPrefabInstance(tileMap))
            UnityEditor.PrefabUtility.RecordPrefabInstancePropertyModifications(tileMap);
#endif
    }

    public void RebuildBoundaryOverlay()
    {
        BindPlanet();
        if (boundaryOverlay == null)
            boundaryOverlay = new NyxaraA2BoundaryOverlay();
        SyncSouthBoundaryChoiceToPlan();
        boundaryOverlay.Rebuild(planet, tileMap, plan);
        if (applyPlanToTileMap)
            ApplyPlanToTileMap();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    public void EnableFullRing()
    {
        if (plan == null)
            plan = new PlanetTileMap.TerrainWorkPlan();
        plan.coverFullRing = true;
        SyncPlanScope();
        hideCoveredPlaceholderWallRenderers = true;
        if (boundaryOverlay == null)
            boundaryOverlay = new NyxaraA2BoundaryOverlay();
        boundaryOverlay.longitudeSteps = Mathf.Max(boundaryOverlay.longitudeSteps, 181);
        boundaryOverlay.latitudeScanMin = Mathf.Min(boundaryOverlay.latitudeScanMin, -55f);
        boundaryOverlay.latitudeScanMax = Mathf.Max(boundaryOverlay.latitudeScanMax, 55f);
        RebuildBoundaryOverlay();
        ApplyPlaceholderWallRenderers();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    public void SyncPlanScope()
    {
        if (plan == null)
            return;
        if (plan.coverFullRing)
        {
            plan.sectorId = "Ring";
            plan.studyLongitudeMin = -180f;
            plan.studyLongitudeMax = 180f;
            plan.sectorLongitudeMin = 0f;
            plan.sectorLongitudeMax = 360f;
            plan.sectorLatitudeMin = -80f;
            plan.sectorLatitudeMax = 80f;
            if (boundaryOverlay != null && boundaryOverlay.longitudeSteps < 120)
                boundaryOverlay.longitudeSteps = 181;
            return;
        }

        plan.sectorId = "A2";
        bool wasRing =
            plan.studyLongitudeMin <= -179f && plan.studyLongitudeMax >= 179f;
        if (wasRing)
        {
            plan.studyLongitudeMin = 20f;
            plan.studyLongitudeMax = 50f;
            plan.sectorLongitudeMin = 40f;
            plan.sectorLongitudeMax = 70f;
            plan.sectorLatitudeMin = -28f;
            plan.sectorLatitudeMax = 36f;
        }
    }

    public void SyncSouthBoundaryChoiceToPlan()
    {
        if (plan == null || boundaryOverlay == null)
            return;
        plan.useOptionalSouthCliffShift =
            boundaryOverlay.activeSouthBoundary == NyxaraA2BoundaryOverlay.SouthBoundaryChoice.OptionalTriggerClearance;
    }

    public void SetHideCoveredPlaceholderWallRenderers(bool hide)
    {
        hideCoveredPlaceholderWallRenderers = hide;
        ApplyPlaceholderWallRenderers();
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
    }

    public void ApplyPlaceholderWallRenderers()
    {
        if (IsOriginalPlayScene())
            return;

        BindPlanet();
        SetCoveredA2WallRenderersEnabled(!hideCoveredPlaceholderWallRenderers);
    }

    void RestorePlaceholderWallRenderers()
    {
        if (IsOriginalPlayScene())
            return;

        BindPlanet();
        SetCoveredA2WallRenderersEnabled(true);
        if (Application.isPlaying)
            SetBorderSolidCollidersEnabled(true);
    }

    void SetBorderSolidCollidersEnabled(bool enabled)
    {
        Transform borders = planet != null ? planet.transform.Find("Borders") : null;
        if (borders == null)
            return;
        for (int i = 0; i < borders.childCount; i++)
        {
            Transform child = borders.GetChild(i);
            if (child == null)
                continue;
            var box = child.GetComponent<BoxCollider>();
            if (box == null || box.isTrigger)
                continue;
            box.enabled = enabled;
        }
    }

    void SetCoveredA2WallRenderersEnabled(bool enabled)
    {
        if (planet == null)
            return;

        SetAllBorderWallRenderersEnabled(true);
        if (enabled)
            return;

        if (plan != null && plan.coverFullRing)
        {
            SetAllBorderWallRenderersEnabled(false);
            return;
        }

        for (int i = 0; i < CoveredA2PlaceholderWallNames.Length; i++)
        {
            Transform wall = FindA2PlaceholderWall(CoveredA2PlaceholderWallNames[i]);
            if (wall == null)
                continue;
            var renderer = wall.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.enabled = false;
        }
    }

    void SetAllBorderWallRenderersEnabled(bool enabled)
    {
        Transform borders = planet != null ? planet.transform.Find("Borders") : null;
        if (borders == null)
            return;
        for (int i = 0; i < borders.childCount; i++)
        {
            Transform child = borders.GetChild(i);
            if (child == null)
                continue;
            var box = child.GetComponent<BoxCollider>();
            if (box == null || box.isTrigger)
                continue;
            var renderer = child.GetComponent<MeshRenderer>();
            if (renderer != null)
                renderer.enabled = enabled;
        }
    }

    Transform FindA2PlaceholderWall(string wallName)
    {
        Transform borders = planet.transform.Find("Borders");
        if (borders != null)
        {
            Transform child = borders.Find(wallName);
            if (child != null)
                return child;
        }

        Transform[] all = planet.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < all.Length; i++)
        {
            Transform t = all[i];
            if (t != null && t.name == wallName && t.parent != null && t.parent.name == "Borders")
                return t;
        }

        return null;
    }

    public Matrix4x4 PlanetLocalToWorld =>
        planet != null ? planet.PlanetLocalToWorld : transform.localToWorldMatrix;

    public Matrix4x4 WorldToPlanetLocal =>
        planet != null ? planet.WorldToPlanetLocal : transform.worldToLocalMatrix;

    static PlanetTileMap.TerrainWorkPlan ClonePlan(PlanetTileMap.TerrainWorkPlan source)
    {
        if (source == null)
            return new PlanetTileMap.TerrainWorkPlan();
        return JsonUtility.FromJson<PlanetTileMap.TerrainWorkPlan>(JsonUtility.ToJson(source));
    }

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        BindPlanet();
        if (planet == null || plan == null)
            return;
        if (boundaryOverlay == null)
            boundaryOverlay = new NyxaraA2BoundaryOverlay();
        if (autoRebuildBoundaryOverlay && !boundaryOverlay.HasSamples)
            RebuildBoundaryOverlay();
        boundaryOverlay.Draw(planet, plan);
    }
#endif
}
