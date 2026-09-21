using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

/// <summary>
/// Editor-only: for each font file listed in FontAssets/fonts.json, bakes TWO real saved assets
/// under Assets/Resources/Fonts:
///   {identity}.asset          - fully baked Static, covering the union of every language that
///                                references this file (scr_System_FontManager.GetUnionCharsetFor)
///   {identity}_dynamic.asset  - an empty-atlas, still-Dynamic shell (no characters pre-added)
///
/// Both ship with the build via Unity's normal Resources inclusion. scr_System_FontManager loads
/// both via Resources.Load and wires the dynamic shell in as the static asset's first fallback for
/// anything the static bake doesn't cover - the dynamic shell then grows on demand at runtime,
/// exactly like a plain in-memory Dynamic font would, the only difference being it's a real saved
/// asset instead of one freshly created via CreateFontAsset every session.
///
/// Baking BOTH as real, independently-owned assets (rather than creating the dynamic one at
/// runtime and cross-referencing it from the static asset's fallbackFontAssetTable) matters: a
/// persisted asset's serialized fields can only cleanly hold references to other real assets, not
/// to transient runtime-only objects - crossing that boundary is what previously produced a
/// "Type mismatch" in the Inspector and, when worked around via Object.Instantiate, a "Destroying
/// assets is not permitted" warning (the clone aliased the static asset's real atlas texture).
///
/// Requires Play Mode to already be running, so scr_FontCharsetCollector has been populated by
/// the normal boot flow (LocalizeDictionary loading dictionaries) - reuses that as-is instead of
/// duplicating dictionary-loading logic in an editor-only tool.
/// </summary>
public static class scr_FontStaticBaker
{
    const string ResourcesFontsFolder = "Assets/Resources/Fonts";

    const int Padding = 8;
    const int LargeCharsetThreshold = 800;
    const int AtlasSizeSmall = 2048;
    const int AtlasSizeLarge = 4096;
    const int PointSizeSmall = 90;
    const int PointSizeLarge = 36;

    enum BakeOutcome { Baked, Skipped, Failed }

    [MenuItem("Tools/Fonts/Rebuild Static Font Assets")]
    static void RebuildChanged() => Rebuild(force: false);

    [MenuItem("Tools/Fonts/Rebuild Static Font Assets (Force)")]
    static void RebuildForced() => Rebuild(force: true);

    static void Rebuild(bool force)
    {
        if (!EditorApplication.isPlaying)
        {
            EditorUtility.DisplayDialog("Rebuild Static Font Assets",
                "Enter Play Mode first - the bake needs the language dictionaries loaded so it knows what characters each font needs to cover.",
                "OK");
            return;
        }

        // Baking deletes and recreates the font assets in place, destroying the Material/Texture
        // sub-objects a currently-in-use font is still referencing. If some UI has already
        // resolved and rendered with a custom (non-fallback) font this session, that destroys a
        // live reference out from under it and crashes on the next layout pass. Only the default
        // fallback font is guaranteed nothing has resolved/rendered a custom font on top of yet.
        var centralControl = scr_System_CentralControl.current;
        if (centralControl != null)
        {
            string lang = centralControl.Language;
            TMP_FontAsset currentFont = centralControl.Font;
            TMP_FontAsset fallbackFont = centralControl.GetFallbackFont(lang);
            if (currentFont != fallbackFont)
            {
                EditorUtility.DisplayDialog("Rebuild Static Font Assets",
                    "A custom font is currently active for the active language, not the default fallback font. " +
                    "Rebaking would destroy the Material/Texture that font is still using and can crash live UI.\n\n" +
                    "Switch to the default font in Settings first, then rebuild.",
                    "OK");
                return;
            }
        }

        var manifest = scr_System_FontManager.LoadFontUses();
        var allFilenames = new HashSet<string>();
        foreach (var list in manifest.Values)
            foreach (var f in list) allFilenames.Add(f);

        if (allFilenames.Count == 0)
        {
            Debug.LogWarning("[FontBake] fonts.json has no entries, nothing to bake");
            return;
        }

        // AssetDatabase.CreateAsset/DeleteAsset (in SaveFontAsset, below) already immediately
        // register and write the specific path each one touches - no broad Refresh needed for
        // that. The only reason to Refresh at all is bootstrapping: a brand-new Resources/Fonts
        // folder that Directory.CreateDirectory just created isn't known to the AssetDatabase yet.
        // A project-wide Refresh (especially while in Play Mode) can cause Unity to reload/reimport
        // assets beyond the ones actually written this run, invalidating in-memory instances that
        // scene/Inspector references already point at - exactly what wiped editor assignments to
        // fonts that were skipped (unchanged) this run, just because something else in the same
        // batch got rebaked. Keeping this scoped to only-when-truly-needed avoids that collateral
        // damage.
        bool folderExisted = Directory.Exists(ResourcesFontsFolder);
        Directory.CreateDirectory(ResourcesFontsFolder);
        if (!folderExisted) AssetDatabase.Refresh();

        int baked = 0, skipped = 0, failed = 0;
        var failedIdentities = new List<string>();
        foreach (var filename in allFilenames)
        {
            switch (BakeOne(filename, force))
            {
                case BakeOutcome.Baked: baked++; break;
                case BakeOutcome.Skipped: skipped++; break;
                case BakeOutcome.Failed:
                    failed++;
                    failedIdentities.Add(scr_System_FontManager.AssetNameFor(filename));
                    break;
            }
        }

        if (baked > 0) AssetDatabase.SaveAssets();

        string summary = $"{baked} baked, {skipped} skipped (unchanged/failed-before), {failed} failed";
        Debug.Log($"[FontBake] done: {summary}");

        string message = summary;
        if (failedIdentities.Count > 0)
            message += "\n\nFailed:\n" + string.Join("\n", failedIdentities);
        EditorUtility.DisplayDialog("Rebuild Static Font Assets", message, "OK");
    }

