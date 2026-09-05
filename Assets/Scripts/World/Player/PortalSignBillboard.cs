using UnityEngine;

/// <summary>
/// Keeps the portal's Sign facing the camera while the Stand stays planted.
/// Super-casual idle: bounce hover, squash-and-stretch, occasional attention pop,
/// and a plant squash when the portal hits the ground.
///
/// Yaw is cylindrical (the oval stays upright, arrow stays planet-up) with a small
/// extra pitch toward the camera so a high-angle follow cam can still read the face.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(900)]
public class PortalSignBillboard : MonoBehaviour
{
    const float MinSqr = 0.0001f;

    [Header("Parts")]
    [Tooltip("The oval board only. Leave empty to auto-find a child named Sign. Stand is never touched.")]
    [SerializeField] Transform sign;

    [Header("Billboard")]
    [Tooltip("Seconds to ease yaw toward the camera. 0 = snap.")]
    [SerializeField, Min(0f)] float yawLag = 0.1f;
    [Tooltip("0 = stay upright. 1 = pitch fully toward the camera. Keep low so the neck still reads as attached.")]
    [SerializeField, Range(0f, 1f)] float pitchTowardCamera = 0.16f;
    [Tooltip("Tick if the arrow is on the far side after play starts.")]
    [SerializeField] bool flipFace;

    [Header("Hover")]
    [SerializeField, Min(0f)] float bobHeight = 0.07f;
    [Tooltip("Hover cycle in Hz. Slightly snappy so it reads as a toy, not a flag.")]
    [SerializeField, Min(0.05f)] float bobSpeed = 0.78f;
    [Tooltip("Squash/stretch amount tied to the hover. Volume-preserving.")]
    [SerializeField, Range(0f, 0.25f)] float squash = 0.11f;
    [Tooltip("Jelly follow-through on scale, in seconds.")]
    [SerializeField, Min(0f)] float scaleFollowThrough = 0.07f;
    [SerializeField, Min(0f)] float tiltDegrees = 5f;
    [SerializeField, Min(0f)] float rollDegrees = 6.5f;

    [Header("Attention")]
    [SerializeField, Min(0.5f)] float popIntervalMin = 2.9f;
    [SerializeField, Min(0.5f)] float popIntervalMax = 4.4f;
    [SerializeField, Min(0.05f)] float popDuration = 0.48f;
    [SerializeField, Range(0f, 0.45f)] float popSquash = 0.16f;
    [SerializeField, Min(0f)] float popLift = 0.1f;

    [Header("Plant")]
    [SerializeField, Min(0.05f)] float plantDuration = 0.58f;
    [SerializeField, Range(0f, 0.55f)] float plantSquash = 0.24f;
    [SerializeField, Min(0f)] float plantLift = 0.18f;

    [Header("Use Excited")]
    [Tooltip("Sharp hop burst when the player touches the portal to teleport.")]
    [SerializeField, Min(0.2f)] float excitedDuration = 0.72f;
    [SerializeField, Range(0f, 0.55f)] float excitedSquash = 0.3f;
    [SerializeField, Min(0f)] float excitedHop = 0.24f;
    [SerializeField, Range(0f, 0.4f)] float excitedPunch = 0.18f;
    [SerializeField, Min(0f)] float excitedNodDegrees = 17f;
    [SerializeField, Min(0f)] float excitedShakeDegrees = 20f;

    Camera _camera;
    bool _captured;
    bool _yawInited;
    Vector3 _restLocalPos;
    Quaternion _restLocalRot = Quaternion.identity;
    Vector3 _restLocalScale = Vector3.one;
    Vector3 _localFaceAxis = Vector3.up;
    Vector3 _localUpAxis = Vector3.forward;
    float _phase;
    float _yaw;
    float _yawVelocity;
    Vector3 _scaleMul = Vector3.one;
    Vector3 _scaleMulVel;
    float _nextPopTime;
    float _popElapsed;
    bool _popping;
    float _plantElapsed;
    bool _planting;
    float _excitedElapsed;
    bool _excited;
    bool _frozen;

