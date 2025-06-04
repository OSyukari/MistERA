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
        ClearCache();

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
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (overrideMemoryCount < 1) break;
                if (Entries[i].Duration == 0) continue;
                overrideMemoryCount -= 1;
                //            if (entries[i].Duration)
                recentMemoryCache.Add(Entries[i].Mod_Lust);
                recentMemoryCache.Add(Entries[i].Mod_Mood);
                recentMemoryCache.Add(Entries[i].Mod_Stress);
            }
            /*
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
            */
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
        foreach (var i in Entries) if (i.isRelevant(targetRef, targetCOM, comTags)) yield return i;
    }

    private Memory_Entry GetMostRecentEntry(int targetRef = -1, COM com = null, List<string> comTags = null, bool checkConscious = true)
    {
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            var mem = Entries[i];
            if (mem.isRelevant(targetRef, com == null ? "" : com.ID, comTags, checkConscious)) return mem;
        }
        return null;
    }

    public void ClearCache()
    {
        if (recentMemoryCache != null) recentMemoryCache.Clear();
        recentMemoryCache = null;
    }


    //private int ownerRefID;
    //Character_Trainable ownerCache = null;
    //Character_Trainable Owner { get { if (ownerCache == null) ownerCache = scr_System_CampaignManager.current.FindInstanceByID(ownerRefID); return ownerCache; } }

    /// <summary>
    /// Duration == -2 -> permanent. <br/> Duration == -1 -> default
    /// </summary>
    /// <param name="s"></param>
    /// <param name="duration"></param>
    /// <returns></returns>
    /// 
    public Memory_Entry AddEntry(MemInstance memInstance, List<string> selfTags, int duration = -1)
    {
        ClearCache();

        var roomRef = scr_System_CampaignManager.current.GetCharaRoomInstance(Owner.RefID).RefID;
        Memory_Entry entry = new Memory_Entry(Owner, null, roomRef, selfTags, memInstance);

        if (this.Last == null || !this.Last.TryMergeWith(entry))
        {
            this.entries.Add(entry.StartTime.Ticks, entry);
            return entry;
        }
        else return this.Last;

    }
    public Memory_Entry AddEntry(string description, List<int> targets, List<string> selfTags, List<string> targetTags, int duration = -1, Memory_Response response = Memory_Response.Accept, Memory_Attitude attitude = Memory_Attitude.Neutral)
    {
        ClearCache();

        var roomRef = scr_System_CampaignManager.current.GetCharaRoomInstance(Owner.RefID).RefID;

        MemInstance memInstance = new MemInstance(targets, targetTags, "", -1, -1, false, response, attitude, description);
        Memory_Entry entry = new Memory_Entry(Owner, null, roomRef, selfTags, memInstance);

        if (this.Last == null || !this.Last.TryMergeWith(entry))
        {
            this.entries.Add(entry.StartTime.Ticks, entry);
            return entry;
        }
        else return this.Last;
    }
    /// <summary>
    /// Duration == -2 -> permanent. <br/> Duration == -1 -> default
    /// </summary>
    /// <param name="ep"></param>
    /// <param name="duration"></param>
    /// <returns></returns>
    public Memory_Entry AddEntry(EvaluationPackage ep, int duration = -1)
    {
        ClearCache();

        List<string> selfTags, targetTags;
        Memory_Attitude attitude;
        List<int> targets = new List<int>();
        foreach(var i in ep.Actors) if (i != Owner) targets.Add(i.RefID);
        bool isDoer = false;
        string description = ep.Package.DescriptionText(Owner.RefID);

        if (ep.Doer == Owner)
        {
            selfTags = ep.DoerSelfTag;
            targetTags = ep.ReceiverTargetTag;
            attitude = ep.DoerAttitude;
            isDoer = true;
        }
        else if (ep.Receiver == Owner && !ep.Package.ComTags.Contains("ignored"))
        {
            selfTags = ep.ReceiverSelfTag;
            targetTags = ep.DoerTargetTag;
            attitude = ep.ReceiverAttitude;
        }
        else if (ep.Master == Owner)
        {
            selfTags = new List<string>();
            targetTags = new List<string>();
            attitude = Memory_Attitude.Neutral;
        }
        else
        {
            return null;
        }
        var memDuration = selfTags.Contains("important") || duration < -1 ? -2 : duration != -1 ? duration : Owner.Stats.MemoryLength;


        var job = ep == null || ep.Package == null ? null : ep.Package.job;
        var roomRef = ep == null || ep.Package == null ? -1 : ep.Package.RoomKey;
        var jobDesc = ep == null || ep.Package == null || ep.Package.job == null || !ep.job.MemoryEntrySoftMerge ? "" : ep.Package.job.GetJobDescription(Owner.RefID);

        MemInstance memInstance = new MemInstance(targets, targetTags, ep.targetCOM == null ? "" : ep.targetCOM.ID, ep.VariantID, ep.Master == null ? -1 : ep.Master.RefID, isDoer, ep.Response, attitude, description);
        Memory_Entry entry = new Memory_Entry(Owner, job, roomRef, selfTags, memInstance, jobDesc, memDuration);

        if (this.Last == null || !this.Last.TryMergeWith(entry))
        {
            this.entries.Add(entry.StartTime.Ticks, entry);
            return entry;
        }
        else return this.Last;
    }
}

