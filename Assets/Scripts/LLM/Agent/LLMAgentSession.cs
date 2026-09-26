using System;
using System.Collections.Generic;

/// <summary>
/// One turn in an agent-mode conversation. See LLMAgentSession.BuildMessages for how each kind gets
/// flattened into wire-shape LLMMessage_Final entries, per envelope.
/// </summary>
public class LLMAgentTurn
{
    public enum Kind { User, Assistant, ToolResult }
    public Kind kind;

    /// <summary>User kind: the player's/orchestrator's input text.</summary>
    public string userText;

    /// <summary>
    /// Assistant kind, OpenAI envelope: the model's plain-text content for this round (may be null if
    /// the round was tool-calls-only) and the tool_calls it made, echoed back verbatim on the next
    /// round per OpenAI's continuation contract.
    /// </summary>
    public string assistantContent;
    public List<LLMMessage.ToolCall> assistantToolCalls;

    /// <summary>
    /// Assistant kind, Claude envelope: the model's raw content[] blocks (text/thinking/tool_use),
    /// echoed back verbatim on the next round per Claude's continuation contract.
    /// </summary>
    public List<LLMClaudeContentBlock> assistantContentBlocks;

    /// <summary>ToolResult kind: the result(s) of whatever tool call(s) the prior Assistant turn made.</summary>
    public List<LLMToolResult> toolResults;

    /// <summary>Hook for a future per-turn trace UI - not consumed by anything yet.</summary>
    public string GetLLMResponseForDisplay()
    {
        return kind == Kind.Assistant ? (assistantContent ?? "") : "";
    }
}

/// <summary>
/// Holds one agent-mode conversation: the resolved base template (system/world-info messages, built
/// once per session - see scr_UpdateHandler.SendLLMRequest(string,bool) for the equivalent one-shot
/// build this mirrors) plus every turn appended since. Every round resends the full transcript
/// (BuildMessages/BuildRequest) - no compaction/caching in this groundwork, see the architecture doc's
/// Out of Scope section.
/// </summary>
/// <summary>
/// Token usage and model time summed over one or more LLM requests - a whole agent session
/// (LLMAgentSession.usageStats) or a single single-shot response. Only requests whose provider
/// actually reported usage contribute tokens; cache figures only from those reporting a cache count.
/// </summary>
public class LLMUsageStats
{
    public int requests;
    public int usageReported;
    public long input;
    public long output;
    public int cacheReported;
    public long cachedInput;
    /// <summary>Prompt tokens of the requests that did report a cache figure - the hit-rate base.</summary>
    public long cacheBaseInput;
    public double seconds;

    public void Add(LLMResponse response)
    {
        if (response == null) return;
        requests++;
        seconds += response.elapsedSeconds;
        var u = response.usage;
        if (u == null || !u.HasData) return;
        usageReported++;
        input += u.Input;
        output += u.Output;
        if (u.Cached.HasValue)
        {
            cacheReported++;
            cachedInput += u.Cached.Value;
            cacheBaseInput += u.Input;
        }
    }

    public static LLMUsageStats From(LLMResponse response)
    {
        var stats = new LLMUsageStats();
        stats.Add(response);
        return stats;
    }
}

/// <summary>
/// One executed (or refused/invalid/aborted) inner AP of an agent run - see LLMAgentSession.executedLog.
/// outcome uses AP_Status: success, refused, none (never passed Validate), aborted.
/// </summary>
public class AgentExecutedRecord
{
    public AP_Status outcome;
    public string line;
}

public class LLMAgentSession
{
    public string sessionId;
    public LLMRequest baseTemplate;
    // Stepwise execution (see scr_UpdateHandler.AgentLoop_Routine): each plan batch costs a round
    // (submit + batch report), plus the concluding no-action narrative - budgeted for multi-action
    // plans instead of the old all-at-once single-round execution.
    public int maxRounds = 30;

    /// <summary>The raw user input this session was started with - kept so "Regenerate" (scr_panel_LLM)
    /// can silently reload startCheckpointPath and restart the whole run with the same input.</summary>
    public string originalUserInput;

    /// <summary>Path written by LLMCheckpointStore.WriteCheckpoint right before round 1 - the state to
    /// roll back to on a whole-run "Regenerate", undoing everything Tool_ExecuteAP did for real.</summary>
    public string startCheckpointPath;

    /// <summary>Path written once this session's terminal round has actually settled (real execution -
    /// including any mid-execution event - fully resolved, or the round loop capped out) - the state
    /// this attempt leaves behind. scr_panel_logs reloads this silently whenever the player switches
    /// back to reviewing this session, so the live game state always matches whatever is on screen.</summary>
    public string finalCheckpointPath;

    /// <summary>Latest 1-based round and phase reported by AgentLoop_Routine (ReportAgentProgress) -
    /// what the panel's status line shows once the run is over.</summary>
    public int lastRound = 0;
    public AgentPhase lastPhase = AgentPhase.Requesting;

    /// <summary>One line per inner AP outcome across every batch of this run, recorded by
    /// ActionPackage_LLM as it settles - kept as display strings (names/time resolved on the spot) so
    /// they stay valid after checkpoint reloads.</summary>
    public List<AgentExecutedRecord> executedLog = new List<AgentExecutedRecord>();

    /// <summary>Token usage and model time over every request of this run (AgentLoop_Routine).</summary>
    public LLMUsageStats usageStats = new LLMUsageStats();

    List<LLMAgentTurn> turns = new List<LLMAgentTurn>();

    public IReadOnlyList<LLMAgentTurn> Turns => turns;

