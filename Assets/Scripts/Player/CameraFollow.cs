using UnityEngine;

/// <summary>
/// Casual follow camera: fixed viewing angle, optional soft tilt to surface up.
/// Does not orbit with the player's facing (avoids dizzy spinning on planets).
/// On curved surfaces, the "behind" axis is parallel-transported so it never flips mid-orbit.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [SerializeField] Transform target;

    [Header("Offset")]
    [SerializeField] float offsetHeight = 12f;
    [SerializeField] float offsetBack = 8f;
    [SerializeField] float offsetSide = 0f;

    [Header("Follow")]
    [SerializeField] float smoothSpeed = 10f;
    [SerializeField] bool lookAtTarget = true;
    [Tooltip("Tilt camera using the target's up (planet surface). Softly smoothed.")]
    [SerializeField] bool alignToTargetUp = true;
    [Tooltip("If off (casual), camera keeps a stable viewing angle and ignores player facing.")]
    [SerializeField] bool followTargetFacing = false;
    [Tooltip("Initial world direction used as \"behind the player\" when not following facing.")]
    [SerializeField] Vector3 fixedBackHint = new Vector3(0.25f, 0f, 1f);
    [SerializeField] float upSmoothSpeed = 8f;

    Vector3 _smoothedUp = Vector3.up;
    Vector3 _smoothedBack = Vector3.forward;
    bool _hasBasis;

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        _hasBasis = false;
        if (target != null && alignToTargetUp)
            _smoothedUp = target.up;
    }

    void LateUpdate()
    {
        if (target == null)
            return;

        Vector3 desiredUp = alignToTargetUp ? target.up : Vector3.up;
        if (desiredUp.sqrMagnitude < 0.0001f)
            desiredUp = Vector3.up;
        desiredUp.Normalize();

        Vector3 previousUp = _smoothedUp;
        if (upSmoothSpeed <= 0f)
            _smoothedUp = desiredUp;
        else
            _smoothedUp = Vector3.Slerp(_smoothedUp, desiredUp, 1f - Mathf.Exp(-upSmoothSpeed * Time.deltaTime)).normalized;

        Vector3 back = ResolveBackDirection(_smoothedUp, previousUp);
        Vector3 right = Vector3.Cross(_smoothedUp, back).normalized;

        Vector3 desired = target.position
                          + _smoothedUp * offsetHeight
                          - back * offsetBack
                          + right * offsetSide;

        if (smoothSpeed <= 0f)
            transform.position = desired;
        else
            transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-smoothSpeed * Time.deltaTime));

        if (lookAtTarget)
        {
            Vector3 toTarget = target.position - transform.position;
            if (toTarget.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(toTarget.normalized, _smoothedUp);
        }
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
