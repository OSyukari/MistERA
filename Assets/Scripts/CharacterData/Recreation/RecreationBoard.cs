using System;
using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>How an offer reaches characters: Fixed = posted by date at its fixed time; Flexible = planned into free time like a membership activity.</summary>
public enum RecreationOfferMode { Fixed, Flexible }

/// <summary>
/// A public activity at an existing location, needing no membership (give the venue's commands no membership gate). It
/// is an activity like a membership's (AsActivity - its workModule, behaviorOverrides, Tags, AcceptanceMods drive the
/// visit; no MemberType needed). Authored in the venue's data file ("MapPlans" -> "recreationOffers",
/// Index_MapPlan) and collected by every world that initializes the venue (WorldPlan.AllRecreationOffers); template data, never saved.
/// <br/>Fixed (fireworks, the new-year shrine visit): posted on the campaign's RecreationBoard by its calendar (dates /
/// holiday and season tags / weekdays - MatchesCalendar) or by the AddRecreationOffer event Result, each posting a hostless World session at its fixed time
/// (RecreationUtility.PostOfferSession) that everyone in the venue's world sees and decides on.
/// <br/>Flexible (the riverside stroll): never posted; every eligible character gets it as a flexible candidate next to
/// their membership activities (RecreationUtility.CollectCandidates), placed in free time with the placement fields
/// below (AsActivity). Its invite visibility may only be Private or Friends.
/// </summary>
public class RecreationOfferDef
{
    public string ID = "";
    public string mainTypeTag = "";
    public List<string> secondaryTags = new List<string>();
    public List<RecreationWeightMod> weightMods = new List<RecreationWeightMod>();
    public string typeID = "";
    public bool allowMultipleInstances = true;
    public bool mergeWithSame = false;
    public List<string> conflictsWith = new List<string>();
    public List<string> replaceCommandTags = new List<string>();
    public List<string> replaceCommands = new List<string>();
    public List<string> activityCommands = new List<string>();
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
    /// <summary>See RecreationActivity.facility.</summary>
    public RecreationFacility facility = null;
    /// <summary>See RecreationActivity.eligibility (checked with requireActorTags / excludeActorTags - OfferAllows).</summary>
    public Event.EventScope_Self eligibility = null;
    public string reserveFurnitureTag = "";
    /// <summary>Maximum attendees in this activity; 0 = no activity-level limit.</summary>
    public int reserveCapacity = 0;
    /// <summary>Fixed 22:00-08:00 private stay; assigns this temporary-home type on launch.</summary>
    public string overnightGuestMemberTypeID = "";
    /// <summary>See RecreationActivity.launchEventID - the offer's begin event, fired when Job_Activity launches it ("" = silent launch).</summary>
    public string launchEventID = "";
    /// <summary>See RecreationActivity.gatherFailedEventID - fired when the gathering fails at T+30 and for each latecomer after ("" = the generic Recreation_GatherFailed_Stay).</summary>
    public string gatherFailedEventID = "";
    /// <summary>See RecreationActivity.endEventID - fired when a launched visit runs its course ("" = the generic Recreation_Activity_End).</summary>
    public string endEventID = "";
    /// <summary>See RecreationActivity.behaviorOverrides - applied while a visit of this offer is in effect.</summary>
    public Dictionary<string, FindJobNode> behaviorOverrides = null;
    /// <summary>See RecreationActivity.Tags.</summary>
    public List<string> Tags = new List<string>();
    /// <summary>See RecreationActivity.AcceptanceMods.</summary>
    public List<PersonalityAcceptanceMod> AcceptanceMods = null;
    /// <summary>See RecreationActivity.provideAcceptanceBonus.</summary>
    public int provideAcceptanceBonus = 0;
    /// <summary>See RecreationActivity.activityMoodlet.</summary>
    public RecreationActivityMoodlet activityMoodlet = null;
    /// <summary>See RecreationActivity.outfit (e.g. a yukata for a fireworks festival).</summary>
    public OutfitSet outfit = null;

