using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

#if UNITY_EDITOR
using UnityEditor;
#endif

public static class scr_System_FontManager
{
    static readonly HashSet<string> ProcessedLanguages = new HashSet<string>();

    const int Padding = 8;
    const int LargeCharsetThreshold = 800;
    const int AtlasSizeSmall = 2048;
    const int AtlasSizeLarge = 4096;
    const int PointSizeSmall = 90;
    const int PointSizeLarge = 36;

    static TMP_FontAsset ReferenceFont;

    static string FontAssetsPath
    {
        get { return Path.Combine(Directory.GetParent(Application.dataPath).FullName, "FontAssets"); }
    }

#if UNITY_EDITOR
    static string AssetsFontsPath
    {
        get { return Path.Combine(Application.dataPath, "Fonts"); }
    }
#endif

    /// <summary>
    /// Enumerates FontAssets/{lang}/*.ttf|*.otf and returns a usable TMP_FontAsset per file.
    /// Cheap: every font resolves instantly as an on-demand Dynamic asset. Identical code path
    /// in-editor and in a build - the ttf/otf files shipped next to the player are all we need.
    /// </summary>
    public static List<TMP_FontAsset> GetAvailableFonts(string lang)
    {
        var result = new List<TMP_FontAsset>();

        string charset = GetCharsetFor(lang);
        if (string.IsNullOrEmpty(charset)) return result;

        foreach (var fontFile in GetFontFiles(lang))
        {
            var fontAsset = GetOrCreateFontAsset(lang, fontFile, charset, ReferenceFont);
            if (fontAsset != null) result.Add(fontAsset);
        }
        return result;
    }

    public static TMP_FontAsset LoadFontAsset(string lang, string assetName)
    {
        string charset = GetCharsetFor(lang);
        if (string.IsNullOrEmpty(charset)) return null;

        foreach (var fontFile in GetFontFiles(lang))
        {
            if (AssetNameFor(fontFile, lang) == assetName) return GetOrCreateFontAsset(lang, fontFile, charset, ReferenceFont);
        }
        return null;
    }

    static int CurrentPointSizeFor(string charset)
    {
        return charset.Length > LargeCharsetThreshold ? PointSizeLarge : PointSizeSmall;
    }

    static string GetCharsetFor(string lang)
    {
        var charsets = scr_FontCharsetCollector.ExportCharsets();
        return charsets.TryGetValue(lang, out var charset) ? charset : "";
    }

    static List<string> GetFontFiles(string lang)
    {
        var files = new List<string>();
        string langFolder = Path.Combine(FontAssetsPath, lang);
        if (!Directory.Exists(langFolder)) return files;

        files.AddRange(Directory.GetFiles(langFolder, "*.ttf"));
        files.AddRange(Directory.GetFiles(langFolder, "*.otf"));
        return files;
    }

    /// <summary>
    /// Fired once per font FILE about to be resolved, before the (cheap) creation call, so a
    /// listener can update UI text and get a chance to render before the coroutine continues.
    /// </summary>
    public static event Action<string /*description*/, int /*index*/, int /*total*/> Observer_FontProgress;

    /// <summary>
    /// Boot-time entry point. Processes the "default" symbol fonts plus the single font file
    /// matching the player's saved selection for their current language. Everything resolves
    /// instantly as Dynamic assets - no baking, no caches.
    /// </summary>
    public static IEnumerator EnsureFontsCoroutine(TMP_FontAsset referenceFont, string currentLang, string currentFontSelection)
    {
        ReferenceFont = referenceFont;

        var pending = new List<(string lang, string charset, List<string> files)>();
        try
        {
            var charsets = scr_FontCharsetCollector.ExportCharsets();
            if (charsets.Count == 0) yield break;

            string root = FontAssetsPath;
            if (!Directory.Exists(root))
            {
                Debug.Log($"[FontGen] FontAssets folder not found at {root}, skipping font generation");
                yield break;
            }

            foreach (var lang in charsets.Keys)
            {
                if (!ProcessedLanguages.Add(lang)) continue;

                if (lang == scr_FontCharsetCollector.DefaultLanguageKey)
                {
                    var defaultFiles = GetFontFiles(lang);
                    if (defaultFiles.Count > 0) pending.Add((lang, charsets[lang], defaultFiles));
                    continue;
                }

                // Not the player's active language, or no override selected for it - nothing to
                // do at boot. (No override = "default"/fallback option in the UI, which already
                // renders fine via DefaultFallbackFont/LanguageFallbackFonts without any
                // generated font.)
                if (lang != currentLang || string.IsNullOrEmpty(currentFontSelection)) continue;

                var matchingFile = GetFontFiles(lang).Find(f => AssetNameFor(f, lang) == currentFontSelection);
                if (matchingFile == null)
                {
                    Debug.LogWarning($"[FontGen][{lang}] saved font selection '{currentFontSelection}' has no matching ttf/otf file, falling back");
                    continue;
                }
                pending.Add((lang, charsets[lang], new List<string> { matchingFile }));
            }
        }
        catch (Exception e)
        {
            // Mirrors the try/catch that used to wrap the whole EnsureFonts call at the caller -
            // a yield-returning call can't sit inside a try/catch, so the guard moves here,
            // covering the synchronous setup. Per-file exceptions are still caught below.
            Debug.LogError($"[FontGen] failed to prepare font generation: {e}");
            yield break;
        }

        int total = 0;
        foreach (var p in pending) total += p.files.Count;

        int startIndex = 0;
        foreach (var (lang, charset, files) in pending)
        {
            yield return GenerateLanguageCoroutine(lang, charset, files, referenceFont, startIndex, total);
            startIndex += files.Count;
        }
    }

