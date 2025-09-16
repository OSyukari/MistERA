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

    // 
}


[System.Serializable]
public class Job_Expedition : Job
{
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

    public void AddResult(string s, List<string> tags, List<int> chara, bool registerMemory = false)
    {
        var result = new ExpeditionMessageEntry();
        result.EventDescription = s;
        registerMemory = registerMemory || tags.Contains("important");
        result.Tags = tags;
        result.Characters = chara;

        TryMerge(scr_System_Time.current.getCurrentTime(), result); 
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

    public ExpeditionStatus status = ExpeditionStatus.inactive;
    public string statusTooltip = "";

    public void UpdateStatus()
    {
        var prev = status;
        bool end = false;
        bool begin = false;
        if (!expeditionActive || Expedition == null)
        {
            status = ExpeditionStatus.inactive;
            statusTooltip = "party is inactive";

            if (prev != ExpeditionStatus.inactive) end = true;
        }
        else if (status == ExpeditionStatus.inactive)
        {
            status = ExpeditionStatus.queued;
            statusTooltip = "party expedition queued";
        }

        if (status != ExpeditionStatus.inactive)
        {
            if (scr_System_Time.current.getCurrentTime().Hour == startHour)
            {
                if (status == ExpeditionStatus.queued)
                {
                    status = ExpeditionStatus.gathering;
                    statusTooltip = "gathering expedition members";
                    begin = true;
                }
            }
            else if (status == ExpeditionStatus.gathering)
            {
                status = ExpeditionStatus.queued;
                statusTooltip = "gathering expedition members failed, retry tomorrow";
                end = true;
            }
        }

        if (status == ExpeditionStatus.gathering)
        {
            bool wait = false;
            foreach(var c in FactionOwner.ManagedChara)
            {
                if (scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID) != this.FactionOwner_Party.OwnerFaction.MainExit) wait = true;
            }
            if (!wait) status = ExpeditionStatus.active;
        }

        if (status == ExpeditionStatus.resting) end = true;

        if (end)
        {
            foreach (var i in this.FactionOwner.ManagedChara)
            {
                i.FactionManager.RemoveFromParty(this.FactionOwner);
            }
        }
        else if (begin)
        {
            foreach (var i in this.FactionOwner.ManagedChara)
            {
                i.FactionManager.AddToParty(this.FactionOwner, Manageable_GuestStatus.Member, false);
            }
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

    public bool expeditionActive = false;

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
    public void SetExpedition(Expedition exp, int startHour)
    {
        this.Expedition = exp;
        this.startHour = exp.HasStartHour ? exp.ForceStartHour : startHour;
    }

    /// <summary>
    /// Please call this from Party
    /// </summary>
    public void SetActive()
    {
        this.expeditionActive = true;
        this.status = ExpeditionStatus.queued;
        this.statusTooltip = "activated";

        UpdateStatus();
    }
    /// <summary>
    /// Please call this from Party
    /// </summary>
    public void EndExpedition()
    {
        this.expeditionActive = false;
    }

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

    public bool isActive { get {
            return this.Expedition != null && this.status >= ExpeditionStatus.queued && this.status != ExpeditionStatus.resting; } }

    [JsonIgnore]
    public Manageable_Party FactionOwner_Party { get { return FactionOwner as  Manageable_Party; } }

    public override bool UpdateActorPackage(Character_Trainable c, out string ss)
    {
        /*
             if character hour has com setting, try get com setting job
            else get random
         */
       // RefreshValidCOMs(true);
        ss = "(Job Expedition): ";
        // actor have job but don't have a action package registered.
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

        if (charaRoom.RefID != parentFaction.MainExit.RefID)
        {
            //Debug.Log("JobFurniture : trying to add pathing package to ["+c.FirstName+"]");
            // 1 - if actor not in job room, set go to room.
            // make movement package
            if (charaRoom.RefID != parentPlusFaction.MainExit.RefID)
            {
                AddResult(LocalizeDictionary.QueryThenParse("exp_event_gathering"), new List<string>(), new List<int> { c.RefID });
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
                if (parentFaction.Job.status == ExpeditionStatus.active)
                {
                    AddResult(LocalizeDictionary.QueryThenParse("exp_event_departure"), new List<string>(), new List<int> { c.RefID });
                    ActionPackage_TeleportTo package = new ActionPackage_TeleportTo(this, c.RefID, parentFaction.MainExit.RefID);
                    if (!package.Validate())
                    {
                        ss += "actor pathing package creation failed ||";
                        return false;
                    }
                    ss += "actor pathing created ||";
                    AddPackage(new List<ActionPackage>() { package });
                }
                else
                {
                    ss += "waiting for group gathering ||";
                }
                return true;
            }
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