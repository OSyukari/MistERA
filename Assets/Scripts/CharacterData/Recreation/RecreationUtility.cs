using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Recreation booking logic. Stateless: the bookings, attendance and plan progress live on Character_Factions, which
/// calls in through its two entry points - OnDayUpdate_Recreation (DailyPlan, day update stage 3) and
/// OnHourUpdate_Recreation (HourlyCheck, after the hourly UpdateSchedule).
/// <br/>DailyPlan books a character's flexible visits for tomorrow - the activities of their recreation memberships
/// (MemberType.recreation - RecreationSpec.activities, one activity per visit) and flexible offers (RecreationOfferDef,
/// Flexible) - into the free time around work and sleep, as RecreationBookings. A visit whose spec has a visibility is
/// hosted as a shared session instead; fixed offers are sessions posted by the board. Sessions are ranked and confirmed
/// in each character's second pass of the hour (LateHourConfirm - RecreationUtility_Groups.cs).
/// The first run for a character also fills the rest of today.
/// <br/>Shape of a day's plan: one chain of flexible visits (2-4h each, MaxDailyHours in total with the day's other
/// bookings) placed as near as fits to an anchor - after the day's last shift (work ->
/// recreation -> sleep) most often, before a shift otherwise - or a random afternoon/evening start on a day off. Visits
/// at the same venue run back to back; anything at another place (another venue, another faction's shift) is kept
/// TravelBufferHours away for the trip. The chain may eat into the start or end of a night (predicted with
/// Character_Factions.PredictSleep - bookings take those hours, the night measures shorter), but never splits it and
/// always leaves Character_Factions.MinSleepHoursWithBookings.
/// <br/>Once planned, a booking stays as it is unless something breaks it (HourlyCheck) - then TryReschedule re-packs
/// that day's movable bookings around the new situation, nearest the original time.
/// <br/>Not planned for: the player character, fallback workers, temporary/dormant actors, characters without a home,
/// held by a party lock, or whose priority home forbids work.
/// <br/>Shared (multi-character) activities - sessions: posting, ranking, confirming, extending invitations - are in RecreationUtility_Groups.cs.
/// </summary>
public static partial class RecreationUtility
{
    public const int MaxDailyHours = 8;
    public const int MinSegmentHours = 2;
    public const int MaxSegmentHours = 4;
    /// <summary>Chance a character books nothing at all for a day.</summary>
    public const double SkipDayChance = 0;
    /// <summary>Chance, after each visit placed, of trying to add another to the chain.</summary>
    public const double ExtendChainChance = 0.5;
    /// <summary>How far back attendance is remembered (Character_Factions.RecordAttendance) - covers the weekly target and cooldowns.</summary>
    public const int AttendanceMemoryDays = 14;
    /// <summary>World-map travel minutes at which a venue's weight halves (1 / (1 + minutes / this)).</summary>
    const float TravelHalfWeightMinutes = 30f;
    /// <summary>
    /// Minutes of sleep left below which a character asleep at the start of a booked hour counts as waking into it rather
    /// than sleeping through it - a night cut short by a booking ends exactly on the hour, and the sleeping status's own
    /// tick may run just after the hourly check.
    /// </summary>
    const int BookingSleepGraceMinutes = 10;

    /// <summary>
    /// Free hours kept between a visit and anything next to it at another place - a work/home schedule hour of another
    /// faction, or a booking at another venue - for the trip (mirrors sleep's buffer hour, Character_Factions.PlaceSleep).
    /// Visits at the same venue / at the schedule's own faction may touch.
    /// </summary>
    public const int TravelBufferHours = 1;

    /// <summary>Absolute-hour interval [start, end); venue = the booking's faction ID where it is one (null otherwise - counts as elsewhere).</summary>
    public struct Span
    {
        public int start, end;
        public string venue;
        public Span(int start, int end, string venue = null) { this.start = start; this.end = end; this.venue = venue; }
        public int Length { get { return end - start; } }
        public bool Contains(int t) { return t >= start && t < end; }
    }

    /// <summary>
    /// The night [start, end) minus the hours covered by bookings is one unbroken run of at least floor hours - a
    /// booking may eat the start or end of a night, never split it or leave less than the floor.
    /// </summary>
    public static bool NightKeepsSleep(int start, int end, IEnumerable<Span> bookings, int floor)
    {
        var list = bookings as IList<Span> ?? bookings.ToList();
        int first = -1, last = -1, count = 0;
        for (int t = start; t < end; t++)
        {
            bool covered = false;
            foreach (var b in list) if (b.Contains(t)) { covered = true; break; }
            if (covered) continue;
            if (first < 0) first = t;
            last = t;
            count++;
        }
        return count >= floor && last - first + 1 == count;
    }

    static int NowAbs()
    {
        return RecreationBooking.AbsoluteHour(scr_System_Time.current.getAbsoluteDay(), scr_System_Time.current.getCurrentTime().Hour);
    }

    // ---------------- change notices ---------------- //

    /// <summary>Data event started on a character whose bookings changed - its line shows $bookingText$.</summary>
    public const string Event_BookingChanged = "Recreation_BookingChanged";

    /// <summary>Why a booking was cancelled - picks the reason text (ui_recreation_cancel_* keys).</summary>
    public enum CancelReason { None, Slept, WorkForbidden, LostAccess, NoTime, Displaced, MembershipChanged, ByEvent, ByRequest, GroupCancelled, GroupFailed, NoShow }

    /// <summary>
    /// One pass's booking changes of one character, announced together (per pass, not per booking) by EndNotice: one
    /// Event_BookingChanged on the character + one misc entry in each home faction's daily report.
    /// </summary>
    class Notice
    {
        public List<RecreationBooking> booked = new List<RecreationBooking>();
        public List<KeyValuePair<RecreationBooking, RecreationBooking>> moved = new List<KeyValuePair<RecreationBooking, RecreationBooking>>();
        public List<KeyValuePair<RecreationBooking, CancelReason>> cancelled = new List<KeyValuePair<RecreationBooking, CancelReason>>();
        public int depth = 0;
        public bool IsEmpty { get { return booked.Count == 0 && moved.Count == 0 && cancelled.Count == 0; } }
    }

    /// <summary>
    /// Open notice per character, collecting the changes of the call in progress (BeginNotice ... EndNotice; nested calls
    /// join the outer one). A change recorded with none open is announced on its own right away (no forced display).
    /// </summary>
    static readonly Dictionary<Character_Factions, Notice> openNotices = new Dictionary<Character_Factions, Notice>();

    static void BeginNotice(Character_Factions f)
    {
        if (f == null) return;
        if (!openNotices.TryGetValue(f, out var n)) openNotices[f] = n = new Notice();
        n.depth++;
    }

    /// <summary>Closes f's notice; the outermost close announces it (displayOverride / quiet: see AnnounceNotice).</summary>
    static void EndNotice(Character_Factions f, bool displayOverride, bool quiet = false)
    {
        if (f == null || !openNotices.TryGetValue(f, out var n)) return;
        if (--n.depth > 0) return;
        openNotices.Remove(f);
        AnnounceNotice(f, n, displayOverride, quiet);
    }

    static void Record(Character_Factions f, Action<Notice> add)
    {
        if (f == null) return;
        if (openNotices.TryGetValue(f, out var n)) { add(n); return; }
        var single = new Notice();
        add(single);
        AnnounceNotice(f, single, false);
    }

    static void RecordBooked(Character_Factions f, RecreationBooking b) { if (b != null) Record(f, n => n.booked.Add(b)); }
    static void RecordMoved(Character_Factions f, RecreationBooking from, RecreationBooking to) { Record(f, n => n.moved.Add(new KeyValuePair<RecreationBooking, RecreationBooking>(from, to))); }

    /// <summary>A booking was cancelled before it ended (Character_Factions.NotifyBookingCancelled) - into the open notice, or announced on its own.</summary>
    public static void RecordCancelled(Character_Factions f, RecreationBooking b, CancelReason reason)
    {
        if (b == null) return;
        // a booking that may not be cancelled on request should not be cancelled by these either - content to fix
        if (b.forbidCancel && (reason == CancelReason.WorkForbidden || reason == CancelReason.ByEvent || reason == CancelReason.Displaced))
            Debug.LogError($"forbidCancel recreation booking of {f?.Owner?.FullName} cancelled ({reason}): {b.sourceKey} @ {b.factionID} day {b.day} {b.startHour:00}:00+{b.hours}h");
        Record(f, n => n.cancelled.Add(new KeyValuePair<RecreationBooking, CancelReason>(b, reason)));
    }

    /// <summary>
    /// Starts Event_BookingChanged on the character with the change list as $bookingText$ (displayOverride: forced on
    /// screen; otherwise shown only when the player can see the character - EventInstance.isVisible), and adds the
    /// same list as a misc entry to each of their home factions' daily report. quiet: daily report only, no event (the
    /// caller shows the change itself, e.g. the cancel-booking dialogue).
    /// </summary>
    static void AnnounceNotice(Character_Factions f, Notice n, bool displayOverride, bool quiet = false)
    {
        var c = f?.Owner;
        if (c == null || n == null || n.IsEmpty) return;

        var lines = new List<string>();
        void Section(string headerKey, IEnumerable<string> entries)
        {
            var list = entries.ToList();
            if (list.Count == 0) return;
            lines.Add(LocalizeDictionary.QueryThenParse(headerKey));
            lines.AddRange(list);
        }
        // each section in time order (bookings are recorded in planning order: offers first, then the chain)
        Section("ui_recreation_notice_booked", n.booked.OrderBy(b => b.AbsStart).Select(b => DescribeBooking(b, c)));
        Section("ui_recreation_notice_moved", n.moved.OrderBy(kv => kv.Value.AbsStart).Select(kv => LocalizeDictionary.QueryThenParse("ui_recreation_notice_movedLine")
            .Replace("$from$", DescribeBooking(kv.Key)).Replace("$to$", DescribeTime(kv.Value))));
        Section("ui_recreation_notice_cancelled", n.cancelled.OrderBy(kv => kv.Key.AbsStart).Select(kv => DescribeBooking(kv.Key, c) + DescribeReason(kv.Value)));

        string titleKey = n.moved.Count == 0 && n.cancelled.Count == 0 ? "ui_recreation_notice_title_booked" : "ui_recreation_notice_title_changed";
        string title = LocalizeDictionary.QueryThenParse(titleKey).Replace("$name$", c.FullName);
        foreach (var home in f.HomeFactions) home?.DailyReport.AddMiscRecord(title, lines);
        if (quiet) return;

        var ev = new EventInstance(c, Event_BookingChanged, "");
        ev.AppendStrings["bookingText"] = new List<string>() { string.Join("\n", lines) };
        ev.displayOverride = displayOverride;
        scr_UpdateHandler.current.EventHandler.StartEvent(ev, false);
    }

