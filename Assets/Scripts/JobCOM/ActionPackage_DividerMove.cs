using System;
using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// Entering / leaving a room divider (stall, private bathroom): com_furniture_divider_enter / _leave, ActionPackageClass
/// "ActionPackage_DividerMove". Temporary like pathing - a transit step that doesn't complete the actor's job on the
/// furniture (Job.actorJobComplete), isn't interrupted / joined. The move is applied on completion (ApplyMove, called
/// by the dividerEnter / dividerLeave tag branch of ActionPackage.Execution). Docs/Furniture/Plan_FurnitureOccupancy.md §4
///
/// The player's move: the move itself is never refused (Request accepts - no 5% failure). The player's followers are
/// its receivers; one who can't go (divider rules, elsewhere, can't move) stops following - named in the tooltip before,
/// dropped from the party and logged at launch. A current target not following yet is first asked along with a real
/// 同行 (com_interaction_partyInvite), resolved at once on launch (ResolveOnLaunch, from the command panel) so a refusal
/// never touches the target's own job; its rate is the command's rate. NPC moves (no player) are unchanged.
/// </summary>
public class ActionPackage_DividerMove : ActionPackage_Interaction
{
    public const string COM_PartyInvite = "com_interaction_partyInvite";

    [JsonIgnore] public override bool isTemporaryAP { get { return true; } }

    /// <summary> player's move, before launch: the current target, asked along first (同行). -1 = nobody </summary>
    [JsonProperty] int inviteRef = -1;
    /// <summary> invite and followers settled (ResolveOnLaunch) - from then on only launchFollowers come along </summary>
    [JsonProperty] bool launchResolved = false;
    /// <summary> the followers setting off with the player (an accepted invitee too) </summary>
    [JsonProperty] List<int> launchFollowers = new List<int>();
    /// <summary> followers dropped at launch - named in the begin line </summary>
    [JsonProperty] List<int> stoppedFollowing = new List<int>();

    /// <summary> button tooltip lines (who would stop following, the invite) - rebuilt on every validation before launch </summary>
    [JsonIgnore] readonly List<string> previewLines = new List<string>();
    /// <summary> before launch: the last validation found a valid 同行 invite (the button reads 一起进入 / 一起离开) </summary>
    [JsonIgnore] bool inviteOffered = false;

    public ActionPackage_DividerMove() : base()
    {

    }
    public ActionPackage_DividerMove(Job job, COM targetCOM, List<int> doer, List<int> receiver, int masterRef) : base(job, targetCOM, doer, receiver, masterRef)
    {

    }

    protected override ActionPackage CopyPackage()
    {
        var copy = new ActionPackage_DividerMove(job, targetCOM, DoerRefs, ReceiverRefs, masterRef);
        copy.SetVariantID(this.validVariant);
        copy.LoggedBegin = this.LoggedBegin;
        copy.inviteRef = inviteRef;
        copy.launchResolved = launchResolved;
        copy.launchFollowers = new List<int>(launchFollowers);
        copy.stoppedFollowing = new List<int>(stoppedFollowing);
        return copy;
    }

    /// <summary> the player alone is the doer, on a furniture </summary>
    [JsonIgnore] bool IsPlayerMove { get { var d = DoerRefs; return d != null && d.Count == 1 && d[0] == 0 && job is Job_Furniture; } }
    [JsonIgnore] bool IsEnter { get { return targetCOM != null && targetCOM.comTags.Contains(FurnitureOccupancyUtility.Tag_DividerEnter); } }

