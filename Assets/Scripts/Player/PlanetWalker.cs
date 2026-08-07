using UnityEngine;
using StarterAssets;

/// <summary>
/// Walks on a spherical planet (Outer Wilds style).
/// On the ship / flat scenes this stays idle and <see cref="TouchController"/> handles movement.
/// On Planet/Galaxy scenes, sticks to the tile MeshCollider (or planet collider) via raycasts.
/// </summary>
[DefaultExecutionOrder(20)] // PINK_FIX
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
    [Tooltip("How far above the collider surface to place the character pivot (usually at the feet).")]
    [SerializeField] float footOffset = 0.02f;
    [SerializeField] float gravityStrength = 18f;
    [SerializeField] float groundProbeDistance = 12f;
    [SerializeField] LayerMask groundLayer;

    [Header("Feel")]
    [SerializeField] float animationSpeedScale = 1.15f;

    SphericalPlanet _planet;
    PlanetTileMap _tiles;
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
        footOffset = Mathf.Clamp(footOffset, 0.001f, 0.2f);
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
            : FindAnyObjectByType<SphericalPlanet>();

        if (planet == null)
            return;

        _planet = planet;
        _tiles = planet.GetComponent<PlanetTileMap>();
        if (_tiles != null)
            _tiles.EnsureWalkColliders();

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
        _tiles = null;

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

        // Trigger only — used for gates / sensors, not for standing on the planet.
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

        _input.jump = false;
        _input.sprint = false;
        _input.look = Vector2.zero;

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
        float step = targetSpeed * Time.deltaTime;
        Vector3 moveDelta = moveDir.sqrMagnitude > 0.001f ? moveDir * step : Vector3.zero;
        moveDelta = ResolveObstacleMove(moveDelta, up);

        Vector3 probeOrigin = transform.position + moveDelta;

        Vector3 radial = (probeOrigin - _planet.Center).normalized;
        if (TryStickToCollider(radial, out Vector3 next, out Vector3 surfaceNormal))
        {
            _grounded = true;
            _fallVelocity = Vector3.zero;
            up = surfaceNormal;
        }
        else
        {
            // No collider hit — fall toward planet and clamp to analytic surface.
            _grounded = false;
            Vector3 toCenter = (_planet.Center - transform.position).normalized;
            _fallVelocity += toCenter * (gravityStrength * Time.deltaTime);
            next = transform.position + moveDelta + _fallVelocity * Time.deltaTime;

            float minDist = GetFallbackSurfaceRadius(radial) + footOffset;
            Vector3 fromCenter = next - _planet.Center;
            if (fromCenter.magnitude < minDist)
            {
                next = _planet.Center + fromCenter.normalized * minDist;
                _fallVelocity = Vector3.zero;
                _grounded = true;
                up = fromCenter.normalized;
            }
            else
            {
                up = _planet.GetUpAt(next);
            }
        }

        Vector3 faceDir = moveDir.sqrMagnitude > 0.001f
            ? Vector3.ProjectOnPlane(moveDir, up)
            : Vector3.ProjectOnPlane(transform.forward, up);
        if (faceDir.sqrMagnitude < 0.001f)
            faceDir = Vector3.ProjectOnPlane(Vector3.forward, up);

        ApplyPose(next, faceDir.normalized, up);
        UpdateAnimator(targetSpeed, inputMagnitude);
    }

    /// <summary>
    /// Capsule-cast along the tangent move so rocks/props on Ground stop the player.
    /// Floor-like hits (normal aligned with planet up) are ignored so we can still walk onto props.
    /// </summary>
    Vector3 ResolveObstacleMove(Vector3 desiredDelta, Vector3 up)
    {
        if (desiredDelta.sqrMagnitude < 0.0000001f)
            return desiredDelta;

        float radius = _controller != null ? Mathf.Max(0.2f, _controller.radius * 0.9f) : 0.28f;
        float height = _controller != null ? Mathf.Max(1.4f, _controller.height) : 1.8f;
        Vector3 bottom = transform.position + up * (radius + 0.05f);
        Vector3 top = transform.position + up * (height - radius);

        float dist = desiredDelta.magnitude;
        Vector3 dir = desiredDelta / dist;
        float skin = 0.04f;

        if (!Physics.CapsuleCast(
                bottom,
                top,
                radius,
                dir,
                out RaycastHit hit,
                dist + skin,
                groundLayer,
                QueryTriggerInteraction.Ignore))
        {
            return desiredDelta;
        }

        // Standing surface under / ahead — allow (will snap onto it via radial stick).
        if (Vector3.Dot(hit.normal, up) > 0.45f)
            return desiredDelta;

        // Only block planet props / planet children, not unrelated scene geometry.
        if (_planet != null
            && hit.collider != null
            && hit.collider.transform != _planet.transform
            && !hit.collider.transform.IsChildOf(_planet.transform))
        {
            return desiredDelta;
        }

        // Hard stop just before the wall, then try a single slide.
        float allowed = Mathf.Max(0f, hit.distance - skin);
        Vector3 limited = dir * allowed;
        Vector3 remainder = desiredDelta - limited;
        Vector3 slide = Vector3.ProjectOnPlane(remainder, hit.normal);
        slide = Vector3.ProjectOnPlane(slide, up);

        if (slide.sqrMagnitude < 0.0000001f)
            return limited;

        float slideDist = slide.magnitude;
        Vector3 slideDir = slide / slideDist;
        if (Physics.CapsuleCast(
                bottom + limited,
                top + limited,
                radius,
                slideDir,
                out RaycastHit slideHit,
                slideDist + skin,
                groundLayer,
                QueryTriggerInteraction.Ignore))
        {
            if (Vector3.Dot(slideHit.normal, up) <= 0.45f)
            {
                float slideAllowed = Mathf.Max(0f, slideHit.distance - skin);
                return limited + slideDir * slideAllowed;
            }
        }

        return limited + slide;
    }

    bool TryStickToCollider(Vector3 radial, out Vector3 feetPosition, out Vector3 normal)
    {
        feetPosition = default;
        normal = radial;

        // Cast from outside the surface inward so we hit the outer tile mesh.
        float castStart = GetFallbackSurfaceRadius(radial) + Mathf.Max(4f, groundProbeDistance * 0.5f);
        Vector3 origin = _planet.Center + radial * castStart;
        float maxDist = castStart + 2f;

        if (!TryRaycastPlanetGround(origin, -radial, maxDist, out RaycastHit hit))
            return false;

        normal = hit.normal.sqrMagnitude > 0.001f ? hit.normal.normalized : radial;
        // Keep normal roughly outward so the character doesn't flip under the mesh.
        if (Vector3.Dot(normal, radial) < 0f)
            normal = -normal;

        feetPosition = hit.point + normal * footOffset;
        return true;
    }

    bool TryRaycastPlanetGround(Vector3 origin, Vector3 direction, float maxDistance, out RaycastHit best)
    {
        best = default;
        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            direction,
            maxDistance,
            groundLayer,
            QueryTriggerInteraction.Ignore);

        if (hits == null || hits.Length == 0)
            return false;

        float bestDist = float.MaxValue;
        bool found = false;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i].collider;
            if (col == null)
                continue;

            // Prefer tile mesh / planet colliders; ignore the player capsule etc.
            if (_planet != null
                && (col.transform == _planet.transform || col.transform.IsChildOf(_planet.transform)))
            {
                if (hits[i].distance < bestDist)
                {
                    bestDist = hits[i].distance;
                    best = hits[i];
                    found = true;
                }
            }
        }

        return found;
    }

    float GetFallbackSurfaceRadius(Vector3 radial)
    {
        if (_tiles != null && _tiles.ProvidesWalkSurface)
            return _tiles.GetWalkSurfaceRadius(radial);
        return _planet.GetTerrainRadius(radial);
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

        Vector3 up = preferredUp.sqrMagnitude > 0.001f ? preferredUp.normalized : Vector3.up;
        if (_tiles != null)
            _tiles.EnsureWalkColliders();

        if (!TryStickToCollider(up, out Vector3 point, out Vector3 normal))
        {
            point = _planet.Center + up * (GetFallbackSurfaceRadius(up) + footOffset);
            normal = up;
        }

        transform.position = point;
        up = normal;

        Vector3 forward = Vector3.ProjectOnPlane(Vector3.forward, up);
        if (forward.sqrMagnitude < 0.001f)
            forward = Vector3.ProjectOnPlane(Vector3.right, up);
        transform.rotation = Quaternion.LookRotation(forward.normalized, up);
        _fallVelocity = Vector3.zero;
        _grounded = true;

        if (_body != null)
        {
            _body.position = transform.position;
            _body.rotation = transform.rotation;
        }
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
