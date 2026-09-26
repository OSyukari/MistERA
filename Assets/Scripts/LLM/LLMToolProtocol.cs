using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static LLMRequest;

/// <summary>
/// One block inside a Claude message's `content` array (used only for agent-mode tool-use/tool-result
/// continuation messages - see LLMMessage_Final_Claude.contentBlocks). Only the fields relevant to a given
/// block `type` are populated: type=="text" carries `text`, type=="tool_use" carries `id`/`name`/
/// `input` (echoing the assistant's own prior call back into the transcript), type=="tool_result"
/// carries `tool_use_id`/`content`/`is_error`.
/// </summary>
public class LLMClaudeContentBlock
{
    public string type;

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string text = null;

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string id = null;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string name = null;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public JToken input = null;

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string tool_use_id = null;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string content = null;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public bool? is_error = null;
}

/// <summary>
/// Envelope-agnostic tool descriptor produced by a tool registry entry (Assets/Scripts/LLM/Tools/).
/// Converted to whichever wire shape the resolved provider's envelope expects via
/// ToOpenAIToolJson()/ToClaudeToolJson(). Distinct from the existing single-synthetic-tool
/// "submit_response" structured-output workaround (LLMRequest.ResponseFormatter_ZAI_tools), but not
/// mutually exclusive with it on the wire: for "toolcall"-strategy providers (DeepSeek/ZAI), agent
/// mode sends a list of these converted tool defs plus one ResponseFormatter_ZAI_tools entry for
/// submit_response in the same `tools` array (see LLMProviderProfile.ApplyRequestShaping).
/// </summary>
public class LLMToolDefinition
{
    public string name;
    public string description;
    public LLMFormatSchema parameters;

    public LLMToolDefinition() { }
    public LLMToolDefinition(string name, string description, LLMFormatSchema parameters)
    {
        this.name = name;
        this.description = description;
        this.parameters = parameters;
    }

    public ResponseFormatter_AgentTool ToOpenAIToolJson()
    {
        parameters?.Purge();
        var entry = new ResponseFormatter_AgentTool();
        entry.function.name = name;
        entry.function.description = description;
        entry.function.parameters = parameters;
        return entry;
    }

    public ResponseFormatter_ClaudeTool ToClaudeToolJson()
    {
        parameters?.Purge();
        return new ResponseFormatter_ClaudeTool { name = name, description = description, input_schema = parameters };
    }
}

/// <summary>
/// Normalized, envelope-independent parsed tool call - see LLMResponseToolParser.TryExtractToolCalls.
/// </summary>
public class LLMToolCallRequest
{
    public string callId;
    public string toolName;
    public string rawArgumentsJson;
}

/// <summary>
/// Normalized tool result to append back into the conversation as the next turn - see
/// LLMAgentSession (Workstream B).
/// </summary>
public class LLMToolResult
{
    public string callId;
    public string toolName;
    public string contentJson;
    public bool isError = false;

    /// <summary>
    /// Shared error-result builder for tool handlers (Assets/Scripts/LLM/Tools/*) and
    /// LLMToolRegistry.Dispatch's unknown-tool-name fallback - JSON-encodes the message properly
    /// (rather than hand-building a JSON string) so a message containing quotes can't produce
    /// malformed contentJson.
    /// </summary>
    public static LLMToolResult Error(LLMToolCallRequest call, string message)
    {
        return new LLMToolResult
        {
            callId = call?.callId,
            toolName = call?.toolName,
            isError = true,
            contentJson = JsonConvert.SerializeObject(new { error = message })
        };
    }
}
