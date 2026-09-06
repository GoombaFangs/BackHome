using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Imports a Nyxara wang tilesheet into a single PlanetTileset asset.
/// Classic layout is 5 columns × 4 rows. Cell size is detected from the PNG
/// (32, 64, 128, 256, or width/5 when the sheet is that 5×4 grid).
/// Skips black/empty cells. Classifies Grass / Shadow Grass corners; synthesizes
/// missing orientations via UV flips only when needed.
/// </summary>
public static class NyxaraTileAtlasImporter
{
    const string SourceTexture = "Assets/Resources/Galaxy/Nyxara/Tiles/Textures/NyxaraTileMap.png";
    const string TilesetPath = "Assets/Resources/Galaxy/Nyxara/Tiles/NyxaraTileset.asset";

    // Tuned for the current lime-grass / dark-green shadow-grass palette.
    static readonly Color32 GrassRgb = new Color32(123, 216, 60, 255);
    static readonly Color32 ShadowGrassRgb = new Color32(36, 151, 57, 255);

    struct SrcTile
    {
        public int col;
        public int row;
        public int dirtCorners;
    }

    struct AtlasTile
    {
        public string id;
        public int col;
        public int row;
        public bool flipU;
        public bool flipV;
        public int dirtCorners;
        public string zone;
    }

    [MenuItem("BackHome/Import Nyxara Tileset")]
    public static void Import()
    {
        Texture2D source = AssetDatabase.LoadAssetAtPath<Texture2D>(SourceTexture);
        if (source == null)
        {
            EditorUtility.DisplayDialog("Nyxara Tileset", $"Missing {SourceTexture}", "OK");
            return;
        }

        int tileSize = DetectTileSize(source.width, source.height);
        if (tileSize <= 0
            || source.width % tileSize != 0
            || source.height % tileSize != 0)
        {
            EditorUtility.DisplayDialog(
                "Nyxara Tileset",
                $"Tilesheet must be a regular grid (5×4 wang sheet, or cells of 16–256px).\nGot {source.width}×{source.height}.",
                "OK");
            return;
        }

        EnsureReadable(SourceTexture, true);
        source = AssetDatabase.LoadAssetAtPath<Texture2D>(SourceTexture);
        ConfigureSourceImporter(SourceTexture);

        int cols = source.width / tileSize;
        int rows = source.height / tileSize;
        Color32[] pixels = source.GetPixels32();

        var unique = new List<SrcTile>();
        var seen = new HashSet<string>();
        int skippedEmpty = 0;

        for (int row = 0; row < rows; row++)
        {
            for (int col = 0; col < cols; col++)
            {
                if (IsMostlyBlack(pixels, source.width, source.height, col, row, tileSize))
                {
                    skippedEmpty++;
                    continue;
                }

                string key = Convert.ToBase64String(HashTile(pixels, source.width, source.height, col, row, tileSize));
                if (!seen.Add(key))
                    continue;

                unique.Add(new SrcTile
                {
                    col = col,
                    row = row,
                    dirtCorners = SampleDirtCorners(pixels, source.width, source.height, col, row, tileSize)
                });
            }
        }

        if (unique.Count == 0)
        {
            EditorUtility.DisplayDialog("Nyxara Tileset", "No non-empty 32×32 tiles found in the sheet.", "OK");
            EnsureReadable(SourceTexture, false);
            return;
        }

        var byCorners = new Dictionary<int, SrcTile>();
        foreach (SrcTile t in unique)
        {
            if (!byCorners.ContainsKey(t.dirtCorners))
                byCorners[t.dirtCorners] = t;
        }

        var atlasTiles = new List<AtlasTile>();
        var cornersToId = new Dictionary<int, string>();

        void Register(string id, int corners, int col, int row, bool flipU, bool flipV, string zone)
        {
            if (cornersToId.ContainsKey(corners))
                return;
            if (atlasTiles.Exists(a => a.id == id))
                id = id + "_f";

            atlasTiles.Add(new AtlasTile
            {
                id = id,
                col = col,
                row = row,
                flipU = flipU,
                flipV = flipV,
                dirtCorners = corners,
                zone = zone
            });
            cornersToId[corners] = id;
        }

        foreach (KeyValuePair<int, SrcTile> kv in byCorners)
        {
            string zone = kv.Key == 0 ? "grass" : "shadow_grass";
            Register(RoleFromDirtCorners(kv.Key), kv.Key, kv.Value.col, kv.Value.row, false, false, zone);
        }

        bool progress = true;
        while (progress)
        {
            progress = false;
            var snapshot = new List<AtlasTile>(atlasTiles);
            foreach (AtlasTile tile in snapshot)
            {
                TrySynth(tile, false, true);
                TrySynth(tile, true, false);
                TrySynth(tile, true, true);
            }

            void TrySynth(AtlasTile baseTile, bool addFlipU, bool addFlipV)
            {
                int newCorners = FlipCorners(baseTile.dirtCorners, addFlipU, addFlipV);
                if (cornersToId.ContainsKey(newCorners))
                    return;

                bool fu = baseTile.flipU ^ addFlipU;
                bool fv = baseTile.flipV ^ addFlipV;
                Register(
                    RoleFromDirtCorners(newCorners),
                    newCorners,
                    baseTile.col,
                    baseTile.row,
                    fu,
                    fv,
                    newCorners == 0 ? "grass" : "shadow_grass");
                progress = true;
            }
        }

        SrcTile dirtSrc = byCorners.ContainsKey(0b1111) ? byCorners[0b1111] : unique[0];
        SrcTile grassSrc = byCorners.ContainsKey(0) ? byCorners[0] : unique[0];
        for (int c = 0; c < 16; c++)
        {
            if (cornersToId.ContainsKey(c))
                continue;
            SrcTile src = c == 0 ? grassSrc : dirtSrc;
            Register(RoleFromDirtCorners(c), c, src.col, src.row, false, false, c == 0 ? "grass" : "shadow_grass");
        }

        atlasTiles.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
        WriteTileset(source, tileSize, atlasTiles, cornersToId);

        EnsureReadable(SourceTexture, false);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        AssignToOpenPlanet();

        int natural = 0;
        for (int i = 0; i < atlasTiles.Count; i++)
        {
            if (!atlasTiles[i].flipU && !atlasTiles[i].flipV)
                natural++;
        }

        Debug.Log(
            $"[BackHome] Nyxara tilesheet {source.width}×{source.height} ({cols}×{rows} @ {tileSize}px). " +
            $"Unique={unique.Count}, empty skipped={skippedEmpty}, entries={atlasTiles.Count} (natural={natural}).");

        EditorUtility.DisplayDialog(
            "Nyxara Tileset",
            $"Imported tilesheet {source.width}×{source.height}\n" +
            $"Unique tiles: {unique.Count}\nEntries: {atlasTiles.Count}\n" +
            $"Assigned on open PlanetTileMap if present.",
            "OK");
    }

