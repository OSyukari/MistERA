using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Globalization;
using System.Linq;
//using MoreLinq;
using Newtonsoft.Json;



/// <summary>
/// Response None = action did not happen in the first place <br/>
/// Response Refuse = proposal happened, refused by receiver <br/>
/// Response Accept = proposal happened and accepted. proceed to execution
/// </summary>
public enum  Memory_Response
{
    None,
    Refuse,
    Interrupted,
    Accept,
    CriticalFailure,
    Failure,
    Success,
    CriticalSuccess
}

public class Memory_Entry
{

    public bool noDisplay = false;
    public DateTime StartTime = DateTime.MinValue;
    [JsonIgnore][NonSerialized] public bool MergeWithAll = false;
    public DateTime EndTime = DateTime.MinValue;
    [JsonIgnore] public string PrintTimeStart { get { return StartTime.ToShortTimeString(); } }
    [JsonIgnore] public string PrintShortTimeStartToEnd { get { return $"{StartTime.ToString("HH:mm")}{(EndTime == StartTime ? "" : " - " + EndTime.ToString("HH:mm"))}"; } }
    [JsonIgnore] public string PrintTimeEndToStart { get { return (EndTime == StartTime ? "" :  EndTime.ToShortTimeString() + "\n- ")+ StartTime.ToShortTimeString(); } }

    [JsonIgnore] public DateTime FinalEndTime { get
        {
            if (EndTime == DateTime.MinValue || EndTime < StartTime) return StartTime;
            else return EndTime;
        } }
    // [JsonIgnore] public bool isSexMemory { get { return this.Tags.Contains("sex"); } }
    // [JsonIgnore] public bool isSexTouchMemory { get { return !isSexMemory && ( this.Tags.Contains("service") || this.Tags.Contains("unsafe") ); } }
    //  [JsonIgnore] public bool isTouchMemory { get { return !isSexTouchMemory && this.Tags.Contains("touch") ; } }

    //  [JsonIgnore] public bool isOnlyRefuseMemory { get { return this.interactions.Find(x => x.response != Memory_Response.Refuse) == null; } }
    public List<string> selfTags = new List<string>();
    List<string> targetTags = new List<string>();

    public void ReEstablishParent(Character_Trainable c)
    {
        this.ownerRef = c.RefID;
        this.owner = c; 

        foreach(var entry in this.interactions)
        {
            Utility.DistinctInPlace(entry.tags);
        }

        InternalUpdate();

    }

    protected int ownerRef = -1;
    private Character_Trainable owner = null;
    [JsonIgnore] public Character_Trainable Owner
    {
        get
        {
            if (owner == null && ownerRef > -1) owner = scr_System_CampaignManager.current.FindInstanceByID(ownerRef);
            return owner;
        }
    }
    /// <summary>
    /// if tags count < 1, return true. else, check every one
    /// </summary>
    /// <param name="tags"></param>
    /// <returns></returns>
    public bool HasInteractionWithTags(List<string> tags)
    {
        if (tags.Count < 1) return true;
        foreach (var i in this.interactions) if (Utility.ListContainsStrict(i.tags, tags)) return true;
        return false;
    }

    // rebuilt in place when dirty (InternalUpdate marks them) - same list instances for the entry's lifetime
    protected List<int> targetRefs = new List<int>();
    bool targetRefsDirty = true;
    [JsonIgnore] public List<int> TargetRefs { get {

            if (targetRefsDirty)
            {
                targetRefsDirty = false;
                targetRefs.Clear();
                var ownerRefID = Owner.RefID;
                foreach (var inst in interactions)
                {
                    foreach (var refID in inst.targets)
                    {
                        if (refID != ownerRefID && !targetRefs.Contains(refID)) targetRefs.Add(refID);
                    }
                }
            }
            return targetRefs;
        } }
    private List<Character_Trainable> targets = new List<Character_Trainable>();
    bool targetsDirty = true;
    [JsonIgnore] public List<Character_Trainable> Targets
    {
        get
        {
            if (targetsDirty)
            {
                targetsDirty = false;
                targets.Clear();
                foreach(var refID in TargetRefs)
                {
                    targets.Add(scr_System_CampaignManager.current.FindInstanceByID(refID));
                }
            }
            else
            {
                // update targets if new added
                foreach (var refID in TargetRefs)
                {
                    bool found = false;
                    foreach (var t in targets) if (t.RefID == refID) { found = true; break; }
                    if (!found) targets.Add(scr_System_CampaignManager.current.FindInstanceByID(refID));
                }
            }
            return targets;
        }
    }

    /// <summary>
    /// Exclude Owner Name
    /// </summary>
    [JsonIgnore] public List<string> TargetNames { get {
            var names = new List<string>();
            foreach (var c in Targets)
            {
                if (c.RefID == Owner.RefID) continue;
                names.Add(c.FirstName);
            }
            foreach (var i in interactions) if (i.unregisteredTargetNames != null) names.AddRange(i.unregisteredTargetNames);
            Utility.DistinctInPlace(names);
            return names;
        } }

    /// <summary>
    /// Called for every remaining character's memory when refID is unregistered, so no interaction
    /// keeps a target reference that could later resolve to an unrelated character (e.g. if refID is
    /// ever reused). Returns true if this entry was changed.
    /// </summary>
    public bool NotifyTargetUnregistered(int refID, string firstName)
    {
        bool changed = false;
        foreach (var inst in interactions)
        {
            if (inst.NotifyTargetUnregistered(refID, firstName)) changed = true;
        }
        if (changed) InternalUpdate();
        return changed;
    }


    [JsonProperty] protected int duration = -1;
    [JsonIgnore] public int Duration { get { return duration; } set { this.duration = value; } }

    public List<MemInstance> interactions = new List<MemInstance>();