    /// <summary>"activity @ venue, when HH:00-HH:00" - for a session booking with owner given, followed by who else is coming (DescribeCompany).</summary>
    public static string DescribeBooking(RecreationBooking b, Character_Trainable owner = null)
    {
        string activity = b.workModule != null && !string.IsNullOrEmpty(b.workModule.jobPostID) ? LocalizeDictionary.QueryThenParse(b.workModule.jobPostID) : "";
        string venue = b.Faction != null ? b.Faction.FactionDisplayName : b.factionID;
        string line = LocalizeDictionary.QueryThenParse("ui_recreation_notice_line")
            .Replace("$activity$", activity).Replace("$venue$", venue).Replace("$time$", DescribeTime(b));
        return owner != null ? line + DescribeCompany(b.Session, owner.RefID) : line;
    }

    /// <summary>"今天 19:00-21:00" / "明天 23:00-次日01:00" / "M月D日 ..." relative to today.</summary>
    static string DescribeTime(RecreationBooking b)
    {
        return DescribeTime(b.day, b.startHour, b.AbsEnd);
    }

    /// <summary>DescribeTime for a visit starting at startHour of absolute day `day`, ending at absolute hour absEnd - bookings and sessions alike (I_ActivityBooking.Tooltip).</summary>
    public static string DescribeTime(int day, int startHour, int absEnd)
    {
        int today = scr_System_Time.current.getAbsoluteDay();
        string when;
        switch (day - today)
        {
            case -1: when = LocalizeDictionary.QueryThenParse("ui_recreation_day_yesterday"); break;
            case 0: when = LocalizeDictionary.QueryThenParse("ui_recreation_day_today"); break;
            case 1: when = LocalizeDictionary.QueryThenParse("ui_recreation_day_tomorrow"); break;
            case 2: when = LocalizeDictionary.QueryThenParse("ui_recreation_day_dayAfter"); break;
            default:
                var date = scr_System_Time.current.getStartTime().Date.AddDays(day);
                when = LocalizeDictionary.QueryThenParse("ui_recreation_day_date").Replace("$month$", date.Month.ToString()).Replace("$day$", date.Day.ToString());
                break;
        }
        int endHour = absEnd % 24;
        string end = $"{endHour:00}:00";
        if (absEnd / 24 != day) end = LocalizeDictionary.QueryThenParse("ui_recreation_day_nextDay") + end;
        return LocalizeDictionary.QueryThenParse("ui_recreation_notice_time")
            .Replace("$when$", when).Replace("$start$", $"{startHour:00}:00").Replace("$end$", end);
    }

    static string DescribeReason(CancelReason reason)
    {
        if (reason == CancelReason.None) return "";
        return LocalizeDictionary.QueryThenParse("ui_recreation_cancel_" + reason);
    }

    // ---------------- daily ---------------- //

    /// <summary>
    /// Character_Factions.OnDayUpdate_Recreation (day update stage 3) - step 1: the board's calendar sessions are posted
    /// (fixed offers, each its daysInAdvance ahead - whichever character plans first), f's session bookings synced, then
    /// f's flexible visits planned - booked solo or posted as sessions f hosts (AddPlannedBookings): a day not planned
    /// yet (tomorrow; today too on the first run) with every activity its daysInAdvance allows, and today - already
    /// planned last night - once more with the same-day-only ones (daysInAdvance 0). Sessions are ranked / confirmed in
    /// the hour's second pass, after the day update (LateHourConfirm).
    /// </summary>
    public static void DailyPlan(Character_Factions f)
    {
        if (f == null || scr_System_CampaignManager.current == null || scr_System_Time.current == null) return;
        // the night's new reservations are announced together, on screen for the player's own household
        BeginNotice(f);
        try
        {
            f.ClearInviteMemory();
            int today = scr_System_Time.current.getAbsoluteDay();
            scr_System_CampaignManager.current.RecreationBoard.PostCalendar();
            bool added = SyncSessionBookings(f);
            if (ShouldPlan(f))
            {
                for (int day = today; day <= today + 1; day++)
                {
                    if (day > f.LastRecreationPlanDay) added |= PlanDay(f, day, int.MaxValue);
                    else if (day == today) added |= PlanDay(f, day, 0);
                }
                f.LastRecreationPlanDay = Math.Max(f.LastRecreationPlanDay, today + 1);
            }
            if (added) f.RefreshSchedule();
        }
        finally
        {
            EndNotice(f, FactionUtility.IsPlayerHousehold(f.Owner));
        }
    }

    /// <summary>
    /// Character_Factions.OnHourLate_Recreation (second pass of the hour - scr_System_Time.Observer_globalTime_HoursLate):
    /// steps 2-3 (SessionPass), once every character has had its hourly tick and, at midnight, its day update - so a
    /// session posted in either (DailyPlan, RefillSlot, an event) is ranked and confirmed by everyone it reaches within
    /// the hour it was posted (a confirmed member booking is in effect at once - RefreshValidity). NPCs that plan only.
    /// At midnight it is announced like the night's reservations (on screen for the player's own household).
    /// </summary>
    public static void LateHourConfirm(Character_Factions f)
    {
        if (f == null || scr_System_CampaignManager.current == null || scr_System_Time.current == null) return;
        if (!ShouldPlan(f)) return;
        bool midnight = scr_System_Time.current.getCurrentTime().Hour == 0;
        BeginNotice(f);
        try { SessionPass(f); }
        finally { EndNotice(f, midnight && FactionUtility.IsPlayerHousehold(f.Owner)); }
    }

    static bool ShouldPlan(Character_Factions f)
    {
        var c = f?.Owner;
        if (c == null || c.isTemporaryActor || c.IsDormant) return false;
        if (c == scr_System_CampaignManager.current.Player) return false;
        if (f.Faction_Home is Manageable_WorkerPool || f.HomeFactions.Count == 0 || f.isPartyLocked) return false;
        return f.HomeFactions[0].AllowWorkFaction(c);
    }

    // ---------------- hourly ---------------- //

    /// <summary>
    /// Character_Factions.OnHourUpdate_Recreation (hourly tick, after UpdateSchedule) - every booking of f not yet ended:
    /// <br/>- under way: sleeping through the hour, or needing to sleep (forced sleep - scheduled sleep never applies
    ///   during a booked hour, the booking takes it) cancels it for good;
    /// <br/>- cancelled: the priority home now forbids work, or a planned visit's access is gone (LostAccess);
    /// <br/>- re-planned on its day (TryReschedule, cancelled if nothing fits): a booking not yet started whose hours a
    ///   work/home schedule now claims, or one eating the planned night (Character_Factions.TryGetPlannedSleep) so that
    ///   less than MinSleepHoursWithBookings remain or the rest is split in two - the unlocked, not started booking
    ///   eating most of it goes first, until the night fits;
    /// <br/>- otherwise, under way with the character at the booked faction: attendance recorded (RecordAttendance) and,
    ///   for a session booking, the member marked arrived.
    /// <br/>Then the visit under way gets its Job_Activity if it has none yet (EnsureActivityJobs).
    /// <br/>First, every session f holds checks itself (UpdateSession - once per hour, whoever asks first) and f's session
    /// bookings follow (SyncSessionBookings). Re-ranking / confirming (SessionPass) is not done here but in the hour's
    /// second pass (LateHourConfirm), once every character has ticked.
    /// </summary>
    public static void HourlyCheck(Character_Factions f)
    {
        var c = f?.Owner;
        if (c == null) return;
        // daytime changes: announced without forced display (shown only when the player can see the character)
        BeginNotice(f);
        try { HourlyCheckInner(f, c); }
        finally { EndNotice(f, false); }
    }

    static void HourlyCheckInner(Character_Factions f, Character_Trainable c)
    {
        // the hour that just ended, before anything below changes the bookings
        BillVisitHour(f, c);
        // ended sessions leave the registry (and their members' inboxes / rankings) - once an hour, whoever comes first
        Sessions.PruneHourly();
        // sessions first: each checks itself (lazily, once an hour), then f's bookings of them follow (RecreationUtility_Groups)
        foreach (var g in TouchedSessions(f)) UpdateSession(g);
        if (SyncSessionBookings(f)) f.RefreshSchedule();
        CheckBookings(f, c);
        EnsureActivityJobs(f, c);
        // steps 2-3 (re-rank / confirm) run in the hour's second pass, once everyone has ticked - LateHourConfirm
    }

    /// <summary>
    /// HourlyCheck, after the bookings were checked: the visit driving f this hour (GetEffectiveBooking - the same
    /// visits the pull coordinates: own workCommands, TryFindScheduledJobNode.TryActivityJob) gets its Job_Activity if it
    /// has none yet - so the job exists from T+0 even while every NPC participant is still on the way (the player can
    /// already wait for it in the gather room). Every participant's own tick asks (the player's too); Create returns the
    /// existing job to whoever comes second. No instance (an unlinked session booking) = no job.
    /// </summary>
    static void EnsureActivityJobs(Character_Factions f, Character_Trainable c)
    {
        var b = f.GetEffectiveBooking();
        if (b == null || b.workModule?.workCommands == null || b.workModule.workCommands.Count == 0) return;
        int nowAbs = NowAbs();
        if (b.AbsStart > nowAbs || b.AbsEnd <= nowAbs) return;
        var key = ActivityKeyFor(f, b);
        if (key == null || Job_Activity.FindFor(key) != null) return;
        Job_Activity.Create(key, c);
    }

