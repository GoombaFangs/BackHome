using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Turn-based player attacks: anyone inside the range ring gets a number (entry order),
/// and each attack tick hits the next number in the circle.
/// </summary>
[RequireComponent(typeof(PlayerVitals))]
public class PlayerRangeCombat : MonoBehaviour
{
    [SerializeField] AttackRangeIndicator rangeIndicator;
    [Tooltip("Fallback radius if no AttackRangeIndicator is found.")]
    [SerializeField, Min(0.05f)] float fallbackRadius = 2.5f;

    PlayerVitals _vitals;
    FloatingWeapon _weapon;
    float _tickCooldown;
    int _turnIndex;
    readonly List<Creature> _queue = new();
    readonly HashSet<Creature> _queued = new();
    readonly HashSet<Creature> _currentlyInside = new();
    readonly List<Creature> _removeBuffer = new();

    public bool HasAttackTargets => _queue.Count > 0;

    /// <summary>Aim at the creature whose turn it is right now.</summary>
    public bool TryGetAttackAimPoint(out Vector3 worldPoint)
    {
        worldPoint = default;
        Creature current = CurrentTurnTarget();
        if (current == null)
            return false;

        worldPoint = GetAimPoint(current);
        return true;
    }

    public void HideRange()
    {
        enabled = false;
        if (rangeIndicator == null)
            rangeIndicator = GetComponentInChildren<AttackRangeIndicator>(true);
        if (rangeIndicator != null)
            rangeIndicator.gameObject.SetActive(false);
        ClearQueue();
    }

    void Awake()
    {
        _vitals = GetComponent<PlayerVitals>();
        _weapon = GetComponent<FloatingWeapon>();
        if (rangeIndicator == null)
            rangeIndicator = GetComponentInChildren<AttackRangeIndicator>(true);
    }

    void OnDisable()
    {
        ClearQueue();
    }

    void Update()
    {
        if (_vitals == null || !_vitals.IsAlive)
        {
            ClearQueue();
            return;
        }

        float attackSpeed = _vitals.AttackSpeed;
        float damage = _vitals.AttackDamage;
        float radius = ResolveRadius();
        if (attackSpeed <= 0f || damage <= 0f || radius <= 0f)
        {
            ClearQueue();
            return;
        }

        UpdateOccupancy(radius);

        if (_queue.Count == 0)
        {
            _tickCooldown = 0f;
            _turnIndex = 0;
            return;
        }

        _tickCooldown -= Time.deltaTime;
        if (_tickCooldown > 0f)
            return;

        _tickCooldown = 1f / attackSpeed;
        StrikeCurrentTurn(damage);
    }

    float ResolveRadius()
    {
        if (_vitals.AttackRange > 0f)
            return _vitals.AttackRange;
        if (rangeIndicator != null)
            return rangeIndicator.GetCombatRadius();
        return fallbackRadius;
    }

    void UpdateOccupancy(float radius)
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
            if (_queued.Add(creature))
                _queue.Add(creature);
        }

        _removeBuffer.Clear();
        for (int i = 0; i < _queue.Count; i++)
        {
            Creature tracked = _queue[i];
            if (tracked == null || !tracked.IsAlive || !_currentlyInside.Contains(tracked))
                _removeBuffer.Add(tracked);
        }

        for (int i = 0; i < _removeBuffer.Count; i++)
            RemoveFromQueue(_removeBuffer[i]);

        SyncQueuedSet();
    }

    void SyncQueuedSet()
    {
        _queued.Clear();
        for (int i = 0; i < _queue.Count; i++)
        {
            if (_queue[i] != null)
                _queued.Add(_queue[i]);
        }
    }

    void StrikeCurrentTurn(float damage)
    {
        Creature target = CurrentTurnTarget();
        if (target == null)
            return;

        int index = _turnIndex;
        if (_weapon == null || !_weapon.TryFire(target, damage, transform.position))
            target.TakeDamage(damage, transform.position);

        if (_queue.Count == 0)
        {
            _turnIndex = 0;
            return;
        }

        _turnIndex = (index + 1) % _queue.Count;
    }

    Creature CurrentTurnTarget()
    {
        int guard = _queue.Count + 2;
        while (_queue.Count > 0 && guard-- > 0)
        {
            if (_turnIndex < 0 || _turnIndex >= _queue.Count)
                _turnIndex = 0;

            Creature current = _queue[_turnIndex];
            if (current != null && current.IsAlive)
                return current;

            RemoveFromQueue(current);
        }

        return null;
    }

    void RemoveFromQueue(Creature creature)
    {
        int index = creature != null ? _queue.IndexOf(creature) : IndexOfDestroyed();
        if (index < 0)
            return;

        Creature removed = _queue[index];
        _queue.RemoveAt(index);
        if (removed != null)
            _queued.Remove(removed);

        if (_queue.Count == 0)
        {
            _turnIndex = 0;
            return;
        }

        if (index < _turnIndex)
            _turnIndex--;
        if (_turnIndex >= _queue.Count)
            _turnIndex = 0;
    }

    int IndexOfDestroyed()
    {
        for (int i = 0; i < _queue.Count; i++)
        {
            if (_queue[i] == null)
                return i;
        }

        return -1;
    }

    void ClearQueue()
    {
        _queue.Clear();
        _queued.Clear();
        _turnIndex = 0;
        _tickCooldown = 0f;
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
