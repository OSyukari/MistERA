using System.Collections.Generic;

/// <summary> Result_Event.printOrder: where an AP-scoped event's output goes relative to its command's completion messages </summary>
public enum EventPrintOrder
{
    /// <summary> the command's completion messages (completion text, stat EXP) first, then the event (default) </summary>
    AfterCompletion,
    /// <summary> the event first: the completed package's logs wait (CompletionLogHold) until the event ends </summary>
    BeforeCompletion,
}

/// <summary>
/// The logs of a completed package whose event prints first (EventPrintOrder.BeforeCompletion): Job.PostUpdateTime collects
/// that round's job log as usual (completion text, the actors' stat EXP - written straight into Job.m - finalized), then
/// hands it here instead of to the update handler. Released once both the logs are in and the event has ended -
/// queued behind the event's own output (AddEventCallback), so they print right after it. An event that never starts
/// still completes (EventInstance.OnCompleted), so the logs are never lost.
/// </summary>
public class CompletionLogHold
{
    public readonly MessageCollect Collector = new MessageCollect();
    bool logsReady, eventDone, released;

    public void LogsReady() { logsReady = true; TryRelease(); }
    public void EventDone() { eventDone = true; TryRelease(); }

    void TryRelease()
    {
        // LogsReady comes from Job.PostUpdateTime (run in parallel per job), EventDone from the main thread
        lock (Collector)
        {
            if (!logsReady || !eventDone || released) return;
            released = true;
        }
        scr_UpdateHandler.current.AddEventCallback(() =>
        {
            scr_UpdateHandler.current.NotifyJobDescriptions(Collector);
            scr_UpdateHandler.current.FlushCollectedLogs(true, false);
        });
    }
}

public class Result_Event
{
    public string id_prepend = "";
    public string id_append = "";
    public string id_overwrite = "";

    public string eventLabel = "";

    public bool requireSuccess = false;
    public bool requireFailure = false;
    /// <summary>AP-scoped successful sleep commands only: finish this event before the sleep transition.</summary>
    public bool deferSleepUntilEnd = false;
    /// <summary>
    /// The player becomes the event's self (who answers its questions) when they are one of the actors, instead of
    /// the first doer - e.g. a shared meal the player joined (com_furniture_getmeal_event). Doer / receiver / actor
    /// targets are unchanged.
    /// </summary>
    public bool playerAsSelf = false;
    /// <summary>
    /// AP-scoped events (results_AP) only: print the event before or after the command's completion messages
    /// (BeforeCompletion holds the package's logs until the event ends - CompletionLogHold). Default AfterCompletion.
    /// </summary>
    public EventPrintOrder printOrder = EventPrintOrder.AfterCompletion;

