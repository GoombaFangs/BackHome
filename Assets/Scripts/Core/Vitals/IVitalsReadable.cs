using System;

/// <summary>
/// Shared read API for world vitals UI (player, creature, or future hosts).
/// </summary>
public interface IVitalsReadable
{
    float CurrentHealth { get; }
    float MaxHealth { get; }
    float CurrentOxygen { get; }
    float MaxOxygen { get; }
    bool HasOxygen { get; }
    bool IsAlive { get; }

    event Action VitalsChanged;
    event Action Damaged;
    event Action Died;
}
