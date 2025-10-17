using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using Newtonsoft.Json;

public class CombatStatManager : I_StatsManager
{

    public enum PostureState
    {
        Broken,
        Recovery,
        Neutral
    }
    [JsonIgnore]
    public Character_Trainable Owner
    {
        get
        {
            return owner;
        }
    }
    [JsonIgnore] public Stats_Derived_Extended_Instance HP { get { return GetStatEx("stats_derived_extended_hp"); } }
    [JsonIgnore] public Stats_Derived_Extended_Instance MP { get { return GetStatEx("stats_derived_extended_mp"); } }
    [JsonIgnore] public Stats_Derived_Extended_Instance Stamina { get { return GetStatEx("stats_derived_extended_stamina"); } }
    [JsonIgnore] public Stats_Derived_Extended_Instance Energy { get { return GetStatEx("stats_derived_extended_energy"); } }

    int _posture = 0;
    public PostureState PostureStatus = PostureState.Neutral;
    public int Posture { get { return _posture; } }
    public int MaxPosture
    {
        get
        {
            return (int)HP.Value;
        }
    }

    float _evasion = 0;

    public void ModEvasion(int evasion)
    {
        this._evasion = Math.Clamp(_evasion + evasion, float.MinValue, float.MaxValue);
    }

    public int Evasion_Pre = 0;
    public int Evasion
    { get
        {
            if (_stat_evasion_base == null) _stat_evasion_base = GetDerivedStat("stats_derived_evasionBase");
            return Math.Clamp((int)(_stat_evasion_base.FinalValue() + _evasion), 0, int.MaxValue);
        }
        set
        {
            _evasion = value;
        }
    }

    public bool Evade(float tracking)
    {
        Evasion_Pre = (int)Evasion;
        bool result = !isPostureBroken && Evasion_Pre > (int)tracking;
        ModEvasion((int)-tracking);
        return result;
    }

    Stats_Derived_Instance _stat_evasion_base = null;


    public int PreviousRoundActionCount = 2;
    public bool CanPush
    { get
        {
            return PreviousRoundActionCount < 3;
        } }

    [JsonIgnore]
    public Dictionary<string, CombatActionPreset> ValidPresets = new Dictionary<string, CombatActionPreset>();
    /// <summary>
    /// Build all possible combo at this stage with different weapon combinations<br/>
    /// A valid combo should use at most one weapon.
    /// </summary>
    protected void InitializePresets()
    {
        var allvalids = new List<CombatAction>();
        if (Owner.Body.AlwaysValidActions.Count > 0) allvalids.AddRange(Owner.Body.AlwaysValidActions);
        foreach (var kvp in Owner.Body.CombatActions) if (kvp.Value.Count > 0) allvalids.AddRange(kvp.Value);
        foreach(var kvp in Owner.Inventory.CombatActions) if (kvp.Value.Count > 0) allvalids.AddRange(kvp.Value);
        allvalids = allvalids.Distinct().ToList();

        foreach(var preset in scr_System_Serializer.current.MasterList.CombatActionPresets.list)
        {
            if (preset.forbidUseInRandom) continue;
            if (Utility.ListContainsStrict(allvalids, preset.Actions) && !ValidPresets.ContainsKey(preset.ID)) ValidPresets.Add(preset.ID, preset);
        }
    }


    public void RestoreAll()
    {
        foreach (var ex in StatsExtended) if (ex != null) ex.RestoreMax();
        this._posture = MaxPosture;
    }

    /// <summary>
    /// Return true if posture break now<br/>
    /// Posture break conditions:<br/>
    /// 1. current posture <= 0<br/>
    /// 2. current damage source is not self<br/>
    /// 3. this instance damage is above 0
    /// 4. not currently broken
    /// </summary>
    /// <param name="value"></param>
    /// <param name="isAttack"></param>
    /// <returns></returns>
    public bool ModPosture(int value, bool isAttack)
    {
        _posture = Math.Clamp((int)(_posture + value), 0, this.MaxPosture);
        if (_posture <= 0 && isAttack && value <= 0 && !isPostureBroken)
        {
            PostureStatus = PostureState.Broken;
            this.Evasion = 0;
            return true;
        }
        return false;
    }

    public bool CanAct
    { get
        {
            return this.HP.Value >= 1;
        } }

    public bool isPostureBroken
    {
        get
        {
            return this.PostureStatus < PostureState.Neutral;
        }
    }

    public void RecoverPosture(bool fullRecovery = false)
    {
        if (fullRecovery)
        {
            _posture = this.MaxPosture;
            PostureStatus = PostureState.Neutral;
            return;
        }
        switch(PostureStatus)
        {
            case PostureState.Broken:
                PostureStatus = PostureState.Recovery;
                break;
            case PostureState.Recovery:
                _posture = this.MaxPosture;
                PostureStatus = PostureState.Neutral;
                break;
            default:
                break;
        }
    }

