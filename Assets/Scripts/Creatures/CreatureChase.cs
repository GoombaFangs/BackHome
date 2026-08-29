using UnityEngine;

/// <summary>
/// Chases the player when they enter this creature's vision range.
/// After <see cref="loseVisionDelay"/> seconds outside vision, loses aggro and returns home.
/// Stops inside attack range so <see cref="CreatureRangeCombat"/> can deal damage.
/// Walks on the spherical planet surface (same stick model as spawn / PlanetWalker).
/// </summary>
[RequireComponent(typeof(Creature))]
public class CreatureChase : MonoBehaviour
{
    enum State
    {
        Idle,
        Aggroed,
        Returning
    }

    [Header("Movement")]
    [SerializeField, Min(0.1f)] float moveSpeed = 4f;
    [SerializeField, Min(0.1f)] float alignSpeed = 10f;
    [SerializeField, Min(0.001f)] float footOffset = 0.05f;
    [SerializeField, Min(1f)] float groundProbeDistance = 12f;
    [SerializeField] LayerMask groundLayer;

    [Header("Vision")]
    [Tooltip("Seconds the player must stay outside vision range before this creature loses aggro and returns home.")]
    [SerializeField, Min(0f)] float loseVisionDelay = 3f;
    [SerializeField, Min(0.05f)] float homeArriveDistance = 0.35f;

    [Header("Hit reaction")]
    [Tooltip("Surface distance shoved away from the attacker. Keep small — a punch, not a launch.")]
    [SerializeField, Min(0f)] float knockbackDistance = 0.2f;
    [Tooltip("Seconds for the shove to ease out. Short = snappy.")]
    [SerializeField, Min(0.05f)] float knockbackDuration = 0.16f;
    [Tooltip("Minimum seconds between knockbacks on this creature.")]
    [SerializeField, Min(0f)] float knockbackCooldown = 0.5f;

    Creature _creature;
    CreatureAnimator _anim;
    PlayerVitals _player;
    SphericalPlanet _planet;
    PlanetTileMap _tiles;

    State _state = State.Idle;
    Vector3 _homePosition;
    Quaternion _homeRotation;
    float _outOfVisionTime;
    bool _homeCaptured;

    bool _knockActive;
    float _knockElapsed;
    float _knockDuration;
    float _knockDistance;
    Vector3 _knockDir;
    Vector3 _knockFaceTarget;
    float _nextKnockbackTime;
    Vector3 _velocity;

    /// <summary>True while actively chasing / fighting the player.</summary>
    public bool IsAggroed => _state == State.Aggroed;

    /// <summary>True during the short hit-shove after taking damage.</summary>
    public bool IsKnockedBack => _knockActive;

    /// <summary>Current surface movement (direction * speed), zero while stopped/attacking/knocked back — used to lead moving targets for slow or telegraphed attacks.</summary>
    public Vector3 Velocity => _velocity;

    void Awake()
    {
        _creature = GetComponent<Creature>();
        _anim = GetComponent<CreatureAnimator>();
        if (_anim == null)
            _anim = GetComponentInChildren<CreatureAnimator>();

        if (groundLayer.value == 0)
            groundLayer = LayerMask.GetMask("Ground");
    }

    void Start()
    {
        CaptureHome();
    }

    void Update()
    {
        if (_creature == null)
        {
            _velocity = Vector3.zero;
            _anim?.ResetToIdle();
            return;
        }

        if (!_creature.IsAlive)
        {
            _velocity = Vector3.zero;
            _anim?.ResetToIdle();
            return;
        }

        if (!_homeCaptured)
            CaptureHome();

        if (!TryResolvePlanet())
        {
            _velocity = Vector3.zero;
            _anim?.SetMoving(false);
            return;
        }

        if (_knockActive)
        {
            _velocity = Vector3.zero;
            _anim?.SetMoving(false);
            return;
        }

        // Default to stationary; MoveToward() re-sets this below on the frames it actually runs.
        _velocity = Vector3.zero;

        switch (_state)
        {
            case State.Idle:
                TickIdle();
                break;
            case State.Aggroed:
                TickAggroed();
                break;
            case State.Returning:
                TickReturning();
                break;
        }
    }

    void LateUpdate()
    {
        if (_knockActive)
            TickKnockback();
    }

    /// <summary>
    /// Short surface shove away from <paramref name="fromWorldPosition"/>.
    /// Ignored while this creature's knockback cooldown is active.
    /// </summary>
    public void ApplyKnockback(Vector3 fromWorldPosition)
    {
        if (_state != State.Aggroed)
            EnterAggro();

        if (knockbackDistance <= 0f || knockbackDuration <= 0f)
            return;

        if (Time.time < _nextKnockbackTime)
            return;

        if (!TryResolvePlanet())
            return;

        Vector3 up = _planet.GetUpAt(transform.position);
        Vector3 away = Vector3.ProjectOnPlane(transform.position - fromWorldPosition, up);
        if (away.sqrMagnitude < 0.0001f)
            away = Vector3.ProjectOnPlane(-transform.forward, up);
        if (away.sqrMagnitude < 0.0001f)
            return;

        _knockDir = away.normalized;
        _knockFaceTarget = fromWorldPosition;
        _knockDistance = knockbackDistance;
        _knockDuration = knockbackDuration;
        _knockElapsed = 0f;
        _knockActive = true;
        _nextKnockbackTime = Time.time + knockbackCooldown;
    }

