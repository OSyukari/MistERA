using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Agent-mode tool: validate and, if valid, actually execute a LIST of actions for real (per the
/// user's locked-in "don't simulate, execute + checkpoint" design - see the architecture doc).
/// Stepwise batching via the shared scr_System_CampaignManager.BuildAPBatch - the same strict-JoinAP
/// partition the submit_response plan-batch flow uses: this round executes only the anchor action
/// plus every later action that can join it; conflicting actions are NOT executed, they come back as
/// remainingActions for the model to resubmit after seeing this round's real results (two talks
/// against different jobs = two rounds, by design). The batch is registered through
/// ExecuteLLMResponseBatch (ActionPackage_LLM wrapper + placeholders + wait-packages + FreeUpdate)
/// while pinning every actor of the FULL submitted list - deferred actors can't wander off - and the
/// call waits for the whole update (including any player-facing event triggered mid-execution) to
/// settle before returning one complete per-action report.
///
/// The list shape is the point: models naturally produce whole action plans, and the old
/// single-action schema made them stall trying to squeeze a list through it. All-or-nothing
/// validation - if ANY action is invalid or unresolvable, nothing executes and the error names each
/// offending entry, so the model can fix and resubmit rather than half-execute a plan.
///
/// Execution is real, not previewed: whole-run rollback is covered by the session-level
/// checkpoints alone (session_start/final - see LLMAgentSession/LLMCheckpointStore), so this tool
/// takes no per-call checkpoints of its own.
/// </summary>
public class Tool_ExecuteAP : ILLMTool
{
    public string Name => "execute_actions";

    class Arg
    {
        public string commandID;
        public int sourceJobID;
        public int doerRefID = -1;
        public int receiverRefID = -1;
        public int repeatCount = 1;
    }

    class Args
    {
        public List<Arg> actions = new List<Arg>();
    }

    class ActionResult
    {
        public string action;
        /// <summary>
        /// False when the action passed this tool's upfront Validate() but was no longer valid by
        /// the time ActionPackage_LLM actually reached it a few simulated minutes later (game state
        /// can change between registration and execution) - messages will be empty in that case.
        /// </summary>
        public bool executed;
        public List<string> messages = new List<string>();
    }

    class Result
    {
        public List<ActionResult> actions = new List<ActionResult>();
        /// <summary>Ambient messages (world activity, resolved prompts) that surfaced during execution.</summary>
        public List<string> messages = new List<string>();
        /// <summary>APs from this call that did NOT execute this round (conflict-deferred by the
        /// strict JoinAP batching) - resubmit them, adjusted if the results warrant it.</summary>
        public List<APJSON> remainingActions = new List<APJSON>();
        /// <summary>What to do next, per the agent preset's agent_nextStep_* strings.</summary>
        public string nextStep;
    }

    public LLMToolDefinition GetDefinition()
    {
        var itemSchema = new LLMFormatSchema.Type_Object(new Dictionary<string, LLMFormatSchema.Type>
        {
            { "commandID", new LLMFormatSchema.Type_Simple("string", "ID of the command/COM to execute (see get_room_detail's possibleCommands for valid values).") },
            { "sourceJobID", new LLMFormatSchema.Type_Simple("integer", "RefID of the job this command runs against (see get_room_detail's possibleCommands).") },
            { "doerRefID", new LLMFormatSchema.Type_Simple("integer", "RefID of the character performing the action, or -1 if none.") },
            { "receiverRefID", new LLMFormatSchema.Type_Simple("integer", "RefID of the character receiving the action, or -1 if none.") },
            { "repeatCount", new LLMFormatSchema.Type_Simple("integer", "How many times to repeat the action. Defaults to 1.") }
        }, "One action to execute.");
        var schema = new LLMFormatSchema();
        schema.properties["actions"] = new LLMFormatSchema.Type_Array(itemSchema,
            "The actions to execute, in intended execution order. All-or-nothing validation: if ANY action is invalid, nothing executes and the error names each offending entry.");
        return new LLMToolDefinition(Name, "Validate and actually execute a list of actions for real. If any action is invalid, returns why and executes nothing. Otherwise this round executes the first action plus every later action that can run together with it (conflicting ones are NOT executed - they come back as remainingActions to resubmit next round, so submit independent groups per call when you can). The executed actions really happen (visible in-game, not a preview) and this call waits until they - and any events they trigger - fully resolve before returning each action's results.", schema);
    }

    public IEnumerator Execute(LLMToolCallRequest call, Action<LLMToolResult> done)
    {
        Args args;
        try { args = JsonConvert.DeserializeObject<Args>(call.rawArgumentsJson); }
        catch { done?.Invoke(LLMToolResult.Error(call, "malformed arguments")); yield break; }

        if (args == null || args.actions == null || args.actions.Count < 1)
        {
            done?.Invoke(LLMToolResult.Error(call, "actions is required and must contain at least one action"));
            yield break;
        }

        var json = new MessageJSON();
        for (int i = 0; i < args.actions.Count; i++)
        {
            json.UpdateVariable.Add(new APJSON
            {
                CommandID = args.actions[i].commandID,
                SourceJobID = args.actions[i].sourceJobID,
                doer_RefID = args.actions[i].doerRefID,
                receiver_RefID = args.actions[i].receiverRefID,
                repeatCount = Math.Max(1, args.actions[i].repeatCount)
            });
        }

        var packages = json.GetActionPackages(out _);

        // All-or-nothing upfront validation, naming each offending entry so the model can fix and
        // resubmit rather than half-execute a plan. Two failure classes: structurally bad entries,
        // and entries GetActionPackages silently dropped (unknown CommandID/SourceJobID - it skips
        // those without erroring, so detect them by tracing which APJSONs made it into a package).
        var invalid = new List<string>();
        for (int i = 0; i < args.actions.Count; i++)
        {
            if (string.IsNullOrEmpty(args.actions[i].commandID)) invalid.Add($"actions[{i}]: commandID is required");
        }

        if (invalid.Count < 1)
        {
            var resolved = new HashSet<APJSON>();
            foreach (var ap in packages) foreach (var ep in ap.epjson) resolved.Add(ep);
            for (int i = 0; i < json.UpdateVariable.Count; i++)
            {
                if (resolved.Contains(json.UpdateVariable[i])) continue;
                invalid.Add($"actions[{i}] ({json.UpdateVariable[i].CommandID} / job {json.UpdateVariable[i].SourceJobID}): could not resolve an action - check commandID/sourceJobID/doerRefID/receiverRefID against get_room_detail's possibleCommands");
            }
        }

        if (invalid.Count < 1)
        {
            foreach (var ap in packages)
            {
                if (ap.Validate()) continue;
                ap.tooltip.RemoveAll(x => string.IsNullOrEmpty(x));
                invalid.Add(ap.GetTooltips(LocalizeDictionary.QueryThenParse("ui_ap_onHoverTooltip_comInvalid")).Replace("$tooltips$", String.Join("\n", ap.tooltip)));
            }
        }

        if (invalid.Count > 0)
        {
            done?.Invoke(LLMToolResult.Error(call, String.Join("\n", invalid)));
            yield break;
        }

        // Stepwise batching, identical to submit_response plan steps (shared
        // scr_System_CampaignManager.BuildAPBatch): this round executes only the anchor plus every
        // AP that JoinAP-merges into it; conflicting APs are deferred and returned as
        // remainingActions. Two talks against different jobs never merge - two rounds by design, so
        // the model sees round 1's real results before committing round 2. The full package list is
        // still passed to ExecuteLLMResponseBatch so deferred-action actors are pinned in place.
        var deferred = new List<APJSON>();
        var batchJson = scr_System_CampaignManager.BuildAPBatch(json, packages, out deferred);

        // One registration for the batch: wrapper + placeholders + actor pinning + capture
        // hooks (trackCapture lands on the same cached AP objects the wrapper's Execution re-derives,
        // so their capturedLogs are readable below), via the shared plan-batch path.
        scr_System_CampaignManager.current.ExecuteLLMResponseBatch(batchJson, packages);

        // Waits for the whole update - including any player-facing event the actions trigger
        // mid-execution (EventManager.Active gates on that too) - to fully settle, however long that
        // takes in real time, per the user's locked-in design: one complete tool result, not split
        // across rounds. ExecuteLLMResponseBatch's FreeUpdate sets Updating synchronously before
        // returning, so there's no race where this WaitUntil could see !Updating before the update
        // has actually begun.
        yield return new WaitUntil(() => !scr_UpdateHandler.current.Updating && !scr_UpdateHandler.current.EventHandler.Active);

        var result = new Result();
        foreach (var ap in batchJson.GetActionPackages(out _))
        {
            int count = 0;
            foreach (var ep in ap.epjson) count = Math.Max(count, ep.repeatCount);
            var entry = new ActionResult { action = $"{ap.DisplayName} x{count}", executed = ap.capturedLog != null };
            if (ap.capturedLog != null) entry.messages.AddRange(ap.capturedLog.DumpMessages());
            result.actions.Add(entry);
        }

        // ap.capturedLog only captures messages tied to a specific ActionPackage's own mcol -
        // append everything else that flowed through MessageLogManager.AddLog during the same
        // real-time window (ambient NPC/room activity, any resolved Question/InputField prompt+answer),
        // so the model sees the full picture alongside the actions' own results, then clear for the
        // next round.
        var session = scr_UpdateHandler.current.CurrentAgentSession;
        if (session != null)
        {
            var ambient = session.DrainInterceptedMessages();
            if (ambient.Count > 0) result.messages.AddRange(ambient);
        }

        // Conflict-deferred APs go back to the model verbatim (raw APJSONs, resubmittable as-is),
        // with the same nextStep guidance the submit_response plan-batch report uses.
        result.remainingActions = deferred;
        result.nextStep = deferred.Count > 0
            ? scr_UpdateHandler.AgentText("agent_nextStep_partial",
                "Partial execution complete - $count$ action(s) remain. Continue them with the execute_actions tool, then submit your final narrative via submit_response.")
                .Replace("$count$", deferred.Count.ToString())
            : scr_UpdateHandler.AgentText("agent_nextStep_final",
                "All submitted actions have been executed. Submit your final narrative response with NO actions (empty UpdateVariable) to conclude, written with the execution results above in mind.");

        done?.Invoke(new LLMToolResult
        {
            callId = call.callId,
            toolName = Name,
            contentJson = JsonConvert.SerializeObject(result, UtilityEX.SerializerSettingsLLM)
        });
    }
}
