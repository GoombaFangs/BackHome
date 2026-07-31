using UnityEngine;
using StarterAssets;

/// <summary>
/// Walks on a spherical planet (Outer Wilds style).
/// On the ship / flat scenes this stays idle and <see cref="TouchController"/> handles movement.
/// On Planet/Galaxy scenes with a <see cref="SphericalPlanet"/>, this takes over.
/// When the planet has a heightmap, feet follow mountains/valleys from that map.
/// </summary>
[DefaultExecutionOrder(20)]
[RequireComponent(typeof(StarterAssetsInputs))]
[RequireComponent(typeof(TouchController))]
public class PlanetWalker : MonoBehaviour
{
    [Tooltip("If on, only activates when the scene name starts with Planet or Galaxy.")]
    [SerializeField] bool onlyInPlanetScenes = true;

    [Header("Movement")]
    [SerializeField] float walkSpeed = 12f;
    [SerializeField] float runSpeed = 14f;
    [SerializeField] float runInputThreshold = 0.7f;
    [SerializeField] float alignSpeed = 12f;
    [SerializeField] float hoverHeight = 0.03f;
    [SerializeField] float gravityStrength = 14f;
    [SerializeField] float groundProbeDistance = 6f;
    [SerializeField] LayerMask groundLayer;

    [Header("Feel")]
    [SerializeField] float animationSpeedScale = 1.15f;

    SphericalPlanet _planet;
    StarterAssetsInputs _input;
    CharacterController _controller;
    TouchController _flatMotor;
    Animator _animator;
    Camera _camera;
    Rigidbody _body;
    CapsuleCollider _triggerBody;

    Vector3 _fallVelocity;
    float _animBlend;
    bool _grounded;
    bool _ownsControl;
    bool _sceneAllowsPlanetWalk;

    int _animIDSpeed;
    int _animIDGrounded;
    int _animIDJump;
    int _animIDFreeFall;
    int _animIDMotionSpeed;

    public bool IsWalkingOnPlanet => _ownsControl && _planet != null;

    void Awake()
    {
        hoverHeight = Mathf.Clamp(hoverHeight, 0.005f, 0.08f);
        gravityStrength = Mathf.Max(10f, gravityStrength);
        groundProbeDistance = Mathf.Max(4f, groundProbeDistance);

        _input = GetComponent<StarterAssetsInputs>();
        _controller = GetComponent<CharacterController>();
        _flatMotor = GetComponent<TouchController>();
        _animator = GetComponent<Animator>();
        _camera = Camera.main;

        if (groundLayer.value == 0)
            groundLayer = LayerMask.GetMask("Ground");

        _animIDSpeed = Animator.StringToHash("Speed");
        _animIDGrounded = Animator.StringToHash("Grounded");
        _animIDJump = Animator.StringToHash("Jump");
        _animIDFreeFall = Animator.StringToHash("FreeFall");
        _animIDMotionSpeed = Animator.StringToHash("MotionSpeed");
    }

    void Start()
    {
        _sceneAllowsPlanetWalk = !onlyInPlanetScenes || SceneRoles.IsPlanetScene();
        if (_sceneAllowsPlanetWalk)
            TryStartPlanetWalk();
    }

    void OnEnable()
    {
        if (_sceneAllowsPlanetWalk)
            TryStartPlanetWalk();
    }

    void OnDisable()
    {
        StopPlanetWalk();
    }

    void Update()
    {
        if (!_sceneAllowsPlanetWalk)
            return;

        if (!_ownsControl || _planet == null)
        {
            TryStartPlanetWalk();
            if (!_ownsControl || _planet == null)
                return;
        }

        TickPlanetWalk();
    }

    void TryStartPlanetWalk()
    {
        if (_ownsControl && _planet != null)
            return;

        SphericalPlanet planet = SphericalPlanet.Instance != null
            ? SphericalPlanet.Instance
            : FindFirstObjectByType<SphericalPlanet>();

        if (planet == null)
            return;

        _planet = planet;
        TakeControl();

        Vector3 preferredUp = transform.position - _planet.Center;
        if (preferredUp.sqrMagnitude < 0.01f)
            preferredUp = Vector3.up;
        SnapToSurface(preferredUp);
    }

    void TakeControl()
    {
        if (_ownsControl)
            return;

        _ownsControl = true;
        if (_flatMotor != null)
            _flatMotor.enabled = false;
        if (_controller != null)
            _controller.enabled = false;

        EnsurePhysicsProxy();
    }

    void StopPlanetWalk()
    {
        if (!_ownsControl)
            return;

        _ownsControl = false;
        _planet = null;

        if (_triggerBody != null)
            _triggerBody.enabled = false;
        if (_body != null)
            _body.detectCollisions = false;
        if (_controller != null)
            _controller.enabled = true;
        if (_flatMotor != null)
            _flatMotor.enabled = true;
    }