    void TickIdle()
    {
        _anim?.SetMoving(false);

        if (!TryGetLivingPlayer(out PlayerVitals player))
            return;

        float vision = _creature.VisionRange;
        if (vision <= 0f)
            return;

        if (GetSurfaceDistanceTo(player.transform.position) <= vision)
            EnterAggro();
    }

    void TickAggroed()
    {
        float vision = _creature.VisionRange;
        float attack = _creature.AttackRange;

        bool playerVisible = false;
        PlayerVitals player = null;

        if (TryGetLivingPlayer(out player) && vision > 0f)
            playerVisible = GetSurfaceDistanceTo(player.transform.position) <= vision;

        if (playerVisible)
        {
            _outOfVisionTime = 0f;
        }
        else
        {
            _outOfVisionTime += Time.deltaTime;
            if (_outOfVisionTime >= loseVisionDelay)
            {
                BeginReturnHome();
                return;
            }
        }

        // Still aggroed: chase / attack even during the lose-vision grace period.
        if (player == null || !player.IsAlive)
        {
            _anim?.SetMoving(false);
            return;
        }

        float distToPlayer = GetSurfaceDistanceTo(player.transform.position);

        if (attack > 0f && distToPlayer <= attack)
        {
            FaceToward(player.transform.position);
            _anim?.SetMoving(false);
            return;
        }

        MoveToward(player.transform.position);
        _anim?.SetMoving(true);
    }

    void TickReturning()
    {
        // Re-acquire if the player walks back into vision while heading home.
        if (TryGetLivingPlayer(out PlayerVitals player))
        {
            float vision = _creature.VisionRange;
            if (vision > 0f && GetSurfaceDistanceTo(player.transform.position) <= vision)
            {
                EnterAggro();
                return;
            }
        }

        float homeDist = GetSurfaceDistanceTo(_homePosition);
        if (homeDist <= homeArriveDistance)
        {
            ArriveHome();
            return;
        }

        MoveToward(_homePosition);
        _anim?.SetMoving(true);
    }

    void EnterAggro()
    {
        _state = State.Aggroed;
        _outOfVisionTime = 0f;
    }

    void BeginReturnHome()
    {
        _state = State.Returning;
        _outOfVisionTime = 0f;
        _anim?.SetAttacking(false);
    }

    void ArriveHome()
    {
        _state = State.Idle;
        _outOfVisionTime = 0f;
        _anim?.SetMoving(false);

        Vector3 up = _planet != null
            ? _planet.GetUpAt(_homePosition)
            : transform.up;

        Vector3 faceDir = Vector3.ProjectOnPlane(_homeRotation * Vector3.forward, up);
        if (faceDir.sqrMagnitude < 0.001f)
            faceDir = Vector3.ProjectOnPlane(transform.forward, up);

        if (faceDir.sqrMagnitude > 0.001f)
            transform.SetPositionAndRotation(_homePosition, Quaternion.LookRotation(faceDir.normalized, up));
        else
            transform.position = _homePosition;
    }

    void CaptureHome()
    {
        _homePosition = transform.position;
        _homeRotation = transform.rotation;
        _homeCaptured = true;
    }

    bool TryGetLivingPlayer(out PlayerVitals player)
    {
        if (!TryResolvePlayer(out player) || player == null || !player.IsAlive)
        {
            player = null;
            return false;
        }

        return true;
    }

    void TickKnockback()
    {
        if (!TryResolvePlanet())
        {
            _knockActive = false;
            return;
        }

        float duration = Mathf.Max(0.01f, _knockDuration);
        float t0 = Mathf.Clamp01(_knockElapsed / duration);
        _knockElapsed += Time.deltaTime;
        float t1 = Mathf.Clamp01(_knockElapsed / duration);

        Vector3 up = _planet.GetUpAt(transform.position);
        Vector3 dir = Vector3.ProjectOnPlane(_knockDir, up);
        Vector3 next = transform.position;

        if (dir.sqrMagnitude > 0.0001f)
        {
            dir.Normalize();
            _knockDir = dir;

            float step = (EaseOutCubic(t1) - EaseOutCubic(t0)) * _knockDistance;
            if (step > 0.00001f)
                StepOnSurface(dir, step, out next, out up);
        }

        Vector3 faceDir = Vector3.ProjectOnPlane(_knockFaceTarget - next, up);
        if (faceDir.sqrMagnitude < 0.001f)
            faceDir = Vector3.ProjectOnPlane(transform.forward, up);
        if (faceDir.sqrMagnitude < 0.001f)
            faceDir = Vector3.ProjectOnPlane(Vector3.forward, up);

        if (faceDir.sqrMagnitude > 0.001f)
            ApplyPose(next, faceDir.normalized, up);
        else
            transform.position = next;

        if (t1 >= 1f)
            _knockActive = false;
    }

