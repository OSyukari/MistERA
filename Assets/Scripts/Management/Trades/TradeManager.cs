using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

/// <summary>
/// Owns both faction payment systems: "System 1" (TradeOrders - player-authored, daily-resolved trades;
/// storage only, resolution logic stays on Manageable.OnDayUpdate_PaymentResolve/AddTradeOrder/etc) and
/// "System 2" (Obligations - salary/rent/fee/debt RecurringObligations, resolved on their own PaymentCadence
/// with per-type arrears/consequence handling, via ResolveDuePass). The two systems deliberately share only
/// storage ownership here, not resolution logic. Instantiated per-Manageable in its constructor the same way
/// SalesManager/MealManager are (see Manageable.SalesManager/MealManager); ResolveDuePass is called from
/// Manageable.OnDayUpdate_PaymentResolve, once per Observer_globalTime_PaymentResolve pass, alongside that
/// method's own TradeOrder resolution.
/// </summary>
public class TradeManager
{
    [JsonIgnore] protected Manageable owner;

    public TradeManager() { }
    public TradeManager(Manageable owner)
    {
        this.owner = owner;
        // Obligation_Rent always exists from the start - it carries owed/arrears state that must persist
        // whether or not any floor currently has rent to charge; nothing about rent is created on demand.
        // GetCycleAccrual walks owner.ManagedFloors itself each cycle and simply totals to null/0 when no
        // floor has an applicable rent cost, so this is inert until there's actually something to collect.
        Obligations.Add(new Obligation_Rent { obligationID = Guid.NewGuid().ToString(), cadence = PaymentCadence.Monthly });
    }

    public void ReEstablishParent(Manageable m)
    {
        this.owner = m;
        foreach (var t in TradeOrders) t.ReEstablishParent(m);
        foreach (var o in Obligations)
        {
            o.ReEstablishParent(m);
            NotifyTargetOfObligation(o);
        }

        // backfill for saves created before Obligation_Rent always existed from construction - one-time
        // migration at load, not a recurring/lazy creation check.
        if (!Obligations.OfType<Obligation_Rent>().Any())
        {
            Obligations.Add(new Obligation_Rent { obligationID = Guid.NewGuid().ToString(), cadence = PaymentCadence.Monthly });
        }
    }

    /// <summary>
    /// Live registry (not persisted - rebuilt via NotifyTargetOfObligation, called from ReEstablishParent
    /// and whenever a new obligation is added) of every OTHER faction's TradeManager that currently owes
    /// this faction at least one obligation, i.e. this faction is the targetFactionID of one or more of
    /// their Obligations. Lets GetIncomingDueSummary find "who will pay me" without scanning every
    /// registered faction in the campaign - only this already-known, small set.
    /// </summary>
    [JsonIgnore] protected List<TradeManager> payingTradeManagers = new List<TradeManager>();

    /// <summary>Called by another faction's TradeManager to register itself as owing this faction money.</summary>
    public void RegisterAsPayer(TradeManager payer)
    {
        if (payer != null && !payingTradeManagers.Contains(payer)) payingTradeManagers.Add(payer);
    }

    /// <summary>
    /// Notifies obligation's target faction that this TradeManager will be paying it - see
    /// payingTradeManagers/RegisterAsPayer. Called whenever an obligation is added (AddDebt/
    /// GetOrCreateSalaryObligation) and for every existing obligation whenever this TradeManager's parent
    /// is reestablished after deserialization (the target's registry is JsonIgnore and doesn't survive a
    /// save/load on its own).
    /// </summary>
    void NotifyTargetOfObligation(RecurringObligation obligation)
    {
        var target = obligation.TargetFaction;
        if (target != null && target.TradeManager != null) target.TradeManager.RegisterAsPayer(this);
    }

    /// <summary>
    /// "System 1" storage - player-authored, daily-resolved TradeOrders. Manageable.TradeOrders is a
    /// pass-through property onto this list (see that property's doc comment for the save-migration
    /// story); Manageable.AddTradeOrder/ProcessAllTransactions/etc keep their exact existing bodies and
    /// resolution logic untouched, just now reading/writing this list instead of a field on Manageable
    /// itself. Kept deliberately separate from Obligations below - the two systems share storage
    /// ownership here, not resolution logic.
    /// </summary>
    [JsonProperty] public List<Manageable.TradeOrder> TradeOrders = new List<Manageable.TradeOrder>();

