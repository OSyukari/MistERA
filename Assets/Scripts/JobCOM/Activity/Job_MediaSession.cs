using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// A group movie / game session (RecreationActivity.requireMedia - Docs/Utnapishtim_Activities/Plan_UtnapishtimActivities.md),
/// launched by the media activity's begin event (LaunchJob, roles "participants"; its instanceKey AppendString links it
/// to the Job_Activity, which then hands its participants over - Job_Activity.DispatchChild).
/// <br/>- Runs for the media item's runtime (comp_Media.runtimeMinutes) from its launch, in the activity's room. The
///   runtime is the job's own state (startTime / ElapsedMinutes) - no package lasts the whole movie.
/// <br/>- The session command is the ordinary TV command's per-item child (requireMedia.sessionCommandID + "_" + item -
///   com_furniture_watchVideo_<item> / com_furniture_playVideoGames_<item>): the same standard 30-minute package as
///   sandbox TV use, with its DC check and relationship changes each round. One shared package per group of the
///   activity (bound - BindActivityExecution / Job_Activity.GroupOf): an NPC joins their group's round or starts one
///   (UpdateActorPackage), so watching together is an interaction between everyone in it. Anyone may break for a need
///   and rejoin a later round: the job is interruptible, and the activity's pull brings them back
///   (Job_Activity.DispatchChild). When the session ends (runtime over, or the activity ended first), the rounds still
///   running resolve then (EndSession - ResolveRunningRounds). The player's followers only join the player's round.
///   Docs/Activities/Plan_MediaSessionSharedPackage.md.
/// <br/>- The item's startEventID fires on the first update, its endEventID when the runtime is over (both optional, no
///   fallback); targets "participants". Then the job ends and the participants are free for the rest of their booking.
/// <br/>- The player is never driven: they start a round from the furniture (the activity's bound copy) or join a running
///   one, with their target / followers (Job_Activity.CompanionsFor) - each answering for themselves.
/// </summary>
public class Job_MediaSession : Job, I_RequireSpecialTracker, I_EventLaunchedJob, I_PlayerJoinableJob, I_ActivityChildJob
{
    [JsonProperty] RecreationActivityKey activityKey = null;
    [JsonProperty] int activityJobRef = -1;
    [JsonProperty] int roomRef = -1;
    [JsonProperty] string mediaItemID = "";
    [JsonProperty] string commandID = "";
    [JsonProperty] DateTime startTime;
    [JsonProperty] int durationMinutes = 60;
    /// <summary>Who belongs to the session: the activity's participants at the launch, plus those joining it later.</summary>
    [JsonProperty] List<int> participantRefs = new List<int>();
    [JsonProperty] bool startEventFired = false;
    [JsonProperty] bool ended = false;

    public Job_MediaSession() : base() { }

    [JsonIgnore] public override string DisplayName { get { return $"|MediaSession {mediaItemID} {(ended ? "ended" : "running")}|"; } }
    [JsonIgnore] public override bool CanBeInterrupted { get { return true; } }
    [JsonIgnore] public override Room_Instance ParentRoom { get { return roomRef < 0 ? null : scr_System_CampaignManager.current.Map.GetRoomByRef(roomRef); } }
    [JsonIgnore] public bool IsRunning { get { return !ended; } }
    [JsonIgnore] int ElapsedMinutes { get { return (int)(scr_System_Time.current.getCurrentTime() - startTime).TotalMinutes; } }
    [JsonIgnore] ItemComponentTemplate_Media Media { get { return ItemComponentTemplate_Media.Of(mediaItemID); } }
    [JsonIgnore] Job_Activity ActivityJob { get { return activityJobRef < 0 ? null : scr_System_CampaignManager.current.FindJobInstanceByID(activityJobRef, false) as Job_Activity; } }
    [JsonIgnore] COM SessionCOM
    {
        get
        {
            if (!commandChecked) RefreshCommandID();
            return string.IsNullOrEmpty(commandID) ? null : scr_System_Serializer.current.MasterList.COMs.GetByID(commandID);
        }
    }

    /// <summary>The stored commandID was checked against the current data this session (RefreshCommandID) - not saved.</summary>
    [JsonIgnore] bool commandChecked = false;

