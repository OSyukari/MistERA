using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Agent-mode tool: fetch current progress (which stages/sub-stages are currently valid) for a quest
/// by ID. Thin wrapper over the existing, stateless QuestUtility.Evaluate (Assets/Scripts/Missions/
/// QuestUtility.cs) - quests are never persisted, every evaluation is recomputed live from current
/// game state, same as the in-game mission-log UI (scr_canvas_missions.cs) already does.
/// </summary>
public class Tool_GetQuestProgress : ILLMTool
{
    public string Name => "get_quest_progress";

    class Args
    {
        public string questID = null;
    }

    class StageResultDTO
    {
        public string id;
        public bool isValid;
        public List<StageResultDTO> subStages = new List<StageResultDTO>();
    }

    class Result
    {
        public string questID;
        public bool visible;
        public List<StageResultDTO> stages = new List<StageResultDTO>();
    }

    public LLMToolDefinition GetDefinition()
    {
        var schema = new LLMFormatSchema();
        schema.properties["questID"] = new LLMFormatSchema.Type_Simple("string", "ID of the quest to check progress for.");
        return new LLMToolDefinition(Name, "Fetch the current progress (which stages/sub-stages are currently valid) for a quest by its ID.", schema);
    }

    public IEnumerator Execute(LLMToolCallRequest call, Action<LLMToolResult> done)
    {
        Args args;
        try { args = JsonConvert.DeserializeObject<Args>(call.rawArgumentsJson) ?? new Args(); }
        catch { done?.Invoke(LLMToolResult.Error(call, "malformed arguments")); yield break; }

        if (string.IsNullOrEmpty(args.questID))
        {
            done?.Invoke(LLMToolResult.Error(call, "questID is required"));
            yield break;
        }

        var quest = Masterlist_Event.Instance.Events.GetQuestByID(args.questID);
        if (quest == null)
        {
            done?.Invoke(LLMToolResult.Error(call, $"no quest with ID '{args.questID}'"));
            yield break;
        }

        var evaluation = QuestUtility.Evaluate(quest);
        var result = new Result { questID = args.questID, visible = evaluation != null };
        if (evaluation != null) result.stages = ConvertStages(evaluation.stages);

        var json = JsonConvert.SerializeObject(result, UtilityEX.SerializerSettingsLLM);
        done?.Invoke(new LLMToolResult { callId = call.callId, toolName = Name, contentJson = json });
    }

    static List<StageResultDTO> ConvertStages(List<QuestStageResult> stages)
    {
        var list = new List<StageResultDTO>();
        foreach (var s in stages)
        {
            list.Add(new StageResultDTO { id = s.ID, isValid = s.isValid, subStages = ConvertStages(s.subStages) });
        }
        return list;
    }
}