    static int FlipCorners(int corners, bool flipU, bool flipV)
    {
        bool nw = (corners & 8) != 0;
        bool ne = (corners & 4) != 0;
        bool se = (corners & 2) != 0;
        bool sw = (corners & 1) != 0;
        if (flipU)
        {
            Swap(ref nw, ref ne);
            Swap(ref sw, ref se);
        }
        if (flipV)
        {
            Swap(ref nw, ref sw);
            Swap(ref ne, ref se);
        }

        int bits = 0;
        if (nw) bits |= 8;
        if (ne) bits |= 4;
        if (se) bits |= 2;
        if (sw) bits |= 1;
        return bits;
    }

    static void Swap(ref bool a, ref bool b)
    {
        bool t = a;
        a = b;
        b = t;
    }

    static int DetectTileSize(int width, int height)
    {
        if (width <= 0 || height <= 0)
            return 0;

        // Classic Nyxara wang sheet: 5 columns × 4 rows, any cell size.
        if (width % 5 == 0 && height % 4 == 0 && width / 5 == height / 4)
            return width / 5;

        int[] candidates = { 256, 128, 96, 64, 48, 32, 16 };
        for (int i = 0; i < candidates.Length; i++)
        {
            int size = candidates[i];
            if (width % size == 0 && height % size == 0)
                return size;
        }

        return 0;
    }

