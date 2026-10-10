using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

/// <summary>How an offer reaches characters: Fixed = posted by date at its fixed time; Flexible = planned into free time like a membership activity.</summary>
public enum RecreationOfferMode { Fixed, Flexible }

/// <summary>
/// A public activity at an existing location, needing no membership (give the venue's commands no membership gate). It
/// is an activity like a membership's (AsActivity - its workModule, behaviorOverrides, Tags, AcceptanceMods drive the
/// visit; no MemberType needed). Authored in the venue's data file ("MapPlans" -> "recreationOffers",
/// Index_MapPlan) and collected by every world that initializes the venue (WorldPlan.AllRecreationOffers); template data, never saved.
/// <br/>Fixed (fireworks, the new-year shrine visit): posted on the campaign's RecreationBoard by its calendar (dates /
/// weekdays) or by the AddRecreationOffer event Result, each posting a hostless World session at its fixed time
/// (RecreationUtility.PostOfferSession) that everyone in the venue's world sees and decides on.
/// <br/>Flexible (the riverside stroll): never posted; every eligible character gets it as a flexible candidate next to
/// their membership activities (RecreationUtility.CollectCandidates), placed in free time with the placement fields
/// below (AsActivity). Its invite visibility may only be Private or Friends.
/// </summary>
public class RecreationOfferDef
{
    public string ID = "";
    public RecreationOfferMode mode = RecreationOfferMode.Fixed;
    /// <summary>Venue: any faction with a MainExit (the booking's travel target).</summary>
    public string factionID = "";
    public int startHour = 18;
    /// <summary>1-24, may run past midnight.</summary>
    public int hours = 2;

    /// <summary>
    /// Optional MemberType the visitor acts under during the visit (Character_Factions.GetScheduledMemberType). Not
    /// needed to run the visit: the offer's own workModule / behaviorOverrides / Tags / AcceptanceMods do that (see
    /// RecreationActivity) - only for a type the visitor should also count as.
    /// </summary>
    public string memberTypeID = "";
    /// <summary>
    /// Copied into each booking: jobPostID / workCommands. Fixed: activeHours/activeDays unused (the offer's time is
    /// fixed). hourlyPayout is paid per hour attended - see RecreationActivity.workModule.
    /// </summary>
    public MapPlan.WorkModuleInit workModule = new MapPlan.WorkModuleInit();
    /// <summary>See RecreationActivity.entranceFee - the one-time price of a visit.</summary>
    public ItemEntry entranceFee = null;
    /// <summary>See RecreationActivity.gatherRoomID - where Job_Activity gathers this offer's participants ("" = the venue's MainExit).</summary>
    public string gatherRoomID = "";
    /// <summary>See RecreationActivity.launchEventID - the offer's begin event, fired when Job_Activity launches it ("" = silent launch).</summary>
    public string launchEventID = "";
    /// <summary>See RecreationActivity.gatherFailedEventID - fired when the gathering fails at T+30 and for each latecomer after ("" = the generic Recreation_GatherFailed_Stay).</summary>
    public string gatherFailedEventID = "";
    /// <summary>See RecreationActivity.behaviorOverrides - applied while a visit of this offer is in effect.</summary>
    public Dictionary<string, FindJobNode> behaviorOverrides = null;
    /// <summary>See RecreationActivity.Tags.</summary>
    public List<string> Tags = new List<string>();
    /// <summary>See RecreationActivity.AcceptanceMods.</summary>
    public List<PersonalityAcceptanceMod> AcceptanceMods = null;

    /// <summary>Flexible: its weight among the character's other flexible candidates (RecreationActivity.baseWeight). Fixed: unused (sessions are ranked by preference).</summary>
    public float weight = 1f;
    /// <summary>Chance each eligible NPC wants to go at all.</summary>
    public float participationChance = 1f;
    /// <summary>No other visit to this offer within this many days either side (0 = only never twice the same day) - e.g. one new-year visit over 01-01..01-03.</summary>
    public int minDaysBetween = 0;
    /// <summary>Actor tags (Utility.GetActorTag) an NPC must all have / must have none of to be eligible.</summary>
    public List<string> requireActorTags = new List<string>();
    public List<string> excludeActorTags = new List<string>();

    /// <summary>Visits to this offer can't be cancelled by the player's request (RecreationBooking.forbidCancel).</summary>
    public bool forbidCancel = false;

    /// <summary>
    /// Fixed: the session's roles and how participants may extend the invitation (its visibility is always World).
    /// Flexible: whether a visit becomes a session (Private / Friends only) and who comes. Null = RecreationInviteSpec.Default.
    /// </summary>
    public RecreationInviteSpec invite = null;

