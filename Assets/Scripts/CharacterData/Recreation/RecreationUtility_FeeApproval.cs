using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

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
///   and payer, never one per participant. A request the player answers for a session they are being asked about
///   (their invitation / a public notice - PlayerPrompt, Plan_ActivityPlayerPrompt.md) goes into that question box
///   instead ([支付并参加] / [支付但不参加] / [拒绝]). Its self is the approver: the player for a player-managed payer, else the
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
        /// <summary>Pre-commit replacement answer; no booking or attendance has been committed yet.</summary>
        public Action<bool> answered;
        /// <summary>
        /// Pre-commit only: the waiting booking would still go through (not started, its session open, the same fit
        /// check it passed before waiting - CanCommitActivityBooking). Checked when the batch is sent (FlushFees).
        /// </summary>
        public Func<bool> stillPossible;
        /// <summary>Pre-commit only: stillPossible failed - the wait ends without an answer (no approval or refusal recorded).</summary>
        public Action withdraw;
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

    /// <summary>The player's answer to their one question box (RecreationUtility_Groups.AnswerPlayerPrompt).</summary>
    enum PromptAnswer { Participate, PayOnly, Refuse }

    /// <summary>
    /// One question box for the player (Plan_ActivityPlayerPrompt.md): their invitation to / public notice of session,
    /// queued in the late pass (QueuePlayerPrompt) and sent from the update's batch (FlushFees) together with every
    /// fee request the player approves for the same session (fees - merged there, never sent on their own).
    /// </summary>
    class PlayerPrompt
    {
        public RecreationGroup session;
        public RecreationGroupMember member;
        /// <summary>Why the player can't take part (localization key - the participate option's tooltip); "" = they may.</summary>
        public string ineligibleReason = "";
        public bool answered;
        public readonly List<FeeRequest> fees = new List<FeeRequest>();
    }

    /// <summary>Queued this update step, sent by FlushFees.</summary>
    static readonly List<FeeItem> feeQueue = new List<FeeItem>();
    /// <summary>The player's prompts queued this update step, sent by FlushFees.</summary>
    static readonly List<PlayerPrompt> promptQueue = new List<PlayerPrompt>();
    /// <summary>Sessions with new members to announce this update step (AnnounceNewMembers once their fees are answered).</summary>
    static readonly List<RecreationGroup> feeTouchedSessions = new List<RecreationGroup>();
    static bool feeFlushQueued = false;
    enum ReplacementFeeState { Pending, Approved, Declined }
    class SessionReplacementFees
    {
        public readonly Dictionary<int, ReplacementFeeState> actors = new Dictionary<int, ReplacementFeeState>();
        public int pending;
    }
    class SoloReplacementFee
    {
        public ReplacementFeeState state;
    }
    static ConditionalWeakTable<RecreationGroup, SessionReplacementFees> sessionReplacementFees = new ConditionalWeakTable<RecreationGroup, SessionReplacementFees>();
    static ConditionalWeakTable<RecreationBooking, SoloReplacementFee> soloReplacementFees = new ConditionalWeakTable<RecreationBooking, SoloReplacementFee>();

    /// <summary>A loaded campaign starts with nothing queued (requests are answered before time advances - none survives a save).</summary>
    public static void ResetFeeApprovals()
    {
        feeQueue.Clear();
        promptQueue.Clear();
        feeTouchedSessions.Clear();
        feeFlushQueued = false;
        sessionReplacementFees = new ConditionalWeakTable<RecreationGroup, SessionReplacementFees>();
        soloReplacementFees = new ConditionalWeakTable<RecreationBooking, SoloReplacementFee>();
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

    static bool TryGetReplacementFeeState(Character_Trainable c, RecreationBooking b, out ReplacementFeeState state)
    {
        state = default(ReplacementFeeState);
        if (c == null || b == null) return false;
        if (b.Session != null)
            return sessionReplacementFees.TryGetValue(b.Session, out var fees) && fees.actors.TryGetValue(c.RefID, out state);
        if (!soloReplacementFees.TryGetValue(b, out var solo)) return false;
        state = solo.state;
        return true;
    }

    /// <summary>A prior pre-commit approval for this session actor, or this exact solo booking on retry.</summary>
    internal static bool IsReplacementFeeApproved(Character_Trainable c, RecreationBooking b)
    {
        return TryGetReplacementFeeState(c, b, out var state)
            && state == ReplacementFeeState.Approved;
    }

    internal static bool ReplacementFeeDeclined(Character_Trainable c, RecreationBooking b)
    {
        return TryGetReplacementFeeState(c, b, out var state)
            && state == ReplacementFeeState.Declined;
    }

    static bool ReplacementFeesPending(RecreationGroup g)
    {
        return g != null && sessionReplacementFees.TryGetValue(g, out var fees) && fees.pending > 0;
    }

    /// <summary>Discard pre-commit answers when a session is cancelled, concluded or unhooked.</summary>
    internal static void ForgetReplacementFees(RecreationGroup g)
    {
        if (g != null) sessionReplacementFees.Remove(g);
    }

    /// <summary>
    /// Call before displacing conflicts. True = waiting or already declined; false = no approval needed or already
    /// approved. Only the first call queues an answer; approved state is recorded before the callback retries booking.
    /// <br/>The waiting booking holds no time: when the batch is sent (FlushFees) it is checked again
    /// (CanCommitActivityBooking - the check the caller passed before waiting; ignoreSleep as a session member does).
    /// If it no longer fits - e.g. the character confirmed something else in its place meanwhile - the request is
    /// withdrawn: nobody is asked, the waiting state is cleared (no approval or refusal recorded, so a later plan may
    /// ask again) and withdrawn runs (the caller's reaction; null = nothing).
    /// </summary>
    internal static bool AwaitReplacementFee(Character_Factions f, RecreationBooking b, Action<bool> answered, Action withdrawn = null)
    {
        var c = f?.Owner;
        if (c == null || b == null) return false;
        if (TryGetReplacementFeeState(c, b, out var state)) return state != ReplacementFeeState.Approved;
        var activity = b.GetActivity(f);
        var payer = FeePayer(c, activity, b.Faction);
        if (payer == null) return false;
        var session = b.Session;
        int actor = c.RefID;
        SessionReplacementFees sessionFees = null;
        SoloReplacementFee soloFee = null;
        if (session != null)
        {
            sessionFees = sessionReplacementFees.GetValue(session, _ => new SessionReplacementFees());
            sessionFees.actors[actor] = ReplacementFeeState.Pending;
            sessionFees.pending++;
        }
        else
        {
            soloFee = new SoloReplacementFee() { state = ReplacementFeeState.Pending };
            soloReplacementFees.Add(b, soloFee);
        }
        EnqueueFee(new FeeItem()
        {
            chara = c, payer = payer, activity = activity, session = session,
            solo = session == null ? b : null, soloKey = b.factionID + "|" + b.sourceKey + "|" + b.AbsStart,
            answered = approved =>
            {
                var result = approved ? ReplacementFeeState.Approved : ReplacementFeeState.Declined;
                if (session != null)
                {
                    if (!sessionReplacementFees.TryGetValue(session, out var current) || !ReferenceEquals(current, sessionFees)
                        || !current.actors.TryGetValue(actor, out var pending) || pending != ReplacementFeeState.Pending) return;
                    current.actors[actor] = result;
                    current.pending--;
                }
                else
                {
                    if (!soloReplacementFees.TryGetValue(b, out var current) || !ReferenceEquals(current, soloFee)
                        || current.state != ReplacementFeeState.Pending) return;
                    current.state = result;
                }
                answered?.Invoke(approved);
            },
            stillPossible = () => b.AbsStart > NowAbs() && (session == null || session.IsOpen)
                && CanCommitActivityBooking(f, b, false, session != null, FindBookingConflicts(f, b)),
            withdraw = () =>
            {
                if (session != null)
                {
                    if (!sessionReplacementFees.TryGetValue(session, out var current) || !ReferenceEquals(current, sessionFees)
                        || !current.actors.TryGetValue(actor, out var pending) || pending != ReplacementFeeState.Pending) return;
                    current.actors.Remove(actor);
                    current.pending--;
                }
                else
                {
                    if (!soloReplacementFees.TryGetValue(b, out var current) || !ReferenceEquals(current, soloFee)
                        || current.state != ReplacementFeeState.Pending) return;
                    soloReplacementFees.Remove(b);
                }
                withdrawn?.Invoke();
            },
        });
        return true;
    }

    static void EnqueueFee(FeeItem item)
    {
        feeQueue.Add(item);
        if (item.session != null && item.answered == null) item.session.feeOutstanding++;
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
        if (c?.FactionManager != null && IsReplacementFeeApproved(c, FindSessionBooking(c.FactionManager, g))) return;
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
        if (IsReplacementFeeApproved(c, b)) return false;
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

    /// <summary>
    /// The player's invitation to / notice of g (member m - marked considered, so g's resolution waits for the answer)
    /// goes out with this update step's batch (FlushFees), merged with the fees the player approves for g. Once per
    /// session per step.
    /// </summary>
    static void QueuePlayerPrompt(RecreationGroup g, RecreationGroupMember m, string ineligibleReason)
    {
        if (g == null || m == null) return;
        m.considered = true;
        for (int i = 0; i < promptQueue.Count; i++) if (promptQueue[i].session == g) return;
        promptQueue.Add(new PlayerPrompt() { session = g, member = m, ineligibleReason = ineligibleReason ?? "" });
        ScheduleFeeFlush();
    }

    // ---------------- sending ---------------- //

    /// <summary>
    /// The update step's batch: first the attending members of every prompted session have their fees queued
    /// (QueueMemberFee - once per member), so they land in the same box; then one request per occurrence and payer -
    /// those the player approves for a prompted session go into that prompt, the rest start on their own
    /// (StartFeeRequest); then the prompts start (StartPlayerPrompt); then each touched session with no answer
    /// outstanding finishes (FinishSessionFees - the others finish on their last answer).
    /// </summary>
    static void FlushFees()
    {
        List<PlayerPrompt> prompts = null;
        if (promptQueue.Count > 0)
        {
            prompts = new List<PlayerPrompt>(promptQueue);
            promptQueue.Clear();
            var player = Player;
            // still inside this flush (feeFlushQueued is set): queued into feeQueue without scheduling another
            for (int i = 0; i < prompts.Count; i++)
            {
                var g = prompts[i].session;
                if (g == null || !g.IsOpen) continue;
                for (int k = 0; k < g.members.Count; k++)
                    if (g.members[k].IsAttending && (player == null || g.members[k].charaRef != player.RefID)) QueueMemberFee(g, g.members[k]);
            }
        }
        feeFlushQueued = false;
        var requests = new List<FeeRequest>();
        List<FeeItem> withdrawn = null;
        for (int i = 0; i < feeQueue.Count; i++)
        {
            var item = feeQueue[i];
            if (item.answered != null && item.session != null && !item.session.IsOpen)
            {
                item.answered(false);
                continue;
            }
            // a waiting booking holds no time: what the character took in its place meanwhile wins - nobody is asked
            if (item.answered != null && item.stillPossible != null && !item.stillPossible())
            {
                (withdrawn ?? (withdrawn = new List<FeeItem>())).Add(item);
                continue;
            }
            if (item.answered == null)
            {
                var f = item.chara?.FactionManager;
                if (item.session != null)
                {
                    var g = item.session;
                    if (!g.IsOpen || item.chara == null || g.FindMember(item.chara.RefID)?.IsAttending != true
                        || f == null || FindSessionBooking(f, g) == null)
                    {
                        g.feeOutstanding = Math.Max(0, g.feeOutstanding - 1);
                        if (!feeTouchedSessions.Contains(g)) feeTouchedSessions.Add(g);
                        continue;
                    }
                }
                else if (item.solo != null && !HoldsBooking(f, item.solo)) continue;
            }
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
        // the withdrawn waits end before anything is shown (a member blocked here is not listed in a box); whatever
        // their reactions queue goes out with the next batch (feeQueue was cleared - a new flush is scheduled)
        if (withdrawn != null) for (int i = 0; i < withdrawn.Count; i++) withdrawn[i].withdraw?.Invoke();

        // every request the player answers for a prompted session goes into that prompt's box
        if (prompts != null)
        {
            var player = Player;
            for (int i = 0; i < prompts.Count; i++)
                for (int r = 0; r < requests.Count; r++)
                {
                    if (requests[r].items[0].session != prompts[i].session || player == null || FeeApprover(requests[r].payer) != player) continue;
                    prompts[i].fees.Add(requests[r]);
                    requests.RemoveAt(r--);
                }
        }
        for (int i = 0; i < requests.Count; i++) StartFeeRequest(requests[i]);
        if (prompts != null) for (int i = 0; i < prompts.Count; i++) StartPlayerPrompt(prompts[i]);
        for (int i = 0; i < touched.Count; i++) if (touched[i].feeOutstanding <= 0) FinishSessionFees(touched[i]);
    }

    /// <summary>Prompt p is not shown (its session gone, its event rejected): its fee requests are sent on their own.</summary>
    static void StartPromptFeesAlone(PlayerPrompt p)
    {
        for (int i = 0; i < p.fees.Count; i++) StartFeeRequest(p.fees[i]);
        p.fees.Clear();
    }

    /// <summary>The members prompt p's fee requests pay for (WouldBeValid counts them as coming).</summary>
    static List<int> PromptPayees(PlayerPrompt p)
    {
        var refs = new List<int>();
        for (int r = 0; r < p.fees.Count; r++)
            for (int i = 0; i < p.fees[r].items.Count; i++)
            {
                var c = p.fees[r].items[i].chara;
                if (c != null && !refs.Contains(c.RefID)) refs.Add(c.RefID);
            }
        return refs;
    }

    /// <summary>
    /// Prompt p's fee text ("" when nothing is paid): the player's own entrance fee, then each attached request - who
    /// takes part, the members each payer pays for with their fee, and the total (FeeRequestText without the activity
    /// line the prompt already shows) - so every member whose booking waits for the answer is named.
    /// </summary>
    static string DescribePromptFees(PlayerPrompt p, Character_Trainable player)
    {
        var lines = new List<string>();
        var activity = ResolveActivity(p.session);
        var fee = activity?.entranceFee;
        if (HasEntranceFee(activity))
            lines.Add(LocalizeDictionary.QueryThenParse("ui_recreation_invite_ownFee").Replace("$name$", player.FullName).Replace("$fee$", FeeLabel(fee.itemID, fee.itemCount)));
        for (int i = 0; i < p.fees.Count; i++) lines.Add(FeeRequestText(p.fees[i], "ui_recreation_fee_request_inline"));
        return lines.Count == 0 ? "" : "\n" + string.Join("\n", lines);
    }

    /// <summary>
    /// Starts request's Recreation_FeeApproval on its approver (FeeApprover): forced on screen for the player; an NPC
    /// approves on its own (npcDecides - the loaded [支付] is its isDefaultAccept). The options are built just before the
    /// question shows (callback "buildOptions" - BuildFeeOptions): [支付], and for the player [支付并一同加入] - pay, then
    /// join: a session they don't attend (PlayerJoinRefusal / PlayerJoinSession), or a solo visit, which becomes a
    /// session its visitor hosts with the player (PlayerJoinSoloRefusal / PlayerJoinSolo); the event's own [拒绝]
    /// declines. No approver = approved; an event that can't start = declined for the player, approved for an NPC.
    /// </summary>
    static void StartFeeRequest(FeeRequest request)
    {
        var approver = FeeApprover(request.payer);
        if (approver == null) { AnswerFeeRequest(request, true); return; }
        bool answered = false;
        var ev = new EventInstance(approver, Event_FeeApproval, "");
        ev.displayOverride = approver == Player;
        ev.AppendStrings["feeRequestText"] = new List<string>() { FeeRequestText(request) };
        ev.AppendStrings["feeConflictText"] = new List<string>() { "" };
        ev.AppendStrings["feeJoinTotal"] = new List<string>() { "" };
        Action approve = () => { if (answered) return; answered = true; AnswerFeeRequest(request, true); };
        Action payAndJoin = () =>
        {
            if (answered) return;
            answered = true;
            var first = request.items[0];
            if (first.session != null)
            {
                AnswerFeeRequest(request, true);
                PlayerJoinSession(first.session);
                return;
            }
            // a solo visit: the approval books it with its "booked" notice held back - it becomes a session the player
            // joins, which announces itself (PlayerJoinSolo; stays solo and announces as booked if it can't be hosted)
            BeginAnnounceScope(false, true);
            try { AnswerFeeRequest(request, true); }
            finally { EndAnnounceScope(false, true); }
            PlayerJoinSolo(first.chara, first.solo);
        };
        ev.FunctionCalls["buildOptions"] = new List<Action>()
        {
            () =>
            {
                ev.StoredOptions["feeOptions"] = BuildFeeOptions(request, approver, approve, payAndJoin, out var conflictText, out var joinTotal);
                ev.AppendStrings["feeConflictText"] = new List<string>() { conflictText };
                ev.AppendStrings["feeJoinTotal"] = new List<string>() { joinTotal };
            }
        };
        ev.FunctionCalls["decline"] = new List<Action>() { () => { if (answered) return; answered = true; AnswerFeeRequest(request, false); } };
        if (!scr_UpdateHandler.current.EventHandler.StartEvent(ev, false) && !answered)
        {
            answered = true;
            AnswerFeeRequest(request, approver != Player);
        }
    }

    /// <summary>
    /// The fee box's loaded options: [支付] (isDefaultAccept - an NPC approver's pick), then - only for the player as
    /// approver - [支付并一同加入] for a session they don't attend (PlayerJoinRefusal) or a solo visit whose activity can
    /// be shared (PlayerJoinSoloRefusal - joining makes it a session; the request's first visitor hosts it), disabled with
    /// the reason when they can't join. Its tooltip (AppendStrings "feeJoinTotal" = joinTotal) is the total paid when
    /// joining: this request plus the player's own entrance fee when they pay one (FeeJoinTotalText). conflictText = the
    /// bookings joining would give up / can't give up ("" when none).
    /// </summary>
    static List<Event.EventEntry.Options> BuildFeeOptions(FeeRequest request, Character_Trainable approver, Action approve, Action payAndJoin,
        out string conflictText, out string joinTotal)
    {
        conflictText = "";
        joinTotal = "";
        var options = new List<Event.EventEntry.Options>()
        {
            new Event.EventEntry.Options()
            {
                option = LocalizeDictionary.QueryThenParse("ui_recreation_fee_pay"),
                isDefaultAccept = true,
                onSelect = ev => approve(),
            }
        };
        var player = Player;
        var first = request.items[0];
        var g = first.session;
        if (approver == null || approver != player) return options;
        string refusal;
        if (g != null)
        {
            var m = g.FindMember(player.RefID);
            if (m != null && m.IsAttending) return options;
            refusal = PlayerJoinRefusal(g, player, out conflictText, out _);
        }
        else if (first.solo != null)
        {
            refusal = PlayerJoinSoloRefusal(first.chara, first.solo, player, out conflictText, out bool applicable);
            if (!applicable) return options;
        }
        else return options;
        joinTotal = FeeJoinTotalText(request, player);
        options.Add(new Event.EventEntry.Options()
        {
            option = LocalizeDictionary.QueryThenParse("ui_recreation_fee_payJoin"),
            tooltip = "feeJoinTotal",
            onSelect = refusal != "" ? null : (Action<EventInstance>)(ev => payAndJoin()),
            disabledReasonKey = refusal,
        });
        return options;
    }

    /// <summary>
    /// The [支付并一同加入] tooltip: everything paid when the player joins - this request's fees plus the player's own
    /// entrance fee when one is charged to them (ChargeEntranceFee's rule: the activity has a fee, their payer is not
    /// the venue itself and is a player faction with a trade manager), per item ("合计：…"), with the player's share
    /// named when it is included.
    /// </summary>
    static string FeeJoinTotalText(FeeRequest request, Character_Trainable player)
    {
        var totals = new List<ItemEntry>();
        void Add(ItemEntry fee)
        {
            if (fee == null || string.IsNullOrEmpty(fee.itemID) || fee.itemCount <= 0) return;
            ItemEntry total = null;
            for (int t = 0; t < totals.Count && total == null; t++) if (totals[t].itemID == fee.itemID) total = totals[t];
            if (total == null) totals.Add(total = new ItemEntry(fee.itemID, fee.itemNameOverwrite, 0, fee.itemCountOverride));
            total.itemCount += fee.itemCount;
        }
        for (int i = 0; i < request.items.Count; i++) if (request.items[i].chara != null) Add(request.items[i].activity?.entranceFee);

        var first = request.items[0];
        var ownFee = first.activity?.entranceFee;
        string venueID = first.session?.factionID ?? first.solo?.factionID ?? "";
        var payer = player?.FactionManager?.GetWorkFactionSourceOrDefault(venueID);
        bool playerPays = ownFee != null && !string.IsNullOrEmpty(ownFee.itemID) && ownFee.itemCount > 0
            && payer != null && payer.ID != venueID && payer.isPlayerFaction && payer.TradeManager != null;
        if (playerPays) Add(ownFee);

        string separator = LocalizeDictionary.QueryThenParse("event_heldMembership_separator");
        var labels = new List<string>(totals.Count);
        for (int i = 0; i < totals.Count; i++) labels.Add(FeeLabel(totals[i].itemID, totals[i].itemCount));
        string text = LocalizeDictionary.QueryThenParse("ui_recreation_fee_joinTotal").Replace("$total$", string.Join(separator, labels));
        if (playerPays)
            text += "\n" + LocalizeDictionary.QueryThenParse("ui_recreation_fee_joinTotal_own")
                .Replace("$name$", player.FullName).Replace("$fee$", FeeLabel(ownFee.itemID, ownFee.itemCount));
        return text;
    }

    /// <summary>
    /// "activity @ venue, time" / participants: everyone taking part, then ($pending$ - PendingParticipantsText) those
    /// whose booking waits for this payment (pre-commit items) / paid by payer: each with their fee / total.
    /// templateKey: the layout (ui_recreation_fee_request; ui_recreation_fee_request_inline leaves the activity line out).
    /// </summary>
    static string FeeRequestText(FeeRequest request, string templateKey = "ui_recreation_fee_request")
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
        var pending = new List<Character_Trainable>();
        for (int i = 0; i < request.items.Count; i++)
        {
            var c = request.items[i].chara;
            if (c == null || participants.Contains(c) || pending.Contains(c)) continue;
            // a pre-commit item (answered) books only once paid: shown as pending, not as taking part
            if (request.items[i].answered != null) pending.Add(c);
            else participants.Add(c);
        }

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
        return LocalizeDictionary.QueryThenParse(templateKey)
            .Replace("$activity$", line)
            .Replace("$participants$", string.Join(separator, names))
            .Replace("$pending$", PendingParticipantsText(pending))
            .Replace("$payer$", request.payer.FactionDisplayName)
            .Replace("$payees$", string.Join("\n", payees))
            .Replace("$total$", string.Join(separator, totalLabels));
    }

    /// <summary>
    /// The $pending$ part of a participant line: those who take part once a pending fee is paid
    /// (ui_recreation_participants_pending, e.g. "（待付款：A、B）"); "" when none - the line shows confirmed names only.
    /// </summary>
    static string PendingParticipantsText(List<Character_Trainable> pending)
    {
        if (pending == null || pending.Count == 0) return "";
        var names = new List<string>(pending.Count);
        for (int i = 0; i < pending.Count; i++) if (pending[i] != null) names.Add(pending[i].FullName);
        if (names.Count == 0) return "";
        return LocalizeDictionary.QueryThenParse("ui_recreation_participants_pending")
            .Replace("$names$", string.Join(LocalizeDictionary.QueryThenParse("event_heldMembership_separator"), names));
    }

    /// <summary>The members prompt p pays for who don't attend yet - their booking waits for the player's payment.</summary>
    static List<Character_Trainable> PromptPendingParticipants(PlayerPrompt p)
    {
        var list = new List<Character_Trainable>();
        if (p == null) return list;
        for (int r = 0; r < p.fees.Count; r++)
            for (int i = 0; i < p.fees[r].items.Count; i++)
            {
                var c = p.fees[r].items[i].chara;
                if (c == null || list.Contains(c)) continue;
                var m = p.session?.FindMember(c.RefID);
                if (m != null && m.IsAttending) continue;
                list.Add(c);
            }
        return list;
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
            if (item.answered != null)
            {
                item.answered(approved);
                continue;
            }
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