    /// <summary>
    /// Agnostic to call path: pass the current EvaluationPackage for EP-scoped targets (this pairing only), or
    /// null for AP-scoped targets (the whole ActionPackage's own doer/receiver). Either way, the doer/receiver/actor
    /// targets are read directly off the AP/EP's own already-resolved Doer/Receiver/Actors — no re-derivation.
    /// prepareSleepEvent (deferSleepUntilEnd): gets the bedtime event and its start - the sleep transition starts it, at
    /// once or once the player has chosen how long to sleep (PlayerSleepChoice); null start = a silent run, started here.
    /// </summary>
    public void Apply(ActionPackage p, EvaluationPackage evp, System.Action<EventInstance, System.Action> prepareSleepEvent = null)
    {
        if (p == null || p.targetCOM == null) return;

        if (requireSuccess || requireFailure)
        {
            // matches EvaluationPackage.Execute's own success predicate (response == Accept || response >= Success)
            bool isSuccess = evp != null
                ? (evp.Response == Memory_Response.Accept || evp.Response >= Memory_Response.Success)
                : p.executeSuccessful;

            if (requireSuccess && !isSuccess) return;
            if (requireFailure && isSuccess) return;
        }

        string finalID = ComposeID(p.targetCOM.ID);
        if (finalID == "") return;

        List<Character_Trainable> doerTargets, receiverTargets, actorTargets;

        if (evp != null)
        {
            if (evp.Doer == null) return;
            doerTargets = new List<Character_Trainable>() { evp.Doer };
            receiverTargets = evp.Receiver != null ? new List<Character_Trainable>() { evp.Receiver } : new List<Character_Trainable>();
            actorTargets = new List<Character_Trainable>(evp.Actors);
        }
        else
        {
            if (p.doer.Count < 1) return;
            doerTargets = new List<Character_Trainable>(p.doer);
            receiverTargets = new List<Character_Trainable>(p.receiver);
            actorTargets = new List<Character_Trainable>(p.Actors);
        }

        var self = doerTargets[0];
        if (playerAsSelf)
        {
            var player = scr_System_CampaignManager.current.Player;
            if (player != null && actorTargets.Contains(player)) self = player;
        }

        // immediateInit: false — construct without loading/validating yet, so doer/receiver/actor/standby/
        // participant are all populated into ev.Targets below BEFORE LoadNext runs Validate() (which is what
        // evaluates the Event's own TargetValidators, e.g. ScopeWithinRef reading ev.Targets["doer"/"receiver"]).
        // Validate() only ever runs once per EventInstance (guarded by instance.scoped), so if it ran during
        // construction here it would always see an empty Targets dict and any ScopeWithinRef scope keyed off
        // "doer"/"receiver"/etc. would never resolve. Mirrors the same construct-then-populate-then-LoadNext
        // sequencing already used by EventUtility.StartEvent(Job_Expedition, SerializableEventPackage).
        var ev = new EventInstance(self, "", "", 100, false);
        // before the event can run (an NPC-only one finishes synchronously): the package's logs wait for its end
        if (printOrder == EventPrintOrder.BeforeCompletion && evp == null)
        {
            var hold = new CompletionLogHold();
            p.CompletionLogHold = hold;
            ev.OnCompleted(hold.EventDone);
        }
        var parent = p.SourceEvent;
        ev.LogMemoryResolver = parent?.LogMemoryResolver;
        if (parent != null && parent.silent)
        {
            ev.silent = true;
            ev.SilentSink = parent.SilentSink;
        }
        ev.Targets.Add("doer", doerTargets);
        ev.Targets.Add("receiver", receiverTargets);
        ev.Targets.Add("actor", actorTargets);
        // the job the package ran on (e.g. the crane machine's owner, CraneMachineUtility.ResolveOwner)
        if (p.job != null) ev.AppendStrings[CraneMachineUtility.SourceJobRefKey] = new List<string>() { p.job.RefID.ToString() };
        bool sleepTransition = prepareSleepEvent != null && deferSleepUntilEnd && evp == null && p.executeSuccessful
            && p.targetCOM.comTags.Contains("sleep");

        // standby: AP-wide actors assigned to no EvaluationPackage at all (e.g. leftover unpaired
        // doer/receiver in a multi-actor AP whose random-match pairing didn't cover everyone)
        var assigned = new List<Character_Trainable>();
        foreach (var otherEp in p.ListEP) assigned.AddRange(otherEp.Actors);
        var standby = new List<Character_Trainable>(p.Actors);
        standby.RemoveAll(x => assigned.Contains(x));
        ev.Targets.Add("standby", standby);

        if (evp != null)
        {
            // participant: every other EP's actor(s), excluding anyone already in this EP's own actor list
            var participant = new List<Character_Trainable>();
            foreach (var otherEp in p.ListEP)
            {
                if (otherEp == evp) continue;
                participant.AddRange(otherEp.Actors);
            }
            participant.RemoveAll(x => actorTargets.Contains(x));
            Utility.DistinctInPlace(participant);
            ev.Targets.Add("participant", participant);
        }

        // now that every target list above is populated, actually load/validate the event — this is what
        // runs TargetValidators (EventUtility.FindTargets, e.g. ScopeWithinRef), so further filtering
        // (e.g. group scenes) can correctly read ev.Targets["doer"/"receiver"/etc.]
        if (ev.silent)
        {
            if (sleepTransition) prepareSleepEvent(ev, null);
            try { ExpeditionSilentRunner.RunChild(parent, ev, finalID, eventLabel); }
            finally { ev.CompleteCallbacks(); }
            return;
        }

        if (sleepTransition) prepareSleepEvent(ev, () => Start(ev, finalID));
        else Start(ev, finalID);
    }

    void Start(EventInstance ev, string finalID)
    {
        try
        {
            ev.LoadNext(finalID, eventLabel);
            if (!ev.isValid || !scr_UpdateHandler.current.EventHandler.StartEvent(ev, false)) ev.CompleteCallbacks();
        }
        catch
        {
            ev.CompleteCallbacks();
            throw;
        }
    }

    string ComposeID(string comID)
    {
        string baseID = id_overwrite != "" ? id_overwrite : comID;
        return id_prepend + baseID + id_append;
    }
}
