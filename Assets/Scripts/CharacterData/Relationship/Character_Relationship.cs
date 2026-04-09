using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

public enum RelationshipScoreType
{
    None = -1,
    Trust,
    Goodwill,
    Desire,
    Badwill,
    Fear
}
public enum RelationshipObedienceType
{
    Rebellious,
    Disobedient,
    Normal,
    Obedient,
    Submissive,
    Total
}

public enum RelationshipAttitudeType
{
    Aversion,
    Dislike,
    Neutral,
    Like,
    Love,
    Favorite
}

public enum PersonalityScoreType
{

}

public class Character_Relationship
{
    /// <summary>
    /// read relationshipscores from math abs
    /// </summary>
    /// <param name="rel"></param>
    public void MergeWith(Character_Relationship rel)
    {
        for (int i = 0; i < relationshipScores.Length; i++)
        {
            if (Math.Abs(relationshipScores[i]) < Math.Abs(rel.relationshipScores[i]))
            {
                relationshipScores[i] = rel.relationshipScores[i];
            }
        }
    }


    Stat_Modifier stressMod = null, moodMod = null;//, lustMod = null;
    bool initialized_moodlet = false;

    public List<Stat_Modifier> GetMoodletModifiers()
    {
        List<Stat_Modifier> results = new List<Stat_Modifier>(3);
        if (!initialized_moodlet)
        {
            stressMod = new Stat_Modifier();
            stressMod.ModString = Target.FirstName;
            stressMod.type = Stat_Modifier.StatMod_Type.addBase;
            stressMod.statID = "chara_status_stress";

            moodMod = new Stat_Modifier();
            moodMod.ModString = Target.FirstName;
            moodMod.type = Stat_Modifier.StatMod_Type.addBase;
            moodMod.statID = "chara_status_mood";

            // lustMod = new Stat_Modifier();
            // lustMod.ModString = Owner.FirstName;
            // lustMod.statID = "chara_status_lust";

            initialized_moodlet = true;
        }
        var tf = Trust_Raw - Fear_Raw;
        if (tf >= 25 || tf <= -25)
        {// trust > fear + 50
            stressMod.SetValueTypeAndString(Stat_Modifier_Type.number, tf >= 25 ? $"{1}" : $"-{1}");
            results.Add(stressMod);
        }

        var gb = Goodwill_Raw - Badwill_Raw;
        if (gb >= 25 || gb <= -25)
        {
            moodMod.SetValueTypeAndString(Stat_Modifier_Type.number, gb >= 25 ? $"{1}" : $"-{1}");
            results.Add(moodMod);

        }

        return results;
    }

    /// <summary>
    /// Wipe cache
    /// </summary>
    public void ConvertToGeneric()
    {
        this.targetRefID = -1;
        this._target = null;
        this.relationshipTypeID_Bio = "";
        _Relationship_Bio = null;
        _Relationship_Personal = null;
        Relationship_Social_Keys.Clear();
        this.relationshipTypeID_Personal = "";
        isA_Bio = false;
        isA_Personal = false;
        //isA_Social = false;
    }

    [JsonProperty] int targetRefID = -1;
    [JsonProperty] string targetBaseID = "";
    public string displayName = "";
    [JsonIgnore] public bool displayable { get { return targetRefID != -1 && this.Owner.RefID != targetRefID; } }

    [JsonProperty] protected string currentAttitude = "";
    RelationshipAttitude _currentAttitude = null;
    List<string> _currentAttitudeTooltip = new List<string>();
    [JsonIgnore] public List<string> CurrentAttitudeTooltip
    { get
        {
            if (scr_System_CampaignManager.current.DebugMode && _currentAttitudeTooltip.Count < 1)
            {
                var tooltip = _currentAttitudeTooltip;
                tooltip.Clear();
                int pos = (int)(Goodwill / 50);
                int neg = (int)(Badwill / 50);
                int des = (int)(Desire / 50);
                //  float trustDiv = Math.Abs(Owner.Stats.Mood.Severity < 0 ? Owner.Stats.Mood.Severity : 0);
                int trustLevel = (int)(Trust / 50);
                int fearLevel = (int)(Fear / 50);
                int baseline = (int)Owner.Stats.GetStatValue("stats_derived_baselineObedience");

                if (scr_System_CampaignManager.current.DebugMode)
                {
                    tooltip.Add("neutral[" + (int)RelationshipAttitudeType.Neutral + "] + goodwill[" + pos + "] + badwill[" + neg + "] + desire[" + des + "]");
                    tooltip.Add("Goodwill:" + Goodwill_Raw.ToString("N1") + "|" + Goodwill_Mult.ToString("N1") + "|" + Goodwill.ToString("N1"));
                    tooltip.Add("Badwill:" + Badwill_Raw.ToString("N1") + "|" + Badwill_Mult.ToString("N1") + "|" + Badwill.ToString("N1"));
                    tooltip.Add("Fear:" + Fear_Raw.ToString("N1") + "|" + Fear_Mult.ToString("N1") + "|" + Fear.ToString("N1"));
                    if (!scr_System_CentralControl.current.isSafeMode)
                    {
                        tooltip.Add("Desire:" + Desire_Raw.ToString("N1") + "|" + Desire_Div.ToString("N1") + "|" + Desire_Mult.ToString("N1") + "|" + Desire.ToString("N1"));
                        tooltip.Add("Corruption:" + Manager.Corruption);
                    }

                    tooltip.Add("neutral[" + (int)RelationshipObedienceType.Normal + "] + trust[" + trustLevel + "] + fear[" + fearLevel + $"] + pride [{Manager.CurrentPride}]");
                    tooltip.Add("Trust:" + Trust_Raw.ToString("N1") + "|" + Trust.ToString("N1"));
                    tooltip.Add("Fear:" + Fear_Raw.ToString("N1") + "|" + Fear_Mult.ToString("N1") + "|" + Fear.ToString("N1"));
                    tooltip.Add($"Pride: {Manager.CurrentPride} mult {Manager.CurrentPrideMod}");
                    tooltip.Add("Baseline:" + baseline.ToString("N1"));
                }
            }else if (!scr_System_CampaignManager.current.DebugMode)
            {
                _currentAttitudeTooltip.Clear();
            }
            return _currentAttitudeTooltip;
        } }


