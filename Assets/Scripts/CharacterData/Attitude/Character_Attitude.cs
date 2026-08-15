using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

[System.Serializable]
public class Index_CharacterAttitudes : I_IndexHasID, I_IndexMergeable, I_RemoveNSFW, I_NeedLateInitialize
{
    [SerializeField] public List<Character_Attitude> list = new List<Character_Attitude>();
    Dictionary<string, Character_Attitude> ID_Dictionary = new Dictionary<string, Character_Attitude>();

    public void RegisterAllID(List<string> messages)
    {
        messages.Add("Index_CharacterAttitudes : registering ID with list length [" + list.Count + "]");

        foreach (var o in this.list)
        {
            if (string.IsNullOrEmpty(o.ID)) continue;
            if (!ID_Dictionary.TryAdd(o.ID, o)) Debug.Log($"failed to add Index_CharacterAttitudes id [{o.ID}] due to duplicate");
        }
    }

    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Index_CharacterAttitudes;
        if (l == null) return;
        else if (l.list == null) return;
        else
        {
            this.list.AddRange(l.list);
        }
    }

    /// <summary>
    /// Sort into priority-descending order; ties broken by ID for determinism. Unlike the old
    /// per-relationship attitude list, there is no secondary MainEmotionKey sort - EmotionKeys/
    /// Requirements on each entry do the actual branching in RelationshipManager.SelectAttitude.
    /// </summary>
    public void LateInitialize()
    {
        list.Sort(delegate (Character_Attitude x, Character_Attitude y)
        {
            if (x.priority != y.priority) return y.priority >= x.priority ? 1 : -1;
            return String.Compare(x.ID, y.ID, StringComparison.Ordinal);
        });
    }

    public Character_Attitude GetByID(string id) { return ID_Dictionary.ContainsKey(id) ? ID_Dictionary[id] : null; }

    public void RemoveNSFW()
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var curr = list[i];
            if (curr.tags.Count > 0 && Utility.ListContainsLoose(scr_System_Serializer.current.nsfwKeywords, curr.tags))
            {
                ID_Dictionary.Remove(curr.ID);
                list.RemoveAt(i);
            }
        }
    }
}

/// <summary>
/// Per-character emotional-reaction state (Angry/Happy/Focused/etc). Replaces the old per-relationship
/// RelationshipAttitude - this is intentionally scoped to "what's my current gut reaction," not
/// "how do I feel about this specific person" (that's carried directly by relationship scores +
/// RelationshipType ladder elsewhere, unaffected by this class).
/// </summary>
[System.Serializable]
public class Character_Attitude
{
    [JsonIgnore]
    public string DisplayName { get { return LocalizeDictionary.QueryThenParse(ID); } }

    public string ID = "";
    public int priority = 0;
    public List<string> tags = new List<string>();

    public RelationshipRequirement Requirements = null;

    /// <summary>
    /// Which RelationshipScoreType(s) this attitude is flavored for. Empty = needs no relationship
    /// signal at all (a pure Mood/Stress/Lust attitude, e.g. Stressed/Relaxed/Neutral).
    /// </summary>
    public List<RelationshipScoreType> EmotionKeys = new List<RelationshipScoreType>();

    /// <summary>
    /// 0 = any sign matches. +1/-1 = only a same-signed round delta reinforces this attitude - e.g.
    /// Angry needs Badwill increasing, not decreasing, so a conciliatory interaction doesn't
    /// "preserve" hostility just because Badwill happened to be the score type touched.
    /// </summary>
    public int preferredDeltaSign = 0;

    /// <summary>
    /// Starting "rounds of contradiction" this attitude can absorb before a recheck is forced.
    /// A matching round never refreshes this back up - only non-matching rounds decrement it.
    /// </summary>
    public int startingIntensity = 1;

