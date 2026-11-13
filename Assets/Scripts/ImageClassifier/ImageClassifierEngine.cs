using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;

public enum ClassificationStatus { Confident, LowConfidence, Unmatched }

/// <summary>Result for one image. "name" is the file name stem (entry name + _variant), e.g. doggystyle_sleeping.</summary>
public class ImageClassification
{
    public ImageTagRecord record;
    public ClassificationStatus status;
    public CategoryRule category;
    public NameRule nameRule;
    public string variant;
    public string name;
    public float score;
    /// <summary>Matched filename text, when editor filename-first naming selected this rule.</summary>
    public string filenameHint;
    public string categoryFilenameHint;
    public string state;
    public string stateSource;

    public void ComposeName()
    {
        name = nameRule == null ? null : nameRule.name
            + (variant == null ? "" : "_" + variant)
            + (state == null ? "" : "_" + state);
    }
    public NameRule runnerUp;
    public float runnerUpScore;
    /// <summary>Every matching entry of the tier that decided, best first (for the score breakdown in the reports).</summary>
    public readonly List<(NameRule rule, float score)> tierCandidates = new List<(NameRule rule, float score)>();
    /// <summary>Why the result is low confidence or unmatched (empty when confident).</summary>
    public readonly List<string> reasons = new List<string>();
    /// <summary>Diagnostic evidence that does not lower classification confidence.</summary>
    public readonly List<string> notes = new List<string>();

    public string CategoryId => category?.id;

    /// <summary>The image sits in a category folder, so that folder (a manual sort or an earlier pass) decided the category.</summary>
    public bool byFolder;
    /// <summary>When byFolder: the category the rules alone would choose (null = rules matched none); differs = a disagreement to review.</summary>
    public string rulesCategoryId;
    /// <summary>The user moved it out of the tool's category folder (record.manual "out"): never moved automatically.</summary>
    public bool keptOut;
    public bool FolderDisagrees => byFolder && !string.Equals(rulesCategoryId, CategoryId, StringComparison.OrdinalIgnoreCase);
    /// <summary>Whether a "include low confidence" Classify pass may move it.</summary>
    public bool MovableWhenLowConfidence => category == null || category.moveLowConfidence;
}

/// <summary>
/// Deterministic classifier over one tag record (Plan_ImageClassifier.md, Task 2 "Engine" and "Confidence").
/// Phase 1: tag categories in listed order, with optional filename-first name-set routing; grouped categories compete by score.
/// Phase 2: the category's name set; lowest matching tier wins, then highest first-clause score, then listed order;
/// then the first hasVariants id whose tags reach variantThreshold.
/// </summary>
public class ImageClassifierEngine
{
    readonly ImageRules rules;
    readonly ImageRuleSettings s;

    public ImageClassifierEngine(ImageRules rules)
    {
        this.rules = rules;
        s = rules.settings;
    }

    public static float TagScore(ImageTagRecord r, string tag)
    {
        if (tag.StartsWith(ImageRules.RatingPrefix, StringComparison.Ordinal))
            return r.rating.TryGetValue(tag.Substring(ImageRules.RatingPrefix.Length), out var rv) ? rv : 0f;
        if (tag.StartsWith(ImageRules.FacePrefix, StringComparison.Ordinal))
            return r.faceTags != null && r.faceTags.TryGetValue(tag.Substring(ImageRules.FacePrefix.Length), out var fv) ? fv : 0f;
        if (tag.StartsWith(ImageRules.VlmPrefix, StringComparison.Ordinal))
            return r.vlm != null && r.vlm == tag.Substring(ImageRules.VlmPrefix.Length) ? 1f : 0f;
        if (r.general.TryGetValue(tag, out var v)) return v;
        return r.character.TryGetValue(tag, out var c) ? c : 0f;
    }