    void MoveToward(Vector3 targetPosition)
    {
        Vector3 up = _planet.GetUpAt(transform.position);
        Vector3 toTarget = Vector3.ProjectOnPlane(targetPosition - transform.position, up);
        if (toTarget.sqrMagnitude < 0.0001f)
            return;

        Vector3 moveDir = toTarget.normalized;
        _velocity = moveDir * moveSpeed;
        StepOnSurface(moveDir, moveSpeed * Time.deltaTime, out Vector3 next, out up);

        Vector3 faceDir = Vector3.ProjectOnPlane(moveDir, up);
        if (faceDir.sqrMagnitude < 0.001f)
            faceDir = Vector3.ProjectOnPlane(transform.forward, up);
        if (faceDir.sqrMagnitude < 0.001f)
            faceDir = Vector3.ProjectOnPlane(Vector3.forward, up);

        ApplyPose(next, faceDir.normalized, up);
    }

    void StepOnSurface(Vector3 moveDir, float distance, out Vector3 next, out Vector3 up)
    {
        Vector3 probeOrigin = transform.position + moveDir * distance;
        Vector3 radial = (probeOrigin - _planet.Center).normalized;

        if (TryStickToCollider(radial, out next, out Vector3 surfaceUp))
        {
            up = surfaceUp;
            return;
        }

        next = probeOrigin;
        float minDist = GetFallbackSurfaceRadius(radial) + footOffset;
        Vector3 fromCenter = next - _planet.Center;
        if (fromCenter.magnitude < minDist)
            next = _planet.Center + fromCenter.normalized * minDist;
        up = _planet.GetUpAt(next);
    }

    static float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        float inv = 1f - t;
        return 1f - inv * inv * inv;
    }

    void FaceToward(Vector3 targetPosition)
    {
        Vector3 up = _planet != null
            ? _planet.GetUpAt(transform.position)
            : transform.up;

        Vector3 faceDir = Vector3.ProjectOnPlane(targetPosition - transform.position, up);
        if (faceDir.sqrMagnitude < 0.001f)
            return;

        ApplyPose(transform.position, faceDir.normalized, up);
    }

    void ApplyPose(Vector3 next, Vector3 faceDir, Vector3 up)
    {
        Quaternion targetRot = Quaternion.LookRotation(faceDir, up);
        Quaternion nextRot = Quaternion.Slerp(
            transform.rotation,
            targetRot,
            1f - Mathf.Exp(-alignSpeed * Time.deltaTime));

        transform.SetPositionAndRotation(next, nextRot);
    }

    float GetSurfaceDistanceTo(Vector3 targetPosition)
    {
        Vector3 origin = transform.position;

        if (_planet != null)
            return PlanetSurfacePose.GetSurfaceDistance(_planet.Center, origin, targetPosition);

        return Vector3.ProjectOnPlane(targetPosition - origin, transform.up).magnitude;
    }

    bool TryResolvePlayer(out PlayerVitals player)
    {
        if (_player != null)
        {
            player = _player;
            return true;
        }

        _player = FindAnyObjectByType<PlayerVitals>();
        player = _player;
        return player != null;
    }

    bool TryResolvePlanet()
    {
        if (_planet != null)
            return true;

        _planet = SphericalPlanet.Instance != null
            ? SphericalPlanet.Instance
            : FindAnyObjectByType<SphericalPlanet>();

        if (_planet == null)
            return false;

        _tiles = _planet.GetComponent<PlanetTileMap>();
        return true;
    }

    bool TryStickToCollider(Vector3 radial, out Vector3 feetPosition, out Vector3 normal)
    {
        feetPosition = default;
        normal = radial;

        float castStart = GetFallbackSurfaceRadius(radial) + Mathf.Max(4f, groundProbeDistance * 0.5f);
        Vector3 origin = _planet.Center + radial * castStart;
        float maxDist = castStart + 2f;

        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            -radial,
            maxDist,
            groundLayer,
            QueryTriggerInteraction.Ignore);

        if (hits == null || hits.Length == 0)
            return false;

        float bestDist = float.MaxValue;
        bool found = false;
        RaycastHit best = default;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i].collider;
            if (col == null)
                continue;

            if (col.transform != _planet.transform && !col.transform.IsChildOf(_planet.transform))
                continue;

            if (hits[i].distance < bestDist)
            {
                bestDist = hits[i].distance;
                best = hits[i];
                found = true;
            }
        }

        if (!found)
            return false;

        normal = best.normal.sqrMagnitude > 0.001f ? best.normal.normalized : radial;
        if (Vector3.Dot(normal, radial) < 0f)
            normal = -normal;

        feetPosition = best.point + normal * footOffset;
        return true;
    }

    float GetFallbackSurfaceRadius(Vector3 radial)
    {
        if (_tiles != null && _tiles.ProvidesWalkSurface)
            return _tiles.GetWalkSurfaceRadius(radial);
        return _planet.GetTerrainRadius(radial);
    }
}
