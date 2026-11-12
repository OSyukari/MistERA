using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

/// <summary>
/// Venue-driven activity lifecycle: each venue keeps the index of the visits planned at it (solo bookings and sessions)
/// and updates itself (Manageable.OnTimeUpdate -> UpdateVenueActivities) - the Job_Activity at T-60, the deferred setup
/// retry at T, forgetting the visit at its end. Nothing here walks the characters: sessions check themselves hourly
/// (RecreationGroupRegistry.OnHourUpdate), characters re-validate their bookings on their own faction / schedule
/// changes, and a venue re-validates its visits on member / MemberType / furniture changes (RevalidateVenueActivities).
/// </summary>
public static partial class RecreationUtility
{
    sealed class VenueActivities
    {
        public readonly List<ActivityOccurrence> occurrences = new List<ActivityOccurrence>();
        public long due = long.MaxValue;
        public bool updating;
    }

    sealed class ActivityOccurrence
    {
        public RecreationBooking booking;
        public RecreationGroup session;
        public VenueActivities venue;
        public long due = long.MinValue;
        public int Start { get { return session != null ? session.AbsStart : booking.AbsStart; } }
        public int End { get { return session != null ? session.AbsEnd : booking.AbsEnd; } }
    }

    // Runtime state belongs to the saved registry, not the singleton or a previous campaign.
    sealed class ActivityLifecycle
    {
        public ActivityLifecycle() { }
        public readonly Dictionary<string, VenueActivities> venues = new Dictionary<string, VenueActivities>();
        public readonly List<string> venueIDs = new List<string>();
        public readonly Dictionary<RecreationBooking, ActivityOccurrence> solos = new Dictionary<RecreationBooking, ActivityOccurrence>();
        public readonly Dictionary<RecreationGroup, ActivityOccurrence> sessions = new Dictionary<RecreationGroup, ActivityOccurrence>();
        // a pool for venue occurrence snapshots (a venue update / re-validation may run inside another's)
        public readonly Stack<List<ActivityOccurrence>> occurrenceBuffers = new Stack<List<ActivityOccurrence>>();
    }

    static readonly ConditionalWeakTable<RecreationGroupRegistry, ActivityLifecycle> activityLifecycles = new ConditionalWeakTable<RecreationGroupRegistry, ActivityLifecycle>();
    static ActivityLifecycle Lifecycle
    {
        get
        {
            var campaign = scr_System_CampaignManager.current;
            return campaign == null || campaign.ActivityLifecycleLoading ? null : activityLifecycles.GetOrCreateValue(campaign.RecreationGroups);
        }
    }

    static long ActivityMinute()
    {
        return (long)NowAbs() * 60 + scr_System_Time.current.getCurrentTime().Minute;
    }

    static VenueActivities ActivityVenue(ActivityLifecycle state, string id)
    {
        if (!state.venues.TryGetValue(id, out var venue))
        {
            state.venues.Add(id, venue = new VenueActivities());
            state.venueIDs.Add(id);
        }
        return venue;
    }

    static List<ActivityOccurrence> RentOccurrences(ActivityLifecycle state, VenueActivities activities)
    {
        var snapshot = state.occurrenceBuffers.Count > 0 ? state.occurrenceBuffers.Pop() : new List<ActivityOccurrence>();
        for (int i = 0; i < activities.occurrences.Count; i++) snapshot.Add(activities.occurrences[i]);
        return snapshot;
    }

    static void ReturnOccurrences(ActivityLifecycle state, List<ActivityOccurrence> snapshot)
    {
        snapshot.Clear();
        state.occurrenceBuffers.Push(snapshot);
    }

    public static void TrackActivityBooking(RecreationBooking b)
    {
        var state = Lifecycle;
        if (state == null || b == null) return;
        if (b.isSessionBooking || string.IsNullOrEmpty(b.factionID)) { ForgetActivityBooking(b); return; }
        if (state.solos.ContainsKey(b)) return;
        var venue = ActivityVenue(state, b.factionID);
        var occurrence = new ActivityOccurrence { booking = b, venue = venue, due = (long)(b.AbsStart - 1) * 60 };
        state.solos.Add(b, occurrence);
        venue.occurrences.Add(occurrence);
        venue.due = Math.Min(venue.due, occurrence.due);
    }

    public static void ForgetActivityBooking(RecreationBooking b)
    {
        var state = Lifecycle;
        if (state == null || b == null || !state.solos.TryGetValue(b, out var occurrence)) return;
        state.solos.Remove(b);
        occurrence.venue.occurrences.Remove(occurrence);
        occurrence.venue.due = long.MinValue;
    }

