using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using Newtonsoft.Json;

[System.Serializable]
public class StatsManager
{


    protected Character_Trainable owner = null;
    [JsonIgnore] public Character_Trainable Owner
    {
        get
        {
            if (owner == null && ownerRefID > -1) owner = scr_System_CampaignManager.current.FindInstanceByID(ownerRefID);
            return owner;
        }
    }

    protected List<Needs> needsList_cache = null;
    [JsonIgnore] public List<Needs> Needs{
        get{
        if (needsList_cache == null){
            var tempList = new List<Needs>();
            needsList_cache = new List<Needs>();
            if (Owner.Race != null) tempList.AddRange(Owner.Race.needs);
            if (Owner.RaceTemplate != null) tempList.AddRange(Owner.RaceTemplate.needs);

            foreach(var i in tempList){
                if (i.requiresStatKeyword != "" && !Owner.hasStatKeyword(i.requiresStatKeyword)) continue;
                // if exist same, replace it.
                needsList_cache.RemoveAll(x=>x.ID == i.ID || i.overwritesNeedsIDs.Contains(x.ID));
                needsList_cache.Add(i);
            }
        }
        return needsList_cache;
    }}

    [SerializeField][JsonProperty] protected List<Stats_Derived_Extended_Instance> list_statsExtended = new List<Stats_Derived_Extended_Instance>();
    [JsonIgnore] public List<Stats_Derived_Extended_Instance> StatsExtended { get { return list_statsExtended; } }

    /// <summary>
    /// Cache the <string statID, object statEX> to save the search for the current session
    /// </summary>
    [JsonIgnore] public List<Stats_Derived_Instance> list_statsDerived { get
        {
            if (_list_statDerived == null)
            {
                _list_statDerived = new List<Stats_Derived_Instance>();
                foreach(var i in scr_System_Serializer.current.index_StatsDerived.list)
                {
                    if (Owner.hasStatKeyword(i.StatKeyword)) _list_statDerived.Add(i.Instantiate(this));
                }
            }
            return _list_statDerived;
        } }
    protected List<Stats_Derived_Instance> _list_statDerived = null;
    public Stats_Derived_Extended_Instance GetStatEx(string statID)
    {
        return StatsExtended.Find(x => x.ID == statID);
    }

    public void ReEstablishParent(Character_Trainable chara)
    {
        this.owner = chara;
        ownerRefID = chara.RefID;

        this.Strength.ReEstablishParent(Owner);
        this.Constitution.ReEstablishParent(Owner);
        this.Psyche.ReEstablishParent(Owner);
        this.Willpower.ReEstablishParent(Owner);

        if (this.StatusInstances != null) foreach (var i in StatusInstances) i.ReEstablishParent(chara);
        foreach (var i in list_statsExtended) i.ReEstablishParent(chara);
        if (this.statusInstancesEx != null) foreach (var i in _statusInstancesEx) i.ReEstablishParent(chara);

        this.RefreshAllStats(true);
    }
    public StatsManager()
    {

    }

    public StatsManager(Character_Trainable chara)
    {
        ReEstablishParent(chara);
        
        //RefreshAllStats();
        if (chara.RefID > -1) InitializeWithID(chara.RefID);
    }
    public void InitializeWithID(int refID, int str = 10, int con = 10, int psy = 10, int wil = 10)
    {
        this.ownerRefID = refID;
        //generate all stat_derived_extended

        //Debug.LogError("Initializing ID "+ownerRefID);
        //if (this.statusInstancesEx == null) this._statusInstancesEx = new List<StatusEx_Instance>();
        //if (this.StatusInstances == null) this._statusInstances = new List<Status_Instance>();
        this.modifiers.Clear();
        this.modifiers_temporary.Clear();

        foreach (var statusEX in scr_System_Serializer.current.index_StatusEX.list)
        {
            //Debug.Log("adding statusEx " + statusEX.statusID + " to chara " + Owner.FullName+" on list isNull? "+(statusInstancesEx == null));
            statusInstancesEx.Add(statusEX.Instantiate(refID));
        }

        foreach(var status in scr_System_Serializer.current.index_Status.list)
        {
            if (status.constant) StatusInstances.Add(status.Instantiate(refID));
        }

        if (Strength == null) Debug.LogError("Strength null");
        Strength.SetValue(str);
        Constitution.SetValue(con);
        Psyche.SetValue(psy);
        Willpower.SetValue(wil);
        RefreshAllStats(true);
    }

    

