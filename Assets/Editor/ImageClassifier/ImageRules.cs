using System.Collections.Generic;

/// <summary>
/// In-memory classification rules (Plan_ImageClassifier.md). Produced by ImageRulesJsonLoader from the rules JSON;
/// a later game-data loader (P4) only has to build the same model. Aliases are already expanded.
/// </summary>
public class ImageRules
{
    public string sourcePath;
    public ImageRuleSettings settings = new ImageRuleSettings();
    public List<CategoryRule> categories = new List<CategoryRule>();
    /// <summary>Name lists by their top-level key ("file_names", "portrait_emotions", ...). Categories pick one with nameSet.</summary>
    public Dictionary<string, List<NameRule>> nameSets = new Dictionary<string, List<NameRule>>();
    /// <summary>Shared variant table (file_name_variants): id -> one clause of tags.</summary>
    public Dictionary<string, RuleClause> variants = new Dictionary<string, RuleClause>();
    /// <summary>Tags the tagger never stores ("excluded_tags"); also stripped from existing records when the tag file is compacted.</summary>
    public HashSet<string> excludedTags = new HashSet<string>();
    /// <summary>Structural problems found while loading (unknown keys, unknown references...). Shown with the lint results.</summary>
    public List<string> loadWarnings = new List<string>();

    /// <summary>"emotion_pass" block; null when the rules have none (then the Tag task skips the pass).</summary>
    public EmotionPassSettings emotionPass;

    public const string DefaultNameSet = "file_names";
    public const string RatingPrefix = "rating:";
    /// <summary>WD14 score on the face crop (emotion pass).</summary>
    public const string FacePrefix = "face:";
    /// <summary>1 when the emotion pass VLM answered this label, else 0.</summary>
    public const string VlmPrefix = "vlm:";
    public static readonly string[] RatingClasses = { "general", "sensitive", "questionable", "explicit" };

    /// <summary>Every tag some rule refers to (rating pseudo-tags included).</summary>
    public HashSet<string> CollectTags()
    {
        var tags = new HashSet<string>();
        void Add(IEnumerable<RuleClause> clauses)
        {
            foreach (var c in clauses) foreach (var t in c.tags) tags.Add(t.tag);
        }
        foreach (var c in categories) { Add(c.all); Add(c.any); Add(c.none); }
        foreach (var set in nameSets.Values) foreach (var n in set) { Add(n.clauses); Add(n.none); }
        Add(variants.Values);
        return tags;
    }
}

/// <summary>The rules JSON "emotion_pass" block: which categories get the face-crop emotion pass, and what the VLM is asked.</summary>
public class EmotionPassSettings
{
    public HashSet<string> categories = new HashSet<string>();
    public List<string> labels = new List<string>();
    /// <summary>"{labels}" is replaced by the comma-separated labels.</summary>
    public string prompt = "";
}

/// <summary>The rules JSON "settings" block. All classification thresholds live here, not in the tool.</summary>
public class ImageRuleSettings
{
    /// <summary>A tag counts when its score reaches this (categories can override with their own threshold).</summary>
    public float tagThreshold = 0.35f;
    /// <summary>A variant (sleeping, rape, ...) applies when one of its tags reaches this.</summary>
    public float variantThreshold = 0.35f;
    /// <summary>A tag within this distance below the threshold that applies (category, tag or variant threshold) is a near miss
    /// and makes the result low confidence. Relative, so a strict category threshold (0.6) does not widen the band.</summary>
    public float nearMissBand = 0.15f;
    /// <summary>A tier claims the image only when its best match reaches this; otherwise later tiers get it.
    /// Lower than minScore so a moderate position hint (on_side 0.83 * 0.5) still names the image, with low confidence.</summary>
    public float tierMinScore = 0.3f;
    /// <summary>A name entry is confident only with at least this score...</summary>
    public float minScore = 0.5f;
    /// <summary>...and at least this lead over the runner-up in the same tier.</summary>
    public float margin = 0.15f;
}

public struct WeightedTag
{
    public string tag;
    public float weight;
}

/// <summary>One clause: OR over its tags. "source" is the clause as written, for messages.</summary>
public class RuleClause
{
    public string source;
    public List<WeightedTag> tags = new List<WeightedTag>();
}

public class CategoryRule
{
    public string id;
    /// <summary>Position in files_category, for messages.</summary>
    public int index;
    /// <summary>Consecutive matching categories with the same group compete by any-clause score.</summary>
    public string group;
    public List<RuleClause> all = new List<RuleClause>();
    public List<RuleClause> any = new List<RuleClause>();
    public List<RuleClause> none = new List<RuleClause>();
    /// <summary>Overrides settings.tagThreshold for this category's clauses.</summary>
    public float? threshold;
    /// <summary>A match stops classification: the image stays unmatched (e.g. an animal with no species tag).</summary>
    public bool hold;
    public string nameSet = ImageRules.DefaultNameSet;
    /// <summary>False: images are only assigned the category; no name is chosen and Classify keeps the original file name.</summary>
    public bool rename = true;
    /// <summary>Category id this one refines (horse_pov refines horse): an image sorted into that category's folder moves here when this
    /// rule's all/none clauses hold - its any clauses (the actor) are not needed, the folder already says so. Nowhere else.</summary>
    public string refines;
    /// <summary>False: low-confidence images of this category never move (portrait).</summary>
    public bool moveLowConfidence = true;
    /// <summary>True: hand-sorted only (human_female). The rules never choose it (its clauses are ignored), but its folder is a category
    /// folder: images put there keep the category - even when re-classifying images in category folders - and only get a name.</summary>
    public bool manual;
}

public class NameRule
{
    public string name;
    public int index;
    /// <summary>Lower wins (see ImageClassifierEngine). Decimal so a tier can be inserted between two others (3.5).</summary>
    public float tier = 1;
    /// <summary>AND over clauses; the first clause gives the score. No clauses = always matches with score 0 (fallbacks like sfw).</summary>
    public List<RuleClause> clauses = new List<RuleClause>();
    public List<RuleClause> none = new List<RuleClause>();
    /// <summary>Variant ids in priority order; the first that applies is appended as _&lt;id&gt;.</summary>
    public List<string> hasVariants = new List<string>();
    /// <summary>Null = every category.</summary>
    public HashSet<string> onlyInCategories;
    /// <summary>Only competes when no regular entry matched (other, sfw). Not held to minScore.</summary>
    public bool fallback;
    /// <summary>Overrides settings.tierMinScore for this entry: the score it needs to claim the image for its tier.</summary>
    public float? tierMinScore;
}
