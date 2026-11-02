using Newtonsoft.Json;

/// <summary>
/// A job an activity's begin event launched (LaunchJob) to run part of the activity - a media session
/// (Job_MediaSession), a checkup (Job_Checkup). It links itself to the activity (Job_Activity.LinkChildJob) through the
/// begin event's instanceKey; the activity then hands it the participants it wants (DispatchChild) - again after every
/// break - and once it ends they are free for the rest of their booking (OnChildJobEnded).
/// </summary>
public interface I_ActivityChildJob
{
    bool IsRunning { get; }
    /// <summary>Whether c belongs to this job (dispatched to it while it runs).</summary>
    bool Wants(Character_Trainable c);
}

/// <summary>
/// Media activities (RecreationActivity.requireMedia - RecreationUtility_Media) and child jobs retain the selected
/// media template, independent of inventory movement; the begin event's LaunchJob starts a child job that links itself
/// here; launched participants are handed to it (DispatchChild) - again after every break - and once it is over they are
/// free (no more dispatch) for the rest of their booking.
/// </summary>
public partial class Job_Activity
{
    /// <summary>Minutes after T a launched media activity waits for its session job (the begin event's LaunchJob) before dispatching as a plain activity.</summary>
    const int MediaSessionGraceMinutes = 60;

    [JsonProperty] string mediaItemID = "";
    /// <summary>The child job the begin event launched (I_ActivityChildJob), -1 = none (yet / any more).</summary>
    [JsonProperty] int childJobRef = -1;
    /// <summary>The child job ran and ended: participants are not dispatched any more.</summary>
    [JsonProperty] bool childFinished = false;

    /// <summary>The media item (base ID) this visit is held around, "" = none.</summary>
    [JsonIgnore] public string MediaItemID { get { return mediaItemID; } }
    [JsonIgnore] public RecreationMediaRequirement MediaRequirement { get { return VisitActivity?.requireMedia; } }
    /// <summary>The running child job, or null (not launched yet / over).</summary>
    [JsonIgnore] public I_ActivityChildJob ChildJob
    {
        get
        {
            if (childJobRef < 0) return null;
            var job = scr_System_CampaignManager.current.FindJobInstanceByID(childJobRef, false) as I_ActivityChildJob;
            return job != null && job.IsRunning ? job : null;
        }
    }

    /// <summary>
    /// At creation, consume the occurrence's already selected template. Never acquire or repick an inventory item.
    /// </summary>
    bool ReserveMedia(RecreationActivity activity)
    {
        if (activity?.requireMedia == null) return true;
        if (!TryGetInstance(out var g, out var b)) return false;
        if (string.IsNullOrEmpty(mediaItemID)) mediaItemID = (g != null ? g.mediaItemID : b?.mediaItemID) ?? "";
        return ItemComponentTemplate_Media.Of(mediaItemID) != null;
    }

    /// <summary>A child job registered itself (its Register) - one per activity.</summary>
    public void LinkChildJob(Job job)
    {
        if (job is I_ActivityChildJob) childJobRef = job.RefID;
    }

    /// <summary>The child job is over: from now on participants are free (DispatchChild releases them).</summary>
    public void OnChildJobEnded(Job job)
    {
        if (job == null || job.RefID != childJobRef) return;
        childFinished = true;
        childJobRef = -1;
    }

    /// <summary>Whether dispatch goes through the child job: one is (or was) linked, or a media visit waiting for its session.</summary>
    [JsonIgnore] bool UsesChildJob { get { return childJobRef >= 0 || childFinished || VisitActivity?.requireMedia != null; } }

    /// <summary>
    /// Dispatch while a child job is involved: a launched participant it wants goes (back) to it; one it doesn't want
    /// gets the plain facility dispatch. Before it exists, a media visit stays put (the begin event still under way) up to
    /// MediaSessionGraceMinutes, then falls back to the plain facility dispatch. Once it is over - nothing (false: free for
    /// the rest of the booking).
    /// </summary>
    bool DispatchChild(Character_Trainable c)
    {
        if (c == null || childFinished || phase != ActivityPhase.Launched) return false;
        var rec = FindParticipant(c.RefID);
        if (rec == null || (rec.state != ActivityParticipantState.Launched && rec.state != ActivityParticipantState.Late)) return false;
        var child = ChildJob;
        var fac = VisitActivity?.facility;
        if (child != null)
        {
            if (!child.Wants(c)) return fac != null && DispatchFacility(c, fac);
            if (child is Job_MediaSession session) session.AddParticipant(c);
            if (c.CurrentJob != (Job)child) c.ChangeCurrentJob((Job)child);
            return true;
        }
        if (VisitActivity?.requireMedia != null && MinutesSinceStart < MediaSessionGraceMinutes) return true;
        return fac != null && DispatchFacility(c, fac);
    }
}