    [SerializeField][JsonProperty] protected Stats_Base baseStat_STR = null, baseStat_CON = null, baseStat_PSY = null, baseStat_WIL = null;
    [JsonIgnore] public Stats_Base Strength { get {
            if (baseStat_STR == null) baseStat_STR = new Stats_Base(this, "Strength");
            return baseStat_STR; } }
    [JsonIgnore] public Stats_Base Constitution { get {
            if (baseStat_CON == null) baseStat_CON = new Stats_Base(this, "Constitution");
            return baseStat_CON; } }
    [JsonIgnore] public Stats_Base Psyche { get {
            if (baseStat_PSY == null) baseStat_PSY = new Stats_Base(this, "Psyche");
            return baseStat_PSY; } }
    [JsonIgnore] public Stats_Base Willpower { get {
            if (baseStat_WIL == null) baseStat_WIL = new Stats_Base(this, "Willpower");
            return baseStat_WIL; } }


    /// <summary>
    /// Lazy Refresh. Only update whats strictly necessary.
    /// </summary>
    public void RefreshAllStats(bool resetPermanentStatCache = false)
    {   // run after any stat change, notify and call refresh


        // clear cache
        if (cached_values == null) cached_values = new Dictionary<Tuple<string, List<string>>, float>();
        else cached_values.Clear();

        if (this.ownerRefID == -1) return;

        if (resetPermanentStatCache)
        {
            // this also force clear the modifiers and recollect all of them
            modifiers.Clear();

            //if (Owner == null) Debug.LogError("Owner Null");
            if (Owner.Race == null) Debug.LogError("Owner " + Owner.FirstName + " Race Null");

            AddStatModifier(Owner.Race.stat_modifiers); // get statMods from race
            AddStatModifier(Owner.RaceTemplate.stat_modifiers); // get statMods from racetemplate
            foreach(var equipRef in Owner.EquippedItemRefs)    // addstatmod equipment
            {
                var equip = scr_System_CampaignManager.current.FindItemInstanceByID(equipRef);
                if (equip.GetComp_Equippable().statModifiers.Count > 0)
                {
                    AddStatModifier(equip.GetComp_Equippable().statModifiers);
                }
            }

            foreach(var i in Owner.Skills.Skills)
            {
                var mods = i.GetStatMods();
                if (mods == null || mods.Count < 1) continue;
                AddStatModifier(mods);
            }
            // addstatmod traits
        }

        foreach (var i in list_statsDerived) i.ClearCache();

        // force refresh Status
        foreach (var status in StatusInstances)
        {

        }

        // remove invalid statEX
        for (int i = StatsExtended.Count - 1; i >= 0; i--)
        {
            if (!Owner.hasStatKeyword(StatsExtended[i].Parent.StatKeyword))
            {
                StatsExtended.RemoveAt(i);
            }
        }

        // add missing statEX
        foreach (var statEX in scr_System_Serializer.current.index_StatsExtended.list)
        {
            if (Owner.hasStatKeyword(statEX.StatKeyword) && StatsExtended.Find(x => x.ID == statEX.ID) == null)
            {
                var newstat = statEX.Instantiate(Owner);
                StatsExtended.Add(newstat);
            }

        }

        // force refresh StatsEx value to keep it valid
        foreach (var statex in this.StatsExtended) statex.Restore(0f);

        foreach (var ex in this.statusInstancesEx) ex.ClearCache();
        needsList_cache = null;
    }


    [JsonIgnore] public bool isSleeping
    {
        get
        {
            return this.GetStatusSeverityByStringMatch("chara_status_sleeping") > 0;
        }
    }
    [JsonIgnore] public float Energy_InteractionCost { get { return -2f; } }
    [JsonIgnore] public float CumThreshold { get { return 100; } }
    [JsonIgnore] public int SleepHours { get { return Owner.hasStatKeyword("sleep") ? (int)GetStatValue("stats_derived_sleepNeed") : 0; } }
    [JsonIgnore] public float SleepDepth { get { return Owner.hasStatKeyword("sleep") ? (float)GetStatValue("stats_derived_sleepDepth") : 0; } }
    [JsonIgnore] public Stats_Derived_Extended_Instance HP { get { return GetStatEx("stats_derived_extended_hp"); } }
    [JsonIgnore] public Stats_Derived_Extended_Instance MP { get { return GetStatEx("stats_derived_extended_mp"); } }
    [JsonIgnore] public Stats_Derived_Extended_Instance Stamina { get { return GetStatEx("stats_derived_extended_stamina"); } }
    [JsonIgnore] public Stats_Derived_Extended_Instance Energy { get { return GetStatEx("stats_derived_extended_energy"); } }

