using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;

/// <summary>
/// Editor-only: turns the atlas + grid manifest produced by
/// FontAssets/tools/extract_noto_color_emoji.py into a real TMP_SpriteAsset and registers
/// it in TMP_Settings.emojiFallbackTextAssets.
///
/// TextMeshProUGUI/TextMeshPro check TMP_Settings.emojiFallbackTextAssets BEFORE the normal
/// font/fallback-font search for any codepoint Unicode classifies as an Emoji (see
/// TMP_Text.emojiFallbackSupport, on by default per-object). Sprites resolved through that
/// path render untinted (TMP sets tint=false, spriteColor=white), so whatever color art
/// lives in the atlas shows as-is - unlike NotoSansSymbols/unifont, which are monochrome
/// outline fonts always tinted by the text's own color.
///
/// Builds the sprite asset entirely by hand (Sprite.Create + AddObjectToAsset) rather than
/// going through Assets/Create/TextMeshPro/Sprite Asset on a sliced texture: that path relies
/// on TextureImporter.spritesheet naming/rects surviving import so TMP_SpriteAssetMenu can
/// parse "0x&lt;HEX&gt;" sprite names into TMP_SpriteCharacter.unicode - in practice neither the
/// names nor the sliced rects came back matching what was assigned (sprites landed named
/// "EmojiAtlas_N" with rects that didn't correspond to the manifest either), so every
/// character ended up with the placeholder unicode 0xFFFE. Creating sprites directly from the
/// manifest's rects removes Unity's texture-slicing importer from the picture entirely.
/// </summary>
public static class scr_EmojiSpriteBaker
{
    const string AtlasPath = "Assets/Resources/Fonts/EmojiAtlas.png";
    const string GridManifestPath = "FontAssets/tools/EmojiAtlas.grid.json";
    const string SpriteAssetPath = "Assets/Resources/Fonts/EmojiAtlas.asset";

    [Serializable] class Cell { public string name; public float x, y, width, height; }
    [Serializable] class Grid { public float cellSize; public int cols, rows; public Cell[] cells; }