    static IEnumerator GenerateLanguageCoroutine(string lang, string charset, List<string> fontFiles,
        TMP_FontAsset referenceFont, int startIndex, int total)
    {
        int generated = 0;
        int attempted = 0;
        bool savedToAssetDatabase = false;

        for (int i = 0; i < fontFiles.Count; i++)
        {
            var fontFile = fontFiles[i];
            attempted++;
            string assetName = AssetNameFor(fontFile, lang);

            Observer_FontProgress?.Invoke($"Resolving font: {assetName}...", startIndex + i, total);
            yield return null;

            try
            {
                var fontAsset = GetOrCreateFontAsset(lang, fontFile, charset, referenceFont);
                if (fontAsset == null) continue;

#if UNITY_EDITOR
                ClearFailureMarker(lang, fontAsset.name);
                if (SaveFontAssetEditorIfMissing(lang, fontAsset)) savedToAssetDatabase = true;
#else
                InjectRuntimeFallback(fontAsset);
#endif
                generated++;
            }
            catch (Exception e)
            {
                // One bad font must never take the rest of the language (or the whole session's
                // font generation) down with it.
                Debug.LogError($"[FontGen][{lang}] failed to process {fontFile}: {e}");
                MarkFailed(lang, assetName, charset, $"exception: {e.Message}");
            }
        }

        if (attempted > 0)
        {
            string verdict = generated == fontFiles.Count ? "resolved" : (generated > 0 ? "partially resolved" : "all failed (markers written)");
            Debug.Log($"[FontGen][{lang}] {verdict} {generated}/{fontFiles.Count} font asset(s) as dynamic fonts");
        }

#if UNITY_EDITOR
        if (savedToAssetDatabase)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
#endif
    }

    /// <summary>
    /// Creates a ready-to-use on-demand Dynamic TMP_FontAsset from a ttf/otf file - instantly,
    /// in-editor and in a build. The atlas starts empty; glyphs rasterize from the source file
    /// only as rendered text actually uses them (TMPro re-loads the face from the stored path
    /// whenever a missing character is requested).
    /// </summary>
    static TMP_FontAsset GetOrCreateFontAsset(string lang, string fontFile, string charset, TMP_FontAsset referenceFont)
    {
        string assetName = AssetNameFor(fontFile, lang);

        if (HasFailureMarker(lang, assetName, charset))
        {
            Debug.LogWarning($"[FontGen][{lang}] {assetName} failed to load previously for this charset, skipping (delete Assets/Fonts/{lang} to retry)");
            return null;
        }

        bool large = charset.Length > LargeCharsetThreshold;
        int atlasSize = large ? AtlasSizeLarge : AtlasSizeSmall;
        int pointSize = large ? PointSizeLarge : PointSizeSmall;

        TMP_FontAsset fontAsset;
        try
        {
            fontAsset = TMP_FontAsset.CreateFontAsset(
                fontFile, 0, pointSize, Padding, GlyphRenderMode.SDFAA,
                atlasSize, atlasSize);
        }
        catch (Exception e)
        {
            Debug.LogError($"[FontGen][{lang}] failed to create font asset from {fontFile}: {e.Message}");
            MarkFailed(lang, assetName, charset, $"exception: {e.Message}");
            return null;
        }

        if (fontAsset == null)
        {
            Debug.LogError($"[FontGen][{lang}] CreateFontAsset returned null for {fontFile}");
            MarkFailed(lang, assetName, charset, "CreateFontAsset returned null");
            return null;
        }

        fontAsset.name = assetName;
        CopyFallbacks(fontAsset, referenceFont);
        Debug.Log($"[FontGen][{lang}] {assetName} ready (on-demand DYNAMIC font, glyphs rasterize as text uses them)");

        return fontAsset;
    }

