using UnityEngine;

/// <summary>
/// 4-bit NESW blob autotile against <see cref="PlanetTileset"/>.
/// </summary>
public static class PlanetBlobAutotile
{
    public static void ResolveAll(PlanetTileMap map)
    {
        if (map == null || !map.HasValidMap())
            return;

        int cells = map.CellCount;
        var visuals = new int[cells];
        int fallback = map.Tileset != null ? Mathf.Max(0, map.Tileset.IndexOfId("Fill_Grass")) : 0;

        for (int lat = 0; lat < map.LatitudeBands; lat++)
        {
            for (int lon = 0; lon < map.LongitudeBands; lon++)
            {
                visuals[lat * map.LongitudeBands + lon] = ResolveVisualIndex(map, lat, lon, fallback);
            }
        }

        map.SetVisualTiles(visuals, rebuild: true);
    }

    public static void ResolveRegion(PlanetTileMap map, int centerLat, int centerLon, int radius)
    {
        if (map == null || !map.HasValidMap())
            return;

        int fallback = map.Tileset != null ? Mathf.Max(0, map.Tileset.IndexOfId("Fill_Grass")) : 0;
        radius = Mathf.Max(0, radius);
        bool any = false;

        for (int dLat = -radius; dLat <= radius; dLat++)
        {
            int lat = centerLat + dLat;
            if (lat < 0 || lat >= map.LatitudeBands)
                continue;

            for (int dLon = -radius; dLon <= radius; dLon++)
            {
                int lon = Mod(centerLon + dLon, map.LongitudeBands);
                int idx = ResolveVisualIndex(map, lat, lon, fallback);
                if (map.SetVisualSilent(lat, lon, idx))
                    any = true;
            }
        }

        if (any)
            map.RebuildVisuals();
    }

    public static int ResolveVisualIndex(PlanetTileMap map, int lat, int lon, int fallback)
    {
        PlanetTileset tileset = map.Tileset;
        if (tileset == null)
            return fallback;

        int self = map.GetTerrain(lat, lon);
        int mask = 0;
        if (IsSame(map, lat + 1, lon, self)) mask |= PlanetTileset.BitN;
        if (IsSame(map, lat, lon + 1, self)) mask |= PlanetTileset.BitE;
        if (IsSame(map, lat - 1, lon, self)) mask |= PlanetTileset.BitS;
        if (IsSame(map, lat, lon - 1, self)) mask |= PlanetTileset.BitW;

        string atlasId = tileset.ResolveAtlasId(self, mask);
        int idx = tileset.IndexOfId(atlasId);
        return idx >= 0 ? idx : fallback;
    }

    public static void PaintTerrain(
        PlanetTileMap map,
        int centerLat,
        int centerLon,
        int terrainIndex,
        int radiusCells,
        bool rebuild)
    {
        if (map == null)
            return;

        bool changed = map.PaintTerrainBrush(centerLat, centerLon, terrainIndex, radiusCells, rebuild: false);
        if (!changed && rebuild)
            return;

        int pad = Mathf.Max(0, radiusCells) + 1;
        ResolveRegion(map, centerLat, centerLon, pad);
    }

    public static void FloodFill(PlanetTileMap map, int startLat, int startLon, int newTerrain)
    {
        if (map == null || !map.HasValidMap() || map.Tileset == null)
            return;

        int target = map.GetTerrain(startLat, startLon);
        if (target == newTerrain)
            return;

        int cells = map.CellCount;
        var visited = new bool[cells];
        var stackLat = new int[cells];
        var stackLon = new int[cells];
        int sp = 0;
        stackLat[sp] = startLat;
        stackLon[sp] = startLon;
        sp++;

        int minLat = startLat, maxLat = startLat;
        int painted = 0;

        while (sp > 0)
        {
            sp--;
            int lat = stackLat[sp];
            int lon = stackLon[sp];
            if (lat < 0 || lat >= map.LatitudeBands)
                continue;
            lon = Mod(lon, map.LongitudeBands);
            int cell = lat * map.LongitudeBands + lon;
            if (visited[cell])
                continue;
            visited[cell] = true;
            if (map.GetTerrain(lat, lon) != target)
                continue;

            map.SetTerrainSilent(lat, lon, newTerrain);
            painted++;
            if (lat < minLat) minLat = lat;
            if (lat > maxLat) maxLat = lat;

            Push(lat + 1, lon);
            Push(lat - 1, lon);
            Push(lat, lon + 1);
            Push(lat, lon - 1);
        }

        if (painted == 0)
            return;

        int fallback = map.Tileset != null ? Mathf.Max(0, map.Tileset.IndexOfId("Fill_Grass")) : 0;
        for (int lat = Mathf.Max(0, minLat - 1); lat <= Mathf.Min(map.LatitudeBands - 1, maxLat + 1); lat++)
        {
            for (int lon = 0; lon < map.LongitudeBands; lon++)
            {
                int idx = ResolveVisualIndex(map, lat, lon, fallback);
                map.SetVisualSilent(lat, lon, idx);
            }
        }

        map.RebuildVisuals();

        void Push(int la, int lo)
        {
            if (sp >= cells)
                return;
            stackLat[sp] = la;
            stackLon[sp] = lo;
            sp++;
        }
    }

    public static void GenerateContinents(PlanetTileMap map, int seed = 11)
    {
        if (map == null || map.Tileset == null || map.Tileset.TerrainCount < 2)
            return;

        int grass = map.Tileset.BaseTerrainIndex;
        int overlay = Mathf.Min(1, map.Tileset.TerrainCount - 1);
        int latBands = map.LatitudeBands;
        int lonBands = map.LongitudeBands;
        var rng = new System.Random(seed);

        for (int lat = 0; lat < latBands; lat++)
        {
            float lat01 = lat / (float)Mathf.Max(1, latBands - 1);
            for (int lon = 0; lon < lonBands; lon++)
            {
                float lon01 = lon / (float)lonBands;
                float n1 = ValueNoise(lon01 * 3.1f + seed * 0.17f, lat01 * 2.4f);
                float n2 = ValueNoise(lon01 * 7.3f - seed * 0.11f, lat01 * 5.1f);
                float n = n1 * 0.65f + n2 * 0.35f;
                float band = 1f - Mathf.Abs(lat01 - 0.5f) * 1.5f;
                int t = grass;
                if (band > 0.12f && n > 0.48f)
                    t = overlay;
                else if (n2 > 0.78f && band > 0.05f)
                    t = overlay;
                else if (rng.NextDouble() > 0.97 && band > 0.2f)
                    t = overlay;
                map.SetTerrainSilent(lat, lon, t);
            }
        }

        ResolveAll(map);
    }

    static bool IsSame(PlanetTileMap map, int lat, int lon, int self)
    {
        if (lat < 0 || lat >= map.LatitudeBands)
            return false;
        lon = Mod(lon, map.LongitudeBands);
        return map.GetTerrain(lat, lon) == self;
    }

    static int Mod(int value, int modulus)
    {
        int m = value % modulus;
        return m < 0 ? m + modulus : m;
    }

    static float ValueNoise(float x, float y)
    {
        float s = Mathf.Sin(x * 12.9898f + y * 78.233f) * 43758.5453f;
        return s - Mathf.Floor(s);
    }
}
