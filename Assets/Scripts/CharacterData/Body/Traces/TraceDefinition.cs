using System;
using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// Visible marks on a body part / organ (Docs/Body/Plan_BodyTraces.md). Definitions live in Index_BodyPartBase.Traces and
/// pick their subclass by "$type"; the saved TraceInstance is one plain class, behavior stays on the definition.
/// </summary>
public enum TraceMergeMode
{
    /// <summary> every application is its own instance with its own timer, shown grouped "name x N" </summary>
    Stack,
    /// <summary> one instance per (id, resolved displayName) on a holder, amount added </summary>
    Merge,
}

public enum TraceMatter { Solid, Liquid, Dried }

public enum TraceCleanAction { Brush, Wipe, Wash }

public enum TraceCleanResult
{
    Keep,
    Remove,
    /// <summary> swap for the definition's dried mark (liquid -> dried) </summary>
    Replace,
}

public abstract class TraceDefinition
{
    [JsonProperty] protected string id = "";
    [JsonIgnore] public string ID { get { return id; } }

    /// <summary> localization key; formatted with TraceUtility.FormatText ($name$ $race$ $part$ $holder$ $amount$) </summary>
    [JsonProperty] protected string displayName = "";
    [JsonIgnore] public string DisplayNameKey { get { return displayName == "" ? id : displayName; } }

    /// <summary> localization key, same placeholders as displayName </summary>
    [JsonProperty] protected string tooltip = "";
    [JsonIgnore] public string TooltipKey { get { return tooltip == "" ? id + "_tooltip" : tooltip; } }

    /// <summary> classification only ("nsfw", "injury", "cum") </summary>
    public List<string> tags = new List<string>();

    /// <summary> act keyword -> chance (0..1) this trace is left per round; only listed keywords leave it </summary>
    public Dictionary<string, float> leaveKeywords = new Dictionary<string, float>();
    /// <summary> checked on the source character, null = always </summary>
    public TraceLeaveCondition leaveCondition = null;

    public TraceMergeMode mergeMode = TraceMergeMode.Stack;
    /// <summary> Stack: most instances of this definition per holder, the one with least remaining is replaced. 0 = unlimited </summary>
    public int maxInstances = 0;
    /// <summary> amount one application gives </summary>
    public float defaultAmount = 1;
    /// <summary> Merge: cap of the merged amount on one holder, 0 = none </summary>
    public float maxAmount = 0;

    /// <summary> may land on a random other trace slot of the part or of a connected part instead (saliva, cum, pubes; not marks) </summary>
    public bool allowRandomSpread = false;
    /// <summary> chance (0..1) per landing to spread, with allowRandomSpread </summary>
    public float spreadChance = 0;

    /// <summary> highest chance of the given keywords, 0 if none matches </summary>
    public float GetKeywordChance(List<string> keywords)
    {
        float chance = 0;
        if (keywords == null || leaveKeywords == null) return 0;
        foreach (var k in keywords)
        {
            if (k != null && leaveKeywords.TryGetValue(k, out var c) && c > chance) chance = c;
        }
        return chance;
    }

    /// <summary> set up a fresh instance (timers) </summary>
    public virtual void OnCreate(TraceInstance t) { }
    /// <summary> false = expired </summary>
    public abstract bool Tick(TraceInstance t, int minutes, Character_Trainable owner);
    public virtual TraceCleanResult OnClean(TraceInstance t, TraceCleanAction action) { return TraceCleanResult.Keep; }
    /// <summary> dried mark this turns into when it expires (Tick false) or is wiped, "" = none </summary>
    [JsonIgnore] public virtual string DriedTraceID { get { return ""; } }
}

/// <summary> injury, marks, tattoos: a heal timer or truly permanent </summary>
public class TraceDefinition_Permanent : TraceDefinition
{
    /// <summary> &lt;= 0 = truly permanent. remaining = duration * amount (amount = severity) </summary>
    public int durationMinutes = 0;
    /// <summary> optional derived stat, its FinalValue multiplies the elapsed minutes (missing = 1) </summary>
    public string healingFactorStatID = "";

    public override void OnCreate(TraceInstance t)
    {
        t.remaining = durationMinutes > 0 ? durationMinutes * Math.Max(t.amount, 0.01f) : 0;
    }

    public override bool Tick(TraceInstance t, int minutes, Character_Trainable owner)
    {
        if (durationMinutes <= 0) return true;
        t.remaining -= minutes * HealMultiplier(owner);
        return t.remaining > 0;
    }

    float HealMultiplier(Character_Trainable owner)
    {
        if (string.IsNullOrEmpty(healingFactorStatID) || owner == null || owner.Stats == null) return 1;
        var stat = owner.Stats.GetDerivedStat(healingFactorStatID);
        if (stat == null) return 1;
        float v = stat.FinalValue();
        return float.IsNaN(v) || float.IsInfinity(v) ? 1 : Math.Max(0, v);
    }
}

