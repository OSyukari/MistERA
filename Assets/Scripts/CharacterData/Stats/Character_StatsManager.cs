using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public interface I_StatsManager
{
    public List<Stat_Modifier> GetModifiers(Stats_Derived_Base obj, string statID, List<string> contexts = null, bool forbidStatus = false);
    public List<Stat_Modifier> GetModifiers(Stats_Base obj, string statID, List<string> contexts = null);
    public List<Stat_Modifier> GetModifiers(StatusEx_Instance obj, string statID, List<string> contexts = null);

    /// <summary>
    /// Owner should not be publicly accessible <br/>
    /// being public means child could call to owner.stats and this will not fly when using a combatstatsmanager copy
    /// </summary>
    /// <returns></returns>
    public string OwnerName();
    public bool hasStatKeyword(string statKeyword);
    public float GetStatValue(string statID, List<string> contexts = null);
    public Stats_Base Strength { get; }
    public Stats_Base Constitution { get; }
    public Stats_Base Psyche { get; }
    public Stats_Base Willpower { get; }
    public Status_Instance GetStatusByStringMatch(string s);
    public StatusEx_Instance GetStatusEXByStringMatch(string s);
    public Stats_Derived_Instance GetDerivedStat(string statID);
    public List<Status_Instance> FindStatusByID(string statID);
    public void SetStatusSeverity(string statusID, float severity);

    [JsonIgnore] public Stats_Derived_Extended_Instance HP { get; }
    [JsonIgnore] public Stats_Derived_Extended_Instance MP { get; }
    [JsonIgnore] public Stats_Derived_Extended_Instance Stamina { get; }
    [JsonIgnore] public Stats_Derived_Extended_Instance Energy { get ; }

    public Character_Trainable Owner { get; }
    public void RestoreAll();

}

public class StatsManager : I_StatsManager
{
    public void AddTrait(Traits s) {
        if (s == null) return;

        if (s.Parent != null && (s.Parent.SortType == Trait_Group_Type.SortedList || s.Parent.SortType == Trait_Group_Type.UnsortedList))
        {
            foreach(var trait in s.Parent.entries)
            {
                if (trait == s)
                {
                    continue;
                }
                else
                {
                    RemoveTrait(trait);
                }
            }
        }

        if (!Traits.Contains(s)) Traits.Add(s);
        if (!traits.Contains(s.ID)) traits.Add(s.ID);
    }
    public void RemoveTrait(Traits s)
    {
        if (s == null) return;
        Traits.Remove(s);
        traits.Remove(s.ID);
    }

    bool traits_init = false;

    public void RefreshTraits()
    {
        Traits.Clear();
        if (Owner != null && Owner.Template != null)
        {
            foreach (var trait in Owner.Template.traits)
            {
                var tr = scr_System_Serializer.current.MasterList.Traits_Groups.GetTraitByID(trait);
                if (tr != null)
                {
                    AddTrait(tr);
                }
            }

            if (Owner.Template.Sensitivity_A != null) AddTrait(Owner.Template.Sensitivity_A);
            if (Owner.Template.Sensitivity_B != null) AddTrait(Owner.Template.Sensitivity_B);
            if (Owner.Template.Sensitivity_C != null) AddTrait(Owner.Template.Sensitivity_C);
            if (Owner.Template.Sensitivity_M != null) AddTrait(Owner.Template.Sensitivity_M);
            if (Owner.Template.Sensitivity_V != null) AddTrait(Owner.Template.Sensitivity_V);

        }
        var traitsCache = new List<string>(this.traits);

        foreach(var trait in traitsCache)
        {
            var tr = scr_System_Serializer.current.MasterList.Traits_Groups.GetTraitByID(trait);
            if (tr != null)
            {
                AddTrait(tr);
            }
        }
        PopulateTraits();
    }

    public void ResetTrait() {

        traits.Clear();
        Traits.Clear();
    }



    protected void PopulateTraits()
    {
        foreach(var tgroup in scr_System_Serializer.current.MasterList.Traits_Groups.traits_All)
        {
            foreach(var tr in tgroup)
            {
                if (tr.SortType != Trait_Group_Type.SortedList && tr.SortType != Trait_Group_Type.UnsortedList) continue;
                //if (tr.Type == Trait_Type.Body) continue;
                if (tr.tags.Contains("do_not_use")) continue;
                if (!tr.allowPopulate) continue;
                var neutral = tr.getNeutralinGroup();
                if (neutral == null) continue;
                foreach(var trr in tr.entries)
                {
                    if (HasTrait(trr))
                    {
                        neutral = null;
                        break;
                    }
                }
                if (neutral == null) continue;
                AddTrait(neutral);
            }
        }
    }