    [MenuItem("Tools/Fonts/Rebuild Emoji Sprite Asset")]
    static void Rebuild()
    {
        if (!File.Exists(GridManifestPath))
        {
            EditorUtility.DisplayDialog("Rebuild Emoji Sprite Asset",
                $"Grid manifest not found at {GridManifestPath}.\n\nRun FontAssets/tools/extract_noto_color_emoji.py first:\n" +
                $"python extract_noto_color_emoji.py NotoColorEmoji.ttf \"{AtlasPath}\" \"{GridManifestPath}\"",
                "OK");
            return;
        }

        var importer = AssetImporter.GetAtPath(AtlasPath) as TextureImporter;
        if (importer == null)
        {
            EditorUtility.DisplayDialog("Rebuild Emoji Sprite Asset",
                $"Atlas texture not found at {AtlasPath}. Point the python script's output there (or copy it in) first.",
                "OK");
            return;
        }

        var grid = JsonConvert.DeserializeObject<Grid>(File.ReadAllText(GridManifestPath));

        // Plain readable texture - Sprite.Create below builds every glyph's Sprite directly
        // from the manifest's rects, so no importer-driven slicing/naming is involved at all.
        importer.textureType = TextureImporterType.Default;
        importer.isReadable = true;
        importer.mipmapEnabled = false;
        importer.SaveAndReimport();

        var atlasTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(AtlasPath);

        if (File.Exists(SpriteAssetPath)) AssetDatabase.DeleteAsset(SpriteAssetPath);

        var spriteAsset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
        AssetDatabase.CreateAsset(spriteAsset, SpriteAssetPath);
        SetVersionField(spriteAsset, "1.1.0");
        spriteAsset.hashCode = TMP_TextUtilities.GetSimpleHashCode(spriteAsset.name);
        spriteAsset.spriteSheet = atlasTexture;

        // faceInfo.pointSize defaults to 0 on a hand-built asset. TMP_Text.cs:4142-4161 treats
        // pointSize <= 0 as "this sprite asset has no real metrics" and instead scales sprites
        // by fontFace.ascentLine / glyph.metrics.height - a formula meant for font-design-unit
        // scale (typically thousands of units per em), not raw pixel heights like our ~100-130px
        // glyphs, which blew them up massively. Setting pointSize > 0 switches to the simple,
        // correct branch: fontSize / pointSize * scale. Using cellSize as the reference point
        // size means a glyph renders at its native pixel size when fontSize == cellSize, scaling
        // proportionally at other sizes - consistent with how the glyph metrics below are defined.
        spriteAsset.faceInfo = new FaceInfo
        {
            pointSize = grid.cellSize,
            scale = 1f,
            ascentLine = grid.cellSize,
            descentLine = 0f,
            lineHeight = grid.cellSize
        };

        // Both properties' setters are internal to the TMP package - the getters hand back
        // the actual mutable backing list instead, so populate those in place.
        var glyphTable = spriteAsset.spriteGlyphTable;
        var characterTable = spriteAsset.spriteCharacterTable;

        for (int i = 0; i < grid.cells.Length; i++)
        {
            var c = grid.cells[i];
            var rect = new Rect(c.x, c.y, c.width, c.height);

            var sprite = Sprite.Create(atlasTexture, rect, new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
            sprite.name = c.name;
            AssetDatabase.AddObjectToAsset(sprite, spriteAsset);

            var glyph = new TMP_SpriteGlyph
            {
                index = (uint)i,
                // Standard "sits on the baseline" convention, like a normal glyph: bearingX=0
                // and advance=width so it doesn't creep left/overlap the preceding character,
                // bearingY=height so the glyph's bottom edge lands on the baseline instead of
                // straddling it.
                metrics = new GlyphMetrics(rect.width, rect.height, 0f, rect.height, rect.width),
                glyphRect = new GlyphRect(rect),
                scale = 1.0f,
                sprite = sprite
            };
            glyphTable.Add(glyph);

            var character = new TMP_SpriteCharacter(Convert.ToUInt32(c.name.Substring(2), 16), glyph)
            {
                name = c.name
            };
            characterTable.Add(character);
        }

        spriteAsset.UpdateLookupTables();

        var shader = Shader.Find("TextMeshPro/Sprite");
        var material = new Material(shader);
        material.SetTexture(ShaderUtilities.ID_MainTex, atlasTexture);
        material.name = spriteAsset.name + " Material";
        spriteAsset.material = material;
        AssetDatabase.AddObjectToAsset(material, spriteAsset);

        EditorUtility.SetDirty(spriteAsset);
        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(SpriteAssetPath);

        RegisterAsEmojiFallback(spriteAsset);

        Debug.Log($"[EmojiBake] {spriteAsset.spriteCharacterTable.Count} emoji baked into {SpriteAssetPath} and registered as an Emoji Fallback Text Asset.");
    }

    /// <summary>
    /// TMP_Asset.version's setter is internal, but leaving it unset is not a cosmetic gap:
    /// TMP_SpriteAsset.Awake() and UpdateLookupTables() both run UpgradeSpriteAsset() whenever
    /// (material != null && string.IsNullOrEmpty(m_Version)) - treating the asset as a legacy
    /// pre-1.1.0 one. UpgradeSpriteAsset() unconditionally clears spriteCharacterTable and
    /// spriteGlyphTable and rebuilds them from the old spriteInfoList field, which is empty on
    /// an asset built this way - wiping every entry back to nothing the first time anything
    /// touches the asset (e.g. TMP resolving an emoji, which calls UpdateLookupTables()).
    /// Setting the field directly via reflection is what a public version= would do anyway.
    /// </summary>
    static void SetVersionField(TMP_SpriteAsset spriteAsset, string version)
    {
        typeof(TMP_Asset).GetField("m_Version", BindingFlags.NonPublic | BindingFlags.Instance)
            .SetValue(spriteAsset, version);
    }

    static void RegisterAsEmojiFallback(TMP_SpriteAsset spriteAsset)
    {
        if (TMP_Settings.instance == null)
        {
            Debug.LogError("[EmojiBake] TMP_Settings.instance is null - import the TMP Essential Resources first.");
            return;
        }

        var list = TMP_Settings.emojiFallbackTextAssets ?? new List<TMP_Asset>();
        if (!list.Contains(spriteAsset)) list.Add(spriteAsset);
        TMP_Settings.emojiFallbackTextAssets = list;

        EditorUtility.SetDirty(TMP_Settings.instance);
        AssetDatabase.SaveAssets();
    }
}