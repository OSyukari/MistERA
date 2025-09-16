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
    public void SetExpedition(Expedition exp, int startHour = -1)
    {
        this.Job.SetExpedition(exp, startHour == -1 ? this.StartHour : startHour);
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
    [JsonIgnore] public int FinalDuration
    { get
        {
            var baseDuration = BaseDuration;
            if (AllowPassNight && baseDuration < 24) {

                var home = this.OwnerFaction as Manageable_HomeFaction;
                var homeSleeHour = home == null ? 0 : home.SharedSleepHour;

                return baseDuration + ((home != null && (baseDuration + FinalStartHour) >= homeSleeHour) ? 8 : 0);
            }
            else return baseDuration + (baseDuration / 24) * 8;
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
    public void StartExpedition()
    {
        this.Job.SetActive();
    }

    public void EndExpedition()
    {
        this.Job.EndExpedition();
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
        return new List<Job_Furniture>();
    }

    public List<Job_Furniture> GetValidJobs_Jobs(Character_Trainable chara, int currentHour, ref string s, bool checkBlacklist = false)
    {
        return new List<Job_Furniture>();
    }

    public List<Job_CharaCOM> GetValidCharaCOMByTag(Character_Trainable chara, string tag, ref string ss, bool restrainedOnly = true)
    {
        return new List<Job_CharaCOM>();
    }

    public List<Job_Furniture> GetValidJobsByCOMID(Character_Trainable chara, string comID, List<string> s = null, bool allowJobPostSearch = true, bool allowNonJobPostSearch = true)
    {
        return new List<Job_Furniture>();
    }

    public List<Job_Furniture> GetValidJobs_Sleep(Character_Trainable chara, int currentHour, List<string> s = null)
    {
        return new List<Job_Furniture>();
    }

    public List<Job_Furniture> GetValidJobs_nonJob_byTags(Character_Trainable chara, int currentHour, string tag, List<string> s = null, bool skipPrivate = false, bool shortestPathOnly = true, bool checkBlacklist = false)
    {
        return new List<Job_Furniture>();
    }

    public void NotifyFurnitureChange(Room_Instance room)
    {
        if (room != this.Room) return;
        Debug.LogError("UNIMPLEMENTED FUNCTIO NCALL");
    }

    public List<int> RoomOwners(int roomRef)
    {
        return this.ManagedRefs;
    }

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
        Room.AddFurniture("furniture_camping_site");

        Room.FactionOwner = this;
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

    [JsonProperty] FactionInventory _inventory;
    public FactionInventory Inventory { get { return _inventory; } }
    public FactionInventory TempInventory;


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

        this.Job.UpdateStatus();

       // string s = "Faction [" + ID + "] manage at hour [" + currentHour + "]";
       // s += "\n" + Inventory.PrintContent();// + " _ " + String.Join(" ", Inventory.PrintTracker());
    }
}

