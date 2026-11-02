using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// A physical checkup (a provided activity - MemberType.providedActivities, e.g. a family doctor's; Docs/Utnapishtim_Activities/
/// Plan_UtnapishtimActivities.md §4.6 + "Rework 2026-10-31"), launched by the checkup activity's begin event (LaunchJob, roles
/// "provider" / "patient"; its instanceKey AppendString links it to the Job_Activity - I_ActivityChildJob). Modelled on
/// Job_CSection:
/// <br/>- Both walk to the activity's room (where it holds the examination bed). Once both are there (Ready):
/// <br/>  - NPC patient: the provider's checkupCommandID package (checkup_examination - like jp_hospital_csection_operation)
///   with the patient as receiver - the checkup begins (refusal: the activity's AcceptanceMods force acceptance).
/// <br/>  - player patient: the provider's package waits in its preparing variant (no receiver) and is offered to the
///   player as a joinable AP (I_PlayerJoinableJob, joinAlone); joining adds the player as receiver and begins the
///   checkup. An unjoined package that runs out is started again: the player is waited for until the activity ends or
///   maxDurationMinutes (user 2026-10-31).
/// <br/>  - player provider: not supported (the player is never driven) - the patient waits until the booking ends.
/// <br/>- The checkup lasts the command's timeScale from its begin; then the job ends and resultEventID fires with targets
///   "doer" / "receiver" (and "provider" / "patient") - both are free for the rest of their booking. A checkup cut short
///   (its package gone before its length) is started again.
/// <br/>- Interruptible while walking / waiting, not once the checkup runs. The player is never driven.
/// </summary>
public class Job_Checkup : Job, I_RequireSpecialTracker, I_EventLaunchedJob, I_ActivityChildJob, I_PlayerJoinableJob
{
    /// <summary>Template field: the command the provider runs on the patient (variant with no receiver = waiting).</summary>
    public string checkupCommandID = "checkup_examination";
    /// <summary>Template field: fired when the checkup has concluded (targets doer / receiver / provider / patient). "" = none.</summary>
    public string resultEventID = "Checkup_Result";
    /// <summary>Template field: hard cap on the job's length (the activity's end comes first for a shorter booking).</summary>
    public int maxDurationMinutes = 60;

    [JsonProperty] RecreationActivityKey activityKey = null;
    [JsonProperty] int activityJobRef = -1;
    [JsonProperty] int roomRef = -1;
    [JsonProperty] int providerRef = -1;
    [JsonProperty] int patientRef = -1;
    [JsonProperty] DateTime startTime;
    /// <summary>When the checkup itself began (default = not yet - walking / waiting).</summary>
    [JsonProperty] DateTime checkupStarted = default;
    [JsonProperty] bool ended = false;

    public Job_Checkup() : base() { }

    [JsonIgnore] public override string DisplayName { get { return $"|Checkup {providerRef}->{patientRef} {(ended ? "ended" : CheckupBegun ? "examining" : "waiting")}|"; } }
    [JsonIgnore] public override bool CanBeInterrupted { get { return !CheckupBegun; } }
    [JsonIgnore] public override Room_Instance ParentRoom { get { return roomRef < 0 ? null : scr_System_CampaignManager.current.Map.GetRoomByRef(roomRef); } }
    [JsonIgnore] public bool IsRunning { get { return !ended; } }
    [JsonIgnore] bool CheckupBegun { get { return checkupStarted != default; } }
    [JsonIgnore] Job_Activity ActivityJob { get { return activityJobRef < 0 ? null : scr_System_CampaignManager.current.FindJobInstanceByID(activityJobRef, false) as Job_Activity; } }
    [JsonIgnore] COM CheckupCOM { get { return scr_System_Serializer.current.MasterList.COMs.GetByID(checkupCommandID); } }
    [JsonIgnore] Character_Trainable Provider { get { return scr_System_CampaignManager.current.FindInstanceByID(providerRef); } }
    [JsonIgnore] Character_Trainable Patient { get { return scr_System_CampaignManager.current.FindInstanceByID(patientRef); } }
    [JsonIgnore] int PlayerRef { get { return scr_System_CampaignManager.current.Player.RefID; } }
    [JsonIgnore] bool ProviderIsPlayer { get { return providerRef == PlayerRef; } }
    [JsonIgnore] bool PatientIsPlayer { get { return patientRef == PlayerRef; } }
    [JsonIgnore] int CheckupMinutes { get { var com = CheckupCOM; return Math.Max(1, com == null ? 20 : com.TimeScale); } }

