using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Damages living <see cref="Creature"/>s inside the player range ring.
/// Instant hit on enter, then ticks every 1 / AttackSpeed seconds while they stay inside.
/// </summary>
[RequireComponent(typeof(PlayerVitals))]
public class PlayerRangeCombat : MonoBehaviour
{
    [SerializeField] PlayerRangeIndicator rangeIndicator;
    [Tooltip("Fallback radius if no PlayerRangeIndicator is found.")]
    [SerializeField, Min(0.05f)] float fallbackRadius = 2.5f;

    PlayerVitals _vitals;
    float _tickCooldown;
    readonly HashSet<Creature> _inside = new();
    readonly HashSet<Creature> _hitThisFrame = new();
    readonly HashSet<Creature> _currentlyInside = new();
    readonly List<Creature> _removeBuffer = new();

    void Awake()
    {
        _vitals = GetComponent<PlayerVitals>();
        if (rangeIndicator == null)
            rangeIndicator = GetComponentInChildren<PlayerRangeIndicator>(true);
    }

    void Update()
    {
        _hitThisFrame.Clear();

        if (_vitals == null || !_vitals.IsAlive)
        {
            _inside.Clear();
            return;
        }

        float attackSpeed = _vitals.AttackSpeed;
        float damage = _vitals.AttackDamage;
        if (attackSpeed <= 0f || damage <= 0f)
            return;

        float radius = rangeIndicator != null ? rangeIndicator.GetCombatRadius() : fallbackRadius;
        UpdateOccupancy(radius, damage);

        float interval = 1f / attackSpeed;
        _tickCooldown -= Time.deltaTime;
        if (_tickCooldown > 0f)
            return;

        _tickCooldown = interval;
        DealTickDamage(damage);
    }

    void UpdateOccupancy(float radius, float damage)
    {
        float radiusSq = radius * radius;
        Vector3 origin = transform.position;
        Vector3 up = SphericalPlanet.Instance != null
            ? SphericalPlanet.Instance.GetUpAt(origin)
            : transform.up;

        Creature[] creatures = FindObjectsByType<Creature>();
        _currentlyInside.Clear();

        for (int i = 0; i < creatures.Length; i++)
        {
            Creature creature = creatures[i];
            if (creature == null || !creature.IsAlive)
                continue;

            if (!IsInsideRange(creature.transform.position, origin, up, radiusSq))
                continue;

            _currentlyInside.Add(creature);

            // First frame inside the ring — immediate hit (skip duplicate tick this frame).
            if (_inside.Add(creature))
            {
                creature.TakeDamage(damage);
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

            creature.TakeDamage(damage);
            if (!creature.IsAlive)
                _removeBuffer.Add(creature);
        }

        for (int i = 0; i < _removeBuffer.Count; i++)
            _inside.Remove(_removeBuffer[i]);
    }

    static bool IsInsideRange(Vector3 target, Vector3 origin, Vector3 up, float radiusSq)
    {
        Vector3 planar = Vector3.ProjectOnPlane(target - origin, up);
        return planar.sqrMagnitude <= radiusSq;
    }
}
