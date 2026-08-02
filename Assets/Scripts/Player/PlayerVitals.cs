using System;
using UnityEngine;

/// <summary>
/// Runtime player vitals. Capacity values (HP, attack, oxygen tank) come from <see cref="PlayerStats"/>;
/// drain rates stay here as tuning for survival feel.
/// </summary>
public class PlayerVitals : MonoBehaviour, IVitalsReadable
{
    [Header("Stats")]
    [SerializeField] PlayerStats stats;

    [Header("Drain Rates")]
    [Tooltip("Oxygen lost per second outside the spaceship.")]
    [SerializeField] float oxygenDrainPerSecond = 4f;
    [Tooltip("HP lost per second while oxygen is empty.")]
    [SerializeField] float healthDrainPerSecond = 5f;

    float _currentHealth;
    float _currentOxygen;

    public PlayerStats Stats => stats;
    public string DisplayName => stats != null ? stats.DisplayName : name;
    public float MaxHealth => stats != null ? stats.MaxHealth : 0f;
    public float AttackDamage => stats != null ? stats.AttackDamage : 0f;
    public float AttackSpeed => stats != null ? stats.AttackSpeed : 0f;
    public float AttackRange => stats != null ? stats.AttackRange : 0f;
    public float MaxOxygen => stats != null ? stats.OxygenTank : 0f;
    public float CurrentHealth => _currentHealth;
    public float CurrentOxygen => _currentOxygen;
    public float HealthNormalized => MaxHealth > 0f ? _currentHealth / MaxHealth : 0f;
    public float OxygenNormalized => MaxOxygen > 0f ? _currentOxygen / MaxOxygen : 0f;
    public bool IsAlive => _currentHealth > 0f;
    public bool HasStats => stats != null;
    public bool HasOxygen => MaxOxygen > 0f;
    public bool IsOnSpaceship => SceneRoles.IsSpaceshipScene();

    public event Action VitalsChanged;
    public event Action Damaged;
    public event Action Died;

    void Awake()
    {
        ResetVitals();
    }

    void OnValidate()
    {
        oxygenDrainPerSecond = Mathf.Max(0f, oxygenDrainPerSecond);
        healthDrainPerSecond = Mathf.Max(0f, healthDrainPerSecond);

        if (!Application.isPlaying && stats != null)
        {
            _currentHealth = stats.MaxHealth;
            _currentOxygen = stats.OxygenTank;
        }
    }

    void Start()
    {
        if (SceneRoles.IsSpaceshipScene())
            RefillOxygen();
        else
            RaiseChanged();
    }

    void Update()
    {
        if (SceneRoles.IsSpaceshipScene())
            return;

        if (_currentOxygen > 0f)
        {
            float next = Mathf.Max(0f, _currentOxygen - oxygenDrainPerSecond * Time.deltaTime);
            if (!Mathf.Approximately(next, _currentOxygen))
            {
                _currentOxygen = next;
                RaiseChanged();
            }
        }
        else if (_currentHealth > 0f && healthDrainPerSecond > 0f)
        {
            float next = Mathf.Max(0f, _currentHealth - healthDrainPerSecond * Time.deltaTime);
            if (!Mathf.Approximately(next, _currentHealth))
            {
                _currentHealth = next;
                RaiseChanged();
                if (!IsAlive)
                    Died?.Invoke();
            }
        }
    }

    public void SetStats(PlayerStats newStats, bool refill = true)
    {
        stats = newStats;
        if (refill)
            ResetVitals();
        else
            RaiseChanged();
    }

    public void ResetVitals()
    {
        _currentHealth = MaxHealth;
        _currentOxygen = MaxOxygen;
        RaiseChanged();
    }

    public void ResetHealth()
    {
        _currentHealth = MaxHealth;
        RaiseChanged();
    }

    public void TakeDamage(float amount)
    {
        if (!IsAlive || amount <= 0f)
            return;

        _currentHealth = Mathf.Max(0f, _currentHealth - amount);
        RaiseChanged();
        Damaged?.Invoke();

        if (!IsAlive)
            Died?.Invoke();
    }

    public void Heal(float amount)
    {
        if (!IsAlive || amount <= 0f || MaxHealth <= 0f)
            return;

        float next = Mathf.Min(MaxHealth, _currentHealth + amount);
        if (Mathf.Approximately(next, _currentHealth))
            return;

        _currentHealth = next;
        RaiseChanged();
    }

    public void RefillOxygen()
    {
        _currentOxygen = MaxOxygen;
        RaiseChanged();
    }

    public void AddOxygen(float amount)
    {
        if (amount <= 0f || MaxOxygen <= 0f)
            return;

        _currentOxygen = Mathf.Min(MaxOxygen, _currentOxygen + amount);
        RaiseChanged();
    }

    void RaiseChanged()
    {
        VitalsChanged?.Invoke();
    }
}
