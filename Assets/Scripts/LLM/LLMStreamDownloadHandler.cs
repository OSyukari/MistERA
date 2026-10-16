using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine.Networking;

/// <summary>
/// Incrementally parses an SSE chat-completions stream (OpenAI-compatible `data: {...}` chunks, or
/// Claude's typed event stream) as bytes arrive from UnityWebRequest, firing onReasoningDelta with
/// the accumulated-so-far reasoning text on every reasoning-bearing chunk. Main body content is only
/// ever exposed via BuildFinalResponse() once the stream is fully drained, since it is a
/// structured-output JSON blob that is not valid until complete - this handler never fires partial
/// main-body content anywhere.
/// </summary>
public class LLMStreamDownloadHandler : DownloadHandlerScript
{
    readonly LLMProviderProfile profile;
    readonly Action<string> onReasoningDelta;

    readonly Decoder utf8Decoder = Encoding.UTF8.GetDecoder();
    readonly StringBuilder pendingLine = new StringBuilder();

    readonly StringBuilder reasoning = new StringBuilder();
    readonly StringBuilder content = new StringBuilder();

    // OpenAI-envelope tool-call argument accumulation (toolcall/preferToolCallArguments strategies
    // stream the structured-output JSON as fragments of tool_calls[].function.arguments rather than
    // as delta.content - see LLMProviderProfile.responseFormatStrategy == "toolcall").
    // A round can carry several parallel tool calls; their fragments interleave and are told apart
    // only by each delta entry's "index", so each index gets its own accumulator (in first-seen order).
    class ToolCallAccumulator
    {
        public string id;
        public string name;
        public readonly StringBuilder arguments = new StringBuilder();
    }
    readonly List<ToolCallAccumulator> toolCalls = new List<ToolCallAccumulator>();
    readonly Dictionary<int, ToolCallAccumulator> toolCallsByIndex = new Dictionary<int, ToolCallAccumulator>();
    bool sawToolCallDelta = false;

    // Claude-envelope content blocks, one per stream "index" (in first-seen order). Each block is
    // rebuilt in full - thinking text + signature, redacted_thinking data, tool_use id/name + streamed
    // input JSON - since the agent loop echoes them back verbatim and Claude rejects a continuation
    // whose thinking blocks lack their signature, and dispatches tool calls from the tool_use blocks.
    class ClaudeBlockAccumulator
    {
        public string type;
        public readonly StringBuilder text = new StringBuilder();
        public readonly StringBuilder thinking = new StringBuilder();
        public string signature;
        public string data;
        public string id;
        public string name;
        public JToken initialInput;
        public readonly StringBuilder partialJson = new StringBuilder();
    }
    readonly List<ClaudeBlockAccumulator> claudeBlocks = new List<ClaudeBlockAccumulator>();
    readonly Dictionary<int, ClaudeBlockAccumulator> claudeBlocksByIndex = new Dictionary<int, ClaudeBlockAccumulator>();
    string claudeStopReason = null;

    // Token usage as reported inside the stream: OpenAI-envelope endpoints put it on the last chunk,
    // Claude splits it across message_start (input/cache) and message_delta (output) - merged here.
    JObject usage = null;

    // First RawTextCap chars of the raw body, kept verbatim. An HTTP error reply is a plain JSON
    // error object rather than SSE `data:` lines, so ProcessLine drops it - this is the only copy.
    const int RawTextCap = 8192;
    readonly StringBuilder rawText = new StringBuilder();

    /// <summary>Raw (non-SSE-parsed) response body, capped at RawTextCap chars - read on request failure.</summary>
    public string RawText => rawText.ToString();

    void MergeUsage(JToken u)
    {
        if (!(u is JObject uo)) return;
        if (usage == null) usage = new JObject();
        foreach (var prop in uo.Properties()) usage[prop.Name] = prop.Value;
    }

    public LLMStreamDownloadHandler(LLMProviderProfile profile, Action<string> onReasoningDelta) : base()
    {
        this.profile = profile;
        this.onReasoningDelta = onReasoningDelta;
    }

    protected override bool ReceiveData(byte[] data, int dataLength)
    {
        if (data == null || dataLength <= 0) return true;

        char[] chars = new char[utf8Decoder.GetCharCount(data, 0, dataLength)];
        int charCount = utf8Decoder.GetChars(data, 0, dataLength, chars, 0);
        pendingLine.Append(chars, 0, charCount);
        if (rawText.Length < RawTextCap) rawText.Append(chars, 0, Math.Min(charCount, RawTextCap - rawText.Length));

        // Drain complete lines, keep any trailing partial line buffered for next call.
        int newlineIndex;
        while ((newlineIndex = IndexOfNewline(pendingLine)) >= 0)
        {
            string line = pendingLine.ToString(0, newlineIndex).TrimEnd('\r');
            pendingLine.Remove(0, newlineIndex + 1);
            ProcessLine(line);
        }
        return true;
    }