    /// <summary>
    /// The player's move sets its own receivers, whoever built it (panel, LLM): before launch every follower who can come
    /// (the others listed in the tooltip), after launch whoever of launchFollowers still can. No side effects here - a
    /// temporary copy (canJoinAP) validates too; the party only changes at launch and at completion.
    /// </summary>
    protected override void BeforeValidate()
    {
        if (!IsPlayerMove || targetCOM == null) return;
        var campaign = scr_System_CampaignManager.current;
        var party = campaign.PlayerPartyMembers;
        var divider = (Job_Furniture)job;
        bool enter = IsEnter;
        var followers = new List<int>(party.Count);
        if (!launchResolved)
        {
            previewLines.Clear();
            // the carried (携带) is no one to ask - they are where the player is anyway (ScopeRef)
            int target = campaign.CurrentTargetRef;
            inviteRef = target > 0 && !party.Contains(target) && !FurnitureOccupancyUtility.IsCarriedRef(target) ? target : -1;
            for (int i = 0; i < party.Count; i++)
            {
                var c = campaign.FindInstanceByID(party[i]);
                if (c == null) continue;
                if (CanFollow(divider, c, enter, out var reason)) followers.Add(c.RefID);
                else previewLines.Add(LocalizeDictionary.QueryThenParse("ui_divider_stopFollowing_tooltip")
                    .Replace("$name$", c.FirstName).Replace("$player$", campaign.Player.FirstName).Replace("$reason$", reason));
            }
        }
        else
        {
            // the ones who can no longer come are dropped (and named) when the move completes - ApplyMove
            for (int i = 0; i < launchFollowers.Count; i++)
            {
                int r = launchFollowers[i];
                if (!party.Contains(r)) continue;
                var c = campaign.FindInstanceByID(r);
                if (c != null && CanFollow(divider, c, enter, out _)) followers.Add(r);
            }
        }
        if (!SameRefs(ReceiverRefs, followers)) ResetRequest(new List<int>(DoerRefs), followers, masterRef, false);
    }

    static bool SameRefs(List<int> a, List<int> b)
    {
        if (a == null || a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++) if (a[i] != b[i]) return false;
        return true;
    }

    /// <summary> c can go in / come out with the player: here, able to move, the divider rules, not held by an activity </summary>
    bool CanFollow(Job_Furniture divider, Character_Trainable c, bool enter, out string reason)
    {
        reason = null;
        if (scr_System_CampaignManager.current.GetCharaRoomInstance(c.RefID) != PackageRoom)
        {
            reason = LocalizeDictionary.QueryThenParse("ui_divider_cannotFollow").Replace("$name$", c.FirstName);
            return false;
        }
        if (!c.canMoveIgnoringTimestop)
        {
            reason = LocalizeDictionary.QueryThenParse("ui_divider_cannotMove").Replace("$name$", c.FirstName);
            return false;
        }
        if (!FurnitureOccupancyUtility.ValidateDividerActor(divider, c, enter, !enter, out reason)) return false;
        if (!divider.AllowsUnboundActivityCommand(c, targetCOM))
        {
            reason = LocalizeDictionary.QueryThenParse("ui_divider_cannotFollow").Replace("$name$", c.FirstName);
            return false;
        }
        return true;
    }

    /// <summary> the move is never refused: the command's rate is the invite's (100% without one) - the button's 0% / deterministic gates apply to it </summary>
    protected override bool Evaluate()
    {
        if (!base.Evaluate()) return false;
        requestRate = 100;
        responseRate = 100;
        inviteOffered = false;
        if (!IsPlayerMove || launchResolved || inviteRef < 1) return isValid;

        var name = scr_System_CampaignManager.current.FindInstanceByID(inviteRef)?.FirstName ?? "";
        if (TryMakeInvite(out var invite, out var reason))
        {
            inviteOffered = true;
            requestRate = invite.RequestRate;
            responseRate = invite.ResponseRate;
            previewLines.Add(LocalizeDictionary.QueryThenParse("ui_divider_invite_tooltip").Replace("$name$", name)
                .Replace("$player$", scr_System_CampaignManager.current.Player.FirstName));
        }
        else if (reason != null) previewLines.Add(reason);
        return isValid;
    }

    /// <summary> 同行 package: the player asks inviteRef along (player command job, as the dialogue tab's 同行), validated. False = no invite, reason = tooltip line or null </summary>
    bool TryMakeInvite(out ActionPackage invite, out string reason)
    {
        invite = null;
        reason = null;
        var campaign = scr_System_CampaignManager.current;
        var c = campaign.FindInstanceByID(inviteRef);
        if (c == null) return false;
        if (!CanFollow((Job_Furniture)job, c, IsEnter, out var why))
        {
            reason = LocalizeDictionary.QueryThenParse("ui_divider_noInvite_tooltip").Replace("$name$", c.FirstName).Replace("$reason$", why);
            return false;
        }
        var com = scr_System_Serializer.current.MasterList.COMs.GetByID(COM_PartyInvite);
        var playerJob = campaign.FindJobInstanceByID(campaign.jobRef_playerCOM);
        if (com == null || playerJob == null) return false;
        invite = com.MakePackage(playerJob, new List<int>(1) { 0 }, new List<int>(1) { c.RefID }, 0);
        if (invite != null && invite.Validate()) return true;
        invite = null;
        reason = LocalizeDictionary.QueryThenParse("ui_divider_inviteInvalid").Replace("$name$", c.FirstName);
        return false;
    }