    /// <summary>
    /// Messages suppressed from the player's normal log during this run (ambient world lines, action
    /// results, and resolved interactive prompts) - drained into the next Tool_ExecuteAP result so they
    /// ride alongside that action's own captured messages as additional context for the model.
    /// </summary>
    readonly List<(DateTime time, string text)> intercepted = new List<(DateTime, string)>();

    public void AppendInterceptedMessage(DateTime time, string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        intercepted.Add((time, text));
    }

    /// <summary>Returns the buffered intercepted messages formatted for the tool result, then clears them.</summary>
    public List<string> DrainInterceptedMessages()
    {
        var outp = new List<string>(intercepted.Count);
        foreach (var m in intercepted) outp.Add($"[{m.time:HH:mm}] {m.text}");
        intercepted.Clear();
        return outp;
    }

    public LLMAgentSession(string sessionId, LLMRequest baseTemplate)
    {
        this.sessionId = sessionId;
        this.baseTemplate = baseTemplate;
    }

    public void AppendUserTurn(string text)
    {
        turns.Add(new LLMAgentTurn { kind = LLMAgentTurn.Kind.User, userText = text });
    }

    /// <summary>
    /// Records the model's raw response as the next turn, in whichever shape its envelope produced,
    /// so it can be echoed back verbatim (required by both providers' tool-calling contracts) when
    /// BuildMessages runs on the next round.
    /// </summary>
    public void AppendAssistantTurn(LLMResponse raw, LLMProviderProfile profile)
    {
        var turn = new LLMAgentTurn { kind = LLMAgentTurn.Kind.Assistant };
        if (IsClaude(profile))
        {
            turn.assistantContentBlocks = new List<LLMClaudeContentBlock>();
            foreach (var block in raw.content)
            {
                turn.assistantContentBlocks.Add(new LLMClaudeContentBlock
                {
                    type = block.type,
                    text = block.type == "text" ? block.text : null,
                    id = block.type == "tool_use" ? block.id : null,
                    name = block.type == "tool_use" ? block.name : null,
                    input = block.type == "tool_use" ? block.input : null
                });
            }
        }
        else
        {
            var message = raw.choices.Count > 0 ? raw.choices[0].message : null;
            turn.assistantContent = message?.content;
            turn.assistantToolCalls = message?.tool_calls;
        }
        turns.Add(turn);
    }

    public void AppendToolResults(List<LLMToolResult> results)
    {
        turns.Add(new LLMAgentTurn { kind = LLMAgentTurn.Kind.ToolResult, toolResults = results });
    }

    static bool IsClaude(LLMProviderProfile profile)
    {
        return profile != null && profile.responseEnvelope == "claude";
    }

    /// <summary>
    /// Flattens baseTemplate's messages plus every turn into the full resendable transcript, shaping
    /// assistant/tool-result turns per the resolved provider's envelope (OpenAI: one {role:"tool",
    /// tool_call_id,content} message per result; Claude: one {role:"user",content:[tool_result,...]}
    /// message per ToolResult turn, since Claude expects parallel tool results grouped together).
    /// </summary>
    public List<LLMMessage_Final> BuildMessages(LLMProviderProfile profile)
    {
        bool claude = IsClaude(profile);
        var messages = new List<LLMMessage_Final>(baseTemplate.messages);

        foreach (var turn in turns)
        {
            switch (turn.kind)
            {
                case LLMAgentTurn.Kind.User:
                    messages.Add(new LLMMessage_Final { role = "user", content = turn.userText });
                    break;

                case LLMAgentTurn.Kind.Assistant:
                    messages.Add(claude
                        ? new LLMMessage_Final_Claude { role = "assistant", contentBlocks = turn.assistantContentBlocks }
                        : new LLMMessage_Final { role = "assistant", content = turn.assistantContent, tool_calls = turn.assistantToolCalls });
                    break;

                case LLMAgentTurn.Kind.ToolResult:
                    if (claude)
                    {
                        var blocks = new List<LLMClaudeContentBlock>();
                        foreach (var r in turn.toolResults)
                        {
                            blocks.Add(new LLMClaudeContentBlock
                            {
                                type = "tool_result",
                                tool_use_id = r.callId,
                                content = r.contentJson,
                                is_error = r.isError ? (bool?)true : null
                            });
                        }
                        messages.Add(new LLMMessage_Final_Claude { role = "user", contentBlocks = blocks });
                    }
                    else
                    {
                        foreach (var r in turn.toolResults)
                        {
                            messages.Add(new LLMMessage_Final { role = "tool", tool_call_id = r.callId, content = r.contentJson });
                        }
                    }
                    break;
            }
        }

        return messages;
    }

    /// <summary>
    /// Convenience wrapper for the orchestrator (Workstream C): clones baseTemplate's generation
    /// params onto a fresh LLMRequest with agentTurn=true and this round's full built transcript.
    /// </summary>
    public LLMRequest BuildRequest(LLMProviderProfile profile) => BuildRequest(profile, BuildMessages(profile));

    /// <summary>
    /// Same as BuildRequest(profile), but takes an already-built message list - lets a caller that also
    /// needs the message count up front (e.g. to check it against a cap) avoid calling BuildMessages twice.
    /// </summary>
    public LLMRequest BuildRequest(LLMProviderProfile profile, List<LLMMessage_Final> messages)
    {
        return new LLMRequest
        {
            model = baseTemplate.model,
            temperature = baseTemplate.temperature,
            max_tokens = baseTemplate.max_tokens,
            max_completion_tokens = baseTemplate.max_completion_tokens,
            top_p = baseTemplate.top_p,
            top_k = baseTemplate.top_k,
            response_format = baseTemplate.response_format,
            agentTurn = true,
            messages = messages
        };
    }
}
