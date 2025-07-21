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
        UpdateBlacklist();
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
        // ClearCache();
        bool clearcache = false;
        foreach (var entry in entries.Values)
        {
            clearcache = entry.Tick(t) || clearcache;
        }
        if (clearcache) ClearCache();
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

    [NonSerialized][JsonIgnore] public List<MemBlacklist> Blacklist = new List<MemBlacklist>();

    /// <summary>
    /// Return true if blacklist match roomref and comid, use for searching job only.
    /// </summary>
    /// <param name="ap"></param>
    /// <returns></returns>
    public bool MatchBlacklist(int roomRef, List<string> availableComID)
    {
        if (availableComID.Count < 1) return false;
        if (roomRef == -1) return false;
        foreach (var b in Blacklist)
        {
            if (b.comID == "" || b.roomRef == -1) continue;
            else if (availableComID.Contains(b.comID) && b.roomRef == roomRef) return true;
        }
        return false;
    }
    /// <summary>
    /// Return count of blacklist match if Owner is receiver and recently refused<br/>
    /// Match by doerRef and by targetCOM
    /// </summary>
    /// <param name="ep"></param>
    /// <returns></returns>
    public int MatchBlacklist(EvaluationPackage ep)
    {
        if (!ep.Package.receiver.Contains(Owner)) return 0;
        int count = 0;
        foreach(var b in Blacklist)
        {
            if (b.targets.Count < 1 || ep.Package.DoerRefs.Count < 1 || !Utility.ListContainsLoose(b.targets, ep.Package.DoerRefs)) continue;
            else if (ep.targetCOM == null || b.targetCOM == null) continue;

            if (ep.targetCOM == b.targetCOM || (ep.targetCOM.comTags.Contains("initSex") && b.targetCOM.comTags.Contains("initSex"))) count += b.count;
            else continue;
        }
        return count;
    }

    protected void UpdateBlacklist()
    {
        Blacklist.Clear();
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            if (Entries[i].Duration == 0) continue;

            Entries[i].FillBlacklist(Blacklist);
            if (!Entries[i].isRefuseOnly) break;
        }
    }

    public string PrintBlacklist()
    {
        List<string> s1 = new List<string>();
        foreach (var i in Blacklist) s1.Add($"{i.roomRef} {i.comID} {String.Join(" ", i.targets)} x{i.count}");
        return String.Join(" | ", s1);
    }

    /// <summary>
    /// Listing all recent [overrideMemoryCount] memory adjustment from newest to oldest<br/>
    /// Older memory adjustment intensity will be reduced by newer if newer has high opposite intensity
    /// </summary>
    /// <param name="overrideMemoryCount">How many memory entry are factored into statMod calculation</param>
    /// <returns></returns>
    public List<Stat_Modifier> GetRecentMemoryStatMods(int overrideMemoryCount = -1)
    {

        if (overrideMemoryCount == -1) overrideMemoryCount = Owner.Stats.MemoryEntryCount;
        List<int> rm_lust = new List<int>();
        List<int> rm_mood = new List<int>();
        List<int> rm_stress = new List<int>();

        if (recentMemoryCache == null)
        {
            overrideMemoryCount = Math.Min(overrideMemoryCount, entries.Count);
            recentMemoryCache = new List<Stat_Modifier>();
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (overrideMemoryCount < 1) break;
                if (Entries[i].Duration == 0) continue;
                else if (Entries[i].selfTags.Contains("unconscious")) continue;
                overrideMemoryCount -= 1;

                AddMoodlet(ref rm_lust, Entries[i].Mod_Lust);
                AddMoodlet(ref rm_mood, Entries[i].Mod_Mood);
                AddMoodlet(ref rm_stress, Entries[i].Mod_Stress);
            }
        }

        return recentMemoryCache;
    }
    List<Stat_Modifier> recentMemoryCache = null;


    public List<Memory_Entry> GetAllMemoryMatch(List<string> selfTags, List<string> targetTags, int minuteRollback)
    {
        var endtime = scr_System_Time.current.getCurrentTime() - TimeSpan.FromMinutes(minuteRollback);
        var results = new List<Memory_Entry>();
        var keys = this.entries.Keys;
        for(int i = keys.Count - 1; i >= 0; i--)
        {
            var key = keys[i];
            if (entries[key].EndTime < endtime) break;
            if (selfTags.Count > 0 && !Utility.ListContainsStrict(entries[key].selfTags, selfTags)) continue;
            if (targetTags.Count > 0 && !entries[key].HasInteractionWithTags(targetTags)) continue;
            results.Add(entries[key]);
        }
        return results;
    }


    protected void AddMoodlet(ref List<int> compareList, Stat_Modifier statmod)
    {
        if (statmod.valueType != Stat_Modifier_Type.number) return;
        if (int.TryParse(statmod.valueString, out int modvalue))
        {
            var oppCount = 0;
            var newVal = 0;
            if (modvalue > 0)
            {
                oppCount = compareList.FindAll(x => x < -modvalue).Count;
                newVal = Math.Max(0, modvalue - oppCount);
            }
            else //modvalue < 0
            {
                oppCount = compareList.FindAll(x => x > -modvalue).Count;
                newVal = Math.Min(0, modvalue + oppCount);
            }

            if (newVal == modvalue) recentMemoryCache.Add(statmod);
            else recentMemoryCache.Add(duplicateMoodlet(statmod, newVal));
            compareList.Add(newVal);
        }
        else return;
        
    }
    protected Stat_Modifier duplicateMoodlet(Stat_Modifier statmod, int value)
    {
        var newstuff = new Stat_Modifier();
        newstuff.statID = statmod.statID;
        newstuff.modKey = statmod.modKey;
        newstuff.type = statmod.type;
        newstuff.SetValueTypeAndString(statmod.valueType, value.ToString());
        //newstuff.SetValueTypeAndString("number", value)
        return newstuff;
    }

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
        Owner.Stats.RefreshAllStats();
        UpdateBlacklist();
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
    public Memory_Entry AddEntry(MemInstance memInstance, List<string> selfTags, int duration = -1, bool mergeWithAll = false)
    {
        // ClearCache();
#if UNITY_EDITOR
        if (memInstance.response == Memory_Response.None) Debug.LogError($"Logging Null response memory on {Owner.FirstName} about {memInstance.description}");
#endif
        var roomRef = scr_System_CampaignManager.current.GetCharaRoomInstance(Owner.RefID).RefID;
        Memory_Entry entry = new Memory_Entry(Owner, null, roomRef, selfTags, memInstance, "", duration);
        entry.MergeWithAll = mergeWithAll;

        if (this.Last == null || !this.Last.TryMergeWith(entry))
        {
            this.entries.Add(entry.EndTime.Ticks, entry);
            ClearCache();
            return entry;
        }
        else
        {
            ClearCache();
            return this.Last;
        }
    }

    /// <summary>
    /// Duration == -2 -> permanent. <br/> Duration == -1 -> default
    /// </summary>
    /// <param name="ep"></param>
    /// <param name="duration"></param>
    /// <returns></returns>
    public Memory_Entry AddEntry(EvaluationPackage ep, int duration = -1, bool mergeWithAll = false, bool interrupted = false)
    {


        List<string> selfTags, targetTags;
        Memory_Attitude attitude;
        List<int> targets = new List<int>();
        foreach(var i in ep.Actors) if (i != Owner) targets.Add(i.RefID);
        bool isDoer = false;
        string description = ep.Package.DescriptionText(Owner.RefID);

#if UNITY_EDITOR
        if (false && ep.Package.targetCOM is COM_FarmRecipe)
        {
            Debug.LogError($"adding farm recipe description {description}");
        }
#endif

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
            if (scr_System_CentralControl.current.LogPrefs.DLog_Memory) Debug.LogError($"Memory manager addentry EP error: {Owner.FirstName} not in ep");
            return null;
        }
        var memDuration = selfTags.Contains("important") || duration < -1 ? -2 : duration != -1 ? duration : Owner.Stats.MemoryLength;


        var job = ep == null || ep.Package == null ? null : ep.Package.job;
        var roomRef = ep == null || ep.Package == null ? -1 : ep.Package.RoomKey;
        var jobDesc = ep == null || ep.Package == null || ep.Package.job == null || !ep.job.MemoryEntrySoftMerge ? "" : ep.Package.job.GetJobDescription(Owner.RefID);

        MemInstance memInstance = new MemInstance(targets, targetTags, ep.targetCOM == null ? "" : ep.targetCOM.ID, ep.VariantID, ep.Master == null ? -1 : ep.Master.RefID, isDoer, interrupted ? Memory_Response.Interrupted : ep.Response, attitude, description);
        if (memInstance.response == Memory_Response.None) Debug.LogError($"Logging Null response memory on {Owner.FirstName} about {memInstance.description}");

        Memory_Entry entry = new Memory_Entry(Owner, job, roomRef, selfTags, memInstance, jobDesc, memDuration);
        entry.StartTime = ep.Package.StartTime;

        if (ep.targetCOM != null && (ep.targetCOM.comTags.Contains("initSex") || ep.targetCOM.comTags.Contains("endSex")))
        {
            entry.MergeWithAll = true;
            entry.entryDescription = memInstance.description;
        }
        else
        {
            entry.MergeWithAll = mergeWithAll;
        }

        if (this.Last == null || !this.Last.TryMergeWith(entry))
        {
            if (scr_System_CentralControl.current.LogPrefs.DLog_Memory) Debug.Log("memory new entry");
            this.entries.Add(entry.EndTime.Ticks, entry);
            ClearCache();
            return entry;
        }
        else
        {
            if (scr_System_CentralControl.current.LogPrefs.DLog_Memory) Debug.Log("memory merge last");
            ClearCache();
            return this.Last;
        }
    }
}

