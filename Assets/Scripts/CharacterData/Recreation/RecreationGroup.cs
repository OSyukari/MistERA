using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

public enum RecreationGroupStatus { Open, Cancelled, Ended }

/// <summary>
/// A participant's own answer to a session (written by that character - RecreationUtility step 3, the invitation event):
/// Invited = has it, not decided yet; Attending = holds the member booking; Blocked = wanted it but couldn't (reason);
/// Declined = gave it up (may take it again on a later re-run); Refused = said no to it - final.
/// </summary>
public enum RecreationMemberState { Invited, Attending, Blocked, Declined, Refused }

/// <summary>How a participant got the session: its host, its own scope (invited by the host / a friend / a faction or world member), or an extended invitation.</summary>
public enum RecreationMemberOrigin { Host, Scope, Extension }

/// <summary>One character a session touched (RecreationGroup.members). charaRef = Character_Trainable.RefID.</summary>
public class RecreationGroupMember
{
    public int charaRef = -1;
    public string roleID = "";
    public RecreationMemberOrigin origin = RecreationMemberOrigin.Scope;
    /// <summary>Extension: who extended the invitation to them (-1 otherwise).</summary>
    public int extendedByRef = -1;
    public RecreationMemberState state = RecreationMemberState.Invited;
    /// <summary>Blocked / Declined: why (a RecreationUtility.CancelReason name or a short tag, debug / tooltips).</summary>
    public string reason = "";
    /// <summary>AcceptSession was asked (the first time they saw it).</summary>
    public bool considered = false;
    /// <summary>Attending: the session's latest self-update found them unable to make it ("" = fine) - see RecreationUtility.UpdateSession.</summary>
    public string failedCheck = "";
    /// <summary>Seen at the venue during the session (no-show check).</summary>
    public bool arrived = false;

    [JsonIgnore] public Character_Trainable Chara { get { return scr_System_CampaignManager.current.FindInstanceByID(charaRef); } }
    /// <summary>Holds the member booking and passed the latest self-update.</summary>
    [JsonIgnore] public bool IsAttending { get { return state == RecreationMemberState.Attending && string.IsNullOrEmpty(failedCheck); } }
}

/// <summary>What one participant extended (RecreationUtility.ExtendInvitation): per invitee validator used, everyone it picked (new and already invited alike).</summary>
public class RecreationSessionExtension
{
    public int extenderRef = -1;
    public string roleID = "";
    public bool inviteRequired = false;
    public int requiredCount = 1;
    public List<int> picks = new List<int>();
}

/// <summary>
/// One shared recreation activity (a session): a venue and a fixed block of time, posted by its host (or by the offer
/// board - hostless) to the characters its visibility reaches (RecreationInviteSpec.visibility). Every touched character
/// ranks it with everything else visible to them and confirms in preference order, holding their own RecreationBooking
/// for it (isSessionBooking, runtime reference - RecreationBooking.Session). Lives in RecreationGroupRegistry (saved);
/// characters, factions and events only hold references, rebuilt on load (RecreationUtility.RelinkAfterLoad) - no UID.
/// <br/>It checks itself once an hour (RecreationUtility.UpdateSession, run lazily by its members' own ticks): who
/// attends, the required invitations, validity - and is cancelled if invalid at T-1, T or T+1 (T = start hour).
/// </summary>
public class RecreationGroup : I_ActivityBooking
{
    /// <summary>-1 = hostless (a fixed offer posted by the board).</summary>
    public int hostRef = -1;
    public int day = 0;
    public int startHour = 0;
    public int hours = 0;
    public string factionID = "";

    public RecreationBookingSource source = RecreationBookingSource.Membership;
    public string sourceKey = "";
    public string activityID = "";
    /// <summary>MemberType members act under unless their access / role says otherwise (the host's acting type, resolved).</summary>
    public string memberTypeID = "";
    public MapPlan.WorkModuleInit workModule = null;
    public bool forbidCancel = false;
    public string sourceEventID = "";

    /// <summary>Where the spec comes from (RecreationUtility.ResolveSpec): "offer:defID", "type:memberTypeID:activityID", or "" = the default spec.</summary>
    public string specRef = "";
    public int minParticipants = 1;

    public RecreationVisibility visibility = RecreationVisibility.Private;
    /// <summary>Faction / World: the faction (world ID for World) whose session list it is posted to.</summary>
    public string scopeFactionID = "";

