using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;


public class ActionPackage_LLM : ActionPackage
{

    [JsonIgnore] public override bool isTemporaryAP { get { return false; } }

    [JsonIgnore] public override int RoomKey { get { return scr_System_CampaignManager.current.CurrentRoom.RefID; } }

    [JsonIgnore] public override string DisplayName { get { return "LLM Package"; } }

    [JsonIgnore] public override List<int> actorRefs { get { return _actorRefs; } }

    /// <summary>
    /// Every character RefID in the FULL submitted plan this wrapper's round belongs to - batch
    /// participants AND conflict-deferred "keep for future's sake" actors alike - populated by
    /// scr_System_CampaignManager.ExecuteLLMResponseBatch. Pure reservation record: deliberately
    /// NOT part of actorRefs, so reserved actors get no job enrollment (Job.AddActor), no
    /// participant memories in Execution(), and no log treatment - their actual room-pin is the
    /// wait-package refreshed on every agent round (scr_System_CampaignManager.PinActorForLLMPlan).
    /// Kept as the hook for any future hard-pin/visibility check that wants to ask the LLM AP
    /// itself who it is holding.
    /// </summary>
    public List<int> pinnedActorRefs = new List<int>();

    public ActionPackage_LLM()
    {

    }
    
    public ActionPackage_LLM(Job job, int duration,  List<int> actorRefs, MessageJSON innerJSON)
    {
        this._actorRefs = actorRefs;
        this.jobRefID = job.RefID;
        this.job_cached = job;
        this.innerJSON = innerJSON;
        this.duration = duration;
        this.doerRefs = actorRefs;
    }
    MessageJSON innerJSON = null;

    /// <summary>
    /// Inner APs in execution order, filtered by the internalState this wrapper assigned them on its
    /// first tick (Request: accepted/refused, left at none when Validate failed).
    /// </summary>
    List<ActionPackage> InnerAPs(params AP_Status[] states)
    {
        var aps = innerJSON.GetActionPackages(out _);
        aps.Sort((x, y) => x.PackagePriority.CompareTo(y.PackagePriority));
        return aps.FindAll(x => Array.IndexOf(states, x.internalState) >= 0);
    }

    static int RepeatCount(ActionPackage ap)
    {
        int count = 0;
        foreach (var ep in ap.epjson) count = Math.Max(count, ep.repeatCount);
        return count;
    }

    static string ActorNames(List<int> refs)
    {
        var names = new List<string>();
        if (refs != null) foreach (var r in refs)
        {
            var c = scr_System_CampaignManager.current.FindInstanceByID(r);
            if (c != null) names.Add(c.FirstName);
        }
        return String.Join(", ", names);
    }

    /// <summary>
    /// Agent runs only: logs one inner AP's settled outcome onto the running agent session, for the
    /// panel's end-of-run summary tooltip. Names and game time are resolved right now. Also
    /// snapshots the AP (ActionPackageRecords) into session.executedRecords for every AP that
    /// actually ran - consumed by scr_panel_logs's Confirm to register every executed AP inside
    /// the run's single consolidated recording entry.
    /// </summary>
    static void RecordOutcome(ActionPackage ap, AP_Status outcome)
    {
        if (!ap.IsAgentRun) return;
        var session = scr_UpdateHandler.current?.CurrentAgentSession;
        if (session == null) return;

        var doers = ActorNames(ap.DoerRefs);
        var receivers = ActorNames(ap.ReceiverRefs);
        var line = LocalizeDictionary.QueryThenParse("ui_llm_agentExec_line", "[$time$] $name$ ($outcome$) $actors$ x$count$")
            .Replace("$time$", scr_System_Time.current.getCurrentTime().ToString("HH:mm"))
            .Replace("$name$", ap.DisplayName)
            .Replace("$outcome$", LocalizeDictionary.QueryThenParse($"ui_llm_agentExec_{outcome}", outcome.ToString()))
            .Replace("$actors$", receivers.Length > 0 ? $"{doers} → {receivers}" : doers)
            .Replace("$count$", RepeatCount(ap).ToString());
        session.executedLog.Add(new AgentExecutedRecord { outcome = outcome, line = line });

        // recording counterpart of executedLog: 'none' outcomes never passed Validate, so they
        // never executed and register nothing
        if (outcome != AP_Status.none) session.executedRecords.Add(new ActionPackageRecords(ap));
    }