    void EnsurePhysicsProxy()
    {
        _body = GetComponent<Rigidbody>();
        if (_body == null)
            _body = gameObject.AddComponent<Rigidbody>();
        _body.isKinematic = true;
        _body.useGravity = false;
        _body.interpolation = RigidbodyInterpolation.Interpolate;
        _body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        _body.detectCollisions = true;

        _triggerBody = GetComponent<CapsuleCollider>();
        if (_triggerBody == null)
            _triggerBody = gameObject.AddComponent<CapsuleCollider>();
        _triggerBody.height = _controller != null ? Mathf.Max(1.6f, _controller.height) : 1.8f;
        _triggerBody.radius = _controller != null ? Mathf.Max(0.28f, _controller.radius) : 0.3f;
        _triggerBody.center = _controller != null ? _controller.center : new Vector3(0f, 0.9f, 0f);
        _triggerBody.isTrigger = true;
        _triggerBody.enabled = true;
    }

    void TickPlanetWalk()
    {
        if (_camera == null)
            _camera = Camera.main;

        float rideHeight = GetRideHeight();
        _input.jump = false;
        _input.sprint = false;
        _input.look = Vector2.zero;

        if (_planet.HasHeightTerrain)
        {
            TickHeightmapWalk(rideHeight);
            return;
        }

        TickColliderWalk(rideHeight);
    }

    void TickHeightmapWalk(float rideHeight)
    {
        Vector3 up = _planet.GetUpAt(transform.position);
        Vector2 moveInput = _input.move;
        float inputMagnitude = Mathf.Clamp01(moveInput.magnitude);
        float targetSpeed = 0f;
        if (inputMagnitude > 0.01f)
        {
            targetSpeed = inputMagnitude >= runInputThreshold
                ? runSpeed
                : walkSpeed * Mathf.Clamp01(inputMagnitude / runInputThreshold);
        }

        Vector3 moveDir = GetTangentMoveDirection(moveInput, up);
        Vector3 radial = up;
        if (moveDir.sqrMagnitude > 0.001f)
        {
            Vector3 slid = transform.position + moveDir * (targetSpeed * Time.deltaTime);
            radial = (slid - _planet.Center).normalized;
        }

        Vector3 surfaceNormal = _planet.GetTerrainNormal(radial);
        Vector3 next = _planet.GetSurfacePoint(radial, rideHeight);
        _grounded = true;
        _fallVelocity = Vector3.zero;

        Vector3 faceDir = moveDir.sqrMagnitude > 0.001f
            ? Vector3.ProjectOnPlane(moveDir, surfaceNormal)
            : Vector3.ProjectOnPlane(transform.forward, surfaceNormal);
        if (faceDir.sqrMagnitude < 0.001f)
            faceDir = Vector3.ProjectOnPlane(Vector3.forward, surfaceNormal);

        ApplyPose(next, faceDir.normalized, surfaceNormal);
        UpdateAnimator(targetSpeed, inputMagnitude);
    }

    void TickColliderWalk(float rideHeight)
    {
        Vector3 up = _planet.GetUpAt(transform.position);
        bool hitGround = Physics.Raycast(
            transform.position + up * 1.25f,
            -up,
            out RaycastHit hit,
            groundProbeDistance,
            groundLayer,
            QueryTriggerInteraction.Ignore);

        if (hitGround)
        {
            up = hit.normal;
            _grounded = true;
        }
        else
        {
            _grounded = false;
        }

        Vector2 moveInput = _input.move;
        float inputMagnitude = Mathf.Clamp01(moveInput.magnitude);
        float targetSpeed = 0f;
        if (inputMagnitude > 0.01f)
        {
            targetSpeed = inputMagnitude >= runInputThreshold
                ? runSpeed
                : walkSpeed * Mathf.Clamp01(inputMagnitude / runInputThreshold);
        }

        Vector3 moveDir = GetTangentMoveDirection(moveInput, up);
        Vector3 next = transform.position + moveDir * (targetSpeed * Time.deltaTime);

        if (_grounded && hitGround)
        {
            _fallVelocity = Vector3.zero;
            next = hit.point + up * rideHeight;
            if (moveDir.sqrMagnitude > 0.001f)
            {
                Vector3 slid = hit.point + moveDir * (targetSpeed * Time.deltaTime);
                Vector3 radial = (slid - _planet.Center).normalized;
                next = _planet.Center + radial * (_planet.Radius + rideHeight);
                if (Physics.Raycast(next + radial * 1.2f, -radial, out RaycastHit stickHit, groundProbeDistance, groundLayer, QueryTriggerInteraction.Ignore))
                {
                    next = stickHit.point + stickHit.normal * rideHeight;
                    up = stickHit.normal;
                }
            }
        }
        else
        {
            Vector3 toCenter = (_planet.Center - transform.position).normalized;
            _fallVelocity += toCenter * (gravityStrength * Time.deltaTime);
            next += _fallVelocity * Time.deltaTime;

            float minDist = _planet.Radius + rideHeight;
            Vector3 fromCenter = next - _planet.Center;
            if (fromCenter.magnitude < minDist)
            {
                next = _planet.Center + fromCenter.normalized * minDist;
                _fallVelocity = Vector3.zero;
                _grounded = true;
                up = fromCenter.normalized;
            }
        }

        Vector3 faceDir = moveDir.sqrMagnitude > 0.001f
            ? moveDir
            : Vector3.ProjectOnPlane(transform.forward, up);
        if (faceDir.sqrMagnitude < 0.001f)
            faceDir = Vector3.ProjectOnPlane(Vector3.forward, up);

        ApplyPose(next, faceDir.normalized, up);
        UpdateAnimator(targetSpeed, inputMagnitude);
    }

