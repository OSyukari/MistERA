using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

/// <summary>
/// C-section on a patient whose labor cannot be delivered naturally (ReproductionUtility.RequiresCSection).
/// Staff gather at the patient's bed; an NPC patient is held in the job on a long rest. Once everyone is in the room
/// the staff start the operation package (operationCOMID, staff as doers):
/// - NPC patient: she is its receiver from the start - the operation begins right away.
/// - Player patient: the package waits without a receiver (its "preparing" variant) and is offered to the player as a
///   joinable AP (I_PlayerJoinableJob, joinAlone); joining begins the operation and restarts the package at its full
///   length, so time passes through it. An unjoined package that runs out is simply started again.
/// The operation lasts operationCOMID's timeScale minutes and gives the patient anesthesiaStatusID. When it is done the
/// job ends and completeEventID runs on the patient - that event delivers the babies (GiveBirth all).
/// Launched by Hospital_Dispatch_CSection.
/// </summary>
public class Job_CSection : Job_MedicalCare, I_PlayerJoinableJob
{
    /// <summary>Template: the staff's operation command; its timeScale is the operation length.</summary>
    public string operationCOMID = "jp_hospital_csection_operation";
    /// <summary>Template: status given to the patient when the operation begins (lasts the operation + 60 min).</summary>
    public string anesthesiaStatusID = "chara_status_anesthesia";
    /// <summary>Template: event run on the patient when the operation is done.</summary>
    public string completeEventID = "Hospital_CSection_Complete";

    [JsonProperty] protected bool surgeryStarted = false;
    [JsonProperty] protected DateTime surgeryStartTime;

    public Job_CSection() : base() { }

    [JsonIgnore] public override string DisplayName { get { return $"|CSection patient[{patientRef}] started[{surgeryStarted}]|"; } }

    [JsonIgnore] protected override bool HoldsPatient { get { return true; } }

    /// <summary>Once begun, the operation runs to its end even though the labor state itself only changes on delivery.</summary>
    protected override bool IsStillNeeded(Character_Trainable patient) { return surgeryStarted || ReproductionUtility.RequiresCSection(patient); }

    protected override string StaffDescriptionKey { get { return surgeryStarted ? "chara_currentjob_medical_csection_staff_operating" : "chara_currentjob_medical_csection_staff_prep"; } }
    protected override string PatientDescriptionKey { get { return surgeryStarted ? "chara_currentjob_medical_csection_patient_operating" : "chara_currentjob_medical_csection_patient_prep"; } }

    [JsonIgnore] COM OperationCOM { get { return scr_System_Serializer.current.MasterList.COMs.GetByID(operationCOMID); } }
    [JsonIgnore] int SurgeryMinutes { get { var com = OperationCOM; return Math.Max(1, com == null ? 60 : com.TimeScale); } }

    protected override List<COM> UpdateAllUsableCOMs()
    {
        var com = OperationCOM;
        return com == null ? new List<COM>() : new List<COM>() { com };
    }

    /// <summary>The staff's running operation package, if any.</summary>
    [JsonIgnore] ActionPackage OperationAP
    {
        get
        {
            foreach (var ap in packages_current) if (ap.targetCOM != null && ap.targetCOM.ID == operationCOMID && ap.Duration > 0) return ap;
            foreach (var ap in packages_previous) if (ap.targetCOM != null && ap.targetCOM.ID == operationCOMID && ap.Duration > 0) return ap;
            return null;
        }
    }

    /// <summary>Not begun yet and every staff member and the patient are in the bed's room.</summary>
    [JsonIgnore] bool Ready
    {
        get
        {
            if (ended || surgeryStarted) return false;
            var room = ParentRoom;
            if (room == null) return false;
            var refs = new List<int>(staffRefs) { patientRef };
            foreach (var r in refs)
            {
                var charaRoom = scr_System_CampaignManager.current.GetCharaRoomInstance(r);
                if (charaRoom == null || charaRoom.RefID != room.RefID) return false;
            }
            return true;
        }
    }

    public bool OffersJoinTo(Character_Trainable player)
    {
        return player != null && player.RefID == patientRef && Ready && OperationAP != null;
    }

    /// <summary>Once everyone is in the room, the staff start the operation package; otherwise the base behavior (walk there, wait).</summary>
    public override bool UpdateActorPackage(Character_Trainable c, out string ss)
    {
        if (!ended && IsActorValid(c.RefID) && Ready && OperationAP == null && OperationCOM != null && StartOperationPackage())
        {
            ss = $"|{DisplayName} [{c.FirstName}]| operation package started";
            return true;
        }
        return base.UpdateActorPackage(c, out ss);
    }

    bool StartOperationPackage()
    {
        var receivers = PatientIsPlayer ? new List<int>() : new List<int>() { patientRef };
        var operation = OperationCOM.MakePackage(this, new List<int>(staffRefs), receivers, staffRefs[0]);
        // Validate picks the COM variant (preparing / operating) - without it COMVariantID stays -1 and the ongoing log indexes variants[-1]
        if (operation == null || !operation.Validate()) return false;

        // drop the waiting packages of everyone taking part
        var participants = new List<int>(staffRefs);
        if (!PatientIsPlayer) participants.Add(patientRef);
        foreach (var ap in packages_current.Concat(packages_previous).ToList())
        {
            if (ap is ActionPackage_Wait && ap.actorRefs.Any(participants.Contains)) RemovePackage(ap, false);
        }

        AddPackage(new List<ActionPackage>() { operation });
        if (!PatientIsPlayer) BeginSurgery();
        return true;
    }

    /// <summary>The player patient joined the operation package (joinable AP): the operation begins now, at its full length.</summary>
    public override void AddActor(int charaRef, string priorityCOMID = "", string priorityCOMTag = "")
    {
        base.AddActor(charaRef, priorityCOMID, priorityCOMTag);
        if (charaRef != patientRef || !PatientIsPlayer || surgeryStarted || ended) return;
        var operation = OperationAP;
        if (operation == null || !operation.ReceiverRefs.Contains(patientRef)) return;
        BeginSurgery();
        operation.Reset();
    }

    public override void LastUpdate()
    {
        base.LastUpdate();
        if (ended || !surgeryStarted) return;
        if ((scr_System_Time.current.getCurrentTime() - surgeryStartTime).TotalMinutes >= SurgeryMinutes) CompleteSurgery();
    }

    void BeginSurgery()
    {
        surgeryStarted = true;
        surgeryStartTime = scr_System_Time.current.getCurrentTime();
        var patient = Patient;
        patient?.Stats.AddOrModStatus(anesthesiaStatusID, 100, SurgeryMinutes + 60);
        // memory at the start of the operation (the patient is now the operation package's receiver);
        // staffRefs lists the doctor role first (Job_MedicalCare.BindRoles)
        var doctor = staffRefs.Count > 0 ? scr_System_CampaignManager.current.FindInstanceByID(staffRefs[0]) : null;
        patient?.AddLaborMemory(Character_Trainable.memory_csection, true, doctor);
    }

    void CompleteSurgery()
    {
        var patient = Patient;
        EndMedicalJob("operation complete");
        if (patient != null && !string.IsNullOrEmpty(completeEventID))
            scr_UpdateHandler.current.EventHandler.StartEvent(new EventInstance(patient, completeEventID, ""), false);
    }
}
