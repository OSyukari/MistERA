using Newtonsoft.Json;
using System.Collections.Generic;

/// <summary>
/// Extracts normalized tool calls from a parsed LLMResponse, forking on the resolved provider's
/// responseEnvelope. Used by the agent-mode orchestration loop (Workstream C) to decide whether a
/// round produced a final answer or more tool calls to dispatch.
/// </summary>
public static class LLMResponseToolParser
{
    /// <summary>
    /// Returns true if any tool calls were found. isFinalAnswer is true whenever none were found. On a
    /// "toolcall"-strategy profile (DeepSeek/ZAI agent mode - see LLMProviderProfile.ApplyRequestShaping's
    /// "toolcall" branch), a "submit_response" call is excluded from calls since it's the structured
    /// final answer, not a real tool dispatch - but only when it's the *sole* call in the response. If
    /// the model bundles submit_response together with real tool calls in the same round (some models do
    /// this to save a round trip), isFinalAnswer stays false so those real calls - which may include
    /// Tool_ExecuteAP actually executing something - still get dispatched instead of silently discarded;
    /// the bundled submit_response content is dropped, since it was necessarily written without seeing
    /// this round's tool results and the model can resubmit once it has them. Callers should then fall
    /// back to the existing LLMResponse.JSON/Reasoning accessors unchanged.
    ///
    /// submitResponseCall is the excluded submit_response call itself (null if there wasn't one - e.g.
    /// a native openai/claude structured-output final answer never has one), so a caller that finds the
    /// final answer's action package(s) invalid can still reply to that specific tool_call_id with the
    /// validation failure instead of silently discarding the whole answer.
    /// </summary>
    public static bool TryExtractToolCalls(LLMResponse response, LLMProviderProfile profile, out List<LLMToolCallRequest> calls, out bool isFinalAnswer, out LLMToolCallRequest submitResponseCall)
    {
        calls = new List<LLMToolCallRequest>();
        submitResponseCall = null;
        if (response == null)
        {
            isFinalAnswer = true;
            return false;
        }

        bool isSubmitResponseStrategy = profile != null && profile.responseFormatStrategy == "toolcall";
        bool claude = profile != null && profile.responseEnvelope == "claude";
        if (claude)
        {
            foreach (var block in response.content)
            {
                if (block.type != "tool_use") continue;
                if (isSubmitResponseStrategy && block.name == "submit_response")
                {
                    submitResponseCall = new LLMToolCallRequest { callId = block.id, toolName = block.name, rawArgumentsJson = block.input != null ? block.input.ToString(Formatting.None) : "{}" };
                    continue;
                }
                calls.Add(new LLMToolCallRequest
                {
                    callId = block.id,
                    toolName = block.name,
                    rawArgumentsJson = block.input != null ? block.input.ToString(Formatting.None) : "{}"
                });
            }
        }
        else
        {
            var message = response.choices.Count > 0 ? response.choices[0].message : null;
            if (message?.tool_calls != null)
            {
                foreach (var tc in message.tool_calls)
                {
                    if (tc.function == null) continue;
                    if (isSubmitResponseStrategy && tc.function.name == "submit_response")
                    {
                        submitResponseCall = new LLMToolCallRequest { callId = tc.id, toolName = tc.function.name, rawArgumentsJson = tc.function.arguments ?? "{}" };
                        continue;
                    }
                    calls.Add(new LLMToolCallRequest
                    {
                        callId = tc.id,
                        toolName = tc.function.name,
                        rawArgumentsJson = tc.function.arguments ?? "{}"
                    });
                }
            }
        }

        if (submitResponseCall != null && calls.Count > 0)
        {
            UnityEngine.Debug.LogWarning($"LLMResponseToolParser: model bundled submit_response with {calls.Count} real tool call(s) in one round - dropping the premature submit_response and dispatching the real call(s); expect it to resubmit next round once it has their results.");
        }

        isFinalAnswer = calls.Count == 0;
        return calls.Count > 0;
    }
}
