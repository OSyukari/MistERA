using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Base of the hospital's event-launched medical jobs (LaunchJob Result with a jobTemplate). Sandbox coordination only:
/// staff walk to the patient's bed and stay with them - births are never decided here (the labor event chain does that).
/// - Roles: "patient" (first character), "doctor" (required) and optional "nurse".
/// - Bed: the one the patient is using now, else the rest_required bed in a room the patient owns in their temporary home.
///   It is reserved while the job runs (only patient and staff may use it; the player is never blocked).
/// - Cannot be interrupted. Ends after maxDurationMinutes, on TerminateJob, or when the patient is gone / left the bed's
///   room / no longer needs it (IsStillNeeded), or a staff member is gone or pulled off it. Ending releases bed and actors.
/// - The player patient is never an actor (they keep playing and rest through the UI); an NPC patient is only taken into
///   the job on a long rest when HoldsPatient.
/// </summary>
public abstract class Job_MedicalCare : Job, I_RequireSpecialTracker, I_EventLaunchedJob, I_EventTerminableJob, I_FurnitureReserver
{
    public static string role_patient = "patient";
    public static string role_doctor = "doctor";
    public static string role_nurse = "nurse";
    static readonly string[] bedTags = { "rest_required", "rest", "sleep" };

    /// <summary>Template field: hard cap on the job's length.</summary>
    public int maxDurationMinutes = 360;

    [JsonProperty] protected int patientRef = -1;
    [JsonProperty] protected List<int> staffRefs = new List<int>();
    [JsonProperty] protected int bedJobRef = -1;
    [JsonProperty] protected DateTime startTime;
    [JsonProperty] protected bool ended = false;

    public Job_MedicalCare() : base() { }

    [JsonIgnore] public override bool CanBeInterrupted { get { return false; } }

    [JsonIgnore] public Character_Trainable Patient { get { return scr_System_CampaignManager.current.FindInstanceByID(patientRef); } }
    [JsonIgnore] protected Job_Furniture Bed { get { return scr_System_CampaignManager.current.FindJobInstanceByID(bedJobRef) as Job_Furniture; } }
    [JsonIgnore] public override Room_Instance ParentRoom { get { return Bed?.ParentRoom; } }
    [JsonIgnore] protected bool PatientIsPlayer { get { return patientRef == scr_System_CampaignManager.current.Player.RefID; } }
    [JsonIgnore] protected int ElapsedMinutes { get { return (int)(scr_System_Time.current.getCurrentTime() - startTime).TotalMinutes; } }

    /// <summary>NPC patient is taken into the job on a long rest (e.g. surgery). False: they keep their own behavior on the reserved bed.</summary>
    [JsonIgnore] protected virtual bool HoldsPatient { get { return false; } }

    /// <summary>Whether the patient still needs this job; checked every update.</summary>
    protected abstract bool IsStillNeeded(Character_Trainable patient);

    /// <summary>Localization keys for GetJobDescription ($patient$ = patient's name).</summary>
    protected abstract string StaffDescriptionKey { get; }
    protected abstract string PatientDescriptionKey { get; }

    public bool MatchTracker(Character_Trainable c) { return c != null && (c.RefID == patientRef || staffRefs.Contains(c.RefID)); }
    public bool AllowsFurnitureUse(Character_Trainable c) { return MatchTracker(c); }

    // ── Launch ─────────────────────────────────────────────────────────────

    public virtual bool BindRoles(Dictionary<string, List<Character_Trainable>> roles, EventInstance ev)
    {
        var patient = roles.TryGetValue(role_patient, out var p) ? p.FirstOrDefault() : null;
        if (patient == null) return false;
        var doctors = roles.TryGetValue(role_doctor, out var d) ? d : new List<Character_Trainable>();
        if (doctors.Count < 1) return false;
        var nurses = roles.TryGetValue(role_nurse, out var n) ? n : new List<Character_Trainable>();

        // one medical job per bed; the patient must already be in the bed's room
        var bed = FindPatientBed(patient);
        if (bed == null || bed.IsReserved || bed.ParentRoom == null) return false;
        var patientRoom = scr_System_CampaignManager.current.GetCharaRoomInstance(patient.RefID);
        if (patientRoom == null || patientRoom.RefID != bed.ParentRoom.RefID) return false;

        patientRef = patient.RefID;
        staffRefs = doctors.Concat(nurses).Where(x => x != null && x.RefID != patient.RefID).Select(x => x.RefID).Distinct().ToList();
        if (staffRefs.Count < 1) return false;
        bedJobRef = bed.RefID;
        var hospital = patient.FactionManager.Faction_Home_Temporary;
        if (hospital != null) FactionOwner = hospital;
        return true;
    }

    /// <summary>The bed the patient is using now (rest/sleep COM on furniture), else a rest_required bed in a room they own in their temporary home.</summary>
    protected static Job_Furniture FindPatientBed(Character_Trainable patient)
    {
        if (patient.CurrentJob is Job_Furniture using_ && bedTags.Any(t => using_.hasActivePackgeWithTag(patient.RefID, t))) return using_;

        var home = patient.FactionManager.Faction_Home_Temporary;
        var rooms = home?.GetOwnedRooms(patient);
        if (rooms == null) return null;
        foreach (var roomRef in rooms)
        {
            var room = scr_System_CampaignManager.current.Map.GetRoomByRef(roomRef);
            if (room == null) continue;
            foreach (var job in room.Jobs)
            {
                if (job is Job_Furniture jf && jf.HasAvailableCOMwithCOMTags("rest_required")) return jf;
            }
        }
        return null;
    }

