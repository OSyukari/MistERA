using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// "Ranks" MasterList index - JSON-defined rank tracks (e.g. an AV actress career), modeled on CharaSkill:
/// code only provides the structure, every level and its promotion requirements live in data.
/// </summary>
public class Index_RankTracks : I_IndexMergeable, I_IndexHasID, I_RemoveElemByTag
{
    public List<RankTrack> list = new List<RankTrack>();

    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Index_RankTracks;
        if (l == null || l.list == null || l.list.Count < 1) return;
        this.list.AddRange(l.list);
    }

    public RankTrack GetByID(string id) { return id != null && ID_Dictionary.TryGetValue(id, out var v) ? v : null; }
    Dictionary<string, RankTrack> ID_Dictionary = new Dictionary<string, RankTrack>();
    public void RegisterAllID(List<string> s)
    {
        foreach (var o in list)
        {
            if (string.IsNullOrEmpty(o.ID)) continue;
            if (!ID_Dictionary.TryAdd(o.ID, o)) Debug.Log($"failed to add Index_RankTracks id [{o.ID}] due to duplicate");
        }
        s.Add("Index_RankTracks initialized with " + list.Count + " elements");
    }

    public void RemoveElemByTag(string tag)
    {
        list.RemoveAll(x => x.tags.Contains(tag));
    }
}

public static class RankUtility
{
    /// <summary>
    /// Rank evaluation: checks chara's next level on trackID (requirements read with studio's release registry).
    /// Promotes and returns true if every requirement is met; otherwise returns false and advice holds one line
    /// per requirement (unmet ones conflict-colored), or a single "already at the top rank" line. newRankName is
    /// the localized name of the level chara holds after the call.
    /// </summary>
    /// <param name="market">faction whose release registry supplies her release / earnings stats (the clientele
    /// provider, e.g. the Kiryu office) - counts every seller.</param>
    /// <param name="studio">optional selling faction, for studio conditions (e.g. requireRank with studio = true).</param>
    public static bool TryPromote(Character_Trainable chara, string trackID, Manageable market, Manageable studio, out string newRankName, List<string> advice)
    {
        newRankName = "";
        if (chara == null)
        {
            Debug.LogError($"RankUtility.TryPromote: missing chara for rank track [{trackID}]");
            return false;
        }
        return TryPromote(chara.Ranks, trackID, new RankContext(chara, studio, market), out newRankName, advice);
    }

    /// <summary>
    /// Studio rank evaluation: same as the character version, on studio's own rank levels (SalesManager.Ranks),
    /// with only studio requirements available (no character in the context).
    /// </summary>
    public static bool TryPromoteStudio(Manageable studio, string trackID, out string newRankName, List<string> advice)
    {
        newRankName = "";
        if (studio == null || studio.SalesManager == null)
        {
            Debug.LogError($"RankUtility.TryPromoteStudio: missing studio or its SalesManager for rank track [{trackID}]");
            return false;
        }
        return TryPromote(studio.SalesManager.Ranks, trackID, new RankContext(null, studio, null), out newRankName, advice);
    }

    static bool TryPromote(RankTracker tracker, string trackID, RankContext ctx, out string newRankName, List<string> advice)
    {
        newRankName = "";
        var track = scr_System_Serializer.current.MasterList.Ranks.GetByID(trackID);
        if (track == null || track.levels.Count == 0)
        {
            Debug.LogError($"RankUtility.TryPromote: missing rank track [{trackID}]");
            return false;
        }

        int current = tracker.GetLevel(trackID);
        newRankName = track.GetLevel(current).LocalizedName;
        int next = current + 1;
        if (next >= track.levels.Count)
        {
            advice?.Add(LocalizeDictionary.QueryThenParse("rank_eval_maxed", "Already at the top rank."));
            return false;
        }

        var nextLevel = track.levels[next];
        if (!nextLevel.Validate(track, ctx, advice)) return false;

        tracker.SetLevel(trackID, next);
        newRankName = nextLevel.LocalizedName;
        return true;
    }
}