    [JsonIgnore] public int MemoryLength { get { return Owner.hasStatKeyword("memory") ? (int) GetStatValue("stats_derived_memoryLength") : 0; } }
    [JsonIgnore] public int MemoryEntryCount { get { return Owner.hasStatKeyword("memory") ? (int) GetStatValue("stats_derived_memoryEntryCount") : 0; } }


    private Status_Instance climaxing = null;
    [JsonIgnore] public Status_Instance Climaxing { get { if (climaxing == null) climaxing = GetStatusByStringMatch("chara_status_climaxing"); return climaxing; } }

    private Status_Instance afterClimax = null;
    [JsonIgnore] public Status_Instance AfterClimax { get { if (afterClimax == null) afterClimax = GetStatusByStringMatch("chara_status_sexual_climax_after"); return afterClimax; } }

    private Status_Instance chara_status_sexual_B = null;
    [JsonIgnore] public Status_Instance Sex_B { get { if (chara_status_sexual_B == null) chara_status_sexual_B = GetStatusByStringMatch("chara_status_sex_B"); return chara_status_sexual_B; } }
    private Status_Instance chara_status_sexual_M = null;
    [JsonIgnore] public Status_Instance Sex_M { get { if (chara_status_sexual_M == null) chara_status_sexual_M = GetStatusByStringMatch("chara_status_sex_M"); return chara_status_sexual_M; } }
    private Status_Instance chara_status_sexual_C = null;
    [JsonIgnore] public Status_Instance Sex_C { get { if (chara_status_sexual_C == null) chara_status_sexual_C = GetStatusByStringMatch("chara_status_sex_C"); return chara_status_sexual_C; } }
    private Status_Instance chara_status_sexual_V = null;
    [JsonIgnore] public Status_Instance Sex_V { get { if (chara_status_sexual_V == null) chara_status_sexual_V = GetStatusByStringMatch("chara_status_sex_V"); return chara_status_sexual_V; } }
    private Status_Instance chara_status_sexual_A = null;
    [JsonIgnore] public Status_Instance Sex_A { get { if (chara_status_sexual_A == null) chara_status_sexual_A = GetStatusByStringMatch("chara_status_sex_A"); return chara_status_sexual_A; } }
    private Status_Instance chara_status_sexual_W = null;
    [JsonIgnore] public Status_Instance Sex_W { get { if (chara_status_sexual_W == null) chara_status_sexual_W = GetStatusByStringMatch("chara_status_sex_W"); return chara_status_sexual_W; } }

    protected StatusEx_Instance _lubrication = null;
    [JsonIgnore] public StatusEx_Instance Lubrication
    {
        get
        {
            if (_lubrication == null) _lubrication = statusInstancesEx.Find(x => x.ID == "chara_status_orificelubrication");
            return _lubrication;
        }
    }
    [JsonIgnore] public bool isLubricated
    {
        get
        {
            return this.Lubrication != null && this.Lubrication.Severity >= 50;
        }
    }
    public void RestoreAll()
    {
        foreach (var ex in StatsExtended) if (ex != null) ex.RestoreMax();
    }

    public void Restore(string baseTypeID, int amount)
    {
        Stats_Derived_Extended_Instance ex = GetStatEx(baseTypeID);
        if (ex != null) ex.Restore(amount);
    }

    protected List<Stat_Modifier> modifiers = new List<Stat_Modifier>();
    protected List<Stat_Modifier> modifiers_temporary = new List<Stat_Modifier>();

    public List<Stat_Modifier> GetModifiers(Stats_Derived_Base obj, string statID, List<string> contexts = null)
    {
        return GetModifiers(statID, contexts, true, false);
    }
    public List<Stat_Modifier> GetModifiers(Stats_Base obj, string statID, List<string> contexts = null)
    {
        return GetModifiers(statID, contexts, false, false);
    }

    public List<Stat_Modifier> GetModifiers(StatusEx_Instance obj, string statID, List<string> contexts = null)
    {
        return GetModifiers(statID, contexts, true, true);
    }