    public override bool Tick(List<int> actorList, int tickDuration = 1, MessageCollect m = null)
    {
        Debug.Log($"llm package ticked! currentDuration {this.Duration}, tick {tickDuration}, doers {String.Join(" ", DoerRefs)}, receivers {String.Join(" ", ReceiverRefs)}");
        return base.Tick(actorList, tickDuration, m);
    }

    protected override bool PreEvaluate()
    {
        isValid = true;

        if (job == null)
        {
            tooltip.Add("ActionPackage preEvaluation: job is null");
            isValid = false;
        }
        else if (innerJSON == null)
        {
            tooltip.Add("ActionPackage preEvaluation: innerJSON is null");
            isValid = false;
        }

        else if (innerJSON.GetActionPackages(out var tooltops2).Count < 1)
        {
            tooltip.Add("ActionPackage preEvaluation: innerJSON no package");
            isValid = false;
        }


        //displayName = "";
        if (tooltip.Count > 0)
        {
            //displayName += String.Join("\n", tooltip);
            Debug.Log("actorPackage pathTo PreEvaluate: [" + String.Join("\n", tooltip) + "]");
        }

        return isValid;
    }

    public override ActionPackage Copy()
    {
        return this;
    }

    protected override bool Evaluate()
    {
        //displayName += (displayName.Length > 0 ? "\n":"")+"Moving to " + scr_System_CampaignManager.current.Map.GetRoomByRef(targetRoomRef).DisplayName;
        return true;
    }

    /// <summary>
    /// Wrapper itself has no EP and is never refused - this is where every inner AP gets its own
    /// accept/refuse roll, on the wrapper's first tick, the same point Tick() rolls a normal AP.
    /// </summary>
    /// <returns></returns>
    protected override bool Request(bool rebuildPackage = true, Memory_Response forceAccept = Memory_Response.None)
    {
        // inner rolls happen once; a later re-request (e.g. retryRequest on wake-up) must not re-roll
        if (requested) return true;
        requested = true;

        foreach (var ap in InnerAPs(AP_Status.none))
        {
            // inner execution messages are consolidated into the wrapper's single recorded entry
            // (RecordConfirmedResponse) instead of being recorded individually; same for memory -
            // one consolidated wrapper entry per actor is registered post-confirm instead of
            // per-EP AddEntry calls (see RegisterConsolidatedMemory)
            ap.suppressRoomRecording = true;
            ap.suppressMemoryLogging = true;
            if (!ap.Validate())
            {
                RecordOutcome(ap, AP_Status.none);
                continue;
            }
            // Single-shot keeps the old "always accepted" guarantee since the model already wrote its
            // narrative around a successful outcome; agent mode lets the real EP-level roll refuse so
            // the model can weave that into its own narration instead.
            var apForceAccept = ap.IsAgentRun ? Memory_Response.None : Memory_Response.Accept;
            ap.internalState = ap.RequestOutsideUpdate(apForceAccept) ? AP_Status.accepted : AP_Status.refused;
        }
        return true;
    }

    public override void LogAcceptanceCheck(MessageCollect m = null)
    {
        base.LogAcceptanceCheck(m);
        if (m == null) m = job.m;
        foreach (var ap in InnerAPs(AP_Status.accepted, AP_Status.refused)) ap.LogAcceptanceCheck(m);
    }

    /// <summary>
    /// Base PackageBegin bails on targetCOM == null, so the wrapper runs the inner APs' begin phase
    /// itself: accepted ones get their full PackageBegin (ExecuteImmediate + launch options +
    /// LogMessage_Begin), refused ones mirror Tick()'s refuse branch - Begin_Refuse and immediate
    /// execution - then are collected (fills capturedLog for the agent runner) and retired, so the
    /// wrapper's Execution only has accepted ones left.
    /// </summary>
    protected override void PackageBegin(MessageCollect m = null)
    {
        if (LoggedBegin) return;
        LoggedBegin = true;
        if (m == null) m = job.m;

        foreach (var ap in InnerAPs(AP_Status.accepted)) ap.PackageBeginOutsideUpdate(m);

        foreach (var ap in InnerAPs(AP_Status.refused))
        {
            ap.Ticked = true;
            ap.LogMessage_Begin_Refuse(m);
            ap.ExecutePackageOutsideUpdate(m);
            job.CollectLogs(ap);
            RecordOutcome(ap, AP_Status.refused);
            CaptureMemory(ap);
            ap.DisablePackage();
            scr_System_CampaignManager.current.Unregister(ap);
        }
    }