    public bool HasTrait(Traits t) { return traits.Contains(t.ID); }
    /// <summary>
    /// Return all status with id match
    /// </summary>
    /// <param name="statID"></param>
    /// <returns></returns>
    public List<Status_Instance> FindStatusByID(string statID)
    {
        var l = new List<Status_Instance>();
        foreach (var i in _statusInstances) if (i.Key.Contains(statID)) l.Add(i.Value);
        return l;
    }
    public Status_Instance FindStatusByExactID(string statID)
    {
        if (_statusInstances.TryGetValue(statID, out var value)) return value;
        else return null;
    }
    public void SetStatusSeverity(string statusID, float severity)
    {
        var instance = FindStatusByExactID(statusID);
        if (instance != null)
        {
            if (Math.Abs(severity - instance.Severity) < float.Epsilon) return;
            instance.SeveritySet(severity);
        }
        else
        {
            AddStatus(statusID, severity);
        }
    }

    public StatusEx_Instance FindStatusEXByExactID(string statID)
    {
        if (_statusInstancesEx.TryGetValue(statID, out var value)) return value;
        else return null;
    }
    public CombatStatManager MakeCombatHandler()
    {
        var handler = new CombatStatManager(Owner, this);
        return handler;
    }

    List<string> _cachedStatKeywords = null;
    public bool hasStatKeyword(string statKeyword)
    {
        return Owner.hasStatKeyword(statKeyword);
    }

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

    [JsonProperty] protected List<Stats_Derived_Extended_Instance> list_statsExtended = new List<Stats_Derived_Extended_Instance>();
    [JsonIgnore] public List<Stats_Derived_Extended_Instance> StatsExtended { get { return list_statsExtended; } }

    /// <summary>
    /// Cache the <string statID, object statEX> to save the search for the current session
    /// </summary>
    [JsonIgnore] public Dictionary<string, Stats_Derived_Instance> list_statsDerived { get
        {
            if (_list_statDerived == null)
            {
                _list_statDerived = new Dictionary<string, Stats_Derived_Instance>();
                foreach(var i in scr_System_Serializer.current.index_StatsDerived.list)
                {
                    if (Owner.hasStatKeyword(i.StatKeyword)) _list_statDerived.Add(i.ID, i.Instantiate(this));
                }
            }
            return _list_statDerived;
        } }

    Dictionary<string, Stats_Derived_Instance> _list_statDerived = null;

    Dictionary<string, Stats_Derived_Extended_Instance> _statEX = new Dictionary<string, Stats_Derived_Extended_Instance>();
    public Stats_Derived_Extended_Instance GetStatEx(string statID)
    {
        if (!_statEX.ContainsKey(statID)) _statEX[statID] = StatsExtended.Find(x => x.ID == statID);
        return _statEX[statID];
    }

    public void ReEstablishParent(Character_Trainable chara)
    {
        this.owner = chara;

        this.Strength.ReEstablishParent(this);
        this.Constitution.ReEstablishParent(this);
        this.Psyche.ReEstablishParent(this);
        this.Willpower.ReEstablishParent(this);

        if (this._statusInstances != null) foreach (var i in _statusInstances) i.Value.ReEstablishParent(chara.Stats);
        foreach (var i in list_statsExtended) i.ReEstablishParent(chara.Stats);
        if (this._statusInstancesEx != null) foreach (var i in _statusInstancesEx) i.Value.ReEstablishParent(chara.Stats);

        this.RefreshTraits();
        this.RefreshAllStats(true);
    }
    public StatsManager()
    {

    }

    public StatsManager(Character_Trainable chara)
    {
        ReEstablishParent(chara);
        
        //RefreshAllStats();
        if (chara.RefID > -1) InitializeWithID(chara);
    }
    public void InitializeWithID(Character_Trainable ownerRef, int str = 10, int con = 10, int psy = 10, int wil = 10)
    {
        this.owner = ownerRef;;
        //generate all stat_derived_extended

        this.modifiers.Clear();
        this.modifiers_temporary.Clear();

        foreach (var statusEX in scr_System_Serializer.current.index_StatusEX.list)
        {
            //Debug.Log("adding statusEx " + statusEX.statusID + " to chara " + Owner.FullName+" on list isNull? "+(statusInstancesEx == null));
            var ss = statusEX.Instantiate(this);
            _statusInstancesEx.Add(ss.ID, ss);
        }
        ClearStatusEXCache();

        foreach(var status in scr_System_Serializer.current.index_Status.list)
        {
            if (status.constant) AddStatus(status.statusID);
        }

        if (Strength == null) Debug.LogError("Strength null");
        Strength.SetValue(str);
        Constitution.SetValue(con);
        Psyche.SetValue(psy);
        Willpower.SetValue(wil);

        // add traits
        RefreshTraits();


        RefreshAllStats(true);
    }