    private List<Stat_Modifier> GetModifiers(string statID, List<string> contexts = null, bool checkStatusInstance = true, bool checkMemory = true)
    {
        List<Stat_Modifier> list = new List<Stat_Modifier>();
        foreach (var mod in modifiers)
        {
            if (mod.statID != statID) continue;
            else
            {
                list.Add(mod);
            }
        }


        foreach (var mod in modifiers_temporary)
        {
            if (mod.statID != statID) continue;
            else
            {
                list.Add(mod);
            }
        }

        // and grab status modifiers
        if (!checkStatusInstance) return list;

        foreach (var status in StatusInstances)
        {
            foreach (var severityMod in status.SeverityModifiers)
            {
                if (severityMod.statID != statID) continue;
                else list.Add(severityMod);
            }
        }

        if (!checkMemory) return list;

        if (Owner.Memory != null)
        {
            var temp = Owner.Memory.GetRecentMemoryStatMods();
            foreach (var mod in temp)
            {
                if (mod.statID != statID) continue;
                else list.Add(mod);
            }
        }
        return list;
    }


    /// <summary>
    /// Dictionary<Tuple<statID, contexts>, value>
    /// </summary>
    protected Dictionary<Tuple<string, List<string>>, float> cached_values = null;

    /// <summary>
    /// On Every Stat add/removal, clear cache to force recalculation next time
    /// </summary>
    /// <param name="mods"></param>
    public void AddStatModifier(List<Stat_Modifier> mods)
    {
        foreach (Stat_Modifier mod_instance in mods)
        {
            for (int i = modifiers.Count - 1; i >= 0; i--)
            {
                if (modifiers[i].statID == mod_instance.statID && modifiers[i].modKey == mod_instance.modKey
                        && ((modifiers[i].type == Stat_Modifier.StatMod_Type.setMult && modifiers[i].type == mod_instance.type)
                            || (modifiers[i].type == Stat_Modifier.StatMod_Type.setBase && modifiers[i].type == mod_instance.type)))
                    modifiers.RemoveAt(i);
            }

            modifiers.Add(mod_instance);
        }
        cached_values.Clear();
    }

    /// <summary>
    /// if contexts is not null, then no cached valued will be read anyway<br/>
    /// accessKey is the stat object trying to get stat : basestat, statEX, etc<br/>
    /// accessKey is there to enforce hierarchical stat processing, preventing circular behavior
    /// Use this function for external stat fetch. <br/>
    /// Do not use this if getting stat for more stat calculation as this might lead to infinite loop, instead use the accessKey overload
    ///
    /// </summary>
    /// <param name="statID"></param>
    /// <param name="contexts"></param>
    /// <returns></returns>
    public float GetStatValue(string statID, List<string> contexts = null)
    {
        //if (contexts == null) contexts = new List<string>();   
        //if (cached_values == null) cached_values = new Dictionary<string, float>>();
        //if (!cached_values.ContainsKey(statID)) cached_values.Add(statID, 0f);
        if (contexts != null)
        {
            contexts = contexts.Distinct().ToList();
            contexts.Sort();
        }

        // Catch Stat Base
        if (statID == "Strength" || statID == "Constitution" || statID == "Psyche" || statID == "Willpower")
        {

            switch (statID)
            {
                case "Strength": return Strength.FinalValue(contexts);
                case "Constitution": return Constitution.FinalValue(contexts);
                case "Psyche": return Psyche.FinalValue(contexts);
                case "Willpower": return Willpower.FinalValue(contexts);
            }
            //Debug.Log("GetStatValue request for chara[" + Owner.FirstName + "] on statBase [" + statID + "] with result [" + value + "]");
        }

        // Catch Stat Derived
        Stats_Derived_Base statDerived = scr_System_Serializer.current.GetByNameOrID_StatsDerivedBase(statID);
        if (statDerived != null && Owner.hasStatKeyword(statDerived.StatKeyword))
        {
            var statD = list_statsDerived.Find(x => x.ID == statID);
            if (statD == null)
            {
                statD = statDerived.Instantiate(this);
                list_statsDerived.Add(statD);
            }
            return statD.FinalValue(contexts);
        }

        // Catch Stat Ex
        Stats_Derived_Extended_Instance statex = GetStatEx(statID);
        if (statex != null)
        {   // do not cache statEX value
            return statex.Value;
        }

        return 0f;
    }