/// <summary>
/// Who a rank requirement is checked against: the ranked character and/or the studio whose release
/// registry (SalesManager) supplies release / earnings stats. Either may be null for tracks that don't need it.
/// </summary>
public class RankContext
{
    public Character_Trainable chara = null;
    /// <summary>The selling faction - studio conditions read its own sales (SalesManager.GetStudioEarnings etc.).</summary>
    public Manageable studio = null;
    /// <summary>The faction whose release registry holds the character's releases (all sellers); falls back to studio.</summary>
    public Manageable market = null;

    public RankContext() { }
    public RankContext(Character_Trainable chara, Manageable studio, Manageable market)
    {
        this.chara = chara;
        this.studio = studio;
        this.market = market;
    }
}

/// <summary>
/// Current level per rank track. Levels only change through an explicit promotion (rank evaluation),
/// never automatically from fame.
/// </summary>
public class RankTracker
{
    [JsonProperty] protected Dictionary<string, int> levels = new Dictionary<string, int>();

    public int GetLevel(string trackID)
    {
        return trackID != null && levels.TryGetValue(trackID, out int level) ? level : 0;
    }

    public void SetLevel(string trackID, int level)
    {
        if (string.IsNullOrEmpty(trackID)) return;
        levels[trackID] = Math.Max(0, level);
    }
}

public class RankTrack
{
    public string ID = "";
    public List<string> tags = new List<string>();

    /// <summary>
    /// This track's rank score = sum of FameTracker fame by type x weight - e.g. an idol debuting in AV
    /// already carries score from her idol fame.
    /// </summary>
    public Dictionary<string, float> fameWeights = new Dictionary<string, float>();

    /// <summary>
    /// levels[0] is the starting level; levels[n].requirements gate promotion from n-1 to n.
    /// </summary>
    public List<RankLevel> levels = new List<RankLevel>();

    [JsonIgnore] public string DisplayName { get { return LocalizeDictionary.QueryThenParse(ID, ID); } }

    public float GetFameScore(Character_Trainable c)
    {
        if (c == null) return 0f;
        float score = 0f;
        foreach (var kvp in fameWeights) score += c.Fame.Get(kvp.Key) * kvp.Value;
        return score;
    }

    /// <summary>
    /// The level entry for a level index, clamped into range; null if the track defines no levels.
    /// </summary>
    public RankLevel GetLevel(int level)
    {
        if (levels.Count == 0) return null;
        return levels[Mathf.Clamp(level, 0, levels.Count - 1)];
    }

    public float GetPricePremium(int level)
    {
        var l = GetLevel(level);
        return l == null ? 1f : l.pricePremium;
    }

    public class RankLevel
    {
        public string DisplayName = "";
        /// <summary>
        /// Sale price multiplier for recordings saved with a main actor at this level (see
        /// RecordingEvaluator.rankTrackID / KojoRecording.pricePremium).
        /// </summary>
        public float pricePremium = 1f;
        /// <summary>
        /// Studio tracks: maximum sales renown at this level (see MapPlan.renownRankTrackID). 0 = uncapped.
        /// </summary>
        public float renownCap = 0f;
        public Require requirements = null;

        [JsonIgnore] public string LocalizedName { get { return LocalizeDictionary.QueryThenParse(DisplayName, DisplayName); } }

        /// <summary>
        /// Whether ctx meets this level's requirements. tooltips (optional) receives one line per condition,
        /// unmet ones in the conflict color - usable directly as promotion advice.
        /// </summary>
        public bool Validate(RankTrack track, RankContext ctx, List<string> tooltips = null)
        {
            if (requirements == null) return true;
            return requirements.Validate(track, ctx, tooltips);
        }
    }

    public abstract class Require
    {
        public List<RequirementEntry> entries = new List<RequirementEntry>();
        public abstract bool Validate(RankTrack track, RankContext ctx, List<string> tooltips = null);
    }

    public class RequireOne : Require
    {
        public override bool Validate(RankTrack track, RankContext ctx, List<string> tooltips = null)
        {
            if (entries == null || entries.Count < 1) return true;
            if (tooltips != null) tooltips.Add(LocalizeDictionary.QueryThenParse("rank_require_one", "Require one of the following:"));
            bool any = false;
            // evaluate every entry (not short-circuit) so the advice lists all options
            foreach (var i in entries) if (i.Validate(track, ctx, tooltips)) any = true;
            return any;
        }
    }