    /// <summary>Base character preference: flexible selection weight, or deterministic session ranking score.</summary>
    public float weight = 1f;
    /// <summary>Chance each eligible NPC wants to go at all.</summary>
    public float participationChance = 1f;
    /// <summary>No other visit to this offer within this many days either side (0 = only never twice the same day) - e.g. one new-year visit over 01-01..01-03.</summary>
    public int minDaysBetween = 0;
    /// <summary>
    /// Shared cooldown / pending-reservation key ("" = the offer's own ID): offers with the same tag count as one - the same
    /// kind of activity at one venue (e.g. the cinema's screenings), so a character books one of them per minDaysBetween
    /// and holds one reservation of them at a time. Not the booking's sourceKey (that stays the ID - FindDef).
    /// </summary>
    public string cooldownTag = "";
    /// <summary>The key the cooldown / one-reservation rules count this offer under: cooldownTag, else the ID.</summary>
    [JsonIgnore] public string CooldownKey { get { return string.IsNullOrEmpty(cooldownTag) ? ID : cooldownTag; } }
    /// <summary>Actor tags (Utility.GetActorTag) an NPC must all have / must have none of to be eligible.</summary>
    public List<string> requireActorTags = new List<string>();
    public List<string> excludeActorTags = new List<string>();

    /// <summary>Visits to this offer can't be cancelled by the player's request (RecreationBooking.forbidCancel).</summary>
    public bool forbidCancel = false;

    /// <summary>Allows extra participants to walk in, independently of invitation visibility. Conservative default: false.</summary>
    public bool allowWalkIn = false;
    /// <summary>See RecreationActivity.npcWalkIn - with allowWalkIn, whether NPCs not booked may walk in too. Default true.</summary>
    public bool npcWalkIn = true;

    /// <summary>See RecreationActivity.trustRewardEligible - Trust between those who promised and showed up on time. Conservative default: false.</summary>
    public bool trustRewardEligible = false;

    /// <summary>
    /// Fixed: the session's roles and how participants may extend the invitation (its visibility is always World).
    /// Flexible: whether a visit becomes a session (Private / Friends only) and who comes. Null = RecreationInviteSpec.Default.
    /// </summary>
    public RecreationInviteSpec invite = null;

    /// <summary>Flexible only: visit length range in hours (clamped to 1-4, like RecreationActivity's). workModule.activeHours / activeDays = when a visit may be.</summary>
    public int minHours = 2;
    public int maxHours = 2;
    /// <summary>Flexible only: soft weekly target (see RecreationActivity.weeklyTarget). Its weight is `weight`, its cooldown minDaysBetween.</summary>
    public int weeklyTarget = 0;

    /// <summary>
    /// How many days ahead it may be booked. Fixed: the calendar posts a date's session this many days before it
    /// (PostCalendar). Flexible: as RecreationActivity.daysInAdvance (0 = only on the day itself).
    /// </summary>
    public int daysInAdvance = 0;