    /// <summary>
    /// The player launched it (command panel, before the package is added and the minute runs): the 同行 invite is rolled,
    /// executed and logged now; then whoever follows comes along and the followers who can't stop following. Once only.
    /// False = the move itself is no longer valid.
    /// </summary>
    public bool ResolveOnLaunch()
    {
        if (launchResolved || !IsPlayerMove || targetCOM == null) return true;
        var campaign = scr_System_CampaignManager.current;
        if (inviteRef > 0 && !campaign.PlayerPartyMembers.Contains(inviteRef) && TryMakeInvite(out var invite, out _)) RunInvite(invite);
        inviteRef = -1;

        var divider = (Job_Furniture)job;
        bool enter = IsEnter;
        var party = campaign.PlayerPartyMembers;
        var dropped = new List<Character_Trainable>();
        launchFollowers.Clear();
        stoppedFollowing.Clear();
        for (int i = 0; i < party.Count; i++)
        {
            var c = campaign.FindInstanceByID(party[i]);
            if (c == null) continue;
            if (CanFollow(divider, c, enter, out _)) launchFollowers.Add(c.RefID);
            else dropped.Add(c);
        }
        for (int i = 0; i < dropped.Count; i++)
        {
            campaign.party.RemoveFromParty(dropped[i]);
            stoppedFollowing.Add(dropped[i].RefID);
        }
        launchResolved = true;
        previewLines.Clear();
        return Validate();
    }

    /// <summary> the 同行 package runs through Tick's accept / refuse steps at once, logged on its job like any player command </summary>
    static void RunInvite(ActionPackage invite)
    {
        var inviteJob = invite.job;
        var m = inviteJob.m;
        invite.SetActive(scr_System_Time.current.getCurrentTime());
        bool accepted = invite.RequestOutsideUpdate();
        invite.LogAcceptanceCheck(m);
        invite.Ticked = true;
        if (accepted)
        {
            invite.internalState = AP_Status.accepted;
            invite.PackageBeginOutsideUpdate(m);
        }
        else
        {
            invite.internalState = AP_Status.refused;
            invite.LogMessage_Begin_Refuse(m);
        }
        // accepted: toggleTeamStatus adds the target to the party; refused: nothing touches the target's own job
        invite.ExecutePackageOutsideUpdate(m);
        if (accepted) invite.internalState = AP_Status.success;
        inviteJob.CollectLogs(invite);
        scr_System_CampaignManager.current.RegisterExecutedAP(invite);
        if (!scr_UpdateHandler.current.Updating) inviteJob.NotifyDescriptionsOutOfUpdate();
    }

    /// <summary> never refused - followers who can't come were dropped, the invite rolled on its own. A launch that skipped the panel (LLM agent) settles here. </summary>
    protected override bool Request(bool rebuildPackage = true, Memory_Response forceAccept = Memory_Response.None)
    {
        if (IsPlayerMove && !launchResolved) ResolveOnLaunch();
        return base.Request(rebuildPackage, Memory_Response.Accept);
    }

    /// <summary> nothing was rolled - the invite logs its own check </summary>
    public override void LogAcceptanceCheck(MessageCollect m = null)
    {
    }

    /// <summary> the player's move with someone along - a follower, or (before launch) a target to ask: 一起进入 / 一起离开 </summary>
    [JsonIgnore] public override string DisplayName
    {
        get
        {
            if (nameOverwrite != "" || !IsPlayerMove) return base.DisplayName;
            if (ReceiverRefs.Count < 1 && (launchResolved || !inviteOffered)) return base.DisplayName;
            return LocalizeDictionary.QueryThenParse(IsEnter ? "com_furniture_divider_enter_together_displayName" : "com_furniture_divider_leave_together_displayName");
        }
    }

    public override string GetTooltips(string s)
    {
        var tooltips = base.GetTooltips(s);
        if (previewLines.Count < 1) return tooltips;
        return tooltips + "\n" + String.Join("\n", previewLines);
    }

