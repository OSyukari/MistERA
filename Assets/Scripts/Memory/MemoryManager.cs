using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using Newtonsoft.Json;


/// <summary>
/// Manage Interaction Memory and Character Experiences
/// </summary>
[System.Serializable]
public class MemoryManager
{

    //public ExperienceManager Experience;
    //public SexLogManager SexLogManager;

    //[SerializeField] protected List<Memory_Entry> entries;

    [SerializeField][JsonProperty] protected SortedList<long, Memory_Entry> entries = null;
    [JsonIgnore] public List<Memory_Entry> Entries { get {
            if (entries == null) entries = new SortedList<long, Memory_Entry>();
            return entries.Values.ToList(); } }
    [JsonIgnore] public Memory_Entry Last { get {

            if (Entries == null || Entries.Count < 1) return null;
            return entries.Values[Entries.Count - 1]; } }

    private int ownerRef = -1;
    private Character_Trainable owner = null;
    [JsonIgnore] public Character_Trainable Owner
    {
        get
        {
            if (owner == null && ownerRef > -1) owner = scr_System_CampaignManager.current.FindInstanceByID(ownerRef);
            return owner;
        }
    }
    public void ReEstablishParent(Character_Trainable c) 
    {
        this.ownerRef = c.RefID;
        this.owner = c;

        foreach (var i in Entries) i.ReEstablishParent(c);
    }

    public MemoryManager()
    {
        this.entries = new SortedList<long, Memory_Entry>();
    }
    public MemoryManager(Character_Trainable c):this()
    {
        ReEstablishParent(c);
    }

    public void Tick(TimeSpan t)
    {
        if(recentMemoryCache != null) recentMemoryCache.Clear();
        recentMemoryCache = null;

        foreach (var entry in entries.Values) entry.Tick(t);
    }


    /// <summary>
    /// Wipe all 0 duration memory entries
    /// </summary>
    public void DailyClear()
    {
        for(var i = entries.Count - 1; i >= 0; i--)
        {
            if (entries.Values[i].Duration == 0) entries.RemoveAt(entries.IndexOfValue(entries.Values[i]));
        }
    }