    /// <summary>
    /// The session command for mediaItemID: the item's own comp_Media.sessionCommandID, else the activity's
    /// (requireMedia.sessionCommandID); its per-item child (GenerateCOM - "&lt;command&gt;_&lt;itemID&gt;", e.g. "Watch
    /// &lt;title&gt; together") when there is one, so the log and memories name what was watched / played. "" = none.
    /// </summary>
    static string ResolveCommandID(Job_Activity activity, string mediaItemID, ItemComponentTemplate_Media media)
    {
        if (media == null) return "";
        string id = !string.IsNullOrEmpty(media.sessionCommandID) ? media.sessionCommandID : activity?.MediaRequirement?.sessionCommandID ?? "";
        if (string.IsNullOrEmpty(id)) return "";
        string itemCommandID = id + "_" + mediaItemID;
        return scr_System_Serializer.current.MasterList.COMs.GetByID(itemCommandID) != null ? itemCommandID : id;
    }

    /// <summary>
    /// A loaded session re-resolves its command from the current data (ResolveCommandID): the stored commandID may name a
    /// command the data no longer has (e.g. the former session-only com_media_watch_&lt;item&gt;). Once the activity job is
    /// found - until then (still loading) the stored one stands and the next call tries again.
    /// </summary>
    void RefreshCommandID()
    {
        var activity = ActivityJob;
        if (activity == null) return;
        commandChecked = true;
        string id = ResolveCommandID(activity, mediaItemID, Media);
        if (string.IsNullOrEmpty(id) || id == commandID) return;
        commandID = id;
        allusableCOMs_cache = null;
    }

    // ---------------- launch ---------------- //

    /// <summary>
    /// Binds to the activity job named by ev's instanceKey: its room, its retained media template (Job_Activity.MediaItemID),
    /// the session command (the item's own, else the activity's), the runtime; participants = roles["participants"]. False
    /// (not launched) without a live media activity job, an item or a command.
    /// </summary>
    public bool BindRoles(Dictionary<string, List<Character_Trainable>> roles, EventInstance ev)
    {
        if (ev == null || !ev.AppendStrings.TryGetValue("instanceKey", out var keys) || keys == null || keys.Count == 0) return false;
        activityKey = RecreationActivityKey.Parse(keys[0]);
        var activity = Job_Activity.FindFor(activityKey);
        if (activity == null || activity.Phase != ActivityPhase.Launched || activity.ParentRoom == null) return false;
        if (activity.ChildJob != null) return false;   // one child job per activity (a late fire of the begin event must not launch another)
        mediaItemID = activity.MediaItemID;
        var media = Media;
        if (media == null) return false;
        commandID = ResolveCommandID(activity, mediaItemID, media);
        commandChecked = true;
        if (SessionCOM == null)
        {
            Debug.LogError($"Job_MediaSession: no session command [{commandID}] for media [{mediaItemID}]");
            return false;
        }
        activityJobRef = activity.RefID;
        roomRef = activity.ParentRoom.RefID;
        durationMinutes = Math.Max(1, media.runtimeMinutes);
        FactionOwner = activity.FactionOwner;
        if (roles.TryGetValue("participants", out var list))
            foreach (var c in list) if (c != null && !participantRefs.Contains(c.RefID)) participantRefs.Add(c.RefID);
        return true;
    }

    public override void Register(int id)
    {
        base.Register(id);
        startTime = scr_System_Time.current.getCurrentTime();
        ActivityJob?.LinkChildJob(this);
        ActivityJob?.RefreshActivityRooms();
    }

    /// <summary>Every launched participant of the activity takes part (I_ActivityChildJob).</summary>
    public bool Wants(Character_Trainable c) { return c != null && !ended; }

    // ---------------- participants ---------------- //

    /// <summary>The player while they take part, or while the session runs in the room they stand in (to join it).</summary>
    public bool MatchTracker(Character_Trainable c)
    {
        if (c == null || ended) return false;
        if (participantRefs.Contains(c.RefID)) return true;
        return c.RefID == 0 && PlayerInRoom;
    }