    static void WriteTileset(Texture2D source, int tileSize, List<AtlasTile> tiles, Dictionary<int, string> cornerToAtlasId)
    {
        var entries = new PlanetTileset.Entry[tiles.Count];
        for (int i = 0; i < tiles.Count; i++)
        {
            AtlasTile t = tiles[i];
            entries[i] = new PlanetTileset.Entry
            {
                id = t.id,
                column = t.col,
                row = t.row,
                flipU = t.flipU,
                flipV = t.flipV,
                walkable = true,
                zoneId = t.zone
            };
        }

        var grass = new PlanetTileset.Terrain
        {
            id = "Grass",
            displayName = "Grass",
            previewColor = new Color(0.48f, 0.85f, 0.24f),
            walkable = true,
            zoneId = "grass",
            fillAtlasId = "Fill_Grass",
            maskToAtlasId = new string[PlanetTileset.MaskCount]
        };
        for (int i = 0; i < PlanetTileset.MaskCount; i++)
            grass.maskToAtlasId[i] = "Fill_Grass";

        var shadowGrass = new PlanetTileset.Terrain
        {
            id = "ShadowGrass",
            displayName = "Shadow Grass",
            previewColor = new Color(0.14f, 0.59f, 0.22f),
            walkable = true,
            zoneId = "shadow_grass",
            fillAtlasId = "Fill_ShadowGrass",
            maskToAtlasId = new string[PlanetTileset.MaskCount]
        };

        for (int mask = 0; mask < PlanetTileset.MaskCount; mask++)
        {
            bool N = (mask & PlanetTileset.BitN) != 0;
            bool E = (mask & PlanetTileset.BitE) != 0;
            bool S = (mask & PlanetTileset.BitS) != 0;
            bool W = (mask & PlanetTileset.BitW) != 0;
            int corners = 0;
            if (N && W) corners |= 8;
            if (N && E) corners |= 4;
            if (S && E) corners |= 2;
            if (S && W) corners |= 1;

            string atlasId;
            if (mask == 0 || corners == 0)
                atlasId = "Fill_ShadowGrass";
            else if (!cornerToAtlasId.TryGetValue(corners, out atlasId))
                atlasId = "Fill_ShadowGrass";

            shadowGrass.maskToAtlasId[mask] = atlasId;
        }

        PlanetTileset tileset = AssetDatabase.LoadAssetAtPath<PlanetTileset>(TilesetPath);
        if (tileset == null)
        {
            tileset = ScriptableObject.CreateInstance<PlanetTileset>();
            AssetDatabase.CreateAsset(tileset, TilesetPath);
        }

        tileset.Configure(source, tileSize, entries, new[] { grass, shadowGrass });
        EditorUtility.SetDirty(tileset);
    }

    static void AssignToOpenPlanet()
    {
        PlanetTileset tileset = AssetDatabase.LoadAssetAtPath<PlanetTileset>(TilesetPath);
        PlanetTileMap[] maps = UnityEngine.Object.FindObjectsByType<PlanetTileMap>(FindObjectsInactive.Exclude);
        for (int i = 0; i < maps.Length; i++)
        {
            if (maps[i] == null)
                continue;
            Undo.RecordObject(maps[i], "Assign Nyxara Tileset");
            maps[i].SetTileset(tileset, refillBase: true);
            EditorUtility.SetDirty(maps[i]);
        }
    }

    static string RoleFromDirtCorners(int corners)
    {
        int count =
            ((corners & 8) != 0 ? 1 : 0) +
            ((corners & 4) != 0 ? 1 : 0) +
            ((corners & 2) != 0 ? 1 : 0) +
            ((corners & 1) != 0 ? 1 : 0);
        if (count == 0) return "Fill_Grass";
        if (count == 4) return "Fill_ShadowGrass";
        if (count == 1)
        {
            if ((corners & 2) != 0) return "ShadowGrass_Tip_SE";
            if ((corners & 1) != 0) return "ShadowGrass_Tip_SW";
            if ((corners & 4) != 0) return "ShadowGrass_Tip_NE";
            return "ShadowGrass_Tip_NW";
        }
        if (count == 3)
        {
            if ((corners & 2) == 0) return "ShadowGrass_Notch_SE";
            if ((corners & 1) == 0) return "ShadowGrass_Notch_SW";
            if ((corners & 4) == 0) return "ShadowGrass_Notch_NE";
            return "ShadowGrass_Notch_NW";
        }
        if ((corners & 0b0011) == 0b0011) return "ShadowGrass_Edge_N";
        if ((corners & 0b1100) == 0b1100) return "ShadowGrass_Edge_S";
        if ((corners & 0b0110) == 0b0110) return "ShadowGrass_Edge_W";
        if ((corners & 0b1001) == 0b1001) return "ShadowGrass_Edge_E";
        if ((corners & 0b1010) == 0b1010) return "ShadowGrass_Diag_NWSE";
        return "ShadowGrass_Diag_NESW";
    }