    public List<Status_Instance> FindStatusByID(string statID)
    {
        return this.StatusInstances.FindAll(x => x.ID.Contains(statID));
    }
    StatsManager Parent = null;
    public CombatStatManager(Character_Trainable Owner, StatsManager stats)
    {
        this.owner = Owner;
        this.Parent = stats;
        // first, copy modifiers
        this._posture = (int)Parent.HP.Value;
        Reset(null,true);
        InitializePresets();
    }


    /// <summary>
    /// Add to combat modifiers specific to this manager and do not touch original
    /// </summary>
    /// <param name="mods"></param>
    public void AddStatModifier(List<Stat_Modifier> mods)
    {
        foreach (Stat_Modifier mod_instance in mods)
        {
            for (int i = modifiers_combat.Count - 1; i >= 0; i--)
            {
                if (modifiers_combat[i].statID == mod_instance.statID && modifiers_combat[i].modKey == mod_instance.modKey
                        && ((modifiers_combat[i].type == Stat_Modifier.StatMod_Type.setMult && modifiers_combat[i].type == mod_instance.type)
                            || (modifiers_combat[i].type == Stat_Modifier.StatMod_Type.setBase && modifiers_combat[i].type == mod_instance.type)))
                    modifiers_combat.RemoveAt(i);
            }

            modifiers_combat.Add(mod_instance);
        }
    }

    protected Stats_Base baseStat_STR = null, baseStat_CON = null, baseStat_PSY = null, baseStat_WIL = null;

    public void Reset(CombatInstance inst, bool fullReset = false)
    {
        this.modifiers_combat.Clear();

        // foreach stat in parent, make copy and reassign parent to this, else their stat query wont work properly
        this.baseStat_STR = Parent.Strength.Copy(this);
        this.baseStat_CON = Parent.Constitution.Copy(this);
        this.baseStat_PSY = Parent.Psyche.Copy(this);
        this.baseStat_WIL = Parent.Willpower.Copy(this);

        // I can directly use parent modifiers since that one is permanent results

        // stats derived -> these are added on request so dont need to copy

        // statsEX
        list_statsExtended.Clear();
        foreach (var ex in Parent.StatsExtended)
        {
            this.list_statsExtended.Add(ex.Copy(this));
        }

        // status
        _statusInstances.Clear();
        foreach (var status in Parent.StatusInstances)
        {
            this._statusInstances.Add(status.Copy(this));
        }

        // statusEX
        _statusInstancesEx.Clear();
        foreach (var ex in Parent.statusInstancesEx)
        {
            this._statusInstancesEx.Add(ex.Copy(this));
        }


        Refresh(inst, fullReset);
    }

    protected void Refresh(CombatInstance inst, bool fullReset = false)
    {   // refer to StatManager.RefreshAllStats
        if (this.Owner == null) return;

        foreach (var i in list_statsDerived) i.ClearCache();

        // add missing statEX -> not required, everything is based on parent anyway
        // remove invalid statEX -> not required, same as prev

        // force refresh StatsEx value to keep it valid
        foreach (var i in StatsExtended) i.ModValue(0f);
        foreach (var ex in this.statusInstancesEx) ex.ClearCache();

        if (fullReset)  this._posture = Math.Clamp(inst == null ? this._posture : inst.PostureStorage[this.Owner.RefID], 0, this.MaxPosture);
        else this._posture = Math.Clamp(this._posture, 0, this.MaxPosture);

        if (fullReset) this._evasion = (int)GetStatValue("stats_derived_evasionBase");
    }

    protected List<Status_Instance> _statusInstances = new List<Status_Instance>();
    protected List<StatusEx_Instance> _statusInstancesEx = new List<StatusEx_Instance>();

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


    public bool hasStatKeyword(string statKeyword)
    {
        return Owner.hasStatKeyword(statKeyword);
    }
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

    protected List<Stat_Modifier> modifiers_combat = new List<Stat_Modifier>();

    protected Character_Trainable owner = null;

