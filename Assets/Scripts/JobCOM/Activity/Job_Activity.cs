using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Marker for temporary campaign-level jobs (scr_System_CampaignManager.temporaryJobs): registered like any job, but
/// belonging to no room's job list - Job_Activity, whose gather room is only where it starts and which lives until its
/// visit's booked hours are over. Saved like every job (Index_JobReferenceID).
/// </summary>
public interface I_TemporaryJob { }

/// <summary>Gathering -> Launched (it began) or Failed (it did not, at T+30) -> Ended (the visit is over). Failed is last so older saves keep their values.</summary>
public enum ActivityPhase { Gathering, Launched, Ended, Failed }

/// <summary>One participant of a Job_Activity (saved on the job) - see ActivityParticipantState.</summary>
public class ActivityParticipant
{
    public int charaRef = -1;
    public string roleID = "";
    /// <summary>The host, or a member whose invite role is required (RecreationInviteTarget.required); a solo visit's one character. Waited for by the launch rule.</summary>
    public bool mandatory = false;
    public ActivityParticipantState state = ActivityParticipantState.Expected;
    /// <summary>Not of the instance's attending set (the player walking in and starting their wait command) - never dropped by SyncParticipants.</summary>
    public bool walkIn = false;
    /// <summary>This participant's entranceFee was charged (RecreationBooking.entranceFeeCharged is the booked ones' own guard).</summary>
    public bool feeCharged = false;
    /// <summary>Was in the gather room at the launch / failure (the outcome event's "waited" target).</summary>
    public bool waited = false;
    /// <summary>Arrived after the launch / failure and got their late event (Job_Activity.ArriveLate) - once.</summary>
    public bool arrivedLate = false;
}

/// <summary>
/// Expected = attending, not arrived at the gather room yet; Present = seen there before the outcome; Launched = took
/// part in the launch (fee charged); Late = joined after the launch (fee charged on join); Left = no longer attending;
/// Released = the activity failed - released when it failed, or turned away on arriving late (Released is last so
/// older saves keep their values).
/// </summary>
public enum ActivityParticipantState { Expected, Present, Launched, Late, Left, Released }

/// <summary>
/// The coordinating job of one booked recreation visit (Plan_ActivityJobs): the visit's instance - the session
/// (RecreationGroup) of a shared visit, else the solo RecreationBooking itself, kept as a RecreationActivityKey -
/// gets exactly one of these, created lazily at T+0 by whichever participant's pull asks first (Create; the pull is
/// TryFindScheduledJobNode's booked-visit branch). It gathers the participants (actors walking to the gather room and
/// running the recreation_activity_wait COM there), launches by the visibility-dependent rule below, then dispatches
/// them - the basic Dispatch being the workModule sandbox a booked visit runs today
/// (TryFindScheduledActivityNode.RunVisit). Even a single-participant visit goes through the same flow.
/// <br/>Outcome rule (LastUpdate, every tick; solo counts as Private). An attending player participant is waited for
/// like anyone (a walk-in never is), but counts as present only after accepting through the wait button - never just by
/// standing in the gather room; an outcome needs at least one NPC present (player-only activities are unsupported).
/// <br/>Created at T+0 by the hourly check of any participant (RecreationUtility.EnsureActivityJobs - so it exists before
/// anyone arrives, e.g. while the NPCs are still travelling), or by a participant's pull, whichever comes first.
/// <br/>- Before T+30: Private / Friends / solo launch once everyone attending is present; Faction / World never before
///   T+5, then once every mandatory participant is present (at T+5 when there is none - a hostless offer).
/// <br/>- At T+30: launch with whoever is there if an NPC is present and no mandatory participant is missing; else the
///   gathering FAILS - the failed event (gatherFailedEventID, default Stay), everyone present released, no fee.
/// <br/>Either way the job lives on until the visit is over: a latecomer reaching the gather room (an NPC walked there
/// by the job, the player through the join command) gets the same outcome event again as self with isLate - joining
/// (and paying) a launched activity, or turned away from a failed one. Released NPCs are never pulled in again: they
/// sandbox the venue while their booking lasts - unless the failed event's Results cancelled it (they go home).
/// <br/>Outcome events get the same injected targets each time (BuildEventTargets - participants / mandatory /
/// optional / host / role keys / waited / late / player) and AppendStrings (isLate, activityRunning | activityFailed,
/// activityName). Self only decides visibility (the player when involved) - event logic must use the targets.
/// The launch charges the entrance fee of everyone present (Obligation_ActivityFee, player payers only - late joiners
/// pay on join); the begin event (launchEventID; "" = silent) also gets an instanceKey AppendString (a custom job its
/// LaunchJob Result launches can link itself to the instance with it). The begin event's final targets are
/// snapshotted (saved) once it ends, so later phases can dispatch to specific target keys; events never survive a
/// save, so a pending snapshot falls back to the injected keys.
/// <br/>Ends when its instance is gone, cancelled or over (AbsEnd). Saved whole (players, participants, snapshot);
/// custom per-activity jobs subclass this and override Dispatch (and the phase rooms) - later passes.
/// </summary>
public class Job_Activity : Job, I_RequireSpecialTracker, I_PlayerJoinableJob, I_TemporaryJob
{
    /// <summary>The COM waiting participants run in the gather room (Data/COM_Defs/COM_Recreation) - a real command, so the player has a package to join.</summary>
    public const string WaitCOMID = "recreation_activity_wait";
    /// <summary>The player's late-join command once the activity has launched (a 1-minute ActionPackage_ActivityWait).</summary>
    public const string JoinCOMID = "recreation_activity_join";
    /// <summary>T+30: the gathering's outcome - launch with whoever is there, or fail (see the class doc).</summary>
    public const int GatherWaitMinutes = 30;
    /// <summary>Faction / World activities never launch before T+5 (public events don't start early).</summary>
    public const int PublicMinDelayMinutes = 5;
    /// <summary>The gathering-failed event of an activity that names none (RecreationActivity.gatherFailedEventID ""): no consequence - everyone sandboxes the venue while their booking lasts.</summary>
    public const string DefaultGatherFailedEventID = "Recreation_GatherFailed_Stay";

