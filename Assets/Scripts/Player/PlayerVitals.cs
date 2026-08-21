using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime vitals. HP / oxygen come from <see cref="PlayerStats"/>.
/// Combat is <see cref="CombatLoadout"/> of that asset's base + Weapons (copied at spawn so play mode
/// can swap guns without dirtying the shared asset).
/// Drain rates stay here as survival feel.
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

    readonly List<WeaponDefinition> _loadout = new();
    bool _loadoutReady;
    float _currentHealth;
    float _currentOxygen;
    bool _invulnerable;

    public PlayerStats Stats => stats;
    public string DisplayName => stats != null ? stats.DisplayName : name;
    public float MaxHealth => stats != null ? stats.MaxHealth : 0f;
    public float MaxOxygen => stats != null ? stats.OxygenTank : 0f;
    public CombatStats Combat => CombatLoadout.Combine(stats != null ? stats.BaseCombat : CombatStats.Zero, ActiveWeapons);
    public float AttackDamage => Combat.AttackDamage;
    public float AttackSpeed => Combat.AttackSpeed;
    public float AttackRange => Combat.AttackRange;
    public IReadOnlyList<WeaponDefinition> Weapons => ActiveWeapons;
    public WeaponDefinition PrimaryWeapon => CombatLoadout.Primary(ActiveWeapons);
    public float CurrentHealth => _currentHealth;
    public float CurrentOxygen => _currentOxygen;
    public float HealthNormalized => MaxHealth > 0f ? _currentHealth / MaxHealth : 0f;
    public float OxygenNormalized => MaxOxygen > 0f ? _currentOxygen / MaxOxygen : 0f;
    public bool IsAlive => _currentHealth > 0f;
    public bool HasStats => stats != null;
    public bool HasOxygen => MaxOxygen > 0f;
    public bool IsOnSpaceship => SceneRoles.IsSpaceshipScene();
    public bool IsInvulnerable => _invulnerable;

    IReadOnlyList<WeaponDefinition> ActiveWeapons
    {
        get
        {
            if (Application.isPlaying)
            {
                EnsureLoadout();
                return _loadout;
            }

            return stats != null ? stats.Weapons : Array.Empty<WeaponDefinition>();
        }
    }

    public event Action VitalsChanged;
    public event Action<float> Damaged;
    public event Action Died;
    public event Action LoadoutChanged;

    void Awake()
    {
        RebuildLoadout();
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
        if (_invulnerable || SceneRoles.IsSpaceshipScene())
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
        RebuildLoadout();
        if (refill)
            ResetVitals();
        else
            RaiseChanged();
        LoadoutChanged?.Invoke();
    }

    /// <summary>Replaces the runtime weapon list. Does not mutate the <see cref="PlayerStats"/> asset.</summary>
    public void SetWeapons(IReadOnlyList<WeaponDefinition> weapons)
    {
        _loadout.Clear();
        _loadoutReady = true;
        if (weapons != null)
        {
            for (int i = 0; i < weapons.Count; i++)
            {
                if (weapons[i] != null)
                    _loadout.Add(weapons[i]);
            }
        }

        RaiseLoadoutChanged();
    }

    /// <summary>Sets slot 0 (the held weapon). Extra weapons in the list stay as stat contributors.</summary>
    public void SetPrimaryWeapon(WeaponDefinition weapon)
    {
        EnsureLoadout();
        if (weapon == null)
        {
            if (_loadout.Count > 0)
                _loadout.RemoveAt(0);
        }
        else if (_loadout.Count == 0)
            _loadout.Add(weapon);
        else
            _loadout[0] = weapon;

        RaiseLoadoutChanged();
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

    public void SetInvulnerable(bool invulnerable)
    {
        _invulnerable = invulnerable;
    }

    public void TakeDamage(float amount)
    {
        if (_invulnerable || !IsAlive || amount <= 0f)
            return;

        _currentHealth = Mathf.Max(0f, _currentHealth - amount);
        RaiseChanged();
        Damaged?.Invoke(amount);

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

    void EnsureLoadout()
    {
        if (_loadoutReady)
            return;
        RebuildLoadout();
    }

    void RebuildLoadout()
    {
        _loadout.Clear();
        _loadoutReady = true;
        if (stats == null)
            return;

        IReadOnlyList<WeaponDefinition> source = stats.Weapons;
        for (int i = 0; i < source.Count; i++)
        {
            if (source[i] != null)
                _loadout.Add(source[i]);
        }
    }

    void RaiseLoadoutChanged()
    {
        RaiseChanged();
        LoadoutChanged?.Invoke();
    }

    void RaiseChanged()
    {
        VitalsChanged?.Invoke();
    }
}
