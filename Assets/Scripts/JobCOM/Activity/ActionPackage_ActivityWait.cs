using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// "Waiting for the activity to begin" (recreation_activity_wait, ActionPackageClass "ActionPackage_ActivityWait") - one
/// per waiting participant of a gathering Job_Activity, the player's own included (offered as a command button, so time
/// advances through it like a wait-until-next-hour). Its duration is the job's longest remaining wait
/// (Job_Activity.WaitMinutesLeft - up to T+30), set whenever it is constructed, reset or repeated, like
/// ActionPackage_WaitUntilNextHour. When the activity can launch, the job interrupts every waiting package
/// (Job_Activity.Launch - for the player's, the update loop is told to stop advancing time). Temporary and not
/// joinable: everyone waits in a package of their own.
/// </summary>
public class ActionPackage_ActivityWait : ActionPackage_Interaction
{
    [JsonIgnore] public override bool isTemporaryAP { get { return true; } }
    [JsonIgnore] public override bool AllowJoining { get { return false; } }

    public ActionPackage_ActivityWait() : base()
    {

    }
    public ActionPackage_ActivityWait(Job job, COM targetCOM, List<int> doer, List<int> receiver, int masterRef) : base(job, targetCOM, doer, receiver, masterRef)
    {

    }

    /// <summary>The player's late-join command (Job_Activity.JoinCOMID) rather than a wait.</summary>
    [JsonIgnore] bool IsJoin { get { return targetCOM != null && targetCOM.ID == Job_Activity.JoinCOMID; } }

    /// <summary>The player's own package - shown as a command button (Job_Activity.MakePlayerWaitPackage / MakePlayerJoinPackage).</summary>
    [JsonIgnore] bool IsPlayers { get { return DoerRefs.Contains(0); } }

    /// <summary>
    /// The player's wait button reads as joining (recreation_activity_wait_playerDisplayName - "等待活动开始" is what the
    /// NPCs are doing); everyone else's package keeps the COM's own name.
    /// </summary>
    [JsonIgnore] public override string DisplayName
    {
        get { return IsPlayers && !IsJoin ? LocalizeDictionary.QueryThenParse("recreation_activity_wait_playerDisplayName") : base.DisplayName; }
    }

    /// <summary>The player's buttons (wait and late join) also tell which activity they join: its name and details (I_ActivityBooking of the job's visit).</summary>
    public override string GetTooltips(string s)
    {
        s = base.GetTooltips(s);
        if (!IsPlayers || !(job is Job_Activity activity)) return s;
        var visit = activity.ActivityInstance;
        if (visit == null) return s;
        var line = LocalizeDictionary.QueryThenParse("recreation_activity_join_tooltip")
            .Replace("$activity$", visit.DisplayName)
            .Replace("$detail$", visit.Tooltip);
        return string.IsNullOrEmpty(s) ? line : $"{s}\n{line}";
    }

    /// <summary>A wait: the job's remaining wait (Job_Activity.WaitMinutesLeft); the late join: 1 minute; else the COM's own timeScale.</summary>
    int WaitDuration
    {
        get
        {
            if (IsJoin) return 1;
            if (job is Job_Activity activity) return activity.WaitMinutesLeft;
            return targetCOM != null ? System.Math.Max(1, targetCOM.TimeScale) : 1;
        }
    }

    /// <summary>A wait is only valid while its activity gathers, the late arrival (join) only once it has an outcome - launched or failed.</summary>
    protected override bool PreEvaluate()
    {
        if (!base.PreEvaluate()) return false;
        if (job is Job_Activity activity)
        {
            bool phaseOk = IsJoin ? activity.HasOutcome : activity.Phase == ActivityPhase.Gathering;
            if (!phaseOk)
            {
                tooltip.Add(LocalizeDictionary.QueryThenParse(IsJoin ? "ui_ap_PreEvaluate_activityNotLaunched" : "ui_ap_PreEvaluate_activityNotGathering"));
                isValid = false;
                return false;
            }
        }
        return true;
    }

    public override void ReInitializeCOM(Job job, COM targetCOM, List<int> doer, List<int> receiver, int masterRef = -1, bool resetDuration = true)
    {
        base.ReInitializeCOM(job, targetCOM, doer, receiver, masterRef, resetDuration);
        if (resetDuration) this.duration = WaitDuration;
    }

    public override void Repeat()
    {
        base.Repeat();
        this.duration = WaitDuration;
    }

    public override void Reset(bool resetRequest = false)
    {
        base.Reset(resetRequest);
        this.duration = WaitDuration;
    }

    protected override ActionPackage CopyPackage()
    {
        // fresh package: duration comes from the job at copy time, not the source package
        var copy = new ActionPackage_ActivityWait(job, targetCOM, DoerRefs, ReceiverRefs, masterRef);
        copy.SetVariantID(this.validVariant);
        copy.LoggedBegin = this.LoggedBegin;
        return copy;
    }
}