    [JsonProperty] protected List<string> traits = new List<string>();
    [JsonIgnore] public List<Traits> Traits = new List<Traits>();

    Dictionary<string, Traits> _queryCache = new Dictionary<string, Traits>();

    public Traits GetTraitByGroupID(string id)
    {
        if (_queryCache.TryGetValue(id, out var trait)) return trait;

        foreach(var t in Traits)
        {
            if (t.Parent.ID == id)
            {
                _queryCache.Add(id, t);
                return t;
            }
        }
        return null;
    }


    [JsonProperty] protected Stats_Base baseStat_STR = null, baseStat_CON = null, baseStat_PSY = null, baseStat_WIL = null;
    [JsonIgnore] public Stats_Base Strength { get {
            if (baseStat_STR == null)
            {
                baseStat_STR = new Stats_Base(this, "Strength");
                if (this.Owner.Template != null) this.baseStat_STR.SetValue(this.Owner.Template.stat_STR);
            }
            return baseStat_STR; } }
    [JsonIgnore] public Stats_Base Constitution { get {
            if (baseStat_CON == null)
            {
                baseStat_CON = new Stats_Base(this, "Constitution");
                if (this.Owner.Template != null) this.baseStat_CON.SetValue(this.Owner.Template.stat_CON);
            }
            return baseStat_CON; } }
    [JsonIgnore] public Stats_Base Psyche { get {
            if (baseStat_PSY == null)
            {
                baseStat_PSY = new Stats_Base(this, "Psyche");
                if (this.Owner.Template != null) this.baseStat_PSY.SetValue(this.Owner.Template.stat_PSY);
            }
                return baseStat_PSY; } }
    [JsonIgnore] public Stats_Base Willpower { get {
            if (baseStat_WIL == null)
            {
                baseStat_WIL = new Stats_Base(this, "Willpower");
                if (this.Owner.Template != null) this.baseStat_WIL.SetValue(this.Owner.Template.stat_WIL);
            }
                return baseStat_WIL; } }


    /// <summary>
    /// Only reset statusEX
    /// </summary>
    public void RefreshAttitude()
    {
        if (this.Mood != null) this.Mood.ClearCache();
        if (this.Stress != null) this.Stress.ClearCache();
    }

    List<string> _permanentTags = new List<string>();
    bool _permanentTags_cached = false;
    [JsonIgnore]
    public List<string> PermanentTags { get
        {
            if (!_permanentTags_cached)
            {
                _permanentTags.Clear();
                _permanentTags_cached = true;
                foreach(var i in this.Owner.Body.Body)
                {
                    // add item tag
                    foreach(var j in i.EquippedItems)
                    {
                        _permanentTags.AddRange(j.Tags);
                    }
                    _permanentTags = Utility.Distinct(_permanentTags);
                }
                // add owner race origin template tags
                //
            }
            return _permanentTags;
        } }

    /// <summary>
    /// Lazy Refresh. Only update whats strictly necessary.
    /// </summary>
    public void RefreshAllStats(bool resetPermanentStatCache = false)
    {   // run after any stat change, notify and call refresh


        // clear cache


        if (this.Owner == null) return;

        if (resetPermanentStatCache)
        {
            // this also force clear the modifiers and recollect all of them
            modifiers.Clear();
            _permanentTags_cached = false;
            //if (Owner == null) Debug.LogError("Owner Null");
            if (Owner.Race == null) Debug.LogError("Owner " + Owner.FirstName + " Race Null");


            AddStatModifier(Owner.Origin.stat_modifiers);
            AddStatModifier(Owner.Race.stat_modifiers); // get statMods from race
            if (Owner.RaceTemplate != null) AddStatModifier(Owner.RaceTemplate.stat_modifiers); // get statMods from racetemplate

            /*
            if (Owner.Template != null && Owner.Template.Sensitivity_A != null) AddStatModifier(Owner.Template.Sensitivity_A.stat_modifiers);
            if (Owner.Template != null && Owner.Template.Sensitivity_B != null) AddStatModifier(Owner.Template.Sensitivity_B.stat_modifiers);
            if (Owner.Template != null && Owner.Template.Sensitivity_C != null) AddStatModifier(Owner.Template.Sensitivity_C.stat_modifiers);
            if (Owner.Template != null && Owner.Template.Sensitivity_M != null) AddStatModifier(Owner.Template.Sensitivity_M.stat_modifiers);
            if (Owner.Template != null && Owner.Template.Sensitivity_V != null) AddStatModifier(Owner.Template.Sensitivity_V.stat_modifiers);
            */

            foreach (var trait in this.Traits)
            {
                if (trait.stat_modifiers.Count > 0)
                {
                    AddStatModifier(trait.stat_modifiers);
                }
            }

            foreach (var equipRef in Owner.EquippedItemRefs)    // addstatmod equipment
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

            // add missing statEX -> HP MP ST EN
            foreach (var statEX in scr_System_Serializer.current.index_StatsExtended.list)
            {
                if (Owner.hasStatKeyword(statEX.StatKeyword) && StatsExtended.Find(x => x.ID == statEX.ID) == null)
                {
                    var newstat = statEX.Instantiate(this);
                    StatsExtended.Add(newstat);
                    _statEX[newstat.ID] = newstat;// Clear();
                }
            }

            // remove invalid statEX
            for (int i = StatsExtended.Count - 1; i >= 0; i--)
            {
                if (!Owner.hasStatKeyword(StatsExtended[i].Parent.StatKeyword))
                {
                    _statEX.Remove(StatsExtended[i].ID);
                    StatsExtended.RemoveAt(i);
                }
            }
        }

        foreach (var i in list_statsDerived) i.Value.ClearCache();

        // force refresh StatsEx value to keep it valid
        foreach (var ex in StatsExtended) ex.ModValue(0f);



        foreach (var ex in this._statusInstancesEx) ex.Value.ClearCache();
        needsList_cache = null;
    }