    public void PlayPlantImpact()
    {
        if (_excited || _frozen)
            return;

        _planting = true;
        _plantElapsed = 0f;
        _popping = false;
        ScheduleNextPop(1.1f);
    }

    public void PlayAttentionPop()
    {
        if (_planting || _excited || _frozen)
            return;

        _popping = true;
        _popElapsed = 0f;
    }

    /// <summary>Sharp cartoon burst when the player steps in to use the portal.</summary>
    public void PlayUseExcited()
    {
        _excited = true;
        _excitedElapsed = 0f;
        _frozen = false;
        _planting = false;
        _popping = false;
        _scaleMulVel = Vector3.zero;
    }

    void OnEnable()
    {
        _phase = Random.Range(0f, 20f);
        _yawInited = false;
        _yawVelocity = 0f;
        _scaleMul = Vector3.one;
        _scaleMulVel = Vector3.zero;
        _popping = false;
        _planting = false;
        _excited = false;
        _frozen = false;
        _captured = false;
        BindSign();
        if (sign != null)
            CaptureRest();
        ScheduleNextPop(0.85f);
    }

    void OnDisable()
    {
        RestoreRest();
    }

    void OnValidate()
    {
        if (popIntervalMax < popIntervalMin)
            popIntervalMax = popIntervalMin;
        if (sign == null)
            BindSign();
    }

    void LateUpdate()
    {
        if (_frozen || !EnsureSign())
            return;

        if (!_captured)
            CaptureRest();

        Camera cam = ResolveCamera();
        Vector3 up = ResolveUp(sign.position);
        Vector3 worldPos = sign.parent != null
            ? sign.parent.TransformPoint(_restLocalPos)
            : _restLocalPos;

        Quaternion restWorld = RestWorldRotation();
        Vector3 restFace = Vector3.ProjectOnPlane(restWorld * _localFaceAxis, up);

        Vector3 planarDir = Vector3.zero;
        Vector3 toCam = Vector3.zero;
        bool hasCamAzimuth = false;
        if (cam != null)
        {
            toCam = cam.transform.position - worldPos;
            Vector3 planar = Vector3.ProjectOnPlane(toCam, up);
            if (planar.sqrMagnitude > MinSqr)
            {
                planarDir = planar.normalized;
                hasCamAzimuth = true;
                float targetYaw = restFace.sqrMagnitude > MinSqr
                    ? Vector3.SignedAngle(restFace, planarDir, up)
                    : 0f;

                if (!_yawInited)
                {
                    _yaw = targetYaw;
                    _yawInited = true;
                }
                else if (yawLag <= 0.0001f)
                    _yaw = targetYaw;
                else
                    _yaw = Mathf.SmoothDampAngle(_yaw, targetYaw, ref _yawVelocity, yawLag);
            }
        }

        Quaternion yawed = Quaternion.AngleAxis(_yaw, up) * restWorld;
        Vector3 lookFwd = Vector3.ProjectOnPlane(yawed * _localFaceAxis, up);
        if (lookFwd.sqrMagnitude < MinSqr)
            lookFwd = PlanarForward(yawed, up);
        else
            lookFwd.Normalize();

        Vector3 right = Vector3.Cross(up, lookFwd);
        if (right.sqrMagnitude < MinSqr)
            right = cam != null ? Vector3.ProjectOnPlane(cam.transform.right, up) : Vector3.right;
        if (right.sqrMagnitude < MinSqr)
            right = Vector3.right;
        else
            right.Normalize();

        float pitchAngle = 0f;
        if (hasCamAzimuth && pitchTowardCamera > 0.0001f && toCam.sqrMagnitude > MinSqr)
        {
            Vector3 pitchAxis = Vector3.Cross(up, planarDir);
            if (pitchAxis.sqrMagnitude > MinSqr)
            {
                pitchAxis.Normalize();
                Vector3 pitched = Vector3.Slerp(planarDir, toCam.normalized, pitchTowardCamera);
                if (pitched.sqrMagnitude > MinSqr)
                    pitchAngle = Vector3.SignedAngle(planarDir, pitched.normalized, pitchAxis);
                right = pitchAxis;
            }
        }

        Quaternion posed = Quaternion.AngleAxis(pitchAngle, right) * yawed;

        float dt = Time.deltaTime;
        float tau = (Time.time + _phase) * Mathf.PI * 2f * bobSpeed;
        float bounce = Mathf.Sin(tau) * 0.8f + Mathf.Sin(tau * 2f + 0.4f) * 0.2f;

        float pulseUp = 1f;
        float pulseLift = 0f;
        float wobbleScale = 1f;
        float nod = 0f;
        float shake = 0f;
        float punch = 1f;
        TickPulses(dt, ref pulseUp, ref pulseLift, ref wobbleScale, ref nod, ref shake, ref punch);

        float idleBlend = _excited ? 0.12f : 1f;
        float bob = bounce * bobHeight * idleBlend;
        float upMul = Mathf.Max(0.35f, (1f + squash * bounce * idleBlend) * pulseUp);
        float planarMul = 1f / Mathf.Sqrt(upMul);

        Vector3 localUpAbs = AbsAxis(_localUpAxis);
        Vector3 targetMul = (Vector3.one * planarMul + localUpAbs * (upMul - planarMul)) * punch;
        float follow = _excited ? Mathf.Min(scaleFollowThrough, 0.025f) : scaleFollowThrough;
        if (follow <= 0.0001f)
            _scaleMul = targetMul;
        else
            _scaleMul = Vector3.SmoothDamp(_scaleMul, targetMul, ref _scaleMulVel, follow);

        float pitchWobble = Mathf.Sin(tau * 0.53f + 0.2f) * tiltDegrees * wobbleScale;
        float rollWobble = Mathf.Sin(tau * 0.67f + 1.1f) * rollDegrees * wobbleScale;
        Quaternion wobble = Quaternion.AngleAxis(pitchWobble + nod, right) * Quaternion.AngleAxis(rollWobble + shake, lookFwd);

        sign.rotation = wobble * posed;
        sign.localScale = Vector3.Scale(_restLocalScale, _scaleMul);

        float lift = bob + pulseLift;
        Vector3 liftLocal = sign.parent != null
            ? sign.parent.InverseTransformDirection(up) * lift
            : up * lift;
        sign.localPosition = _restLocalPos + liftLocal;

        if (_excited && _excitedElapsed >= excitedDuration)
            FreezeSign(posed);
    }

