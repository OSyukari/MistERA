using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public interface I_StatsManager
{
    public void GetModifiers(List<Stat_Modifier> results, Stats_Derived_Base obj, string statID, List<string> contexts = null, bool forbidStatus = false);
    public void GetModifiers(List<Stat_Modifier> results, Stats_Base obj, string statID, List<string> contexts = null);
    public void GetModifiers(List<Stat_Modifier> results, StatusEx_Instance obj, string statID, List<string> contexts = null);

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
    /// <summary>
    /// Appends matching status instances into <paramref name="results"/>. Caller owns and clears the list.
    /// </summary>
    public void FindStatusByID(string statID, List<Status_Instance> results);
    public void SetStatusSeverity(string statusID, float severity);

    [JsonIgnore] public Stats_Derived_Extended_Instance HP { get; }
    [JsonIgnore] public Stats_Derived_Extended_Instance MP { get; }
    [JsonIgnore] public Stats_Derived_Extended_Instance Stamina { get; }
    [JsonIgnore] public Stats_Derived_Extended_Instance Energy { get ; }

    public Character_Trainable Owner { get; }
    public void RestoreAll();

    /// <summary>
    /// Monotonic counter, incremented on structural changes that invalidate every stat (modifier rebuild, status
    /// add/remove, UpdateStatus). Stat caches store the version they were computed at and lazily self-invalidate on
    /// mismatch. Finer changes go through the per-stat calls below (Docs/Stats/Plan_StatInvalidation.md).
    /// </summary>
    [JsonIgnore] public int StatModsVersion { get; }
    public void NotifyStatModsChanged();

    // ---- per-stat invalidation (dependency edges captured while nodes compute) ----
    /// <summary>Brackets a node's recompute: reads registered meanwhile become edges into it. Pair with EndStatCompute.</summary>
    public void BeginStatCompute(I_StatNode node);
    public void EndStatCompute(I_StatNode node);
    /// <summary>A node's value was read: the node computing right now (if any) depends on it.</summary>
    public void RegisterStatRead(I_StatNode node);
    /// <summary>A status severity was read by statusMatch (Contains match, as FindStatusByID / GetStatusByStringMatch).</summary>
    public void RegisterStatusValueRead(string statusMatch);
    /// <summary>A status' severity value changed (decay, variation tick, SeverityAdd / Set): dirties its value readers.</summary>
    public void NotifyStatusValueChanged(Status_Instance status);
    /// <summary>A status crossed into another variant: dirties the stats its old and new variant modifiers target.</summary>
    public void NotifyStatusIndexChanged(Status_Instance status, int previousIndex);
    /// <summary>Dirties the node of statID (base / derived / StatusEx) and its readers; no-op when it has no instance.</summary>
    public void MarkStatDirty(string statID);
}

public class StatsManager : I_StatsManager
{
    /// <summary>
    /// trait requirements (race keywords / appearance) against the owner; no owner = no check
    /// </summary>
    bool IsTraitValid(Traits s)
    {
        if (Owner == null) return true;
        return s.IsValidFor(Owner);
    }

    public void AddTrait(Traits s) {
        if (s == null) return;
        if (!IsTraitValid(s))
        {
            Debug.LogWarning($"AddTrait [{s.ID}] refused on {Owner.FirstName}: requirements not met");
            return;
        }

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
        // body traits are owned (and saved) by Character_Body, only the runtime list holds them here
        if (!(s.Parent != null && s.Parent.isBodyTrait) && !traits.Contains(s.ID)) traits.Add(s.ID);
        _queryCache.Clear();
    }
    public void RemoveTrait(Traits s)
    {
        if (s == null) return;
        Traits.Remove(s);
        traits.Remove(s.ID);
        _queryCache.Clear();
    }

    bool traits_init = false;

