using System.Collections;
using System.Collections.Generic;
using System;

/// <summary>
/// Registry of agent-mode tool handlers. Consulted both to build the tool-definition list sent to
/// the model (GetAllDefinitions, feeds LLMProviderUtils.ApplyRequestShaping's agentToolDefs
/// parameter) and to dispatch an incoming tool call to its handler (Dispatch, called from the
/// agent-mode orchestration loop - Workstream C).
/// </summary>
public static class LLMToolRegistry
{
    static readonly Dictionary<string, ILLMTool> tools = new Dictionary<string, ILLMTool>();

    public static void Register(ILLMTool tool)
    {
        if (tool == null || string.IsNullOrEmpty(tool.Name)) return;
        tools[tool.Name] = tool;
    }

    /// <summary>
    /// Registers every tool built so far. Idempotent (re-registering just overwrites the same keys).
    /// </summary>
    public static void RegisterDefaults()
    {
        Register(new Tool_GetCharacterDetail());
        Register(new Tool_GetRoomDetail());
        Register(new Tool_GetFactionMap());
        Register(new Tool_GetQuestProgress());
        Register(new Tool_ExecuteAP());
    }

    public static List<LLMToolDefinition> GetAllDefinitions()
    {
        var defs = new List<LLMToolDefinition>();
        foreach (var tool in tools.Values) defs.Add(tool.GetDefinition());
        return defs;
    }

    /// <summary>
    /// Dispatches a parsed tool call to its registered handler. An unknown tool name produces an
    /// isError result (so the model can self-correct on the next round) rather than throwing.
    /// </summary>
    public static IEnumerator Dispatch(LLMToolCallRequest call, Action<LLMToolResult> done)
    {
        if (call == null || string.IsNullOrEmpty(call.toolName) || !tools.TryGetValue(call.toolName, out var tool))
        {
            done?.Invoke(LLMToolResult.Error(call, $"unknown tool '{call?.toolName}'"));
            yield break;
        }

        yield return tool.Execute(call, done);
    }
}
