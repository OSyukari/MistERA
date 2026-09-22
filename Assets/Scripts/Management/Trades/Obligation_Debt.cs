using System;
using Newtonsoft.Json;

/// <summary>
/// Reusable, JSON-authored template for a debt's terms - interest, cadence, and payment-outcome events -
/// referenced by ID (see Obligation_Debt.debtClassID/DebtClass) instead of authoring those terms inline on
/// every individual loan. Registered in Index_MapPlan.debtClasses/GetByID_DebtClassDef, the same pattern as
/// SalesClienteleDef/MemberType (authored under a data file's top-level "MapPlans" block). Obligation_Debt
/// looks this up LIVE by ID rather than copying its values in at creation time, so balance-patching a
/// class's terms later automatically applies to every debt already using it - same live-template
/// convention as MemberType.workModule/SalesClienteleInstance.BaseDef.
/// </summary>
public class DebtClassDef
{
    public string ID = "";

    /// <summary>Compounding interest per due cycle, as a percentage of the current principal (owed).
    /// Ignored if interestFlatAmount is set and non-zero.</summary>
    public float interestRatePercent = 0f;

    /// <summary>Optional flat interest added per cycle regardless of current balance (e.g. ErAV's narrated
    /// "always +Y450,000/month, never decreases" debt) - takes priority over interestRatePercent when set.</summary>
    public ItemEntry interestFlatAmount = null;

    public PaymentCadence cadence = PaymentCadence.Monthly;

    /// <summary>
    /// Bank-loan style: how many cycles the loan is planned to take to fully repay, used together with
    /// interestRatePercent (NOT interestFlatAmount - a flat fee has no rate to amortize against) to compute
    /// a fixed per-cycle installment once, at debt creation - see Obligation_Debt's constructor. 0 (default)
    /// means no fixed term - the debt only accrues interest with no auto-charged installment at all (manual
    /// repayment only, e.g. ErAV's Kanon debt), unless the caller passes an explicit paymentAmount instead.
    /// </summary>
    public int repaymentTermCycles = 0;

    /// <summary>Fired via TradeManager.FireObligationEvent (see Obligation_Debt.HandlePaymentEvent) when a
    /// debt using this class resolves successfully/fails, if set. Non-empty by default (same "always-on
    /// global default" treatment as WorldPlan.onRentPaidEventID/MembershipFeeInit.onPaidEventID/
    /// MapPlan.WorkModuleInit.onPaidEventID) so every debt fires the generic default event for free.</summary>
    public string onPaidEventID = "OnDebtPaid";
    public string onFailedEventID = "OnDebtFailed";

    /// <summary>Optional localization dictionary key naming this specific debt (e.g. "Kanon's debt" for
    /// debtclass_erav_kanon) - shown in GetDisplayName and the paid/failed event text (as $sourceName$/
    /// $debtName$) in place of the generic "欠款"/"the debt" wording when set, so a faction with several
    /// distinct debts can tell which one a notification is about. Same override/fallback convention as
    /// MembershipFeeInit.membershipFeeName (see Obligation_Debt.GetDisplayName/HandlePaymentEvent).</summary>
    public string debtName = "";
}

/// <summary>
/// A loan: principal owed by the owning (debtee) faction to targetFactionID (the lender). Accumulate-type:
/// interest keeps compounding every due cycle regardless of whether the last installment was paid - no
/// instant default/consequence, matching the ErAV "principal only increases, never decreases" narration.
/// Interest/cadence/payment events all come from debtClassID's DebtClassDef, not authored per instance -
/// see that class's doc comment.
/// </summary>
public class Obligation_Debt : RecurringObligation
{
    /// <summary>Which DebtClassDef (Index_MapPlan.debtClasses) supplies this debt's interest/cadence/
    /// payment-event terms - see DebtClass.</summary>
    [JsonProperty] public string debtClassID = "";

    [JsonIgnore] public DebtClassDef DebtClass { get { return scr_System_Serializer.current.MasterList.MapPlans.GetByID_DebtClassDef(debtClassID); } }

    /// <summary>Scheduled installment charged each due cycle, clamped to the remaining principal so the
    /// final payment never overdraws. Null/zero means interest still accrues but nothing is auto-charged
    /// (manual repayment only, via TradeManager.RepayDebtExtra) - unlike interest/cadence, this is kept
    /// per-instance rather than on DebtClassDef, since two different loans using the same class may still
    /// want different installment schedules (or none at all).</summary>
    [JsonProperty] public ItemEntry paymentAmount = null;

    [JsonProperty] public DateTime originDate;