    /// <summary>Flexible only: visit length range in hours (clamped to 2-4, like RecreationActivity's). workModule.activeHours / activeDays = when a visit may be.</summary>
    public int minHours = 2;
    public int maxHours = 2;
    /// <summary>Flexible only: soft weekly target (see RecreationActivity.weeklyTarget). Its weight is `weight`, its cooldown minDaysBetween.</summary>
    public int weeklyTarget = 0;

    /// <summary>
    /// How many days ahead it may be booked. Fixed: the calendar posts a date's session this many days before it
    /// (PostCalendar). Flexible: as RecreationActivity.daysInAdvance (0 = only on the day itself).
    /// </summary>
    public int daysInAdvance = 1;

    [JsonIgnore] RecreationActivity _activity = null;
    /// <summary>
    /// The offer as a RecreationActivity (ID = the def's ID): the planner's placement code (Flexible), and what a visit
    /// of it applies while in effect (RecreationUtility.ResolveActivity - workModule, entranceFee, behaviorOverrides, Tags, AcceptanceMods).
    /// </summary>
    [JsonIgnore] public RecreationActivity AsActivity
    {
        get
        {
            if (_activity == null)
                _activity = new RecreationActivity()
                {
                    ID = ID,
                    workModule = workModule,
                    minHours = minHours,
                    maxHours = maxHours,
                    minDaysBetween = minDaysBetween,
                    weeklyTarget = weeklyTarget,
                    baseWeight = weight,
                    daysInAdvance = daysInAdvance,
                    forbidCancel = forbidCancel,
                    invite = invite,
                    entranceFee = entranceFee,
                    gatherRoomID = gatherRoomID,
                    launchEventID = launchEventID,
                    gatherFailedEventID = gatherFailedEventID,
                    behaviorOverrides = behaviorOverrides,
                    Tags = Tags,
                    AcceptanceMods = AcceptanceMods,
                };
            return _activity;
        }
    }

    /// <summary>Calendar: "MM-DD" dates it is posted on every year.</summary>
    public List<string> dates = new List<string>();
    /// <summary>Calendar: weekly days it is posted on, index 0 = Monday ... 6 = Sunday, 1 = posted (empty = none).</summary>
    public List<int> weekdays = new List<int>();

    /// <summary>The activity's name: its workModule.jobPostID localized (also the booking's schedule name), else its ID.</summary>
    [JsonIgnore] public string DisplayName
    {
        get { return workModule != null && !string.IsNullOrEmpty(workModule.jobPostID) ? LocalizeDictionary.QueryThenParse(workModule.jobPostID) : ID; }
    }

    /// <summary>Whether the calendar posts this offer on date (no dates and no weekdays = event-posted only).</summary>
    public bool MatchesCalendar(DateTime date)
    {
        if (dates != null && dates.Contains(date.ToString("MM-dd"))) return true;
        int weekday = ((int)date.DayOfWeek + 6) % 7;
        return weekdays != null && weekday < weekdays.Count && weekdays[weekday] != 0;
    }

    public bool IsEligible(Character_Trainable c)
    {
        if ((requireActorTags == null || requireActorTags.Count == 0) && (excludeActorTags == null || excludeActorTags.Count == 0)) return true;
        var tags = new List<string>();
        UtilityEX.GetActorTag(ref tags, c);
        if (requireActorTags != null && requireActorTags.Exists(t => !tags.Contains(t))) return false;
        if (excludeActorTags != null && excludeActorTags.Exists(t => tags.Contains(t))) return false;
        return true;
    }
}

/// <summary>
/// One posted fixed offer: RecreationOfferDef defID on absolute day `day`, optionally at another time than the def's.
/// Its session (the one characters take part in) is created alongside it (RecreationUtility.PostOfferSession); this
/// record keeps the posted hours for the RequireRoomExisting.requireActiveRecreationOffer command gate (GetUnderway).
/// </summary>
public class RecreationOffer
{
    public string defID = "";
    public int day = 0;
    /// <summary>-1 = the def's own.</summary>
    public int startHour = -1;
    public int hours = -1;
    /// <summary>Posting order (RecreationBoard.LatestSerial right after it was posted).</summary>
    public int serial = 0;

    [JsonIgnore] public RecreationOfferDef Def { get { return RecreationBoard.FindDef(defID); } }
    [JsonIgnore] public int StartHour { get { return startHour >= 0 ? startHour : (Def?.startHour ?? 0); } }
    [JsonIgnore] public int Hours { get { return hours > 0 ? hours : (Def?.hours ?? 0); } }
    [JsonIgnore] public int AbsStart { get { return RecreationBooking.AbsoluteHour(day, StartHour); } }
    [JsonIgnore] public int AbsEnd { get { return AbsStart + Hours; } }
}