    [JsonIgnore] bool PlayerInRoom
    {
        get
        {
            var room = ParentRoom;
            var playerRoom = scr_System_CampaignManager.current.GetCharaRoomInstance(0);
            return room != null && playerRoom != null && room.RefID == playerRoom.RefID;
        }
    }

    /// <summary>The panel tracks the session (its running rounds as join buttons) while it runs in the room the player stands in and they're not already in it.</summary>
    public bool OffersJoinTo(Character_Trainable player) { return player != null && player.RefID == 0 && !ended && PlayerInRoom && !IsInSession(0); }

    /// <summary>Minutes of the session still to run (at least 1).</summary>
    [JsonIgnore] public int MinutesLeft { get { return Math.Max(1, durationMinutes - ElapsedMinutes); } }

    /// <summary>charaRef runs one of this session's packages right now (not run out).</summary>
    bool IsInSession(int charaRef)
    {
        foreach (var ap in packages_current) if (ap.actorRefs.Contains(charaRef) && ap.Duration > 0) return true;
        foreach (var ap in packages_previous) if (ap.actorRefs.Contains(charaRef) && ap.Duration > 0) return true;
        return false;
    }

    /// <summary>
    /// The player's own session package (the activity's bound furniture offer - Job_Activity.AppendParticipantCommandOffers): a round of the session command in its room
    /// (the TV command's per-item child - repeatable like any TV command, until the session ends) - clicking it makes the player a participant
    /// (AddActor); they're never driven. Null without a room / command.
    /// </summary>
    public ActionPackage MakePlayerSessionPackage(Character_Trainable player)
    {
        var com = SessionCOM;
        var room = ParentRoom;
        if (player == null || com == null || room == null || ended) return null;
        return com.MakePackage(this, new List<int>() { player.RefID }, new List<int>(), player.RefID, room);
    }

    /// <summary>c belongs to the session (a participant at its launch, joined since, or joining now through the activity).</summary>
    public void AddParticipant(Character_Trainable c)
    {
        if (c != null && !participantRefs.Contains(c.RefID)) participantRefs.Add(c.RefID);
    }

    public override bool IsJobValid() { return !ended; }

    public override bool IsActorValid(int doerRefID) { return !ended && (participantRefs.Contains(doerRefID) || doerRefID == 0); }

    public override void AddActor(int charaRef, string priorityCOMID = "", string priorityCOMTag = "")
    {
        if (!participantRefs.Contains(charaRef)) participantRefs.Add(charaRef);
        base.AddActor(charaRef, priorityCOMID, priorityCOMTag);
    }

    public override bool hasActorCompletedJob(int refID) { return ended; }

    protected override List<COM> UpdateAllUsableCOMs()
    {
        var list = new List<COM>();
        var com = SessionCOM;
        if (com != null) list.Add(com);
        return list;
    }

    /// <summary>
    /// Walk to the session's room, then join a running round (JoinableRounds - canJoinAP keeps groups apart), else start one -
    /// bound to the activity and that group. Every next round, and whoever dropped out, comes back through here.
    /// The player is never driven: they only keep the package they joined. The player's followers go with the player:
    /// they join the player's round, never start one of their own (the player's round takes them along -
    /// Job_Activity.CompanionsFor); without one they are released and follow the player.
    /// </summary>
    public override bool UpdateActorPackage(Character_Trainable c, out string ss)
    {
        ss = $"|{DisplayName} [{c?.FirstName}]|";
        if (ended || c == null || !IsActorValid(c.RefID)) { ss += "released"; return false; }
        if (packages_current.Exists(x => x.actorRefs.Contains(c.RefID))) { ss += "has current package"; return true; }
        if (packages_previous.Exists(x => x.actorRefs.Contains(c.RefID) && x.Duration > 0)) { ss += "has ongoing package"; return true; }
        if (c.RefID == 0) { ss += "player never driven"; return false; }
        bool follower = scr_System_CampaignManager.current.PlayerPartyMembers.Contains(c.RefID);
        if (follower && !IsInSession(0)) { ss += "follows the player - no round of the player's to join"; return false; }

        var room = ParentRoom;
        if (room == null) { ss += "room gone"; return false; }
        var charaRoom = scr_System_CampaignManager.current.GetCharaRoomInstance(c.RefID);
        if (charaRoom == null || charaRoom.RefID != room.RefID)
        {
            var pathPkg = new ActionPackage_PathTo(this, c.RefID, room.RefID);
            if (!pathPkg.Validate()) { ss += "cannot path to the session room"; return false; }
            AddPackage(new List<ActionPackage>() { pathPkg });
            ss += "pathing to the session room";
            return true;
        }
        // join an ongoing round over starting one, the way every NPC joins (Job.PickJoinable / JoinPackage)
        var round = PickJoinable(JoinableRounds(c));
        if (follower)
        {
            // PickJoinable prefers the player's round: anything else means the player's can't take them
            if (round == null || !round.actorRefs.Contains(0)) { ss += "follows the player - cannot join the player's round"; return false; }
            return JoinPackage(c, round, ref ss);
        }
        if (round != null) return JoinPackage(c, round, ref ss);
        var activity = ActivityJob;
        var com = SessionCOM;
        var pkg = com?.MakePackage(this, new List<int>() { c.RefID }, new List<int>(), -1, room);
        if (pkg != null && activity != null) pkg.BindActivityExecution(activity);
        if (pkg == null || !pkg.Validate()) { ss += $"session command [{commandID}] invalid"; return false; }
        AddPackage(new List<ActionPackage>() { pkg });
        ss += $"running [{commandID}]";
        return true;
    }

