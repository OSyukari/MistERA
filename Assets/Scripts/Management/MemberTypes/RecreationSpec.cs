using System.Collections.Generic;

/// <summary>
/// MemberType.recreation: makes a MemberType a recreation membership the daily RecreationUtility.DailyPlan books visits for
/// (sports club, salon, cram school...). The holder joins through Character_Factions.AddRecreationFaction (JoinHandler_Recreation);
/// the membership fee stays on MemberType.membershipFee - one fee per member, however many activities they use. Each
/// planned visit is ONE of the activities, booked as a RecreationBooking with its own copy of that activity's workModule.
/// Inherited by child MemberTypes like any other field.
/// </summary>
public class RecreationSpec
{
    /// <summary>The bookable activities of this membership (gym training, yoga, swimming...). A visit picks one.</summary>
    public List<RecreationActivity> activities = new List<RecreationActivity>();

    /// <summary>The activity with this ID, or null.</summary>
    public RecreationActivity GetActivity(string activityID)
    {
        if (activities == null || string.IsNullOrEmpty(activityID)) return null;
        return activities.Find(a => a != null && a.ID == activityID);
    }
}

/// <summary>
/// One bookable activity - of a recreation membership (RecreationSpec.activities), or a recreation offer's
/// (RecreationOfferDef.AsActivity). Its workModule drives the visit like a shift drives a worker: the booking copies its
/// schedule part, and the default behavior_job (TryFindScheduledJobNode, a booked visit's branch) does one random
/// workCommand after another, wherever in the venue the furniture offering it stands. Like a MemberType it may also
/// carry behaviorOverrides, Tags and AcceptanceMods, applied while a visit of it is in effect - on top of (and before)
/// the MemberType's - see Character_Factions.GetScheduledActivity. Money: entranceFee once per visit, at the activity's
/// launch (RecreationUtility.ChargeEntranceFee), workModule.hourlyPayout per hour attended (RecreationUtility.BillVisitHour). Template data: a booking finds it live
/// (RecreationUtility.ResolveActivity), never saves it.
/// </summary>
public class RecreationActivity
{
    /// <summary>Unique within the membership; with the faction ID it keys the activity's cooldown / weekly target (faction ID + ":" + this).</summary>
    public string ID = "";

    /// <summary>Stable conflict/merge identity; empty uses ID, never a localized name or cooldown tag.</summary>
    public string typeID = "";
    public bool allowMultipleInstances = true;
    public bool mergeWithSame = false;
    /// <summary>Other type IDs forbidden in the same execution room; either side's declaration counts.</summary>
    public List<string> conflictsWith = new List<string>();
    /// <summary>Covered furniture command tags. Empty infers the facility's comTag.</summary>
    public List<string> replaceCommandTags = new List<string>();
    /// <summary>Covered command IDs (including variants). Empty infers workModule.workCommands.</summary>
    public List<string> replaceCommands = new List<string>();
    /// <summary>Enrolled execution commands on furniture/the linked child job. Empty keeps workCommands; entry always uses wait/join.</summary>
    public List<string> activityCommands = new List<string>();

    public string TypeID { get { return string.IsNullOrEmpty(typeID) ? ID : typeID; } }

    /// <summary>
    /// Template of every visit's work module: jobPostID (the activity's name) / workCommands / peakHours are copied into
    /// each booking. activeHours here = the hours of day a visit may cover (empty = any hour); activeDays = the days it may
    /// be booked on (7 or 14 entries like a shift's, empty = every day). Not a fixed schedule - the planner picks the hours.
    /// <br/>hourlyPayout, read live for every hour the visitor is at the venue (RecreationUtility.BillVisitHour): the venue
    /// pays the visitor's payer faction like a wage (Obligation_Salary - paymentCadence, onPaidEventID / onFailedEventID).
    /// hourlyCost is not used for visits - a visit's price is entranceFee.
    /// </summary>
    public MapPlan.WorkModuleInit workModule = new MapPlan.WorkModuleInit();

    /// <summary>
    /// One-time price of a visit (null / 0 = free): charged once, when Job_Activity launches the activity (late joiners:
    /// on joining; a visit with no job to take part in: on reaching the venue - RecreationUtility.ChargeEntranceFee), to the visitor's payer (Character_Factions.GetWorkFactionSourceOrDefault - the
    /// priority home), settled at the end of the day (Obligation_ActivityFee - player payers only). A membership
    /// activity whose fee covers it leaves it empty.
    /// </summary>
    public ItemEntry entranceFee = null;

    /// <summary>
    /// The room Job_Activity gathers this activity's participants in before launch (a room ID at the venue). "" = the
    /// venue's MainExit room.
    /// </summary>
    public string gatherRoomID = "";
    /// <summary>
    /// Furniture the activity is bound to (RecreationFacility - found by the command it offers, never by room ID), or
    /// null. Its room becomes the gather room, chosen when the activity's job is created (late binding - the player may
    /// move furniture). Not used together with reserveFurnitureTag.
    /// </summary>
    public RecreationFacility facility = null;

