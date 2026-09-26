using System;
using System.Collections;

/// <summary>
/// Common interface for agent-mode tool handlers (Assets/Scripts/LLM/Tools/*). Registered once via
/// LLMToolRegistry.Register (see LLMToolRegistry.RegisterDefaults); dispatched by the agent-mode
/// orchestration loop (Workstream C) whenever the model issues a matching tool call.
/// </summary>
public interface ILLMTool
{
    /// <summary>Wire tool name, e.g. "get_character_detail" - must match GetDefinition().name.</summary>
    string Name { get; }

    LLMToolDefinition GetDefinition();

    /// <summary>
    /// Executes the call and reports the result via `done`, exactly once. A coroutine rather than a
    /// plain method since some tools (Tool_ExecuteAP) may need to wait across many frames - info-only
    /// tools should just call `done` and `yield break` immediately.
    /// </summary>
    IEnumerator Execute(LLMToolCallRequest call, Action<LLMToolResult> done);
}