    /// <summary>The ongoing rounds c may join (as Job_Furniture.MakePackagesJoinable): canJoinAP decides - the activity's group / walk-in admission included.</summary>
    List<ActionPackage> JoinableRounds(Character_Trainable c)
    {
        var result = new List<ActionPackage>();
        var packages = ActivePackages;
        for (int i = 0; i < packages.Count; i++)
        {
            var ap = packages[i];
            if (ap.isTemporaryAP || ap.isPaused || ap.Duration <= 1 || !ap.AllowJoining || ap.actorRefs.Contains(c.RefID)) continue;
            if (ap.canJoinAP(c, out _, out _, out _) >= 0) result.Add(ap);
        }
        return result;
    }

    public override void RemoveActor(int charaRef)
    {
        // re-entrancy guard like Job_MedicalCare: ChangeCurrentJob calls RemoveActor back
        if (actorJoinTime.ContainsKey(charaRef))
        {
            actorJoinTime.Remove(charaRef);
            var c = scr_System_CampaignManager.current.FindInstanceByID(charaRef);
            if (c != null && c.CurrentJob == this) c.ChangeCurrentJob(null);
        }
        base.RemoveActor(charaRef);
    }

    /// <summary>"Doing" + the session command's own name (its per-item child: e.g. "一起观看《...》" / "一起玩《...》").</summary>
    public override string GetJobDescription(int charaRef)
    {
        var com = SessionCOM;
        var item = Masterlist_Items.Instance?.Index.GetByID(mediaItemID);
        string action = com != null ? com.DisplayName().Replace("$name$", item?.DisplayName ?? mediaItemID) : (item?.DisplayName ?? mediaItemID);
        return LocalizeDictionary.QueryThenParse("ui_recreation_mediaSession_description").Replace("$action$", action);
    }

    // ---------------- update / end ---------------- //

    public override void LastUpdate()
    {
        if (ended) return;
        var activity = ActivityJob;
        if (activity == null || activity.Phase == ActivityPhase.Ended || activity.Phase == ActivityPhase.Failed)
        { EndSession("activity gone", false); return; }
        activity.RefreshActivityRooms();
        if (!startEventFired)
        {
            startEventFired = true;
            FireMediaEvent(Media?.startEventID);
        }
        if (ElapsedMinutes >= durationMinutes) EndSession("runtime over", true);
    }

    /// <summary>The activity is ending (Job_Activity.EndJob): the session ends with it now, so its running rounds resolve before the activity's end event.</summary>
    public void EndWithActivity() { EndSession("activity ended", false); }