    /// <summary>Sum of score * weight over the clause's tags that reach the threshold. 0 = clause not satisfied.</summary>
    static float ClauseScore(ImageTagRecord r, RuleClause clause, float threshold)
    {
        foreach (var requirement in clause.requires)
            if (ClauseScore(r, requirement, threshold) <= 0f) return 0f;
        float sum = 0f;
        foreach (var t in clause.tags)
        {
            float v = TagScore(r, t.tag);
            if (v >= threshold) sum += v * t.weight;
        }
        return sum;
    }

    /// <summary>Highest tag scoring in [low, high) - a near miss for this clause - or null.</summary>
    static (string tag, float score)? NearMiss(ImageTagRecord r, RuleClause clause, float low, float high)
    {
        (string, float)? best = null;
        foreach (var t in clause.tags)
        {
            float v = TagScore(r, t.tag);
            if (v >= low && v < high && (best == null || v > best.Value.Item2)) best = (t.tag, v);
        }
        return best;
    }

    float ThresholdOf(CategoryRule c) => c.threshold ?? s.tagThreshold;

    static bool SameAnyClauses(CategoryRule a, CategoryRule b) =>
        a.any.Select(c => c.source).SequenceEqual(b.any.Select(c => c.source));

    /// <summary>Category match score (sum of any-clause scores; 0 for rules without any), or null when it does not match.
    /// With a relaxed threshold, all/any are checked at that threshold (none keeps the real one) - used for near misses.</summary>
    float? MatchCategory(ImageTagRecord r, CategoryRule c, float? relaxedThreshold = null)
    {
        float th = relaxedThreshold ?? ThresholdOf(c);
        foreach (var clause in c.all) if (ClauseScore(r, clause, th) <= 0f) return null;
        float anyScore = 0f;
        if (c.any.Count > 0)
        {
            foreach (var clause in c.any) anyScore += ClauseScore(r, clause, th);
            if (anyScore <= 0f) return null;
        }
        foreach (var clause in c.none) if (ClauseScore(r, clause, ThresholdOf(c)) > 0f) return null;
        return anyScore;
    }

    public ImageClassification Classify(ImageTagRecord r, bool useFilenameHints = false)
    {
        var result = new ImageClassification { record = r };
        int matchedIndex = ClassifyCategory(r, result);
        if (matchedIndex < 0 && (result.category?.hold == true || result.reasons.Any(x => x.StartsWith("held by rule", StringComparison.Ordinal)))) return result;

        if (useFilenameHints)
        {
            var candidates = new List<ImageClassification>();
            foreach (var category in rules.categories.Where(c => c.filenameFirst && c.rename && !c.hold && !c.manual))
            {
                var candidate = new ImageClassification { record = r, category = category };
                if (!rules.nameSets.TryGetValue(category.nameSet, out var names)) continue;
                if (category.allowedNames != null) names = names.Where(n => category.allowedNames.Contains(n.name)).ToList();
                if (NameFromFilename(r, candidate, names)) candidates.Add(candidate);
            }
            // Resolve containing names across name sets too: golden shower is pee, not shower.
            candidates = candidates.Where(candidate => !candidates.Any(other => other.nameRule.name != candidate.nameRule.name
                && FilenameMatches(r, other.filenameHint).Cast<Match>().Any(outer =>
                    FilenameMatches(r, candidate.filenameHint).Cast<Match>().Any(inner => outer.Index <= inner.Index
                        && outer.Index + outer.Length >= inner.Index + inner.Length && outer.Length > inner.Length))))
                .ToList();
            if (candidates.Count > 1 && candidates.Select(c => c.nameRule.name).Distinct().Count() == 1)
            {
                // Shared names (pee) use the tag-selected context; without it prefer the nonsexual name set.
                var contextual = candidates.FirstOrDefault(c => c.category.nameSet == result.category?.nameSet)
                    ?? candidates.FirstOrDefault(c => c.category.nameSet != ImageRules.DefaultNameSet)
                    ?? candidates[0];
                candidates = new List<ImageClassification> { contextual };
            }
            // A source that names an unrelated action as well is ambiguous, not an expression override.
            if (candidates.Count == 1)
            {
                var candidate = candidates[0];
                // Actor/view routing is still tag-driven when it already selected the action name set.
                if (candidate.category.nameSet == ImageRules.DefaultNameSet && result.category?.nameSet == ImageRules.DefaultNameSet)
                {
                    NameInCategory(r, result, true);
                    AddCategoryNearMisses(r, result, matchedIndex);
                    return result;
                }
                bool conflict = false;
                if (result.category != null && result.category.nameSet != candidate.category.nameSet
                    && rules.nameSets.TryGetValue(result.category.nameSet, out var names))
                {
                    if (result.category.allowedNames != null) names = names.Where(n => result.category.allowedNames.Contains(n.name)).ToList();
                    var action = new ImageClassification { record = r, category = result.category };
                    conflict = NameFromFilename(r, action, names);
                }
                if (!conflict)
                {
                    candidate.categoryFilenameHint = candidate.filenameHint;
                    if (candidate.CategoryId != result.CategoryId)
                        candidate.notes.Add($"filename selects category {candidate.CategoryId}; tags suggested {result.CategoryId ?? "none"}");
                    ApplyState(r, candidate, true);
                    AddCategoryNearMisses(r, candidate, rules.categories.IndexOf(candidate.category));
                    return candidate;
                }
            }
        }
        if (matchedIndex < 0) return result;

        NameInCategory(r, result, useFilenameHints);
        AddCategoryNearMisses(r, result, matchedIndex);
        return result;
    }