    /// <summary> pool: spare MemBlacklist objects to reuse instead of allocating (taken from its end) </summary>
    public void FillBlacklist(List<MemBlacklist> Blacklist, List<MemBlacklist> pool = null)
    {
        foreach(var i in this.interactions)
        {
            if (i.response < Memory_Response.Accept)
            {
                // in case of memory merge contains both accept and refuse, skip it
                bool hasAccepted = false;
                foreach (var x in this.interactions)
                {
                    if (x.response >= Memory_Response.Interrupted && x.isSimilar(i)) { hasAccepted = true; break; }
                }
                if (hasAccepted) continue;

                MemBlacklist lists = null;
                foreach (var x in Blacklist)
                {
                    if (x.comID == i.comID && x.roomRef == this.roomRef && Utility.ListEquals(i.targets, x.targets)) { lists = x; break; }
                }
                if (lists != null) lists.count += i.stackCount;
                else if (pool != null && pool.Count > 0)
                {
                    var reused = pool[pool.Count - 1];
                    pool.RemoveAt(pool.Count - 1);
                    reused.Set(this.roomRef, i);
                    Blacklist.Add(reused);
                }
                else Blacklist.Add(new MemBlacklist(this.roomRef, i));
            }
        }
    }

    public Memory_Entry()
    {

    }
    /// <summary>
    /// This should also serve as sexlog
    /// </summary>
    /// <param name="ownerRef"></param>
    /// <param name="targetRefs"></param>
    /// <param name="description"></param>
    /// <param name="attitude"></param>
    /// <param name="response"></param>
    /// <param name="attitude_end"></param>
    /// <param name="duration"></param>
    /// <param name="targetCOM"></param>
    /// <param name="comVariantID"></param>
    /// <param name="isDoer"></param>
    /// <param name="masterRef"></param>
    /// <param name="tags"></param>
    /// 
    /*
    public Memory_Entry(int ownerRef, List<int> targetRefs, List<string> description,  Memory_Response response, Memory_Attitude attitude, int duration = -1, COM targetCOM = null, int comVariantID = -1, bool isDoer = true, int masterRef = -1, List<string> selfTags = null, List<string> targetTags = null, string roomName = null) : this()
    {
        this.StartTime = scr_System_Time.current.getCurrentTime();
        this.ownerRef = ownerRef;

        // targetRefs need manual updating
        if (targetRefs != null)
        {
            this.targetRefs.AddRange(targetRefs);
            this.targetRefs = this.targetRefs.Distinct().ToList();
        }

        MemInstance newInst;
        newInst = new MemInstance(targetRefs, (targetCOM == null ? "" : targetCOM.ID), comVariantID, masterRef, isDoer, response, attitude);
        this.interactions.Add(newInst);
        
        if (!isEvaluationCached) EvaluateAll();
        else EvaluateSingle(Owner, newInst, ref cache_score, ref cache_acceptCount, ref cache_refuseCount);

        description.RemoveAll(x => x.Length < 1);
        if (description.Count > 0) this.description.AddRange(description);
        this.duration = duration;
        this.roomName = roomName;

        // merge tags
        if (selfTags != null) this.selfTags.AddRange(selfTags);
        this.selfTags = selfTags.Distinct().ToList();

        if (targetTags != null) this.targetTags.AddRange(targetTags);
        this.targetTags = targetTags.Distinct().ToList();

        updateMemInstanceDescription();

        //Debug.LogError("New Memory Entry with descriptions: " + String.Join(" | ", description));
    }*/

    public bool softMerge = false;
    public int jobRefID = -1;
    public string entryDescription = "";
    // Includes title-only overrides (replaceDescription=false); automatic titles must not take ownership back.
    public bool explicitTitleOverride = false;
    // Presentation only: null preserves the original interaction/supplemental narrative.
    [JsonProperty] public string descriptionOverride = null;

    /// <summary>
    /// deferDisplay: only the merge-relevant state is built (UpdateMergeState) - a candidate that merges into the
    /// last entry is discarded, so its descriptions/moodlets are never needed. Call BuildDisplay before storing it.
    /// </summary>
    public Memory_Entry(Character_Trainable c, Job job, int roomRef, List<string> selfTags, MemInstance mem, string entryDescription = "", int duration = -1, bool deferDisplay = false) : this()
    {
        int comTime = mem == null || mem.comID == "" ? 0 : scr_System_Serializer.current.index_COM.GetByID(mem.comID).TimeScale;
        
        this.EndTime = scr_System_Time.current.getCurrentTime();
        this.StartTime = EndTime - new TimeSpan(0, comTime, 0);
        this.ownerRef = c.RefID;
        this.interactions.Add(mem);

        this.duration = selfTags != null && selfTags.Contains("important") ? -1 : duration < -1 ? -2 : Owner.Stats.MemoryLength; 
        this.roomRef = roomRef;
        if (selfTags != null) AddDistinct(this.selfTags, selfTags);
        this.entryDescription = entryDescription;
        if (job != null) this.jobRefID = job.RefID;
        this.softMerge = job == null || job.MemoryEntrySoftMerge;
        // update all internal
        if (deferDisplay) UpdateMergeState();
        else InternalUpdate();
    }

    static void AddDistinct(List<string> target, List<string> source)
    {
        if (source == null) return;
        for (int i = 0; i < source.Count; i++)
        {
            if (!target.Contains(source[i])) target.Add(source[i]);
        }
    }

