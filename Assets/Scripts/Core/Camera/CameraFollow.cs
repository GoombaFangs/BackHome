using UnityEngine;

/// <summary>
/// Casual follow camera: fixed viewing angle, optional soft tilt to surface up.
/// Does not orbit with the player's facing (avoids dizzy spinning on planets).
/// On curved surfaces, the "behind" axis is parallel-transported so it never flips mid-orbit.
/// Terrain bumps are filtered via radial up + separately damped focus height.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [SerializeField] Transform target;

    [Header("Offset")]
    // XP Hero-style high angle: steep look-down (~70°), not over-the-shoulder.
    [SerializeField] float offsetHeight = 24f;
    [SerializeField] float offsetBack = 8f;
    [SerializeField] float offsetSide = 0f;

    [Header("Follow")]
    [SerializeField] float smoothSpeed = 10f;
    [SerializeField] bool lookAtTarget = true;
    [Tooltip("Tilt camera using surface up. Softly smoothed.")]
    [SerializeField] bool alignToTargetUp = true;
    [Tooltip("On planets, use radial up (center→player) instead of terrain slope normals.")]
    [SerializeField] bool preferPlanetRadialUp = true;
    [Tooltip("If off (casual), camera keeps a stable viewing angle and ignores player facing.")]
    [SerializeField] bool followTargetFacing = false;
    [Tooltip("Initial world direction used as \"behind the player\" when not following facing.")]
    [SerializeField] Vector3 fixedBackHint = new Vector3(0.25f, 0f, 1f);
    [SerializeField] float upSmoothSpeed = 5f;
    [Tooltip("How quickly the camera pivot tracks the player along the ground plane.")]
    [SerializeField] float focusSmoothSpeed = 8f;
    [Tooltip("How quickly the camera pivot tracks height/radial bob. Lower = less shake on uneven terrain.")]
    [SerializeField] float heightSmoothSpeed = 2.5f;

    Vector3 _smoothedUp = Vector3.up;
    Vector3 _smoothedBack = Vector3.forward;
    Vector3 _smoothedFocus;
    bool _hasBasis;
    bool _hasFocus;

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        _hasBasis = false;
        _hasFocus = false;
        if (target != null && alignToTargetUp)
            _smoothedUp = ResolveDesiredUp(target.position, target.up);
    }

    void LateUpdate()
    {
        if (target == null)
            return;

        Vector3 desiredUp = ResolveDesiredUp(target.position, target.up);
        if (desiredUp.sqrMagnitude < 0.0001f)
            desiredUp = Vector3.up;
        desiredUp.Normalize();

        Vector3 previousUp = _smoothedUp;
        if (upSmoothSpeed <= 0f)
            _smoothedUp = desiredUp;
        else
            _smoothedUp = Vector3.Slerp(_smoothedUp, desiredUp, 1f - Mathf.Exp(-upSmoothSpeed * Time.deltaTime)).normalized;

        UpdateSmoothedFocus(_smoothedUp);

        Vector3 back = ResolveBackDirection(_smoothedUp, previousUp);
        Vector3 right = Vector3.Cross(_smoothedUp, back).normalized;

        Vector3 desired = _smoothedFocus
                          + _smoothedUp * offsetHeight
                          - back * offsetBack
                          + right * offsetSide;

        if (smoothSpeed <= 0f)
            transform.position = desired;
        else
            transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-smoothSpeed * Time.deltaTime));

        if (lookAtTarget)
        {
            Vector3 toTarget = _smoothedFocus - transform.position;
            if (toTarget.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(toTarget.normalized, _smoothedUp);
        }
    }

    Vector3 ResolveDesiredUp(Vector3 worldPosition, Vector3 fallbackUp)
    {
        if (!alignToTargetUp)
            return Vector3.up;

        if (preferPlanetRadialUp && SphericalPlanet.Instance != null)
            return SphericalPlanet.Instance.GetUpAt(worldPosition);

        return fallbackUp.sqrMagnitude > 0.0001f ? fallbackUp.normalized : Vector3.up;
    }

    void UpdateSmoothedFocus(Vector3 up)
    {
        Vector3 targetPos = target.position;
        if (!_hasFocus)
        {
            _smoothedFocus = targetPos;
            _hasFocus = true;
            return;
        }

        Vector3 delta = targetPos - _smoothedFocus;
        Vector3 alongUp = Vector3.Project(delta, up);
        Vector3 alongPlane = delta - alongUp;

        float planarT = focusSmoothSpeed <= 0f ? 1f : 1f - Mathf.Exp(-focusSmoothSpeed * Time.deltaTime);
        float heightT = heightSmoothSpeed <= 0f ? 1f : 1f - Mathf.Exp(-heightSmoothSpeed * Time.deltaTime);

        _smoothedFocus += alongPlane * planarT + alongUp * heightT;
    }

    Vector3 ResolveBackDirection(Vector3 up, Vector3 previousUp)
    {
        if (followTargetFacing)
        {
            Vector3 hint = -target.forward;
            if (hint.sqrMagnitude < 0.0001f)
                hint = fixedBackHint.sqrMagnitude > 0.0001f ? fixedBackHint : Vector3.forward;

            Vector3 facingBack = Vector3.ProjectOnPlane(hint, up);
            if (facingBack.sqrMagnitude < 0.001f)
                facingBack = Vector3.ProjectOnPlane(Vector3.right, up);
            if (facingBack.sqrMagnitude < 0.001f)
                facingBack = Vector3.forward;

            _smoothedBack = facingBack.normalized;
            _hasBasis = true;
            return _smoothedBack;
        }

        // Carry the previous tangent "back" with the rotating surface up.
        // Projecting a fixed world compass every frame flips at the antipode (~half orbit).
        if (!_hasBasis)
        {
            Vector3 seed = fixedBackHint.sqrMagnitude > 0.0001f ? fixedBackHint : Vector3.forward;
            Vector3 seeded = Vector3.ProjectOnPlane(seed, up);
            if (seeded.sqrMagnitude < 0.001f)
                seeded = Vector3.ProjectOnPlane(Vector3.right, up);
            if (seeded.sqrMagnitude < 0.001f)
                seeded = Vector3.forward;

            _smoothedBack = seeded.normalized;
            _hasBasis = true;
            return _smoothedBack;
        }

        Vector3 transported = _smoothedBack;
        if (previousUp.sqrMagnitude > 0.0001f && (previousUp - up).sqrMagnitude > 0.000001f)
            transported = Quaternion.FromToRotation(previousUp.normalized, up) * _smoothedBack;

        Vector3 back = Vector3.ProjectOnPlane(transported, up);
        if (back.sqrMagnitude < 0.001f)
        {
            back = Vector3.Cross(up, transported);
            if (back.sqrMagnitude < 0.001f)
                back = Vector3.ProjectOnPlane(Vector3.right, up);
        }

        _smoothedBack = back.normalized;
        return _smoothedBack;
    }
}
