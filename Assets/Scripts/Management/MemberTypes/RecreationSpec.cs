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

    /// <summary>This activity's override for FindJobNode.behaviorOverrideID, or null.</summary>
    public FindJobNode GetBehaviorOverride(string behaviorOverrideID)
    {
        if (behaviorOverrides == null || string.IsNullOrEmpty(behaviorOverrideID)) return null;
        return behaviorOverrides.TryGetValue(behaviorOverrideID, out var node) ? node : null;
    }

    /// <summary>Visit length range in hours, clamped to RecreationUtility.MinSegmentHours-MaxSegmentHours (2-4).</summary>
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

    /// <summary>Who a character booking a visit invites along (null = RecreationInviteSpec.Default; solo = nobody).</summary>
    public RecreationInviteSpec invite = null;

    /// <summary>Whether day (daysLookahead from today) is allowed by workModule.activeDays.</summary>
    public bool AllowsDay(int daysLookahead)
    {
        return workModule == null || MapPlan.WorkModuleInit.IsDayActive(workModule.activeDays, daysLookahead);
    }

    /// <summary>Whether hour of day may be covered by a visit (workModule.activeHours, empty = any).</summary>
    public bool AllowsHour(int hourOfDay)
    {
        return workModule == null || workModule.activeHours == null || workModule.activeHours.Count == 0 || workModule.activeHours.Contains(hourOfDay);
    }

    public int MinHours { get { return System.Math.Max(RecreationUtility.MinSegmentHours, System.Math.Min(minHours, RecreationUtility.MaxSegmentHours)); } }
    public int MaxHours { get { return System.Math.Max(MinHours, System.Math.Min(maxHours, RecreationUtility.MaxSegmentHours)); } }
}
