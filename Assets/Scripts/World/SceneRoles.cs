using System;
using UnityEngine.SceneManagement;

/// <summary>
/// Shared scene-role checks used by locomotion, vitals, and bootstrap.
/// </summary>
public static class SceneRoles
{
    public static bool IsSpaceshipScene()
    {
        string name = SceneManager.GetActiveScene().name;
        return !string.IsNullOrEmpty(name)
               && name.StartsWith("SpaceShip", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsPlanetScene()
    {
        string name = SceneManager.GetActiveScene().name;
        if (string.IsNullOrEmpty(name))
            return false;

        return name.StartsWith("Galaxy", StringComparison.OrdinalIgnoreCase)
               || name.StartsWith("Planet", StringComparison.OrdinalIgnoreCase);
    }
}