    static int IndexOfNewline(StringBuilder sb)
    {
        for (int i = 0; i < sb.Length; i++) if (sb[i] == '\n') return i;
        return -1;
    }

    void ProcessLine(string line)
    {
        if (string.IsNullOrEmpty(line)) return;
        // Both envelopes' event framing is carried in the JSON payload's own "type" field, so
        // "event: ..." lines (Claude sends these) are intentionally ignored here.
        if (!line.StartsWith("data:")) return;

        string payload = line.Substring(5).TrimStart();
        if (payload == "[DONE]") return; // OpenAI-envelope terminator; nothing further to do.

        JObject obj;
        try { obj = JObject.Parse(payload); }
        catch { return; }

        if (profile != null && profile.responseEnvelope == "claude") ProcessClaudeEvent(obj);
        else ProcessOpenAIChunk(obj);
    }

    void ProcessOpenAIChunk(JObject obj)
    {
        MergeUsage(obj["usage"]);

        // the include_usage final chunk carries an empty choices array
        var choices = obj["choices"] as JArray;
        if (choices == null || choices.Count == 0) return;
        var choice = choices[0];
        if (choice == null) return;
        var delta = choice["delta"];
        if (delta == null) return;

        var reasoningDelta = delta["reasoning_content"]?.Value<string>();
        if (!string.IsNullOrEmpty(reasoningDelta))
        {
            reasoning.Append(reasoningDelta);
            onReasoningDelta?.Invoke(reasoning.ToString());
        }

        var contentDelta = delta["content"]?.Value<string>();
        if (!string.IsNullOrEmpty(contentDelta)) content.Append(contentDelta);

        var toolCallDeltas = delta["tool_calls"] as JArray;
        if (toolCallDeltas != null && toolCallDeltas.Count > 0)
        {
            sawToolCallDelta = true;
            foreach (var tc in toolCallDeltas)
            {
                var acc = GetToolCallAccumulator(tc);
                var id = tc["id"]?.Value<string>();
                if (!string.IsNullOrEmpty(id)) acc.id = id;
                var name = tc["function"]?["name"]?.Value<string>();
                if (!string.IsNullOrEmpty(name)) acc.name = name;
                var args = tc["function"]?["arguments"]?.Value<string>();
                if (!string.IsNullOrEmpty(args)) acc.arguments.Append(args);
            }
        }
    }

    /// <summary>
    /// Resolves a tool_calls delta entry to its accumulator by "index". An endpoint that omits index
    /// is treated as single-call-at-a-time: a delta carrying a new id opens a new call, anything else
    /// continues the latest one.
    /// </summary>
    ToolCallAccumulator GetToolCallAccumulator(JToken tc)
    {
        var indexToken = tc["index"];
        if (indexToken != null && indexToken.Type == JTokenType.Integer)
        {
            int index = indexToken.Value<int>();
            if (!toolCallsByIndex.TryGetValue(index, out var byIndex))
            {
                byIndex = new ToolCallAccumulator();
                toolCallsByIndex[index] = byIndex;
                toolCalls.Add(byIndex);
            }
            return byIndex;
        }

        var id = tc["id"]?.Value<string>();
        var last = toolCalls.Count > 0 ? toolCalls[toolCalls.Count - 1] : null;
        if (last == null || (!string.IsNullOrEmpty(id) && last.id != null && last.id != id))
        {
            last = new ToolCallAccumulator();
            toolCalls.Add(last);
        }
        return last;
    }

    void ProcessClaudeEvent(JObject obj)
    {
        var type = obj["type"]?.Value<string>();
        switch (type)
        {
            case "content_block_start":
                {
                    var block = GetClaudeBlock(obj);
                    var start = obj["content_block"];
                    block.type = start?["type"]?.Value<string>();
                    block.id = start?["id"]?.Value<string>();
                    block.name = start?["name"]?.Value<string>();
                    block.data = start?["data"]?.Value<string>();
                    block.initialInput = start?["input"];
                    var initialText = start?["text"]?.Value<string>();
                    if (!string.IsNullOrEmpty(initialText)) block.text.Append(initialText);
                    break;
                }
            case "content_block_delta":
                {
                    var block = GetClaudeBlock(obj);
                    var delta = obj["delta"];
                    var deltaType = delta?["type"]?.Value<string>();
                    if (deltaType == "thinking_delta")
                    {
                        var text = delta["thinking"]?.Value<string>();
                        if (!string.IsNullOrEmpty(text))
                        {
                            block.thinking.Append(text);
                            reasoning.Append(text);
                            onReasoningDelta?.Invoke(reasoning.ToString());
                        }
                    }
                    else if (deltaType == "signature_delta")
                    {
                        block.signature = (block.signature ?? "") + delta["signature"]?.Value<string>();
                    }
                    else if (deltaType == "text_delta")
                    {
                        var text = delta["text"]?.Value<string>();
                        if (!string.IsNullOrEmpty(text)) block.text.Append(text);
                    }
                    else if (deltaType == "input_json_delta")
                    {
                        var partial = delta["partial_json"]?.Value<string>();
                        if (!string.IsNullOrEmpty(partial)) block.partialJson.Append(partial);
                    }
                    break;
                }
            case "message_start":
                MergeUsage(obj["message"]?["usage"]);
                break;
            case "message_delta":
                MergeUsage(obj["usage"]);
                var stopReason = obj["delta"]?["stop_reason"]?.Value<string>();
                if (!string.IsNullOrEmpty(stopReason)) claudeStopReason = stopReason;
                break;
            // content_block_stop / message_stop: no bookkeeping needed beyond what
            // content_block_start/delta already captured.
        }
    }