    /// <summary>The instance this job coordinates (RecreationUtility.ActivityKeyFor / ResolveActivityInstance).</summary>
    [JsonProperty] public RecreationActivityKey key = null;
    /// <summary>The gather room (activity.gatherRoomID, "" = the venue's MainExit), resolved at creation.</summary>
    [JsonProperty] int gatherRoomRef = -1;
    /// <summary>The activity's begin event (RecreationActivity.launchEventID), read at creation; "" = silent launch.</summary>
    [JsonProperty] string launchEventID = "";
    /// <summary>The activity's gathering-failed event (RecreationActivity.gatherFailedEventID, "" = DefaultGatherFailedEventID), read at creation.</summary>
    [JsonProperty] string gatherFailedEventID = "";
    /// <summary>The visit's display name (the activity's jobPostID localized), read at creation - the gathering-failed event's $activityName$.</summary>
    [JsonProperty] string activityNameKey = "";
    [JsonProperty] ActivityPhase phase = ActivityPhase.Gathering;
    /// <summary>Legacy: older saves ended a job quietly when nobody came by T+30 - such a job gathers again on load (OnAfterDeserialize). Never set now.</summary>
    [JsonProperty] bool endedQuiet = false;
    [JsonProperty] List<ActivityParticipant> participants = new List<ActivityParticipant>();
    /// <summary>The begin event's final targets (copied once it ended - saved), the event's target keys -> character refs; later phases dispatch to these. Null until taken.</summary>
    [JsonProperty] Dictionary<string, List<int>> targetSnapshot = null;
    /// <summary>The targets the job injected into its begin event - the snapshot's fallback (events never survive a save).</summary>
    [JsonProperty] Dictionary<string, List<int>> injectedTargets = null;
    /// <summary>Begin event fired, its targets not copied yet - see OnAfterDeserialize for the save fallback.</summary>
    [JsonProperty] bool snapshotPending = false;

    // runtime only
    [JsonIgnore] RecreationGroup _session = null;
    [JsonIgnore] RecreationBooking _solo = null;
    [JsonIgnore] Manageable _venue = null;
    [JsonIgnore] EventInstance _launchEvent = null;
    [JsonIgnore] bool _subscribed = false;

    public Job_Activity() : base() { }

    // ---------------- basics ---------------- //

    [JsonIgnore] public override string DisplayName { get { return $"|Activity {key} {phase}|"; } }

    /// <summary>Always interruptible: gathering (a restroom trip is fine - the pull brings them back), and once launched the actors left on it are only waiting to be dispatched or released.</summary>
    [JsonIgnore] public override bool CanBeInterrupted { get { return true; } }

    [JsonIgnore] public override Room_Instance ParentRoom { get { return gatherRoomRef < 0 ? null : scr_System_CampaignManager.current.Map.GetRoomByRef(gatherRoomRef); } }

    public override bool IsJobValid() { return phase != ActivityPhase.Ended; }

    public override bool IsActorValid(int doerRefID)
    {
        var rec = FindParticipant(doerRefID);
        return phase != ActivityPhase.Ended && rec != null && rec.state != ActivityParticipantState.Left && rec.state != ActivityParticipantState.Released;
    }

    /// <summary>The gathering has an outcome: it launched, or it failed.</summary>
    [JsonIgnore] public bool HasOutcome { get { return phase == ActivityPhase.Launched || phase == ActivityPhase.Failed; } }

    /// <summary>Ended; for the player also once there is an outcome - they are released (the player is never driven), e.g. after arriving late (JoinCOMID).</summary>
    public override bool hasActorCompletedJob(int refID) { return phase == ActivityPhase.Ended || (refID == 0 && HasOutcome); }

    /// <summary>
    /// The panel tracks this job for the player while they take part, or - a walk-in - while it gathers / runs in the
    /// room they are standing in (Private scope included: its buttons then show disabled - see targetActorRef).
    /// </summary>
    public bool MatchTracker(Character_Trainable c)
    {
        if (c == null || participants == null) return false;
        if (participants.Exists(p => p != null && p.charaRef == c.RefID)) return true;
        if (c.RefID != 0 || phase == ActivityPhase.Ended) return false;
        return PlayerInGatherRoom;
    }

    [JsonIgnore] bool PlayerInGatherRoom
    {
        get
        {
            var room = ParentRoom;
            var playerRoom = scr_System_CampaignManager.current.GetCharaRoomInstance(0);
            return room != null && playerRoom != null && room.RefID == playerRoom.RefID;
        }
    }

    [JsonIgnore] public COM WaitCOM { get { return scr_System_Serializer.current.MasterList.COMs.GetByID(WaitCOMID); } }
    [JsonIgnore] public COM JoinCOM { get { return scr_System_Serializer.current.MasterList.COMs.GetByID(JoinCOMID); } }

    protected override List<COM> UpdateAllUsableCOMs()
    {
        var list = new List<COM>();
        if (WaitCOM != null) list.Add(WaitCOM);
        if (JoinCOM != null) list.Add(JoinCOM);
        return list;
    }

    /// <summary>
    /// Whether the panel tracks this job for the player's buttons (MakePlayerWaitPackage / MakePlayerJoinPackage), only
    /// while the player is in the gather room: gathering and not already waiting here; or with an outcome (launched /
    /// failed) and the player has not had it yet - arriving late (Private scope only for its own participants).
    /// Private scope keeps the gathering button visible but DISABLED (targetActorRef) - it only tells the player an
    /// activity is going on.
    /// </summary>
    public bool OffersJoinTo(Character_Trainable player)
    {
        if (player == null || player.RefID != 0 || phase == ActivityPhase.Ended) return false;
        // only in the gather room: a participant is tracked wherever they are (MatchTracker), and a package elsewhere
        // could never start (same-room check) - the button would follow the player around, always invalid
        if (!PlayerInGatherRoom) return false;
        if (phase == ActivityPhase.Gathering) return !IsWaiting(player.RefID);
        return PlayerMayJoin && IsLateArrival(FindParticipant(player.RefID));
    }

    /// <summary>With an outcome: c has not arrived yet and would be a late arrival (the pull walks them to the gather room).</summary>
    public bool AwaitsLateArrival(Character_Trainable c) { return c != null && HasOutcome && IsLateArrival(FindParticipant(c.RefID)); }

    /// <summary>c took part in the launched activity (at the launch, or joining late).</summary>
    public bool TookPart(Character_Trainable c)
    {
        var rec = c == null ? null : FindParticipant(c.RefID);
        return rec != null && (rec.state == ActivityParticipantState.Launched || rec.state == ActivityParticipantState.Late);
    }

    /// <summary>rec (null = not a participant yet) would be a late arrival now: still expected, or a walk-in-to-be, and has not had their late event.</summary>
    static bool IsLateArrival(ActivityParticipant rec)
    {
        return rec == null || (rec.state == ActivityParticipantState.Expected && !rec.arrivedLate);
    }

    /// <summary>
    /// The player's own "join the activity" package (JoinCOMID - valid once there is an outcome, ActionPackage_ActivityWait):
    /// the panel turns it into a command button; clicking it is the player's late arrival (AddActor -> ArriveLate).
    /// </summary>
    public ActionPackage MakePlayerJoinPackage(Character_Trainable player)
    {
        if (player == null || JoinCOM == null) return null;
        return JoinCOM.MakePackage(this, new List<int>() { player.RefID }, new List<int>(), player.RefID, ParentRoom);
    }