    public Stats_Derived_Instance GetDerivedStat(string statID)
    {
        Stats_Derived_Base statDerived = scr_System_Serializer.current.GetByNameOrID_StatsDerivedBase(statID);
        if (statDerived != null && Owner.hasStatKeyword(statDerived.StatKeyword))
        {
            var statD = list_statsDerived.Find(x => x.ID == statID);
            if (statD == null)
            {
                statD = statDerived.Instantiate(this);
                list_statsDerived.Add(statD);
            }
            return statD;
        }
        return null;
    }


    [SerializeField][JsonProperty] private int pauseXMinAfterMod_Sex = 0;


    /// <summary>
    /// Called by character update
    /// </summary>
    /// <param name="t"></param>
    public void UpdateTimeMinute(TimeSpan t)
    {
        /*
        Strength.ClearCache();
        Constitution.ClearCache();
        Psyche.ClearCache();
        Willpower.ClearCache();

        foreach (var i in list_statsDerived) i.ClearCache();
        */

        bool refresh = false;

        for (int i = StatusInstances.Count - 1; i >= 0; i--)
        {
            int time = t.Minutes;

            if (StatusInstances[i].BaseRef.variationMode.variationType == Status_Base.Status_Variation_Type.sex && pauseXMinAfterMod_Sex > 0)
            {
                StatusInstances[i].pauseXMinAfterMod += pauseXMinAfterMod_Sex;
            }

            if (StatusInstances[i].pauseXMinAfterMod > 0)
            {
                time -= Math.Min(t.Minutes, StatusInstances[i].pauseXMinAfterMod);
                StatusInstances[i].pauseXMinAfterMod -= Math.Min(t.Minutes, StatusInstances[i].pauseXMinAfterMod);
            }


            if (StatusInstances[i].pauseXMinAfterMod == 0 && time > 0 && StatusInstances[i].BaseRef.variationMode.Decay != 0)
            {
                switch (StatusInstances[i].BaseRef.variationMode.variationType)
                {
                    case Status_Base.Status_Variation_Type.linear:
                        refresh = StatusInstances[i].SeverityAdd(StatusInstances[i].BaseRef.variationMode.Decay * time) || refresh;
                        break;
                    case Status_Base.Status_Variation_Type.sine:
                        
                        //Debug.Log("Sine variation not implemented for status.");
                        break;
                    case Status_Base.Status_Variation_Type.sex:
                        refresh = StatusInstances[i].SeverityAdd(StatusInstances[i].BaseRef.variationMode.Decay * time) || refresh;
                        break;
                    default:
                        break;

                }
            }

            var curr = StatusInstances[i];

            if (!StatusInstances[i].BaseRef.constant && StatusInstances[i].SeverityDisplayName == "")
            {   // on status disappear
                
            }

            if (!curr.BaseRef.constant && curr.pauseXMinAfterMod == 0 && curr.duration > 0)
            {   // status tick
                curr.duration = Math.Max(0, curr.duration - time);
                if (curr.duration == 0)
                {   // on status expire

                    Debug.Log($"status {curr.ID} on {Owner.FirstName} expired, removing");
                    StatusInstances.RemoveAt(i);
                    refresh = true;

                    // if sleep expire, fullrest()
                    if (curr.BaseRef.statusID == "chara_status_sleeping") Owner.FullRest();
                }
            }
        }
        pauseXMinAfterMod_Sex = Math.Max(pauseXMinAfterMod_Sex - t.Minutes, 0);
        if (!hasSexualStimulation) consecutiveClimaxCount = 0;

        //Debug.LogError("Setting CurrentlyCliaxed to false");
        //currentlyClimaxed =  1;


        // refresh character Status Mod
        if (refresh) UpdateStatus();

    }

    [JsonIgnore] protected bool hasSexualStimulation { get { return pauseXMinAfterMod_Sex > 0 || SexStimulation.Severity != 0 ; } }

    [SerializeField][JsonProperty] protected int consecutiveClimaxCount = 0;
    [JsonIgnore] public int ConsecutiveClimaxCount { get { return consecutiveClimaxCount; } }
    [SerializeField][JsonProperty] bool currentlyClimaxed = false;
    [JsonIgnore] public bool JustClimaxed { get { return Climaxing.Severity >= 1; } }

