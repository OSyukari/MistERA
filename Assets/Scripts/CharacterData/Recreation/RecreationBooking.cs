using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>Where a RecreationBooking came from - decides who may re-plan it (only the planner's own Membership/Offer bookings).</summary>
public enum RecreationBookingSource { Membership, Offer, Event }

/// <summary>
/// One planned recreation visit: `hours` hours from startHour of absolute campaign day `day`
/// (scr_System_Time.getAbsoluteDay) at faction factionID - it may run past midnight into the next day. Held in
/// Character_Factions' booking list. While it is in effect (Character_Factions.GetEffectiveBooking: after work unless
/// overrideWork, before home), Character_Factions.CurrentJobScheduleFaction returns that faction and the faction's
/// GetSchedule/GetHourlySchedule return workModule.CachedSchedule - so travel, TryFindScheduledJobNode, the schedule UI...
/// treat it like a shift. May be at the character's own home/work factions. Never a shift hour: Manageable.IsOnShift is
/// false while a booking is in effect, so it pays no salary and counts as no staff.
/// </summary>
public class RecreationBooking : I_ActivityBooking
{
    /// <summary>
    /// RefID of the character whose list holds this booking (-1 = not added yet). Set by Character_Factions.AddBooking
    /// (and on load for saves made before it existed - PostReloadUpdate_Recreation); Owner resolves it.
    /// </summary>
    public int ownerRef = -1;
    [JsonIgnore] Character_Trainable _owner = null;
    /// <summary>The character holding this booking (ownerRef through the campaign manager, cached), or null.</summary>
    [JsonIgnore] public Character_Trainable Owner
    {
        get
        {
            if (_owner == null && ownerRef >= 0 && scr_System_CampaignManager.current != null)
                _owner = scr_System_CampaignManager.current.FindInstanceByID(ownerRef);
            return _owner;
        }
    }

    /// <summary>Absolute day the booking STARTS on (scr_System_Time.getAbsoluteDay).</summary>
    public int day = 0;
    public int startHour = 0;
    /// <summary>1-24; startHour + hours past 24 continues into the next day.</summary>
    public int hours = 0;
    public string factionID = "";

    /// <summary>
    /// MemberType the character acts under while this booking is active (behaviorOverrides, Tags, AcceptanceMods - see
    /// Character_Factions.GetScheduledMemberType), e.g. the recreation membership type offered by the character's own
    /// home/work faction (visited without joining). Empty = the faction's own MemberType for the character (a
    /// membership's live type; none for a plain visitor). The visit's activity applies its own on top (GetActivity).
    /// </summary>
    public string memberTypeID = "";

    /// <summary>This booking's own copy of its source's work module: commands/jobPostID as authored, activeHours = the booked hours.</summary>
    public MapPlan.WorkModuleInit workModule = null;

    /// <summary>Membership visits: the RecreationActivity (RecreationSpec.activities) this visit is - its rules when re-planning. "" for offers/events.</summary>
    public string activityID = "";

    public RecreationBookingSource source = RecreationBookingSource.Membership;
    /// <summary>
    /// What produced it - also the cooldown/attendance key: a membership activity's "factionID:activityID", the offer's
    /// ID, or the event's template ID (else its faction ID).
    /// </summary>
    public string sourceKey = "";
    /// <summary>Event bookings: never moved or re-planned by the planner, only cancelled.</summary>
    public bool locked = false;
    /// <summary>
    /// Event bookings only: takes its hours even from work/home schedules (checked before work in
    /// Character_Factions.CurrentJobScheduleFaction, never broken by a schedule claiming them). Still yields to forbid-work.
    /// </summary>
    public bool overrideWork = false;
    /// <summary>Optional event started on the character when this booking is cancelled before it ends.</summary>
    public string onCancelledEventID = "";
    /// <summary>
    /// The player may not ask the character to cancel it (Opt_CancelBooking - listed, drawn disabled). From the
    /// activity / offer def it was planned from, or for an event booking its own flag OR its template's. Other
    /// cancellations still happen; those that should not (RecreationUtility.RecordCancelled) are logged as errors.
    /// </summary>
    public bool forbidCancel = false;
    /// <summary>Event bookings: the event that made it (SetRecreationBooking) - shown as who arranged it.</summary>
    public string sourceEventID = "";
    /// <summary>
    /// This booking is this character's part of a shared session (Session, runtime reference relinked on load by start
    /// hour + venue - RecreationUtility.RelinkAfterLoad) in role roleID. In effect only while the session is open and
    /// valid and they attend (RecreationUtility.IsSessionBookingLive); never moved by the planner.
    /// </summary>
    public bool isSessionBooking = false;
    public string roleID = "";
    [JsonIgnore] public RecreationGroup Session { get; set; }

    /// <summary>
    /// The RefID of the Job_Activity coordinating this visit (-1 = none). Only a SOLO visit is its own instance
    /// (RecreationUtility.ActivityKeyFor): a session booking's job ref lives on its Session
    /// (RecreationGroup.activityJobRef), which the ActivityJob getter follows.
    /// </summary>
    public int activityJobRef = -1;
    /// <summary>The coordinating Job_Activity of this visit's instance (the session's when this is a session booking), or null.</summary>
    [JsonIgnore] public Job ActivityJob
    {
        get
        {
            if (Session != null) return Session.ActivityJob;
            return scr_System_CampaignManager.current == null ? null : scr_System_CampaignManager.current.FindJobInstanceByID(activityJobRef, false);
        }
    }

    /// <summary>A flexible visit the planner placed (a membership activity, a flexible offer) - it may be moved to make room (RecreationUtility.IsMovableVisit).</summary>
    public bool flexible = false;

    /// <summary>Last absolute hour this visit was billed for (RecreationUtility.BillVisitHour; -1 = none) - so an hour is never billed twice.</summary>
    public int lastBilledAbs = -1;
    /// <summary>This visit's activity entranceFee has been charged (once per visit - RecreationUtility.ChargeEntranceFee, at the activity's launch / a late join / the plain-sandbox fallback).</summary>
    public bool entranceFeeCharged = false;

    [JsonIgnore] RecreationActivity _activity = null;
    /// <summary>
    /// The activity this booking is a visit of (RecreationUtility.ResolveActivity - template data, looked up live, never
    /// saved), cached once found; null when it is none (an event booking without a template, a MemberType's own workModule).
    /// </summary>
    public RecreationActivity GetActivity(Character_Factions f)
    {
        if (_activity == null) _activity = RecreationUtility.ResolveActivity(f, this);
        return _activity;
    }

    /// <summary>Saves made before sessions: the old group key. A booking still carrying one is dropped on load (Character_Factions.PostReloadUpdate_Recreation).</summary>
    [JsonProperty("groupUID")] int legacyGroupUID = 0;
    [JsonIgnore] public bool IsLegacyGroupBooking { get { return legacyGroupUID != 0; } }

    /// <summary>First booked hour as an absolute hour count (day * 24 + startHour) - see AbsoluteHour.</summary>
    [JsonIgnore] public int AbsStart { get { return day * 24 + startHour; } }
    /// <summary>First hour AFTER the booking, absolute.</summary>
    [JsonIgnore] public int AbsEnd { get { return AbsStart + hours; } }

    // ---------------- I_ActivityBooking (a session booking answers for its session) ---------------- //

    [JsonIgnore] public string DisplayName { get { return Session != null ? Session.DisplayName : RecreationUtility.ActivityDisplayName(workModule); } }
    [JsonIgnore] public System.DateTime StartTime { get { return RecreationUtility.AbsHourToDateTime(AbsStart); } }
    /// <summary>The owner as the participant (a session booking: its session's attending members), the gather floor and room, start and end hour.</summary>
    [JsonIgnore] public string Tooltip
    {
        get
        {
            if (Session != null) return Session.Tooltip;
            var owner = Owner;
            var participants = new List<Character_Trainable>();
            if (owner != null) participants.Add(owner);
            return RecreationUtility.BuildActivityDetail(participants, factionID, GetActivity(owner?.FactionManager), day, startHour, AbsEnd, source, workModule);
        }
    }

    /// <summary>Absolute hour count of hour on absolute day absDay.</summary>
    public static int AbsoluteHour(int absDay, int hour) { return absDay * 24 + hour; }

    public bool Covers(int absDay, int hour)
    {
        int t = AbsoluteHour(absDay, hour);
        return t >= AbsStart && t < AbsEnd;
    }

    /// <summary>Overlaps absolute hours [absStart, absEnd).</summary>
    public bool Overlaps(int absStart, int absEnd) { return AbsStart < absEnd && absStart < AbsEnd; }

    [JsonIgnore] Manageable _faction = null;
    [JsonIgnore] public Manageable Faction
    {
        get
        {
            if (_faction == null && !string.IsNullOrEmpty(factionID)) _faction = scr_System_CampaignManager.current.FindFactionByID(factionID);
            return _faction;
        }
    }

    [JsonIgnore] public MemberType MemberType
    {
        get { return !string.IsNullOrEmpty(memberTypeID) && FactionUtility.TryGetMemberType(memberTypeID, out var t) ? t : null; }
    }

    /// <summary>The hourly schedule this booking stands for (empty, so inactive, when its module has no commands - behavior then comes from MemberType's behavior_job override).</summary>
    [JsonIgnore] public Manageable.HourlySchedule Schedule
    {
        get { return workModule != null ? workModule.CachedSchedule : null; }
    }

    /// <summary>A copy of this booking cut to its first `hours` hours (shares workModule - template data, read-only) - for the past-booking record.</summary>
    public RecreationBooking CopyTruncated(int hours)
    {
        return new RecreationBooking()
        {
            ownerRef = ownerRef,
            day = day,
            startHour = startHour,
            hours = hours,
            factionID = factionID,
            memberTypeID = memberTypeID,
            workModule = workModule,
            activityID = activityID,
            source = source,
            sourceKey = sourceKey,
            locked = locked,
            overrideWork = overrideWork,
            onCancelledEventID = onCancelledEventID,
            forbidCancel = forbidCancel,
            sourceEventID = sourceEventID,
            isSessionBooking = isSessionBooking,
            roleID = roleID,
            Session = Session,
            flexible = flexible,
            lastBilledAbs = lastBilledAbs,
            entranceFeeCharged = entranceFeeCharged,
            activityJobRef = activityJobRef,
        };
    }

    /// <summary>USED FOR SERIALIZER ONLY</summary>
    public RecreationBooking() { }

    /// <summary>
    /// A booking of hours hours from startHour on absolute day `day` at factionID, its workModule copied from template
    /// (jobPostID, workCommands, peakHours - template's activeHours/activeDays describe when it MAY be booked and are
    /// replaced by the booked hours, wrapping past midnight). Returns null for an hour outside 0-23 or a length outside 1-24.
    /// </summary>
    public static RecreationBooking Create(int day, int startHour, int hours, string factionID, MapPlan.WorkModuleInit template,
        RecreationBookingSource source, string sourceKey, string memberTypeID = "", bool locked = false, string onCancelledEventID = "", bool overrideWork = false,
        string activityID = "", bool forbidCancel = false, string sourceEventID = "", RecreationGroup session = null, string roleID = "", bool flexible = false)
    {
        if (hours < 1 || hours > 24 || startHour < 0 || startHour > 23 || string.IsNullOrEmpty(factionID)) return null;

        var module = new MapPlan.WorkModuleInit();
        if (template != null)
        {
            module.jobPostID = template.jobPostID;
            module.workCommands = new List<string>(template.workCommands);
            module.peakHours = new List<int>(template.peakHours);
        }
        // informational only - the booking itself decides when it applies (Covers), not IsActiveAt
        for (int h = startHour; h < startHour + hours; h++) module.activeHours.Add(h % 24);

        return new RecreationBooking()
        {
            day = day,
            startHour = startHour,
            hours = hours,
            factionID = factionID,
            memberTypeID = memberTypeID ?? "",
            activityID = activityID ?? "",
            workModule = module,
            source = source,
            sourceKey = sourceKey ?? "",
            locked = locked,
            overrideWork = overrideWork,
            onCancelledEventID = onCancelledEventID ?? "",
            forbidCancel = forbidCancel,
            sourceEventID = sourceEventID ?? "",
            isSessionBooking = session != null,
            Session = session,
            roleID = roleID ?? "",
            flexible = flexible,
        };
    }
}
