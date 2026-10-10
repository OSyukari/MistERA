using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Newtonsoft.Json;

public class Character_Factions
{
    // Owner Ref
    int ownerRefID = -1;
    Character_Trainable ownerPointer = null;
    [JsonIgnore] public Character_Trainable Owner { get { if (ownerPointer == null) ownerPointer = scr_System_CampaignManager.current.FindInstanceByID(ownerRefID);
            return ownerPointer;
        } }

    //----------------
    [JsonProperty] string FactionID_Home = "";
    Manageable Faction_Home_Cache = null;
    [JsonIgnore] public Manageable Faction_Home{ get{
        if (Faction_Home_Cache == null && FactionID_Home != "") Faction_Home_Cache = scr_System_CampaignManager.current.FindFactionByID(FactionID_Home);
        return Faction_Home_Cache;
        }
    }

    //----------------
    [JsonProperty] string Faction_Home_Temporary_FactionID = "";
    Manageable Faction_Home_Temporary_Cache = null;
    [JsonIgnore] public Manageable Faction_Home_Temporary { get
        {
            if (Faction_Home_Temporary_Cache == null && Faction_Home_Temporary_FactionID != "") Faction_Home_Temporary_Cache = scr_System_CampaignManager.current.FindFactionByID(Faction_Home_Temporary_FactionID);
            return Faction_Home_Temporary_Cache;
        } }

    //-----------------
    /// <summary>
    /// workFactionID -> ID of the faction whose Management Canvas was used to dispatch this character
    /// into that work faction's job (see button_ScheduleMemberType.OnClickButton). Only populated when
    /// AddWorkFaction is called with an explicit sourceFaction; entries missing here (older saves, or
    /// call sites that don't track dispatch) fall back to HomeFactions[0] - see GetWorkFactionSource.
    /// </summary>
    [JsonProperty] Dictionary<string, string> FactionIDs_WorkSource = new Dictionary<string, string>();
    [JsonProperty] List<string> FactionIDs_Work = new List<string>();
    /// <summary>
    /// Work factions whose MemberType shift this character currently doesn't attend (toggled off in the Management
    /// external job tab): still a member there (fees, access, commands), but the shift gives no schedule - see
    /// Manageable.GetMemberTypeSchedule. Only the MemberType shift is paused, never hours assigned through charaSchedules.
    /// </summary>
    [JsonProperty] List<string> FactionIDs_WorkPaused = new List<string>();
    List<Manageable> Factions_Work_Cache = null;
    [JsonIgnore] public List<Manageable> Factions_Work{get
        {
            if (Factions_Work_Cache == null && FactionIDs_Work != null)
            {
                Factions_Work_Cache = new List<Manageable>();
                foreach(var i in FactionIDs_Work) Factions_Work_Cache.Add(scr_System_CampaignManager.current.FindFactionByID(i));
            }
            return Factions_Work_Cache;
        }
    }
    //--------------------------

    /// <summary>
    /// Recreation memberships (gym, salon, cram school...) - a third membership kind next to home and work, kept in
    /// its own list so nothing that walks WorkFactions (job schedule priority, salary/strike, worker pools, the
    /// work-post join dialogue) ever sees them. Held MemberTypes have no fixed hours: their visits are booked day by
    /// day. A faction is in at most one of this character's lists (a faction holds one MemberType per character):
    /// AddRecreationFaction refuses a home/work faction, and becoming home/work at a recreation faction drops it here.
    /// </summary>
    [JsonProperty] List<string> FactionIDs_Recreation = new List<string>();
    List<Manageable> Factions_Recreation_Cache = null;
    [JsonIgnore] public List<Manageable> RecreationFactions { get
        {
            if (FactionIDs_Recreation == null) FactionIDs_Recreation = new List<string>();
            if (Factions_Recreation_Cache == null)
            {
                Factions_Recreation_Cache = new List<Manageable>();
                foreach (var i in FactionIDs_Recreation) Factions_Recreation_Cache.Add(scr_System_CampaignManager.current.FindFactionByID(i));
            }
            return Factions_Recreation_Cache;
        }
    }
    //--------------------------

    //--------------------------

    List<Manageable> _homefactions = null;
    /// <summary>
    /// PRIORITY LISTING, FROM MOST PRIORITY TO LEAST
    /// </summary>
    [JsonIgnore] public List<Manageable> HomeFactions { get
        {
            if (_homefactions == null)
            {
                _homefactions = new List<Manageable>();
                if (Faction_Home_Temporary != null) _homefactions.Add(Faction_Home_Temporary);
                if (Faction_Home != null) _homefactions.Add(Faction_Home);
            }

            return _homefactions;
        } }
    [JsonIgnore] public List<Manageable> WorkFactions { get { return Factions_Work; } }



    public Character_Factions()
    {

    }

    public void ReEstablishParentData(Character_Trainable owner)
    {
        if (this.ownerPointer == null && owner != null)
        {
            this.ownerPointer = owner;
            this.ownerRefID = owner.RefID;
        }
    }

    /// <summary>
    /// if factionID is empty, then create faction with character name
    /// </summary>
    /// <param name="homeFactionID"></param>
    public void SetHomeFaction(string homeFactionID, MemberType status, bool sendEvent = true)
    {
        if (homeFactionID != FactionID_Home)
        {
            if (Faction_Home != null)
            {
                Faction_Home.RemoveFromFaction(Owner);
                if (Owner == null) Debug.LogError($"Error SetHomeFaction Owner Null on [{ownerRefID}]");
            }
            this.FactionID_Home = homeFactionID;
            this.Faction_Home_Cache = null;   // else Faction_Home below still returns the old faction
            DropRecreationEntry(homeFactionID);
        }
        //Debug.Log("SetHomeFaction called on " + Owner.FirstName + " with arguments homeFactionID["+ homeFactionID+ "] isManager["+isManager+"]");
        if (this.Faction_Home != null)
        {
            Faction_Home.AddToFaction(Owner, status, sendEvent);
            if (this.Owner.isTemporaryActor && Faction_Home.isPlayerRelatedFaction) this.Owner.isTemporaryActor = false;
        }

        UpdateFactionPriorityList();
    }


    /// <summary>
    /// if factionID is empty, set to null
    /// </summary>
    /// <param name="tempFactionID"></param>
    public void SetTempHomeFaction(string tempFactionID, MemberType status, bool sendEvent = true)
    {
        if (tempFactionID != Faction_Home_Temporary_FactionID)
        {
            if (Faction_Home_Temporary != null) Faction_Home_Temporary.RemoveFromFaction(Owner);
            this.Faction_Home_Temporary_FactionID = tempFactionID;
            this.Faction_Home_Temporary_Cache = null;   // else Faction_Home_Temporary below still returns the old faction
            DropRecreationEntry(tempFactionID);
        }

        if (Faction_Home_Temporary != null && status != null)
        {
            Faction_Home_Temporary.AddToFaction(Owner, status, sendEvent);
            if (this.Owner.isTemporaryActor && Faction_Home_Temporary.isPlayerRelatedFaction) this.Owner.isTemporaryActor = false;
        }
        UpdateFactionPriorityList();

        // the permanent home is told its member was taken by / returned from a temp home (e.g. a fallback worker pool wakes them)
        Faction_Home?.OnMemberTempHomeChanged(Owner);
    }

    /// <summary>
    /// Faction responsible for this character's daily need check/consumption today: the active party's
    /// root faction when on a non-locked active party, otherwise HomeFactions[0]. Null while party-locked
    /// (kidnapped characters are exempt) or with no home faction. Single source of truth shared by
    /// FlagForDailyNeed (registration), DailyNeedConsumption (query/report), and
    /// Manageable.GetMaintenanceCost_Chara (the per-character ownership filter) - keeping all three in sync.
    /// </summary>
    [JsonIgnore]
    public Manageable DailyNeedResponsibleFaction
    {
        get
        {
            if (this.isPartyLocked) return null;
            if (this.CurrentActiveParty != null) return this.CurrentActiveParty.FactionOwnerRoot;
            return this.HomeFactions.Count > 0 ? this.HomeFactions[0] : null;
        }
    }

    public void FlagForDailyNeed()
    {
        this.DailyNeedResponsibleFaction?.RegisterForResourceConsumption(Owner.RefID);
    }



    public void DailyNeedConsumption()
    {
        bool returnValue = true;
        Manageable home = this.DailyNeedResponsibleFaction;

        if (home != null && home.isPlayerFaction && home.WasDailyNeedCovered(Owner.RefID))
        {
            List<string> failedTags = new List<string>();
            foreach(var v in Owner.Stats.Needs)
            {
                var v2 = home.QueryDailyCharaMaintenanceResult(v.consumeItemByTag);
                if (!v2) failedTags.Add(v.consumeItemByTag);
                if (!v2 && v.statusDebuffID != "")
                {   // add status debuff
                    Owner.Stats.AddOrModStatus(v.statusDebuffID, 1441, 1441);
                    var debuff = Owner.Stats.FindStatusByExactID(v.statusDebuffID);
                    home.DailyReport.AddManageReport(LocalizeDictionary.QueryThenParse("ui_management_overview_daily_debuff")
                        .Replace("$resource$", LocalizeDictionary.QueryThenParse(v.consumeItemByTag))
                        .Replace("$name$", Owner.FirstName)
                        .Replace("$status$", debuff != null ? debuff.SeverityDisplayName : v.statusDebuffID), true);
                }
                returnValue = v2 && returnValue;
            }

            // increase relationship
            List<Character_Trainable> trustManagers = new List<Character_Trainable>();
            var scoreinc = returnValue ? 1 : -1;
            foreach (var manager in home.Managers)
            {
                if (Owner.RefID == manager.RefID) continue;

                trustManagers.Add(manager);
                Owner.Relationships.IncreaseRelationshipWith(manager.RefID, RelationshipScoreType.Trust, scoreinc);// FindRelationshipWith(manager.RefID).ModRelationValue(RelationshipScoreType.Trust, 1);

                var s = LocalizeDictionary.QueryThenParse("ui_management_overview_daily_trust")
                    .Replace("$name$", Owner.FirstName)
                    .Replace("$leader$", manager.FirstName)
                    .Replace("$score$", LocalizeDictionary.QueryThenParse("relationship_trust"))
                    .Replace("$count$", scoreinc.ToString("+0;-#"));

                home.DailyReport.AddManageReport(s, !returnValue);

            }

            // home bundles every member's failure into one OnDailyNeedsFailed alert at day update 3
            if (failedTags.Count > 0) home.RecordDailyNeedFailure(Owner, failedTags, trustManagers, scoreinc);
        }
        // else, no home faction, or home did not consume for this chara today (absent / responsibility changed since registration), dont check it.
    }