/// <summary>
/// Campaign-wide list of posted fixed recreation offers (saved with the campaign). Each day's calendar offers are posted
/// each def's daysInAdvance days ahead (PostCalendar - once a day, whichever character plans first); the
/// AddRecreationOffer event Result posts more. Each posting creates a hostless World session
/// (RecreationUtility.PostOfferSession); characters find it in their world's session list, never by a push from here.
/// </summary>
public class RecreationBoard
{
    [JsonProperty] List<RecreationOffer> offers = new List<RecreationOffer>();
    /// <summary>Absolute day the calendar was last run (PostCalendar - -1 = never).</summary>
    [JsonProperty] int lastCalendarDay = -1;
    /// <summary>Counts every offer ever posted; each offer keeps its value (RecreationOffer.serial).</summary>
    [JsonProperty] int postSerial = 0;

    /// <summary>Serial of the newest posted offer (0 = none yet). Characters compare it with their own last seen one.</summary>
    [JsonIgnore] public int LatestSerial { get { return postSerial; } }

    /// <summary>
    /// The posted offer from one of defIDs under way right now at factionID (its posted start/length cover the current
    /// hour - yesterday's included, for one running past midnight), or null. Read by the
    /// RequireRoomExisting.requireActiveRecreationOffer command gate.
    /// </summary>
    public RecreationOffer GetUnderway(IList<string> defIDs, string factionID)
    {
        if (defIDs == null || defIDs.Count == 0 || string.IsNullOrEmpty(factionID)) return null;
        int today = scr_System_Time.current.getAbsoluteDay();
        PostCalendar();   // today's calendar offers exist even if no character has planned today
        int nowAbs = RecreationBooking.AbsoluteHour(today, scr_System_Time.current.getCurrentTime().Hour);
        foreach (var o in offers)
        {
            if (o.day < today - 1 || o.day > today || !defIDs.Contains(o.defID)) continue;
            var def = o.Def;
            if (def == null || def.factionID != factionID) continue;
            if (o.AbsStart <= nowAbs && nowAbs < o.AbsEnd) return o;
        }
        return null;
    }

    /// <summary>The offer def with this ID from the campaign's loaded worlds (WorldPlan.AllRecreationOffers - a later entry wins), or null.</summary>
    public static RecreationOfferDef FindDef(string id)
    {
        if (string.IsNullOrEmpty(id) || scr_System_CampaignManager.current == null) return null;
        RecreationOfferDef found = null;
        foreach (var world in scr_System_CampaignManager.current.GetLoadedWorldPlans())
            foreach (var def in world.AllRecreationOffers) if (def != null && def.ID == id) found = def;
        return found;
    }

    /// <summary>
    /// Posts fixed offer defID on absolute day `day` unless already posted there, with its hostless World session
    /// (RecreationUtility.PostOfferSession). False if the def is unknown or Flexible (flexible offers are never posted).
    /// </summary>
    public bool Post(string defID, int day, int startHour = -1, int hours = -1)
    {
        var def = FindDef(defID);
        if (def == null) return false;
        if (def.mode != RecreationOfferMode.Fixed)
        {
            UnityEngine.Debug.LogError($"RecreationBoard.Post: offer [{defID}] is {def.mode} - only fixed offers are posted");
            return false;
        }
        if (offers.Exists(o => o.defID == defID && o.day == day)) return true;
        var offer = new RecreationOffer() { defID = defID, day = day, startHour = startHour, hours = hours, serial = ++postSerial };
        offers.Add(offer);
        RecreationUtility.PostOfferSession(def, offer);
        return true;
    }

    /// <summary>
    /// Once a day: posts each fixed offer's calendar dates from today up to its daysInAdvance days ahead (already posted
    /// ones are skipped - Post), and drops past offers. Run lazily by RecreationUtility.DailyPlan (whichever character
    /// plans first) and GetUnderway; a no-op for the rest of the day.
    /// </summary>
    public void PostCalendar()
    {
        int today = scr_System_Time.current.getAbsoluteDay();
        if (lastCalendarDay == today) return;
        lastCalendarDay = today;
        offers.RemoveAll(o => o.day < today - 1);   // yesterday's may still run past midnight

        var defs = scr_System_CampaignManager.current.GetLoadedWorldPlans()
            .SelectMany(w => w.AllRecreationOffers).Where(d => d != null && d.mode == RecreationOfferMode.Fixed).ToList();
        foreach (var def in defs)
        {
            for (int ahead = 0; ahead <= Math.Max(0, def.daysInAdvance); ahead++)
            {
                var date = scr_System_Time.current.getStartTime().Date.AddDays(today + ahead);
                if (def.MatchesCalendar(date)) Post(def.ID, today + ahead);
            }
        }
    }
}
