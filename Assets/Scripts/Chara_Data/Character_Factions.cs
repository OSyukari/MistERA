using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections;
using UnityEngine;
using Newtonsoft.Json;

[System.Serializable]
public class Character_Factions
{
    // Owner Ref
    int ownerRefID = -1;
    Character_Trainable ownerPointer = null;
    Character_Trainable Owner { get { if (ownerPointer == null) ownerPointer = scr_System_CampaignManager.current.FindInstanceByID(ownerRefID);
            return ownerPointer;
        } }

    //----------------
    [SerializeField][JsonProperty] string FactionID_Home = "";
    Manageable Faction_Home_Cache = null;
    [JsonIgnore] public Manageable Faction_Home{ get{
        if (Faction_Home_Cache == null && FactionID_Home != "") Faction_Home_Cache = scr_System_CampaignManager.current.FindFactionByID(FactionID_Home);
        return Faction_Home_Cache;
        }
    }

    //----------------
    [SerializeField][JsonProperty] string Faction_Home_Temporary_FactionID = "";
    Manageable Faction_Home_Temporary_Cache = null;
    [JsonIgnore] public Manageable Faction_Home_Temporary { get
        {
            if (Faction_Home_Temporary_Cache == null && Faction_Home_Temporary_FactionID != "") Faction_Home_Temporary_Cache = scr_System_CampaignManager.current.FindFactionByID(Faction_Home_Temporary_FactionID);
            return Faction_Home_Temporary_Cache;
        } }

    //-----------------
    [SerializeField][JsonProperty] List<string> FactionIDs_Work = new List<string>();
    List<Manageable> Factions_Work_Cache = null;
    List<Manageable> Factions_Work{get
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
    /// PRIORITY LISTING, FROM MOST PRIORITY TO LEAST
    /// </summary>
    [JsonIgnore] public List<Manageable> HomeFactions { get
        {
            List<Manageable> list = new List<Manageable>();
            if (Faction_Home_Temporary != null) list.Add(Faction_Home_Temporary);
            if (Faction_Home != null) list.Add(Faction_Home);
            return list;
        } }
    [JsonIgnore] public List<Manageable> WorkFactions { get
        {
            return Factions_Work;
        } }

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
    public void SetHomeFaction(string homeFactionID, bool isManager = false)
    {
        if (homeFactionID != FactionID_Home)
        {
            if (Faction_Home != null) Faction_Home.RemoveFromFaction(Owner);
            this.FactionID_Home = homeFactionID;
        }
        //Debug.Log("SetHomeFaction called on " + Owner.FirstName + " with arguments homeFactionID["+ homeFactionID+ "] isManager["+isManager+"]");
        if (this.Faction_Home != null) Faction_Home.AddToFaction(Owner, isManager ? Manageable_GuestStatus.Manager : Manageable_GuestStatus.Member);
        UpdateFactionPriorityList();
    }


    /// <summary>
    /// if factionID is empty, set to null
    /// </summary>
    /// <param name="tempFactionID"></param>
    public void SetTempHomeFaction(string tempFactionID)
    {
        if (tempFactionID != Faction_Home_Temporary_FactionID)
        {
            if (Faction_Home_Temporary != null) Faction_Home_Temporary.RemoveFromFaction(Owner);
            this.Faction_Home_Temporary_FactionID = tempFactionID;
        }

        if (Faction_Home_Temporary != null) Faction_Home_Temporary.AddToFaction(Owner, Manageable_GuestStatus.Visitor);
        UpdateFactionPriorityList();
    }


    public void DailyNeedConsumption()
    {
        bool returnValue = true;
        if (HomeFactions != null && HomeFactions.Count > 0)
        {
            foreach(var v in Owner.Stats.Needs)
            {
                var v2 = HomeFactions[0].QueryDailyCharaMaintenanceResult(v.consumeItemByTag);
                if (!v2 && v.statusDebuffID != "")
                {   // add status debuff
                    Owner.Stats.AddOrModStatus(v.statusDebuffID, 1441, 1441);
                    HomeFactions[0].AddDailyReportEntry("Due to missing resource "+v.consumeItemByTag+", "+Owner.FirstName+" is now "+v.statusDebuffID);
                }
                returnValue = v2 && returnValue;
            }


            // increase relationship
            foreach (var manager in HomeFactions[0].Managers)
            {
                if (Owner.RefID == manager.RefID) continue;

                if (returnValue){
                    Owner.Relationships.FindRelationshipWith(manager.RefID).ModRelationValue(RelationshipScoreType.Trust, 1);
                    HomeFactions[0].AddDailyReportEntry(Owner.FirstName+"'s trust toward "+manager.FirstName+" has increased by 1");
                } 
                else{
                    Owner.Relationships.FindRelationshipWith(manager.RefID).ModRelationValue(RelationshipScoreType.Trust, -1);
                    HomeFactions[0].AddDailyReportEntry(Owner.FirstName+"'s trust toward "+manager.FirstName+" has decreased by 1");
                }
            }

        }
        // else, no home faction, dont check it.
    }

    [JsonIgnore]
    public Manageable CurrentlyActiveFaction
    {
        get
        {
            var currentRoomID = scr_System_CampaignManager.current.Map.FindRoomByChara(Owner.RefID).RefID;
            foreach (var i in Factions) if (i != null && i.ManagedRooms.ContainsKey(currentRoomID)) return i;
            return null;
        }
    }

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

        foreach(var v in WorkFactions) v.NotifyFactionMemberChange();
    }