    protected bool CanMergeWith(Memory_Entry other)
    {
        if (this.EndTime.Ticks == other.EndTime.Ticks)
        {
            if (scr_System_CentralControl.current.LogPrefs.DLog_Memory && Owner == scr_System_CampaignManager.current.CurrentTarget) Debug.Log($"memory entry merge, same endtime tick merge|");
            return true;
        }
        if (this.ownerRef != other.ownerRef)
        {
            if (scr_System_CentralControl.current.LogPrefs.DLog_Memory && Owner == scr_System_CampaignManager.current.CurrentTarget) Debug.Log($"memory entry merge error, ownerRef not mergeable |{ownerRef}|-|{other.ownerRef}|");
            return false;
        }
        if (this.roomRef != other.roomRef)
        {
            if (scr_System_CentralControl.current.LogPrefs.DLog_Memory && Owner == scr_System_CampaignManager.current.CurrentTarget) Debug.Log($"memory entry merge error, tags not roomRef |{roomRef}|-|{other.roomRef}|");
            return false;
        }

        if (other.HasTag("initSex")) return false;

        if (other.HasTag("expeditionEnd")) return false;
        if (other.HasTag("expedition") && selfTags.Contains("expedition") && !selfTags.Contains("expeditionEnd")) return true;

        else if (MergeWithAll || other.MergeWithAll)
        {
            return !selfTags.Contains("forbidMerge") && !other.selfTags.Contains("forbidMerge");
        }
        else
        {
            if (jobRefID != -1 && other.jobRefID != -1 && jobRefID != other.jobRefID)
            {
                if (scr_System_CentralControl.current.LogPrefs.DLog_Memory && Owner == scr_System_CampaignManager.current.CurrentTarget) Debug.Log($"memory entry merge error, jobref not identical |{jobRefID}|-|{other.jobRefID}|");
                return false;
            }

            if (!UtilityEX.AreMemoryTagsMergeable(selfTags, other.selfTags))
            {
                if (scr_System_CentralControl.current.LogPrefs.DLog_Memory && Owner == scr_System_CampaignManager.current.CurrentTarget) Debug.Log($"memory entry merge error, tags not mergeable |{String.Join(" ", selfTags)}|-|{String.Join(" ", other.selfTags)}|");
                return false;
            }
            if (this.entryDescription != "" && other.entryDescription != "" && this.entryDescription != other.entryDescription)
            {
                if (scr_System_CentralControl.current.LogPrefs.DLog_Memory && Owner == scr_System_CampaignManager.current.CurrentTarget) Debug.Log($"memory entry merge error, description not identical |{entryDescription}|-|{other.entryDescription}|");
                return false;
            }
            if (this.softMerge != other.softMerge)
            {
                if (scr_System_CentralControl.current.LogPrefs.DLog_Memory && Owner == scr_System_CampaignManager.current.CurrentTarget) Debug.Log($"memory entry merge error, softMerge not identical |{softMerge}|-|{other.softMerge}|");
                return false;
            }

            if (!softMerge)
            {
                // forbid merge if start time is 1 hour ago
                if ((other.EndTime - this.StartTime).TotalMinutes > 61) return false;

                foreach (var j in other.interactions)
                {
                    bool merged = false;
                    foreach (var i in interactions) if (i.canMergeWith(j)) merged = true;
                    if (!merged) return false;
                }
            }
            else // softmerge
            {
                // allow not refuseonly to merge with all
                // if refuseonly then only merge with other refuseonly 
                if (this.isRefuseOnly && !other.isRefuseOnly)
                {
                    if (scr_System_CentralControl.current.LogPrefs.DLog_Memory && Owner == scr_System_CampaignManager.current.CurrentTarget) Debug.Log($"memory entry merge error, softMerge isRefuseOnly |{isRefuseOnly}|-|{other.isRefuseOnly}|");
                    return false;
                }
                if (!UtilityEX.AreMemoryTagsMergeable(targetTags, other.targetTags))
                {
                    if (scr_System_CentralControl.current.LogPrefs.DLog_Memory && Owner == scr_System_CampaignManager.current.CurrentTarget) Debug.Log($"memory entry merge error, softMerge targettags |{String.Join(" ", targetTags)}|-|{String.Join(" ", other.targetTags)}|");
                    return false;
                }
            }
        }

        return true;
    }

    public bool TryMergeWith(Memory_Entry other)
    {
        if (!CanMergeWith(other)) return false;
        MergeFrom(other);
        return true;
    }

    /// <summary>
    /// Merges other into this entry without the CanMergeWith rules - used directly by forced merges
    /// (MemoryManager.MergeLLMSession), through TryMergeWith otherwise.
    /// </summary>
    public void MergeFrom(Memory_Entry other)
    {
        foreach (var j in other.interactions)
        {
            bool merged = false;
            foreach (var i in this.interactions)
            {
                if (i.TryMergeWith(j))
                {

                    merged = true;
                    break;
                }
            }
            if (!merged) this.interactions.Add(j);
            j.tags.Remove("mergeWithAll");
        }

        this.EndTime = other.EndTime;
        other.selfTags.Remove("mergeWithAll");
        AddDistinct(this.selfTags, other.selfTags);

        this.jobRefID = !MergeWithAll && this.jobRefID != -1 ? this.jobRefID : other.jobRefID != -1 ? other.jobRefID : -1;
        this.entryDescription = this.entryDescription != "" ? this.entryDescription : other.entryDescription != "" ? other.entryDescription : "";
        if (!this.explicitTitleOverride && (other.explicitTitleOverride
            || (this.descriptionOverride == null && other.descriptionOverride != null)))
        {
            if (other.descriptionOverride != null) this.descriptionOverride = other.descriptionOverride;
            this.entryDescription = other.entryDescription;
            this.explicitTitleOverride = other.explicitTitleOverride;
            this.disableRoomName = other.disableRoomName;
            this.roomNameOverride = other.roomNameOverride;
        }

        this.MergeWithAll = false;

        if (this.duration < 0 || other.duration < 0) this.duration = -1;
        else if (this.selfTags.Contains("important")) this.duration = -1;
        else this.duration = Math.Max(this.duration, other.duration);

        // update description, stat calculation, etc
        InternalUpdate();
    }

    [JsonIgnore] [NonSerialized] public bool isRefuseOnly = false;


    List<MemInstance> Actions = new List<MemInstance>();
    protected void InternalUpdate()
    {
        UpdateMergeState();
        UpdateDisplay();
    }

    /// <summary>
    /// Finishes an entry built with deferDisplay (descriptions, moodlets) - MemoryManager calls this once the
    /// candidate is actually stored rather than merged into the last entry.
    /// </summary>
    public void BuildDisplay()
    {
        UpdateDisplay();
    }