    public class ModifiersPackage
    {
        EvaluationPackage.Modifiers isdoer_isthreat = null;
        EvaluationPackage.Modifiers notdoer_isthreat = null;
        EvaluationPackage.Modifiers isdoer_notthreat = null;
        EvaluationPackage.Modifiers notdoer_notthreat = null;

        public void Clear()
        {
            if (isdoer_isthreat != null) isdoer_isthreat.Reset();
            if (notdoer_isthreat != null) notdoer_isthreat.Reset();
            if (isdoer_notthreat != null) isdoer_notthreat.Reset();
            if (notdoer_notthreat != null) notdoer_notthreat.Reset();
        }

        public EvaluationPackage.Modifiers GetModifier(COM com, bool isdoer, bool isthreat)
        {
            if (isdoer)
            {
                if (isthreat)
                {
                    if (isdoer_isthreat == null) isdoer_isthreat = new EvaluationPackage.Modifiers(com, true, true);
                    return isdoer_isthreat;
                }
                else
                {
                    if (isdoer_notthreat == null) isdoer_notthreat = new EvaluationPackage.Modifiers(com, true, false);
                    return isdoer_notthreat;
                }
            }
            else
            {
                if (isthreat)
                {
                    if (notdoer_isthreat == null) notdoer_isthreat = new EvaluationPackage.Modifiers(com, false, true);
                    return notdoer_isthreat;
                }
                else
                {
                    if (notdoer_notthreat == null) notdoer_notthreat = new EvaluationPackage.Modifiers(com, false,false);
                    return notdoer_notthreat;
                }
            }
        }
    }


    Dictionary<COM, ModifiersPackage> cachedPackages = new Dictionary<COM, ModifiersPackage>();

    public EvaluationPackage.Modifiers GetEPWillingness(COM com, bool isdoer, bool isthreat)
    {
        if (!cachedPackages.ContainsKey(com))
        {
            cachedPackages.Add(com, new ModifiersPackage());
        }
        return cachedPackages[com].GetModifier(com, isdoer, isthreat);        
    }

    public RelationshipAttitude GetCurrentAttitude(bool forceRefresh = false)
    {
        if (forceRefresh || _currentAttitude == null)
        {
            if (currentAttitude == "") UpdateAttitude(forceRefresh, true);
            else if (forceRefresh) UpdateAttitude(forceRefresh, false);
            else SetCurrentAttitude(scr_System_Serializer.current.MasterList.Character_RelationshipAttitudes.GetByID(currentAttitude), true);
        }
        return _currentAttitude;
    }

    /// <summary>
    /// Contains attitude change event trigger
    /// </summary>
    /// <param name="value"></param>
    public void SetCurrentAttitude(RelationshipAttitude value, bool silent)
    {
        if (_currentAttitude == value) return;
        else if (value == null) return;
        else
        {
            if (scr_System_CentralControl.current.LogPrefs.DLog_Attitude) Debug.Log($"{Owner.CallName} Changing attitude toward {Target.CallName} to {value.DisplayName}. isSilent? {silent}");
            if (!silent && Owner.RefID != 0)
            {
                bool visible = scr_System_CampaignManager.current.isCharaVisibleToPlayer(Owner.RefID);
                bool recording = Owner.CurrentRoom != null && Owner.CurrentRoom.HasRecording;
                
                if (!visible && !recording)
                {

                }
                else if (_currentAttitude != null)
                {

                    var s = LocalizeDictionary.QueryThenParse("event_AttitudeChange_string")
                        .Replace("$self.name$", Owner.CallName)
                        .Replace("$target.name$", Target.CallName)
                        .Replace("$originalAttitude$", _currentAttitude.DisplayName)
                        .Replace("$newAttitude$", value.DisplayName);

                    var desc = new DescriptionCollector(s, this);
                    scr_UpdateHandler.current.AppendMessageAfter(desc, Owner.CurrentRoom);
                    /*
                    if (visible) scr_UpdateHandler.current.AppendMessageAfter(s, false);
                    if (recording) 
                    {
                        Owner.CurrentRoom.NotifyKojoCollect(new DescriptionCollector(s,  this));
                    }*/
                }
            }
            _currentAttitude = value;
            //currentAttitude = "";

            currentAttitude = value.ID;
        }
        _currentAttitudeTooltip.Clear();
    }

    public void ClearEPCache()
    {
        foreach(var package in cachedPackages)
        {
            package.Value.Clear();
        }
        this.cachedPackages.Clear();
    }

    public int RelationshipCooldown = 0;

