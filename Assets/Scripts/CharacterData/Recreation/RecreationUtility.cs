using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

/// <summary>
/// Recreation booking logic. Stateless: the bookings, attendance and plan progress live on Character_Factions, which
/// calls through DailyPlan (day update stage 3) and explicit activity-change reconciliation.
/// <br/>DailyPlan books a character's flexible visits for tomorrow - the activities of their recreation memberships
/// (MemberType.recreation - RecreationSpec.activities, one activity per visit) and flexible offers (RecreationOfferDef,
/// Flexible) - into the free time around work and sleep, as RecreationBookings. A visit whose spec has a visibility is
/// hosted as a shared session instead; fixed offers are sessions posted by the board. Sessions are answered in the
/// hour's late passes and then resolve themselves (RecreationUtility_Groups.cs). Every activity announces itself
/// (AnnounceSolo / AnnounceSession) - no per-character booking notices.
/// The first run for a character also fills the rest of today.
/// <br/>Shape of a day's plan: one chain of flexible visits (1-4h each, MaxDailyHours in total with the day's other
/// bookings) placed as near as fits to an anchor - after the day's last shift (work ->
/// recreation -> sleep) most often, before a shift otherwise - or a random afternoon/evening start on a day off. Visits
/// at the same venue run back to back; anything at another place (another venue, another faction's shift) is kept
/// TravelBufferHours away for the trip. A visit the character plans for themselves never takes a predicted night
/// (Character_Factions.PredictSleep); only a fixed-time one (a fixed offer, an event booking) or an invitation they
/// accept may - they give up the sleep and wake up in time for it (WakeForSessions).
/// <br/>Once planned, a booking stays as it is unless an external change breaks it: a session or event booking taking
/// its time re-packs that day's movable bookings (TryReschedule); a work / home schedule claiming it, lost access or a
/// lost facility cancel it (re-validation on those changes - nothing is polled).
/// <br/>Not planned for: the player character, fallback workers, temporary/dormant actors, characters without a home,
/// held by a party lock, or whose priority home forbids work.
/// <br/>Shared (multi-character) activities - sessions: posting, ranking, confirming, extending invitations - are in RecreationUtility_Groups.cs.
/// </summary>
public static partial class RecreationUtility
{
    public const int MaxDailyHours = 8;
    /// <summary>Shortest visit (RecreationActivity.MinHours clamp). 1 since 2026-10-31 (the 1-h checkup); activities default to minHours 2.</summary>
    public const int MinSegmentHours = 1;
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
    /// Free hours kept between a visit and anything next to it at another place - a work/home schedule hour of another
    /// faction, or a booking at another venue - for the trip (mirrors sleep's buffer hour, Character_Factions.PlaceSleep).
    /// Visits at the same venue / at the schedule's own faction may touch.
    /// </summary>
    public const int TravelBufferHours = 1;

    /// <summary>
    /// Getting-up minutes (dressing etc.) kept before a visit after a night's sleep, on top of the trip from home to the
    /// venue: a visit may not start within DayContext.WakeUpBufferHours (travel + this, rounded up to whole hours) after a
    /// predicted night's sleep ends - nor at the end of a night it shortens.
    /// </summary>
    public const int WakeUpPrepMinutes = 30;

    /// <summary>
    /// Awake hours needed before a visit at venueID when getting up at from: the world-map trip between them
    /// (TravelMinutes) plus WakeUpPrepMinutes, rounded up to whole hours - at least 1.
    /// </summary>
    public static int WakeUpBufferHours(Manageable from, string venueID)
    {
        var venue = string.IsNullOrEmpty(venueID) ? null : scr_System_CampaignManager.current.FindFactionByID(venueID);
        return Math.Max(1, (int)Math.Ceiling((TravelMinutes(from, venue) + WakeUpPrepMinutes) / 60f));
    }

    /// <summary>Absolute-hour interval [start, end); venue = the booking's faction ID where it is one (null otherwise - counts as elsewhere).</summary>
    public struct Span
    {
        public int start, end;
        public string venue;
        public bool sleeping;
        public Span(int start, int end, string venue = null, bool sleeping = false) { this.start = start; this.end = end; this.venue = venue; this.sleeping = sleeping; }
        public int Length { get { return end - start; } }
        public bool Contains(int t) { return t >= start && t < end; }
    }

    static int NowAbs()
    {
        return RecreationBooking.AbsoluteHour(scr_System_Time.current.getAbsoluteDay(), scr_System_Time.current.getCurrentTime().Hour);
    }

    /// <summary>Stable in-place insertion sort (small lists) - the order LINQ OrderBy gave, without its allocations.</summary>
    static void SortStable<T>(List<T> list, Comparison<T> compare)
    {
        for (int i = 1; i < list.Count; i++)
        {
            var item = list[i];
            int j = i - 1;
            while (j >= 0 && compare(list[j], item) > 0) { list[j + 1] = list[j]; j--; }
            list[j + 1] = item;
        }
    }

    // ---------------- change notices ---------------- //

    /// <summary>Data event an activity announces itself with (confirmed / rescheduled / cancelled) - its line shows $activityText$.</summary>
    public const string Event_ActivityNotice = "Recreation_ActivityNotice";

    /// <summary>Why a booking was cancelled - picks the reason text (ui_recreation_cancel_* keys).</summary>
    public enum CancelReason { None, Slept, WorkForbidden, LostAccess, NoTime, Displaced, MembershipChanged, ByEvent, ByRequest, GroupCancelled, GroupFailed, NoShow, FacilityRemoved }

    /// <summary>What an activity announces about itself.</summary>
    public enum ActivityNoticeKind { Confirmed, Rescheduled, Cancelled }

    /// <summary>
    /// Announcement scope of the call in progress (BeginAnnounceScope ... EndAnnounceScope, nested): displayOverride =
    /// what is announced inside is forced on screen (an event's displayOverride, the midnight planning of the player's
    /// household); quiet = no solo activity announces itself (the caller shows the change itself - the cancel-booking
    /// dialogue; a session the change cancels still announces).
    /// </summary>
    static int announceForcedDepth, announceQuietDepth;

    static void BeginAnnounceScope(bool displayOverride, bool quiet = false)
    {
        if (displayOverride) announceForcedDepth++;
        if (quiet) announceQuietDepth++;
    }

    static void EndAnnounceScope(bool displayOverride, bool quiet = false)
    {
        if (displayOverride && announceForcedDepth > 0) announceForcedDepth--;
        if (quiet && announceQuietDepth > 0) announceQuietDepth--;
    }

    /// <summary>Reused participant buffer for a solo announcement (an announcement never re-enters another).</summary>
    static readonly List<Character_Trainable> soloParticipants = new List<Character_Trainable>(1);

    /// <summary>
    /// A solo activity (a booking without a session) announces itself: booked (it needs no confirmation), moved (from =
    /// its old time) or cancelled (reason). Its owner is the participant.
    /// </summary>
    static void AnnounceSolo(RecreationBooking b, ActivityNoticeKind kind, CancelReason reason = CancelReason.None, RecreationBooking from = null)
    {
        if (b == null || b.Session != null || b.isSessionBooking || announceQuietDepth > 0) return;
        var owner = b.Owner;
        if (owner == null) return;
        soloParticipants.Clear();
        soloParticipants.Add(owner);
        try
        {
            string line = kind == ActivityNoticeKind.Rescheduled && from != null
                ? LocalizeDictionary.QueryThenParse("ui_recreation_notice_movedLine").Replace("$from$", DescribeBooking(from)).Replace("$to$", DescribeTime(b))
                : DescribeBooking(b);
            Announce(kind, line, reason, soloParticipants, false);
        }
        finally { soloParticipants.Clear(); }
    }

    /// <summary>A session announces itself once resolved (ResolveSession - confirmed) or when cancelled (CancelSession) - participants = who attends / was coming.</summary>
    static void AnnounceSession(RecreationGroup g, ActivityNoticeKind kind, CancelReason reason, List<Character_Trainable> participants)
    {
        if (g == null) return;
        // the night's sessions resolve at midnight: forced on screen for the player's household, as the night's solo bookings are
        bool midnight = scr_System_Time.current.getCurrentTime().Hour == 0;
        Announce(kind, DescribeSessionLine(g), reason, participants, midnight);
    }

    /// <summary>
    /// Starts Event_ActivityNotice: "[header] activity @ venue, time (reason)" + "participants: A, B". Self = the first
    /// participant who is the player or visible to them; forced on screen (displayOverride) inside a forcing scope, or
    /// householdForced with a participant of the player's household - then self is the first participant. Nothing when
    /// no participant qualifies.
    /// </summary>
    static void Announce(ActivityNoticeKind kind, string line, CancelReason reason, List<Character_Trainable> participants, bool householdForced)
    {
        if (participants == null || participants.Count == 0 || scr_UpdateHandler.current == null) return;
        var campaign = scr_System_CampaignManager.current;
        Character_Trainable self = null;
        bool forced = announceForcedDepth > 0;
        for (int i = 0; i < participants.Count; i++)
        {
            var c = participants[i];
            if (c == null) continue;
            if (self == null && (c == campaign.Player || campaign.isCharaVisibleToPlayer(c.RefID))) self = c;
            if (householdForced && !forced && FactionUtility.IsPlayerHousehold(c)) forced = true;
        }
        if (forced && self == null)
            for (int i = 0; i < participants.Count && self == null; i++) self = participants[i];
        if (self == null) return;

        string header = LocalizeDictionary.QueryThenParse(kind == ActivityNoticeKind.Confirmed ? "ui_recreation_notice_booked"
            : kind == ActivityNoticeKind.Rescheduled ? "ui_recreation_notice_moved" : "ui_recreation_notice_cancelled");
        var names = new List<string>(participants.Count);
        for (int i = 0; i < participants.Count; i++) if (participants[i] != null) names.Add(participants[i].FullName);
        string text = header + line + (kind == ActivityNoticeKind.Cancelled ? DescribeReason(reason) : "") + "\n"
            + LocalizeDictionary.QueryThenParse("ui_recreation_notice_participants")
                .Replace("$names$", string.Join(LocalizeDictionary.QueryThenParse("event_heldMembership_separator"), names));

        var ev = new EventInstance(self, Event_ActivityNotice, "");
        ev.AppendStrings["activityText"] = new List<string>() { text };
        ev.displayOverride = forced;
        scr_UpdateHandler.current.EventHandler.StartEvent(ev, false);
    }

