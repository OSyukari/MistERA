using System;
using Newtonsoft.Json;

/// <summary>
/// Base for a "System 2" faction obligation - salary, membership fee, rent, or debt - resolved on its own
/// PaymentCadence rather than daily like Manageable.TradeOrder (System 1, player-authored trades, left
/// completely untouched by this feature). Template-method shaped: ResolveCycle/Decline drive the shared
/// due-check/accrual/charge/event-fire flow; subclasses only supply what's type-specific (how much accrues
/// each cycle, how much to actually try to collect, and - later - type-specific consequences).
/// </summary>
public abstract class RecurringObligation
{
    [JsonProperty] public string obligationID = "";
    [JsonProperty] public string targetFactionID = "";

    [JsonIgnore] protected Manageable targetFactionCache = null;
    [JsonIgnore]
    public Manageable TargetFaction
    {
        get
        {
            if (targetFactionCache == null) targetFactionCache = scr_System_CampaignManager.current.FindFactionByID(targetFactionID);
            return targetFactionCache;
        }
        set
        {
            targetFactionCache = value;
            targetFactionID = value == null ? "" : value.ID;
        }
    }

    [JsonProperty] public PaymentCadence cadence = PaymentCadence.Daily;

    /// <summary>
    /// Current outstanding balance - doubles as "unpaid wage/fee backlog" for freeze-type obligations
    /// (SalaryObligation/MembershipFeeObligation) and as "debt principal" for accumulate-type ones
    /// (RentObligation/DebtObligation). Greater than zero means suspended - see IsSuspended.
    /// </summary>
    [JsonProperty] public ItemEntry owed = new ItemEntry();

    [JsonIgnore] public bool IsSuspended { get { return owed != null && owed.itemCount > 0; } }

    /// <summary>Fired via TradeManager.FireObligationEvent when a cycle resolves successfully, if set.</summary>
    [JsonProperty] public string onPaidEventID = "";

    /// <summary>Fired via TradeManager.FireObligationEvent when a cycle fails (insufficient funds, or a
    /// future player decline), if set.</summary>
    [JsonProperty] public string onFailedEventID = "";

    /// <summary>Reserved for a future "ask the player to confirm or decline this bill" flow - not enforced
    /// yet; every due obligation auto-resolves regardless of these two fields.</summary>
    [JsonProperty] public bool requiresConfirmation = false;
    [JsonProperty] public bool isPendingDecision = false;

    /// <summary>
    /// True - owed keeps growing every missed cycle (RentObligation, DebtObligation): "we do not want an
    /// instant eviction/default, but the fee should still increase."
    /// False - owed freezes at whatever was first missed and stops accruing further charges until fully
    /// repaid (SalaryObligation, MembershipFeeObligation): "the owed amount stays... but doesn't cumulate."
    /// </summary>
    protected abstract bool ArrearsAccumulate { get; }

    /// <summary>How much new charge to add to owed this cycle (hours-based wage, flat fee/rent, debt interest).</summary>
    protected abstract ItemEntry GetCycleAccrual(Manageable owner);

    /// <summary>
    /// How much to actually try to collect this cycle - defaults to the full owed balance. DebtObligation
    /// overrides this to clamp to its scheduled installment instead of demanding the whole principal at once,
    /// falling back to accrualThisCycle (this same cycle's GetCycleAccrual result, already folded into owed
    /// by the time this runs - see ResolveCycleInternal) when there's no scheduled installment at all, so a
    /// debt with no fixed term still gets charged at least its interest instead of never resolving anything.
    /// </summary>
    protected virtual ItemEntry GetPaymentAttemptAmount(ItemEntry accrualThisCycle)
    {
        return owed;
    }

    /// <summary>
    /// Live "how much would be charged if this resolved right now" - unlike owed (which only reflects state
    /// as of the last actual resolution), this accounts for any accrual that would happen if GetCycleAccrual
    /// ran right now but hasn't actually been folded into owed yet (that only happens on the real
    /// resolution day). Base implementation just returns owed, correct for obligation types with nothing
    /// meaningful to preview ahead of resolution (MembershipFee, Debt). Obligation_Salary overrides this
    /// for its hour-by-hour pendingWages accrual; Obligation_Rent overrides it to preview GetCycleAccrual's
    /// live floor-holdings sum, since without either override this stays 0 (and so invisible in
    /// TradeManager.GetDueSummary/GetIncomingDueSummary) for the entire cadence period until the one real
    /// resolution day actually arrives. Takes owner since some overrides (Rent) need it to compute the
    /// live preview and obligations don't otherwise carry a reference back to their own faction.
    /// </summary>
    public virtual ItemEntry GetProjectedDue(Manageable owner)
    {
        return owed;
    }