    Action _callback = null;
    bool _callbackExecute = false;
    /// <summary>
    /// This is used to store relationship update callback. Eventcalls do nothing (since event conflict resolve is much more complicated)
    /// </summary>
    public void PostUpdateCallback()
    {
        if (scr_System_CentralControl.current.LogPrefs.DLog_Relationships) Debug.Log($"{Owner.CallName} Relationship PostUpdateCallback, existCallback? {_callback != null}");
        if (!_callbackExecute && _callback != null)
        {
            _callbackExecute = true;
            _callback.Invoke();
            _callback = null;
            _callbackExecute = false;
        }
        ClearEPCache();
    }

    protected void CheckMaintainRelationship()
    {
        if (_currentAttitude == null) return;
        if (RelationshipCooldown > 0) return;
        if (this.Relationship_Personal != null && this.Relationship_Personal.CanMaintain(this, !isA_Personal))
        {
            if (this.Relationship_Personal.CanUpgradeInto(this, out var relation, out var isA))
            {
                Debug.LogError($"{Owner.FirstName} -> {Target.FirstName} CheckMaintainRelationship {this.Relationship_Personal.DisplayName} CanUpgradeInto {relation.DisplayName}");
                RelationshipCooldown = 6;
                TryChangeRelationship(relation, isA);
                
            }
            return;
        }
        else
        {
            if (this.relationshipTypeID_Personal != "") Debug.Log($"{Owner.CallName} Relationship CANNOT MAINTAIN RELATIONSHIP ID {this.relationshipTypeID_Personal}");
            bool isA = false;
            RelationshipType relation = null;
            var reverse = Target.Relationships.FindRelationshipWith(Owner);
            foreach (var rel in scr_System_Serializer.current.MasterList.RelationshipTypes.ProposableRelationships)
            {
                if (rel == this.Relationship_Personal) continue;
                if (rel.canPropose(false) && rel.isValid(this, false) && rel.CanMaintain(reverse, true))
                {
                    // event propose relationship change
                    relation = rel;
                    isA = true;
                    break;
                }
                else if (!rel.isEqualRelationship && rel.canPropose(true) && rel.isValid(this, true) && rel.CanMaintain(reverse, false))
                {
                    // event propose relationship change
                    relation = rel;
                    isA = false;
                    break;
                }
            }

            TryChangeRelationship(relation, isA);
        }
    }

    protected void TryChangeRelationship(RelationshipType relation, bool isA)
    {
        if (relation != null)
        {
            RelationshipCooldown = 6;
            if (relation.requireTargetValidation(!isA))
            {
                Debug.Log($"{Owner.CallName} new Relationship {relation.DisplayName} require validation, setting up event");
                // first log kojo for asking relation
                var eventID = $"{relation.ID}_propose{(relation.isEqualRelationship ? "" : isA ? "_A" : "_B")}";
                //

                // send event to ask permission
                var evinst = new EventInstance(Owner, "RequestRelationshipChange", "");
                evinst.AppendStrings.Add("targetRel", new List<string>() { eventID });
                //LogKojoMessage(eventID,"", evinst.message);
                evinst.Targets.Add("target", new List<Character_Trainable>() { Target });

                var npcCallback = new List<Action>();
                npcCallback.Add(() => { Target.Relationships.FindRelationshipWith(Owner).TryAcceptRelationship(relation, !isA); });
                evinst.FunctionCalls.Add("acceptanceCheck", npcCallback);

                var acceptCallback = new List<Action>();
                acceptCallback.Add(() => { SetPersonalRelationship(relation, isA, true, true); });
                evinst.FunctionCalls.Add("accept", acceptCallback);

                var refuseCallback = new List<Action>();
                refuseCallback.Add(() => Target.Relationships.FindRelationshipWith(Owner).NotifyRefuse(relation, isA));
                evinst.FunctionCalls.Add("refuse", refuseCallback);

                evinst.AppendStrings.Add("original", new List<string>() { this.Relationship_Personal == null ? "-" : Relationship_Personal.GetDisplayName(Owner, !this.isA_Personal) });
                evinst.AppendStrings.Add("new", new List<string>() { relation.GetDisplayName(Owner, !isA) });

                scr_UpdateHandler.current.EventHandler.StartEventAuto(evinst);
            }
            else
            {
                Debug.Log($"{Owner.CallName} new Relationship {relation.DisplayName} does not require validation, changing");
                SetPersonalRelationship(relation, isA, true);
            }
        }
        else
        {
            if (Relationship_Personal == null)
            {   // no change
                RelationshipCooldown = 0;
            }
            else
            {   // break
                RelationshipCooldown = 6;
                //Debug.LogError("Wiping existing relationships ?!");
                SetPersonalRelationship(null, isA, true);
            }
        }
    }


    public void TryAcceptRelationship(RelationshipType relation, bool isA)
    {
        if (relation.isValid(this, !isA))
        {
            SetPersonalRelationship(relation, isA, true, true);
        }
        else
        {
            NotifyRefuse(relation, isA);
        }
    }

