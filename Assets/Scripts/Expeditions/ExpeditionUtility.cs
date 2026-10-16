using System;
using System.Collections.Generic;

public static class ExpeditionUtility
{
    /*
    Expedition parsing
    1. read random expevent, load it into AP
    2. on AP end, load expEvent into ExpResult
       if player input required, then inject ExpResult resultHandler
    3. on job end if no unresolved then return
    4. for each unresolved, display button and on player click show event/show combat
       on combat/event resolve reinjiect into expResult
     
     */

    /// <summary> user pref: banked expedition events run as soon as they are created (only for events banked from now on) </summary>
    public static bool ImmediateRunEnabled
    {
        get
        {
            var control = scr_System_CentralControl.current;
            if (control == null || control.DisplaySetting == null) return false;
            var pref = control.DisplaySetting.immediateRunExpeditionEvent;
            return pref != null && pref.value;
        }
    }

    /// <summary>
    /// Builds the event of a banked log entry (m.unresolved) the way the resolve button plays it: self = player, and the
    /// ResolveEvent / RetryEvent / AddEventMessagePostResolve callbacks write back into m (ResolveEvent: resolved, result
    /// text logged; RetryEvent: back to unresolved = stays banked). The caller starts it. Null if m holds no package.
    /// </summary>
    public static EventInstance PrepareBankedEvent(Job_Expedition job, ExpeditionMessageEntry m)
    {
        var package = m?.unresolved;
        if (job == null || package == null || !package.isValid) return null;

        var ev = EventUtility.StartEvent(job, package);
        package.isResolved = true;

        var callbacks1 = new List<Action>();
        ev.FunctionCalls.Add("ResolveEvent", callbacks1);
        callbacks1.Add(() => {
            m.resolveEventName = package.DisplayName;
            m.resolveMessage = ev.DumpCurrentLine;
            package.isResolved = true;
            m.unresolved = null;
        });

        var callbacks2 = new List<Action>();
        ev.FunctionCalls.Add("RetryEvent", callbacks2);
        callbacks2.Add(() => {
            package.isResolved = false;
        });

        var callbacks3 = new List<Action>();
        ev.FunctionCalls.Add("AddEventMessagePostResolve", callbacks3);
        callbacks3.Add(() => {
            if (ev.AppendStrings.ContainsKey("AddEventMessagePostResolve"))
            {
                m.resolveMessage = $"{m.resolveMessage}\n{String.Join("\n", ev.AppendStrings["AddEventMessagePostResolve"])}";
            }
        });
        return ev;
    }

    /// <summary> immediateRunExpeditionEvent: plays a just-banked entry right away (same as clicking its button) </summary>
    public static void RunBankedEventNow(Job_Expedition job, ExpeditionMessageEntry m)
    {
        var ev = PrepareBankedEvent(job, m);
        if (ev != null) scr_UpdateHandler.current.EventHandler.StartEvent(ev, false);
    }

    /// <summary> another banked entry of job still waits for the player (blocks the self release entry) </summary>
    public static bool HasOtherUnresolved(Job_Expedition job, ExpeditionMessageEntry except)
    {
        if (job == null) return false;
        foreach (var list in job.ExpeditionResults.Values)
        {
            foreach (var entry in list)
            {
                if (entry == except || entry.unresolved == null || entry.unresolved.isResolved) continue;
                return true;
            }
        }
        return false;
    }

    public static ActionPackage_Expedition RandEvent(Character_Trainable c, ExpeditionInstance exp, Manageable_Party p)
    {
        var dict = new Dictionary<ActionPackage_Expedition, int>();
        foreach (var i in exp.AllEvents)
        {
            var ii = new ActionPackage_Expedition(new List<Character_Trainable>() { c }, p.Job , i);
            if (ii.weight < 1) continue;
            dict.Add(ii, ii.weight);
        }
        return Utility.WeightedRandInDict(dict);
    }

    /// <summary>
    /// Uses encounter rate to calc cooldown
    /// </summary>
    /// <param name="exp"></param>
    /// <param name="p"></param>
    /// <returns></returns>
    public static int Cooldown(ExpeditionInstance exp, Manageable_Party p)
    {
        //var totalWeight = dict.Values.Sum();
        var currentMin = scr_System_Time.current.getCurrentTime().Minute;
        //Debug.Log($"Before DiceUntil: {(int)(totalWeight / exp.EventRate)} {60 - currentMin} {exp.EventRate} {totalWeight}");
        if (exp.Base.EventRate <= 0f) return 60 - currentMin;
        if (exp.Base.EventRate >= 1.0f) return 0;
        return Utility.DiceUntil(100, 60-currentMin, (int)(exp.Base.EventRate * 100));
    }

    public static ExpResults RandResult(ExpEvents exp, Manageable_Party party, ActionPackage_Expedition p)
    {
        var dict = new Dictionary<ExpResults, int>();

        string lockedFaction = "";

        foreach(var c in p.Actors)
        {
            if (c.FactionManager.isPartyLocked)
            {
                lockedFaction = c.FactionManager.CurrentLockedParty.ID;
                break;
            }
        }

        foreach (var i in exp.possibleResults)
        {
            if (i.TargetGenerations.Count > 0)
            {
                //bool targetGenValid = true;
                string factionTemplate = p.Job_Expedition == null ? "" : p.Job_Expedition.FactionOwner_Party.OwnerFaction.ID;
                /*
                foreach(var gen in i.TargetGenerations)
                {
                    if (lockedFaction != "" && gen.factionTemplate != "" && gen.factionTemplate != lockedFaction)
                    {   
                        // then check if target is already kidnapped
                        // target is kidnapped if current faction current party guest status is visitor or prisoner

                        // allow gen if first kidnap and if same faction kidnap
                        // do not allow 2nd kidnap
                        targetGenValid = false;
                        break;
                    }
                }
                if (!targetGenValid)
                {
                    Debug.Log($"Exp RandResult [{exp.eventID}] excluding result generating factionTemplate due to active actor locked in [{lockedFaction}]");
                    // cannot resolve a second generation in a 
                    continue;
                }*/
                // check if targetgen conflict with p.
            }
            var ii = GetWeight(p.Actors, i, party);
            if (ii < 1) continue;
            dict.Add(i, ii);
        }
        return Utility.WeightedRandInDict(dict);
    }

    public static int GetWeight(List<Character_Trainable> targets, ExpResults ev, Manageable_Party p)
    {
        int weight = ev.baseWeight;

        if (!ev.teamRequirement.Validate(targets, p, out var result, out bool hardlock)) return -1;
        foreach (var wmod in ev.weightMods)
        {
            if (wmod.teamRequirement.Validate(targets, p, out var result2, out hardlock))
            {
                weight += wmod.modValue;
            }

        }
        return weight;
    }

}