    public override void Register(int id)
    {
        base.Register(id);
        startTime = scr_System_Time.current.getCurrentTime();
        Bed?.ReserveFor(this);
        foreach (var staff in staffRefs)
        {
            var c = scr_System_CampaignManager.current.FindInstanceByID(staff);
            if (c != null) c.ChangeCurrentJob(this);
        }
        var patient = Patient;
        if (HoldsPatient && patient != null && !PatientIsPlayer) patient.ChangeCurrentJob(this);
    }

    // ── Actors ─────────────────────────────────────────────────────────────

    public override bool IsJobValid() => !ended;

    public override bool IsActorValid(int doerRefID) { return !ended && (staffRefs.Contains(doerRefID) || HoldsPatient && doerRefID == patientRef); }

    public override void RemoveActor(int charaRef)
    {
        // Remove from actorJoinTime first as a re-entrancy guard: ChangeCurrentJob calls RemoveActor back.
        if (this.actorJoinTime.ContainsKey(charaRef))
        {
            this.actorJoinTime.Remove(charaRef);
            var c = scr_System_CampaignManager.current.FindInstanceByID(charaRef);
            if (c != null && c.CurrentJob == this) c.ChangeCurrentJob(null);
        }
        base.RemoveActor(charaRef);
    }

    public override bool hasActorCompletedJob(int refID) { return ended; }

    public override string GetJobDescription(int charaRef)
    {
        var name = Patient == null ? "" : Patient.FirstName;
        return LocalizeDictionary.QueryThenParse(charaRef == patientRef ? PatientDescriptionKey : StaffDescriptionKey).Replace("$patient$", name);
    }

    /// <summary>Everyone walks to the bed's room, then stays (staff wait by the bed; a held NPC patient rests).</summary>
    public override bool UpdateActorPackage(Character_Trainable c, out string ss)
    {
        ss = $"|{DisplayName} [{c.FirstName}]|";
        if (ended || !IsActorValid(c.RefID)) { ss += "released"; return false; }

        if (packages_current.Exists(x => x.actorRefs.Contains(c.RefID))) { ss += "has current package"; return true; }
        if (packages_previous.Exists(x => x.actorRefs.Contains(c.RefID) && x.Duration > 0)) { ss += "has ongoing package"; return true; }

        var room = ParentRoom;
        if (room == null) { ss += "bed gone"; return false; }
        var charaRoom = scr_System_CampaignManager.current.GetCharaRoomInstance(c.RefID);
        if (charaRoom == null || charaRoom.RefID != room.RefID)
        {
            var pathPkg = new ActionPackage_PathTo(this, c.RefID, room.RefID);
            if (!pathPkg.Validate()) { ss += "cannot path to bed room"; return false; }
            AddPackage(new List<ActionPackage>() { pathPkg });
            ss += "pathing to bed room";
            return true;
        }
        AddPackage(new List<ActionPackage>() { new ActionPackage_Wait(this, c.RefID, 30) });
        ss += "staying by the bed";
        return true;
    }

    // ── Update / end ───────────────────────────────────────────────────────

    /// <summary>Sequential per-update check (special tracker) - ends the job when it no longer applies.</summary>
    public override void LastUpdate()
    {
        if (ended) return;
        var reason = GetEndReason();
        if (reason != null) EndMedicalJob(reason);
    }

    /// <summary>Null while the job should go on.</summary>
    protected virtual string GetEndReason()
    {
        if (ElapsedMinutes >= maxDurationMinutes) return "max duration reached";
        var patient = Patient;
        if (patient == null) return "patient gone";
        if (!IsStillNeeded(patient)) return "no longer needed";
        var room = ParentRoom;
        if (room == null) return "bed gone";
        var patientRoom = scr_System_CampaignManager.current.GetCharaRoomInstance(patientRef);
        if (patientRoom == null || patientRoom.RefID != room.RefID) return "patient left the bed's room";
        if (HoldsPatient && !PatientIsPlayer && patient.CurrentJob != this) return "patient pulled off the job";
        foreach (var staff in staffRefs)
        {
            var c = scr_System_CampaignManager.current.FindInstanceByID(staff);
            if (c == null || c.CurrentJob != this) return "staff gone or pulled off the job";
        }
        return null;
    }

    public void TerminateByEvent() { EndMedicalJob("terminated by event"); }

    protected void EndMedicalJob(string reason)
    {
        if (ended) return;
        ended = true;
        if (scr_System_CentralControl.current.LogPrefs.DLog_Jobs) Debug.Log($"{DisplayName} ended: {reason}");
        OnEnded(reason);

        Bed?.ReleaseReservation(this);
        foreach (var refID in actorRefID.ToList()) RemoveActor(refID);

        if (!scr_UpdateHandler.current.Updating) NotifyDescriptionsOutOfUpdate();
        scr_System_CampaignManager.current.NotifyEndJob(this);
    }

    /// <summary>Hook for subclasses, before bed and actors are released.</summary>
    protected virtual void OnEnded(string reason) { }
}