    /// <summary>HourlyCheck's per-booking part: sleeping through / forbidden / lost access / claimed by a schedule / breaking sleep - see HourlyCheck.</summary>
    static void CheckBookings(Character_Factions f, Character_Trainable c)
    {
        if (f.RecreationBookings.Count == 0) return;
        int today = scr_System_Time.current.getAbsoluteDay();
        int nowAbs = NowAbs();
        var priorityHome = f.HomeFactions.Count > 0 ? f.HomeFactions[0] : null;
        bool workForbidden = priorityHome != null && !priorityHome.AllowWorkFaction(c);

        var unfixable = new List<KeyValuePair<RecreationBooking, CancelReason>>();
        var movable = new List<RecreationBooking>();
        foreach (var b in f.RecreationBookings.ToList())
        {
            if (b == null || b.AbsEnd <= nowAbs) continue;
            bool started = b.AbsStart <= nowAbs;
            if (started && (IsSleepingThroughHour(c) || c.shouldSleep))
            {
                f.CancelBooking(b, true, CancelReason.Slept);
                continue;
            }
            if (workForbidden) unfixable.Add(new KeyValuePair<RecreationBooking, CancelReason>(b, CancelReason.WorkForbidden));
            else if (LostAccess(f, b)) unfixable.Add(new KeyValuePair<RecreationBooking, CancelReason>(b, CancelReason.LostAccess));
            else if (!started && ClaimedByOwnSchedule(f, b, today)) movable.Add(b);
            else if (started && (I_IsJobGiver)b.Faction == f.CurrentLocaleFaction)
            {
                f.RecordAttendance(b.sourceKey, b.day);
                MarkArrived(b, c);
            }
        }
        foreach (var b in BookingsBreakingSleep(f, nowAbs)) if (!unfixable.Exists(kv => kv.Key == b) && !movable.Contains(b)) movable.Add(b);

        foreach (var kv in unfixable) f.CancelBooking(kv.Key, true, kv.Value);
        // a re-plan moves the day's other bookings too - skip any already handled that way
        foreach (var b in movable) if (f.RecreationBookings.Contains(b)) TryReschedule(f, b);
    }

    static bool IsSleepingThroughHour(Character_Trainable c)
    {
        if (!c.isSleeping) return false;
        var sleeping = c.Stats.GetStatusByStringMatch(StatsUtility.Status_Sleeping);
        return sleeping == null || sleeping.duration < 0 || sleeping.duration > BookingSleepGraceMinutes;
    }

    /// <summary>
    /// A planned (Membership-source) visit whose access is gone: a membership visit (no memberTypeID) whose membership is
    /// gone, or a visit at one of the character's own factions that no longer is one or no longer offers that type
    /// (OwnFactionOffers).
    /// </summary>
    static bool LostAccess(Character_Factions f, RecreationBooking b)
    {
        if (b.source != RecreationBookingSource.Membership) return false;
        if (b.Faction == null) return true;
        if (string.IsNullOrEmpty(b.memberTypeID)) return !f.RecreationFactions.Contains(b.Faction);
        return !OwnFactionOffers(f, b.Faction, b.memberTypeID);
    }

    /// <summary>Any booked hour a work/home schedule claims (bookings never compete with those - see Character_Factions.CurrentJobScheduleFaction).</summary>
    static bool ClaimedByOwnSchedule(Character_Factions f, RecreationBooking b, int today)
    {
        if (b.overrideWork) return false;
        for (int t = b.AbsStart; t < b.AbsEnd; t++)
            if (f.CurrentJobScheduleFaction(t % 24, t / 24 - today, false) != null) return true;
        return false;
    }

    /// <summary>The bookings to drop so the planned night keeps at least MinSleepHoursWithBookings in one piece (see HourlyCheck).</summary>
    static List<RecreationBooking> BookingsBreakingSleep(Character_Factions f, int nowAbs)
    {
        var result = new List<RecreationBooking>();
        if (!f.TryGetPlannedSleep(out int start, out int end) || end <= nowAbs) return result;

        var eating = f.RecreationBookings.Where(b => b != null && b.AbsEnd > nowAbs && b.Overlaps(start, end)).ToList();
        while (!NightKeepsSleep(start, end, eating.Select(b => new Span(b.AbsStart, b.AbsEnd)), f.MinSleepHoursWithBookings))
        {
            var drop = eating.Where(b => !b.locked && b.AbsStart > nowAbs)
                .OrderByDescending(b => Math.Min(b.AbsEnd, end) - Math.Max(b.AbsStart, start)).FirstOrDefault();
            if (drop == null) break;
            result.Add(drop);
            eating.Remove(drop);
        }
        return result;
    }

    // ---------------- the visit's activity, and its money ---------------- //

    /// <summary>
    /// The activity booking b is a visit of (template data, looked up live - see RecreationBooking.GetActivity): an
    /// offer's (its sourceKey names an offer def - RecreationOfferDef.AsActivity), else activityID within the MemberType f
    /// acts under for b (ActingTypeID: b's memberTypeID, else f's membership at the venue). Null when it is neither (an
    /// event booking without a template, or templated on a MemberType's own workModule).
    /// </summary>
    public static RecreationActivity ResolveActivity(Character_Factions f, RecreationBooking b)
    {
        if (b == null) return null;
        var def = RecreationBoard.FindDef(b.sourceKey);
        if (def != null) return def.AsActivity;
        if (string.IsNullOrEmpty(b.activityID) || f == null) return null;
        return FactionUtility.TryGetMemberType(ActingTypeID(f, b), out var type) ? type?.recreation?.GetActivity(b.activityID) : null;
    }

    /// <summary>
    /// A visit's entrance fee (activity.entranceFee), once: booking given = once per booking
    /// (RecreationBooking.entranceFeeCharged - set even when there is nothing to pay); a booking-less walk-in's own
    /// guard is the caller's (Job_Activity's participant record). c's payer
    /// (Character_Factions.GetWorkFactionSourceOrDefault - the priority home) owes venue, settled at the end of the day
    /// (Obligation_ActivityFee). Only a player payer is tracked, like membership fees (NPC-to-NPC charges always succeed
    /// - TradeManager.TryChargeObligation). Charged by Job_Activity at launch / on a late join, and by the pull's
    /// plain-sandbox fallback (TryFindScheduledJobNode.TryActivityJob) once the visitor is at the venue.
    /// </summary>
    public static void ChargeEntranceFee(Character_Trainable c, Manageable venue, RecreationActivity activity, RecreationBooking booking)
    {
        if (booking != null)
        {
            if (booking.entranceFeeCharged) return;
            booking.entranceFeeCharged = true;
        }
        var f = c?.FactionManager;
        var fee = activity?.entranceFee;
        if (f == null || venue == null || fee == null || string.IsNullOrEmpty(fee.itemID) || fee.itemCount <= 0) return;
        var payer = f.GetWorkFactionSourceOrDefault(venue.ID);
        if (payer == null || payer == venue || !payer.isPlayerFaction || payer.TradeManager == null) return;
        payer.TradeManager.GetOrCreateActivityFeeObligation(venue).AccrueEntranceFee(c, activity.workModule?.jobPostID, fee);
    }

    /// <summary>
    /// HourlyCheck, first: bills the hour that just ended once (RecreationBooking.lastBilledAbs) if a visit drove it
    /// (Character_Factions.GetBookingInEffectAtAbs - its booking may be over by now) and c is still at its venue (arrived
    /// during the hour, not yet left) - the attended-hour rule a shift's wage follows (Manageable.OnHourUpdate): the
    /// visit's activity (ResolveActivity) workModule.hourlyPayout - the venue owes c's payer a wage
    /// (TradeManager.GetOrCreateSalaryObligation - the module's paymentCadence and onPaidEventID / onFailedEventID,
    /// named after the activity), like a shift hour. workModule.hourlyCost is not used for visits; the entrance fee is
    /// charged at the activity's launch (ChargeEntranceFee) - here only for a booking without commands of its own,
    /// which never gets a Job_Activity.
    /// <br/>Payer = Character_Factions.GetWorkFactionSourceOrDefault (the priority home - like a recreation membership's fee).
    /// </summary>
    static void BillVisitHour(Character_Factions f, Character_Trainable c)
    {
        int hourAbs = NowAbs() - 1;
        var b = f.GetBookingInEffectAtAbs(hourAbs);
        if (b == null || b.lastBilledAbs >= hourAbs) return;
        var venue = b.Faction;
        if (venue == null || (I_IsJobGiver)venue != f.CurrentLocaleFaction) return;
        b.lastBilledAbs = hourAbs;

        var activity = b.GetActivity(f);
        // a booking without commands of its own never gets a Job_Activity (TryFindScheduledJobNode) - its fee is charged here, on the first attended hour
        if (b.workModule?.workCommands == null || b.workModule.workCommands.Count == 0) ChargeEntranceFee(c, venue, activity, b);

        var module = activity?.workModule;
        bool hasPayout = module?.hourlyPayout != null && module.hourlyPayout.Exists(i => i != null && i.itemCount > 0);
        if (!hasPayout) return;

        var payer = f.GetWorkFactionSourceOrDefault(venue.ID);
        if (payer == null || payer == venue) return;

        if (venue.TradeManager != null)
        {
            string sourceName = !string.IsNullOrEmpty(module.jobPostID) ? LocalizeDictionary.QueryThenParse(module.jobPostID) : venue.FactionDisplayName;
            var wage = venue.TradeManager.GetOrCreateSalaryObligation(payer, module.paymentCadence, sourceName, c.RefID);
            foreach (var pay in module.hourlyPayout) if (pay != null && pay.itemCount > 0) wage.AccrueHour(pay, module.onPaidEventID, module.onFailedEventID);
        }
    }