    /// <summary>
    /// For each party chara is in, check if party active and should apply.
    /// <br/>
    /// If chara currently has a work schedule, return work schedule location
    /// else return home faction
    /// </summary>
    [JsonIgnore]
    public Manageable CurrentlyActiveFaction
    {
        get
        {
            var faction = CurrentJobScheduleFaction();
            return faction != null ? faction : HomeFactions.Count > 0 ? HomeFactions[0] : null;
        }
    }


    [JsonProperty] string activePartyID = "";
    [JsonProperty] string activePartyOwnerID = "";
    Manageable_Party _party = null;


    [JsonIgnore]
    public Manageable_Party CurrentParty
    {
        get
        {
            if (_party == null && activePartyID != "" && activePartyOwnerID != "")
            {
                _party = scr_System_CampaignManager.current.FindFactionByID(activePartyOwnerID).GetParty(activePartyID);
            }
            return _party;
        }
        set
        {
            _party = value;
            activePartyID = _party == null ? "" : _party.ID;
            activePartyOwnerID = _party == null ? "" : _party.OwnerFaction.ID;
        }
    }

    [JsonIgnore]
    public Manageable_Party CurrentActiveParty
    {
        get
        {
            if (this.CurrentLockedParty != null) return this.CurrentLockedParty;
            else if (this.CurrentParty != null && (this.CurrentParty.isActive || !this.CurrentParty.isPlayerFaction)) return this.CurrentParty;
            return null;
        }
    }
    [JsonIgnore]
    public bool isPartyLocked { get { return this.CurrentLockedParty != null; } }

    [JsonProperty] string lockedPartyID = "";
    [JsonProperty] string lockedPartyOwnerID = "";
    Manageable_Party _lockedparty = null;

    [JsonIgnore]
    public Manageable_Party CurrentLockedParty
    {
        get
        {
            if (_lockedparty == null && lockedPartyID != "" && lockedPartyOwnerID != "")
            {
                _lockedparty = scr_System_CampaignManager.current.FindFactionByID(lockedPartyOwnerID).GetParty(lockedPartyID);
            }
            return _lockedparty;
        }
        set
        {
            _lockedparty = value;
            lockedPartyID = _lockedparty == null ? "" : _lockedparty.ID;
            lockedPartyOwnerID = _lockedparty == null ? "" : _lockedparty.OwnerFaction.ID;
        }
    }

    [JsonIgnore]
    public I_IsJobGiver CurrentLocaleFaction
    { get
        {
            var room = scr_System_CampaignManager.current.GetCharaRoomInstance(Owner.RefID);
            return room == null ? null : room.FactionOwner as I_IsJobGiver;
        } }

    [JsonIgnore]
    public string CurrentlyActiveFactionStatus
    {
        get
        {
            if (CurrentlyActiveFaction == null) return "";
            return CurrentlyActiveFaction.GetCharaSocialStandingName(Owner.RefID);
        }
    }
    [JsonIgnore]
    public string CurrentlyActiveFactionTooltip
    {
        get
        {
            return Owner.CurrentActiveFactionName;
        }
    }

    /// <summary>
    /// The character's current MemberType, preferring an active party over the active faction
    /// (mirrors Utility.GetActorTag's MemberType Tags precedence). Null if neither is present.
    /// </summary>
    [JsonIgnore]
    public MemberType CurrentActiveMemberType
    {
        get
        {
            var party = CurrentActiveParty;
            if (party != null) return party.GetMemberType(Owner);
            var faction = CurrentlyActiveFaction;
            return faction != null ? GetScheduledMemberType(faction) : null;
        }
    }
    [JsonIgnore]
    public MemberType CurrentLocaleMemberType
    {
        get
        {
            var faction = CurrentLocaleFaction;
            return faction != null ? faction.GetMemberType(Owner) : null;
        }
    }
    /// <summary>
    /// True if this character currently holds MemberType memberTypeID in any faction they belong to
    /// (HomeFactions + WorkFactions + RecreationFactions), regardless of which faction/party is currently active. Does not
    /// check party membership (parties are tracked separately via TrackedPartyRef) since MemberType
    /// authorship for this kind of check is expected to be faction-scoped (e.g. a job post).
    /// </summary>
    public bool HasMemberTypeInAnyFaction(string memberTypeID)
    {
        if (string.IsNullOrEmpty(memberTypeID)) return false;
        foreach (var faction in Factions)
            if (faction != null && MemberType.Matches(faction.GetMemberType(Owner), memberTypeID)) return true;
        return false;
    }

    /// <summary>
    /// Party-aware counterpart of CurrentlyActiveFaction: the active party (see CurrentActiveParty) if any,
    /// else the currently active faction. Same precedence as CurrentActiveMemberType.
    /// </summary>
    [JsonIgnore]
    public I_IsJobGiver CurrentActiveJobGiver
    {
        get
        {
            var party = CurrentActiveParty;
            if (party != null) return party;
            return CurrentlyActiveFaction;
        }
    }

    /// <summary>
    /// Every job giver this character belongs to: home + work + recreation factions, plus every party they are
    /// rostered in (TrackedPartyRef) and the current/locked party.
    /// </summary>
    [JsonIgnore]
    public List<I_IsJobGiver> AllJobGivers
    {
        get
        {
            var list = new List<I_IsJobGiver>(Factions.Count + trackedPartyRef.Count + 2);
            foreach (var f in Factions) if (f != null) list.Add(f);
            foreach (var jobRef in trackedPartyRef)
            {
                var party = (scr_System_CampaignManager.current.FindJobInstanceByID(jobRef) as Job_Expedition)?.FactionOwner_Party;
                if (party != null && !list.Contains(party)) list.Add(party);
            }
            if (CurrentParty != null && !list.Contains(CurrentParty)) list.Add(CurrentParty);
            if (CurrentLockedParty != null && !list.Contains(CurrentLockedParty)) list.Add(CurrentLockedParty);
            return list;
        }
    }

    /// <summary>
    /// True if jobGiver (faction or party) is one of AllJobGivers.
    /// </summary>
    public bool BelongsToJobGiver(I_IsJobGiver jobGiver)
    {
        if (jobGiver == null) return false;
        if (jobGiver is Manageable m) return Factions.Contains(m);
        var party = jobGiver as Manageable_Party;
        if (party == null) return false;
        if (CurrentParty == party || CurrentLockedParty == party) return true;
        return party.Job != null && trackedPartyRef.Contains(party.Job.RefID);
    }

    public void AddWorkFaction(string factionID, MemberType status, bool sendEvent = true, Manageable sourceFaction = null)
    {
        Manageable targetFaction = Factions_Work.Find(x => x.ID == factionID);
        if (targetFaction == null) targetFaction = scr_System_CampaignManager.current.FindFactionByID(factionID);

        if (targetFaction == null) return;
        else
        {
            // hired where they were a recreation member: the work MemberType replaces the membership
            DropRecreationEntry(targetFaction.ID);
            targetFaction.AddToFaction(Owner, status, sendEvent);
            if (!Factions_Work.Contains(targetFaction)) this.Factions_Work.Add(targetFaction);
            if (!FactionIDs_Work.Contains(targetFaction.ID)) this.FactionIDs_Work.Add(targetFaction.ID);
            if (sourceFaction != null) FactionIDs_WorkSource[targetFaction.ID] = sourceFaction.ID;
        }

        UpdateFactionPriorityList();

    }

    public void AddWorkFaction(string factionID, bool isManager = false)
        => AddWorkFaction(factionID, isManager ? FactionUtility.MemberType_Manager : FactionUtility.MemberType_Member);

    /// <summary>
    /// The faction whose Management Canvas dispatched this character into workFactionID's job (see
    /// AddWorkFaction's sourceFaction param), if explicitly tracked - otherwise null. Most callers want
    /// GetWorkFactionSourceOrDefault instead, which folds in the HomeFactions[0] fallback; this raw
    /// accessor exists for callers (e.g. UI) that need to distinguish "explicitly overridden" from
    /// "defaulted to home".
    /// </summary>
    protected Manageable GetWorkFactionSource(string workFactionID)
    {
        if (FactionIDs_WorkSource != null && FactionIDs_WorkSource.TryGetValue(workFactionID, out var sourceID))
            return scr_System_CampaignManager.current.FindFactionByID(sourceID);
        return null;
    }

    /// <summary>
    /// The faction that should be treated as the source for workFactionID's obligations (salary,
    /// membership fees, etc.): the explicitly tracked dispatcher (GetWorkFactionSource) if any, otherwise
    /// HomeFactions[0]. Every work faction assignment resolves to *some* source through this fallback, so
    /// every caller that needs "who does this job's obligations belong to" should go through here rather
    /// than re-deriving the HomeFactions[0] fallback itself.
    /// </summary>
    public Manageable GetWorkFactionSourceOrDefault(string workFactionID)
    {
        return GetWorkFactionSource(workFactionID) ?? (HomeFactions.Count > 0 ? HomeFactions[0] : null);
    }

    public void PrioritizeWorkFaction(string factionID)
    {
        if (!FactionIDs_Work.Contains(factionID)) return;
        FactionIDs_Work.Remove(factionID);
        FactionIDs_Work.Insert(0, factionID);
        UpdateFactionPriorityList();
    }
    [JsonProperty] List<int> trackedPartyRef = new List<int>();