    void TickPulses(
        float dt,
        ref float pulseUp,
        ref float pulseLift,
        ref float wobbleScale,
        ref float nod,
        ref float shake,
        ref float punch)
    {
        nod = 0f;
        shake = 0f;
        punch = 1f;

        if (_excited)
        {
            _excitedElapsed += dt;
            float t = excitedDuration > 0.0001f
                ? Mathf.Clamp01(_excitedElapsed / excitedDuration)
                : 1f;
            EvaluateExcited(t, out pulseUp, out pulseLift, out nod, out shake, out punch);
            wobbleScale = t >= 1f ? 0f : 0.12f;
            return;
        }

        if (_planting)
        {
            _plantElapsed += dt;
            float t = plantDuration > 0.0001f ? _plantElapsed / plantDuration : 1f;
            EvaluatePulse(t, plantSquash, plantLift, out pulseUp, out pulseLift);
            wobbleScale = 0.15f;
            if (t >= 1f)
            {
                _planting = false;
                ScheduleNextPop(0.7f);
            }
            return;
        }

        if (!_popping && Time.time >= _nextPopTime)
            PlayAttentionPop();

        if (!_popping)
            return;

        _popElapsed += dt;
        float popT = popDuration > 0.0001f ? _popElapsed / popDuration : 1f;
        EvaluatePulse(popT, popSquash, popLift, out pulseUp, out pulseLift);
        wobbleScale = 0.35f;
        if (popT >= 1f)
        {
            _popping = false;
            ScheduleNextPop(0f);
        }
    }

    void ScheduleNextPop(float extraDelay)
    {
        float min = popIntervalMin;
        float max = Mathf.Max(popIntervalMin, popIntervalMax);
        _nextPopTime = Time.time + extraDelay + Random.Range(min, max);
    }

