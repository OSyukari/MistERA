using System;
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

    // membertype requirement: does the character hold MemberType requireMemberType, either only in
    // their currently-active faction/party (requireMemberTypeCurrentActive) or in any faction they
    // belong to (see Character_Factions.HasMemberTypeInAnyFaction)
    public string requireMemberType = "";
    public bool requireMemberTypeCurrentActive = false;

    public List<string> requireInflatedBodyTags = new List<string>();
    public List<string> requireExtremeInflatedBodyTags = new List<string>();

    public List<string> requireAbsentJobwithCOMTag = new List<string>();
    public List<string> requireExistingJobwithCOMTag = new List<string>();

    public List<RequireStatusValue> requireStatusValue = new List<RequireStatusValue>();


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

        if (this.requireMemberType == "" && req.requireMemberType != "") this.requireMemberType = req.requireMemberType;
        this.requireMemberTypeCurrentActive = this.requireMemberTypeCurrentActive || req.requireMemberTypeCurrentActive;

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

/// <summary>Which Obligation_Debt signal a RequireFactionDebt compares - see that class's field property.</summary>
public enum FactionDebtField
{
    OwedAmount,
    LastPaymentFailed,
    TotalFailureCount
}

/// <summary>
/// Debt/loan comparator between two named factions, mirroring RequireKojoVariable's shape but reading off
/// the real TradeManager/Obligation_Debt system (TradeManager.GetDebtTo) instead of a scripted variable.
/// Needs no character/relationship context, unlike RequireKojoVariable - debt lives on the borrower's own
/// TradeManager regardless of which character is asking.
/// </summary>
public class RequireFactionDebt
{
    public string borrowerFactionID = "";
    public string lenderFactionID = "";
    public bool checkExistOnly = false;
    public LogicalOperand operand = LogicalOperand.none;
    public int value = 0;
    /// <summary>Optional. If filled, the currently-relevant number (owed amount, or elapsed cadence-cycles
    /// if resolvedCount is set) is stored into the caller's append-string collector under this key (e.g.
    /// for quest/dialogue text via $key$ substitution) - same convention as RequireKojoVariable.appendStringKey.</summary>
    public string appendStringKey = "";

    /// <summary>
    /// -1 (default) = not used - this requirement compares the owed amount instead (via checkExistOnly/
    /// operand/value as usual). Any other value switches this requirement to comparing, via the same
    /// operand field, how many full cadence-cycles have elapsed since the debt's Obligation_Debt.originDate
    /// - the unit is whatever that debt's OWN cadence is (Daily -&gt; days, Weekly -&gt; weeks, Monthly -&gt;
    /// calendar months, etc.), so e.g. "at least one billing cycle has passed" is correct regardless of
    /// which cadence a given debt actually uses, without a separate manually-incremented counter (which is
    /// how this used to be tracked, and was never actually incremented).
    /// </summary>
    public int resolvedCount = -1;

    /// <summary>
    /// Which underlying signal this requirement compares when resolvedCount isn't overriding it (resolvedCount
    /// &gt;= 0 always wins - see CurrentAmount). OwedAmount is the default (matches every existing authored
    /// requirement). LastPaymentFailed/TotalFailureCount read Obligation_Debt.lastPaymentFailed/
    /// totalFailureCount directly - exposed purely for quest/event consumption (e.g. narrative branches on
    /// "did the last payment bounce" or "how many times has this loan gone unpaid").
    /// </summary>
    public FactionDebtField field = FactionDebtField.OwedAmount;

    [JsonIgnore] public bool isValid { get { return borrowerFactionID != "" && lenderFactionID != "" && (resolvedCount >= 0 || checkExistOnly || operand != LogicalOperand.none); } }

    Obligation_Debt ResolveDebt()
    {
        var borrower = scr_System_CampaignManager.current.FindFactionByID(borrowerFactionID);
        var lender = scr_System_CampaignManager.current.FindFactionByID(lenderFactionID);
        if (borrower == null || lender == null || borrower.TradeManager == null) return null;
        return borrower.TradeManager.GetDebtTo(lender);
    }

    static int ElapsedCyclesSinceOrigin(Obligation_Debt debt)
    {
        var now = scr_System_Time.current.getCurrentTime();
        switch (debt.cadence)
        {
            case PaymentCadence.Daily: return (int)(now - debt.originDate).TotalDays;
            case PaymentCadence.Weekly: return (int)((now - debt.originDate).TotalDays / 7);
            case PaymentCadence.Biweekly: return (int)((now - debt.originDate).TotalDays / 14);
            case PaymentCadence.Yearly:
            {
                int years = now.Year - debt.originDate.Year;
                if (now.Month < debt.originDate.Month || (now.Month == debt.originDate.Month && now.Day < debt.originDate.Day)) years--;
                return Math.Max(0, years);
            }
            case PaymentCadence.Monthly:
            default:
            {
                int months = (now.Year - debt.originDate.Year) * 12 + (now.Month - debt.originDate.Month);
                if (now.Day < debt.originDate.Day) months--;
                return Math.Max(0, months);
            }
        }
    }

    [JsonIgnore]
    public int CurrentAmount
    {
        get
        {
            var debt = ResolveDebt();
            if (debt == null) return 0;
            if (resolvedCount >= 0) return ElapsedCyclesSinceOrigin(debt);
            switch (field)
            {
                case FactionDebtField.LastPaymentFailed: return debt.lastPaymentFailed ? 1 : 0;
                case FactionDebtField.TotalFailureCount: return debt.totalFailureCount;
                default: return debt.owed != null ? debt.owed.itemCount : 0;
            }
        }
    }

    public bool Validate()
    {
        if (resolvedCount >= 0) return Utility.CompareValue(CurrentAmount, operand, resolvedCount);
        int amount = CurrentAmount;
        if (checkExistOnly) return (amount > 0) == (value != 0);
        return Utility.CompareValue(amount, operand, value);
    }
}