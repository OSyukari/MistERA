using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

/// <summary>
/// Static checks of the rules (Plan_ImageClassifier.md, ImageRuleLinter): load warnings, tags missing from the tagger vocabulary,
/// excluded tags that a rule needs, and thresholds the tag file cannot serve. Data-based checks (rules that matched 0 images)
/// are done by ImageAnalysis.
/// </summary>
public static class ImageRuleLinter
{
    /// <summary>Tag names of a WD14 model, from &lt;modelsFolder&gt;/&lt;model&gt;.csv (column 2). Null when the folder is not set or the file is missing.</summary>
    public static HashSet<string> LoadVocabulary(string modelsFolder, string model)
    {
        if (string.IsNullOrEmpty(modelsFolder)) return null;
        var csv = Path.Combine(modelsFolder, model + ".csv");
        if (!File.Exists(csv)) return null;
        var names = new HashSet<string>(StringComparer.Ordinal);
        bool header = true;
        foreach (var line in File.ReadLines(csv, Encoding.UTF8))
        {
            if (header) { header = false; continue; }
            // tag_id,name,category,count; names contain no commas in the WD14 lists, but quoted fields are handled anyway
            var name = SecondCsvField(line);
            if (!string.IsNullOrEmpty(name)) names.Add(name);
        }
        return names;
    }

    static string SecondCsvField(string line)
    {
        int first = line.IndexOf(',');
        if (first < 0) return null;
        if (first + 1 < line.Length && line[first + 1] == '"')
        {
            int end = line.IndexOf('"', first + 2);
            return end < 0 ? null : line.Substring(first + 2, end - first - 2);
        }
        int second = line.IndexOf(',', first + 1);
        return second < 0 ? line.Substring(first + 1) : line.Substring(first + 1, second - first - 1);
    }

    public static List<string> Lint(ImageRules rules, HashSet<string> vocabulary, ICollection<string> excludedTags, ImageTagHeader tagHeader)
    {
        var warnings = new List<string>(rules.loadWarnings);
        var tags = rules.CollectTags();

        foreach (var tag in tags.Where(t => t.StartsWith(ImageRules.RatingPrefix, StringComparison.Ordinal)))
            if (!ImageRules.RatingClasses.Contains(tag.Substring(ImageRules.RatingPrefix.Length)))
                warnings.Add($"unknown rating pseudo-tag [{tag}] (valid: {string.Join(", ", ImageRules.RatingClasses.Select(c => ImageRules.RatingPrefix + c))})");

        var vlmLabels = tags.Where(t => t.StartsWith(ImageRules.VlmPrefix, StringComparison.Ordinal)).Select(t => t.Substring(ImageRules.VlmPrefix.Length)).ToList();
        if (vlmLabels.Count > 0)
        {
            if (rules.emotionPass == null)
                warnings.Add($"rules use vlm: labels ({string.Join(", ", vlmLabels)}) but there is no emotion_pass block, so they never match");
            else
            {
                var offList = vlmLabels.Where(l => !rules.emotionPass.labels.Contains(l)).ToList();
                if (offList.Count > 0) warnings.Add($"vlm: labels not in emotion_pass.labels (the VLM is never offered them): {string.Join(", ", offList)}");
            }
        }

        if (vocabulary != null)
        {
            // face: tags are WD14 tags on the face crop, so they share the vocabulary; vlm: labels are checked above
            var unknown = tags.Where(t => !t.StartsWith(ImageRules.RatingPrefix, StringComparison.Ordinal) && !t.StartsWith(ImageRules.VlmPrefix, StringComparison.Ordinal))
                .Select(t => t.StartsWith(ImageRules.FacePrefix, StringComparison.Ordinal) ? t.Substring(ImageRules.FacePrefix.Length) : t)
                .Where(t => !vocabulary.Contains(t)).Distinct()
                .OrderBy(t => t, StringComparer.Ordinal).ToList();
            if (unknown.Count > 0)
                warnings.Add($"{unknown.Count} rule tags are not in the tagger vocabulary and can never match: {string.Join(", ", unknown)}");
        }

        if (excludedTags != null && excludedTags.Count > 0)
        {
            var needed = excludedTags.Where(t => tags.Contains(t) || tags.Contains(ImageRules.FacePrefix + t)).OrderBy(t => t, StringComparer.Ordinal).ToList();
            if (needed.Count > 0)
                warnings.Add($"excluded tags used by rules (they are never stored, so these clauses cannot see them): {string.Join(", ", needed)}");
        }

        if (tagHeader != null)
        {
            // only real thresholds matter; a near-miss band reaching below the stored range just sees fewer near misses
            float lowest = Math.Min(rules.settings.tagThreshold, rules.settings.variantThreshold);
            foreach (var c in rules.categories) if (c.threshold.HasValue) lowest = Math.Min(lowest, c.threshold.Value);
            if (lowest < tagHeader.threshold - 1e-6f)
                warnings.Add($"a rule threshold ({lowest:0.##}) is below the tag file threshold ({tagHeader.threshold:0.##}); tags in between were never stored. Re-tag with a lower threshold or raise the rule threshold.");
        }
        return warnings;
    }
}