    /// <summary>
    /// A booking was cancelled before it ended (Character_Factions.NotifyBookingCancelled): a solo one announces itself;
    /// a session booking is only that member's answer (the session announces itself if it is cancelled).
    /// </summary>
    public static void OnBookingCancelled(Character_Factions f, RecreationBooking b, CancelReason reason)
    {
        if (b == null) return;
        // a booking that may not be cancelled on request should not be cancelled by these either - content to fix
        if (b.forbidCancel && (reason == CancelReason.WorkForbidden || reason == CancelReason.ByEvent || reason == CancelReason.Displaced))
            Debug.LogError($"forbidCancel recreation booking of {f?.Owner?.FullName} cancelled ({reason}): {b.sourceKey} @ {b.factionID} day {b.day} {b.startHour:00}:00+{b.hours}h");
        AnnounceSolo(b, ActivityNoticeKind.Cancelled, reason);
    }

    /// <summary>"activity @ venue, when HH:00-HH:00" of session g.</summary>
    static string DescribeSessionLine(RecreationGroup g)
    {
        var venue = scr_System_CampaignManager.current.FindFactionByID(g.factionID);
        return LocalizeDictionary.QueryThenParse("ui_recreation_notice_line")
            .Replace("$activity$", g.DisplayName).Replace("$venue$", venue != null ? venue.FactionDisplayName : g.factionID)
            .Replace("$time$", DescribeTime(g.day, g.startHour, g.AbsEnd));
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
        int endHour = absEnd % 24;
        string end = $"{endHour:00}:00";
        if (absEnd / 24 != day) end = LocalizeDictionary.QueryThenParse("ui_recreation_day_nextDay") + end;
        return LocalizeDictionary.QueryThenParse("ui_recreation_notice_time")
            .Replace("$when$", DescribeDay(day)).Replace("$start$", $"{startHour:00}:00").Replace("$end$", end);
    }

    /// <summary>"今天 19:00" - the start of a visit at absolute hour absStart, relative to today (the management report's activity list).</summary>
    public static string DescribeStart(int absStart)
    {
        return LocalizeDictionary.QueryThenParse("ui_recreation_notice_start")
            .Replace("$when$", DescribeDay(absStart / 24)).Replace("$start$", $"{absStart % 24:00}:00");
    }