    /// <summary>charaRef holds one of this job's wait packages (not run out).</summary>
    bool IsWaiting(int charaRef)
    {
        return packages_current.Concat(packages_previous).Any(ap => ap.targetCOM != null && ap.targetCOM.ID == WaitCOMID
            && ap.actorRefs.Contains(charaRef) && ap.Duration > 0);
    }

    /// <summary>
    /// The player's own "wait for the activity to begin" package (the panel turns it into a command button - clicking it
    /// adds it to this job, makes the player a participant (AddActor) and advances time through it, up to
    /// WaitMinutesLeft). Valid only while gathering (ActionPackage_ActivityWait); the panel shows it while OffersJoinTo.
    /// </summary>
    public ActionPackage MakePlayerWaitPackage(Character_Trainable player)
    {
        if (player == null || WaitCOM == null) return null;
        return WaitCOM.MakePackage(this, new List<int>() { player.RefID }, new List<int>(), player.RefID, ParentRoom);
    }

    /// <summary>Duration of a wait package started now: until T+30, the gathering's outcome - at least 1 minute.</summary>
    [JsonIgnore] public int WaitMinutesLeft { get { return Math.Max(1, GatherWaitMinutes - MinutesSinceStart); } }

    /// <summary>A Private-scope activity (a solo visit counts as Private).</summary>
    [JsonIgnore] bool PrivateScope
    {
        get
        {
            if (key == null || !key.isSession) return true;
            if (_session == null && !TryGetInstance(out _, out _)) return true;
            return _session == null || _session.visibility == RecreationVisibility.Private;
        }
    }

    /// <summary>
    /// Whether the player may join this activity: Friends / Faction / World are open to walk-ins (even without a
    /// booking); Private (and a solo visit) only to one of its own participants (an invited member attending, the host).
    /// </summary>
    [JsonIgnore] public bool PlayerMayJoin
    {
        get { return !PrivateScope || FindParticipant(0) != null; }
    }

    /// <summary>
    /// Private scope with no place for the player: every join button of this job renders INVALID (greyed) instead of
    /// disappearing, so the button still tells the player an activity is going on. -2 never equals a CurrentTargetRef,
    /// which is what the join-button validator compares this against (scr_panel_COMmanager).
    /// </summary>
    [JsonIgnore] public override int targetActorRef
    {
        get { return PlayerMayJoin ? base.targetActorRef : -2; }
    }

    /// <summary>
    /// The player starting one of their commands (the panel's Execute -> ChangeCurrentJob -> AddActor). Gathering (wait):
    /// a player without a record becomes an optional walk-in (mandatory if host). With an outcome (join): the player's
    /// late arrival (ArriveLate - joins a launched activity, or is turned away from a failed one); they are then
    /// released (hasActorCompletedJob). In Private scope only the activity's own participants get here (the buttons are
    /// disabled for everyone else).
    /// </summary>
    public override void AddActor(int charaRef, string priorityCOMID = "", string priorityCOMTag = "")
    {
        base.AddActor(charaRef, priorityCOMID, priorityCOMTag);
        if (charaRef != 0 || !PlayerMayJoin) return;
        if (HasOutcome)
        {
            var player = scr_System_CampaignManager.current.FindInstanceByID(0);
            if (player != null) ArriveLate(player);
            return;
        }
        if (phase != ActivityPhase.Gathering || FindParticipant(0) != null) return;
        bool host = key != null && key.isSession && _session != null && _session.hostRef == 0;
        participants.Add(new ActivityParticipant()
        {
            charaRef = 0,
            roleID = host ? RecreationUtility.HostRoleID : "",
            mandatory = host,
            walkIn = true,
        });
    }

    // ---------------- the instance ---------------- //

    /// <summary>The resolved instance (cached; liveness - open session, live booking - is checked separately each tick).</summary>
    bool TryGetInstance(out RecreationGroup session, out RecreationBooking solo)
    {
        session = null;
        solo = null;
        if (key == null || scr_System_CampaignManager.current == null) return false;
        if (_session == null && _solo == null && !RecreationUtility.ResolveActivityInstance(key, out _session, out _solo)) return false;
        session = _session;
        solo = _solo;
        return true;
    }

    /// <summary>The visit this job coordinates - its session, else the solo booking (I_ActivityBooking: name, start, details) - or null when gone.</summary>
    [JsonIgnore] public I_ActivityBooking ActivityInstance
    {
        get { return TryGetInstance(out var g, out var b) ? (I_ActivityBooking)g ?? b : null; }
    }

    [JsonIgnore] Manageable Venue
    {
        get
        {
            if (_venue == null && key != null && scr_System_CampaignManager.current != null)
                _venue = scr_System_CampaignManager.current.FindFactionByID(key.factionID);
            return _venue;
        }
    }

    /// <summary>The solo visit's owner (null for a session / when gone).</summary>
    [JsonIgnore] Character_Factions OwnerFactions
    {
        get
        {
            if (key == null || key.isSession) return null;
            return scr_System_CampaignManager.current.FindInstanceByID(key.ownerRef)?.FactionManager;
        }
    }

    /// <summary>Faction / World visibility (their launch rule); a solo visit counts as Private.</summary>
    [JsonIgnore] bool PublicScope
    {
        get
        {
            if (key == null || !key.isSession) return false;
            if (_session == null && !TryGetInstance(out _, out _)) return false;
            return _session != null && (_session.visibility == RecreationVisibility.Faction || _session.visibility == RecreationVisibility.World);
        }
    }

    /// <summary>c's live booking of this visit (a member's session booking / the solo booking itself), or null.</summary>
    public RecreationBooking BookingOf(Character_Trainable c)
    {
        if (c == null || key == null) return null;
        var f = c.FactionManager;
        if (f == null) return null;
        if (key.isSession)
        {
            if (_session == null && !TryGetInstance(out _, out _)) return null;
            return f.RecreationBookings.FirstOrDefault(b => b != null && b.isSessionBooking && b.Session == _session);
        }
        var booking = f.FindBookingStartedAt(key.absStart, key.factionID);
        return booking != null && booking.Session == null ? booking : null;
    }

    /// <summary>The visit's activity (template data, read live through a participant's booking - RecreationBooking.GetActivity), or null.</summary>
    RecreationActivity ActivityOf(Character_Trainable c)
    {
        var b = BookingOf(c);
        return b == null ? null : b.GetActivity(c.FactionManager);
    }