    void EvaluateExcited(float t, out float upMul, out float lift, out float nod, out float shake, out float punch)
    {
        t = Mathf.Clamp01(t);
        if (t < 0.11f)
        {
            float u = t / 0.11f;
            float s = u * u;
            upMul = Mathf.Lerp(1f, 1f - excitedSquash, s);
            lift = 0f;
            nod = Mathf.Lerp(0f, -excitedNodDegrees * 0.45f, s);
            shake = 0f;
            punch = Mathf.Lerp(1f, 1f - excitedPunch * 0.35f, s);
            return;
        }

        float v = (t - 0.11f) / 0.89f;
        float hops = Mathf.Max(
            Hop01(v, 0f, 0.28f),
            Hop01(v, 0.24f, 0.22f) * 0.78f,
            Hop01(v, 0.44f, 0.18f) * 0.55f);
        float damp = Mathf.Exp(-1.25f * v);
        float land = 1f - hops;

        upMul = 1f + excitedSquash * (hops * 0.9f - land * 0.42f);
        lift = excitedHop * hops;
        nod = excitedNodDegrees * (hops * 1.1f - 0.22f) * damp;
        shake = excitedShakeDegrees * Mathf.Sin(v * Mathf.PI * 11f) * damp;
        punch = 1f + excitedPunch * (0.32f + 0.8f * hops) * damp;

        if (v <= 0.62f)
            return;

        float settle = Smooth01((v - 0.62f) / 0.38f);
        upMul = Mathf.Lerp(upMul, 1f, settle);
        lift = Mathf.Lerp(lift, 0f, settle);
        nod = Mathf.Lerp(nod, 0f, settle);
        shake = Mathf.Lerp(shake, 0f, settle);
        punch = Mathf.Lerp(punch, 1f, settle);
    }

    void FreezeSign(Quaternion posed)
    {
        _frozen = true;
        _excited = false;
        _popping = false;
        _planting = false;
        _scaleMul = Vector3.one;
        _scaleMulVel = Vector3.zero;

        sign.localPosition = _restLocalPos;
        sign.localScale = _restLocalScale;
        sign.rotation = posed;
    }

    static float Hop01(float t, float start, float duration)
    {
        if (duration <= 0.0001f)
            return 0f;

        float u = (t - start) / duration;
        if (u <= 0f || u >= 1f)
            return 0f;

        float arc = 4f * u * (1f - u);
        return arc * arc;
    }

    static void EvaluatePulse(float t, float squashAmt, float lift, out float upMul, out float liftOffset)
    {
        t = Mathf.Clamp01(t);
        if (t < 0.2f)
        {
            float u = Smooth01(t / 0.2f);
            upMul = Mathf.Lerp(1f, 1f - squashAmt, u);
            liftOffset = 0f;
            return;
        }

        float v = (t - 0.2f) / 0.8f;
        float damp = Mathf.Exp(-4.1f * v);
        upMul = 1f + squashAmt * 0.7f * Mathf.Sin(v * Mathf.PI * 2.05f) * damp;
        liftOffset = lift * Mathf.Sin(Mathf.PI * Mathf.Clamp01(v * 1.12f)) * Mathf.Exp(-2.35f * v);
    }

    static float Smooth01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    bool EnsureSign()
    {
        if (sign != null)
            return true;
        BindSign();
        return sign != null;
    }

    void BindSign()
    {
        if (sign != null)
            return;
        if (name == "Sign")
        {
            sign = transform;
            return;
        }

        sign = FindNamed(transform, "Sign");
    }

