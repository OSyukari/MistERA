using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Text;


public class LLMMessage_Final
{
    public string role;

    /// <summary>
    /// A property (not a plain field) so LLMMessage_Final_Claude below can override it - ordinary
    /// messages (and OpenAI tool-result messages) use this plain string as-is.
    /// </summary>
    public virtual string content { get; set; }

    /// <summary>OpenAI tool-result continuation message field (role == "tool").</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string tool_call_id = null;

    /// <summary>
    /// Present when re-emitting an assistant turn that made tool calls (OpenAI requires echoing the
    /// exact tool_calls array back into the transcript on the following round).
    /// </summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public List<LLMMessage.ToolCall> tool_calls = null;
}

/// <summary>
/// Claude-only continuation message: tool-use echo / tool-result messages need `content` to be an
/// array of typed blocks rather than a plain string (see LLMClaudeContentBlock, LLMToolProtocol.cs).
/// Overrides the base's string `content` (never used on this subclass - always null, so
/// LLMRequest.ReplaceString/ReplaceType's existing null-check already skips it with no extra casing)
/// and carries the real data via `contentBlocks`, mapped onto that same "content" JSON key.
/// </summary>
public class LLMMessage_Final_Claude : LLMMessage_Final
{
    [JsonIgnore]
    public override string content { get => null; set { } }

    [JsonProperty("content")]
    public List<LLMClaudeContentBlock> contentBlocks;
}