    [JsonIgnore] DateTime StartDateTime
    {
        get
        {
            if (key == null) return scr_System_Time.current.getCurrentTime();
            int day = key.isSession ? key.day : key.absStart / 24;
            int hour = key.isSession ? key.startHour : key.absStart % 24;
            return scr_System_Time.current.getStartTime().Date.AddDays(day).AddHours(hour);
        }
    }

    /// <summary>Minutes since T, the booked start hour (may be negative briefly if a pull raced the clock).</summary>
    [JsonIgnore] int MinutesSinceStart { get { return (int)(scr_System_Time.current.getCurrentTime() - StartDateTime).TotalMinutes; } }

    /// <summary>Where the job stands - the pull hook (TryFindScheduledJobNode) reads this: Gathering joins, Launched dispatches or joins late.</summary>
    [JsonIgnore] public ActivityPhase Phase { get { return phase; } }


    static int NowAbs() { return RecreationBooking.AbsoluteHour(scr_System_Time.current.getAbsoluteDay(), scr_System_Time.current.getCurrentTime().Hour); }

    public ActivityParticipant FindParticipant(int charaRef) { return participants == null ? null : participants.Find(p => p != null && p.charaRef == charaRef); }

    // ---------------- creation ---------------- //

    /// <summary>
    /// Creates and registers the coordinating job of the instance instanceKey names, populated from its live attending
    /// set - whichever participant's pull asks first creates it (never a central loop). Null when the instance cannot
    /// be resolved (a null key is "no instance - don't create"), its venue or gather room is gone, or it already has a
    /// job (that one is returned).
    /// </summary>
    public static Job_Activity Create(RecreationActivityKey instanceKey, Character_Trainable firstAsker)
    {
        if (instanceKey == null) return null;
        if (!RecreationUtility.ResolveActivityInstance(instanceKey, out var g, out var b)) return null;
        // an existing job is returned; a ref left pointing at no job (lost from a save, ended without clearing) is dropped so a new one can be made
        if (g != null && g.activityJobRef >= 0)
        {
            if (g.ActivityJob is Job_Activity existing && existing.Phase != ActivityPhase.Ended) return existing;
            g.activityJobRef = -1;
        }
        if (g == null && b != null && b.Session == null && b.activityJobRef >= 0)
        {
            if (b.ActivityJob is Job_Activity existing && existing.Phase != ActivityPhase.Ended) return existing;
            b.activityJobRef = -1;
        }

        var venue = scr_System_CampaignManager.current.FindFactionByID(instanceKey.factionID);
        if (venue == null)
        {
            Debug.LogError($"Job_Activity.Create: venue [{instanceKey.factionID}] not found for {instanceKey}");
            return null;
        }

        var job = new Job_Activity();
        job.key = instanceKey;
        job._session = g;
        job._solo = b;
        job._venue = venue;
        job.FactionOwner = venue;

        // gather room + begin event: the first asker's visit activity, read live (a booking-less asker falls back to the venue's MainExit and a silent launch)
        var f = firstAsker?.FactionManager;
        var booking = g != null ? f?.RecreationBookings.FirstOrDefault(x => x != null && x.isSessionBooking && x.Session == g) : b;
        var activity = booking?.GetActivity(f);
        var room = ResolveGatherRoom(venue, activity);
        if (room == null)
        {
            Debug.LogError($"Job_Activity.Create: no gather room for {instanceKey} at {venue.ID}");
            return null;
        }
        job.gatherRoomRef = room.RefID;
        job.launchEventID = activity?.launchEventID ?? "";
        job.gatherFailedEventID = activity?.gatherFailedEventID ?? "";
        job.activityNameKey = activity?.workModule?.jobPostID ?? "";

        job.SyncParticipants();
        scr_System_CampaignManager.current.Register(job);   // Register links the instance (activityJobRef)
        return job;
    }

    /// <summary>The instance's existing activity job (its activityJobRef), or null - the pull's first lookup.</summary>
    public static Job_Activity FindFor(RecreationActivityKey instanceKey)
    {
        if (instanceKey == null) return null;
        if (!RecreationUtility.ResolveActivityInstance(instanceKey, out var g, out var b)) return null;
        return g != null ? g.ActivityJob as Job_Activity : b.ActivityJob as Job_Activity;
    }

    /// <summary>Link the saved ref so a loaded save (and every pull) finds this job through the instance.</summary>
    public override void Register(int id)
    {
        base.Register(id);
        if (TryGetInstance(out var g, out var b))
        {
            if (g != null) g.activityJobRef = id;
            else if (b.Session == null) b.activityJobRef = id;
        }
    }

    /// <summary>activity's gather room at venue (its gatherRoomID among the venue's rooms, else the venue's MainExit) - also used by I_ActivityBooking.Tooltip (RecreationUtility.BuildActivityDetail).</summary>
    public static Room_Instance ResolveGatherRoom(Manageable venue, RecreationActivity activity)
    {
        if (venue == null) return null;
        if (activity != null && !string.IsNullOrEmpty(activity.gatherRoomID))
        {
            foreach (var kvp in venue.ManagedRooms)
                if (kvp.Value != null && kvp.Value.Base != null && kvp.Value.Base.ID == activity.gatherRoomID) return kvp.Value;
            Debug.LogError($"Job_Activity: gather room [{activity.gatherRoomID}] not found at {venue.ID} - falling back to MainExit");
        }
        return venue.MainExit;
    }

    // ---------------- participants ---------------- //

    /// <summary>
    /// Rebuilds the expected participant set from the instance's live attending set: new attendees join as Expected
    /// (mandatory = the host / a required role), those no longer attending - their booking was cancelled, slept
    /// through, called away - stop being waited for (Left). Walk-ins (the player) are left alone.
    /// </summary>
    void SyncParticipants()
    {
        if (!TryGetInstance(out var g, out var b)) return;
        if (g != null)
        {
            var spec = RecreationUtility.ResolveSpec(g.specRef);
            foreach (var m in g.members)
            {
                if (m == null) continue;
                var rec = FindParticipant(m.charaRef);
                if (rec == null)
                {
                    if (!m.IsAttending) continue;
                    participants.Add(new ActivityParticipant()
                    {
                        charaRef = m.charaRef,
                        roleID = m.roleID,
                        mandatory = m.charaRef == g.hostRef || spec.GetRole(m.roleID)?.required == true,
                    });
                }
                else if (!m.IsAttending && !rec.walkIn && rec.state != ActivityParticipantState.Launched && rec.state != ActivityParticipantState.Late)
                {
                    rec.state = ActivityParticipantState.Left;
                }
            }
        }
        else
        {
            // solo: the owner's booking must still be live (a cancelled one ends the job anyway)
            var f = OwnerFactions;
            bool live = f != null && b != null && f.RecreationBookings.Contains(b);
            var rec = FindParticipant(key.ownerRef);
            if (live && rec == null) participants.Add(new ActivityParticipant() { charaRef = key.ownerRef, mandatory = true });
            else if (!live && rec != null && rec.state == ActivityParticipantState.Expected) rec.state = ActivityParticipantState.Left;
        }
    }