    public bool AddToPartyAsTemp(I_IsJobGiver party, MemberType status, MemberType homeStatus, bool isLock = false)
    {
        var p = party as Manageable_Party;
        if (p == null) return false;

        return AddToPartyAsTemp(p, status, homeStatus, isLock);
    }
    public bool AddToPartyAsTemp(Manageable_Party party, MemberType status, MemberType homeStatus, bool isLock = false)
    {
        //if (this.CurrentActiveParty != null && this.CurrentActiveParty != party) return false;

        if (isLock)
        {
            if (this.CurrentLockedParty != null && this.CurrentLockedParty != party)
            {
                this.CurrentLockedParty.NotifyCharaKidnapped(this.Owner, party);
                this.CurrentLockedParty.RemoveFromFaction(this.Owner);
            }
            if (this.CurrentParty != null) this.CurrentParty.NotifyCharaKidnapped(this.Owner, party);

            this.CurrentLockedParty = party;
        }
        else
        {
            this.CurrentParty = party;
        }

        party.AddToFaction(Owner, status, true);


        if (Faction_Home == null) SetHomeFaction(party.OwnerFaction.ID, homeStatus, false);
        else SetTempHomeFaction(party.OwnerFaction.ID, homeStatus, false);

        AddPartyTracker(party);

        UpdateFactionPriorityList();
        return true;
    }

    public bool AddToParty(I_IsJobGiver party, MemberType status, bool setHomeFaction, bool isLock = false)
    {
        if (party is Manageable_Party) return AddToParty(party as Manageable_Party, status, setHomeFaction, isLock);
        else return false;
    }

    public bool AddToParty(Manageable_Party party, MemberType status, bool setHomeFaction, bool isLock = false)
    {
        //if (this.CurrentActiveParty != null && this.CurrentActiveParty != party) return false;

        if (isLock)
        {
            if (this.CurrentLockedParty != null && this.CurrentLockedParty != party)
            {
                this.CurrentLockedParty.NotifyCharaKidnapped(this.Owner, party);
                this.CurrentLockedParty.RemoveFromFaction(this.Owner);
            }
            if (this.CurrentParty != null) this.CurrentParty.NotifyCharaKidnapped(this.Owner, party);

            this.CurrentLockedParty = party;
        }
        else
        {
            if (this.CurrentParty != null && this.CurrentParty != party)
            {
                Debug.LogError($"Error AddToParty, [{Owner.FirstName}] already assigned to [{this.CurrentParty.FullFactionDisplayName}], cannot join [{party.FullFactionDisplayName}]");
                return false;
            }
            else this.CurrentParty = party;
        }

        party.AddToFaction(Owner, status, true);

        if (setHomeFaction)
        {
            if (Faction_Home == null) SetHomeFaction(party.OwnerFaction.ID, status, false);
            else SetTempHomeFaction(party.OwnerFaction.ID, status, false);
        }
        
        AddPartyTracker(party);

        UpdateFactionPriorityList();
        return true;
    }
    /// <summary>
    /// Only wipe the CurrentActiveParty if match
    /// </summary>
    /// <param name="party"></param>
    /// <param name="forceRemove">allow removing anyg CurrentParty</param>
    /// <param name="unlock">allow removing anything LockedParty</param>
    public void RemoveFromParty(Manageable_Party party, bool forceRemove = false, bool unlock = false)
    {
        if (this.CurrentLockedParty == party || unlock)
        {
            var p = this.CurrentLockedParty;
            this.CurrentLockedParty = null;
            if (p != null) p.RemoveFromFaction(Owner);
        }
        if (this.CurrentParty == party || forceRemove) this.CurrentParty = null;
       
        UpdateFactionPriorityList();
    }
    /// <summary>
    /// Only wipe the CurrentActiveParty if match
    /// </summary>
    /// <param name="party"></param>
    public void RemoveFromParty(I_IsJobGiver party)
    {
        var p = party as Manageable_Party;
        if (p == null) return;

        RemoveFromParty(p);
    }


    public void AddPartyTracker(Manageable_Party party)
    {
        if (!this.trackedPartyRef.Contains(party.Job.RefID)) trackedPartyRef.Add(party.Job.RefID);
    }
    public void RemovePartyTracker(Manageable_Party party)
    {
        if (this.CurrentParty == party) this.CurrentParty = null;
        trackedPartyRef.Remove(party.Job.RefID);
    }

    /// <summary>
    /// Job.RefID of every party this character is currently rostered in (roster membership,
    /// not just the active/locked party) - kept in sync by AddPartyTracker/RemovePartyTracker
    /// on both the UI roster-edit path (Manageable_Party.AddToFaction/RemoveFromFaction) and
    /// the gathering-join path (AddToParty/RemoveFromParty). See
    /// FactionUtility.TryGetPartyGatheringOverride.
    /// </summary>
    [JsonIgnore] public List<int> TrackedPartyRef { get { return trackedPartyRef; } }


    /// <summary>
    /// Setting a command requires sourceFaction to actually be one of the character's own factions
    /// (home or work) - unsetting (selectedCOM == null) is always allowed.
    /// </summary>
    /// <param name="sourceFaction"></param>
    /// <param name="hour"></param>
    /// <param name="selectedCOM"></param>
    public void SetSchedule(Manageable sourceFaction, int hour, COM selectedCOM)
    {
        //string message = "";

        if (selectedCOM != null && !HomeFactions.Contains(sourceFaction) && !WorkFactions.Contains(sourceFaction))
        {
            Debug.LogError($"setschedule single target {sourceFaction.FactionDisplayName} not in home or work factions, return");
            return;
        }
        sourceFaction.SetWorkHour(Owner, hour, selectedCOM);

        List<string> s = new List<string>();
        UpdateSchedule(ref s);
    }

    /// <summary>
    /// Toggles the customOverride "Sandbox" flag for a single hour on sourceFaction - see
    /// Manageable.SetWorkHourSandbox/HourlySchedule.Sandbox. Sandbox specifically represents being
    /// dispatched to work at sourceFaction, so it only makes sense for a faction the character is
    /// actually employed by (unlike SetSchedule(Manageable, int, COM), which also allows home factions).
    /// </summary>
    public void SetScheduleSandbox(Manageable sourceFaction, int hour, bool sandbox)
    {
        if (sandbox && !WorkFactions.Contains(sourceFaction))
        {
            Debug.LogError($"setschedulesandbox target {sourceFaction.FactionDisplayName} not in workfactions, return");
            return;
        }
        sourceFaction.SetWorkHourSandbox(Owner, hour, sandbox);

        List<string> s = new List<string>();
        UpdateSchedule(ref s);
    }

    /// <summary>
    /// Whether sourceFaction is currently using the player's own charaSchedules entries (comIDs and/or
    /// Sandbox) instead of its shared workModule schedule - see Manageable.GetUseCustomOverride. Scoped
    /// per faction: toggling this for one work faction (e.g. an externalJob) has no effect on any other
    /// faction's own schedule for this character.
    /// </summary>
    public bool GetUseCustomOverride(Manageable sourceFaction)
    {
        return sourceFaction != null && sourceFaction.GetUseCustomOverride(Owner);
    }

    /// <summary>
    /// Flips whether sourceFaction reads this character's schedule from charaSchedules (true) or the
    /// shared workModule (false) - see Manageable.SetUseCustomOverride. Purely a read-path switch:
    /// toggling it back and forth never touches the underlying per-hour data.
    /// </summary>
    public void SetUseCustomOverride(Manageable sourceFaction, bool value)
    {
        if (sourceFaction == null) return;
        sourceFaction.SetUseCustomOverride(Owner, value);

        List<string> s = new List<string>();
        UpdateSchedule(ref s);
    }


    
    /// <summary>
    /// if chara already belong to said faction (eg home faction) apply it directly
    /// if chara does not belong:
    /// - then job setting will be as job faction
    /// - register as job faction and apply
    /// </summary>
    /// <param name="sourceFaction"></param>
    /// <param name="preset"></param>
    public void SetSchedule(Manageable sourceFaction, Manageable.JobPostPreset preset)
    {
        //string message = "";
        if (preset == null || !preset.isActive)
        {
            if (WorkFactions.Contains(sourceFaction)) RemoveWorkFaction(sourceFaction.ID);
        }
        else
        {
            // a recreation member assigned a job post here is hired (AddWorkFaction drops the membership)
            if (!WorkFactions.Contains(sourceFaction) && !HomeFactions.Contains(sourceFaction)) AddWorkFaction(sourceFaction.ID);
            foreach(var hour in preset.activeHours)
            {
                sourceFaction.SetWorkHour(Owner, hour, preset.jobPostID, preset.workCommands);
            }
        }

        List<string> s = new List<string>();
        UpdateSchedule(ref s);
        
        //Debug.Log($"chara {Owner.FirstName} setschedule {preset.jobPostID} for faction {sourceFaction.ID}, {message}");
    }


    public void RemoveWorkFaction(string factionID)
    {
        Manageable targetFaction = Factions_Work.Find(x => x.ID == factionID);
        if (targetFaction == null) return;
        targetFaction.RemoveFromFaction(Owner);
        this.FactionIDs_Work.Remove(targetFaction.ID);
        this.FactionIDs_WorkSource.Remove(targetFaction.ID);
        if (FactionIDs_WorkPaused != null) FactionIDs_WorkPaused.Remove(targetFaction.ID);
        this.Factions_Work.Remove(targetFaction);

        UpdateFactionPriorityList();
    }

    /// <summary>Whether this character's MemberType shift at workFaction is paused (see FactionIDs_WorkPaused).</summary>
    public bool IsWorkPaused(Manageable workFaction)
    {
        return workFaction != null && FactionIDs_WorkPaused != null && FactionIDs_WorkPaused.Contains(workFaction.ID);
    }

