using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// Pending retail sales income for one SalesClienteleInstance, accumulated daily by
/// SalesManager.DailyUpdate (see ItemMatch/AccrueSale) instead of paying out immediately - the currency
/// only actually lands in the faction's inventory once this clientele's own PaymentCadence resolves.
/// A receivable, not a payable: there is no TargetFaction (the "payer" is a simulated clientele, not a
/// real faction) - see AttemptPayment, which credits owner directly instead of the normal owner-pays-
/// target direction. Freeze-type only in the formal sense (ArrearsAccumulate=false); AttemptPayment always
/// succeeds, so arrears never actually occur in practice.
/// </summary>
public class Obligation_Sales : RecurringObligation
{
    /// <summary>Which SalesClienteleInstance (SalesManager.clientele/clientele_override) this obligation
    /// accumulates for - see TradeManager.GetOrCreateSalesObligation.</summary>
    [JsonProperty] public string clienteleID = "";

    /// <summary>Running total of sales income accrued since the last resolved payout - see AccrueSale.</summary>
    [JsonProperty] protected ItemEntry pendingPayment = new ItemEntry();

    /// <summary>
    /// Distinct product name strings that have sold into this obligation's pendingPayment since the last
    /// resolved payout - a plain string list (not object references), so two sales of a differently-priced
    /// or differently-timed but identically-named product merge into one entry instead of duplicating.
    /// Snapshotted into lastCycleSourceNames by GetCycleAccrual (same reasoning as
    /// RecurringObligation.ResolveCycleInternal's attempt-copy fix - without the snapshot, GetDisplayName
    /// would see an already-cleared list immediately after a resolution, since accrual/reset always runs
    /// before the resolved-outcome print).
    /// </summary>
    [JsonProperty] protected List<string> pendingSourceNames = new List<string>();
    [JsonIgnore] protected List<string> lastCycleSourceNames = new List<string>();

    /// <summary>Units sold since the last resolved payout - cycle-scoped, same "pending, snapshotted on
    /// resolve" lifecycle as pendingPayment/pendingSourceNames (not the lifetime totalUnitsSold below).
    /// Shown in GetDisplayName as "how much this cycle's pending total actually represents".</summary>
    [JsonProperty] protected int pendingUnitsSold = 0;
    [JsonIgnore] protected int lastCycleUnitsSold = 0;

    /// <summary>
    /// Units sold today specifically - reset once per day via ResetDailyTracking (called from
    /// SalesManager.DailyUpdate before today's sales run), not persisted (a day-scoped live snapshot, same
    /// convention as Manageable.DailyReport). Backs PrintDailyActivity, the "收支变动" entry showing what
    /// actually sold since the last daily update - distinct from pendingPayment/pendingUnitsSold, which
    /// cover the whole cadence period rather than just today. Deliberately tracks units only, not money -
    /// the sale itself resolves daily, but the payment doesn't (it accumulates into pendingPayment and only
    /// actually pays out on the obligation's own cadence), so showing a money figure in the daily tooltip
    /// would misleadingly imply a transaction already happened.
    /// </summary>
    [JsonIgnore] protected List<string> todaySourceNames = new List<string>();
    [JsonIgnore] protected int todayUnitsSold = 0;

    /// <summary>Lifetime count of individual item instances sold into this obligation, across every
    /// AccrueSale call ever made - never reset, unlike pendingUnitsSold/todayUnitsSold. Not currently
    /// shown in any tooltip; kept as a background stat for future quest/event use.</summary>
    [JsonProperty] public int totalUnitsSold = 0;

    protected override bool ArrearsAccumulate { get { return false; } }

    /// <summary>
    /// Adds one day's sale proceeds for one product to the running total. Called from
    /// SalesManager.DailyUpdate for every clientele/ItemMatch pair that actually sold something today.
    /// </summary>
    public void AccrueSale(ItemEntry payment, string sourceName, int unitsSold)
    {
        if (payment == null || payment.itemCount <= 0) return;

        AddInto(pendingPayment, payment);
        if (!string.IsNullOrEmpty(sourceName) && !pendingSourceNames.Contains(sourceName)) pendingSourceNames.Add(sourceName);
        pendingUnitsSold += unitsSold;

        if (!string.IsNullOrEmpty(sourceName) && !todaySourceNames.Contains(sourceName)) todaySourceNames.Add(sourceName);
        todayUnitsSold += unitsSold;

        totalUnitsSold += unitsSold;
    }

    static void AddInto(ItemEntry target, ItemEntry add)
    {
        if (string.IsNullOrEmpty(target.itemID))
        {
            target.itemID = add.itemID;
            target.itemNameOverwrite = add.itemNameOverwrite;
            target.itemCountOverride = add.itemCountOverride;
        }
        target.itemCount += add.itemCount;
    }

    static string BuildName(List<string> sources, int units)
    {
        string joined = sources.Count > 0 ? string.Join(", ", sources) : LocalizeDictionary.QueryThenParse("obligation_sales_name_unknown");
        return LocalizeDictionary.QueryThenParse("obligation_sales_name")
            .Replace("$sources$", joined).Replace("$units$", units.ToString());
    }