    private void calculateValue(ref int i, ref int max, ref int min, int memValue)
    {
        max = Math.Max(max, memValue);
        min = Math.Min(min, memValue);
        if (memValue >= 0) i = Math.Min(max, i + memValue);
        else i = Math.Max(min, i + memValue);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="overrideMemoryCount">How many memory entry are factored into statMod calculation</param>
    /// <returns></returns>
    public List<Stat_Modifier> GetRecentMemoryStatMods(int overrideMemoryCount = -1)
    {
        if (overrideMemoryCount == -1) overrideMemoryCount = Owner.Stats.MemoryEntryCount;
        if (recentMemoryCache == null)
        {
            overrideMemoryCount = Math.Min(overrideMemoryCount, entries.Count);
            recentMemoryCache = new List<Stat_Modifier>();
            for (int i = 0; i < overrideMemoryCount && entries.Count - 1 - i >= 0; i++)
            {
                if (Entries[i].Duration == 0)
                {
                    overrideMemoryCount++;
                    continue;
                }
                int ii = entries.Count - 1 - i;
                //            if (entries[i].Duration)
                recentMemoryCache.Add(Entries[ii].Mod_Lust);
                recentMemoryCache.Add(Entries[ii].Mod_Mood);
                recentMemoryCache.Add(Entries[ii].Mod_Stress);
            }
        }

        return recentMemoryCache;
    }
    List<Stat_Modifier> recentMemoryCache = null;

    public int GetMemoryAdjustment(EvaluationPackage.Modifiers modifiers, int targetRef, COM com, List<string> tags = null, bool requireConsciousness = true)
    {
        Memory_Entry returnMem = null;
        if (tags == null) tags = com.comTags;

        if (!Owner.hasStatKeyword("memory")) return 0;
        else if (Owner.Stats.Consciousness.Tags.Contains("consciousness_reduced") || Owner.Stats.Consciousness.Tags.Contains("consciousness_unconscious"))
        {
            // choose memory regardless of target
            // choose memory regardless of command
            return ProcessMemoryEntry(modifiers, GetMostRecentEntry(-1, null, tags, requireConsciousness), targetRef, com);

            returnMem = GetMostRecentEntry(-1, com);
            if (returnMem != null) return ProcessMemoryEntry(modifiers, returnMem, targetRef, com);
            else return ProcessMemoryEntry(modifiers, GetMostRecentEntry(-1, null, tags), targetRef, com);
        }
        else
        {
            // choose com with only target
            // choose memory regardless of command
            return ProcessMemoryEntry(modifiers, GetMostRecentEntry(targetRef, null, tags, requireConsciousness), targetRef, com);

            returnMem = GetMostRecentEntry(targetRef, com);
            if (returnMem != null) return ProcessMemoryEntry(modifiers, returnMem, targetRef, com);
            else return ProcessMemoryEntry(modifiers, GetMostRecentEntry(targetRef, null, tags), targetRef, com);
        }
        // if high consciousness then require same REFID
        // if reduced then allow different REFID
    }


    public Memory_Entry FindEntryByDateTime(DateTime time)
    {
        return entries.ContainsKey(time.Ticks) ? entries[time.Ticks] : null;
    }
    public Memory_Entry FindEntryByDateTimeTick(long ticks)
    {
        return entries.ContainsKey(ticks) ? entries[ticks] : null;
    }
    private int ProcessMemoryEntry(EvaluationPackage.Modifiers modifiers, Memory_Entry mem, int targetRef, COM com)
    {
        if (mem == null) return 0;
        else return mem.GetInfluence(modifiers);
    }


    public IEnumerable<Memory_Entry> FindEntryWithFilter(int targetRef = -1, string targetCOM = "", List<string> comTags = null)
    {
        if (targetRef == -1 && targetCOM == "" && comTags == null) yield break;
        foreach (var i in Entries) if (i.Validate(targetRef, targetCOM, comTags)) yield return i;
    }

    private Memory_Entry GetMostRecentEntry(int targetRef = -1, COM com = null, List<string> comTags = null, bool checkConscious = true)
    {
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var mem = Entries[i];
            if (mem.Validate(targetRef, com == null ? "" : com.ID, comTags, checkConscious)) return mem;
        }
        return null;
    }




    //private int ownerRefID;
    //Character_Trainable ownerCache = null;
    //Character_Trainable Owner { get { if (ownerCache == null) ownerCache = scr_System_CampaignManager.current.FindInstanceByID(ownerRefID); return ownerCache; } }



    public void EndOngoingLog(DateTime actorJoinTime)
    {
        //SexLogManager.EndOngoingLog(timestamp);
        if (this.entries == null || this.entries.Count < 1) return;
        var last = this.Entries[this.entries.Count - 1];

        last.EndOngoing(scr_System_Time.current.getCurrentTime());
    }


    /// External Interfaces 
    /// 

    public void AddEntry_COM(List<string> selfTags, List<string> targetCOMtags, int targetRef, COM targetCOM, int comVariant, bool isDoer, string description, Memory_Response response, Memory_Attitude attitude, int duration = -1, int masterRef = -1, List<int> additionalActors = null)
    {
        //Memory_Entry_COM entry = new Memory_Entry_COM(ownerRef, targetRef, targetCOM, description, attitude_begin, attitude_end, duration);
        AddEntry(selfTags, targetCOMtags, isDoer, targetRef, description, response, attitude, duration, targetCOM, comVariant,  masterRef, additionalActors);
    }

    public void AddEntry_Request(List<string> selfTags, List<string> targetCOMtags, int targetRef, COM targetCOM, int comVariant, bool isDoer, string description, Memory_Attitude attitude, Memory_Response response, int duration = -1, int masterRef = -1, List<int> additionalActors = null)
    {
        //Memory_Entry_Request entry = new Memory_Entry_Request(ownerRef, targetRef, targetCOM, description, attitude, response, duration);
        AddEntry(selfTags, targetCOMtags, isDoer, targetRef, description, response, attitude, duration, targetCOM, comVariant,  masterRef, additionalActors);
    }

