using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Agent-mode tool: fetch the possible interactions for ANY combination of actors - doers,
/// receivers, and an optional master - via LLM_WorldState.PossibleInteractions
/// (LLM/Worldstate/PossibleInteractions.cs) filled by LLMUtils.CollectCOMInfo(info). This is the
/// on-demand source of the commandID/sourceJobID/doerRefID/receiverRefID (and masterRef) values
/// that execute_actions expects. Room co-location is enforced inside CollectCOMInfo: when not
/// all actors share one room, the result states no valid interactions and lists where each
/// actor currently is.
/// </summary>
public class Tool_GetPossibleInteractions : ILLMTool
{
    public string Name => "get_possible_interactions";

    class Args
    {
        public List<int> doerRefIDs = new List<int>();
        public List<int> receiverRefIDs = new List<int>();
        public int masterRefID = -1;
    }

    public LLMToolDefinition GetDefinition()
    {
        var schema = new LLMFormatSchema();
        schema.properties["doerRefIDs"] = new LLMFormatSchema.Type_Array(new LLMFormatSchema.Type_Simple("integer", "RefID of a character performing the actions."), "The character(s) who would perform the actions. At least one is required. Note: individual interaction commands are only computed when exactly ONE doer and exactly one receiver are given; the player's special commands are computed whenever the player is the ONLY doer (with or without receivers).");
        schema.properties["receiverRefIDs"] = new LLMFormatSchema.Type_Array(new LLMFormatSchema.Type_Simple("integer", "RefID of a character receiving the actions."), "The character(s) the actions target, if any. Omit for actions the doer performs alone (e.g. furniture interactions).");
        schema.properties["masterRefID"] = new LLMFormatSchema.Type_Simple("integer", "RefID of the character the doer takes orders from. ONLY set this when the doer would NOT perform the action willingly - i.e. the action is ordered/coerced by the receiver or by a third party. Default -1 means the doer acts of their own will. Never point this at the doer themself.");
        return new LLMToolDefinition(Name, "Fetch the possible interactions (available commands, with acceptance rates) for any combination of actors: doer(s), optional receiver(s), and an optional ordering master. All actors must be in the same room, otherwise the result reports no valid interactions and where each actor currently is. This is the source of the commandID/sourceJobID/doerRefID/receiverRefID/masterRefID values that execute_actions expects.", schema);
    }

    public IEnumerator Execute(LLMToolCallRequest call, Action<LLMToolResult> done)
    {
        Args args;
        try { args = JsonConvert.DeserializeObject<Args>(call.rawArgumentsJson) ?? new Args(); }
        catch { done?.Invoke(LLMToolResult.Error(call, "malformed arguments")); yield break; }

        if (args.doerRefIDs == null || args.doerRefIDs.Count < 1)
        {
            done?.Invoke(LLMToolResult.Error(call, "doerRefIDs is required and must contain at least one character RefID"));
            yield break;
        }

        var mgr = scr_System_CampaignManager.current;

        var doers = new List<Character_Trainable>();
        var missing = new List<string>();
        foreach (var r in args.doerRefIDs)
        {
            var c = mgr.FindInstanceByID(r);
            if (c == null) missing.Add($"doer RefID {r}"); else doers.Add(c);
        }

        var receivers = new List<Character_Trainable>();
        if (args.receiverRefIDs != null)
        {
            foreach (var r in args.receiverRefIDs)
            {
                var c = mgr.FindInstanceByID(r);
                if (c == null) missing.Add($"receiver RefID {r}"); else receivers.Add(c);
            }
        }

        Character_Trainable master = null;
        if (args.masterRefID >= 0)
        {
            master = mgr.FindInstanceByID(args.masterRefID);
            if (master == null) missing.Add($"master RefID {args.masterRefID}");
        }

        if (missing.Count > 0)
        {
            done?.Invoke(LLMToolResult.Error(call, $"no character found for: {String.Join(", ", missing)}"));
            yield break;
        }

        var info = new LLM_WorldState.PossibleInteractions(doers, receivers, master);
        LLMUtils.CollectCOMInfo(info);

        var json = JsonConvert.SerializeObject(info, UtilityEX.SerializerSettingsLLM);
        done?.Invoke(new LLMToolResult { callId = call.callId, toolName = Name, contentJson = json });
    }
}
