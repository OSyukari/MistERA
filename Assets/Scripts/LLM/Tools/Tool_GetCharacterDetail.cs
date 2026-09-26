using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Agent-mode tool: fetch detail on a single character by RefID. Thin wrapper over
/// LLM_WorldState.CharaStorage's existing cheap-vs-full-detail constructor split (LLMUtils.cs) - the
/// same class already used to build the per-character entries in the full world-state dump.
/// </summary>
public class Tool_GetCharacterDetail : ILLMTool
{
    public string Name => "get_character_detail";

    class Args
    {
        public int characterRef = -1;
        public bool fullDetail = true;
    }

    public LLMToolDefinition GetDefinition()
    {
        var schema = new LLMFormatSchema();
        schema.properties["characterRef"] = new LLMFormatSchema.Type_Simple("integer", "RefID of the character to inspect. If this doesn't match any character directly, it's also tried as a job RefID (e.g. SourceJobID seen elsewhere in world info can look like a character RefID) and that job's owning character is returned instead/as well.");
        schema.properties["fullDetail"] = new LLMFormatSchema.Type_Simple("boolean", "If true, include relationships, memories, equipment, status effects, and the character card. If false, only name/description/current activity/location.");
        return new LLMToolDefinition(Name, "Fetch detailed information about a character by RefID. Returns every distinct character that matches, in case characterRef is ambiguous between a character RefID and a job RefID.", schema);
    }

    public IEnumerator Execute(LLMToolCallRequest call, Action<LLMToolResult> done)
    {
        Args args;
        try { args = JsonConvert.DeserializeObject<Args>(call.rawArgumentsJson) ?? new Args(); }
        catch { done?.Invoke(LLMToolResult.Error(call, "malformed arguments")); yield break; }

        var mgr = scr_System_CampaignManager.current;
        var matches = new List<LLM_WorldState.CharaStorage>();
        var seenRefs = new HashSet<int>();

        var byCharacterRef = mgr.FindInstanceByID(args.characterRef);
        if (byCharacterRef != null && seenRefs.Add(byCharacterRef.RefID))
        {
            matches.Add(new LLM_WorldState.CharaStorage(byCharacterRef, null, args.fullDetail));
        }

        // The model sometimes confuses a character's interaction-job RefID (e.g. the SourceJobID shown
        // next to a character's name in PossibleInteractions) with the character's own RefID - fall
        // back to resolving characterRef as a job RefID and returning that job's owning character too.
        var job = mgr.FindJobInstanceByID(args.characterRef, logMiss: false) as Job_CharaCOM;
        var byJobRef = job?.Owner;
        if (byJobRef != null && seenRefs.Add(byJobRef.RefID))
        {
            matches.Add(new LLM_WorldState.CharaStorage(byJobRef, null, args.fullDetail));
        }

        if (matches.Count == 0)
        {
            done?.Invoke(LLMToolResult.Error(call, $"no character with RefID {args.characterRef}, and no character's interaction job has RefID {args.characterRef} either"));
            yield break;
        }

        var json = JsonConvert.SerializeObject(matches, UtilityEX.SerializerSettingsLLM);
        done?.Invoke(new LLMToolResult { callId = call.callId, toolName = Name, contentJson = json });
    }
}
