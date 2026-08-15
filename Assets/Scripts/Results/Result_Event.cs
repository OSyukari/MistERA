using System.Collections.Generic;

public class Result_Event
{
    public string id_prepend = "";
    public string id_append = "";
    public string id_overwrite = "";

    public string eventLabel = "";

    public bool requireSuccess = false;
    public bool requireFailure = false;

    /// <summary>
    /// Agnostic to call path: pass the current EvaluationPackage for EP-scoped targets (this pairing only), or
    /// null for AP-scoped targets (the whole ActionPackage's own doer/receiver). Either way, the doer/receiver/actor
    /// targets are read directly off the AP/EP's own already-resolved Doer/Receiver/Actors — no re-derivation.
    /// </summary>
    public void Apply(ActionPackage p, EvaluationPackage evp)
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

        // immediateInit: false — construct without loading/validating yet, so doer/receiver/actor/standby/
        // participant are all populated into ev.Targets below BEFORE LoadNext runs Validate() (which is what
        // evaluates the Event's own TargetValidators, e.g. ScopeWithinRef reading ev.Targets["doer"/"receiver"]).
        // Validate() only ever runs once per EventInstance (guarded by instance.scoped), so if it ran during
        // construction here it would always see an empty Targets dict and any ScopeWithinRef scope keyed off
        // "doer"/"receiver"/etc. would never resolve. Mirrors the same construct-then-populate-then-LoadNext
        // sequencing already used by EventUtility.StartEvent(Job_Expedition, SerializableEventPackage).
        var ev = new EventInstance(self, "", "", 100, false);
        ev.Targets.Add("doer", doerTargets);
        ev.Targets.Add("receiver", receiverTargets);
        ev.Targets.Add("actor", actorTargets);

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
        ev.LoadNext(finalID, eventLabel);

        scr_UpdateHandler.current.EventHandler.StartEvent(ev, false);
    }

    string ComposeID(string comID)
    {
        string baseID = id_overwrite != "" ? id_overwrite : comID;
        return id_prepend + baseID + id_append;
    }
}