    public void RefreshTraits()
    {
        Traits.Clear();
        _queryCache.Clear();

        // saves from before the body traits refactor persisted sensitivity ids here; they now come from the body
        // also drop traits whose requirements (race keywords / appearance) the owner no longer meets
        traits.RemoveAll(id =>
        {
            var tr = scr_System_Serializer.current.MasterList.Traits_Groups.GetTraitByID(id);
            if (tr == null) return false;
            if (tr.Parent != null && tr.Parent.isBodyTrait) return true;
            if (!IsTraitValid(tr))
            {
                Debug.Log($"Removing trait [{id}] from {Owner.FirstName}: requirements not met");
                return true;
            }
            return false;
        });

        if (Owner != null && Owner.Template != null)
        {
            foreach (var trait in Owner.Template.traits)
            {
                var tr = scr_System_Serializer.current.MasterList.Traits_Groups.GetTraitByID(trait);
                if (tr != null)
                {
                    if (tr.Parent != null && tr.Parent.isBodyTrait) continue; // seeded into the body instead
                    if (!IsTraitValid(tr)) continue;
                    AddTrait(tr);
                }
            }
        }
        if (Owner != null && Owner.Body != null)
        {
            // body traits still go into the runtime list so their stat_modifiers apply and displays / GetTraitByGroupID find them
            foreach (var tr in Owner.Body.BodyTraits) AddTrait(tr);
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
        _queryCache.Clear();
    }



    protected void PopulateTraits()
    {
        if (false && scr_System_CentralControl.current.isSafeMode)
        {
            Debug.Log($"PopulateTraits check exist: masterlist? {scr_System_Serializer.current.MasterList != null} " +
                $"Traits_Groups? {scr_System_Serializer.current.MasterList != null && scr_System_Serializer.current.MasterList.Traits_Groups != null} " +
                $"traits_All? {scr_System_Serializer.current.MasterList != null && scr_System_Serializer.current.MasterList.Traits_Groups != null && scr_System_Serializer.current.MasterList.Traits_Groups.traits_All != null}");
        }
        var raceKeywords = new List<string>();
        Trait_Requirement.GetRaceKeywords(Owner, raceKeywords);
        foreach(var tgroup in scr_System_Serializer.current.MasterList.Traits_Groups.traits_All)
        {
            foreach(var tr in tgroup)
            {
                if (tr.SortType != Trait_Group_Type.SortedList && tr.SortType != Trait_Group_Type.UnsortedList) continue;
                if (tr.isBodyTrait) continue; // Character_Body fills its own defaults
                if (tr.tags.Contains("do_not_use")) continue;
                if (!tr.allowPopulate) continue;
                // invalid group -> null, invalid neutral -> first valid entry
                var neutral = Owner == null ? tr.getNeutralinGroup() : tr.GetNeutralValid(raceKeywords, Owner.Appearance);
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

    public bool HasTrait(Traits t) { return HasTrait(t.ID); }
    public bool HasTrait(string id) { return traits.Contains(id) || (Owner != null && Owner.Body != null && Owner.Body.HasBodyTrait(id)); }
    /// <summary>
    /// Return all status with id match
    /// </summary>
    /// <param name="statID"></param>
    /// <returns></returns>
    public List<Status_Instance> FindStatusByID(string statID)
    {
        var l = new List<Status_Instance>();
        FindStatusByID(statID, l);
        return l;
    }
    public void FindStatusByID(string statID, List<Status_Instance> results)
    {
        foreach (var i in _statusInstances) if (i.Key.Contains(statID)) results.Add(i.Value);
    }
    public Status_Instance FindStatusByExactID(string statID)
    {
        if (_statusInstances.TryGetValue(statID, out var value)) return value;
        else return null;
    }
    int _statModsVersion = 0;
    [JsonIgnore] public int StatModsVersion { get { return _statModsVersion; } }
    /// <summary>
    /// Bump-only (no recompute), so it is safe to call from anywhere, including mid-computation
    /// and from inside RefreshAllStats' statbar loop. Stale caches recompute lazily on next read.
    /// </summary>
    public void NotifyStatModsChanged()
    {
        _statModsVersion++;
        _anyChangeVersion++;
    }

    #region Per-stat invalidation (Docs/Stats/Plan_StatInvalidation.md)
    int _anyChangeVersion = 0;
    /// <summary>
    /// Bumped by every invalidation, structural or per-stat. For copies that cannot follow per-stat edges
    /// (CombatStatManager composes it into its own StatModsVersion = the old whole-character behaviour).
    /// </summary>
    [JsonIgnore] public int AnyChangeVersion { get { return _anyChangeVersion; } }

    // nodes currently recomputing, innermost last; reads register an edge into the innermost
    [JsonIgnore] readonly List<I_StatNode> _computeStack = new List<I_StatNode>(4);
    // status severity readers, by the match string they read with (Contains, as FindStatusByID / GetStatusByStringMatch)
    [JsonIgnore] readonly Dictionary<string, StatDependents> _statusValueReaders = new Dictionary<string, StatDependents>();

    public void BeginStatCompute(I_StatNode node)
    {
        _computeStack.Add(node);
    }
    public void EndStatCompute(I_StatNode node)
    {
        int last = _computeStack.Count - 1;
        if (last >= 0 && ReferenceEquals(_computeStack[last], node)) { _computeStack.RemoveAt(last); return; }
        // unbalanced (an exception skipped an End): drop down to this node so later edges stay correct
        for (int i = last; i >= 0; i--)
        {
            bool found = ReferenceEquals(_computeStack[i], node);
            _computeStack.RemoveAt(i);
            if (found) return;
        }
    }
    public void RegisterStatRead(I_StatNode node)
    {
        int last = _computeStack.Count - 1;
        if (last < 0) return;
        var reader = _computeStack[last];
        if (!ReferenceEquals(reader, node)) node.Dependents.Add(reader);
    }
    public void RegisterStatusValueRead(string statusMatch)
    {
        int last = _computeStack.Count - 1;
        if (last < 0 || string.IsNullOrEmpty(statusMatch)) return;
        if (!_statusValueReaders.TryGetValue(statusMatch, out var readers))
        {
            readers = new StatDependents();
            _statusValueReaders.Add(statusMatch, readers);
        }
        readers.Add(_computeStack[last]);
    }
    public void NotifyStatusValueChanged(Status_Instance status)
    {
        _anyChangeVersion++;
        if (_statusValueReaders.Count == 0) return;
        var id = status.ID;
        foreach (var kvp in _statusValueReaders)
            if (id.Contains(kvp.Key)) kvp.Value.MarkAllDirty();
    }
    public void NotifyStatusIndexChanged(Status_Instance status, int previousIndex)
    {
        // value readers too: summation_capmod reads the index, and an index can move without the value (threshold mod)
        NotifyStatusValueChanged(status);
        var variants = status.BaseRef?.variants;
        if (variants == null) return;
        if (previousIndex >= 0 && previousIndex < variants.Count) MarkStatIDsDirty(variants[previousIndex].stat_modifiers);
        MarkStatIDsDirty(status.SeverityModifiers);
    }
    void MarkStatIDsDirty(List<Stat_Modifier> mods)
    {
        if (mods == null) return;
        for (int i = 0; i < mods.Count; i++) MarkStatDirty(mods[i].statID);
    }
    public void MarkStatDirty(string statID)
    {
        _anyChangeVersion++;
        if (string.IsNullOrEmpty(statID)) return;
        // stored fields only: an instance not created yet has nothing cached (it computes fresh when created)
        if (baseStat_STR != null && statID == baseStat_STR.ID) { baseStat_STR.MarkStatDirty(); return; }
        if (baseStat_CON != null && statID == baseStat_CON.ID) { baseStat_CON.MarkStatDirty(); return; }
        if (baseStat_PSY != null && statID == baseStat_PSY.ID) { baseStat_PSY.MarkStatDirty(); return; }
        if (baseStat_WIL != null && statID == baseStat_WIL.ID) { baseStat_WIL.MarkStatDirty(); return; }
        if (_list_statDerived != null && _list_statDerived.TryGetValue(statID, out var derived)) { derived.MarkStatDirty(); return; }
        if (_statusInstancesEx.TryGetValue(statID, out var statusEx)) statusEx.MarkStatDirty();
    }
    #endregion

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

    /// <summary>
    /// Set severity to a 0-1 position along the status' own [variants[0], variants[last]] threshold
    /// range, so the caller doesn't need to know or care whether that status is scaled 0-100, -100-0,
    /// etc. Creates the status (at ratio 0) first if it doesn't exist yet.
    /// </summary>
    public void SetStatusSeverityRatio(string statusID, float ratio)
    {
        var instance = FindStatusByExactID(statusID);
        if (instance == null)
        {
            AddStatus(statusID, 0f);
            instance = FindStatusByExactID(statusID);
        }
        instance?.SeverityRatioSet(ratio);
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

        // Save migration: chara_status_sex_climax_count was renamed so the chara_status_sex_ prefix summation
        // (chara_status_sex_stimulation_pos) no longer counts climaxes as stimulation. Only runs for saves holding the old ID.
        // The old instance is never read: its definition is gone (Severity would dereference a null BaseRef), and nothing
        // ever wrote a value to it, so the renamed status starts at 0.
        if (this._statusInstances != null && _statusInstances.ContainsKey("chara_status_sex_climax_count"))
        {
            _statusInstances.Remove("chara_status_sex_climax_count");
            ClearStatusInstanceCache();
            if (!_statusInstances.ContainsKey("chara_status_climax_count")) AddStatus("chara_status_climax_count");
        }

        // Save migration: status definitions can move between the Status and StatusEX lists
        // across versions (e.g. chara_status_pain), but instances are serialized per save.
        if (this._statusInstances != null)
        {
            var orphans = new List<string>();
            foreach (var i in _statusInstances) if (scr_System_Serializer.current.GetByNameOrID_Status_Base(i.Key) == null) orphans.Add(i.Key);
            foreach (var key in orphans)
            {
                Debug.Log($"Dropping status [{key}] from {chara.FullName}: no Status_Base definition (removed or moved to StatusEX)");
                _statusInstances.Remove(key);
            }
            if (orphans.Count > 0) ClearStatusInstanceCache();
        }

        foreach (var statusEX in scr_System_Serializer.current.index_StatusEX.list)
        {
            if (!_statusInstancesEx.ContainsKey(statusEX.statusID))
            {
                _statusInstancesEx.Add(statusEX.statusID, statusEX.Instantiate(this));
                ClearStatusEXCache();
            }
        }

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
            if (_statusInstancesEx.ContainsKey(statusEX.statusID)) continue; // ReEstablishParent may have added it already
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
    /// <summary>The memory-fed StatusEx (MemoryManager.IsMemoryStatID) go dirty; recomputed lazily on the next read.</summary>
    public void RefreshAttitude()
    {
        MarkStatDirty(MemoryManager.MemoryStat_Mood);
        MarkStatDirty(MemoryManager.MemoryStat_Stress);
        MarkStatDirty(MemoryManager.MemoryStat_Lust);
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
                    Utility.DistinctInPlace(_permanentTags);
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
                var comp = Owner.Body.ResolveEquip(equipRef)?.Comp_Equippable;
                if (comp != null && comp.statModifiers.Count > 0)
                {
                    AddStatModifier(comp.statModifiers);
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

        NotifyStatModsChanged();
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
            return this.GetStatusByStringMatch(StatsUtility.Status_Sleeping) != null;
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

    #region Debug stat queries
    // For logs: read stored or already-cached state only. Unlike Severity / MaxValue / readiness getters they never evaluate
    // modifiers, fill caches or consume RNG. Cached StatusEx severity: StatusEx_Instance.Debug_TryGetCachedSeverity.

    /// <summary>The stored value of the statbar (HP, ST...) whose parent ID is statbarID - false when this character has none.</summary>
    public bool Debug_TryGetStatbarValue(string statbarID, out float value)
    {
        for (int i = 0; i < list_statsExtended.Count; i++)
        {
            var bar = list_statsExtended[i];
            if (bar == null || bar.StoredParentID != statbarID) continue;
            value = bar.Value;
            return true;
        }
        value = 0f;
        return false;
    }

    /// <summary>Appends the stored severity of every status matching statusID (FindStatusByID matching), comma separated.</summary>
    public void Debug_AppendStoredStatusSeverities(string statusID, System.Text.StringBuilder sb)
    {
        bool first = true;
        foreach (var i in _statusInstances)
        {
            if (!i.Key.Contains(statusID)) continue;
            if (!first) sb.Append(',');
            sb.Append(i.Value.StoredSeverity.ToString(System.Globalization.CultureInfo.InvariantCulture));
            first = false;
        }
    }
    #endregion


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
    [JsonIgnore] public List<Stat_Modifier> Modifiers { get { return this.modifiers; } }
    protected List<Stat_Modifier> modifiers_temporary = new List<Stat_Modifier>();
    [JsonIgnore] public List<Stat_Modifier> Modifiers_Temporary { get { return this.modifiers_temporary; } }

    [JsonIgnore] private readonly Dictionary<string, (Stat_Modifier addBase, Stat_Modifier setMult)> _lubricationStimulationMods = new Dictionary<string, (Stat_Modifier addBase, Stat_Modifier setMult)>();

    public void GetModifiers(List<Stat_Modifier> results, Stats_Derived_Base obj, string statID, List<string> contexts = null, bool forbidStatus = false)
    {
        if (!forbidStatus && obj.sexStimulationAddPerPoint.HasValue)
        {
            if (!_lubricationStimulationMods.TryGetValue(statID, out var pair))
            {
                pair = (
                    new Stat_Modifier("lubrication_stimulation", 0f) { statID = statID, type = Stat_Modifier.StatMod_Type.addBase },
                    new Stat_Modifier("lubrication_stimulation", 0f) { statID = statID, type = Stat_Modifier.StatMod_Type.setMult });
                _lubricationStimulationMods.Add(statID, pair);
            }

            var stimulation = SexStimulation?.UnclampedSeverity ?? 0f;
            if (float.IsNaN(stimulation) || float.IsInfinity(stimulation)) stimulation = 0f;
            var coefficient = obj.sexStimulationAddPerPoint.Value;
            if (float.IsNaN(coefficient) || float.IsInfinity(coefficient)) coefficient = 0f;
            pair.addBase.SetNumber(Math.Max(0f, stimulation));
            pair.setMult.SetNumber(coefficient);
            var displayName = LocalizeDictionary.QueryThenParse("ui_lubrication_stimulation");
            pair.addBase.DisplayName = displayName;
            pair.setMult.DisplayName = displayName;
            // Seed the block even at coefficient zero so ordinary trait modifiers can enable it.
            results.Add(pair.addBase);
            results.Add(pair.setMult);
        }
        GetModifiers(results, statID, contexts, !forbidStatus, false);

        // StatusEx levels can mod derived stats (eg. chara_status_sex_stimulation_pos -> v/a/o_acceptance). Only StatusEx
        // whose definition targets this stat are evaluated, and only here (not the shared path a StatusEx itself uses).
        if (!forbidStatus)
        {
            foreach (var statusEx in statusInstancesEx)
            {
                var variants = statusEx.BaseRef?.variants;
                if (variants == null) continue;
                // Avoid capturing predicates on this hot path; only evaluate statuses targeting this stat.
                bool targetsStat = false;
                for (int i = 0; i < variants.Count && !targetsStat; i++)
                {
                    var variantMods = variants[i].stat_modifiers;
                    if (variantMods == null) continue;
                    for (int j = 0; j < variantMods.Count; j++)
                    {
                        if (variantMods[j].statID != statID) continue;
                        targetsStat = true;
                        break;
                    }
                }
                if (!targetsStat) continue;
                foreach (var severityMod in statusEx.SeverityModifiers)
                {
                    if (severityMod.statID == statID) results.Add(severityMod);
                }
            }
        }
    }
    public void GetModifiers(List<Stat_Modifier> results, Stats_Base obj, string statID, List<string> contexts = null)
    {
        GetModifiers(results, statID, contexts, false, false);
    }

    public void GetModifiers(List<Stat_Modifier> results, StatusEx_Instance obj, string statID, List<string> contexts = null)
    {
        GetModifiers(results, statID, contexts, true, MemoryManager.IsMemoryStatID(statID), obj.BaseRef.constant && obj.BaseRef.capModded && obj.BaseRef.noDisplay);
    }

    /// <summary>
    /// Appends matching modifiers into <paramref name="results"/>. Caller owns and clears the list.
    /// </summary>
    protected void GetModifiers(List<Stat_Modifier> results, string statID, List<string> contexts = null, bool checkStatusInstance = true, bool checkMemory = true, bool checkRelationships = false)
    {
        foreach (var mod in modifiers)
        {
            if (mod.statID != statID) continue;
            else
            {
                results.Add(mod);
            }
        }


        foreach (var mod in modifiers_temporary)
        {
            if (mod.statID != statID) continue;
            else
            {
                results.Add(mod);
            }
        }

        // Launched recreation activities supply live, derived moodlets. They are intentionally not stored in
        // Modifiers_Temporary: participation is the source of truth and repeated dispatches must not stack them.
        Job_Activity.AppendActivityMoodletModifiers(Owner, statID, results);

        // and grab status modifiers

        foreach (var status in StatusInstances)
        {
            if (!checkStatusInstance && status.hasThresholdMod) continue;
            foreach (var severityMod in status.SeverityModifiers)
            {
                if (severityMod.statID != statID) continue;
                else results.Add(severityMod);
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
                    else results.Add(mod);
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
                    else results.Add(mod);
                }
            }
        }*/
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
        NotifyStatModsChanged();
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

    public void PreUpdateTimeTick(int minutes = 1)
    {
        bool timestopped = Owner.isTimeStopped;
        if (timestopped) return;
        for (int i = StatusInstances.Count - 1; i >= 0; i--)
        {
            var status = StatusInstances[i];
            if (status.BaseRef.constant) continue;
            status.elapsedTime += minutes;
            // Severity includes Variation(elapsedTime): a time-driven variation moved the value without SeverityAdd
            if (status.hasTimeVariation) NotifyStatusValueChanged(status);
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

        // snapshot: the loop removes / adds statuses. Reused buffer - one call per character per minute
        var keys = _statusKeyScratch;
        keys.Clear();
        foreach (var key in _statusInstances.Keys) keys.Add(key);
        for (int k = 0; k < keys.Count; k++)
        {
            var key = keys[k];
            // TotalMinutes, not Minutes: the Minutes component is 0 for whole-hour spans (EndDormantState catch-up)
            int time = (int)(Owner.isTimeStopped ? t.TotalMinutes : t_real.TotalMinutes);
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
                    else if (curr.BaseRef.statusID == StatsUtility.Status_Sleeping)
                    {
                        refresh = true;
                        float totalMissing = curr.duration + Owner.ScheduledSleepMissingMinutes;
                        Owner.Stats.AddOrModStatus(StatsUtility.Status_SleepDeprived, totalMissing, (int)(totalMissing * 0.5f));
                        Owner.ScheduledSleepMissingMinutes = 0;
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
                        else if (curr.BaseRef.statusID == StatsUtility.Status_Sleeping)
                        {
                            Owner.FullRest();
                            if (Owner.ScheduledSleepMissingMinutes > 0)
                            {
                                Owner.Stats.AddOrModStatus(StatsUtility.Status_SleepDeprived, Owner.ScheduledSleepMissingMinutes, (int)(Owner.ScheduledSleepMissingMinutes * 0.5f));
                                Owner.ScheduledSleepMissingMinutes = 0;
                            }
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
        if (pauseLLMTicks > 0) pauseLLMTicks = Math.Max(pauseLLMTicks - (int)t.TotalMinutes, 0);
        //else pauseXMinAfterMod = Math.Max(pauseXMinAfterMod - t.Minutes, 0);
        if (!hasSexualStimulation) consecutiveClimaxCount = 0;

        //Debug.LogError("Setting CurrentlyCliaxed to false");
        //currentlyClimaxed =  1;


        // refresh character Status Mod
        if (refresh) UpdateStatus();

    }

    [JsonIgnore] readonly List<string> _statusKeyScratch = new List<string>();

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
            if (_sexStimulation == null) _sexStimulation = FindStatusEXByExactID("chara_status_sex_stimulation");
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


    private StatusEx_Instance _pain = null;
    [JsonIgnore] public StatusEx_Instance Pain
    {
        get
        {
            if (_pain == null) _pain = FindStatusEXByExactID("chara_status_pain");
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

    public bool hasStatusEXTag(string s)
    {
        foreach(var i in statusInstancesEx)
        {
            if (i.Tags.Contains(s)) return true;
        }
        return false;
    }

    public bool hasStatusTag(string s)
    {
        foreach (var i in StatusInstances)
        {
            if (i.Tags.Contains(s)) return true;
        }
        return false;
    }



    /// <summary>
    /// Console command "forcePlayerConscious" override. false: use the game's system. true: always treated as conscious.
    /// Not saved - resets to false on load.
    /// </summary>
    [JsonIgnore] public bool DebugForceConscious = false;

    [JsonIgnore] public bool isConsciousnessUnconscious { get { return !DebugForceConscious && hasStatusEXTag(StatsUtility.Stat_Tag_Unconscious); } }
    [JsonIgnore] public bool isConsciousnessReduced { get { return !DebugForceConscious && (hasStatusEXTag(StatsUtility.Stat_Tag_ConsReduced) || hasStatusEXTag(StatsUtility.Stat_Tag_Unconscious)); } }

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
        Status_Instance first = null;
        int count = 0;
        foreach (var entry in _statusInstances)
        {
            if (!entry.Key.Contains(s)) continue;
            if (count == 0) first = entry.Value;
            count++;
        }
        if (count < 1) return -1;
        else if (count > 1)
        {
            Debug.LogError($"Warning GetStatusSeverityByStringMatch {s} has found {count} candidates, returning first");

        }
        return first.Severity;
    }

    public void RemoveStatusByStringMatch(string s)
    {
        bool update = false;
        List<string> removalEvents = null;
        var keys = _statusInstances.Keys.ToArray();
        foreach(var k in keys)
        {
            if (k.Contains(s))
            {
                var removed = this._statusInstances[k];
                if (removed != null && !string.IsNullOrEmpty(removed.onRemoveEventID))
                {
                    if (removalEvents == null) removalEvents = new List<string>();
                    removalEvents.Add(removed.onRemoveEventID);
                }
                this._statusInstances.Remove(k);
                ClearStatusInstanceCache();
                update = true;
            }
        }

        if (update) UpdateStatus();
        if (removalEvents != null) foreach (var eventID in removalEvents) FireStatusRemovalEvent(eventID);
    }

    public void RemoveStatusByExactID(string statusID)
    {
        if (!this._statusInstances.TryGetValue(statusID, out var removed)) return;
        this._statusInstances.Remove(statusID);
        ClearStatusInstanceCache();
        UpdateStatus();
        if (removed != null && !string.IsNullOrEmpty(removed.onRemoveEventID)) FireStatusRemovalEvent(removed.onRemoveEventID);
    }

    /// <summary>Starts a status instance's onRemoveEventID on its owner (after the status is already gone).</summary>
    void FireStatusRemovalEvent(string eventID)
    {
        if (Owner == null || scr_UpdateHandler.current == null) return;
        var ev = new EventInstance(Owner, eventID, "");
        scr_UpdateHandler.current.EventHandler.StartEvent(ev, false);
    }

    /// <summary>
    /// Keeps an optional "pain companion" status (chara_status_pain_&lt;tiedStatusID&gt;) in lockstep
    /// with a tied status, using the tied status' own |Severity|. Cycle statuses author their own
    /// severity range as positive (ticks up, e.g. chara_status_pms_luteal 0..100) or negative (ticks
    /// down, e.g. chara_status_pms_menstrual -100..0) depending on when in the phase symptoms peak —
    /// either way, 0 always means "no symptom" and the far end from 0 is worst, so abs() normalizes
    /// both conventions onto one "how bad right now" magnitude without needing to know which one the
    /// tied status used. No-ops silently when no chara_status_pain_&lt;id&gt; Status_Base is defined,
    /// so only statuses actually authored to hurt do anything here.
    /// </summary>
    public void SyncPainCompanion(string tiedStatusID)
    {
        if (string.IsNullOrEmpty(tiedStatusID)) return;
        string companionID = "chara_status_pain_" + tiedStatusID;
        if (scr_System_Serializer.current.GetByNameOrID_Status_Base(companionID) == null) return;

        var tied = FindStatusByExactID(tiedStatusID);
        if (tied == null)
        {
            if (FindStatusByExactID(companionID) != null) RemoveStatusByExactID(companionID);
            return;
        }
        SetStatusSeverity(companionID, Math.Abs(tied.Severity));
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
        NotifyStatModsChanged();
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

            NotifyStatModsChanged();
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