    /// <summary>
    /// Pauses (or resumes) attending workFaction's MemberType shift without leaving it - the character stays a member
    /// (fees, access) but the shift stops driving their schedule. Only for one of this character's work factions.
    /// </summary>
    public void SetWorkPaused(Manageable workFaction, bool paused)
    {
        if (workFaction == null) return;
        if (FactionIDs_WorkPaused == null) FactionIDs_WorkPaused = new List<string>();
        if (paused)
        {
            if (!WorkFactions.Contains(workFaction))
            {
                Debug.LogError($"SetWorkPaused target {workFaction.FactionDisplayName} not in workfactions, return");
                return;
            }
            if (!FactionIDs_WorkPaused.Contains(workFaction.ID)) FactionIDs_WorkPaused.Add(workFaction.ID);
        }
        else FactionIDs_WorkPaused.Remove(workFaction.ID);

        List<string> s = new List<string>();
        UpdateSchedule(ref s);
    }

    /// <summary>
    /// Joins factionID as a recreation member (see RecreationFactions). Refused (false) when the faction already is one
    /// of this character's home or work factions - it would overwrite the MemberType held there. Re-joining an existing
    /// membership just changes its MemberType.
    /// </summary>
    public bool AddRecreationFaction(string factionID, MemberType status, bool sendEvent = true)
    {
        var targetFaction = scr_System_CampaignManager.current.FindFactionByID(factionID);
        if (targetFaction == null || status == null) return false;
        if (WorkFactions.Contains(targetFaction) || HomeFactions.Contains(targetFaction))
        {
            Debug.LogWarning($"AddRecreationFaction: [{Owner?.FirstName}] already belongs to [{factionID}] as home/work, membership refused");
            return false;
        }

        targetFaction.AddToFaction(Owner, status, sendEvent);
        if (!FactionIDs_Recreation.Contains(targetFaction.ID)) FactionIDs_Recreation.Add(targetFaction.ID);
        Factions_Recreation_Cache = null;

        UpdateFactionPriorityList();
        return true;
    }

    public void RemoveRecreationFaction(string factionID)
    {
        var targetFaction = RecreationFactions.Find(x => x != null && x.ID == factionID);
        if (targetFaction == null) return;
        targetFaction.RemoveFromFaction(Owner);
        if (FactionIDs_Recreation.Remove(factionID)) Factions_Recreation_Cache = null;
        // visits planned for the membership go with it; an event's visit there doesn't need membership and stays
        CancelBookingsAt(factionID, true);

        UpdateFactionPriorityList();
    }

    /// <summary>
    /// For a faction about to hold this character as home/work instead: if it was a recreation membership, removes it from
    /// the recreation list and cancels the visits booked through it (they ran under the membership's MemberType, which the
    /// new one replaces). Other bookings there (offers, events) stay.
    /// </summary>
    void DropRecreationEntry(string factionID)
    {
        if (FactionIDs_Recreation == null || string.IsNullOrEmpty(factionID)) return;
        if (!FactionIDs_Recreation.Remove(factionID)) return;
        Factions_Recreation_Cache = null;
        CancelBookingsAt(factionID, true);
    }

    //--------------------------
    /// <summary>
    /// Planned recreation visits (see RecreationBooking), dated by absolute day, kept until they end (PruneBookings).
    /// Read through GetEffectiveBooking by CurrentJobScheduleFaction (after work, before home) and the booked faction's
    /// schedule getters. Sleep placement deliberately ignores them (ValidateSchedule), so a booking overlapping sleep
    /// takes those hours and the sleep window measured by Character_Trainable.Sleep comes out shorter.
    /// </summary>
    [JsonProperty] List<RecreationBooking> recreationBookings = new List<RecreationBooking>();
    [JsonIgnore] public IReadOnlyList<RecreationBooking> RecreationBookings { get { return Bookings; } }
    List<RecreationBooking> Bookings { get { if (recreationBookings == null) recreationBookings = new List<RecreationBooking>(); return recreationBookings; } }

    /// <summary>The booking covering hour (-1 = now) daysLookahead days from today, or null.</summary>
    public RecreationBooking GetBookingAt(int hour = -1, int daysLookahead = 0)
    {
        if (recreationBookings == null || recreationBookings.Count == 0) return null;
        if (hour == -1) hour = scr_System_Time.current.getCurrentTime().Hour;
        int day = scr_System_Time.current.getAbsoluteDay(daysLookahead);
        foreach (var b in recreationBookings) if (b != null && b.Covers(day, hour)) return b;
        return null;
    }

    /// <summary>
    /// The booking (never a session booking - those are their session's instance) starting exactly at absolute hour
    /// absStart at factionID: the live list, else the past-booking record (a cancelled visit's job keeps resolving
    /// until it notices). Job_Activity's instance lookup (RecreationUtility.ResolveActivityInstance). Null when none.
    /// </summary>
    public RecreationBooking FindBookingStartedAt(int absStart, string factionID)
    {
        foreach (var b in Bookings)
            if (b != null && !b.isSessionBooking && b.AbsStart == absStart && b.factionID == factionID) return b;
        if (pastRecreationBookings != null)
            foreach (var b in pastRecreationBookings)
                if (b != null && !b.isSessionBooking && b.AbsStart == absStart && b.factionID == factionID) return b;
        return null;
    }

    /// <summary>
    /// Adds booking unless it overlaps another booking or targets a missing faction. Any faction may be booked, this
    /// character's own home/work factions included (GetEffectiveBooking decides when a booking there beats their own
    /// schedule). Refreshes the schedule on success unless refresh is false (batch callers - the planner - refresh once at the end).
    /// </summary>
    public bool AddBooking(RecreationBooking booking, bool refresh = true)
    {
        if (booking == null || booking.hours < 1) return false;
        if (booking.Faction == null) return false;
        foreach (var b in Bookings) if (b != null && b.Overlaps(booking.AbsStart, booking.AbsEnd)) return false;

        if (Owner != null) booking.ownerRef = Owner.RefID;
        Bookings.Add(booking);
        if (refresh) RefreshSchedule();
        return true;
    }

    /// <summary>
    /// Removes booking; if it had not ended yet, starts its onCancelledEventID (if any) on this character and announces
    /// the cancellation (reason - see NotifyBookingCancelled). Refreshes the schedule unless refresh is false (callers
    /// mid-way through a faction change that refreshes it afterwards).
    /// </summary>
    public void CancelBooking(RecreationBooking booking, bool refresh = true, RecreationUtility.CancelReason reason = RecreationUtility.CancelReason.None)
    {
        if (!RemoveBooking(booking)) return;
        RecordPastBooking(booking);
        NotifyBookingCancelled(booking, reason);
        if (refresh) RefreshSchedule();
    }

    /// <summary>Takes booking off the list with no event and no refresh - for the planner moving it (RecreationUtility.TryReschedule).</summary>
    public bool RemoveBooking(RecreationBooking booking)
    {
        return booking != null && Bookings.Remove(booking);
    }

    /// <summary>
    /// A booking was cancelled before it ended: announces it (RecreationUtility.RecordCancelled - the change notice) and
    /// starts its onCancelledEventID (if any) on this character. Nothing for a booking that had already ended.
    /// </summary>
    public void NotifyBookingCancelled(RecreationBooking booking, RecreationUtility.CancelReason reason = RecreationUtility.CancelReason.None)
    {
        if (booking == null || HasBookingEnded(booking)) return;
        // a session booking given up: this character's answer to the session changes (it re-validates on its next hourly update)
        RecreationUtility.OnSessionBookingCancelled(this, booking, reason);
        RecreationUtility.RecordCancelled(this, booking, reason);
        if (!string.IsNullOrEmpty(booking.onCancelledEventID))
            scr_UpdateHandler.current.EventHandler.StartEvent(Owner, booking.onCancelledEventID, "", false);
    }

    public void RefreshSchedule()
    {
        var s = new List<string>();
        UpdateSchedule(ref s);
    }

    /// <summary>The planned night (see plannedSleepStartAbs) as absolute hours [start, end); false when none is planned.</summary>
    public bool TryGetPlannedSleep(out int startAbs, out int endAbs)
    {
        startAbs = plannedSleepStartAbs;
        endAbs = plannedSleepEndAbs;
        return startAbs >= 0 && endAbs > startAbs;
    }

    //--------------------------
    /// <summary>Last absolute day RecreationUtility.DailyPlan has planned this character's bookings up to (-1 = never).</summary>
    [JsonProperty] int lastRecreationPlanDay = -1;
    [JsonIgnore] public int LastRecreationPlanDay { get { return lastRecreationPlanDay; } set { lastRecreationPlanDay = value; } }

    /// <summary>
    /// Characters who refused this character's recreation invitations (RefIDs) - never invited by them again. Not saved;
    /// cleared at the start of this character's daily planning (RecreationUtility.DailyPlan).
    /// </summary>
    [JsonIgnore] protected HashSet<int> inviteRefusedBy = new HashSet<int>();

    public bool HasRefusedInvite(int charaRef) { return inviteRefusedBy != null && inviteRefusedBy.Contains(charaRef); }
    public void AddInviteRefusal(int charaRef) { if (inviteRefusedBy == null) inviteRefusedBy = new HashSet<int>(); inviteRefusedBy.Add(charaRef); }
    public void ClearInviteMemory() { inviteRefusedBy?.Clear(); }

    //-------------------------- recreation sessions (runtime only - rebuilt on load: RecreationUtility.RelinkAfterLoad + recreationDirty)
    /// <summary>Sessions pushed to this character (invited, extended to them, hosted by them) - read in their own ranking pass.</summary>
    [JsonIgnore] readonly List<RecreationGroup> sessionInbox = new List<RecreationGroup>();
    [JsonIgnore] int inboxSerial = 0;
    /// <summary>Inbox serial / faction and world list serials (RecreationGroupRegistry.PostedSerial) as of this character's last ranking pass.</summary>
    [JsonIgnore] int seenInboxSerial = -1;
    [JsonIgnore] readonly Dictionary<string, int> seenListSerials = new Dictionary<string, int>();
    /// <summary>The kept preference order of the sessions this character accepted (RecreationUtility.RankSessions), best first.</summary>
    [JsonIgnore] public List<RecreationGroup> SessionRanking = new List<RecreationGroup>();
    /// <summary>The schedule was rebuilt for a real change (UpdateSchedule with fullrebuild), or a save was loaded: the next hourly tick re-runs ranking and confirming.</summary>
    [JsonIgnore] public bool RecreationDirty = false;