    /// <summary>"System 2" storage - salary/rent/fee/debt RecurringObligations.</summary>
    [JsonProperty] public List<RecurringObligation> Obligations = new List<RecurringObligation>();

    /// <summary>
    /// Floors this faction is actually renting from someone else, keyed by Floor_Instance.refID -> landlord
    /// factionID (empty string = renting, but no specific landlord tracked yet - charged to the Recycler
    /// as a plain expenditure). A floor NOT present in this dictionary is owned outright. Populated at
    /// floor-add time (Manageable.AddToFaction, when isRenting is true) - see
    /// Obligation_Rent.GetCycleAccrual for how this drives what's charged. Existing saves predating this
    /// dictionary have no entries for their floors, so they're treated as owning them - intentional.
    /// </summary>
    [JsonProperty] public Dictionary<int, string> rentedFloors = new Dictionary<int, string>();

    /// <summary>
    /// Last-resolved-cycle print line for every obligation, per cadence, keyed by obligationID - the
    /// historical counterpart to the live GetDueSummary/GetIncomingDueSummary preview. TradeManager owns
    /// this storage (not the obligation itself - see RecurringObligation.PrintOutcome/PrintIncome, which
    /// are stateless), so a fresh resolution always atomically overwrites exactly its own obligationID's
    /// entry. This is what a single shared, wholesale-Cleared() report got wrong: an obligation's
    /// resolution can write into a DIFFERENT faction's TradeManager (the payer recording an "incoming" line
    /// for whoever it paid, straight into that faction's own dictionary here - see RecordObligationOutcome),
    /// and with one shared list cleared once per resolving cadence, that faction's own same-day resolution
    /// pass could wipe the just-written line out again depending on iteration order. Keying by obligationID
    /// avoids that entirely: each obligation's entry is only ever touched by that same obligation resolving,
    /// never by anything else's cadence-wide clear.
    /// </summary>
    [JsonProperty] protected Dictionary<PaymentCadence, Dictionary<string, string>> resolvedOutcomes = new Dictionary<PaymentCadence, Dictionary<string, string>>();

    Dictionary<string, string> GetResolvedOutcomes(PaymentCadence cadence)
    {
        if (!resolvedOutcomes.TryGetValue(cadence, out var dict))
        {
            dict = new Dictionary<string, string>();
            resolvedOutcomes[cadence] = dict;
        }
        return dict;
    }

    /// <summary>
    /// Live registry (JsonIgnore) of every obligation due today that's still waiting on a successful charge
    /// - populated fresh each day on pass 0, drained as each one's payment attempt succeeds, matching the
    /// same "snapshot copy, retry, remove on resolve" shape as Manageable.pendingTradeOrdersToday.
    /// </summary>
    [JsonIgnore] List<RecurringObligation> pendingObligationsToday = new List<RecurringObligation>();