    /// <summary>"销售（product name(s)） - $units$件" for this cycle's pending total - prefers the
    /// currently-accruing source list/count (live "due" preview case); once that's empty (right after a
    /// resolution reset it, nothing new has sold yet this cycle) falls back to whichever sources/count were
    /// actually part of the last resolved payout, so PrintOutcome's just-resolved line still names the
    /// right products instead of showing an already-cleared list.</summary>
    public override string GetDisplayName(Manageable owner)
    {
        bool hasPending = pendingSourceNames.Count > 0;
        return BuildName(hasPending ? pendingSourceNames : lastCycleSourceNames, hasPending ? pendingUnitsSold : lastCycleUnitsSold);
    }

    protected override ItemEntry GetCycleAccrual(Manageable owner)
    {
        var accrued = pendingPayment;
        pendingPayment = new ItemEntry(accrued.itemID, accrued.itemNameOverwrite, 0, accrued.itemCountOverride);
        lastCycleSourceNames = pendingSourceNames;
        pendingSourceNames = new List<string>();
        lastCycleUnitsSold = pendingUnitsSold;
        pendingUnitsSold = 0;
        return accrued;
    }

    /// <summary>
    /// owed alone understates what's actually pending, since AccrueSale runs every day but owed only
    /// updates once per cadence resolution - lets the "due" preview stay accurate day-to-day instead of
    /// only refreshing on the cadence's own paydate.
    /// </summary>
    public override ItemEntry GetProjectedDue(Manageable owner)
    {
        var projected = new ItemEntry(owed);
        if (string.IsNullOrEmpty(projected.itemID))
        {
            projected.itemID = pendingPayment.itemID;
            projected.itemNameOverwrite = pendingPayment.itemNameOverwrite;
            projected.itemCountOverride = pendingPayment.itemCountOverride;
        }
        projected.itemCount += pendingPayment.itemCount;
        return projected;
    }

    /// <summary>Sales income is always incoming to owner ("收入"), never outgoing - unlike every other
    /// obligation type, it's a receivable owned by the same faction it credits (see AttemptPayment), so the
    /// live "due" preview must use the incoming format, not the generic base-class "支付" template.</summary>
    public override string PrintDue(Manageable owner)
    {
        var projected = GetProjectedDue(owner);
        if (projected == null || projected.itemCount <= 0) return "";
        return LocalizeDictionary.QueryThenParse("obligation_report_incoming")
            .Replace("$name$", GetDisplayName(owner)).Replace("$item$", projected.Print);
    }

    /// <summary>Same reasoning as PrintDue - a just-resolved sale is always "收入" for owner, never "支付".</summary>
    public override string PrintOutcome(Manageable owner, bool success, ItemEntry attempt, string warning)
    {
        if (!success)
        {
            string line = $"{GetDisplayName(owner)}: failed";
            if (!string.IsNullOrEmpty(warning)) line += "\n" + Utility.WrapTextColor(warning, scr_System_CentralControl.current.DisplaySetting.TextColor_conflict.Color);
            return line;
        }

        if (attempt == null || attempt.itemCount <= 0) return "";
        return LocalizeDictionary.QueryThenParse("obligation_report_incoming")
            .Replace("$name$", GetDisplayName(owner)).Replace("$item$", attempt.Print);
    }

    /// <summary>Clears today's activity tracking - called once at the start of each day's
    /// SalesManager.DailyUpdate, before that day's sales run, so PrintDailyActivity only ever reflects the
    /// most recent day rather than accumulating across multiple days between UI checks.</summary>
    public void ResetDailyTracking()
    {
        todaySourceNames = new List<string>();
        todayUnitsSold = 0;
    }

    /// <summary>
    /// "收支变动" entry for what actually sold today specifically - distinct from GetDisplayName/PrintDue,
    /// which describe the whole cadence period's pending total. Units only, no money - the sale itself
    /// resolves daily, but the payment doesn't pay out until the obligation's own cadence, so showing a
    /// money figure here (unlike every other "收支变动" line, which only ever appears once something has
    /// actually been paid) would misleadingly read as a completed transaction. Queried live (not
    /// stored/logged), same pattern as PrintDue reading pendingPayment - see
    /// initScript_ManagementOverview.cs, which calls this directly on every Obligation_Sales rather than
    /// through TradeManager.GetResolvedSummary (that path is for actual cadence resolutions, which this
    /// isn't - a sale can sell every day while its obligation only resolves once a month).
    /// </summary>
    public string PrintDailyActivity(Manageable owner)
    {
        if (todayUnitsSold <= 0) return "";
        return BuildName(todaySourceNames, todayUnitsSold);
    }

    /// <summary>
    /// A sales obligation is a receivable: the "payer" is a simulated clientele, not a real faction, so
    /// the normal owner-pays-target charge (TradeManager.TryChargeObligation) doesn't apply here - instead
    /// this materializes the amount directly into owner's inventory, unconditionally (no player-vs-NPC
    /// gating - matches the pre-migration behavior, which credited Owner.Inventory directly regardless of
    /// isPlayerFaction). Always succeeds; there's no "insufficient funds" concept for income.
    /// </summary>
    protected override bool AttemptPayment(TradeManager manager, Manageable owner, ItemEntry attempt, out string warning)
    {
        warning = "";
        if (attempt != null && attempt.itemCount > 0 && !string.IsNullOrEmpty(attempt.itemID))
        {
            owner.Inventory.AddItem(WorldManager.Instantiate(attempt.itemID, attempt.itemCountOverride ? "" : attempt.itemNameOverwrite, attempt.itemCount));
        }
        return true;
    }
}