    static void CopyFallbacks(TMP_FontAsset fontAsset, TMP_FontAsset referenceFont)
    {
        if (referenceFont == null) return;

        if (scr_System_Serializer.current.DisableFontFallback) return;

        var source = referenceFont.fallbackFontAssetTable;
        if (source == null || source.Count == 0) return;

        var fallbacks = new List<TMP_FontAsset>();
        foreach (var f in source)
        {
            if (f == null || f == fontAsset) continue;
            fallbacks.Add(f);
        }
        fontAsset.fallbackFontAssetTable = fallbacks;
    }

    static string AssetNameFor(string ttfPath, string lang)
    {
        string name = Path.GetFileNameWithoutExtension(ttfPath);
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return $"{name}_{lang}";
    }

    [Serializable]
    class FailureMarkerData
    {
        public string charset;
        public string reason;
    }

#if UNITY_EDITOR
    static string AssetFileFor(string lang, string assetName)
    {
        return Path.Combine(AssetsFontsPath, lang, assetName + ".asset");
    }

    static string MarkerPath(string lang, string assetName)
    {
        return AssetFileFor(lang, assetName) + ".failed";
    }

    static void MarkFailed(string lang, string assetName, string charset, string reason)
    {
        try
        {
            Directory.CreateDirectory(Path.Combine(AssetsFontsPath, lang));
            File.WriteAllText(MarkerPath(lang, assetName), JsonConvert.SerializeObject(
                new FailureMarkerData { charset = charset, reason = reason }));
            Debug.LogWarning($"[FontGen][{lang}] failure marker written for {assetName} (delete Assets/Fonts/{lang} to retry)");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[FontGen][{lang}] could not write failure marker for {assetName}: {e.Message}");
        }
    }

    static bool HasFailureMarker(string lang, string assetName, string charset)
    {
        try
        {
            string marker = MarkerPath(lang, assetName);
            if (!File.Exists(marker)) return false;
            var data = JsonConvert.DeserializeObject<FailureMarkerData>(File.ReadAllText(marker));
            if (data == null) return false;
            return data.charset == charset;
        }
        catch (Exception)
        {
            return false;
        }
    }

    static void ClearFailureMarker(string lang, string assetName)
    {
        try
        {
            string marker = MarkerPath(lang, assetName);
            if (File.Exists(marker)) File.Delete(marker);
        }
        catch (Exception) { }
    }

    // Saves a small Dynamic shell .asset for editor convenience (manual assignment, inspector
    // browsing). The runtime never depends on these files - players resolve fonts from the
    // ttf/otf files directly. The atlas textures MUST be added as sub-assets: a serialized
    // TMP_FontAsset without its atlas texture breaks TMPro's TMP_PreBuildProcessor (it calls
    // atlasTexture.width on every Dynamic asset in the project during builds).
    static bool SaveFontAssetEditorIfMissing(string lang, TMP_FontAsset fontAsset)
    {
        string assetFile = AssetFileFor(lang, fontAsset.name);
        if (File.Exists(assetFile)) return false;

        Directory.CreateDirectory(Path.Combine(AssetsFontsPath, lang));
        string assetPath = $"Assets/Fonts/{lang}/{fontAsset.name}.asset";

        var textures = fontAsset.atlasTextures;
        for (int i = 0; i < textures.Length; i++)
        {
            if (textures[i] == null) continue;
            textures[i].name = i == 0 ? fontAsset.name + " Atlas" : $"{fontAsset.name} Atlas {i}";
        }

        AssetDatabase.CreateAsset(fontAsset, assetPath);

        if (fontAsset.material != null)
        {
            fontAsset.material.name = fontAsset.name + " Material";
            AssetDatabase.AddObjectToAsset(fontAsset.material, assetPath);
        }

        for (int i = 0; i < textures.Length; i++)
        {
            if (textures[i] == null) continue;
            AssetDatabase.AddObjectToAsset(textures[i], assetPath);
        }

        Debug.Log($"[FontGen][{lang}] saved {assetPath}");
        return true;
    }
#else
    static void MarkFailed(string lang, string assetName, string charset, string reason) { }
    static bool HasFailureMarker(string lang, string assetName, string charset) { return false; }
#endif

#if !UNITY_EDITOR
    static void InjectRuntimeFallback(TMP_FontAsset fontAsset)
    {
        var fallbacks = TMP_Settings.fallbackFontAssets;
        if (fallbacks == null)
        {
            fallbacks = new List<TMP_FontAsset>();
            TMP_Settings.fallbackFontAssets = fallbacks;
        }
        if (!fallbacks.Contains(fontAsset)) fallbacks.Add(fontAsset);
    }
#endif
}