    public static void TrackActivitySession(RecreationGroup g)
    {
        var state = Lifecycle;
        if (state == null || g == null || g.concluded || g.status == RecreationGroupStatus.Ended || string.IsNullOrEmpty(g.factionID)
            || state.sessions.ContainsKey(g)) return;
        var venue = ActivityVenue(state, g.factionID);
        var occurrence = new ActivityOccurrence { session = g, venue = venue, due = (long)(g.AbsStart - 1) * 60 };
        state.sessions.Add(g, occurrence);
        venue.occurrences.Add(occurrence);
        venue.due = Math.Min(venue.due, occurrence.due);
    }

    public static void ForgetActivitySession(RecreationGroup g)
    {
        var state = Lifecycle;
        if (state == null || g == null || !state.sessions.TryGetValue(g, out var occurrence)) return;
        state.sessions.Remove(g);
        occurrence.venue.occurrences.Remove(occurrence);
        occurrence.venue.due = long.MinValue;
    }

    /// <summary>A character left the campaign: their solo visits leave the venue index (their sessions find them gone on their hourly check).</summary>
    public static void ForgetActivityCharacter(Character_Factions f)
    {
        if (f == null) return;
        var bookings = f.RecreationBookings;
        for (int i = 0; i < bookings.Count; i++) ForgetActivityBooking(bookings[i]);
    }

    /// <summary>
    /// Session g changed (posted, attendance, validity, cancelled): it is (re-)indexed at its venue and its venue
    /// occurrence becomes due, so the venue's next update re-reads it (Job_Activity creation, its end). Nothing else -
    /// no character is queued.
    /// </summary>
    public static void NotifyActivityChanged(RecreationGroup g)
    {
        var state = Lifecycle;
        if (state == null || g == null) return;
        TrackActivitySession(g);
        if (state.sessions.TryGetValue(g, out var occurrence))
        {
            occurrence.due = long.MinValue;
            occurrence.venue.due = long.MinValue;
        }
    }

    /// <summary>The venue's furniture changed (Manageable.NotifyFurnitureChange): its planned visits re-validate (RevalidateVenueActivities - a lost facility cancels them).</summary>
    public static void NotifyVenueActivitiesChanged(Manageable venue)
    {
        RevalidateVenueActivities(venue);
    }

    /// <summary>
    /// The visits planned at venue re-validate themselves - on a change of venue's members / MemberTypes
    /// (Manageable.AddToFaction / RemoveFromFaction) or furniture: a visit not started whose facility is gone is
    /// cancelled (a session as a whole - FacilityRemoved); a participant whose access is gone (LostAccess - a membership
    /// left, a house activity no longer offered, an own-faction type no longer offered) has their booking cancelled.
    /// Reads the venue's own occurrence list only: never every session or character.
    /// </summary>
    public static void RevalidateVenueActivities(Manageable venue)
    {
        var state = Lifecycle;
        if (state == null || venue == null || scr_System_Time.current == null || !state.venues.TryGetValue(venue.ID, out var activities)) return;
        activities.due = long.MinValue;
        int nowAbs = NowAbs();
        var snapshot = RentOccurrences(state, activities);
        try
        {
            foreach (var occurrence in snapshot)
            {
                occurrence.due = long.MinValue;
                var g = occurrence.session;
                if (g != null)
                {
                    if (!state.sessions.ContainsKey(g) || !g.IsOpen || g.AbsEnd <= nowAbs) continue;
                    var activity = ResolveActivity(g);
                    if (g.AbsStart > nowAbs && activity?.facility != null && !FacilityAvailable(venue, activity))
                    {
                        CancelSession(g, CancelReason.FacilityRemoved);
                        continue;
                    }
                    // members are only ever appended: the count taken here covers everyone checked
                    for (int i = 0, n = g.members.Count; i < n && i < g.members.Count; i++)
                    {
                        var m = g.members[i];
                        if (m.state != RecreationMemberState.Attending) continue;
                        var mf = m.Chara?.FactionManager;
                        var b = mf == null ? null : FindSessionBooking(mf, g);
                        if (b != null && LostAccess(mf, b)) mf.CancelBooking(b, true, CancelReason.LostAccess);
                    }
                    continue;
                }
                var booking = occurrence.booking;
                var f = booking?.Owner?.FactionManager;
                if (f == null || !state.solos.ContainsKey(booking) || !f.HasBooking(booking) || booking.AbsEnd <= nowAbs) continue;
                if (booking.AbsStart > nowAbs && FacilityLostFor(f, booking)) f.CancelBooking(booking, true, CancelReason.FacilityRemoved);
                else if (LostAccess(f, booking)) f.CancelBooking(booking, true, CancelReason.LostAccess);
            }
        }
        finally { ReturnOccurrences(state, snapshot); }
    }

