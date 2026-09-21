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
    string toolCallId = null;
    string toolCallName = null;
    readonly StringBuilder toolCallArguments = new StringBuilder();
    bool sawToolCallDelta = false;

    // Claude-envelope content_block_start bookkeeping: which index is "thinking" vs "text".
    readonly Dictionary<int, string> claudeBlockTypes = new Dictionary<int, string>();

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
        var choice = obj["choices"]?[0];
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

        var toolCalls = delta["tool_calls"] as JArray;
        if (toolCalls != null && toolCalls.Count > 0)
        {
            sawToolCallDelta = true;
            var tc = toolCalls[0];
            var id = tc["id"]?.Value<string>();
            if (!string.IsNullOrEmpty(id)) toolCallId = id;
            var name = tc["function"]?["name"]?.Value<string>();
            if (!string.IsNullOrEmpty(name)) toolCallName = name;
            var args = tc["function"]?["arguments"]?.Value<string>();
            if (!string.IsNullOrEmpty(args)) toolCallArguments.Append(args);
        }
    }

    void ProcessClaudeEvent(JObject obj)
    {
        var type = obj["type"]?.Value<string>();
        switch (type)
        {
            case "content_block_start":
                {
                    int index = obj["index"]?.Value<int>() ?? 0;
                    var blockType = obj["content_block"]?["type"]?.Value<string>();
                    if (blockType != null) claudeBlockTypes[index] = blockType;
                    break;
                }
            case "content_block_delta":
                {
                    var delta = obj["delta"];
                    var deltaType = delta?["type"]?.Value<string>();
                    if (deltaType == "thinking_delta")
                    {
                        var text = delta["thinking"]?.Value<string>();
                        if (!string.IsNullOrEmpty(text))
                        {
                            reasoning.Append(text);
                            onReasoningDelta?.Invoke(reasoning.ToString());
                        }
                    }
                    else if (deltaType == "text_delta")
                    {
                        var text = delta["text"]?.Value<string>();
                        if (!string.IsNullOrEmpty(text)) content.Append(text);
                    }
                    break;
                }
            // content_block_stop / message_start / message_delta / message_stop: no bookkeeping
            // needed beyond what content_block_start/delta already captured.
        }
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
    /// scr_menu_LLMQuery.LoadResponse/Animate) needs no streaming-specific branches downstream.
    /// </summary>
    public LLMResponse BuildFinalResponse()
    {
        var response = new LLMResponse();
        if (profile != null && profile.responseEnvelope == "claude")
        {
            if (reasoning.Length > 0) response.content.Add(new LLMResponse.choice_claude { type = "thinking", thinking = reasoning.ToString() });
            response.content.Add(new LLMResponse.choice_claude { type = "text", text = content.ToString() });
        }
        else
        {
            var message = new LLMMessage { role = "assistant" };
            if (reasoning.Length > 0) message.reasoning_content = reasoning.ToString();

            if (sawToolCallDelta)
            {
                message.tool_calls = new List<LLMMessage.ToolCall> {
                    new LLMMessage.ToolCall {
                        id = toolCallId,
                        type = "function",
                        function = new LLMMessage.FunctionCall { name = toolCallName, arguments = toolCallArguments.ToString() }
                    }
                };
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
