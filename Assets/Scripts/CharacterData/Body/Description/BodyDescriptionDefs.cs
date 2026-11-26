using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// Prose description of one organ (Docs/Body/Plan_CharacterDescription.md). Referenced by BodyInternal_Base.descriptionProfile.
/// <br/>Base text: the dictionary key "&lt;current trait of baseTraitGroup&gt;_bodydesc" if it exists, else the first matching base row.
/// Every $group$ placeholder of the base text is filled by the first matching row of that group (own groups replace
/// inherited ruleset groups of the same name). Missing / unmatched group = empty. All text keys go through
/// CharacterDescriptionUtility.Text, so a personality can replace any of them ("key@personalityID").
/// </summary>
[System.Serializable]
public class BodyDescriptionProfile
{
    public string id = "";
    /// <summary> ids of BodyDescriptionRuleset applied first, in order (rulesets don't inherit) </summary>
    public List<string> inheritRulesets = new List<string>();
    /// <summary> exposed (default): only while the organ is uncovered, or Xray / Debug. always: regardless of clothing </summary>
    public string visibility = "exposed";
    /// <summary> trait group whose current trait picks the base text key "&lt;traitID&gt;_bodydesc" </summary>
    public string baseTraitGroup = "";
    public List<BodyDescriptionRow> @base = new List<BodyDescriptionRow>();
    public Dictionary<string, List<BodyDescriptionRow>> groups = new Dictionary<string, List<BodyDescriptionRow>>();

    [JsonIgnore] public bool IsAlwaysVisible { get { return visibility == "always"; } }
}

/// <summary> fragment groups shared by several profiles (BodyDescriptionProfile.inheritRulesets) </summary>
[System.Serializable]
public class BodyDescriptionRuleset
{
    public string id = "";
    public Dictionary<string, List<BodyDescriptionRow>> groups = new Dictionary<string, List<BodyDescriptionRow>>();
}

[System.Serializable]
public class BodyDescriptionRange
{
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public float? min = null;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public float? max = null;

    public bool Contains(float v)
    {
        if (min.HasValue && v < min.Value) return false;
        if (max.HasValue && v > max.Value) return false;
        return true;
    }

    public override string ToString()
    {
        if (min.HasValue && max.HasValue) return min.Value + "–" + max.Value;
        if (min.HasValue) return "≥" + min.Value;
        if (max.HasValue) return "≤" + max.Value;
        return "any";
    }
}

/// <summary>
/// One candidate text. All conditions optional, empty = always. Within a group the first matching row wins.
/// Text placeholders: $item$ (matched equipment), $content$ (matching contents, joined), $name$ (owner first name).
/// </summary>
[System.Serializable]
public class BodyDescriptionRow
{
    public string id = "";
    /// <summary> dictionary key of the fragment; empty = the row matches and writes nothing </summary>
    public string text = "";

    /// <summary> trait ids the owner has, as seen - all of them </summary>
    public List<string> requireTraits = new List<string>();
    public List<string> excludeTraits = new List<string>();
    /// <summary> level of this organ's sensitivity skill (no skill = 0) </summary>
    public BodyDescriptionRange sensitivity = null;
    /// <summary> level of this organ's expansion skill (no skill = 0) </summary>
    public BodyDescriptionRange expansion = null;
    /// <summary> any of: empty, some, visible, extreme. Organs that hold nothing never match </summary>
    public List<string> contentState = new List<string>();
    /// <summary> some content item has each of these tags </summary>
    public List<string> requireContentTags = new List<string>();
    /// <summary> no content item has any of these tags </summary>
    public List<string> excludeContentTags = new List<string>();
    /// <summary> true: something is equipped on this organ's slots; false: nothing is </summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public bool? requireEquip = null;
    /// <summary> an equipped item carries all of these tags (it becomes $item$) </summary>
    public List<string> requireEquipTags = new List<string>();
    /// <summary> no equipped item carries any of these tags </summary>
    public List<string> excludeEquipTags = new List<string>();
    /// <summary> owner's reproduction cycle stage name (Menstrual, Ovulation, Estrus, Pregnant...), any of </summary>
    public List<string> cycleStages = new List<string>();
    /// <summary> owner's sex stimulation severity (chara_status_sex_stimulation, ~0-100) </summary>
    public BodyDescriptionRange arousal = null;
    /// <summary> this organ's EffectiveLubrication; organs that need no lubrication never match </summary>
    public BodyDescriptionRange lubrication = null;
    /// <summary> this organ's IsVirgin </summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)] public bool? virgin = null;

    [JsonIgnore]
    public bool IsUnconditional
    {
        get
        {
            return (requireTraits == null || requireTraits.Count == 0) && (excludeTraits == null || excludeTraits.Count == 0)
                && sensitivity == null && expansion == null
                && (contentState == null || contentState.Count == 0)
                && (requireContentTags == null || requireContentTags.Count == 0) && (excludeContentTags == null || excludeContentTags.Count == 0)
                && requireEquip == null
                && (requireEquipTags == null || requireEquipTags.Count == 0) && (excludeEquipTags == null || excludeEquipTags.Count == 0)
                && (cycleStages == null || cycleStages.Count == 0)
                && arousal == null && lubrication == null && virgin == null;
        }
    }
}