    /// <summary>
    /// Live "due" preview line for TradeManager.GetDueSummary - default builds the generic
    /// "$name$：<color>支付</color> $item$" format from GetDisplayName+GetProjectedDue (empty if nothing's
    /// projected). Obligation_Debt overrides this: a single ItemEntry (GetProjectedDue) can't show both
    /// the actual next-installment amount and the much larger total remaining balance in one line, so it
    /// builds a combined line directly instead of relying on this generic template.
    /// </summary>
    public virtual string PrintDue(Manageable owner)
    {
        var projected = GetProjectedDue(owner);
        if (projected == null || projected.itemCount <= 0) return "";
        return LocalizeDictionary.QueryThenParse("obligation_report_due")
            .Replace("$name$", GetDisplayName(owner)).Replace("$item$", projected.Print);
    }

    /// <summary>Same live preview, phrased for the RECEIVING side ("收入") - see TradeManager.GetIncomingDueSummary.</summary>
    public virtual string PrintIncomingDue(Manageable owner)
    {
        var projected = GetProjectedDue(owner);
        if (projected == null || projected.itemCount <= 0) return "";
        return LocalizeDictionary.QueryThenParse("obligation_report_incoming")
            .Replace("$name$", GetDisplayName(owner)).Replace("$item$", projected.Print);
    }

    /// <summary>
    /// Consequence hooks for IsSuspended flipping on/off - deliberately no-op for now (service-halting
    /// consequences like blocking job dispatch or denying school access are a later pass, per subclass).
    /// </summary>
    protected virtual void OnSuspended(Manageable owner) { }
    protected virtual void OnResumed(Manageable owner) { }

    /// <summary>
    /// Localized, type-specific display name for this obligation instance, e.g. "租金（Floor Name）" or
    /// "工资（Clerk）" - used by TradeManager.GetDueSummary/GetIncomingDueSummary/RecordObligationOutcome
    /// when reporting. Takes owner (the faction actually holding this obligation - not necessarily "this
    /// TradeManager's owner" when called from GetIncomingDueSummary, which reports on someone else's
    /// obligations) since obligations don't otherwise carry a reference back to their own faction.
    /// </summary>
    public abstract string GetDisplayName(Manageable owner);

    /// <summary>
    /// Localized, colored one-line summary of this obligation's own (paying) side of a just-resolved
    /// cycle - same due-format as the live GetDueSummary preview (obligation_report_due) on a real
    /// success, or a bare "name: failed" marker plus the warning text on failure. Empty on a trivial
    /// success (nothing actually owed this cycle, e.g. a job post that accrued no hours), so it doesn't
    /// clutter the report. Deliberately stateless - takes the outcome as plain parameters rather than
    /// reading it off stored fields, since TradeManager.RecordObligationOutcome is what keeps the actual
    /// history (see resolvedOutcomes there), not the obligation itself. Virtual since Obligation_Sales
    /// overrides it - unlike every other type, it's a receivable owned by the same faction it credits
    /// (see AttemptPayment), so its own resolution is "收入", never "支付".
    /// </summary>
    public virtual string PrintOutcome(Manageable owner, bool success, ItemEntry attempt, string warning)
    {
        if (!success)
        {
            string line = $"{GetDisplayName(owner)}: failed";
            if (!string.IsNullOrEmpty(warning)) line += "\n" + Utility.WrapTextColor(warning, scr_System_CentralControl.current.DisplaySetting.TextColor_conflict.Color);
            return line;
        }

        if (attempt == null || attempt.itemCount <= 0) return "";
        return LocalizeDictionary.QueryThenParse("obligation_report_due")
            .Replace("$name$", GetDisplayName(owner)).Replace("$item$", attempt.Print);
    }

    /// <summary>Same just-resolved outcome, phrased for the RECEIVING side ("收入") - only ever non-empty
    /// on a real, non-zero transfer; a failure has no incoming side to show for whoever didn't get paid.</summary>
    public string PrintIncome(Manageable owner, ItemEntry attempt)
    {
        if (attempt == null || attempt.itemCount <= 0) return "";
        return LocalizeDictionary.QueryThenParse("obligation_report_incoming")
            .Replace("$name$", GetDisplayName(owner)).Replace("$item$", attempt.Print);
    }

