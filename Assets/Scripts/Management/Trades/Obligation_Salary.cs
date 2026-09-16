using System.Linq;
using Newtonsoft.Json;

/// <summary>
/// Wage owed by the owning faction (employer) to an employee's home/work-source faction. Freeze-type: if
/// a payday fails, the missed wage stays owed and the obligation stops accruing new hours-based charges
/// until it's fully paid off - "the owed amount stays, and the employee will not come to work; once the
/// player pays, they come back." (The actual job-dispatch suspension is future work - see OnSuspended.)
/// </summary>
public class Obligation_Salary : RecurringObligation
{
    /// <summary>Running total of wages accrued since the last resolved payday - see AccrueHour.</summary>
    [JsonProperty] protected ItemEntry pendingWages = new ItemEntry();

    /// <summary>
    /// The single MemberType DisplayName or JobPostPreset Name this obligation exists for. Obligations are
    /// keyed by (target faction, cadence, sourceName, payeeRefID) in TradeManager.GetOrCreateSalaryObligation,
    /// so two different sources (or two different people under the same source) paying the same target
    /// faction on the same cadence never share an instance/pool - each one's owed balance, arrears state,
    /// and display name stay independent instead of merging.
    /// </summary>
    [JsonProperty] public string sourceName = "";

    /// <summary>
    /// Character_Trainable.RefID of the specific worker this obligation's wages are for - see sourceName's
    /// doc comment. Resolved on demand (via scr_System_CampaignManager.FindInstanceByID) rather than
    /// cached, same reasoning as RecurringObligation.TargetFaction not caching across saves - only used
    /// for display (GetDisplayName) and HandlePaymentEvent's "actor getting paid".
    /// </summary>
    [JsonProperty] public int payeeRefID = -1;

    protected override bool ArrearsAccumulate { get { return false; } }

    /// <summary>
    /// Adds one hour's wage to the running total. Called every active work hour from
    /// Manageable.OnHourUpdate (via TradeManager.GetOrCreateSalaryObligation). paidEventID/failedEventID
    /// are sourced per-call from whichever MemberType/JobPostPreset paid this hour (expected to be
    /// consistent across calls into the same obligation, since GetOrCreateSalaryObligation already keys by
    /// sourceName) and simply overwrite the inherited onPaidEventID/onFailedEventID fields.
    /// </summary>
    public void AccrueHour(ItemEntry hourlyRate, string paidEventID, string failedEventID)
    {
        if (hourlyRate == null) return;
        if (string.IsNullOrEmpty(pendingWages.itemID))
        {
            pendingWages.itemID = hourlyRate.itemID;
            pendingWages.itemNameOverwrite = hourlyRate.itemNameOverwrite;
            pendingWages.itemCountOverride = hourlyRate.itemCountOverride;
        }
        pendingWages.itemCount += hourlyRate.itemCount;

        onPaidEventID = paidEventID ?? "";
        onFailedEventID = failedEventID ?? "";
    }

    /// <summary>"工资（MemberType/job post name - PayeeFirstName）" - falls back to a generic label for
    /// legacy saves from before sourceName/payeeRefID were tracked, or if the payee can no longer be
    /// resolved (e.g. removed from the campaign).</summary>
    public override string GetDisplayName(Manageable owner)
    {
        string name = !string.IsNullOrEmpty(sourceName) ? sourceName : LocalizeDictionary.QueryThenParse("obligation_salary_name_unknown");
        var payee = payeeRefID >= 0 ? scr_System_CampaignManager.current.FindInstanceByID(payeeRefID) : null;
        return LocalizeDictionary.QueryThenParse(payee != null ? "obligation_salary_name" : "obligation_salary_name_noPayee")
            .Replace("$membertype$", name).Replace("$payee$", payee != null ? payee.FirstName : "");
    }

    protected override ItemEntry GetCycleAccrual(Manageable owner)
    {
        var accrued = pendingWages;
        pendingWages = new ItemEntry(accrued.itemID, accrued.itemNameOverwrite, 0, accrued.itemCountOverride);
        return accrued;
    }

    /// <summary>
    /// Scaffolding for a future salary-payment event: gathers the actors a real implementation will need -
    /// the employing faction, that faction's manager, and the specific worker being paid - without
    /// constructing or firing anything yet.
    /// </summary>
    protected override void HandlePaymentEvent(TradeManager manager, Manageable owner, bool success, ItemEntry attempt)
    {
        string eventID = success ? onPaidEventID : onFailedEventID;
        if (string.IsNullOrEmpty(eventID)) return;

        Manageable jobOwnerFaction = owner;
        Character_Trainable jobOwnerManager = owner.Managers.FirstOrDefault();
        Character_Trainable paidActor = payeeRefID >= 0 ? scr_System_CampaignManager.current.FindInstanceByID(payeeRefID) : null;

        // Scaffolding only - eventID/jobOwnerFaction/jobOwnerManager/paidActor are exactly what a future
        // EventInstance(...) call will need. No EventInstance is constructed/fired yet.
    }

    /// <summary>
    /// owed alone understates what's actually due for a daily (or any) cadence salary, since wages accrue
    /// every work hour but owed only gets updated once per resolution - this is what lets a daily paycheck's
    /// "amount due" stay accurate hour-to-hour instead of only refreshing once a day.
    /// </summary>
    public override ItemEntry GetProjectedDue(Manageable owner)
    {
        var projected = new ItemEntry(owed);
        if (string.IsNullOrEmpty(projected.itemID))
        {
            projected.itemID = pendingWages.itemID;
            projected.itemNameOverwrite = pendingWages.itemNameOverwrite;
            projected.itemCountOverride = pendingWages.itemCountOverride;
        }
        projected.itemCount += pendingWages.itemCount;
        return projected;
    }
}