    [JsonIgnore] public IReadOnlyList<RecreationGroup> SessionInbox { get { return sessionInbox; } }

    /// <summary>Puts session in this character's inbox (once) - a push from its host / extender, never a broadcast.</summary>
    public void PushSession(RecreationGroup session)
    {
        if (session == null || sessionInbox.Contains(session)) return;
        sessionInbox.Add(session);
        inboxSerial++;
    }

    /// <summary>Takes session out of the inbox and the ranking (refused, or the session ended - RecreationUtility.UnhookSession).</summary>
    public void DropSession(RecreationGroup session)
    {
        if (session == null) return;
        sessionInbox.Remove(session);
        SessionRanking.Remove(session);
    }

    /// <summary>Whether the inbox or any of listIDs' session lists changed since the last MarkSessionListsSeen.</summary>
    public bool SessionListsChanged(IEnumerable<string> listIDs)
    {
        if (seenInboxSerial != inboxSerial) return true;
        var registry = scr_System_CampaignManager.current.RecreationGroups;
        foreach (var id in listIDs)
        {
            int serial = registry.PostedSerial(id);
            if (serial == 0) continue;
            if (!seenListSerials.TryGetValue(id, out int seen) || seen != serial) return true;
        }
        return false;
    }

    public void MarkSessionListsSeen(IEnumerable<string> listIDs)
    {
        seenInboxSerial = inboxSerial;
        var registry = scr_System_CampaignManager.current.RecreationGroups;
        foreach (var id in listIDs) seenListSerials[id] = registry.PostedSerial(id);
    }

    /// <summary>
    /// After a save is loaded (sessions already relinked - RecreationGroupRegistry.OnAfterLoad): drops, quietly, bookings
    /// whose session is gone and those of the old group system; the next hourly tick re-ranks (RecreationDirty). Saves
    /// made before RecreationBooking.ownerRef existed get it stamped here.
    /// </summary>
    public void PostReloadUpdate_Recreation()
    {
        if (recreationBookings != null)
            recreationBookings.RemoveAll(b => b != null && (b.IsLegacyGroupBooking || (b.isSessionBooking && b.Session == null)));
        if (Owner != null)
        {
            if (recreationBookings != null) foreach (var b in recreationBookings) if (b != null && b.ownerRef < 0) b.ownerRef = Owner.RefID;
            if (pastRecreationBookings != null) foreach (var b in pastRecreationBookings) if (b != null && b.ownerRef < 0) b.ownerRef = Owner.RefID;
        }
        RecreationDirty = true;
    }

    /// <summary>Recreation source key (a membership's faction ID, an offer's ID) -> absolute days a visit was attended, last RecreationUtility.AttendanceMemoryDays only.</summary>
    [JsonProperty] Dictionary<string, List<int>> recreationAttendance = new Dictionary<string, List<int>>();

    /// <summary>Notes a visit for sourceKey on absolute day `day` (once per day), forgetting days older than the planner looks back.</summary>
    public void RecordAttendance(string sourceKey, int day)
    {
        if (string.IsNullOrEmpty(sourceKey)) return;
        if (recreationAttendance == null) recreationAttendance = new Dictionary<string, List<int>>();
        if (!recreationAttendance.TryGetValue(sourceKey, out var days)) recreationAttendance[sourceKey] = days = new List<int>();
        if (!days.Contains(day)) days.Add(day);
        days.RemoveAll(d => d < day - RecreationUtility.AttendanceMemoryDays);
    }

    /// <summary>Absolute days a visit for sourceKey was attended (remembered window only), empty if none.</summary>
    public IReadOnlyList<int> GetAttendedDays(string sourceKey)
    {
        if (recreationAttendance != null && !string.IsNullOrEmpty(sourceKey) && recreationAttendance.TryGetValue(sourceKey, out var days)) return days;
        return Array.Empty<int>();
    }

    /// <summary>
    /// Cancels this character's bookings at factionID (only those made for a membership there if membershipOnly) without
    /// refreshing the schedule - for faction changes, which refresh it themselves (UpdateFactionPriorityList).
    /// </summary>
    void CancelBookingsAt(string factionID, bool membershipOnly)
    {
        if (recreationBookings == null || recreationBookings.Count == 0 || string.IsNullOrEmpty(factionID)) return;
        foreach (var b in recreationBookings.ToList())
        {
            if (b == null || b.factionID != factionID) continue;
            if (membershipOnly && b.source != RecreationBookingSource.Membership) continue;
            CancelBooking(b, false, RecreationUtility.CancelReason.MembershipChanged);
        }
    }

    static bool HasBookingEnded(RecreationBooking b)
    {
        int now = RecreationBooking.AbsoluteHour(scr_System_Time.current.getAbsoluteDay(), scr_System_Time.current.getCurrentTime().Hour);
        return b.AbsEnd <= now;
    }

    /// <summary>Drops bookings that have ended (into the past-booking record) - run from the hourly UpdateSchedule.</summary>
    void PruneBookings()
    {
        PrunePastBookings();
        if (recreationBookings == null || recreationBookings.Count == 0) return;
        foreach (var b in recreationBookings) if (b != null && HasBookingEnded(b)) RecordPastBooking(b);
        recreationBookings.RemoveAll(b => b == null || HasBookingEnded(b));
    }

    /// <summary>
    /// Bookings no longer in recreationBookings (ended, or cancelled after they started - cut to the hours already
    /// spent) that still reach into today, so the schedule UI can show today's elapsed booked hours, and the hour that
    /// just ended can still be billed (GetBookingInEffectAtAbs). Never read by the live gameplay getters (GetBookingAt /
    /// GetEffectiveBooking). Entries ending before today are dropped (PrunePastBookings).
    /// </summary>
    [JsonProperty] List<RecreationBooking> pastRecreationBookings = new List<RecreationBooking>();

    /// <summary>Keeps the part of booking that has already elapsed (if any) in the past-booking record.</summary>
    void RecordPastBooking(RecreationBooking booking)
    {
        if (booking == null) return;
        int now = RecreationBooking.AbsoluteHour(scr_System_Time.current.getAbsoluteDay(), scr_System_Time.current.getCurrentTime().Hour);
        int spent = Math.Min(booking.hours, now - booking.AbsStart);
        if (spent < 1) return;
        if (pastRecreationBookings == null) pastRecreationBookings = new List<RecreationBooking>();
        pastRecreationBookings.Add(spent == booking.hours ? booking : booking.CopyTruncated(spent));
        PrunePastBookings();
    }

    /// <summary>
    /// Drops past-booking entries that ended before today began. One ending exactly at midnight is kept until the next
    /// day: the 00:00 tick still bills its last hour (GetBookingInEffectAtAbs - RecreationUtility.BillVisitHour).
    /// </summary>
    void PrunePastBookings()
    {
        if (pastRecreationBookings == null || pastRecreationBookings.Count == 0) return;
        int todayStart = RecreationBooking.AbsoluteHour(scr_System_Time.current.getAbsoluteDay(), 0);
        pastRecreationBookings.RemoveAll(b => b == null || b.AbsEnd < todayStart);
    }

    /// <summary>
    /// The booking that drove absolute hour absHour (RecreationBooking.AbsoluteHour) under GetEffectiveBooking's rules
    /// (IsBookingInEffect), or null - a live one, else the past-booking record's (ended, or cancelled after it started).
    /// For billing the hour that just ended (RecreationUtility.BillVisitHour), whose booking UpdateSchedule may already
    /// have pruned.
    /// </summary>
    public RecreationBooking GetBookingInEffectAtAbs(int absHour)
    {
        int day = absHour / 24, hour = absHour % 24;
        RecreationBooking found = null;
        if (recreationBookings != null)
            foreach (var b in recreationBookings) if (b != null && b.Covers(day, hour)) { found = b; break; }
        if (found == null && pastRecreationBookings != null)
            foreach (var b in pastRecreationBookings) if (b != null && b.Covers(day, hour)) { found = b; break; }
        if (found == null) return null;
        return IsBookingInEffect(found, hour, day - scr_System_Time.current.getAbsoluteDay()) ? found : null;
    }

    /// <summary>
    /// UI only: the booking that drives today's hour - the live one (GetEffectiveBooking), else for an elapsed hour the
    /// past-booking record's entry covering it, under the same rules as a live booking (work / forbid-work win unless overrideWork).
    /// </summary>
    public RecreationBooking GetUiBooking(int hour)
    {
        var live = GetEffectiveBooking(hour, 0);
        if (live != null) return live;
        if (pastRecreationBookings == null || pastRecreationBookings.Count == 0) return null;
        if (hour >= scr_System_Time.current.getCurrentTime().Hour) return null;

        int day = scr_System_Time.current.getAbsoluteDay();
        foreach (var b in pastRecreationBookings)
            if (b != null && b.Covers(day, hour)) return IsBookingInEffect(b, hour, 0) ? b : null;
        return null;
    }

    /// <summary>
    /// Daily recreation planning entry point - Character_Trainable's day update, stage 3 only (after factions and
    /// characters updated). The logic is RecreationUtility.DailyPlan; this class only stores the bookings.
    /// </summary>
    public void OnDayUpdate_Recreation()
    {
        RecreationUtility.DailyPlan(this);
    }

    /// <summary>
    /// Hourly recreation booking check entry point - Character_Trainable's hourly tick, right after UpdateSchedule.
    /// The logic is RecreationUtility.HourlyCheck.
    /// </summary>
    public void OnHourUpdate_Recreation()
    {
        RecreationUtility.HourlyCheck(this);
    }