    protected void NotifyRefuse(RelationshipType relation, bool isA)
    {
        var refuseID = $"{relation.ID}_refuse{(relation.isEqualRelationship ? "" : isA ? "_A" : "_B")}";
        LogKojoMessage(refuseID);
        Target.Relationships.FindRelationshipWith(Owner).NotifyRefused(relation, !isA);
        var v = Owner.Memory.AddEntryMSG(LocalizeDictionary.QueryThenParse("ui_memory_relationship_refuse")
            .Replace("$target$", Target.FirstName)
            .Replace("$relationship$", relation.GetDisplayName(Owner, !isA)), new List<string>() { "forbidMerge", "important" });
        v.disableRoomName = true;
    }
    protected void NotifyRefused(RelationshipType relation, bool isA)
    {
        var refuseID = $"{relation.ID}_refused{(relation.isEqualRelationship ? "" : isA ? "_A" : "_B")}";
        LogKojoMessage(refuseID);
        var v = Owner.Memory.AddEntryMSG(LocalizeDictionary.QueryThenParse("ui_memory_relationship_refused")
            .Replace("$target$",Target.FirstName)
            .Replace("$relationship$", relation.GetDisplayName(Owner, !isA)), new List<string>() { "forbidMerge", "important" });
        v.disableRoomName = true;
    }

    protected void LogKojoMessage(string s, string suffix = "", MessageCollect m = null)
    {
        if (s == "") return;
        if (Owner.RefID == 0) return;
        var kol = new KojoCollector(Owner, s, "");
        kol.LoadRel(this);
        kol = Owner.Relationships.GetKOJOMessage_Suffix(kol, m);
        //var kojo = Owner.Relationships.Personality.GetKOJOMessage(s, this, new List<string>(), new List<string>());
        if (kol != null)
        {
            if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log($"[{Owner.FirstName}] -> [{Target.FirstName}] get kojomsg for event [{s}]");
            kol.ReplaceString("$self$", Owner.FirstName);
            kol.ReplaceString("$target$", Target.FirstName);
            //kol.message = kojo.message.Replace("$self$", Owner.FirstName).Replace();
           // bool recording = Owner.CurrentRoom != null && Owner.CurrentRoom.HasRecording;
            //bool visible = Owner.RefID == 0 || Target.RefID == 0;

            if (m == null)
            {
                scr_UpdateHandler.current.AppendKojoMessage(kol, Owner.CurrentRoom );
                //scr_UpdateHandler.current.AppendKojoMessage(kojo, visible, recording ? Owner.CurrentRoom : null);
               // if (visible) scr_UpdateHandler.current.FlushCollectedLogs_PreEvents();
            }
            else
            {
                // if m exist, then it means its a event message request
                // in such cases, do not log recording
                m.AddKojo(kol, null);
            }
            
        }
    }
    public bool HasPermission_Follow()
    {
        if (this.Relationship_Bio != null && this.Relationship_Bio.HasPermission_Follow(!isA_Bio)) return true;
        foreach (var key in Relationship_Social_Keys)
        {
            if (tryGetSocialFaction(key, out var rel, out var isa) && rel != null && rel.HasPermission_Follow(!isa)) return true;
        }
        if (this.Relationship_Personal != null && this.Relationship_Personal.HasPermission_Follow(!isA_Personal)) return true;
        if (this.Owner.Relationships.CurrentPride < PrideLevel.High) return true;
        return false;
    }
    public bool HasPermission_Intimacy_Low()
    {
        if (this.Relationship_Bio != null && this.Relationship_Bio.HasPermission_Intimacy_Low(!isA_Bio)) return true;
        foreach (var key in Relationship_Social_Keys)
        {
            if (tryGetSocialFaction(key, out var rel, out var isa) && rel != null && rel.HasPermission_Intimacy_Low(!isa)) return true;
        }
        if (this.Relationship_Personal != null && this.Relationship_Personal.HasPermission_Intimacy_Low(!isA_Personal)) return true;
        if (this.Owner.Relationships.CurrentPride < PrideLevel.High) return true;
        return false;
    }
    public bool HasPermission_Intimacy_Medium()
    {
        if (this.Relationship_Bio != null && this.Relationship_Bio.HasPermission_Intimacy_Medium(!isA_Bio)) return true;
        foreach (var key in Relationship_Social_Keys)
        {
            if (tryGetSocialFaction(key, out var rel, out var isa) && rel != null && rel.HasPermission_Intimacy_Medium(!isa)) return true;
        }
        if (this.Relationship_Personal != null && this.Relationship_Personal.HasPermission_Intimacy_Medium(!isA_Personal)) return true;
        if (this.Owner.Relationships.CurrentPride < PrideLevel.Medium) return true;
        return false;
    }
    public bool HasPermission_Intimacy_High()
    {
        if (this.Relationship_Bio != null && this.Relationship_Bio.HasPermission_Intimacy_High(!isA_Bio)) return true;
        foreach (var key in Relationship_Social_Keys)
        {
            if (tryGetSocialFaction(key, out var rel, out var isa) && rel != null && rel.HasPermission_Intimacy_High(!isa)) return true;
        }
        if (this.Relationship_Personal != null && this.Relationship_Personal.HasPermission_Intimacy_High(!isA_Personal)) return true;
        if (this.Owner.Relationships.CurrentPride < PrideLevel.Low) return true;
        return false;
    }
    public bool HasPermission_Family()
    {
        if (this.Relationship_Bio != null && this.Relationship_Bio.HasPermission_Family(!isA_Bio)) return true;
        foreach (var key in Relationship_Social_Keys)
        {
            if (tryGetSocialFaction(key, out var rel, out var isa) && rel != null && rel.HasPermission_Family(!isa)) return true;
        }
        if (this.Relationship_Personal != null && this.Relationship_Personal.HasPermission_Family(!isA_Personal)) return true;
        if (this.Owner.Relationships.CurrentPride < PrideLevel.Low) return true;
        return false;
    }