    ClaudeBlockAccumulator GetClaudeBlock(JObject evt)
    {
        int index = evt["index"]?.Value<int>() ?? 0;
        if (!claudeBlocksByIndex.TryGetValue(index, out var block))
        {
            block = new ClaudeBlockAccumulator();
            claudeBlocksByIndex[index] = block;
            claudeBlocks.Add(block);
        }
        return block;
    }

    /// <summary>
    /// A tool_use block's input arrives as input_json_delta fragments of one JSON object; a call with
    /// no arguments may send none at all, leaving content_block_start's (empty) input as the value.
    /// </summary>
    static JToken BuildToolInput(ClaudeBlockAccumulator block)
    {
        if (block.partialJson.Length > 0)
        {
            try { return JToken.Parse(block.partialJson.ToString()); }
            catch
            {
                UnityEngine.Debug.LogError($"LLM stream: failed to parse tool_use input for '{block.name}' from [{block.partialJson}]");
                return new JObject();
            }
        }
        return block.initialInput ?? new JObject();
    }

    protected override void CompleteContent()
    {
        // Final partial line (e.g. missing trailing newline on the very last chunk) is processed here.
        if (pendingLine.Length > 0)
        {
            ProcessLine(pendingLine.ToString().TrimEnd('\r'));
            pendingLine.Clear();
        }
    }

    /// <summary>
    /// Synthesizes a fully-populated LLMResponse matching what a non-streamed response of the same
    /// shape would look like, so the rest of the pipeline (LLMResponse.JSON, LLMResponse.Reasoning,
    /// scr_panel_LLM's single-shot response handling) needs no streaming-specific branches downstream.
    /// </summary>
    public LLMResponse BuildFinalResponse()
    {
        var response = new LLMResponse();
        UnityEngine.Debug.Log($"LLM stream usage: {(usage != null ? usage.ToString(Newtonsoft.Json.Formatting.None) : "none reported")}");
        if (usage != null) response.usage = usage.ToObject<LLMResponse.usages>();
        if (profile != null && profile.responseEnvelope == "claude")
        {
            response.stop_reason = claudeStopReason;
            foreach (var block in claudeBlocks)
            {
                switch (block.type)
                {
                    case "thinking":
                        response.content.Add(new LLMResponse.choice_claude { type = "thinking", thinking = block.thinking.ToString(), signature = block.signature });
                        break;
                    case "redacted_thinking":
                        response.content.Add(new LLMResponse.choice_claude { type = "redacted_thinking", data = block.data });
                        break;
                    case "tool_use":
                        response.content.Add(new LLMResponse.choice_claude { type = "tool_use", id = block.id, name = block.name, input = BuildToolInput(block) });
                        break;
                    case "text":
                        response.content.Add(new LLMResponse.choice_claude { type = "text", text = block.text.ToString() });
                        break;
                    // other block types (server tools etc.) are never requested by this client
                }
            }
            // Keep the old always-has-a-text-block shape for non-tool replies (LLMResponse.JSON reads it).
            if (!response.content.Exists(c => c.type == "text" || c.type == "tool_use"))
                response.content.Add(new LLMResponse.choice_claude { type = "text", text = "" });
        }
        else
        {
            var message = new LLMMessage { role = "assistant" };
            if (reasoning.Length > 0) message.reasoning_content = reasoning.ToString();

            if (sawToolCallDelta)
            {
                message.tool_calls = new List<LLMMessage.ToolCall>();
                foreach (var acc in toolCalls)
                {
                    message.tool_calls.Add(new LLMMessage.ToolCall {
                        id = acc.id,
                        type = "function",
                        function = new LLMMessage.FunctionCall { name = acc.name, arguments = acc.arguments.ToString() }
                    });
                }
            }
            else
            {
                message.content = content.ToString();
            }

            response.choices.Add(new LLMResponse.choice { index = 0, message = message, finish_reason = "stop" });
        }
        return response;
    }
}