    /// <summary>
    /// Expected participants standing in the gather room become Present - the player only once they accepted by
    /// clicking the wait button (actor of this job, waiting): standing in the room never grabs them into the activity.
    /// </summary>
    void UpdatePresence()
    {
        var room = ParentRoom;
        if (room == null) return;
        foreach (var rec in participants)
        {
            if (rec == null || rec.state != ActivityParticipantState.Expected) continue;
            if (rec.charaRef == 0 && !(actorRefID.Contains(0) && IsWaiting(0))) continue;
            var chara = scr_System_CampaignManager.current.FindInstanceByID(rec.charaRef);
            if (chara == null || !chara.canAct || chara.isSleeping) continue;
            var charaRoom = scr_System_CampaignManager.current.GetCharaRoomInstance(rec.charaRef);
            if (charaRoom != null && charaRoom.RefID == room.RefID) rec.state = ActivityParticipantState.Present;
        }
    }

    /// <summary>c's participant record, created (Expected) if missing: an instance member keeps their role, anyone else is a walk-in.</summary>
    ActivityParticipant RecordFor(Character_Trainable c)
    {
        var rec = FindParticipant(c.RefID);
        if (rec != null) return rec;
        rec = new ActivityParticipant() { charaRef = c.RefID };
        var m = TryGetInstance(out var g, out _) && g != null ? g.FindMember(c.RefID) : null;
        if (m != null)
        {
            rec.roleID = m.roleID;
            rec.mandatory = m.charaRef == g.hostRef || RecreationUtility.ResolveSpec(g.specRef).GetRole(m.roleID)?.required == true;
        }
        else rec.walkIn = true;
        participants.Add(rec);
        return rec;
    }

    /// <summary>
    /// c reached the gather room after the outcome (an NPC walked there by the job, the player through the join
    /// command) - once per participant: the same outcome event again, c as self, with isLate.
    /// <br/>- Launched: c joins late - Late, the entrance fee, their place in the target snapshot (participants /
    ///   optional / their role key only - never the event's own keys), the begin event. True.
    /// <br/>- Failed: c is turned away - Released, the failed event (its Results decide the consequences, e.g. cancelling
    ///   c's booking). False.
    /// <br/>Someone already taking part (Launched / Late) is true, anyone else not a late arrival (Left, Released,
    /// already arrived) false - nothing fired.
    /// </summary>
    public bool ArriveLate(Character_Trainable c)
    {
        if (c == null || !HasOutcome) return false;
        var rec = RecordFor(c);
        if (rec.state == ActivityParticipantState.Launched || rec.state == ActivityParticipantState.Late) return true;
        if (!IsLateArrival(rec)) return false;
        rec.arrivedLate = true;

        if (phase == ActivityPhase.Launched)
        {
            rec.state = ActivityParticipantState.Late;
            // late arrivals count as optional (Plan_ActivityJobs): participants + optional + their role key, when it exists
            if (targetSnapshot != null)
            {
                AddToSnapshot("participants", c.RefID);
                AddToSnapshot("optional", c.RefID);
                if (!string.IsNullOrEmpty(rec.roleID) && targetSnapshot.ContainsKey(rec.roleID)) AddToSnapshot(rec.roleID, c.RefID);
            }
            ChargeFee(rec);
            if (!string.IsNullOrEmpty(launchEventID)) FireOutcomeEvent(launchEventID, c, c);
            return true;
        }

        rec.state = ActivityParticipantState.Released;
        FireOutcomeEvent(FailedEventID, c, c);
        return false;
    }

    void AddToSnapshot(string targetKey, int charaRef)
    {
        if (targetSnapshot == null) return;
        if (!targetSnapshot.TryGetValue(targetKey, out var list))
        {
            if (targetKey != "participants" && targetKey != "optional") return;   // never the event's own keys
            targetSnapshot[targetKey] = list = new List<int>();
        }
        if (!list.Contains(charaRef)) list.Add(charaRef);
    }

    /// <summary>
    /// The entrance fee, once per participant (RecreationUtility.ChargeEntranceFee - the booked one's own
    /// RecreationBooking.entranceFeeCharged guards it too). A walk-in without a booking pays the visit's activity read
    /// through any participant's booking (VisitActivity).
    /// </summary>
    void ChargeFee(ActivityParticipant rec)
    {
        if (rec == null || rec.feeCharged) return;
        rec.feeCharged = true;
        var c = scr_System_CampaignManager.current.FindInstanceByID(rec.charaRef);
        if (c == null) return;
        var booking = BookingOf(c);
        var activity = booking != null ? booking.GetActivity(c.FactionManager) : VisitActivity;
        RecreationUtility.ChargeEntranceFee(c, Venue, activity, booking);
    }

    /// <summary>The visit's activity read through the first participant holding a booking of it (template data, live) - for those without one (walk-ins).</summary>
    [JsonIgnore] RecreationActivity VisitActivity
    {
        get
        {
            if (participants == null) return null;
            foreach (var p in participants)
            {
                var chara = p == null ? null : scr_System_CampaignManager.current.FindInstanceByID(p.charaRef);
                var activity = chara == null ? null : ActivityOf(chara);
                if (activity != null) return activity;
            }
            return null;
        }
    }

    // ---------------- gather and launch ---------------- //

    /// <summary>
    /// The per-tick gather check (I_RequireSpecialTracker): presence, then the outcome rule (see the class doc). The
    /// player is waited for like anyone else when they are an attending participant (Gates - a walk-in never is), but
    /// an outcome needs at least one NPC participant present (player-only activities are not supported).
    /// </summary>
    void CheckGather()
    {
        SyncParticipants();
        UpdatePresence();
        int minutes = MinutesSinceStart;
        bool npcPresent = participants.Exists(p => p != null && p.charaRef != 0 && p.state == ActivityParticipantState.Present);
        bool mandatoryMissing = participants.Exists(p => Gates(p) && p.mandatory && p.state == ActivityParticipantState.Expected);

        if (minutes >= GatherWaitMinutes)
        {
            // T+30: launch with whoever is here, unless nobody is or a mandatory participant (the player too) is missing
            if (npcPresent && !mandatoryMissing) Launch("gather window over");
            else Fail(npcPresent ? "mandatory participant missing" : "nobody gathered");
            return;
        }
        if (!npcPresent) return;
        if (PublicScope)
        {
            // never before T+5; then once every mandatory participant is present (there may be none - a hostless offer)
            if (minutes >= PublicMinDelayMinutes && !mandatoryMissing) Launch("all mandatory present");
            return;
        }
        // Private / Friends / solo: everyone attending is here (they stop being waited for the moment they stop attending)
        if (!participants.Exists(p => Gates(p) && p.state == ActivityParticipantState.Expected)) Launch("everyone attending present");
    }

