using System.Collections.Generic;

public class Result_Event
{
    public string id_prepend = "";
    public string id_append = "";
    public string id_overwrite = "";

    public string eventLabel = "";

    /// <summary>
    /// Agnostic to call path: pass the current EvaluationPackage for EP-scoped targets (this pairing only), or
    /// null for AP-scoped targets (the whole ActionPackage's own doer/receiver). Either way, the doer/receiver/actor
    /// targets are read directly off the AP/EP's own already-resolved Doer/Receiver/Actors — no re-derivation.
    /// </summary>
    public void Apply(ActionPackage p, EvaluationPackage evp)
    {
        if (p == null || p.targetCOM == null) return;

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

        var ev = new EventInstance(self, finalID, eventLabel);
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

        // let event scoping sort it out — further filtering (e.g. group scenes) happens
        // via the Event's own TargetValidators (EventUtility.FindTargets, e.g. ScopeWithinRef)
        scr_UpdateHandler.current.EventHandler.StartEvent(ev, false);
    }

    string ComposeID(string comID)
    {
        string baseID = id_overwrite != "" ? id_overwrite : comID;
        return id_prepend + baseID + id_append;
    }
}