    /// <summary>
    /// The MemberType this character acts under at faction right now: the booking in effect there (GetEffectiveBooking)
    /// if it names its own memberTypeID (e.g. a festival spectator, gym staff training as a member), else the faction's
    /// own MemberType for them. Used wherever the active faction's MemberType is read (behavior overrides, actor tags,
    /// CurrentActiveMemberType).
    /// </summary>
    public MemberType GetScheduledMemberType(I_IsJobGiver faction)
    {
        if (faction == null) return null;
        var booking = GetEffectiveBooking();
        if (booking != null && booking.MemberType != null && (I_IsJobGiver)booking.Faction == faction) return booking.MemberType;
        return faction.GetMemberType(Owner);
    }

    /// <summary>
    /// The recreation activity this character is visiting at faction right now: the activity of the booking in effect
    /// there (GetEffectiveBooking - RecreationBooking.GetActivity), else null. Applied wherever GetScheduledMemberType is,
    /// on top of it: its behaviorOverrides first (FindJobNodeRoot.TryGetJob), its Tags (Utility.GetActorTag) and
    /// AcceptanceMods (CurrentActiveActivity) next to the MemberType's.
    /// </summary>
    public RecreationActivity GetScheduledActivity(I_IsJobGiver faction)
    {
        if (faction == null) return null;
        var booking = GetEffectiveBooking();
        if (booking == null || (I_IsJobGiver)booking.Faction != faction) return null;
        return booking.GetActivity(this);
    }

    /// <summary>The activity counterpart of CurrentActiveMemberType: none while a party is active (the party's type rules then), else GetScheduledActivity of the active faction.</summary>
    [JsonIgnore]
    public RecreationActivity CurrentActiveActivity
    {
        get
        {
            if (CurrentActiveParty != null) return null;
            var faction = CurrentlyActiveFaction;
            return faction != null ? GetScheduledActivity(faction) : null;
        }
    }

    List<Manageable> _factions = null;
    /// <summary>
    /// Listing factions in order of priority. Work (internal priority order) > Home/TempHome > Recreation
    /// </summary>
    [JsonIgnore] public List<Manageable> Factions  { get {
            if (_factions == null)
            {
                _factions = new List<Manageable>(WorkFactions.Count + HomeFactions.Count + RecreationFactions.Count);
                foreach (var faction in WorkFactions)
                {
                    if (_factions.Contains(faction)) continue;
                    _factions.Add(faction);
                }
                foreach(var faction in HomeFactions)
                {
                    if (_factions.Contains(faction)) continue;
                    _factions.Add(faction);
                }
                foreach (var faction in RecreationFactions)
                {
                    if (faction == null || _factions.Contains(faction)) continue;
                    _factions.Add(faction);
                }
            }
           
            return _factions; } }

    [JsonIgnore] public List<Manageable> ManagerFactions { get
        {
            
            
                var managerfactionListCache = new List<Manageable>();
                foreach(var i in Factions) if (i.isCharaManager(Owner)) managerfactionListCache.Add(i);
            
            return managerfactionListCache;
        } }

    /// <summary>
    /// Same as FactionPriorityList, but only for factions in which chara is manager, Listing factions in order of priority. Work (internal priority order) > Home/TempHome
    /// </summary>
    private void UpdateFactionPriorityList()
    {
        _factions = null;
        _homefactions = null;
        if (FactionIDs_Work == null) FactionIDs_Work = new List<string>();

        this.Faction_Home_Temporary_Cache = null;
        this.Faction_Home_Cache = null;
        this.Factions_Work_Cache = null;
        this.Factions_Recreation_Cache = null;

        foreach (var v in HomeFactions) v.NotifyFactionMemberChange();
        foreach (var v in WorkFactions) v.NotifyFactionMemberChange();
        foreach (var v in RecreationFactions) v?.NotifyFactionMemberChange();

        // Alerts every faction managing this character (home AND work) to recalculate/recollect
        // membership fees right now, rather than waiting for the next daily TradeManager.ResolveDuePass
        // sweep - covers every faction-membership change (added/removed from a faction, home/temp-home
        // reassigned, dispatched to a work faction with an explicit sourceFaction), since every mutation
        // that can change HomeFactions/WorkFactions calls UpdateFactionPriorityList. Not just HomeFactions:
        // a work faction can itself be the tracked payer for another of this character's work factions
        // (see GetWorkFactionSourceOrDefault), so it needs the same eager notification home gets.
        foreach (var v in Factions) v.TradeManager?.EnsureMembershipFeeObligationFor(Owner);

        this.Owner.NotifyFactionChange();

        var s = new List<string>();
        UpdateSchedule(ref s);
    }

    /// <summary>
    /// return value of Null include case where chara has private schedule!!!!
    /// <br/>Only the priority home faction (HomeFactions[0] - temp home if set, else home) is ever
    /// considered; lower-priority home factions' schedules are ignored. Work factions are skipped
    /// entirely while the priority home faction forbids work (Manageable.AllowWorkFaction).
    /// <br/>Recreation bookings come after every work faction (before them if the booking is overrideWork, an event's)
    /// and before the priority home - see GetEffectiveBooking. includeRecreation = false leaves them out (sleep
    /// placement - see ValidateSchedule - and the planner's own occupancy checks).
    /// </summary>
    /// <param name="hour"></param>
    /// <returns></returns>
    public Manageable CurrentJobScheduleFaction(int hour = -1, int daysLookahead = 0, bool includeRecreation = true)
    {
        if (hour == -1) hour = scr_System_Time.current.getCurrentTime().Hour;
        var priorityHome = HomeFactions.Count > 0 ? HomeFactions[0] : null;

        if (includeRecreation)
        {
            var booking = GetEffectiveBooking(hour, daysLookahead);
            if (booking != null) return booking.Faction;
        }

        var work = WorkingFactionAt(hour, daysLookahead, priorityHome);
        if (work != null) return work;

        if (priorityHome != null && priorityHome.HasScheduleFor(this.Owner, hour, daysLookahead)) return priorityHome;
        return null;
    }

    /// <summary>
    /// The work faction whose own schedule (Manageable.HasScheduleFor - bookings never count) claims hour, in work
    /// priority order: one the character can and should work for, or one that is also the priority home (it keeps its
    /// work-order slot, with home semantics). Null while the priority home forbids work.
    /// </summary>
    Manageable WorkingFactionAt(int hour, int daysLookahead, Manageable priorityHome)
    {
        if (priorityHome != null && !priorityHome.AllowWorkFaction(Owner)) return null;
        foreach (var faction in WorkFactions)
        {
            if (faction == null || !faction.HasScheduleFor(this.Owner, hour, daysLookahead)) continue;
            if (faction == priorityHome || (Owner.CanWorkFor(faction) && Owner.ShouldWorkFor(faction))) return faction;
        }
        return null;
    }

    /// <summary>
    /// The booking that actually drives hour (-1 = now), daysLookahead days from today, or null: the booking covering it
    /// (GetBookingAt), unless the priority home forbids work, or a work schedule claims the hour (WorkingFactionAt) and the
    /// booking is not overrideWork. An unpaid membership fee (CanWorkFor) does NOT drop it - it only stops new visits
    /// being booked there (RecreationUtility.CollectCandidates); visits already booked or under way go ahead. It beats
    /// the priority home's own schedule. Single source of truth for CurrentJobScheduleFaction and the booked faction's
    /// schedule getters (Manageable.GetSchedule) - so a booking at one's own workplace never stands in for a real shift
    /// hour there, and one at home does replace the home schedule.
    /// </summary>
    public RecreationBooking GetEffectiveBooking(int hour = -1, int daysLookahead = 0)
    {
        if (hour == -1) hour = scr_System_Time.current.getCurrentTime().Hour;
        var booking = GetBookingAt(hour, daysLookahead);
        // a session booking only while its session is open and valid and this character attends (otherwise it just holds the time)
        if (booking != null && booking.isSessionBooking && !RecreationUtility.IsSessionBookingLive(booking, Owner)) return null;
        return IsBookingInEffect(booking, hour, daysLookahead) ? booking : null;
    }

    /// <summary>The GetEffectiveBooking rules for booking at hour: its faction exists, the priority home allows work, and no work schedule claims the hour unless overrideWork.</summary>
    bool IsBookingInEffect(RecreationBooking booking, int hour, int daysLookahead)
    {
        if (booking?.Faction == null) return false;

        var priorityHome = HomeFactions.Count > 0 ? HomeFactions[0] : null;
        if (priorityHome != null && !priorityHome.AllowWorkFaction(Owner)) return false;
        if (!booking.overrideWork && WorkingFactionAt(hour, daysLookahead, priorityHome) != null) return false;
        return true;
    }

    public string CurrentJobName(int hour)
    {
        var v = CurrentJobScheduleFaction(hour);
        if(v == null) return privateSchedule.Get(hour).Name;
        return v.GetSchedule(Owner, hour).Name;
    }

    public Manageable.HourlySchedule CurrentJobPost(int hour = -1, int daysLookahead = 0)
    {
        if (hour == -1) hour = scr_System_Time.current.getCurrentTime().Hour;
        var v = CurrentJobScheduleFaction((int)hour, daysLookahead);
        if(v == null) return privateSchedule.Get(hour);
        return v.GetSchedule(Owner, hour, daysLookahead);
    }

    [JsonProperty] protected Manageable.Job_Schedule privateSchedule =  new Manageable.Job_Schedule();
    [JsonProperty] protected Manageable.Job_Schedule pastSchedule = new Manageable.Job_Schedule();
    [JsonIgnore] public bool HasSleepSchedule { get { return privateSchedule.HasWorkHoursWithCOM("com_furniture_sleep"); } }

