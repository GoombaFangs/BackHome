using System;
using UnityEngine;

/// <summary>
/// Snapshot of a painted <see cref="PlanetTileMap"/>: terrain ids, ground heights, and grid size.
/// Load and save these from the planet inspector — they are editor authoring assets, not runtime state.
/// </summary>
[CreateAssetMenu(menuName = "BackHome/Planet Tile Map Preset", fileName = "PlanetTilePreset")]
public class PlanetTileMapPreset : ScriptableObject
{
    [SerializeField] string displayName = "Tile Preset";
    [SerializeField] PlanetTileset tileset;
    [SerializeField, HideInInspector] int tilesAroundEquator = 72;
    [SerializeField, HideInInspector] int latitudeBands = 36;
    [SerializeField, HideInInspector] int longitudeBands = 72;
    [SerializeField, HideInInspector] int[] terrainIds = Array.Empty<int>();
    [SerializeField, HideInInspector] float[] groundHeights = Array.Empty<float>();
    [SerializeField, HideInInspector] int[] tileIndices = Array.Empty<int>();

    public string DisplayName =>
        string.IsNullOrWhiteSpace(displayName) ? name : displayName;

    public PlanetTileset Tileset => tileset;
    public int TilesAroundEquator => tilesAroundEquator;
    public int LatitudeBands => latitudeBands;
    public int LongitudeBands => longitudeBands;
    public int CellCount => latitudeBands * longitudeBands;

    public bool HasValidData()
    {
        int cells = latitudeBands * longitudeBands;
        return latitudeBands > 0
               && longitudeBands > 0
               && terrainIds != null
               && terrainIds.Length == cells;
    }

    public void SetDisplayName(string value)
    {
        displayName = string.IsNullOrWhiteSpace(value) ? name : value.Trim();
    }

    public void SetData(
        int aroundEquator,
        int latBands,
        int lonBands,
        int[] terrains,
        float[] heights,
        int[] visuals,
        PlanetTileset sourceTileset)
    {
        tilesAroundEquator = Mathf.Clamp(aroundEquator, 16, 256);
        latitudeBands = Mathf.Max(1, latBands);
        longitudeBands = Mathf.Max(1, lonBands);
        tileset = sourceTileset;

        int cells = latitudeBands * longitudeBands;
        terrainIds = CloneOrCreate(terrains, cells, 0);
        groundHeights = CloneOrCreate(heights, cells, PlanetTileMap.DefaultGroundHeight);
        tileIndices = CloneOrCreate(visuals, cells, 0);

        if (string.IsNullOrWhiteSpace(displayName))
            displayName = name;
    }

    public bool TryCopyData(
        out int aroundEquator,
        out int latBands,
        out int lonBands,
        out int[] terrains,
        out float[] heights,
        out int[] visuals)
    {
        aroundEquator = tilesAroundEquator;
        latBands = latitudeBands;
        lonBands = longitudeBands;
        terrains = null;
        heights = null;
        visuals = null;
        if (!HasValidData())
            return false;

        int cells = CellCount;
        terrains = (int[])terrainIds.Clone();
        heights = CloneOrCreate(groundHeights, cells, PlanetTileMap.DefaultGroundHeight);
        visuals = CloneOrCreate(tileIndices, cells, 0);
        return true;
    }

    public int ComputeContentHash()
    {
        return PlanetTileMap.ComputeMapContentHash(
            tilesAroundEquator,
            latitudeBands,
            longitudeBands,
            terrainIds,
            groundHeights);
    }

    static int[] CloneOrCreate(int[] source, int cells, int fill)
    {
        var copy = new int[cells];
        if (source != null && source.Length == cells)
            Array.Copy(source, copy, cells);
        else
            for (int i = 0; i < cells; i++)
                copy[i] = fill;
        return copy;
    }

    static float[] CloneOrCreate(float[] source, int cells, float fill)
    {
        var copy = new float[cells];
        if (source != null && source.Length == cells)
            Array.Copy(source, copy, cells);
        else
            for (int i = 0; i < cells; i++)
                copy[i] = fill;
        return copy;
    }
}
