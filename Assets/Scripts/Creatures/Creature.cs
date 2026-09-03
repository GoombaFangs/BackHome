using System;
using UnityEngine;

/// <summary>
/// Runtime creature instance. Reads base stats from a <see cref="CreatureStats"/> asset
/// so the same component works for Grimling and future creatures.
/// </summary>
public class Creature : MonoBehaviour, IVitalsReadable
{
    [SerializeField] CreatureStats stats;
    [SerializeField, Min(0f)] float destroyDelay = 0.1f;

    float _currentHealth;
    bool _dying;
    Action _diedNoArg;
    CreatureChase _chase;

    public CreatureStats Stats => stats;
    public string DisplayName => stats != null ? stats.DisplayName : name;
    public float MaxHealth => stats != null ? stats.MaxHealth : 0f;
    public float VisionRange => stats != null ? stats.VisionRange : 0f;
    public float AttackDamage => stats != null ? stats.AttackDamage : 0f;
    public float AttackSpeed => stats != null ? stats.AttackSpeed : 0f;
    public float AttackRange => stats != null ? stats.AttackRange : 0f;
    public float CurrentHealth => _currentHealth;
    public float CurrentOxygen => 0f;
    public float MaxOxygen => 0f;
    public bool HasOxygen => false;
    public float HealthNormalized => MaxHealth > 0f ? _currentHealth / MaxHealth : 0f;
    public bool IsAlive => _currentHealth > 0f && !_dying;
    public bool HasStats => stats != null;

    /// <summary>True while frozen - e.g. the instant the player dies, so every creature stops dead
    /// in place and can't keep chasing/attacking during the short beat where the player's own
    /// death animation plays out. <see cref="CreatureChase"/> and <see cref="CreatureRangeCombat"/>
    /// both check this and fully stop while it's true. Set via <see cref="SetFrozen"/>.</summary>
    public bool IsFrozen { get; private set; }

    /// <summary>Current surface movement (direction * speed), zero while stationary — lets telegraphed attacks lead a moving target.</summary>
    public Vector3 Velocity => _chase != null ? _chase.Velocity : Vector3.zero;

    public event Action VitalsChanged;
    public event Action<float> Damaged;
    public event Action<Creature> Died;

    event Action IVitalsReadable.Died
    {
        add => _diedNoArg += value;
        remove => _diedNoArg -= value;
    }

    void Awake()
    {
        _chase = GetComponent<CreatureChase>();
        ResetHealth();
    }

    void OnValidate()
    {
        destroyDelay = Mathf.Max(0f, destroyDelay);
        if (!Application.isPlaying && stats != null)
            _currentHealth = stats.MaxHealth;
    }

    public void SetStats(CreatureStats newStats, bool refillHealth = true)
    {
        stats = newStats;
        if (refillHealth)
            ResetHealth();
        else
            RaiseVitalsChanged();
    }

    public void ResetHealth()
    {
        _dying = false;
        _currentHealth = MaxHealth;
        RaiseVitalsChanged();
    }

    public void TakeDamage(float amount)
    {
        ApplyDamage(amount);
    }

    /// <summary>
    /// Damages this creature and knocks it away from <paramref name="sourcePosition"/> along the surface.
    /// </summary>
    public void TakeDamage(float amount, Vector3 sourcePosition)
    {
        if (!ApplyDamage(amount))
            return;

        _chase?.ApplyKnockback(sourcePosition);
    }

    bool ApplyDamage(float amount)
    {
        if (!IsAlive || amount <= 0f)
            return false;

        _currentHealth = Mathf.Max(0f, _currentHealth - amount);
        RaiseVitalsChanged();
        Damaged?.Invoke(amount);

        if (_currentHealth <= 0f)
            BeginDeath();

        return true;
    }

    /// <summary>Freezes/unfreezes this creature - see <see cref="IsFrozen"/>.</summary>
    public void SetFrozen(bool frozen)
    {
        IsFrozen = frozen;
    }

    public void Heal(float amount)
    {
        if (!IsAlive || amount <= 0f || MaxHealth <= 0f)
            return;

        float next = Mathf.Min(MaxHealth, _currentHealth + amount);
        if (Mathf.Approximately(next, _currentHealth))
            return;

        _currentHealth = next;
        RaiseVitalsChanged();
    }

    void BeginDeath()
    {
        if (_dying)
            return;

        _dying = true;
        _diedNoArg?.Invoke();
        Died?.Invoke(this);

        if (destroyDelay <= 0f)
            Destroy(gameObject);
        else
            Destroy(gameObject, destroyDelay);
    }

    void RaiseVitalsChanged()
    {
        VitalsChanged?.Invoke();
    }
}