    /// <summary>
    /// UI-only accessor spanning today (dayOffset 0) and tomorrow (dayOffset 1), safe for a schedule
    /// preview to read even for already-elapsed hours - unlike CurrentJobPost/GetJobPost (which read
    /// the rolling 24h privateSchedule, re-anchored every hourly recompute and NOT calendar-stable),
    /// hours before "now" here are frozen as historical record and never rewritten.
    /// </summary>
    public Manageable.HourlySchedule GetUiSchedule(int hour)
    {
        if (FactionUtility.TryGetPartyGatheringOverride(Owner, hour, out var sc)) return sc;

        var faction = CurrentJobScheduleFaction(hour, 0);
        if (faction != null) return faction.GetSchedule(Owner, hour, 0);
        var currentHour = scr_System_Time.current.getCurrentTime().Hour;
        if (hour < currentHour) return pastSchedule.Get(hour);
        else return privateSchedule.Get(hour);
    }

    [JsonIgnore]
    public bool HasPlayerFaction
    {
        get
        {
            return this.Factions.Any(x => x.isPlayerFaction);
        }
    }
    /// <summary>
    /// Wipe and rebuild personal sleep schedule.<br/>
    /// Use this whenever an external schedule modification has taken place<br/>
    /// To modify a given chara's schedule, it's preferable to use SetSchedule() as it calls every necessary update internally.
    /// </summary>
    /// <param name="s"></param>
    public void UpdateSchedule_old(ref List<string> s)
    {
        var scheduleValidation = ValidateSchedule(ref s);
        privateSchedule.Clear();

        var consecutiveRestHour = scheduleValidation.Item2;
        var consecutiveSleepHours = scheduleValidation.Item1;
        var sleepHours = Owner.Stats.SleepHours;

        if (HomeFactions.Count < 1) return;

        var homeSleepHour = HomeFactions[0].NightStartHour;
        if (consecutiveRestHour >= 24 || (homeSleepHour >= 0 && consecutiveSleepHours[homeSleepHour] > 0 && consecutiveSleepHours[(homeSleepHour + sleepHours) % 24] == consecutiveSleepHours[homeSleepHour] + sleepHours))
        {   // consecutivehours contain start of sleep and end of sleep
            // we assign it normally
            //int endHour = (HomePriorityList[0].SharedSleepHour + sleepHours) % 24;
            int targetHour;
            for (int i = 0; i < sleepHours; i++)
            {
                targetHour = (homeSleepHour + i) % 24;
                privateSchedule.Get(targetHour).Set("com_furniture_sleep");
            }
        }
        else if (consecutiveRestHour >= sleepHours)
        {
            /*
        if free hours equals sleep hour: every hour is sleep hour
        prioritize one hour before sleep, then one hour after sleep, then more hours before sleep
         */
            if (consecutiveRestHour - sleepHours >= sleepHours)
            {   // 2 hours after sleep, rest before sleep
                int endHour = Array.IndexOf(consecutiveSleepHours, consecutiveSleepHours.Max()) - 2;
                for (int i = sleepHours; i > 0; i--)
                {
                    int targetHour = endHour - i;
                    privateSchedule.Get(targetHour < 0 ? targetHour + 24 : targetHour).Set( "com_furniture_sleep");
                }
            }
            else if (consecutiveRestHour - sleepHours >= 1)
            {   // only 1 hour free, prioritize early rise
                int endHour = Array.IndexOf(consecutiveSleepHours, consecutiveSleepHours.Max());
                for (int i = sleepHours; i > 0; i--)
                {
                    int targetHour = endHour - i;
                    privateSchedule.Get(targetHour < 0 ? targetHour + 24 : targetHour).Set("com_furniture_sleep");
                }
            }
            else if (consecutiveRestHour - sleepHours == 0)
            {   // immediately sleep
                int endHour = Array.IndexOf(consecutiveSleepHours, consecutiveSleepHours.Max()) + 1;
                for (int i = sleepHours; i > 0; i--)
                {
                    int targetHour = endHour - i;
                    privateSchedule.Get(targetHour < 0 ? targetHour + 24 : targetHour).Set("com_furniture_sleep");
                }
            }
        }
    }


    /// <summary>
    /// How many hours of occupancy data ValidateSchedule/RecomputePrivateSchedule look ahead when
    /// placing sleep. Wider than privateSchedule's 24-slot output on purpose, so a wake-time search
    /// anchored near the edge of a single day already has the next day's occupancy in view instead of
    /// only discovering the correct placement several hourly recomputes later once the rolling window
    /// has slid far enough to see it directly.
    /// </summary>
    private const int ScheduleLookaheadHours = 28;

    /// <summary>
    /// Recomputes and writes privateSchedule (the rolling 24h window) for the given currentHour.
    /// Extracted out of UpdateSchedule so callers can always run the 48h UI-registry propagation
    /// afterward, regardless of which of this method's several early-return paths was taken.<br/>
    /// Sleep scheduling algorithm (design 2.3+).<br/>
    /// Happy path: aligns wake to faction DayStartHour (or 6:00 for 24/24 factions), trait offset clamped within free hours.<br/>
    /// Conflict path: fits sleep in longest free block with 1-hour buffer before work; trait offset ignored.<br/>
    /// Operates on a rolling 24h horizon anchored at the current hour (meant to be re-run every hour, not
    /// once per day) so a day-of-week schedule change (e.g. weekday vs weekend work hours) is picked up
    /// as soon as it comes into range, instead of only at the previous midnight's recompute.
    /// </summary>
    private void RecomputePrivateSchedule(ref List<string> s, int currentHour)
    {
        var scheduleValidation  = ValidateSchedule(ref s, null, false, currentHour);
        privateSchedule.Clear();
        plannedSleepStartAbs = plannedSleepEndAbs = -1;

        var consecutiveFreeRun  = scheduleValidation.Item1; // indexed by r = hours-from-now, see ValidateSchedule
        var consecutiveRestHour = scheduleValidation.Item2;
        var sleepHours          = Owner.Stats.SleepHours;

        if (sleepHours == 0 || HomeFactions.Count < 1) return;
        if (consecutiveRestHour < sleepHours) return; // ValidateSchedule already logged the warning

        int wakeR = PlaceSleep(consecutiveFreeRun, sleepHours, GetTargetWakeR(currentHour, sleepHours), SleepTraitOffset);
        if (wakeR < 0) return;

        // fills sleepHours hours ending just before relative hour wakeR, converting back to absolute hour-of-day
        // only at the point of writing
        for (int i = 0; i < sleepHours; i++)
        {
            int r = wakeR - sleepHours + i;
            int absHour = (currentHour + r) % 24;
            if (absHour >= currentHour && currentHour + r > 23) continue;
            privateSchedule.Get(absHour).Set("com_furniture_sleep");
        }

        int nowAbs = RecreationBooking.AbsoluteHour(scr_System_Time.current.getAbsoluteDay(), currentHour);
        plannedSleepStartAbs = nowAbs + wakeR - sleepHours;
        plannedSleepEndAbs = nowAbs + wakeR;
    }

    /// <summary>
    /// The sleep block the last RecomputePrivateSchedule placed, as absolute hours [start, end) (RecreationBooking.AbsoluteHour),
    /// whole even where privateSchedule's 24 slots could not hold it; -1 when none was placed. Kept while the recompute is
    /// skipped (mid-sleep), so it stays the night in progress. Read by RecreationUtility.HourlyCheck (sleep floor / no split).
    /// </summary>
    [JsonProperty] int plannedSleepStartAbs = -1;
    [JsonProperty] int plannedSleepEndAbs = -1;

    /// <summary>GetStatValue returns 0 safely when stat_derived_wakeupOffset is not yet defined.</summary>
    int SleepTraitOffset { get { return (int)Owner.Stats.GetStatValue("stats_derived_wakeupOffset"); } }

    /// <summary>
    /// The priority home's wake target (DayStartHour, or 6:00 for 24/24 factions) as an hour offset from anchorHour -
    /// its next reachable occurrence.
    /// </summary>
    int GetTargetWakeR(int anchorHour, int sleepHours)
    {
        var homeFaction = HomeFactions[0];
        int targetWake  = homeFaction.HasDayNight ? homeFaction.DayStartHour : 6;
        // Convert to relative-hour space: the next occurrence of targetWake from "now" (today if
        // still ahead, tomorrow if already passed) - this is what makes "wake at 6am" unambiguous
        // regardless of the current hour, instead of assuming it always means "today at 6am".
        int targetWakeR = (targetWake - anchorHour + 24) % 24;
        // targetWakeR in [0, sleepHours] is unreachable as a fresh wake point - reaching it would
        // require having already started sleeping before "now", which CanWakeAt's r > sleepHours
        // guard forbids. (targetWakeR==0, currentHour==targetWake exactly, is just the most
        // obvious case of this - but 1..sleepHours are equally impossible, just less obviously so.)
        // Its real next occurrence is a full cycle away, so re-anchor the search there. With the
        // wider ScheduleLookaheadHours-hour lookahead, targetWakeR+24 is (for realistic sleepHours)
        // a directly checkable candidate, so Step 2 can confirm "wake at tomorrow's exact
        // target hour" outright instead of Step 3's search silently accepting whatever immediate
        // forward slot happens to be free (which, for a jobless/free character, is literally r ==
        // sleepHours+1 - sleep starting the very next hour, in the middle of the day).
        if (targetWakeR <= sleepHours) targetWakeR += 24;
        return targetWakeR;
    }

