using System.Collections;
using UnityEngine;

/// <summary>
/// Casual follow camera: fixed viewing angle, optional soft tilt to surface up.
/// Does not orbit with the player's facing (avoids dizzy spinning on planets).
/// On curved surfaces, the "behind" axis is parallel-transported so it never flips mid-orbit.
/// Terrain bumps are filtered via radial up + separately damped focus height.
/// Motion framing: slight zoom-out on walk start, then sustained sphere-aware perspective settle.
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

    [Header("Motion Framing")]
    [Tooltip("Pull framing out while moving, then settle into a sphere-readable travel pose.")]
    [SerializeField] bool enableMotionFraming = true;
    [Tooltip("Extra height while moving (dolly zoom-out).")]
    [SerializeField] float moveExtraHeight = 1.1f;
    [Tooltip("Extra back distance while moving.")]
    [SerializeField] float moveExtraBack = 0.7f;
    [Tooltip("Extra FOV while moving (subtle perspective widen). 0 = leave FOV alone.")]
    [SerializeField] float moveExtraFov = 1.5f;
    [Tooltip("Onset punch multiplier on the motion zoom when walk starts.")]
    [SerializeField] float walkStartBoost = 1.12f;
    [Tooltip("How long the start boost fades into sustained travel framing.")]
    [SerializeField] float walkStartBoostDuration = 0.9f;
    [Tooltip("How fast framing expands when motion begins.")]
    [SerializeField] float zoomOutSpeed = 3.5f;
    [Tooltip("How fast framing returns when stopping.")]
    [SerializeField] float zoomInSpeed = 2.2f;
    [Tooltip("Seconds of continuous walking before full sustained perspective correction.")]
    [SerializeField] float sustainSettleTime = 2f;
    [Tooltip("During sustained walk, bias pitch more top-down (extra height, less back) so sphere curvature reads flatter.")]
    [SerializeField] float sustainPitchHeight = 0.8f;
    [Tooltip("During sustained walk, reduce back offset (pairs with sustainPitchHeight).")]
    [SerializeField] float sustainPitchBack = -0.3f;
    [Tooltip("Focus look-ahead along move direction (world units at full motion).")]
    [SerializeField] float lookAheadDistance = 1.4f;
    [SerializeField] float lookAheadSmoothSpeed = 3.5f;
    [Tooltip("Fallback speed used to normalize velocity when PlanetWalker is absent.")]
    [SerializeField] float referenceMoveSpeed = 12f;

    Vector3 _smoothedUp = Vector3.up;
    Vector3 _smoothedBack = Vector3.forward;
    Vector3 _smoothedFocus;
    bool _hasBasis;
    bool _hasFocus;

    PlanetWalker _walker;
    Camera _camera;
    float _baseFov;
    bool _hasBaseFov;

    float _smoothedMotion;
    float _onsetBoost = 1f;
    float _sustainAmount;
    float _movingTimer;
    Vector3 _smoothedLookAhead;
    Vector3 _lastTargetPos;
    bool _hasLastTargetPos;

    bool _snapToTarget;

    public float OffsetHeight => offsetHeight;
    public float OffsetBack => offsetBack;

    /// <summary>Overrides the follow distance/height (e.g. for a cinematic wide shot). Restore afterwards.</summary>
    public void SetOffsets(float height, float back)
    {
        offsetHeight = height;
        offsetBack = back;
    }

    /// <summary>
    /// While true, the camera tracks the target with zero smoothing/motion-framing lag - always
    /// dead-centered. Use for fast scripted moves (e.g. a crash-landing cinematic) where the
    /// normal casual-follow damping can't keep up and the target visibly drifts off-frame.
    /// </summary>
    public void SetSnapToTarget(bool snap)
    {
        _snapToTarget = snap;
    }

    Coroutine _shakeRoutine;
    Vector3 _shakeOffset;
    Vector3 _heartbeatOffset;

    /// <summary>Brief positional shake (e.g. on crash impact). Decays smoothly to zero over duration.</summary>
    public void Shake(float duration, float magnitude)
    {
        if (duration <= 0f || magnitude <= 0f)
            return;
        if (_shakeRoutine != null)
            StopCoroutine(_shakeRoutine);
        _shakeRoutine = StartCoroutine(ShakeRoutine(duration, magnitude));
    }

    /// <summary>
    /// Sustained camera-space kick (x/y) added on top of <see cref="Shake"/>.
    /// Used for low-HP heartbeat. Pass <see cref="Vector3.zero"/> to clear.
    /// </summary>
    public void SetHeartbeatOffset(Vector3 offset)
    {
        _heartbeatOffset = offset;
    }

    IEnumerator ShakeRoutine(float duration, float magnitude)
    {
        float t = 0f;
        float seedX = Random.value * 100f;
        float seedY = Random.value * 100f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float damper = 1f - Mathf.Clamp01(t / duration);
            float x = (Mathf.PerlinNoise(seedX + Time.time * 28f, 0f) - 0.5f) * 2f;
            float y = (Mathf.PerlinNoise(0f, seedY + Time.time * 28f) - 0.5f) * 2f;
            _shakeOffset = new Vector3(x, y, 0f) * magnitude * damper;
            yield return null;
        }
        _shakeOffset = Vector3.zero;
        _shakeRoutine = null;
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
        _hasBasis = false;
        _hasFocus = false;
        _walker = target != null ? target.GetComponent<PlanetWalker>() : null;
        _hasLastTargetPos = false;
        _smoothedLookAhead = Vector3.zero;
        _smoothedMotion = 0f;
        _onsetBoost = 1f;
        _sustainAmount = 0f;
        _movingTimer = 0f;
        if (target != null && alignToTargetUp)
            _smoothedUp = ResolveDesiredUp(target.position, target.up);
    }

    void Awake()
    {
        _camera = GetComponent<Camera>();
        if (_camera != null)
        {
            _baseFov = _camera.fieldOfView;
            _hasBaseFov = true;
        }
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
        if (_snapToTarget || upSmoothSpeed <= 0f)
            _smoothedUp = desiredUp;
        else
            _smoothedUp = Vector3.Slerp(_smoothedUp, desiredUp, 1f - Mathf.Exp(-upSmoothSpeed * Time.deltaTime)).normalized;

        if (_snapToTarget)
            _smoothedMotion = 0f;
        else
            SampleMotion(_smoothedUp);
        UpdateSmoothedFocus(_smoothedUp);

        Vector3 back = ResolveBackDirection(_smoothedUp, previousUp);
        Vector3 right = Vector3.Cross(_smoothedUp, back).normalized;

        ResolveFramingOffsets(out float height, out float backDist, out float fov);

        Vector3 desired = _smoothedFocus
                          + _smoothedUp * height
                          - back * backDist
                          + right * offsetSide;

        if (_snapToTarget || smoothSpeed <= 0f)
            transform.position = desired;
        else
            transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-smoothSpeed * Time.deltaTime));

        if (lookAtTarget)
        {
            Vector3 toTarget = _smoothedFocus - transform.position;
            if (toTarget.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(toTarget.normalized, _smoothedUp);
        }

        Vector3 shake = _shakeOffset + _heartbeatOffset;
        if (shake.sqrMagnitude > 0.0000001f)
            transform.position += transform.right * shake.x + transform.up * shake.y;

        if (_hasBaseFov && _camera != null && enableMotionFraming && moveExtraFov > 0.01f)
        {
            float fovSpeed = _smoothedMotion > 0.05f ? zoomOutSpeed : zoomInSpeed;
            _camera.fieldOfView = Mathf.Lerp(_camera.fieldOfView, fov, 1f - Mathf.Exp(-fovSpeed * Time.deltaTime));
        }
    }

    void SampleMotion(Vector3 up)
    {
        Vector3 velocity = Vector3.zero;
        float amount = 0f;

        if (_walker == null && target != null)
            _walker = target.GetComponent<PlanetWalker>();

        if (_walker != null && _walker.IsWalkingOnPlanet)
        {
            velocity = _walker.PlanarVelocity;
            amount = _walker.MotionAmount;
        }
        else if (target != null)
        {
            if (_hasLastTargetPos && Time.deltaTime > 0.0001f)
            {
                Vector3 delta = target.position - _lastTargetPos;
                velocity = Vector3.ProjectOnPlane(delta, up) / Time.deltaTime;
            }

            float refSpeed = Mathf.Max(0.01f, referenceMoveSpeed);
            amount = Mathf.Clamp01(velocity.magnitude / refSpeed);
            _lastTargetPos = target.position;
            _hasLastTargetPos = true;
        }

        float previousMotion = _smoothedMotion;
        float motionT = amount > _smoothedMotion
            ? (zoomOutSpeed <= 0f ? 1f : 1f - Mathf.Exp(-zoomOutSpeed * Time.deltaTime))
            : (zoomInSpeed <= 0f ? 1f : 1f - Mathf.Exp(-zoomInSpeed * Time.deltaTime));
        _smoothedMotion = Mathf.Lerp(_smoothedMotion, amount, motionT);

        // Soft onset: only a gentle nudge when leaving idle, not a hard punch.
        if (amount > 0.2f && previousMotion < 0.08f)
            _onsetBoost = Mathf.Max(_onsetBoost, walkStartBoost);

        if (_onsetBoost > 1.001f)
        {
            float decay = walkStartBoostDuration <= 0.01f
                ? 1f
                : Time.deltaTime / walkStartBoostDuration;
            _onsetBoost = Mathf.Lerp(_onsetBoost, 1f, Mathf.Clamp01(decay * 0.65f));
        }
        else
        {
            _onsetBoost = 1f;
        }

        if (amount > 0.08f)
            _movingTimer += Time.deltaTime;
        else
            _movingTimer = Mathf.Max(0f, _movingTimer - Time.deltaTime * 0.9f);

        float sustainTarget = sustainSettleTime <= 0.01f
            ? (amount > 0.08f ? 1f : 0f)
            : Mathf.Clamp01(_movingTimer / sustainSettleTime);
        float sustainBlend = amount > 0.08f ? Mathf.Min(zoomOutSpeed, 2.2f) : zoomInSpeed;
        float sustainT = 1f - Mathf.Exp(-sustainBlend * Time.deltaTime);
        _sustainAmount = Mathf.Lerp(_sustainAmount, sustainTarget * _smoothedMotion, sustainT);

        Vector3 lookDir = Vector3.ProjectOnPlane(velocity, up);
        Vector3 desiredLookAhead = lookDir.sqrMagnitude > 0.01f
            ? lookDir.normalized * (lookAheadDistance * _smoothedMotion)
            : Vector3.zero;

        // Keep look-ahead on the current tangent plane (sphere-safe).
        desiredLookAhead = Vector3.ProjectOnPlane(desiredLookAhead, up);
        float lookT = lookAheadSmoothSpeed <= 0f ? 1f : 1f - Mathf.Exp(-lookAheadSmoothSpeed * Time.deltaTime);
        _smoothedLookAhead = Vector3.Lerp(_smoothedLookAhead, desiredLookAhead, lookT);
        _smoothedLookAhead = Vector3.ProjectOnPlane(_smoothedLookAhead, up);
    }

    void ResolveFramingOffsets(out float height, out float backDist, out float fov)
    {
        height = offsetHeight;
        backDist = offsetBack;
        fov = _hasBaseFov ? _baseFov : 60f;

        if (_snapToTarget || !enableMotionFraming)
            return;

        float zoom = _smoothedMotion * _onsetBoost;
        height += moveExtraHeight * zoom;
        backDist += moveExtraBack * zoom;

        // Sustained walk: pitch more top-down so curved ground foreshortens less on screen.
        height += sustainPitchHeight * _sustainAmount;
        backDist += sustainPitchBack * _sustainAmount;

        height = Mathf.Max(1f, height);
        backDist = Mathf.Max(0.5f, backDist);
        fov = _baseFov + moveExtraFov * _smoothedMotion;
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
        if (_snapToTarget)
        {
            _smoothedFocus = target.position;
            _hasFocus = true;
            return;
        }

        Vector3 targetPos = target.position + _smoothedLookAhead;
        if (!_hasFocus)
        {
            _smoothedFocus = target.position;
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
