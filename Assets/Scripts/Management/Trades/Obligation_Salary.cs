using Newtonsoft.Json;
using System.Collections.Generic;

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

    /// <summary>
    /// Debug override: when scr_System_CentralControl.debug_refuse_salary_payment is on, a non-player
    /// employer (which otherwise always succeeds via TryChargeObligation's infinite-Recycler substitution)
    /// always fails instead - lets the failure/suspension/resume flow be tested without bankrupting the
    /// player's own faction. Player factions keep the real charge attempt either way.
    /// </summary>
    protected override bool AttemptPayment(TradeManager manager, Manageable owner, ItemEntry attempt, out string warning)
    {
        if (scr_System_CentralControl.current != null
            && scr_System_CentralControl.current.debug_refuse_salary_payment
            && !owner.isPlayerFaction)
        {
            warning = $"debug_refuse_salary_payment forced failure - Source[{owner.FactionDisplayName}] Target[{(TargetFaction != null ? TargetFaction.FactionDisplayName : "(expenditure)")}]";
            return false;
        }
        return base.AttemptPayment(manager, owner, attempt, out warning);
    }

    protected override ItemEntry GetCycleAccrual(Manageable owner)
    {
        var accrued = pendingWages;
        pendingWages = new ItemEntry(accrued.itemID, accrued.itemNameOverwrite, 0, accrued.itemCountOverride);
        return accrued;
    }

    /// <summary>
    /// Fires the salary payment-outcome event, if any - eventID comes straight off onPaidEventID/
    /// onFailedEventID (unlike Rent/Debt/MembershipFee's live-template lookups, these are legitimately
    /// per-instance here: AccrueHour re-baked them from the current workModule/preset every active work
    /// hour, so they're already effectively live despite being stored fields - see AccrueHour's doc
    /// comment). Silently skipped on a trivial cycle (no hours accrued this cadence). Fired both ways via
    /// FireObligationEventBothSides - label "" for the paying employer's own framing, "payee" for the
    /// employee's home faction's; each side's visibility is independently gated on that side's own
    /// isPlayerFaction (see TradeManager.FireObligationEvent), so an unrelated NPC employer's payroll never
    /// surfaces just because the employee's home faction happens to be player-managed, or vice versa.
    ///
    /// Trust: a resolved (non-trivial) cycle adjusts the payee's trust toward every manager of the employer
    /// - gain = (int)cadence * 2 + 1 on success, loss = gain * 2 on failure (longer cadences mean bigger
    /// paychecks, so both the goodwill earned by honoring one and the resentment earned by missing one
    /// scale with cadence). Applied here, BEFORE firing; the injected trustManagers/trustChange let the
    /// event's check_trust branch narrate it (one aggregated line covering all managers - see
    /// TradeManager.FireObligationEvent's trustManagers doc). resumed (success clearing a prior missed
    /// payment - see cycleWasSuspended) additionally lets OnSalaryPaid's check_resumed branch announce the
    /// employee's return to work; interrupted (the mirror-image - a failure that newly suspends the
    /// obligation, not a repeat failure on an already-suspended backlog) lets OnSalaryFailed's own
    /// check_interrupted branch announce the employee stopping work, mirroring Obligation_Rent's own
    /// resumed/interrupted trailers.
    /// </summary>
    protected override void HandlePaymentEvent(TradeManager manager, Manageable owner, bool success, ItemEntry attempt)
    {
        if (attempt == null || attempt.itemCount <= 0) return;

        string eventID = success ? onPaidEventID : onFailedEventID;
        if (string.IsNullOrEmpty(eventID)) return;

        Character_Trainable paidActor = payeeRefID >= 0 ? scr_System_CampaignManager.current.FindInstanceByID(payeeRefID) : null;

        // owner.GetCharaSocialStandingName gives the full "employer的role" title (e.g. "SunMart的便利店员"),
        // not just the bare role - see Manageable.GetCharaSocialStandingName. Falls back to the plain
        // sourceName field if paidActor can no longer be resolved or no longer holds any status at owner
        // (matches GetDisplayName's own fallback for the same edge case).
        string fullSourceName = paidActor != null ? owner.GetCharaSocialStandingName(paidActor) : "";
        if (string.IsNullOrEmpty(fullSourceName)) fullSourceName = sourceName;

        // payee's trust toward every employer manager moves by the cadence-scaled amount (identical across
        // managers - skip the payee themself in the rare self-managed case)
        int trustGain = (int)cadence * 2 + 1;
        int trustChange = success ? trustGain : -(trustGain * 2);
        List<Character_Trainable> trustManagers = null;
        if (paidActor != null)
        {
            trustManagers = new List<Character_Trainable>();
            foreach (var m in owner.Managers)
            {
                if (m == null || m.RefID == paidActor.RefID) continue;
                trustManagers.Add(m);
                paidActor.Relationships.IncreaseRelationshipWith(m.RefID, RelationshipScoreType.Trust, trustChange);
            }
            if (trustManagers.Count < 1) trustManagers = null;
        }

        FireObligationEventBothSides(manager, owner, eventID, "", "payee", attempt, targetChara: paidActor,
            sourceName: fullSourceName, resumed: success && cycleWasSuspended, interrupted: !success && !cycleWasSuspended,
            trustManagers: trustManagers, trustChange: trustChange);
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
