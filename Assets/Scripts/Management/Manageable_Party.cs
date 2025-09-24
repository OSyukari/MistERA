using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using System;
using System.Linq;
using QuikGraph;

[System.Serializable]
public enum ExpeditionStatus
{
    Inactive,
    Queued,
    Gathering,
    Ongoing,
    Resting,
    Returning
}
[System.Serializable]
public enum PartyStatus
{
    Unavailable,
    Inactive,
    Active
}

public interface I_IsJobGiver
{
    public List<Job_Furniture> GetValidJobs_Meal(Character_Trainable chara, int currentHour, List<string> s = null);
    public List<Job_Furniture> GetValidJobs_Jobs(Character_Trainable chara, int currentHour, ref string s, bool checkBlacklist = false);
    [JsonIgnore]
    public string FactionDisplayName { get; }
    public List<Job_CharaCOM> GetValidCharaCOMByTag(Character_Trainable chara, string tag, ref string ss, bool restrainedOnly = true);
    public List<Job_Furniture> GetValidJobsByCOMID(Character_Trainable chara, string comID, List<string> s = null, bool allowJobPostSearch = true, bool allowNonJobPostSearch = true);
    public List<Job_Furniture> GetValidJobs_Sleep(Character_Trainable chara, int currentHour, List<string> s = null);
    public List<Job_Furniture> GetValidJobs_nonJob_byTags(Character_Trainable chara, int currentHour, string tag, List<string> s = null, bool skipPrivate = false, bool shortestPathOnly = true, bool checkBlacklist = false);
    public void NotifyFurnitureChange(Room_Instance room);
    public List<int> RoomOwners(int roomRef);
    [JsonIgnore] public List<Floor_Instance> ManagedFloors { get; }
    [JsonIgnore] public List<Manageable> ConnectedFactions { get; }
    [JsonIgnore] public Room_Instance MainExit { get; }
    [JsonIgnore] public FactionInventory Inventory { get; }
    [JsonIgnore] public List<Character_Trainable> Managers { get; }
    [JsonIgnore] public List<Character_Trainable> ManagedChara { get; }
    [JsonIgnore] public bool isPlayerFaction { get; }
}

[System.Serializable]
public enum PartyAvailability
{
    Unavailable,
    Inactive,
    Active
}

[System.Serializable]
public class Manageable_Party : I_IsJobGiver
{
    [JsonIgnore]
    public string ExpeditionName
    {
        get
        {
            return this.Job.Expedition == null ? " - " : this.Job.Expedition.DisplayName;
        }
    }

    [JsonIgnore] public bool CanStartExpedition { get { return this.Job.Expedition != null && GetAvailability(out string tootlip) == PartyAvailability.Inactive; } }
    [JsonIgnore] public bool CanResolveExpedition { get { return this.Job.Expedition != null && this.Job.isActive; } }
    public void SetExpedition(Expedition exp)
    {
        this.Job.SetExpedition(exp);
        this._recurringCooldown = 0;
        this._recurringTicked = false;
    }

