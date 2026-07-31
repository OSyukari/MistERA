using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

[System.Serializable]
public class CharaReq
{

    public BodyEquipLayer clothingRequirement = BodyEquipLayer.Outer;
    public List<string> requireUndressedTags = new List<string>();
    public List<string> BodyTags = new List<string>();
    public int minRevealingScore = -1;

    public int cost_EN = 0;
    public int cost_ST = 0;

    public bool allowPlayer = true;
    public bool allowNPC = true;

    public bool requireConscious = true;
    public bool requireUnconscious = false;
    // require conscious to react, like work.
    // action that do not require conscious are action that are done unilaterally
    public bool requireUnrestrained = false;
    public bool requireAction = true;
    public bool requireNoTeammate = false;
    public bool requireFollowing = false;
    public bool requireNotFollowing = false;
    public bool requireTimestopped = false;
    public bool addPartyMembers = false;
    public bool requireUndressed = false;
    public bool requireMovement = false;
    public bool requireCombat = false;
    public bool requireFullHP = false;
    public bool requireMissingHP = false;
    //public bool requireAroused = false;

    public bool requireMale = false;
    public bool requireFemale = false;

    public List<string> requireInflatedBodyTags = new List<string>();
    public List<string> requireExtremeInflatedBodyTags = new List<string>();

    public List<string> requireAbsentJobwithCOMTag = new List<string>();
    public List<string> requireExistingJobwithCOMTag = new List<string>();


    public void Read(CharaReq req)
    {
        this.BodyTags.AddRange(req.BodyTags);
        this.requireUndressedTags.AddRange(req.requireUndressedTags);
        this.requireAbsentJobwithCOMTag.AddRange(req.requireAbsentJobwithCOMTag);
        this.requireExistingJobwithCOMTag.AddRange(req.requireExistingJobwithCOMTag);
        this.BodyTags = this.BodyTags.Distinct().ToList();
        this.requireUndressedTags = this.requireUndressedTags.Distinct().ToList();
        requireConscious = requireConscious && req.requireConscious;
        requireUnrestrained = requireUnrestrained || req.requireUnrestrained;
        requireMovement = requireMovement || req.requireMovement;
        requireAction = requireAction && req.requireAction;
        requireMale = this.requireMale || req.requireMale;
        requireFemale = this.requireFemale || req.requireFemale;

        requireUnconscious = requireUnconscious || req.requireUnconscious;
        requireFollowing = requireFollowing || req.requireFollowing;
        requireNotFollowing = requireNotFollowing || req.requireNotFollowing;
        requireTimestopped = requireTimestopped || req.requireTimestopped;

        //requireAroused = this.requireAroused || req.requireAroused;
        if (this.minRevealingScore == -1 && req.minRevealingScore != -1) this.minRevealingScore = req.minRevealingScore;
        if (this.cost_EN == 0 && req.cost_EN != 0) this.cost_EN = req.cost_EN;
        if (this.cost_ST == 0 && req.cost_ST != 0) this.cost_ST = req.cost_ST;
        this.addPartyMembers = this.addPartyMembers || req.addPartyMembers;
        this.requireNoTeammate = this.requireNoTeammate || req.requireNoTeammate;

        this.requireUndressed = this.requireUndressed || req.requireUndressed;
        this.requireCombat = this.requireCombat || req.requireCombat;
        this.requireFullHP = this.requireFullHP || req.requireFullHP;
        this.requireMissingHP = this.requireMissingHP || req.requireMissingHP;

    }
}

public class RequireStatValue
{
    public string statID = "";
    public LogicalOperand operand = LogicalOperand.none;
    public string value = "";
    [JsonIgnore] public bool isValid { get { return this.statID != "" && operand != LogicalOperand.none && value != ""; } }
    public bool Validate(Character_Trainable chara)
    {
        if (chara == null) return false;
        return chara.CompareStatValue(statID, operand, value);
    }
}

public class RequireStatusValue
{
    public string statusID = "";
    public bool checkExistOnly = false;
    public bool checkSeverityIndex = false;
    public LogicalOperand operand = LogicalOperand.none;
    public float value = 0;
    [JsonIgnore]
    public bool isValid
    {
        get
        {
            if (this.statusID == "") return false;
            if (!checkExistOnly && operand == LogicalOperand.none) return false;
            if (checkSeverityIndex && value < 0) return false;
            return true;
        }
    }
    public bool Validate(Character_Trainable chara)
    {
        // Exact hits first: a substring match could silently resolve an aggregate
        // id (e.g. chara_status_pain) to one of its sub-statuses (chara_status_pain_sex)
        var status = chara.Stats.FindStatusByExactID(statusID);
        var statusEx = status != null ? null : chara.Stats.FindStatusEXByExactID(statusID);
        if (status == null && statusEx == null) status = chara.Stats.GetStatusByStringMatch(statusID);
        if (status == null && statusEx == null) statusEx = chara.Stats.GetStatusEXByStringMatch(statusID);

        if (checkExistOnly) return status != null || statusEx != null;
        if (status == null && statusEx == null) return false;
        if (checkSeverityIndex) return Utility.CompareValue(status != null ? status.SeverityIndex : statusEx.SeverityIndex, operand, value);
        else return Utility.CompareValue(status != null ? status.Severity : statusEx.Severity, operand, value);
    }
}

public class RequireKojoVariable
{
    public bool isDailyVariable = false;
    public string variableID = "";
    public bool checkExistOnly = false;
    public LogicalOperand operand = LogicalOperand.none;
    public int value = 0;
    /// <summary>
    /// Optional. If filled, the currently-scoped variable's value is stored into the caller's
    /// append-string collector under this key (e.g. for quest stage display via $key$ substitution).
    /// </summary>
    public string appendStringKey = "";

    [JsonIgnore] public bool isValid { get { return this.variableID != "" && (checkExistOnly || operand != LogicalOperand.none); } }
    public bool Validate(Character_Relationship rel)
    {
        if (checkExistOnly) return (rel.Owner.Relationships.GetKojoVariableExist(isDailyVariable, rel, variableID) == (value != 0));
        else return Utility.CompareValue(rel.Owner.Relationships.GetKojoVariable(isDailyVariable, rel, variableID), operand, value);
    }
}