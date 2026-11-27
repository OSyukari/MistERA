using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// A pet bath activity (MemberType.providedActivities: house_activity_wash_pet on membertype_house_pet), launched by
/// PetWash_Begin (LaunchJob, roles "owner" / "pet"; linked to Job_Activity via instanceKey - I_ActivityChildJob).
/// Modelled on Job_Checkup:
/// - Both gather in the activity's bathroom (ParentRoom).
/// - NPC owner: starts com_furniture_takeShower with doers=[owner], receivers=[pet]. When the shower AP completes,
///   com_furniture_takeShower's own result_event (com_furniture_takeShower_event) fires and this job ends.
/// - Player owner: the player is never driven; the pet waits in the bathroom until the booking ends or the player
///   washes them.
/// </summary>
public class Job_WashPet : Job, I_RequireSpecialTracker, I_EventLaunchedJob, I_ActivityChildJob
{
    /// <summary>Template field: the shower command the owner runs on the pet.</summary>
    public string showerCommandID = "com_furniture_takeShower";
    /// <summary>Template field: hard cap on the job's length (the activity's end comes first for a shorter booking).</summary>
    public int maxDurationMinutes = 60;

    [JsonProperty] RecreationActivityKey activityKey = null;
    [JsonProperty] int activityJobRef = -1;
    [JsonProperty] int roomRef = -1;
    [JsonProperty] int ownerRef = -1;
    [JsonProperty] int petRef = -1;
    [JsonProperty] DateTime startTime;
    [JsonProperty] DateTime washStarted = default;
    [JsonProperty] bool ended = false;

    public Job_WashPet() : base() { }

    [JsonIgnore] public override string DisplayName { get { return $"|WashPet {ownerRef}->{petRef} {(ended ? "ended" : WashBegun ? "washing" : "waiting")}|"; } }
    [JsonIgnore] public override bool CanBeInterrupted { get { return !WashBegun; } }
    [JsonIgnore] public override Room_Instance ParentRoom { get { return roomRef < 0 ? null : scr_System_CampaignManager.current.Map.GetRoomByRef(roomRef); } }
    [JsonIgnore] public bool IsRunning { get { return !ended; } }
    [JsonIgnore] bool WashBegun { get { return washStarted != default; } }
    [JsonIgnore] Job_Activity ActivityJob { get { return activityJobRef < 0 ? null : scr_System_CampaignManager.current.FindJobInstanceByID(activityJobRef, false) as Job_Activity; } }
    [JsonIgnore] COM ShowerCOM { get { return scr_System_Serializer.current.MasterList.COMs.GetByID(showerCommandID); } }
    [JsonIgnore] Character_Trainable Owner { get { return scr_System_CampaignManager.current.FindInstanceByID(ownerRef); } }
    [JsonIgnore] Character_Trainable Pet { get { return scr_System_CampaignManager.current.FindInstanceByID(petRef); } }
    [JsonIgnore] int PlayerRef { get { return scr_System_CampaignManager.current.Player.RefID; } }
    [JsonIgnore] bool OwnerIsPlayer { get { return ownerRef == PlayerRef; } }
    [JsonIgnore] bool PetIsPlayer { get { return petRef == PlayerRef; } }

    public bool BindRoles(Dictionary<string, List<Character_Trainable>> roles, EventInstance ev)
    {
        if (ev == null || !ev.AppendStrings.TryGetValue("instanceKey", out var keys) || keys == null || keys.Count == 0) return false;
        activityKey = RecreationActivityKey.Parse(keys[0]);
        var activity = Job_Activity.FindFor(activityKey);
        if (activity == null || activity.Phase != ActivityPhase.Launched || activity.ParentRoom == null || activity.ChildJob != null) return false;
        var owner = FirstOf(roles, "owner");
        var pet = FirstOf(roles, "pet");
        if (owner == null || pet == null || owner == pet) return false;
        if (ShowerCOM == null)
        {
            Debug.LogError($"Job_WashPet: shower command [{showerCommandID}] not found");
            return false;
        }
        activityJobRef = activity.RefID;
        roomRef = activity.ParentRoom.RefID;
        ownerRef = owner.RefID;
        petRef = pet.RefID;
        FactionOwner = activity.FactionOwner;
        return true;
    }

    static Character_Trainable FirstOf(Dictionary<string, List<Character_Trainable>> roles, string role)
    {
        if (!roles.TryGetValue(role, out var list) || list == null) return null;
        foreach (var c in list) if (c != null) return c;
        return null;
    }

    public override void Register(int id)
    {
        base.Register(id);
        startTime = scr_System_Time.current.getCurrentTime();
        ActivityJob?.LinkChildJob(this);
        var owner = Owner;
        if (owner != null && !OwnerIsPlayer) owner.ChangeCurrentJob(this);
        var pet = Pet;
        if (pet != null && !PetIsPlayer) pet.ChangeCurrentJob(this);
        if (Ready && ShowerAP == null && StartShowerPackage()) ActivateShowerPackageNow();
    }

    void ActivateShowerPackageNow()
    {
        var now = scr_System_Time.current.getCurrentTime();
        for (int i = packages_current.Count - 1; i >= 0; i--)
        {
            var p = packages_current[i];
            if (p.targetCOM == null || p.targetCOM.ID != showerCommandID || p.Duration < 0) continue;
            RemovePackageAt_Current(i);
            p.SetActive(now);
            scr_System_CampaignManager.current.Register(p);
            AddPackage_Previous(p);
        }
    }

