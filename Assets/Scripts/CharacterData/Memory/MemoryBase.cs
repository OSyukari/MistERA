using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
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
    [JsonProperty] public List<string> selfTags = new List<string>();
    List<string> targetTags = new List<string>();

    public void ReEstablishParent(Character_Trainable c)
    {
        this.ownerRef = c.RefID;
        this.owner = c; 

        foreach(var entry in this.interactions)
        {
            entry.tags = entry.tags.Distinct().ToList();
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

    protected List<int> targetRefs = null;
    [JsonIgnore] public List<int> TargetRefs { get { 
            
            if (targetRefs == null)
            {
                targetRefs = new List<int>();
                foreach (var inst in interactions)
                {
                    targetRefs.AddRange(inst.targets);
                }
                targetRefs = targetRefs.Distinct().ToList();
                targetRefs.Remove(Owner.RefID);
            }
            return targetRefs;
        } }
    private List<Character_Trainable> targets = null;
    [JsonIgnore] public List<Character_Trainable> Targets
    {
        get
        {
            if (targets == null)
            {
                targets = new List<Character_Trainable>();
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
                    if (targets.Find(x=>x.RefID == refID) == null) targets.Add(scr_System_CampaignManager.current.FindInstanceByID(refID));
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
            return names;
        } }


    [JsonProperty] protected int duration = -1;
    [JsonIgnore] public int Duration { get { return duration; } set { this.duration = value; } }

    public List<MemInstance> interactions = new List<MemInstance>();

    public void FillBlacklist(List<MemBlacklist> Blacklist)
    {
        foreach(var i in this.interactions)
        {
            if (i.response < Memory_Response.Accept)
            {
                // in case of memory merge contains both accept and refuse, skip it
                if (this.interactions.Find(x => x.isSimilar(i) && x.response >= Memory_Response.Interrupted) != null) continue;
                var lists = Blacklist.Find(x => x.comID == i.comID && x.roomRef == this.roomRef && Utility.ListEquals(i.targets, x.targets));
                if (lists != null) lists.count += i.stackCount;
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

    public Memory_Entry(Character_Trainable c, Job job, int roomRef, List<string> selfTags, MemInstance mem, string entryDescription = "", int duration = -1) : this()
    {
        int comTime = mem == null || mem.comID == "" ? 0 : scr_System_Serializer.current.index_COM.GetByID(mem.comID).TimeScale;
        
        this.EndTime = scr_System_Time.current.getCurrentTime();
        this.StartTime = EndTime - new TimeSpan(0, comTime, 0);
        this.ownerRef = c.RefID;
        this.interactions.Add(mem);

        this.duration = selfTags != null && selfTags.Contains("important") ? -1 : duration < -1 ? -2 : Owner.Stats.MemoryLength; 
        this.roomRef = roomRef;
        if (selfTags != null)
        {
            this.selfTags.AddRange(selfTags);
            this.selfTags = this.selfTags.Distinct().ToList();
        }
        this.entryDescription = entryDescription;
        if (job != null) this.jobRefID = job.RefID;
        this.softMerge = job == null || job.MemoryEntrySoftMerge;
        // update all internal
        InternalUpdate();
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

        if (other.Tags.Contains("initSex")) return false;

        if (other.Tags.Contains("expeditionEnd")) return false;
        if (other.Tags.Contains("expedition") && selfTags.Contains("expedition") && !selfTags.Contains("expeditionEnd")) return true;

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
        this.selfTags.AddRange(other.selfTags);
        this.selfTags = this.selfTags.Distinct().ToList();

        this.jobRefID = !MergeWithAll && this.jobRefID != -1 ? this.jobRefID : other.jobRefID != -1 ? other.jobRefID : -1;
        this.entryDescription = this.entryDescription != "" ? this.entryDescription : other.entryDescription != "" ? other.entryDescription : "";

        this.MergeWithAll = false;

        if (this.duration < 0 || other.duration < 0) this.duration = -1;
        else if (this.selfTags.Contains("important")) this.duration = -1;
        else this.duration = Math.Max(this.duration, other.duration);

        // update description, stat calculation, etc
        InternalUpdate();
        return true;
    }

    [JsonIgnore] [NonSerialized] public bool isRefuseOnly = false;


    List<MemInstance> Actions = new List<MemInstance>();
    protected void InternalUpdate()
    {
        Actions.Clear();
        targetRefs = null;
        targets = null;
        targetTags.Clear();
        memInstanceDescriptionCache = new List<string>();
        float scoreMod_Mood = 0, scoreMod_Stress = 0, scoreMod_Lust = 0;
        cache_score = 0; cache_acceptCount = 0; cache_refuseCount = 0;
        int maxLust = 0, minLust = 0, maxMood = 0, minMood = 0, maxStress = 0, minStress = 0;

        foreach (var i in interactions)
        {
            memInstanceDescriptionCache.Add(i.Print());

            if (i.response != Memory_Response.Refuse) cache_acceptCount += i.stackCount;
            else cache_refuseCount += i.stackCount;
            int iLust = i.Lust, iMood = i.Mood, iStress = i.Stress;


            cache_score += i.AttitudeScore(Owner);

            maxLust = Math.Max(maxLust, iLust);
            minLust = Math.Min(minLust, iLust);
            maxMood = Math.Max(maxMood, iMood);
            minMood = Math.Min(minMood, iMood);
            maxStress = Math.Max(maxStress, iStress);
            minStress = Math.Min(minStress, iStress);

            targetTags.AddRange(i.tags);
            targetTags = targetTags.Distinct().ToList();

            if (i.isAction) Actions.Add(i);

            //Debug.Log($"memory merge with {i.description}, Mood|{scoreMod_Mood}+{iMood} {maxMood} {minMood}| Stress|{scoreMod_Stress}+{iStress} {maxStress} {minStress}| Lust|{scoreMod_Lust}+{iLust} {maxLust} {minLust}|");
            // EvaluateSingle(Owner, i, ref cache_score, ref cache_acceptCount, ref cache_refuseCount);
            scoreMod_Lust += iLust * i.stackCount;
            scoreMod_Mood += iMood * i.stackCount;
            scoreMod_Stress += iStress * i.stackCount;
        }

        Mod_Stress.Clear();
        var stresslist = SplitScore((int)scoreMod_Stress, minStress, maxStress);
        for(int i = 0; i < stresslist.Count; i++)
        {
            var moodlet = initMoodlet("chara_status_stress", i);
            moodlet.DisplayName = this.ToString(true, false);
            moodlet.SetValueTypeAndString(Stat_Modifier_Type.number, stresslist[i].ToString());
            Mod_Stress.Add(moodlet);
        }

        Mod_Mood.Clear();
        var moodlist = SplitScore((int)scoreMod_Mood, minMood, maxMood);
        for (int i = 0; i < moodlist.Count; i++)
        {
            var moodlet = initMoodlet("chara_status_mood", i);
            moodlet.DisplayName = this.ToString(true, false);
            moodlet.SetValueTypeAndString(Stat_Modifier_Type.number, moodlist[i].ToString());
            Mod_Mood.Add(moodlet);
        }

        Mod_Lust.Clear();
        var lustlist = SplitScore((int)scoreMod_Lust, minLust, maxLust);
        for (int i = 0; i < lustlist.Count; i++)
        {
            var moodlet = initMoodlet("chara_status_lust", i);
            moodlet.DisplayName = this.ToString(true, false);
            moodlet.SetValueTypeAndString(Stat_Modifier_Type.number, lustlist[i].ToString());
            Mod_Lust.Add(moodlet);
        }

        moodSum = "";
        stressSum = "";
        lustSum = "";

        if (Mod_Mood.Count > 3)
        {
            int sum = 0;
            foreach (var i in Mod_Mood) sum += (int)UtilityEX.StatValue(i, null);
            moodSum = $"...{sum.ToString("+0;-#")}";
        }
        else foreach (var i in Mod_Mood) moodSum += UtilityEX.StatValue(i, null).ToString("+0;-#");

        if (Mod_Stress.Count > 3)
        {
            int sum = 0;
            foreach (var i in Mod_Stress) sum += (int)UtilityEX.StatValue(i, null);
            stressSum = $"...{sum.ToString("+0;-#")}";
        }
        else foreach (var i in Mod_Stress) stressSum += UtilityEX.StatValue(i, null).ToString("+0;-#");

        if (Mod_Lust.Count > 3)
        {
            int sum = 0;
            foreach (var i in Mod_Lust) sum += (int)UtilityEX.StatValue(i, null);
            lustSum = $"...{sum.ToString("+0;-#")}";
        }
        else foreach (var i in Mod_Lust) lustSum += UtilityEX.StatValue(i, null).ToString("+0;-#");

        MergeWithAll = MergeWithAll || (targetTags.Contains("initSex") && !targetTags.Contains("endSex"));

        isEvaluationCached = true;

        isRefuseOnly = cache_refuseCount > cache_acceptCount && cache_acceptCount == 0;
    }

    string moodSum = "", stressSum = "", lustSum = "";
    [JsonIgnore] public string MoodSum { get { return moodSum; } }
    [JsonIgnore] public string StressSum { get { return stressSum; } }
    [JsonIgnore] public string LustSum { get { return lustSum; } }
    public int roomRef = -1;

    protected List<int> SplitScore(int original, int min, int max)
    {
        List<int> result = new List<int>();
        while (original != 0)
        {
            if (max > 0 && original >= max)
            {
                original -= max;
                result.Add(max);
            }
            else if (min < 0 && original <= min)
            {
                original -= min;
                result.Add(min);
            }
            else if (original != 0)
            {
                result.Add(original);
                break;
            }
            else
            {
                break;
            }
        }
        return result;
    }


    [JsonIgnore] public List<string> Tags { get { return Enumerable.Concat(selfTags, targetTags).ToList(); } }

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
        if (Tags.Contains("important")) duration = -1;
        else if (duration > 0) duration = Math.Max(duration - t, 0);

        return duration == 0;
    }


    public bool isRelevant(int targetRef = -1, string targetCOM = "", List<string> comTags = null, bool requireConsciousness = true) 
    {
        if ((targetRef == -1 || this.TargetRefs.Contains(targetRef))
            && (targetCOM == "" || hasInteractionWithCOMID(targetCOM))
            && (comTags == null || Utility.ListContainsStrict(this.Tags, comTags))
            && (!requireConsciousness || (!this.Tags.Contains("unconscious") && (!this.Tags.Contains("sleeping") && (Owner.CanActInTimeStop || !this.Tags.Contains("timestop")))))) return true;
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

    public List<Stat_Modifier> Mod_Stress = new List<Stat_Modifier>();
    public List<Stat_Modifier> Mod_Mood = new List<Stat_Modifier>();
    public List<Stat_Modifier> Mod_Lust = new List<Stat_Modifier>();


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
        else if (this.MemInstanceDescriptions != null && this.MemInstanceDescriptions.Count > 0) body = MemInstanceDescriptions[0];
        else body = "Error no stuff";

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


    private List<string> memInstanceDescriptionCache = null;
    [JsonIgnore] public List<string> MemInstanceDescriptions
    {
        get
        {
            return memInstanceDescriptionCache;
        }
    }

    public void Draw(scr_memoryBox box, bool shortenTime = false)
    {
        box.timeStamp.text = shortenTime ? FinalEndTime.ToString("MM/dd") : PrintShortTimeStartToEnd;
        box.memText.SetText(ToString(true));

        List<string> additional = new List<string>();
        if (entryDescription.Length > 0) additional.Add(entryDescription);
        if (!scr_System_CentralControl.current.isSafeMode && Tags.Count > 0) additional.Add(PrintTags);
        additional.AddRange(MemInstanceDescriptions);

        if (scr_System_CentralControl.current.isSafeMode) additional.Add($"Statmod: Check{cache_score.ToString("+0;-#")} Mood{moodSum} Stress{stressSum}");
        else additional.Add($"Statmod: Check{cache_score.ToString("+0;-#")} Mood{moodSum} Stress{stressSum} Lust{lustSum}");

        if (scr_System_CampaignManager.current.DebugMode) additional.Add("Internal Duration " + Duration);
        box.memText.SetExternalTooltip(String.Join("\n", additional));
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
        this.roomRef = roomRef;
        this.targets = instance.targets;
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
    public List<string> tags = new List<string>();
    public bool isDoer = true;
    public int masterRef = -1;
    public string comID = "";
    //public int comVariantID = -1;
    public int attitude = (int)Memory_Attitude.Neutral;
    public int stackCount = 1;
    public Memory_Response response = Memory_Response.None;
    public string description = "";

    /// <summary>
    /// match similarity except: attitude, stackcount, response, description
    /// </summary>
    /// <param name="inst"></param>
    /// <returns></returns>
    public bool isSimilar(MemInstance inst)
    {
        return Utility.ListEquals(targets, inst.targets) &&
            Utility.ListEquals(tags, inst.tags) && 
            comID == inst.comID && masterRef == inst.masterRef && isDoer == inst.isDoer;
    }

    [JsonIgnore]
    public bool isAction { get { return this.comID != ""; } }
    [JsonIgnore]
    public Memory_Attitude Attitude
    {
        get
        {
            return (Memory_Attitude)Math.Min(Math.Max((int)Memory_Attitude.None + 1, attitude / stackCount), (int)Enum.GetValues(typeof(Memory_Attitude)).Cast<Memory_Attitude>().Last());
        }
    }

    public string Print()
    {
        List<string> dscs = this.description.Split("||").ToList();
        string dsc = dscs.Count > 0 ? dscs[0] : "";

        for(int i = 1; i < dscs.Count; i++)
        {
            var keyword = "$elem" + i.ToString("N0") + "$";
            dsc = dsc.Replace(keyword, dscs[i]);
        }

        return stackCount > 1 ? $"{dsc} x{stackCount}" : dsc;
    }
    public MemInstance()
    {

    }
    public MemInstance(List<int> targets, List<string> targetTags, string comID, int comVariantID, int masterRef, bool isDoer, Memory_Response response, Memory_Attitude attitude, string description)
    {
        // we dont want target list null cuz we have addrange operate on it
        this.targets = targets == null ? new List<int>() : targets;
        this.tags = targetTags;
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
        if (!Utility.ListEquals(mem.targets, this.targets)) return false;
        if (this.isDoer != mem.isDoer) return false;
        if (this.masterRef != mem.masterRef) return false;
        if (this.comID != mem.comID) return false;
        //if (this.comVariantID != mem.comVariantID) return false;
        if (this.response != mem.response) return false;
        
        List<string> descSplit = this.description.Split("||").ToList();
        List<string> otherSplit = mem.description.Split("||").ToList();

        if (descSplit.Count != otherSplit.Count) return false;
        else if (descSplit.Count > 0 && descSplit[0] != otherSplit[0]) return false;
        return true;
    }

    public bool TryMergeWith(in MemInstance mem)
    {
        if (!canMergeWith(mem)) return false;

        this.tags.AddRange(mem.tags);
        this.tags = this.tags.Distinct().ToList();

        this.modLust += mem.modLust;
        this.modMood += mem.modMood;
        this.modStress += mem.modStress;

        this.attitude += mem.attitude;
        this.stackCount += mem.stackCount;

        List<string> descSplit = this.description.Split("||").ToList();
        List<string> otherSplit = mem.description.Split("||").ToList();

        for(int i = 1; i < descSplit.Count && i < otherSplit.Count; i++)
        {
            if (int.TryParse(descSplit[i], out int selfCount) && int.TryParse(otherSplit[i], out int otherCount))
            {
                descSplit[i] = (selfCount + otherCount).ToString();
            }
            else
            {
                Debug.LogError($"MemInstance merge description error, failed to int.parse {descSplit[i]} or {otherSplit[i]} to int");
            }
        }

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