    /// <summary> temporary packages log no begin line - the player's move names who came along and who stopped following </summary>
    protected override void PackageBegin(MessageCollect m = null)
    {
        base.PackageBegin(m);
        if (LoggedBegin || !IsPlayerMove) return;
        LoggedBegin = true;
        var player = scr_System_CampaignManager.current.Player;
        string names = Names(ReceiverRefs), stopped = Names(stoppedFollowing);
        // alone: silent, as any transit step
        if (names.Length < 1 && stopped.Length < 1) return;
        string s = names.Length > 0
            ? LocalizeDictionary.QueryThenParse(IsEnter ? "ui_divider_enter_together" : "ui_divider_leave_together").Replace("$player$", player.FirstName).Replace("$names$", names)
            : LocalizeDictionary.QueryThenParse(IsEnter ? "com_furniture_divider_enter_begin_1" : "com_furniture_divider_leave_begin_1").Replace("$doer.firstname$", player.FirstName);
        if (stopped.Length > 0) s += "\n" + StoppedLine(stopped);

        bool logging = m != null;
        if (!logging) m = mcol;
        var desc = new DescriptionCollector(s);
        desc.LoadActors(actorRefs);
        desc.LoadActors(stoppedFollowing);
        desc.message_excludeRelated = s;
        m.AddMessage_Before(desc, true, logging ? RecordingRoom : null);
        if (!logging) packageStateChanged = true;
    }

    /// <summary>
    /// Completion (the dividerEnter / dividerLeave branch of Execution): everyone moves together - the doer, and with the
    /// player whoever of launchFollowers still can; the rest stop following (logged).
    /// </summary>
    public void ApplyMove(MessageCollect m)
    {
        var divider = job as Job_Furniture;
        if (divider == null) return;
        var campaign = scr_System_CampaignManager.current;
        bool enter = IsEnter;
        var actors = Actors;
        var movers = new List<Character_Trainable>(actors.Count);
        List<int> dropped = null;
        if (IsPlayerMove && launchResolved)
        {
            movers.Add(campaign.Player);
            var party = campaign.PlayerPartyMembers;
            for (int i = 0; i < launchFollowers.Count; i++)
            {
                int r = launchFollowers[i];
                if (!party.Contains(r)) continue;
                var c = campaign.FindInstanceByID(r);
                if (c == null) continue;
                if (CanFollow(divider, c, enter, out _)) movers.Add(c);
                else
                {
                    if (dropped == null) dropped = new List<int>();
                    dropped.Add(r);
                }
            }
        }
        else
        {
            for (int i = 0; i < actors.Count; i++) if (actors[i] != null) movers.Add(actors[i]);
        }

        // one target check after everyone moved (not halfway: player inside, target still outside)
        FurnitureOccupancyUtility.BeginBatch();
        try
        {
            for (int i = 0; i < movers.Count; i++)
            {
                var c = movers[i];
                if (enter) FurnitureOccupancyUtility.Enter(c, divider, OccupancyReason.Enter);
                else if (c.FurnitureLocation == divider) FurnitureOccupancyUtility.Leave(c, OccupancyReason.LeaveCommand);
            }
        }
        finally { FurnitureOccupancyUtility.EndBatch(); }

        if (dropped == null || m == null) return;
        for (int i = 0; i < dropped.Count; i++) campaign.party.RemoveFromParty(dropped[i]);
        var desc = new DescriptionCollector(StoppedLine(Names(dropped)));
        desc.LoadActors(actorRefs);
        desc.LoadActors(dropped);
        desc.message_excludeRelated = desc.message;
        m.AddMessage_After(desc, RecordingRoom);
    }

    static string StoppedLine(string names)
    {
        return LocalizeDictionary.QueryThenParse("ui_divider_stopFollowing").Replace("$names$", names)
            .Replace("$player$", scr_System_CampaignManager.current.Player.FirstName);
    }

    static string Names(List<int> refs)
    {
        if (refs == null || refs.Count < 1) return "";
        var campaign = scr_System_CampaignManager.current;
        var sb = new System.Text.StringBuilder();
        string sep = null;
        for (int i = 0; i < refs.Count; i++)
        {
            var c = campaign.FindInstanceByID(refs[i]);
            if (c == null) continue;
            if (sb.Length > 0) sb.Append(sep ?? (sep = LocalizeDictionary.QueryThenParse("ui_entry_separator")));
            sb.Append(c.FirstName);
        }
        return sb.ToString();
    }
}