    /// <summary>
    /// Handles this obligation's payment-outcome event on a just-resolved cycle - default behavior fires
    /// the generic per-obligation onPaidEventID/onFailedEventID via TradeManager.FireObligationEvent's
    /// generic actor resolution (owner/target's manager), unchanged from before. Obligation types that
    /// need their own event sourcing and/or actor set (Obligation_Salary sources its event ID from
    /// whichever MemberType/JobPostPreset paid into it and collects the specific workers paid;
    /// Obligation_Rent sources it from the landlord's own override or a world-level fallback) override
    /// this instead of relying on the generic path.
    /// </summary>
    protected virtual void HandlePaymentEvent(TradeManager manager, Manageable owner, bool success, ItemEntry attempt)
    {
        manager.FireObligationEvent(success ? onPaidEventID : onFailedEventID, TargetFaction, attempt);
    }

    /// <summary>
    /// Attempts to actually move this cycle's charge - default behavior is the normal "owner owes target"
    /// direction (TradeManager.TryChargeObligation deducts from owner, credits TargetFaction). Obligation_Sales
    /// overrides this to flip direction entirely: a sales obligation is a receivable, not a payable - money
    /// should materialize into owner's inventory from nothing (simulated clientele, not a real paying
    /// faction), never deducted from anyone, always succeeding.
    /// </summary>
    protected virtual bool AttemptPayment(TradeManager manager, Manageable owner, ItemEntry attempt, out string warning)
    {
        return manager.TryChargeObligation(owner, TargetFaction, attempt, out warning);
    }

    public virtual void ReEstablishParent(Manageable m)
    {
        targetFactionCache = null;
    }

    /// <summary>
    /// Runs one cadence-due check + charge attempt, resolving immediately (single pass) - used by the
    /// not-yet-wired manual confirm/decline UI (TradeManager.ConfirmObligation), not by the daily batch.
    /// No-ops (returns true) if cadence isn't due today. See BeginDailyCycleIfDue/TryResolvePendingCycle/
    /// FinalizeFailedCycle for the daily multi-pass equivalent (TradeManager.ResolveDuePass), which this is
    /// now a thin single-pass wrapper around.
    /// </summary>
    public bool ResolveCycle(TradeManager manager, Manageable owner, DateTime date, out string warning)
    {
        warning = "";
        if (!BeginDailyCycleIfDue(owner, date)) return true;
        if (TryResolvePendingCycle(manager, owner, out warning)) return true;
        FinalizeFailedCycle(manager, owner, warning);
        return false;
    }

    /// <summary>
    /// Explicit player "decline this bill" action (future UI) - accrues this cycle's charge exactly like
    /// ResolveCycle would (bypassing the cadence-due gate, same as before), but skips the actual charge
    /// attempt entirely and forces the failure branch, so it routes through the same freeze/accumulate
    /// arrears accounting as an insufficient-funds failure.
    /// </summary>
    public void Decline(TradeManager manager, Manageable owner)
    {
        BeginCycleIfNeeded(owner);
        FinalizeFailedCycle(manager, owner, "");
    }

    // ---------------------------------------------------------------------
    // Daily multi-pass protocol (TradeManager.ResolveDuePass) - split out of what used to be one atomic
    // ResolveCycleInternal call so a same-day insufficient-funds failure can be retried across several
    // passes (see Observer_globalTime_PaymentResolve) without re-running GetCycleAccrual every retry, which
    // would double-charge/double-accrue. cyclePending/cycleAttempt/cycleWasSuspended carry this cycle's
    // state across passes; none of it is persisted (JsonIgnore) since it never needs to survive a save
    // mid-day and always starts fresh from BeginDailyCycleIfDue/BeginCycleIfNeeded the next time it's due.
    // ---------------------------------------------------------------------

    [JsonIgnore] bool cyclePending = false;
    [JsonIgnore] ItemEntry cycleAttempt = null;

    /// <summary>Whether this obligation was already suspended (owed > 0) before this resolved cycle began -
    /// protected rather than private so HandlePaymentEvent overrides (see Obligation_Rent) can tell a
    /// plain successful cycle apart from one that just cleared a prior missed payment. Still reset to false
    /// at the end of FinishCycle like the other cycle-scoped fields above, but not yet reset by the time
    /// HandlePaymentEvent itself runs.</summary>
    [JsonIgnore] protected bool cycleWasSuspended = false;

