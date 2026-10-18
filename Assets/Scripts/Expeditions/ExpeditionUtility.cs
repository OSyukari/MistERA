using System;
using System.Collections.Generic;
using UnityEngine;

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

    /// <summary> exploration pick for a member on the job: exploration events + the current day/night list </summary>
    public static ActionPackage_Expedition RandEvent(Character_Trainable c, ExpeditionInstance exp, Manageable_Party p)
    {
        var dict = new Dictionary<ActionPackage_Expedition, int>();
        int hour = scr_System_Time.current.getCurrentTime().Hour;
        foreach (var i in exp.GetEventPool(true, hour))
        {
            var ii = new ActionPackage_Expedition(new List<Character_Trainable>() { c }, p.Job , i);
            if (ii.weight < 1) continue;
            dict.Add(ii, ii.weight);
        }
        return Utility.WeightedRandInDict(dict);
    }

    /// <summary>
    /// Off-time pick (Job_Expedition.TickOffTimeEvents) for a member off the job: the current day/night list only,
    /// team requirements validated in off-time mode. Null = nothing pickable.
    /// </summary>
    public static ExpEvents RandOffTimeEvent(Character_Trainable c, ExpeditionInstance exp, Manageable_Party p)
    {
        var dict = new Dictionary<ExpEvents, int>();
        int hour = scr_System_Time.current.getCurrentTime().Hour;
        var actors = new List<Character_Trainable>() { c };
        foreach (var ev in exp.GetEventPool(false, hour))
        {
            int weight = GetEventWeight(ev, actors, p, hour, true);
            if (weight < 1) continue;
            dict.Add(ev, weight);
        }
        return dict.Count > 0 ? Utility.WeightedRandInDict(dict) : null;
    }

    /// <summary>
    /// Event weight: baseWeight + team weightMods, time-of-day mods (ExpEvents.timeWeightMods), then the def's keyword
    /// weightModifiers. -1 if the team requirement fails. Same order as ActionPackage_Expedition's weight.
    /// </summary>
    public static int GetEventWeight(ExpEvents ev, List<Character_Trainable> actors, Manageable_Party p, int hour, bool offTime)
    {
        if (ev == null || (p != null && p.HasExplorationEventCooldown(ev.eventID))) return -1;
        int weight = ev.baseWeight;
        if (!ev.teamRequirement.Validate(actors, p, out var tooltip, out var hardlock, offTime)) return -1;
        foreach (var wmod in ev.weightMods)
        {
            if (wmod.teamRequirement.Validate(actors, p, out var discard, out hardlock, offTime)) weight += wmod.modValue;
        }
        weight = ev.ApplyTimeWeight(weight, hour);
        var exp = p == null || p.Job == null ? null : p.Job.Expedition;
        if (exp != null) weight = exp.GetWeightModifiers(ev.tags, weight);
        return weight;
    }

    /// <summary>
    /// Rolls and applies a result of ev for actors (log line, costs, results_characters, memory, faction results, then the
    /// result's event: runImmediate / banked / immediateRunExpeditionEvent). Shared by the exploration action
    /// (ActionPackage_Expedition.Execution, which then applies progress and cooldown) and off-time events (offTime: no
    /// progress, no general encounter cooldown, and a banked entry keeps its targets while they stay off the job).
    /// Both paths respect and start the party's per-event cooldown. Null = no result.
    /// </summary>
    public static ExpResults ApplyResult(Job_Expedition jobb, ExpEvents ev, List<Character_Trainable> actors, string memEntryName, bool offTime)
    {
        if (jobb == null || ev == null || actors == null || actors.Count < 1) return null;
        // Another actor may have completed this encounter since this action was selected.
        if (jobb.FactionOwner_Party.HasExplorationEventCooldown(ev.eventID)) return null;
        var result = RandResult(ev, jobb.FactionOwner_Party, actors, offTime);
        if (result == null) return null;

        // Set before applying effects or starting a scene, including scenes banked for later resolution.
        jobb.FactionOwner_Party.StartExplorationEventCooldown(ev.eventID, ev.cooldownHours);

        var actorRefs = new List<int>();
        foreach (var i in actors) actorRefs.Add(i.RefID);

        var r = jobb.AddResult($"{LocalizeDictionary.QueryThenParse(result.resultText)}", new List<string>(), new List<int>(actorRefs));

        foreach (var i in actors)
        {
            ev.teamRequirement.ApplyCost(i, r == null ? null : r.Tooltips);

            foreach (var j in result.results_characters)
            {
                ResultCharaUtility.Apply(j, jobb.FactionOwner_Party, i, r == null ? null : r.Tooltips);
            }

            if (result.logCharaMemory)
            {
                var ids = new List<int>();
                var names2 = new List<string>();
                foreach (var j in actors)
                {
                    if (j == i) continue;
                    ids.Add(j.RefID);
                    names2.Add(j.CallName);
                }
                var newMem = new MemInstance(ids, new List<string>(), "", -1, -1, true, Memory_Response.Accept, Memory_Attitude.Neutral,
                                    LocalizeDictionary.QueryThenParse(result.resultText).Replace("$names$", names2.Count > 0 ?
                                        LocalizeDictionary.QueryThenParse("exp_event_active_memory_teammates").Replace("$team$", String.Join(", ", names2)) : ""));
                var entry = i.Memory.AddEntry(newMem, new List<string>() { "expedition" });
                entry.entryDescription = memEntryName;
                entry.disableRoomName = true;
            }
        }

        foreach (var j in result.results_factions)
        {
            ResultFactionUtility.Apply(j, jobb, r == null ? null : r.Tooltips);
        }

        if (result.eventID != "")
        {
            var package = new SerializableEventPackage();
            package.eventID = result.eventID;
            package.eventLabel = result.eventLabel;
            package.keepTargetsOffJob = offTime;
            if (result.eventAppendStringKey != "")
            {
                var list = new List<string>();
                if (result.eventAppendString != "") list.Add(result.eventAppendString);
                else list.Add(result.resultText);

                package.AppendStrings.Add(result.eventAppendStringKey, list);
            }

            List<int> party = new List<int>(), frontline = new List<int>(), backline = new List<int>();
            var actorNames = new List<string>();
            foreach (var i in actors)
            {
                party.Add(i.RefID);
                actorNames.Add(i.CallName);
                switch (jobb.FactionOwner_Party.GetTeamComp(i.RefID))
                {
                    case Manageable_Party.PartyComposition.frontline:
                        frontline.Add(i.RefID);
                        break;
                    case Manageable_Party.PartyComposition.backline:
                        backline.Add(i.RefID);
                        break;
                    default: break;

                }
            }
            package.Targets.Add("party", party);
            package.Targets.Add("teamA_frontline", frontline);
            package.Targets.Add("teamA_backline", backline);
            package.AppendStrings[EventInstance.HeaderLocationKey] = new List<string>() { jobb.Expedition.Base.DisplayName };
            package.AppendStrings[EventInstance.HeaderActorNamesKey] = actorNames;
            package.overrideTargetScope = result.overrideTargetScope;
            package.targetScopes = result.TargetValidators;
            package.overrideTargetGen = result.overrideTargetGeneration;
            package.targetGens = result.TargetGenerations;

            if (package.isValid && result.runImmediate)
            {
                Debug.Log("event runimmediate start");
                var evInst = EventUtility.StartEvent(jobb, package);
                scr_UpdateHandler.current.EventHandler.StartEvent(evInst, false);
            }
            else if (r != null)
            {
                r.unresolved = package;
                // user pref: play it now, exactly as if its button was clicked (RetryEvent leaves it banked)
                if (ImmediateRunEnabled) RunBankedEventNow(jobb, r);
            }
            else
            {
                Debug.LogError("ExpResult flagged for no_logging but has unresolved package.");
            }
        }
        return result;
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

    public static ExpResults RandResult(ExpEvents exp, Manageable_Party party, List<Character_Trainable> actors, bool offTime = false)
    {
        var dict = new Dictionary<ExpResults, int>();

        string lockedFaction = "";

        foreach(var c in actors)
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
                string factionTemplate = party == null ? "" : party.OwnerFaction.ID;
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
            var ii = GetWeight(actors, i, party, offTime);
            if (ii < 1) continue;
            dict.Add(i, ii);
        }
        return Utility.WeightedRandInDict(dict);
    }

    public static int GetWeight(List<Character_Trainable> targets, ExpResults ev, Manageable_Party p, bool offTime = false)
    {
        int weight = ev.baseWeight;

        if (!ev.teamRequirement.Validate(targets, p, out var result, out bool hardlock, offTime)) return -1;
        foreach (var wmod in ev.weightMods)
        {
            if (wmod.teamRequirement.Validate(targets, p, out var result2, out hardlock, offTime))
            {
                weight += wmod.modValue;
            }

        }
        return weight;
    }

}
