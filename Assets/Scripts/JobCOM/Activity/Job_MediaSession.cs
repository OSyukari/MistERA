using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// A group movie / game session (RecreationActivity.requireMedia - Docs/Utnapishtim_Activities/Plan_UtnapishtimActivities.md),
/// launched by the media activity's begin event (LaunchJob, roles "participants"; its instanceKey AppendString links it
/// to the Job_Activity, which then hands its participants over - Job_Activity.DispatchChild).
/// <br/>- Runs for the media item's runtime (comp_Media.runtimeMinutes) from its launch, in the activity's room.
/// <br/>- Each participant repeats a short silent command (the session command - com_media_watch / com_media_play) in
///   a package of their own, so anyone may break for a need and rejoin: the job is interruptible, and the activity's
///   pull brings them back (Job_Activity.DispatchChild).
/// <br/>- The item's startEventID fires on the first update, its endEventID when the runtime is over (both optional, no
///   fallback); targets "participants". Then the job ends and the participants are free for the rest of their booking.
/// <br/>- The player is never driven: while the session runs in the room they stand in, the command panel shows them a
///   button of their own (MakePlayerSessionPackage - the session command lasting the rest of the session); clicking it
///   makes them a participant and they watch / play to the end.
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
    [JsonIgnore] COM SessionCOM { get { return string.IsNullOrEmpty(commandID) ? null : scr_System_Serializer.current.MasterList.COMs.GetByID(commandID); } }

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
        var requirement = activity.MediaRequirement;
        commandID = !string.IsNullOrEmpty(media.sessionCommandID) ? media.sessionCommandID : requirement?.sessionCommandID ?? "";
        // the command's per-item child (GenerateCOM - "<command>_<itemID>", e.g. "Watch <title> together") when there is
        // one, so the log and memories name what was watched / played
        string itemCommandID = commandID + "_" + mediaItemID;
        if (!string.IsNullOrEmpty(commandID) && scr_System_Serializer.current.MasterList.COMs.GetByID(itemCommandID) != null) commandID = itemCommandID;
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

    /// <summary>The panel offers the player their session button while it runs in the room they stand in and they're not already in it.</summary>
    public bool OffersJoinTo(Character_Trainable player) { return player != null && player.RefID == 0 && !ended && PlayerInRoom && !IsInSession(0); }

    /// <summary>Minutes of the session still to run (at least 1) - the length of the player's package.</summary>
    [JsonIgnore] public int MinutesLeft { get { return Math.Max(1, durationMinutes - ElapsedMinutes); } }

    /// <summary>charaRef runs one of this session's packages right now (not run out).</summary>
    bool IsInSession(int charaRef)
    {
        foreach (var ap in packages_current) if (ap.actorRefs.Contains(charaRef) && ap.Duration > 0) return true;
        foreach (var ap in packages_previous) if (ap.actorRefs.Contains(charaRef) && ap.Duration > 0) return true;
        return false;
    }

    /// <summary>
    /// The player's own session package (the command panel shows it as a button while OffersJoinTo): the session command
    /// in its room, lasting the rest of the session (ActionPackage_MediaSession) - clicking it makes the player a
    /// participant (AddActor) and time advances to the end; they're never driven. Null without a room / command.
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
    /// Walk to the session's room, then run the session command there in a package of one's own (issued again each time
    /// it finishes). The player is never driven: they only keep the package they joined.
    /// </summary>
    public override bool UpdateActorPackage(Character_Trainable c, out string ss)
    {
        ss = $"|{DisplayName} [{c?.FirstName}]|";
        if (ended || c == null || !IsActorValid(c.RefID)) { ss += "released"; return false; }
        if (packages_current.Exists(x => x.actorRefs.Contains(c.RefID))) { ss += "has current package"; return true; }
        if (packages_previous.Exists(x => x.actorRefs.Contains(c.RefID) && x.Duration > 0)) { ss += "has ongoing package"; return true; }
        if (c.RefID == 0) { ss += "player never driven"; return false; }

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
        var com = SessionCOM;
        var pkg = com?.MakePackage(this, new List<int>() { c.RefID }, new List<int>(), -1, room);
        if (pkg == null || !pkg.Validate()) { ss += $"session command [{commandID}] invalid"; return false; }
        AddPackage(new List<ActionPackage>() { pkg });
        ss += $"running [{commandID}]";
        return true;
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

    /// <summary>Ends the session: the end event (finished only), actors released, the activity told (its participants are free from now on).</summary>
    void EndSession(string reason, bool finished)
    {
        if (ended) return;
        ended = true;
        if (finished) FireMediaEvent(Media?.endEventID);
        // the player watching to the end: their package ends with the session - stop advancing time through it
        bool playerWatching = IsInSession(0);
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
