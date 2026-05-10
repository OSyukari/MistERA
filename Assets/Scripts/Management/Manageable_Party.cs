using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using System;
using System.Linq;
using QuikGraph;

public enum ExpeditionStatus
{
    Inactive,
    Queued,
    Gathering,
    Ongoing,
    Resting,
    Returning
}
public enum PartyStatus
{
    Unavailable,
    Inactive,
    Active
}

public enum PartyAvailability
{
    Unavailable,
    Inactive,
    Active
}


public class Manageable_Party : I_IsJobGiver
{



    [JsonProperty] protected int rallyJobID = -1;
    protected Job_MoveLocation _rallyJob = null;
    [JsonIgnore]
    public Job_MoveLocation FactionRallyJob
    {
        get
        {
            if (_rallyJob == null && this.MainExit != null)
            {
                if (rallyJobID == -1)
                {
                    _rallyJob = new Job_MoveLocation();
                    _rallyJob.FactionOwner = this;
                    rallyJobID = scr_System_CampaignManager.current.Register(_rallyJob);
                }
                else
                {
                    _rallyJob = scr_System_CampaignManager.current.FindJobInstanceByID(rallyJobID) as Job_MoveLocation;
                }
            }
            return _rallyJob;
        }
    }

    [JsonIgnore] public Manageable Faction { get { return null; } }
    [JsonIgnore]
    public string ExpeditionName
    {
        get
        {
            return this.Job.ExpeditionName;
        }
    }

    [JsonIgnore] public bool CanStartExpedition { get { return this.Job.Expedition != null && GetAvailability(out string tootlip) == PartyAvailability.Inactive; } }
    [JsonIgnore] public bool CanResolveExpedition { get { return this.Job.Expedition != null && this.Job.isActive; } }
    public void SetExpedition(ExpeditionInstance exp)
    {
        this.Job.SetExpedition(exp);
        this._recurringCooldown = 0;
        this._recurringTicked = false;
    }
    [JsonIgnore]
    public bool isMealHour { get { return this.OwnerFaction.isMealHour; } }

    public bool isMealHourAt(int hour)
    {
        return this.OwnerFaction.isMealHourAt(hour);
    }
    public bool AllowPassNight = true;
    public int RecurringCooldown = 0;
    public bool IsRecurring = false;
    public bool PrioritizeResting = true;
    public int StartHour = 8;
    [JsonIgnore] public int FinalStartHour
    {
        get
        {
            if (this.Job.Expedition == null || !this.Job.Expedition.Base.HasStartHour) return StartHour;
            return this.Job.Expedition.Base.ForceStartHour;
        }
    }

    bool _cachedSleepHours = false;
    List<int> _sleepHours = new List<int>();
    [JsonIgnore]
    public List<int> SleepHours
    { get
        {
            if (!_cachedSleepHours)
            {
                _sleepHours.Clear();
                _sleepHours = ComputeSleepHours_new();
                /*
                var maxSleepHour = 0;
                foreach (var i in this.ManagedChara) maxSleepHour = Math.Max(maxSleepHour, i.Stats.SleepHours);
                var home = this.OwnerFaction;
                var homeSleeHour = (home == null || !home.HasDayNight) ? 22 : home.NightStartHour;

                for (int j = 0; j < maxSleepHour; j++)
                {
                    _sleepHours.Add((homeSleeHour + j) % 24);
                }*/
            }
            return _sleepHours;
        } }

    private List<int> ComputeSleepHours_new()
    {
        var home = this.OwnerFaction;
        int wakeHour = (home != null && home.HasDayNight) ? home.DayStartHour : 6;

        int maxSleepHours = 0;
        foreach (var c in ManagedChara) maxSleepHours = Math.Max(maxSleepHours, c.Stats.SleepHours);

        var result = new List<int>();

        // Fill backward from wakeHour: NPC wakes at wakeHour, sleeps the preceding maxSleepHours hours
        for (int i = 1; i <= maxSleepHours; i++) result.Add((wakeHour - i + 24) % 24);

        return result;
    }

    [JsonIgnore] public int FinalDuration
    { get
        {
            List<int> pauseHours = new List<int>();
            pauseHours.AddRange(this.OwnerFaction.mealHours);

            var baseDuration = BaseDuration;

            if (AllowPassNight || baseDuration >= 24) 
            {
                pauseHours.AddRange(SleepHours);

            }
            pauseHours = Utility.Distinct(pauseHours);
            var error = 0;

            if (pauseHours.Count > 22) error = 999;

            var finalCount = 0;
            var startHour = this.FinalStartHour;

            while (baseDuration > 0 && error < 24)
            {
                if (!pauseHours.Contains(startHour))
                {
                    baseDuration -= 1;
                    error = 0;
                }
                else
                {
                    error++;
                }
                finalCount++;
                startHour = (startHour + 1) % 24;
            }

            return error >= 24 ? 999 : finalCount;
        }
    }
    [JsonIgnore]
    public int BaseDuration
    {
        get
        {
            var baseDuration = this.Job.Expedition == null ? 0 : this.Job.Expedition.Base.DurationHour;
            return baseDuration;
        }
    }