    public void SetSchedule(Manageable sourceFaction, int hour, COM selectedCOM)
    {
        if ((Factions.Find(x => x.ID == sourceFaction.ID) == null) )return;
        if (!sourceFaction.ManagedRefs.Contains(Owner.RefID)) return;
        sourceFaction.SetWorkHours(Owner, hour, selectedCOM);

        List<string> s = new List<string>();
        UpdateSchedule(ref s);
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

    /// <summary>
    /// Listing factions in order of priority. Work (internal priority order) > Home/TempHome
    /// </summary>
    [JsonIgnore] public List<Manageable> Factions  { get { 
                var factionListCache = new List<Manageable>();
                factionListCache.AddRange(WorkFactions);
                factionListCache.AddRange(HomeFactions);
            
            return factionListCache; } }

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
        if (FactionIDs_Work == null) FactionIDs_Work = new List<string>();

        this.Faction_Home_Temporary_Cache = null;
        this.Faction_Home_Cache = null;
        this.Factions_Work_Cache = null;

        foreach (var v in HomeFactions) v.NotifyFactionMemberChange();

        var s = new List<string>();
        UpdateSchedule(ref s);
    }


    public Manageable CurrentJobScheduleFaction(int hour = -1)
    {
        if (hour == -1) hour = scr_System_Time.current.getCurrentTime().Hour;
        foreach (var faction in Factions)
        {
            Manageable.Job_Schedule schedule = faction.GetSchedule(Owner);
            if (schedule == null) continue;
            //string comID = schedule.Get(hour).comIDs;
            if (schedule.Get(hour).comIDs.Count > 0) return faction;
        }
        return null;
    }

    public string CurrentJobName(int hour)
    {
        var v = CurrentJobScheduleFaction(hour);
        if(v == null) return "none";
        return v.GetSchedule(Owner).Get(hour).Name;
    }

    public Manageable.HourlySchedule CurrentJobPost(int hour = -1)
    {
        if (hour == -1) hour = scr_System_Time.current.getCurrentTime().Hour;
        var v = CurrentJobScheduleFaction((int)hour);
        if(v == null) return null;
        return v.GetSchedule(Owner).Get(hour);
    }

    [SerializeField][JsonProperty] protected Manageable.Job_Schedule privateSchedule =  new Manageable.Job_Schedule();
    [JsonIgnore] public bool HasSleepSchedule { get { return privateSchedule.GetWorkHoursWithCOM("com_furniture_sleep") > 0; } }

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
    public Tuple<int[], int> ValidateSchedule(ref List<string> s)
    {
        int consecutiveRestHour = 0;
        int counter = 0;

        int[] consecutiveSleepHours = new int[24];

        for(int i = 0; i < 48; i++)
        {
            if (CurrentJobScheduleFaction(i%24) != null) counter = 0;
            else
            {
                if (counter < 24) counter++;
                consecutiveRestHour = Math.Max(consecutiveRestHour, counter);
            }
            consecutiveSleepHours[i%24] = counter;
        }

        int listMax = consecutiveSleepHours.Max();
        int sleepHours = Owner.Stats.SleepHours;

        s.Add("Required Sleep hours [" + sleepHours + "]");

        if (consecutiveRestHour < sleepHours) s.Add("Does not have enough freetime for a full rest");
        else s.Add("Max Consecutive free hours [" + consecutiveRestHour + "] listMax ["+ listMax+ "] indexOflistMax [" + Array.IndexOf(consecutiveSleepHours, consecutiveSleepHours.Max()).ToString() + "]");
        s.Add("\n"+String.Join(" ", consecutiveSleepHours));

        // if we dont have enough consecutive time, we wipe everything and everytime character rest it falls dead sleep
        return new Tuple<int[], int>(consecutiveSleepHours, consecutiveRestHour);

    }
}