    static bool IsMostlyBlack(Color32[] pixels, int width, int height, int col, int row, int size)
    {
        int baseX = col * size;
        int topY = height - row * size - 1;
        int black = 0;
        int n = 0;
        for (int ly = 0; ly < size; ly += 2)
        {
            int y = topY - ly;
            for (int lx = 0; lx < size; lx += 2)
            {
                Color32 c = pixels[y * width + (baseX + lx)];
                if (c.r < 25 && c.g < 25 && c.b < 25)
                    black++;
                n++;
            }
        }
        return black * 2 >= n;
    }

    static int SampleDirtCorners(Color32[] pixels, int width, int height, int col, int row, int size)
    {
        int baseX = col * size;
        int topY = height - row * size - 1;
        int corner = Mathf.Max(4, size / 4);
        int inset = Mathf.Max(1, size / 32);
        int far = Mathf.Max(inset, size - inset - corner);

        bool CornerDirt(int x0, int y0FromTop)
        {
            int dirt = 0;
            int n = 0;
            for (int y = y0FromTop; y < y0FromTop + corner && y < size; y++)
            {
                for (int x = x0; x < x0 + corner && x < size; x++)
                {
                    Color32 c = pixels[(topY - y) * width + (baseX + x)];
                    if (c.r < 25 && c.g < 25 && c.b < 25)
                        continue;
                    if (IsDirt(c)) dirt++;
                    n++;
                }
            }
            return n > 0 && dirt * 2 >= n;
        }

        int bits = 0;
        if (CornerDirt(inset, inset)) bits |= 8;
        if (CornerDirt(far, inset)) bits |= 4;
        if (CornerDirt(far, far)) bits |= 2;
        if (CornerDirt(inset, far)) bits |= 1;
        return bits;
    }

    static bool IsDirt(Color32 c)
    {
        int dg =
            (c.r - GrassRgb.r) * (c.r - GrassRgb.r) +
            (c.g - GrassRgb.g) * (c.g - GrassRgb.g) +
            (c.b - GrassRgb.b) * (c.b - GrassRgb.b);
        int dd =
            (c.r - ShadowGrassRgb.r) * (c.r - ShadowGrassRgb.r) +
            (c.g - ShadowGrassRgb.g) * (c.g - ShadowGrassRgb.g) +
            (c.b - ShadowGrassRgb.b) * (c.b - ShadowGrassRgb.b);
        return dd <= dg;
    }

    static byte[] HashTile(Color32[] pixels, int width, int height, int col, int row, int size)
    {
        int baseX = col * size;
        int topY = height - row * size - 1;
        var data = new byte[size * size * 3];
        int i = 0;
        for (int ly = 0; ly < size; ly++)
        {
            int y = topY - ly;
            for (int lx = 0; lx < size; lx++)
            {
                Color32 c = pixels[y * width + (baseX + lx)];
                data[i++] = c.r;
                data[i++] = c.g;
                data[i++] = c.b;
            }
        }
        using (var sha = SHA1.Create())
            return sha.ComputeHash(data);
    }

    static void EnsureReadable(string path, bool readable)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null || importer.isReadable == readable)
            return;
        importer.isReadable = readable;
        importer.SaveAndReimport();
    }

    static void ConfigureSourceImporter(string path)
    {
        var importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
            return;
        importer.textureType = TextureImporterType.Default;
        importer.mipmapEnabled = false;
        importer.sRGBTexture = true;
        importer.filterMode = FilterMode.Bilinear;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.maxTextureSize = 2048;
        importer.SaveAndReimport();
    }
}
