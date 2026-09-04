using UnityEngine;

/// <summary>
/// Shared math for picking a random point within a given world-space radius of an anchor on a
/// spherical planet - area-uniform across the disk/annulus (sqrt-distributed radius), so points
/// aren't biased toward the anchor's exact center. Used by anything that scatters positions around
/// a point on a planet (creature spawn points, portal-relative player spawn, etc.) so the
/// distribution only has to be tuned/fixed in one place instead of being copy-pasted per system.
/// </summary>
public static class PlanetRadialSampling
{
    static System.Random _rng;

    /// <summary>
    /// Samples a direction (from <paramref name="planet"/>'s center) whose distance from
    /// <paramref name="anchorWorldPosition"/> - measured along the planet's surface - falls
    /// between <paramref name="minWorldRadius"/> and <paramref name="maxWorldRadius"/> world units.
    /// </summary>
    public static bool TryGetRandomPointNear(
        SphericalPlanet planet,
        Vector3 anchorWorldPosition,
        float minWorldRadius,
        float maxWorldRadius,
        out Vector3 direction)
    {
        direction = Vector3.up;
        if (planet == null)
            return false;

        Vector3 anchorUp = anchorWorldPosition - planet.Center;
        if (anchorUp.sqrMagnitude < 0.0001f)
            return false;
        anchorUp.Normalize();

        minWorldRadius = Mathf.Max(0f, minWorldRadius);
        maxWorldRadius = Mathf.Max(minWorldRadius, maxWorldRadius);

        if (maxWorldRadius <= 0.0001f)
        {
            direction = anchorUp;
            return true;
        }

        _rng ??= new System.Random();

        float planetRadius = Mathf.Max(0.01f, planet.Radius);
        float minAngleDeg = Mathf.Asin(Mathf.Clamp01(minWorldRadius / planetRadius)) * Mathf.Rad2Deg;
        float maxAngleDeg = Mathf.Asin(Mathf.Clamp01(maxWorldRadius / planetRadius)) * Mathf.Rad2Deg;

        // Sample uniformly by area across the [min, max] annulus (area ~ angle^2 for small
        // angles, same approximation as a flat disk) rather than by angle directly - otherwise
        // points would bunch up near the inner edge.
        float minArea = minAngleDeg * minAngleDeg;
        float maxArea = maxAngleDeg * maxAngleDeg;
        float angle = Mathf.Sqrt(minArea + (float)_rng.NextDouble() * (maxArea - minArea));
        float spin = (float)_rng.NextDouble() * 360f;

        direction = JitterDirection(anchorUp, angle, spin);
        return true;
    }

    /// <summary>Convenience overload sampling the full disk out to <paramref name="maxWorldRadius"/> (no inner exclusion).</summary>
    public static bool TryGetRandomPointNear(
        SphericalPlanet planet,
        Vector3 anchorWorldPosition,
        float maxWorldRadius,
        out Vector3 direction) =>
        TryGetRandomPointNear(planet, anchorWorldPosition, 0f, maxWorldRadius, out direction);

    static Vector3 JitterDirection(Vector3 dir, float angleDegrees, float spinDegrees)
    {
        Vector3 axis = Vector3.Cross(dir, Vector3.up);
        if (axis.sqrMagnitude < 0.0001f)
            axis = Vector3.Cross(dir, Vector3.right);
        axis = (Quaternion.AngleAxis(spinDegrees, dir) * axis).normalized;
        return (Quaternion.AngleAxis(angleDegrees, axis) * dir).normalized;
    }
}
