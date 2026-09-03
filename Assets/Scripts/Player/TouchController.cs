using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using StarterAssets;

/// <summary>
/// Top-down walk/run for Robot Kyle.
/// CharacterController + StarterAssetsInputs (WASD / mobile move joystick only).
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(StarterAssetsInputs))]
public class TouchController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] float walkSpeed = 2f;
    [SerializeField] float runSpeed = 5.5f;
    [SerializeField] float rotationSmoothTime = 0.12f;
    [SerializeField] float speedChangeRate = 10f;
    [SerializeField] float gravity = -15f;
    [SerializeField] float stopDistance = 0.25f;
    [SerializeField] float runInputThreshold = 0.7f;

    [Header("Ground")]
    [SerializeField] LayerMask groundLayer;
    [SerializeField] float groundedOffset = -0.14f;
    [SerializeField] float groundedRadius = 0.28f;

    [Header("Click / Tap To Move")]
    [Tooltip("Off by default when using the floating on-screen joystick.")]
    [SerializeField] bool enableClickToMove = false;

    CharacterController _controller;
    StarterAssetsInputs _input;
    Animator _animator;
    Camera _camera;
    PlayerVitals _vitals;

    float _speed;
    float _animationBlend;
    float _targetRotation;
    float _rotationVelocity;
    float _verticalVelocity;
    bool _grounded;
    bool _hasAnimator;

    Vector3 _clickTarget;
    bool _hasClickTarget;

    /// <summary>Base walk speed scaled by <see cref="PlayerStats.MoveSpeedMultiplier"/>.</summary>
    public float WalkSpeed => walkSpeed * MoveSpeedMultiplier;

    /// <summary>Multiplies walk/run speed. Comes from <see cref="PlayerStats"/> via <see cref="PlayerVitals"/> (1 = normal).</summary>
    float MoveSpeedMultiplier => _vitals != null && _vitals.Stats != null ? _vitals.Stats.MoveSpeedMultiplier : 1f;

    /// <summary>0..1 move intensity from stick magnitude, clamped to 1 at the run threshold (same
    /// semantics as <see cref="PlanetWalker.MotionAmount"/>) - lets other systems (e.g. DustTrailVfx)
    /// detect "is running" without duplicating runInputThreshold.</summary>
    public float MotionAmount { get; private set; }

    int _animIDSpeed;
    int _animIDGrounded;
    int _animIDJump;
    int _animIDFreeFall;
    int _animIDMotionSpeed;
    int _animIDMoving;
    int _animIDIdleVariant;

    // Starts true so the very first Move (player not moving yet) rolls the initial idle pick.
    bool _wasMoving = true;

    // Chance that Idle1 (rather than Idle2) is picked whenever the player comes to a stop.
    const float Idle1Chance = 0.8f;

    void Reset()
    {
        groundLayer = LayerMask.GetMask("Ground");
    }

    void Awake()
    {
        _controller = GetComponent<CharacterController>();
        _input = GetComponent<StarterAssetsInputs>();
        _hasAnimator = TryGetComponent(out _animator);
        _camera = Camera.main;
        _vitals = GetComponent<PlayerVitals>();

        if (groundLayer.value == 0)
            groundLayer = LayerMask.GetMask("Ground");

        _input.cursorLocked = false;
        _input.cursorInputForLook = false;
        _input.analogMovement = true;
        _input.jump = false;
        _input.sprint = false;
        _input.look = Vector2.zero;

        // Sci-fi kit ramps are ~45° with a lip at the base — default 45/0.25 often blocks.
        _controller.slopeLimit = Mathf.Max(_controller.slopeLimit, 60f);
        _controller.stepOffset = Mathf.Max(_controller.stepOffset, 0.5f);
        if (_controller.skinWidth < 0.08f)
            _controller.skinWidth = 0.08f;

        AssignAnimationIDs();
    }

    void AssignAnimationIDs()
    {
        _animIDSpeed = Animator.StringToHash("Speed");
        _animIDGrounded = Animator.StringToHash("Grounded");
        _animIDJump = Animator.StringToHash("Jump");
        _animIDFreeFall = Animator.StringToHash("FreeFall");
        _animIDMotionSpeed = Animator.StringToHash("MotionSpeed");
        _animIDMoving = Animator.StringToHash("Moving");
        _animIDIdleVariant = Animator.StringToHash("IdleVariant");
    }

    void Update()
    {
        PlanetWalker walker = GetComponent<PlanetWalker>();
        if (walker != null && walker.IsWalkingOnPlanet)
            return;

        _hasAnimator = TryGetComponent(out _animator);
        if (_camera == null)
            _camera = Camera.main;

        // Walk/run only — ignore leftover Starter Assets actions.
        _input.jump = false;
        _input.sprint = false;
        _input.look = Vector2.zero;

        UpdateClickToMove();
        GroundedCheck();
        ApplyGravity();
        Move();
    }

    void UpdateClickToMove()
    {
        if (!enableClickToMove)
            return;

        if (_input.move.sqrMagnitude > 0.01f)
        {
            _hasClickTarget = false;
            return;
        }

        if (!TryGetPointerDown(out Vector2 screenPos))
            return;

        if (IsPointerOverUI())
            return;

        if (TryGetGroundPoint(screenPos, out Vector3 worldPoint))
        {
            _clickTarget = worldPoint;
            _hasClickTarget = true;
        }
    }

    void GroundedCheck()
    {
        Vector3 spherePosition = new Vector3(
            transform.position.x,
            transform.position.y - groundedOffset,
            transform.position.z);

        _grounded = Physics.CheckSphere(
            spherePosition,
            groundedRadius,
            groundLayer,
            QueryTriggerInteraction.Ignore);

        if (_hasAnimator)
        {
            _animator.SetBool(_animIDGrounded, _grounded);
            _animator.SetBool(_animIDJump, false);
        }
    }

    void ApplyGravity()
    {
        if (_grounded && _verticalVelocity < 0f)
            _verticalVelocity = -2f;

        _verticalVelocity += gravity * Time.deltaTime;

        if (_hasAnimator)
            _animator.SetBool(_animIDFreeFall, !_grounded && _verticalVelocity < 0f);
    }

    void Move()
    {
        Vector2 moveInput = _input.move;
        float inputMagnitude = Mathf.Clamp01(moveInput.magnitude);

        if (moveInput.sqrMagnitude < 0.01f && _hasClickTarget)
        {
            Vector3 toTarget = _clickTarget - transform.position;
            toTarget.y = 0f;
            float distance = toTarget.magnitude;

            if (distance <= stopDistance)
            {
                _hasClickTarget = false;
                moveInput = Vector2.zero;
                inputMagnitude = 0f;
            }
            else
            {
                Vector3 camForward = GetCameraFlatForward();
                Vector3 camRight = GetCameraFlatRight();
                Vector3 dir = toTarget / distance;
                moveInput = new Vector2(Vector3.Dot(dir, camRight), Vector3.Dot(dir, camForward));
                if (moveInput.sqrMagnitude > 1f)
                    moveInput.Normalize();
                inputMagnitude = 1f;
            }
        }

        MotionAmount = inputMagnitude > 0.01f ? Mathf.Clamp01(inputMagnitude / runInputThreshold) : 0f;

        // Soft push = walk, hard push = run. No sprint button.
        float speedMultiplier = MoveSpeedMultiplier;
        float targetSpeed = 0f;
        if (inputMagnitude > 0.01f)
        {
            targetSpeed = inputMagnitude >= runInputThreshold
                ? runSpeed
                : Mathf.Lerp(walkSpeed * 0.5f, walkSpeed, inputMagnitude / runInputThreshold);
            targetSpeed *= speedMultiplier;
        }

        float currentHorizontalSpeed = new Vector3(_controller.velocity.x, 0f, _controller.velocity.z).magnitude;

        if (Mathf.Abs(currentHorizontalSpeed - targetSpeed) > 0.1f)
        {
            _speed = Mathf.Lerp(currentHorizontalSpeed, targetSpeed, Time.deltaTime * speedChangeRate);
            _speed = Mathf.Round(_speed * 1000f) / 1000f;
        }
        else
        {
            _speed = targetSpeed;
        }

        _animationBlend = Mathf.Lerp(_animationBlend, targetSpeed, Time.deltaTime * speedChangeRate);
        if (_animationBlend < 0.01f)
            _animationBlend = 0f;

        Vector3 inputDirection = new Vector3(moveInput.x, 0f, moveInput.y).normalized;

        if (moveInput.sqrMagnitude >= 0.01f)
        {
            _targetRotation = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg
                              + (_camera != null ? _camera.transform.eulerAngles.y : 0f);

            float rotation = Mathf.SmoothDampAngle(
                transform.eulerAngles.y,
                _targetRotation,
                ref _rotationVelocity,
                rotationSmoothTime);

            transform.rotation = Quaternion.Euler(0f, rotation, 0f);
        }

        Vector3 targetDirection = Quaternion.Euler(0f, _targetRotation, 0f) * Vector3.forward;
        _controller.Move(
            targetDirection.normalized * (_speed * Time.deltaTime)
            + new Vector3(0f, _verticalVelocity, 0f) * Time.deltaTime);

        if (_hasAnimator)
        {
            _animator.SetFloat(_animIDSpeed, _animationBlend);
            _animator.SetFloat(_animIDMotionSpeed, Mathf.Max(inputMagnitude, 0.01f));

            // Every time the character comes to a stop, re-roll which idle animation starts first
            // (80% Idle1 / 20% Idle2). The AnimatorController then alternates Idle1/Idle2 on its
            // own every 4 loops.
            bool moving = inputMagnitude > 0.05f;
            if (!moving && _wasMoving)
                _animator.SetInteger(_animIDIdleVariant, Random.value < Idle1Chance ? 0 : 1);
            _wasMoving = moving;
            _animator.SetBool(_animIDMoving, moving);
        }
    }

    // Animation events on Starter Assets clips — keep quiet receivers.
    void OnFootstep(AnimationEvent animationEvent) { }

    void OnLand(AnimationEvent animationEvent) { }

    Vector3 GetCameraFlatForward()
    {
        if (_camera == null)
            return Vector3.forward;

        Vector3 f = _camera.transform.forward;
        f.y = 0f;
        return f.sqrMagnitude > 0.001f ? f.normalized : Vector3.forward;
    }

    Vector3 GetCameraFlatRight()
    {
        if (_camera == null)
            return Vector3.right;

        Vector3 r = _camera.transform.right;
        r.y = 0f;
        return r.sqrMagnitude > 0.001f ? r.normalized : Vector3.right;
    }

    static bool TryGetPointerDown(out Vector2 screenPos)
    {
        screenPos = default;

        Touchscreen touchscreen = Touchscreen.current;
        if (touchscreen != null && touchscreen.primaryTouch.press.wasPressedThisFrame)
        {
            screenPos = touchscreen.primaryTouch.position.ReadValue();
            return true;
        }

        Mouse mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            screenPos = mouse.position.ReadValue();
            return true;
        }

        return false;
    }

    static bool IsPointerOverUI()
    {
        if (EventSystem.current == null)
            return false;

        if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.isPressed)
            return EventSystem.current.IsPointerOverGameObject(Touchscreen.current.primaryTouch.touchId.ReadValue());

        return EventSystem.current.IsPointerOverGameObject();
    }

    bool TryGetGroundPoint(Vector2 screenPos, out Vector3 worldPoint)
    {
        worldPoint = default;
        if (_camera == null)
            return false;

        Ray ray = _camera.ScreenPointToRay(screenPos);
        RaycastHit[] hits = Physics.RaycastAll(ray, 500f, groundLayer, QueryTriggerInteraction.Ignore);
        if (hits.Length == 0)
            return false;

        float playerY = transform.position.y;
        float bestScore = float.MaxValue;
        RaycastHit best = hits[0];

        for (int i = 0; i < hits.Length; i++)
        {
            float heightDelta = Mathf.Abs(hits[i].point.y - playerY);
            float score = heightDelta * 8f + hits[i].distance;
            if (score < bestScore)
            {
                bestScore = score;
                best = hits[i];
            }
        }

        worldPoint = best.point;
        return true;
    }
}
