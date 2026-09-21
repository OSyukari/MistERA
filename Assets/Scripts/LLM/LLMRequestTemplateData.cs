using System.Collections.Generic;
/// <summary>
/// Tree-shaped template data, deserialized directly from Assets/LLM/LLM_Preset.json.
/// Kept separate from LLMRequest (the flat, wire-ready payload) because the template's
/// messages need $type polymorphism (LLMMessage_All / LLMMessage_Select) to deserialize,
/// while LLMRequest.messages is List&lt;LLMMessage_Final&gt;.
/// </summary>
public class LLMPresetTemplateData
{
    public List<LLMMessage> messages = new List<LLMMessage>();

    public float temperature = 0.7f;
    public int? max_tokens = null;
    public int? max_completion_tokens = 512;
    public double? top_p = 1.0;
    public int? top_k = 40;
}

/// <summary>
/// Per-mode overlay data, deserialized from Assets/LLM/Request_Slow.json / Request_Fast.json.
/// Applied on top of a loaded LLMPresetTemplateData: each entry in replacements is substituted
/// as %%key%% into whatever message content still contains it (e.g. "rules" -> %%rules%%),
/// and response_format supplies the mode's own (differently-shaped) structured-output schema.
/// </summary>
public class LLMRequestTemplateData
{
    public Dictionary<string, string> replacements = new Dictionary<string, string>();
    public LLMRequest.ResponseFormatter response_format = null;
}
