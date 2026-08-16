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
    readonly System.Collections.Generic.HashSet<Creature> _inside = new();
    readonly System.Collections.Generic.HashSet<Creature> _hitThisFrame = new();
    readonly System.Collections.Generic.HashSet<Creature> _currentlyInside = new();
    readonly System.Collections.Generic.List<Creature> _removeBuffer = new();

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

    static bool IsInsideRange(Vector3 target, Vector3 origin, Vector3? planetCenter, Vector3 fallbackUp, float radius)
    {
        if (planetCenter.HasValue)
            return PlanetSurfacePose.GetSurfaceDistance(planetCenter.Value, origin, target) <= radius;

        Vector3 planar = Vector3.ProjectOnPlane(target - origin, fallbackUp);
        return planar.sqrMagnitude <= radius * radius;
    }
}
