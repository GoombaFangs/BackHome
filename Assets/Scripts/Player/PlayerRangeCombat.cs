using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Turn-based player attacks per equipped gun. Anyone inside a weapon's range
/// gets a number (entry order), and that gun ticks through the circle at its own speed.
/// </summary>
[RequireComponent(typeof(PlayerVitals))]
public class PlayerRangeCombat : MonoBehaviour
{
    [SerializeField, Min(0.05f)] float fallbackRadius = 2.5f;

    PlayerVitals _vitals;
    FloatingWeapon _weapon;
    readonly float[] _tickCooldown = new float[CombatLoadout.MaxWeapons];
    readonly int[] _turnIndex = new int[CombatLoadout.MaxWeapons];
    readonly Creature[] _aimTarget = new Creature[CombatLoadout.MaxWeapons];
    readonly List<Creature> _queue = new();
    readonly HashSet<Creature> _queued = new();
    readonly HashSet<Creature> _currentlyInside = new();
    readonly List<Creature> _removeBuffer = new();

    /// <summary>Aim at the creature this slot will shoot next.</summary>
    public bool TryGetAttackAimPoint(int slot, out Vector3 worldPoint)
    {
        worldPoint = default;
        Creature current = AimTarget(slot);
        if (current == null)
            return false;

        worldPoint = GetAimPoint(current);
        return true;
    }

    public void HideRange()
    {
        enabled = false;
        ClearQueue();
    }

    void Awake()
    {
        _vitals = GetComponent<PlayerVitals>();
        _weapon = GetComponent<FloatingWeapon>();
        if (SceneRoles.IsSpaceshipScene())
            HideRange();
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

        IReadOnlyList<WeaponDefinition> weapons = _vitals.Weapons;
        int weaponCount = 0;
        if (weapons != null)
            weaponCount = Mathf.Min(weapons.Count, CombatLoadout.MaxWeapons);

        float radius = ResolveRadius();
        if (radius <= 0f || weaponCount <= 0)
        {
            ClearQueue();
            return;
        }

        UpdateOccupancy(radius);
        CombatStats origin = _vitals.BaseCombat;

        for (int i = 0; i < weaponCount; i++)
        {
            WeaponDefinition definition = weapons[i];
            CombatStats combat = _vitals.CombatFor(definition);
            float range = combat.AttackRange;
            _aimTarget[i] = PeekTarget(i, range);

            if (combat.AttackSpeed <= 0f || combat.AttackDamage <= 0f || range <= 0f)
                continue;

            if (_aimTarget[i] == null)
            {
                _tickCooldown[i] = 0f;
                continue;
            }

            _tickCooldown[i] -= Time.deltaTime;
            if (_tickCooldown[i] > 0f)
                continue;

            if (_weapon != null && !_weapon.IsSlotReady(i))
                continue;

            _tickCooldown[i] = 1f / combat.AttackSpeed;
            Strike(i, _aimTarget[i], combat.AttackDamage, range);
        }

        for (int i = weaponCount; i < CombatLoadout.MaxWeapons; i++)
        {
            _tickCooldown[i] = 0f;
            _turnIndex[i] = 0;
            _aimTarget[i] = null;
        }
    }

    float ResolveRadius()
    {
        if (_vitals.AttackRange > 0f)
            return _vitals.AttackRange;
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

    void Strike(int slot, Creature target, float damage, float range)
    {
        if (target == null || !target.IsAlive)
            return;

        int hitIndex = _queue.IndexOf(target);
        if (_weapon == null || !_weapon.TryFire(slot, target, damage, transform.position))
            target.TakeDamage(damage, transform.position);

        if (_queue.Count == 0)
        {
            _turnIndex[slot] = 0;
            _aimTarget[slot] = PeekTarget(slot, range);
            return;
        }

        if (hitIndex >= 0)
            _turnIndex[slot] = (hitIndex + 1) % _queue.Count;
        else
            _turnIndex[slot] = (_turnIndex[slot] + 1) % _queue.Count;

        _aimTarget[slot] = PeekTarget(slot, range);
    }

    Creature AimTarget(int slot)
    {
        if (slot < 0 || slot >= CombatLoadout.MaxWeapons)
            return null;

        Creature current = _aimTarget[slot];
        if (current != null && current.IsAlive)
            return current;

        return PeekTarget(slot, SlotRange(slot));
    }

    float SlotRange(int slot)
    {
        IReadOnlyList<WeaponDefinition> weapons = _vitals != null ? _vitals.Weapons : null;
        if (weapons == null || slot < 0 || slot >= weapons.Count)
            return 0f;
        return _vitals.CombatFor(weapons[slot]).AttackRange;
    }

    Creature PeekTarget(int slot, float range)
    {
        if (range <= 0f || _queue.Count == 0)
            return null;

        int start = _turnIndex[slot];
        if (start < 0 || start >= _queue.Count)
            start = 0;

        Vector3 origin = transform.position;
        Vector3? planetCenter = SphericalPlanet.Instance != null
            ? SphericalPlanet.Instance.Center
            : (Vector3?)null;

        for (int n = 0; n < _queue.Count; n++)
        {
            int index = (start + n) % _queue.Count;
            Creature current = _queue[index];
            if (current == null || !current.IsAlive)
                continue;
            if (!IsInsideRange(current.transform.position, origin, planetCenter, transform.up, range))
                continue;
            return current;
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

        for (int slot = 0; slot < CombatLoadout.MaxWeapons; slot++)
        {
            if (_aimTarget[slot] == removed)
                _aimTarget[slot] = null;

            if (_queue.Count == 0)
            {
                _turnIndex[slot] = 0;
                continue;
            }

            if (index < _turnIndex[slot])
                _turnIndex[slot]--;
            if (_turnIndex[slot] >= _queue.Count)
                _turnIndex[slot] = 0;
        }
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
        for (int i = 0; i < CombatLoadout.MaxWeapons; i++)
        {
            _tickCooldown[i] = 0f;
            _turnIndex[i] = 0;
            _aimTarget[i] = null;
        }
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