    /// <summary>
    /// Wrapper interrupted mid-round: inner APs that already printed Begin but haven't executed get
    /// their abort line too.
    /// </summary>
    public override void LogMessage_Begin_Abort(MessageCollect m = null)
    {
        base.LogMessage_Begin_Abort(m);
        if (m == null) m = job.m;
        foreach (var ap in InnerAPs(AP_Status.accepted))
        {
            ap.LogMessage_Begin_Abort(m);
            ap.internalState = AP_Status.aborted;
            RecordOutcome(ap, AP_Status.aborted);
        }
    }

    public override void LogMessage_Ongoing(MessageCollect m, Character_Trainable target = null)
    {
        Debug.LogError($"LogMessage_Ongoing called on LLM package - inner APs are not forwarded, doers {String.Join(" ", DoerRefs)}");
        // base dereferences targetCOM, which the wrapper never has
        if (targetCOM != null) base.LogMessage_Ongoing(m, target);
    }
    [JsonIgnore]
    public override bool AllowJoining
    {
        get
        {
            return false;
        }
    }

    /// <summary>
    /// move one step along the path. Does not have EvaluationPackage attached to it !!!!
    /// </summary>
    protected override void Execution(MessageCollect m = null, List<Action> eventCollector = null)
    {
        Debug.Log("ActionPackage_LLM Execute!");

        if (m == null) m = job.m;
        if (!requested) Request();

        // memory for this run is NOT registered here anymore: inner APs run with
        // suppressMemoryLogging, and one consolidated wrapper entry per actor is registered at
        // confirm time instead (agent: Button_Confirm; single-shot: wrapper execution end, since
        // its execution only ever starts from Button_Confirm) - see RegisterConsolidatedMemory

        job.m.displayOverride = true;

        List<string> execresult = new List<string>();

        foreach (var ap in InnerAPs(AP_Status.none)) execresult.Add($"{ap.DisplayName} did not pass validate");
        foreach (var ap in InnerAPs(AP_Status.refused)) execresult.Add($"{ap.DisplayName} refused, executed on first tick");

        // Only accepted APs are left: their begin phase already ran on the wrapper's first tick, so
        // this is their command-complete point (LogResultCheck/Kojo/Climax/After fire inside Execution).
        foreach (var ap in InnerAPs(AP_Status.accepted))
        {
            if (ap.Validate())
            {
                int count = 0;
                foreach (var ep in ap.epjson) count = Math.Max(count, ep.repeatCount);
                ap.internalState = AP_Status.success;
                for(int i = 0; i < count; i++)
                {
                    ap.ExecutePackageOutsideUpdate(job.m);
                }
                execresult.Add($"{ap.DisplayName} x{count}");
                job.CollectLogs(ap);
                RecordOutcome(ap, AP_Status.success);
                CaptureMemory(ap);
            }
            else
            {
                // began on the first tick but no longer valid at completion
                ap.LogMessage_Begin_Abort(job.m);
                ap.internalState = AP_Status.aborted;
                RecordOutcome(ap, AP_Status.aborted);
                execresult.Add($"{ap.DisplayName} no longer valid at completion, aborted");
            }
            ap.DisablePackage();
            scr_System_CampaignManager.current.Unregister(ap);
        }
        Debug.Log($"End LLM Execution, executed package {execresult.Count}\n{String.Join("\n", execresult)}");
        //scr_UpdateHandler.current.skipCurrentRoundClimaxCheck = true;
        job.m.exp.leftAlignOverride = true;

        //m.messages_before.Clear();
        //m.messages_after.Clear();
        //m.messages_kojo.Clear();
        //m.messages_kojo_after.Clear();

        FlushConsolidatedMemory();

        job.NotifyDescriptionsOutOfUpdate(false);
        scr_UpdateHandler.current.FlushCollectedLogs(true, true);

    }