    public class RequireAll : Require
    {
        public override bool Validate(RankTrack track, RankContext ctx, List<string> tooltips = null)
        {
            if (entries == null || entries.Count < 1) return true;
            bool all = true;
            // evaluate every entry (not short-circuit) so the advice lists every unmet condition
            foreach (var i in entries) if (!i.Validate(track, ctx, tooltips)) all = false;
            return all;
        }
    }

    /// <summary>
    /// Every condition set in one entry must hold. Release / earnings conditions need ctx.studio; character
    /// conditions need ctx.chara - a missing context fails the condition.
    /// </summary>
    public class RequirementEntry
    {
        public CompareValue requireFameScore = null;
        public List<RequireFame> requireFame = new List<RequireFame>();
        public List<RequireReleases> requireReleases = new List<RequireReleases>();
        public CompareValue requireEarnings = null;
        public List<RequireRank> requireRank = new List<RequireRank>();

        // studio conditions (ctx.studio's SalesManager)
        public CompareValue requireRenown = null;
        public List<RequireReleases> requireStudioReleases = new List<RequireReleases>();
        public CompareValue requireStudioEarnings = null;
        /// <summary>
        /// Faction IDs the studio's managers must also manage (e.g. owning the film studio).
        /// </summary>
        public List<string> requireManagesFaction = new List<string>();

        public bool Validate(RankTrack track, RankContext ctx, List<string> tooltips = null)
        {
            bool ok = true;
            var chara = ctx?.chara;
            // sales = the selling studio (studio conditions), market = registry holding the character's releases
            var sales = ctx?.studio?.SalesManager;
            var market = (ctx?.market ?? ctx?.studio)?.SalesManager;

            if (requireFameScore != null && requireFameScore.isValid)
            {
                float current = track.GetFameScore(chara);
                ok &= Line(tooltips, chara != null && Utility.CompareValue(current, requireFameScore.operand, requireFameScore.value),
                    $"{LocalizeDictionary.QueryThenParse("rank_req_fameScore", "Rank score")} {Op(requireFameScore.operand)} {requireFameScore.value:0} ({current:0})");
            }

            foreach (var r in requireFame)
            {
                if (!r.isValid) continue;
                float current = chara == null ? 0f : chara.Fame.Get(r.fameType);
                ok &= Line(tooltips, chara != null && Utility.CompareValue(current, r.operand, r.value),
                    $"{LocalizeDictionary.QueryThenParse($"fametype_{r.fameType}", r.fameType)} {Op(r.operand)} {r.value:0} ({current:0})");
            }

            foreach (var r in requireReleases)
            {
                if (!r.isValid) continue;
                int current = chara == null || market == null ? 0 : market.CountActorReleases(chara.BaseID, r.minGrade);
                string grade = string.IsNullOrEmpty(r.minGrade) ? "" : $" [{r.minGrade}+]";
                ok &= Line(tooltips, chara != null && market != null && Utility.CompareValue(current, r.operand, r.value),
                    $"{LocalizeDictionary.QueryThenParse("rank_req_releases", "Lead releases")}{grade} {Op(r.operand)} {r.value} ({current})");
            }

            if (requireEarnings != null && requireEarnings.isValid)
            {
                long current = chara == null || market == null ? 0 : market.GetActorEarnings(chara.BaseID);
                ok &= Line(tooltips, chara != null && market != null && Utility.CompareValue((float)current, requireEarnings.operand, requireEarnings.value),
                    $"{LocalizeDictionary.QueryThenParse("rank_req_earnings", "Earnings as lead")} {Op(requireEarnings.operand)} {requireEarnings.value:0} ({current})");
            }

            foreach (var r in requireRank)
            {
                if (!r.isValid) continue;
                var other = scr_System_Serializer.current.MasterList.Ranks.GetByID(r.trackID);
                var tracker = r.studio ? sales?.Ranks : chara?.Ranks;
                int current = tracker == null ? 0 : tracker.GetLevel(r.trackID);
                string name = other != null ? other.DisplayName : r.trackID;
                string levelName = other?.GetLevel(r.value)?.LocalizedName ?? r.value.ToString();
                string currentName = other?.GetLevel(current)?.LocalizedName ?? current.ToString();
                ok &= Line(tooltips, tracker != null && Utility.CompareValue(current, r.operand, r.value),
                    $"{name} {Op(r.operand)} {levelName} ({currentName})");
            }

            if (requireRenown != null && requireRenown.isValid)
            {
                float current = sales == null ? 0f : sales.Renown;
                ok &= Line(tooltips, sales != null && Utility.CompareValue(current, requireRenown.operand, requireRenown.value),
                    $"{LocalizeDictionary.QueryThenParse("rank_req_renown", "Studio renown")} {Op(requireRenown.operand)} {requireRenown.value:0} ({current:0})");
            }

            foreach (var r in requireStudioReleases)
            {
                if (!r.isValid) continue;
                int current = sales == null ? 0 : sales.CountStudioReleases(r.minGrade);
                string grade = string.IsNullOrEmpty(r.minGrade) ? "" : $" [{r.minGrade}+]";
                ok &= Line(tooltips, sales != null && Utility.CompareValue(current, r.operand, r.value),
                    $"{LocalizeDictionary.QueryThenParse("rank_req_studioReleases", "Studio releases")}{grade} {Op(r.operand)} {r.value} ({current})");
            }

            if (requireStudioEarnings != null && requireStudioEarnings.isValid)
            {
                long current = sales == null ? 0 : sales.GetStudioEarnings();
                ok &= Line(tooltips, sales != null && Utility.CompareValue((float)current, requireStudioEarnings.operand, requireStudioEarnings.value),
                    $"{LocalizeDictionary.QueryThenParse("rank_req_studioEarnings", "Studio sales")} {Op(requireStudioEarnings.operand)} {requireStudioEarnings.value:0} ({current})");
            }

            foreach (var factionID in requireManagesFaction)
            {
                if (string.IsNullOrEmpty(factionID)) continue;
                var target = scr_System_CampaignManager.current.FindFactionByID(factionID);
                var studio = ctx?.studio;
                bool manages = studio != null && target != null && studio.ManagerRefs != null && target.ManagerRefs != null
                    && studio.ManagerRefs.Intersect(target.ManagerRefs).Any();
                string name = target != null ? target.FactionDisplayName : factionID;
                ok &= Line(tooltips, manages, LocalizeDictionary.QueryThenParse("rank_req_managesFaction", "Manages $faction$").Replace("$faction$", name));
            }

            return ok;
        }

