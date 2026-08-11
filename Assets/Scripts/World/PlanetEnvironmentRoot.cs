using UnityEngine;

/// <summary>
/// Shared lookup for a planet's "Environment" child — the parent all streamed content (grass/tree/
/// rock runtime pools) spawns under, so streamed objects appear nested under the planet itself
/// rather than under "Streamers" (which only hosts the streaming components, not their output).
/// </summary>
public static class PlanetEnvironmentRoot
{
    const string ChildName = "Environment";

    /// <summary>Finds (or creates) <paramref name="planet"/>'s "Environment" child. Falls back to
    /// <paramref name="fallback"/> when no planet is resolved yet.</summary>
    public static Transform FindOrCreate(SphericalPlanet planet, Transform fallback)
    {
        if (planet == null)
            return fallback;

        Transform existing = planet.transform.Find(ChildName);
        if (existing != null)
            return existing;

        var go = new GameObject(ChildName);
        go.transform.SetParent(planet.transform, false);
        return go.transform;
    }
}