    /// <summary>Ends the session: the end event (finished only), actors released, the activity told (its participants are free from now on).</summary>
    void EndSession(string reason, bool finished)
    {
        if (ended) return;
        ended = true;
        if (finished) FireMediaEvent(Media?.endEventID);
        // the player watching to the end: their package ends with the session - stop advancing time through it
        bool playerWatching = IsInSession(0);
        // rounds under way resolve now, as if they had run out (results, DC, relationship changes) - never cut off, whether
        // the runtime is over or the activity ended first (its booking is the runtime rounded up to hours, from T - the
        // session starts later, so the booking often ends before the movie does)
        ResolveRunningRounds();
        var packages = new List<ActionPackage>(packages_current.Count + packages_previous.Count);
        packages.AddRange(packages_current);
        packages.AddRange(packages_previous);
        foreach (var ap in packages) RemovePackage(ap, false);
        if (playerWatching) scr_UpdateHandler.current.NotifyPlayerPackageInterrupted();
        foreach (var refID in new List<int>(actorRefID)) RemoveActor(refID);
        var activity = ActivityJob;
        activity?.OnChildJobEnded(this);
        activity?.RefreshActivityRooms();
        if (!scr_UpdateHandler.current.Updating) NotifyDescriptionsOutOfUpdate();
        scr_System_CampaignManager.current.NotifyEndJob(this);
        if (scr_System_CentralControl.current.LogPrefs.DLog_Jobs) Debug.Log($"{DisplayName} ended: {reason}");
    }

    /// <summary>
    /// Executes every round under way (begun, not paused, time left) now - the execution its last minute would have run,
    /// no repeat - as the tick does (registered as executed), then collects its logs the way PostUpdateTime would (this
    /// job is unregistered right after, so it gets no PostUpdateTime of its own). A round not begun yet was never asked
    /// and is just dropped.
    /// </summary>
    void ResolveRunningRounds()
    {
        List<ActionPackage> running = null;
        for (int i = 0; i < packages_previous.Count; i++)
        {
            var ap = packages_previous[i];
            if (ap == null || ap.Duration <= 0 || !ap.Ticked || ap.isPaused || ap.isTemporaryAP) continue;
            if (running == null) running = new List<ActionPackage>();
            running.Add(ap);
        }
        if (running == null) return;
        var campaign = scr_System_CampaignManager.current;
        bool visible = isVisibleToPlayer;
        bool anyLogged = false;
        for (int i = 0; i < running.Count; i++)
        {
            var ap = running[i];
            ap.PackageRepeat = false;
            ap.ExecutePackageOutsideUpdate();
            if (!campaign.isPackageExecuted(ap)) campaign.RegisterExecutedAP(ap);
            if (visible || ap.isOwnRoomVisibleToPlayer) { CollectLogs(ap); anyLogged = true; }
            if (!ap.isTemporaryAP) actorJobComplete.AddRange(ap.actorRefs);
        }
        if (!anyLogged) return;
        m.exp.AddRelevantChara(this.actorRemove);
        m.exp.AddRelevantChara(this.actorJobComplete);
        m.exp.AddRelevantChara(this.actorRefID);
        // in the update loop: handed over now, flushed with the minute's logs; out of it EndSession's
        // NotifyDescriptionsOutOfUpdate sends them
        if (scr_UpdateHandler.current.Updating) scr_UpdateHandler.current.NotifyJobDescriptions(m);
    }

    /// <summary>
    /// Starts eventID ("" = none) with target "participants" (the session's participants) and AppendStrings mediaName /
    /// activityName; self = the player if taking part (forced on screen), else the first participant - self only decides
    /// visibility.
    /// </summary>
    void FireMediaEvent(string eventID)
    {
        if (string.IsNullOrEmpty(eventID)) return;
        var charas = new List<Character_Trainable>(participantRefs.Count);
        foreach (int r in participantRefs)
        {
            var c = scr_System_CampaignManager.current.FindInstanceByID(r);
            if (c != null) charas.Add(c);
        }
        if (charas.Count == 0) return;
        var self = charas.Find(x => x.RefID == 0) ?? charas[0];
        var ev = new EventInstance(self, eventID, "");
        ev.displayOverride = self.RefID == 0;
        ev.Targets["participants"] = charas;
        var item = Masterlist_Items.Instance?.Index.GetByID(mediaItemID);
        ev.AppendStrings["mediaName"] = new List<string>() { item?.DisplayName ?? mediaItemID };
        scr_UpdateHandler.current.EventHandler.StartEvent(ev, false);
    }
}