        static string Op(LogicalOperand operand) { return LocalizeDictionary.QueryThenParse(operand.ToString()); }

        // adds a requirement line (conflict-colored when unmet) and returns met
        static bool Line(List<string> tooltips, bool met, string text)
        {
            if (tooltips != null)
                tooltips.Add(met ? text : $"<color={scr_System_CentralControl.current.DisplaySetting.TextColor_conflict.Hex}>{text}</color>");
            return met;
        }
    }

    public class CompareValue
    {
        public LogicalOperand operand = LogicalOperand.none;
        public float value = 0f;
        [JsonIgnore] public bool isValid { get { return operand != LogicalOperand.none; } }
    }

    public class RequireFame : CompareValue
    {
        public string fameType = "";
        [JsonIgnore] public new bool isValid { get { return !string.IsNullOrEmpty(fameType) && operand != LogicalOperand.none; } }
    }

    /// <summary>
    /// Count of release lineages graded minGrade or better (empty = any grade) where the character is a main
    /// actor in the best-selling edition (SalesManager.CountActorReleases).
    /// </summary>
    public class RequireReleases
    {
        public string minGrade = "";
        public LogicalOperand operand = LogicalOperand.none;
        public int value = 0;
        [JsonIgnore] public bool isValid { get { return operand != LogicalOperand.none; } }
    }

    /// <summary>
    /// The current level on another rank track - the character's, or with studio = true the studio's.
    /// </summary>
    public class RequireRank
    {
        public string trackID = "";
        public bool studio = false;
        public LogicalOperand operand = LogicalOperand.none;
        public int value = 0;
        [JsonIgnore] public bool isValid { get { return !string.IsNullOrEmpty(trackID) && operand != LogicalOperand.none; } }
    }
}