    /// <summary>
    /// Everything CanMergeWith / TryMergeWith / HasTag read: target tags, actions, accept/refuse counts, score.
    /// Allocation-free (lists refilled in place).
    /// </summary>
    void UpdateMergeState()
    {
        Actions.Clear();
        targetRefsDirty = true;
        targetsDirty = true;
        tagsDirty = true;
        targetTags.Clear();
        cache_score = 0; cache_acceptCount = 0; cache_refuseCount = 0;

        foreach (var i in interactions)
        {
            if (i.response != Memory_Response.Refuse) cache_acceptCount += i.stackCount;
            else cache_refuseCount += i.stackCount;

            cache_score += i.AttitudeScore(Owner);

            AddDistinct(targetTags, i.tags);

            if (i.isAction) Actions.Add(i);
        }

        MergeWithAll = MergeWithAll || (targetTags.Contains("initSex") && !targetTags.Contains("endSex"));

        isEvaluationCached = true;

        isRefuseOnly = cache_refuseCount > cache_acceptCount && cache_acceptCount == 0;
    }

    /// <summary> Tooltip description lines + stress/mood/lust moodlets. Requires UpdateMergeState (Actions, isRefuseOnly) first. </summary>
    void UpdateDisplay()
    {
        if (memInstanceDescriptionCache == null) memInstanceDescriptionCache = new List<string>(memInstanceDescription.Count + interactions.Count);
        else memInstanceDescriptionCache.Clear();
        memInstanceDescriptionCache.AddRange(memInstanceDescription);

        float scoreMod_Mood = 0, scoreMod_Stress = 0, scoreMod_Lust = 0;
        int maxLust = 0, minLust = 0, maxMood = 0, minMood = 0, maxStress = 0, minStress = 0;

        // "raped" is a role tag on the victim's own selfTags (set from ReceiverSelfTag), not on the
        // MemInstance's own tags (which carry DoerTargetTag's "rape" perpetrator-role tag instead) -
        // so this has to be checked at the entry level rather than per-instance like MemInstance.Stress
        // used to before this was moved out of it.
        bool isRaped = this.selfTags.Contains("raped");

        foreach (var i in interactions)
        {
            memInstanceDescriptionCache.Add(i.Print());

            int iLust = i.Lust, iMood = i.Mood, iStress = i.Stress;

            maxLust = Math.Max(maxLust, iLust);
            minLust = Math.Min(minLust, iLust);
            maxMood = Math.Max(maxMood, iMood);
            minMood = Math.Min(minMood, iMood);
            maxStress = Math.Max(maxStress, iStress);
            minStress = Math.Min(minStress, iStress);

            //Debug.Log($"memory merge with {i.description}, Mood|{scoreMod_Mood}+{iMood} {maxMood} {minMood}| Stress|{scoreMod_Stress}+{iStress} {maxStress} {minStress}| Lust|{scoreMod_Lust}+{iLust} {maxLust} {minLust}|");
            // EvaluateSingle(Owner, i, ref cache_score, ref cache_acceptCount, ref cache_refuseCount);
            scoreMod_Lust += iLust * i.stackCount;
            scoreMod_Mood += iMood * i.stackCount;
            scoreMod_Stress += iStress * i.stackCount;
        }

        if (isRaped && !selfTags.Contains("ignored")) scoreMod_Stress += selfTags.Contains("penetration") ? -2 : -1;

        // moodlet key is fixed at the first build: EndTime advances on every merge, and every distinct
        // ModString is registered for good in Utility.GetUniqueID's table
        if (moodletKeyTicks == 0) moodletKeyTicks = EndTime.Ticks;
        string moodletName = this.ToString(true, false);
        BuildMoodlets(Mod_Stress, MemoryManager.MemoryStat_Stress, (int)scoreMod_Stress, minStress, maxStress, moodletName);
        BuildMoodlets(Mod_Mood, MemoryManager.MemoryStat_Mood, (int)scoreMod_Mood, minMood, maxMood, moodletName);
        BuildMoodlets(Mod_Lust, MemoryManager.MemoryStat_Lust, (int)scoreMod_Lust, minLust, maxLust, moodletName);

        sumsDirty = true;
    }

    /// <summary>
    /// Splits original into chunks capped at max (positive) / min (negative), one moodlet per chunk. Existing
    /// moodlets in list are reused by index (Stat_Modifier.SetNumber keeps the value string cached), surplus removed.
    /// </summary>
    void BuildMoodlets(List<Stat_Modifier> list, string statID, int original, int min, int max, string displayName)
    {
        int count = 0;
        while (original != 0)
        {
            int part;
            if (max > 0 && original >= max) part = max;
            else if (min < 0 && original <= min) part = min;
            else part = original;

            original -= part;
            SetMoodlet(list, count, statID, part, displayName);
            count++;
        }
        if (list.Count > count) list.RemoveRange(count, list.Count - count);
    }

    void SetMoodlet(List<Stat_Modifier> list, int index, string statID, int value, string displayName)
    {
        Stat_Modifier moodlet;
        if (index < list.Count) moodlet = list[index];
        else
        {
            moodlet = initMoodlet(statID, index);
            list.Add(moodlet);
        }
        moodlet.DisplayName = displayName;
        // valueString_backup holds the unadjusted value - MemoryManager.AddMoodlet reads it and SetNumbers the adjusted one
        moodlet.SetNumber(value);
        moodlet.valueString_backup = moodlet.valueString;
    }

    [JsonIgnore][NonSerialized] long moodletKeyTicks = 0;

    string moodSum = "", stressSum = "", lustSum = "";
    bool sumsDirty = true;
    [JsonIgnore] public string MoodSum { get { UpdateSums(); return moodSum; } }
    [JsonIgnore] public string StressSum { get { UpdateSums(); return stressSum; } }
    [JsonIgnore] public string LustSum { get { UpdateSums(); return lustSum; } }
    public int roomRef = -1;

    /// <summary> tooltip-only strings, built on first read after an update </summary>
    void UpdateSums()
    {
        if (!sumsDirty) return;
        sumsDirty = false;
        moodSum = PrintSum(Mod_Mood);
        stressSum = PrintSum(Mod_Stress);
        lustSum = PrintSum(Mod_Lust);
    }

