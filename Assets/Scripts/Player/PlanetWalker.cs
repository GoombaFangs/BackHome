using UnityEngine;
using StarterAssets;

/// <summary>
/// Walks on a spherical planet (Outer Wilds style).
/// On the ship / flat scenes this stays idle and <see cref="TouchController"/> handles movement.
/// On Planet/Galaxy scenes, sticks to the tile MeshCollider (or planet collider) via raycasts.
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
    [Tooltip("How far above the collider surface to place the character pivot (usually at the feet).")]
    [SerializeField] float footOffset = 0.02f;
    [Tooltip("Extra clearance added above the surface while running. The run animation dips lower than idle/walk, so without this the character visually sinks into the ground/planet while running.")]
    [SerializeField] float runFootHoverBoost = 0.08f;
    [SerializeField] float gravityStrength = 18f;
    [SerializeField] float groundProbeDistance = 12f;
    [SerializeField] LayerMask groundLayer;

    SphericalPlanet _planet;
    PlanetTileMap _tiles;
    StarterAssetsInputs _input;
    CharacterController _controller;
    TouchController _flatMotor;
    Animator _animator;
    Camera _camera;
    PlayerVitals _vitals;
    Rigidbody _body;
    CapsuleCollider _triggerBody;

    Vector3 _fallVelocity;
    float _animBlend;
    bool _grounded;
    bool _ownsControl;
    bool _sceneAllowsPlanetWalk;
    bool _pendingFootResnap;
    float _footDropBelowPivot = -1f;
    float _runHoverBlend;
    Vector3 _routeAnchor;
    bool _hasRouteAnchor;
    bool _routeIgnoresApplied;
    float _stuckSeconds;

    /// <summary>When true, planet locomotion is frozen (crash-land intro plays on this Animator).</summary>
    public bool LockLocomotion { get; set; }

    int _animIDSpeed;
    int _animIDGrounded;
    int _animIDJump;
    int _animIDFreeFall;
    int _animIDMotionSpeed;
    int _animIDMoving;
    int _animIDIdleVariant;

    // Starts true so the very first Update (player not moving yet) rolls the initial idle pick.
    bool _wasMoving = true;

    // Chance that Idle1 (rather than Idle2) is picked whenever the player comes to a stop.
    const float Idle1Chance = 0.8f;

    public bool IsWalkingOnPlanet => _ownsControl && _planet != null;

    /// <summary>Base walk speed scaled by <see cref="PlayerStats.MoveSpeedMultiplier"/>.</summary>
    public float WalkSpeed => walkSpeed * MoveSpeedMultiplier;

    /// <summary>Multiplies walk/run speed. Comes from <see cref="PlayerStats"/> via <see cref="PlayerVitals"/> (1 = normal).</summary>
    float MoveSpeedMultiplier => _vitals != null && _vitals.Stats != null ? _vitals.Stats.MoveSpeedMultiplier : 1f;

    /// <summary>Running animation playback rate. Comes from <see cref="PlayerStats"/> via <see cref="PlayerVitals"/> (1 = normal).</summary>
    float RunningAnimationSpeed => _vitals != null && _vitals.Stats != null ? _vitals.Stats.RunningAnimationSpeed : 1f;

    /// <summary>Intended tangent velocity (units/sec). Drives motion-aware camera framing.</summary>
    public Vector3 PlanarVelocity { get; private set; }

    /// <summary>0..1 move intensity from stick magnitude (run threshold maps near 1).</summary>
    public float MotionAmount { get; private set; }

    void Awake()
    {
        gravityStrength = Mathf.Max(10f, gravityStrength);
        groundProbeDistance = Mathf.Max(4f, groundProbeDistance);

        _input = GetComponent<StarterAssetsInputs>();
        _controller = GetComponent<CharacterController>();
        _flatMotor = GetComponent<TouchController>();
        _animator = GetComponent<Animator>();
        _camera = Camera.main;
        _vitals = GetComponent<PlayerVitals>();

        if (groundLayer.value == 0)
            groundLayer = LayerMask.GetMask("Ground");

        _animIDSpeed = Animator.StringToHash("Speed");
        _animIDGrounded = Animator.StringToHash("Grounded");
        _animIDJump = Animator.StringToHash("Jump");
        _animIDFreeFall = Animator.StringToHash("FreeFall");
        _animIDMotionSpeed = Animator.StringToHash("MotionSpeed");
        _animIDMoving = Animator.StringToHash("Moving");
        _animIDIdleVariant = Animator.StringToHash("IdleVariant");
    }

    void Start()
    {
        _sceneAllowsPlanetWalk = !onlyInPlanetScenes || SceneRoles.IsPlanetScene();
        if (_sceneAllowsPlanetWalk && !LockLocomotion)
            TryStartPlanetWalk();
    }

    /// <summary>Takes planet control (snaps to the surface, disables CharacterController) without
    /// unlocking locomotion. Call before releasing a cinematic lock so TouchController never
    /// Move()s on a disabled collider.</summary>
    public void EnsureWalkingOnPlanet()
    {
        _sceneAllowsPlanetWalk = !onlyInPlanetScenes || SceneRoles.IsPlanetScene();
        if (_sceneAllowsPlanetWalk)
            TryStartPlanetWalk();
    }

    void OnEnable()
    {
        if (_sceneAllowsPlanetWalk && !LockLocomotion)
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

        if (LockLocomotion)
            return;

        if (!_ownsControl || _planet == null)
        {
            TryStartPlanetWalk();
            if (!_ownsControl || _planet == null)
                return;
        }

        if (_pendingFootResnap)
            ResnapIfFootClearanceReady();

        if (LockLocomotion)
            return;

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
        {
            if (_tiles.ProvidesWalkSurface && _tiles.WalkMeshCollider != null
                && _tiles.WalkMeshCollider.sharedMesh == null)
            {
                _tiles.RebuildVisuals();
            }

            _tiles.EnsureWalkColliders();
        }

        _footDropBelowPivot = -1f;
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
        PlanarVelocity = Vector3.zero;
        MotionAmount = 0f;

        if (!_ownsControl)
            return;

        _ownsControl = false;
        ClearRouteCollisionIgnores();
        _planet = null;
        _tiles = null;
        _stuckSeconds = 0f;
        _hasRouteAnchor = false;

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
        _body.collisionDetectionMode = CollisionDetectionMode.Discrete;
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
        ApplyRouteCollisionIgnores();
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
        float speedMultiplier = MoveSpeedMultiplier;
        float targetSpeed = 0f;
        if (inputMagnitude > 0.01f)
        {
            targetSpeed = inputMagnitude >= runInputThreshold
                ? runSpeed
                : walkSpeed * Mathf.Clamp01(inputMagnitude / runInputThreshold);
            targetSpeed *= speedMultiplier;
        }

        Vector3 moveDir = GetTangentMoveDirection(moveInput, up);
        PlanarVelocity = moveDir.sqrMagnitude > 0.001f ? moveDir * targetSpeed : Vector3.zero;
        MotionAmount = inputMagnitude > 0.01f
            ? Mathf.Clamp01(inputMagnitude / runInputThreshold)
            : 0f;

        bool wantsRun = inputMagnitude >= runInputThreshold;
        _runHoverBlend = Mathf.MoveTowards(_runHoverBlend, wantsRun ? 1f : 0f, Time.deltaTime * 4f);

        float step = targetSpeed * Time.deltaTime;
        Vector3 moveDelta = moveDir.sqrMagnitude > 0.001f ? moveDir * step : Vector3.zero;
        float hover = GetPivotClearance(up);
        if (_controller != null && _controller.enabled)
            _controller.enabled = false;
        if (_flatMotor != null && _flatMotor.enabled)
            _flatMotor.enabled = false;
        NyxaraRouteBounds.EnsureRouteWallsCannotTrap(_planet, _tiles);
        if (!_routeIgnoresApplied)
            ApplyRouteCollisionIgnores();
        Vector3 from = transform.position;
        moveDelta = NyxaraRouteBounds.FilterMove(_planet, _tiles, from, moveDelta, hover);
        moveDelta = ResolveObstacleMove(moveDelta, up, includeBorderWalls: !NyxaraRouteBounds.IsActive(_tiles));

        Vector3 probeOrigin = NyxaraRouteBounds.ClampPosition(
            _planet, _tiles, from + moveDelta, hover);
        Vector3 radial = (probeOrigin - _planet.Center).normalized;
        if (radial.sqrMagnitude < 0.0001f)
            radial = up;

        Vector3 next;
        Vector3 surfaceNormal = radial;
        if (TryStickToCollider(radial, out next, out surfaceNormal) &&
            !NyxaraRouteBounds.IsOffRoute(_planet, _tiles, next))
        {
            _grounded = true;
            _fallVelocity = Vector3.zero;
            up = surfaceNormal;
        }
        else
        {
            next = _planet.Center + radial * (GetFallbackSurfaceRadius(radial) + hover);
            up = radial;
            _grounded = true;
            _fallVelocity = Vector3.zero;
        }

        // Safety net: whatever branch produced `next` (mesh raycast or analytic fallback),
        // never let the character render below the known-good analytic floor. This guards
        // against bad/stale collider data (e.g. a prop mid-rebuild) or a big delta-time spike
        // punching the capsule through the mesh — both of which previously left the player
        // stuck under the terrain with no way to recover.
        Vector3 fromCenterFinal = next - _planet.Center;
        float finalRadius = fromCenterFinal.magnitude;
        Vector3 finalUp = finalRadius > 0.0001f ? fromCenterFinal / finalRadius : up;
        float floorRadius = GetFallbackSurfaceRadius(finalUp) + GetPivotClearance(finalUp);
        if (finalRadius < floorRadius - 0.001f)
        {
            next = _planet.Center + finalUp * floorRadius;
            up = finalUp;
            _grounded = true;
            _fallVelocity = Vector3.zero;
        }

        next = NyxaraRouteBounds.ClampPosition(_planet, _tiles, next, GetPivotClearance((next - _planet.Center).normalized));
        next = RecoverOrRememberRoute(next, up);
        next = UnstickIfImmobile(from, next, inputMagnitude, hover);
        fromCenterFinal = next - _planet.Center;
        if (fromCenterFinal.sqrMagnitude > 0.0001f)
            up = fromCenterFinal.normalized;

        Vector3 faceDir = moveDir.sqrMagnitude > 0.001f
            ? Vector3.ProjectOnPlane(moveDir, up)
            : Vector3.ProjectOnPlane(transform.forward, up);
        if (faceDir.sqrMagnitude < 0.001f)
            faceDir = Vector3.ProjectOnPlane(Vector3.forward, up);

        ApplyPose(next, faceDir.normalized, up);
        UpdateAnimator(targetSpeed, inputMagnitude);
    }

    static readonly RaycastHit[] ObstacleHits = new RaycastHit[24];

    /// <summary>
    /// Capsule-cast along tangent move. Border cubes block only when the Nyxara route band
    /// is off. Props still block in both modes. Tile mesh is the floor, not a wall.
    /// </summary>
    Vector3 ResolveObstacleMove(Vector3 desiredDelta, Vector3 up, bool includeBorderWalls)
    {
        if (desiredDelta.sqrMagnitude < 0.0000001f)
            return desiredDelta;

        float scale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z);
        float radius = (_controller != null ? Mathf.Max(0.2f, _controller.radius * 0.9f) : 0.28f) * scale;
        float height = (_controller != null ? Mathf.Max(1.4f, _controller.height) : 1.8f) * scale;
        Vector3 bottom = transform.position + up * (radius + 0.05f);
        Vector3 top = transform.position + up * (height - radius);

        float dist = desiredDelta.magnitude;
        Vector3 dir = desiredDelta / dist;
        float skin = 0.04f;
        Vector3 radial = (transform.position - _planet.Center).normalized;

        if (includeBorderWalls &&
            TryNearestBlockingWall(bottom, top, radius, dir, dist + skin, out RaycastHit wallHit))
            return LimitAndSlideAlongWall(desiredDelta, dir, skin, up, radial, bottom, top, radius, wallHit);

        if (TryNearestBlockingProp(bottom, top, radius, dir, dist + skin, up, radial, out RaycastHit propHit))
            return LimitAndSlideAlongWall(desiredDelta, dir, skin, up, radial, bottom, top, radius, propHit);

        return desiredDelta;
    }

    bool TryNearestBlockingWall(
        Vector3 bottom,
        Vector3 top,
        float radius,
        Vector3 dir,
        float maxDistance,
        out RaycastHit best)
    {
        best = default;
        int count = Physics.CapsuleCastNonAlloc(
            bottom,
            top,
            radius,
            dir,
            ObstacleHits,
            maxDistance,
            groundLayer,
            QueryTriggerInteraction.Ignore);
        if (count <= 0)
            return false;

        float nearest = float.MaxValue;
        bool found = false;
        int n = Mathf.Min(count, ObstacleHits.Length);
        for (int i = 0; i < n; i++)
        {
            RaycastHit hit = ObstacleHits[i];
            if (hit.collider == null || !NyxaraTerrainCollision.IsBlockingWall(hit.collider))
                continue;
            if (NyxaraRouteBounds.ShouldIgnorePhysicsWall(hit.collider, _tiles))
                continue;
            if (!IsPlanetObstacle(hit.collider))
                continue;
            if (hit.distance >= nearest)
                continue;
            nearest = hit.distance;
            best = hit;
            found = true;
        }

        return found;
    }

    bool TryNearestBlockingProp(
        Vector3 bottom,
        Vector3 top,
        float radius,
        Vector3 dir,
        float maxDistance,
        Vector3 up,
        Vector3 radial,
        out RaycastHit best)
    {
        best = default;
        int count = Physics.CapsuleCastNonAlloc(
            bottom,
            top,
            radius,
            dir,
            ObstacleHits,
            maxDistance,
            groundLayer,
            QueryTriggerInteraction.Ignore);
        if (count <= 0)
            return false;

        float nearest = float.MaxValue;
        bool found = false;
        int n = Mathf.Min(count, ObstacleHits.Length);
        for (int i = 0; i < n; i++)
        {
            RaycastHit hit = ObstacleHits[i];
            if (!IsPlanetObstacle(hit.collider))
                continue;
            if (IsWalkableObstacleHit(hit.collider, hit.normal, up, radial))
                continue;
            if (hit.distance >= nearest)
                continue;
            nearest = hit.distance;
            best = hit;
            found = true;
        }

        return found;
    }

    Vector3 LimitAndSlideAlongWall(
        Vector3 desiredDelta,
        Vector3 dir,
        float skin,
        Vector3 up,
        Vector3 radial,
        Vector3 bottom,
        Vector3 top,
        float radius,
        RaycastHit hit)
    {
        float allowed = Mathf.Max(0f, hit.distance - skin);
        Vector3 limited = dir * allowed;
        Vector3 remainder = desiredDelta - limited;
        Vector3 slide = Vector3.ProjectOnPlane(remainder, hit.normal);
        slide = Vector3.ProjectOnPlane(slide, up);

        if (slide.sqrMagnitude < 0.0000001f)
            return limited;

        float slideDist = slide.magnitude;
        Vector3 slideDir = slide / slideDist;
        if (TryNearestBlockingWall(
                bottom + limited,
                top + limited,
                radius,
                slideDir,
                slideDist + skin,
                out RaycastHit slideWall))
        {
            float slideAllowed = Mathf.Max(0f, slideWall.distance - skin);
            return limited + slideDir * slideAllowed;
        }

        if (TryNearestBlockingProp(
                bottom + limited,
                top + limited,
                radius,
                slideDir,
                slideDist + skin,
                up,
                radial,
                out RaycastHit slideHit))
        {
            float slideAllowed = Mathf.Max(0f, slideHit.distance - skin);
            return limited + slideDir * slideAllowed;
        }

        return limited + slide;
    }

    void ApplyRouteCollisionIgnores()
    {
        if (_planet == null)
            return;
        NyxaraRouteBounds.ApplyIgnoreCollisions(gameObject, _planet, _tiles);
        _routeIgnoresApplied = true;
    }

    void ClearRouteCollisionIgnores()
    {
        if (_planet != null)
            NyxaraRouteBounds.ApplyIgnoreCollisions(gameObject, _planet, null);
        _routeIgnoresApplied = false;
    }

    Vector3 RecoverOrRememberRoute(Vector3 next, Vector3 up)
    {
        if (_planet == null || _tiles == null)
            return next;

        float hover = GetPivotClearance((next - _planet.Center).normalized);
        if (NyxaraRouteBounds.TryRecover(
                _planet, _tiles, next, hover, _hasRouteAnchor, _routeAnchor, out Vector3 recovered))
        {
            _grounded = true;
            _fallVelocity = Vector3.zero;
            return recovered;
        }

        GetCapsuleEnds(next, up, out Vector3 bottom, out Vector3 top, out float radius);
        if (NyxaraRouteBounds.TryUnstickFromInvisibleWall(
                _planet, _tiles, next, bottom, top, radius, hover,
                _hasRouteAnchor, _routeAnchor, out recovered))
        {
            _grounded = true;
            _fallVelocity = Vector3.zero;
            return recovered;
        }

        if (NyxaraRouteBounds.IsComfortable(_planet, _tiles, next))
        {
            _routeAnchor = next;
            _hasRouteAnchor = true;
        }

        return next;
    }

    Vector3 UnstickIfImmobile(Vector3 from, Vector3 next, float inputMagnitude, float hover)
    {
        if (inputMagnitude < 0.2f)
        {
            _stuckSeconds = 0f;
            return next;
        }

        Vector3 up = (from - _planet.Center).sqrMagnitude > 0.0001f
            ? (from - _planet.Center).normalized
            : Vector3.up;
        float moved = Vector3.ProjectOnPlane(next - from, up).magnitude;
        float expected = Mathf.Max(0.04f, walkSpeed * MoveSpeedMultiplier * Time.deltaTime);
        if (moved > expected * 0.06f)
        {
            _stuckSeconds = 0f;
            return next;
        }

        _stuckSeconds += Time.deltaTime;
        if (_stuckSeconds < 0.08f)
            return next;

        _stuckSeconds = 0f;
        if (NyxaraRouteBounds.TrySnapToCenter(_planet, _tiles, next, hover, out Vector3 center))
        {
            _grounded = true;
            _fallVelocity = Vector3.zero;
            return center;
        }

        if (_hasRouteAnchor && NyxaraRouteBounds.IsComfortable(_planet, _tiles, _routeAnchor))
        {
            _grounded = true;
            _fallVelocity = Vector3.zero;
            return _routeAnchor;
        }

        return next;
    }

    void GetCapsuleEnds(Vector3 feet, Vector3 up, out Vector3 bottom, out Vector3 top, out float radius)
    {
        float scale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z);
        radius = (_controller != null ? Mathf.Max(0.2f, _controller.radius * 0.9f) : 0.28f) * scale;
        float height = (_controller != null ? Mathf.Max(1.4f, _controller.height) : 1.8f) * scale;
        bottom = feet + up * (radius + 0.05f);
        top = feet + up * (height - radius);
    }

    static bool IsWalkableObstacleHit(Collider col, Vector3 normal, Vector3 up, Vector3 radial)
    {
        if (NyxaraTerrainCollision.IsBlockingWall(col))
            return false;

        if (normal.sqrMagnitude < 0.001f)
            return false;

        normal.Normalize();
        if (Vector3.Dot(normal, up) > 0.4f)
            return true;

        // Outward-facing prop slope (common when stepping onto a rock on the sphere).
        return Vector3.Dot(normal, radial) > 0.2f;
    }

    bool IsPlanetObstacle(Collider col)
    {
        if (col == null || _planet == null)
            return false;

        if (col.transform == _planet.transform)
            return false;

        if (!col.transform.IsChildOf(_planet.transform))
            return false;

        if (_tiles != null && _tiles.IsWalkSurfaceCollider(col))
            return false;

        if (NyxaraRouteBounds.IsInvisibleTrap(col, _tiles) ||
            NyxaraRouteBounds.ShouldIgnorePhysicsWall(col, _tiles))
            return false;

        return !IsNonBlockingPropCollider(col);
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

        feetPosition = hit.point + normal * GetPivotClearance(normal);
        return true;
    }

    void ResnapIfFootClearanceReady()
    {
        if (_planet == null)
            return;

        float previous = _footDropBelowPivot;
        _footDropBelowPivot = -1f;
        float next = GetFootDropBelowPivot();
        if (previous >= 0f && Mathf.Abs(next - previous) < 0.02f)
        {
            _pendingFootResnap = false;
            return;
        }

        Vector3 up = transform.position - _planet.Center;
        if (up.sqrMagnitude < 0.0001f)
            up = Vector3.up;
        SnapToSurface(up);
        _pendingFootResnap = false;
    }

    float GetPivotClearance(Vector3 up)
    {
        float scale = Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z);
        float clearance = Mathf.Max(footOffset, 0.02f * scale);

        if (_controller != null)
        {
            // Keep the capsule bottom above the surface when the pivot sits at the feet.
            float soleOffset = (_controller.center.y - _controller.height * 0.5f) * scale;
            clearance = Mathf.Max(clearance, soleOffset + 0.04f * scale);
        }

        float footDrop = GetFootDropBelowPivot() * scale;
        if (footDrop > clearance + 0.02f)
            _pendingFootResnap = true;

        // The run animation's foot bob dips lower than idle/walk, but foot drop above is sampled
        // once (not re-measured every frame of the cycle), so add extra headroom while running
        // to keep the character from visually sinking into the ground/planet.
        float runBoost = runFootHoverBoost * _runHoverBlend * scale;

        return Mathf.Min(2f, Mathf.Max(clearance, footDrop + footOffset * scale + runBoost, 0.02f));
    }

    float GetFootDropBelowPivot()
    {
        if (_footDropBelowPivot >= 0f)
            return _footDropBelowPivot;

        float below = 0f;
        if (_animator != null && _animator.isHuman)
        {
            if (!_animator.isInitialized)
                _animator.Update(0f);

            below = Mathf.Max(below, GetLocalFootDrop(_animator.GetBoneTransform(HumanBodyBones.LeftFoot)));
            below = Mathf.Max(below, GetLocalFootDrop(_animator.GetBoneTransform(HumanBodyBones.RightFoot)));
        }

        if (below < 0.001f)
            below = GetRendererDropBelowPivot();

        if (below < 0.001f && _controller != null)
            below = Mathf.Max(0f, _controller.height * 0.5f - _controller.center.y);

        _footDropBelowPivot = below;
        return _footDropBelowPivot;
    }

    float GetLocalFootDrop(Transform foot)
    {
        if (foot == null)
            return 0f;

        return Mathf.Max(0f, -transform.InverseTransformPoint(foot.position).y);
    }

    float GetRendererDropBelowPivot()
    {
        float below = 0f;
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;

            Vector3 localMin = transform.InverseTransformPoint(renderer.bounds.min);
            below = Mathf.Max(below, -localMin.y);
        }

        return below;
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

        if (_tiles != null && _planet != null)
            return _tiles.TryPickWalkSurfaceHit(hits, _planet.Center, direction, out best);

        Vector3 radial = direction.sqrMagnitude > 0.001f ? -direction.normalized : Vector3.up;
        float minAcceptableRadius = GetFallbackSurfaceRadius(radial) - 0.05f;
        float bestRadius = -1f;
        bool found = false;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i].collider;
            if (col == null || _planet == null)
                continue;
            if (col.GetComponent<SphericalPlanet>() == null)
                continue;

            Vector3 normal = hits[i].normal.sqrMagnitude > 0.001f
                ? hits[i].normal.normalized
                : radial;
            if (Vector3.Dot(normal, radial) < 0.35f)
                continue;

            float surfaceRadius = (hits[i].point - _planet.Center).magnitude;
            if (surfaceRadius < minAcceptableRadius)
                continue;

            if (surfaceRadius > bestRadius)
            {
                bestRadius = surfaceRadius;
                best = hits[i];
                found = true;
            }
        }

        return found;
    }

    static bool IsNonBlockingPropCollider(Collider col)
    {
        if (col == null)
            return true;

        Transform t = col.transform;
        while (t != null)
        {
            if (t.name.IndexOf("Grass", System.StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
            if (t.GetComponent<SphericalPlanet>() != null)
                break;
            t = t.parent;
        }

        return false;
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
            point = _planet.Center + up * (GetFallbackSurfaceRadius(up) + GetPivotClearance(up));
            normal = up;
        }

        // Same floor safety net as TickPlanetWalk — never spawn/snap under the analytic surface.
        Vector3 fromCenter = point - _planet.Center;
        float radius = fromCenter.magnitude;
        Vector3 radialUp = radius > 0.0001f ? fromCenter / radius : normal;
        float floorRadius = GetFallbackSurfaceRadius(radialUp) + GetPivotClearance(radialUp);
        if (radius < floorRadius - 0.001f)
        {
            point = _planet.Center + radialUp * floorRadius;
            normal = radialUp;
        }

        point = NyxaraRouteBounds.ClampPosition(_planet, _tiles, point, GetPivotClearance(radialUp));
        point = RecoverOrRememberRoute(point, radialUp);
        fromCenter = point - _planet.Center;
        if (fromCenter.sqrMagnitude > 0.0001f)
            normal = fromCenter.normalized;

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
        _animator.SetFloat(_animIDMotionSpeed, Mathf.Max(inputMagnitude, 0.01f) * RunningAnimationSpeed);

        // Every time the character comes to a stop, re-roll which idle animation starts first
        // (80% Idle1 / 20% Idle2). The AnimatorController then alternates Idle1/Idle2 on its own
        // every 4 loops.
        bool moving = inputMagnitude > 0.05f;
        if (!moving && _wasMoving)
            _animator.SetInteger(_animIDIdleVariant, Random.value < Idle1Chance ? 0 : 1);
        _wasMoving = moving;
        _animator.SetBool(_animIDMoving, moving);
    }
}