    // ---------------- launch ---------------- //

    /// <summary>Binds to the launched activity job named by ev's instanceKey (its room), roles provider / patient (one each, not the same). False = not launched.</summary>
    public bool BindRoles(Dictionary<string, List<Character_Trainable>> roles, EventInstance ev)
    {
        if (ev == null || !ev.AppendStrings.TryGetValue("instanceKey", out var keys) || keys == null || keys.Count == 0) return false;
        activityKey = RecreationActivityKey.Parse(keys[0]);
        var activity = Job_Activity.FindFor(activityKey);
        if (activity == null || activity.Phase != ActivityPhase.Launched || activity.ParentRoom == null || activity.ChildJob != null) return false;
        var provider = FirstOf(roles, "provider");
        var patient = FirstOf(roles, "patient");
        if (provider == null || patient == null || provider == patient) return false;
        if (CheckupCOM == null)
        {
            Debug.LogError($"Job_Checkup: checkup command [{checkupCommandID}] not found");
            return false;
        }
        activityJobRef = activity.RefID;
        roomRef = activity.ParentRoom.RefID;
        providerRef = provider.RefID;
        patientRef = patient.RefID;
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
        // as Job_MedicalCare.Register: the NPCs are taken into the job now (the player never), and - both gathered in the
        // room at the launch - the 体检 package is ready at once (for a player patient to join), not on the provider's next think
        var provider = Provider;
        if (provider != null && !ProviderIsPlayer) provider.ChangeCurrentJob(this);
        var patient = Patient;
        if (patient != null && !PatientIsPlayer) patient.ChangeCurrentJob(this);
        if (Ready && CheckupAP == null && StartCheckupPackage()) ActivateCheckupPackageNow();
    }

    /// <summary>
    /// Moves the just-added 体检 package to executing now - Job.PreUpdateTime's own step (SetActive, register, into
    /// packages_previous), otherwise done on the next tick. The join button lists executing packages only
    /// (Job.JoinablePackages), so without it the player sees it a minute after Checkup_Begin.
    /// </summary>
    void ActivateCheckupPackageNow()
    {
        var now = scr_System_Time.current.getCurrentTime();
        for (int i = packages_current.Count - 1; i >= 0; i--)
        {
            var p = packages_current[i];
            if (p.targetCOM == null || p.targetCOM.ID != checkupCommandID || p.Duration < 0) continue;
            RemovePackageAt_Current(i);
            p.SetActive(now);
            scr_System_CampaignManager.current.Register(p);
            AddPackage_Previous(p);
        }
    }

    // ---------------- actors ---------------- //

    public bool MatchTracker(Character_Trainable c) { return c != null && !ended && (c.RefID == providerRef || c.RefID == patientRef); }

    public bool Wants(Character_Trainable c) { return c != null && !ended && (c.RefID == providerRef || c.RefID == patientRef); }

    public override bool IsJobValid() { return !ended; }

    public override bool IsActorValid(int doerRefID) { return !ended && (doerRefID == providerRef || doerRefID == patientRef); }

    public override bool hasActorCompletedJob(int refID) { return ended; }

    protected override List<COM> UpdateAllUsableCOMs()
    {
        var list = new List<COM>();
        var com = CheckupCOM;
        if (com != null) list.Add(com);
        return list;
    }

