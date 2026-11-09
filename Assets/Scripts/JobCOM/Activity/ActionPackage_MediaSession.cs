using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// A media session's command (com_media_watch / com_media_play and their per-item children - ActionPackageClass
/// "ActionPackage_MediaSession"), run in a Job_MediaSession (Docs/Activities/Plan_MediaSessionSharedPackage.md).
/// <br/>- A short round (the COM's timeScale, capped by what is left of the session) shared by one group of the activity:
///   NPCs join their group's running round (Job_MediaSession.UpdateActorPackage), the player starts or joins one from
///   the command panel, their target / followers are in it too (Job_Activity.CompanionsFor), each answering for themselves.
/// <br/>- One round per package, no repeat (PackageRepeat is for the player's sex commands only): NPCs request the next
///   round themselves (Job_MediaSession.UpdateActorPackage), the player clicks again. The movie's progress is the job's
///   own state, never a package's duration - anyone leaving drops the round (Job.RemoveActor), the rest request a new one.
/// <br/>- Joinable with no actor cap while the session runs.
/// </summary>
public class ActionPackage_MediaSession : ActionPackage_Interaction
{
    // not a temporary AP: it must begin / log / write memories like any command (the memory names the movie)
    [JsonIgnore] public override bool AllowJoining { get { return job is Job_MediaSession session && session.IsRunning; } }

    public ActionPackage_MediaSession() : base() { }

    public ActionPackage_MediaSession(Job job, COM targetCOM, List<int> doer, List<int> receiver, int masterRef) : base(job, targetCOM, doer, receiver, masterRef)
    {
    }

    /// <summary>One round: the COM's own timeScale, never past the end of the session.</summary>
    int SessionDuration
    {
        get
        {
            int round = targetCOM != null ? System.Math.Max(1, targetCOM.TimeScale) : 1;
            if (job is Job_MediaSession session) round = System.Math.Min(round, session.MinutesLeft);
            return round;
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