    public List<Job_Furniture> GetValidJobs_byTags(Character_Trainable chara, int currentHour, string tag, List<string> s = null, bool skipPrivate = false, bool shortestPathOnly = true, bool checkBlacklist = true, List<int> restrictRoomList = null)
    {
        //Debug.Log("Begin getvalidRecreation");

        List<Job_Furniture> possibleJobs;
        string ss = " (" + ID + ")";
        if (scr_System_CentralControl.current.isSafeMode && scr_System_Serializer.current.nsfwKeywords.Contains(tag)) return new List<Job_Furniture>();
        if (chara.Jail != null && chara.Jail.ownerJob != null)
        {

        }
        if (!FactionUtility.TryFindValidNonJobInstances(jobPosts, managedRoomRefs, out possibleJobs, chara, "", tag, checkBlacklist))
        {
            ss += $" found no valid [{tag}] instances offered by Furnitures from chara[{chara.FirstName}] currenthour[{currentHour}], checkBlacklist[{checkBlacklist}]";
            if (s != null) s.Add(ss);
            return new List<Job_Furniture>();
        }

        //if (chara.isImprisoned) Debug.Log($"Prisoner {chara.CallName} with {possibleJobs.Count} possible instances");

        if (skipPrivate)
        {
            possibleJobs.RemoveAll(x => x.ParentRoom.isRoomPrivate);
        }
        if (restrictRoomList != null)
        {
            possibleJobs.RemoveAll(x => x.ParentRoom == null || !restrictRoomList.Contains(x.ParentRoom.RefID));
        }

        if (!FactionUtility.TryValidateAllInstances(ref possibleJobs, chara))
        {
            ss += $" cannot pass validate check for any of the {tag} job instances";
            if (s != null) s.Add(ss);
            return new List<Job_Furniture>();
        }

        //if (chara.isImprisoned) Debug.Log($"Prisoner {chara.CallName} with {possibleJobs.Count} possible instances post validateall");

        //bool result = FactionUtility.GetValidPaths(ref possibleJobs, chara, ref ss, !shortestPathOnly);
        if (FactionUtility.GetValidPaths(ref possibleJobs, chara, ref ss, !shortestPathOnly))
        {
            //Debug.Log("GetValidPaths success after " + (DateTime.Now - startTime).TotalNanoseconds + "ms");
            return possibleJobs;
        }
        else
        {
            if (chara.isImprisoned) Debug.Log($"Prisoner {chara.CallName} no validpaths");
            //Debug.Log("GetValidPaths failed after " + (DateTime.Now - startTime).TotalNanoseconds + "ms");
            return new List<Job_Furniture>();
        }

    }
    [JsonProperty] protected int _recurringCooldown = 0;
    [JsonProperty] protected bool _recurringTicked = false;
    public void ExpeditionEnd()
    {
        this._recurringCooldown = this.RecurringCooldown;
        // clear all prisoners
        var list = charaGuestStatus.Keys.ToList();
        foreach(var i in list)
        {
            var status = charaGuestStatus[i];
            if (status == Manageable_GuestStatus.Prisoner || status == Manageable_GuestStatus.Visitor)
            {
                var c = scr_System_CampaignManager.current.FindInstanceByID(i);
                RemoveFromFaction(c);
            }
        }
        // clear all temporaryactors
        var list2 = new List<int>( scr_System_CampaignManager.current.Map.CharaInRoom(this.Room.RefID));
        //Debug.LogError($"ExpeditionEnd clearing room actor {String.Join("|", list2)}");
        foreach (int charaRef in list2)
        {
            var c = scr_System_CampaignManager.current.FindInstanceByID(charaRef);

            if (c.isTemporaryActor && (c.CurrentJob == null || c.CurrentJobRefID == -1) && !c.FactionManager.HasPlayerFaction)
            {
                Debug.LogError($"Detected chara {c.CallName} inside {this.FullFactionDisplayName}, unregistering");
                scr_System_CampaignManager.current.Unregister(c);
            }
            else
            {
                Debug.LogError($"ERROR character {c.CallName} stuck inside {this.FullFactionDisplayName}");
            }
        }
    }