    private StatusEx_Instance _sexStimulation = null;
    [JsonIgnore] public StatusEx_Instance SexStimulation
    {
        get
        {
            if (_sexStimulation == null) _sexStimulation = statusInstancesEx.Find(x => x.ID == "chara_status_sex_stimulation");
            return _sexStimulation;
        }
    }

    private Status_Instance fatigue = null;
    [JsonIgnore] public Status_Instance Fatigue
    {
        get
        {
            if (fatigue == null) fatigue = StatusInstances.Find(x => x.ID == "chara_status_fatigue");
            return fatigue;
        }
    }




    [JsonIgnore] public StatusEx_Instance Consciousness
    {
        get
        {
            if (consciousness == null) consciousness = statusInstancesEx.Find(x => x.ID == "chara_status_consciousness");
            return consciousness;
        }
    }

    [JsonIgnore] public bool isConsciousnessUnconscious { get { return Consciousness.Tags.Contains("consciousness_unconscious"); } }
    [JsonIgnore] public bool isConsciousnessReduced { get { return Consciousness.Tags.Contains("consciousness_reduced") || Consciousness.Tags.Contains("consciousness_unconscious"); } }

    private StatusEx_Instance consciousness = null;

    protected int ownerRefID = -1;



    //public List<Stats_Derived_InstanceBase> statsDerived;




    [SerializeField][JsonProperty] protected List<Status_Instance> _statusInstances = new List<Status_Instance>();
    [SerializeField][JsonProperty] protected List<StatusEx_Instance> _statusInstancesEx = new List<StatusEx_Instance>();
    [JsonIgnore] public List<StatusEx_Instance> statusInstancesEx_Displayable
    {
        get
        {
            List<StatusEx_Instance> list = new List<StatusEx_Instance>();
            foreach(StatusEx_Instance i in statusInstancesEx)
            {
                if (i.BaseRef.noDisplay) continue;
                if (i.SeverityDisplayName == "") continue;
                list.Add(i);
            }
            return list;
        }
    }
    [JsonIgnore] public List<StatusEx_Instance> statusInstancesEx
    {
        get
        {
            return _statusInstancesEx;
        }
    }

    [JsonIgnore] public List<Status_Instance> StatusInstances { get { return _statusInstances; } }
    [JsonIgnore] public List<Status_Instance> StatusInstances_Displayable
    {
        get
        {
            List<Status_Instance> list = new List<Status_Instance>();
            foreach (Status_Instance i in StatusInstances)
            {
                if (i.BaseRef.noDisplay) continue;
                if (i.SeverityDisplayName == "") continue;
                list.Add(i);
            }
            return list;
        }
    }

    public bool HasStatusByStringMatch(string s)
    {
        return (this.StatusInstances.Find(x => x.ID.Contains(s)) != null);
    }

    public Status_Instance GetStatusByStringMatch(string s)
    {
        Status_Instance si = this.StatusInstances.Find(x => x.ID.Contains(s));
        if (si != null) return si;
        else return null;
    }

    public float GetStatusSeverityByStringMatch(string s)
    {
        Status_Instance si = this.StatusInstances.Find(x => x.ID.Contains(s));
        if (si != null) return si.Severity;
        else return -1;
    }

    public void RemoveStatusByStringMatch(string s)
    {
        List<Status_Instance> removelist = this.StatusInstances.FindAll(x => x.ID.Contains(s));

        foreach (Status_Instance status in removelist) StatusInstances.Remove(status);

    }