    /// <summary>
    /// Ungated: (re-)begins today's cycle if not already begun - folds this cycle's accrual into owed and
    /// computes the amount that'll actually be attempted, exactly once. Freeze-type obligations skip
    /// accruing a new charge while already suspended (owed > 0) - they just keep retrying to collect the
    /// existing backlog; accumulate-type obligations always accrue a fresh charge regardless of suspension
    /// state. Safe to call repeatedly - a no-op once cyclePending is already true.
    /// </summary>
    void BeginCycleIfNeeded(Manageable owner)
    {
        if (cyclePending) return;
        cycleWasSuspended = IsSuspended;

        ItemEntry accrual = null;
        if (ArrearsAccumulate || !cycleWasSuspended)
        {
            accrual = GetCycleAccrual(owner);
            if (accrual != null)
            {
                if (string.IsNullOrEmpty(owed.itemID))
                {
                    owed.itemID = accrual.itemID;
                    owed.itemNameOverwrite = accrual.itemNameOverwrite;
                    owed.itemCountOverride = accrual.itemCountOverride;
                }
                owed.itemCount += accrual.itemCount;
            }
        }

        cycleAttempt = GetPaymentAttemptAmount(accrual);
        cyclePending = true;
    }

    /// <summary>
    /// Gated entry point for the daily batch (TradeManager.ResolveDuePass, pass 0) - checks the cadence-due
    /// date first, then begins the cycle if due. Returns whether this obligation now has a pending cycle to
    /// resolve today (i.e. whether the caller should track it for TryResolvePendingCycle/FinalizeFailedCycle).
    /// </summary>
    public bool BeginDailyCycleIfDue(Manageable owner, DateTime date)
    {
        if (cyclePending) return true;
        if (!PaymentCadenceUtility.IsResolutionDay(cadence, date)) return false;
        BeginCycleIfNeeded(owner);
        return true;
    }

    /// <summary>
    /// Retries collecting this cycle's already-computed attempt amount - safe to call once per pass without
    /// re-accruing. Returns true (and finalizes as a success) once collected, or immediately if nothing was
    /// actually owed this cycle. Returns false (leaving the cycle pending for another pass, or for
    /// FinalizeFailedCycle) on insufficient funds.
    /// </summary>
    public bool TryResolvePendingCycle(TradeManager manager, Manageable owner, out string warning)
    {
        warning = "";
        if (!cyclePending) return true;

        if (cycleAttempt == null || cycleAttempt.itemCount <= 0)
        {
            FinishCycle(manager, owner, success: true, warning: "");
            return true;
        }

        if (AttemptPayment(manager, owner, cycleAttempt, out warning))
        {
            FinishCycle(manager, owner, success: true, warning: "");
            return true;
        }

        return false;
    }

    /// <summary>
    /// Called once a pass's retry has been exhausted (TradeManager.ResolveDuePass's final pass) for
    /// whatever's still pending - applies the same arrears/suspension consequences a failed resolution
    /// always has, using warning as produced by this same pass's TryResolvePendingCycle call (no redundant
    /// extra charge attempt).
    /// </summary>
    public void FinalizeFailedCycle(TradeManager manager, Manageable owner, string warning)
    {
        if (!cyclePending) return;
        FinishCycle(manager, owner, success: false, warning: warning);
    }

    void FinishCycle(TradeManager manager, Manageable owner, bool success, string warning)
    {
        // Snapshot before owed gets decremented below - GetPaymentAttemptAmount's base implementation
        // returns owed itself (the same object), not a copy, so mutating owed in place on success would
        // silently zero cycleAttempt out too (they're the same reference), right before RecordObligationOutcome
        // reads it. Without this copy, every successful resolution would print as if nothing happened.
        var resolvedAmount = cycleAttempt == null ? null : new ItemEntry(cycleAttempt);

        if (success)
        {
            if (cycleAttempt != null) owed.itemCount = Math.Max(0, owed.itemCount - cycleAttempt.itemCount);
            if (cycleWasSuspended && !IsSuspended) OnResumed(owner);
        }
        else
        {
            if (!cycleWasSuspended && IsSuspended) OnSuspended(owner);
        }

        HandlePaymentEvent(manager, owner, success, resolvedAmount);
        manager.RecordObligationOutcome(this, success, resolvedAmount, warning);

        cyclePending = false;
        cycleAttempt = null;
        cycleWasSuspended = false;
    }
}