    void ApplyPose(Vector3 next, Vector3 faceDir, Vector3 up)
    {
        Quaternion targetRot = Quaternion.LookRotation(faceDir, up);
        Quaternion nextRot = Quaternion.Slerp(
            transform.rotation,
            targetRot,
            1f - Mathf.Exp(-alignSpeed * Time.deltaTime));

        if (_body != null)
        {
            _body.position = next;
            _body.rotation = nextRot;
        }

        transform.SetPositionAndRotation(next, nextRot);
    }

    Vector3 GetTangentMoveDirection(Vector2 moveInput, Vector3 up)
    {
        if (moveInput.sqrMagnitude < 0.01f)
            return Vector3.zero;

        Vector3 camForward = _camera != null ? _camera.transform.forward : transform.forward;
        Vector3 camRight = _camera != null ? _camera.transform.right : transform.right;
        camForward = Vector3.ProjectOnPlane(camForward, up);
        camRight = Vector3.ProjectOnPlane(camRight, up);

        if (camForward.sqrMagnitude < 0.001f)
            camForward = Vector3.ProjectOnPlane(transform.forward, up);
        if (camRight.sqrMagnitude < 0.001f)
            camRight = Vector3.Cross(up, camForward);

        camForward.Normalize();
        camRight.Normalize();

        Vector3 dir = camRight * moveInput.x + camForward * moveInput.y;
        return dir.sqrMagnitude > 0.001f ? dir.normalized : Vector3.zero;
    }

    void SnapToSurface(Vector3 preferredUp)
    {
        if (_planet == null)
            return;

        float rideHeight = GetRideHeight();
        Vector3 up = preferredUp.sqrMagnitude > 0.001f ? preferredUp.normalized : Vector3.up;

        if (_planet.HasHeightTerrain)
        {
            transform.position = _planet.GetSurfacePoint(up, rideHeight);
            up = _planet.GetTerrainNormal(up);
        }
        else
        {
            Vector3 point = _planet.GetSurfacePoint(up, rideHeight);
            transform.position = point;
            if (Physics.Raycast(point + up * 5f, -up, out RaycastHit hit, 10f, groundLayer, QueryTriggerInteraction.Ignore))
            {
                transform.position = hit.point + hit.normal * rideHeight;
                up = hit.normal;
            }
            else
            {
                up = _planet.GetUpAt(transform.position);
            }
        }

        Vector3 forward = Vector3.ProjectOnPlane(Vector3.forward, up);
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.ProjectOnPlane(Vector3.right, up);
        transform.rotation = Quaternion.LookRotation(forward.normalized, up);
        _fallVelocity = Vector3.zero;
        _grounded = true;
    }

    float GetRideHeight()
    {
        float pivotToFeet = 0f;

        if (_controller != null)
        {
            float half = Mathf.Max(_controller.radius, _controller.height * 0.5f);
            pivotToFeet = Mathf.Max(pivotToFeet, _controller.center.y - half);
        }

        if (_triggerBody != null)
        {
            float half = Mathf.Max(_triggerBody.radius, _triggerBody.height * 0.5f);
            pivotToFeet = Mathf.Max(pivotToFeet, _triggerBody.center.y - half);
        }

        return Mathf.Clamp(pivotToFeet + hoverHeight, 0.02f, 0.25f);
    }

    void UpdateAnimator(float targetSpeed, float inputMagnitude)
    {
        if (_animator == null)
            return;

        _animBlend = Mathf.Lerp(_animBlend, targetSpeed, Time.deltaTime * 10f);
        _animator.SetBool(_animIDGrounded, _grounded);
        _animator.SetBool(_animIDJump, false);
        _animator.SetBool(_animIDFreeFall, !_grounded);
        _animator.SetFloat(_animIDSpeed, _animBlend);
        _animator.SetFloat(_animIDMotionSpeed, Mathf.Max(inputMagnitude, 0.01f) * animationSpeedScale);
    }

}