    public void SetPersonalRelationship(RelationshipType a, bool isA, bool sendKojo, bool propagate = true)
    {
        var memString = a != null ? LocalizeDictionary.QueryThenParse("ui_memory_relationship_change") : LocalizeDictionary.QueryThenParse("ui_memory_relationship_end");

        memString = memString.Replace("$target$", Target.FirstName);
        memString = a != null ? memString.Replace("$relationship$", a.GetDisplayName(Owner, !isA))
            : memString.Replace("$relationship$", _Relationship_Personal.GetDisplayName(Owner, !isA_Personal));

        if (sendKojo && Owner.RefID != 0)
        {
            //Debug.Log($"{a == null} {relationshipTypeID_Personal == ""}");
            var eventID = a != null ? $"{a.ID}_set{(a.isEqualRelationship ? "" : isA ? "_A" : "_B")}"
                : this.relationshipTypeID_Personal != "" ? $"{this.relationshipTypeID_Personal}_break{(_Relationship_Personal.isEqualRelationship ? "" : isA ? "_A" : "_B")}" : "";

            LogKojoMessage(eventID);
        }

        var desc = new DescriptionCollector($"[{memString}]");
        // do not load rel, cuz it will consider target actor as relevantActor. we dont want that.
        desc.relevantActors.Add(Owner.RefID);
        scr_UpdateHandler.current.AppendMessageAfter(desc, Owner.CurrentRoom, true);

        RelationshipCooldown = 6;

        var v = Owner.Memory.AddEntryMSG(memString, new List<string>() { "forbidMerge","important" });
        v.disableRoomName = true;

        this._Relationship_Personal = a;
        this.relationshipTypeID_Personal = a == null ? "" : a.ID;
        this.isA_Personal = isA;

        OnRelationshioChange();
        if (propagate) this.Target.Relationships.FindRelationshipWith(this.Owner).SetPersonalRelationship(a, !isA, sendKojo, false);
    }

    public RelationshipScoreType MaxScoreType()
    {
        float trust = Trust;
        float fear = Fear;
        float good = Goodwill;
        float bad = Badwill;
        float desire = Desire;

        int maxIndex = 0;
        float maxValue = trust;

        if (fear > maxValue) { maxValue = fear; maxIndex = 1; }
        if (good > maxValue) { maxValue = good; maxIndex = 2; }
        if (bad > maxValue) { maxValue = bad; maxIndex = 3; }
        if (desire > maxValue) { maxValue = desire; maxIndex = 4; }

        return (RelationshipScoreType)maxIndex;
    }

    protected RelationshipAttitude UpdateAttitude(bool forceRefresh = false, bool silent = false)
    {
        if (forceRefresh || _currentAttitude == null || !_currentAttitude.isValidAttitude(this))
        {
            foreach(var i in scr_System_Serializer.current.MasterList.Character_RelationshipAttitudes.list)
            {
                if (_currentAttitude == i)
                {
                    //
                }
                else if (i.MainEmotionKey != RelationshipScoreType.None && this.CurrentEmotionKey != i.MainEmotionKey) continue;

                if (!i.isValidAttitude(this)) continue;

                SetCurrentAttitude(i, silent);// CurrentAttitude = i;
                break;
            }

        }
        ClearEPCache();
        return _currentAttitude;
    }

    public void NotifyFactionChange()
    {
        UpdateSocialFactions();
        OnRelationshioChange();
        ClearEPCache();
    }

    protected void OnRelationshioChange()
    {
        _trustCap_cached = false;
        UpdateAttitude(false, true);
    }


    [JsonIgnore] public RelationshipManager Manager = null;// { get { return _manager; } }
    [JsonIgnore] public Character_Trainable Owner { get { return this.Manager.Owner; } }

    public void ChangePersonalRelationship(string newID, bool isA = false)
    {
        relationshipTypeID_Personal = newID;
        _Relationship_Personal = null;
        this.isA_Personal = isA;
    }
    [JsonProperty] string relationshipTypeID_Bio = "";
    protected RelationshipType _Relationship_Bio = null;
    public bool isA_Bio = false;
    [JsonIgnore]
    public RelationshipType Relationship_Bio
    {
        get
        {
            if (Target == null) return null;
            if (relationshipTypeID_Bio == "") return null;
            if (_Relationship_Bio == null) _Relationship_Bio = scr_System_Serializer.current.MasterList.RelationshipTypes.GetByID(relationshipTypeID_Bio);
            return _Relationship_Bio;
        }
    }

    //protected RelationshipType _Relationship_Social = null;
    //[JsonIgnore] public bool isA_Social = false;

    List<I_IsJobGiver> _relationship_Social_Keys = null;

    [JsonIgnore]
    public
    List<I_IsJobGiver> Relationship_Social_Keys
    {
        get
        {
            if (_relationship_Social_Keys == null)
            {
                UpdateSocialFactions();
            }
            return _relationship_Social_Keys;
        }
    }
    Dictionary<I_IsJobGiver, RelationshipType> rel_per_faction = null;
    Dictionary<I_IsJobGiver, bool> rel_per_faction_isa = null;