    /// <summary>Whether p is waited for by the outcome rule: every attending participant - the player too, unless only a walk-in.</summary>
    static bool Gates(ActivityParticipant p) { return p != null && (p.charaRef != 0 || !p.walkIn); }

    /// <summary>The failed event: the activity's gatherFailedEventID, else DefaultGatherFailedEventID (Stay).</summary>
    [JsonIgnore] string FailedEventID { get { return string.IsNullOrEmpty(gatherFailedEventID) ? DefaultGatherFailedEventID : gatherFailedEventID; } }

    /// <summary>Drops every wait package; the player's: the update loop is advancing time through it - stop it here. Then releases the player (never driven).</summary>
    void EndWaiting()
    {
        bool playerWasWaiting = false;
        foreach (var ap in packages_current.Concat(packages_previous).ToList())
        {
            if (ap.targetCOM == null || ap.targetCOM.ID != WaitCOMID) continue;
            if (ap.actorRefs.Contains(0) && ap.Duration > 0) playerWasWaiting = true;
            RemovePackage(ap, false);
        }
        if (playerWasWaiting) scr_UpdateHandler.current.NotifyPlayerPackageInterrupted();
        if (actorRefID.Contains(0)) RemoveActor(0);
    }

    /// <summary>The launch: present participants become Launched (waited) and pay, the waits end, and the begin event (if any) starts.</summary>
    void Launch(string reason)
    {
        phase = ActivityPhase.Launched;
        foreach (var rec in participants)
        {
            if (rec.state != ActivityParticipantState.Present) continue;
            rec.state = ActivityParticipantState.Launched;
            rec.waited = true;
            ChargeFee(rec);
        }
        EndWaiting();
        StartBeginEvent();
        if (scr_System_CentralControl.current.LogPrefs.DLog_Jobs) Debug.Log($"{DisplayName} launched ({reason})");
    }

    /// <summary>
    /// The gathering failed at T+30: present participants are Released (waited) - no fee - their waits end, every actor
    /// is let go (released NPCs are never pulled in again: they sandbox the venue while their booking lasts), and the
    /// failed event (FailedEventID) fires - its Results decide the consequences (e.g. cancelling the bookings of
    /// "waited" - Recreation_GatherFailed_Leave). The job lives on for late arrivals (ArriveLate).
    /// </summary>
    void Fail(string reason)
    {
        phase = ActivityPhase.Failed;
        foreach (var rec in participants)
        {
            if (rec.state != ActivityParticipantState.Present) continue;
            rec.state = ActivityParticipantState.Released;
            rec.waited = true;
        }
        EndWaiting();
        foreach (var refID in actorRefID.ToList()) RemoveActor(refID);
        FireOutcomeEvent(FailedEventID, null, OutcomeSelf());
        if (scr_System_CentralControl.current.LogPrefs.DLog_Jobs) Debug.Log($"{DisplayName} gathering failed ({reason})");
    }

    // ---------------- outcome events ---------------- //

    /// <summary>The target keys the job injects itself - role keys may not take these names.</summary>
    static readonly HashSet<string> ReservedTargetKeys = new HashSet<string>() { "participants", "mandatory", "optional", "host", "waited", "late", "player" };

    /// <summary>
    /// Self of the outcome's own (not late) fire - ONLY for the event's visibility: the player if they waited or are
    /// missing, else the host if they waited, else the first who waited, else the first missing. Null = no one involved.
    /// </summary>
    Character_Trainable OutcomeSelf()
    {
        var involved = participants.Where(p => p != null && (p.waited || p.state == ActivityParticipantState.Expected)).ToList();
        if (involved.Count == 0) return null;
        int hostRef = key.isSession && _session != null ? _session.hostRef : -1;
        var pick = involved.Find(p => p.charaRef == 0)
            ?? involved.Find(p => p.waited && p.charaRef == hostRef)
            ?? involved.Find(p => p.waited)
            ?? involved[0];
        return scr_System_CampaignManager.current.FindInstanceByID(pick.charaRef);
    }

    /// <summary>
    /// The targets every outcome event gets (begin and failed alike, the original fire and a late arrival's):
    /// <br/>- participants: everyone still involved (all but Left); mandatory / optional: split of them; one key per
    ///   invite role (roleID) of them;
    /// <br/>- host: the host, when they waited or are this latecomer;
    /// <br/>- waited: who was in the gather room at the launch / failure;
    /// <br/>- late: the original fire - who was still expected (absent) at the outcome; a late fire - the latecomer;
    /// <br/>- player: the player.
    /// </summary>
    Dictionary<string, List<int>> BuildEventTargets(Character_Trainable latecomer)
    {
        var targets = new Dictionary<string, List<int>>();
        void Add(string k, int r)
        {
            if (!targets.TryGetValue(k, out var list)) targets[k] = list = new List<int>();
            if (!list.Contains(r)) list.Add(r);
        }
        var involved = participants.Where(p => p != null && p.state != ActivityParticipantState.Left).ToList();
        foreach (var p in involved)
        {
            Add("participants", p.charaRef);
            Add(p.mandatory ? "mandatory" : "optional", p.charaRef);
            if (!string.IsNullOrEmpty(p.roleID) && !ReservedTargetKeys.Contains(p.roleID)) Add(p.roleID, p.charaRef);
            if (p.waited) Add("waited", p.charaRef);
        }
        int hostRef = key.isSession && _session != null ? _session.hostRef : -1;
        if (hostRef >= 0 && (involved.Exists(p => p.charaRef == hostRef && p.waited) || (latecomer != null && latecomer.RefID == hostRef))) Add("host", hostRef);
        if (latecomer != null) Add("late", latecomer.RefID);
        else foreach (var p in involved.Where(p => p.state == ActivityParticipantState.Expected)) Add("late", p.charaRef);
        Add("player", 0);
        return targets;
    }

