/// <summary>
/// Staff stay with a patient in intense labor (sandbox only). The patient keeps her own bed rest on the reserved bed -
/// her resting is what lets the labor chain roll the birth. Ends when nobody is in intense labor any more.
/// Launched by Hospital_Dispatch_IntensiveCare.
/// </summary>
public class Job_IntensiveCare : Job_MedicalCare
{
    public Job_IntensiveCare() : base() { }

    [Newtonsoft.Json.JsonIgnore] public override string DisplayName { get { return $"|IntensiveCare patient[{patientRef}]|"; } }

    protected override bool IsStillNeeded(Character_Trainable patient) { return ReproductionUtility.IsInIntenseLabor(patient); }

    protected override string StaffDescriptionKey { get { return "chara_currentjob_medical_intensiveCare_staff"; } }
    protected override string PatientDescriptionKey { get { return "chara_currentjob_medical_intensiveCare_patient"; } }
}
