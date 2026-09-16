using UnityEditor;
using UnityEngine;

/// <summary>
/// Wires Nyxara to the Handpainted Grass and Ground textures.
/// Those sheets are seamless fills, so the planet splat-blends them
/// instead of packing wang stamps.
/// </summary>
public static class NyxaraTileAtlasImporter
{
    const string TilesetPath = "Assets/Resources/Galaxy/Nyxara/Tiles/NyxaraTileset.asset";
    const float SplatTiling = 1f;
    const float BlendSharpness = 1.65f;

    static readonly string[] SplatPaths =
    {
        "Assets/Resources/Galaxy/Nyxara/Tiles/Textures/Grass/Grass_normal/Grass_normal_up.png",
        "Assets/Resources/Galaxy/Nyxara/Tiles/Textures/Dirt/dirt_normal/dirt_normal_up.png",
        "Assets/Resources/Galaxy/Nyxara/Tiles/Textures/Dirt/dirt_clay/dirt_clay_up.png",
        "Assets/Resources/Galaxy/Nyxara/Tiles/Textures/Grass/Grass_darked/Grass_darked_up.png"
    };

    struct TerrainSpec
    {
        public string id;
        public string displayName;
        public string zoneId;
        public Color previewColor;
    }

    static readonly TerrainSpec[] TerrainSpecs =
    {
        new TerrainSpec
        {
            id = "Grass",
            displayName = "Grass",
            zoneId = "grass",
            previewColor = new Color(0.38f, 0.62f, 0.18f)
        },
        new TerrainSpec
        {
            id = "Dirt",
            displayName = "Dirt",
            zoneId = "dirt",
            previewColor = new Color(0.55f, 0.38f, 0.22f)
        },
        new TerrainSpec
        {
            id = "Clay",
            displayName = "Clay",
            zoneId = "clay",
            previewColor = new Color(0.72f, 0.48f, 0.28f)
        },
        new TerrainSpec
        {
            id = "DarkGrass",
            displayName = "Dark Grass",
            zoneId = "dark_grass",
            previewColor = new Color(0.22f, 0.42f, 0.16f)
        }
    };

    [MenuItem("BackHome/Import Nyxara Tileset")]
    public static void Import()
    {
        var splats = new Texture2D[SplatPaths.Length];
        for (int i = 0; i < SplatPaths.Length; i++)
        {
            ConfigureSplatImporter(SplatPaths[i]);
            splats[i] = AssetDatabase.LoadAssetAtPath<Texture2D>(SplatPaths[i]);
            if (splats[i] == null)
            {
                EditorUtility.DisplayDialog(
                    "Nyxara Tileset",
                    "Missing handpainted texture:\n" + SplatPaths[i],
                    "OK");
                return;
            }
        }

        var entries = new PlanetTileset.Entry[TerrainSpecs.Length];
        var terrains = new PlanetTileset.Terrain[TerrainSpecs.Length];
        for (int t = 0; t < TerrainSpecs.Length; t++)
        {
            TerrainSpec spec = TerrainSpecs[t];
            string fillId = "Fill_" + spec.id;
            entries[t] = new PlanetTileset.Entry
            {
                id = fillId,
                column = 0,
                row = 0,
                cornerNW = t,
                cornerNE = t,
                cornerSW = t,
                cornerSE = t,
                walkable = true,
                zoneId = spec.zoneId
            };

            var mask = new string[PlanetTileset.MaskCount];
            for (int m = 0; m < mask.Length; m++)
                mask[m] = fillId;

            terrains[t] = new PlanetTileset.Terrain
            {
                id = spec.id,
                displayName = spec.displayName,
                previewColor = spec.previewColor,
                walkable = true,
                zoneId = spec.zoneId,
                fillAtlasId = fillId,
                maskToAtlasId = mask
            };
        }

        PlanetTileset tileset = AssetDatabase.LoadAssetAtPath<PlanetTileset>(TilesetPath);
        if (tileset == null)
        {
            tileset = ScriptableObject.CreateInstance<PlanetTileset>();
            AssetDatabase.CreateAsset(tileset, TilesetPath);
        }

        int tileSize = Mathf.Max(32, splats[0].width);
        tileset.Configure(splats[0], tileSize, entries, terrains);
        tileset.ConfigureSplat(splats, SplatTiling, BlendSharpness);
        EditorUtility.SetDirty(tileset);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        AssignToOpenPlanet();

        var summary = new System.Text.StringBuilder();
        summary.AppendLine("Nyxara now splat-blends the handpainted ground pack.");
        summary.AppendLine("Brushes: Grass, Dirt, Clay, Dark Grass.");
        summary.AppendLine("Paint on the planet, then click Bake Mesh if the view is stale.");
        Debug.Log("[BackHome] " + summary.ToString().Replace("\n", " "));
        EditorUtility.DisplayDialog("Nyxara Tileset", summary.ToString(), "OK");
    }

    static void AssignToOpenPlanet()
    {
        PlanetTileset tileset = AssetDatabase.LoadAssetAtPath<PlanetTileset>(TilesetPath);
        PlanetTileMap[] maps = Object.FindObjectsByType<PlanetTileMap>(FindObjectsInactive.Exclude);
        for (int i = 0; i < maps.Length; i++)
        {
            if (maps[i] == null)
                continue;
            Undo.RecordObject(maps[i], "Assign Nyxara Tileset");
            maps[i].SetTileset(tileset, refillBase: false);
            EditorUtility.SetDirty(maps[i]);
        }
    }

    static void ConfigureSplatImporter(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            return;

        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.mipmapEnabled = true;
        importer.filterMode = FilterMode.Bilinear;
        importer.anisoLevel = 8;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.npotScale = TextureImporterNPOTScale.ToNearest;
        importer.textureCompression = TextureImporterCompression.CompressedHQ;
        importer.maxTextureSize = 2048;
        importer.alphaIsTransparency = false;
        importer.spriteImportMode = SpriteImportMode.None;
        importer.SaveAndReimport();
    }
}
