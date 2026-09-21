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
    public bool extractSystemMessage = false;
    public string responseFormatStrategy = "openai"; // "openai" | "claude" | "toolcall"
    public string toolChoiceMode = "none"; // "none" | "forced"
    public string nudgeMessage = null;

    // ---- Response parsing ----
    public string responseEnvelope = "openai"; // "openai" | "claude" - descriptive only
    public bool preferToolCallArguments = false;

    // ---- Streaming ----
    /// <summary>
    /// True if this provider's endpoint supports SSE streaming (stream: true) in a way this client
    /// understands: OpenAI-compatible `data: {...}` chunks with choices[0].delta.content /
    /// choices[0].delta.reasoning_content (responseEnvelope == "openai"), or Claude's
    /// content_block_delta / thinking_delta / text_delta event stream (responseEnvelope == "claude").
    /// </summary>
    public bool supportsStreaming = false;

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

    public static void ApplyRequestShaping(LLMRequest payload, LLMProviderProfile profile)
    {
        if (profile == null) return;

        payload.stream = profile.supportsStreaming;

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

        switch (profile.responseFormatStrategy)
        {
            case "claude":
                if (payload.response_format != null)
                {
                    payload.output_config = new ResponseFormatter_Claude(payload.response_format);
                    payload.response_format = null;
                }
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

        if (!string.IsNullOrEmpty(profile.nudgeMessage))
        {
            payload.messages.Add(new LLMMessage_Final { role = "user", content = profile.nudgeMessage });
        }
    }

    /// <summary>
    /// Single implementation of prefer-tool-call-arguments -> deserialize -> strip-code-fence-and-retry
    /// -> content_blocks-vs-content_string fallback, shared by LLMMessage.GetContent() (OpenAI-shaped
    /// choices[].message envelope) and LLMResponse.choice_claude.GetContent() (Claude's content[].text
    /// envelope, which passes toolCalls/profile as null since that shape never carries tool_calls).
    /// </summary>
    public static MessageJSON UnwrapMessageJSON(string content, List<LLMMessage.ToolCall> toolCalls, LLMProviderProfile profile)
    {
        if (profile != null && profile.preferToolCallArguments
            && toolCalls != null && toolCalls.Count > 0 && toolCalls[0].function != null
            && !string.IsNullOrEmpty(toolCalls[0].function.arguments))
        {
            content = toolCalls[0].function.arguments;
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