    /// <summary> reads the unadjusted value (valueString_backup) - GetRecentMemoryStatMods may have SetNumber'd the live one </summary>
    static string PrintSum(List<Stat_Modifier> mods)
    {
        if (mods.Count > 3)
        {
            int sum = 0;
            foreach (var i in mods) sum += BaseMoodletValue(i);
            return $"...{sum.ToString("+0;-#")}";
        }
        string s = "";
        foreach (var i in mods) s += BaseMoodletValue(i).ToString("+0;-#");
        return s;
    }

    static int BaseMoodletValue(Stat_Modifier mod)
    {
        return int.TryParse(mod.valueString_backup, out int value) ? value : (int)UtilityEX.StatValue(mod, null);
    }

    public bool HasTag(string tag)
    {
        if (this.selfTags.Contains(tag)) return true;
        if (this.targetTags.Contains(tag)) return true;
        return false;
    }
    private List<string> tagsCache = new List<string>();
    bool tagsDirty = true;
    [JsonIgnore] public List<string> Tags { get {
            if (tagsDirty)
            {
                tagsDirty = false;
                tagsCache.Clear();
                tagsCache.AddRange(selfTags);
                tagsCache.AddRange(targetTags);
            }
            return tagsCache;
        } }
    [JsonIgnore] public int TagsCount { get { return selfTags.Count + targetTags.Count; } }
    [JsonIgnore] public string PrintTags { get
        {
            return "Relevant Tags:\n[" + String.Join(" ", selfTags) + "]\n[" + String.Join(",",targetTags)+"]";
        } }

    /// <summary>
    /// return true if duration after tick == 0
    /// </summary>
    /// <param name="t"></param>
    /// <returns></returns>
    public bool Tick(int t)
    {
        if (HasTag("important")) duration = -1;
        else if (duration > 0) duration = Math.Max(duration - t, 0);

        return duration == 0;
    }


    public bool isRelevant(int targetRef = -1, string targetCOM = "", List<string> comTags = null, bool requireConsciousness = true) 
    {
        if ((targetRef == -1 || this.TargetRefs.Contains(targetRef))
            && (targetCOM == "" || hasInteractionWithCOMID(targetCOM))
            && (comTags == null || Utility.ListContainsStrict(this.Tags, comTags))
            && (!requireConsciousness || (!HasTag("unconscious") && (!HasTag("sleeping") && (Owner.CanActInTimeStop || !HasTag("timestop")))))) return true;
        return false; 
    }

    public bool hasInteractionWithCOMID(string comID)
    {
        return this.interactions.Find(x=>x.comID == comID) != null;
    }

    [JsonIgnore]
    public bool HasStatMod
    {
        get
        {
            return Mod_Stress.Count > 0 || Mod_Mood.Count > 0 || Mod_Lust.Count > 0;
        }
    }

    [JsonIgnore] public List<Stat_Modifier> Mod_Stress = new List<Stat_Modifier>();
    [JsonIgnore] public List<Stat_Modifier> Mod_Mood = new List<Stat_Modifier>();
    [JsonIgnore] public List<Stat_Modifier> Mod_Lust = new List<Stat_Modifier>();


    private Stat_Modifier initMoodlet(string statID, int addEx = 0)
    {
        var newstuff = new Stat_Modifier();
        newstuff.statID = statID;
        newstuff.ModString = $"Memory_{EndTime.Ticks}_{addEx}";
        newstuff.type = Stat_Modifier.StatMod_Type.addBase;
        //newstuff.SetValueTypeAndString("number", value)
        return newstuff;
    }

    public bool disableRoomName = false;
    public string roomNameOverride = "";

    public string ToString(bool withDescription = false, bool withRoomName = true, bool withTimeStamp = false)
    {
        string s = "";
        //bool printed = true;
        if (withTimeStamp) s += PrintShortTimeStartToEnd + ": ";

        string body = "";


        if (isRefuseOnly && this.entryDescription != "") body = LocalizeDictionary.QueryThenParse("job_desc_refuseOnly").Replace("$jobdesc$", entryDescription); 
        else if (this.entryDescription != "") body = LocalizeDictionary.QueryThenParse( entryDescription);
        else if (Actions.Count > 0) body = Actions[0].Print();
        // = MemInstanceDescriptions[0], without building the override-prefixed list
        else if (descriptionOverride != null) body = descriptionOverride;
        else if (memInstanceDescriptionCache != null && memInstanceDescriptionCache.Count > 0) body = memInstanceDescriptionCache[0];
        else body = "Error no stuff";

        if (withDescription && descriptionOverride != null) body += "\n" + descriptionOverride;

        if (withRoomName && !disableRoomName)
        {
            var roomname = roomNameOverride != "" ? roomNameOverride : this.roomRef == -1 ? "unknown" : scr_System_CampaignManager.current.Map.GetRoomByRef(roomRef).DisplayNameShort;
            return LocalizeDictionary.QueryThenParse("ui_entry_memory_withRoomName").Replace("$desc$", body).Replace("$roomname$", roomname);
        } else return body;


        /*
        if (isSexMemory)
        {   // dont care about actual interaction count, lob everything inside
            if (interactions.Count > 0 && interactions[0].comID != "" && scr_System_Serializer.current.GetByNameOrID_COM(interactions[0].comID).comTags.Contains("initSex"))
            {
                s += scr_System_Serializer.current.GetByNameOrID_COM(interactions[0].comID).DisplayName(interactions[0].comVariantID);
            }
            else
            {
                s += "had sex";
            }
            s += (TargetRefs.Count > 0 && TargetNames.Count > 0 ? " with " + String.Join(",", TargetNames) : " with " + (Owner.isFemale ? "herself" : "himself"));
        }
        else if (isSexTouchMemory)
        {
            s += "got molested";
        }
        else if (isTouchMemory)
        {   // same as above, lob everything inside
            s += "got intimate";
            s += (TargetRefs.Count > 0 && TargetNames.Count > 0 ? " with " + String.Join(",", TargetNames) : " with " + (Owner.isFemale ? "herself" : "himself"));
        }
        else if (interactions.Count < 2 && this.interactions[0].comID != "")
        {   // single type COM entry, we do care about count. but let's assume everything is same type.
            string comName = scr_System_Serializer.current.GetByNameOrID_COM(interactions[0].comID).DisplayName(interactions[0].comVariantID);

            s += comName + (interactions.Count > 1 ? " "+interactions.Count + " times" : "");
            s += TargetRefs.Count > 0 && TargetNames.Count > 0 ? " with " + String.Join(",", TargetNames) : "";
        }
        else if (Tags.Contains("timeResume"))
        {
            s += "reacting to timestop end" + interactions.Count;
            s += TargetRefs.Count > 0 && TargetNames.Count > 0 ? " with " + String.Join(",", TargetNames) : "";
        }
        else if (!withDescription)
        {
            s += "had some undefined interactions x" + interactions.Count;
            s += TargetRefs.Count > 0 && TargetNames.Count > 0 ? " with " + String.Join(",", TargetNames) : "";
        }
        else
        {
            printed = false;
        }

        if (printed && withRoomName) s += " in " + roomName;
        if (withDescription)
        {
            foreach(var ss in description)
            {
                if (ss.Length < 1) continue;
                var ssplit = ss.Split("||");
                if (ssplit.Length < 1) continue;
                if (ssplit.Length < 2) s += (s.Length > 0 ? "\n" : "") + ss;
                else
                {

                    var newS = ssplit[0];
                    //Debug.LogError("Replacing String initial ["+newS+"] from " + String.Join("||", ssplit));
                    for (int i = 1; i < ssplit.Length; i++)
                    {
                        var keyword = "$elem" + i.ToString("N0") + "$";
                        newS = newS.Replace(keyword, ssplit[i]);
                        //Debug.LogError("Replace Keyword String [" + keyword + "] by ["+ ssplit[i] + "] result [" + newS + "]");
                    }
                    s += (s.Length > 0 ? "\n" : "") + newS;
                }
            }
           // s += "\n" + String.Join("\n", description);
        }

        return s;*/
    }


