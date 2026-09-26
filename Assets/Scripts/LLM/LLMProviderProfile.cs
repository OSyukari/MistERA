using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Networking;
using static LLMRequest;

/// <summary>
/// Describes one LLM provider's request/response quirks, deserialized from
/// Assets/LLM/Providers/*.json. Runtime code resolves a preset to its profile via
/// scr_System_CentralControl.ResolveProviderProfile instead of sniffing endpoint/model strings.
/// </summary>
public class LLMProviderProfile
{
    /// <summary>
    /// Unique provider id, declared in the profile's own JSON - not derived from filename. Also
    /// used to build its localized display name via LocalizeDictionary key "LLM_Provider_{id}".
    /// </summary>
    public string id = "";

    /// <summary>
    /// True only for the "custom" profile: shows the free-text endpoint/key fields in the preset
    /// creation UI instead of prefilling from defaultEndpoint/defaultModelListEndpoint.
    /// </summary>
    public bool isCustomEntry = false;

    public string defaultEndpoint = "";
    public string defaultModelListEndpoint = "";

    // ---- Auth ----
    public string authHeaderName = "Authorization";
    public string authHeaderPrefix = "Bearer ";
    public Dictionary<string, string> extraHeaders = null;

    // ---- Request shaping ----
    public string tokenLimitField = "max_completion_tokens"; // or "max_tokens"
    public int? tokenLimitDefault = null;
    public int? tokenLimitCap = null;
    public bool top_k = false; // true = this endpoint accepts top_k; false (default) = strip it
    public bool top_p = false; // true = this endpoint accepts top_p; false (default) = strip it
    public bool supportsReasoningEffort = false; // true = this endpoint accepts reasoning_effort; false (default) = strip it
    public string reasoningEffort = null; // fallback value applied when the request doesn't set one; ignored when supportsReasoningEffort is false
    public bool extractSystemMessage = false;
    public string responseFormatStrategy = "openai"; // "openai" | "claude" | "toolcall"
    public string toolChoiceMode = "none"; // "none" | "forced"
    public string nudgeMessage = null;

    // ---- Response parsing ----
    public string responseEnvelope = "openai"; // "openai" | "claude" - descriptive only
    public bool preferToolCallArguments = false;

    // ---- Agent mode ----
    /// <summary>
    /// True if this provider's endpoint should be sent real tool definitions during an agentic
    /// tool-calling turn (LLMRequest.agentTurn == true), shaped per responseEnvelope. For "openai"/
    /// "claude" native responseFormatStrategy providers the real tools are sent alongside native
    /// structured output. For "toolcall"-strategy providers (DeepSeek/ZAI) the real tools are sent
    /// alongside the existing single-synthetic-tool ("submit_response") structured-output workaround -
    /// see agentToolChoiceMode/agentNudgeMessage below and ApplyRequestShaping's "toolcall" branch.
    /// </summary>
    public bool supportsAgentTools = false;

    /// <summary>
    /// tool_choice value to send when agent mode is combined with the "toolcall" responseFormatStrategy
    /// (DeepSeek/ZAI) - "required" forces some tool call every round without naming one (preserving the
    /// reliability guarantee toolChoiceMode:"forced" already gives the single-tool case), "auto" allows
    /// plain-text replies too. Independent of toolChoiceMode, which only governs the pre-existing
    /// single-forced-tool workaround. Needs live verification per provider - see plan doc.
    /// </summary>
    public string agentToolChoiceMode = "required";

    /// <summary>
    /// Nudge appended on agent-mode + "toolcall"-strategy turns instead of nudgeMessage (which says
    /// "submit now," misleading on rounds where the model should call an info/action tool first). Null/
    /// empty skips the nudge - the submit_response tool's own description already explains its contract.
    /// </summary>
    public string agentNudgeMessage = null;

    // ---- Streaming ----
    /// <summary>
    /// True if this provider's endpoint supports SSE streaming (stream: true) in a way this client
    /// understands: OpenAI-compatible `data: {...}` chunks with choices[0].delta.content /
    /// choices[0].delta.reasoning_content (responseEnvelope == "openai"), or Claude's
    /// content_block_delta / thinking_delta / text_delta event stream (responseEnvelope == "claude").
    /// </summary>
    public bool supportsStreaming = false;

    /// <summary>
    /// OpenAI-envelope streaming only: send stream_options.include_usage so the endpoint appends a
    /// final usage chunk. Off by default - only enable for endpoints known to accept the parameter
    /// (an endpoint that rejects unknown fields would fail every request).
    /// </summary>
    public bool streamIncludeUsage = false;

