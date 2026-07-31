using System;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Health + oxygen. Oxygen drains outside the spaceship; HP drains when oxygen is empty.
/// </summary>
public class PlayerVitals : MonoBehaviour
{
    [Header("Health")]
    [SerializeField] float maxHealth = 100f;
    [Tooltip("HP lost per second while oxygen is empty.")]
    [SerializeField] float healthDrainPerSecond = 5f;

    [Header("Oxygen")]
    [Tooltip("Oxygen tank capacity.")]
    [SerializeField] float maxOxygen = 100f;
    [Tooltip("Oxygen lost per second outside the spaceship.")]
    [SerializeField] float oxygenDrainPerSecond = 4f;

    float _currentHealth;
    float _currentOxygen;

    public float MaxHealth => maxHealth;
    public float MaxOxygen => maxOxygen;
    public float CurrentHealth => _currentHealth;
    public float CurrentOxygen => _currentOxygen;
    public float HealthNormalized => maxHealth > 0f ? _currentHealth / maxHealth : 0f;
    public float OxygenNormalized => maxOxygen > 0f ? _currentOxygen / maxOxygen : 0f;
    public bool IsOnSpaceship => IsSpaceshipScene();

    public event Action VitalsChanged;

    void Awake()
    {
        _currentHealth = maxHealth;
        _currentOxygen = maxOxygen;
    }

    void Start()
    {
        if (IsSpaceshipScene())
            RefillOxygen();
        else
            RaiseChanged();
    }

    void Update()
    {
        if (IsSpaceshipScene())
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
            }
        }
    }

    public void TakeDamage(float amount)
    {
        if (amount <= 0f || _currentHealth <= 0f)
            return;

        _currentHealth = Mathf.Max(0f, _currentHealth - amount);
        RaiseChanged();
    }

    public void Heal(float amount)
    {
        if (amount <= 0f)
            return;

        _currentHealth = Mathf.Min(maxHealth, _currentHealth + amount);
        RaiseChanged();
    }

    public void RefillOxygen()
    {
        _currentOxygen = maxOxygen;
        RaiseChanged();
    }

    public void AddOxygen(float amount)
    {
        if (amount <= 0f)
            return;

        _currentOxygen = Mathf.Min(maxOxygen, _currentOxygen + amount);
        RaiseChanged();
    }

    void RaiseChanged()
    {
        VitalsChanged?.Invoke();
    }

    static bool IsSpaceshipScene()
    {
        string name = SceneManager.GetActiveScene().name;
        return !string.IsNullOrEmpty(name)
               && name.StartsWith("SpaceShip", StringComparison.OrdinalIgnoreCase);
    }
}