    public int obedienceMod = 0;
    public int obedienceMod_Max = 0;
    public double obedienceMod_trust = 0;
    public double obedienceMod_goodwill = 0;
    public double obedienceMod_desire = 0;
    public double obedienceMod_badwill = 0;
    public double obedienceMod_fear = 0;

    // Symmetric, capped score-gain amplification - applied in RelationshipManager.IncreaseRelationshipWith
    // when the score type being modified is one of this attitude's EmotionKeys.
    public double amplify_trust = 1.0;
    public double amplify_goodwill = 1.0;
    public double amplify_desire = 1.0;
    public double amplify_badwill = 1.0;
    public double amplify_fear = 1.0;
    public double amplifyCap = 2.0;

    public int GetObedienceMod(Character_Relationship rel)
    {
        int value = 0;
        if (obedienceMod_goodwill != 0) value += (int)(rel.Goodwill * obedienceMod_goodwill);
        if (obedienceMod_desire != 0) value += (int)(rel.Desire * obedienceMod_desire);
        if (obedienceMod_fear != 0) value += (int)(rel.Fear * obedienceMod_fear);
        if (obedienceMod_badwill != 0) value += (int)(rel.Badwill * obedienceMod_badwill);
        if (obedienceMod_trust != 0) value += (int)(rel.Trust * obedienceMod_trust);
        value += obedienceMod;
        return Math.Min(value, obedienceMod_Max);
    }

    public float ApplyAmplification(RelationshipScoreType type, float amount)
    {
        if (!EmotionKeys.Contains(type)) return amount;

        double mult;
        switch (type)
        {
            case RelationshipScoreType.Trust: mult = amplify_trust; break;
            case RelationshipScoreType.Goodwill: mult = amplify_goodwill; break;
            case RelationshipScoreType.Desire: mult = amplify_desire; break;
            case RelationshipScoreType.Badwill: mult = amplify_badwill; break;
            case RelationshipScoreType.Fear: mult = amplify_fear; break;
            default: mult = 1.0; break;
        }
        mult = Math.Min(mult, amplifyCap);
        return (float)(amount * mult);
    }

    /// <summary>
    /// True if this round's dominant emotion key (if any) reinforces this attitude, per EmotionKeys +
    /// preferredDeltaSign. Attitudes with no EmotionKeys always match - they don't decay off relationship
    /// signal at all; see RelationshipManager.FinalizeAttitudeRound's separate Requirements-revalidation safety
    /// valve for how those still get reconsidered when they become stale (e.g. stress passes).
    /// </summary>
    public bool MatchesRound(RelationshipScoreType? roundKey, int roundDeltaSign)
    {
        // Empty EmotionKeys ("no relationship signal needed") only matches a round that itself carried
        // no relationship signal - a round WITH a real delta is a mismatch even for these, so a
        // relationship-driven event can still erode a stale Neutral/Stressed/Relaxed and force a
        // recheck. Without this, an attitude with no Requirements at all (Neutral) can never leave -
        // it always "matches", so intensity never decays and the round-end Requirements safety valve
        // (which Neutral trivially always passes, having no Requirements) never fires either.
        if (EmotionKeys.Count == 0) return !roundKey.HasValue;
        if (!roundKey.HasValue) return false;
        if (!EmotionKeys.Contains(roundKey.Value)) return false;
        if (preferredDeltaSign == 0) return true;
        return preferredDeltaSign == roundDeltaSign;
    }

    public bool Requirements_Validate(Character_Trainable owner, Character_Relationship dominantRel)
    {
        return Requirements == null || Requirements.Validate(owner, dominantRel);
    }

    /// <summary>
    /// Cheap round-end safety check (Mood/Stress/Lust only) used to force a recheck for EmotionKeys-less
    /// attitudes that would otherwise never decay via the round mechanism.
    /// </summary>
    public bool isStillValid(StatsManager stats)
    {
        return Requirements == null || Requirements.ValidateStatExOnly(stats);
    }
}