    /// <summary>
    /// Who may take part (chara_conditions, self = the booker / invitee / joiner), checked with the planner's activity
    /// rules (RecreationUtility.ActivityAllows) - so it gates booking, invitations and joining alike. Null = anyone.
    /// </summary>
    public Event.EventScope_Self eligibility = null;

    /// <summary>
    /// A media item (ItemComponent_Media - a movie, a game) from the venue's (the home's) inventory the visit is held
    /// around, or null. One is reserved when the visit is booked (RecreationBooking / RecreationGroup.mediaItemID), its
    /// runtime sets the booking's length (whole hours, not clamped to minHours-maxHours), and the begin event's LaunchJob
    /// runs it as a Job_MediaSession (fixed length; participants repeat a short silent command, may break and rejoin).
    /// </summary>
    public RecreationMediaRequirement requireMedia = null;

    /// <summary>
    /// Shared cooldown / pending-reservation key ("" = faction ID + ":" + ID): activities with the same tag count as one -
    /// e.g. "medical_checkup", so a character gets one checkup per minDaysBetween whoever provides it (a family doctor at
    /// home, a clinic later). Also the booking's sourceKey.
    /// </summary>
    public string cooldownTag = "";

    /// <summary>One tagged furniture unit per visit/session; its room overrides gatherRoomID.</summary>
    public string reserveFurnitureTag = "";
    public int reserveCapacity = 0;
    /// <summary>Non-empty enables a fixed 22:00-08:00 temporary-home stay in the reserved private room.</summary>
    public string overnightGuestMemberTypeID = "";

    /// <summary>
    /// Event fired when Job_Activity launches this activity (the begin event - the official "activity begins"; the job
    /// injects the participant lists as its targets and snapshots the event's final targets afterwards). "" = no event:
    /// a silent launch.
    /// </summary>
    public string launchEventID = "";

    /// <summary>
    /// Event fired when the gathering fails at T+30 (no NPC participant there, or a mandatory one missing -
    /// Job_Activity.Fail), and again for each latecomer arriving afterwards (isLate). Its Results are the consequences
    /// (e.g. cancelling bookings). Gets the same injected targets / AppendStrings as the begin event (see
    /// Job_Activity.BuildEventTargets / FireOutcomeEvent) - self only decides visibility. "" = the generic
    /// Recreation_GatherFailed_Stay (no consequence: everyone sandboxes the venue while their booking lasts);
    /// Recreation_GatherFailed_Leave cancels the bookings of those who waited (they go home) - both in
    /// Data/Events/Recreation/recreation_activity_events.json; or a custom event.
    /// </summary>
    public string gatherFailedEventID = "";

    /// <summary>
    /// Behavior nodes swapped in while a visit of this activity is in effect, keyed by FindJobNode.behaviorOverrideID -
    /// same shape as MemberType.behaviorOverrides, and checked before the acting MemberType's (FindJobNodeRoot.TryGetJob).
    /// Null = none (the booked visit runs its workModule through the default tree).
    /// </summary>
    public Dictionary<string, FindJobNode> behaviorOverrides = null;

    /// <summary>Actor tags added while a visit of this activity is in effect (Utility.GetActorTag), next to the MemberType's Tags.</summary>
    public List<string> Tags = new List<string>();

    /// <summary>
    /// PersonalityAcceptanceMods applied while a visit of this activity is in effect (EvaluationPackage.ApplyPersonalityMods,
    /// with the MemberType's) - e.g. more accepting while out to have fun. Null = none.
    /// </summary>
    public List<PersonalityAcceptanceMod> AcceptanceMods = null;

    /// <summary>
    /// Optional outfit (Character_Wardrobe) worn during a visit of this activity - e.g. a swimsuit. Copied into the
    /// visitor's wardrobe under OutfitUtility.ActivityKey (once per key; kept if the wardrobe already has one). Its key is ignored.
    /// </summary>
    public OutfitSet outfit = null;

    /// <summary>This activity's override for FindJobNode.behaviorOverrideID, or null.</summary>
    public FindJobNode GetBehaviorOverride(string behaviorOverrideID)
    {
        if (behaviorOverrides == null || string.IsNullOrEmpty(behaviorOverrideID)) return null;
        return behaviorOverrides.TryGetValue(behaviorOverrideID, out var node) ? node : null;
    }

    /// <summary>Visit length range in hours, clamped to RecreationUtility.MinSegmentHours-MaxSegmentHours (1-4; default 2-3).</summary>
    public int minHours = 2;
    public int maxHours = 3;

    /// <summary>Hard cooldown: at least this many days from one visit of this activity to the next (0 = none; never twice on one day).</summary>
    public int minDaysBetween = 0;

    /// <summary>Soft weekly target: visits over the last 7 days below it raise the odds of booking, at or above it lower them (0 = no target).</summary>
    public int weeklyTarget = 0;