    bool isEvaluationCached = false;
    int cache_score, cache_acceptCount, cache_refuseCount;

    [JsonIgnore] public int CachedScore { get { return cache_score; } }

    public int GetInfluence(EvaluationPackage.Modifiers modifiers, bool isSame = false)
    {
        int returnVal = 0;
        bool addNumber = false;

        string s1 = "", s2 = "";

        var cache_score_2 = Math.Min(cache_score, 5);
        if (cache_score_2 != 0)
        {
            returnVal += cache_score_2;

            if (cache_score_2 > 0) s1 = LocalizeDictionary.QueryThenParse("comLogs_causes_previousLogs_positive").Replace("$amount$", (cache_score_2).ToString("+0;-#")).Replace("$linkTooltip$", "comLogs_tooltip_goodOutcome");
            else if (cache_score_2 < 0) s1 = LocalizeDictionary.QueryThenParse("comLogs_causes_previousLogs_negative").Replace("$amount$", (cache_score_2).ToString("+0;-#")).Replace("$linkTooltip$", "comLogs_tooltip_badOutcome");

            if (addNumber) modifiers.AddModifier(ownerRef, s1, cache_score_2);
            else modifiers.AddModifier(ownerRef, s1, 0);
        }

        if (cache_refuseCount > (cache_acceptCount+1))
        {
            var difference = -(cache_refuseCount - cache_acceptCount + 1);
            returnVal += difference;

            s2 = LocalizeDictionary.QueryThenParse("comLogs_causes_previousLogs_negative").Replace("$amount$", (difference).ToString("+0;-#")).Replace("$linkTooltip$", "comLogs_tooltip_repeatedRefusal");

            if (addNumber) modifiers.AddModifier(ownerRef, s2, difference);
            else modifiers.AddModifier(ownerRef, s2, 0);
        }

        return returnVal;
    }

    

    private string dictionaryKeyword = "ui_entry_memory_description";


    public List<string> memInstanceDescription = new List<string>();
    private List<string> memInstanceDescriptionCache = null;
    /// <summary> descriptionOverride (SetMemoryNameOverride) is added in front of the other lines, never replaces them </summary>
    [JsonIgnore] public List<string> MemInstanceDescriptions
    {
        get
        {
            if (descriptionOverride == null) return memInstanceDescriptionCache;
            var lines = new List<string>() { descriptionOverride };
            if (memInstanceDescriptionCache != null) lines.AddRange(memInstanceDescriptionCache);
            return lines;
        }
    }

    /// <summary>
    /// Rebuilds the display/stat caches after directly editing memInstanceDescription (or any
    /// other field normally only refreshed through the merge paths) - InternalUpdate itself is
    /// protected, so external assemblers (e.g. MemoryManager.ApplyLLMDescription) go through this.
    /// </summary>
    public void RefreshCache()
    {
        InternalUpdate();
    }

    public void Draw(scr_memoryBox box, bool shortenTime = false)
    {
        box.timeStamp.text = shortenTime ? FinalEndTime.ToString("MM/dd") : PrintShortTimeStartToEnd;
        box.memText.SetText(ToString(descriptionOverride == null));
        box.memText.SetExternalTooltip(GetDescription());
    }

