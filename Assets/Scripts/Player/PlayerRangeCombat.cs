using UnityEngine;

/// <summary>
/// Damages living <see cref="Creature"/>s inside the player range ring.
/// Instant hit on enter, then ticks every 1 / AttackSpeed seconds while they stay inside.
/// </summary>
[RequireComponent(typeof(PlayerVitals))]
public class PlayerRangeCombat : MonoBehaviour
{
    [SerializeField] AttackRangeIndicator rangeIndicator;
    [Tooltip("Fallback radius if no AttackRangeIndicator is found.")]
    [SerializeField, Min(0.05f)] float fallbackRadius = 2.5f;

    PlayerVitals _vitals;
    float _tickCooldown;
    Creature _aimTarget;
    readonly System.Collections.Generic.HashSet<Creature> _inside = new();
    readonly System.Collections.Generic.HashSet<Creature> _hitThisFrame = new();
    readonly System.Collections.Generic.HashSet<Creature> _currentlyInside = new();
    readonly System.Collections.Generic.List<Creature> _removeBuffer = new();

    public bool HasAttackTargets => _inside.Count > 0;

    /// <summary>
    /// Aim point of the creature currently being attacked.
    /// Holds the same target while it stays in range, unless another is clearly closer.
    /// </summary>
    public bool TryGetAttackAimPoint(out Vector3 worldPoint)
    {
        worldPoint = default;
        Creature nearest = FindNearestInside();
        Creature held = IsValidAimTarget(_aimTarget) ? _aimTarget : null;

        if (held != null && nearest != null && nearest != held)
        {
            float heldSqr = SqrTo(held);
            float nearestSqr = SqrTo(nearest);
            // Switch only when the new one is ~20% closer, so the weapon doesn't flicker.
            if (nearestSqr >= heldSqr * 0.64f)
                nearest = held;
        }

        _aimTarget = nearest ?? held;
        if (_aimTarget == null)
            return false;

        worldPoint = GetAimPoint(_aimTarget);
        return true;
    }

    public void HideRange()
    {
        enabled = false;
        if (rangeIndicator == null)
            rangeIndicator = GetComponentInChildren<AttackRangeIndicator>(true);
        if (rangeIndicator != null)
            rangeIndicator.gameObject.SetActive(false);
    }

    void Awake()
    {
        _vitals = GetComponent<PlayerVitals>();
        if (rangeIndicator == null)
            rangeIndicator = GetComponentInChildren<AttackRangeIndicator>(true);
    }

    void Update()
    {
        _hitThisFrame.Clear();

        if (_vitals == null || !_vitals.IsAlive)
        {
            _inside.Clear();
            _aimTarget = null;
            return;
        }

        float attackSpeed = _vitals.AttackSpeed;
        float damage = _vitals.AttackDamage;
        float radius = ResolveRadius();
        if (attackSpeed <= 0f || damage <= 0f || radius <= 0f)
            return;

        UpdateOccupancy(radius, damage);

        float interval = 1f / attackSpeed;
        _tickCooldown -= Time.deltaTime;
        if (_tickCooldown > 0f)
            return;

        _tickCooldown = interval;
        DealTickDamage(damage);
    }

    float ResolveRadius()
    {
        if (_vitals.AttackRange > 0f)
            return _vitals.AttackRange;
        if (rangeIndicator != null)
            return rangeIndicator.GetCombatRadius();
        return fallbackRadius;
    }

    void UpdateOccupancy(float radius, float damage)
    {
        Vector3 origin = transform.position;
        Vector3? planetCenter = SphericalPlanet.Instance != null
            ? SphericalPlanet.Instance.Center
            : (Vector3?)null;

        Creature[] creatures = FindObjectsByType<Creature>();
        _currentlyInside.Clear();

        for (int i = 0; i < creatures.Length; i++)
        {
            Creature creature = creatures[i];
            if (creature == null || !creature.IsAlive)
                continue;

            if (!IsInsideRange(creature.transform.position, origin, planetCenter, transform.up, radius))
                continue;

            _currentlyInside.Add(creature);

            if (_inside.Add(creature))
            {
                creature.TakeDamage(damage, origin);
                _hitThisFrame.Add(creature);
            }
        }

        _removeBuffer.Clear();
        foreach (Creature tracked in _inside)
        {
            if (tracked == null || !tracked.IsAlive || !_currentlyInside.Contains(tracked))
                _removeBuffer.Add(tracked);
        }

        for (int i = 0; i < _removeBuffer.Count; i++)
            _inside.Remove(_removeBuffer[i]);

        if (_aimTarget != null && !_inside.Contains(_aimTarget))
            _aimTarget = null;
    }

    void DealTickDamage(float damage)
    {
        _removeBuffer.Clear();
        foreach (Creature creature in _inside)
        {
            if (creature == null || !creature.IsAlive)
            {
                _removeBuffer.Add(creature);
                continue;
            }

            if (_hitThisFrame.Contains(creature))
                continue;

            creature.TakeDamage(damage, transform.position);
            if (!creature.IsAlive)
                _removeBuffer.Add(creature);
        }

        for (int i = 0; i < _removeBuffer.Count; i++)
            _inside.Remove(_removeBuffer[i]);
    }

    Creature FindNearestInside()
    {
        Creature nearest = null;
        float bestSqr = float.MaxValue;
        foreach (Creature creature in _inside)
        {
            if (!IsValidAimTarget(creature))
                continue;

            float sqr = SqrTo(creature);
            if (sqr >= bestSqr)
                continue;

            bestSqr = sqr;
            nearest = creature;
        }

        return nearest;
    }

    bool IsValidAimTarget(Creature creature)
    {
        return creature != null && creature.IsAlive && _inside.Contains(creature);
    }

    float SqrTo(Creature creature)
    {
        Vector3 delta = creature.transform.position - transform.position;
        if (SphericalPlanet.Instance != null)
        {
            Vector3 up = SphericalPlanet.Instance.GetUpAt(transform.position);
            delta = Vector3.ProjectOnPlane(delta, up);
        }

        return delta.sqrMagnitude;
    }

    static Vector3 GetAimPoint(Creature creature)
    {
        Vector3 up = SphericalPlanet.Instance != null
            ? SphericalPlanet.Instance.GetUpAt(creature.transform.position)
            : creature.transform.up;
        return creature.transform.position + up * 0.7f;
    }

    static bool IsInsideRange(Vector3 target, Vector3 origin, Vector3? planetCenter, Vector3 fallbackUp, float radius)
    {
        if (planetCenter.HasValue)
            return PlanetSurfacePose.GetSurfaceDistance(planetCenter.Value, origin, target) <= radius;

        Vector3 planar = Vector3.ProjectOnPlane(target - origin, fallbackUp);
        return planar.sqrMagnitude <= radius * radius;
    }
}