    /// <summary>
    /// f's payer for visits at venue (GetWorkFactionSourceOrDefault) has an unpaid visit bill there (suspended
    /// Obligation_ActivityFee): no new visits of venue's activities or offers are booked until it is paid - visits
    /// already booked go ahead (the membership-fee rule, Character_Trainable.CanWorkFor).
    /// </summary>
    static bool HasUnpaidActivityFee(Character_Factions f, Manageable venue)
    {
        if (venue == null) return false;
        var payer = f.GetWorkFactionSourceOrDefault(venue.ID);
        if (payer?.TradeManager == null) return false;
        foreach (var o in payer.TradeManager.Obligations)
            if (o is Obligation_ActivityFee fee && fee.targetFactionID == venue.ID && fee.IsSuspended) return true;
        return false;
    }

    // ---------------- one day ---------------- //

    /// <summary>One candidate flexible visit for a day: an activity of a recreation membership, of a membership offered by the character's own home/work faction, or a flexible offer.</summary>
    class Candidate
    {
        public Manageable faction;
        public RecreationActivity activity;
        public string sourceKey;
        /// <summary>Acting MemberType of the booking (own-faction offer: the offered type; flexible offer: its memberTypeID); "" = the character's own type there (a membership).</summary>
        public string memberTypeID = "";
        public float weight;
        /// <summary>Membership (an activity) or Offer (a flexible offer - sourceKey = its ID).</summary>
        public RecreationBookingSource source = RecreationBookingSource.Membership;
    }

    /// <summary>
    /// What a day's placement must respect: work/home schedule hours, the character's other bookings, predicted nights,
    /// "not before next hour", and the daily cap. Built per character per planned day.
    /// </summary>
    class DayContext
    {
        public Character_Trainable c;
        public Character_Factions f;
        public int today, day, dayStart, dayEnd, nowAbs, sleepFloor;
        public List<Span> nights = new List<Span>();
        public List<Span> fixedBookings = new List<Span>();
        public int bookedToday;
        readonly Dictionary<int, Manageable> occupied = new Dictionary<int, Manageable>();
        /// <summary>Priority home (HomeFactions[0]) - whose meal hours a visit elsewhere must not take unless the venue feeds them (MealAllows).</summary>
        readonly Manageable home;
        /// <summary>Venue faction ID -> the venue (null if not found) when it can feed c (Manageable.CanOfferMealTo), else null.</summary>
        readonly Dictionary<string, Manageable> mealVenues = new Dictionary<string, Manageable>();
        /// <summary>CanHost results of this day's planning, per spec and BookingCheck.Key (mandatory specs only).</summary>
        public readonly Dictionary<(RecreationInviteSpec, string), bool> canHost = new Dictionary<(RecreationInviteSpec, string), bool>();

        /// <param name="keepBooking">Which of f's bookings count as taken (null = all) - HasTimeFor leaves out the ones an invitation would clash with.</param>
        public DayContext(Character_Factions f, int day, Func<RecreationBooking, bool> keepBooking = null)
        {
            this.f = f;
            c = f.Owner;
            today = scr_System_Time.current.getAbsoluteDay();
            this.day = day;
            dayStart = RecreationBooking.AbsoluteHour(day, 0);
            dayEnd = dayStart + 24;
            nowAbs = NowAbs();
            sleepFloor = f.MinSleepHoursWithBookings;
            home = f.HomeFactions.Count > 0 ? f.HomeFactions[0] : null;

            foreach (var b in f.RecreationBookings) if (keepBooking == null || keepBooking(b)) AddFixed(b);

            // the real night in progress / next, then predictions for the night ending this morning and the one starting
            // this evening, each anchored at the midday before it (as the hourly recompute would see it then - an
            // anchor too close to the wake target pushes it a full day out) - first found wins where they overlap
            if (f.TryGetPlannedSleep(out int ps, out int pe)) AddNight(ps, pe);
            AddPredictedNight(day - 1, 12);
            AddPredictedNight(day, 12);
        }

        /// <summary>b's hours are taken from now on (an existing booking, or an offer just picked for this day) - also counted against the daily cap on its day.</summary>
        public void AddFixed(RecreationBooking b)
        {
            if (b == null) return;
            fixedBookings.Add(new Span(b.AbsStart, b.AbsEnd, b.factionID));
            if (b.day == day) bookedToday += b.hours;
        }

        void AddPredictedNight(int anchorDay, int anchorHour)
        {
            if (f.PredictSleep(anchorDay - today, anchorHour, out int s, out int e)) AddNight(s, e);
        }

        void AddNight(int s, int e)
        {
            if (e <= dayStart - MaxSegmentHours || s >= dayEnd + MaxSegmentHours) return;
            foreach (var n in nights) if (n.start < e && s < n.end) return;
            nights.Add(new Span(s, e));
        }

        /// <summary>A work/home schedule claims absolute hour t (bookings excluded).</summary>
        public bool Occupied(int t) { return OccupiedBy(t) != null; }

        /// <summary>The faction whose work/home schedule claims absolute hour t (bookings excluded), or null.</summary>
        public Manageable OccupiedBy(int t)
        {
            if (occupied.TryGetValue(t, out var v)) return v;
            v = f.CurrentJobScheduleFaction(((t % 24) + 24) % 24, (int)Math.Floor(t / 24.0) - today, false);
            occupied[t] = v;
            return v;
        }

        /// <summary>
        /// A visit at venue (faction ID) may take hour of day `hour`: it isn't one of the home's meal hours, or the venue is
        /// the home itself, or the venue can feed c then - it has a meal spot for c (Manageable.CanOfferMealTo) and `hour`
        /// is one of its own meal hours (meals are only served there in them - TryFindMealNode, COM_TakeMeal).
        /// </summary>
        public bool MealAllows(int hour, string venue)
        {
            if (home == null || !home.isMealHourAt(hour) || venue == home.ID) return true;
            if (venue == null) return false;
            if (!mealVenues.TryGetValue(venue, out var v))
            {
                v = scr_System_CampaignManager.current.FindFactionByID(venue);
                if (v != null && !v.CanOfferMealTo(c)) v = null;
                mealVenues[venue] = v;
            }
            return v != null && v.isMealHourAt(hour);
        }