    void CaptureRest()
    {
        if (sign == null)
            return;

        _restLocalPos = sign.localPosition;
        _restLocalRot = sign.localRotation;
        _restLocalScale = sign.localScale;
        _scaleMul = Vector3.one;
        _captured = true;

        Quaternion restWorld = RestWorldRotation();
        Vector3 up = ResolveUp(sign.position);
        _localUpAxis = BestAlignedLocalAxis(restWorld, up);

        MeshFilter filter = sign.GetComponent<MeshFilter>();
        if (filter != null && filter.sharedMesh != null)
            _localFaceAxis = ThinnestLocalAxis(filter.sharedMesh.bounds.size, _localUpAxis);
        else
            _localFaceAxis = BestAlignedLocalAxis(restWorld, PlanarForward(restWorld, up));

        if (flipFace)
            _localFaceAxis = -_localFaceAxis;
    }

    void RestoreRest()
    {
        if (!_captured || sign == null)
            return;

        sign.localPosition = _restLocalPos;
        sign.localRotation = _restLocalRot;
        sign.localScale = _restLocalScale;
    }

    Quaternion RestWorldRotation()
    {
        return sign != null && sign.parent != null
            ? sign.parent.rotation * _restLocalRot
            : _restLocalRot;
    }

    Vector3 ResolveUp(Vector3 worldPos)
    {
        if (SphericalPlanet.Instance != null)
        {
            Vector3 up = SphericalPlanet.Instance.GetUpAt(worldPos);
            if (up.sqrMagnitude > MinSqr)
                return up.normalized;
        }

        if (sign != null && sign.parent != null)
        {
            Vector3 parentUp = sign.parent.up;
            if (parentUp.sqrMagnitude > MinSqr)
                return parentUp.normalized;
        }

        return transform.up.sqrMagnitude > MinSqr ? transform.up.normalized : Vector3.up;
    }

    Camera ResolveCamera()
    {
        if (_camera != null)
            return _camera;

        _camera = Camera.main;
        if (_camera == null)
            _camera = FindAnyObjectByType<Camera>();
        return _camera;
    }

    static Vector3 PlanarForward(Quaternion worldRot, Vector3 up)
    {
        Vector3 fwd = Vector3.ProjectOnPlane(worldRot * Vector3.forward, up);
        if (fwd.sqrMagnitude < MinSqr)
            fwd = Vector3.ProjectOnPlane(worldRot * Vector3.right, up);
        if (fwd.sqrMagnitude < MinSqr)
            fwd = Vector3.ProjectOnPlane(Vector3.forward, up);
        return fwd.sqrMagnitude > MinSqr ? fwd.normalized : Vector3.forward;
    }

    static Vector3 BestAlignedLocalAxis(Quaternion worldRot, Vector3 worldDir)
    {
        Vector3 dir = worldDir.sqrMagnitude > MinSqr ? worldDir.normalized : Vector3.up;
        Vector3 bestAxis = Vector3.up;
        float best = -1f;
        SampleAxis(worldRot, Vector3.right, dir, ref best, ref bestAxis);
        SampleAxis(worldRot, Vector3.up, dir, ref best, ref bestAxis);
        SampleAxis(worldRot, Vector3.forward, dir, ref best, ref bestAxis);
        return bestAxis;
    }

    static void SampleAxis(Quaternion worldRot, Vector3 axis, Vector3 dir, ref float best, ref Vector3 bestAxis)
    {
        float d = Vector3.Dot(worldRot * axis, dir);
        float mag = Mathf.Abs(d);
        if (mag <= best)
            return;

        best = mag;
        bestAxis = d < 0f ? -axis : axis;
    }

    static Vector3 ThinnestLocalAxis(Vector3 size, Vector3 localUp)
    {
        Vector3 upAbs = AbsAxis(localUp);
        float x = upAbs.x > 0.75f ? float.MaxValue : size.x;
        float y = upAbs.y > 0.75f ? float.MaxValue : size.y;
        float z = upAbs.z > 0.75f ? float.MaxValue : size.z;
        if (x <= y && x <= z)
            return Vector3.right;
        if (y <= z)
            return Vector3.up;
        return Vector3.forward;
    }

    static Vector3 AbsAxis(Vector3 axis)
    {
        return new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z));
    }

    static Transform FindNamed(Transform root, string objectName)
    {
        if (root == null)
            return null;
        if (root.name == objectName)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindNamed(root.GetChild(i), objectName);
            if (found != null)
                return found;
        }

        return null;
    }
}