/// <summary> things caught on the body: solid (brushed off), liquid (wiped to a dried mark, evaporates), dried (shower only) </summary>
public class TraceDefinition_Temporary : TraceDefinition
{
    public TraceMatter matter = TraceMatter.Solid;
    /// <summary> Solid / Dried: optional natural fade, &lt;= 0 = never </summary>
    public int durationMinutes = 0;

    /// <summary> Liquid: ingestible liquid item deposited when it lands in an organ container ("" = surface only) </summary>
    public string liquidItemID = "";
    /// <summary>
    /// Liquid: cap of this liquid in a target organ = source Height/100 * Weight * ratio, all sources counted together,
    /// 0 = no cap. Container deposits only, surface traces are not capped by it.
    /// </summary>
    public float containerCapRatio = 0;
    /// <summary> Liquid: part of a container deposit that also lands on the holder surface </summary>
    public float spillRatio = 0;
    /// <summary> Liquid: surface amount lost per minute </summary>
    public float evaporationPerMinute = 0;
    /// <summary> Liquid: what wiping, and evaporating to 0, turn it into </summary>
    public string driedTraceID = "";
    /// <summary> amount the dried mark keeps </summary>
    public float driedAmountRatio = 1;

    [JsonIgnore] public override string DriedTraceID { get { return matter == TraceMatter.Liquid ? driedTraceID : ""; } }

    public override void OnCreate(TraceInstance t)
    {
        t.remaining = matter != TraceMatter.Liquid && durationMinutes > 0 ? durationMinutes : 0;
    }

    public override bool Tick(TraceInstance t, int minutes, Character_Trainable owner)
    {
        if (matter == TraceMatter.Liquid)
        {
            if (evaporationPerMinute <= 0) return true;
            t.amount -= evaporationPerMinute * minutes;
            return t.amount > 0.001f;
        }
        if (durationMinutes <= 0) return true;
        t.remaining -= minutes;
        return t.remaining > 0;
    }

    public override TraceCleanResult OnClean(TraceInstance t, TraceCleanAction action)
    {
        switch (matter)
        {
            case TraceMatter.Solid:
                return TraceCleanResult.Remove;
            case TraceMatter.Liquid:
                if (action == TraceCleanAction.Brush) return TraceCleanResult.Keep;
                if (action == TraceCleanAction.Wipe) return string.IsNullOrEmpty(driedTraceID) ? TraceCleanResult.Remove : TraceCleanResult.Replace;
                return TraceCleanResult.Remove;
            case TraceMatter.Dried:
                return action == TraceCleanAction.Wash ? TraceCleanResult.Remove : TraceCleanResult.Keep;
        }
        return TraceCleanResult.Keep;
    }
}

/// <summary> checked on the character leaving the trace </summary>
public class TraceLeaveCondition
{
    /// <summary> SexStimulation.Severity must be at least this, 0 = no check </summary>
    public float minSexStimulation = 0;
    /// <summary> pregnant, or lactating (not implemented yet, never true) </summary>
    public bool requirePregnantOrLactating = false;
    public List<RequireStatusValue> requireStatusValue = new List<RequireStatusValue>();
    /// <summary> chance tier from the source's body trait tags, null = none </summary>
    public TraitTagChance traitTagChance = null;

    /// <summary> false = not left; chanceMult multiplies the keyword chance </summary>
    public bool IsMet(Character_Trainable c, out float chanceMult)
    {
        chanceMult = 1;
        if (c == null) return false;
        if (minSexStimulation > 0 && (c.Stats?.SexStimulation == null || c.Stats.SexStimulation.Severity < minSexStimulation)) return false;
        if (requirePregnantOrLactating && !(c.ReproCycle != null && c.ReproCycle.isPregnant)) return false;
        if (requireStatusValue != null)
        {
            foreach (var r in requireStatusValue)
            {
                if (r != null && r.isValid && !r.Validate(c)) return false;
            }
        }
        if (traitTagChance != null)
        {
            chanceMult = traitTagChance.GetMultiplier(c);
            if (chanceMult <= 0) return false;
        }
        return true;
    }
}

/// <summary>
/// Chance multiplier from the tags of the character's current body trait of a group. Highest matching tag wins;
/// no trait / no matching tag = 0. No trait ids are hardcoded: any trait of the group works by carrying a tag.
/// </summary>
public class TraitTagChance
{
    public string traitGroupID = "";
    public Dictionary<string, float> tagMultipliers = new Dictionary<string, float>();

