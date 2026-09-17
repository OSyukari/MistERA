using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

public static class scr_FontGenDiagnostics
{
    const string TestString = "的一是在不了有和人这中大为上个国我以要他时来用们生到作地于出就分对成会可主发年动同工也能下过子说产种面而方后多定行学法所民得经ABCabc0123！？。，％（）【】《》—…";

    const string TestFileName = "testCharacters.txt";

    static string GetTestString(string langDir)
    {
        string file = Path.Combine(langDir, TestFileName);
        if (File.Exists(file))
        {
            try
            {
                string content = File.ReadAllText(file);
                if (!string.IsNullOrEmpty(content)) return content;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[FontDiag] failed to read {file}: {e.Message}");
            }
        }
        return TestString;
    }

    [MenuItem("Tools/FontGen/Diagnose Fonts")]
    public static void Diagnose()
    {
        string root = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "FontAssets");
        if (!Directory.Exists(root))
        {
            Debug.LogError($"[FontDiag] FontAssets folder not found at {root}");
            return;
        }

        foreach (var langDir in Directory.GetDirectories(root))
        {
            string lang = Path.GetFileName(langDir);
            string test = GetTestString(langDir);
            var files = Directory.GetFiles(langDir, "*.ttf", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(langDir, "*.otf", SearchOption.AllDirectories));
            foreach (var file in files)
            {
                TestFont(lang, file, test);
            }
        }
        Debug.Log("[FontDiag] diagnosis complete");
    }

    static void TestFont(string lang, string file, string test)
    {
        TMP_FontAsset fontAsset = null;
        try
        {
            fontAsset = TMP_FontAsset.CreateFontAsset(file, 0, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[FontDiag][{lang}] {Path.GetFileName(file)}: CreateFontAsset threw: {e.Message}");
            return;
        }

        if (fontAsset == null)
        {
            Debug.LogError($"[FontDiag][{lang}] {Path.GetFileName(file)}: face REJECTED by engine (LoadFontFace failed)");
            return;
        }

        string family = fontAsset.faceInfo.familyName;

        fontAsset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
        bool ok = fontAsset.TryAddCharacters(test, out string missing);
        fontAsset.atlasPopulationMode = AtlasPopulationMode.Static;

        int total = CountCodepoints(test);
        int miss = CountCodepoints(missing);

        if (ok) Debug.Log($"[FontDiag][{lang}] {Path.GetFileName(file)}: OK - family [{family}], support {total}/{total}");
        else Debug.LogError($"[FontDiag][{lang}] {Path.GetFileName(file)}: BROKEN - family [{family}], support {total - miss}/{total}, missing: {(missing.Length > 40 ? missing.Substring(0, 40) : missing)}");

        Object.DestroyImmediate(fontAsset);
    }

    static int CountCodepoints(string s)
    {
        if (string.IsNullOrEmpty(s)) return 0;
        int count = 0;
        for (int i = 0; i < s.Length; i++)
        {
            if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) i++;
            count++;
        }
        return count;
    }
}
