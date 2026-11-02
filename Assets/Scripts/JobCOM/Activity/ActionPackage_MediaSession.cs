using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// A media session's command (com_media_watch / com_media_play and their per-item children - ActionPackageClass
/// "ActionPackage_MediaSession"), run in a Job_MediaSession.
/// <br/>- An NPC's package lasts the COM's own timeScale (15 min) and is issued again each time (breaks allowed).
/// <br/>- The player's package (Job_MediaSession.MakePlayerSessionPackage - shown as a command button) lasts the rest of
///   the session (Job_MediaSession.MinutesLeft): the player stays and watches / plays to the end, time advancing through
///   it. Clicking it makes the player a participant (Job_MediaSession.AddActor); the session ending early interrupts it.
/// <br/>Not joinable: everyone runs a package of their own (the player through their own button).
/// </summary>
public class ActionPackage_MediaSession : ActionPackage_Interaction
{
    // not a temporary AP: it must begin / log / write memories like any command (the memory names the movie)
    [JsonIgnore] public override bool AllowJoining { get { return false; } }

    public ActionPackage_MediaSession() : base() { }

    public ActionPackage_MediaSession(Job job, COM targetCOM, List<int> doer, List<int> receiver, int masterRef) : base(job, targetCOM, doer, receiver, masterRef)
    {
    }

    /// <summary>The player's own package (their command button).</summary>
    [JsonIgnore] bool IsPlayers { get { return DoerRefs.Contains(0); } }

    /// <summary>The player: the rest of the session; an NPC: the COM's own timeScale.</summary>
    int SessionDuration
    {
        get
        {
            if (IsPlayers && job is Job_MediaSession session) return session.MinutesLeft;
            return targetCOM != null ? System.Math.Max(1, targetCOM.TimeScale) : 1;
        }
    }

    /// <summary>Valid only while the session runs, in its room.</summary>
    protected override bool PreEvaluate()
    {
        if (!base.PreEvaluate()) return false;
        if (!(job is Job_MediaSession session) || !session.IsRunning)
        {
            // names what is over: this package's own command ("一起观看《...》" / "一起玩《...》" - its per-item child)
            tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_PreEvaluate_mediaSessionOver").Replace("$action$", targetCOM?.DisplayName() ?? ""));
            isValid = false;
            return false;
        }
        return true;
    }

    public override void ReInitializeCOM(Job job, COM targetCOM, List<int> doer, List<int> receiver, int masterRef = -1, bool resetDuration = true)
    {
        base.ReInitializeCOM(job, targetCOM, doer, receiver, masterRef, resetDuration);
        if (resetDuration) this.duration = SessionDuration;
    }

    public override void Repeat()
    {
        base.Repeat();
        this.duration = SessionDuration;
    }

    public override void Reset(bool resetRequest = false)
    {
        base.Reset(resetRequest);
        this.duration = SessionDuration;
    }

    protected override ActionPackage CopyPackage()
    {
        // fresh package: duration comes from the session at copy time, not the source package
        var copy = new ActionPackage_MediaSession(job, targetCOM, DoerRefs, ReceiverRefs, masterRef);
        copy.SetVariantID(this.validVariant);
        copy.LoggedBegin = this.LoggedBegin;
        return copy;
    }
}