    /// <summary>
    /// Starts eventID with BuildEventTargets(latecomer) and the AppendStrings isLate (a late fire only), hasWaited /
    /// hasLate (when those targets are non-empty), activityRunning (launched) or activityFailed (failed), activityName
    /// (+ instanceKey when given). self only decides visibility
    /// (forced on screen when it is the player) - the event's logic must use the targets. Null when there is no event /
    /// self, or it was rejected.
    /// </summary>
    EventInstance FireOutcomeEvent(string eventID, Character_Trainable latecomer, Character_Trainable self, Dictionary<string, List<int>> targets = null, bool withInstanceKey = false)
    {
        if (string.IsNullOrEmpty(eventID) || self == null) return null;
        var ev = new EventInstance(self, eventID, "");
        ev.displayOverride = self.RefID == 0;
        foreach (var kvp in targets ?? BuildEventTargets(latecomer))
        {
            var list = kvp.Value.Select(r => scr_System_CampaignManager.current.FindInstanceByID(r)).Where(x => x != null).ToList();
            if (list.Count > 0) ev.Targets[kvp.Key] = list;
        }
        if (latecomer != null) ev.AppendStrings["isLate"] = new List<string>() { "true" };
        // presence flags of the waited / late targets, so the event can branch on them (ExistAppendStrings)
        if (ev.Targets.ContainsKey("waited")) ev.AppendStrings["hasWaited"] = new List<string>() { "true" };
        if (ev.Targets.ContainsKey("late")) ev.AppendStrings["hasLate"] = new List<string>() { "true" };
        ev.AppendStrings[phase == ActivityPhase.Failed ? "activityFailed" : "activityRunning"] = new List<string>() { "true" };
        ev.AppendStrings["activityName"] = new List<string>() { string.IsNullOrEmpty(activityNameKey) ? "" : LocalizeDictionary.QueryThenParse(activityNameKey) };
        if (withInstanceKey) ev.AppendStrings["instanceKey"] = new List<string>() { key.Serialize() };
        return scr_UpdateHandler.current.EventHandler.StartEvent(ev, false) ? ev : null;
    }

    /// <summary>
    /// Starts the begin event (the outcome event with an instanceKey AppendString; self = OutcomeSelf) and snapshots its
    /// final targets once it ends - or takes the injected targets as the snapshot directly (silent launch, rejected event).
    /// </summary>
    void StartBeginEvent()
    {
        var injected = BuildEventTargets(null);
        injectedTargets = injected;
        var ev = FireOutcomeEvent(launchEventID, null, OutcomeSelf(), injected, withInstanceKey: true);
        if (ev == null)
        {
            TakeSnapshot(injected);   // silent launch / rejected - the injected keys are the snapshot
            return;
        }
        _launchEvent = ev;
        snapshotPending = true;
        SubscribeEventEnd();
    }

    // ---------------- the target snapshot ---------------- //

    void SubscribeEventEnd()
    {
        if (_subscribed) return;
        _subscribed = true;
        scr_UpdateHandler.current.Observer_PostUpdateTime_EventEnd += OnEventEnd;
    }

    void UnsubscribeEventEnd()
    {
        if (!_subscribed) return;
        _subscribed = false;
        scr_UpdateHandler.current.Observer_PostUpdateTime_EventEnd -= OnEventEnd;
    }

    /// <summary>First callback with the event handler idle: the begin event is over - copy its final targets. (Kept subscribed until then; other events running meanwhile are fine.)</summary>
    void OnEventEnd(bool handlerActive)
    {
        if (handlerActive || !snapshotPending) return;
        var ev = _launchEvent;
        if (ev == null)
        {
            TakeSnapshot(injectedTargets ?? new Dictionary<string, List<int>>());
            return;
        }
        var snapshot = new Dictionary<string, List<int>>();
        foreach (var kvp in ev.Targets)
        {
            var refs = kvp.Value?.Where(x => x != null).Select(x => x.RefID).ToList();
            if (refs != null && refs.Count > 0) snapshot[kvp.Key] = refs;
        }
        TakeSnapshot(snapshot);
    }

    /// <summary>Stores the final targets and closes the snapshot bookkeeping (the event's own targets version and the injected-fallback overload).</summary>
    void TakeSnapshot(Dictionary<string, List<int>> targets)
    {
        targetSnapshot = targets ?? new Dictionary<string, List<int>>();
        snapshotPending = false;
        injectedTargets = null;
        _launchEvent = null;
        UnsubscribeEventEnd();
    }

    public override void OnAfterDeserialize()
    {
        if (participants == null) participants = new List<ActivityParticipant>();
        base.OnAfterDeserialize();
        // events never survive a save: a still-pending snapshot falls back to the injected targets
        if (snapshotPending) TakeSnapshot(injectedTargets ?? new Dictionary<string, List<int>>());
        // saves from when nobody-by-T+30 ended a job quietly: it gathers again (it lives until the visit is over)
        if (endedQuiet && phase == ActivityPhase.Ended)
        {
            phase = ActivityPhase.Gathering;
            endedQuiet = false;
            // that end marked everyone Left - expected again; SyncParticipants drops whoever no longer attends
            foreach (var rec in participants) if (rec != null && rec.state == ActivityParticipantState.Left) rec.state = ActivityParticipantState.Expected;
        }
    }

    // ---------------- dispatch ---------------- //

    /// <summary>
    /// Sends participant c to their part of the launched activity. The basic version is the workModule sandbox a
    /// booked visit runs today (TryFindScheduledActivityNode.RunVisit): while c is busy with one of the schedule's
    /// workCommands it keeps at it, else it draws a random workCommand and goes to the venue furniture offering it -
    /// which takes c over (Job_Furniture). Later phase types dispatch per target key instead ("command X for key Y in
    /// room Z" via furniture or a package built here - that is what the per-room package support is for).
    /// </summary>
    public virtual bool Dispatch(Character_Trainable c)
    {
        var booking = BookingOf(c);
        var venue = booking?.Faction;
        if (c == null || venue == null) return false;
        return TryFindScheduledActivityNode.RunVisit(c, venue, false, scr_System_Time.current.getCurrentTime().Hour,
            FactionUtility.GetHeuristic(PathfindHeuristic.random), TryFindScheduledActivityNode.VisitFilter, null);
    }

