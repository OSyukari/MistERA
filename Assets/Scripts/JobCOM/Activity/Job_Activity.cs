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
/// gets exactly one of these, created by its venue (Create; participants only look it up in
/// TryFindScheduledJobNode's booked-visit branch). It gathers the participants (actors walking to the gather room and
/// running the recreation_activity_wait COM there), launches by the visibility-dependent rule below, then dispatches
/// them - the basic Dispatch being the workModule sandbox a booked visit runs today
/// (TryFindScheduledActivityNode.RunVisit). Even a single-participant visit goes through the same flow.
/// <br/>Outcome rule (LastUpdate, every tick; solo counts as Private). An attending player participant is waited for
/// like anyone (a walk-in never is), but counts as present only after accepting through the wait button - never just by
/// standing in the gather room; ordinary shared activities need an NPC present. A reserved private visit also supports the player's explicit solo wait.
/// <br/>Created at T-60 by its venue (RecreationUtility.UpdateVenueActivities), independent of any participant; from
/// then on participants in a free hour may gather early (TryFindScheduledJobNode.TryEarlyActivityJob) and the player may wait.
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
public partial class Job_Activity : Job, I_RequireSpecialTracker, I_PlayerJoinableJob, I_TemporaryJob
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
    [JsonIgnore] bool _handlingFailure = false;

    public Job_Activity() : base() { }

    // ---------------- basics ---------------- //

    [JsonIgnore] string _displayName = null;
    [JsonIgnore] ActivityPhase _displayNamePhase;
    [JsonIgnore] (bool? isSession, int? day, int? startHour, string factionID, string sourceKey, int? hostRef, int? ownerRef, int? absStart) _displayNameKey;

    [JsonIgnore] public override string DisplayName
    {
        get
        {
            // Compare key fields without formatting: the public key can also be changed in place.
            var nameKey = (key?.isSession, key?.day, key?.startHour, key?.factionID, key?.sourceKey, key?.hostRef, key?.ownerRef, key?.absStart);
            if (_displayName == null || _displayNamePhase != phase || _displayNameKey != nameKey)
            {
                _displayName = $"|Activity {key} {phase}|";
                _displayNamePhase = phase;
                _displayNameKey = nameKey;
            }
            return _displayName;
        }
    }

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
        return PlayerJoinRoom != null;
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
    /// Gathering is offered in the gather room. A launched visit is offered beside its actual local attendees,
    /// once per player and visit. Admission is checked by the package and again when the event starts.
    /// </summary>
    public bool OffersJoinTo(Character_Trainable player)
    {
        if (player == null || player.RefID != 0 || !GatherOpen || PlayerJoinRoom == null) return false;
        if (phase == ActivityPhase.Gathering) return !IsWaiting(player.RefID);
        var rec = FindParticipant(player.RefID);
        return phase == ActivityPhase.Launched && (rec == null || (!rec.arrivedLate && !rec.waited));
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
    /// The player's short entry package beside a launched activity's local attendees. Its PackageBegin invokes
    /// JoinPlayer; generic actor registration never dispatches the event.
    /// </summary>
    public ActionPackage MakePlayerJoinPackage(Character_Trainable player)
    {
        if (player == null || JoinCOM == null) return null;
        var room = PlayerJoinRoom;
        return room == null ? null : JoinCOM.MakePackage(this, new List<int>() { player.RefID }, new List<int>(), player.RefID, room);
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

    /// <summary>Current source liveness, not just the coordinator's last hourly/tick snapshot.</summary>
    [JsonIgnore] bool VisitIsLive
    {
        get
        {
            if (phase == ActivityPhase.Ended || scr_System_Time.current == null || !TryGetInstance(out var g, out var b)) return false;
            int now = NowAbs();
            if (g != null) return g.IsOpen && now >= g.AbsStart && now < g.AbsEnd;
            return b != null && OwnerFactions?.RecreationBookings.Contains(b) == true && now >= b.AbsStart && now < b.AbsEnd;
        }
    }

    /// <summary>
    /// The player may wait / join: the visit is live, or - gathering before T (the job exists from T-60, made by its
    /// venue) - its instance is still open. The player's wait / join checks (and the gather room's command
    /// substitution) use it; everything else keeps VisitIsLive.
    /// </summary>
    [JsonIgnore] bool GatherOpen
    {
        get
        {
            if (setupPending) return false;
            if (VisitIsLive) return true;
            if (phase != ActivityPhase.Gathering || !TryGetInstance(out var g, out var b)) return false;
            return g != null ? g.IsOpen : b != null && OwnerFactions?.RecreationBookings.Contains(b) == true;
        }
    }

    bool IsConfirmedAttendee(Character_Trainable c)
    {
        if (c == null || !TryGetInstance(out var g, out var b)) return false;
        var booking = BookingOf(c);
        return booking != null && c.FactionManager.RecreationBookings.Contains(booking)
            && (g != null ? g.FindMember(c.RefID)?.IsAttending == true : b.ownerRef == c.RefID);
    }

    /// <summary>Invitation scope does not grant walk-in access; confirmed bookings remain valid attendance.</summary>
    [JsonIgnore] public bool PlayerMayJoin
    {
        get { return IsConfirmedAttendee(scr_System_CampaignManager.current?.Player) || VisitActivity?.allowWalkIn == true; }
    }

    /// <summary>Only already-arrived, live attendees in this physical room, never absent invitees or the whole crowd.</summary>
    List<Character_Trainable> LocalCompanions(Room_Instance room, int exceptRef)
    {
        var result = new List<Character_Trainable>();
        if (room == null || room.FactionOwner?.Faction != Venue) return result;
        foreach (var rec in participants)
        {
            if (rec == null || rec.charaRef == exceptRef
                || (rec.state != ActivityParticipantState.Launched && rec.state != ActivityParticipantState.Late)) continue;
            var c = scr_System_CampaignManager.current.FindInstanceByID(rec.charaRef);
            if (c == null || !c.canAct || c.isSleeping) continue;
            if (!rec.walkIn && !IsConfirmedAttendee(c)) continue;
            if (scr_System_CampaignManager.current.GetCharaRoomInstance(c.RefID)?.RefID == room.RefID) result.Add(c);
        }
        return result;
    }

    [JsonIgnore] Room_Instance PlayerJoinRoom
    {
        get
        {
            var room = scr_System_CampaignManager.current?.GetCharaRoomInstance(0);
            if (phase == ActivityPhase.Gathering) return PlayerInGatherRoom ? room : null;
            return phase == ActivityPhase.Launched && LocalCompanions(room, 0).Count > 0 ? room : null;
        }
    }

    /// <summary>Shared, side-effect-free admission check for UI and event execution. reason is a localization key.</summary>
    public bool CanPlayerJoin(Character_Trainable player, Room_Instance room, bool joining, out string reason)
    {
        reason = "ui_activityJoin_ended";
        if (!GatherOpen || (joining ? phase != ActivityPhase.Launched : phase != ActivityPhase.Gathering)) return false;
        reason = "ui_activityJoin_ineligible";
        if (player == null || player.RefID != 0 || !player.canAct || player.isSleeping) return false;
        reason = "ui_activityJoin_closed";
        if (!PlayerMayJoin) return false;
        reason = "ui_activityJoin_wrongRoom";
        if (room == null || scr_System_CampaignManager.current.GetCharaRoomInstance(0)?.RefID != room.RefID
            || (!joining && room.RefID != gatherRoomRef)) return false;
        reason = "ui_activityJoin_access";
        if (Venue == null || room.FactionOwner?.Faction != Venue || !room.CanBeAccessedBy(player)) return false;
        reason = "ui_activityJoin_suspended";
        if (!player.CanWorkFor(Venue)
            || (!IsConfirmedAttendee(player) && RecreationUtility.HasUnpaidActivityFee(player.FactionManager, Venue))) return false;
        reason = "ui_activityJoin_ineligible";
        var offer = RecreationBoard.FindDef(key.isSession ? _session.sourceKey : _solo.sourceKey);
        if (offer != null && !offer.IsEligible(player)) return false;
        reason = AdmissionRefusal(player);
        if (reason != "") return false;
        if (joining)
        {
            reason = "ui_activityJoin_busy";
            var rec = FindParticipant(0);
            if (rec != null && (rec.arrivedLate || rec.waited || rec.state == ActivityParticipantState.Released)) return false;
            reason = "ui_activityJoin_noPartner";
            if (LocalCompanions(room, 0).Count == 0) return false;
        }
        reason = "";
        return true;
    }

    /// <summary>A short explicit join fires the activity's authored outcome; no timers or automatic reward settlement.</summary>
    public bool JoinPlayer(Character_Trainable player, Room_Instance room)
    {
        if (!CanPlayerJoin(player, room, true, out _)) return false;
        var companions = LocalCompanions(room, player.RefID);
        var rec = RecordFor(player);
        var oldState = rec.state;
        bool confirmed = IsConfirmedAttendee(player);
        rec.walkIn = !confirmed;
        if (!confirmed) { rec.mandatory = false; rec.roleID = ""; }
        // Set before StartEvent: events may execute immediately and re-enter through callbacks.
        rec.arrivedLate = true;
        rec.state = ActivityParticipantState.Late;
        if (!CheckInOvernight(rec))
        {
            rec.arrivedLate = false;
            rec.state = oldState;
            return false;
        }
        var targets = new Dictionary<string, List<int>>()
        {
            ["joining"] = new List<int>() { player.RefID },
            ["companions"] = companions.Select(c => c.RefID).ToList(),
            ["participants"] = companions.Select(c => c.RefID).Concat(new[] { player.RefID }).ToList(),
            ["player"] = new List<int>() { player.RefID },
        };
        var localRefs = new HashSet<int>(targets["participants"]);
        foreach (var pair in BuildEventTargets(player))
            if (!targets.ContainsKey(pair.Key)) targets[pair.Key] = pair.Value.Where(localRefs.Contains).ToList();
        if (!string.IsNullOrEmpty(launchEventID) && FireOutcomeEvent(launchEventID, player, player, targets, isJoin: true) == null)
        {
            rec.arrivedLate = false;
            rec.state = oldState;
            CheckOutOvernight(player.RefID);
            return false;
        }
        ChargeFee(rec);
        AddToSnapshot("participants", player.RefID);
        AddToSnapshot("optional", player.RefID);
        return true;
    }

    /// <summary>
    /// An activity closed to additional players: every join button renders INVALID (greyed) instead of
    /// disappearing, so the button still tells the player an activity is going on. -2 never equals a CurrentTargetRef,
    /// which is what the join-button validator compares this against (scr_panel_COMmanager).
    /// </summary>
    [JsonIgnore] public override int targetActorRef
    {
        get { return PlayerMayJoin ? base.targetActorRef : -2; }
    }

    /// <summary>
    /// Gathering registers a waiting player; launched visits only register the actor. The short join package owns
    /// event invocation, so ChangeCurrentJob and AddPackage cannot each fire it.
    /// </summary>
    public override void AddActor(int charaRef, string priorityCOMID = "", string priorityCOMTag = "")
    {
        if (charaRef == 0)
        {
            var player = scr_System_CampaignManager.current.Player;
            var room = scr_System_CampaignManager.current.GetCharaRoomInstance(0);
            if (!CanPlayerJoin(player, room, phase == ActivityPhase.Launched, out _)) return;
        }
        base.AddActor(charaRef, priorityCOMID, priorityCOMTag);
        if (charaRef != 0 || !PlayerMayJoin) return;
        // Player arrival is committed by the join AP, not either of the two AddActor calls made by the UI.
        if (HasOutcome) return;
        if (phase != ActivityPhase.Gathering) return;
        var existing = FindParticipant(0);
        if (existing != null)
        {
            if (!existing.arrivedLate && !existing.waited)
            {
                // Cancellation updates the booking before SyncParticipants updates the cached participant state.
                if (existing.state == ActivityParticipantState.Left) existing.state = ActivityParticipantState.Expected;
                existing.walkIn = !IsConfirmedAttendee(scr_System_CampaignManager.current.Player);
                if (existing.walkIn) { existing.mandatory = false; existing.roleID = ""; }
            }
            return;
        }
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

    /// <summary>T, the booked start, has been reached (the player's wait button reads "wait to begin" before it).</summary>
    [JsonIgnore] public bool HasStarted { get { return MinutesSinceStart >= 0; } }

    /// <summary>Where the job stands - the pull hook (TryFindScheduledJobNode) reads this: Gathering joins, Launched dispatches or joins late.</summary>
    [JsonIgnore] public ActivityPhase Phase { get { return phase; } }


    static int NowAbs() { return RecreationBooking.AbsoluteHour(scr_System_Time.current.getAbsoluteDay(), scr_System_Time.current.getCurrentTime().Hour); }

    public ActivityParticipant FindParticipant(int charaRef) { return participants == null ? null : participants.Find(p => p != null && p.charaRef == charaRef); }

    // ---------------- creation ---------------- //

    /// <summary>
    /// Creates and registers the coordinating job of the instance instanceKey names, populated from its live attending
    /// set. Only the venue creates it (RecreationUtility.UpdateVenueActivities, from T-60) - never a participant's pull.
    /// The definition is read from the instance itself (its session, else the solo booking), not from whoever asks.
    /// Null when the instance cannot be resolved (a null key is "no instance - don't create"), or its venue or gather
    /// room is gone; an instance that already has a job returns that one. A structurally unavailable room / furniture /
    /// media fails the gathering - no resource is acquired, so there is nothing to wait for. notify: after complete
    /// setup, publish the change to the instance's affected parties (false for the venue's own batch, which queued them).
    /// </summary>
    public static Job_Activity Create(RecreationActivityKey instanceKey, bool notify = true)
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

        // gather room + begin event: the occurrence's own activity, read live (none falls back to the venue's MainExit and a silent launch)
        var activity = job.VisitActivity;
        var room = ResolveGatherRoom(venue, activity);
        // a facility visit starts in the execution room its planning selected, when that still qualifies
        if (activity?.facility != null)
        {
            var planned = RecreationUtility.GetPlannedActivityRoom(g != null ? (I_ActivityBooking)g : b);
            if (planned != null && RecreationUtility.RoomQualifies(planned, activity.facility)) room = planned;
        }
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
        string setupFailure = job.TrySetup(activity);
        if (setupFailure != null)
        {
            // a failed / incomplete setup publishes nothing itself (cancellations its failure makes notify on their own)
            if (job.MinutesSinceStart < 0)
            {
                // before T: retried at the activity hour (RetryDueSetup) rather than failing the gathering early
                job.setupPending = true;
                job.UnregisterActivityRooms();
                if (scr_System_CentralControl.current.LogPrefs.DLog_Jobs) Debug.Log($"{job.DisplayName} setup deferred to T ({setupFailure})");
            }
            else job.Fail(setupFailure);
            return job;
        }
        job.RefreshActivityRooms();
        if (notify) job.NotifyInstanceChanged();
        return job;
    }

    /// <summary>
    /// Selects the room / furniture / media the occurrence uses (no resource is acquired). Null = complete, else the failure.
    /// </summary>
    string TrySetup(RecreationActivity activity)
    {
        return !ReserveFurniture(activity) ? "reserved furniture unavailable"
            : !ReserveFacility(activity) ? "facility unavailable"
            : !ReserveMedia(activity) ? "no media item available" : null;
    }

    /// <summary>
    /// Setup failed before T: retried once at T (the venue's due transition, else this job's own tick). Until then
    /// the job only tracks presence - no room context, no early attendance, no player wait.
    /// </summary>
    [JsonProperty] bool setupPending = false;
    [JsonIgnore] public bool SetupPending { get { return setupPending; } }

    /// <summary>The deferred setup at the activity hour: complete it (and publish), or fail the gathering now.</summary>
    public void RetryDueSetup()
    {
        if (!setupPending || phase != ActivityPhase.Gathering || MinutesSinceStart < 0) return;
        setupPending = false;
        string failure = TrySetup(VisitActivity);
        if (failure != null) { Fail(failure); return; }
        RefreshActivityRooms();
        NotifyInstanceChanged();
    }

    void NotifyInstanceChanged()
    {
        if (!TryGetInstance(out var g, out _)) return;
        if (g != null) RecreationUtility.NotifyActivityChanged(g);
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

    /// <summary>
    /// activity's gather room at venue: for a furniture-bound activity the first room its facility qualifies in
    /// (RecreationUtility.FacilityRooms - the job may still pick another when it reserves a unit), else its gatherRoomID
    /// among the venue's rooms, else the venue's MainExit - also used by I_ActivityBooking.Tooltip (RecreationUtility.BuildActivityDetail).
    /// </summary>
    public static Room_Instance ResolveGatherRoom(Manageable venue, RecreationActivity activity)
    {
        if (venue == null) return null;
        // (a room visit's room is the host's - only known to the job, ReserveFacility)
        if (activity?.facility != null && !activity.facility.hostPrivateRoom)
        {
            var facilityRoom = RecreationUtility.FacilityRooms(venue, activity).FirstOrDefault();
            if (facilityRoom != null) return facilityRoom;
        }
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
            if (charaRoom != null && charaRoom.RefID == room.RefID)
            {
                rec.state = ActivityParticipantState.Present;
                // arrived: the session's no-show check reads it
                RecreationUtility.OnActivityArrived(chara, BookingOf(chara));
            }
        }
    }

    /// <summary>c's participant record, created (Expected) if missing: an instance member keeps their role, anyone else is a walk-in.</summary>
    ActivityParticipant RecordFor(Character_Trainable c)
    {
        var rec = FindParticipant(c.RefID);
        if (rec != null) return rec;
        rec = new ActivityParticipant() { charaRef = c.RefID };
        var m = TryGetInstance(out var g, out _) && g != null ? g.FindMember(c.RefID) : null;
        if (m?.IsAttending == true)
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
        // late, but here: not a no-show
        RecreationUtility.OnActivityArrived(c, BookingOf(c));

        if (phase == ActivityPhase.Launched)
        {
            if (AdmissionRefusal(c) != "" || !CheckInOvernight(rec))
            {
                rec.state = ActivityParticipantState.Released;
                BookingOf(c)?.Owner?.FactionManager.CancelBooking(BookingOf(c), true, RecreationUtility.CancelReason.GroupFailed);
                return false;
            }
            rec.state = ActivityParticipantState.Late;
            // late arrivals count as optional (Plan_ActivityJobs): participants + optional + their role key, when it exists
            if (targetSnapshot != null)
            {
                AddToSnapshot("participants", c.RefID);
                AddToSnapshot("optional", c.RefID);
                if (!string.IsNullOrEmpty(rec.roleID) && targetSnapshot.ContainsKey(rec.roleID)) AddToSnapshot(rec.roleID, c.RefID);
            }
            ChargeFee(rec);
            RecreationUtility.OnActivityAttended(c, BookingOf(c));
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
        var c = scr_System_CampaignManager.current.FindInstanceByID(rec.charaRef);
        if (c == null) return;
        var booking = BookingOf(c);
        var activity = booking != null ? booking.GetActivity(c.FactionManager) : VisitActivity;
        if (Venue == null) return;
        rec.feeCharged = true;
        RecreationUtility.ChargeEntranceFee(c, Venue, activity, booking);
    }

    /// <summary>Resolve policy from the instance itself, including solo visits and booking-less walk-ins.</summary>
    [JsonIgnore] RecreationActivity VisitActivity
    {
        get
        {
            if (!TryGetInstance(out var g, out var b)) return null;
            return g != null ? RecreationUtility.ResolveActivity(g) : b?.GetActivity(OwnerFactions);
        }
    }

    // ---------------- gather and launch ---------------- //

    /// <summary>
    /// The per-tick gather check (I_RequireSpecialTracker): presence, then the outcome rule (see the class doc). The
    /// player is waited for like anyone else when they are an attending participant (Gates - a walk-in never is).
    /// A reserved private visit may launch for the explicitly waiting player alone; ordinary crowds still need an NPC.
    /// </summary>
    void CheckGather()
    {
        SyncParticipants();
        UpdatePresence();
        int minutes = MinutesSinceStart;
        // before T (made at T-60 - participants gather ahead of T): presence only, no launch / fail before T
        if (minutes < 0) return;
        bool privateUnit = !string.IsNullOrEmpty(VisitActivity?.reserveFurnitureTag);
        bool npcPresent = participants.Exists(p => p != null && (p.charaRef != 0 || privateUnit) && p.state == ActivityParticipantState.Present);
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
        var present = participants.Where(p => p.state == ActivityParticipantState.Present).ToList();
        if (!CanLaunchReserved(present)) { Fail("reserved room admission failed"); return; }
        foreach (var rec in present)
            if (!CheckInOvernight(rec)) { Fail("overnight check-in failed"); return; }
        phase = ActivityPhase.Launched;
        foreach (var rec in participants)
        {
            if (rec.state != ActivityParticipantState.Present) continue;
            rec.state = ActivityParticipantState.Launched;
            rec.waited = true;
            ChargeFee(rec);
            // took part: the visit counts as attended (the planner's cooldowns / weekly targets)
            var launched = scr_System_CampaignManager.current.FindInstanceByID(rec.charaRef);
            RecreationUtility.OnActivityAttended(launched, BookingOf(launched));
        }
        EndWaiting();
        StartBeginEvent();
        // gathering context leaves the gather room; execution rooms follow the dispatched participants (LastUpdate)
        RefreshActivityRooms();
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
        if (_handlingFailure || phase == ActivityPhase.Ended) return;
        _handlingFailure = true;
        try
        {
            phase = ActivityPhase.Failed;
            ReleaseReservedResources();
            ReleaseFacility();
            foreach (var rec in participants)
            {
                if (rec.state != ActivityParticipantState.Present) continue;
                rec.state = ActivityParticipantState.Released;
                rec.waited = true;
            }
            EndWaiting();
            foreach (var refID in actorRefID.ToList()) RemoveActor(refID);
            FireOutcomeEvent(FailedEventID, null, OutcomeSelf());
            if (!string.IsNullOrEmpty(VisitActivity?.reserveFurnitureTag)) CancelReservedVisit();
            if (scr_System_CentralControl.current.LogPrefs.DLog_Jobs) Debug.Log($"{DisplayName} gathering failed ({reason})");
        }
        finally { _handlingFailure = false; }
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
    EventInstance FireOutcomeEvent(string eventID, Character_Trainable latecomer, Character_Trainable self, Dictionary<string, List<int>> targets = null, bool withInstanceKey = false, bool isJoin = false)
    {
        if (string.IsNullOrEmpty(eventID) || self == null) return null;
        var ev = new EventInstance(self, eventID, "");
        ev.displayOverride = self.RefID == 0;
        foreach (var kvp in targets ?? BuildEventTargets(latecomer))
        {
            var list = kvp.Value.Select(r => scr_System_CampaignManager.current.FindInstanceByID(r)).Where(x => x != null).ToList();
            if (list.Count > 0) ev.Targets[kvp.Key] = list;
        }
        if (!isJoin)
        {
            var room = latecomer == null ? ParentRoom : scr_System_CampaignManager.current.GetCharaRoomInstance(latecomer.RefID);
            var present = LocalCompanions(room, latecomer?.RefID ?? -1);
            if (latecomer == null)
            {
                if (present.Count > 0) ev.Targets["attendees"] = present;
                if (present.Count >= 2) ev.AppendStrings["hasSharedAttendees"] = new List<string>() { "true" };
                if (present.Count > 0) ev.Targets["participating"] = present.ToList();
            }
            else
            {
                ev.Targets["joining"] = new List<Character_Trainable>() { latecomer };
                if (present.Count > 0) ev.Targets["companions"] = present;
                ev.Targets["participating"] = present.Concat(new[] { latecomer }).ToList();
            }
        }
        else ev.Targets["participating"] = ev.Targets["participants"].ToList();
        if (ev.Targets.TryGetValue("companions", out var companions) && companions.Count > 0)
            ev.AppendStrings["hasCompanions"] = new List<string>() { "true" };
        if (isJoin) ev.AppendStrings["isJoin"] = new List<string>() { "true" };
        if (phase == ActivityPhase.Launched && VisitActivity?.trustRewardEligible == true)
            ev.AppendStrings["activityTrustEligible"] = new List<string>() { "true" };
        if (latecomer != null) ev.AppendStrings["isLate"] = new List<string>() { "true" };
        // presence flags of the waited / late targets, so the event can branch on them (ExistAppendStrings)
        if (ev.Targets.ContainsKey("waited")) ev.AppendStrings["hasWaited"] = new List<string>() { "true" };
        if (ev.Targets.ContainsKey("late")) ev.AppendStrings["hasLate"] = new List<string>() { "true" };
        ev.AppendStrings[phase == ActivityPhase.Failed ? "activityFailed" : "activityRunning"] = new List<string>() { "true" };
        ev.AppendStrings["activityName"] = new List<string>() { string.IsNullOrEmpty(activityNameKey) ? "" : LocalizeDictionary.QueryThenParse(activityNameKey) };
        // a media visit's item ($mediaName$ - RecreationActivity.requireMedia)
        if (!string.IsNullOrEmpty(mediaItemID))
            ev.AppendStrings["mediaName"] = new List<string>() { Masterlist_Items.Instance?.Index.GetByID(mediaItemID)?.DisplayName ?? mediaItemID };
        if (withInstanceKey || isJoin) ev.AppendStrings["instanceKey"] = new List<string>() { key.Serialize() };
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
        // Once checked in, the ordinary home behavior (including sleep) owns this guest.
        if (HasOvernightGuest(c)) return false;
        if (!string.IsNullOrEmpty(VisitActivity?.reserveFurnitureTag)) return DispatchReserved(c);
        if (UsesChildJob) return DispatchChild(c);
        var fac = VisitActivity?.facility;
        if (fac != null) return DispatchFacility(c, fac);
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
        ReleaseReservedResources();
        ReleaseFacility();
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

        if (setupPending)
        {
            RetryDueSetup();   // the venue normally does this at T first
            if (setupPending) { CheckGather(); return; }   // still before T: presence only
            if (phase != ActivityPhase.Gathering) return;
        }
        SyncReservationParticipants();
        if (phase == ActivityPhase.Ended) return;
        CheckFacility();
        if (phase == ActivityPhase.Gathering) CheckGather();
        // this coordinator's own room associations only: movement, breaks, child jobs and phase changes reconcile here
        if (phase == ActivityPhase.Gathering || phase == ActivityPhase.Launched) RefreshActivityRooms();
    }
}
