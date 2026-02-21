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
        this.ownerPointer = owner;
        this.ownerRefID = owner.RefID;
    }

    /// <summary>
    /// if factionID is empty, then create faction with character name
    /// </summary>
    /// <param name="homeFactionID"></param>
    public void SetHomeFaction(string homeFactionID, Manageable_GuestStatus status = Manageable_GuestStatus.Member, bool sendEvent = true)
    {
        if (homeFactionID != FactionID_Home)
        {
            if (Faction_Home != null) Faction_Home.RemoveFromFaction(Owner);
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
    public void SetTempHomeFaction(string tempFactionID, Manageable_GuestStatus status = Manageable_GuestStatus.Visitor, bool sendEvent = true)
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

                if (returnValue){
                    Owner.Relationships.IncreaseRelationshipWith(manager.RefID, RelationshipScoreType.Trust, 1);// FindRelationshipWith(manager.RefID).ModRelationValue(RelationshipScoreType.Trust, 1);
                    HomeFactions[0].DailyReport.AddManageReport(Owner.FirstName+"'s trust toward "+manager.FirstName+" has increased by 1");
                } 
                else{
                    Owner.Relationships.IncreaseRelationshipWith(manager.RefID, RelationshipScoreType.Trust, -1);
                    HomeFactions[0].DailyReport.AddManageReport(Owner.FirstName+"'s trust toward "+manager.FirstName+" has decreased by 1", true);
                }
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

    public void AddWorkFaction(string factionID, bool isManager = false)
    {
        Manageable targetFaction = Factions_Work.Find(x => x.ID == factionID);
        if (targetFaction == null) targetFaction = scr_System_CampaignManager.current.FindFactionByID(factionID);

        if (targetFaction == null) return;
        else
        {
            targetFaction.AddToFaction(Owner, isManager ? Manageable_GuestStatus.Manager : Manageable_GuestStatus.Member);
            if (!Factions_Work.Contains(targetFaction))  this.Factions_Work.Add(targetFaction);
            if (!FactionIDs_Work.Contains(targetFaction.ID)) this.FactionIDs_Work.Add(targetFaction.ID);
        }

        UpdateFactionPriorityList();

    }
    [JsonProperty] List<int> trackedPartyRef = new List<int>();

    public bool AddToPartyAsTemp(I_IsJobGiver party, Manageable_GuestStatus status, Manageable_GuestStatus homeStatus, bool isLock = false)
    {
        var p = party as Manageable_Party;
        if (p == null) return false;

        return AddToPartyAsTemp(p, status, homeStatus, isLock);
    }
    public bool AddToPartyAsTemp(Manageable_Party party, Manageable_GuestStatus status, Manageable_GuestStatus homeStatus, bool isLock = false)
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
    public bool AddToParty(I_IsJobGiver party, Manageable_GuestStatus status, bool setHomeFaction, bool isLock = false)
    {
        var p = party as Manageable_Party;
        if (p == null) return false;

        return AddToParty(p, status, setHomeFaction, isLock);
    }
    public bool AddToParty(Manageable_Party party, Manageable_GuestStatus status, bool setHomeFaction, bool isLock = false)
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
        trackedPartyRef.Remove(party.Job.RefID);
    }


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
    public Manageable CurrentJobScheduleFaction(int hour = -1)
    {
        if (hour == -1) hour = scr_System_Time.current.getCurrentTime().Hour;
        foreach (var faction in Factions)
        {
            if (faction.HasScheduleFor(this.Owner, hour)) return faction;
        }
        return null;
    }

    public string CurrentJobName(int hour)
    {
        var v = CurrentJobScheduleFaction(hour);
        if(v == null) return privateSchedule.Get(hour).Name;
        return v.GetSchedule(Owner).Get(hour).Name;
    }

    public Manageable.HourlySchedule CurrentJobPost(int hour = -1)
    {
        if (hour == -1) hour = scr_System_Time.current.getCurrentTime().Hour;
        var v = CurrentJobScheduleFaction((int)hour);
        if(v == null) return privateSchedule.Get(hour);
        return v.GetSchedule(Owner).Get(hour);
    }

    [JsonProperty] protected Manageable.Job_Schedule privateSchedule =  new Manageable.Job_Schedule();
    [JsonIgnore] public bool HasSleepSchedule { get { return privateSchedule.HasWorkHoursWithCOM("com_furniture_sleep"); } }

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
    public void UpdateSchedule(ref List<string> s)
    {
        var scheduleValidation = ValidateSchedule(ref s);
        privateSchedule.Clear();

        var consecutiveRestHour = scheduleValidation.Item2;
        var consecutiveSleepHours = scheduleValidation.Item1;
        var sleepHours = Owner.Stats.SleepHours;

        if (HomeFactions.Count < 1) return;

        var homeSleepHour = (HomeFactions[0] as Manageable_HomeFaction).SharedSleepHour;
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
    /// Check if chara has enough sleep hours.<br/>
    /// Run this if there is no external modification to schedule (just to ckeck warnings) <br/>
    /// If a modification has taken place, use UpdateSchedule() instead
    /// </summary>
    /// <param name="s"></param>
    public Tuple<int[], int> ValidateSchedule(ref List<string> s, List<int> extraSchedule = null, bool extraDebug = false)
    {
        int consecutiveRestHour = 0;
        int counter = 0;
        int[] consecutiveSleepHours = new int[24];

        for(int i = 0; i < 48; i++)
        {
            if (CurrentJobScheduleFaction(i%24) != null || (extraSchedule != null && extraSchedule.Contains(i%24))) counter = 0;
            else
            {
                if (counter < 24) counter++;
                consecutiveRestHour = Math.Max(consecutiveRestHour, counter);
            }
            consecutiveSleepHours[i%24] = counter;
        }

        int listMax = consecutiveSleepHours.Max();
        int sleepHours = Owner.Stats.SleepHours;

        if(extraDebug) s.Add("Required Sleep hours [" + sleepHours + "]");

        if (consecutiveRestHour < sleepHours) s.Add(Utility.WrapTextColor("Does not have enough freetime for a full rest", scr_System_CentralControl.current.DisplaySetting.TextColor_conflict.Color) );
        else if (extraDebug) s.Add("Max Consecutive free hours [" + consecutiveRestHour + "] listMax ["+ listMax+ "] indexOflistMax [" + Array.IndexOf(consecutiveSleepHours, consecutiveSleepHours.Max()).ToString() + "]");
        if (extraDebug) s.Add("\n"+String.Join(" ", consecutiveSleepHours));

        // if we dont have enough consecutive time, we wipe everything and everytime character rest it falls dead sleep
        return new Tuple<int[], int>(consecutiveSleepHours, consecutiveRestHour);

    }
}

