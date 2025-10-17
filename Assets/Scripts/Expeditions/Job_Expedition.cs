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
            var newstring = EventDescription.Replace("$names$", String.Join(", ", names));
            return newstring;

        }
    }

    public SerializableEventPackage unresolved = null;
    public string resolveEventName = "";
    public string resolveMessage = "";
    public List<string> resolveTooltips = new List<string>();

    public void NotifyCharaExit(int refID)
    {
        if (unresolved == null || unresolved.isResolved) return;
        if (Characters.Remove(refID))
        {
            //Debug.LogError($"NotifyCharaExit {scr_System_CampaignManager.current.FindInstanceByID(refID).CallName} in {FullDescription} original {EventDescription}");
            unresolved.NotifyCharaExit(refID);
        }
    }
    public bool NotifyCharaReturn(int refID)
    {
        if (unresolved == null || unresolved.isResolved) return false;
        if (!Characters.Contains(refID)) Characters.Add(refID);
        //Debug.LogError($"NotifyCharaReturn {refID}");
        return true;
    }
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
    public bool overrideTargetGen = false;
    public List<Event.GenerationParameters> targetGens = new List<Event.GenerationParameters>();
    [JsonIgnore]
    public bool isValid
    { get
        {
            return eventID != "";
        }
    }

    [JsonIgnore]
    public string DisplayName { get { return LocalizeDictionary.QueryThenParse(eventID); } }
    public void NotifyCharaExit(int refID)
    {
        foreach(var i in Targets)
        {
            i.Value.Remove(refID);
        }
    }
    public bool isResolved = false;

}


public class Job_Expedition : Job
{
    [JsonIgnore]
    public override string DisplayName
    {
        get
        {
            return LocalizeDictionary.QueryThenParse($"ui_management_expeditionJob_{this.status}")
                    .Replace("$expName$", this.Expedition == null ? "-" : Expedition.Base.DisplayName);
        }
    }