    /// <summary>"昨天" / "今天" / "明天" / "后天" / "M月D日" for absolute day `day`.</summary>
    static string DescribeDay(int day)
    {
        switch (day - scr_System_Time.current.getAbsoluteDay())
        {
            case -1: return LocalizeDictionary.QueryThenParse("ui_recreation_day_yesterday");
            case 0: return LocalizeDictionary.QueryThenParse("ui_recreation_day_today");
            case 1: return LocalizeDictionary.QueryThenParse("ui_recreation_day_tomorrow");
            case 2: return LocalizeDictionary.QueryThenParse("ui_recreation_day_dayAfter");
            default:
                var date = scr_System_Time.current.getStartTime().Date.AddDays(day);
                return LocalizeDictionary.QueryThenParse("ui_recreation_day_date").Replace("$month$", date.Month.ToString()).Replace("$day$", date.Day.ToString());
        }
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
    /// planned last night - once more with the same-day-only ones (daysInAdvance 0). The sessions posted here are
    /// answered in the hour's late passes (OnLatePass) and resolve themselves after (ResolveSession).
    /// </summary>
    public static void DailyPlan(Character_Factions f)
    {
        if (f == null || scr_System_CampaignManager.current == null || scr_System_Time.current == null) return;
        // the night's solo activities announce themselves on screen for the player's own household
        bool household = FactionUtility.IsPlayerHousehold(f.Owner);
        BeginAnnounceScope(household);
        try
        {
            f.ClearInviteMemory();
            int today = scr_System_Time.current.getAbsoluteDay();
            scr_System_CampaignManager.current.RecreationBoard.PostCalendar();
            bool added = false;
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
        finally { EndAnnounceScope(household); }
    }

    static bool ShouldPlan(Character_Factions f)
    {
        var c = f?.Owner;
        if (c == null || c.isTemporaryActor || c.IsDormant) return false;
        if (c == scr_System_CampaignManager.current.Player) return false;
        if (f.Faction_Home is Manageable_WorkerPool || f.HomeFactions.Count == 0 || f.isPartyLocked) return false;
        return f.HomeFactions[0].AllowWorkFaction(c);
    }

    // Booking re-validation is change-driven, never polled: the character's own faction / forbid-work changes
    // (RevalidateBookings), its schedule rebuilds (CancelClaimedBookings), and the venue's member / MemberType / furniture
    // changes (RevalidateVenueActivities). Venue due processing owns coordinator creation.

    /// <summary>
    /// f's factions or forbid-work state changed (Character_Factions.UpdateFactionPriorityList, Manageable.SetAllowWork):
    /// every booking not ended is cancelled when the priority home now forbids work (WorkForbidden) or its access is gone
    /// (LostAccess). The rare change re-validates; nothing checks hourly.
    /// </summary>
    public static void RevalidateBookings(Character_Factions f)
    {
        var c = f?.Owner;
        if (c == null || f.RecreationBookings.Count == 0 || scr_System_Time.current == null) return;
        int nowAbs = NowAbs();
        var priorityHome = f.HomeFactions.Count > 0 ? f.HomeFactions[0] : null;
        bool workForbidden = priorityHome != null && !priorityHome.AllowWorkFaction(c);
        bool changed = false;
        var snapshot = RentBookingList(f.RecreationBookings);
        try
        {
            foreach (var b in snapshot)
            {
                if (b == null || b.AbsEnd <= nowAbs || !f.HasBooking(b)) continue;
                CancelReason reason;
                if (workForbidden) reason = CancelReason.WorkForbidden;
                else if (LostAccess(f, b)) reason = CancelReason.LostAccess;
                else continue;
                f.CancelBooking(b, false, reason);
                changed = true;
            }
        }
        finally { ReturnBookingList(snapshot); }
        if (changed) f.RefreshSchedule();
    }

    /// <summary>CancelClaimedBookings in progress: its own schedule refresh does not re-enter it.</summary>
    static bool cancellingClaimed;

    /// <summary>
    /// Character_Factions.UpdateSchedule (a full rebuild - its work / home schedule may have changed): a booking not
    /// started whose hours a work / home schedule now claims is cancelled (NoTime) - not moved; the planner books again
    /// on its next day update.
    /// </summary>
    public static void CancelClaimedBookings(Character_Factions f)
    {
        if (cancellingClaimed || f?.Owner == null || f.RecreationBookings.Count == 0 || scr_System_Time.current == null) return;
        int today = scr_System_Time.current.getAbsoluteDay();
        int nowAbs = NowAbs();
        bool changed = false;
        cancellingClaimed = true;
        var snapshot = RentBookingList(f.RecreationBookings);
        try
        {
            foreach (var b in snapshot)
            {
                if (b == null || b.AbsStart <= nowAbs || !f.HasBooking(b) || !ClaimedByOwnSchedule(f, b, today)) continue;
                f.CancelBooking(b, false, CancelReason.NoTime);
                changed = true;
            }
            if (changed) f.RefreshSchedule();
        }
        finally
        {
            ReturnBookingList(snapshot);
            cancellingClaimed = false;
        }
    }

    /// <summary>Reusable booking lists for the reconciliation passes (a pool, so a nested pass gets its own).</summary>
    static readonly Stack<List<RecreationBooking>> bookingListPool = new Stack<List<RecreationBooking>>();

    /// <summary>A cleared pooled list - with copyFrom's items when given (a mutation-safe snapshot). Give it back with ReturnBookingList.</summary>
    static List<RecreationBooking> RentBookingList(IReadOnlyList<RecreationBooking> copyFrom = null)
    {
        var list = bookingListPool.Count > 0 ? bookingListPool.Pop() : new List<RecreationBooking>();
        if (copyFrom != null) for (int i = 0; i < copyFrom.Count; i++) list.Add(copyFrom[i]);
        return list;
    }

    static void ReturnBookingList(List<RecreationBooking> list)
    {
        list.Clear();
        bookingListPool.Push(list);
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
        if (string.IsNullOrEmpty(b.memberTypeID))
        {
            if (f.RecreationFactions.Contains(b.Faction)) return false;
            // a house / provided activity (no membership): access = the venue still offers it, and for the one who booked
            // it it is still their home; an invitee from outside the home (a hired maid, a friend - user 2026-11-01) is
            // only never searched for it (ActivitySources), not excluded
            bool invitee = b.isSessionBooking && b.roleID != HostRoleID;
            if (!invitee && !f.HomeFactions.Contains(b.Faction)) return true;
            return OfferedHouseActivity(b.Faction, b.activityID) == null;
        }
        return !OwnFactionOffers(f, b.Faction, b.memberTypeID);
    }

    /// <summary>
    /// The house activity activityID faction still offers its home members: in its HouseActivities (shared + its own), or
    /// provided by one of its members' types (a family doctor still assigned). Null when none.
    /// </summary>
    static RecreationActivity OfferedHouseActivity(Manageable faction, string activityID)
    {
        if (faction == null || string.IsNullOrEmpty(activityID)) return null;
        return faction.HouseActivities?.GetActivity(activityID) ?? ProvidedActivitiesAt(faction)?.GetActivity(activityID);
    }

    /// <summary>
    /// Booking b of f is a house / provided activity (no membership or offered type behind it): a Membership booking (the
    /// planner's) or Event booking (template house:...) with no memberTypeID whose activity the venue's houseActivities /
    /// members' types give. It acts under f's own MemberType.
    /// </summary>
    static bool IsHouseBooking(Character_Factions f, RecreationBooking b)
    {
        if (b == null || f == null || !string.IsNullOrEmpty(b.memberTypeID)) return false;
        if (b.source != RecreationBookingSource.Membership && b.source != RecreationBookingSource.Event) return false;
        if (string.IsNullOrEmpty(b.activityID)) return false;
        if (f.RecreationFactions.Contains(b.Faction)) return false;
        return HouseActivity(b.factionID, b.activityID) != null;
    }

    /// <summary>Any booked hour a work/home schedule claims (bookings never compete with those - see Character_Factions.CurrentJobScheduleFaction).</summary>
    static bool ClaimedByOwnSchedule(Character_Factions f, RecreationBooking b, int today)
    {
        if (b.overrideWork) return false;
        for (int t = b.AbsStart; t < b.AbsEnd; t++)
            if (f.CurrentJobScheduleFaction(t % 24, t / 24 - today, false) != null) return true;
        return false;
    }

    // ---------------- the visit's activity, and its money ---------------- //

    /// <summary>
    /// The activity booking b is a visit of (template data, looked up live - see RecreationBooking.GetActivity): an
    /// offer's (its sourceKey names an offer def - RecreationOfferDef.AsActivity), else activityID within the MemberType f
    /// acts under for b (ActingTypeID: b's memberTypeID, else f's membership at the venue), else the venue's house
    /// activity of that ID (MapPlan.houseActivities). Null when it is none of these (an event booking without a template,
    /// or templated on a MemberType's own workModule).
    /// </summary>
    public static RecreationActivity ResolveActivity(Character_Factions f, RecreationBooking b)
    {
        if (b == null) return null;
        var def = RecreationBoard.FindDef(b.sourceKey);
        if (def != null) return def.AsActivity;
        if (string.IsNullOrEmpty(b.activityID) || f == null) return null;
        var activity = FactionUtility.TryGetMemberType(ActingTypeID(f, b), out var type) ? type?.recreation?.GetActivity(b.activityID) : null;
        return activity ?? HouseActivity(b.factionID, b.activityID);
    }

    /// <summary>Length (hours) used for a planned expedition whose duration can't be computed (Manageable_Party.FinalDuration 999).</summary>
    const int UnknownExpeditionHours = 24;

    /// <summary>
    /// The departure windows of the party expedition f's character has planned (user 2026-10-30): their party
    /// (Character_Factions.CurrentParty) has an expedition set and active but not yet under way (Job_Expedition
    /// inactive / queued / gathering). It departs at its start hour (Job_Expedition.startHour, else the party's
    /// FinalStartHour) - retried daily - and lasts the party's FinalDuration: the window of every start from today on
    /// that is still ahead (or gathering right now) - today's and tomorrow's, as far as the planner looks. Venue = the
    /// party's ID (another place - the travel buffer applies).
    /// </summary>
    static List<Span> PlannedExpeditionWindows(Character_Factions f, int nowAbs)
    {
        var result = new List<Span>();
        var party = f?.CurrentParty;
        var job = party?.Job;
        if (party == null || job == null || !party.isPlayerFaction || job.Expedition == null || !job.ExpeditionActive) return result;
        if (job.status == Job_Expedition.ExpeditionStatus.active || job.status == Job_Expedition.ExpeditionStatus.resting
            || job.status == Job_Expedition.ExpeditionStatus.returning) return result;
        int startHour = job.startHour >= 0 ? job.startHour : party.FinalStartHour;
        int length = party.FinalDuration;
        if (length <= 0 || length >= 999) length = UnknownExpeditionHours;
        int today = scr_System_Time.current.getAbsoluteDay();
        for (int d = today; d <= today + 1; d++)
        {
            int s = RecreationBooking.AbsoluteHour(d, startHour);
            bool gatheringNow = job.status == Job_Expedition.ExpeditionStatus.gathering && s <= nowAbs && nowAbs < s + length;
            if (s >= nowAbs || gatheringNow) result.Add(new Span(s, s + length, party.ID));
        }
        return result;
    }

    /// <summary>
    /// The house activity activityID of faction factionID (MapPlan.houseActivities), else the provided activity of that ID
    /// (MemberType.providedActivities - RecreationUtility.ProvidedActivity), or null.
    /// </summary>
    public static RecreationActivity HouseActivity(string factionID, string activityID)
    {
        if (string.IsNullOrEmpty(factionID) || string.IsNullOrEmpty(activityID)) return null;
        return scr_System_CampaignManager.current?.FindFactionByID(factionID)?.HouseActivities?.GetActivity(activityID)
            ?? ProvidedActivity(activityID);
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
    /// Financial accounting: bills the hour that just ended once (RecreationBooking.lastBilledAbs) if a visit drove it
    /// (Character_Factions.GetBookingInEffectAtAbs - its booking may be over by now) and c is still at its venue (arrived
    /// during the hour, not yet left) - the attended-hour rule a shift's wage follows (Manageable.OnHourUpdate): the
    /// visit's activity (ResolveActivity) workModule.hourlyPayout - the venue owes c's payer a wage
    /// (TradeManager.GetOrCreateSalaryObligation - the module's paymentCadence and onPaidEventID / onFailedEventID,
    /// named after the activity), like a shift hour. workModule.hourlyCost is not used for visits; the entrance fee is
    /// charged at the activity's launch (ChargeEntranceFee) - here only for a booking without commands of its own,
    /// which never gets a Job_Activity.
    /// <br/>Payer = Character_Factions.GetWorkFactionSourceOrDefault (the priority home - like a recreation membership's fee).
    /// </summary>
    public static void BillVisitHour(Character_Factions f)
    {
        if (f?.Owner != null) BillVisitHour(f, f.Owner);
    }

    public static void BillVisitHour(Character_Factions f, Character_Trainable c)
    {
        if (f == null || c == null || scr_System_Time.current == null) return;
        int hourAbs = NowAbs() - 1;
        var b = f.GetBookingInEffectAtAbs(hourAbs);
        if (b == null || b.lastBilledAbs >= hourAbs) return;
        var venue = b.Faction;
        if (venue == null || (I_IsJobGiver)venue != f.CurrentLocaleFaction) return;
        b.lastBilledAbs = hourAbs;

        var activity = b.GetActivity(f);
        // a booking without commands of its own never gets a Job_Activity (TryFindScheduledJobNode) - its fee is charged here, on the first attended hour
        if (string.IsNullOrEmpty(activity?.reserveFurnitureTag) && !IsOvernight(activity)
            && (b.workModule?.workCommands == null || b.workModule.workCommands.Count == 0)) ChargeEntranceFee(c, venue, activity, b);

        var module = activity?.workModule;
        bool hasPayout = module?.hourlyPayout != null && module.hourlyPayout.Exists(i => i != null && i.itemCount > 0);
        if (!hasPayout) return;

        var payer = f.GetWorkFactionSourceOrDefault(venue.ID);
        if (payer == null || payer == venue) return;

        if (venue.TradeManager != null)
        {
            string sourceName = !string.IsNullOrEmpty(module.jobPostID) ? LocalizeDictionary.QueryThenParse(module.jobPostID) : venue.FactionDisplayName;
            var wage = venue.TradeManager.GetOrCreateSalaryObligation(payer, module.paymentCadence, sourceName, c.RefID);
            // holiday premium of the billed hour's day (it ended at midnight = yesterday's)
            float multiplier = module.PayoutMultiplierOnDay(venue, scr_System_Time.current.getCurrentTime().Hour == 0 ? -1 : 0);
            foreach (var pay in module.hourlyPayout) if (pay != null && pay.itemCount > 0) wage.AccrueHour(Manageable.ScalePayout(pay, multiplier), module.onPaidEventID, module.onFailedEventID);
        }
    }

    /// <summary>
    /// f's payer for visits at venue (GetWorkFactionSourceOrDefault) has an unpaid visit bill there (suspended
    /// Obligation_ActivityFee): no new visits of venue's activities or offers are booked until it is paid - visits
    /// already booked go ahead (the membership-fee rule, Character_Trainable.CanWorkFor).
    /// </summary>
    public static bool HasUnpaidActivityFee(Character_Factions f, Manageable venue)
    {
        if (venue == null) return false;
        var payer = f.GetWorkFactionSourceOrDefault(venue.ID);
        if (payer?.TradeManager == null) return false;
        foreach (var o in payer.TradeManager.Obligations)
            if (o is Obligation_ActivityFee fee && fee.targetFactionID == venue.ID && fee.IsSuspended) return true;
        return false;
    }

    // ---------------- occurrence placement policy ---------------- //

    sealed class PlannedActivityRoom { public Room_Instance room; }
    static readonly ConditionalWeakTable<I_ActivityBooking, PlannedActivityRoom> plannedActivityRooms = new ConditionalWeakTable<I_ActivityBooking, PlannedActivityRoom>();

    public static string ActivityTypeID(RecreationActivity activity)
    {
        return activity?.TypeID ?? "";
    }

    /// <summary>Definition conflicts are symmetric, independent of localized names and cooldown keys.</summary>
    public static bool ActivitiesConflict(RecreationActivity a, RecreationActivity b)
    {
        if (a == null || b == null) return false;
        string left = ActivityTypeID(a), right = ActivityTypeID(b);
        return (!string.IsNullOrEmpty(right) && a.conflictsWith?.Contains(right) == true)
            || (!string.IsNullOrEmpty(left) && b.conflictsWith?.Contains(left) == true)
            || (!string.IsNullOrEmpty(left) && left == right && (!a.allowMultipleInstances || !b.allowMultipleInstances));
    }

    /// <summary>Definition policy only. Runtime merging must also check the source overload for content, time, room and scope.</summary>
    public static bool CanMergeActivities(RecreationActivity a, RecreationActivity b)
    {
        if (a == null || b == null || !a.mergeWithSame || !b.mergeWithSame || IsOvernight(a) || IsOvernight(b)) return false;
        string type = ActivityTypeID(a);
        if (string.IsNullOrEmpty(type) || type != ActivityTypeID(b) || a.launchEventID != b.launchEventID) return false;
        if (a.conflictsWith?.Contains(type) == true || b.conflictsWith?.Contains(type) == true) return false;
        if ((a.facility?.hostPrivateRoom == true) != (b.facility?.hostPrivateRoom == true)) return false;
        return (a.requireMedia == null) == (b.requireMedia == null)
            && (a.requireMedia == null || (a.requireMedia.mediaTag == b.requireMedia.mediaTag
                && a.requireMedia.sessionCommandID == b.requireMedia.sessionCommandID));
    }

    /// <summary>Captured content compatibility; this never queries current inventory.</summary>
    public static bool CanMergeActivities(RecreationActivity a, RecreationActivity b, string mediaA, string mediaB)
    {
        return CanMergeActivities(a, b) && (a.requireMedia == null || (!string.IsNullOrEmpty(mediaA) && mediaA == mediaB));
    }

    static I_ActivityBooking OccurrenceSource(I_ActivityBooking source)
    {
        return source is RecreationBooking b && b.Session != null ? (I_ActivityBooking)b.Session : source;
    }

    static RecreationActivity OccurrenceActivity(I_ActivityBooking source)
    {
        return source is RecreationGroup g ? ResolveActivity(g) : (source as RecreationBooking)?.GetActivity((source as RecreationBooking)?.Owner?.FactionManager);
    }

    static int OccurrenceStart(I_ActivityBooking source) => source is RecreationGroup g ? g.AbsStart : ((RecreationBooking)source).AbsStart;
    static int OccurrenceEnd(I_ActivityBooking source) => source is RecreationGroup g ? g.AbsEnd : ((RecreationBooking)source).AbsEnd;
    static string OccurrenceVenue(I_ActivityBooking source) => source is RecreationGroup g ? g.factionID : ((RecreationBooking)source).factionID;
    static string OccurrenceMedia(I_ActivityBooking source) => source is RecreationGroup g ? g.mediaItemID : MediaItemOf((RecreationBooking)source);
    static Character_Trainable OccurrenceHost(I_ActivityBooking source) => source is RecreationGroup g ? g.Host : ((RecreationBooking)source).Owner;

    static bool MergeScopeAllows(RecreationActivity activity, Character_Trainable visitor, I_ActivityBooking source)
    {
        if (!(source is RecreationGroup g)) return activity?.invite?.IsMandatory != true
            && OccurrenceActivity(source)?.invite?.IsMandatory != true;
        var spec = ResolveSpec(g.specRef);
        if (spec.InviteeValidators?.Exists(v => v != null && v.required) == true) return false;
        if (visitor?.FactionManager == null) return false;
        var member = g.FindMember(visitor.RefID);
        if (member?.state == RecreationMemberState.Refused) return false;
        if (member != null) return true;
        return (g.visibility == RecreationVisibility.Faction || g.visibility == RecreationVisibility.World)
            && SeesList(visitor.FactionManager, g.scopeFactionID)
            && PickScopeRole(g, spec, visitor) != null;
    }

    public static bool CanMergeActivities(I_ActivityBooking left, I_ActivityBooking right)
    {
        left = OccurrenceSource(left);
        right = OccurrenceSource(right);
        if (left == null || right == null) return false;
        if (ReferenceEquals(left, right)) return true;
        if (OccurrenceVenue(left) != OccurrenceVenue(right) || OccurrenceStart(left) >= OccurrenceEnd(right)
            || OccurrenceStart(right) >= OccurrenceEnd(left)) return false;
        if (!CanMergeActivities(OccurrenceActivity(left), OccurrenceActivity(right), OccurrenceMedia(left), OccurrenceMedia(right))) return false;
        if (OccurrenceActivity(left)?.requireMedia != null
            && (OccurrenceStart(left) != OccurrenceStart(right) || OccurrenceEnd(left) != OccurrenceEnd(right))) return false;
        if (!MergeScopesCompatible(left, right)) return false;
        var venue = scr_System_CampaignManager.current?.FindFactionByID(OccurrenceVenue(left));
        var room = venue == null ? null : GetPlannedActivityRoom(left);
        return room != null && room == GetPlannedActivityRoom(right);
    }

    static bool MergeScopesCompatible(I_ActivityBooking left, I_ActivityBooking right)
    {
        if (left is RecreationGroup a && right is RecreationGroup b)
        {
            // Separate private invitations and required-role dependencies must never leak across sources.
            if (a.visibility != b.visibility || a.scopeFactionID != b.scopeFactionID
                || a.visibility == RecreationVisibility.Private || a.visibility == RecreationVisibility.Friends
                || ResolveSpec(a.specRef).IsMandatory || ResolveSpec(b.specRef).IsMandatory
                || ResolveSpec(a.specRef).InviteeValidators?.Exists(v => v != null && v.required) == true
                || ResolveSpec(b.specRef).InviteeValidators?.Exists(v => v != null && v.required) == true) return false;
            return true;
        }
        return MergeScopeAllows(OccurrenceActivity(left), OccurrenceHost(left), right)
            && MergeScopeAllows(OccurrenceActivity(right), OccurrenceHost(right), left);
    }

    static List<I_ActivityBooking> PlacementOccurrences(Manageable venue)
    {
        var result = new List<I_ActivityBooking>();
        var state = Lifecycle;
        if (state == null || !state.venues.TryGetValue(venue.ID, out var indexed)) return result;
        foreach (var occurrence in indexed.occurrences)
        {
            var g = occurrence.session;
            var b = occurrence.booking;
            if (g != null && g.IsOpen && g.AbsEnd > NowAbs()) result.Add(g);
            else if (b != null && !b.isSessionBooking && b.AbsEnd > NowAbs()
                && b.Owner?.FactionManager.HasBooking(b) == true) result.Add(b);
        }
        result.Sort((a, b) => OccurrenceStart(a).CompareTo(OccurrenceStart(b)));
        return result;
    }

    static List<Room_Instance> ActivityRooms(Manageable venue, RecreationActivity activity, Character_Trainable host)
    {
        if (activity?.facility != null) return FacilityRooms(venue, activity, host);
        if (!string.IsNullOrEmpty(activity?.reserveFurnitureTag))
        {
            var reservationRooms = new List<Room_Instance>();
            foreach (var j in ReservationFurniture(venue, activity))
                if (j.ParentRoom != null && !reservationRooms.Contains(j.ParentRoom)) reservationRooms.Add(j.ParentRoom);
            return reservationRooms;
        }
        var executionRooms = new List<Room_Instance>();
        if (activity?.workModule?.workCommands != null)
            foreach (var candidate in venue.ManagedRooms.Values)
            {
                if (candidate == null || candidate.isRoomPrison || (candidate.isRoomPrivate
                    && (host == null || venue.GetOwnedRooms(host)?.Contains(candidate.RefID) != true))) continue;
                if (RoomOffersAnyCommand(candidate, activity.workModule.workCommands)) executionRooms.Add(candidate);
            }
        if (executionRooms.Count > 0) return executionRooms;
        var room = Job_Activity.ResolveGatherRoom(venue, activity);
        return room == null ? new List<Room_Instance>() : new List<Room_Instance>() { room };
    }

    static bool RoomOffersAnyCommand(Room_Instance room, List<string> commands)
    {
        foreach (var furniture in room.Furnitures)
        {
            var job = furniture?.JobGiver;
            if (job == null) continue;
            foreach (var command in commands)
                if (job.allusableCOM_Contains(command)) return true;
        }
        return false;
    }

    static void RememberActivityRoom(I_ActivityBooking source, Room_Instance room)
    {
        if (source != null) plannedActivityRooms.GetValue(OccurrenceSource(source), _ => new PlannedActivityRoom()).room = room;
    }

    /// <summary>Logical execution room selected at planning; gathering can remain at a different entrance room.</summary>
    public static Room_Instance GetPlannedActivityRoom(I_ActivityBooking source)
    {
        source = OccurrenceSource(source);
        if (source == null) return null;
        var job = source is RecreationGroup g ? g.ActivityJob : ((RecreationBooking)source).ActivityJob;
        var activity = OccurrenceActivity(source);
        var venue = scr_System_CampaignManager.current?.FindFactionByID(OccurrenceVenue(source));
        if (venue == null) return null;
        var rooms = ActivityRooms(venue, activity, OccurrenceHost(source));
        if (job?.ParentRoom != null && rooms.Contains(job.ParentRoom)) return job.ParentRoom;
        if (plannedActivityRooms.TryGetValue(source, out var selected) && rooms.Contains(selected.room)) return selected.room;
        foreach (var room in rooms)
        {
            bool blocked = false;
            foreach (var other in PlacementOccurrences(venue))
            {
                if (ReferenceEquals(source, other) || OccurrenceStart(source) >= OccurrenceEnd(other) || OccurrenceStart(other) >= OccurrenceEnd(source)) continue;
                if (plannedActivityRooms.TryGetValue(other, out var assigned) && assigned.room == room
                    && ActivitiesConflict(activity, OccurrenceActivity(other))
                    && !(CanMergeActivities(activity, OccurrenceActivity(other), OccurrenceMedia(source), OccurrenceMedia(other))
                        && (activity.requireMedia == null || (OccurrenceStart(source) == OccurrenceStart(other) && OccurrenceEnd(source) == OccurrenceEnd(other)))
                        && MergeScopesCompatible(source, other))) { blocked = true; break; }
            }
            if (blocked) continue;
            RememberActivityRoom(source, room);
            return room;
        }
        return null;
    }

    public static bool CanPlaceActivity(RecreationActivity activity, Manageable venue, int start, int end,
        Character_Trainable host = null, RecreationGroup exceptSession = null, RecreationBooking exceptBooking = null)
    {
        return CanPlaceActivity(activity, venue, start, end, host, exceptSession, exceptBooking,
            exceptSession?.mediaItemID ?? exceptBooking?.mediaItemID);
    }

    static bool CanPlaceActivity(RecreationActivity activity, Manageable venue, int start, int end,
        Character_Trainable host, RecreationGroup exceptSession, RecreationBooking exceptBooking, string mediaItemID)
    {
        return TrySelectActivityRoom(activity, venue, start, end, host, exceptSession, exceptBooking, mediaItemID, out _);
    }

    public static bool TrySelectActivityRoom(RecreationActivity activity, Manageable venue, int start, int end,
        Character_Trainable host, RecreationGroup exceptSession, RecreationBooking exceptBooking, string mediaItemID, out Room_Instance room)
    {
        room = null;
        if (venue == null || end <= start) return false;
        var occurrences = PlacementOccurrences(venue);
        foreach (var source in occurrences) GetPlannedActivityRoom(source);
        var rooms = ActivityRooms(venue, activity, host);
        // No activity definition means an ordinary event visit, not an execution policy to enforce.
        if (activity == null) { room = rooms.Count > 0 ? rooms[0] : null; return true; }
        for (int pass = 0; pass < 2; pass++)
        foreach (var candidateRoom in rooms)
        {
            bool blocked = false, merge = exceptSession != null && candidateRoom == GetPlannedActivityRoom(exceptSession);
            foreach (var source in occurrences)
            {
                if (ReferenceEquals(source, exceptSession) || ReferenceEquals(source, exceptBooking)
                    || OccurrenceEnd(source) <= start || OccurrenceStart(source) >= end
                    || GetPlannedActivityRoom(source) != candidateRoom) continue;
                var other = OccurrenceActivity(source);
                bool compatible = CanMergeActivities(activity, other, mediaItemID ?? "", OccurrenceMedia(source))
                    && (activity.requireMedia == null || (start == OccurrenceStart(source) && end == OccurrenceEnd(source)))
                    && MergeScopeAllows(activity, host, source)
                    && (exceptSession == null || MergeScopesCompatible(exceptSession, source));
                if (compatible) merge = true;
                else if (ActivitiesConflict(activity, other)) { blocked = true; break; }
            }
            if (!blocked && (pass != 0 || merge)) { room = candidateRoom; return true; }
        }
        return false;
    }

    static I_ActivityBooking FindCompatibleOccurrence(Character_Factions f, RecreationBooking booking)
    {
        var activity = booking.GetActivity(f);
        if (activity?.mergeWithSame != true || booking.Faction == null) return null;
        foreach (var source in PlacementOccurrences(booking.Faction))
        {
            if (ReferenceEquals(source, booking) || OccurrenceEnd(source) <= booking.AbsStart || OccurrenceStart(source) >= booking.AbsEnd) continue;
            if (!CanMergeActivities(activity, OccurrenceActivity(source), booking.mediaItemID, OccurrenceMedia(source))
                || !MergeScopeAllows(activity, f.Owner, source)) continue;
            if (activity.requireMedia != null && (booking.AbsStart != OccurrenceStart(source) || booking.AbsEnd != OccurrenceEnd(source))) continue;
            if (source is RecreationGroup g && (booking.AbsStart < g.AbsStart || booking.AbsEnd > g.AbsEnd
                || !BookingCheck.For(g).Passes(f.Owner, out _))) continue;
            var session = source as RecreationGroup;
            if (TrySelectActivityRoom(activity, booking.Faction, booking.AbsStart, booking.AbsEnd, session?.Host ?? f.Owner,
                session, booking, booking.mediaItemID, out var room) && room == GetPlannedActivityRoom(source)) return source;
        }
        return null;
    }

    /// <summary>AddPlannedBookings hook: consumes an eligible shared occurrence without changing the visitor's booked hours.</summary>
    public static bool TryJoinPlannedBooking(Character_Factions f, RecreationBooking booking)
    {
        if (f?.Owner == null || booking == null || booking.isSessionBooking) return false;
        if (!(FindCompatibleOccurrence(f, booking) is RecreationGroup g)) return false;
        var member = g.FindMember(f.Owner.RefID);
        string role = member?.roleID ?? PickScopeRole(g, ResolveSpec(g.specRef), f.Owner);
        if (role == null || !f.Owner.AcceptSession(g, MakeContext(g, f.Owner))) return false;
        booking.Session = g;
        booking.isSessionBooking = true;
        booking.roleID = role;
        if (!f.AddBooking(booking, false))
        {
            booking.Session = null;
            booking.isSessionBooking = false;
            booking.roleID = "";
            return false;
        }
        if (member == null)
        {
            member = new RecreationGroupMember() { charaRef = f.Owner.RefID, roleID = role, origin = RecreationMemberOrigin.Scope };
            g.members.Add(member);
        }
        member.considered = true;
        member.state = RecreationMemberState.Attending;
        member.reason = member.failedCheck = "";
        f.PushSession(g);
        // joining a session: it announces itself and wakes its members as it confirms (ResolveSession) - an already
        // confirmed one wakes this joiner now
        if (g.resolved && g.valid) WakeForSessions(f.Owner);
        RefreshValidity(g);
        NotifyActivityChanged(g);
        return true;
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
        /// <summary>The media item picked for a media activity (RecreationActivity.requireMedia), "" = none.</summary>
        public string mediaItemID = "";
        /// <summary>A length the visit must have (a media item's runtime in whole hours), 0 = the activity's own range.</summary>
        public int fixedHours = 0;
        public I_ActivityBooking existingSource;
    }

    /// <summary>
    /// What a day's placement must respect: work/home schedule hours, the character's other bookings, predicted nights,
    /// "not before next hour", and the daily cap. Built per character per planned day.
    /// </summary>
    class DayContext
    {
        public Character_Trainable c;
        public Character_Factions f;
        public int today, day, dayStart, dayEnd, nowAbs;
        /// <summary>Nights are no limit (an invitee may give up sleep for an invitation - HasTimeFor); otherwise no visit takes a predicted night.</summary>
        public readonly bool ignoreSleep;
        public List<Span> nights = new List<Span>();
        public List<Span> fixedBookings = new List<Span>();
        public int bookedToday;
        readonly Dictionary<int, Manageable> occupied = new Dictionary<int, Manageable>();
        /// <summary>Priority home (HomeFactions[0]) - whose meal hours a visit elsewhere must not take unless the venue feeds them (MealAllows).</summary>
        readonly Manageable home;
        /// <summary>Venue faction ID -> the venue (null if not found) when it can feed c (Manageable.CanOfferMealTo), else null.</summary>
        readonly Dictionary<string, Manageable> mealVenues = new Dictionary<string, Manageable>();
        /// <summary>Venue faction ID -> the venue (null if not found), for VenueOpen.</summary>
        readonly Dictionary<string, Manageable> venues = new Dictionary<string, Manageable>();
        /// <summary>Venue faction ID -> WakeUpBufferHours for it.</summary>
        readonly Dictionary<string, int> wakeUpBuffers = new Dictionary<string, int>();
        /// <summary>CanHost results of this day's planning, per spec and BookingCheck.Key (mandatory specs only).</summary>
        public readonly Dictionary<(RecreationInviteSpec, string), bool> canHost = new Dictionary<(RecreationInviteSpec, string), bool>();

        /// <param name="keepBooking">Which of f's bookings count as taken (null = all) - HasTimeFor leaves out the ones an invitation would clash with.</param>
        /// <param name="ignoreSleep">See DayContext.ignoreSleep.</param>
        public DayContext(Character_Factions f, int day, Func<RecreationBooking, bool> keepBooking = null, bool ignoreSleep = false)
        {
            this.f = f;
            c = f.Owner;
            today = scr_System_Time.current.getAbsoluteDay();
            this.day = day;
            this.ignoreSleep = ignoreSleep;
            dayStart = RecreationBooking.AbsoluteHour(day, 0);
            dayEnd = dayStart + 24;
            nowAbs = NowAbs();
            home = f.HomeFactions.Count > 0 ? f.HomeFactions[0] : null;

            var bookings = f.RecreationBookings;
            for (int i = 0; i < bookings.Count; i++) if (keepBooking == null || keepBooking(bookings[i])) AddFixed(bookings[i]);
            blocked.AddRange(PlannedExpeditionWindows(f, nowAbs));

            // the real night in progress / next, then predictions for the night ending this morning and the one starting
            // this evening, each anchored at the midday before it (as the hourly recompute would see it then - an
            // anchor too close to the wake target pushes it a full day out) - first found wins where they overlap
            if (f.TryGetPlannedSleep(out int ps, out int pe)) AddNight(ps, pe);
            AddPredictedNight(day - 1, 12);
            AddPredictedNight(day, 12);
        }

        /// <summary>
        /// Hours no visit may cover, apart from bookings: a planned party expedition's departure window
        /// (PlannedExpeditionWindows) - like a booking at another venue (travel buffer around it), but not counted against
        /// the daily cap or the night (the expedition decides its own rest).
        /// </summary>
        public List<Span> blocked = new List<Span>();

        /// <summary>b's hours are taken from now on (an existing booking, or an offer just picked for this day) - also counted against the daily cap on its day.</summary>
        public void AddFixed(RecreationBooking b)
        {
            if (b == null) return;
            bool stay = IsOvernight(b.GetActivity(f));
            fixedBookings.Add(new Span(b.AbsStart, b.AbsEnd, b.factionID, stay));
            if (b.day == day && !stay) bookedToday += b.hours;
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
        /// is one of its own meal hours (meals are only served there in them - TryFindMealNode, com_furniture_getmeal's requireFaction.requireMealHour).
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

        /// <summary>The venue (faction ID) is open at hour of day `hour`: within its active hours (Manageable.IsActiveHour - always, for one without them or not found).</summary>
        public bool VenueOpen(int hour, string venue)
        {
            if (venue == null) return true;
            if (!venues.TryGetValue(venue, out var v))
            {
                v = scr_System_CampaignManager.current.FindFactionByID(venue);
                venues[venue] = v;
            }
            return v == null || v.IsActiveHour(hour);
        }

        /// <summary>
        /// Awake hours kept before a visit at venue (faction ID) after a night's sleep: the world-map trip from the priority
        /// home (where the night is slept) to the venue plus WakeUpPrepMinutes, rounded up to whole hours - 1 for a venue
        /// with no world-map trip (the home itself, same place, or no route found).
        /// </summary>
        public int WakeUpBufferHours(string venue)
        {
            string key = venue ?? "";
            if (wakeUpBuffers.TryGetValue(key, out int hours)) return hours;
            hours = RecreationUtility.WakeUpBufferHours(home, venue);
            wakeUpBuffers[key] = hours;
            return hours;
        }

        /// <summary>
        /// Whether [s, e) at venue (faction ID) can be booked for activity (null = no hour limit, e.g. an offer) next to the
        /// chain's own spans: starts on this day and after now, free of schedules and other bookings, allowed hours, the
        /// venue open (VenueOpen), daily
        /// cap, home meal hours only where the venue feeds c (MealAllows), TravelBufferHours free to anything nearby at
        /// another place, and - unless ignoreSleep - WakeUpBufferHours awake after a night and no predicted night taken.
        /// </summary>
        public bool CanPlace(int s, int e, RecreationActivity activity, List<Span> chain, string venue,
            RecreationGroup exceptSession = null, RecreationBooking exceptBooking = null, string mediaItemID = null)
        {
            if (e <= s || s < dayStart || s >= dayEnd || s <= nowAbs) return false;
            bool stay = IsOvernight(activity);
            var venueFaction = scr_System_CampaignManager.current.FindFactionByID(venue);
            if (!HasReservationSpace(venueFaction, activity, s, e, exceptSession, exceptBooking)) return false;
            if (!CanPlaceActivity(activity, venueFaction, s, e, exceptSession?.Host ?? c, exceptSession, exceptBooking, mediaItemID)) return false;
            int chainHours = 0;
            foreach (var span in chain) chainHours += span.Length;
            if (!stay && bookedToday + chainHours + (e - s) > MaxDailyHours) return false;

            for (int t = s; t < e; t++)
            {
                if (Occupied(t)) return false;
                if (activity != null && !activity.AllowsHour(((t % 24) + 24) % 24)) return false;
                if (!stay && !VenueOpen(((t % 24) + 24) % 24, venue)) return false;
                if (!stay && !MealAllows(((t % 24) + 24) % 24, venue)) return false;
                foreach (var span in fixedBookings) if (span.Contains(t)) return false;
                foreach (var span in chain) if (span.Contains(t)) return false;
                foreach (var span in blocked) if (span.Contains(t)) return false;
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
            foreach (var span in blocked) if (TooClose(span)) return false;

            if (!stay && !ignoreSleep && nights.Count > 0)
            {
                // WakeUpBufferHours: none of the hours just before the visit may be sleep (a night ending at s would have
                // c start it straight out of bed)
                int wakeUpBuffer = WakeUpBufferHours(venue);
                foreach (var n in nights) if (n.start < s && s <= n.end + wakeUpBuffer - 1) return false;
                // a visit planned by c themselves never takes their sleep
                foreach (var n in nights) if (n.start < e && s < n.end) return false;
            }
            return true;
        }
    }

    /// <summary>
    /// Plans f's flexible visits on absolute day `day`: one chain from the first anchor it fits at, placed around f's
    /// existing bookings; each then joins an existing session, books solo or hosts a session (AddPlannedBookings). Fixed
    /// offers are ranked on committed changes, moving these visits if needed. True if anything was booked (the caller refreshes
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
                if (activity == null || IsOvernight(activity) || string.IsNullOrEmpty(activity.ID) || activity.daysInAdvance > maxAdvance) continue;
                string key = ActivitySourceKey(source.faction, activity);
                if (!ActivityAllows(ctx.f, ctx.day, key, activity, source.faction)) continue;
                float weight = activity.baseWeight * WeeklyFactor(ctx, key, activity) * TravelFactor(home, source.faction) * PersonalityFactor(ctx.c, source.faction, activity);
                if (weight <= 0f) continue;
                var cand = new Candidate() { faction = source.faction, activity = activity, sourceKey = key, memberTypeID = source.memberTypeID, weight = weight };
                result.Add(cand);
            }
        }

        foreach (var def in FlexibleOffers())
        {
            var activity = def.AsActivity;
            if (IsOvernight(activity)) continue;
            if (activity.daysInAdvance > maxAdvance) continue;
            var venue = scr_System_CampaignManager.current.FindFactionByID(def.factionID);
            if (!OfferAllows(ctx.f, ctx.day, def) || !activity.AllowsDay(ctx.day - ctx.today, venue)) continue;
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
        if (faction == null || string.IsNullOrEmpty(memberTypeID)) return false;
        // OwnFactions(f) contains faction: the priority home or a work faction
        if (!(f.HomeFactions.Count > 0 && f.HomeFactions[0] == faction) && !f.WorkFactions.Contains(faction)) return false;
        return faction.GetOfferedRecreationTypes().Exists(t => t.ID == memberTypeID);
    }

    /// <summary>Days f has a visit for key: attended (remembered window) or booked.</summary>
    static HashSet<int> VisitDays(Character_Factions f, string key)
    {
        var days = new HashSet<int>(f.GetAttendedDays(key));
        var bookings = f.RecreationBookings;
        for (int i = 0; i < bookings.Count; i++) if (bookings[i] != null && bookings[i].sourceKey == key) days.Add(bookings[i].day);
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
    /// types f's own factions offer (OwnFactions), then the house activities of each of f's home factions (permanent and
    /// temporary - MapPlan.houseActivities; acting under f's own MemberType there, memberTypeID ""). A faction whose fee f
    /// hasn't paid gives none (CanWorkFor), nor one with an unpaid visit bill (HasUnpaidActivityFee).
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
        foreach (var faction in f.HomeFactions)
        {
            if (faction == null || !c.CanWorkFor(faction) || HasUnpaidActivityFee(f, faction)) continue;
            var spec = faction.HouseActivities;
            if (spec != null) yield return new ActivitySource() { faction = faction, spec = spec, memberTypeID = "" };
            // what the home's members' types provide (a family doctor's checkup) - the provider is the activity's required invite role
            var provided = ProvidedActivitiesAt(faction);
            if (provided != null) yield return new ActivitySource() { faction = faction, spec = provided, memberTypeID = "" };
        }
    }

    /// <summary>
    /// The rules activity (key = faction ID + ":" + activity ID) must pass for f on absolute day `day` - no weights: the
    /// day allowed (activeDays), its cooldown clear, and no other reservation of it pending (HasPendingReservation;
    /// except = the session being checked, which doesn't count against itself).
    /// </summary>
    static bool ActivityAllows(Character_Factions f, int day, string key, RecreationActivity activity, Manageable venue, RecreationGroup except = null)
    {
        int ahead = day - scr_System_Time.current.getAbsoluteDay();
        // the provider of a provided activity (a family doctor) serves many - their clients' cooldown / one-at-a-time rules don't bind them
        bool provider = ProvidesActivity(f.Owner, activity);
        return activity.AllowsDay(ahead, venue) && activity.AllowsAdvance(ahead) && (provider || CooldownAllows(f, day, key, activity.minDaysBetween))
            && IsEligible(f.Owner, activity) && FacilityAvailable(venue, activity)
            && (provider || !HasPendingReservation(f, key, except))
            && (except == null || ReservationRefusal(f, PreviewBooking(except)) == "");
    }

    /// <summary>c passes activity's eligibility (its chara_conditions, c as self); no eligibility = anyone.</summary>
    public static bool IsEligible(Character_Trainable c, RecreationActivity activity)
    {
        if (c == null) return false;
        if (activity?.eligibility == null) return true;
        return EventUtility.isCharaValid(activity.eligibility, ConditionContext(c), c);
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
        if (!IsEligible(f.Owner, def.AsActivity) || !FacilityAvailable(venue, def.AsActivity)) return false;
        return def.IsEligible(f.Owner) && (except == null || ReservationRefusal(f, PreviewBooking(except)) == "");
    }

    /// <summary>
    /// One reservation per activity: f already holds a booking of key (an activity's / offer's sourceKey) that hasn't
    /// ended, or hosts an open session of it not yet ended (posted, not booked yet) - except's own booking / except itself
    /// not counted. A new one of the same activity waits until that one is over.
    /// </summary>
    static bool HasPendingReservation(Character_Factions f, string key, RecreationGroup except)
    {
        int nowAbs = NowAbs();
        var bookings = f.RecreationBookings;
        for (int i = 0; i < bookings.Count; i++)
        {
            var b = bookings[i];
            if (b != null && b.sourceKey == key && b.AbsEnd > nowAbs && (except == null || b.Session != except)) return true;
        }
        var inbox = f.SessionInbox;
        for (int i = 0; i < inbox.Count; i++)
        {
            var g = inbox[i];
            if (g != null && g != except && g.IsOpen && g.AbsEnd > nowAbs && g.sourceKey == key && g.hostRef == f.Owner.RefID) return true;
        }
        return false;
    }

    /// <summary>Below the weekly target: more likely the further below; at or above it: rapidly less likely.</summary>
    static float WeeklyFactor(DayContext ctx, string key, RecreationActivity activity)
    {
        if (activity.weeklyTarget <= 0) return 1f;
        int count = 0;
        foreach (int d in VisitDays(ctx.f, key)) if (d >= ctx.day - 6 && d < ctx.day) count++;
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

    /// <summary>from root|to root -> TravelMinutes, for absolute day travelMinutesCacheDay only (rebuilt each day).</summary>
    static readonly Dictionary<string, float> travelMinutesCache = new Dictionary<string, float>();
    static int travelMinutesCacheDay = int.MinValue;

    /// <summary>World-map travel minutes between the two factions' roots - 0 for the same place, a missing one, or no route.</summary>
    public static float TravelMinutes(Manageable from, Manageable to)
    {
        var a = from?.FactionOwnerRoot;
        var b = to?.FactionOwnerRoot;
        if (a == null || b == null || a == b) return 0f;

        int today = scr_System_Time.current.getAbsoluteDay();
        if (travelMinutesCacheDay != today) { travelMinutesCache.Clear(); travelMinutesCacheDay = today; }

        string key = a.ID + "|" + b.ID;
        if (!travelMinutesCache.TryGetValue(key, out float minutes))
        {
            if (!scr_System_CampaignManager.current.Map.TryGetWorldMapTravelMinutes(a, b, out minutes, out _)) minutes = 0f;
            travelMinutesCache[key] = minutes;
        }
        return minutes;
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
        var weights = new List<float>(pool.Count);

        bool Place(Candidate candidate, bool existingOnly, out Span span)
        {
            return chain.Count == 0
                ? PlaceNear(ctx, candidate, chain, anchor, forward, existingOnly, out span)
                : TryExtend(ctx, candidate, chain, chainStart, chainEnd, forward, existingOnly, out span);
        }

        while (pool.Count > 0)
        {
            if (chain.Count > 0 && !Utility.RandomChance(ExtendChainChance)) break;
            weights.Clear();
            foreach (var x in pool) weights.Add(x.weight);
            int pick = WeightedIndex(weights);
            var cand = pool[pick];
            pool.RemoveAt(pick);

            Span span = default;
            bool placed = false;
            foreach (var source in PlacementOccurrences(cand.faction))
            {
                if (!CanMergeActivities(cand.activity, OccurrenceActivity(source)) || !MergeScopeAllows(cand.activity, ctx.c, source)) continue;
                string media = OccurrenceMedia(source);
                if (cand.activity.requireMedia != null && !MediaMatches(media, cand.activity.requireMedia)) continue;
                cand.existingSource = source;
                cand.mediaItemID = media ?? "";
                cand.fixedHours = cand.activity.requireMedia == null ? 0 : MediaHours(media);
                if (Place(cand, true, out span)) { placed = true; break; }
            }
            if (!placed)
            {
                cand.existingSource = null;
                cand.mediaItemID = "";
                cand.fixedHours = 0;
                // Only creating a new occurrence needs a host's own room, required invitees and inventory detection.
                if (cand.activity.facility?.hostPrivateRoom == true && FacilityRooms(cand.faction, cand.activity, ctx.c).Count == 0) continue;
                var check = cand.source == RecreationBookingSource.Offer
                    ? BookingCheck.ForOffer(RecreationBoard.FindDef(cand.sourceKey), ctx.day)
                    : BookingCheck.ForActivity(cand.faction.ID, cand.activity.ID, ctx.day);
                if (!CanHost(ctx, cand.activity.invite, check)) continue;
                if (cand.activity.requireMedia != null)
                {
                    cand.mediaItemID = PickMedia(cand.faction, cand.activity);
                    if (string.IsNullOrEmpty(cand.mediaItemID)) continue;
                    cand.fixedHours = MediaHours(cand.mediaItemID);
                }
                placed = Place(cand, false, out span);
            }
            if (!placed) continue;
            var booking = RecreationBooking.Create(span.start / 24, span.start % 24, span.Length, cand.faction.ID, cand.activity.workModule,
                cand.source, cand.sourceKey, cand.memberTypeID, activityID: cand.source == RecreationBookingSource.Membership ? cand.activity.ID : "",
                forbidCancel: cand.activity.forbidCancel, flexible: true);
            if (booking == null) continue;
            booking.mediaItemID = cand.mediaItemID ?? "";
            var existingSession = cand.existingSource as RecreationGroup;
            if (!TrySelectActivityRoom(cand.activity, cand.faction, span.start, span.end, existingSession?.Host ?? ctx.c, existingSession, booking,
                booking.mediaItemID, out var room)) continue;
            RememberActivityRoom(booking, room);

            result.Add(booking);
            chainStart = chain.Count == 0 ? span.start : Math.Min(chainStart, span.start);
            chainEnd = chain.Count == 0 ? span.end : Math.Max(chainEnd, span.end);
            chain.Add(span);
        }
        return result;
    }

    /// <summary>The candidate's lengths to try: its fixed length (a media runtime) only, else the activity's (LengthsToTry(activity)).</summary>
    static List<int> LengthsToTry(Candidate cand)
    {
        if (cand.fixedHours > 0) return new List<int>() { cand.fixedHours };
        return LengthsToTry(cand.activity);
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
    static bool PlaceNear(DayContext ctx, Candidate cand, List<Span> chain, int anchor, bool forward, bool existingOnly, out Span span)
    {
        var lengths = LengthsToTry(cand);
        for (int n = 0; n < 24; n++)
        {
            foreach (int offset in n == 0 ? new[] { 0 } : (forward ? new[] { n, -n } : new[] { -n, n }))
            {
                int pivot = anchor + offset;
                foreach (int l in lengths)
                {
                    span = forward ? new Span(pivot, pivot + l, cand.faction.ID) : new Span(pivot - l, pivot, cand.faction.ID);
                    if (CandidateFits(ctx, cand, chain, span, existingOnly)) return true;
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
    static bool TryExtend(DayContext ctx, Candidate cand, List<Span> chain, int chainStart, int chainEnd, bool forward, bool existingOnly, out Span span)
    {
        var lengths = LengthsToTry(cand);
        foreach (bool side in new[] { forward, !forward })
        {
            foreach (int l in lengths)
            {
                foreach (int gap in new[] { 0, TravelBufferHours })
                {
                    span = side ? new Span(chainEnd + gap, chainEnd + gap + l, cand.faction.ID) : new Span(chainStart - gap - l, chainStart - gap, cand.faction.ID);
                    if (CandidateFits(ctx, cand, chain, span, existingOnly)) return true;
                }
            }
        }
        span = default;
        return false;
    }

    static bool CandidateFits(DayContext ctx, Candidate candidate, List<Span> chain, Span span, bool existingOnly)
    {
        var source = existingOnly ? candidate.existingSource : null;
        var session = source as RecreationGroup;
        if (existingOnly)
        {
            if (source == null || OccurrenceEnd(source) <= span.start || OccurrenceStart(source) >= span.end) return false;
            if (candidate.activity.requireMedia != null && (span.start != OccurrenceStart(source) || span.end != OccurrenceEnd(source))) return false;
            if (session != null && (span.start < session.AbsStart || span.end > session.AbsEnd
                || !BookingCheck.For(session).Passes(ctx.c, out _))) return false;
        }
        return ctx.CanPlace(span.start, span.end, candidate.activity, chain, candidate.faction.ID,
            exceptSession: session, mediaItemID: candidate.mediaItemID);
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
    /// triggered reconciliation - or a session needs its time). Re-packs that day's movable bookings (flexible visits not frozen yet -
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

        var group = new List<RecreationBooking>();
        var bookings = f.RecreationBookings;
        for (int i = 0; i < bookings.Count; i++) if (Movable(bookings[i]) && bookings[i].day == broken.day) group.Add(bookings[i]);
        SortStable(group, (a, b) => a.AbsStart.CompareTo(b.AbsStart));
        if (!group.Contains(broken)) group.Add(broken);
        int origin = int.MaxValue;
        foreach (var b in group) origin = Math.Min(origin, b.AbsStart);
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
                if (moved != null) moved.mediaItemID = old.mediaItemID;
                if (moved != null && f.AddBooking(moved, false))
                {
                    kept.Add(old);
                    if (moved != old) AnnounceSolo(moved, ActivityNoticeKind.Rescheduled, from: old);
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
                if (ctx.CanPlace(cursor + gap, cursor + gap + b.hours, activity, chain, b.factionID, mediaItemID: MediaItemOf(b))) { start = cursor + gap; break; }
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
    /// hostless World session goes on the venue's world list and committed changes trigger eligible characters' ranking.
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
        foreach (var b in new List<RecreationBooking>(f.RecreationBookings))
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
                var candNames = new List<string>();
                foreach (var x in cands) candNames.Add($"{x.sourceKey} (advance {x.activity.daysInAdvance})");
                result.Add($"day {day}{(day == today ? " (today)" : "")}: " + (cands.Count == 0 ? "none" : string.Join(", ", candNames)));
            }
        }
        DailyPlan(f);
        f.RefreshSchedule();
        if (ShouldPlan(f)) SessionPass(f);

        string Members(RecreationGroup g)
        {
            var parts = new List<string>();
            foreach (var m in g.members)
                parts.Add($"{m.roleID}:{m.Chara?.FullName ?? m.charaRef.ToString()}:{m.origin}:{m.state}{(string.IsNullOrEmpty(m.reason) ? "" : "(" + m.reason + ")")}"
                    + $"{(string.IsNullOrEmpty(m.failedCheck) ? "" : "!" + m.failedCheck)}{(m.arrived ? "+" : "")}");
            return string.Join(", ", parts);
        }

        result.Add("-- bookings --");
        var sorted = new List<RecreationBooking>();
        foreach (var x in f.RecreationBookings) if (x != null) sorted.Add(x);
        SortStable(sorted, (x, y) => x.AbsStart.CompareTo(y.AbsStart));
        foreach (var b in sorted)
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
            var ext = new List<string>();
            foreach (var e in g.extensions)
            {
                if (e.extenderRef != c.RefID) continue;
                var picks = new List<string>();
                foreach (var r in e.picks) picks.Add(scr_System_CampaignManager.current.FindInstanceByID(r)?.FullName ?? r.ToString());
                ext.Add($"{e.roleID}{(e.inviteRequired ? " required" : "")} -> " + string.Join(",", picks));
            }
            if (ext.Count > 0) line += " | extended: " + string.Join("; ", ext);
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
        foreach (var faction in f.HomeFactions)
        {
            if (faction?.HouseActivities?.activities == null || !c.CanWorkFor(faction)) continue;
            foreach (var activity in faction.HouseActivities.activities)
                if (activity != null && !string.IsNullOrEmpty(activity.ID))
                    result.Add($"house:{activity.ID} @ {faction.ID} (home activity{(IsEligible(c, activity) ? "" : ", not eligible")}{(FacilityAvailable(faction, activity) ? "" : ", no facility")})");
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
    /// still rolled for an optional spec); a session that is cancelled leaves the booking going on alone. The activity
    /// announces itself (AnnounceSolo, or the session as it resolves), displayOverride = the calling event's own. startHour NextHourStart
    /// (event argument "next") = the next full hour. The session's invitees answer at once (ConfirmInvitedNow), before
    /// publishing the coherent occurrence. notify:false defers publication to a caller-owned batch. invitee: invited into the first
    /// role they qualify for instead of that role's search (e.g. the doctor the player made the appointment with).
    /// </summary>
    /// <summary>SetEventBooking startHour: the next full hour (event argument "next").</summary>
    public const int NextHourStart = -2;

    public static bool SetEventBooking(Character_Trainable c, string factionID, int dayOffset, int startHour, int hours,
        string templateID, bool wakeForIt, bool overrideWork, string onCancelledEventID, bool displayOverride = false,
        bool forbidCancel = false, string sourceEventID = "", bool solo = false, bool hostAsPlayer = false, Character_Trainable invitee = null, bool notify = true)
    {
        var f = c?.FactionManager;
        if (f == null) return false;
        BeginAnnounceScope(displayOverride);
        try
        {
            return SetEventBookingInner(c, f, factionID, dayOffset, startHour, hours, templateID, wakeForIt, overrideWork,
                onCancelledEventID, forbidCancel, sourceEventID, solo, hostAsPlayer, invitee, notify);
        }
        finally { EndAnnounceScope(displayOverride); }
    }

    static bool SetEventBookingInner(Character_Trainable c, Character_Factions f, string factionID, int dayOffset, int startHour, int hours,
        string templateID, bool wakeForIt, bool overrideWork, string onCancelledEventID, bool forbidCancel, string sourceEventID, bool solo, bool hostAsPlayer,
        Character_Trainable invitee, bool notify = true)
    {
        if (factionID == "@home") factionID = f.HomeFactions.Count > 0 && f.HomeFactions[0] != null ? f.HomeFactions[0].ID : "";
        if (scr_System_CampaignManager.current.FindFactionByID(factionID) == null) return false;

        MapPlan.WorkModuleInit module = null;
        string memberTypeID = "";
        string activityID = "";
        string sourceKey = string.IsNullOrEmpty(templateID) ? factionID : templateID;
        var def = RecreationBoard.FindDef(templateID);
        if (def != null) { module = def.workModule; memberTypeID = def.memberTypeID; forbidCancel |= def.forbidCancel; }
        else if (templateID != null && templateID.StartsWith("house:"))
        {
            // "house:<activityID>" - a house activity of the venue (MapPlan.houseActivities) or a provided one
            // (MemberType.providedActivities): acting under the booker's own type there, its cooldown key shared with the planner's
            var house = HouseActivity(factionID, templateID.Substring(6));
            if (house == null) return false;
            module = house.workModule;
            activityID = house.ID;
            forbidCancel |= house.forbidCancel;
            sourceKey = ActivitySourceKey(scr_System_CampaignManager.current.FindFactionByID(factionID), house);
        }
        else if (!string.IsNullOrEmpty(templateID))
        {
            int split = templateID.IndexOf(':');
            string typeID = split < 0 ? templateID : templateID.Substring(0, split);
            if (FactionUtility.TryGetMemberType(typeID, out var type) && type != null)
            {
                var activities = type.recreation?.activities;
                var activity = split < 0 ? activities?.Find(a => a != null) : type.recreation?.GetActivity(templateID.Substring(split + 1));
                module = activity != null ? activity.workModule : type.workModule;
                activityID = activity != null ? activity.ID : "";
                memberTypeID = type.ID;
                if (activity != null) forbidCancel |= activity.forbidCancel;
            }
        }

        if (startHour == NextHourStart)
        {
            // the next full hour - tomorrow 0:00 after 23:xx
            startHour = scr_System_Time.current.getCurrentTime().Hour + 1;
            dayOffset = startHour >= 24 ? 1 : 0;
            startHour %= 24;
        }
        else if (startHour < 0) { startHour = scr_System_Time.current.getCurrentTime().Hour; dayOffset = 0; }
        int day = scr_System_Time.current.getAbsoluteDay() + dayOffset;
        var booking = RecreationBooking.Create(day, startHour, hours, factionID, module, RecreationBookingSource.Event,
            sourceKey, memberTypeID, true, onCancelledEventID, overrideWork, activityID,
            forbidCancel, sourceEventID);
        if (booking == null) return false;
        if (def != null && def.factionID != factionID) return false;
        var bookedActivity = booking.GetActivity(f);
        if (bookedActivity?.requireMedia != null)
        {
            booking.mediaItemID = PickMedia(booking.Faction, bookedActivity);
            if (string.IsNullOrEmpty(booking.mediaItemID)) return false;
        }
        if (!TrySelectActivityRoom(bookedActivity, booking.Faction, booking.AbsStart, booking.AbsEnd, c,
            null, booking, booking.mediaItemID, out var plannedRoom)) return false;
        if (ReservationRefusal(f, booking) != "") return false;

        var inTheWay = new List<RecreationBooking>();
        var bookings = f.RecreationBookings;
        for (int i = 0; i < bookings.Count; i++) if (bookings[i] != null && bookings[i].Overlaps(booking.AbsStart, booking.AbsEnd)) inTheWay.Add(bookings[i]);
        if (inTheWay.Exists(b => b.locked)) return false;
        foreach (var b in inTheWay) f.RemoveBooking(b);
        if (!f.AddBooking(booking, false))
        {
            foreach (var b in inTheWay) f.AddBooking(b, false);   // put back as it was
            return false;
        }
        RememberActivityRoom(booking, plannedRoom);
        foreach (var b in inTheWay) TryReschedule(f, b, CancelReason.Displaced);
        if (wakeForIt) CutSleepFor(c, booking);
        // the player never hosts (not in scope yet) - their event bookings stay solo; except with hostAsPlayer (the
        // "host" flag - e.g. an appointment the player makes with a family doctor: the activity's invite roles find the provider)
        if (!solo && (c != Player || hostAsPlayer))
        {
            var spec = ResolveSpec(SpecRefFor(f, booking));
            var vis = EffectiveVisibility(spec, booking);
            if (vis != RecreationVisibility.None && !spec.solo && (spec.IsMandatory || Utility.RandomChance(spec.inviteChance))
                && TryHostSession(f, booking, spec, vis, true, invitee) && booking.Session != null)
                ConfirmInvitedNow(booking.Session);
        }
        // not hosted: a solo activity, complete once booked (a hosted one announces itself as it resolves)
        if (booking.Session == null) AnnounceSolo(booking, ActivityNoticeKind.Confirmed);
        f.RefreshSchedule();
        if (booking.Session != null) NotifyActivityChanged(booking.Session);
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

    // ---------------- waking for sessions ---------------- //

    /// <summary>How far ahead (minutes) a session booking can make a sleep end early - beyond any realistic wake-up buffer.</summary>
    const int SessionWakeLookaheadMinutes = 24 * 60;

    /// <summary>
    /// Minutes from now until c has to be up for their next session booking not yet started - of a session confirmed to
    /// take place and that c attends (IsSessionBookingLive): its start minus WakeUpBufferHours for the trip from where c
    /// is now (else their priority home). 0 or less = already within that window. int.MaxValue when there is none within
    /// SessionWakeLookaheadMinutes.
    /// </summary>
    public static int MinutesUntilSessionWake(Character_Trainable c)
    {
        var f = c?.FactionManager;
        if (f == null || f.RecreationBookings.Count == 0) return int.MaxValue;

        int nowMinutes = NowAbs() * 60 + scr_System_Time.current.getCurrentTime().Minute;
        Manageable from = null;
        int result = int.MaxValue;
        var bookings = f.RecreationBookings;
        for (int i = 0; i < bookings.Count; i++)
        {
            var b = bookings[i];
            if (b == null || !b.isSessionBooking || !IsSessionBookingLive(b, c)) continue;
            int untilStart = b.AbsStart * 60 - nowMinutes;
            if (untilStart <= 0 || untilStart > SessionWakeLookaheadMinutes) continue;
            if (from == null)
            {
                var locale = f.CurrentLocaleFaction;
                from = (locale == null ? null : scr_System_CampaignManager.current.FindFactionByID(locale.FactionID))
                    ?? (f.HomeFactions.Count > 0 ? f.HomeFactions[0] : null);
            }
            result = Math.Min(result, untilStart - WakeUpBufferHours(from, b.factionID) * 60);
        }
        return result;
    }

    /// <summary>
    /// A session c attends was confirmed (ResolveSession, or c joined one already confirmed): a sleep running past MinutesUntilSessionWake now ends there (at
    /// least a minute from now) - the cut counted as missed sleep, as CutSleepFor. A sleep started later is capped the
    /// same way by Character_Trainable.Sleep.
    /// </summary>
    static void WakeForSessions(Character_Trainable c)
    {
        if (!c.isSleeping) return;
        var sleeping = c.Stats.GetStatusByStringMatch(StatsUtility.Status_Sleeping);
        if (sleeping == null || sleeping.duration < 0) return;   // no running timer to cut

        int wake = MinutesUntilSessionWake(c);
        if (wake == int.MaxValue) return;
        wake = Math.Max(1, wake);
        if (wake >= sleeping.duration) return;

        c.ScheduledSleepMissingMinutes += sleeping.duration - wake;
        sleeping.duration = wake;
    }

    /// <summary>
    /// CancelRecreationBooking: cancels c's bookings (each firing its onCancelledEventID) at factionID ("" = anywhere):
    /// when "now" = the one under way, "today" = those under way or starting today, "all" (default) = every one not ended.
    /// Returns how many were cancelled. Each cancelled activity announces itself, displayOverride = the calling event's own.
    /// </summary>
    public static int CancelBookings(Character_Trainable c, string factionID, string when, bool displayOverride = false)
    {
        var f = c?.FactionManager;
        if (f == null) return 0;
        BeginAnnounceScope(displayOverride);
        try { return CancelBookingsInner(f, factionID, when); }
        finally { EndAnnounceScope(displayOverride); }
    }

    static int CancelBookingsInner(Character_Factions f, string factionID, string when)
    {
        int today = scr_System_Time.current.getAbsoluteDay();
        int hour = scr_System_Time.current.getCurrentTime().Hour;
        int nowAbs = RecreationBooking.AbsoluteHour(today, hour);

        int count = 0;
        foreach (var b in new List<RecreationBooking>(f.RecreationBookings))
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
        var result = new List<RecreationBooking>();
        var bookings = f.RecreationBookings;
        for (int i = 0; i < bookings.Count; i++) if (bookings[i] != null && bookings[i].AbsEnd > nowAbs) result.Add(bookings[i]);
        SortStable(result, (a, b) => a.AbsStart.CompareTo(b.AbsStart));
        return result;
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
    /// <summary>
    /// ListAppointmentSlots: one option per start hour, from the next full hour to the end of tomorrow, at which c can
    /// book templateID ("house:&lt;activityID&gt;" - a house / provided activity) at factionID ("@home" = c's priority home)
    /// for `hours` hours together with provider (IsAppointmentSlotFree). Option text = the time ("今天 14:00-16:00");
    /// picking one sets Parameters[selectedParamKey] = its absolute start hour (SetRecreationBooking start
    /// "param:&lt;key&gt;"). Only free slots are listed (user 2026-10-31).
    /// </summary>
    public static List<Event.EventEntry.Options> BuildAppointmentSlotOptions(Character_Trainable c, Character_Trainable provider,
        string factionID, string templateID, int hours, string selectedParamKey)
    {
        var result = new List<Event.EventEntry.Options>();
        var f = c?.FactionManager;
        if (f == null || provider?.FactionManager == null || hours < 1) return result;
        if (factionID == "@home") factionID = f.HomeFactions.Count > 0 && f.HomeFactions[0] != null ? f.HomeFactions[0].ID : "";
        if (templateID == null || !templateID.StartsWith("house:")) return result;
        var activity = HouseActivity(factionID, templateID.Substring(6));
        if (activity == null) return result;

        int lastEnd = (scr_System_Time.current.getAbsoluteDay() + 2) * 24;   // the end of tomorrow
        for (int start = NowAbs() + 1; start + hours <= lastEnd; start++)
        {
            if (!IsAppointmentSlotFree(f, provider.FactionManager, factionID, activity, start, hours)) continue;
            int absStart = start;
            result.Add(new Event.EventEntry.Options()
            {
                option = DescribeTime(start / 24, start % 24, start + hours),
                onSelect = ev => ev.Parameters[selectedParamKey] = absStart,
            });
        }
        return result;
    }

    /// <summary>
    /// Side-effect-free: whether a SetRecreationBooking of activity at [absStart, absStart + hours) by f would go
    /// through and provider (pf) would confirm it - the checks SetEventBookingInner / ConfirmInvitedNow (TryConfirm)
    /// make: inside the activity's activeHours; the venue's facility / reservation space (ReservationRefusal); neither
    /// of them has a booking in the way that would not just move (only IsMovableVisit ones do - the player's own
    /// appointments and invitations stay); the provider has the time (HasTimeFor - work, sleep, daily cap).
    /// </summary>
    static bool IsAppointmentSlotFree(Character_Factions f, Character_Factions pf, string factionID, RecreationActivity activity, int absStart, int hours)
    {
        var active = activity.workModule?.activeHours;
        if (active != null && active.Count > 0)
            for (int h = absStart; h < absStart + hours; h++) if (!active.Contains(h % 24)) return false;

        var venue = scr_System_CampaignManager.current.FindFactionByID(factionID);
        string sourceKey = ActivitySourceKey(venue, activity);
        var booking = RecreationBooking.Create(absStart / 24, absStart % 24, hours, factionID, activity.workModule, RecreationBookingSource.Event,
            sourceKey, "", true, "", false, activity.ID);
        if (booking == null || ReservationRefusal(f, booking) != "") return false;
        int nowAbs = NowAbs();
        foreach (var b in FindBookingConflicts(f, booking)) if (!IsMovableVisit(b, nowAbs)) return false;

        var providerBooking = RecreationBooking.Create(absStart / 24, absStart % 24, hours, factionID, activity.workModule, RecreationBookingSource.Event,
            sourceKey, "", false, "", false, activity.ID);
        if (providerBooking == null) return false;
        foreach (var b in FindBookingConflicts(pf, providerBooking)) if (!IsMovableVisit(b, nowAbs)) return false;
        return HasTimeFor(pf, providerBooking);
    }

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
    /// fires as for any cancellation). Not announced (quiet scope): the dialogue shows text ("booking line (reason)")
    /// itself. False, nothing done, for a forbidCancel booking or one already ended / gone.
    /// </summary>
    public static bool CancelByRequest(Character_Trainable c, RecreationBooking b, out string text)
    {
        text = "";
        var f = c?.FactionManager;
        if (f == null || b == null || b.forbidCancel || b.AbsEnd <= NowAbs() || !f.HasBooking(b)) return false;
        text = DescribeBooking(b) + DescribeReason(CancelReason.ByRequest);
        BeginAnnounceScope(false, true);
        try { f.CancelBooking(b, true, CancelReason.ByRequest); }
        finally { EndAnnounceScope(false, true); }
        return true;
    }
}
