using UnityEngine;

/// <summary>
/// Casual follow camera: fixed viewing angle, optional soft tilt to surface up.
/// Does not orbit with the player's facing (avoids dizzy spinning on planets).
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
    [Tooltip("If off (casual), camera keeps a fixed compass angle and ignores player facing.")]
    [SerializeField] bool followTargetFacing = false;
    [Tooltip("World direction used as \"behind the player\" when not following facing.")]
    [SerializeField] Vector3 fixedBackHint = new Vector3(0.25f, 0f, 1f);
    [SerializeField] float upSmoothSpeed = 8f;

    Vector3 _smoothedUp = Vector3.up;

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
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

        if (upSmoothSpeed <= 0f)
            _smoothedUp = desiredUp;
        else
            _smoothedUp = Vector3.Slerp(_smoothedUp, desiredUp, 1f - Mathf.Exp(-upSmoothSpeed * Time.deltaTime)).normalized;

        Vector3 back = ResolveBackDirection(_smoothedUp);
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

    Vector3 ResolveBackDirection(Vector3 up)
    {
        Vector3 hint = followTargetFacing ? -target.forward : fixedBackHint;
        if (hint.sqrMagnitude < 0.0001f)
            hint = Vector3.forward;

        Vector3 back = Vector3.ProjectOnPlane(hint, up);
        if (back.sqrMagnitude < 0.001f)
            back = Vector3.ProjectOnPlane(Vector3.right, up);
        if (back.sqrMagnitude < 0.001f)
            back = Vector3.forward;

        return back.normalized;
    }
}