    void UpdateSocialFactions()
    {
        _relationship_Social_Keys = new List<I_IsJobGiver>();
        rel_per_faction = new Dictionary<I_IsJobGiver, RelationshipType>();
        rel_per_faction_isa = new Dictionary<I_IsJobGiver, bool>();
        if (targetRefID == -1) return;
        if (Owner.FactionManager.CurrentActiveParty != null)
        {
            var party = Owner.FactionManager.CurrentActiveParty;
            if (party.ManagedChara.Contains(Target) && (party.isPrisoner(Owner) || party.isPrisoner(Target)))
            {
                var relation = party.GetRelationshipBetween(Owner.RefID, Target.RefID, out var social);
                if (relation != null)
                {
                    _relationship_Social_Keys.Add(party);
                    rel_per_faction.Add(party, relation);
                    rel_per_faction_isa.Add(party, social);
                }
            }
        }

        bool added = false;
        foreach(var faction in Owner.FactionManager.Factions)
        {
            if (!faction.isManagedChara(targetRefID)) continue;
            var rel = faction.GetRelationshipBetween(Owner.RefID, targetRefID, out var isa);
            if (rel != null)
            {
                _relationship_Social_Keys.Add(faction);
                rel_per_faction.Add(faction, rel);
                rel_per_faction_isa.Add(faction, isa);
                added = true;
            }
        }

        if (!added && Owner.FactionManager.HomeFactions.Count > 0)
        {
            var home = Owner.FactionManager.HomeFactions[0];
            var rel = relationshipScoresSum >= 100 ? home.Relationship_Acquaintance : home.Relationship_Stranger;
            _relationship_Social_Keys.Add(home);
            rel_per_faction.Add(home, rel);
            rel_per_faction_isa.Add(home, false);
        }
    }
    public bool tryGetSocialFaction(I_IsJobGiver faction, out RelationshipType rel, out bool isA)
    {
        if (rel_per_faction.TryGetValue(faction, out rel))
        {
            if (rel_per_faction_isa.TryGetValue(faction, out isA)) return true;
            isA = false;
            return true;
        }
        else
        {
            isA = false;
            return false;
        }
    }

    [JsonIgnore]
    public int TrustCap { get
        {
            if (!_trustCap_cached)
            {
                UpdateTrustCap();
            }
            return _trustCap;
        } }
    bool _trustCap_cached = false;
    int _trustCap = 0;
    [JsonIgnore]
    public string TrustCap_Tooltip { get
        {
            if (!_trustCap_cached)
            {
                UpdateTrustCap();
            }
            return _trustCap_tooltip;
        } }
    string _trustCap_tooltip = "";
    protected void UpdateTrustCap()
    {
        _trustCap = 25;
        _trustCap_tooltip = LocalizeDictionary.QueryThenParse("relationship_trust_cap_uiDisplay").Replace("$value$", $"{_trustCap}");

        if (Relationship_Bio != null)
        {
            var i = Relationship_Bio.GetTrustCapIncrease(!isA_Bio);
            if (i != 0)
            {
                _trustCap += i;
                _trustCap_tooltip += $"{i.ToString("+0;-#")}{Relationship_Bio.GetDisplayName(Owner, !isA_Bio)}";
            }
        }
        foreach(var key in Relationship_Social_Keys)
        {
            if (tryGetSocialFaction(key, out var rel, out var isa) && rel != null)
            {
                var i = rel.GetTrustCapIncrease(!isa);
                if (i != 0)
                {
                    _trustCap += i;
                    _trustCap_tooltip += $"{i.ToString("+0;-#")}{rel.GetDisplayName(Owner, !isa)}";
                }

            }
        }
        if (Relationship_Personal != null)
        {
            var i = Relationship_Personal.GetTrustCapIncrease(!isA_Personal);
            if (i != 0)
            {
                _trustCap += i;
                _trustCap_tooltip += $"{i.ToString("+0;-#")}{Relationship_Personal.GetDisplayName(Owner, !isA_Personal)}";
            }
        }
        _trustCap_cached = true;
    }


    [JsonProperty] string relationshipTypeID_Personal = "";
    protected RelationshipType _Relationship_Personal = null;
    public bool isA_Personal = false;
    [JsonIgnore]
    public RelationshipType Relationship_Personal
    {
        get
        {
            if (Target == null) return null;
            if (relationshipTypeID_Personal == "") return null;
            if (_Relationship_Personal == null) _Relationship_Personal = scr_System_Serializer.current.MasterList.RelationshipTypes.GetByID(relationshipTypeID_Personal);
            return _Relationship_Personal;
        }
    }

    [JsonIgnore] public int TargetID { get { return targetRefID; } }
    protected Character_Trainable _target = null;
    [JsonIgnore]
    public Character_Trainable Target
    {
        get
        {
            if (_target == null && targetRefID >= 0) _target = scr_System_CampaignManager.current.FindInstanceByID(targetRefID);
            return _target;
        }
    }

    [JsonIgnore] public string TargetBaseID { get { return targetBaseID; } }
    [JsonProperty] float[] relationshipScores = new float[5] { 0f, 0f, 0f, 0f, 0f };
    [JsonIgnore]
    protected int relationshipScoresSum { get
        {
            float sum = 0f;
            foreach (var i in relationshipScores) sum += i;
            return (int)sum;
        } }

    [JsonIgnore]
    public string Debug_RelationshipScores
    {
        get
        {
            return $"| {RelationshipScoreType.Trust} {relationshipScores[(int)RelationshipScoreType.Trust]} | " +
                $"{RelationshipScoreType.Fear} {relationshipScores[(int)RelationshipScoreType.Fear]} |" +
                $"{RelationshipScoreType.Goodwill} {relationshipScores[(int)RelationshipScoreType.Goodwill]} |" +
                $"{RelationshipScoreType.Badwill} {relationshipScores[(int)RelationshipScoreType.Badwill]} |" +
                $"{RelationshipScoreType.Desire} {relationshipScores[(int)RelationshipScoreType.Desire]} |";
        }
    }