    // ---------------- consolidated memory (see RegisterConsolidatedMemory) ----------------

    /// <summary>Accumulated (AP x actor) interactions captured from executed inner APs - survives
    /// across the wrapper's first tick (refused APs are captured in PackageBegin) until Execution
    /// flushes it.</summary>
    [JsonIgnore] List<LLMMemoryInteraction> pendingMemory = new List<LLMMemoryInteraction>();
    /// <summary>First-experience records deferred by the suppressed inner executions.</summary>
    [JsonIgnore] List<EvaluationPackage.DelayedFirstExperience> pendingFirstExp = new List<EvaluationPackage.DelayedFirstExperience>();
    /// <summary>Whether any inner AP ran as part of an agent round (trackCapture) - decides where
    /// the accumulation goes: agent sessions flush into LLMAgentSession for Button_Confirm to
    /// consume, single-shot registers right here (its execution only ever starts from
    /// Button_Confirm, so wrapper execution end IS post-confirm).</summary>
    [JsonIgnore] bool pendingAgentRun = false;

    /// <summary>
    /// Mirrors MemoryManager.AddEntry(ep)'s role/tag/attitude resolution, but instead of
    /// registering a memory entry per EP right away, captures one LLMMemoryInteraction per
    /// (EP x participating actor) for the consolidated post-confirm registration. Also drains the
    /// AP's deferred first-experience records (see EvaluationPackage.Execute's suppression path).
    /// </summary>
    void CaptureMemory(ActionPackage ap)
    {
        pendingAgentRun = pendingAgentRun || ap.IsAgentRun;

        foreach (var ep in ap.ListEP)
        {
            foreach (var c in ep.Actors)
            {
                List<string> tags;
                Memory_Attitude attitude;
                bool isDoer = false;
                if (ep.Doer == c)
                {
                    tags = ep.ReceiverTargetTag;
                    attitude = ep.DoerAttitude;
                    isDoer = true;
                }
                else if (ep.Receiver == c && !ap.ComTags.Contains("ignored"))
                {
                    tags = ep.DoerTargetTag;
                    attitude = ep.ReceiverAttitude;
                }
                else if (ep.Master == c)
                {
                    tags = new List<string>();
                    attitude = Memory_Attitude.Neutral;
                }
                else continue;

                var targets = new List<int>();
                foreach (var t in ep.Actors) if (t != c) targets.Add(t.RefID);

                pendingMemory.Add(new LLMMemoryInteraction()
                {
                    ownerRef = c.RefID,
                    isDoer = isDoer,
                    masterRef = ep.Master == null ? -1 : ep.Master.RefID,
                    comID = ep.targetCOM == null ? "" : ep.targetCOM.ID,
                    response = ep.Response,
                    attitude = attitude,
                    description = ap.DescriptionText(c.RefID),
                    targets = targets,
                    tags = new List<string>(tags)
                });
            }
        }

        if (ap.suppressedFirstExp != null)
        {
            pendingFirstExp.AddRange(ap.suppressedFirstExp);
            ap.suppressedFirstExp = null;
        }
    }

    /// <summary>
    /// End-of-Execution destination of the captured memory data: agent rounds flush into the
    /// running session (consumed by scr_panel_logs.Button_Confirm once the player accepts the
    /// run), single-shot registers immediately - ExecuteLLMResponse only ever runs from
    /// Button_Confirm, so this moment is already post-confirm for it.
    /// </summary>
    void FlushConsolidatedMemory()
    {
        if (pendingMemory.Count < 1 && pendingFirstExp.Count < 1) return;

        if (pendingAgentRun)
        {
            var session = scr_UpdateHandler.current?.CurrentAgentSession;
            if (session != null)
            {
                session.memoryInteractions.AddRange(pendingMemory);
                session.memoryFirstExps.AddRange(pendingFirstExp);
                session.memoryDuration += innerJSON.timeCost;
            }
        }
        else
        {
            RegisterConsolidatedMemory(innerJSON.summary, pendingMemory, pendingFirstExp, innerJSON.relevantActorRefs, innerJSON.timeCost);
        }

        pendingMemory.Clear();
        pendingFirstExp.Clear();
    }

