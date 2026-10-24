using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class ExpeditionUtility
{
    public const string FeatureKeywordsKey = "expeditionFeatureKeywords";
    /// <summary> AppendStrings key holding the RefID of the Job_Expedition an exploration / begin event came from (ResolveJob) </summary>
    public const string JobRefKey = "expeditionJobRef";

    public static void InjectFeatureKeywords(Dictionary<string, List<string>> appendStrings, ExpeditionInstance expedition)
    {
        if (expedition != null) appendStrings[FeatureKeywordsKey] = new List<string>(expedition.FeatureKeywords);
    }

    public static void InjectJobRef(Dictionary<string, List<string>> appendStrings, Job_Expedition job)
    {
        if (job != null) appendStrings[JobRefKey] = new List<string>() { job.RefID.ToString() };
    }

    /// <summary>
    /// The expedition job of an event: AppendStrings[JobRefKey] (exploration results, begin events), else the current party
    /// job of a Targets["party"] character. Null if neither resolves.
    /// </summary>
    public static Job_Expedition ResolveJob(EventInstance ev)
    {
        if (ev == null) return null;
        if (ev.AppendStrings.TryGetValue(JobRefKey, out var refs) && refs != null && refs.Count > 0 && int.TryParse(refs[0], out var id)
            && scr_System_CampaignManager.current.FindJobInstanceByID(id, false) is Job_Expedition job) return job;
        if (ev.Targets.TryGetValue("party", out var party) && party != null)
        {
            foreach (var c in party)
            {
                var partyJob = c?.FactionManager.CurrentActiveParty?.Job;
                if (partyJob != null) return partyJob;
            }
        }
        return null;
    }

    /// <summary> Targets "party" / "teamA_frontline" / "teamA_backline" for actors of party p - as exploration events get them </summary>
    public static void FillActorTargets(Dictionary<string, List<Character_Trainable>> targets, List<Character_Trainable> actors, Manageable_Party p)
    {
        var party = new List<Character_Trainable>();
        var frontline = new List<Character_Trainable>();
        var backline = new List<Character_Trainable>();
        foreach (var c in actors)
        {
            if (c == null || party.Contains(c) || p?.GetMemberType(c)?.isHidden == true) continue;
            party.Add(c);
            var comp = p == null ? Manageable_Party.PartyComposition.none : p.GetTeamComp(c.RefID);
            if (comp == Manageable_Party.PartyComposition.frontline) frontline.Add(c);
            else if (comp == Manageable_Party.PartyComposition.backline) backline.Add(c);
        }
        targets["party"] = party;
        targets["teamA_frontline"] = frontline;
        targets["teamA_backline"] = backline;
    }

    internal static bool ReferencesParty(EventInstance ev, Manageable_Party party, IEnumerable<int> extraRefs = null)
    {
        if (ev == null || party == null) return false;
        if (ev.AppendStrings.TryGetValue(JobRefKey, out var jobs) && jobs != null && jobs.Contains(party.Job.RefID.ToString())) return true;
        var members = new HashSet<int>(party.ManagedRefs);
        if (extraRefs != null) members.UnionWith(extraRefs);
        return (ev.Self != null && members.Contains(ev.Self.RefID))
            || ev.PartyMemberSnapshots.Values.Any(roster => roster.Any(member => members.Contains(member.charaRef)))
            || ev.Targets.Values.Any(chars => chars != null && chars.Any(c => c != null && members.Contains(c.RefID)));
    }

    internal static bool ReferencesParty(SerializableEventPackage package, Manageable_Party party, IEnumerable<int> extraRefs = null)
    {
        if (package == null || party == null) return false;
        if (package.AppendStrings.TryGetValue(JobRefKey, out var jobs) && jobs != null && jobs.Contains(party.Job.RefID.ToString())) return true;
        var members = new HashSet<int>(party.ManagedRefs);
        if (extraRefs != null) members.UnionWith(extraRefs);
        return package.Targets.Values.Any(refs => refs != null && refs.Any(members.Contains))
            || (package.PartyMemberSnapshots != null && package.PartyMemberSnapshots.Values.Any(roster => roster.Any(member => members.Contains(member.charaRef))));
    }

    internal static Dictionary<int, List<PartyMemberSnapshot>> CapturePartyMembers(Manageable_Party party, IEnumerable<int> anchors)
    {
        var snapshots = new Dictionary<int, List<PartyMemberSnapshot>>();
        foreach (var anchor in anchors)
        {
            if (!snapshots.ContainsKey(anchor)) snapshots.Add(anchor, party.SnapshotMembers());
        }
        return snapshots;
    }

    public static void FillActorTargets(SerializableEventPackage package, List<Character_Trainable> actors, Manageable_Party party)
    {
        var targets = new Dictionary<string, List<Character_Trainable>>();
        FillActorTargets(targets, actors, party);
        foreach (var target in targets) package.Targets[target.Key] = target.Value.Select(c => c.RefID).Distinct().ToList();
    }

    /// <summary>
    /// Cast check of a result whose event (or its targetScope override) scopes TargetScope.PartyMembersOfRef: each such scope
    /// is run, in order, on a probe built like the real event (self = player, actors as "party", no generation, never
    /// queued). One missing its minTargetCount = the result can't be rolled now (reason added). Other scopes are left to the
    /// event's own validation.
    /// </summary>
    public static bool CheckCast(ExpResults r, List<Character_Trainable> actors, Manageable_Party p, List<string> reasons = null)
    {
        if (r == null || string.IsNullOrEmpty(r.eventID)) return true;
        var scopes = r.overrideTargetScope ? r.TargetValidators : scr_System_Serializer.current.GetEventByID(r.eventID)?.TargetValidators;
        if (scopes == null || !scopes.Exists(x => x != null && x.baseScope == TargetScope.PartyMembersOfRef)) return true;

        var player = scr_System_CampaignManager.current.Player;
        var probe = new EventInstance(player, r.eventID, "", immediateInit: false, forbidGeneration: true) { silent = true };
        FillActorTargets(probe.Targets, actors, p);
        foreach (var scope in scopes)
        {
            if (scope == null || scope.baseScope != TargetScope.PartyMembersOfRef) continue;
            if (!EventUtility.FindTargets(scope, probe, player, ref probe.Targets))
            {
                reasons?.Add($"[{r.eventID}] no valid cast for [{string.Join(",", scope.refKeys)}] ({string.Join(" ", scope.extraScopeArguments)})");
                return false;
            }
        }
        return true;
    }

    /// <summary> whether ev has at least one result rollable for actors now (GetWeight >= 1); reasons of every result if none </summary>
    public static bool HasRollableResult(ExpEvents ev, List<Character_Trainable> actors, Manageable_Party p, bool offTime, List<string> reasons = null)
    {
        if (ev == null || ev.possibleResults == null || ev.possibleResults.Count == 0) return true;   // nothing to judge
        var collected = reasons == null ? null : new List<string>();
        foreach (var r in ev.possibleResults)
        {
            if (GetWeight(actors, r, p, offTime, collected) >= 1) return true;
        }
        if (reasons != null)
        {
            reasons.Add($"[{ev.eventID}] no rollable result");
            reasons.AddRange(collected);
        }
        return false;
    }

    public static bool StartBeginEvent(Job_Expedition job)
    {
        var expedition = job?.Expedition;
        var definition = expedition?.Base;
        if (definition == null || string.IsNullOrEmpty(definition.beginEventID) || job.FactionOwner_Party == null) return false;
        if (!job.FactionOwner_Party.TryRetirePreviousHiddenCast())
        {
            job.DeferBeginEvent();
            return true;
        }

        var package = new SerializableEventPackage { eventID = definition.beginEventID };
        FillActorTargets(package, job.FactionOwner_Party.ManagedChara, job.FactionOwner_Party);
        InjectFeatureKeywords(package.AppendStrings, expedition);
        InjectJobRef(package.AppendStrings, job);
        // Begin scenes have no rolled outcome. Keep their resolved summary and EXP in the same plain log entry.
        ExpeditionMessageEntry entry = null;
        var eventName = package.DisplayName;
        var actors = job.FactionOwner_Party.ManagedChara.Where(c => c != null).Select(c => c.RefID).ToList();
        var ev = EventUtility.StartEvent(job, package, experienceObserver: record => {
            var text = new List<string>();
            MessageCollect.RecordText(record, scr_System_CampaignManager.current.Player, text);
            if (text.Count == 0) return;
            if (entry == null) entry = job.AddResult(eventName, new List<string>(), actors, registerMemory: false, merge: false);
            entry.Tooltips.AddRange(text);
            job.SyncLogMemory(entry);
        }, logEntry: create => {
            if (entry == null && create) entry = job.AddResult(eventName, new List<string>(), actors, registerMemory: false, merge: false);
            return entry;
        });
        ev.FunctionCalls["ResolveEvent"] = new List<Action> { () => {
            var summary = ev.ExpeditionSummary;
            if (string.IsNullOrEmpty(summary)) return;
            if (entry == null) entry = job.AddResult(eventName, new List<string>(), actors, registerMemory: false, merge: false);
            entry.EventDescription = summary;
            job.SyncLogMemory(entry);
        } };
        return ev.isValid && scr_UpdateHandler.current.EventHandler.StartEvent(ev, false);
    }

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

        // the entry's memory moves to the resolve time, where the scene's own memories join it
        job.TouchLogMemory(m);
        var ev = EventUtility.StartEvent(job, package, experienceObserver: record => {
            MessageCollect.RecordText(record, scr_System_CampaignManager.current.Player, m.Tooltips);
            job.SyncLogMemory(m);
        }, logEntry: create => m);
        package.isResolved = true;

        var callbacks1 = new List<Action>();
        ev.FunctionCalls.Add("ResolveEvent", callbacks1);
        callbacks1.Add(() => {
            m.resolveEventName = package.DisplayName;
            m.resolveMessage = ev.ExpeditionSummary;
            package.isResolved = true;
            m.unresolved = null;
            job.SyncLogMemory(m);
            if (job.status == Job_Expedition.ExpeditionStatus.inactive && !job.ExpeditionActive)
                job.ReleaseCompletedExpedition();
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
                job.SyncLogMemory(m);
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
                if (entry == except) continue;
                if (entry.pendingSilent != null || (entry.unresolved != null && !entry.unresolved.isResolved)) return true;
            }
        }
        return false;
    }

    /// <summary> exploration pick for a member on the job: exploration events + the current day/night list </summary>
    /// <param name="debug">optional: one line per pool event with its final weight, and why it can't be rolled (weight below 1)</param>
    public static ActionPackage_Expedition RandEvent(Character_Trainable c, ExpeditionInstance exp, Manageable_Party p, List<string> debug = null)
    {
        if (p?.HasRetiringHiddenCast == true || p?.Job?.BeginEventPending == true || p?.GetMemberType(c)?.isHidden == true) return null;
        var dict = new Dictionary<ActionPackage_Expedition, int>();
        int hour = scr_System_Time.current.getCurrentTime().Hour;
        foreach (var i in exp.GetEventPool(true, hour))
        {
            var ii = new ActionPackage_Expedition(new List<Character_Trainable>() { c }, p.Job , i);
            if (debug != null)
            {
                debug.Add($"{i.eventID}: weight {ii.weight}");
                if (ii.weight < 1 && ii.WeightTooltip != null) foreach (var reason in ii.WeightTooltip) debug.Add($"    {reason}");
            }
            if (ii.weight < 1) continue;
            dict.Add(ii, ii.weight);
        }
        return Utility.WeightedRandInDict(dict);
    }

    /// <summary>
    /// Off-time pick (Job_Expedition.TickOffTimeEvents) for a member off the job: the current day/night list only,
    /// team requirements validated in off-time mode. Null = nothing pickable.
    /// </summary>
    public static ExpEvents RandOffTimeEvent(Character_Trainable c, ExpeditionInstance exp, Manageable_Party p, List<string> debug = null)
    {
        if (p?.HasRetiringHiddenCast == true || p?.Job?.BeginEventPending == true || p?.GetMemberType(c)?.isHidden == true) return null;
        var dict = new Dictionary<ExpEvents, int>();
        int hour = scr_System_Time.current.getCurrentTime().Hour;
        var actors = new List<Character_Trainable>() { c };
        foreach (var ev in exp.GetEventPool(false, hour))
        {
            var reasons = debug == null ? null : new List<string>();
            int weight = GetEventWeight(ev, actors, p, hour, true, reasons);
            if (debug != null)
            {
                debug.Add($"{ev.eventID}: off-time weight {weight}");
                if (weight < 1) foreach (var reason in reasons) debug.Add($"    {reason}");
            }
            if (weight < 1) continue;
            dict.Add(ev, weight);
        }
        return dict.Count > 0 ? Utility.WeightedRandInDict(dict) : null;
    }

    /// <summary>
    /// Event weight: baseWeight + team weightMods, time-of-day mods (ExpEvents.timeWeightMods), then the def's keyword
    /// weightModifiers. -1 if the team requirement fails, or no result of the event could be rolled now (HasRollableResult:
    /// result requirements, time weights, the cast check). Same order as ActionPackage_Expedition's weight.
    /// </summary>
    public static int GetEventWeight(ExpEvents ev, List<Character_Trainable> actors, Manageable_Party p, int hour, bool offTime, List<string> reasons = null)
    {
        if (ev == null || (p != null && p.HasExplorationEventCooldown(ev.eventID))) return -1;
        int weight = ev.baseWeight;
        if (!ev.teamRequirement.Validate(actors, p, out var tooltip, out var hardlock, offTime))
        {
            reasons?.AddRange(tooltip);
            return -1;
        }
        foreach (var wmod in ev.weightMods)
        {
            if (wmod.teamRequirement.Validate(actors, p, out var discard, out hardlock, offTime)) weight += wmod.modValue;
        }
        weight = ev.ApplyTimeWeight(weight, hour);
        var exp = p == null || p.Job == null ? null : p.Job.Expedition;
        if (exp != null) weight = exp.GetWeightModifiers(ev.tags, weight);
        if (weight >= 1 && !HasRollableResult(ev, actors, p, offTime, reasons)) return -1;
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
        actors = actors.Where(c => c != null && jobb.FactionOwner_Party.GetMemberType(c)?.isHidden != true).Distinct().ToList();
        if (actors.Count == 0 || jobb.FactionOwner_Party.HasRetiringHiddenCast || jobb.BeginEventPending) return null;
        var expedition = jobb.Expedition;
        // Another actor may have completed this encounter since this action was selected.
        if (jobb.FactionOwner_Party.HasExplorationEventCooldown(ev.eventID)) return null;
        var result = RandResult(ev, jobb.FactionOwner_Party, actors, offTime);
        if (result == null) return null;
        // Capture before result effects can move actors or alter membership. Do not select scoped targets yet.
        var partySnapshots = string.IsNullOrEmpty(result.eventID) ? null : CapturePartyMembers(jobb.FactionOwner_Party, actors.Select(c => c.RefID));

        // Set before applying effects or starting a scene, including scenes banked for later resolution.
        jobb.FactionOwner_Party.StartExplorationEventCooldown(ev.eventID, ev.cooldownHours);

        var actorRefs = new List<int>();
        foreach (var i in actors) actorRefs.Add(i.RefID);

        // A scene needs its own stored entry, not AddResult's merged, unstored return value.
        // mirrored into the party members' memory (logCharaMemory: per-result opt-out)
        var r = jobb.AddResult($"{LocalizeDictionary.QueryThenParse(result.resultText)}", new List<string>(), new List<int>(actorRefs),
            registerMemory: result.logCharaMemory, merge: string.IsNullOrEmpty(result.eventID));
        // also covers the later syncs of a scene entry (banked resolve, silent run)
        if (!result.logCharaMemory) r.mirrorMemory = false;

        foreach (var i in actors)
        {
            ev.teamRequirement.ApplyCost(i, r == null ? null : r.Tooltips);

            foreach (var j in result.results_characters)
            {
                ResultCharaUtility.Apply(j, jobb.FactionOwner_Party, i, r == null ? null : r.Tooltips);
            }
        }

        foreach (var j in result.results_factions)
        {
            ResultFactionUtility.Apply(j, jobb, r == null ? null : r.Tooltips);
        }
        // cost/result tooltip lines - only a stored entry shows them (a merged line's r is not in the log)
        if (jobb.OwnsLogEntry(r)) jobb.SyncLogMemory(r);

        if (result.eventID != "")
        {
            var package = new SerializableEventPackage();
            package.eventID = result.eventID;
            package.eventLabel = result.eventLabel;
            package.PartyMemberSnapshots = partySnapshots;
            package.keepTargetsOffJob = offTime;
            if (result.eventAppendStringKey != "")
            {
                var list = new List<string>();
                if (result.eventAppendString != "") list.Add(result.eventAppendString);
                else list.Add(result.resultText);

                package.AppendStrings.Add(result.eventAppendStringKey, list);
            }

            var actorNames = new List<string>();
            foreach (var i in actors)
            {
                actorNames.Add(i.CallName);
            }
            FillActorTargets(package, actors, jobb.FactionOwner_Party);
            package.AppendStrings[EventInstance.HeaderLocationKey] = new List<string>() { jobb.Expedition.Base.DisplayName };
            package.AppendStrings[EventInstance.HeaderActorNamesKey] = actorNames;
            InjectFeatureKeywords(package.AppendStrings, expedition);
            InjectJobRef(package.AppendStrings, jobb);
            package.overrideTargetScope = result.overrideTargetScope;
            package.targetScopes = result.TargetValidators;
            package.overrideTargetGen = result.overrideTargetGeneration;
            package.targetGens = result.TargetGenerations;

            var mode = result.EffectiveEventMode;
            if (package.isValid && mode == ExpResults.EventMode.auto)
            {
                // silent: played at the job's next update (never inside the action that rolled it), output into r
                if (r != null) jobb.QueueSilentRun(r, package);
                else Debug.LogError("ExpResult flagged for no_logging but has an auto event.");
            }
            else if (package.isValid && mode == ExpResults.EventMode.immediate)
            {
                Debug.Log("event runimmediate start");
                var evInst = EventUtility.StartEvent(jobb, package, experienceObserver: record =>
                {
                    if (r == null) return;
                    MessageCollect.RecordText(record, scr_System_CampaignManager.current.Player, r.Tooltips);
                    jobb.SyncLogMemory(r);
                }, logEntry: create => r);
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

    /// <summary>
    /// Result weight: baseWeight + weightMods, then timeWeightMods at the current hour. -1 if the team requirement fails
    /// or the result's event misses its cast (CheckCast). reasons (optional) gets why.
    /// </summary>
    public static int GetWeight(List<Character_Trainable> targets, ExpResults ev, Manageable_Party p, bool offTime = false, List<string> reasons = null)
    {
        int weight = ev.baseWeight;

        if (!ev.teamRequirement.Validate(targets, p, out var result, out bool hardlock, offTime))
        {
            reasons?.AddRange(result);
            return -1;
        }
        foreach (var wmod in ev.weightMods)
        {
            if (wmod.teamRequirement.Validate(targets, p, out var result2, out hardlock, offTime))
            {
                weight += wmod.modValue;
            }

        }
        weight = ExpEvents.ApplyTimeWeight(ev.timeWeightMods, weight, scr_System_Time.current.getCurrentTime().Hour);
        if (weight >= 1 && !CheckCast(ev, targets, p, reasons)) return -1;
        return weight;
    }

}