    [JsonIgnore]
    public float Trust_Base
    {
        get
        {
            return relationshipScores[(int)RelationshipScoreType.Trust];
        }
    }
    [JsonIgnore]
    public float Trust_Bonus
    {
        get
        {
            var score = 0f
               + (Relationship_Personal == null ? 0 : Relationship_Personal.GetRelModForStat(this.isA_Personal, RelationshipScoreType.Trust))
               + (Relationship_Bio == null ? 0 : Relationship_Bio.GetRelModForStat(this.isA_Bio, RelationshipScoreType.Trust));

            foreach (var key in Relationship_Social_Keys)
            {
                if (tryGetSocialFaction(key, out var rel, out var isa) && rel != null)
                {
                    score += rel.GetRelModForStat(isa, RelationshipScoreType.Trust);
                }
            }

            return score;
        }
    }
    [JsonIgnore]
    public float Trust_Raw
    {
        get
        {
            return Trust_Base + Trust_Bonus;
        }
    }
    [JsonIgnore]
    public float Trust_Mult
    {
        get
        {
            return (float)Manager.CurrentPrideMod;
        }
    }
    [JsonIgnore]
    public float Trust
    {
        get
        {
            return Trust_Raw * Trust_Mult;
        }
    }


    [JsonIgnore]
    public float Fear_Base
    {
        get
        {
            return relationshipScores[(int)RelationshipScoreType.Fear];
        }
    }
    [JsonIgnore]
    public float Fear_Bonus
    {
        get
        {
            var score = 0f
                + (Relationship_Personal == null ? 0 : Relationship_Personal.GetRelModForStat(this.isA_Personal, RelationshipScoreType.Fear))

                + (Relationship_Bio == null ? 0 : Relationship_Bio.GetRelModForStat(this.isA_Bio, RelationshipScoreType.Fear));

            foreach (var key in Relationship_Social_Keys)
            {
                if (tryGetSocialFaction(key, out var rel, out var isa) && rel != null)
                {
                    score += rel.GetRelModForStat(isa, RelationshipScoreType.Fear);
                }
            }

            return score;
        }
    }

    [JsonIgnore]
    public float Fear_Raw
    {
        get
        {
            return Fear_Base + Fear_Bonus;
        }
    }
    protected float Fear_Mult
    {
        get
        {
            if (Owner.Stats.Stress == null) return 0;
            float fearDiv = (1 - (Owner.Stats.Mood != null && Owner.Stats.Mood.Severity >= 1 ? 1 : 0) - (Owner.Stats.Stress.Severity)) / 2;
            //Math.Max(1, 1 + Math.Abs(Owner.Stats.Mood.Severity >= 1 ? Owner.Stats.Mood.Severity : 0) + Math.Abs(Owner.Stats.Stress.Severity > -1 ? Owner.Stats.Mood.Severity + 1 : 0));
            return fearDiv > 0 ? fearDiv : 0;
        }
    }
    [JsonIgnore]
    public float Fear
    {
        get
        {
            return Fear_Raw * Fear_Mult;
        }
    }

    [JsonIgnore]
    public float Badwill_Base
    {
        get
        {
            return relationshipScores[(int)RelationshipScoreType.Badwill];
        }
    }
    [JsonIgnore]
    public float Badwill_Bonus
    {
        get
        {
            var score = 0f
                + (Relationship_Personal == null ? 0 : Relationship_Personal.GetRelModForStat(this.isA_Personal, RelationshipScoreType.Badwill))
                + (Relationship_Bio == null ? 0 : Relationship_Bio.GetRelModForStat(this.isA_Bio, RelationshipScoreType.Badwill));

            foreach (var key in Relationship_Social_Keys)
            {
                if (tryGetSocialFaction(key, out var rel, out var isa) && rel != null)
                {
                    score += rel.GetRelModForStat(isa, RelationshipScoreType.Badwill);
                }
            }

            return score;
        }
    }
    [JsonIgnore]
    public float Badwill_Raw
    {
        get
        {
            return Badwill_Base + Badwill_Bonus;
        }
    }

    protected float Badwill_Mult
    {
        get
        {
            float negDiv = (2 - (Owner.Stats.Mood == null ? 0 : Owner.Stats.Mood.Severity) + (Owner.Stats.Stress != null && Owner.Stats.Stress.Severity <= -1 ? -Owner.Stats.Stress.Severity : 0)) / 4;
            return negDiv > 0 ? negDiv : 0;
        }
    }
    [JsonIgnore]
    public float Badwill
    {
        get
        {
            var result = Badwill_Raw * Badwill_Mult;
            return result > Fear ? result : 0;
        }
    }
    [JsonIgnore]
    public float Goodwill_Base
    {
        get
        {
            return relationshipScores[(int)RelationshipScoreType.Goodwill];
        }
    }
    [JsonIgnore]
    public float Goodwill_Bonus
    {
        get
        {
            var score = 0f
                + (Relationship_Personal == null ? 0 : Relationship_Personal.GetRelModForStat(this.isA_Personal, RelationshipScoreType.Goodwill))
                + (Relationship_Bio == null ? 0 : Relationship_Bio.GetRelModForStat(this.isA_Bio, RelationshipScoreType.Goodwill));

            foreach (var key in Relationship_Social_Keys)
            {
                if (tryGetSocialFaction(key, out var rel, out var isa) && rel != null)
                {
                    score += rel.GetRelModForStat(isa, RelationshipScoreType.Goodwill);
                }
            }
            return score;
        }
    }
    [JsonIgnore]
    public float Goodwill_Raw
    {
        get
        {
           return Goodwill_Base + Goodwill_Bonus;
        }
    }