    [JsonIgnore] public bool isSleeping
    {
        get
        {
            return this.GetStatusByStringMatch("chara_status_sleeping") != null;
        }
    }
    [JsonIgnore] public float Energy_InteractionCost { get { return -2f; } }
    [JsonIgnore] public float CumThreshold { get { return 100; } }
    [JsonIgnore] public int SleepHours { get { return Owner.hasStatKeyword("sleep") ? (int)GetStatValue("stats_derived_sleepNeed") : 0; } }
    [JsonIgnore] public float SleepDepth { get { return Owner.hasStatKeyword("sleep") ? (float)GetStatValue("stats_derived_sleepDepth") : 0; } }

    Stats_Derived_Extended_Instance _hp = null;
    [JsonIgnore] public Stats_Derived_Extended_Instance HP { get {
            if (_hp == null) _hp = GetStatEx("stats_derived_extended_hp");
            return _hp;
        }
    }
    Stats_Derived_Extended_Instance _mp = null;
    [JsonIgnore] public Stats_Derived_Extended_Instance MP { get { 
            if (_mp == null) _mp =  GetStatEx("stats_derived_extended_mp");
            return _mp;
        }
    }
    Stats_Derived_Extended_Instance _st = null;
    [JsonIgnore] public Stats_Derived_Extended_Instance Stamina { get {
            if (_st == null) _st = GetStatEx("stats_derived_extended_stamina");
            return _st;
        } }
    Stats_Derived_Extended_Instance _en = null;
    [JsonIgnore] public Stats_Derived_Extended_Instance Energy { get {
            if (_en == null) _en = GetStatEx("stats_derived_extended_energy");
            return _en;
        } }

    [JsonIgnore] public int MemoryLength { get { return Owner.hasStatKeyword("memory") ? (int) GetStatValue("stats_derived_memoryLength") : 0; } }
    [JsonIgnore] public int MemoryEntryCount { get { return Owner.hasStatKeyword("memory") ? (int) GetStatValue("stats_derived_memoryEntryCount") : 0; } }