    /// <summary>
    /// True if this debt's most recently resolved cycle failed to collect its installment - updated in
    /// HandlePaymentEvent, which already receives success/failure every resolution. Purely for quest/event
    /// consumption (see RequireFactionDebt) - not used to drive any UI "is something wrong" signal (that's
    /// derived from PrintOutcome's resolved-outcome strings instead, see
    /// initScript_ManagementOverview.cs).
    /// </summary>
    [JsonProperty] public bool lastPaymentFailed = false;

    /// <summary>Lifetime count of failed collection attempts on this debt - same "quest/event only" scope
    /// as lastPaymentFailed, never reset.</summary>
    [JsonProperty] public int totalFailureCount = 0;

    protected override bool ArrearsAccumulate { get { return true; } }

    public Obligation_Debt() { }

    public Obligation_Debt(string lenderFactionID, ItemEntry principal, string debtClassID, ItemEntry paymentAmount = null)
    {
        this.obligationID = Guid.NewGuid().ToString();
        this.targetFactionID = lenderFactionID;
        this.owed = principal == null ? new ItemEntry() : new ItemEntry(principal);
        this.debtClassID = debtClassID;
        var def = DebtClass;
        this.cadence = def != null ? def.cadence : PaymentCadence.Monthly;
        this.originDate = scr_System_Time.current.getCurrentTime();

        if (paymentAmount != null)
        {
            this.paymentAmount = paymentAmount;
        }
        else if (def != null && def.repaymentTermCycles > 0 && def.interestRatePercent > 0f && this.owed.itemCount > 0)
        {
            // Bank-loan style fixed installment (standard amortization), computed once from the starting
            // principal and never recalculated - a missed payment just leaves more owed (plus continued
            // interest) outstanding, which naturally takes more cycles to clear at this same fixed rate
            // rather than needing any separate "in arrears" tracking.
            double r = def.interestRatePercent / 100.0;
            double p0 = this.owed.itemCount;
            double payment = p0 * r / (1 - Math.Pow(1 + r, -def.repaymentTermCycles));
            this.paymentAmount = new ItemEntry(this.owed.itemID, this.owed.itemNameOverwrite, (int)Math.Ceiling(payment), this.owed.itemCountOverride);
        }
    }

    /// <summary>Alias - "principal" is exactly the base class's owed balance for a debt.</summary>
    [JsonIgnore] public ItemEntry principal { get { return owed; } }

    [JsonIgnore] public Manageable LenderFaction { get { return TargetFaction; } }

    protected override ItemEntry GetCycleAccrual(Manageable owner)
    {
        if (owed == null || owed.itemCount <= 0) return null;
        var def = DebtClass;
        if (def == null) return null;

        if (def.interestFlatAmount != null && def.interestFlatAmount.itemCount != 0)
        {
            return new ItemEntry(def.interestFlatAmount);
        }
        else if (def.interestRatePercent != 0f)
        {
            int interest = (int)Math.Round(owed.itemCount * def.interestRatePercent / 100f);
            return new ItemEntry(owed.itemID, owed.itemNameOverwrite, interest, owed.itemCountOverride);
        }
        return null;
    }

    /// <summary>
    /// Scheduled installment (amortized or manually-authored paymentAmount) takes priority; falls back to
    /// collecting at least this cycle's interest (accrualThisCycle) when there's no scheduled installment
    /// at all (manual-repayment-only debts, e.g. ErAV's Kanon debt) - otherwise nothing would ever
    /// actually be charged/deducted, and owed would just accrue forever without ever resolving anything.
    /// </summary>
    protected override ItemEntry GetPaymentAttemptAmount(ItemEntry accrualThisCycle)
    {
        if (owed == null || owed.itemCount <= 0) return null;

        var target = (paymentAmount != null && paymentAmount.itemCount > 0) ? paymentAmount : accrualThisCycle;
        if (target == null || target.itemCount <= 0) return null;

        var amount = Math.Min(target.itemCount, owed.itemCount);
        return new ItemEntry(owed.itemID, owed.itemNameOverwrite, amount, owed.itemCountOverride);
    }

