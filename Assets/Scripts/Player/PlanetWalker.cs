using System;
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
    bool _pendingFootResnap;
    float _footDropBelowPivot = -1f;

    int _animIDSpeed;
    int _animIDGrounded;
    int _animIDJump;
    int _animIDFreeFall;
    int _animIDMotionSpeed;

    public bool IsWalkingOnPlanet => _ownsControl && _planet != null;

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

        if (_pendingFootResnap)
            ResnapIfFootClearanceReady();

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
        PlanarVelocity = moveDir.sqrMagnitude > 0.001f ? moveDir * targetSpeed : Vector3.zero;
        MotionAmount = inputMagnitude > 0.01f
            ? Mathf.Clamp01(inputMagnitude / runInputThreshold)
            : 0f;

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

            float minDist = GetFallbackSurfaceRadius(radial) + GetPivotClearance(radial);
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

        Vector3 faceDir = moveDir.sqrMagnitude > 0.001f
            ? Vector3.ProjectOnPlane(moveDir, up)
            : Vector3.ProjectOnPlane(transform.forward, up);
        if (faceDir.sqrMagnitude < 0.001f)
            faceDir = Vector3.ProjectOnPlane(Vector3.forward, up);

        ApplyPose(next, faceDir.normalized, up);
        UpdateAnimator(targetSpeed, inputMagnitude);
    }

    /// <summary>
    /// Capsule-cast along tangent move. Steep walls block; outward slopes / walkable tops are allowed
    /// so the player can step onto rocks and props (height comes from radial snap).
    /// </summary>
    Vector3 ResolveObstacleMove(Vector3 desiredDelta, Vector3 up)
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

        if (!IsPlanetObstacle(hit.collider))
            return desiredDelta;

        // Floor or outward-facing slope — walk onto it; radial snap sets height.
        if (IsWalkableObstacleHit(hit.normal, up, radial))
            return desiredDelta;

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
                QueryTriggerInteraction.Ignore)
            && IsPlanetObstacle(slideHit.collider)
            && !IsWalkableObstacleHit(slideHit.normal, up, radial))
        {
            float slideAllowed = Mathf.Max(0f, slideHit.distance - skin);
            return limited + slideDir * slideAllowed;
        }

        return limited + slide;
    }

    static bool IsWalkableObstacleHit(Vector3 normal, Vector3 up, Vector3 radial)
    {
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

        return Mathf.Max(clearance, footDrop + footOffset * scale, 0.02f);
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
            if (renderer == null || !renderer.enabled)
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

        Vector3 radial = direction.sqrMagnitude > 0.001f ? -direction.normalized : Vector3.up;
        MeshCollider tileCollider = _tiles != null ? _tiles.WalkMeshCollider : null;
        bool tilesProvideWalkSurface = _tiles != null && _tiles.ProvidesWalkSurface;
        float minAcceptableRadius = GetFallbackSurfaceRadius(radial) - 0.05f;

        float bestRadius = -1f;
        bool found = false;
        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i].collider;
            if (col == null || _planet == null)
                continue;

            if (!IsWalkSurfaceCollider(col, tileCollider, tilesProvideWalkSurface))
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

    static bool IsWalkSurfaceCollider(Collider col, MeshCollider tileCollider, bool tilesProvideWalkSurface)
    {
        if (col == null)
            return false;

        if (tilesProvideWalkSurface && tileCollider != null && tileCollider.enabled)
            return col == tileCollider;

        if (col is SphereCollider && col.GetComponent<SphericalPlanet>() != null)
            return !tilesProvideWalkSurface || tileCollider == null || !tileCollider.enabled;

        return false;
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