    // ---- Custom-preset auto-detection fallback ----
    public List<string> endpointHints = null;
    public List<string> modelNameHints = null;

    // ---- Template node overrides (see LLMMessage.tag) ----
    public List<string> enableNodeByTag = null;
    public List<string> disableNodeByTag = null;
}

public static class LLMProviderUtils
{
    public static void ApplyAuthHeaders(UnityWebRequest request, LLMProviderProfile profile, string apiKey)
    {
        if (profile == null)
        {
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);
            return;
        }

        if (profile.extraHeaders != null)
        {
            foreach (var kvp in profile.extraHeaders) request.SetRequestHeader(kvp.Key, kvp.Value);
        }

        request.SetRequestHeader(profile.authHeaderName ?? "Authorization", (profile.authHeaderPrefix ?? "") + apiKey);
    }

    public static void ApplyRequestShaping(LLMRequest payload, LLMProviderProfile profile, List<LLMToolDefinition> agentToolDefs = null)
    {
        if (profile == null) return;

        payload.stream = profile.supportsStreaming;
        payload.stream_options = payload.stream && profile.streamIncludeUsage && profile.responseEnvelope != "claude"
            ? new LLMRequest.StreamOptions() : null;

        if (profile.extractSystemMessage)
        {
            var systemMessages = new List<string>();
            for (int i = payload.messages.Count - 1; i >= 0; i--)
            {
                if (payload.messages[i].role == "system")
                {
                    systemMessages.Add(payload.messages[i].content);
                    payload.messages.RemoveAt(i);
                }
            }
            systemMessages.Reverse();
            payload.system = string.Join("\n", systemMessages);
        }

        if (!profile.top_k) payload.top_k = null;
        if (!profile.top_p) payload.top_p = null;
        // Claude doesn't take a top-level reasoning_effort field at all - ApplyClaudeEffort (below)
        // relocates it into output_config.effort instead, so it stays here for that branch to consume
        // even though profile.responseEnvelope == "claude" also has supportsReasoningEffort == true.
        if (profile.supportsReasoningEffort)
        {
            if (payload.reasoningEffort == null) payload.reasoningEffort = profile.reasoningEffort;
        }
        else
        {
            payload.reasoningEffort = null;
        }

        if (profile.tokenLimitField == "max_tokens")
        {
            payload.max_completion_tokens = null;
            if (payload.max_tokens == null) payload.max_tokens = profile.tokenLimitDefault;
            else if (profile.tokenLimitCap.HasValue && payload.max_tokens > profile.tokenLimitCap) payload.max_tokens = profile.tokenLimitCap;
        }
        else
        {
            payload.max_tokens = null;
            if (payload.max_completion_tokens == null) payload.max_completion_tokens = profile.tokenLimitDefault;
        }

        // "int" is not a valid JSON-Schema type - every provider needs this fixed up regardless
        // of which response_format strategy it ends up using below.
        if (payload.response_format != null) payload.response_format.ReplaceType("int", "integer");

        bool realAgentToolsActive = payload.agentTurn && profile.supportsAgentTools
            && agentToolDefs != null && agentToolDefs.Count > 0;

        if (realAgentToolsActive)
        {
            if (profile.responseFormatStrategy == "toolcall")
            {
                // DeepSeek/ZAI: send the real registered tools plus the existing single-synthetic-tool
                // ("submit_response") structured-output workaround in the same tools list, so the model
                // can freely call either kind each round - LLMResponseToolParser treats a submit_response
                // call as the final answer instead of dispatching it through LLMToolRegistry.
                var tools = agentToolDefs.ConvertAll(t => (ResponseFormatter_Tools)t.ToOpenAIToolJson());
                if (payload.response_format?.json_schema?.schema != null)
                {
                    var submitTool = new ResponseFormatter_ZAI_tools();
                    submitTool.function.parameters = payload.response_format.json_schema.schema;
                    submitTool.function.parameters.Purge();
                    tools.Add(submitTool);
                    payload.response_format = null;
                }
                payload.tools = tools;
                payload.tool_choice = profile.agentToolChoiceMode;
            }
            else
            {
                // Real multi-tool agentic turn: send the actual registered tools alongside the
                // provider's native structured-output mechanism.
                bool claude = profile.responseEnvelope == "claude";
                if (claude)
                {
                    payload.tools = agentToolDefs.ConvertAll(t => (ResponseFormatter_Tools)t.ToClaudeToolJson());
                    payload.tool_choice = new ResponseFormatter_ClaudeToolChoice();

                    // Claude's own structured-output mechanism (output_config) is independent of tool
                    // calling and can coexist with a real tools list, so the model can still answer
                    // directly, schema-validated, once it's done calling tools.
                    if (profile.responseFormatStrategy == "claude" && payload.response_format != null)
                    {
                        payload.output_config = new ResponseFormatter_Claude(payload.response_format);
                        payload.response_format = null;
                    }
                    ApplyClaudeEffort(payload);
                }
                else
                {
                    payload.tools = agentToolDefs.ConvertAll(t => (ResponseFormatter_Tools)t.ToOpenAIToolJson());
                    payload.tool_choice = "auto";
                    // response_format (OpenAI json_schema) is left attached as-is so the model can still
                    // answer directly, schema-validated, once it's done calling tools.
                }
            }
        }
        else
        {
            switch (profile.responseFormatStrategy)
            {
                case "claude":
                    if (payload.response_format != null)
                    {
                        payload.output_config = new ResponseFormatter_Claude(payload.response_format);
                        payload.response_format = null;
                    }
                    ApplyClaudeEffort(payload);
                    break;

                case "toolcall":
                    if (payload.response_format?.json_schema?.schema != null)
                    {
                        var formatter = new ResponseFormatter_ZAI_tools();
                        formatter.function.parameters = payload.response_format.json_schema.schema;
                        formatter.function.parameters.Purge();
                        payload.tools = new List<ResponseFormatter_Tools> { formatter };
                        payload.response_format = null;
                        if (profile.toolChoiceMode == "forced") payload.tool_choice = new ResponseFormatter_ZAI_toolchoice();
                    }
                    break;

                default:
                    break; // "openai": plain passthrough
            }
        }

        bool usedToolcallAgentBranch = realAgentToolsActive && profile.responseFormatStrategy == "toolcall";
        var effectiveNudge = usedToolcallAgentBranch ? profile.agentNudgeMessage : profile.nudgeMessage;
        if (!string.IsNullOrEmpty(effectiveNudge))
        {
            payload.messages.Add(new LLMMessage_Final { role = "user", content = effectiveNudge });
        }
    }

    /// <summary>
    /// Claude has no top-level reasoning_effort field - its equivalent lives at output_config.effort
    /// (see platform.claude.com/docs/en/build-with-claude/effort). Moves LLMRequest.reasoningEffort
    /// there instead of letting it serialize verbatim, creating output_config if a structured-output
    /// schema hasn't already claimed it.
    /// </summary>
    static void ApplyClaudeEffort(LLMRequest payload)
    {
        if (string.IsNullOrEmpty(payload.reasoningEffort)) return;
        if (payload.output_config == null) payload.output_config = new ResponseFormatter_Claude();
        payload.output_config.effort = payload.reasoningEffort;
        payload.reasoningEffort = null;
    }

    /// <summary>
    /// Single implementation of prefer-tool-call-arguments -> deserialize -> strip-code-fence-and-retry
    /// -> content_blocks-vs-content_string fallback, shared by LLMMessage.GetContent() (OpenAI-shaped
    /// choices[].message envelope) and LLMResponse.choice_claude.GetContent() (Claude's content[].text
    /// envelope, which passes toolCalls/profile as null since that shape never carries tool_calls).
    /// </summary>
    public static MessageJSON UnwrapMessageJSON(string content, List<LLMMessage.ToolCall> toolCalls, LLMProviderProfile profile)
    {
        if (profile != null && profile.preferToolCallArguments && toolCalls != null && toolCalls.Count > 0)
        {
            var submitCall = toolCalls.Find(t => t.function != null && t.function.name == "submit_response");
            if (submitCall?.function != null && !string.IsNullOrEmpty(submitCall.function.arguments))
            {
                content = submitCall.function.arguments;
            }
        }

        MessageJSON json;
        try
        {
            json = JsonConvert.DeserializeObject<MessageJSON>(content);
        }
        catch
        {
            try
            {
                json = JsonConvert.DeserializeObject<MessageJSON>(LLMMessage.StripCodeFence(content));
            }
            catch
            {
                UnityEngine.Debug.LogError($"error failed to deserialize MessageJSON object from [{content}]");
                json = new MessageJSON();
                json.content_string = content ?? "null";
                return json;
            }
        }

        try
        {
            var blocks = JsonConvert.DeserializeObject<LLMMessage.MessageJSON_blocks>(content);
            json.content_blocks = blocks.content;
        }
        catch
        {
            try
            {
                var simple = JsonConvert.DeserializeObject<LLMMessage.MessageJSON_simple>(content);
                json.content_string = simple.content;
            }
            catch
            {
                UnityEngine.Debug.LogError($"error failed to deserialize MessageJSON object from [{content}]");
                json.content_string = content ?? "null";
            }
        }

        return json;
    }
}