    /// <summary>
    /// Shows the per-cycle installment actually due (paymentAmount, clamped to remaining owed) instead of
    /// the full remaining principal - a 30M debt with a 500K/month installment should read as "500K due",
    /// not "30M due". Falls back to the full owed balance only when there's no scheduled installment at
    /// all (manual-repayment-only debts, e.g. ErAV's Kanon debt). Used by PrintIncomingDue (the lender's
    /// side) - PrintDue (below) overrides the debtor's own line to show the total balance too.
    /// </summary>
    public override ItemEntry GetProjectedDue(Manageable owner)
    {
        if (paymentAmount != null && paymentAmount.itemCount > 0 && owed != null && owed.itemCount > 0)
        {
            var amount = Math.Min(paymentAmount.itemCount, owed.itemCount);
            return new ItemEntry(owed.itemID, owed.itemNameOverwrite, amount, owed.itemCountOverride);
        }
        return owed;
    }

    /// <summary>
    /// "$name$：<color>支付</color> next-installment （总金额X）" - a single ItemEntry (GetProjectedDue)
    /// can't show both the actual next-installment amount and the much larger total remaining balance at
    /// once, so this builds the combined line directly. "Next installment" is whatever
    /// GetPaymentAttemptAmount would actually try to collect (the amortized/manual installment, clamped to
    /// remaining owed) - or, if there's no scheduled installment at all (manual-repayment-only debts, e.g.
    /// ErAV's Kanon debt), the interest that will accrue this cycle instead, since that's the only amount
    /// actually "due" in any real sense for that case.
    /// </summary>
    public override string PrintDue(Manageable owner)
    {
        if (owed == null || owed.itemCount <= 0) return "";

        var installment = GetPaymentAttemptAmount(GetCycleAccrual(owner));
        if (installment == null || installment.itemCount <= 0) return "";

        string suffix = LocalizeDictionary.QueryThenParse("obligation_debt_total_suffix").Replace("$total$", owed.Print);
        return LocalizeDictionary.QueryThenParse("obligation_report_due")
            .Replace("$name$", GetDisplayName(owner)).Replace("$item$", installment.Print + suffix);
    }

    /// <summary>"欠款（lender faction name）", or DebtClass.debtName in place of the generic "欠款" wording
    /// when set - same override/fallback convention as Obligation_MembershipFee.GetDisplayName's feeName.</summary>
    public override string GetDisplayName(Manageable owner)
    {
        string lenderName = TargetFaction != null ? TargetFaction.FactionDisplayName : targetFactionID;

        var def = DebtClass;
        string debtName = def != null && !string.IsNullOrEmpty(def.debtName)
            ? LocalizeDictionary.QueryThenParse(def.debtName)
            : LocalizeDictionary.QueryThenParse("obligation_debt_generic_name");

        return LocalizeDictionary.QueryThenParse("obligation_debt_name").Replace("$debtName$", debtName).Replace("$lender$", lenderName);
    }

    /// <summary>
    /// Sources onPaidEventID/onFailedEventID from DebtClass instead of a per-instance field - see
    /// DebtClassDef's doc comment for why these live on the shared class, not the individual loan. Also
    /// updates lastPaymentFailed/totalFailureCount, since this is already called once per resolution with
    /// success known. Silently skipped on a trivial cycle (nothing actually charged), same "empty on
    /// trivial success" rule as PrintOutcome/the other obligation types. Fired both ways via
    /// FireObligationEventBothSides, always with DebtClass.debtName (or the generic "欠款"/"the debt"
    /// fallback) as sourceName - same override/fallback convention as Obligation_MembershipFee's feeName -
    /// so a faction with several distinct debts can tell which one a notification is about.
    /// </summary>
    protected override void HandlePaymentEvent(TradeManager manager, Manageable owner, bool success, ItemEntry attempt)
    {
        lastPaymentFailed = !success;
        if (!success) totalFailureCount++;

        if (attempt == null || attempt.itemCount <= 0) return;

        var def = DebtClass;
        string eventID = success ? (def != null ? def.onPaidEventID : "") : (def != null ? def.onFailedEventID : "");

        bool resumed = success && cycleWasSuspended;

        // Only a real (player) faction can ever actually fail a payment - TradeManager.TryChargeObligation
        // substitutes the Recycler (which always "succeeds") for any non-player owner - so owner.Inventory
        // is always the genuine, meaningful balance to report here, never an NPC's faked one.
        ItemEntry available = success ? null : new ItemEntry(attempt.itemID, attempt.itemNameOverwrite, owner.Inventory.GetItemCount(attempt.itemID), attempt.itemCountOverride);

        string debtName = def != null && !string.IsNullOrEmpty(def.debtName)
            ? LocalizeDictionary.QueryThenParse(def.debtName)
            : LocalizeDictionary.QueryThenParse("obligation_debt_generic_name");

        FireObligationEventBothSides(manager, owner, eventID, "", "payee", attempt, available, resumed, sourceName: debtName);
    }
}
