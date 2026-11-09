using System;
using System.Collections.Generic;

/// <summary>
/// Entrance-fee approval (Docs/Activities/Plan_ActivityFeeApproval.md): a participant of a paid activity takes part only
/// once the manager of their payer (the priority home - the payer of ChargeEntranceFee) approved the fee.
/// <br/>- Who is asked about: a session's attending members when it resolves (ResolveSession waits - resolveDue), a member
///   joining a session that already resolved (OnJoinedResolvedSession), a solo visit just booked (AddSolo - announced once
///   approved), an NPC walking into a running activity (Job_Activity.HoldWalkInFee). Each member / walk-in is asked once.
///   The player pays for themselves by accepting / hosting / joining; the player's party and anyone joining a package
///   holding the player go with the player.
/// <br/>- Batched: everything queued during one update step (the hour's late passes, the day update, a minute) is sent
///   together from the update's event callbacks (FlushFees) - one Recreation_FeeApproval event per activity occurrence
///   and payer, never one per participant. Its self is the approver: the player for a player-managed payer, else the
///   payer's first manager (npcDecides: approve). Events are answered before time advances.
/// <br/>- Declined: everyone of the request drops out - a member Blocked ("feeDeclined") with their booking cancelled
///   (FeeDeclined), a solo visit removed quietly (never announced), a walk-in never admitted to that activity again.
/// <br/>- Booking notices of members joining a resolved session (and a posted offer's members at its resolution - it
///   always takes place, so it announces nothing else) wait for the same answers and go out together per session
///   (AnnounceNewMembers).
/// </summary>
public static partial class RecreationUtility
{
    /// <summary>The approval event (Data/Events/Recreation/recreation_fee_approval.json), started on the approver.</summary>
    public const string Event_FeeApproval = "Recreation_FeeApproval";

    /// <summary>One participant whose fee is put to their payer - of a session, a solo visit or a walk-in (exactly one set).</summary>
    class FeeItem
    {
        public Character_Trainable chara;
        public Manageable payer;
        public RecreationActivity activity;
        public RecreationGroup session;
        public RecreationBooking solo;
        public Job_Activity walkInJob;
        public ActionPackage walkInVia;
        /// <summary>A solo visit's occurrence: venue + source + start (solo visits of one activity at one time are asked together).</summary>
        public string soloKey = "";

        public bool SameOccurrence(FeeItem other)
        {
            if (session != null) return other.session == session;
            if (walkInJob != null) return other.walkInJob == walkInJob;
            return solo != null && other.solo != null && other.soloKey == soloKey;
        }
    }

    /// <summary>One approval event: the items of one occurrence and payer.</summary>
    class FeeRequest
    {
        public Manageable payer;
        public readonly List<FeeItem> items = new List<FeeItem>();
    }

    /// <summary>Queued this update step, sent by FlushFees.</summary>
    static readonly List<FeeItem> feeQueue = new List<FeeItem>();
    /// <summary>Sessions with new members to announce this update step (AnnounceNewMembers once their fees are answered).</summary>
    static readonly List<RecreationGroup> feeTouchedSessions = new List<RecreationGroup>();
    static bool feeFlushQueued = false;

    /// <summary>A loaded campaign starts with nothing queued (requests are answered before time advances - none survives a save).</summary>
    public static void ResetFeeApprovals()
    {
        feeQueue.Clear();
        feeTouchedSessions.Clear();
        feeFlushQueued = false;
    }

    /// <summary>
    /// The faction c's entrance fee for activity at venue is put to, or null when there is nothing to ask: no fee, the
    /// player (pays for themselves), no payer, or the venue paying itself. Payer = ChargeEntranceFee's
    /// (Character_Factions.GetWorkFactionSourceOrDefault - the priority home).
    /// </summary>
    public static Manageable FeePayer(Character_Trainable c, RecreationActivity activity, Manageable venue)
    {
        var fee = activity?.entranceFee;
        if (c == null || c.RefID == 0 || venue == null || fee == null || string.IsNullOrEmpty(fee.itemID) || fee.itemCount <= 0) return null;
        var payer = c.FactionManager?.GetWorkFactionSourceOrDefault(venue.ID);
        if (payer == null || payer == venue) return null;
        return payer;
    }

    /// <summary>Who answers for payer: the player when they manage it, else its first manager; null = nobody (approved).</summary>
    static Character_Trainable FeeApprover(Manageable payer)
    {
        if (payer == null) return null;
        if (payer.isPlayerFaction) return Player;
        var refs = payer.ManagerRefs;
        if (refs == null) return null;
        for (int i = 0; i < refs.Count; i++)
        {
            var c = scr_System_CampaignManager.current.FindInstanceByID(refs[i]);
            if (c != null) return c;
        }
        return null;
    }