    protected float Goodwill_Mult
    {
        get
        {
            float posDiv = (2 + (Owner.Stats.Mood == null ? 0 : Owner.Stats.Mood.Severity) + (Owner.Stats.Stress != null && Owner.Stats.Stress.Severity <= -1 ? -Owner.Stats.Stress.Severity : 0)) / 4;
            return posDiv > 0 ? posDiv : 0;
        }
    }
    [JsonIgnore]
    public float Goodwill
    {
        get
        {
            var result = Goodwill_Raw * Goodwill_Mult;
            return result > Fear ? result : 0;
        }
    }
    [JsonIgnore]
    public float Desire_Base
    {
        get
        {
            return relationshipScores[(int)RelationshipScoreType.Desire];
        }
    }
    [JsonIgnore]
    public float Desire_Bonus
    {
        get
        {
            var score = 0f
                + (Relationship_Personal == null ? 0 : Relationship_Personal.GetRelModForStat(this.isA_Personal, RelationshipScoreType.Desire))
                + (Relationship_Bio == null ? 0 : Relationship_Bio.GetRelModForStat(this.isA_Bio, RelationshipScoreType.Desire));


            foreach (var key in Relationship_Social_Keys)
            {
                if (tryGetSocialFaction(key, out var rel, out var isa) && rel != null)
                {
                    score += rel.GetRelModForStat(isa, RelationshipScoreType.Desire);
                }
            }
            return score;
        }
    }

    [JsonIgnore]
    public float Desire_Raw
    {
        get
        {
            return Desire_Base + Desire_Bonus;
        }
    }

    protected float Desire_Div
    {
        get
        {
            var desireDiv = Math.Max(1, 1 + Manager.RelationshipDesireRank(this));
            return desireDiv;
        }
    }
    protected float Desire_Mult
    {
        get
        {
            if (Owner.Stats.Lust == null) return 0;
            var desireDiv = (Math.Max(0, Owner.Stats.Lust.Severity) + 1) / 2;
            return desireDiv;
        }
    }

    [JsonIgnore] public float Desire { get { return Desire_Raw / Desire_Div * Desire_Mult; } }

    [JsonIgnore] public string TargetName { get { return this.displayName != "" ? LocalizeDictionary.QueryThenParse(this.displayName) : Target != null ? Target.FirstName : "missing"; } }


    public Character_Relationship()
    {

    }

    public string relationText = "";
    public void ReEstablishParent(RelationshipManager manager)
    {
        this.Manager = manager;
        relationText = LocalizeDictionary.QueryThenParse("UI_chara_relationship_text");
    }

    public void ResetAttitude()
    {
        _currentAttitude = null;
        currentAttitude = "";
    }

    public bool HasRelationship(I_IsJobGiver sourceFaction, RelationshipType rel, bool isA)
    {
        if (this.Relationship_Bio == rel && this.isA_Bio == isA) return true;
        else if (this.Relationship_Personal == rel && this.isA_Personal == isA) return true;
        else if (sourceFaction != null && tryGetSocialFaction(sourceFaction, out var oldrel, out var oldA) && oldrel == rel && oldA == isA) return true;
        else return false;
    }
    public void PostReloadUpdate()
    {
        UpdateAttitude(false, true);
    }

    public Character_Relationship(RelationshipManager manager, Character_Trainable target, RelationshipManager.presetRelationship template, string overrideCallName = "", string forceBaseID = "")
    {
        ReEstablishParent(manager);

        this.targetRefID = target.RefID;
        this.targetBaseID = forceBaseID != "" ? forceBaseID : Target.BaseID;
        this.displayName = overrideCallName != "" ? overrideCallName : "";

        if (template != null)
        {
            if (template.initialBiologicalRelationship != "")
            {
                this.relationshipTypeID_Bio = template.initialBiologicalRelationship;
                this.isA_Bio = template.initialBiologicalRelationship_isA;
            }

            if (template.initialPersonalRelationship != "")
            {
                this.relationshipTypeID_Personal = template.initialPersonalRelationship;
                this.isA_Personal = template.initialPersonalRelationship_isA;
            }
        }
    }

    public RelationshipScoreType CurrentEmotionKey = RelationshipScoreType.Trust;

    public void ModRelationValue(RelationshipScoreType type, float value, bool silent = true)
    {
        if (type == RelationshipScoreType.Goodwill || type == RelationshipScoreType.Badwill)
        {
            relationshipScores[(int)type] = Math.Max(0, relationshipScores[(int)type] + value);
        }
        else if (type == RelationshipScoreType.Trust)
        {
            relationshipScores[(int)type] = Math.Max(relationshipScores[(int)type] + value, TrustCap);
        }
        else
        {
            relationshipScores[(int)type] += value;
        }

        if (value > 0) CurrentEmotionKey = type;

        _currentAttitudeTooltip.Clear();
        UpdateAttitude(true, false);
        if (Target != null)
        {
            //_currentAttitude = null;

            if (!silent && RelationshipCooldown == 0 && !Owner.Stats.isConsciousnessUnconscious)
            {
               // var eventInstance = new EventInstance(this.Owner, "AttitudeChange", "");
              //  eventInstance.Targets.Add("target", new List<Character_Trainable>() { Target });

                scr_UpdateHandler.current.AddEventCallback(CheckMaintainRelationship);

            }
        }
    }

}