    public string GetDescription()
    {
        List<string> additional = new List<string>();
        if (descriptionOverride == null && entryDescription.Length > 0) additional.Add(entryDescription);
        if (!scr_System_CentralControl.current.isSafeMode && TagsCount > 0) additional.Add(PrintTags);
        additional.AddRange(MemInstanceDescriptions);

        UpdateSums();
        if (scr_System_CentralControl.current.isSafeMode) additional.Add($"Statmod: Check{cache_score.ToString("+0;-#")} Mood{moodSum} Stress{stressSum}");
        else additional.Add($"Statmod: Check{cache_score.ToString("+0;-#")} Mood{moodSum} Stress{stressSum} Lust{lustSum}");

        if (scr_System_CampaignManager.current.DebugMode) additional.Add("Internal Duration " + Duration);
        return String.Join("\n", additional);
    }
}

public class MemBlacklist
{
    public List<int> targets = new List<int>();
    public string comID = "";
    public int roomRef = -1;
    public int count = 1;
    public MemBlacklist(int roomRef, MemInstance instance)
    {
        Set(roomRef, instance);
    }

    /// <summary> (re)initialize - MemoryManager reuses blacklist objects across rebuilds </summary>
    public void Set(int roomRef, MemInstance instance)
    {
        this.roomRef = roomRef;
        this.targets = instance.targets;
        if (this.comID != instance.comID) this.cacheCOM = null;
        this.comID = instance.comID;
        this.count = instance.stackCount;
    }

    protected COM cacheCOM = null;
    public COM targetCOM { get
        {
            if (this.cacheCOM == null && this.comID != "") this.cacheCOM = scr_System_Serializer.current.MasterList.COMs.GetByID(this.comID);
            return this.cacheCOM;
        } }
}


public class MemInstance
{
    public List<int> targets = new List<int>();
    /// <summary>
    /// FirstName of any target removed from <see cref="targets"/> by <see cref="NotifyTargetUnregistered"/>,
    /// kept so this memory can still be displayed/printed after its subject no longer exists.
    /// Left null until first needed - almost no MemInstance ever has one.
    /// </summary>
    public List<string> unregisteredTargetNames = null;
    public List<string> tags = new List<string>();
    public bool isDoer = true;
    public int masterRef = -1;
    public string comID = "";
    //public int comVariantID = -1;
    public int attitude = (int)Memory_Attitude.Neutral;
    public int stackCount = 1;
    public Memory_Response response = Memory_Response.None;

    /// <summary>
    /// Description template. A source string "text with $elem1$ $elem2$||1||300" (loc keys ui_entry_memory_description_*)
    /// is split once into the template ("text with $elem1$ $elem2$") and numeric values (descValues) - merging sums the
    /// values, Print substitutes them. Old saves storing the joined string are split on first access.
    /// </summary>
    public string description
    {
        get { EnsureDescriptionParsed(); return _description; }
        set { _description = value ?? ""; descParsed = false; printCache = null; }
    }
    string _description = "";
    bool descParsed = false;
    /// <summary> $elemN$ values (index 0 = $elem1$), null when the description has none </summary>
    [JsonProperty] protected float[] descValues = null;
    /// <summary> decimal places each value was written with ("F1" amounts), reused when printing </summary>
    [JsonProperty] protected int[] descDecimals = null;
    string printCache = null;

    const string DescSeparator = "||";

    void EnsureDescriptionParsed()
    {
        if (descParsed) return;
        descParsed = true;

        int first = _description.IndexOf(DescSeparator, StringComparison.Ordinal);
        if (first < 0) return; // already a template (new saves keep descValues as loaded)

        int count = 0;
        for (int p = first; p >= 0; p = _description.IndexOf(DescSeparator, p + DescSeparator.Length, StringComparison.Ordinal)) count++;

        descValues = new float[count];
        descDecimals = new int[count];
        var culture = CultureInfo.CurrentCulture;
        var decimalSeparator = culture.NumberFormat.NumberDecimalSeparator;
        int start = first + DescSeparator.Length;
        for (int n = 0; n < count; n++)
        {
            int end = _description.IndexOf(DescSeparator, start, StringComparison.Ordinal);
            if (end < 0) end = _description.Length;
            var segment = _description.AsSpan(start, end - start).Trim();

            // amounts arrive formatted ("N0" -> "1,200", "F1" -> "12.5") in the current culture
            if (!float.TryParse(segment, NumberStyles.Float | NumberStyles.AllowThousands, culture, out descValues[n]))
                Debug.LogError($"MemInstance description value is not a number [{segment.ToString()}] in [{_description}]");

            int dot = segment.IndexOf(decimalSeparator.AsSpan(), StringComparison.Ordinal);
            descDecimals[n] = dot < 0 ? 0 : segment.Length - dot - decimalSeparator.Length;
            start = end + DescSeparator.Length;
        }
        _description = _description.Substring(0, first);
    }

    static readonly string[] ElemKeys = { "$elem1$", "$elem2$", "$elem3$", "$elem4$", "$elem5$", "$elem6$", "$elem7$", "$elem8$" };
    static string ElemKey(int number)
    {
        return number <= ElemKeys.Length ? ElemKeys[number - 1] : "$elem" + number + "$";
    }
    static readonly string[] DecimalFormats = { "N0", "N1", "N2", "N3" };
    string FormatDescValue(int index)
    {
        int decimals = descDecimals != null && index < descDecimals.Length ? descDecimals[index] : 0;
        return descValues[index].ToString(decimals < DecimalFormats.Length ? DecimalFormats[decimals] : "N" + decimals);
    }

    /// <summary>
    /// match similarity except: attitude, stackcount, response, description
    /// </summary>
    /// <param name="inst"></param>
    /// <returns></returns>
    public bool isSimilar(MemInstance inst)
    {
        return comID == inst.comID && masterRef == inst.masterRef && isDoer == inst.isDoer &&
            Utility.ListEquals(targets, inst.targets) &&
            Utility.ListEquals(tags, inst.tags);
    }

    [JsonIgnore]
    public bool isAction { get { return this.comID != ""; } }

    static readonly int MaxAttitude = GetMaxAttitude();
    static int GetMaxAttitude()
    {
        int max = int.MinValue;
        foreach (Memory_Attitude value in Enum.GetValues(typeof(Memory_Attitude))) max = Math.Max(max, (int)value);
        return max;
    }

    [JsonIgnore]
    public Memory_Attitude Attitude
    {
        get
        {
            return (Memory_Attitude)Math.Min(Math.Max((int)Memory_Attitude.None + 1, attitude / stackCount), MaxAttitude);
        }
    }

    /// <summary> cached until description / values / stackCount change (description setter, TryMergeWith) </summary>
    public string Print()
    {
        if (printCache != null) return printCache;
        EnsureDescriptionParsed();

        string dsc = _description;
        if (descValues != null)
        {
            for (int i = 0; i < descValues.Length; i++) dsc = dsc.Replace(ElemKey(i + 1), FormatDescValue(i));
        }

        // $elemN$ counters already carry the merged count - the xN suffix only for plain descriptions
        printCache = stackCount > 1 && descValues == null ? $"{dsc} x{stackCount}" : dsc;
        return printCache;
    }
    public MemInstance()
    {

    }
    public MemInstance(List<int> targets, List<string> targetTags, string comID, int comVariantID, int masterRef, bool isDoer, Memory_Response response, Memory_Attitude attitude, string description)
    {
        // we dont want target list null cuz we have addrange operate on it
        this.targets = targets == null ? new List<int>() : targets;
        // own copy: callers pass shared lists (EvaluationPackage's cached tag lists, one selfTag list for several
        // instances) and merges edit tags in place
        this.tags = targetTags == null ? new List<string>() : new List<string>(targetTags);
        Utility.DistinctInPlace(this.tags);
        this.comID = comID;
        //this.comVariantID = comVariantID;
        this.masterRef = masterRef;
        this.isDoer = isDoer;
        this.attitude = attitude == Memory_Attitude.None ? (int)Memory_Attitude.Neutral : (int)attitude;
        this.response = response > Memory_Response.Accept ? Memory_Response.Accept : response;
        this.description = description;
        //Debug.LogError($"new MemInstance, [{String.Join("|", this.targets)}] [{comID}] [{comVariantID}] [{masterRef}] [{isDoer}] [{this.attitude}] [{this.response}]");
    }

    public void ResetInternal(Memory_Response response, Memory_Attitude attitude)
    {
        this.attitude = attitude == Memory_Attitude.None ? (int)Memory_Attitude.Neutral : (int)attitude;
        this.response = response > Memory_Response.Accept ? Memory_Response.Accept : response;
    }

    /// <summary>
    /// Removes refID from targets (a character being unregistered can no longer be resolved by
    /// FindInstanceByID) and stacks its FirstName into unregisteredTargetNames instead, once per
    /// occurrence removed. Returns true if targets was modified.
    /// </summary>
    public bool NotifyTargetUnregistered(int refID, string firstName)
    {
        bool changed = false;
        for (int i = targets.Count - 1; i >= 0; i--)
        {
            if (targets[i] != refID) continue;
            targets.RemoveAt(i);
            if (unregisteredTargetNames == null) unregisteredTargetNames = new List<string>();
            unregisteredTargetNames.Add(firstName);
            changed = true;
        }
        return changed;
    }



    [JsonIgnore] public int Mood {
        get
        {
            var value = modMood;
            if (tags.Contains("unsafe") || tags.Contains("ignored")) return (int)value;
            
            value += ((float)Attitude - (float)Memory_Attitude.Neutral);

            if (tags.Contains("recreation"))
            {   // if recreation related, as long as its not bad, increase mood
                //if (Attitude >= Memory_Attitude.Neutral) value += 1;
            }
            else
            {

            }

            return (int)value; } }

    [JsonIgnore] public int Stress {
        get
        {
            var value = modStress;
            if ( tags.Contains("ignored")) return (int)value;
            
            if (tags.Contains("job"))
            {
                value -= 1;
                if (response > Memory_Response.Accept && response < Memory_Response.Success) value -= 1;
                else if (response > Memory_Response.Success) value += 1;
            }
            else if (tags.Contains("recreation"))
            {   // if recreation related, as long as its not bad, decrease stress
                if (Attitude > Memory_Attitude.Neutral) value += 1;
            }

            return (int)value;
        } }
    [JsonIgnore] public int Lust {
        get
        {
            var value = modLust;
            if (tags.Contains("ignored")) return (int)value;
            //if (Attitude > Memory_Attitude.Neutral && (tags.Contains("sex") || tags.Contains("massage") || tags.Contains("touch")) && !tags.Contains("safe")) value += 1;
            return (int)value;
        } }

    [JsonProperty] protected float modMood = 0, modStress = 0, modLust = 0;
    public void AddMoodletScore(float modMood, float modStress, float modLust)
    {
        this.modMood += modMood;
        this.modLust += modLust;
        this.modStress += modStress;
    }

    public bool canMergeWith(in MemInstance mem)
    {
        if (this.isDoer != mem.isDoer) return false;
        if (this.masterRef != mem.masterRef) return false;
        if (this.comID != mem.comID) return false;
        //if (this.comVariantID != mem.comVariantID) return false;
        if (this.response != mem.response) return false;
        if (!Utility.ListEquals(mem.targets, this.targets)) return false;

        // same template with the same number of $elemN$ values (description getters split old joined strings)
        if (this.description != mem.description) return false;
        int selfCount = this.descValues == null ? 0 : this.descValues.Length;
        int otherCount = mem.descValues == null ? 0 : mem.descValues.Length;
        return selfCount == otherCount;
    }

    public bool TryMergeWith(in MemInstance mem)
    {
        if (!canMergeWith(mem)) return false;

        for (int i = 0; i < mem.tags.Count; i++)
        {
            if (!this.tags.Contains(mem.tags[i])) this.tags.Add(mem.tags[i]);
        }

        this.modLust += mem.modLust;
        this.modMood += mem.modMood;
        this.modStress += mem.modStress;

        this.attitude += mem.attitude;
        this.stackCount += mem.stackCount;

        // $elemN$ counters add up (canMergeWith guarantees equal lengths)
        if (this.descValues != null)
        {
            for (int i = 0; i < this.descValues.Length; i++)
            {
                this.descValues[i] += mem.descValues[i];
                if (mem.descDecimals != null && this.descDecimals != null && mem.descDecimals[i] > this.descDecimals[i]) this.descDecimals[i] = mem.descDecimals[i];
            }
        }
        printCache = null;

        return true;
    }

    /// <summary>
    /// Might require owner input to factor in more stuff
    /// </summary>
    /// <param name="owner"></param>
    /// <returns></returns>
    public int AttitudeScore(Character_Trainable owner)
    {
        int score = 0;
        
        switch (response)
        {
            case Memory_Response.Refuse: score -= 1; break;
            //case Memory_Response.Success: score += 1; break;
            case Memory_Response.CriticalFailure: score -= 2; break;
            case Memory_Response.CriticalSuccess: score += 2; break;
            default: break;
        }

        if (Attitude > Memory_Attitude.None) score += (Attitude - Memory_Attitude.Neutral);

        return score;
    }
}

