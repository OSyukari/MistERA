using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
using System.Linq;

public class ExpeditionMessageEntry
{
    public string EventDescription = "";
    public List<string> Tooltips = new List<string>();
    public List<string> Tags = new List<string>();
    public List<int> Characters = new List<int>();

    [JsonIgnore]
    public string FullDescription
    {
        get
        {
            var names = new List<string>();
            foreach (var i in Characters) names.Add(scr_System_CampaignManager.current.FindInstanceByID(i).CallName);
            return $"{EventDescription.Replace("$names$", String.Join(", ", names))}";

        }
    }

    public SerializableEventPackage unresolved = null;

}


public class SerializableEventPackage
{
/* 
 * this package will remain dormant until player click
 * this package will only preserve 
 */
    public string eventID = "";
    public string eventLabel = "";
    public Dictionary<string, List<int>> Targets = new Dictionary<string, List<int>>();
    public Dictionary<string, List<string>> AppendStrings = new Dictionary<string, List<string>>();
    public bool overrideTargetScope = false;
    public List<Event.EventScope_Target> targetScopes = new List<Event.EventScope_Target>();
    [JsonIgnore]
    public bool isValid
    { get
        {
            return eventID != "";
        }
    }

    [JsonIgnore]
    public string DisplayName { get { return LocalizeDictionary.QueryThenParse(eventID); } }
    public bool resolved = false;
}


[System.Serializable]
public class Job_Expedition : Job
{
    [JsonIgnore]
    public override string DisplayName
    {
        get
        {
            return LocalizeDictionary.QueryThenParse($"ui_management_expeditionJob_{this.status}")
                    .Replace("$expName$", this.Expedition == null ? "-" : this.Expedition.DisplayName);
        }
    }

    [JsonIgnore]
    public string RemainingTime
    {
        get
        {
            return LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_remainingTime")
                    .Replace("$time$", $"{((double)this.RemainingMinutes / 60.0).ToString("F1")}");
        }
    }

    [JsonIgnore]
    public string DisplayName_EndJob
    {
        get
        {
            if (this.status == ExpeditionStatus.returning)
            {
                if (canReturn) LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_resolving");
                else return LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_requireManualresolve");
            }
            if (isActive) return LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_abort");
            return LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_cancel");

        }
    }

    public override void OnAfterDeserialize()
    {
        base.OnAfterDeserialize();
    }

    [JsonIgnore] public override bool CanBeInterrupted { get { return false; } }

    public SortedDictionary<DateTime, List<ExpeditionMessageEntry>> ExpeditionResults = new SortedDictionary<DateTime, List<ExpeditionMessageEntry>>();

    protected void TryMerge(DateTime time, ExpeditionMessageEntry ExpeditionResult)
    {
        if (ExpeditionResults.ContainsKey(time))
        {
            bool merged = false;
            foreach (var i in ExpeditionResults[time])
            {
                if (i.EventDescription == ExpeditionResult.EventDescription)
                {
                    i.Tags.AddRange(ExpeditionResult.Tags);
                    i.Tags = i.Tags.Distinct().ToList();
                    i.Characters.AddRange(ExpeditionResult.Characters);
                    i.Characters = i.Characters.Distinct().ToList();
                    merged = true;
                }
            }
            if (!merged) ExpeditionResults[time].Add( ExpeditionResult );
        }
        else
        {
            ExpeditionResults.Add(scr_System_Time.current.getCurrentTime(), new List<ExpeditionMessageEntry>() { ExpeditionResult });
        }
    }

    public ExpeditionMessageEntry AddResult(string s, List<string> tags, List<int> chara, bool registerMemory = false)
    {
        var result = new ExpeditionMessageEntry();
        result.EventDescription = s;
        result.Tags = tags;
        result.Characters = chara;

        TryMerge(scr_System_Time.current.getCurrentTime(), result);
        return result;
    }

    public enum ExpeditionStatus
    {
        inactive,
        queued,
        gathering,
        active,
        resting,
        returning
    }

    protected void ClearLogs()
    {
        ExpeditionResults.Clear();
    }