    bool InRoom(int charaRef)
    {
        var room = ParentRoom;
        var charaRoom = scr_System_CampaignManager.current.GetCharaRoomInstance(charaRef);
        return room != null && charaRoom != null && charaRoom.RefID == room.RefID;
    }

    /// <summary>Provider and patient (the player too) are in the activity's room, and the checkup hasn't begun.</summary>
    [JsonIgnore] bool Ready { get { return !ended && !CheckupBegun && InRoom(providerRef) && InRoom(patientRef); } }

    bool HasPackage(int charaRef)
    {
        foreach (var ap in packages_current) if (ap.actorRefs.Contains(charaRef)) return true;
        foreach (var ap in packages_previous) if (ap.actorRefs.Contains(charaRef) && ap.Duration > 0) return true;
        return false;
    }

    /// <summary>The running checkup-command package (waiting or examining), if any.</summary>
    [JsonIgnore] ActionPackage CheckupAP
    {
        get
        {
            foreach (var ap in packages_current) if (ap.targetCOM != null && ap.targetCOM.ID == checkupCommandID && ap.Duration > 0) return ap;
            foreach (var ap in packages_previous) if (ap.targetCOM != null && ap.targetCOM.ID == checkupCommandID && ap.Duration > 0) return ap;
            return null;
        }
    }

    /// <summary>The player patient may join the provider's preparing package once both are in the room (Job_CSection.OffersJoinTo).</summary>
    public bool OffersJoinTo(Character_Trainable player)
    {
        if (player == null || player.RefID != patientRef || !Ready) return false;
        var ap = CheckupAP;
        return ap != null && !ap.actorRefs.Contains(player.RefID);
    }