    public float GetMultiplier(Character_Trainable c)
    {
        if (c == null || c.Body == null || string.IsNullOrEmpty(traitGroupID) || tagMultipliers == null) return 0;
        var trait = c.Body.GetBodyTrait(traitGroupID);
        if (trait == null || trait.tags == null) return 0;
        float mult = 0;
        foreach (var tag in trait.tags)
        {
            if (tag != null && tagMultipliers.TryGetValue(tag, out var m) && m > mult) mult = m;
        }
        return mult;
    }
}

/// <summary> one trace on a holder. Source fields are a snapshot taken at creation, text is formatted on display. </summary>
public class TraceInstance
{
    public string traceID = "";
    /// <summary> trace slot of its holder it sits on (holder's traceSlots); None = old save, fixed on load </summary>
    public BodyPartEquipSlot slot = BodyPartEquipSlot.None;
    public float amount = 1;
    /// <summary> minutes left (permanent heal timer / solid fade) </summary>
    public float remaining = 0;
    public long appliedTick = 0;

    public int sourceRefID = -1;
    public string sourceName = "";
    public string sourceRaceID = "";
    public string sourceRaceName = "";
    /// <summary> organ / part base id of the source </summary>
    public string sourcePartID = "";

    [JsonIgnore] TraceDefinition _base = null;
    [JsonIgnore] public TraceDefinition Base
    {
        get
        {
            if (_base == null) _base = TraceUtility.GetDefinition(traceID);
            return _base;
        }
    }

    public void CopySourceFrom(TraceInstance o)
    {
        sourceRefID = o.sourceRefID;
        sourceName = o.sourceName;
        sourceRaceID = o.sourceRaceID;
        sourceRaceName = o.sourceRaceName;
        sourcePartID = o.sourcePartID;
    }
}

/// <summary> who leaves a trace (snapshot data for TraceInstance) </summary>
public class TraceSource
{
    public int refID = -1;
    public string name = "";
    public string raceID = "";
    public string raceName = "";
    public string partID = "";

    public static TraceSource From(Character_Trainable c, string partID = "")
    {
        var s = new TraceSource() { partID = partID ?? "" };
        if (c == null) return s;
        s.refID = c.RefID;
        s.name = c.FirstName;
        if (c.Race != null)
        {
            s.raceID = c.Race.ID;
            s.raceName = c.Race.DisplayName;
        }
        return s;
    }

    public static TraceSource From(I_TraceHolder holder)
    {
        return holder == null ? new TraceSource() : From(holder.Owner, holder.TraceHolderID);
    }

    /// <summary> semen: the cum item's father, by name / race even if the character is gone </summary>
    public static TraceSource FromCum(Item_Instance_Cum cum)
    {
        var s = new TraceSource();
        if (cum == null) return s;
        if (cum.Owner != null) s = From(cum.Owner, "");
        else
        {
            s.name = cum.FatherName;
            s.raceID = cum.raceID;
            s.raceName = cum.race != null ? cum.race.DisplayName : "";
        }
        return s;
    }

    public void WriteTo(TraceInstance t)
    {
        t.sourceRefID = refID;
        t.sourceName = name;
        t.sourceRaceID = raceID;
        t.sourceRaceName = raceName;
        t.sourcePartID = partID;
    }
}

/// <summary> a body part or organ that can hold / leave traces (BodyPart_Instance, BodyInternal_Instance) </summary>
public interface I_TraceHolder
{
    Character_Trainable Owner { get; }
    /// <summary> never null </summary>
    List<TraceInstance> Traces { get; }
    /// <summary> the slots traces can sit on (Base.traceSlots), never null; empty = holds none </summary>
    List<BodyPartEquipSlot> TraceSlots { get; }
    /// <summary> TraceSlots not empty </summary>
    bool AllowTraces { get; }
    bool CanLeaveTraces { get; }
    List<string> LeaveTraceIDs { get; }
    /// <summary> the part of an organ, null for a part </summary>
    I_TraceHolder ParentHolder { get; }
    /// <summary> its own equip slots (Base.AvailableSlots, organ: Mouth / Nipples / Penis...), never null </summary>
    List<BodyPartEquipSlot> OwnSlots { get; }
    /// <summary> organ: the parent part's slots it sits under (Base.coverSlots), part: empty. Never null </summary>
    List<BodyPartEquipSlot> UnderSlots { get; }
    string TraceHolderID { get; }
    string DisplayName { get; }
    /// <summary> a trace on this slot is uncovered enough to be seen (revealing score below 2) </summary>
    bool IsSlotVisible(BodyPartEquipSlot slot);
    bool hasTag(string tag);
}

/// <summary> where a trace sits: a holder and one of its trace slots </summary>
public struct TraceSpot
{
    public I_TraceHolder holder;
    public BodyPartEquipSlot slot;
    public TraceSpot(I_TraceHolder holder, BodyPartEquipSlot slot) { this.holder = holder; this.slot = slot; }
    public bool IsValid { get { return holder != null && holder.AllowTraces; } }
}