    /// <summary>
    /// Use this on AI factions generated during events for MIA and kidnappings<br/>
    /// Remember to register actor before next update cycle, otherwise the job will set status to off due to no actor
    /// </summary>
    /// <returns></returns>
    public void ForceStartExpedition()
    {
        this.Job.ExpeditionResults.Clear();
        this._recurringTicked = true;
        Job.RemainingMinutes = Job.Expedition.Base.DurationHour > 0 ? Job.Expedition.Base.DurationHour * 60 : -1;
        Job.status = Job_Expedition.ExpeditionStatus.active;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="forceStart">Only use this on AI factions</param>
    /// <returns></returns>
    public bool TryStartExpedition()
    {
        if (_recurringCooldown <= 0 && !this._recurringTicked)
        {
            this.Job.ExpeditionResults.Clear();
            this._recurringTicked = true;

            foreach(var c in this.ManagedChara)
            {
                if (this.charaPartyComposition.ContainsKey(c.RefID) && !c.canFight)
                {
                    this.Job.AddResult("failed to start expedition due to chara cannot fight", new List<string>(), new List<Character_Trainable>() { c });
                    return false;
                }
            }

            Job.RemainingMinutes = Job.Expedition.Base.DurationHour > 0 ? Job.Expedition.Base.DurationHour * 60 : -1;
            return true;
        }
        else if (!this._recurringTicked)
        {
            this._recurringCooldown -= 1;
            this._recurringTicked = true;
        }
        return false;
    }

    public void OnDayUpdate_0()
    {
        this._recurringTicked = false;
    }

    bool _cache_isplayerFaction_result = false;
    bool _cached_isplayerFaction = false;

    [JsonIgnore] public bool isPlayerFaction { get {
            return this.OwnerFaction.isPlayerFaction; } }

    [JsonIgnore] public bool isPlayerRelatedFaction
    {
        get
        {
            if (this.OwnerFaction.isPlayerFaction) return true;
            if (!_cached_isplayerFaction)
            {
                _cache_isplayerFaction_result = false;
                foreach (var i in this.charaGuestStatus.Keys)
                {
                    if (_cache_isplayerFaction_result) break;
                    var c = scr_System_CampaignManager.current.FindInstanceByID(i);
                    foreach (var m in c.FactionManager.Factions)
                    {
                        if (m.isPlayerFaction)
                        {
                            _cache_isplayerFaction_result = true;
                            break;
                        }
                    }
                }
            }
            return _cache_isplayerFaction_result;
        }
    }

    public Manageable_GuestStatus GetStatus(Character_Trainable c)
    {
        if (this.charaGuestStatus.TryGetValue(c.RefID, out var status)) return status;
        else return Manageable_GuestStatus.Hidden;
    }

    public PartyAvailability GetAvailability(out string tooltip)
    {
        if (isActive)
        {
            tooltip = LocalizeDictionary.QueryThenParse("ui_management_partyAvailability_active");
            return PartyAvailability.Active;
        }

        tooltip = "";
        List<string> ttips = new List<string>();
        if (this.ManagedRefs.Count < 1)
        {
            tooltip = LocalizeDictionary.QueryThenParse("ui_management_partyAvailability_nomember");
            return PartyAvailability.Unavailable;
        }
        bool canAct = true;
        foreach(var i in this.ManagedChara)
        {
            if (!i.canAct)
            {
                canAct = false;
                ttips.Add(LocalizeDictionary.QueryThenParse("ui_management_partyAvailability_cannotAct").Replace("$name$", i.FirstName));
            }
            else if (i.FactionManager.CurrentActiveParty != null && i.FactionManager.CurrentActiveParty != this)
            {
                canAct = false;
                ttips.Add(LocalizeDictionary.QueryThenParse("ui_management_partyAvailability_occupied").Replace("$name$", i.FirstName));
            }
        }
        if (!canAct)
        {
            tooltip = String.Join("\n", ttips);
            return PartyAvailability.Unavailable;
        }
        else
        {

            tooltip = LocalizeDictionary.QueryThenParse("ui_management_partyAvailability_inactive");
            return PartyAvailability.Inactive;
        }
    }

    /// <summary>
    /// Use GetAvailability for failure tooltip
    /// </summary>
    [JsonIgnore]
    public PartyAvailability Availability { get {
            return GetAvailability(out var s); 
        } }


    [JsonIgnore]
    public bool isActive { get
        {
            return Job != null && Job.isActive;
        } }
    [JsonIgnore]
    public bool hasExpeditionSet
    {
        get
        {
            return Job != null && Job.Expedition != null;
        }
    }

    public string ID;

    [JsonProperty]
    protected string ownerFactionID = "";
    Manageable _ownerFaction = null;

    [JsonIgnore]
    public Manageable OwnerFaction
    {
        get
        {
            if (_ownerFaction == null) _ownerFaction = scr_System_CampaignManager.current.FindFactionByID(ownerFactionID);
            return _ownerFaction;
        }
        set
        {
            _ownerFaction = value;
            ownerFactionID = _ownerFaction == null ? "" : _ownerFaction.ID;
        }
    }

    public enum PartyComposition
    {
        none,
        frontline,
        backline
    }

    public PartyComposition GetTeamComp(int refID)
    {
        if (this.charaPartyComposition.TryGetValue(refID, out var value)) return value;
        return PartyComposition.none;
    }
    public void SetTeamComp(int refID, PartyComposition value)
    {
        this.charaPartyComposition[refID] = value;
    }

    protected void InternalUpdate()
    {
        _managedChara = null;
        _cached_isplayerFaction = false;
        _cachedSleepHours = false;
    }

    /// <summary>
    /// Can also be used to change guest status
    /// </summary>
    /// <param name="c"></param>
    /// <param name="guestStatus"></param>
    public void AddToFaction(Character_Trainable c, Manageable_GuestStatus guestStatus, bool sendEvent = true)
    {
        //c.AddToFaction(this);
        if (!charaGuestStatus.ContainsKey(c.RefID)) charaGuestStatus.Add(c.RefID, guestStatus);
        else charaGuestStatus[c.RefID] = guestStatus;

        if (!charaPartyComposition.ContainsKey(c.RefID)) charaPartyComposition.Add(c.RefID, PartyComposition.frontline);

        // set manager roles
        if (!OwnerFaction.ManagedChara.Contains(c)) OwnerFaction.AddToFaction(c, guestStatus, false);

        if (sendEvent && guestStatus == Manageable_GuestStatus.Prisoner)
        {
            FactionUtility.SendImprisonEvent(this, c);
            if (this.Job.isActive)
            {
                var sss = "$names$ captured $target$".Replace("$target$", c.FirstName);
                this.Job.AddResult(sss, new List<string>(), this.Job.Actors, true);
            }
        }

        c.FactionManager.AddPartyTracker(this);
        InternalUpdate();
    }

    [JsonProperty] protected bool _remove_dirty = false;
    protected bool DeletingBlocker = false;
    public void RemoveFromFaction(Character_Trainable c)
    {
        if (c == null) return;
        charaGuestStatus.Remove(c.RefID);

        c.FactionManager.RemovePartyTracker(this);
        InternalUpdate();

        if (DeletingBlocker) return;
        CleanupDetection();
    }

    public bool CleanupDetection()
    {
        if (isPlayerFaction) return false;

        bool delete = true;
        foreach (var i in ManagedRefs)
        {
            var cc = scr_System_CampaignManager.current.FindInstanceByID(i);
            if (cc == null) continue;   // chara already deleted
            if (!cc.isTemporaryActor || cc.FactionManager.HasPlayerFaction || cc.CurrentJob != null)
            {
                delete = false;
                break;
            }
        }

        if (delete)
        {
            DeletingBlocker = true;
            Debug.Log($"Cleanup: Removing Subfaction {this.FullFactionDisplayName}");
            for (int i = this.ManagedRefs.Count - 1; i >= 0; i--)
            {
                var cc = scr_System_CampaignManager.current.FindInstanceByID(this.ManagedRefs[i]);
                if (cc == null) continue;
                this.ManagedRefs.RemoveAt(i);
                scr_System_CampaignManager.current.Unregister(cc);
            }
            scr_System_CampaignManager.current.Unregister(Job);
            scr_System_CampaignManager.current.UnregisterRoom(MainExit.RefID);
            Inventory.Destroy();
            this.OwnerFaction.RemoveSubfaction(this);
            return true;
        }
        else return false;
        
    }

    public List<Job_Furniture> GetValidJobs_Meal(Character_Trainable chara, int currentHour, List<string> s = null)
    {
        List<Job_Furniture> possibleJobs;
        string ss = " (" + ID + ")";
        if (!this.OwnerFaction.mealHours.Contains(currentHour)) return new List<Job_Furniture>();

        if (!FactionUtility.TryFindValidNonJobInstances(nonjobPosts, managedRoomRefs, out possibleJobs, chara, "", "food_meal", false))
        {
            ss += " found no valid [food_meal] instances offered by Furnitures";
            if (s != null) s.Add(ss);
            return new List<Job_Furniture>();
        }
        else if (!FactionUtility.TryValidateAllInstances(ref possibleJobs, chara))
        {
            ss += " cannot pass validate check for any of the Meal job instances";
            if (s != null) s.Add(ss);
            return new List<Job_Furniture>();
        }
        else
        {
            if (FactionUtility.GetValidPaths(ref possibleJobs, chara, ref ss)) return possibleJobs;
            else return new List<Job_Furniture>();
        }
    }

    public List<Job_Furniture> GetValidJobs_Jobs(Character_Trainable chara, int currentHour, ref string s, bool checkBlacklist = true)
    {
        return new List<Job_Furniture>();
    }

    public List<Job_CharaCOM> GetValidCharaCOMByTag(Character_Trainable chara, string tag, ref string ss, bool restrainedOnly = true)
    {
        List<Job_CharaCOM> possibleJobs = new List<Job_CharaCOM>();
        foreach (var i in ManagedChara)
        {
            if (i.RefID != chara.RefID &&
                (scr_System_CentralControl.current.CanInteractWith(chara.RefID, i.RefID)) &&
                (!restrainedOnly || i.isImprisoned || i.isRestrained) &&
                (i.InteractionJob.HasAvailableCOMwithCOMTags(new List<string>() { tag }))
                )
            {
                possibleJobs.Add(i.InteractionJob);
            }
        }

        if (GetValidPaths(ref possibleJobs, chara, ref ss))
        {
            return possibleJobs;
        }
        else return new List<Job_CharaCOM>();
    }

    protected bool GetValidPaths(ref List<Job_CharaCOM> possibleJobs, Character_Trainable chara, ref string s, bool randInsteadofShortest = false)
    {
        string ss = "";
        List<int> rooms = new List<int>();
        foreach (var x in possibleJobs) rooms.Add(x.ParentRoom.RefID);
        SortedDictionary<int, Dictionary<int, IEnumerable<TaggedEdge<int, Door_Instance>>>> sortedList = scr_System_CampaignManager.current.Map.FilterValidPathsOptimized(chara, rooms, randInsteadofShortest);
        var list = sortedList.Count > 0 ? sortedList.Last().Value : new Dictionary<int, IEnumerable<TaggedEdge<int, Door_Instance>>>();
        possibleJobs = possibleJobs.FindAll(x => list.ContainsKey(x.ParentRoom.RefID));

        for (var i = possibleJobs.Count - 1; i >= 0; i--)
        {

            // just in case thing is not pathable
            IEnumerable<TaggedEdge<int, Door_Instance>> path = list[possibleJobs[i].ParentRoom.RefID];
            if (path != null || scr_System_CampaignManager.current.Map.FindRoomByChara(chara.RefID).RefID == possibleJobs[i].ParentRoom.RefID)
            {
                // continue
            }
            else
            {
                possibleJobs.RemoveAt(i);

                var a = scr_System_CampaignManager.current.Map.FindRoomByChara(chara.RefID);
                var b = possibleJobs[0].ParentRoom;
                ss += " found no pathable job instances from [" + a.RefID + " " + a.DisplayName + "] to [" + b.RefID + " " + b.DisplayName + "]";
                if (s != null) s += ss;
            }
        }

        if (possibleJobs.Count > 0)
        {
            //s += " setting job instance to " + possibleJobs[0].RefID + " with coms [" + String.Join(",", possibleJobs[0].allusableCOMStrings) + "]in room " + possibleJobs[0].ParentRoom.DisplayName;
            //chara.ChangeCurrentJob(possibleJobs[0]);
            return true;
        }
        else
        {
            ss += " possibleJobs.Count <= 0";
            if (s != null) s += ss;
            return false;
        }
    }

    public List<Job_Furniture> GetValidJobsByCOMID(Character_Trainable chara, string comID, List<string> s = null, bool allowJobPostSearch = true, bool allowNonJobPostSearch = true, List<int> restrictRoomList = null)
    {
        string ss = " (" + ID + ")";

        List<Job_Furniture> possibleJobs;
        COM targetCOM = scr_System_Serializer.current.GetByNameOrID_COM(comID);
        if (targetCOM == null) return null;

        if (targetCOM.comTags.Contains("job") && allowJobPostSearch)
        {
            if (!FactionUtility.TryFindValidJobInstances(jobPosts, out possibleJobs, null, chara, comID, false))
            {
                ss += " found no valid [" + comID + "] instances offered by Furnitures job";
                if (s != null) s.Add(ss);
                return null;
            }
        }
        else if (allowNonJobPostSearch)
        {
            if (!FactionUtility.TryFindValidNonJobInstances(nonjobPosts, managedRoomRefs, out possibleJobs, chara, comID, "", false))
            {
                ss += " found no valid [" + comID + "] instances offered by Furnitures nonjob";
                if (s != null) s.Add(ss);
                return null;
            }
        }
        else
        {
            ss += " all job post search for [" + comID + "] are disabled, aborted";
            if (s != null) s.Add(ss);
            return null;
        }


        if (!FactionUtility.TryValidateAllInstances(ref possibleJobs, chara))
        {
            ss += " cannot pass validate check for any of the offered [" + comID + "] job instances";
            if (s != null) s.Add(ss);
            return null;
        }
        else
        {
            if (FactionUtility.GetValidPaths(ref possibleJobs, chara, ref ss)) return possibleJobs;
            else return null;
        }
    }

    public List<Job_Furniture> GetValidJobs_Sleep(Character_Trainable chara, int currentHour, List<string> s = null)
    {
        List<Job_Furniture> possibleJobs;
        string ss = " (" + ID + ")";
        if (!FactionUtility.TryFindValidNonJobInstances(nonjobPosts, managedRoomRefs, out possibleJobs, chara, "com_furniture_sleep", "", false))
        {
            ss += " found no valid [com_furniture_sleep] instances offered by Furnitures";
            if (s != null) s.Add(ss);
            return new List<Job_Furniture>();
        }
        else if (!FactionUtility.TryValidateAllInstances(ref possibleJobs, chara))
        {
            ss += " cannot pass validate check for any of the Sleep job instances";
            if (s != null) s.Add(ss);
            return new List<Job_Furniture>();
        }
        else
        {
            if (FactionUtility.GetValidPaths(ref possibleJobs, chara, ref ss)) return possibleJobs;
            else return new List<Job_Furniture>();
        }
    }

    [JsonIgnore]
    public Dictionary<int, List<int>> ManagedRooms { get { return this.managedRoomRefs; } }
    [JsonIgnore] public Dictionary<COM, List<Job_Furniture>> NonjobPosts { get { return this.nonjobPosts; } }

    public List<Job_Furniture> GetValidJobs_nonJob_byTags(Character_Trainable chara, int currentHour, string tag, List<string> s = null, bool skipPrivate = false, bool shortestPathOnly = true, bool checkBlacklist = true, List<int> restrictRoomList = null)
    {
        //Debug.Log("Begin getvalidRecreation");
        List<Job_Furniture> possibleJobs;
        string ss = " (" + ID + ")";
        if (scr_System_Serializer.current.nsfwKeywords.Contains(tag)) return new List<Job_Furniture>();
        if (!FactionUtility.TryFindValidNonJobInstances(nonjobPosts, managedRoomRefs, out possibleJobs, chara, "", tag, checkBlacklist))
        {
            ss += $" found no valid tags [{tag}] instances offered by Furnitures from chara[" + chara.FirstName + "] currenthour[" + currentHour + "]";
            if (s != null) s.Add(ss);
            return new List<Job_Furniture>();
        }

        if (skipPrivate)
        {
            possibleJobs.RemoveAll(x => x.ParentRoom.isRoomPrivate);
        }
        if (restrictRoomList != null)
        {
            possibleJobs.RemoveAll(x => x.ParentRoom == null || !restrictRoomList.Contains(x.ParentRoom.RefID));
        }

        if (!FactionUtility.TryValidateAllInstances(ref possibleJobs, chara))
        {
            ss += $" cannot pass validate check for any of the {tag} job instances";
            if (s != null) s.Add(ss);
            return new List<Job_Furniture>();
        }


        //bool result = FactionUtility.GetValidPaths(ref possibleJobs, chara, ref ss, !shortestPathOnly);
        if (FactionUtility.GetValidPaths(ref possibleJobs, chara, ref ss, !shortestPathOnly))
        {
            //Debug.Log("GetValidPaths success after " + (DateTime.Now - startTime).TotalNanoseconds + "ms");
            return possibleJobs;
        }
        else
        {
            //Debug.Log("GetValidPaths failed after " + (DateTime.Now - startTime).TotalNanoseconds + "ms");
            return new List<Job_Furniture>();
        }
    }

    public void NotifyFurnitureChange(Room_Instance room)
    {
        RefreshRoomJobs();
    }

    /// <summary>
    /// Remove C from job unresolved results, remove from job, and internalUpdate
    /// </summary>
    /// <param name="c"></param>
    /// <param name="kidnapLoc"></param>
    public void NotifyCharaKidnapped(Character_Trainable c, Manageable_Party kidnapLoc, bool messageLoggin = true)
    {
        if (kidnapLoc != this)
        {
            if (messageLoggin) this.Job.AddResult(LocalizeDictionary.QueryThenParse("ui_management_expedition_notifyCharaMIA").Replace("$location$", kidnapLoc.FactionDisplayName), new List<string>(), new List<Character_Trainable>() { c });
        }

        this.Job.RemoveActor(c.RefID);
        InternalUpdate();
        //Debug.Log($"NotifyCharaKidnapped {c.CallName}, status {this.Job.status}, actors[{String.Join("|", this.Job.actorRefID)}], actorInRoom[{String.Join("|", scr_System_CampaignManager.current.CharaInRoom(MainExit.RefID))}] ");
        this.Job.UpdateStatus(-1, -1, false);
        foreach(var home in c.FactionManager.HomeFactions)
        {
            home.NotifyCharaKidnapped(c);
        }
    }

    public List<int> RoomOwners(int roomRef)
    {
        return this.ManagedRefs;
    }

    public bool skipTryGetJob(Character_Trainable c)
    {
        if (charaGuestStatus.TryGetValue(c.RefID, out var status))
        {
            return status == Manageable_GuestStatus.Hidden;
        }
        else return false;
    }
    public bool isPrisoner(Character_Trainable c)
    {
        return isPrisoner(c.RefID);
    }
    public bool isPrisoner(int c)
    {
        if (charaGuestStatus.TryGetValue(c, out var status))
        {
            return status == Manageable_GuestStatus.Prisoner;
        }
        else return false;
    }

    public RelationshipType GetRelationshipBetween(int self, int target, out bool isA)
    {
        isA = false;
        if (!ManagedRefs.Contains(self) || !ManagedRefs.Contains(target))
        {
            if (FactionOwnerRoot.ID == "AlwaysHostile")
            {
                return FactionOwnerRoot.Relationship_Enemy;
            }
            else
            {

                return null;
            }
        }

        if (isPrisoner(self) && !isPrisoner(target))
        {
            return FactionOwnerRoot.Relationship_Prisoner;
        }
        else if (!isPrisoner(self) && isPrisoner(target))
        {
            isA = true;
            return FactionOwnerRoot.Relationship_Prisoner;
        }
        else return FactionOwnerRoot.GetRelationshipBetween(self, target, out isA);
    }

    public bool isMember(Character_Trainable c)
    {
        return isMember(c.RefID);
    }
    public bool isMember(int refID)
    {
        if (charaGuestStatus.TryGetValue(refID, out var status))
        {
            return status < Manageable_GuestStatus.Visitor;
        }
        else return false;
    }

    [JsonProperty] protected Dictionary<int, PartyComposition> charaPartyComposition = new Dictionary<int, PartyComposition>();
    [JsonProperty] protected Dictionary<int, Manageable_GuestStatus> charaGuestStatus = new Dictionary<int, Manageable_GuestStatus>();
    [JsonIgnore] public List<int> ManagedRefs { get { return charaGuestStatus.Keys.ToList();}}
    List<Character_Trainable> _managedChara = null;
    [JsonIgnore]
    public List<Character_Trainable> ManagedChara
    {
        get
        {
            if (_managedChara == null)
            {
                _managedChara = new List<Character_Trainable>();
                foreach(var i in this.ManagedRefs) _managedChara.Add(scr_System_CampaignManager.current.FindInstanceByID(i));
            }
            return _managedChara;
        }
    }
    [JsonIgnore]
    public List<Character_Trainable> ManagedChara_Displayables
    {
        get
        {
            return ManagedChara.FindAll(x => GetStatus(x) != Manageable_GuestStatus.Hidden);
        }
    }
    [JsonProperty] protected Dictionary<int, List<int>> managedRoomRefs = null;
    public Manageable_Party() { }
    public Manageable_Party(Manageable owner, string id)
    {
        OwnerFaction = owner;
        this.ID = id;
        DisplayName = "new party";
        this._job = new Job_Expedition(this);
        this.innerJobRef = scr_System_CampaignManager.current.Register(this._job);


        _room = new Room_Instance(null, null);
        roomRef = scr_System_CampaignManager.current.Register(_room);

        managedRoomRefs = new Dictionary<int, List<int>>();
        managedRoomRefs.Add(Room.RefID, new List<int>());
        Room.AddFurniture("furniture_camping_site");
        Room.SetFaction(this);// = this;
        //RefreshRoomJobs();

        //if (room.isRoomPrivate) roomOwnerships.Add(room.RefID, new List<int>());

        InternalUpdate();
        RefreshRoomJobs();
    }

    protected void RefreshRoomJobs()
    {
        foreach (var j in Room.Jobs)
        {
            if (!(j is Job_Furniture)) continue;

            //if (!(kvp.Value as Job_Furniture).hasProductionJob) NonProductionJobs.Add(kvp.Value as Job_Furniture);
            foreach (var com in j.allusableCOMs) AddJobPost(com, j as Job_Furniture);
        }
    }

    public void ReEstablishParent(Manageable FactionOwner)
    {
        this.OwnerFaction = FactionOwner;
        this.Inventory.ReEstablishParent(this);
        InternalUpdate();
        RefreshRoomJobs();
    }

    protected void AddJobPost(COM com, Job_Furniture job)
    {
        job.FactionOwner = this;
        //if (!com.requirements.requirement.req_Doers.allowNPC) return; // this one excludes cryosleep so no

        if (!com.comTags.Contains("job"))
        {
            if (!nonjobPosts.ContainsKey(com)) nonjobPosts.Add(com, new List<Job_Furniture>());
            if (nonjobPosts[com].Find(x => x.RefID == job.RefID) == null) nonjobPosts[com].Add(job);
        }
        else
        {
            if (!jobPosts.ContainsKey(com)) jobPosts.Add(com, new List<Job_Furniture>());
            if (jobPosts[com].Find(x => x.RefID == job.RefID) == null) jobPosts[com].Add(job);
        }
    }

    Dictionary<COM, List<Job_Furniture>> nonjobPosts = new Dictionary<COM, List<Job_Furniture>>();
    Dictionary<COM, List<Job_Furniture>> jobPosts = new Dictionary<COM, List<Job_Furniture>>();

    [JsonProperty] protected int roomRef = -1;
    Room_Instance _room = null;
    [JsonIgnore] public Room_Instance Room
    { get
        {
            if (_room == null) _room = scr_System_CampaignManager.current.Map.GetRoomByRef(roomRef);
            return _room;
        } }

    [JsonProperty] protected string DisplayName = "";

    /// <summary>
    /// progress is negative, losing progress is positive
    /// </summary>
    /// <param name="progress"></param>
    /// <param name="relevantActors"></param>
    public void NotifyExpeditionProgress(int progress, List<int> relevantActors = null)
    {
        if (this.Job.Expedition == null)
        {
            Debug.LogError("error job expedition null");
            return;
        }
        // if this notice source is a require-rescue expedition, then cannot rescue self
        Job.Expedition.ModProgress(progress, this.Job.Expedition.Base.CanBeRescued );
       // Debug.Log($"{this.FullFactionDisplayName} modprogress {progress} lock? {this.Job.Expedition.Base.CanBeRescued} expRate [{Job.Expedition.ExploreRate}]");
        var baseExp = this.Job.Expedition.Base;
        if (baseExp.CanRescue && baseExp.keywords.Count > 0 && relevantActors != null && relevantActors.Count > 0)
        {
            var kidnappers = OwnerFaction.KidnapFactions;
            foreach(var k in kidnappers)
            {
                if (k.Job.Expedition == null) continue;
                var kexp = k.Job.Expedition;
                if (!kexp.Base.CanBeRescued) continue;
                if (kexp == this.Job.Expedition) continue;
                if (!Utility.ListContainsLoose(kexp.Base.keywords, baseExp.keywords)) continue;
                if (kexp.ModProgress(progress))
                {
                    string rescueText = LocalizeDictionary.QueryThenParse("ui_management_expedition_rescueEventText");
                    List<string> victimNames = new List<string>();
                    foreach(var c in k.Job.actorRefID)
                    {
                        if (this.FactionOwnerRoot.isManagedChara(c)) victimNames.Add(scr_System_CampaignManager.current.FindInstanceByID(c).CallName);
                    }
                    // send rescue event
                    var r1 = this.Job.AddResult(rescueText.Replace("$victims$",String.Join(",", victimNames)), new List<string>(), relevantActors, false);
                    var r2 = k.Job.AddResult(LocalizeDictionary.QueryThenParse("ui_management_expedition_rescueReceiverText"), new List<string>(), relevantActors, false);

                    var package = new SerializableEventPackage();
                    package.eventID = kexp.Base.rescueEventID;
                    package.eventLabel = kexp.Base.rescueEventLabel;

                    package.rescueJobID = k.Job.RefID;

                    List<int> party = new List<int>(), frontline = new List<int>(), backline = new List<int>();
                    List<int> victims = new List<int>();
                    foreach (var i in relevantActors)
                    {
                        party.Add(i);
                        switch (GetTeamComp(i))
                        {
                            case PartyComposition.frontline:
                                frontline.Add(i);
                                break;
                            case PartyComposition.backline:
                                backline.Add(i);
                                break;
                            default: break;
                        }
                    }
                    package.Targets.Add("party", party);
                    package.Targets.Add("teamA_frontline", frontline);
                    package.Targets.Add("teamA_backline", backline);

                    foreach(var i in scr_System_CampaignManager.current.CharaRefsInRoom(k.Room.RefID))
                    {
                        if (this.FactionOwnerRoot.isMember(i)) victims.Add(i);
                    }

                    package.Targets.Add("victims", victims);
                    // scope target everybody as enemy

                    r1.unresolved = package;

                    // this must not be. on save load these 2 will not be the same thing
                   // r2.unresolved = package;

                }
            }
        }
    }


    [JsonIgnore]
    public string BackgroundImagePath
    {
        get
        {
            return this.Job == null || this.Job.Expedition == null ? "" : this.Job.Expedition.Base.backgroundImagePath;
        }
    }


    [JsonIgnore]
    public string FactionDisplayName
    {
        get
        {
            return DisplayName;
        }
        set
        {
            this.DisplayName = value;
        }
    }

    [JsonIgnore]
    public string FullFactionDisplayName
    {
        get
        {
            return $"{this.OwnerFaction.FactionDisplayName}_{this.FactionDisplayName}";
        }
    }

    [JsonProperty] FactionInventory _inventory = null;
    public FactionInventory Inventory { get {
            if (this._inventory == null) this._inventory = new FactionInventory(this, new List<string>() { "food_meal" });
            return _inventory; } }


    [JsonProperty] int innerJobRef = -1;

    Job_Expedition _job = null;
    [JsonIgnore]
    public Job_Expedition Job { get
        {
            if (_job == null)
            {
                _job = scr_System_CampaignManager.current.FindJobInstanceByID(innerJobRef) as Job_Expedition;
            }
            return _job;
        } }

    [JsonIgnore] public List<Floor_Instance> ManagedFloors { get { return new List<Floor_Instance>(); } }

    [JsonIgnore]
    public List<Manageable> ConnectedFactions
    {
        get { return new List<Manageable>(); } }

    [JsonIgnore] public Room_Instance MainExit { get { return this.Room; } }

    [JsonIgnore]
    public List<Character_Trainable> Managers{
        get
        {
            var v = new List<Character_Trainable>(OwnerFaction.Managers);
            foreach (var i in this.charaGuestStatus) if (i.Value == Manageable_GuestStatus.Manager) v.Add(scr_System_CampaignManager.current.FindInstanceByID(i.Key));
            return v;
        }
    }

    public List<int> GetOwnedRooms(Character_Trainable c)
    {
        if (this.isMember(c) || this.isPrisoner(c)) return new List<int>() { Room.RefID };
        else return null;
    }

    [JsonIgnore] public Manageable FactionOwnerRoot
    {
        get
        {
            return this.OwnerFaction;
        }
    }

    /// <summary>
    /// This gets called every minute
    /// </summary>
    /// <param name="currentHour"></param>
    /// <param name="currentMinute"></param>
    public void Manage(int currentHour, int currentMinute)
    {
        bool allowLazyRefresh = currentMinute % 15 != 0;
        foreach (var kvpair in nonjobPosts)
        {
            foreach (var post in kvpair.Value)
            {
                post.RefreshValidCOMs(allowLazyRefresh);
            }
        }

        foreach (var kvpair in jobPosts)
        {
            foreach (var post in kvpair.Value)
            {
                post.RefreshValidCOMs(false);
            }
        }

        this.Job.UpdateStatus(currentHour, currentMinute);
       // string s = "Faction [" + ID + "] manage at hour [" + currentHour + "]";
       // s += "\n" + Inventory.PrintContent();// + " _ " + String.Join(" ", Inventory.PrintTracker());
    }
}