    /// <summary>Whether a refining category (refines: X) applies to an image sorted into X: its all and none clauses, not its any
    /// clauses (the folder already establishes the actor). E.g. horse_pov needs only the pov tag for an image in horse/.</summary>
    public bool MatchesRefinement(ImageTagRecord r, CategoryRule c)
    {
        float th = ThresholdOf(c);
        foreach (var clause in c.all) if (ClauseScore(r, clause, th) <= 0f) return false;
        foreach (var clause in c.none) if (ClauseScore(r, clause, th) > 0f) return false;
        return true;
    }

    /// <summary>Classification with the category already decided (by the folder the image is in): only the name is chosen.</summary>
    public ImageClassification ClassifyInCategory(ImageTagRecord r, CategoryRule category, bool useFilenameHints = false)
    {
        return NameInCategory(r, new ImageClassification { record = r, category = category }, useFilenameHints);
    }

    ImageClassification NameInCategory(ImageTagRecord r, ImageClassification result, bool useFilenameHints)
    {
        if (!result.category.rename)
        {
            // category only (e.g. portrait): no name, the original file name is kept; confidence comes from the category alone
            result.status = result.reasons.Count == 0 ? ClassificationStatus.Confident : ClassificationStatus.LowConfidence;
            return result;
        }
        if (!rules.nameSets.TryGetValue(result.category.nameSet, out var nameSet))
        {
            result.status = ClassificationStatus.Unmatched;
            result.reasons.Add($"name set [{result.category.nameSet}] missing");
            return result;
        }
        if (result.category.allowedNames != null) nameSet = nameSet.Where(n => result.category.allowedNames.Contains(n.name)).ToList();
        if (!useFilenameHints || result.category.hold
            || !NameFromFilename(r, result, nameSet)) ClassifyName(r, result, nameSet, useFilenameHints);
        ApplyState(r, result, useFilenameHints);
        return result;
    }

    public void ApplyState(ImageTagRecord record, ImageClassification result, bool useFilenameHints = false)
    {
        result.state = null;
        result.stateSource = null;
        foreach (var state in rules.states)
        {
            var alias = useFilenameHints ? new[] { state.id }.Concat(state.filenameHints)
                .FirstOrDefault(a => FilenameMatches(record, a).Count > 0) : null;
            bool tagged = state.any.Any(c => ClauseScore(record, c, s.tagThreshold) > 0f);
            if (alias == null && !tagged) continue;
            result.state = state.id;
            result.stateSource = alias == null ? "rules/tags" : "filename ID " + alias;
            break;
        }
        result.ComposeName();
    }