        /// <summary>
        /// Whether [s, e) at venue (faction ID) can be booked for activity (null = no hour limit, e.g. an offer) next to the
        /// chain's own spans: starts on this day and after now, free of schedules and other bookings, allowed hours, daily
        /// cap, home meal hours only where the venue feeds c (MealAllows), TravelBufferHours free to anything nearby at
        /// another place, and every night keeps its sleep.
        /// </summary>
        public bool CanPlace(int s, int e, RecreationActivity activity, List<Span> chain, string venue)
        {
            if (e <= s || s < dayStart || s >= dayEnd || s <= nowAbs) return false;
            int chainHours = 0;
            foreach (var span in chain) chainHours += span.Length;
            if (bookedToday + chainHours + (e - s) > MaxDailyHours) return false;

            for (int t = s; t < e; t++)
            {
                if (Occupied(t)) return false;
                if (activity != null && !activity.AllowsHour(((t % 24) + 24) % 24)) return false;
                if (!MealAllows(((t % 24) + 24) % 24, venue)) return false;
                foreach (var span in fixedBookings) if (span.Contains(t)) return false;
                foreach (var span in chain) if (span.Contains(t)) return false;
            }

            // travel: a schedule hour of another faction, or a booking elsewhere, may not touch the visit
            for (int k = 1; k <= TravelBufferHours; k++)
            {
                var before = OccupiedBy(s - k);
                if (before != null && before.ID != venue) return false;
                var after = OccupiedBy(e + k - 1);
                if (after != null && after.ID != venue) return false;
            }
            bool TooClose(Span span) => span.venue != venue
                && ((span.end <= s && span.end > s - TravelBufferHours) || (span.start >= e && span.start < e + TravelBufferHours));
            foreach (var span in fixedBookings) if (TooClose(span)) return false;
            foreach (var span in chain) if (TooClose(span)) return false;

            if (nights.Count > 0)
            {
                var all = new List<Span>(fixedBookings);
                all.AddRange(chain);
                all.Add(new Span(s, e));
                foreach (var n in nights) if (!NightKeepsSleep(n.start, n.end, all, sleepFloor)) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Plans f's flexible visits on absolute day `day`: one chain from the first anchor it fits at, placed around f's
    /// existing bookings; each is then booked solo or hosted as a session (AddPlannedBookings). Fixed offers are sessions
    /// f ranks in the hour's second pass (LateHourConfirm), moving these visits if needed. True if anything was booked (the caller refreshes
    /// the schedule once).
    /// </summary>
    /// <param name="maxAdvance">Only activities whose daysInAdvance is at most this (0 = the same-day-only ones, for a day already planned).</param>
    static bool PlanDay(Character_Factions f, int day, int maxAdvance)
    {
        var ctx = new DayContext(f, day);
        if (ctx.dayEnd <= ctx.nowAbs + 1) return false;
        if (Utility.RandomChance(SkipDayChance)) return false;

        var result = new List<RecreationBooking>();
        var candidates = CollectCandidates(ctx, maxAdvance);
        if (candidates.Count > 0)
        {
            foreach (var anchor in OrderedAnchors(ctx))
            {
                var placed = PackChain(ctx, anchor.Key, anchor.Value, candidates);
                if (placed.Count == 0) continue;
                result.AddRange(placed);
                break;
            }
        }

        return AddPlannedBookings(f, result);
    }

    /// <summary>
    /// The day's candidate visits, one per activity (RecreationSpec.activities): those of every recreation membership
    /// (acting under the membership itself), and those of every recreation membership the character's priority home /
    /// work factions offer (Manageable.GetOfferedRecreationTypes - booked acting under that type). Their own members need
    /// no membership: what they may do there is up to the commands' requirements (bookable commands gate only with
    /// requireJobFaction). Cooldown / weekly-target key = faction ID + ":" + activity ID. The scopes (ActivitySources) and
    /// rules (ActivityAllows) are also what an invitee must pass to come along (BookingCheck).
    /// <br/>Plus every flexible offer ctx's character is eligible for (OfferAllows - no membership needed; key = its ID),
    /// placed like an activity (RecreationOfferDef.AsActivity).
    /// <br/>Each only if ctx's day is within its daysInAdvance (ActivityAllows / OfferAllows), and its daysInAdvance is
    /// at most maxAdvance (PlanDay re-planning today: the same-day-only ones).
    /// </summary>
    static List<Candidate> CollectCandidates(DayContext ctx, int maxAdvance = int.MaxValue)
    {
        var result = new List<Candidate>();
        var home = ctx.f.HomeFactions[0];

        foreach (var source in ActivitySources(ctx.f))
        {
            if (source.spec.activities == null) continue;
            foreach (var activity in source.spec.activities)
            {
                if (activity == null || string.IsNullOrEmpty(activity.ID) || activity.daysInAdvance > maxAdvance) continue;
                string key = source.faction.ID + ":" + activity.ID;
                if (!ActivityAllows(ctx.f, ctx.day, key, activity)) continue;
                // a mandatory session needs its required people (CanHost)
                if (!CanHost(ctx, activity.invite, BookingCheck.ForActivity(source.faction.ID, activity.ID, ctx.day))) continue;
                float weight = activity.baseWeight * WeeklyFactor(ctx, key, activity) * TravelFactor(home, source.faction) * PersonalityFactor(ctx.c, source.faction, activity);
                if (weight <= 0f) continue;
                result.Add(new Candidate() { faction = source.faction, activity = activity, sourceKey = key, memberTypeID = source.memberTypeID, weight = weight });
            }
        }

        foreach (var def in FlexibleOffers())
        {
            var activity = def.AsActivity;
            if (activity.daysInAdvance > maxAdvance) continue;
            if (!OfferAllows(ctx.f, ctx.day, def) || !activity.AllowsDay(ctx.day - ctx.today)) continue;
            if (!CanHost(ctx, def.invite, BookingCheck.ForOffer(def, ctx.day))) continue;
            var venue = scr_System_CampaignManager.current.FindFactionByID(def.factionID);
            float weight = activity.baseWeight * WeeklyFactor(ctx, def.ID, activity) * TravelFactor(home, venue) * PersonalityFactor(ctx.c, venue, activity);
            if (weight <= 0f) continue;
            result.Add(new Candidate() { faction = venue, activity = activity, sourceKey = def.ID, memberTypeID = def.memberTypeID, weight = weight, source = RecreationBookingSource.Offer });
        }
        return result;
    }

    /// <summary>Every flexible offer def of the loaded worlds (a later world's entry wins).</summary>
    static IEnumerable<RecreationOfferDef> FlexibleOffers()
    {
        var seen = new HashSet<string>();
        foreach (var world in scr_System_CampaignManager.current.GetLoadedWorldPlans())
        {
            foreach (var entry in world.AllRecreationOffers)
            {
                if (entry == null || string.IsNullOrEmpty(entry.ID) || !seen.Add(entry.ID)) continue;
                var def = RecreationBoard.FindDef(entry.ID);
                if (def != null && def.mode == RecreationOfferMode.Flexible) yield return def;
            }
        }
    }

    /// <summary>The character's own factions whose recreation offers they may book without joining: the priority home, then the work factions.</summary>
    static IEnumerable<Manageable> OwnFactions(Character_Factions f)
    {
        var seen = new HashSet<Manageable>();
        if (f.HomeFactions.Count > 0 && f.HomeFactions[0] != null && seen.Add(f.HomeFactions[0])) yield return f.HomeFactions[0];
        foreach (var w in f.WorkFactions) if (w != null && seen.Add(w)) yield return w;
    }

    /// <summary>faction is still one of f's own factions (OwnFactions) and still offers recreation MemberType memberTypeID.</summary>
    static bool OwnFactionOffers(Character_Factions f, Manageable faction, string memberTypeID)
    {
        if (faction == null || string.IsNullOrEmpty(memberTypeID) || !OwnFactions(f).Contains(faction)) return false;
        return faction.GetOfferedRecreationTypes().Exists(t => t.ID == memberTypeID);
    }

    /// <summary>Days f has a visit for key: attended (remembered window) or booked.</summary>
    static HashSet<int> VisitDays(Character_Factions f, string key)
    {
        var days = new HashSet<int>(f.GetAttendedDays(key));
        foreach (var b in f.RecreationBookings) if (b != null && b.sourceKey == key) days.Add(b.day);
        return days;
    }

    /// <summary>Never twice on one day; with minDays (minDaysBetween), no other visit of key within that many days either side of `day`.</summary>
    static bool CooldownAllows(Character_Factions f, int day, string key, int minDays)
    {
        int gap = Math.Max(1, minDays);
        foreach (var d in VisitDays(f, key)) if (Math.Abs(d - day) < gap) return false;
        return true;
    }

    /// <summary>
    /// One scope the planner finds bookable activities in for a character: a recreation membership (memberTypeID "" -
    /// they act under the membership) or a recreation type one of their own home/work factions offers (acting under that type).
    /// </summary>
    struct ActivitySource
    {
        public Manageable faction;
        public RecreationSpec spec;
        public string memberTypeID;
    }

    /// <summary>
    /// Where f may book activities (the scopes CollectCandidates searches): f's recreation memberships, then the recreation
    /// types f's own factions offer (OwnFactions). A faction whose fee f hasn't paid gives none (CanWorkFor), nor one
    /// with an unpaid visit bill (HasUnpaidActivityFee).
    /// </summary>
    static IEnumerable<ActivitySource> ActivitySources(Character_Factions f)
    {
        var c = f.Owner;
        foreach (var faction in f.RecreationFactions)
        {
            if (faction == null || !c.CanWorkFor(faction) || HasUnpaidActivityFee(f, faction)) continue;
            var spec = faction.GetMemberType(c)?.recreation;
            if (spec != null) yield return new ActivitySource() { faction = faction, spec = spec, memberTypeID = "" };
        }
        foreach (var faction in OwnFactions(f))
        {
            if (!c.CanWorkFor(faction) || HasUnpaidActivityFee(f, faction)) continue;
            foreach (var type in faction.GetOfferedRecreationTypes())
                if (type?.recreation != null) yield return new ActivitySource() { faction = faction, spec = type.recreation, memberTypeID = type.ID };
        }
    }

    /// <summary>
    /// The rules activity (key = faction ID + ":" + activity ID) must pass for f on absolute day `day` - no weights: the
    /// day allowed (activeDays), its cooldown clear, and no other reservation of it pending (HasPendingReservation;
    /// except = the session being checked, which doesn't count against itself).
    /// </summary>
    static bool ActivityAllows(Character_Factions f, int day, string key, RecreationActivity activity, RecreationGroup except = null)
    {
        int ahead = day - scr_System_Time.current.getAbsoluteDay();
        return activity.AllowsDay(ahead) && activity.AllowsAdvance(ahead) && CooldownAllows(f, day, key, activity.minDaysBetween)
            && !HasPendingReservation(f, key, except);
    }

    /// <summary>
    /// The rules offer def must pass for f on absolute day `day` - no weights or chances: its cooldown clear, no other
    /// reservation of it pending (HasPendingReservation; except = the session being checked), its venue there with no
    /// unpaid visit bill of f's payer (HasUnpaidActivityFee), f eligible (actor tags).
    /// </summary>
    static bool OfferAllows(Character_Factions f, int day, RecreationOfferDef def, RecreationGroup except = null)
    {
        if (def == null || !CooldownAllows(f, day, def.ID, def.minDaysBetween)) return false;
        if (day - scr_System_Time.current.getAbsoluteDay() > def.daysInAdvance) return false;
        if (HasPendingReservation(f, def.ID, except)) return false;
        var venue = scr_System_CampaignManager.current.FindFactionByID(def.factionID);
        if (venue == null || HasUnpaidActivityFee(f, venue)) return false;
        return def.IsEligible(f.Owner);
    }

    /// <summary>
    /// One reservation per activity: f already holds a booking of key (an activity's / offer's sourceKey) that hasn't
    /// ended, or hosts an open session of it not yet ended (posted, not booked yet) - except's own booking / except itself
    /// not counted. A new one of the same activity waits until that one is over.
    /// </summary>
    static bool HasPendingReservation(Character_Factions f, string key, RecreationGroup except)
    {
        int nowAbs = NowAbs();
        foreach (var b in f.RecreationBookings)
            if (b != null && b.sourceKey == key && b.AbsEnd > nowAbs && (except == null || b.Session != except)) return true;
        foreach (var g in f.SessionInbox)
            if (g != null && g != except && g.IsOpen && g.AbsEnd > nowAbs && g.sourceKey == key && g.hostRef == f.Owner.RefID) return true;
        return false;
    }

    /// <summary>Below the weekly target: more likely the further below; at or above it: rapidly less likely.</summary>
    static float WeeklyFactor(DayContext ctx, string key, RecreationActivity activity)
    {
        if (activity.weeklyTarget <= 0) return 1f;
        int count = VisitDays(ctx.f, key).Count(d => d >= ctx.day - 6 && d < ctx.day);
        if (count < activity.weeklyTarget) return 1f + 0.5f * (activity.weeklyTarget - count);
        return Mathf.Pow(0.3f, count - activity.weeklyTarget + 1);
    }

    /// <summary>home root|venue root -> TravelFactor, for absolute day travelCacheDay only (rebuilt each day).</summary>
    static readonly Dictionary<string, float> travelCache = new Dictionary<string, float>();
    static int travelCacheDay = int.MinValue;

    /// <summary>Venues needing world-map travel from home weigh less the further they are.</summary>
    static float TravelFactor(Manageable home, Manageable venue)
    {
        var from = home?.FactionOwnerRoot;
        var to = venue?.FactionOwnerRoot;
        if (from == null || to == null) return 1f;

        int today = scr_System_Time.current.getAbsoluteDay();
        if (travelCacheDay != today) { travelCache.Clear(); travelCacheDay = today; }

        string key = from.ID + "|" + to.ID;
        if (!travelCache.TryGetValue(key, out float factor))
        {
            factor = scr_System_CampaignManager.current.Map.TryGetWorldMapTravelMinutes(from, to, out float minutes, out _)
                ? 1f / (1f + minutes / TravelHalfWeightMinutes) : 1f;
            travelCache[key] = factor;
        }
        return factor;
    }

    /// <summary>Hook for personality-driven taste (traits, interests...). Neutral for now.</summary>
    static float PersonalityFactor(Character_Trainable c, Manageable venue, RecreationActivity activity)
    {
        return 1f;
    }

    /// <summary>
    /// Chain anchors for the day in weighted-random order: absolute hour -> the chain's first visit starts at it (true) or
    /// ends at it (false), as near as fits (PlaceNear). After the day's last shift weighs most, after earlier shifts next,
    /// before a shift least; a day without any schedule gets one random start, afternoon/evening favored.
    /// </summary>
    static List<KeyValuePair<int, bool>> OrderedAnchors(DayContext ctx)
    {
        var anchors = new List<KeyValuePair<int, bool>>();
        var weights = new List<float>();
        int lastEnd = -1;
        bool anyOccupied = false;
        for (int t = ctx.dayStart; t < ctx.dayEnd; t++)
        {
            bool occ = ctx.Occupied(t), prevOcc = ctx.Occupied(t - 1);
            anyOccupied |= occ;
            if (prevOcc && !occ) { anchors.Add(new KeyValuePair<int, bool>(t, true)); weights.Add(2f); lastEnd = anchors.Count - 1; }
            if (!prevOcc && occ) { anchors.Add(new KeyValuePair<int, bool>(t, false)); weights.Add(1f); }
        }
        if (lastEnd >= 0) weights[lastEnd] = 4f;

        if (!anyOccupied)
        {
            var hours = new List<int>();
            var hourWeights = new List<float>();
            for (int h = 10; h <= 20; h++) { hours.Add(h); hourWeights.Add(h >= 14 && h <= 19 ? 3f : 1f); }
            anchors.Add(new KeyValuePair<int, bool>(ctx.dayStart + hours[WeightedIndex(hourWeights)], true));
            weights.Add(1f);
        }

        var ordered = new List<KeyValuePair<int, bool>>();
        while (anchors.Count > 0)
        {
            int i = WeightedIndex(weights);
            ordered.Add(anchors[i]);
            anchors.RemoveAt(i);
            weights.RemoveAt(i);
        }
        return ordered;
    }

    /// <summary>
    /// Grows one chain of visits from anchor: candidates in weighted-random order. The first visit is placed as near the
    /// anchor as fits (PlaceNear - forward: starting at it, else: ending at it); each further one, only tried with
    /// ExtendChainChance, joins an end of the chain (TryExtend - its preferred side, forward = after it, else the other).
    /// Visits at the same venue touch; at another venue they keep TravelBufferHours apart (DayContext.CanPlace).
    /// Returns the new visits.
    /// </summary>
    static List<RecreationBooking> PackChain(DayContext ctx, int anchor, bool forward, List<Candidate> candidates)
    {
        var result = new List<RecreationBooking>();
        var pool = new List<Candidate>(candidates);
        var chain = new List<Span>();
        int chainStart = anchor, chainEnd = anchor;

        while (pool.Count > 0)
        {
            if (chain.Count > 0 && !Utility.RandomChance(ExtendChainChance)) break;
            int pick = WeightedIndex(pool.Select(x => x.weight).ToList());
            var cand = pool[pick];
            pool.RemoveAt(pick);

            Span span;
            bool placed = chain.Count == 0
                ? PlaceNear(ctx, cand, chain, anchor, forward, out span)
                : TryExtend(ctx, cand, chain, chainStart, chainEnd, forward, out span);
            if (!placed) continue;
            var booking = RecreationBooking.Create(span.start / 24, span.start % 24, span.Length, cand.faction.ID, cand.activity.workModule,
                cand.source, cand.sourceKey, cand.memberTypeID, activityID: cand.source == RecreationBookingSource.Membership ? cand.activity.ID : "",
                forbidCancel: cand.activity.forbidCancel, flexible: true);
            if (booking == null) continue;

            result.Add(booking);
            chainStart = chain.Count == 0 ? span.start : Math.Min(chainStart, span.start);
            chainEnd = chain.Count == 0 ? span.end : Math.Max(chainEnd, span.end);
            chain.Add(span);
        }
        return result;
    }

    /// <summary>The activity's lengths to try: a random one in its range first, then the rest longest first.</summary>
    static List<int> LengthsToTry(RecreationActivity activity)
    {
        int preferred = UnityEngine.Random.Range(activity.MinHours, activity.MaxHours + 1);
        var lengths = new List<int>() { preferred };
        for (int l = activity.MaxHours; l >= activity.MinHours; l--) if (l != preferred) lengths.Add(l);
        return lengths;
    }

    /// <summary>
    /// The chain's first visit, as near anchor as it fits (like sleep's search outward from its wake target,
    /// Character_Factions.PlaceSleep): forward = starting at anchor, else ending at it; then 1, 2... hours off - away
    /// from the anchor's schedule side first (later for forward, earlier otherwise), so a shift's end/start that is
    /// already past or needs the travel hour still finds the nearest free slot. Whole day searched.
    /// </summary>
    static bool PlaceNear(DayContext ctx, Candidate cand, List<Span> chain, int anchor, bool forward, out Span span)
    {
        var lengths = LengthsToTry(cand.activity);
        for (int n = 0; n < 24; n++)
        {
            foreach (int offset in n == 0 ? new[] { 0 } : (forward ? new[] { n, -n } : new[] { -n, n }))
            {
                int pivot = anchor + offset;
                foreach (int l in lengths)
                {
                    span = forward ? new Span(pivot, pivot + l, cand.faction.ID) : new Span(pivot - l, pivot, cand.faction.ID);
                    if (ctx.CanPlace(span.start, span.end, cand.activity, chain, cand.faction.ID)) return true;
                }
            }
        }
        span = default;
        return false;
    }

    /// <summary>
    /// A further visit at an end of the chain [chainStart, chainEnd): its preferred side (forward = after) first, then the
    /// other; right against it, or TravelBufferHours off when the neighbouring visit is at another venue.
    /// </summary>
    static bool TryExtend(DayContext ctx, Candidate cand, List<Span> chain, int chainStart, int chainEnd, bool forward, out Span span)
    {
        var lengths = LengthsToTry(cand.activity);
        foreach (bool side in new[] { forward, !forward })
        {
            foreach (int l in lengths)
            {
                foreach (int gap in new[] { 0, TravelBufferHours })
                {
                    span = side ? new Span(chainEnd + gap, chainEnd + gap + l, cand.faction.ID) : new Span(chainStart - gap - l, chainStart - gap, cand.faction.ID);
                    if (ctx.CanPlace(span.start, span.end, cand.activity, chain, cand.faction.ID)) return true;
                }
            }
        }
        span = default;
        return false;
    }

    static int WeightedIndex(List<float> weights)
    {
        float total = 0f;
        foreach (var w in weights) total += Mathf.Max(0f, w);
        if (total <= 0f) return UnityEngine.Random.Range(0, weights.Count);
        float roll = UnityEngine.Random.Range(0f, total);
        for (int i = 0; i < weights.Count; i++)
        {
            roll -= Mathf.Max(0f, weights[i]);
            if (roll < 0f) return i;
        }
        return weights.Count - 1;
    }

    // ---------------- re-planning ---------------- //

    /// <summary>
    /// broken can no longer happen as planned (a schedule now claims its hours, or it eats too much of a night - see
    /// HourlyCheck - or a session needs its time). Re-packs that day's movable bookings (flexible visits not frozen yet -
    /// IsMovableVisit - broken among them) as one chain (TravelBufferHours apart where the venue changes) at the start
    /// nearest their original one, keeping as many as fit in their original order; those that no longer fit are
    /// cancelled. A frozen, locked, event or session booking is never moved - it is just cancelled. unplacedReason = what
    /// a cancellation here is announced as (NoTime; Displaced when an event booking or a session took the time).
    /// </summary>
    public static void TryReschedule(Character_Factions f, RecreationBooking broken, CancelReason unplacedReason = CancelReason.NoTime)
    {
        if (f == null || broken == null) return;
        int nowAbs = NowAbs();

        // a session booking's time is shared - never moved, only given up; a flexible visit is frozen from the hour before it
        bool Movable(RecreationBooking b) => b != null && IsMovableVisit(b, nowAbs);
        if (!Movable(broken))
        {
            // may already be off the list (SetEventBooking clears the way first) - cancelled either way
            f.RemoveBooking(broken);
            f.NotifyBookingCancelled(broken, unplacedReason);
            f.RefreshSchedule();
            return;
        }

        var group = f.RecreationBookings.Where(b => Movable(b) && b.day == broken.day).OrderBy(b => b.AbsStart).ToList();
        if (!group.Contains(broken)) group.Add(broken);
        int origin = group.Min(b => b.AbsStart);
        foreach (var b in group) f.RemoveBooking(b);

        var ctx = new DayContext(f, broken.day);
        List<KeyValuePair<RecreationBooking, int>> best = null;
        for (int d = 0; d < 24 && (best == null || best.Count < group.Count); d++)
        {
            foreach (int s0 in d == 0 ? new[] { origin } : new[] { origin - d, origin + d })
            {
                var placed = PlaceInOrder(ctx, group, s0);
                if (best == null || placed.Count > best.Count) best = placed;
            }
        }

        var kept = new HashSet<RecreationBooking>();
        if (best != null)
        {
            foreach (var kv in best)
            {
                var old = kv.Key;
                var moved = old.AbsStart == kv.Value ? old : RecreationBooking.Create(kv.Value / 24, kv.Value % 24, old.hours, old.factionID, old.workModule,
                    old.source, old.sourceKey, old.memberTypeID, old.locked, old.onCancelledEventID, old.overrideWork, old.activityID, old.forbidCancel, old.sourceEventID,
                    flexible: old.flexible);
                if (moved != null && f.AddBooking(moved, false))
                {
                    kept.Add(old);
                    if (moved != old) RecordMoved(f, old, moved);
                }
            }
        }
        foreach (var b in group) if (!kept.Contains(b)) f.NotifyBookingCancelled(b, unplacedReason);
        f.RefreshSchedule();
    }

    /// <summary>
    /// group laid end to end from s0 in order - TravelBufferHours apart where the venue changes - skipping any that
    /// don't fit there (the next tries the same spot).
    /// </summary>
    static List<KeyValuePair<RecreationBooking, int>> PlaceInOrder(DayContext ctx, List<RecreationBooking> group, int s0)
    {
        var placed = new List<KeyValuePair<RecreationBooking, int>>();
        var chain = new List<Span>();
        int cursor = s0;
        foreach (var b in group)
        {
            // the visit's activity: a flexible offer's, else from the type it runs under (an own-faction visit's offered type, else the membership held there)
            var activity = ActivityOf(ctx.c, b);
            int start = -1;
            foreach (int gap in chain.Count == 0 ? new[] { 0 } : new[] { 0, TravelBufferHours })
                if (ctx.CanPlace(cursor + gap, cursor + gap + b.hours, activity, chain, b.factionID)) { start = cursor + gap; break; }
            if (start < 0) continue;
            placed.Add(new KeyValuePair<RecreationBooking, int>(b, start));
            chain.Add(new Span(start, start + b.hours, b.factionID));
            cursor = start + b.hours;
        }
        return placed;
    }

    // ---------------- event entry points ---------------- //

    /// <summary>
    /// AddRecreationOffer: posts fixed offer def defID on day today + dayOffset (optionally at another start/length) - its
    /// hostless World session goes on the venue's world list, and each character there ranks it in the hour's second pass (LateHourConfirm).
    /// False if the def is unknown or flexible.
    /// </summary>
    public static bool PostOffer(string defID, int dayOffset, int startHour = -1, int hours = -1)
    {
        int day = scr_System_Time.current.getAbsoluteDay() + dayOffset;
        return scr_System_CampaignManager.current.RecreationBoard.Post(defID, day, startHour, hours);
    }

    /// <summary>
    /// Debug (console listallvalidbookings true): drops c's planner-made flexible visits that have not started (silently,
    /// no notice/event), forgets which days were planned, runs DailyPlan right away (the rest of today + tomorrow - step
    /// 1) and then c's own ranking / confirming pass at once (steps 2-3 - other characters still answer on their own next
    /// tick). Event bookings, session bookings and visits under way stay. Returns c's bookings, then the sessions c ranks
    /// (best first) - each with its members as role:name:origin:state(reason).
    /// </summary>
    public static List<string> DebugReplan(Character_Trainable c)
    {
        var result = new List<string>();
        var f = c?.FactionManager;
        if (f == null) return result;

        int nowAbs = NowAbs();
        foreach (var b in f.RecreationBookings.ToList())
            if (b != null && !b.locked && b.source != RecreationBookingSource.Event && !b.isSessionBooking && b.AbsStart > nowAbs) f.RemoveBooking(b);
        f.LastRecreationPlanDay = scr_System_Time.current.getAbsoluteDay() - 1;

        if (!ShouldPlan(f)) result.Add("not planned for: player / temporary / dormant / no home / worker pool / party-locked / home forbids work");
        else
        {
            // what the planner can pick from, per day (empty = nothing to plan: no membership / offer allowed then)
            int today = scr_System_Time.current.getAbsoluteDay();
            result.Add("-- candidates --");
            for (int day = today; day <= today + 1; day++)
            {
                var dayCtx = new DayContext(f, day);
                var cands = dayCtx.dayEnd <= dayCtx.nowAbs + 1 ? new List<Candidate>() : CollectCandidates(dayCtx);
                result.Add($"day {day}{(day == today ? " (today)" : "")}: " + (cands.Count == 0 ? "none"
                    : string.Join(", ", cands.Select(x => $"{x.sourceKey} (advance {x.activity.daysInAdvance})"))));
            }
        }
        DailyPlan(f);
        f.RefreshSchedule();
        if (ShouldPlan(f))
        {
            BeginNotice(f);
            try { SessionPass(f); }
            finally { EndNotice(f, false); }
        }

        string Members(RecreationGroup g) => string.Join(", ", g.members.Select(m =>
            $"{m.roleID}:{m.Chara?.FullName ?? m.charaRef.ToString()}:{m.origin}:{m.state}{(string.IsNullOrEmpty(m.reason) ? "" : "(" + m.reason + ")")}"
            + $"{(string.IsNullOrEmpty(m.failedCheck) ? "" : "!" + m.failedCheck)}{(m.arrived ? "+" : "")}"));

        result.Add("-- bookings --");
        foreach (var b in f.RecreationBookings.Where(x => x != null).OrderBy(x => x.AbsStart))
        {
            string line = $"{DescribeBooking(b)} [{b.source} {b.sourceKey}{(b.locked ? " locked" : "")}{(b.flexible ? " flexible" : "")}]";
            var g = b.Session;
            if (g != null) line += $" session {g.Label} as {b.roleID}: " + Members(g);
            result.Add(line);
        }
        result.Add($"-- sessions in inbox: {f.SessionInbox.Count} --");
        foreach (var g in f.SessionInbox) result.Add($"{g.Label}: " + Members(g));
        result.Add("-- sessions ranked (best first) --");
        foreach (var g in f.SessionRanking)
        {
            string line = $"{g.Label}: " + Members(g);
            var ext = g.extensions.Where(e => e.extenderRef == c.RefID).ToList();
            if (ext.Count > 0) line += " | extended: " + string.Join("; ", ext.Select(e => $"{e.roleID}{(e.inviteRequired ? " required" : "")} -> "
                + string.Join(",", e.picks.Select(r => scr_System_CampaignManager.current.FindInstanceByID(r)?.FullName ?? r.ToString()))));
            result.Add(line);
        }
        return result;
    }

    /// <summary>
    /// Debug (console listallvalidbookings): every booking c could make, one line each as "templateID @ factionID (source)"
    /// - templateID / factionID as SetRecreationBooking takes them. The activities of c's recreation memberships and of the
    /// memberships c's own home/work factions offer (CollectCandidates' sources, minus a faction whose fee is unpaid), and
    /// every loaded offer def c is eligible for (posted or not). Day/cooldown/weekly rules are not applied.
    /// </summary>
    public static List<string> ListValidBookingIDs(Character_Trainable c)
    {
        var result = new List<string>();
        var f = c?.FactionManager;
        if (f == null) return result;

        void AddAll(Manageable faction, MemberType type, string source)
        {
            if (type?.recreation?.activities == null) return;
            foreach (var activity in type.recreation.activities)
                if (activity != null && !string.IsNullOrEmpty(activity.ID)) result.Add($"{type.ID}:{activity.ID} @ {faction.ID} ({source})");
        }

        foreach (var faction in f.RecreationFactions)
        {
            if (faction == null || !c.CanWorkFor(faction)) continue;
            AddAll(faction, faction.GetMemberType(c), "membership");
        }
        foreach (var faction in OwnFactions(f))
        {
            if (!c.CanWorkFor(faction)) continue;
            foreach (var type in faction.GetOfferedRecreationTypes()) AddAll(faction, type, "offered by own faction");
        }

        var seen = new HashSet<string>();
        foreach (var world in scr_System_CampaignManager.current.GetLoadedWorldPlans())
        {
            foreach (var def in world.AllRecreationOffers)
            {
                if (def == null || string.IsNullOrEmpty(def.ID) || !seen.Add(def.ID)) continue;
                var resolved = RecreationBoard.FindDef(def.ID);   // a later world's entry wins
                if (resolved == null || !resolved.IsEligible(c)) continue;
                result.Add($"{resolved.ID} @ {resolved.factionID} ({(resolved.mode == RecreationOfferMode.Flexible ? "flexible offer" : "fixed offer")})");
            }
        }
        return result;
    }

    /// <summary>
    /// SetRecreationBooking: a locked event booking of c at factionID ("@home" = c's priority home; c's own home/work
    /// factions are allowed), `hours` hours from startHour (-1 = this hour) on today + dayOffset. templateID (optional)
    /// gives its work module and acting MemberType: an offer def ID, or a MemberType ID - "typeID:activityID" for one of
    /// its recreation activities, plain "typeID" for its first activity, else its own workModule. Planned bookings in the
    /// way are moved (TryReschedule) or cancelled; another locked one in the way
    /// refuses it. wakeForIt: if c is asleep, the sleep is cut to end at the booking's start (the cut counted as missed
    /// sleep). overrideWork: see RecreationBooking.overrideWork. forbidCancel: the player may not ask c to cancel it (also
    /// set when the template's own forbidCancel is). sourceEventID: the calling event (shown as who arranged it). Unless
    /// solo, when the template's invite spec (an offer def's / activity's) has a visibility, c hosts the booking as a
    /// session at its own time, already attending (TryHostSession - no AcceptHosting: the event decided; inviteChance
    /// still rolled for an optional spec); a session that is cancelled leaves the booking going on alone. The change is
    /// announced like any other (AnnounceNotice), displayOverride = the calling event's own.
    /// </summary>
    public static bool SetEventBooking(Character_Trainable c, string factionID, int dayOffset, int startHour, int hours,
        string templateID, bool wakeForIt, bool overrideWork, string onCancelledEventID, bool displayOverride = false,
        bool forbidCancel = false, string sourceEventID = "", bool solo = false)
    {
        var f = c?.FactionManager;
        if (f == null) return false;
        BeginNotice(f);
        try { return SetEventBookingInner(c, f, factionID, dayOffset, startHour, hours, templateID, wakeForIt, overrideWork, onCancelledEventID, forbidCancel, sourceEventID, solo); }
        finally { EndNotice(f, displayOverride); }
    }

    static bool SetEventBookingInner(Character_Trainable c, Character_Factions f, string factionID, int dayOffset, int startHour, int hours,
        string templateID, bool wakeForIt, bool overrideWork, string onCancelledEventID, bool forbidCancel, string sourceEventID, bool solo)
    {
        if (factionID == "@home") factionID = f.HomeFactions.Count > 0 && f.HomeFactions[0] != null ? f.HomeFactions[0].ID : "";
        if (scr_System_CampaignManager.current.FindFactionByID(factionID) == null) return false;

        MapPlan.WorkModuleInit module = null;
        string memberTypeID = "";
        string activityID = "";
        var def = RecreationBoard.FindDef(templateID);
        if (def != null) { module = def.workModule; memberTypeID = def.memberTypeID; forbidCancel |= def.forbidCancel; }
        else if (!string.IsNullOrEmpty(templateID))
        {
            int split = templateID.IndexOf(':');
            string typeID = split < 0 ? templateID : templateID.Substring(0, split);
            if (FactionUtility.TryGetMemberType(typeID, out var type) && type != null)
            {
                var activities = type.recreation?.activities;
                var activity = split < 0 ? activities?.FirstOrDefault(a => a != null) : type.recreation?.GetActivity(templateID.Substring(split + 1));
                module = activity != null ? activity.workModule : type.workModule;
                activityID = activity != null ? activity.ID : "";
                memberTypeID = type.ID;
                if (activity != null) forbidCancel |= activity.forbidCancel;
            }
        }

        if (startHour < 0) { startHour = scr_System_Time.current.getCurrentTime().Hour; dayOffset = 0; }
        int day = scr_System_Time.current.getAbsoluteDay() + dayOffset;
        var booking = RecreationBooking.Create(day, startHour, hours, factionID, module, RecreationBookingSource.Event,
            string.IsNullOrEmpty(templateID) ? factionID : templateID, memberTypeID, true, onCancelledEventID, overrideWork, activityID,
            forbidCancel, sourceEventID);
        if (booking == null) return false;

        var inTheWay = f.RecreationBookings.Where(b => b != null && b.Overlaps(booking.AbsStart, booking.AbsEnd)).ToList();
        if (inTheWay.Exists(b => b.locked)) return false;
        foreach (var b in inTheWay) f.RemoveBooking(b);
        if (!f.AddBooking(booking, false))
        {
            foreach (var b in inTheWay) f.AddBooking(b, false);   // put back as it was
            return false;
        }
        RecordBooked(f, booking);
        foreach (var b in inTheWay) TryReschedule(f, b, CancelReason.Displaced);
        if (wakeForIt) CutSleepFor(c, booking);
        // the player never hosts (not in scope yet) - their event bookings stay solo
        if (!solo && c != Player)
        {
            var spec = ResolveSpec(SpecRefFor(f, booking));
            var vis = EffectiveVisibility(spec, booking);
            if (vis != RecreationVisibility.None && !spec.solo && (spec.IsMandatory || Utility.RandomChance(spec.inviteChance)))
                TryHostSession(f, booking, spec, vis, true);
        }
        f.RefreshSchedule();
        return true;
    }

    /// <summary>If c is asleep past booking's start, the sleep now ends at it (at least a minute from now); the cut is missed sleep.</summary>
    static void CutSleepFor(Character_Trainable c, RecreationBooking booking)
    {
        if (!c.isSleeping) return;
        var sleeping = c.Stats.GetStatusByStringMatch(StatsUtility.Status_Sleeping);
        if (sleeping == null || sleeping.duration < 0) return;   // no running timer to cut

        var now = scr_System_Time.current.getCurrentTime();
        int untilStart = Math.Max(1, (booking.AbsStart - NowAbs()) * 60 - now.Minute);
        if (untilStart >= sleeping.duration) return;

        c.ScheduledSleepMissingMinutes += sleeping.duration - untilStart;
        sleeping.duration = untilStart;
    }

    /// <summary>
    /// CancelRecreationBooking: cancels c's bookings (each firing its onCancelledEventID) at factionID ("" = anywhere):
    /// when "now" = the one under way, "today" = those under way or starting today, "all" (default) = every one not ended.
    /// Returns how many were cancelled. Announced like any other change (AnnounceNotice), displayOverride = the calling event's own.
    /// </summary>
    public static int CancelBookings(Character_Trainable c, string factionID, string when, bool displayOverride = false)
    {
        var f = c?.FactionManager;
        if (f == null) return 0;
        BeginNotice(f);
        try { return CancelBookingsInner(f, factionID, when); }
        finally { EndNotice(f, displayOverride); }
    }

    static int CancelBookingsInner(Character_Factions f, string factionID, string when)
    {
        int today = scr_System_Time.current.getAbsoluteDay();
        int hour = scr_System_Time.current.getCurrentTime().Hour;
        int nowAbs = RecreationBooking.AbsoluteHour(today, hour);

        int count = 0;
        foreach (var b in f.RecreationBookings.ToList())
        {
            if (b == null || b.AbsEnd <= nowAbs) continue;
            if (!string.IsNullOrEmpty(factionID) && b.factionID != factionID) continue;
            bool underWay = b.Covers(today, hour);
            if (when == "now" && !underWay) continue;
            if (when == "today" && !underWay && b.day != today) continue;
            f.CancelBooking(b, false, CancelReason.ByEvent);
            count++;
        }
        if (count > 0) f.RefreshSchedule();
        return count;
    }

    // ---------------- player requests (Opt_CancelBooking) ---------------- //

    /// <summary>c's bookings not ended yet (under way included), in time order.</summary>
    public static List<RecreationBooking> PendingBookings(Character_Trainable c)
    {
        var f = c?.FactionManager;
        if (f == null) return new List<RecreationBooking>();
        int nowAbs = NowAbs();
        return f.RecreationBookings.Where(b => b != null && b.AbsEnd > nowAbs).OrderBy(b => b.AbsStart).ToList();
    }

    /// <summary>c's booking not ended yet that starts at absolute hour absStart (bookings never overlap, so the start identifies one), or null.</summary>
    public static RecreationBooking FindPendingBooking(Character_Trainable c, int absStart)
    {
        return PendingBookings(c).Find(b => b.AbsStart == absStart);
    }

    /// <summary>
    /// ListCancellableBookings: one question option per booking of c not ended yet (PendingBookings) - text = DescribeBooking,
    /// tooltip = who arranged it (DescribeArrangedBy); a forbidCancel one is drawn disabled (ui_recreation_cancelForbidden).
    /// Picking one only remembers it - ev.Parameters[selectedParamKey] = its AbsStart - for the event's accept check and
    /// CancelSelectedBooking.
    /// </summary>
    public static List<Event.EventEntry.Options> BuildCancelOptions(Character_Trainable c, string selectedParamKey)
    {
        var result = new List<Event.EventEntry.Options>();
        foreach (var b in PendingBookings(c))
        {
            int absStart = b.AbsStart;
            // a session booking: who else is coming, and whether cancelling calls it off for everyone
            string tooltip = DescribeArrangedBy(c, b);
            string company = DescribeCompany(b.Session, c.RefID);
            if (company != "") tooltip += "\n" + company.Trim();
            if (WouldBreakSession(b, c)) tooltip += "\n" + LocalizeDictionary.QueryThenParse("ui_recreation_cancel_breaksGroup");
            result.Add(new Event.EventEntry.Options()
            {
                option = DescribeBooking(b),
                tooltip = tooltip,
                onSelect = b.forbidCancel ? null : (Action<EventInstance>)(ev => ev.Parameters[selectedParamKey] = absStart),
                disabledReasonKey = b.forbidCancel ? "ui_recreation_cancelForbidden" : "",
            });
        }
        return result;
    }

    /// <summary>
    /// Who arranged b: someone who invited c (a session's host, or a participant who extended the invitation), a session
    /// c joined from a faction / world post (its host's), c themselves (a flexible visit / a public offer), or the event
    /// that booked it (its ID localized).
    /// </summary>
    public static string DescribeArrangedBy(Character_Trainable c, RecreationBooking b)
    {
        var g = b.Session;
        var member = g?.FindMember(c.RefID);
        if (member != null && member.origin != RecreationMemberOrigin.Host)
        {
            var inviter = InviterOf(g, member);
            if (inviter != null) return LocalizeDictionary.QueryThenParse("ui_recreation_arrangedBy_Invite").Replace("$inviter$", inviter.FullName);
            var host = g.Host;
            if (host != null) return LocalizeDictionary.QueryThenParse("ui_recreation_arrangedBy_Joined").Replace("$host$", host.FullName);
        }
        switch (b.source)
        {
            case RecreationBookingSource.Membership:
                return LocalizeDictionary.QueryThenParse("ui_recreation_arrangedBy_Membership").Replace("$name$", c.FullName);
            case RecreationBookingSource.Offer:
                return LocalizeDictionary.QueryThenParse("ui_recreation_arrangedBy_Offer").Replace("$name$", c.FullName);
            default:
                if (string.IsNullOrEmpty(b.sourceEventID)) return LocalizeDictionary.QueryThenParse("ui_recreation_arrangedBy_Unknown");
                return LocalizeDictionary.QueryThenParse("ui_recreation_arrangedBy_Event")
                    .Replace("$event$", LocalizeDictionary.QueryThenParse(b.sourceEventID, b.sourceEventID));
        }
    }

    /// <summary>
    /// CancelSelectedBooking: cancels c's booking b at the player's request (CancelReason.ByRequest; onCancelledEventID
    /// fires as for any cancellation). Announced quietly - the home factions' daily report only, no Event_BookingChanged:
    /// the dialogue shows text ("booking line (reason)") itself. False, nothing done, for a forbidCancel booking or one
    /// already ended / gone.
    /// </summary>
    public static bool CancelByRequest(Character_Trainable c, RecreationBooking b, out string text)
    {
        text = "";
        var f = c?.FactionManager;
        if (f == null || b == null || b.forbidCancel || b.AbsEnd <= NowAbs() || !f.RecreationBookings.Contains(b)) return false;
        text = DescribeBooking(b) + DescribeReason(CancelReason.ByRequest);
        BeginNotice(f);
        try { f.CancelBooking(b, true, CancelReason.ByRequest); }
        finally { EndNotice(f, false, true); }
        return true;
    }
}