    /// <summary>
    /// Registers the consolidated LLM memory for a finalized session: for every relevant actor ONE
    /// wrapper entry (entryDescription = summary) via MemoryManager.AddLLMEntry, holding one
    /// interaction instance per executed AP the actor participated in; relevant-but-
    /// non-participating actors get the wrapper with a lightweight "present" instance (no
    /// witnessing system exists - that instance is the stand-in). Then replays the deferred
    /// first-experience records so the LLM interaction triggers first experience from here, never
    /// from the suppressed inner executions. Everything the inner EPs applied as side effects
    /// (experience gains, relationship deltas, stat costs, body state) is untouched by all this.
    /// </summary>
    public static void RegisterConsolidatedMemory(string summary, List<LLMMemoryInteraction> interactions, List<EvaluationPackage.DelayedFirstExperience> firstExps, List<int> relevantActorRefs, int duration)
    {
        if (interactions == null || interactions.Count < 1) return;
        if (string.IsNullOrEmpty(summary)) summary = LocalizeDictionary.QueryThenParse("ui_memory_llm_summary_default", "LLM interaction");

        var byOwner = new Dictionary<int, List<MemInstance>>();
        foreach (var inter in interactions)
        {
            var inst = new MemInstance(inter.targets, inter.tags, inter.comID, -1, inter.masterRef, inter.isDoer, inter.response, inter.attitude, inter.description);
            if (!byOwner.TryGetValue(inter.ownerRef, out var list)) byOwner[inter.ownerRef] = list = new List<MemInstance>();
            list.Add(inst);
        }

        var roomRef = scr_System_CampaignManager.current.CurrentRoom.RefID;

        var actors = new List<int>(relevantActorRefs);
        foreach (var ownerRef in byOwner.Keys) if (!actors.Contains(ownerRef)) actors.Add(ownerRef);

        foreach (var actorRef in actors)
        {
            var actor = scr_System_CampaignManager.current.FindInstanceByID(actorRef);
            if (actor == null) continue;

            byOwner.TryGetValue(actorRef, out var instances);
            if (instances == null)
            {
                // wrapper-only registration for relevant non-participants - the present-instance
                // carries the other run participants as targets so the wrapper line's $names$
                // (Memory_Entry.TargetNames) resolves for them too
                var presentTargets = new List<int>(actors);
                presentTargets.Remove(actorRef);
                instances = new List<MemInstance>() { new MemInstance(presentTargets, new List<string>(), "", -1, -1, true, Memory_Response.Accept, Memory_Attitude.Neutral, LocalizeDictionary.QueryThenParse("ui_memory_llm_present", "was present")) };
            }
            actor.Memory.AddLLMEntry(summary, instances, roomRef, duration);
        }

        if (firstExps == null) return;
        foreach (var fe in firstExps)
        {
            var body = fe.exp.body;
            if (body == null || body.Owner == null) continue;
            if (body.NotifySexExperience(fe.hasPermission, fe.exp.targetName, fe.exp.comName, fe.exp.comtags, fe.exp.targetBodytags))
            {
                // first experience loss - same message and important memory entry the per-EP
                // loop in EvaluationPackage.Execute produces, replayed here at confirm time
                string s = LocalizeDictionary.QueryThenParse("messagelog_lose_first_experience").Replace("$bodypart$", body.DisplayName);
                UtilityEX.StringReplace(body.Owner, ref s);
                scr_System_CampaignManager.current.AddLog(body.Owner.RefID, s, true);

                var memInst2 = new MemInstance(new List<int>() { fe.exp.targetRef }, new List<string>() { "important" }, "", -1, -1, false, Memory_Response.Accept, fe.attitude, body.FirstExperienceDesc);
                body.Owner.Memory.AddEntry(memInst2, new List<string>() { "important" }, -2, true);
            }
        }
    }

    public override void DisablePackage(bool extraTick = false)
    {
        base.DisablePackage(extraTick);
    }


}