    // ---------------- queueing ---------------- //

    static void EnqueueFee(FeeItem item)
    {
        feeQueue.Add(item);
        if (item.session != null) item.session.feeOutstanding++;
        ScheduleFeeFlush();
    }

    static void TouchSession(RecreationGroup g)
    {
        if (g != null && !feeTouchedSessions.Contains(g)) feeTouchedSessions.Add(g);
        ScheduleFeeFlush();
    }

    /// <summary>Once per update step: FlushFees runs from the update's event callbacks, after everything of this step queued.</summary>
    static void ScheduleFeeFlush()
    {
        if (feeFlushQueued || scr_UpdateHandler.current == null) return;
        feeFlushQueued = true;
        scr_UpdateHandler.current.AddEventCallback(FlushFees);
    }

    /// <summary>m's fee for session g is put to their payer - once (feeChecked); nothing to ask = nothing queued.</summary>
    static void QueueMemberFee(RecreationGroup g, RecreationGroupMember m)
    {
        if (g == null || m == null || m.feeChecked) return;
        m.feeChecked = true;
        var c = m.Chara;
        var activity = ResolveActivity(g);
        var payer = FeePayer(c, activity, scr_System_CampaignManager.current.FindFactionByID(g.factionID));
        if (payer == null) return;
        EnqueueFee(new FeeItem() { chara = c, payer = payer, activity = activity, session = g });
    }

    /// <summary>ResolveSession: every attending member's fee is put to their payer; true = answers outstanding (the resolution waits - resolveDue).</summary>
    static bool QueueSessionFees(RecreationGroup g)
    {
        for (int i = 0; i < g.members.Count; i++)
            if (g.members[i].IsAttending) QueueMemberFee(g, g.members[i]);
        return g.feeOutstanding > 0;
    }

    /// <summary>
    /// m joined g after it resolved (TryConfirmInner / TryJoinPlannedBooking): their fee is put to their payer and they
    /// are announced with this update's other newcomers of g once answered (AnnounceNewMembers).
    /// </summary>
    static void OnJoinedResolvedSession(RecreationGroup g, RecreationGroupMember m)
    {
        if (g == null || m == null) return;
        QueueMemberFee(g, m);
        if (!g.pendingAnnounce.Contains(m.charaRef)) g.pendingAnnounce.Add(m.charaRef);
        TouchSession(g);
    }

    /// <summary>
    /// AddSolo: a solo visit just booked. False = nothing to ask (the caller announces it now); true = its fee is put to
    /// its payer - announced once approved, removed quietly when declined.
    /// </summary>
    static bool QueueSoloFee(Character_Factions f, RecreationBooking b)
    {
        var c = f?.Owner;
        if (c == null || b == null) return false;
        var activity = b.GetActivity(f);
        var payer = FeePayer(c, activity, b.Faction);
        if (payer == null) return false;
        EnqueueFee(new FeeItem() { chara = c, payer = payer, activity = activity, solo = b, soloKey = b.factionID + "|" + b.sourceKey + "|" + b.AbsStart });
        return true;
    }

    /// <summary>Job_Activity.HoldWalkInFee: c walking into job through via - their fee put to payer (answered: Job_Activity.OnWalkInFeeAnswered).</summary>
    public static void QueueWalkInFee(Job_Activity job, Character_Trainable c, ActionPackage via, RecreationActivity activity, Manageable payer)
    {
        if (job == null || c == null || payer == null) return;
        EnqueueFee(new FeeItem() { chara = c, payer = payer, activity = activity, walkInJob = job, walkInVia = via });
    }

    // ---------------- sending ---------------- //

    /// <summary>
    /// The update step's batch: one request per occurrence and payer (StartFeeRequest), then each touched session with
    /// no answer outstanding finishes (FinishSessionFees - the others finish on their last answer).
    /// </summary>
    static void FlushFees()
    {
        feeFlushQueued = false;
        var requests = new List<FeeRequest>();
        for (int i = 0; i < feeQueue.Count; i++)
        {
            var item = feeQueue[i];
            FeeRequest request = null;
            for (int r = 0; r < requests.Count && request == null; r++)
                if (requests[r].payer == item.payer && requests[r].items[0].SameOccurrence(item)) request = requests[r];
            if (request == null)
            {
                request = new FeeRequest() { payer = item.payer };
                requests.Add(request);
            }
            request.items.Add(item);
        }
        feeQueue.Clear();
        var touched = new List<RecreationGroup>(feeTouchedSessions);
        feeTouchedSessions.Clear();

        for (int i = 0; i < requests.Count; i++) StartFeeRequest(requests[i]);
        for (int i = 0; i < touched.Count; i++) if (touched[i].feeOutstanding <= 0) FinishSessionFees(touched[i]);
    }

