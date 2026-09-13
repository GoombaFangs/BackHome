using UnityEngine;

/// <summary>
/// Rebuilds land tiles and fits the concentric ocean sphere.
/// Leftover ridge/cliff art is removed. Ocean is not painted on the tilemap.
/// </summary>
public static class PlanetLayout
{
    static readonly string[] LegacyObjectNames =
    {
        "NyxaraA2TerrainStudy",
        "NyxaraA2NorthRidge",
        "NyxaraA2SouthCliff",
        "NyxaraA2Terrain"
    };

    public static int Rebuild(SphericalPlanet planet)
    {
        if (planet == null)
            return 0;

        int removed = StripLegacyArt(planet);

        PlanetTileMap tiles = planet.GetComponent<PlanetTileMap>();
        if (tiles != null && tiles.isActiveAndEnabled)
            tiles.RebuildVisuals();

        NyxaraSeaFit sea = planet.GetComponentInChildren<NyxaraSeaFit>(true);
        if (sea != null)
            sea.Fit();

        return removed;
    }

    public static int StripLegacyArt(SphericalPlanet planet)
    {
        if (planet == null)
            return 0;

        int removed = 0;
        Transform[] all = planet.GetComponentsInChildren<Transform>(true);
        for (int i = all.Length - 1; i >= 0; i--)
        {
            Transform t = all[i];
            if (t == null || t == planet.transform)
                continue;
            if (!IsLegacyName(t.name))
                continue;
            DestroyObject(t.gameObject);
            removed++;
        }

        return removed;
    }

    static bool IsLegacyName(string name)
    {
        for (int i = 0; i < LegacyObjectNames.Length; i++)
        {
            if (name == LegacyObjectNames[i])
                return true;
        }

        return false;
    }

    static void DestroyObject(GameObject go)
    {
        if (go == null)
            return;
        if (Application.isPlaying)
            Object.Destroy(go);
        else
            Object.DestroyImmediate(go);
    }
}
