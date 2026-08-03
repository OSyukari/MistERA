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
    Character_Trainable Owner { get { if (ownerPointer == null) ownerPointer = scr_System_CampaignManager.current.FindInstanceByID(ownerRefID);
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
    [JsonProperty] List<string> FactionIDs_Work = new List<string>();
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
        }

        if (Faction_Home_Temporary != null)
        { 
            Faction_Home_Temporary.AddToFaction(Owner, status, sendEvent);
            if (this.Owner.isTemporaryActor && Faction_Home_Temporary.isPlayerRelatedFaction) this.Owner.isTemporaryActor = false;
        }
        UpdateFactionPriorityList();
    }

    public void FlagForDailyNeed()
    {
        if (!this.isPartyLocked)
        {
            if (this.CurrentActiveParty != null)
            {
                var party = this.CurrentActiveParty.FactionOwnerRoot;
                if (party != null)
                {
                    //Debug.LogError($"Registering daily consumption for {Owner.CallName} on faction {party.FactionDisplayName}");
                    party.RegisterForResourceConsumption(Owner.RefID);
                }
            }
            else if (this.HomeFactions.Count > 0)
            {
                var home = this.HomeFactions[0];
                if (home != null)
                {
                    //Debug.LogError($"Registering daily consumption for {Owner.CallName} on faction {home.FactionDisplayName}");
                    home.RegisterForResourceConsumption(Owner.RefID);
                }
            }
        }
    }

    

    public void DailyNeedConsumption()
    {
        bool returnValue = true;
        Manageable home = null;
        if (!this.isPartyLocked)
        {
            if (this.CurrentActiveParty != null) home = this.CurrentActiveParty.FactionOwnerRoot;
            else if (this.HomeFactions.Count > 0) home = this.HomeFactions[0];
        }

        if (home != null && home.isPlayerFaction)
        {
            foreach(var v in Owner.Stats.Needs)
            {
                var v2 = home.QueryDailyCharaMaintenanceResult(v.consumeItemByTag);
                if (!v2 && v.statusDebuffID != "")
                {   // add status debuff
                    Owner.Stats.AddOrModStatus(v.statusDebuffID, 1441, 1441);
                    HomeFactions[0].DailyReport.AddManageReport("Due to missing resource "+v.consumeItemByTag+", "+Owner.FirstName+" is now "+v.statusDebuffID, true);
                }
                returnValue = v2 && returnValue;
            }

            // increase relationship
            foreach (var manager in HomeFactions[0].Managers)
            {
                if (Owner.RefID == manager.RefID) continue;

                var scoreinc = returnValue ? 1 : -1;
                Owner.Relationships.IncreaseRelationshipWith(manager.RefID, RelationshipScoreType.Trust, scoreinc);// FindRelationshipWith(manager.RefID).ModRelationValue(RelationshipScoreType.Trust, 1);

                var s = LocalizeDictionary.QueryThenParse("ui_management_overview_daily_trust")
                    .Replace("$name$", Owner.FirstName)
                    .Replace("$leader$", manager.FirstName)
                    .Replace("$score$", LocalizeDictionary.QueryThenParse("relationship_trust"))
                    .Replace("$count$", scoreinc.ToString("+0;-#"));

                HomeFactions[0].DailyReport.AddManageReport(s, !returnValue);

            }
        }
        // else, no home faction, dont check it.
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
            return room.FactionOwner as I_IsJobGiver;
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

    public void AddWorkFaction(string factionID, MemberType status, bool sendEvent = true)
    {
        Manageable targetFaction = Factions_Work.Find(x => x.ID == factionID);
        if (targetFaction == null) targetFaction = scr_System_CampaignManager.current.FindFactionByID(factionID);

        if (targetFaction == null) return;
        else
        {
            targetFaction.AddToFaction(Owner, status, sendEvent);
            if (!Factions_Work.Contains(targetFaction)) this.Factions_Work.Add(targetFaction);
            if (!FactionIDs_Work.Contains(targetFaction.ID)) this.FactionIDs_Work.Add(targetFaction.ID);
        }

        UpdateFactionPriorityList();

    }

    public void AddWorkFaction(string factionID, bool isManager = false)
        => AddWorkFaction(factionID, isManager ? FactionUtility.MemberType_Manager : FactionUtility.MemberType_Member);
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
    /// Workfaction for now does not allow setting single, so if target is not registered as home it will skip setting
    /// </summary>
    /// <param name="sourceFaction"></param>
    /// <param name="hour"></param>
    /// <param name="selectedCOM"></param>
    public void SetSchedule(Manageable sourceFaction, int hour, COM selectedCOM)
    {
        //string message = "";

        if (selectedCOM != null && !HomeFactions.Contains(sourceFaction))
        {
            Debug.LogError($"setschedule single target {sourceFaction.FactionDisplayName} not in homefactions, return");
            return;
        }
        sourceFaction.SetWorkHour(Owner, hour, selectedCOM);
        
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
            if (!this.Factions.Contains(sourceFaction)) AddWorkFaction(sourceFaction.ID);
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
        this.Factions_Work.Remove(targetFaction);

        UpdateFactionPriorityList();
    }

    List<Manageable> _factions = null;
    /// <summary>
    /// Listing factions in order of priority. Work (internal priority order) > Home/TempHome
    /// </summary>
    [JsonIgnore] public List<Manageable> Factions  { get { 
            if (_factions == null)
            {
                _factions = new List<Manageable>(WorkFactions.Count + HomeFactions.Count);
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

        foreach (var v in HomeFactions) v.NotifyFactionMemberChange();
        foreach (var v in WorkFactions) v.NotifyFactionMemberChange();

        this.Owner.NotifyFactionChange();

        var s = new List<string>();
        UpdateSchedule(ref s);
    }

    /// <summary>
    /// return value of Null include case where chara has private schedule!!!!
    /// </summary>
    /// <param name="hour"></param>
    /// <returns></returns>
    public Manageable CurrentJobScheduleFaction(int hour = -1, int daysLookahead = 0)
    {
        if (hour == -1) hour = scr_System_Time.current.getCurrentTime().Hour;
        foreach (var faction in Factions)
        {
            if (faction.HasScheduleFor(this.Owner, hour, daysLookahead)) return faction;
        }
        return null;
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
    private const int ScheduleLookaheadHours = 36;

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

        var consecutiveFreeRun  = scheduleValidation.Item1; // indexed by r = hours-from-now, see ValidateSchedule
        var consecutiveRestHour = scheduleValidation.Item2;
        var sleepHours          = Owner.Stats.SleepHours;

        if (sleepHours == 0 || HomeFactions.Count < 1) return;
        if (consecutiveRestHour < sleepHours) return; // ValidateSchedule already logged the warning

        // CanWakeAt(r): all sleepHours hours immediately before relative hour r are free.
        // r is an offset from "now" (0..ScheduleLookaheadHours-1), not a wrapping absolute hour.
        // Requires r > sleepHours (not just r > 0) so the resulting sleep block - hours
        // [r-sleepHours, r-1] - always starts at r==1 ("next hour") at the earliest, never r==0
        // ("now"). A recompute triggered mid-hour (e.g. from a player's SetSchedule edit) must never
        // mark the current hour as sleep, since doing so would immediately trip UpdateSchedule's
        // sleeping-guard and freeze further edits until game-clock time moves past it.
        // r>=ScheduleLookaheadHours is rejected outright since it falls outside the computed horizon.
        bool CanWakeAt(int r) => r > sleepHours && r <= ScheduleLookaheadHours - 1 && consecutiveFreeRun[r - 1] >= sleepHours;

        // CanWakeAtWithBuffer(r): same as CanWakeAt, but also requires relative hour r itself to
        // be free, so the character never wakes directly into a job with zero prep/travel time.
        bool CanWakeAtWithBuffer(int r) => CanWakeAt(r) && consecutiveFreeRun[r] > 0;

        // WriteSleep(wakeR): fills sleepHours hours ending just before relative hour wakeR,
        // converting back to absolute hour-of-day only at the point of writing.
        void WriteSleep(int wakeR)
        {
            for (int i = 0; i < sleepHours; i++)
            {
                int r = wakeR - sleepHours + i;
                int absHour = (currentHour + r) % 24;
                if (absHour >= currentHour && currentHour + r > 23) continue;
                privateSchedule.Get(absHour).Set("com_furniture_sleep");
            }
        }

        var homeFaction = HomeFactions[0];
        int targetWake  = homeFaction.HasDayNight ? homeFaction.DayStartHour : 6;
        // Convert to relative-hour space: the next occurrence of targetWake from "now" (today if
        // still ahead, tomorrow if already passed) - this is what makes "wake at 6am" unambiguous
        // regardless of the current hour, instead of assuming it always means "today at 6am".
        int targetWakeR = (targetWake - currentHour + 24) % 24;
        // targetWakeR in [0, sleepHours] is unreachable as a fresh wake point - reaching it would
        // require having already started sleeping before "now", which CanWakeAt's r > sleepHours
        // guard above forbids. (targetWakeR==0, currentHour==targetWake exactly, is just the most
        // obvious case of this - but 1..sleepHours are equally impossible, just less obviously so.)
        // Its real next occurrence is a full cycle away, so re-anchor the search there. With the
        // wider ScheduleLookaheadHours-hour lookahead, targetWakeR+24 is (for realistic sleepHours)
        // a directly checkable candidate, so Step 2 below can confirm "wake at tomorrow's exact
        // target hour" outright instead of Step 3's search silently accepting whatever immediate
        // forward slot happens to be free (which, for a jobless/free character, is literally r ==
        // sleepHours+1 - sleep starting the very next hour, in the middle of the day).
        if (targetWakeR <= sleepHours) targetWakeR += 24;
        //Debug.LogError($"UpdateSchedule {consecutiveRestHour} {String.Join("|", consecutiveFreeRun)}");

        // Step 2: Happy path — sleep aligned to faction day start.
        // Requires a free buffer hour at targetWakeR itself; if a job sits right at targetWakeR
        // (zero buffer), this gate fails and we fall through to Step 3, which searches for a
        // wake hour that leaves at least 1 free hour before the job.
        if (CanWakeAtWithBuffer(targetWakeR))
        {
            // GetStatValue returns 0 safely when stat_derived_wakeupOffset is not yet defined
            int traitOffset = (int)Owner.Stats.GetStatValue("stats_derived_wakeupOffset");
            int desiredWakeR = targetWakeR - traitOffset;

            if (desiredWakeR < 0 || desiredWakeR > ScheduleLookaheadHours - 1 || !CanWakeAtWithBuffer(desiredWakeR))
            {
                // Clamp: step back toward targetWakeR one hour at a time
                int step = traitOffset > 0 ? 1 : -1;
                for (int n = 1; n <= Math.Abs(traitOffset); n++)
                {
                    desiredWakeR += step;
                    if (desiredWakeR >= 0 && desiredWakeR <= ScheduleLookaheadHours - 1 && CanWakeAtWithBuffer(desiredWakeR)) break;
                }
                if (desiredWakeR < 0 || desiredWakeR > ScheduleLookaheadHours - 1 || !CanWakeAtWithBuffer(desiredWakeR)) desiredWakeR = targetWakeR; // full fallback
            }

            WriteSleep(desiredWakeR);
            return;
        }

        // Step 3: Conflict path — bidirectional search from targetWakeR, traits ignored.
        // At each distance n, check backward first (prefers later wake = more night-aligned).
        // Bounded by the horizon edges [0, ScheduleLookaheadHours-1] - unlike absolute-hour
        // arithmetic, going past either edge means "outside the horizon we just computed", not
        // "wrap to yesterday".
        for (int n = 1; n <= ScheduleLookaheadHours - 1; n++)
        {
            int bw = targetWakeR - n;
            if (bw >= 0 && CanWakeAtWithBuffer(bw)) { WriteSleep(bw); return; }

            int fw = targetWakeR + n;
            if (fw <= ScheduleLookaheadHours - 1 && CanWakeAtWithBuffer(fw)) { WriteSleep(fw); return; }
        }

        // Step 4: Fallback — no free buffer hour exists (block length == sleepHours exactly).
        // Find nearest CanWakeAt without the buffer requirement.
        for (int n = 0; n <= ScheduleLookaheadHours - 1; n++)
        {
            int bw = targetWakeR - n;
            if (bw >= 0 && CanWakeAt(bw)) { WriteSleep(bw); return; }
            int fw = targetWakeR + n;
            if (n > 0 && fw <= ScheduleLookaheadHours - 1 && CanWakeAt(fw)) { WriteSleep(fw); return; }
        }
    }

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

        if (privateSchedule.HasWorkHoursWithCOM(currentHour, "com_furniture_sleep"))
        {
            // dont do anything
        }
        else
        {
            RecomputePrivateSchedule(ref s, currentHour);
        }

        var job = CurrentJobScheduleFaction(currentHour);
        if (job != null) pastSchedule.CopyFrom(job.GetSchedule(Owner), currentHour);
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

        int consecutiveRestHour = 0;
        int counter = 0;
        // Indexed by r = hours-from-now (0..ScheduleLookaheadHours-1), NOT absolute hour-of-day -
        // this is a linear horizon starting at startHour, not a repeating daily cycle. Deliberately
        // wider than privateSchedule's 24-slot output - see ScheduleLookaheadHours.
        int[] consecutiveFreeRun = new int[ScheduleLookaheadHours];

        for (int r = 0; r < ScheduleLookaheadHours; r++)
        {
            int absHour = (startHour + r) % 24;
            int daysLookahead = (startHour + r) / 24;
            if (CurrentJobScheduleFaction(absHour, daysLookahead) != null || (extraSchedule != null && extraSchedule.Contains(absHour))) counter = 0;
            else
            {
                counter++;
                consecutiveRestHour = Math.Max(consecutiveRestHour, counter);
            }
            consecutiveFreeRun[r] = counter;
        }

        int listMax = consecutiveFreeRun.Max();
        int sleepHours = Owner.Stats.SleepHours;

        if(extraDebug && s != null) s.Add("Required Sleep hours [" + sleepHours + "]");

        if (consecutiveRestHour < sleepHours && s != null) s.Add(Utility.WrapTextColor("Does not have enough freetime for a full rest", scr_System_CentralControl.current.DisplaySetting.TextColor_conflict.Color) );
        else if (extraDebug && s != null) s.Add("Max Consecutive free hours [" + consecutiveRestHour + "] listMax ["+ listMax+ "] indexOflistMax [" + Array.IndexOf(consecutiveFreeRun, consecutiveFreeRun.Max()).ToString() + "]");
        if (extraDebug && s != null) s.Add("\n"+String.Join(" ", consecutiveFreeRun));

        // if we dont have enough consecutive time, we wipe everything and everytime character rest it falls dead sleep
        return new Tuple<int[], int>(consecutiveFreeRun, consecutiveRestHour);

    }
}