    /// <summary>
    /// Gathering: walk to the gather room and run the wait COM there (ActionPackage_ActivityWait, lasting the remaining
    /// wait). With an outcome: someone who took part (Launched / Late) is dispatched - nothing to dispatch to right now:
    /// released into the sandbox (still a participant); a latecomer walks to the gather room, and arriving there is
    /// their late arrival (ArriveLate - joining a launched activity then dispatched, or turned away from a failed one).
    /// Anyone the job is done with (Left, Released, ended) is released (false - Character_Trainable.TryGetJob drops the
    /// job). The player is never driven.
    /// </summary>
    public override bool UpdateActorPackage(Character_Trainable c, out string ss)
    {
        ss = $"|{DisplayName} [{c.FirstName}]|";
        if (phase == ActivityPhase.Ended || c == null) { ss += "ended"; return false; }
        var rec = FindParticipant(c.RefID);

        if (HasOutcome)
        {
            if (c.RefID == 0) { ss += "player never driven"; return false; }
            if (rec != null && (rec.state == ActivityParticipantState.Left || rec.state == ActivityParticipantState.Released)) { ss += "done with this activity"; return false; }
            if (rec != null && (rec.state == ActivityParticipantState.Launched || rec.state == ActivityParticipantState.Late))
            {
                if (Dispatch(c)) { ss += "dispatched"; return true; }   // may have left this job (ChangeCurrentJob)
                ss += "nothing to dispatch to, released to the sandbox";   // still a participant
                return false;
            }

            // a latecomer: walk to the gather room - arriving there is the late arrival
            if (packages_current.Exists(x => x.actorRefs.Contains(c.RefID))) { ss += "has current package"; return true; }
            if (packages_previous.Exists(x => x.actorRefs.Contains(c.RefID) && x.Duration > 0)) { ss += "has ongoing package"; return true; }
            var lateRoom = ParentRoom;
            if (lateRoom == null) { ss += "gather room gone"; return false; }
            var lateCharaRoom = scr_System_CampaignManager.current.GetCharaRoomInstance(c.RefID);
            if (lateCharaRoom == null || lateCharaRoom.RefID != lateRoom.RefID)
            {
                var pathPkg = new ActionPackage_PathTo(this, c.RefID, lateRoom.RefID);
                if (!pathPkg.Validate()) { ss += "cannot path to gather room"; return false; }
                AddPackage(new List<ActionPackage>() { pathPkg });
                ss += "late - pathing to gather room";
                return true;
            }
            if (ArriveLate(c) && Dispatch(c)) { ss += "arrived late, joined, dispatched"; return true; }
            ss += phase == ActivityPhase.Failed ? "arrived late - activity failed, released" : "arrived late, nothing to dispatch to - released";
            return false;
        }

        // gathering
        if (rec == null || rec.state == ActivityParticipantState.Left) { ss += "not waited for"; return false; }
        {
            if (packages_current.Exists(x => x.actorRefs.Contains(c.RefID))) { ss += "has current package"; return true; }
            if (packages_previous.Exists(x => x.actorRefs.Contains(c.RefID) && x.Duration > 0)) { ss += "has ongoing package"; return true; }

            var room = ParentRoom;
            if (room == null) { ss += "gather room gone"; return false; }

            // the player is never driven: they joined a wait package (they are in the room) - never path them
            if (c.RefID != 0)
            {
                var charaRoom = scr_System_CampaignManager.current.GetCharaRoomInstance(c.RefID);
                if (charaRoom == null || charaRoom.RefID != room.RefID)
                {
                    var pathPkg = new ActionPackage_PathTo(this, c.RefID, room.RefID);
                    if (!pathPkg.Validate()) { ss += "cannot path to gather room"; return false; }
                    AddPackage(new List<ActionPackage>() { pathPkg });
                    ss += "pathing to gather room";
                    return true;
                }
            }
            else
            {
                var playerRoom = scr_System_CampaignManager.current.GetCharaRoomInstance(c.RefID);
                if (playerRoom == null || playerRoom.RefID != room.RefID) { ss += "player left the gather room"; return false; }
            }

            var waitPkg = MakeWaitPackage(c, room);
            if (waitPkg == null) { ss += "wait package invalid"; return false; }
            AddPackage(new List<ActionPackage>() { waitPkg });
            ss += "waiting in gather room";
            return true;
        }
    }

    ActionPackage MakeWaitPackage(Character_Trainable c, Room_Instance room)
    {
        var com = WaitCOM;
        if (com == null) return null;
        var pkg = com.MakePackage(this, new List<int>() { c.RefID }, new List<int>(), -1, room);
        return pkg != null && pkg.Validate() ? pkg : null;
    }

    // ---------------- end ---------------- //

    /// <summary>
    /// Ends the job - only when its instance is gone, cancelled or over (LastUpdate): the actors are released, the
    /// instance link cleared and the job unregistered (NotifyEndJob). Until then it stays alive for late arrivals.
    /// </summary>
    void EndJob(string reason)
    {
        if (phase == ActivityPhase.Ended) return;
        phase = ActivityPhase.Ended;
        endedQuiet = false;
        foreach (var rec in participants) if (rec.state != ActivityParticipantState.Left) rec.state = ActivityParticipantState.Left;
        TakeSnapshot(targetSnapshot ?? injectedTargets ?? new Dictionary<string, List<int>>());   // closes a pending subscription too
        // still gathering when the visit was cancelled / ran out: drop the waits - a player waiting must not keep advancing time through a dead job
        EndWaiting();
        foreach (var refID in actorRefID.ToList()) RemoveActor(refID);

        if (TryGetInstance(out var g, out var b))
        {
            if (g != null) { if (g.activityJobRef == RefID) g.activityJobRef = -1; }
            else if (b != null && b.Session == null && b.activityJobRef == RefID) b.activityJobRef = -1;
        }
        if (!scr_UpdateHandler.current.Updating) NotifyDescriptionsOutOfUpdate();
        scr_System_CampaignManager.current.NotifyEndJob(this);
        if (scr_System_CentralControl.current.LogPrefs.DLog_Jobs) Debug.Log($"{DisplayName} ended: {reason}");
    }

    /// <summary>Releases an actor the usual way (the character's own ChangeCurrentJob calls this back - re-entrancy guarded like Job_MedicalCare).</summary>
    public override void RemoveActor(int charaRef)
    {
        if (actorJoinTime.ContainsKey(charaRef))
        {
            actorJoinTime.Remove(charaRef);
            var c = scr_System_CampaignManager.current.FindInstanceByID(charaRef);
            if (c != null && c.CurrentJob == this) c.ChangeCurrentJob(null);
        }
        base.RemoveActor(charaRef);
    }

    /// <summary>
    /// Per-tick (I_RequireSpecialTracker, specialUpdateJobs): instance liveness (gone / cancelled / over ends the
    /// job), and the gather check while gathering.
    /// </summary>
    public override void LastUpdate()
    {
        if (phase == ActivityPhase.Ended) return;

        if (!TryGetInstance(out var g, out var b)) { EndJob("instance gone"); return; }
        int nowAbs = NowAbs();
        if (g != null)
        {
            if (!g.IsOpen) { EndJob("session " + g.status); return; }
            if (nowAbs >= g.AbsEnd) { EndJob("session over"); return; }
        }
        else
        {
            // a solo visit must still be its owner's LIVE booking - found only in the past-booking record (cancelled,
            // or naturally over and not pruned yet) ends the job
            var f = OwnerFactions;
            if (f == null || b == null || !f.RecreationBookings.Contains(b)) { EndJob("booking no longer live"); return; }
            if (nowAbs >= b.AbsEnd) { EndJob("visit over"); return; }
        }

        if (phase == ActivityPhase.Gathering) CheckGather();
    }
}