    /// <summary>
    /// Starts request's Recreation_FeeApproval on its approver (FeeApprover): forced on screen for the player; an NPC
    /// approves on its own (npcDecides). No approver = approved; an event that can't start = declined for the player,
    /// approved for an NPC.
    /// </summary>
    static void StartFeeRequest(FeeRequest request)
    {
        var approver = FeeApprover(request.payer);
        if (approver == null) { AnswerFeeRequest(request, true); return; }
        bool answered = false;
        var ev = new EventInstance(approver, Event_FeeApproval, "");
        ev.displayOverride = approver == Player;
        ev.AppendStrings["feeRequestText"] = new List<string>() { FeeRequestText(request) };
        ev.FunctionCalls["approve"] = new List<Action>() { () => { if (answered) return; answered = true; AnswerFeeRequest(request, true); } };
        ev.FunctionCalls["decline"] = new List<Action>() { () => { if (answered) return; answered = true; AnswerFeeRequest(request, false); } };
        if (!scr_UpdateHandler.current.EventHandler.StartEvent(ev, false) && !answered)
        {
            answered = true;
            AnswerFeeRequest(request, approver != Player);
        }
    }

    /// <summary>"activity @ venue, time" / participants: everyone taking part / paid by payer: each with their fee / total.</summary>
    static string FeeRequestText(FeeRequest request)
    {
        var first = request.items[0];
        string line;
        List<Character_Trainable> participants;
        if (first.session != null)
        {
            line = DescribeSessionLine(first.session);
            participants = first.session.AttendingCharas();
        }
        else if (first.walkInJob != null)
        {
            line = first.walkInJob.FeeActivityLine();
            participants = first.walkInJob.FeeParticipants();
        }
        else
        {
            line = DescribeBooking(first.solo);
            participants = new List<Character_Trainable>();
        }
        for (int i = 0; i < request.items.Count; i++)
            if (request.items[i].chara != null && !participants.Contains(request.items[i].chara)) participants.Add(request.items[i].chara);

        var names = new List<string>(participants.Count);
        for (int i = 0; i < participants.Count; i++) if (participants[i] != null) names.Add(participants[i].FullName);

        var payees = new List<string>(request.items.Count);
        var totals = new List<ItemEntry>();
        for (int i = 0; i < request.items.Count; i++)
        {
            var item = request.items[i];
            var fee = item.activity?.entranceFee;
            if (item.chara == null || fee == null) continue;
            payees.Add(LocalizeDictionary.QueryThenParse("ui_recreation_fee_payee").Replace("$name$", item.chara.FullName).Replace("$fee$", FeeLabel(fee.itemID, fee.itemCount)));
            ItemEntry total = null;
            for (int t = 0; t < totals.Count && total == null; t++) if (totals[t].itemID == fee.itemID) total = totals[t];
            if (total == null) totals.Add(total = new ItemEntry(fee.itemID, fee.itemNameOverwrite, 0, fee.itemCountOverride));
            total.itemCount += fee.itemCount;
        }
        var totalLabels = new List<string>(totals.Count);
        for (int i = 0; i < totals.Count; i++) totalLabels.Add(FeeLabel(totals[i].itemID, totals[i].itemCount));

        string separator = LocalizeDictionary.QueryThenParse("event_heldMembership_separator");
        return LocalizeDictionary.QueryThenParse("ui_recreation_fee_request")
            .Replace("$activity$", line)
            .Replace("$participants$", string.Join(separator, names))
            .Replace("$payer$", request.payer.FactionDisplayName)
            .Replace("$payees$", string.Join("\n", payees))
            .Replace("$total$", string.Join(separator, totalLabels));
    }

    /// <summary>"item xN".</summary>
    static string FeeLabel(string itemID, int count)
    {
        var name = Masterlist_Items.Instance?.Index.GetByID(itemID)?.DisplayName ?? itemID;
        return $"{name} x{count}";
    }

    // ---------------- answers ---------------- //