    [JsonIgnore] public int PlanningAdvanceDays { get { return AsActivity.PlanningAdvanceDays; } }
    public bool AllowsAdvance(int daysLookahead) { return AsActivity.AllowsAdvance(daysLookahead); }

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
                    mainTypeTag = mainTypeTag,
                    secondaryTags = secondaryTags,
                    weightMods = weightMods,
                    typeID = typeID,
                    allowMultipleInstances = allowMultipleInstances,
                    mergeWithSame = mergeWithSame,
                    conflictsWith = conflictsWith,
                    replaceCommandTags = replaceCommandTags,
                    replaceCommands = replaceCommands,
                    activityCommands = activityCommands,
                    workModule = workModule,
                    minHours = minHours,
                    maxHours = maxHours,
                    minDaysBetween = minDaysBetween,
                    weeklyTarget = weeklyTarget,
                    baseWeight = weight,
                    daysInAdvance = daysInAdvance,
                    forbidCancel = forbidCancel,
                    allowWalkIn = allowWalkIn,
                    npcWalkIn = npcWalkIn,
                    trustRewardEligible = trustRewardEligible,
                    invite = invite,
                    entranceFee = entranceFee,
                    gatherRoomID = gatherRoomID,
                    facility = facility,
                    eligibility = eligibility,
                    reserveFurnitureTag = reserveFurnitureTag,
                    reserveCapacity = reserveCapacity,
                    overnightGuestMemberTypeID = overnightGuestMemberTypeID,
                    launchEventID = launchEventID,
                    gatherFailedEventID = gatherFailedEventID,
                    endEventID = endEventID,
                    behaviorOverrides = behaviorOverrides,
                    Tags = Tags,
                    AcceptanceMods = AcceptanceMods,
                    provideAcceptanceBonus = provideAcceptanceBonus,
                    activityMoodlet = activityMoodlet,
                    outfit = outfit,
                };
            return _activity;
        }
    }

    /// <summary>Calendar source: "MM-DD" dates it is posted on every year.</summary>
    public List<string> dates = new List<string>();
    /// <summary>Calendar source: posted on a date whose holiday (HolidaySystem, the venue's world) has any of these tags - e.g. "newyear".</summary>
    public List<string> holidayTags = new List<string>();
    /// <summary>Calendar source: posted on a date covered by a season (HolidaySystem, the venue's world) with any of these tags - e.g. "hanabi".</summary>
    public List<string> seasonTags = new List<string>();
    /// <summary>
    /// Calendar filter: weekly days a source date must fall on, index 0 = Monday ... 6 = Sunday, 1 = allowed (empty = every
    /// day). With no source at all (no dates / holidayTags / seasonTags) it is the source itself: posted every allowed weekday.
    /// </summary>
    public List<int> weekdays = new List<int>();
    /// <summary>Calendar filter: never posted on a date whose holiday has any of these tags.</summary>
    public List<string> excludeHolidayTags = new List<string>();
    /// <summary>Calendar filter: never posted on a date covered by a season with any of these tags.</summary>
    public List<string> excludeSeasonTags = new List<string>();

    /// <summary>The activity's name: its workModule.jobPostID localized (also the booking's schedule name), else its ID.</summary>
    [JsonIgnore] public string DisplayName
    {
        get { return workModule != null && !string.IsNullOrEmpty(workModule.jobPostID) ? LocalizeDictionary.QueryThenParse(workModule.jobPostID) : ID; }
    }

    /// <summary>
    /// Whether the calendar posts this offer on date in world (whose holidays/seasons the tags are checked against): any
    /// source (dates / holidayTags / seasonTags) matches, then every filter (weekdays / excludeHolidayTags /
    /// excludeSeasonTags) passes. No source and no weekdays = event-posted only.
    /// </summary>
    public bool MatchesCalendar(WorldPlan world, DateTime date)
    {
        bool hasDates = dates != null && dates.Count > 0;
        bool hasHolidayTags = holidayTags != null && holidayTags.Count > 0;
        bool hasSeasonTags = seasonTags != null && seasonTags.Count > 0;
        bool hasWeekdays = weekdays != null && weekdays.Count > 0;
        if (!hasDates && !hasHolidayTags && !hasSeasonTags && !hasWeekdays) return false;

        if (hasDates || hasHolidayTags || hasSeasonTags)
        {
            bool source = (hasDates && dates.Contains(date.ToString("MM-dd")))
                || (hasHolidayTags && holidayTags.Exists(t => HolidaySystem.HolidayHasTag(world, date, t)))
                || (hasSeasonTags && seasonTags.Exists(t => HolidaySystem.InSeason(world, date, t)));
            if (!source) return false;
        }

        if (hasWeekdays)
        {
            int weekday = ((int)date.DayOfWeek + 6) % 7;
            if (weekday >= weekdays.Count || weekdays[weekday] == 0) return false;
        }
        if (excludeHolidayTags != null && excludeHolidayTags.Exists(t => HolidaySystem.HolidayHasTag(world, date, t))) return false;
        if (excludeSeasonTags != null && excludeSeasonTags.Exists(t => HolidaySystem.InSeason(world, date, t))) return false;
        return true;
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
/// Campaign-wide list of posted fixed recreation offers. Each day's calendar offers are posted each def's daysInAdvance
/// days ahead (PostCalendar - once a day, whichever character plans first); the AddRecreationOffer event Result posts
/// more. Each posting creates a hostless World session (RecreationUtility.PostOfferSession); characters find it in their
/// world's session list, never by a push from here.
/// <br/>World data, never saved: the posts are rebuilt from the loaded worlds on load (RebuildAfterLoad), and so are their
/// sessions nobody holds (RecreationGroupRegistry saves only those someone holds). The one saved part is eventOffers -
/// what AddRecreationOffer posted, which no world data reproduces.
/// </summary>
public class RecreationBoard
{
    /// <summary>The posted offers - rebuilt on load (RebuildAfterLoad), never saved.</summary>
    [JsonIgnore] List<RecreationOffer> offers = new List<RecreationOffer>();
    /// <summary>The AddRecreationOffer event Result's posts (PostOffer) - game state, not world data: the only saved part of the board.</summary>
    [JsonProperty] List<RecreationOffer> eventOffers = new List<RecreationOffer>();
    /// <summary>Absolute day the calendar was last run (PostCalendar - -1 = never; reset on load).</summary>
    [JsonIgnore] int lastCalendarDay = -1;
    /// <summary>Counts every offer posted since the campaign was loaded; each offer keeps its value (RecreationOffer.serial).</summary>
    [JsonIgnore] int postSerial = 0;

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
    /// (RecreationUtility.PostOfferSession). Flexible offers and private overnight stays are never World posts.
    /// byEvent: the AddRecreationOffer event Result's post - also kept in eventOffers (saved, re-posted on load).
    /// answerNow: every NPC who sees its session answers in their next late pass, not only at the day update
    /// (RecreationGroupRegistry.MarkAnswerNow - an already posted one too); a reloaded post waits for the day update.
    /// </summary>
    public bool Post(string defID, int day, int startHour = -1, int hours = -1, bool byEvent = false, bool answerNow = false)
    {
        var def = PostableDef(defID);
        if (def == null) return false;
        if (FindPosted(defID, day) == null)
        {
            var offer = new RecreationOffer() { defID = defID, day = day, startHour = startHour, hours = hours, serial = ++postSerial };
            if (byEvent && FindEventOffer(defID, day) == null) eventOffers.Add(offer);
            offers.Add(offer);
            RecreationUtility.PostOfferSession(def, offer);
        }
        if (answerNow)
        {
            var registry = scr_System_CampaignManager.current.RecreationGroups;
            var session = FindPostedSession(registry, defID, day);
            if (session != null) registry.MarkAnswerNow(session);
        }
        return true;
    }

    /// <summary>The open session posted for defID on `day`, or null (not placed - RecreationUtility.PostOfferSession may decline).</summary>
    static RecreationGroup FindPostedSession(RecreationGroupRegistry registry, string defID, int day)
    {
        var all = registry.All;
        for (int i = 0; i < all.Count; i++)
        {
            var g = all[i];
            if (g != null && g.IsPostedOffer && g.IsOpen && !g.concluded && g.sourceKey == defID && g.day == day) return g;
        }
        return null;
    }

    /// <summary>defID's def if it can be posted: known, Fixed (logged otherwise), not a private overnight stay - else null.</summary>
    static RecreationOfferDef PostableDef(string defID)
    {
        var def = FindDef(defID);
        if (def == null) return null;
        // A stay is a private per-booking room, never a shared hostless World crowd.
        if (!string.IsNullOrEmpty(def.overnightGuestMemberTypeID)) return null;
        if (def.mode != RecreationOfferMode.Fixed)
        {
            UnityEngine.Debug.LogError($"RecreationBoard.Post: offer [{defID}] is {def.mode} - only fixed offers are posted");
            return null;
        }
        return def;
    }

    RecreationOffer FindPosted(string defID, int day)
    {
        for (int i = 0; i < offers.Count; i++) if (offers[i] != null && offers[i].defID == defID && offers[i].day == day) return offers[i];
        return null;
    }

    RecreationOffer FindEventOffer(string defID, int day)
    {
        for (int i = 0; i < eventOffers.Count; i++) if (eventOffers[i] != null && eventOffers[i].defID == defID && eventOffers[i].day == day) return eventOffers[i];
        return null;
    }

    /// <summary>
    /// Once a day: posts each fixed offer's calendar dates from today up to its daysInAdvance days ahead (already posted
    /// ones are skipped - Post), and drops past offers. Run lazily by RecreationUtility.DailyPlan (whichever character
    /// plans first) and GetUnderway, and on load (RebuildAfterLoad); a no-op for the rest of the day.
    /// </summary>
    public void PostCalendar()
    {
        int today = scr_System_Time.current.getAbsoluteDay();
        if (lastCalendarDay == today) return;
        lastCalendarDay = today;
        // yesterday's may still run past midnight
        for (int i = offers.Count - 1; i >= 0; i--) if (offers[i] == null || offers[i].day < today - 1) offers.RemoveAt(i);
        for (int i = eventOffers.Count - 1; i >= 0; i--) if (eventOffers[i] == null || eventOffers[i].day < today - 1) eventOffers.RemoveAt(i);

        var ids = new List<string>();
        foreach (var world in scr_System_CampaignManager.current.GetLoadedWorldPlans())
            foreach (var d in world.AllRecreationOffers)
                if (d != null && !string.IsNullOrEmpty(d.ID) && !ids.Contains(d.ID)) ids.Add(d.ID);
        // each ID once, as the def that wins it (FindDef) - an overridden entry's calendar never posts
        foreach (var id in ids)
        {
            var def = FindDef(id);
            if (def == null || def.mode != RecreationOfferMode.Fixed) continue;
            int advance = def.PlanningAdvanceDays;
            if (advance < 0) continue;
            for (int ahead = 0; ahead <= advance; ahead++)
                if (CalendarPosts(def, today + ahead)) Post(def.ID, today + ahead);
        }
    }

    /// <summary>
    /// Fixed offer def's calendar falls on absolute day `day` (CalendarPosts) - whether or not it is posted yet. Read by
    /// the organization planner to keep performances off time-sensitive offers (RecreationUtility.PerformanceSlotConflicts).
    /// </summary>
    public static bool PostsOn(RecreationOfferDef def, int day) { return def != null && CalendarPosts(def, day); }

    /// <summary>
    /// The calendar posts def on absolute day `day`: its dates / holiday or season tags / weekdays match that date in the
    /// venue's world (any of them, for a venue in several worlds); a venue no loaded world initializes falls back to the
    /// worlds listing the offer.
    /// </summary>
    static bool CalendarPosts(RecreationOfferDef def, int day)
    {
        var campaign = scr_System_CampaignManager.current;
        var date = scr_System_Time.current.getStartTime().Date.AddDays(day);
        var worlds = campaign.FindWorldsContainingFaction(def.factionID);
        if (worlds.Count > 0)
        {
            for (int i = 0; i < worlds.Count; i++) if (def.MatchesCalendar(worlds[i], date)) return true;
            return false;
        }
        foreach (var world in campaign.GetLoadedWorldPlans())
        {
            bool lists = false;
            foreach (var d in world.AllRecreationOffers) if (d != null && d.ID == def.ID) { lists = true; break; }
            if (lists && def.MatchesCalendar(world, date)) return true;
        }
        return false;
    }

    /// <summary>
    /// After a save is loaded (scr_System_CampaignManager, right after RecreationGroupRegistry.OnAfterLoad): the posts are
    /// rebuilt from the loaded worlds, never taken from the save.
    /// <br/>- Every posted fixed offer's session the save still holds (an older save's unheld ones too): nobody holds it
    ///   (RecreationGroup.HoldsAnyone) = dropped silently (RecreationGroupRegistry.Remove); held and still what the data
    ///   posts there (MatchingPost - the same day, start and length, by the calendar or a saved event post) = its post
    ///   record is restored; held but no longer posted = cancelled with its announcement (OfferRemoved) on the first
    ///   update - events are not started while loading.
    /// <br/>- Then the calendar runs (PostCalendar) and the saved event posts are posted again: new sessions only where none
    ///   is held already.
    /// </summary>
    public void RebuildAfterLoad()
    {
        RecreationUtility.ResetFeeApprovals();
        offers.Clear();
        lastCalendarDay = -1;
        postSerial = 0;
        if (eventOffers == null) eventOffers = new List<RecreationOffer>();
        int today = scr_System_Time.current.getAbsoluteDay();
        for (int i = eventOffers.Count - 1; i >= 0; i--) if (eventOffers[i] == null || eventOffers[i].day < today - 1) eventOffers.RemoveAt(i);

        var registry = scr_System_CampaignManager.current.RecreationGroups;
        var unheld = new List<RecreationGroup>();
        List<RecreationGroup> removed = null;
        var all = registry.All;
        for (int i = 0; i < all.Count; i++)
        {
            var g = all[i];
            if (g == null || g.concluded || !g.IsPostedOffer) continue;
            // the player's answer to its notice keeps it too (RecreationGroup.HoldsPlayerAnswer - never asked again)
            if (!g.HoldsAnyone && !g.HoldsPlayerAnswer) { unheld.Add(g); continue; }
            var post = MatchingPost(g);
            if (post != null) offers.Add(post);
            else if (g.IsOpen) (removed ?? (removed = new List<RecreationGroup>())).Add(g);
        }
        for (int i = 0; i < unheld.Count; i++) registry.Remove(unheld[i]);

        PostCalendar();
        for (int i = 0; i < eventOffers.Count; i++)
        {
            var o = eventOffers[i];
            if (FindPosted(o.defID, o.day) != null) continue;
            var def = PostableDef(o.defID);
            if (def == null) continue;
            o.serial = ++postSerial;
            offers.Add(o);
            RecreationUtility.PostOfferSession(def, o);
        }

        if (removed != null)
            scr_UpdateHandler.current.AddEventCallback(() =>
            {
                for (int i = 0; i < removed.Count; i++) RecreationUtility.CancelRemovedOfferSession(removed[i]);
            });
    }

    /// <summary>
    /// The post record of held session g if the world data still posts it as it is - a saved event post of the same def
    /// and day, or the calendar's (def known and postable, the calendar posting it that day) - with the same start and
    /// length; null when it no longer does.
    /// </summary>
    RecreationOffer MatchingPost(RecreationGroup g)
    {
        var def = FindDef(g.sourceKey);
        if (def == null || def.mode != RecreationOfferMode.Fixed || !string.IsNullOrEmpty(def.overnightGuestMemberTypeID)) return null;
        var post = FindEventOffer(g.sourceKey, g.day);
        if (post == null)
        {
            if (!CalendarPosts(def, g.day)) return null;
            post = new RecreationOffer() { defID = def.ID, day = g.day };
        }
        if (post.StartHour != g.startHour || post.Hours != g.hours) return null;
        post.serial = ++postSerial;
        return post;
    }
}