    /// <summary>
    /// Multi-pass counterpart to the old single-shot ResolveDue - called once per
    /// Observer_globalTime_PaymentResolve pass (see Manageable.OnDayUpdate_PaymentResolve). Pass 0 snapshots
    /// every obligation actually due today (RecurringObligation.BeginDailyCycleIfDue folds in this cycle's
    /// accrual exactly once); every pass then retries collecting each still-pending one
    /// (TryResolvePendingCycle, safe to call repeatedly - it never re-accrues), and the final pass treats
    /// whatever's still stuck as a real failure (FinalizeFailedCycle) instead of retrying forever. Spreading
    /// this across passes - the same passes TradeOrders retry across - is what lets e.g. today's incoming
    /// sales/salary payment actually be available in time to cover today's rent, even across factions (every
    /// faction's own ResolveDuePass gets called once per pass before any of them sees the next pass).
    /// </summary>
    public void ResolveDuePass(DateTime date, int pass, int totalPasses)
    {
        if (pass == 0)
        {
            // Must run before the snapshot loop below, not in Manageable.OnDayUpdate_1 (which only fires
            // after this whole multi-pass settlement already completed for today) - a membership-fee
            // obligation ensured too late would miss today's BeginDailyCycleIfDue check entirely and not
            // resolve until its cadence's next occurrence, not just "one day late".
            EnsureMembershipFeeObligations();

            pendingObligationsToday.Clear();
            foreach (var obligation in Obligations)
            {
                if (obligation.BeginDailyCycleIfDue(owner, date)) pendingObligationsToday.Add(obligation);
            }
        }

        bool isFinalPass = pass == totalPasses - 1;
        for (int i = pendingObligationsToday.Count - 1; i >= 0; i--)
        {
            var obligation = pendingObligationsToday[i];
            if (obligation.TryResolvePendingCycle(this, owner, out string warning))
            {
                pendingObligationsToday.RemoveAt(i);
            }
            else if (isFinalPass)
            {
                obligation.FinalizeFailedCycle(this, owner, warning);
                pendingObligationsToday.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Transfers amount from owner to target - unlike TradeOrder, every System 2 obligation type is a
    /// one-directional "owner owes target" charge, so there's no Entry/Cost/reversed barter shape to model.
    /// target may be null (e.g. Obligation_Rent today, which has no landlord/recipient concept yet) - in
    /// that case the charge is still deducted from a real player owner as a pure expenditure (routed to
    /// the shared Recycler to be disposed of, same as any other consumed-and-discarded items elsewhere in
    /// the codebase) rather than transferred anywhere. Non-player factions are backed by the shared,
    /// infinite Recycler inventory (same substitution TradeOrder.ProcessOrder uses), so only player-managed
    /// factions need real fund verification - NPC-to-NPC obligations always succeed trivially.
    /// </summary>
    public bool TryChargeObligation(Manageable owner, Manageable target, ItemEntry amount, out string warning)
    {
        warning = "";
        if (amount == null || amount.itemID == "" || amount.itemCount <= 0) return true;

        FactionInventory recycler = scr_System_CampaignManager.current.Recycler;
        FactionInventory ownerInv = owner.isPlayerFaction ? owner.Inventory : recycler;
        FactionInventory targetInv = target != null && target.isPlayerFaction && target != owner ? target.Inventory : recycler;

        if (ownerInv == recycler || ownerInv.HasRequiredItems(amount, 1))
        {
            if (ownerInv == recycler)
            {
                // non-player owner: nothing real to deduct; materialize the amount for a real target, if any
                if (target != null) targetInv.AddItem(WorldManager.Instantiate(amount.itemID, amount.itemCountOverride ? "" : amount.itemNameOverwrite, amount.itemCount));
            }
            else
            {
                var removed = ownerInv.RemoveItem(amount.itemID, amount.itemCount);
                if (target != null) targetInv.AddItem(removed);
                else recycler.AddItem(removed); // no target faction - pure expenditure, items are simply spent
            }

            return true;
        }
        else
        {
            warning = $"obligation payment Source[{owner.FactionDisplayName}] Target[{(target != null ? target.FactionDisplayName : "(expenditure)")}] Failed - insufficient funds";
            return false;
        }
    }

    /// <summary>
    /// Records this cycle's resolved/failed outcome - asks the obligation to print itself (see
    /// RecurringObligation.PrintOutcome/PrintIncome, both stateless) and stores the resulting string into
    /// resolvedOutcomes: the paying (this) side always, and, if TargetFaction resolves, the receiving
    /// side too - stored directly into that faction's own TradeManager, same as a TradeOrder's transfer
    /// touches both sides' inventories in one step. Called once per obligation at the end of
    /// ResolveCycle/Decline.
    /// </summary>
    public void RecordObligationOutcome(RecurringObligation obligation, bool success, ItemEntry attempt, string warning)
    {
        GetResolvedOutcomes(obligation.cadence)[obligation.obligationID] = obligation.PrintOutcome(owner, success, attempt, warning);

        var target = obligation.TargetFaction;
        if (target == null || target.TradeManager == null) return;

        var targetOutcomes = target.TradeManager.GetResolvedOutcomes(obligation.cadence);
        var incomingLine = success ? obligation.PrintIncome(owner, attempt) : "";
        if (string.IsNullOrEmpty(incomingLine)) targetOutcomes.Remove(obligation.obligationID);
        else targetOutcomes[obligation.obligationID] = incomingLine;
    }

    /// <summary>
    /// Historical counterpart to GetDueSummary/GetIncomingDueSummary: what actually happened last time each
    /// relevant obligation under this cadence resolved (paid/received, or failed) - already merges both
    /// directions, since RecordObligationOutcome writes the receiving side straight into the target
    /// faction's own copy of this same dictionary.
    /// </summary>
    public IEnumerable<string> GetResolvedSummary(PaymentCadence cadence)
    {
        foreach (var line in GetResolvedOutcomes(cadence).Values)
        {
            if (!string.IsNullOrEmpty(line)) yield return line;
        }
    }

    /// <summary>
    /// Live "how much is currently projected to be paid/received at the next resolution" for every
    /// obligation under the given cadence - computed fresh from each obligation's current state
    /// (RecurringObligation.GetProjectedDue) every time this is called, rather than a value cached at the
    /// last resolution. This is what keeps e.g. a daily-cadence salary's "amount due" accurate hour to
    /// hour (as AccrueHour runs) instead of only refreshing once a day when ResolveDue actually charges it.
    /// </summary>
    public IEnumerable<string> GetDueSummary(PaymentCadence cadence)
    {
        foreach (var obligation in Obligations)
        {
            if (obligation.cadence != cadence) continue;
            var line = obligation.PrintDue(owner);
            if (!string.IsNullOrEmpty(line)) yield return line;
        }
    }

    /// <summary>
    /// Live "how much is projected to be RECEIVED from other factions at their next resolution" - the
    /// counterpart to GetDueSummary. An obligation only ever lives on the paying/owing faction's
    /// TradeManager (e.g. an employer's Obligation_Salary owed to an employee's home faction lives on the
    /// employer, not the home faction), so this faction's own Obligations has nothing to show for money
    /// owed TO it. Only scans payingTradeManagers (see RegisterAsPayer) - the small, already-known set of
    /// TradeManagers that have registered as owing this faction - rather than every faction in the campaign.
    /// </summary>
    public IEnumerable<string> GetIncomingDueSummary(PaymentCadence cadence)
    {
        foreach (var payer in payingTradeManagers)
        {
            if (payer == null) continue;
            foreach (var obligation in payer.Obligations)
            {
                if (obligation.cadence != cadence) continue;
                if (obligation.targetFactionID != owner.ID) continue;

                var line = obligation.PrintIncomingDue(payer.owner);
                if (!string.IsNullOrEmpty(line)) yield return line;
            }
        }
    }

    /// <summary>
    /// Fires eventID (if set) via a direct-by-ID EventInstance, mirroring FactionUtility.SendImprisonEvent's
    /// pattern rather than a trigger-tag scan, since each obligation names one specific event. No-ops if
    /// eventID is empty or no acting character can be resolved for owner. Always exposes $factionName$ -
    /// owner's own FactionDisplayName, i.e. whichever faction this obligation actually belongs to (paid or
    /// failed to pay). amount/available are exposed as $amount$/$available$ via ItemEntry.Print (item name
    /// + count, not a bare number) - available e.g. for a failed-payment event that wants to show "$amount$
    /// needed, only $available$ on hand" (see Obligation_Rent.HandlePaymentEvent). resumed, if true, adds a
    /// "resumed" AppendStrings key with no particular value - callers' event JSON can branch on its mere
    /// presence via the ExistAppendStrings executor (see OnRentPaid's check_resumed branch) rather than
    /// needing a dedicated event/label per caller for that same "and by the way, X resumed" add-on.
    /// targetChara, if set, is used as "target" instead of resolving a generic manager off counterpart -
    /// for callers whose event is actually about one specific character rather than a faction-to-faction
    /// relationship (see Obligation_MembershipFee.HandlePaymentEvent, which fires one event per affected
    /// member rather than one per resolved cycle). feeName, if set, is exposed as $feeName$ - a
    /// caller-resolved noun (e.g. a school's membershipFee overriding the generic "会员费"/"Membership fee"
    /// wording with "学费") the event text can substitute in wherever it would otherwise hardcode the
    /// generic term. sourceName, if set, is exposed as $sourceName$ - e.g. the MemberType/job post name a
    /// salary payment was for (see Obligation_Salary.HandlePaymentEvent).
    ///
    /// Regardless of targetChara, counterpart's own FactionDisplayName (when counterpart is set) is always
    /// additionally exposed as $counterpartName$ - the raw interpolation engine only ever resolves "$X.name$"
    /// off a Character_Trainable (see Utility.CollectString), so this is the only way to name the OTHER
    /// faction directly rather than a manager's personal name. Naturally swaps identity across the two
    /// FireObligationEventBothSides calls (payer call: counterpart=TargetFaction; payee call:
    /// counterpart=the original owner) exactly like $factionName$ does, so e.g. Obligation_Rent's "rent"
    /// message can say "paid rent to $counterpartName$" on one side and "collected rent from
    /// $counterpartName$" on the other, each correctly naming whichever faction isn't the one speaking.
    ///
    /// displayOverride is always owner.isPlayerFaction alone - single-sided, never considering counterpart.
    /// Every obligation type fires this once "as" the payer and once "as" the payee (see
    /// RecurringObligation.FireObligationEventBothSides), each via that side's own TradeManager, so each
    /// call's owner is already whichever faction that particular firing is narrating for - no OR needed.
    /// </summary>
    public void FireObligationEvent(string eventID, Manageable counterpart, ItemEntry amount, string label = "", ItemEntry available = null, bool resumed = false, Character_Trainable targetChara = null, string feeName = "", string sourceName = "")
    {
        if (string.IsNullOrEmpty(eventID)) return;

        Character_Trainable actingChara = owner.isPlayerFaction
            ? scr_System_CampaignManager.current.Player
            : owner.Managers.FirstOrDefault();
        if (actingChara == null) return;

        var ev = new EventInstance(actingChara, eventID, label);
        ev.displayOverride = owner.isPlayerFaction;
        ev.AppendStrings["factionName"] = new List<string> { owner.FactionDisplayName };
        if (counterpart != null) ev.AppendStrings["counterpartName"] = new List<string> { counterpart.FactionDisplayName };

        if (targetChara != null)
        {
            ev.Targets["target"] = new List<Character_Trainable> { targetChara };
        }
        else if (counterpart != null)
        {
            Character_Trainable counterpartChara = counterpart.isPlayerFaction
                ? scr_System_CampaignManager.current.Player
                : counterpart.Managers.FirstOrDefault();
            if (counterpartChara != null) ev.Targets["target"] = new List<Character_Trainable> { counterpartChara };
        }

        if (amount != null)
        {
            if (!ev.AppendStrings.ContainsKey("amount")) ev.AppendStrings["amount"] = new List<string>();
            ev.AppendStrings["amount"].Add(amount.Print);
        }

        if (available != null)
        {
            if (!ev.AppendStrings.ContainsKey("available")) ev.AppendStrings["available"] = new List<string>();
            ev.AppendStrings["available"].Add(available.Print);
        }

        if (resumed)
        {
            if (!ev.AppendStrings.ContainsKey("resumed")) ev.AppendStrings["resumed"] = new List<string>();
            ev.AppendStrings["resumed"].Add("true");
        }

        if (!string.IsNullOrEmpty(feeName))
        {
            if (!ev.AppendStrings.ContainsKey("feeName")) ev.AppendStrings["feeName"] = new List<string>();
            ev.AppendStrings["feeName"].Add(feeName);
        }

        if (!string.IsNullOrEmpty(sourceName))
        {
            if (!ev.AppendStrings.ContainsKey("sourceName")) ev.AppendStrings["sourceName"] = new List<string>();
            ev.AppendStrings["sourceName"].Add(sourceName);
        }

        scr_UpdateHandler.current.EventHandler.StartEvent(ev, false);
    }

    // ---------------------------------------------------------------------
    // Salary convenience helper (operates over Obligations.OfType<Obligation_Salary>())
    // ---------------------------------------------------------------------

    /// <summary>
    /// Finds or creates the Obligation_Salary owed to target at the given cadence - one obligation per
    /// (target faction, cadence, sourceName, payeeRefID) combination, so each individual worker gets their
    /// own instance/accumulator even when several people share the same job post/MemberType under the
    /// same employer and cadence - see Obligation_Salary.AccrueHour/GetDisplayName (which names the
    /// specific payee) and HandlePaymentEvent (which needs a single, unambiguous "actor getting paid").
    /// Called every active work hour from Manageable.OnHourUpdate.
    /// </summary>
    public Obligation_Salary GetOrCreateSalaryObligation(Manageable target, PaymentCadence cadence, string sourceName, int payeeRefID)
    {
        sourceName = sourceName ?? "";
        var existing = Obligations.OfType<Obligation_Salary>().FirstOrDefault(x => x.targetFactionID == target.ID && x.cadence == cadence && x.sourceName == sourceName && x.payeeRefID == payeeRefID);
        if (existing != null) return existing;

        var obligation = new Obligation_Salary();
        obligation.obligationID = Guid.NewGuid().ToString();
        obligation.TargetFaction = target;
        obligation.cadence = cadence;
        obligation.sourceName = sourceName;
        obligation.payeeRefID = payeeRefID;
        Obligations.Add(obligation);
        NotifyTargetOfObligation(obligation);
        return obligation;
    }

    // ---------------------------------------------------------------------
    // Sales convenience helper (operates over Obligations.OfType<Obligation_Sales>())
    // ---------------------------------------------------------------------

    /// <summary>
    /// Finds or creates the Obligation_Sales for this faction's own clienteleID/cadence pair - one per
    /// clientele, so every ItemMatch that sells to a given clientele on a given day accrues into the same
    /// pending payment/source list (see Obligation_Sales.AccrueSale) regardless of which product it was.
    /// Unlike GetOrCreateSalaryObligation/GetOrCreateRentObligationFor, this never sets TargetFaction -
    /// a sales obligation is owned by the selling faction itself with no counterpart (see
    /// Obligation_Sales.AttemptPayment). Called from SalesManager.DailyUpdate.
    /// </summary>
    public Obligation_Sales GetOrCreateSalesObligation(string clienteleID, PaymentCadence cadence)
    {
        var existing = Obligations.OfType<Obligation_Sales>().FirstOrDefault(x => x.clienteleID == clienteleID);
        if (existing != null) return existing;

        var obligation = new Obligation_Sales();
        obligation.obligationID = Guid.NewGuid().ToString();
        obligation.clienteleID = clienteleID;
        obligation.cadence = cadence;
        Obligations.Add(obligation);
        return obligation;
    }

    // ---------------------------------------------------------------------
    // Rent convenience helper (operates over Obligations.OfType<Obligation_Rent>())
    // ---------------------------------------------------------------------

    /// <summary>
    /// Finds or creates the Obligation_Rent targeting a specific real landlord faction ID - one per
    /// landlord (Obligation_Rent.GetCycleAccrual sums every rented floor whose TradeManager.rentedFloors
    /// entry matches this landlordFactionID). Deliberately does NOT resolve/verify the landlord faction
    /// here - only targetFactionID (a raw string) is set, never the resolving TargetFaction setter -
    /// since this is called from Manageable.AddToFaction at world-init time, when the landlord faction may
    /// not have been instantiated yet depending on faction init order. Resolution happens lazily, on
    /// first actual access to TargetFaction (same cached-lazy-lookup pattern already used everywhere else
    /// on RecurringObligation/Obligation_Debt), by which point every faction is guaranteed to exist.
    /// </summary>
    public Obligation_Rent GetOrCreateRentObligationFor(string landlordFactionID)
    {
        var existing = Obligations.OfType<Obligation_Rent>().FirstOrDefault(x => x.targetFactionID == landlordFactionID);
        if (existing != null) return existing;

        var obligation = new Obligation_Rent();
        obligation.obligationID = Guid.NewGuid().ToString();
        obligation.targetFactionID = landlordFactionID;
        obligation.cadence = PaymentCadence.Monthly;
        Obligations.Add(obligation);
        return obligation;
    }

    // ---------------------------------------------------------------------
    // MembershipFee convenience helpers (operate over Obligations.OfType<Obligation_MembershipFee>())
    // ---------------------------------------------------------------------

    /// <summary>
    /// Finds or creates the Obligation_MembershipFee targeting a specific provider faction at the given
    /// cadence - one per (provider, cadence) pair, so e.g. two children of the same household both
    /// enrolled at the same school under the same cadence accrue into one shared obligation
    /// (Obligation_MembershipFee.GetCycleAccrual sums every relevant member itself). Never sets a fee
    /// amount - that's always read live off the member's current MemberType.membershipFee, never stored
    /// here (see GetRelevantFees).
    /// </summary>
    public Obligation_MembershipFee GetOrCreateMembershipFeeObligation(Manageable providerFaction, PaymentCadence cadence)
    {
        var existing = Obligations.OfType<Obligation_MembershipFee>().FirstOrDefault(x => x.targetFactionID == providerFaction.ID && x.cadence == cadence);
        if (existing != null) return existing;

        var obligation = new Obligation_MembershipFee();
        obligation.obligationID = Guid.NewGuid().ToString();
        obligation.TargetFaction = providerFaction;
        obligation.cadence = cadence;
        Obligations.Add(obligation);
        NotifyTargetOfObligation(obligation);
        return obligation;
    }

    /// <summary>
    /// Ensures an Obligation_MembershipFee shell exists (on THIS, the paying faction's own TradeManager)
    /// for whichever fee-charging faction c currently works at - only acts for the faction that's actually
    /// the tracked source for that particular work faction (Character_Factions.
    /// GetWorkFactionSourceOrDefault: the faction that dispatched c there, defaulting to c's home faction
    /// when untracked - see AddWorkFaction's sourceFaction param). A character can be "managed" by more
    /// than one faction at once (e.g. a school manages its students as members too, and a job's employer
    /// manages its workers), so this is checked per work faction rather than once for the whole character -
    /// letting a work faction that dispatched c elsewhere (e.g. sent an employee to study at a school) pick
    /// up that fee instead of always billing home. Also requires owner to be player-managed (NPC-to-NPC
    /// fees are trivially faked via the Recycler substitution anyway - TryChargeObligation - so this would
    /// be wasted work for an NPC faction). Only ensures the (work faction, cadence) pairing exists - the
    /// fee amount itself is never stored here; Obligation_MembershipFee.GetCycleAccrual re-reads the live
    /// MemberType.membershipFee every time it actually resolves.
    ///
    /// Two call sites: Character_Factions.UpdateFactionPriorityList calls this per-character (on every
    /// faction managing them, not just home) the moment their faction membership actually changes
    /// (added/removed from a faction, home/temp-home reassigned, work-faction source overridden) - the
    /// "alternate call site" a brand-new campaign or freshly-joined member needs, same reasoning as
    /// Obligation_Rent's eager creation at floor-add time, so the obligation (and so the UI) doesn't have
    /// to wait for the next daily ResolveDuePass tick. EnsureMembershipFeeObligations (below) is the daily
    /// full-roster sweep that still runs as a catch-all.
    /// </summary>
    public void EnsureMembershipFeeObligationFor(Character_Trainable c)
    {
        if (!owner.isPlayerFaction || c == null) return;

        foreach (var workFaction in c.FactionManager.WorkFactions)
        {
            if (workFaction == null || workFaction == owner) continue;
            var status = workFaction.GetMemberType(c);
            if (status == null || status.membershipFee == null) continue;
            if (c.FactionManager.GetWorkFactionSourceOrDefault(workFaction.ID) != owner) continue;

            GetOrCreateMembershipFeeObligation(workFaction, status.membershipFee.cadence);
        }
    }

    /// <summary>
    /// Daily full-roster sweep, calling EnsureMembershipFeeObligationFor for each of owner's own managed
    /// characters - a catch-all alongside the eager per-character call from
    /// Character_Factions.UpdateFactionPriorityList (e.g. covering a MemberType.membershipFee itself being
    /// balance-patched after the character already joined, with no faction-membership change to re-trigger
    /// the eager path). Called from ResolveDuePass's pass-0 block (before that pass's own due-check
    /// snapshot), not from Manageable.OnDayUpdate_1 - see that call site's comment for why timing matters.
    /// </summary>
    public void EnsureMembershipFeeObligations()
    {
        if (!owner.isPlayerFaction) return;
        foreach (var c in owner.ManagedChara) EnsureMembershipFeeObligationFor(c);
    }

    // ---------------------------------------------------------------------
    // Debt convenience helpers (operate over Obligations.OfType<Obligation_Debt>())
    // ---------------------------------------------------------------------

    public Obligation_Debt GetDebtTo(string lenderFactionID)
    {
        return Obligations.OfType<Obligation_Debt>().FirstOrDefault(x => x.targetFactionID == lenderFactionID);
    }

    /// <summary>Debt owed FROM owner TO lender - Manageable-typed convenience overload of GetDebtTo(string).</summary>
    public Obligation_Debt GetDebtTo(Manageable lender)
    {
        return lender == null ? null : GetDebtTo(lender.ID);
    }

    /// <summary>
    /// The loan owner has extended TO borrower - the reverse direction from GetDebtTo: a debt obligation
    /// lives on the BORROWER's own TradeManager (targeting the lender), never on the lender's, so this
    /// reaches over to borrower's TradeManager and asks its GetDebtTo(owner) instead of looking at
    /// owner's own Obligations.
    /// </summary>
    public Obligation_Debt GetLoanGiven(Manageable borrower)
    {
        return borrower != null && borrower.TradeManager != null ? borrower.TradeManager.GetDebtTo(owner) : null;
    }

    public int GetTotalDebt()
    {
        int total = 0;
        foreach (var d in Obligations.OfType<Obligation_Debt>()) total += d.owed == null ? 0 : d.owed.itemCount;
        return total;
    }

    /// <summary>
    /// Seeds a new debt to lenderFactionID using debtClassID's terms (interest/cadence/payment events -
    /// see DebtClassDef), or - if owner already owes that lender - adds principal onto the existing
    /// instance instead of creating a second one (this is the "lend more money" path). debtClassID is only
    /// applied on first creation - a top-up onto an already-existing debt leaves whatever class it
    /// originally started with untouched, since the caller adding more principal isn't necessarily the
    /// same "source" that first set up the loan's terms.
    /// </summary>
    public Obligation_Debt AddDebt(string lenderFactionID, ItemEntry principal, string debtClassID, ItemEntry paymentAmount = null)
    {
        var existing = GetDebtTo(lenderFactionID);
        if (existing != null)
        {
            if (existing.owed == null) existing.owed = new ItemEntry(principal);
            else existing.owed.itemCount += principal == null ? 0 : principal.itemCount;
            return existing;
        }

        var debt = new Obligation_Debt(lenderFactionID, principal, debtClassID, paymentAmount);
        Obligations.Add(debt);
        NotifyTargetOfObligation(debt);
        return debt;
    }

    /// <summary>
    /// Manual extra repayment (the "repay extra" dialogue action) - reduces principal directly, clamped to
    /// zero, without waiting for the debt's own cadence. Does not itself transfer the item off owner's
    /// inventory; callers that need the currency actually deducted should do so before/along with this call
    /// (kept separate since manual repayment may be driven by an Event Result that already has its own
    /// item-cost handling).
    /// </summary>
    public bool RepayDebtExtra(Obligation_Debt debt, ItemEntry amount)
    {
        if (debt == null || !Obligations.Contains(debt)) return false;
        if (debt.owed == null || amount == null) return false;
        debt.owed.itemCount = Math.Max(0, debt.owed.itemCount - amount.itemCount);
        return true;
    }

    // ---------------------------------------------------------------------
    // Deferred decline/confirm scaffolding (not called from anywhere yet)
    // ---------------------------------------------------------------------

    public bool ConfirmObligation(RecurringObligation obligation, DateTime date)
    {
        if (obligation == null || !Obligations.Contains(obligation)) return false;
        return obligation.ResolveCycle(this, owner, date, out _);
    }

    public void DeclineObligation(RecurringObligation obligation)
    {
        if (obligation == null || !Obligations.Contains(obligation)) return;
        obligation.Decline(this, owner);
    }
}