    public RecreationGroupStatus status = RecreationGroupStatus.Open;
    public RecreationUtility.CancelReason cancelReason = RecreationUtility.CancelReason.None;
    /// <summary>Absolute hour of the latest self-update (-1 = none yet).</summary>
    public int lastUpdatedAbsHour = -1;
    /// <summary>Valid at the latest self-update (kept as it was at T+1 from then on).</summary>
    public bool valid = false;

    public List<RecreationGroupMember> members = new List<RecreationGroupMember>();
    public List<RecreationSessionExtension> extensions = new List<RecreationSessionExtension>();

    /// <summary>
    /// The RefID of the Job_Activity coordinating this session (-1 = none yet / no more). Saved, so a loaded save
    /// re-links the job; the job holds this session's identity back (RecreationActivityKey - sessions have no UID).
    /// </summary>
    public int activityJobRef = -1;
    /// <summary>The coordinating Job_Activity, or null (no ref yet, or the job is gone).</summary>
    [JsonIgnore] public Job ActivityJob
    {
        get { return scr_System_CampaignManager.current == null ? null : scr_System_CampaignManager.current.FindJobInstanceByID(activityJobRef, false); }
    }

    [JsonIgnore] public int AbsStart { get { return RecreationBooking.AbsoluteHour(day, startHour); } }
    [JsonIgnore] public int AbsEnd { get { return AbsStart + hours; } }
    [JsonIgnore] public bool IsOpen { get { return status == RecreationGroupStatus.Open; } }
    [JsonIgnore] public Character_Trainable Host { get { return hostRef < 0 ? null : scr_System_CampaignManager.current.FindInstanceByID(hostRef); } }

    // ---------------- I_ActivityBooking ---------------- //

    [JsonIgnore] public string DisplayName { get { return RecreationUtility.ActivityDisplayName(workModule); } }
    [JsonIgnore] public System.DateTime StartTime { get { return RecreationUtility.AbsHourToDateTime(AbsStart); } }
    /// <summary>The attending members, the gather floor and room, start and end hour.</summary>
    [JsonIgnore] public string Tooltip
    {
        get { return RecreationUtility.BuildActivityDetail(AttendingCharas(), factionID, RecreationUtility.ResolveActivity(this), day, startHour, AbsEnd, source, workModule); }
    }

    public RecreationGroupMember FindMember(int charaRef) { return members.Find(m => m.charaRef == charaRef); }

    /// <summary>Attending members (of roleID only, when given) - see RecreationGroupMember.IsAttending.</summary>
    public int AttendingCount(string roleID = null)
    {
        return members.Count(m => m.IsAttending && (roleID == null || m.roleID == roleID));
    }

    /// <summary>Members of roleID who count against its maxParticipants: not having said no or given up.</summary>
    public int ActiveCount(string roleID)
    {
        return members.Count(m => m.roleID == roleID && (m.state == RecreationMemberState.Invited || m.state == RecreationMemberState.Attending));
    }

    /// <summary>The attending members' characters, except exceptRef.</summary>
    public List<Character_Trainable> AttendingCharas(int exceptRef = -1)
    {
        var result = new List<Character_Trainable>();
        foreach (var m in members)
        {
            if (!m.IsAttending || m.charaRef == exceptRef) continue;
            var c = m.Chara;
            if (c != null) result.Add(c);
        }
        return result;
    }

    /// <summary>Debug label: host (or source) + start + venue.</summary>
    [JsonIgnore] public string Label
    {
        get
        {
            string who = Host != null ? Host.FullName : sourceKey;
            return $"[{who} day{day} {startHour:00}:00+{hours}h @ {factionID} {visibility} {status}{(valid ? " valid" : "")}]";
        }
    }
}

/// <summary>
/// Campaign-wide list of recreation sessions (scr_System_CampaignManager.RecreationGroups) - the only saved copy of a
/// session. Also keeps, at runtime, each faction's / world's list of the Faction / World sessions posted to it
/// (PostedTo, with a serial per list so its members notice new posts by pull). Ended sessions are dropped (Prune).
/// </summary>
public class RecreationGroupRegistry
{
    [JsonProperty] List<RecreationGroup> sessions = new List<RecreationGroup>();