    /// <summary>
    /// Return first match
    /// </summary>
    /// <param name="s"></param>
    /// <returns></returns>
    public StatusEx_Instance GetStatusEXByStringMatch(string s)
    {
        foreach(var k in this._statusInstancesEx)
        {
            if (k.Key == s || k.Key.Contains(s)) return k.Value;
        }
        return null;
    }


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
            if (_lubrication == null) _lubrication = GetStatusEXByStringMatch("chara_status_sex_stimulation_pos");
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
        if (ex != null) ex.ModValue(amount);
    }

    protected List<Stat_Modifier> modifiers = new List<Stat_Modifier>();
    public List<Stat_Modifier> Modifiers { get { return this.modifiers; } }
    protected List<Stat_Modifier> modifiers_temporary = new List<Stat_Modifier>();
    public List<Stat_Modifier> Modifiers_Temporary { get { return this.modifiers_temporary; } }

    public List<Stat_Modifier> GetModifiers(Stats_Derived_Base obj, string statID, List<string> contexts = null, bool forbidStatus = false)
    {
        return GetModifiers(statID, contexts, !forbidStatus, false);
    }
    public List<Stat_Modifier> GetModifiers(Stats_Base obj, string statID, List<string> contexts = null)
    {
        return GetModifiers(statID, contexts, false, false);
    }

    public List<Stat_Modifier> GetModifiers(StatusEx_Instance obj, string statID, List<string> contexts = null)
    {
        return GetModifiers(statID, contexts, true, true, obj.BaseRef.constant && obj.BaseRef.capModded && obj.BaseRef.noDisplay);
    }

    protected List<Stat_Modifier> GetModifiers(string statID, List<string> contexts = null, bool checkStatusInstance = true, bool checkMemory = true, bool checkRelationships = false)
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

        if (checkMemory)
        {
            if (Owner.Memory != null)
            {
                var temp = Owner.Memory.GetRecentMemoryStatMods();
                foreach (var mod in temp)
                {
                    if (mod.statID != statID) continue;
                    else list.Add(mod);
                }
            }
        }


        /*
        if (checkRelationships)
        {
            foreach (var c in scr_System_CampaignManager.current.GetCharaRoomInstance(Owner.RefID).RoomChara)
            {
                if (c == Owner) continue;
                var temp = Owner.Relationships.GetRelationshipStatMod(c);
                Debug.Log($"checkRelationships for {Owner.CallName} has found mod count {temp.Count}, filtering for {statID}");
                foreach (var mod in temp)
                {
                    if (mod.statID != statID) continue;
                    else list.Add(mod);
                }
            }
        }*/

        return list;
    }


    /// <summary>
    /// Dictionary<Tuple<statID, contexts>, value>
    /// </summary>


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
                if (modifiers[i].statID == mod_instance.statID && modifiers[i].ModKey == mod_instance.ModKey
                        && ((modifiers[i].type == Stat_Modifier.StatMod_Type.setMult && modifiers[i].type == mod_instance.type)
                            || (modifiers[i].type == Stat_Modifier.StatMod_Type.setBase && modifiers[i].type == mod_instance.type)))
                    modifiers.RemoveAt(i);
            }

            modifiers.Add(mod_instance);
        }
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
        if (contexts != null)
        {
            Utility.DistinctInPlace(contexts);
        }

        // Catch Stat Base
        if      (statID == Strength.ID) return Strength.FinalValue(contexts);
        else if (statID == Constitution.ID) return Constitution.FinalValue(contexts);
        else if (statID == Psyche.ID) return Psyche.FinalValue(contexts);
        else if (statID == Willpower.ID) return Willpower.FinalValue(contexts);


        // Catch Stat Derived
        Stats_Derived_Base statDerived = scr_System_Serializer.current.GetByNameOrID_StatsDerivedBase(statID);
        if (statDerived != null && Owner.hasStatKeyword(statDerived.StatKeyword))
        {
            if (!list_statsDerived.TryGetValue(statID, out var statD))
            {
                statD = statDerived.Instantiate(this);
                list_statsDerived.Add(statID, statD);
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

    /// <summary>
    /// Only statEX can be modded, other will return false
    /// </summary>
    /// <param name="statID"></param>
    /// <param name="value"></param>
    /// <returns></returns>
    public bool ModStatValue(string statID, float value)
    {
        // Catch Stat Base
        if (statID == Strength.ID) return false;
        else if (statID == Constitution.ID) return false;
        else if (statID == Psyche.ID) return false;
        else if (statID == Willpower.ID) return false;


        // Catch Stat Derived
        Stats_Derived_Base statDerived = scr_System_Serializer.current.GetByNameOrID_StatsDerivedBase(statID);
        if (statDerived != null) return false;

        // Catch Stat Ex
        Stats_Derived_Extended_Instance statex = GetStatEx(statID);
        if (statex != null)
        {
            statex.ModValue(value);
            return true;
        }

        return false;
    }

    public bool HasStat(string statID)
    {
        if (statID == Strength.ID || statID == Constitution.ID || statID == Psyche.ID || statID == Willpower.ID) return true;

        // Catch Stat Derived
        Stats_Derived_Base statDerived = scr_System_Serializer.current.GetByNameOrID_StatsDerivedBase(statID);
        if (statDerived != null && Owner.hasStatKeyword(statDerived.StatKeyword)) return true;

        // Catch Stat Ex
        Stats_Derived_Extended_Instance statex = GetStatEx(statID);
        if (statex != null) return true;

        return false;
    }

    public Stats_Derived_Instance GetDerivedStat(string statID)
    {
        Stats_Derived_Base statDerived = scr_System_Serializer.current.GetByNameOrID_StatsDerivedBase(statID);
        if (statDerived != null && Owner.hasStatKeyword(statDerived.StatKeyword))
        {
            if (!list_statsDerived.TryGetValue(statID, out var statD))
            {
                statD = statDerived.Instantiate(this);
                list_statsDerived.Add(statID, statD);
            }
            return statD;
        }
        return null;
    }


    [JsonProperty] private int pauseXMinAfterMod = 0;

    [JsonIgnore] public int pauseLLMTicks = 0;

    public void PreUpdateTimeTick()
    {
        bool timestopped = Owner.isTimeStopped;
        if (timestopped) return;
        for (int i = StatusInstances.Count - 1; i >= 0; i--)
        {
            if (!StatusInstances[i].BaseRef.constant) StatusInstances[i].elapsedTime += 1;
        }
    }

    /// <summary>
    /// Called by character update
    /// </summary>
    /// <param name="t"></param>
    public void UpdateTimeMinute(TimeSpan t, TimeSpan t_real)
    {
        /*
        Strength.ClearCache();
        Constitution.ClearCache();
        Psyche.ClearCache();
        Willpower.ClearCache();

        foreach (var i in list_statsDerived) i.ClearCache();
        */


        bool refresh = false;

        var keys = _statusInstances.Keys.ToArray();
        foreach (var key in keys)
        {
            int time = Owner.isTimeStopped ? t.Minutes : t_real.Minutes;
            var curr = _statusInstances[key];

            // Sex-type statuses have their decay and duration paused while either timer is active:
            // - curr.pauseXMinAfterMod: per-instance, refreshed when THIS status is stimulated
            // - pauseLLMTicks: manager-level, set for the duration of an LLM sex interaction
            bool sexPaused = curr.BaseRef.variationMode.pauseXMinAfterMod > 0 && (curr.pauseXMinAfterMod > 0 || pauseLLMTicks > 0);
            if (curr.pauseXMinAfterMod > 0) curr.pauseXMinAfterMod = Math.Max(0, curr.pauseXMinAfterMod - time);

            if (!sexPaused && time > 0 && curr.Decay != 0)
            {
                var decay = curr.BaseRef.variationMode.baselineVariation.Decay(this, curr.Severity);
                if (decay != 0)
                {
                    var final = decay * time;
                    if (curr.SeverityAdd(final)) refresh = true;
                }
            }

            if (!curr.BaseRef.constant)
            {   // on status disappear
                // special handling
                if (curr.CanBeRemovedBySeverity && curr.duration > 0)
                {
                    if (curr.BaseRef.allowNaturalRemoval)
                    {
                        refresh = curr.SeverityMods.Count > 0;
                        Debug.LogError($"status {curr.ID} severity at 0 while duration still at {curr.duration}");
                        RemoveStatusByStringMatch(key);
                    }
                    else if (curr.BaseRef.statusID == "chara_status_sleeping")
                    {
                        refresh = true;
                        // remove sleep special
                        Owner.Stats.AddOrModStatus("chara_status_sleep_deprived", curr.duration);
                        Debug.Log($"status {curr.ID} severity at 0 while duration still at {curr.duration}, replacing with chara_status_sleep_deprived");
                        Owner.WakeUp(false);
                    }
                    else
                    {
                        // throw error
                        Debug.LogError($"status {curr.ID} does not allow natural removal but lacking removal handling");
                    }
                }

                // expired with time, allow removal with no calls
                else if (!sexPaused && curr.duration >= 0)
                {   // status tick
                    curr.duration = Math.Max(0, curr.duration - time);
                    if (curr.duration == 0)
                    {   // on status expire

                        //Debug.Log($"status {curr.ID} on {Owner.FirstName} expired, removing");

                        if (curr.BaseRef.allowNaturalRemoval)
                        {
                            refresh = curr.SeverityMods.Count > 0;
                            RemoveStatusByStringMatch(key);
                        }
                        else if (curr.BaseRef.statusID == "chara_status_sleeping")
                        {
                            Owner.FullRest();
                            Owner.WakeUp(false);
                            refresh = true;
                        }
                        else
                        {
                            Debug.LogError($"status {curr.ID} does not allow natural removal but lacking removal handling");
                        }
                    }
                    else if (curr.hasRandomVariation && time > 0)
                    {
                        refresh = true;
                    }
                }
            }
        }
        if (pauseLLMTicks > 0) pauseLLMTicks = Math.Max(pauseLLMTicks - t.Minutes, 0);
        //else pauseXMinAfterMod = Math.Max(pauseXMinAfterMod - t.Minutes, 0);
        if (!hasSexualStimulation) consecutiveClimaxCount = 0;

        //Debug.LogError("Setting CurrentlyCliaxed to false");
        //currentlyClimaxed =  1;


        // refresh character Status Mod
        if (refresh) UpdateStatus();

    }

    [JsonIgnore] protected bool hasSexualStimulation { get { return pauseXMinAfterMod > 0 || (SexStimulation != null && SexStimulation.Severity != 0) ; } }

    [JsonProperty] protected int consecutiveClimaxCount = 0;
    [JsonIgnore] public int ConsecutiveClimaxCount { get { return consecutiveClimaxCount; } }
    [JsonProperty] bool currentlyClimaxed = false;
    [JsonIgnore] public bool JustClimaxed { get { return Climaxing.Severity >= 1; } }

    private StatusEx_Instance _sexStimulation = null;
    [JsonIgnore] public StatusEx_Instance SexStimulation
    {
        get
        {
            if (_sexStimulation == null) _sexStimulation = GetStatusEXByStringMatch("chara_status_sex_stimulation");
            return _sexStimulation;
        }
    }

    private Status_Instance fatigue = null;
    protected Status_Instance Fatigue
    {
        get
        {
            if (fatigue == null) fatigue = FindStatusByExactID("chara_status_fatigue");
            return fatigue;
        }
    }
    [JsonIgnore]
    public bool Fatigued
    {
        get
        {
            if (Fatigue == null) return false;
            return Fatigue.SeverityIndex < 2;
        }
    }


    private Status_Instance _pain = null;
    [JsonIgnore] public Status_Instance Pain
    {
        get
        {
            if (_pain == null) _pain = FindStatusByExactID("chara_status_pain");
            return _pain;
        }
    }

    [JsonIgnore] public StatusEx_Instance Consciousness
    {
        get
        {
            if (consciousness == null) consciousness = FindStatusEXByExactID("chara_status_consciousness");
            return consciousness;
        }
    }

    [JsonIgnore] public bool isConsciousnessUnconscious { get { return Consciousness.Tags.Contains("consciousness_unconscious"); } }
    [JsonIgnore] public bool isConsciousnessReduced { get { return Consciousness.Tags.Contains("consciousness_reduced") || Consciousness.Tags.Contains("consciousness_unconscious"); } }

    private StatusEx_Instance consciousness = null;

    protected int ownerRefID = -1;



    //public List<Stats_Derived_InstanceBase> statsDerived;




    [JsonProperty] protected Dictionary<string, Status_Instance> _statusInstances = new Dictionary<string, Status_Instance>();
    [JsonProperty] protected Dictionary<string, StatusEx_Instance> _statusInstancesEx = new Dictionary<string, StatusEx_Instance>();

    void ClearStatusEXCache()
    {
        _list_statusInstacesEX = null;
    }

    //List<StatusEx_Instance> _list_statusInstacesEX_Displayable = null;
    List<StatusEx_Instance> _list_statusInstacesEX = null;
    [JsonIgnore] public List<StatusEx_Instance> statusInstancesEx
    {
        get
        {
            if (_list_statusInstacesEX == null) _list_statusInstacesEX = _statusInstancesEx.Values.ToList();
            return _list_statusInstacesEX;
        }
    }

    List<Status_Instance> _StatusInstances = null;
    [JsonIgnore] public List<Status_Instance> StatusInstances { get { 
            if (_StatusInstances == null)
            {
                _StatusInstances = _statusInstances.Values.ToList();
            }
            return _StatusInstances; } }


    void ClearStatusInstanceCache()
    {
        _StatusInstances = null;
    }

    public bool HasStatusByStringMatch(string s)
    {
        var candidates = FindStatusByID(s);
        return candidates.Count > 0;
    }

    public Status_Instance GetStatusByStringMatch(string s)
    {
        var candidates = FindStatusByID(s);
        if (candidates.Count < 1) return null;
        else if (candidates.Count > 1)
        {
            Debug.LogError($"Warning GetStatusByStringMatch {s} has found {candidates.Count} candidates, returning first");

        }
        return candidates[0];
    }

    public float GetStatusSeverityByStringMatch(string s)
    {
        var candidates = FindStatusByID(s);
        if (candidates.Count < 1) return -1;
        else if (candidates.Count > 1)
        {
            Debug.LogError($"Warning GetStatusSeverityByStringMatch {s} has found {candidates.Count} candidates, returning first");

        }
        return candidates[0].Severity;
    }

    public void RemoveStatusByStringMatch(string s)
    {
        bool update = false;
        var keys = _statusInstances.Keys.ToArray();
        foreach(var k in keys)
        {
            if (k.Contains(s))
            {
                this._statusInstances.Remove(k);
                ClearStatusInstanceCache();
                update = true;
            }
        }

        if (update) UpdateStatus();

    }

    public void AddOrModStatus(string s, float modSeverity = 0f, int modDuration = -1, float severityCap = -1f)
    {
        if (s == null || s == "" || s.Length < 1) return;
        Status_Instance instance = GetStatusByStringMatch(s);
        if (instance != null && instance.BaseRef.constant && modSeverity == 0f)
        {
            if (scr_System_CentralControl.current.LogPrefs.DLog_Status) Debug.LogError($"ERROR modding constant statusInstance {s} with null severity");
            return;
        }
        else if (instance != null)
        {
            if (instance == AfterClimax)
            {
                consecutiveClimaxCount += 1;
                //Debug.LogError("SETTING CURRENTLYCLIMAXED TO TRUE");
                //if (currentlyClimaxed == 0) currentlyClimaxed = 2;
                //else currentlyClimaxed += 1;
            }
            else if (instance.BaseRef.variationMode.randomVariation is Status_Base.RandomVariation_Sex)
            {   // climax_count, sex_BMCVAW
                if (Climaxing.Severity >= 1) return;
                else if (AfterClimax != null && AfterClimax.Severity < 0)
                {
                    AfterClimax.SeverityAdd(Math.Min(Math.Abs(AfterClimax.Severity), modSeverity), severityCap);
                    //pauseXMinAfterMod = Math.Max(pauseXMinAfterMod, 1);
                }
                //Debug.LogError("MATH MIN ["+Math.Abs(afterClimax.Severity).ToString()+"] ["+ modSeverity.ToString() + "]");
                //pauseXMinAfterMod = Math.Max(pauseXMinAfterMod, instance.BaseRef.variationMode.pauseXMinAfterMod);
                instance.pauseXMinAfterMod = instance.BaseRef.variationMode.pauseXMinAfterMod;
            }

            //Debug.LogError("Stimulating status " + s + " with severityCap at " + severityCap);
            instance.SeverityAdd(modSeverity, severityCap);

            if (instance.duration != -1) instance.duration += modDuration;
        }
        else
        {
            AddStatus(s, modSeverity, modDuration);
        }
        UpdateStatus();
    }

    bool previouslyUnconscious = false;

    private void UpdateStatus()
    {
        previouslyUnconscious = isConsciousnessUnconscious;
        this.modifiers_temporary.Clear();
        foreach (var i in _statusInstancesEx) i.Value.ClearCache(true);

        RefreshAllStats();
    }

    protected void AddStatus(string s, float initialSeverity = 0f, int durationMinute = -1)
    {
        Status_Base target = scr_System_Serializer.current.GetByNameOrID_Status_Base(s);
        if (target != null)
        {
            Status_Instance si = target.Instantiate(this, initialSeverity, durationMinute);
            this._statusInstances.Add(si.ID, si);

            ClearStatusInstanceCache();

            if (si.BaseRef.variationMode.randomVariation is Status_Base.RandomVariation_Sex)
            {
                this.pauseXMinAfterMod = Math.Max(this.pauseXMinAfterMod, si.BaseRef.variationMode.pauseXMinAfterMod);
                si.pauseXMinAfterMod = si.BaseRef.variationMode.pauseXMinAfterMod;
            }
        }
        else Debug.LogError("AddStatus Failed cuz target status ["+s+"] unfound");
    }

    public string OwnerName()
    {
        return this.Owner.FirstName;
    }

    [JsonIgnore] public StatusEx_Instance Lust
    {
        get
        {
            if (!Owner.hasStatKeyword("lust")) return null;
            if (lust == null ) lust = FindStatusEXByExactID("chara_status_lust");
            return lust;
        }
    }
    [JsonIgnore] private StatusEx_Instance lust = null;
    [JsonIgnore]
    public Status_Instance Lust_Hidden
    {
        get
        {
            if (!Owner.hasStatKeyword("lust")) return null;
            if (lust_hidden == null) lust_hidden = FindStatusByExactID("chara_status_lust_hidden");
            return lust_hidden;
        }
    }
    [JsonIgnore] private Status_Instance lust_hidden = null;

    [JsonIgnore] public StatusEx_Instance Stress
    {
        get
        {
            if (!Owner.hasStatKeyword("stress")) return null;
            if (stress == null) stress = FindStatusEXByExactID("chara_status_stress");
            return stress;
        }
    }
    [JsonIgnore] private StatusEx_Instance stress = null;

    [JsonIgnore] public StatusEx_Instance Mood
    {
        get
        {
            if (!Owner.hasStatKeyword("mood")) return null;
            if (mood == null ) mood = FindStatusEXByExactID("chara_status_mood");
            return mood;
        }
    }
    [JsonIgnore] private StatusEx_Instance mood = null;



}