    /// <summary>
    /// use description to append more data using '||' split
    /// </summary>
    /// <param name="targetRef"></param>
    /// <param name="isDoer"></param>
    /// <param name="tags"></param>
    /// <param name="description"></param>
    /// <param name="attitude"></param>
    /// <param name="response"></param>
    /// <param name="duration"></param>
    /// <param name="masterRef"></param>
    public void AddEntry_Custom(List<string> selfTags, List<string> targetCOMtags, int targetRef, bool isDoer, string description, Memory_Attitude attitude, Memory_Response response, int duration = -1, int masterRef = -1)
    {
        //Memory_Entry_Custom entry = new Memory_Entry_Custom(ownerRef, targetRef, tags, description, attitude, response, duration);
        //if (description != null && description.Length > 0) Debug.LogError("Adding custom description " + description);
        AddEntry(selfTags, targetCOMtags, isDoer, targetRef, description,response, attitude, duration, null, -1, masterRef);
    }

    /// <summary>
    ///
    /// </summary>
    /// <param name="targetRef"></param>
    /// <param name="desc">Description. It is possible to insert multiple description using Split '||' with first item being the unique string ID, all other elements (numbers) will be merged and replace back in.</param>
    /// <param name="attitude"></param>
    /// <param name="response"></param>
    /// <param name="duration"></param>
    /// <param name="targetCOM"></param>
    /// <param name="tags">If targetCOM is non null, then anything filled in tags will be added to targetCOM's tags as additional tags.</param>
    protected void AddEntry(List<string> selfTags, List<string> targetCOMtags, bool isDoer, int targetRef, string desc, Memory_Response response, Memory_Attitude attitude, int duration = -1, COM targetCOM = null, int comVariant = -1,  int masterRef = -1, List<int> additionalActors = null)
    {
        bool debug = true && Owner.BaseID == "Campaign1_Char_Ako";

        duration = Owner.Stats.MemoryLength;
        var description = new List<string>();
        if (desc != null && desc.Length > 0) description.Add(desc);
        if (selfTags == null) selfTags = new List<string>();
        if(targetCOMtags == null) targetCOMtags = new List<string>();

        selfTags = selfTags.Distinct().ToList();
        targetCOMtags = targetCOMtags.Distinct().ToList();
        //if (targetCOM != null && targetCOM.comTags.Contains("initSex")) tags.Add("sex");

        var last = entries.Count > 0 ? Entries[entries.Count - 1] : null;

        List<int> targetRefs = new List<int> { targetRef };
        if (additionalActors != null) targetRefs.AddRange(additionalActors);

        int mergeComUnderTimeframe = 60;

        if (last != null && (last.StartTime == scr_System_Time.current.getCurrentTime()))// || (last.Tags.Contains("initSex") && !last.Tags.Contains("endSex"))))
        {
            if (debug) Debug.Log("MemoryManager sametimemerge");
            last.MergeEntry(isDoer, targetRefs, desc, response, attitude, duration, targetCOM, comVariant,  masterRef, selfTags, targetCOMtags);
        }
        else if (   last != null &&
                    areTagsMergeable(selfTags, last.selfTags) && areTagsMergeable(targetCOMtags, last.targetTags) &&
                    ( targetCOM == null || targetCOM.TimeScale <= 30) &&
                    (   // limit merging to stack that is less than 5 minutes and total less than 15 minutes. Longer com will not be merged. Sorter COM will be merged up to 15 min
                        //(last.Tags.Contains("mergeWithAll") || tags.Contains("mergeWithAll")) ||
                        ((targetCOM == null || last.hasInteractionWithCOMID(targetCOM.ID)) && ((scr_System_Time.current.getCurrentTime() - last.StartTime).TotalMinutes <= mergeComUnderTimeframe)) || // non sex require targetcom the same, allow new actor joining
                        (last.isSexMemory && last.isOngoing) || // targetCOM.isSexCOM, allow sex to merge with all non sex
                        (last.isSexTouchMemory && last.isOngoing && targetCOM != null && (targetCOM.isSexCOM || targetCOM.isTouchCOM || targetCOM.isUnsafe)) ||
                        (last.isTouchMemory && (targetCOM != null && !targetCOM.isSexCOM) && ((targetCOM != null && targetCOM.isTouchCOM) || response != Memory_Response.Refuse))  // targetCOM.isTouchCOM, forbid upward merge with sex com
                    )
                )
        {
            if (debug) Debug.Log("MemoryManager tagsmerge");
            // merge lastEntry with current Entry
            // masterref not merged, store it with com
            last.MergeEntry(isDoer, targetRefs, desc, response, attitude, duration, targetCOM, comVariant, masterRef, selfTags, targetCOMtags);
        }
        else
        {
            // make new entry
            if (debug)
            {
                if (last != null) Debug.Log("MemoryManager makeNewEntry, prev conditions:" 
                + " tagsMergeble[" + (areTagsMergeable(selfTags, last.selfTags)) + " "+ (areTagsMergeable(targetCOMtags, last.targetTags)) 
                + "] timecost [" + ((targetCOM == null || targetCOM.TimeScale <= 5)) + "]"
               // +" cond0["+ (last.Tags.Contains("") || tags.Contains("mergeWithAll")) + "]" 
                + $" cond1[{targetCOM != null} {targetCOM != null && last.hasInteractionWithCOMID(targetCOM.ID)} {(scr_System_Time.current.getCurrentTime() - last.StartTime).Minutes < mergeComUnderTimeframe}"
                + "] cond2[" + (last.isSexMemory)+ " " + (last.isOngoing)
                + $"] cond3[{last.isSexTouchMemory} {last.isOngoing} {targetCOM != null && targetCOM.isSexCOM} {targetCOM != null && targetCOM.isTouchCOM} {targetCOM != null && targetCOM.isUnsafe}"
                + "] cond4["+ (last.isTouchMemory)+ " " + (targetCOM != null && !targetCOM.isSexCOM) + " "+ (targetCOM != null && targetCOM.isTouchCOM)+ "||"+ (response != Memory_Response.Refuse) + "]");
                else Debug.Log("MemoryManager newEntry last null");
            }
            string roomName = scr_System_CampaignManager.current.GetCharaRoomInstance(this.ownerRef).DisplayName;
            Memory_Entry entry = new Memory_Entry(this.ownerRef, targetRefs, description, response, attitude, duration, targetCOM, comVariant, isDoer, masterRef, selfTags, targetCOMtags, roomName);
            entries.Add(entry.StartTime.Ticks, entry);
            /*  new empty entry with mergewithall
             
                if (debug) Debug.Log("MemoryManager mergewithall， cannot merge with prev, cond1["+(last != null) +"] cond2.1["+(last == null?"null": last.isOngoing) +"] cond2.2["+(last == null?"null": last.StartTime == scr_System_Time.current.getCurrentTime()) +"]");
                string roomName = scr_System_CampaignManager.current.GetCharaRoomInstance(this.ownerRef).DisplayName;
                Memory_Entry entry = new Memory_Entry(this.ownerRef, targetRefs, description, attitude, response, attitude_end, duration, targetCOM, comVariant, isDoer, masterRef, tags, roomName);
                entries.Add(entry.StartTime.Ticks, entry);
             
             */
        }
    }

    protected bool areTagsMergeable(List<string> newTags, List<string> lastTags)
    {
        var returnVal = true;

        returnVal = (newTags.Contains("timestop") == lastTags.Contains("timestop")) && returnVal;
        returnVal = (newTags.Contains("sleeping") == lastTags.Contains("sleeping")) && returnVal;
        returnVal = (newTags.Contains("unconscious") == lastTags.Contains("unconscious")) && returnVal;

        returnVal = !(newTags.Contains("sex") && lastTags.Contains("safe")) && returnVal;

        return returnVal || newTags.Contains("mergeWithAll") || lastTags.Contains("mergeWithAll");
    }

}