    class PostedList
    {
        public List<RecreationGroup> sessions = new List<RecreationGroup>();
        public int serial = 0;
    }
    [JsonIgnore] readonly Dictionary<string, PostedList> posted = new Dictionary<string, PostedList>();

    [JsonIgnore] public IReadOnlyList<RecreationGroup> All { get { return Sessions; } }
    List<RecreationGroup> Sessions { get { if (sessions == null) sessions = new List<RecreationGroup>(); return sessions; } }

    /// <summary>
    /// Registers session (dropping ended ones first) and, for Faction / World visibility, posts it to its scope's list.
    /// A new session counts as self-updated in the hour it is posted: its members answer in this hour's second pass
    /// (RecreationUtility.LateHourConfirm), so a member ticking after the post must not validate - and, from T-1, cancel -
    /// it before anyone could answer. Its first self-update is next hour.
    /// </summary>
    public void Add(RecreationGroup session)
    {
        if (session == null) return;
        if (session.lastUpdatedAbsHour < 0 && scr_System_Time.current != null)
            session.lastUpdatedAbsHour = RecreationBooking.AbsoluteHour(scr_System_Time.current.getAbsoluteDay(), scr_System_Time.current.getCurrentTime().Hour);
        Prune();
        Sessions.Add(session);
        Post(session);
    }

    void Post(RecreationGroup session)
    {
        if (session.visibility != RecreationVisibility.Faction && session.visibility != RecreationVisibility.World) return;
        if (string.IsNullOrEmpty(session.scopeFactionID)) return;
        if (!posted.TryGetValue(session.scopeFactionID, out var list)) posted[session.scopeFactionID] = list = new PostedList();
        if (list.sessions.Contains(session)) return;
        list.sessions.Add(session);
        list.serial++;
    }

    /// <summary>The Faction / World sessions posted to factionID (a world ID for World), oldest first. Empty if none.</summary>
    public IReadOnlyList<RecreationGroup> PostedTo(string factionID)
    {
        if (!string.IsNullOrEmpty(factionID) && posted.TryGetValue(factionID, out var list)) return list.sessions;
        return System.Array.Empty<RecreationGroup>();
    }

    /// <summary>Changes whenever a session is posted to factionID's list (0 = never).</summary>
    public int PostedSerial(string factionID)
    {
        return !string.IsNullOrEmpty(factionID) && posted.TryGetValue(factionID, out var list) ? list.serial : 0;
    }

    [JsonIgnore] int lastPrunedAbs = -1;
    /// <summary>
    /// Prune, at most once per game hour - run lazily by whichever character's hourly recreation check comes first
    /// (RecreationUtility.HourlyCheck - never a central loop), so ended sessions go even when no new one is added.
    /// </summary>
    public void PruneHourly()
    {
        int nowAbs = RecreationBooking.AbsoluteHour(scr_System_Time.current.getAbsoluteDay(), scr_System_Time.current.getCurrentTime().Hour);
        if (lastPrunedAbs == nowAbs) return;
        lastPrunedAbs = nowAbs;
        Prune();
    }

    /// <summary>
    /// Drops sessions that have ended (AbsEnd passed): marked Ended, unhooked from the characters holding them
    /// (RecreationUtility.UnhookSession) and from their scope's list. A cancelled session stays until its end, so its
    /// members' bookings can still find it (also across a save) and drop themselves.
    /// </summary>
    public void Prune()
    {
        int nowAbs = RecreationBooking.AbsoluteHour(scr_System_Time.current.getAbsoluteDay(), scr_System_Time.current.getCurrentTime().Hour);
        foreach (var s in Sessions.Where(s => s == null || s.AbsEnd <= nowAbs).ToList())
        {
            Sessions.Remove(s);
            if (s == null) continue;
            if (s.IsOpen) s.status = RecreationGroupStatus.Ended;
            if (!string.IsNullOrEmpty(s.scopeFactionID) && posted.TryGetValue(s.scopeFactionID, out var list)) list.sessions.Remove(s);
            RecreationUtility.UnhookSession(s);
        }
    }

    /// <summary>After a save is loaded (characters exist): prunes, re-posts the Faction / World sessions, and relinks members (RecreationUtility.RelinkAfterLoad).</summary>
    public void OnAfterLoad()
    {
        posted.Clear();
        Prune();
        foreach (var s in Sessions) Post(s);
        foreach (var s in Sessions) RecreationUtility.RelinkAfterLoad(s);
    }
}