    /// <summary>
    /// Sleep scheduling algorithm (design 2.3+), pure: the wake hour, as an offset r into freeRun (ValidateSchedule's
    /// consecutive-free-hours array), of a sleepHours sleep ending just before it - or -1 if none fits. Shared by
    /// RecomputePrivateSchedule and PredictSleep so a predicted night is exactly what the hourly recompute will place.
    /// </summary>
    static int PlaceSleep(int[] freeRun, int sleepHours, int targetWakeR, int traitOffset)
    {
        int horizon = freeRun.Length;

        // CanWakeAt(r): all sleepHours hours immediately before relative hour r are free.
        // r is an offset from "now" (0..horizon-1), not a wrapping absolute hour.
        // Requires r > sleepHours (not just r > 0) so the resulting sleep block - hours
        // [r-sleepHours, r-1] - always starts at r==1 ("next hour") at the earliest, never r==0
        // ("now"). A recompute triggered mid-hour (e.g. from a player's SetSchedule edit) must never
        // mark the current hour as sleep, since doing so would immediately trip UpdateSchedule's
        // sleeping-guard and freeze further edits until game-clock time moves past it.
        // r>=horizon is rejected outright since it falls outside the computed horizon.
        bool CanWakeAt(int r) => r > sleepHours && r <= horizon - 1 && freeRun[r - 1] >= sleepHours;

        // CanWakeAtWithBuffer(r): same as CanWakeAt, but also requires relative hour r itself to
        // be free, so the character never wakes directly into a job with zero prep/travel time.
        bool CanWakeAtWithBuffer(int r) => CanWakeAt(r) && freeRun[r] > 0;

        // Step 2: Happy path — sleep aligned to faction day start.
        // Requires a free buffer hour at targetWakeR itself; if a job sits right at targetWakeR
        // (zero buffer), this gate fails and we fall through to Step 3, which searches for a
        // wake hour that leaves at least 1 free hour before the job.
        if (CanWakeAtWithBuffer(targetWakeR))
        {
            int desiredWakeR = targetWakeR - traitOffset;

            if (desiredWakeR < 0 || desiredWakeR > horizon - 1 || !CanWakeAtWithBuffer(desiredWakeR))
            {
                // Clamp: step back toward targetWakeR one hour at a time
                int step = traitOffset > 0 ? 1 : -1;
                for (int n = 1; n <= Math.Abs(traitOffset); n++)
                {
                    desiredWakeR += step;
                    if (desiredWakeR >= 0 && desiredWakeR <= horizon - 1 && CanWakeAtWithBuffer(desiredWakeR)) break;
                }
                if (desiredWakeR < 0 || desiredWakeR > horizon - 1 || !CanWakeAtWithBuffer(desiredWakeR)) desiredWakeR = targetWakeR; // full fallback
            }

            return desiredWakeR;
        }

        // Step 3: Conflict path — bidirectional search from targetWakeR, traits ignored.
        // At each distance n, check backward first (prefers later wake = more night-aligned).
        // Bounded by the horizon edges [0, horizon-1] - unlike absolute-hour
        // arithmetic, going past either edge means "outside the horizon we just computed", not
        // "wrap to yesterday".
        for (int n = 1; n <= horizon - 1; n++)
        {
            int bw = targetWakeR - n;
            if (bw >= 0 && CanWakeAtWithBuffer(bw)) return bw;

            int fw = targetWakeR + n;
            if (fw <= horizon - 1 && CanWakeAtWithBuffer(fw)) return fw;
        }

        // Step 4: Fallback — no free buffer hour exists (block length == sleepHours exactly).
        // Find nearest CanWakeAt without the buffer requirement.
        for (int n = 0; n <= horizon - 1; n++)
        {
            int bw = targetWakeR - n;
            if (bw >= 0 && CanWakeAt(bw)) return bw;
            int fw = targetWakeR + n;
            if (n > 0 && fw <= horizon - 1 && CanWakeAt(fw)) return fw;
        }
        return -1;
    }

    /// <summary>
    /// The sleep block the hourly recompute would place if it ran at anchorHour, anchorDaysFromToday days from today
    /// (same occupancy - work/home schedules, recreation bookings excluded - and same PlaceSleep), as absolute hours
    /// [startAbs, endAbs). For the recreation planner to see a future day's nights before the rolling privateSchedule
    /// reaches them. Anchor before the night starts (e.g. midday) - a recompute never places sleep in the anchor hour.
    /// False when no sleep would be placed.
    /// </summary>
    public bool PredictSleep(int anchorDaysFromToday, int anchorHour, out int startAbs, out int endAbs)
    {
        startAbs = endAbs = -1;
        int sleepHours = Owner.Stats.SleepHours;
        if (sleepHours == 0 || HomeFactions.Count < 1) return false;

        var freeRun = BuildFreeRun(anchorHour, anchorDaysFromToday, null, out int maxRun);
        if (maxRun < sleepHours) return false;

        int wakeR = PlaceSleep(freeRun, sleepHours, GetTargetWakeR(anchorHour, sleepHours), SleepTraitOffset);
        if (wakeR < 0) return false;

        int anchorAbs = RecreationBooking.AbsoluteHour(scr_System_Time.current.getAbsoluteDay(anchorDaysFromToday), anchorHour);
        startAbs = anchorAbs + wakeR - sleepHours;
        endAbs = anchorAbs + wakeR;
        return true;
    }

    /// <summary>
    /// Fewest sleep hours a night may keep once recreation bookings eat into it (half of SleepHours, rounded up) - see
    /// RecreationUtility.HourlyCheck.
    /// </summary>
    [JsonIgnore] public int MinSleepHoursWithBookings { get { return (Owner.Stats.SleepHours + 1) / 2; } }

    /// <summary>
    /// Wipe and rebuild personal sleep schedule.<br/>
    /// Use this whenever an external schedule modification has taken place<br/>
    /// To modify a given chara's schedule, it's preferable to use SetSchedule() as it calls every necessary update internally.<br/>
    /// If the character is currently mid-sleep, this is a no-op (see the guard below) - recomputing
    /// here (e.g. from an hourly tick) could shift or cancel a sleep block already in progress.<br/>
    /// First rebuilds privateSchedule (the rolling 24h window used by gameplay/AI reads), then
    /// propagates that result into uiSchedule48 (the calendar-anchored 48h window used by UI reads) -
    /// see PersonalScheduleWindow48.ApplyRollingWindow.
    /// </summary>
    public void UpdateSchedule(ref List<string> s, bool fullrebuild = true)
    {
        int currentHour = scr_System_Time.current.getCurrentTime().Hour;
        PruneBookings();
        // a real change (not the plain hourly recompute): the next hourly tick re-ranks / re-confirms recreation sessions
        if (fullrebuild) RecreationDirty = true;

        if (privateSchedule.HasWorkHoursWithCOM(currentHour, "com_furniture_sleep"))
        {
            // dont do anything
        }
        else
        {
            RecomputePrivateSchedule(ref s, currentHour);
        }

        // the hourly getter, not the whole-day Job_Schedule one: that one is null for a faction not managing this
        // character (a recreation booking at a public venue), and skips MemberType workModule hours
        var job = CurrentJobScheduleFaction(currentHour);
        if (job != null)
        {
            var post = job.GetSchedule(Owner, currentHour);
            if (post != null) pastSchedule.Get(currentHour).CopyFrom(post);
            else pastSchedule.Get(currentHour).Set("");
        }
        else pastSchedule.CopyFrom(privateSchedule, currentHour);
    }

    /// <summary>
    /// Check if chara has enough sleep hours.<br/>
    /// Run this if there is no external modification to schedule (just to ckeck warnings) <br/>
    /// If a modification has taken place, use UpdateSchedule() instead<br/>
    /// Walks a linear ScheduleLookaheadHours-hour horizon starting at startHour (defaults to the
    /// current game hour), not a fixed midnight-anchored day, so hours that fall on a future day are
    /// checked against that day's actual day-of-week schedule (daysLookahead) instead of assuming
    /// today's schedule repeats.
    /// </summary>
    /// <param name="s"></param>
    public Tuple<int[], int> ValidateSchedule(ref List<string> s, List<int> extraSchedule = null, bool extraDebug = false, int startHour = -1)
    {
        if (startHour == -1) startHour = scr_System_Time.current.getCurrentTime().Hour;

        var consecutiveFreeRun = BuildFreeRun(startHour, 0, extraSchedule, out int consecutiveRestHour);

        int listMax = consecutiveFreeRun.Max();
        int sleepHours = Owner.Stats.SleepHours;

        if(extraDebug && s != null) s.Add("Required Sleep hours [" + sleepHours + "]");

        if (consecutiveRestHour < sleepHours && s != null) s.Add(Utility.WrapTextColor("Does not have enough freetime for a full rest", scr_System_CentralControl.current.DisplaySetting.TextColor_conflict.Color) );
        else if (extraDebug && s != null) s.Add("Max Consecutive free hours [" + consecutiveRestHour + "] listMax ["+ listMax+ "] indexOflistMax [" + Array.IndexOf(consecutiveFreeRun, consecutiveFreeRun.Max()).ToString() + "]");
        if (extraDebug && s != null) s.Add("\n"+String.Join(" ", consecutiveFreeRun));

        // if we dont have enough consecutive time, we wipe everything and everytime character rest it falls dead sleep
        return new Tuple<int[], int>(consecutiveFreeRun, consecutiveRestHour);

    }

    /// <summary>
    /// Consecutive free hours over a ScheduleLookaheadHours horizon from startHour, startDayOffset days from today:
    /// entry r = free hours in a row ending at hour r (0 when occupied). Indexed by r = hours-from-start, NOT absolute
    /// hour-of-day - a linear horizon, not a repeating daily cycle, deliberately wider than privateSchedule's 24 slots.
    /// Occupied = a work/home schedule (CurrentJobScheduleFaction) or an extraSchedule hour-of-day. Recreation bookings
    /// never count: sleep is placed as if they weren't there, and a booking overlapping it simply takes those hours
    /// (the sleep window then measures shorter). maxRun = longest free run.
    /// </summary>
    int[] BuildFreeRun(int startHour, int startDayOffset, List<int> extraSchedule, out int maxRun)
    {
        maxRun = 0;
        int counter = 0;
        int[] freeRun = new int[ScheduleLookaheadHours];

        for (int r = 0; r < ScheduleLookaheadHours; r++)
        {
            int absHour = (startHour + r) % 24;
            int daysLookahead = startDayOffset + (startHour + r) / 24;
            if (CurrentJobScheduleFaction(absHour, daysLookahead, false) != null || (extraSchedule != null && extraSchedule.Contains(absHour))) counter = 0;
            else
            {
                counter++;
                maxRun = Math.Max(maxRun, counter);
            }
            freeRun[r] = counter;
        }
        return freeRun;
    }
}