    public void AddOrModStatus(string s, float modSeverity = 0f, int modDuration = -1, float severityCap = -1f)
    {
        if (s == null || s == "" || s.Length < 1) return;
        Status_Instance instance = this.StatusInstances.Find(x => x.ID == s);
        if (instance != null && instance.BaseRef.constant && modSeverity == 0f && modDuration == -1) return;

        if (instance != null)
        {

            if (instance.BaseRef.variationMode.variationType == Status_Base.Status_Variation_Type.sex)
            {
                if (AfterClimax != null && AfterClimax.Severity < 0)
                {
                    //Debug.LogError("MATH MIN ["+Math.Abs(afterClimax.Severity).ToString()+"] ["+ modSeverity.ToString() + "]");
                    AfterClimax.SeverityAdd(Math.Min(Math.Abs(AfterClimax.Severity), modSeverity));
                    pauseXMinAfterMod_Sex = Math.Max(pauseXMinAfterMod_Sex, 1);

                    //if (modSeverity> 0) AddOrModStatus(s, Math.Min(Math.Abs(afterClimax.Severity)) modSeverity - difference, modDuration);
                }
            }

            if (s == "chara_status_sexual_climax_after")
            {
                consecutiveClimaxCount += 1;
                //Debug.LogError("SETTING CURRENTLYCLIMAXED TO TRUE");
                //if (currentlyClimaxed == 0) currentlyClimaxed = 2;
                //else currentlyClimaxed += 1;
                if (Climaxing.Severity < 1) Climaxing.SeverityAdd(2);
                else Climaxing.SeverityAdd(1);
            }

            //Debug.LogError("Stimulating status " + s + " with severityCap at " + severityCap);
            if ((!(severityCap > -1f)) || ((instance.Severity + modSeverity) < severityCap)) instance.SeverityAdd(modSeverity);
            else if (severityCap > -1f && instance.Severity < severityCap)
            {
                instance.FlagMaxed();
                instance.SeverityAdd(severityCap - instance.Severity);
            }
            else if (severityCap > -1f && instance.Severity >= severityCap) instance.FlagMaxed();

                //instance.SeverityAdd(Math.Min(modSeverity, severityCap - instance.Severity));



            if (instance.duration != -1) instance.duration += modDuration;
            instance.pauseXMinAfterMod = instance.BaseRef.variationMode.pauseXMinAfterMod;
            if (instance.BaseRef.variationMode.variationType == Status_Base.Status_Variation_Type.sex)
            {
                pauseXMinAfterMod_Sex = Math.Max(pauseXMinAfterMod_Sex, instance.BaseRef.variationMode.pauseXMinAfterMod);
                //foreach (var inst in instances) if (inst.BaseRef.variationMode.variationType == Status_Base.Status_Variation_Type.sex) inst.pauseXMinAfterMod = inst.BaseRef.variationMode.pauseXMinAfterMod;
            }
        }
        else
        {
            AddStatus(s, modSeverity, modDuration);
        }
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        this.modifiers_temporary.Clear();
        foreach (var i in statusInstancesEx) i.ClearCache();
    }

    protected void AddStatus(string s, float initialSeverity = 0f, int durationMinute = -1)
    {
        Status_Base target = scr_System_Serializer.current.GetByNameOrID_Status_Base(s);
        if (target != null)
        {
            Status_Instance si = target.Instantiate(ownerRefID, initialSeverity, durationMinute);
            this.StatusInstances.Add(si);

            if (si.BaseRef.variationMode.variationType == Status_Base.Status_Variation_Type.sex) foreach (var inst in StatusInstances) if (inst.BaseRef.variationMode.variationType == Status_Base.Status_Variation_Type.sex) inst.pauseXMinAfterMod = inst.BaseRef.variationMode.pauseXMinAfterMod;
        }
        else Debug.LogError("AddStatus Failed cuz target status ["+s+"] unfound");
    }
    [JsonIgnore] public StatusEx_Instance Lust
    {
        get
        {
            if (!Owner.hasStatKeyword("lust")) return null;
            if (lust == null ) lust = statusInstancesEx.Find(x => x.ID == "chara_status_lust");
            return lust;
        }
    }
    [JsonIgnore] private StatusEx_Instance lust = null;

    [JsonIgnore] public StatusEx_Instance Stress
    {
        get
        {
            if (!Owner.hasStatKeyword("stress")) return null;
            if (stress == null) stress = statusInstancesEx.Find(x => x.ID == "chara_status_stress");
            return stress;
        }
    }
    [JsonIgnore] private StatusEx_Instance stress = null;

    [JsonIgnore] public StatusEx_Instance Mood
    {
        get
        {
            if (!Owner.hasStatKeyword("mood")) return null;
            if (mood == null ) mood = statusInstancesEx.Find(x => x.ID == "chara_status_mood");
            return mood;
        }
    }
    [JsonIgnore] private StatusEx_Instance mood = null;

    [System.Serializable]
    public class ModStorage
    {
        public float baseValue = 0.0f;
        public float baseMult = 1.0f;
        public float addValue = 0.0f;
        public float addMult = 0.0f;
        public ModStorage(float baseMult, float baseValue = 0.0f)
        {
            this.baseValue = baseValue;
            this.baseMult = baseMult;
            addMult = 0.0f;
            addValue = 0.0f;
        }
    }
}