    /// <summary>
    /// Walk to the room; there, once both are in it, the checkup package is started (StartCheckupPackage); otherwise
    /// wait. The player is never driven.
    /// </summary>
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
            if (!pathPkg.Validate()) { ss += "cannot path to the examination room"; return false; }
            AddPackage(new List<ActionPackage>() { pathPkg });
            ss += "pathing to the examination room";
            return true;
        }

        if (Ready && CheckupAP == null && StartCheckupPackage())
        {
            ss += CheckupBegun ? "examining" : "checkup package waiting for the player";
            return true;
        }
        AddPackage(new List<ActionPackage>() { new ActionPackage_Wait(this, c.RefID, 10) });
        ss += "waiting in the examination room";
        return true;
    }

    /// <summary>
    /// As Job_CSection.StartOperationPackage: the provider's checkupCommandID package - an NPC patient is its receiver
    /// from the start (begins now); a player patient joins it (preparing variant, no receiver - the join adds them as
    /// receiver). False = invalid (they wait and try again).
    /// </summary>
    bool StartCheckupPackage()
    {
        var com = CheckupCOM;
        var room = ParentRoom;
        // the player is never driven: a player provider never starts it (the patient waits until the booking ends)
        if (com == null || room == null || ProviderIsPlayer) return false;

        var receivers = PatientIsPlayer ? new List<int>() : new List<int>() { patientRef };
        var pkg = com.MakePackage(this, new List<int>() { providerRef }, receivers, providerRef, room);
        // Validate picks the variant (preparing / examining)
        if (pkg == null || !pkg.Validate()) return false;

        // drop the waiting packages of the NPCs taking part
        var dropped = new List<ActionPackage>();
        foreach (var ap in packages_current) if (ap is ActionPackage_Wait && IsNpcParticipant(ap)) dropped.Add(ap);
        foreach (var ap in packages_previous) if (ap is ActionPackage_Wait && IsNpcParticipant(ap)) dropped.Add(ap);
        foreach (var ap in dropped) RemovePackage(ap, false);

        AddPackage(new List<ActionPackage>() { pkg });
        if (!PatientIsPlayer) BeginCheckup();
        return true;
    }

    bool IsNpcParticipant(ActionPackage ap)
    {
        foreach (var r in ap.actorRefs) if (r != PlayerRef && (r == providerRef || r == patientRef)) return true;
        return false;
    }

    /// <summary>The player patient joined the preparing package (joinable AP): the checkup begins now, at its full length (Job_CSection.AddActor).</summary>
    public override void AddActor(int charaRef, string priorityCOMID = "", string priorityCOMTag = "")
    {
        base.AddActor(charaRef, priorityCOMID, priorityCOMTag);
        if (charaRef != patientRef || !PatientIsPlayer || CheckupBegun || ended) return;
        var ap = CheckupAP;
        if (ap == null || !ap.ReceiverRefs.Contains(patientRef)) return;
        BeginCheckup();
        ap.Reset();
    }

    void BeginCheckup()
    {
        checkupStarted = scr_System_Time.current.getCurrentTime();
    }

    public override void RemoveActor(int charaRef)
    {
        // re-entrancy guard like Job_MedicalCare: ChangeCurrentJob calls RemoveActor back
        if (actorJoinTime.ContainsKey(charaRef))
        {
            actorJoinTime.Remove(charaRef);
            var c = scr_System_CampaignManager.current.FindInstanceByID(charaRef);
            if (c != null && c.CurrentJob == this) c.ChangeCurrentJob(null);
        }
        base.RemoveActor(charaRef);
    }

    // ---------------- update / end ---------------- //

    /// <summary>
    /// Per tick: the checkup has concluded once its length has passed since it began - then the end and the result
    /// event. Its package gone before that (cut short) = start again. Ends without a result when the activity is gone
    /// or after maxDurationMinutes.
    /// </summary>
    public override void LastUpdate()
    {
        if (ended) return;
        var activity = ActivityJob;
        if (activity == null || activity.Phase == ActivityPhase.Ended) { EndCheckup("activity gone", false); return; }
        var now = scr_System_Time.current.getCurrentTime();
        if (CheckupBegun)
        {
            if ((now - checkupStarted).TotalMinutes >= CheckupMinutes) { EndCheckup("checkup concluded", true); return; }
            if (CheckupAP == null) checkupStarted = default;   // cut short - start again
        }
        if ((now - startTime).TotalMinutes >= maxDurationMinutes) EndCheckup("timed out", false);
    }

    void EndCheckup(string reason, bool concluded)
    {
        if (ended) return;
        ended = true;
        var actors = new List<int>(actorRefID);
        foreach (var refID in actors) RemoveActor(refID);
        ActivityJob?.OnChildJobEnded(this);
        if (!scr_UpdateHandler.current.Updating) NotifyDescriptionsOutOfUpdate();
        scr_System_CampaignManager.current.NotifyEndJob(this);
        if (scr_System_CentralControl.current.LogPrefs.DLog_Jobs) Debug.Log($"{DisplayName} ended: {reason}");
        if (concluded) FireResult();
    }

    /// <summary>resultEventID with targets doer / provider (the provider) and receiver / patient (the patient); self = the player if involved, else the patient (visibility only).</summary>
    void FireResult()
    {
        if (string.IsNullOrEmpty(resultEventID)) return;
        var provider = Provider;
        var patient = Patient;
        if (provider == null || patient == null) return;
        var self = ProviderIsPlayer ? provider : patient;
        var ev = new EventInstance(self, resultEventID, "");
        ev.displayOverride = ProviderIsPlayer || PatientIsPlayer;
        ev.Targets["doer"] = new List<Character_Trainable>() { provider };
        ev.Targets["provider"] = new List<Character_Trainable>() { provider };
        ev.Targets["receiver"] = new List<Character_Trainable>() { patient };
        ev.Targets["patient"] = new List<Character_Trainable>() { patient };
        scr_UpdateHandler.current.EventHandler.StartEvent(ev, false);
    }
}