    [JsonIgnore]
    public string RemainingTime
    {
        get
        {
            if (this.RemainingMinutes < 0) return "";
            else return LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_remainingTime")
                    .Replace("$time$", $"{((double)this.RemainingMinutes / 60.0).ToString("F1")}");
        }
    }
    [JsonIgnore]
    public string RemainingProgress
    {
        get
        {
            if (Expedition == null || Expedition.ExploreRate < 0 || Expedition.Base.MaxExplorationRate <= 0) return "";
            
            return LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_remainingProgress")
                    .Replace("$rate$", ((double)(Expedition.Base.MaxExplorationRate - Expedition.ExploreRate) / Expedition.Base.MaxExplorationRate).ToString("P1"));
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

    public override void AddActor(int charaRef, string priorityCOMID = "", string priorityCOMTag = "")
    {
        base.AddActor(charaRef, priorityCOMID, priorityCOMTag);
        if (isActive && hasUnresolvedResult)
        {
            foreach (var i in ExpeditionResults)
            {
                foreach (var result in i.Value)
                {
                    if (!result.NotifyCharaReturn(charaRef)) continue;
                    if (result.unresolved.Targets.TryGetValue("party", out var partyList))
                    {
                        if (!partyList.Contains(charaRef))   partyList.Add(charaRef);
                    }
                    else result.unresolved.Targets.Add("party", new List<int> { charaRef });

                    string key = "";

                    switch (FactionOwner_Party.GetTeamComp(charaRef))
                    {
                        case Manageable_Party.PartyComposition.frontline:
                            key = "teamA_frontline";
                            break;
                        case Manageable_Party.PartyComposition.backline:
                            key = "teamA_backline";
                            break;
                        default: break;
                    }
                    
                    if (key != "")
                    {
                        if (result.unresolved.Targets.TryGetValue(key, out var formation))
                        {
                            if (!formation.Contains(charaRef)) formation.Add(charaRef);
                        }
                        else result.unresolved.Targets.Add(key, new List<int> { charaRef });
                    }
                }
            }
        }

        UpdateStatus(-1, -1, false);
    }
    public void DumpLogInto(Job_Expedition other)
    {
        foreach(var i in this.ExpeditionResults)
        {
            other.ExpeditionResults.Add(i.Key, i.Value);
        }
        this.ExpeditionResults.Clear();
    }

    public ExpeditionMessageEntry AddResult(string s, List<string> tags, List<Character_Trainable> chara, bool registerMemory = false)
    {
        var charaRefs = new List<int>();
        foreach (var c in chara)
        {
            charaRefs.Add(c.RefID);
        }

        return AddResult(s, tags, charaRefs, registerMemory);
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

    public void UpdateStatus(int currentHour = -1, int currentMinute = -1, bool tickPackage = true)
    {
        if (currentHour == -1 || currentMinute == -1)
        {
            var cur = scr_System_Time.current.getCurrentTime();
            currentHour = cur.Hour;
            currentMinute = cur.Minute;
        }

        if (tickPackage) this.packageCooldown = Math.Max(this.packageCooldown - 1, 0);
        if (!this.FactionOwner_Party.isPlayerFaction) return;
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
                    AddResult(LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_start"), new List<string>(), new List<int>());
                    properExitRefs.Clear();
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
                    var memstr = LocalizeDictionary.QueryThenParse("exp_event_departure_memory").Replace("$loc$", this.Expedition.Base.DisplayName);
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
            if (RemainingMinutes != -1)
            {
                if (RemainingMinutes > 0 && tickPackage) RemainingMinutes = Math.Max(0, RemainingMinutes - 1);
                if (RemainingMinutes == 0) this.status = ExpeditionStatus.returning;
            }
            if (this.Expedition.ExploreRate == 0)
            {
                this.status = ExpeditionStatus.returning;
            }

            if (this.actorRefID.Count < 1 && scr_System_CampaignManager.current.CharaInRoom(this.ParentRoom.RefID).Count < 1)
            {
                this.status = ExpeditionStatus.returning;
                this.AddResult(LocalizeDictionary.QueryThenParse("ui_management_expedition_jobInterruptMIA"), new List<string>(), new List<Character_Trainable>());
            }
        }

        if (this.status == ExpeditionStatus.returning && this.actorRefID.Count < 1)
        {
            var charaList = scr_System_CampaignManager.current.CharaInRoom(this.ParentRoom.RefID);
            var charaCount = charaList.Count;
            if (charaCount > 0)
            {
                foreach (var c in charaList)
                {
                    if (c.isTemporaryActor && (c.CurrentJob == null || c.CurrentJobRefID == -1) && !c.FactionManager.HasPlayerFaction)
                    {
                       // Debug.Log($"Checking Charalist {c.CallName} is removable");
                        charaCount--;
                    }
                }
            }

            if (charaCount < 1)
            {
                properExitRefs.Clear();
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
    }
    /// <summary>
    /// Return Parent FactionOwner (Party) Main Exit (camp room)
    /// </summary>
    [JsonIgnore]
    public override Room_Instance ParentRoom
    {
        get
        {
            return FactionOwner_Party.MainExit;
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
    public void SetExpedition(ExpeditionInstance exp)
    {
        this.Expedition = exp;
    }

    public int RemainingMinutes = -1;


    [JsonProperty] int expeditionRefID = -1;
    ExpeditionInstance _exp = null;

    public override void RemoveActor(int charaRef)
    {
        for (int index = packages_previous.Count - 1; index >= 0; index--)
        {
            var p = packages_previous[index];
            if (p.actorRefs.Contains(charaRef))
            {
                p.DisablePackage();
                if (! scr_System_CampaignManager.current.Unregister(p))
                {
                    Debug.LogError($"EXPEDITION REMOVEACTOR Unregister ERROR package {p.DisplayName}");
                }
                else
                {
                    Debug.Log($"EXPEDITION REMOVEACTOR disabling package {p.DisplayName}");
                }
                packages_previous.RemoveAt(index);
            }
        }
        for (int index = packages_current.Count - 1; index >= 0; index--)
        {
            var p = packages_current[index];
            if (p.actorRefs.Contains(charaRef))
            {
                p.DisablePackage();
                if (!scr_System_CampaignManager.current.Unregister(p))
                {
                    Debug.LogError($"EXPEDITION REMOVEACTOR Unregister ERROR package {p.DisplayName}");
                }
                else
                {
                    Debug.Log($"EXPEDITION REMOVEACTOR disabling package {p.DisplayName}");
                }
                packages_current.RemoveAt(index);
            }
        }
        base.RemoveActor(charaRef);
    }

    [JsonIgnore]
    public ExpeditionInstance Expedition
    {
        get
        {
            if (_exp == null && expeditionRefID != -1)
            {
                _exp = scr_System_CampaignManager.current.FindExpeditionByID(expeditionRefID);
            }
            return _exp;
        }
        set
        {
            _exp = value;
            expeditionRefID = value == null ? -1 : value.RefID;
        }
    }

    [JsonIgnore]
    public bool hasUnresolvedResult
    {
        get
        {
            foreach(var i in this.ExpeditionResults.Values)
            {
                foreach (var j in i) if (j.unresolved != null) return true;
            }
            return false;
        }
    }
    [JsonIgnore] public bool canReturn { get { return isActive && this.status == ExpeditionStatus.returning && !hasUnresolvedResult; } }
    [JsonIgnore] public bool isResting { get { return isActive && this.status == ExpeditionStatus.resting; } }
    [JsonIgnore] public bool isActive { get {
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
                    if (Expedition != null && Expedition.DescriptionText.Count > 0) return LocalizeDictionary.QueryThenParse(Utility.GetRandomElement(Expedition.DescriptionText)).Replace("$names$", String.Join(", ", ActorNames));
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

    public bool canExit(int charaRef)
    {
        return properExitRefs.Contains(charaRef);
    }
    public List<int> properExitRefs = new List<int>();
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
        var parentPlusFaction = FactionOwner.FactionOwnerRoot;

        if (isActive && status == ExpeditionStatus.returning && !canReturn)
        {
            // dont do anything
            return true;
        }
        else if (canReturn)
        {
            
            int exitRef = -1;
            if (parentPlusFaction.MainExit != null && parentPlusFaction.isMember(c.RefID)) exitRef = parentPlusFaction.MainExit.RefID;

            if (exitRef == -1)
            {
                if (!this.Expedition.Base.CanBeRescued) Debug.LogError("ERROR!!!!");

                // require rescue
                return true;
            }
            else if (charaRoom.RefID != exitRef)
            {   // return chara
                AddResult(LocalizeDictionary.QueryThenParse("exp_event_returning"), new List<string>(), new List<int> { c.RefID });
                ActionPackage_TeleportTo package = new ActionPackage_TeleportTo(this, c.RefID, exitRef);
                if (!package.Validate())
                {
                    ss += "actor pathing package creation failed ||";
                    return false;
                }
                ss += "actor pathing created ||";
                AddPackage(new List<ActionPackage>() { package });

                var memStr = LocalizeDictionary.QueryThenParse("exp_event_return_memory").Replace("$loc$", Expedition.Base.DisplayName);
                var newMem = new MemInstance(new List<int>(), new List<string>(), "", -1, -1, true, Memory_Response.Accept, Memory_Attitude.Neutral, memStr);

                var entry = c.Memory.AddEntry(newMem, new List<string>() { "expeditionEnd" });
                entry.disableRoomName = true;

                return true;
            }
            else
            {   // release chara
                //Debug.LogError($"Error fail to return {c.CallName} from party {FactionOwner_Party.FullFactionDisplayName}");
                properExitRefs.Add(c.RefID);
                return false;
            }
        }
        else if (charaRoom.RefID != parentFaction.MainExit.RefID)
        {
            //Debug.Log("JobFurniture : trying to add pathing package to ["+c.FirstName+"]");
            // 1 - if actor not in job room, set go to room.
            // make movement package
            if (parentPlusFaction == null || parentPlusFaction.MainExit == null)
            {
                Debug.LogError($"{c.CallName} UpdateActorPackage Error!: {FactionOwner.FactionDisplayName} hasNullOwner? {parentPlusFaction == null} hasNullMainExit? {(parentPlusFaction == null || parentPlusFaction.MainExit == null)}");
                return false;
            }
            else if (charaRoom.RefID != parentPlusFaction.MainExit.RefID)
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
        else if (FactionOwner_Party.isPrisoner(c))
        {
            ss += "is being imprisoned, cannot explore ||";
            return true;
        }
        else if (RemainingMinutes != 0 && Expedition.ExploreRate != 0)
        {

            //Debug.Log("JobFurniture : [" + c.FirstName + "] at work location, adding job command with [" + validCOMs.Count + "] valid jobCOMs [" + String.Join(",", s) + "]");
            // 2 - if actor is in room, set COM package
            // make COM package
            var list1 = MakePackages(c);

            var pl1 = list1.Count > 0 ? Utility.GetRandomElement(list1) : null;

            if (pl1 != null)
            {
                AddPackage(new List<ActionPackage>() { pl1 });
                ss += $"creating package [{pl1.DescriptionText(c.RefID)}] among [{list1.Count}]";
                return true;
            }
            else
            {
                ss += "actor has not valid command or has completed all commands";
                return false;
            }
        }
        else
        {
            // idle
            return true;
        }
    }

    public void StartCooldown()
    {
        if (this.Expedition == null || this.FactionOwner_Party == null) return;
        this.packageCooldown = ExpeditionUtility.Cooldown(Expedition, this.FactionOwner_Party);
        Debug.Log($"Expedition cooldown set to: {packageCooldown}");
    }

    [JsonProperty] protected int packageCooldown = 0;

    /// <summary>
    /// Make a single random package
    /// </summary>
    /// <param name="c"></param>
    /// <param name="allowInvalid"></param>
    /// <returns></returns>
    public override List<ActionPackage> MakePackages(Character_Trainable c, bool allowInvalid = false, List<string> debug = null)
    {
        //Debug.Log("JobFurniture : [" + c.FirstName + "] at work location, adding job command with [" + validCOMs.Count + "] valid jobCOMs [" + String.Join(",", s) + "]");
        // 2 - if actor is in room, set COM package
        // make COM package
        List<ActionPackage> results = new List<ActionPackage>();

        int loop = 0;
        while (loop < 10)
        {
            loop++;
            var newAP = ExpeditionUtility.RandEvent(Expedition, this.FactionOwner_Party);
            if (newAP == null) continue;
            if (!newAP.Actors.Contains(c)) continue;
            newAP.ReEstablishParent(this);// = this;
            if (newAP.Validate() || allowInvalid)
            {
                results.Add(newAP);
                break;
            }
        }

        return results;
    }

    public override string GetJobDescription(int charaRef)
    {
        return LocalizeDictionary.QueryThenParse("chara_currentjob_expedition");
        //return base.GetJobDescription(charaRef);
    }
}