    /// <summary>The approver answered request (or nobody had to): every item approved / declined, then each of its sessions finishes once nothing is outstanding.</summary>
    static void AnswerFeeRequest(FeeRequest request, bool approved)
    {
        var sessions = new List<RecreationGroup>();
        for (int i = 0; i < request.items.Count; i++)
        {
            var item = request.items[i];
            if (approved) ApproveFeeItem(item);
            else DeclineFeeItem(item);
            if (item.session != null)
            {
                item.session.feeOutstanding = Math.Max(0, item.session.feeOutstanding - 1);
                if (!sessions.Contains(item.session)) sessions.Add(item.session);
            }
        }
        for (int i = 0; i < sessions.Count; i++) if (sessions[i].feeOutstanding <= 0) FinishSessionFees(sessions[i]);
    }

    /// <summary>Approved: a session member stays (announced by the session), a held solo visit announces itself, a walk-in joins (Job_Activity.OnWalkInFeeAnswered).</summary>
    static void ApproveFeeItem(FeeItem item)
    {
        if (item.solo != null)
        {
            var f = item.chara?.FactionManager;
            if (HoldsBooking(f, item.solo)) AnnounceSolo(item.solo, ActivityNoticeKind.Confirmed);
        }
        else if (item.walkInJob != null) item.walkInJob.OnWalkInFeeAnswered(item.chara, item.walkInVia, true);
    }

    /// <summary>
    /// Declined: a session member drops out - Blocked ("feeDeclined"), their booking cancelled (FeeDeclined), no notice of
    /// them joining; a solo visit is removed quietly (it was never announced); a walk-in is never admitted again.
    /// </summary>
    static void DeclineFeeItem(FeeItem item)
    {
        var c = item.chara;
        var f = c?.FactionManager;
        if (item.session != null)
        {
            var g = item.session;
            g.pendingAnnounce.Remove(c != null ? c.RefID : -1);
            var m = c == null ? null : g.FindMember(c.RefID);
            if (m == null || m.state != RecreationMemberState.Attending) return;
            // the answer first, so the booking's cancellation does not answer again (OnSessionBookingCancelled)
            m.state = RecreationMemberState.Blocked;
            m.reason = "feeDeclined";
            m.failedCheck = "";
            var b = f == null ? null : FindSessionBooking(f, g);
            if (b != null) f.CancelBooking(b, true, CancelReason.FeeDeclined);
            RefreshValidity(g);
            NotifyActivityChanged(g);
        }
        else if (item.solo != null)
        {
            if (!HoldsBooking(f, item.solo)) return;
            BeginAnnounceScope(false, true);
            try { f.CancelBooking(item.solo, true, CancelReason.FeeDeclined); }
            finally { EndAnnounceScope(false, true); }
        }
        else if (item.walkInJob != null) item.walkInJob.OnWalkInFeeAnswered(c, item.walkInVia, false);
    }

    /// <summary>f still holds booking b (not cancelled / moved meanwhile).</summary>
    static bool HoldsBooking(Character_Factions f, RecreationBooking b)
    {
        if (f == null || b == null) return false;
        var bookings = f.RecreationBookings;
        for (int i = 0; i < bookings.Count; i++) if (bookings[i] == b) return true;
        return false;
    }

    /// <summary>g's answers are all in: a resolution waiting for them goes on (ResolveSession), and its new members are announced (AnnounceNewMembers).</summary>
    static void FinishSessionFees(RecreationGroup g)
    {
        if (g == null || g.feeOutstanding > 0) return;
        if (g.resolveDue) OnPlayerAnswered(g);
        AnnounceNewMembers(g);
    }

    /// <summary>
    /// The members g gained since its last notice (pendingAnnounce) who still attend, in one "booked" notice of g
    /// (AnnounceSession - shown for the player's household at midnight) - once their fees are answered.
    /// </summary>
    static void AnnounceNewMembers(RecreationGroup g)
    {
        if (g == null || g.pendingAnnounce.Count == 0 || g.feeOutstanding > 0 || !g.resolved) return;
        var joined = new List<Character_Trainable>(g.pendingAnnounce.Count);
        for (int i = 0; i < g.pendingAnnounce.Count; i++)
        {
            var m = g.FindMember(g.pendingAnnounce[i]);
            var c = m != null && m.IsAttending ? m.Chara : null;
            if (c != null) joined.Add(c);
        }
        g.pendingAnnounce.Clear();
        if (!g.IsOpen || joined.Count == 0) return;
        AnnounceSession(g, ActivityNoticeKind.Confirmed, CancelReason.None, joined);
    }

    /// <summary>"activity @ venue, when HH:00-HH:00" of session g (the fee request / walk-in line).</summary>
    public static string DescribeSession(RecreationGroup g) { return DescribeSessionLine(g); }
}
