using UnityEngine;

/// <summary>
/// Keeps a prop's up-axis aligned to spherical planet curvature.
/// Does NOT auto-move on Play — position is only changed when you explicitly
/// snap, drag in the editor, or enable alignEveryFrame.
/// </summary>
[DisallowMultipleComponent]
public class PlanetSurfaceAlign : MonoBehaviour
{
    [SerializeField] SphericalPlanet planet;
    [SerializeField] float hover = PlanetSurfacePose.DefaultHover;
    [SerializeField] float yaw;
    [Tooltip("If enabled, re-snap every LateUpdate (runtime). Off by default for static props.")]
    [SerializeField] bool alignEveryFrame;

    PlanetTileMap _tiles;
    bool _snapping;

    public void Configure(SphericalPlanet targetPlanet, float yawDegrees, float hoverOffset)
    {
        planet = targetPlanet;
        yaw = yawDegrees;
        hover = Mathf.Max(0f, hoverOffset);
        CacheTiles();
        SnapToSurface(recordUndo: false);
    }

    void OnValidate()
    {
        hover = Mathf.Max(0f, hover);
    }

    void LateUpdate()
    {
        if (!alignEveryFrame || !Application.isPlaying)
            return;
        SnapToSurface(recordUndo: false);
    }

    void CacheTiles()
    {
        if (planet == null)
            PlanetSurfacePose.TryResolvePlanet(transform, out planet, out _tiles);
        else
            _tiles = planet.GetComponent<PlanetTileMap>();
    }

    /// <summary>
    /// Snap using current world position as a direction hint (keeps yaw + hover).
    /// </summary>
    public void SnapToSurface(bool recordUndo = true)
    {
        if (_snapping)
            return;

        CacheTiles();
        if (planet == null)
            return;

        _snapping = true;
        try
        {
            if (!PlanetSurfacePose.TryGetPoseFromWorldPoint(
                    planet,
                    _tiles,
                    transform.position,
                    yaw,
                    hover,
                    out Vector3 position,
                    out Quaternion rotation,
                    out _))
            {
                return;
            }

#if UNITY_EDITOR
            if (recordUndo && !Application.isPlaying)
                UnityEditor.Undo.RecordObject(transform, "Snap Prop To Planet Surface");
#endif
            transform.SetPositionAndRotation(position, rotation);
        }
        finally
        {
            _snapping = false;
        }
    }

    /// <summary>
    /// Align rotation only — keep the current distance from planet center (height).
    /// </summary>
    public void AlignRotationOnly(bool recordUndo = true)
    {
        if (_snapping)
            return;

        CacheTiles();
        if (planet == null)
            return;

        Vector3 radial = transform.position - planet.Center;
        if (radial.sqrMagnitude < 0.0001f)
            return;

        Vector3 up = radial.normalized;
        // Prefer terrain/walk normal when available, but keep radius unchanged.
        if (_tiles != null && _tiles.ProvidesWalkSurface)
            up = _tiles.GetWalkSurfaceNormal(radial);
        else
            up = planet.GetTerrainNormal(radial);

        if (Vector3.Dot(up, radial) < 0f)
            up = -up;

        Quaternion rotation = PlanetSurfacePose.RotationFromUp(up, yaw);

        _snapping = true;
        try
        {
#if UNITY_EDITOR
            if (recordUndo && !Application.isPlaying)
                UnityEditor.Undo.RecordObject(transform, "Align Prop Rotation");
#endif
            transform.rotation = rotation;
        }
        finally
        {
            _snapping = false;
        }
    }

    /// <summary>
    /// Editor drag: update yaw from rotation if needed, capture height as hover, then snap laterally.
    /// </summary>
    public void EditorHandleMoved()
    {
        if (_snapping)
            return;

        CacheTiles();
        if (planet == null)
            return;

        Vector3 radial = transform.position - planet.Center;
        if (radial.sqrMagnitude > 0.0001f)
        {
            Vector3 upHint = radial.normalized;
            float extracted = PlanetSurfacePose.ExtractYaw(transform.rotation, upHint);
            if (!Mathf.Approximately(extracted, yaw))
                yaw = extracted;

            // Remember how high the user placed the prop above the analytic surface.
            float surfaceRadius = _tiles != null && _tiles.ProvidesWalkSurface
                ? _tiles.GetWalkSurfaceRadius(upHint)
                : planet.GetTerrainRadius(upHint);
            float currentRadius = radial.magnitude;
            hover = Mathf.Max(0f, currentRadius - surfaceRadius);
        }

        SnapToSurface(recordUndo: true);
    }

    void OnDrawGizmosSelected()
    {
        if (planet == null)
            CacheTiles();
        if (planet == null)
            return;

        Vector3 up = planet.GetUpAt(transform.position);
        Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.85f);
        Gizmos.DrawRay(transform.position, up * 1.5f);
    }
}