    /// <summary>
    /// Planned (not started) occurrences at venue whose facility furniture j provides - the packing warning. Reads the
    /// venue's own occurrence list only: never every session or character.
    /// </summary>
    public static int CountPlannedFacilityUse(Manageable venue, Job_Furniture j)
    {
        var state = Lifecycle;
        if (state == null || venue == null || j == null || !state.venues.TryGetValue(venue.ID, out var activities)) return 0;
        int now = NowAbs(), count = 0;
        foreach (var occurrence in activities.occurrences)
        {
            RecreationActivity activity;
            if (occurrence.session != null)
            {
                if (!occurrence.session.IsOpen || occurrence.session.AbsStart <= now) continue;
                activity = ResolveActivity(occurrence.session);
            }
            else
            {
                var b = occurrence.booking;
                var f = b.Owner?.FactionManager;
                if (f == null || b.isSessionBooking || b.AbsStart <= now || !f.HasBooking(b)) continue;
                activity = b.GetActivity(f);
            }
            var fac = activity?.facility;
            if (fac != null && !string.IsNullOrEmpty(fac.comTag) && j.HasAvailableCOMwithCOMTags(fac.comTag)) count++;
        }
        return count;
    }

    /// <summary>Financial accrual only: no lifecycle checks, ranking or notices.</summary>
    public static void AccrueVisitHour(Character_Factions f)
    {
        if (f?.Owner != null && scr_System_Time.current != null) BillVisitHour(f, f.Owner);
    }

    /// <summary>
    /// Manageable.OnTimeUpdate: venue's due visits - from T-60 its Job_Activity exists (created once; a setup deferred at
    /// T-60 is retried at T), and at its end the visit leaves the index (a session is concluded - RecreationGroupRegistry.Prune).
    /// The idle path (nothing due) touches nothing.
    /// </summary>
    public static void UpdateVenueActivities(Manageable venue)
    {
        var state = Lifecycle;
        if (state == null || venue == null || scr_System_Time.current == null
            || scr_System_Time.current.DebugSkipBeforeLastDay || !state.venues.TryGetValue(venue.ID, out var activities)) return;
        long now = ActivityMinute();
        if (activities.updating || now < activities.due) return;
        activities.updating = true;
        // Only a due/dirty venue takes a mutation-safe snapshot, into a pooled buffer.
        var snapshot = RentOccurrences(state, activities);
        try
        {
            activities.due = long.MaxValue;
            foreach (var occurrence in snapshot)
            {
                bool tracked = occurrence.session != null ? state.sessions.ContainsKey(occurrence.session) : state.solos.ContainsKey(occurrence.booking);
                if (!tracked) continue;
                if (now >= occurrence.due)
                {
                    int start = occurrence.Start, end = occurrence.End;
                    if (now >= (long)(start - 1) * 60 && now < (long)end * 60 && (occurrence.session == null || occurrence.session.IsOpen))
                    {
                        var f = occurrence.booking == null ? null : scr_System_CampaignManager.current.FindInstanceByID(occurrence.booking.ownerRef)?.FactionManager;
                        var activity = occurrence.session != null ? ResolveActivity(occurrence.session) : ResolveActivity(f, occurrence.booking);
                        var module = occurrence.session != null ? occurrence.session.workModule : occurrence.booking.workModule;
                        if (!string.IsNullOrEmpty(activity?.reserveFurnitureTag) || IsOvernight(activity) || (module?.workCommands != null && module.workCommands.Count > 0))
                        {
                            var key = occurrence.session != null ? ActivityKeyFor(occurrence.session) : ActivityKeyFor(f, occurrence.booking);
                            var existing = key == null ? null : Job_Activity.FindFor(key);
                            if (key != null && existing == null) Job_Activity.Create(key, notify: false);
                            // a setup deferred at T-60 is retried at the T due point
                            else existing?.RetryDueSetup();
                        }
                    }
                    if (now >= (long)end * 60)
                    {
                        if (occurrence.session != null) Sessions.Prune(occurrence.session);
                        else ForgetActivityBooking(occurrence.booking);
                        continue;
                    }
                    long next = (long)end * 60;
                    if (now < (long)(start - 1) * 60) next = (long)(start - 1) * 60;
                    else if (now < (long)start * 60) next = (long)start * 60;
                    occurrence.due = next;
                }
                activities.due = Math.Min(activities.due, occurrence.due);
            }
        }
        finally
        {
            activities.updating = false;
            ReturnOccurrences(state, snapshot);
        }
    }

    /// <summary>After a load (the lifecycle is not kept while loading): every character's bookings and every live session are indexed at their venue again.</summary>
    public static void RebuildActivityLifecycle(IEnumerable<Character_Trainable> characters)
    {
        planningLateCycle = -1;
        automaticPlanningDepth = 0;
        dailyDiscoveryClosed = true;
        activityLifecycles.Remove(Sessions);
        foreach (var c in characters)
        {
            var f = c?.FactionManager;
            if (f == null) continue;
            foreach (var b in f.RecreationBookings) TrackActivityBooking(b);
        }
        foreach (var g in Sessions.All) TrackActivitySession(g);
    }
}