    public bool AllowPassNight = true;
    public int RecurringCooldown = 0;
    public bool IsRecurring = false;
    public int StartHour = 8;
    [JsonIgnore] public int FinalStartHour
    {
        get
        {
            if (this.Job.Expedition == null || !this.Job.Expedition.HasStartHour) return StartHour;
            return this.Job.Expedition.ForceStartHour;
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

                var maxSleepHour = 0;
                foreach (var i in this.ManagedChara) maxSleepHour = Math.Max(maxSleepHour, i.Stats.SleepHours);
                var home = this.OwnerFaction as Manageable_HomeFaction;
                var homeSleeHour = home == null ? 22 : home.SharedSleepHour;

                for (int j = 0; j < maxSleepHour; j++)
                {
                    _sleepHours.Add((homeSleeHour + j) % 24);
                }
            }
            return _sleepHours;
        } }

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
            pauseHours = pauseHours.Distinct().ToList();
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
            var baseDuration = this.Job.Expedition == null ? 0 : this.Job.Expedition.DurationHour;
            return baseDuration;
        }
    }

    [JsonProperty] protected int _recurringCooldown = 0;
    [JsonProperty] protected bool _recurringTicked = false;
    public void ExpeditionEnd()
    {
        this._recurringCooldown = this.RecurringCooldown;
    }

    public bool TryStartExpedition()
    {
        if (_recurringCooldown <= 0 && !this._recurringTicked)
        {
            this._recurringTicked = true;
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

    [JsonIgnore] public bool isPlayerFaction { get { return this.OwnerFaction.isPlayerFaction; } }

    public PartyAvailability GetAvailability(out string tooltip)
    {
        if (isActive)
        {
            tooltip = "party is active";
            return PartyAvailability.Active;
        }

        tooltip = "";
        List<string> ttips = new List<string>();
        if (this.ManagedRefs.Count < 1)
        {
            tooltip = "does not have any member";
            return PartyAvailability.Unavailable;
        }
        bool canAct = true;
        foreach(var i in this.ManagedChara)
        {
            if (!i.canAct)
            {
                canAct = false;
                ttips.Add($"{i.FirstName} cannot act");
            }
            else if (i.FactionManager.CurrentActiveParty != null && i.FactionManager.CurrentActiveParty != this)
            {
                canAct = false;
                ttips.Add($"{i.FirstName} is active in another party");
            }
        }
        if (!canAct)
        {
            tooltip = String.Join("\n", ttips);
            return PartyAvailability.Unavailable;
        }
        else return PartyAvailability.Inactive;
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

    /// <summary>
    /// Can also be used to change guest status
    /// </summary>
    /// <param name="c"></param>
    /// <param name="guestStatus"></param>
    public void AddToFaction(Character_Trainable c, Manageable_GuestStatus guestStatus)
    {
        //c.AddToFaction(this);
        if (!charaGuestStatus.ContainsKey(c.RefID)) charaGuestStatus.Add(c.RefID, guestStatus);
        else charaGuestStatus[c.RefID] = guestStatus;

        if (!charaPartyComposition.ContainsKey(c.RefID)) charaPartyComposition.Add(c.RefID, PartyComposition.frontline);

        // set manager roles
        if (!OwnerFaction.ManagedChara.Contains(c)) OwnerFaction.AddToFaction(c, guestStatus);

        _managedChara = null;

        c.FactionManager.AddPartyTracker(this);
    }

    public void RemoveFromFaction(Character_Trainable c)
    {
        if (c == null) return;
        charaGuestStatus.Remove(c.RefID);
        _managedChara = null;

        c.FactionManager.RemovePartyTracker(this);
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

    public List<Job_Furniture> GetValidJobs_Jobs(Character_Trainable chara, int currentHour, ref string s, bool checkBlacklist = false)
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
        SortedDictionary<int, Dictionary<int, IEnumerable<TaggedEdge<int, Door_Instance>>>> sortedList = scr_System_CampaignManager.current.Map.FilterValidPathsParallel(chara.RefID, rooms, randInsteadofShortest);
        var list = sortedList.Count > 0 ? sortedList.First().Value : new Dictionary<int, IEnumerable<TaggedEdge<int, Door_Instance>>>();
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

    public List<Job_Furniture> GetValidJobsByCOMID(Character_Trainable chara, string comID, List<string> s = null, bool allowJobPostSearch = true, bool allowNonJobPostSearch = true)
    {
        string ss = " (" + ID + ")";

        List<Job_Furniture> possibleJobs;
        COM targetCOM = scr_System_Serializer.current.GetByNameOrID_COM(comID);
        if (targetCOM == null) return null;

        if (targetCOM.comTags.Contains("job") && allowJobPostSearch)
        {
            if (!FactionUtility.TryFindValidJobInstances(jobPosts, out possibleJobs, chara, comID, false))
            {
                ss += " found no valid [" + comID + "] instances offered by Furnitures";
                if (s != null) s.Add(ss);
                return null;
            }
        }
        else if (allowNonJobPostSearch)
        {
            if (!FactionUtility.TryFindValidNonJobInstances(nonjobPosts, managedRoomRefs, out possibleJobs, chara, comID, "", false))
            {
                ss += " found no valid [" + comID + "] instances offered by Furnitures";
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

    public List<Job_Furniture> GetValidJobs_nonJob_byTags(Character_Trainable chara, int currentHour, string tag, List<string> s = null, bool skipPrivate = false, bool shortestPathOnly = true, bool checkBlacklist = false)
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

    public List<int> RoomOwners(int roomRef)
    {
        return this.ManagedRefs;
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