    public bool MatchTracker(Character_Trainable c) { return c != null && !ended && (c.RefID == ownerRef || c.RefID == petRef); }
    public bool Wants(Character_Trainable c) { return c != null && !ended && (c.RefID == ownerRef || c.RefID == petRef); }
    public override bool IsJobValid() { return !ended; }
    public override bool IsActorValid(int doerRefID) { return !ended && (doerRefID == ownerRef || doerRefID == petRef); }
    public override bool hasActorCompletedJob(int refID) { return ended; }

    protected override List<COM> UpdateAllUsableCOMs()
    {
        var list = new List<COM>();
        var com = ShowerCOM;
        if (com != null) list.Add(com);
        return list;
    }

    bool InRoom(int charaRef)
    {
        var room = ParentRoom;
        var charaRoom = scr_System_CampaignManager.current.GetCharaRoomInstance(charaRef);
        return room != null && charaRoom != null && charaRoom.RefID == room.RefID;
    }

    [JsonIgnore] bool Ready { get { return !ended && !WashBegun && InRoom(ownerRef) && InRoom(petRef); } }

    bool HasPackage(int charaRef)
    {
        foreach (var ap in packages_current) if (ap.actorRefs.Contains(charaRef)) return true;
        foreach (var ap in packages_previous) if (ap.actorRefs.Contains(charaRef) && ap.Duration > 0) return true;
        return false;
    }

    [JsonIgnore] ActionPackage ShowerAP
    {
        get
        {
            foreach (var ap in packages_current) if (ap.targetCOM != null && ap.targetCOM.ID == showerCommandID && ap.Duration > 0) return ap;
            foreach (var ap in packages_previous) if (ap.targetCOM != null && ap.targetCOM.ID == showerCommandID && ap.Duration > 0) return ap;
            return null;
        }
    }

    bool HasCompletedShowerAP()
    {
        foreach (var ap in packages_completed)
            if (ap.targetCOM != null && ap.targetCOM.ID == showerCommandID) return true;
        return false;
    }

    public override bool UpdateActorPackage(Character_Trainable c, out string ss)
    {
        ss = $"|{DisplayName} [{c?.FirstName}]|";
        if (ended || c == null || !IsActorValid(c.RefID)) { ss += "released"; return false; }
        if (HasPackage(c.RefID)) { ss += "has a package"; return true; }
        if (c.RefID == PlayerRef) { ss += "player never driven"; return false; }

        var room = ParentRoom;
        if (room == null) { ss += "room gone"; return false; }
        if (!InRoom(c.RefID))
        {
            var pathPkg = new ActionPackage_PathTo(this, c.RefID, room.RefID);
            if (!pathPkg.Validate()) { ss += "cannot path to bathroom"; return false; }
            AddPackage(new List<ActionPackage>() { pathPkg });
            ss += "pathing to bathroom";
            return true;
        }

        if (Ready && ShowerAP == null && StartShowerPackage())
        {
            ss += "washing pet";
            return true;
        }
        AddPackage(new List<ActionPackage>() { new ActionPackage_Wait(this, c.RefID, 10) });
        ss += "waiting in bathroom";
        return true;
    }

    bool StartShowerPackage()
    {
        var com = ShowerCOM;
        var room = ParentRoom;
        if (com == null || room == null || OwnerIsPlayer) return false;

        var pkg = com.MakePackage(this, new List<int>() { ownerRef }, new List<int>() { petRef }, ownerRef, room);
        if (pkg == null || !pkg.Validate()) return false;

        var dropped = new List<ActionPackage>();
        foreach (var ap in packages_current) if (ap is ActionPackage_Wait && IsNpcParticipant(ap)) dropped.Add(ap);
        foreach (var ap in packages_previous) if (ap is ActionPackage_Wait && IsNpcParticipant(ap)) dropped.Add(ap);
        foreach (var ap in dropped) RemovePackage(ap, false);

        AddPackage(new List<ActionPackage>() { pkg });
        washStarted = scr_System_Time.current.getCurrentTime();
        return true;
    }

    bool IsNpcParticipant(ActionPackage ap)
    {
        foreach (var r in ap.actorRefs) if (r != PlayerRef && (r == ownerRef || r == petRef)) return true;
        return false;
    }

    public override void RemoveActor(int charaRef)
    {
        if (actorJoinTime.ContainsKey(charaRef))
        {
            actorJoinTime.Remove(charaRef);
            var c = scr_System_CampaignManager.current.FindInstanceByID(charaRef);
            if (c != null && c.CurrentJob == this) c.ChangeCurrentJob(null);
        }
        base.RemoveActor(charaRef);
    }

    public override void LastUpdate()
    {
        if (ended) return;
        var activity = ActivityJob;
        if (activity == null || activity.Phase == ActivityPhase.Ended) { EndWash("activity gone"); return; }
        var now = scr_System_Time.current.getCurrentTime();
        if (WashBegun)
        {
            if (HasCompletedShowerAP()) { EndWash("wash completed"); return; }
            if (ShowerAP == null) washStarted = default;
        }
        if ((now - startTime).TotalMinutes >= maxDurationMinutes) EndWash("timed out");
    }

    void EndWash(string reason)
    {
        if (ended) return;
        ended = true;
        var actors = new List<int>(actorRefID);
        foreach (var refID in actors) RemoveActor(refID);
        ActivityJob?.OnChildJobEnded(this);
        if (!scr_UpdateHandler.current.Updating) NotifyDescriptionsOutOfUpdate();
        scr_System_CampaignManager.current.NotifyEndJob(this);
        if (scr_System_CentralControl.current.LogPrefs.DLog_Jobs) Debug.Log($"{DisplayName} ended: {reason}");
    }
}