    [JsonIgnore]
    protected List<string> ActorNames
    {
        get
        {

                var _cachedNames = new List<string>();
                foreach (var i in this.actorRefID) _cachedNames.Add(scr_System_CampaignManager.current.FindInstanceByID(i).CallName);

            return _cachedNames;
        }
    }

    public ExpeditionStatus status = ExpeditionStatus.inactive;
    public string statusTooltip = "";

    public void UpdateStatus(int currentHour, int currentMinute)
    {
        this.packageCooldown = Math.Max(this.packageCooldown - 1, 0);
        bool begin = false;
        if (!ExpeditionActive || Expedition == null)
        {
            status = ExpeditionStatus.inactive;
            statusTooltip = "party is inactive";
        }
        else if (status == ExpeditionStatus.inactive)
        {
            status = ExpeditionStatus.queued;
            statusTooltip = "party expedition queued";
        }

        if (status != ExpeditionStatus.inactive)
        {
            if (currentHour == startHour)
            {
                if (status == ExpeditionStatus.queued && FactionOwner_Party.TryStartExpedition())
                {
                    ClearLogs();
                    AddResult(LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_start"), new List<string>(), new List<int>());
                    this.RemainingMinutes = Expedition.DurationHour * 60;
                    status = ExpeditionStatus.gathering;
                    statusTooltip = "gathering expedition members";
                    begin = true;
                }
            }
            else if (status == ExpeditionStatus.gathering)
            {
                status = ExpeditionStatus.returning;
                statusTooltip = "gathering expedition members failed, retry tomorrow";
                AddResult(LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_cancel_missing"), new List<string>(), new List<int> {  });
            }
        }

        if (status == ExpeditionStatus.gathering)
        {
            bool wait = false;
            foreach(var c in FactionOwner.ManagedChara)
            {
                if (scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID) != this.FactionOwner_Party.MainExit) wait = true;
            }
            if (!wait)
            {

                status = ExpeditionStatus.active;
                AddResult(LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_start_final"), new List<string>(), new List<int>());
                StartCooldown();
                foreach (var cref in this.actorRefID)
                {
                    var c = scr_System_CampaignManager.current.FindInstanceByID(cref);
                    var memstr = LocalizeDictionary.QueryThenParse("exp_event_departure_memory").Replace("$loc$", this.Expedition.DisplayName);
                    var newMem = new MemInstance(new List<int>(), new List<string>(), "", -1, -1, true, Memory_Response.Accept, Memory_Attitude.Neutral, memstr);

                    var entry = c.Memory.AddEntry(newMem, new List<string>() { "forbidMerge" });
                    //entry.entryDescription = memstr;
                    entry.disableRoomName = true;
                }


                

            }
        }


        if (begin)
        {
            bool success = true;
            foreach (var i in this.FactionOwner.ManagedChara)
            {
                success = success && i.FactionManager.AddToParty(this.FactionOwner, Manageable_GuestStatus.Member, false);
                if (!success) break;
            }
            if (!success)
            {
                foreach (var i in this.FactionOwner.ManagedChara) i.FactionManager.RemoveFromParty(this.FactionOwner);
                status = ExpeditionStatus.queued;
            }
        }
        else if (this.status == ExpeditionStatus.active || this.status == ExpeditionStatus.resting)
        {

            if (this.FactionOwner_Party.OwnerFaction.mealHours.Contains(currentHour)
                    || ((this.FactionOwner_Party.AllowPassNight || this.FactionOwner_Party.BaseDuration > 23) && this.FactionOwner_Party.SleepHours.Contains(currentHour))) this.status = ExpeditionStatus.resting;
            else this.status = ExpeditionStatus.active;
        }

        if (this.status == ExpeditionStatus.active)
        {
            RemainingMinutes = Math.Max(0, RemainingMinutes - 1);
            if (RemainingMinutes == 0) this.status = ExpeditionStatus.returning;
        }

        if (this.status == ExpeditionStatus.returning && this.actorRefID.Count < 1)
        {
            this.ExpeditionActive = FactionOwner_Party.IsRecurring;
            this.status = this.ExpeditionActive ? ExpeditionStatus.queued : ExpeditionStatus.inactive;
            this.statusTooltip = "expedition concluded";
            var result = AddResult(LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_end"), new List<string>(), new List<int>());
            List<string> obtained = new List<string>();
            FactionOwner_Party.Inventory.Dump(FactionOwner_Party.OwnerFaction.Inventory, obtained);
            result.Tooltips.Add($"obtained {String.Join(", ", obtained)}");
            FactionOwner_Party.ExpeditionEnd();
        }
    }
    /// <summary>
    /// Return Parent FactionOwner (Party) Main Exit (camp room)
    /// </summary>
    [JsonIgnore]
    public override Room_Instance ParentRoom
    {
        get
        {
            return FactionOwner.MainExit;
        }
    }

    [JsonProperty] string activePartyID = "";
    [JsonProperty] string activePartyOwnerID = "";

    [JsonProperty] bool _expeditionActive = false;
    [JsonIgnore] public bool ExpeditionActive
    {
        get
        {
            return _expeditionActive;
        }
        set
        {
            
            if (value == true)
            {
                if (this.Expedition != null)
                {
                    _expeditionActive = true;
                    FactionOwner_Party.OnDayUpdate_0(); // wipe recurring
                    this.startHour = FactionOwner_Party.FinalStartHour;
                }
            }
            else
            {
                _expeditionActive = value;
            }
        }
    }


    public int startHour = -1;

    public Job_Expedition() : base() { }

    public Job_Expedition(Manageable_Party p) : base() 
    {
        this.FactionOwner = p;
    }

    public List<ActionPackage_Expedition> storedResults = new List<ActionPackage_Expedition>();

    public void StoreResult(ActionPackage_Expedition res)
    {
        storedResults.Add(res);
    }
    /// <summary>
    /// Please call this from Party
    /// </summary>
    public void SetExpedition(Expedition exp)
    {
        this.Expedition = exp;
    }

    [JsonProperty] protected int RemainingMinutes = 0;


    [JsonProperty] string expeditionID = "";
    Expedition _exp = null;

    [JsonIgnore]
    public Expedition Expedition
    {
        get
        {
            if (_exp == null && expeditionID != "")
            {
                _exp = Expeditions.ExpeditionEntry.GetByID(expeditionID);
            }
            return _exp;
        }
        set
        {
            _exp = value;
            expeditionID = value == null ? "" : value.ExpeditionID;
        }
    }

    public bool hasUnresolvedResult
    {
        get
        {
            foreach(var i in this.ExpeditionResults.Values)
            {
                foreach (var j in i) if (j.unresolved != null && !j.unresolved.resolved) return true;
            }
            return false;
        }
    }
    public bool canReturn { get { return isActive && this.status == ExpeditionStatus.returning && !hasUnresolvedResult; } }
    public bool isResting { get { return isActive && this.status == ExpeditionStatus.resting; } }
    public bool isActive { get {
            return this.Expedition != null && this.status > ExpeditionStatus.queued; } }

    [JsonIgnore]
    public Manageable_Party FactionOwner_Party { get { return FactionOwner as  Manageable_Party; } }

    [JsonIgnore]
    public string DescriptionString
    { get
        {
            var names = new List<string>();
            switch(this.status)
            {
                case ExpeditionStatus.active:
                    if (this.Expedition.DescriptionText.Count > 0) return LocalizeDictionary.QueryThenParse(Utility.GetRandomElement(this.Expedition.DescriptionText)).Replace("$names$", String.Join(", ", ActorNames));
                    else return LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_team_active").Replace("$names$", String.Join(", ", ActorNames));
                case ExpeditionStatus.resting:
                    return LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_team_resting").Replace("$names$", String.Join(", ", ActorNames));
                case ExpeditionStatus.gathering:
                    return LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_start");
                case ExpeditionStatus.returning:
                    return LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_team_returning").Replace("$names$", String.Join(", ", ActorNames));
                default:
                    return "?";
            }
        } }

    public override bool UpdateActorPackage(Character_Trainable c, out string ss)
    {
        /*
             if character hour has com setting, try get com setting job
            else get random
         */
       // RefreshValidCOMs(true);
        ss = "(Job Expedition): ";
        // actor have job but don't have a action package registeredDisplayName.
        //Character_Trainable c = scr_System_CampaignManager.current.FindInstanceByID(actorRefID[i]);

        // Check has ongoing package
        var temp = packages_current.FindAll(x => x.actorRefs.Contains(c.RefID));
        if (temp.Count > 0)
        {
            ss += c.FirstName + " already have package |";
            foreach (var i in temp) ss += i.DisplayName + "|";
            return true;
        }

        // check has ongoing package 2
        List<ActionPackage> tempList = packages_previous.FindAll(x => x.actorRefs.Contains(c.RefID));
        if (tempList.Exists(x => x.Duration > 0))
        {
            ss += c.FirstName + " already have ongoing previous package";
            return true;
        }
        /* Expedition ignores jobcomplete
        else if (actorJobComplete.Contains(c.RefID) || c.RefID == 0)
        {
            ss += c.FirstName + " have completed job, releasing";
            return false;
        }*/

        // pathing
        var charaRoom = scr_System_CampaignManager.current.GetCharaRoomInstance(c.RefID);
        var desiredCOMID = this.actorRefIDStorage[c.RefID].comID;
        var desiredCOM = this.allusableCOMs.Find(x => x.ID == desiredCOMID);

        var parentFaction = FactionOwner_Party;
        var parentPlusFaction = parentFaction == null ? null : parentFaction.OwnerFaction;

        if (isActive && status == ExpeditionStatus.returning && !canReturn)
        {
            // dont do anything
            return true;
        }
        if (canReturn)
        {
            actorJobComplete.Add(c.RefID);

            if (charaRoom.RefID != parentPlusFaction.MainExit.RefID)
            {   // return chara
                AddResult(LocalizeDictionary.QueryThenParse("exp_event_returning"), new List<string>(), new List<int> { c.RefID });
                ActionPackage_TeleportTo package = new ActionPackage_TeleportTo(this, c.RefID, parentPlusFaction.MainExit.RefID);
                if (!package.Validate())
                {
                    ss += "actor pathing package creation failed ||";
                    return false;
                }
                ss += "actor pathing created ||";
                AddPackage(new List<ActionPackage>() { package });

                var memStr = LocalizeDictionary.QueryThenParse("exp_event_return_memory").Replace("$loc$", this.Expedition.DisplayName);
                var newMem = new MemInstance(new List<int>(), new List<string>(), "", -1, -1, true, Memory_Response.Accept, Memory_Attitude.Neutral, memStr);

                var entry = c.Memory.AddEntry(newMem, new List<string>() { "expeditionEnd" });
                entry.disableRoomName = true;

                return true;
            }
            else
            {   // release chara
                return false;
            }
        }
        if (charaRoom.RefID != parentFaction.MainExit.RefID)
        {
            //Debug.Log("JobFurniture : trying to add pathing package to ["+c.FirstName+"]");
            // 1 - if actor not in job room, set go to room.
            // make movement package
            
            if (charaRoom.RefID != parentPlusFaction.MainExit.RefID)
            {
                //AddResult(LocalizeDictionary.QueryThenParse("exp_event_gathering"), new List<string>(), new List<int> { c.RefID });
                ActionPackage_PathTo package = new ActionPackage_PathTo(this, c.RefID, parentPlusFaction.MainExit.RefID);
                if (!package.Validate())
                {
                    ss += "actor pathing package creation failed ||";
                    return false;
                }
                ss += "actor pathing created ||";
                AddPackage(new List<ActionPackage>() { package });
                return true;
            }
            else
            {
                
                //AddResult(LocalizeDictionary.QueryThenParse("exp_event_departure"), new List<string>(), new List<int> { c.RefID });
                ActionPackage_TeleportTo package = new ActionPackage_TeleportTo(this, c.RefID, parentFaction.MainExit.RefID);
                if (!package.Validate())
                {
                    ss += "actor pathing package creation failed ||";
                    return false;
                }
                ss += "actor pathing created ||";
                AddPackage(new List<ActionPackage>() { package });

                /*
                var newMem = new MemInstance(new List<int>(), new List<string>(), "", -1, -1, true, Memory_Response.Accept, Memory_Attitude.Neutral,
                                    LocalizeDictionary.QueryThenParse("exp_event_departure_memory").Replace("$loc$", this.Expedition.DisplayName));

                var entry = c.Memory.AddEntry(newMem, new List<string>() { "expedition" });
                entry.entryDescription = LocalizeDictionary.QueryThenParse("exp_event_departure_memory").Replace("$loc$", this.Expedition.DisplayName);
                entry.disableRoomName = true;
                */
                return true;
            }
        }
        else if (this.status == ExpeditionStatus.gathering)
        {
            ss += "waiting for team to gather ||";
            return true;
        }
        else if (desiredCOM != null && desiredCOM.requirements.clothingRequirement < BodyEquipLayer.Outer && c.NeedUndress(desiredCOM.requirements.clothingRequirement, Revealing.Erotic))
        {
            ActionPackage_Undress package = new ActionPackage_Undress(this, c.RefID, desiredCOM.requirements.clothingRequirement, Revealing.Erotic);
            if (!package.Validate())
            {
                ss += "actor undress package creation failed ||";
                return false;
            }
            ss += "actor undress created ||";
            AddPackage(new List<ActionPackage>() { package });
            return true;
        }
        else if (this.isResting)
        {
            ss += "expedition on break! ||";
            return false;
        }
        else if (this.status == ExpeditionStatus.returning && canReturn)
        {
            ss += "expedition concluded, returning ||";
            return false;
        }
        else if (packageCooldown > 0)
        {
            ss += "expedition active, exploring, no event ||";
            return true;
        }
        else
        {

            //Debug.Log("JobFurniture : [" + c.FirstName + "] at work location, adding job command with [" + validCOMs.Count + "] valid jobCOMs [" + String.Join(",", s) + "]");
            // 2 - if actor is in room, set COM package
            // make COM package
            var list1 = MakePackages(c);

            var pl1 = list1.Count > 0 ? Utility.GetRandomElement(list1) : null;

            if (pl1 != null)
            {
                AddPackage(new List<ActionPackage>() { pl1 });
                ss += "creating package " + pl1.DescriptionText(c.RefID);
                return true;
            }
            else
            {
                ss += "actor has not valid command or has completed all commands";
                return false;
            }
        }
    }

    public void StartCooldown()
    {
        if (this.Expedition == null || this.FactionOwner_Party == null) return;
        this.packageCooldown = ExpeditionUtility.Cooldown(this.Expedition, this.FactionOwner_Party);
        Debug.Log($"Expedition cooldown set to: {packageCooldown}");
    }

    [JsonProperty] protected int packageCooldown = 0;

    /// <summary>
    /// Make a single random package
    /// </summary>
    /// <param name="c"></param>
    /// <param name="allowInvalid"></param>
    /// <returns></returns>
    public override List<ActionPackage> MakePackages(Character_Trainable c, bool allowInvalid = false)
    {
        //Debug.Log("JobFurniture : [" + c.FirstName + "] at work location, adding job command with [" + validCOMs.Count + "] valid jobCOMs [" + String.Join(",", s) + "]");
        // 2 - if actor is in room, set COM package
        // make COM package
        List<ActionPackage> results = new List<ActionPackage>();

        int loop = 0;
        while (loop < 10)
        {
            var newAP = ExpeditionUtility.RandEvent(Expedition, this.FactionOwner_Party);
            newAP.ReEstablishParent(this);// = this;
            if (newAP.Validate() || allowInvalid)
            {
                results.Add(newAP);
                break;
            }
            loop++;
        }

        return results;
    }

    public override string GetJobDescription(int charaRef)
    {
        return LocalizeDictionary.QueryThenParse("chara_currentjob_expedition");
        //return base.GetJobDescription(charaRef);
    }
}