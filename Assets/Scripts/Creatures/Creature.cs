using System;
using UnityEngine;

/// <summary>
/// Runtime creature instance. Reads base stats from a <see cref="CreatureStats"/> asset
/// so the same component works for Grimling and future creatures.
/// </summary>
public class Creature : MonoBehaviour
{
    [SerializeField] CreatureStats stats;

    float _currentHealth;

    public CreatureStats Stats => stats;
    public string DisplayName => stats != null ? stats.DisplayName : name;
    public float MaxHealth => stats != null ? stats.MaxHealth : 0f;
    public float AttackDamage => stats != null ? stats.AttackDamage : 0f;
    public float CurrentHealth => _currentHealth;
    public float HealthNormalized => MaxHealth > 0f ? _currentHealth / MaxHealth : 0f;
    public bool IsAlive => _currentHealth > 0f;
    public bool HasStats => stats != null;

    public event Action HealthChanged;
    public event Action<Creature> Died;

    void Awake()
    {
        ResetHealth();
    }

    void OnValidate()
    {
        if (!Application.isPlaying && stats != null)
            _currentHealth = stats.MaxHealth;
    }

    public void SetStats(CreatureStats newStats, bool refillHealth = true)
    {
        stats = newStats;
        if (refillHealth)
            ResetHealth();
        else
            RaiseHealthChanged();
    }

    public void ResetHealth()
    {
        _currentHealth = MaxHealth;
        RaiseHealthChanged();
    }

    public void TakeDamage(float amount)
    {
        if (!IsAlive || amount <= 0f)
            return;

        _currentHealth = Mathf.Max(0f, _currentHealth - amount);
        RaiseHealthChanged();

        if (!IsAlive)
            Died?.Invoke(this);
    }

    public void Heal(float amount)
    {
        if (!IsAlive || amount <= 0f || MaxHealth <= 0f)
            return;

        float next = Mathf.Min(MaxHealth, _currentHealth + amount);
        if (Mathf.Approximately(next, _currentHealth))
            return;

        _currentHealth = next;
        RaiseHealthChanged();
    }

    void RaiseHealthChanged()
    {
        HealthChanged?.Invoke();
    }
}
