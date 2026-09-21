using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

public static class scr_System_FontManager
{
    static readonly HashSet<string> ProcessedLanguages = new HashSet<string>();

    // Keyed by identity (sanitized filename, no language suffix - font files are flat under
    // FontAssets/ now, so identity is unique by construction). Shared across every language that
    // references the same file: two languages pointing at the same ttf resolve to the exact same
    // font asset pair, never duplicated.
    static readonly Dictionary<string, TMP_FontAsset> ResolvedFonts = new Dictionary<string, TMP_FontAsset>();

    const int Padding = 8;
    const int LargeCharsetThreshold = 800;
    const int AtlasSizeSmall = 2048;
    const int AtlasSizeLarge = 4096;
    const int PointSizeSmall = 90;
    const int PointSizeLarge = 36;

    static TMP_FontAsset ReferenceFont;

    public static string FontAssetsPath
    {
        get { return Path.Combine(Directory.GetParent(Application.dataPath).FullName, "FontAssets"); }
    }

    static Dictionary<string, List<string>> _fontUses;

    /// <summary>
    /// FontAssets/fonts.json - Dictionary&lt;language, list of filenames&gt;. Filenames are flat
    /// (no per-language subfolders) and keep their extension, both so the same physical font file
    /// can be shared by several languages without duplication, and so a future non-ttf/otf format
    /// can be distinguished by extension without changing the manifest schema.
    /// </summary>
    public static Dictionary<string, List<string>> LoadFontUses()
    {
        if (_fontUses != null) return _fontUses;

        string path = Path.Combine(FontAssetsPath, "fonts.json");
        try
        {
            if (File.Exists(path))
            {
                _fontUses = JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(File.ReadAllText(path))
                    ?? new Dictionary<string, List<string>>();
            }
            else
            {
                Debug.LogWarning($"[FontGen] fonts.json not found at {path}, no custom fonts will be available");
                _fontUses = new Dictionary<string, List<string>>();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[FontGen] failed to read fonts.json: {e.Message}");
            _fontUses = new Dictionary<string, List<string>>();
        }
        return _fontUses;
    }

    static List<string> GetFontFilenames(string lang)
    {
        return LoadFontUses().TryGetValue(lang, out var list) ? list : new List<string>();
    }

    /// <summary>
    /// Every character needed by any language that references this filename, unioned together -
    /// the font is baked/sized once per file, not once per language. Duplicates across languages
    /// are harmless here: this is only ever used for a length-based atlas-size heuristic and as
    /// input to TryAddCharacters, which already skips characters it's seen (from source: a cheap
    /// dictionary-lookup check per character).
    /// </summary>
    public static string GetUnionCharsetFor(string filename)
    {
        var manifest = LoadFontUses();
        var charsets = scr_FontCharsetCollector.ExportCharsets();
        var sb = new StringBuilder();
        foreach (var kvp in manifest)
        {
            if (!kvp.Value.Contains(filename)) continue;
            if (charsets.TryGetValue(kvp.Key, out var charset)) sb.Append(charset);
        }
        return sb.ToString();
    }

    public static string AssetNameFor(string filename)
    {
        string name = Path.GetFileNameWithoutExtension(filename);
        foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    /// <summary>
    /// Enumerates the font files available for a language (per FontAssets/fonts.json) and returns
    /// a usable TMP_FontAsset per file - the pre-baked Static asset if one exists (Resources/Fonts),
    /// otherwise a bare on-demand Dynamic asset. Identical code path in-editor and in a build.
    /// </summary>
    public static List<TMP_FontAsset> GetAvailableFonts(string lang)
    {
        var result = new List<TMP_FontAsset>();
        foreach (var filename in GetFontFilenames(lang))
        {
            var fontAsset = GetOrCreateFontAsset(filename, ReferenceFont);
            if (fontAsset != null) result.Add(fontAsset);
        }
        return result;
    }

    public static TMP_FontAsset LoadFontAsset(string lang, string assetName)
    {
        foreach (var filename in GetFontFilenames(lang))
        {
            if (AssetNameFor(filename) == assetName) return GetOrCreateFontAsset(filename, ReferenceFont);
        }
        return null;
    }

    /// <summary>
    /// Fired once per font FILE about to be resolved, before the (cheap) resolution call, so a
    /// listener can update UI text and get a chance to render before the coroutine continues.
    /// </summary>
    public static event Action<string /*description*/, int /*index*/, int /*total*/> Observer_FontProgress;

    /// <summary>
    /// Boot-time entry point. Processes the "default" symbol fonts plus the single font file
    /// matching the player's saved selection for their current language. Resolution is a
    /// Resources.Load (for the Static asset, if one's been baked) plus an empty Dynamic asset
    /// creation - no baking happens here, ever.
    /// </summary>
    public static IEnumerator EnsureFontsCoroutine(TMP_FontAsset referenceFont, string currentLang, string currentFontSelection)
    {
        ReferenceFont = referenceFont;

        var pending = new List<(string lang, List<string> filenames)>();
        try
        {
            var charsets = scr_FontCharsetCollector.ExportCharsets();
            if (charsets.Count == 0) yield break;

            if (!Directory.Exists(FontAssetsPath))
            {
                Debug.Log($"[FontGen] FontAssets folder not found at {FontAssetsPath}, skipping font generation");
                yield break;
            }

            foreach (var lang in charsets.Keys)
            {
                if (!ProcessedLanguages.Add(lang)) continue;

                if (lang == scr_FontCharsetCollector.DefaultLanguageKey)
                {
                    var defaultFiles = GetFontFilenames(lang);
                    if (defaultFiles.Count > 0) pending.Add((lang, defaultFiles));
                    continue;
                }

                // Not the player's active language, or no override selected for it - nothing to
                // do at boot. (No override = "default"/fallback option in the UI, which already
                // renders fine via DefaultFallbackFont/LanguageFallbackFonts without any
                // generated font.)
                if (lang != currentLang || string.IsNullOrEmpty(currentFontSelection)) continue;

                var matchingFile = GetFontFilenames(lang).Find(f => AssetNameFor(f) == currentFontSelection);
                if (matchingFile == null)
                {
                    Debug.LogWarning($"[FontGen][{lang}] saved font selection '{currentFontSelection}' has no matching font file, falling back");
                    continue;
                }
                pending.Add((lang, new List<string> { matchingFile }));
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
        foreach (var p in pending) total += p.filenames.Count;

        int startIndex = 0;
        foreach (var (lang, filenames) in pending)
        {
            yield return GenerateForLanguageCoroutine(lang, filenames, startIndex, total);
            startIndex += filenames.Count;
        }
    }

    static IEnumerator GenerateForLanguageCoroutine(string lang, List<string> filenames, int startIndex, int total)
    {
        int resolved = 0;

        for (int i = 0; i < filenames.Count; i++)
        {
            string filename = filenames[i];
            string identity = AssetNameFor(filename);

            Observer_FontProgress?.Invoke($"Resolving font: {identity}...", startIndex + i, total);
            yield return null;

            try
            {
                var fontAsset = GetOrCreateFontAsset(filename, ReferenceFont);
                if (fontAsset != null) resolved++;
            }
            catch (Exception e)
            {
                // One bad font must never take the rest of the language (or the whole session's
                // font generation) down with it.
                Debug.LogError($"[FontGen][{lang}] failed to process {filename}: {e}");
            }
        }

        if (filenames.Count > 0)
        {
            string verdict = resolved == filenames.Count ? "resolved" : (resolved > 0 ? "partially resolved" : "all failed");
            Debug.Log($"[FontGen][{lang}] {verdict} {resolved}/{filenames.Count} font asset(s)");
        }
    }

    /// <summary>
    /// Resolves the usable font for a filename: the pre-baked Static asset from Resources/Fonts
    /// if the editor bake command has produced one, with a Dynamic asset (same ttf, on-demand,
    /// never eagerly baked) wired in as its first fallback for anything the bake didn't cover.
    /// Falls back to a bare Dynamic asset if no Static bake exists yet - never fails outright just
    /// because nobody's run the bake command. Cached by identity, so every language sharing this
    /// filename gets the exact same instances, resolved once.
    /// </summary>
    static TMP_FontAsset GetOrCreateFontAsset(string filename, TMP_FontAsset referenceFont)
    {
        string identity = AssetNameFor(filename);
        if (ResolvedFonts.TryGetValue(identity, out var cached)) return cached;

        string filePath = Path.Combine(FontAssetsPath, filename);
        if (!File.Exists(filePath))
        {
            Debug.LogWarning($"[FontGen] font file not found: {filePath}");
            return null;
        }

        TMP_FontAsset staticAsset = Resources.Load<TMP_FontAsset>("Fonts/" + identity);

        // Prefer the baked dynamic shell (a real saved asset, own independent atlas texture) over
        // creating one fresh here - a persisted asset's fallbackFontAssetTable can only cleanly
        // reference other real assets, not a transient runtime-only TMP_FontAsset. Referencing a
        // transient object from staticAsset's serialized field is what showed up as "Type
        // mismatch" in the Inspector.
        TMP_FontAsset dynamicAsset = Resources.Load<TMP_FontAsset>("Fonts/" + identity + "_dynamic");

        if (dynamicAsset == null)
        {
            // Bake command hasn't produced a shell for this font yet - fall back to an in-memory
            // one. Safe on its own (its atlas texture is a plain `new Texture2D`, never aliases a
            // persisted asset), but if staticAsset also exists, referencing this transient object
            // from its fallbackFontAssetTable will show the same "Type mismatch" cosmetic issue
            // until the bake command is run.
            string unionCharset = GetUnionCharsetFor(filename);
            bool large = unionCharset.Length > LargeCharsetThreshold;
            int atlasSize = large ? AtlasSizeLarge : AtlasSizeSmall;
            int pointSize = large ? PointSizeLarge : PointSizeSmall;

            try
            {
                dynamicAsset = TMP_FontAsset.CreateFontAsset(
                    filePath, 0, pointSize, Padding, GlyphRenderMode.SDFAA, atlasSize, atlasSize);
            }
            catch (Exception e)
            {
                Debug.LogError($"[FontGen] failed to create dynamic fallback for {filename}: {e.Message}");
                dynamicAsset = null;
            }
        }

        if (dynamicAsset == null && staticAsset == null)
        {
            Debug.LogError($"[FontGen] {identity} could not be resolved - no static bake and dynamic creation failed");
            return null;
        }

        TMP_FontAsset result;
        if (staticAsset != null)
        {
            var chain = new List<TMP_FontAsset>();
            if (dynamicAsset != null)
            {
                dynamicAsset.name = identity;
                chain.Add(dynamicAsset);
            }
            chain.AddRange(BuildFallbackList(referenceFont, staticAsset));
            staticAsset.fallbackFontAssetTable = chain;
            staticAsset.name = identity;
            result = staticAsset;
        }
        else
        {
            dynamicAsset.name = identity;
            dynamicAsset.fallbackFontAssetTable = BuildFallbackList(referenceFont, dynamicAsset);
            result = dynamicAsset;
        }

        ResolvedFonts[identity] = result;

#if !UNITY_EDITOR
        InjectRuntimeFallback(result);
#endif

        Debug.Log($"[FontGen] {identity} ready ({(staticAsset != null ? "static bake + dynamic fallback" : "dynamic only, no static bake found")})");

        return result;
    }

    static List<TMP_FontAsset> BuildFallbackList(TMP_FontAsset referenceFont, TMP_FontAsset exclude)
    {
        var result = new List<TMP_FontAsset>();
        if (referenceFont == null) return result;
        if (scr_System_Serializer.current.DisableFontFallback) return result;

        var source = referenceFont.fallbackFontAssetTable;
        if (source == null) return result;

        foreach (var f in source)
        {
            if (f == null || f == exclude) continue;
            result.Add(f);
        }
        return result;
    }

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