    static MatchCollection FilenameMatches(ImageTagRecord record, string alias)
    {
        var source = string.IsNullOrEmpty(record.originalName) ? record.path : record.originalName;
        var stem = Path.GetFileNameWithoutExtension((source ?? "").Replace('\\', '/'));
        var tokens = Regex.Matches(alias ?? "", @"[\p{L}\p{N}]+").Cast<Match>().Select(m => Regex.Escape(m.Value));
        var body = string.Join(@"[^\p{L}\p{N}]*", tokens);
        return Regex.Matches(stem, body.Length == 0 ? "(?!)" : @"(?<![\p{L}\p{N}])" + body + @"(?![\p{L}\p{N}])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    bool NameFromFilename(ImageTagRecord r, ImageClassification result, List<NameRule> nameSet)
    {
        var matches = new List<(NameRule rule, Match match)>();
        foreach (var n in nameSet)
        {
            if (n.fallback || (n.onlyInCategories != null && !n.onlyInCategories.Contains(result.CategoryId))) continue;
            IEnumerable<string> aliases = new[] { n.name };
            if (rules.filenameHints.TryGetValue(n.name, out var extra)) aliases = aliases.Concat(extra);
            foreach (var alias in aliases)
            {
                foreach (Match match in FilenameMatches(r, alias))
                    matches.Add((n, match));
            }
        }
        // Prefer a complete id over a contained id (reverse_cowgirl vs cowgirl), not over an unrelated conflicting id.
        var specific = matches.Where(m => !matches.Any(other => other.rule.name != m.rule.name
            && other.match.Index <= m.match.Index && other.match.Index + other.match.Length >= m.match.Index + m.match.Length
            && other.match.Length > m.match.Length)).ToList();
        if (specific.Select(m => m.rule.name).Distinct().Count() != 1) return false;
        var chosen = specific[0];
        result.nameRule = chosen.rule;
        result.filenameHint = chosen.match.Value;
        result.score = 1f;
        result.variant = PickVariant(r, chosen.rule, result, true);
        result.name = result.variant == null ? chosen.rule.name : $"{chosen.rule.name}_{result.variant}";
        result.status = result.reasons.Count == 0 ? ClassificationStatus.Confident : ClassificationStatus.LowConfidence;
        return true;
    }

    /// <summary>Sets result.category, or marks the result unmatched. Returns the index of the rule that decided, or -1.</summary>
    int ClassifyCategory(ImageTagRecord r, ImageClassification result)
    {
        var cats = rules.categories;
        for (int i = 0; i < cats.Count; i++)
        {
            if (cats[i].manual) continue;   // hand-sorted only
            var score = MatchCategory(r, cats[i]);
            if (score == null) continue;

            var chosen = cats[i];
            if (!string.IsNullOrEmpty(chosen.group))
            {
                // every matching category of this consecutive group competes; highest score wins, listed order breaks ties
                var matches = new List<(CategoryRule rule, float score)> { (chosen, score.Value) };
                for (int j = i + 1; j < cats.Count && cats[j].group == chosen.group; j++)
                {
                    if (cats[j].manual) continue;
                    var other = MatchCategory(r, cats[j]);
                    if (other != null) matches.Add((cats[j], other.Value));
                }
                var winner = matches[0];
                foreach (var m in matches) if (m.score > winner.score) winner = m;
                chosen = winner.rule;
                // a close call only means something between different evidence (horse vs dog), not between a rule and its
                // specialisation with the same any clauses (horse_pov vs horse, decided by the extra all clause)
                foreach (var m in matches)
                    if (m.rule != winner.rule && winner.score - m.score < s.margin && !SameAnyClauses(m.rule, winner.rule))
                        result.reasons.Add($"category close call: {winner.rule.id} {winner.score:0.00} vs {m.rule.id} {m.score:0.00}");
            }
            if (chosen.hold)
            {
                result.status = ClassificationStatus.Unmatched;
                result.reasons.Add($"held by rule [{chosen.id}] (stays in place)");
                return -1;
            }
            result.category = chosen;
            return i;
        }
        result.status = ClassificationStatus.Unmatched;
        result.reasons.Add("no category matched");
        return -1;
    }

    /// <summary>Earlier near-matching categories lower confidence unless a filename-confirmed name opts into informational notes.</summary>
    void AddCategoryNearMisses(ImageTagRecord r, ImageClassification result, int matchedIndex)
    {
        for (int i = 0; i < matchedIndex; i++)
        {
            var c = rules.categories[i];
            if (c.hold || c.manual || c.id == result.category.id) continue;
            float low = ThresholdOf(c) - s.nearMissBand;
            if (MatchCategory(r, c, low) == null) continue;
            string evidence = "";
            foreach (var clause in c.all.Concat(c.any))
            {
                var nm = NearMiss(r, clause, low, ThresholdOf(c));
                if (nm != null) { evidence = $" ({nm.Value.tag} {nm.Value.score:0.00})"; break; }
            }
            string message = $"near-miss category {c.id}{evidence}";
            if (result.categoryFilenameHint != null || (result.filenameHint != null && result.category.filenameConfirmedNearMisses.Contains(c.id)))
                result.notes.Add(message + " (informational: name confirmed by filename)");
            else
            {
                result.reasons.Add(message);
                if (result.status == ClassificationStatus.Confident) result.status = ClassificationStatus.LowConfidence;
            }
        }
    }

    /// <summary>Fallback entries (other, sfw) and entries without clauses are not held to minScore.</summary>
    static bool IsFallback(NameRule n) => n.fallback || n.clauses.Count == 0;

    void ClassifyName(ImageTagRecord r, ImageClassification result, List<NameRule> nameSet, bool useFilenameHints)
    {
        var matches = new List<(NameRule rule, float score)>();
        foreach (var n in nameSet)
        {
            if (n.filenameOnly) continue;
            if (n.onlyInCategories != null && !n.onlyInCategories.Contains(result.category.id)) continue;
            var score = MatchName(r, n);
            if (score != null) matches.Add((n, score.Value));
        }
        // fallback entries only compete when no regular entry matched
        var candidates = matches.Where(m => !m.rule.fallback).ToList();
        if (candidates.Count == 0) candidates = matches;
        if (candidates.Count == 0)
        {
            result.status = ClassificationStatus.Unmatched;
            string hint = "";
            if (rules.emotionPass != null && rules.emotionPass.categories.Contains(result.category.id))
                hint = r.emotionError != null ? $" (emotion pass failed: {r.emotionError})"
                     : r.face == null ? " (emotion pass not run yet - run Tag)"
                     : r.face == false ? " (no face found)"
                     : r.vlm == null ? $" (VLM answered off-list: {r.vlmRaw})" : "";
            result.reasons.Add($"no {result.category.nameSet} entry matched in category {result.category.id}{hint}");
            return;
        }

        // the lowest tier whose best match reaches tierMinScore wins, so a stray hint in an earlier tier
        // (all_fours 0.42*0.5 -> doggystyle 0.21) cannot override strong evidence later (fellatio 0.98 -> oral);
        // without such evidence anywhere, the lowest matching tier wins. Below minScore the result is low confidence.
        var tiers = candidates.GroupBy(m => m.rule.tier).OrderBy(g => g.Key).ToList();
        var chosenTier = tiers.FirstOrDefault(g => g.Any(m => IsFallback(m.rule) || m.score >= (m.rule.tierMinScore ?? s.tierMinScore))) ?? tiers[0];
        var inTier = chosenTier.ToList();   // listed order kept, so ties go to the earlier entry
        var bestMatch = inTier[0];
        foreach (var m in inTier) if (m.score > bestMatch.score) bestMatch = m;
        (NameRule rule, float score)? secondMatch = null;
        foreach (var m in inTier)
            if (m.rule != bestMatch.rule && (secondMatch == null || m.score > secondMatch.Value.score)) secondMatch = m;

        result.tierCandidates.AddRange(inTier.OrderByDescending(m => m.score));  // stable: ties keep listed order
        var best = bestMatch.rule;
        float bestScore = bestMatch.score;
        var second = secondMatch?.rule;
        float secondScore = secondMatch?.score ?? 0f;

        result.nameRule = best;
        result.score = bestScore;
        result.runnerUp = second;
        result.runnerUpScore = secondScore;
        result.variant = PickVariant(r, best, result, useFilenameHints);
        result.name = result.variant == null ? best.name : $"{best.name}_{result.variant}";

        if (!IsFallback(best) && bestScore < s.minScore) result.reasons.Add($"weak evidence: {best.name} {bestScore:0.00} < {s.minScore:0.00}");
        if (second != null && bestScore - secondScore < s.margin)
            result.reasons.Add($"close runner-up: {best.name} {bestScore:0.00} vs {second.name} {secondScore:0.00}");
        result.status = result.reasons.Count == 0 ? ClassificationStatus.Confident : ClassificationStatus.LowConfidence;
    }

    /// <summary>The tags behind an entry's score: first-clause tags that reach tagThreshold, with their score and rule weight,
    /// highest contribution first. Their score * weight sums to the entry score.</summary>
    public List<(string tag, float score, float weight)> Contributions(ImageTagRecord r, NameRule n)
    {
        var list = new List<(string tag, float score, float weight)>();
        if (n.clauses.Count == 0) return list;
        foreach (var t in n.clauses[0].tags)
        {
            float v = TagScore(r, t.tag);
            if (v >= s.tagThreshold) list.Add((t.tag, v, t.weight));
        }
        list.Sort((a, b) => (b.score * b.weight).CompareTo(a.score * a.weight));
        return list;
    }

    /// <summary>First-clause score, or null when an AND clause is unmet or a none clause hits.</summary>
    float? MatchName(ImageTagRecord r, NameRule n)
    {
        foreach (var clause in n.none) if (ClauseScore(r, clause, s.tagThreshold) > 0f) return null;
        if (n.clauses.Count == 0) return 0f;
        float first = ClauseScore(r, n.clauses[0], s.tagThreshold);
        if (first <= 0f) return null;
        for (int i = 1; i < n.clauses.Count; i++) if (ClauseScore(r, n.clauses[i], s.tagThreshold) <= 0f) return null;
        return first;
    }

    /// <summary>First variant with tag evidence or editor-enabled filename evidence. Variants before it, or all when none applied,
    /// with a tag just under the threshold are reported as near misses.</summary>
    string PickVariant(ImageTagRecord r, NameRule n, ImageClassification result, bool useFilenameHints)
    {
        // Shared leaf clauses let filename evidence participate in compound variants too (rape + filename cumshot).
        var filenameClauses = new HashSet<string>();
        if (useFilenameHints)
            foreach (var hint in rules.filenameVariantHints)
                if (rules.variants.TryGetValue(hint.Key, out var hinted) && hinted.requires.Count == 0
                    && hint.Value.Any(a => FilenameMatches(r, a).Count > 0)) filenameClauses.Add(hinted.source);
        bool Matches(RuleClause clause, float threshold) => filenameClauses.Contains(clause.source)
            || (clause.requires.All(part => Matches(part, threshold))
                && clause.tags.Any(t => t.weight > 0f && TagScore(r, t.tag) >= threshold));
        foreach (var id in n.hasVariants)
        {
            if (!rules.variants.TryGetValue(id, out var clause)) continue;
            if (useFilenameHints && rules.filenameVariantHints.TryGetValue(id, out var aliases)
                && aliases.Any(a => FilenameMatches(r, a).Count > 0)) return id;
            if (Matches(clause, s.variantThreshold)) return id;
            if (id == n.defaultVariant) continue;
            float low = s.variantThreshold - s.nearMissBand;
            if (!Matches(clause, low)) continue;
            foreach (var part in new[] { clause }.Concat(clause.requires))
            {
                var nm = NearMiss(r, part, low, s.variantThreshold);
                if (nm == null) continue;
                result.reasons.Add($"near-miss variant {id} ({nm.Value.tag} {nm.Value.score:0.00})");
                break;
            }
        }
        return n.defaultVariant;
    }
}