    static BakeOutcome BakeOne(string filename, bool force)
    {
        string identity = scr_System_FontManager.AssetNameFor(filename);
        string filePath = Path.Combine(scr_System_FontManager.FontAssetsPath, filename);

        if (!File.Exists(filePath))
        {
            Debug.LogWarning($"[FontBake][{identity}] source file not found: {filePath}");
            return BakeOutcome.Failed;
        }

        string failMarkerPath = $"{ResourcesFontsFolder}/{identity}.failed";
        if (!force && File.Exists(failMarkerPath))
        {
            Debug.LogWarning($"[FontBake][{identity}] previously failed to bake, skipping (use Force to retry)");
            return BakeOutcome.Skipped;
        }

        string staticAssetPath = $"{ResourcesFontsFolder}/{identity}.asset";
        string dynamicAssetPath = $"{ResourcesFontsFolder}/{identity}_dynamic.asset";
        string signaturePath = $"{ResourcesFontsFolder}/{identity}.signature";

        string charset = scr_System_FontManager.GetUnionCharsetFor(filename);
        bool upToDate = File.Exists(staticAssetPath) && File.Exists(dynamicAssetPath)
            && File.Exists(signaturePath) && File.ReadAllText(signaturePath) == charset;

        if (!force && upToDate)
        {
            Debug.Log($"[FontBake][{identity}] unchanged, skipping");
            return BakeOutcome.Skipped;
        }

        bool large = charset.Length > LargeCharsetThreshold;
        int atlasSize = large ? AtlasSizeLarge : AtlasSizeSmall;
        int pointSize = large ? PointSizeLarge : PointSizeSmall;

        TMP_FontAsset staticAsset = TryCreate(filePath, pointSize, atlasSize, identity, failMarkerPath);
        if (staticAsset == null) return BakeOutcome.Failed;

        staticAsset.TryAddCharacters(charset, out string missing);
        if (!string.IsNullOrEmpty(missing))
            Debug.LogWarning($"[FontBake][{identity}] {missing.Length} character(s) not present in this font file");

        // Frozen after baking - scr_System_FontManager relies on this never accepting further
        // dynamic additions at runtime (TMPro itself refuses once AtlasPopulationMode is Static).
        staticAsset.atlasPopulationMode = AtlasPopulationMode.Static;

        // Empty shell, deliberately never TryAddCharacters'd here - stays Dynamic, grows on demand
        // at runtime for whatever the static bake didn't cover.
        TMP_FontAsset dynamicAsset = TryCreate(filePath, pointSize, atlasSize, identity, failMarkerPath);
        if (dynamicAsset == null) return BakeOutcome.Failed;

        SaveFontAsset(staticAsset, staticAssetPath, identity);
        SaveFontAsset(dynamicAsset, dynamicAssetPath, identity + "_dynamic");

        File.WriteAllText(signaturePath, charset);
        if (File.Exists(failMarkerPath)) File.Delete(failMarkerPath);

        Debug.Log($"[FontBake][{identity}] baked static ({staticAsset.characterTable.Count} glyph(s)) + dynamic shell -> {ResourcesFontsFolder}/");
        return BakeOutcome.Baked;
    }

    static TMP_FontAsset TryCreate(string filePath, int pointSize, int atlasSize, string identity, string failMarkerPath)
    {
        try
        {
            var fontAsset = TMP_FontAsset.CreateFontAsset(filePath, 0, pointSize, Padding, GlyphRenderMode.SDFAA, atlasSize, atlasSize);
            if (fontAsset == null)
            {
                Debug.LogError($"[FontBake][{identity}] CreateFontAsset returned null");
                File.WriteAllText(failMarkerPath, "CreateFontAsset returned null");
            }
            return fontAsset;
        }
        catch (Exception e)
        {
            Debug.LogError($"[FontBake][{identity}] failed to create font asset: {e.Message}");
            File.WriteAllText(failMarkerPath, e.Message);
            return null;
        }
    }

    // Delete any pre-existing asset first - CreateAsset would overwrite the root asset, but the
    // previous atlas texture/material sub-assets would otherwise linger orphaned.
    static void SaveFontAsset(TMP_FontAsset fontAsset, string assetPath, string baseName)
    {
        fontAsset.name = baseName;

        if (File.Exists(assetPath)) AssetDatabase.DeleteAsset(assetPath);

        var textures = fontAsset.atlasTextures;
        for (int i = 0; i < textures.Length; i++)
        {
            if (textures[i] == null) continue;
            textures[i].name = i == 0 ? baseName + " Atlas" : $"{baseName} Atlas {i}";
        }

        AssetDatabase.CreateAsset(fontAsset, assetPath);

        if (fontAsset.material != null)
        {
            fontAsset.material.name = baseName + " Material";
            AssetDatabase.AddObjectToAsset(fontAsset.material, assetPath);
        }

        for (int i = 0; i < textures.Length; i++)
        {
            if (textures[i] == null) continue;
            AssetDatabase.AddObjectToAsset(textures[i], assetPath);
        }
    }
}