    protected List<Stat_Modifier> GetModifiers(string statID, List<string> contexts = null, bool checkStatusInstance = true, bool checkMemory = true)
    {
        List<Stat_Modifier> list = new List<Stat_Modifier>();
        foreach (var mod in Parent.Modifiers)
        {
            if (mod.statID != statID) continue;
            else
            {
                list.Add(mod);
            }
        }


        foreach (var mod in Parent.Modifiers_Temporary)
        {
            if (mod.statID != statID) continue;
            else
            {
                list.Add(mod);
            }
        }

        foreach (var mod in modifiers_combat)
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

    public string OwnerName()
    {
        return this.Owner.FirstName;
    }

    public float GetStatValue(string statID, List<string> contexts = null)
    {
        if (contexts != null)
        {
            contexts = contexts.Distinct().ToList();
            contexts.Sort();
        }

        // Catch Stat Base
        if (statID == Strength.ID) return Strength.FinalValue(contexts);
        else if (statID == Constitution.ID) return Constitution.FinalValue(contexts);
        else if (statID == Psyche.ID) return Psyche.FinalValue(contexts);
        else if (statID == Willpower.ID) return Willpower.FinalValue(contexts);


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

    protected Stats_Derived_Extended_Instance GetStatEx(string statID)
    {
        return StatsExtended.Find(x => x.ID == statID);
    }

    protected List<Stats_Derived_Extended_Instance> list_statsExtended = new List<Stats_Derived_Extended_Instance>();
    [JsonIgnore] public List<Stats_Derived_Extended_Instance> StatsExtended { get { return list_statsExtended; } }

    [JsonIgnore]
    protected List<Stats_Derived_Instance> list_statsDerived
    {
        get
        {
            if (_list_statDerived == null)
            {
                _list_statDerived = new List<Stats_Derived_Instance>();
                foreach (var i in scr_System_Serializer.current.index_StatsDerived.list)
                {
                    if (Owner.hasStatKeyword(i.StatKeyword)) _list_statDerived.Add(i.Instantiate(this));
                }
            }
            return _list_statDerived;
        }
    }
    protected List<Stats_Derived_Instance> _list_statDerived = null;
    public Status_Instance GetStatusByStringMatch(string s)
    {
        Status_Instance si = this.StatusInstances.Find(x => x.ID.Contains(s));
        if (si != null) return si;
        else return null;
    }


    [JsonIgnore]
    public List<StatusEx_Instance> statusInstancesEx_Displayable
    {
        get
        {
            List<StatusEx_Instance> list = new List<StatusEx_Instance>();
            foreach (StatusEx_Instance i in statusInstancesEx)
            {
                if (i.BaseRef.noDisplay) continue;
                if (!i.SeverityDisplayable) continue;
                list.Add(i);
            }
            return list;
        }
    }
    [JsonIgnore]
    public List<StatusEx_Instance> statusInstancesEx
    {
        get
        {
            return _statusInstancesEx;
        }
    }

    [JsonIgnore] public List<Status_Instance> StatusInstances { get { return _statusInstances; } }
    [JsonIgnore]
    public List<Status_Instance> StatusInstances_Displayable
    {
        get
        {
            List<Status_Instance> list = new List<Status_Instance>();
            foreach (Status_Instance i in StatusInstances)
            {
                if (i.BaseRef.noDisplay) continue;
                if (!i.SeverityDisplayable) continue;
                list.Add(i);
            }
            return list;
        }
    }

    [JsonIgnore]
    public Stats_Base Strength
    {
        get
        {
            return baseStat_STR;
        }
    }
    [JsonIgnore]
    public Stats_Base Constitution
    {
        get
        {
            return baseStat_CON;
        }
    }
    [JsonIgnore]
    public Stats_Base Psyche
    {
        get
        {
            return baseStat_PSY;
        }
    }
    [JsonIgnore]
    public Stats_Base Willpower
    {
        get
        {
            return baseStat_WIL;
        }
    }

    /// <summary>
    /// Will return a list of all active defense comp AND (if exist target parts) part defense comp
    /// </summary>
    /// <param name="source"></param>
    /// <returns></returns>
    public List<DefenseStats> GetDefenses(CombatActionInstance source, bool isPrecalc)
    {
        var result  =new List<DefenseStats>();
        var handler = source.Handler;
        var lastAction_target = handler.LastActionsOngoing.ContainsKey(source.targetRef.RefID) ? handler.LastActionsOngoing[source.targetRef.RefID] : null;

        // get cover / get active defense (both of them are activated by actions, so only one exist)
        // get active defense (weapon block)
        if (lastAction_target != null && lastAction_target.lingeringDefense.isValid && !handler.ActorStats[source.ownerRef.RefID].isPostureBroken)
        {
            //Debug.Log($"lingering defense {lastAction_target.lingeringDefense.Name} found");
            result.Add(lastAction_target.lingeringDefense);
        }
        // spell effect constant defense goes here        
        // TODO

        // get bodypart (active defense/target/random) armor

        if (source.targetPartRef != null) result.Add(source.targetPartDefense);
        else if (source.targetPartRef == null && !isPrecalc)
        {
            // select random part -> first for now to be deterministic
            source.targetPartRef = Utility.GetRandomElement(source.targetRef.Body.Body);
            ItemComponent_Defense armor = null;
            var layer = Utility.GetRandomElement(source.targetPartRef.availableSlots);
            var i = source.targetPartRef.GetRandArmor(layer);

            if (i != null && i.Comp_Defense != null)
            {
                armor = i.Comp_Defense;
                if (armor != null)
                {
                    source.targetPartDefense.Set(i.DisplayName, armor);
                    result.Add(source.targetPartDefense);
                }
            }

            if (source.targetPartRef.Defense != null) result.Add(source.targetPartRef.Defense);
        }

        return result;
    } 
    /*
    // Self Defense Stat
    public float GetMovement(CombatInstance inst)
    {
        var movStat = inst.ActorStats[ownerRef.RefID].GetDerivedStat("stats_derived_evasionBase");
        return movStat == null ? 0f : movStat.FinalValue();
    } 
    */



}