    /// <summary>Relative weight against the character's other candidates for the same day.</summary>
    public float baseWeight = 1f;

    /// <summary>
    /// How many days ahead a visit may be booked: 0 = only on the day itself (planned that day's midnight), 1 = from the
    /// day before (default). The planner looks at most one day ahead anyway (RecreationUtility.DailyPlan); invitations
    /// to a session of it follow the same limit (RecreationUtility.ActivityAllows).
    /// </summary>
    public int daysInAdvance = 1;

    /// <summary>Whether a visit on the day daysLookahead from today may be booked now (daysInAdvance).</summary>
    public bool AllowsAdvance(int daysLookahead) { return daysLookahead <= daysInAdvance; }

    /// <summary>Visits of this activity can't be cancelled by the player's request (RecreationBooking.forbidCancel).</summary>
    public bool forbidCancel = false;

    /// <summary>Allows extra participants to walk in, independently of invitation visibility. Conservative default: false.</summary>
    public bool allowWalkIn = false;

    /// <summary>Reserved for future activity-level trust payouts, not the generic 0.1 interaction reward. Conservative default: false.</summary>
    public bool trustRewardEligible = false;

    /// <summary>Who a character booking a visit invites along (null = RecreationInviteSpec.Default; solo = nobody).</summary>
    public RecreationInviteSpec invite = null;

    /// <summary>
    /// Whether day (daysLookahead from today) is allowed by workModule.activeDays, and - venue given - not a day off it is
    /// closed on (workModule.holidayPayoutMultiplier 0, the venue's world calendar).
    /// </summary>
    public bool AllowsDay(int daysLookahead, Manageable venue = null)
    {
        return workModule == null || (MapPlan.WorkModuleInit.IsDayActive(workModule.activeDays, daysLookahead) && workModule.RunsOnDay(venue, daysLookahead));
    }

    /// <summary>Whether hour of day may be covered by a visit (workModule.activeHours, empty = any).</summary>
    public bool AllowsHour(int hourOfDay)
    {
        return workModule == null || workModule.activeHours == null || workModule.activeHours.Count == 0 || workModule.activeHours.Contains(hourOfDay);
    }

    public int MinHours { get { return System.Math.Max(RecreationUtility.MinSegmentHours, System.Math.Min(minHours, RecreationUtility.MaxSegmentHours)); } }
    public int MaxHours { get { return System.Math.Max(MinHours, System.Math.Min(maxHours, RecreationUtility.MaxSegmentHours)); } }
}

/// <summary>RecreationActivity.requireMedia: which media item the visit needs (ItemComponent_Media) and what its participants do during it.</summary>
public class RecreationMediaRequirement
{
    /// <summary>The item's comp_Media.mediaTags must contain this (e.g. "movie", "game"). "" = any media.</summary>
    public string mediaTag = "";
    /// <summary>The short repeating command of the session (e.g. com_media_watch) - an item's own comp_Media.sessionCommandID wins.</summary>
    public string sessionCommandID = "";
}

/// <summary>
/// Legacy (RecreationFacility.reserve): still parsed from authored data, but imposes no exclusivity - activities never
/// hold furniture or rooms. Concurrency is RecreationActivity policy (allowMultipleInstances / conflictsWith / mergeWithSame).
/// </summary>
public enum FacilityReserve
{
    None,
    Unit,
    Room,
}

/// <summary>
/// RecreationActivity.facility: the furniture an activity needs, matched by the command it offers
/// (Job.HasAvailableCOMwithCOMTags) - so any furniture doing the job qualifies, wherever it stands. Rooms qualify by
/// their furniture; the room is chosen when the activity's job is created (RecreationUtility.FacilityRooms /
/// Job_Activity.ReserveFacility), never stored on a booking.
/// </summary>
public class RecreationFacility
{
    /// <summary>The pool: furniture at the venue offering a command with this comTag (e.g. training_shooting).</summary>
    public string comTag = "";
    public FacilityReserve reserve = FacilityReserve.None;
    /// <summary>Unit: attendees per unit (0 = no limit).</summary>
    public int unitCapacity = 0;
    /// <summary>The chosen room must hold at least this many units.</summary>
    public int minUnits = 1;
    /// <summary>Other command tags some furniture in the same room must offer (e.g. "rest" - somewhere to sit).</summary>
    public List<string> requireInRoom = new List<string>();
    /// <summary>Private rooms (Room_Instance.isRoomPrivate) qualify too. Default false: no movie night in someone's bedroom.</summary>
    public bool allowPrivateRooms = false;
    /// <summary>
    /// A room visit: only a private room the host owns at the venue qualifies (the host = the session's host / the solo
    /// visit's owner), and while the visit runs its participants may use that room's furniture (RecreationUtility.IsActivityGuestIn).
    /// </summary>
    public bool hostPrivateRoom = false;
    /// <summary>Participants are dispatched only within the facility room (TryFindScheduledActivityNode.RunVisit's restrictRoomList).</summary>
    public bool dispatchInRoom = true;
}
