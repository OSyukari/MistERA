using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using UnityEngine;




public class LLMMessage
{
    public string role;
    public string content;

    /// <summary>
    /// OpenAI-compatible reasoning text (DeepSeek/GLM/Kimi-style convention) on the response
    /// envelope's message. Never set on outgoing request messages that reuse this class.
    /// </summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string reasoning_content = null;

    public string name;
    public bool enabled = true;

    /// <summary>
    /// Optional tag used for provider-driven enable/disable overrides (see
    /// LLMProviderProfile.enableNodeByTag/disableNodeByTag). Only consulted on root-level entries of
    /// LLMPresetTemplateData.messages - tags on nested LLMMessage_All/LLMMessage_Select children are
    /// present on the type but intentionally never read.
    /// </summary>
    public string tag = null;

    [JsonIgnore]
    public virtual string Content
    {
        get
        {
            return content;
        }
    }

    /// <summary>
    /// Resolves this node to its final text, applying setvar/getvar against the shared
    /// dictionary as it goes. Only meaningful to call when enabled && isValid.
    /// </summary>
    public virtual string Resolve(Dictionary<string, string> vars)
    {
        return LLMUtils.ApplyMacros(content, vars);
    }

    [JsonIgnore]
    public virtual bool isValid
    {
        get
        {
            return content != null && content.Length > 0;
        }
    }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public List<ToolCall> tool_calls = null;

    public class ToolCall
    {
        public string id;
        public string type;
        public FunctionCall function;
    }
    public class FunctionCall
    {
        public string name;
        public string arguments;
    }

    public LLMMessage() { }
    public LLMMessage(LLMMessage message)
    {
        this.role = message.role;
        this.content = message.content;// StripCodeFence(message.content);
    }

    internal static string StripCodeFence(string content)
    {
        if (string.IsNullOrEmpty(content)) return content;


        MatchCollection matches = LLMUtils.regex_JSONWrapper.Matches(content);

        foreach (var match in matches.ToList())
        {
            return match.Groups["jsonContent"].Value;
        }
        /*
        int start = content.IndexOf("```json");
        if (start < 0) return content;

        int end = content.LastIndexOf("```");
        if (end <= start) return content;

        int lineStart = content.IndexOf('\n', start);
        if (lineStart < 0 || lineStart >= end)
        {
            string body = content.Substring(start + 3, end - start - 3).Trim();
            int jb = body.IndexOfAny(new[] { '{', '[' });
            if (jb >= 0) body = body.Substring(jb);
            return body;
        }

        return content.Substring(lineStart + 1, end - lineStart - 1).Trim();*/
        return content;
    }

    MessageJSON json = null;

    public MessageJSON json_serialized = null;

    public MessageJSON GetContent()
    {
        if (json != null) return json;

        var profile = scr_System_CentralControl.current.ResolveProviderProfile(scr_System_CentralControl.current.LLMSetting.chatCompletionModel);
        json = LLMProviderUtils.UnwrapMessageJSON(content, tool_calls, profile);
        json_serialized = json;
        return json;
    }


    public class MessageJSON_blocks
    {
        public List<MessageParagraph> content = new List<MessageParagraph>();
    }

    public class MessageJSON_simple
    {
        public string content;
    }
}