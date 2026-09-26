using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using UnityEngine;
using static LLMUtils;

public class LLM_Setting
{
    public bool enabled = true;

    /// <summary>
    /// When true, scr_UpdateHandler.SendLLMRequest(string,bool) redirects to SendAgentRequest instead of
    /// the single-shot path - see scr_UpdateHandler.cs. Persisted via the same encrypted llmSetting.json
    /// round-trip as the rest of LLM_Setting (StoreLLMSetting/LoadLLMSetting).
    /// </summary>
    public bool useAgentMode = false;

    public class ChatCompletion
    {
        public string id = System.Guid.NewGuid().ToString();

        [Obsolete("kept only for one-time migration of old llmSetting.json saves; use providerId")]
        public int APIType = 0;

        /// <summary>
        /// References an id in scr_System_CentralControl.LLMProviderProfiles (e.g.
        /// "LLM_Provider_google", "LLM_Provider_anthropic", ...; each id doubles as its own
        /// LocalizeDictionary key). Null, unresolved, or the isCustomEntry profile's id falls back
        /// to hint-based auto-detection - see scr_System_CentralControl.ResolveProviderProfile.
        /// </summary>
        public string providerId = null;

        public string modellist = "";
        public string endpoint = "";
        public string key = "";
        public string model = "";
        public string comment = "";
    }

    public string currentPresetId = null;
    public List<ChatCompletion> chatCompletionModels = new List<ChatCompletion>();

    /// <summary>
    /// Selects the current prompt-template preset (resolved via
    /// scr_System_CentralControl.CurrentPreset) - a distinct axis from currentPresetId,
    /// which selects the API/endpoint credentials profile.
    /// </summary>
    public string currentPromptTemplateId = null;

    [JsonIgnore]
    public ChatCompletion chatCompletionModel
    {
        get
        {
            return currentPresetId == null ? null : chatCompletionModels.Find(c => c.id == currentPresetId);
        }
    }
}




public class LLMRequest
{
    public List<string> prepend = null;
    public List<LLMMessage_Final> messages = new List<LLMMessage_Final>();
    public string currentString;

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string? system = null;

    public string model;
    public float temperature = 0.7f;

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public int? max_tokens = null;

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public int? max_completion_tokens = 512;
    public bool stream = false;

    /// <summary>OpenAI-envelope streaming option; set by ApplyRequestShaping only when the profile's
    /// streamIncludeUsage asks for a usage chunk at the end of the stream.</summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public StreamOptions stream_options = null;

    public class StreamOptions
    {
        public bool include_usage = true;
    }


    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public double? top_p = 1.0; 
    
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public int? top_k = 40;

    /// <summary>
    /// Provider-specific reasoning/thinking effort level (e.g. "low"/"medium"/"high"). Valid values
    /// differ per provider - see the "_reasoningEffortOptions" comment in each Assets/LLM/Providers/*.json
    /// file. Stripped by LLMProviderUtils.ApplyRequestShaping when LLMProviderProfile.supportsReasoningEffort
    /// is false, same as top_k/top_p. For the "claude" responseEnvelope this field is never actually sent
    /// under this name - ApplyRequestShaping.ApplyClaudeEffort relocates it to output_config.effort instead,
    /// since Claude has no flat reasoning_effort parameter.
    /// </summary>
    [JsonProperty("reasoning_effort", NullValueHandling = NullValueHandling.Ignore)]
    public string reasoningEffort = null;

    public ResponseFormatter response_format = null;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public ResponseFormatter_Claude output_config = null;


    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public List<ResponseFormatter_Tools> tools = null;

    /// <summary>
    /// Either a ResponseFormatter_ZAI_toolchoice (existing forced-single-tool structured-output
    /// workaround), the string "auto" (OpenAI-envelope agent mode), or a
    /// ResponseFormatter_ClaudeToolChoice (Claude-envelope agent mode) - whichever ApplyRequestShaping
    /// assigned for this request. Never read back in code, only serialized.
    /// </summary>
    public object tool_choice = null;

    /// <summary>
    /// True for a request built by the agentic tool-calling orchestrator, as opposed to the existing
    /// single-shot/structured-output path. Read by LLMProviderUtils.ApplyRequestShaping to decide
    /// whether to shape `tools`/`tool_choice` for a real multi-tool agent turn instead of the existing
    /// toolcall-strategy submit_response workaround - the two are mutually exclusive per request.
    /// Never sent to the API.
    /// </summary>
    [JsonIgnore]
    public bool agentTurn = false;


    /// <summary>
    /// Resolves its own provider profile so root-level nodes can be force-enabled/disabled by
    /// LLMMessage.tag via LLMProviderProfile.enableNodeByTag/disableNodeByTag, decided before
    /// Resolve() runs (so a skipped node's {{setvar}} side effects never fire) and without mutating
    /// req itself, since req is the shared cached LLMPresetTemplate reused across every request.
    /// agentMode gates the two mode-reserved tags - "agent_only" nodes ride only on agent-mode
    /// builds, "single_shot_only" only on single-shot builds - so one preset file serves both modes.
    /// </summary>
    public void LoadTemplate(LLMPresetTemplateData req, bool agentMode = false)
    {
        this.temperature = req.temperature;
        this.max_completion_tokens = req.max_completion_tokens;
        this.max_tokens = req.max_tokens;
        this.top_p = req.top_p;
        this.top_k = req.top_k;

        var profile = scr_System_CentralControl.current.ResolveProviderProfile(scr_System_CentralControl.current.LLMSetting.chatCompletionModel);

        var vars = new Dictionary<string, string>();
        foreach (var root in req.messages)
        {
            if (root == null || !root.isValid) continue;

            // Mode-reserved tags: agent-vs-single-shot is a request-time decision (unlike the
            // profile tag overrides below, which are provider-driven). A node's own enabled flag
            // and the profile overrides still apply on top of this gate.
            if (!string.IsNullOrEmpty(root.tag))
            {
                if (root.tag == "agent_only" && !agentMode) continue;
                if (root.tag == "single_shot_only" && agentMode) continue;
            }

            bool enabledByTag = profile != null && !string.IsNullOrEmpty(root.tag)
                && profile.enableNodeByTag != null && profile.enableNodeByTag.Contains(root.tag);
            if (!root.enabled && !enabledByTag) continue;

            bool disabledByTag = profile != null && !string.IsNullOrEmpty(root.tag)
                && profile.disableNodeByTag != null && profile.disableNodeByTag.Contains(root.tag);
            if (disabledByTag) continue;

            var content = root.Resolve(vars);
            if (string.IsNullOrEmpty(content)) continue;
            messages.Add(new LLMMessage_Final { role = root.role, content = content });
        }
    }

    /// <summary>
    /// Applies a mode-specific overlay (Request_Slow.json / Request_Fast.json) on top of an
    /// already-loaded preset: each replacements entry is substituted as %%key%% into the
    /// resolved messages (e.g. "rules" -> %%rules%%), and response_format supplies this
    /// mode's own structured-output schema.
    /// </summary>
    public void LoadTemplate(LLMRequestTemplateData req)
    {
        if (req.replacements != null)
        {
            foreach (var kvp in req.replacements)
            {
                if (string.IsNullOrEmpty(kvp.Value)) continue;
                ReplaceString($"%%{kvp.Key}%%", kvp.Value);
            }
        }
        if (req.response_format != null) this.response_format = req.response_format;
    }

    public void ReplaceString(string a, string b)
    {
        foreach(var message in this.messages)
        {
            // content is always null on an LLMMessage_Final_Claude (its override returns null - the
            // real data lives in contentBlocks instead, see LLMMessage_Final.cs) - these only ever get
            // appended after this substitution pass runs on the initial template build, but guard
            // anyway since it's now a valid state for any LLMMessage_Final to be in.
            if (message.content == null) continue;
            message.content = message.content.Replace(a, b);
        }
    }
    public void ReplaceType(string a, string b)
    {
        foreach (var message in this.messages)
        {
            if (message.content == null) continue;
            message.content = message.content.Replace(a, b);
        }
    }

    public void Purge()
    {
        currentString = null;
        prepend = null;
        if (this.response_format != null) this.response_format.Purge();


    }

    public class ResponseFormatter_Tools
    {

    }

    public class ResponseFormatter_ZAI_tools : ResponseFormatter_Tools
    {

        public class ResponseFormatter_ZAI_tools_2
        {
            public string name = "submit_response";
            public string description = "Submit the complete structured game response. You MUST call this function with every required field populated; do not write the JSON in your text content.";
            public LLMFormatSchema parameters = new LLMFormatSchema();
        }

        public string type = "function";
        public ResponseFormatter_ZAI_tools_2 function = new ResponseFormatter_ZAI_tools_2();

        public void ReplaceType(string a, string b)
        {
            if (this.function != null && this.function.parameters != null)
            {
                this.function.parameters.ReplaceType(a, b);
            }
        }
    }

    /// <summary>
    /// One OpenAI-style function-tool definition entry for agent-mode's real multi-tool loop
    /// (responseEnvelope == "openai"). Shares the wire shape with ResponseFormatter_ZAI_tools
    /// ({type:"function", function:{name,description,parameters}}) but is kept as a distinct type
    /// since ZAI_tools is specifically the single-synthetic-"submit_response"-tool structured-output
    /// workaround (responseFormatStrategy == "toolcall"). For "toolcall"-strategy agent turns, a
    /// ResponseFormatter_ZAI_tools entry for submit_response rides alongside a list of these in the
    /// same `tools` array (see LLMProviderProfile.ApplyRequestShaping's "toolcall" agent branch) -
    /// the two types stay distinct so LLMResponseToolParser can tell them apart by shape/origin, not
    /// because they're mutually exclusive on the wire.
    /// </summary>
    public class ResponseFormatter_AgentTool : ResponseFormatter_Tools
    {
        public class FunctionDef
        {
            public string name;
            public string description;
            public LLMFormatSchema parameters;
        }
        public string type = "function";
        public FunctionDef function = new FunctionDef();
    }

    /// <summary>
    /// One Claude-style tool definition entry for agent mode (responseEnvelope == "claude") - flat
    /// {name, description, input_schema} shape, distinct from OpenAI's nested {type,function{...}}.
    /// </summary>
    public class ResponseFormatter_ClaudeTool : ResponseFormatter_Tools
    {
        public string name;
        public string description;
        public LLMFormatSchema input_schema;
    }

    /// <summary>Claude's {"type":"auto"} tool_choice shape for agent mode.</summary>
    public class ResponseFormatter_ClaudeToolChoice
    {
        public string type = "auto";
    }

    public LLMRequest() { }
    public LLMRequest(bool initialize)
    {
        this.response_format = new ResponseFormatter(initialize);
    }

    public class ResponseFormatter_Claude
    {
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public ResponseFormatter_Claude_2 format;

        /// <summary>
        /// Claude's real reasoning-effort control - "low" | "medium" | "high" | "xhigh" | "max",
        /// set here instead of as a top-level reasoning_effort field. See ApplyRequestShaping.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string effort;

        public class ResponseFormatter_Claude_2
        {
            public string type = "json_schema";
            public LLMFormatSchema schema;
        }

        public ResponseFormatter_Claude() { }

        public ResponseFormatter_Claude(ResponseFormatter format)
        {
            this.format = new ResponseFormatter_Claude_2();
            this.format.schema = format.json_schema.schema;
        }

    }


    public class ResponseFormatter_ZAI_toolchoice
    {
        public class ResponseFormatter_ZAI_toolchoice2
        {
            public string name = "submit_response";
        }

        public string type = "function";
        public ResponseFormatter_ZAI_toolchoice2 function = new ResponseFormatter_ZAI_toolchoice2();
    }

    public class ResponseFormatter
    {
        public string type = "json_schema";
        public JsonSchema json_schema = new JsonSchema();

        public ResponseFormatter() { }
        public ResponseFormatter(bool initialize)
        {
            json_schema = new JsonSchema();
        }

        public void Purge()
        {
            if (this.json_schema != null) this.json_schema.Purge();
        }

        public void ReplaceType(string a, string b)
        {
            if (this.json_schema != null)
            {
                this.json_schema.ReplaceType(a, b);
            }
        }

        public class JsonSchema
        {
            public string name = "mistera_format";
            public bool strict = true;
            public LLMFormatSchema schema = new LLMFormatSchema();

            public void ReplaceType(string a, string b)
            {
                if (this.schema != null)
                {
                    this.schema.ReplaceType(a, b);
                }
            }

            public JsonSchema()
            {

            }

            public void Purge()
            {
                if (this.schema != null) this.schema.Purge();
            }

            
        }

    }

}

public class LLMFormatSchema
{
    public string type = "object";
    public Dictionary<string, Type> properties = new Dictionary<string, Type>();


    public void ReplaceType(string a, string b)
    {
        if (this.properties != null)
        {
            foreach (var p in properties.Values) p.ReplaceType(a, b);
        }
    }
    public LLMFormatSchema()
    {

    }
    public class Type
    {
        public string description;
        public string? example = null;
        public virtual void Purge()
        {

        }
        public Type()
        {

        }
        public Type(string s)
        {
            this.description = s;
        }

        public virtual void ReplaceType(string a, string b)
        {

        }
    }

    public class Type_Simple : Type
    {
        public string type;
        public Type_Simple()
        {

        }
        public Type_Simple(string type, string desc) : base(desc)
        {
            this.type = type;
        }

        public override void ReplaceType(string a, string b)
        {
            if (this.type == a) this.type = b;
        }
    }
    public class Type_Enum : Type
    {
        public string type = "string";

        [JsonProperty("enum")]
        public List<string> enums = new List<string>();
        public Type_Enum()
        {

        }
        public Type_Enum(List<string> enums, string desc) : base(desc)
        {
            this.enums = enums;
        }
        // New Constructor: Handles any Enum type
        public Type_Enum(System.Type enumType, string desc) : base(desc)
        {
            if (enumType == null)
                throw new ArgumentNullException(nameof(enumType));

            if (!enumType.IsEnum)
                throw new ArgumentException("Provided type must be an Enum.", nameof(enumType));

            // GetNames retrieves the string representations of all members
            this.enums = Enum.GetNames(enumType).ToArray().ToList();
        }

        public override void ReplaceType(string a, string b)
        {
            if (this.type == a) this.type = b;
        }
    }

    public class Type_Object : Type
    {

        public string type = "object";
        public Dictionary<string, Type> properties;
        public Type_Object()
        {

        }
        public Type_Object(Dictionary<string, Type> properties, string desc) : base(desc)
        {
            this.properties = properties;
        }

        public List<string> required = new List<string>();
        public bool additionalProperties = false;
        public override void Purge()
        {
            required.Clear();
            foreach (var type in this.properties)
            {
                required.Add(type.Key);
                type.Value.Purge();
            }
        }
        public override void ReplaceType(string a, string b)
        {
            if (this.type == a) this.type = b;
            foreach (var p in properties.Values) p.ReplaceType(a, b);
        }
    }

    public class Type_Array : Type
    {
        public string type = "array";
        public Type items;
        public Type_Array()
        {

        }
        public Type_Array(Type items, string desc) : base(desc)
        {
            this.items = items;
        }

        public override void ReplaceType(string a, string b)
        {
            if (this.type == a) this.type = b;
            items.ReplaceType(a, b);
        }
    }


    public List<string> required = new List<string>();
    public bool additionalProperties = false;

    public void Purge()
    {
        required.Clear();
        foreach (var type in this.properties)
        {
            required.Add(type.Key);
            type.Value.Purge();
        }
    }
}


public class MessageParagraph : I_hasPortrait
{
    public string content_text;
    public int portraitRefID = -1;
    public List<string> portraitTags = new List<string>();
    public string CommandID;

    /// <summary>
    /// In-game clock time ("HH:mm") this block's content takes place, LLM-provided per the schema.
    /// Copied by the model from its context's time anchors (worldInfo current time, the [HH:mm]
    /// prefixes on tool-result/batch-report messages) rather than invented. Read by
    /// scr_panel_logs.DrawBlockHeader for the block header label and its cumulative
    /// "every captured game message with time <= this block's time" tooltip. Null/empty/garbage on
    /// old or nonconforming responses - consumers must TryParseClock it.
    /// </summary>
    public string time = null;

    [JsonIgnore]
    public List<string> SelfPortraitTag { get { return portraitTags; } }
    [JsonIgnore]
    public List<string> TargetPortraitTag { get { return new List<string>(); } }
}
public class MessageJSON
{
    //public string think;
    public string summary;
    public string content_string;
    public List<MessageParagraph> content_blocks = new List<MessageParagraph>();
    public int timeCost = 0;
    public List<int> relevantActorRefs = new List<int>();
    public List<APJSON> UpdateVariable = new List<APJSON>();
    /// <summary>
    /// Start at 0
    /// </summary>
    public int animatedIndex = 0;

    [JsonIgnore]
    public bool CanAnimate
    {
        get
        {
            return (content_blocks.Count > animatedIndex) || (content_string != null && content_string.Length > animatedIndex);
        }
    }


    protected List<ActionPackage> actionpackages = null;
    protected List<string> tooltips = new List<string>();
    public string disclaimer;

    public List<ActionPackage> GetActionPackages(out List<string> tooltip)
    {
        tooltip = tooltips;
        if (actionpackages == null)
        {
            tooltips.Clear();
            actionpackages = new List<ActionPackage>();
            Debug.Log($"parsing AP, total json count {UpdateVariable.Count}");

            foreach (var tempAP in UpdateVariable)
            {
                if (tempAP.innerCOM == null) continue;

                if (tempAP.command_result == Memory_Response.None) tempAP.command_result = Memory_Response.Accept;

                var job = scr_System_CampaignManager.current.FindJobInstanceByID(tempAP.SourceJobID);
                if (job == null) continue;

                var doers = new List<int>();
                if (tempAP.doer_RefID != -1) doers.Add(tempAP.doer_RefID);

                var receivers = new List<int>();
                if (tempAP.receiver_RefID != -1) receivers.Add(tempAP.receiver_RefID);

                var masterRef = tempAP.doer_RefID;

                bool merged = false;
                foreach (var ap in actionpackages)
                {
                    if (ap.JoinAP(tempAP, out var error))
                    {
                        merged = true;
                        tooltips.Add($"{tempAP.CommandID}({tempAP.doer_RefID}+{tempAP.receiver_RefID}) merged with {ap.targetCOM.ID}({String.Join(" ", ap.DoerRefs)}+{String.Join(" ", ap.ReceiverRefs)})");
                        break;
                    }
                    else
                    {
                        tooltips.Add($"{tempAP.CommandID}({tempAP.doer_RefID}+{tempAP.receiver_RefID}) cannot merge with {ap.targetCOM.ID}({String.Join(" ", ap.DoerRefs)}+{String.Join(" ", ap.ReceiverRefs)}) due to {error}");
                    }
                }
                if (merged)
                {
                    continue;
                }
                else
                {
                    var newap = tempAP.innerCOM.MakePackage(job, doers, receivers, masterRef);
                    newap.epjson.Add(tempAP);
                    actionpackages.Add(newap);
                    Debug.Log($"parsing AP create package, current at {actionpackages.Count}");
                }
            }
        }
        return actionpackages;
    }
}
public class APJSON
{
    public string CommandID;
    public int SourceJobID;
    public Memory_Response command_result = Memory_Response.None;
    public int doer_RefID = -1;
    public Memory_Attitude participant_attitude = Memory_Attitude.None;
    public int receiver_RefID = -1;
    public int source_content_text_Index;
    public int repeatCount = 1;

    [JsonIgnore]
    public COM innerCOM
    {
        get
        {
            if (_com == null && CommandID != null && CommandID.Length > 0)
            {
                _com = scr_System_Serializer.current.MasterList.COMs.GetByID(CommandID);
            }
            return _com;
        }
    }
    COM _com = null;
}


public class LLMResponse
{
    public string id;
    public string created;
    public string model;
    public string role;
    public string type;
    public List<choice> choices = new List<choice>();
    public usages usage;
    public string stop_reason;
    public List<choice_claude> content = new List<choice_claude>();

    public class choice
    {
        public int index;
        public LLMMessage message;
        public string finish_reason;


        [JsonIgnore]
        public MessageJSON JSON
        {
            get
            {
                if (message == null) return null;
                return message.GetContent();
            }
        }
    }

    public class choice_claude
    {
        public string type;
        public string text;

        /// <summary>
        /// Claude extended-thinking block text, populated only when type == "thinking".
        /// </summary>
        public string thinking;

        /// <summary>
        /// Tool-use block fields, populated only when type == "tool_use" - see
        /// LLMResponseToolParser.TryExtractToolCalls, which reads these to build a normalized
        /// LLMToolCallRequest for the agent-mode orchestration loop.
        /// </summary>
        public string id;
        public string name;
        public JToken input;

        [JsonIgnore]
        public MessageJSON JSON
        {
            get
            {
                return GetContent();
            }
        }
        MessageJSON json = null;
        public MessageJSON json_serialized = null;
        public MessageJSON GetContent()
        {
            if (json != null) return json;

            // Claude's content[].text envelope never carries tool_calls, so toolCalls/profile are null here.
            json = LLMProviderUtils.UnwrapMessageJSON(text, null, null);
            json_serialized = json;
            return json;
        }
    }
    /// <summary>
    /// Token usage as each provider reports it - OpenAI/ZAI/DeepSeek (prompt_/completion_tokens, with
    /// cache hits under prompt_tokens_details.cached_tokens or DeepSeek's prompt_cache_hit_tokens) or
    /// Claude (input_/output_tokens, cache under cache_read_/cache_creation_input_tokens). Use the
    /// normalized Input/Output/Cached getters rather than the raw fields.
    /// </summary>
    public class usages
    {
        public int prompt_tokens;
        public int completion_tokens;
        public int total_tokens;
        public int input_tokens;
        public int output_tokens;

        public promptDetails prompt_tokens_details;
        public int? prompt_cache_hit_tokens;
        public int? prompt_cache_miss_tokens;
        public int? cache_read_input_tokens;
        public int? cache_creation_input_tokens;

        public class promptDetails
        {
            public int? cached_tokens;
        }

        /// <summary>Total prompt tokens, cached ones included (Claude reports cache reads/writes
        /// separately from input_tokens, so they are added back).</summary>
        [JsonIgnore] public int Input => prompt_tokens > 0 ? prompt_tokens
            : input_tokens + (cache_read_input_tokens ?? 0) + (cache_creation_input_tokens ?? 0);
        [JsonIgnore] public int Output => completion_tokens > 0 ? completion_tokens : output_tokens;
        /// <summary>Prompt tokens served from the provider's cache; null when the provider reports no
        /// cache figure at all (distinct from a reported 0).</summary>
        [JsonIgnore] public int? Cached => prompt_tokens_details?.cached_tokens ?? prompt_cache_hit_tokens ?? cache_read_input_tokens;
        [JsonIgnore] public bool HasData => Input > 0 || Output > 0;
    }

    /// <summary>Wall-clock seconds the request took, stamped by SendLLMRequest_Routine (not sent/saved).</summary>
    [JsonIgnore] public double elapsedSeconds;

    [JsonIgnore]
    public MessageJSON JSON
    {
        get
        {
            if (choices.Count > 0) return choices[0].JSON;
            var textBlock = content.FirstOrDefault(c => c.type == "text");
            return textBlock?.JSON;
        }
    }

    /// <summary>
    /// Reasoning/thinking text pulled from whichever envelope shape is populated - OpenAI-compatible
    /// reasoning_content on the message, or Claude's thinking-typed content block. Independent of
    /// streaming: fully populated in one shot for non-streamed responses, or accumulated
    /// incrementally during a stream (see LLMStreamDownloadHandler).
    /// </summary>
    [JsonIgnore]
    public string Reasoning
    {
        get
        {
            if (choices.Count > 0) return choices[0].message?.reasoning_content;
            var thinkingBlock = content.FirstOrDefault(c => c.type == "thinking");
            return thinkingBlock?.thinking;
        }
    }
}


public class LLM_WorldState
{
    public class CharaStorage
    {
        public string FirstName;
        public int RefID;
        public string Description;
        public List<string> Status = null;
        public List<MemoryStorage> Memories = null;
        public string CurrentlyDoing;
        public string CurrentLocation;
        public string NextHourPlan;
        public Dictionary<string, RelationshipStorage> Relationships = null;
        public List<string> equipments = null;
        //public Dictionary<string, string> schedule = null;
        public string LorebookEntry = null;
        public List<string> ValidPortraitTags = new List<string>();
        public List<string> ValidPortraitTags_target = new List<string>();

        public class RelationshipStorage
        {
            public Dictionary<string, int> Scores = new Dictionary<string, int>();
            public string CurrentRelationships = "";
            public string CurrentAttitude = "";

            public RelationshipStorage()
            {

            }
            public RelationshipStorage(Character_Relationship rel, bool isgeneric = false)
            {
                Scores.Add("Trust", (int)rel.Trust);
                Scores.Add("Goodwill", (int)rel.Goodwill);
                Scores.Add("Badwill", (int)rel.Badwill);
                Scores.Add("Fear", (int)rel.Fear);
                Scores.Add("Desire", (int)rel.Desire);

                if (!isgeneric)
                {
                    List<string> relName = new List<string>();
                    if (rel.Relationship_Bio != null)
                    {
                        var name = rel.Relationship_Bio.GetDisplayName(rel.Owner, !rel.isA_Bio);
                        if (name.Length > 0)
                        {
                            relName.Add($"{name}");
                        }
                    }
                    foreach (var key in rel.Relationship_Social_Keys)
                    {
                        if (rel.tryGetSocialFaction(key, out var rel2, out var isA))
                        {
                            var name = rel2.GetDisplayName(rel.Owner, !isA);
                            if (name.Length > 0)
                            {
                                relName.Add($"{name}");
                            }
                        }
                    }
                    if (rel.Relationship_Personal != null)
                    {
                        var name = rel.Relationship_Personal.GetDisplayName(rel.Owner, !rel.isA_Personal);
                        if (name.Length > 0)
                        {
                            relName.Add(name);
                        }
                    }

                    CurrentAttitude = $"{rel.Owner.GetCurrentAttitude()?.DisplayName ?? ""}";

                    CurrentRelationships = rel.relationText.Replace("$name$", $"{rel.TargetName}" + (rel.Target.isTemporaryActor && rel.Target.Title.Length > 0 ? $"({rel.Target.Title})" : "")).Replace("$relation$", relName.Count > 0 ? String.Join(",", relName) : "no relation");
                }
            }
        }

        public class MemoryStorage
        {
            public string timestamp;
            public string summary;
            public List<string> details = new List<string>();
            public string memoryEffects;

            public MemoryStorage()
            {

            }
            public MemoryStorage(Memory_Entry mem)
            {
                timestamp = $"{mem.FinalEndTime.ToString("MM/dd")}, {mem.PrintShortTimeStartToEnd}";
                summary = mem.ToString();
                details = new List<string>(mem.MemInstanceDescriptions);
                memoryEffects = $"Statmod: Acceptance check{mem.CachedScore.ToString("+0;-#")} Mood{mem.MoodSum} Stress{mem.StressSum} Lust{mem.LustSum}";
            }
        }

        public CharaStorage()
        {

        }
        public CharaStorage(Character_Trainable c, I_IsJobGiver faction, bool fullLoad = false)
        {
            FirstName = c.FirstName;

            int nextHour = scr_System_Time.current.getCurrentTime().Hour + 1;
            if (nextHour >= 24) nextHour -= 24;
            var nextHourJob = c.FactionManager.CurrentJobPost(nextHour);
            var nextHourFaction = c.FactionManager.CurrentJobScheduleFaction(nextHour);

            RefID = c.RefID;
            bool isPlayer = scr_System_CampaignManager.current.IsPlayer(c);
            // Player can hold standing in multiple factions at once (home + work factions), unlike
            // NPCs whose sandbox behavior only ever depends on their single CurrentlyActiveFaction -
            // list all of them so the LLM knows about roles the player isn't currently active in.
            string factionStatus = isPlayer
                ? String.Join(", ", c.FactionManager.Factions.Where(f => f != null).Select(f => $"{f.FactionDisplayName}: {f.GetCharaSocialStandingName(c.RefID)}"))
                : c.FactionManager.CurrentlyActiveFactionStatus;
            Description = $"{c.Race.DisplayName} {c.RaceTemplate.DisplayName} {factionStatus}";
            if (isPlayer) Description += ", IS PLAYER CHARACTER";
            CurrentlyDoing = c.GetJobDescription();
            var room = scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID);
            if (room != null) CurrentLocation = $"{(room.parentFloor != null ? $"{room.parentFloor.displayName}, " : "" )}{room.DisplayName}";
            NextHourPlan = ((nextHourJob == null || nextHourJob.Name == "") ? LocalizeDictionary.QueryThenParse("chara_currentjob_free") : nextHourJob.Name + (nextHourFaction != null ? $"({nextHourFaction.FactionDisplayName})" : ""));

            if (fullLoad)
            {
                LorebookEntry = c.CharacterCard;
                Relationships = new Dictionary<string, RelationshipStorage>();
                equipments = new List<string>();
                //schedule = new Dictionary<string, string>();
                Status = new List<string>();
                Memories = new List<MemoryStorage>();

                if (c.Memory.Entries != null)
                {
                    foreach (var i in c.Memory.Entries)
                    {
                        var newmm = new MemoryStorage(i);
                        Memories.Add(newmm);
                    }
                }
                foreach (var i in c.Relationships.Relationships) Relationships.Add($"attitude toward {i.Target.FirstName}", new RelationshipStorage(i));
                foreach (var i in c.Relationships.GenericRelationship) Relationships.Add($"attitude towards {LocalizeDictionary.QueryThenParse( i.Key)}", new RelationshipStorage(i.Value, true));
            
                if (c.Stats != null)
                {
                    if (c.Stats.Mood != null) Status.Add(c.Stats.Mood.SeverityDisplayName);
                    if (c.Stats.Stress != null) Status.Add(c.Stats.Stress.SeverityDisplayName);
                    if (c.Stats.Lust != null) Status.Add(c.Stats.Lust.SeverityDisplayName);
                    foreach(var status in c.Stats.statusInstancesEx)
                    {
                        if (status.BaseRef.noDisplay) continue;
                        if (!status.Displayable) continue;
                        Status.Add(status.SeverityDisplayName);
                    }
                    foreach (var status in c.Stats.StatusInstances)
                    {
                        if (status.BaseRef.noDisplay) continue;
                        if (!status.Displayable) continue; 
                        Status.Add(status.SeverityDisplayName);
                    }
                }

                foreach(var equipref in c.Body.EquippedItemRefs)
                {
                    var equip = scr_System_CampaignManager.current.FindItemInstanceByID(equipref);
                    var equiptooltip = equip.Base.Tooltip == "no_tooltip" ? "" : $": {equip.Base.Tooltip}";
                    equipments.Add($"{equip.DisplayName}{equiptooltip}");
                }
                foreach(var kwd in c.Body.BodyDescription)
                {
                    equipments.Add(kwd);
                }

                /*
                for(int i = 0; i < 24; i++)
                {
                    var name = c.GetJobPost(i).Name;
                    if(name != "") schedule.Add($"{i}H", name);
                }*/

                if (c.PortraitManager != null)
                {
                    c.PortraitManager.CollectAllTags(ValidPortraitTags, ValidPortraitTags_target);
                }
            
            }
        }
    }

    public Dictionary<string, List<string>> FloorDescriptions = new Dictionary<string, List<string>>();// <floorName, <roomRefID, roomDescription>> with each room name and present chara;
    public string CurrentRoomInfo = null;
    public Dictionary<string, string> Lorebook = new Dictionary<string, string>();
    public Dictionary<string, CharaStorage> Characters = new Dictionary<string, CharaStorage>(); // <refID, description>
    public Dictionary<string, Dictionary<string, Dictionary<string, SerializedAP>>> PossibleInteractions = new Dictionary<string, Dictionary<string, Dictionary<string, SerializedAP>>>(); // <targetName, <commandID, tooltips>>

    public LLM_WorldState()
    {
        //bool isdebug = scr_System_CampaignManager.current.DebugMode;
        //if (isdebug) scr_System_CampaignManager.current.DebugMode = false;

        var currentRoom = scr_System_CampaignManager.current.CurrentRoom;
        var faction = currentRoom == null ? null : currentRoom.FactionOwner;

        if (faction != null)
        {
            foreach(var floor in faction.ManagedFloors)
            {
                var dic = new List<string>();
                foreach(var room in floor.rooms)
                {
                    if (!dic.Contains(room.DisplayName)) dic.Add(room.DisplayName);

                    if (room == currentRoom)
                    {
                        var names = new List<string>();
                        foreach (var i in room.RoomChara) names.Add(i.FirstName);

                        List<string> aps = new List<string>();
                        foreach (var ap in scr_System_CampaignManager.current.GetRegisteredAPByRoom(room.RefID, false))
                        {
                            if (ap.job.isPlayerRelatedJob) continue;
                            if (ap.isTemporaryAP) continue;
                            aps.Add(ap.DescriptionText());
                        }
                        CurrentRoomInfo = $"[{room.DisplayName}]\nRoomInfo:[{room.DisplayableFurnitureNames}]\nRoom Cleanliness: {room.RoomCleanliness()}\nRoom Items:{(room.Inventory.Contents.Count > 0 ? $"[\n{room.Inventory.PrintContent()}]" : "no item")}\nChara in room:[{String.Join(", ", names)}]\nOngoing command in room:[{(aps.Count > 0 ? String.Join("\n", aps) : "no ongoing")}]";

                        if (room.parentFloor != null && room.parentFloor.MapTemplate != null)
                        {
                            foreach (var kvp in room.parentFloor.MapTemplate.Lorebooks)
                            {
                                Lorebook.Add(kvp.Key, kvp.Value);
                            }
                        }
                    }
                }
                FloorDescriptions.Add(floor.displayName, dic);
            }


            foreach(var c in faction.ManagedChara)
            {

               if (currentRoom.RoomChara.Contains(c))
                {// more detailed desc
                    Characters.Add(c.FullName, new CharaStorage(c, faction, true));
                }
                else
                {
                    Characters.Add(c.FullName, new CharaStorage(c, faction, false));
                }

            }
        }

        // A character can be physically present in the current room without being a managed member of
        // its owning faction (e.g. an unaffiliated guest/visitor NPC) - the loop above only covers
        // faction.ManagedChara, so anyone else in the room was otherwise visible only by first name in
        // CurrentRoomInfo's "Chara in room" list, with no RefID anywhere in world info at all. Serialize
        // everyone physically in the room regardless of faction ownership. Indexer assignment since
        // faction.ManagedChara may already have added some of them above.
        if (currentRoom != null)
        {
            foreach (var c in currentRoom.RoomChara)
            {
                if (!Characters.ContainsKey(c.FullName)) Characters[c.FullName] = new CharaStorage(c, faction, true);
            }
        }

        // collect world info
        if (scr_System_CampaignManager.current.CurrentCampaign != null)
        {
            Lorebook.Add($"Current Campaign: [{scr_System_CampaignManager.current.CurrentCampaign.DisplayName}]",$"\nCampaign Info:[\n {scr_System_CampaignManager.current.CurrentCampaign.Tooltip}\n]");

            foreach (var kvp in scr_System_CampaignManager.current.CurrentCampaign.Lorebooks)
            {
                Lorebook.Add(kvp.Key, kvp.Value);
            }
        }

        //List<string> relationshipTypes = new List<string>();
        //foreach(var i in scr_System_Serializer.current.MasterList.RelationshipTypes.list_personal)
        //{
        //    relationshipTypes.Add($"{i.DisplayName}: {i.Tooltip}");
        //}
        //Lorebook.Add($"All personal relationship types",$"[{String.Join("\n", relationshipTypes)}]");

        var currentTime = scr_System_Time.current.getCurrentTime();
        string dayofWeek = LocalizeDictionary.QueryThenParse("ui_calendar_dayOfWeek_" + currentTime.DayOfWeek);
        Lorebook.Add("Current World Time Hour", $"{currentTime.ToShortDateString()}, {currentTime.ToShortTimeString()}, {dayofWeek}");


        var startTime = scr_System_Time.current.getStartTime();
        var dayCount = currentTime - startTime;
        Lorebook.Add("Time Since Campaign Start", $"{currentTime.Year - startTime.Year} year, {dayCount.Days + 1} days");
        Lorebook.Add("isTimeStopped", $"{scr_System_Time.current.TimeStop}");

        LLMUtils.CollectCOMInfo(PossibleInteractions, currentRoom);

      //  if (isdebug) scr_System_CampaignManager.current.DebugMode = true;
    }

}


public static class LLMUtils
{

    public static Regex regex_JSONWrapper = new Regex(@"```json(?<jsonContent>.*?)```", RegexOptions.Singleline);

    static readonly Regex regex_comment = new Regex(@"\{\{//.*?\}\}", RegexOptions.Singleline);
    static readonly Regex regex_setvar = new Regex(@"\{\{setvar::(?<name>[a-zA-Z0-9_]+)::(?<value>.*?)\}\}", RegexOptions.Singleline);
    static readonly Regex regex_getvar = new Regex(@"\{\{getvar::(?<name>[a-zA-Z0-9_]+)\}\}", RegexOptions.Singleline);

    /// <summary>
    /// Strips {{//comment}} macros, applies {{setvar::name::value}} (mutating vars and
    /// removing itself from the text), then substitutes {{getvar::name}} using the
    /// now-current vars state. vars is threaded across nodes by the caller so setvar
    /// effects from earlier nodes are visible to getvar reads in later ones.
    /// </summary>
    public static string ApplyMacros(string content, Dictionary<string, string> vars)
    {
        if (string.IsNullOrEmpty(content)) return content;

        content = regex_comment.Replace(content, "");

        content = regex_setvar.Replace(content, m =>
        {
            if (vars != null) vars[m.Groups["name"].Value] = m.Groups["value"].Value;
            return "";
        });

        content = regex_getvar.Replace(content, m =>
        {
            if (vars == null) return "";
            return vars.TryGetValue(m.Groups["name"].Value, out var v) ? v : "";
        });

        return content;
    }

    static void AddChild(ActionPackage ap, SerializedAP child, Dictionary<string, SerializedAP> tooltips)
    {
        if (child == null) return;
        child.SourceJobID = null;
        child.Summary = null;
        child.TimeCost = null;
        if (child.AcceptanceRate != null) child.AcceptanceCheck = null;
        child.Doers = null;
        child.Receivers = null;

        if (tooltips.TryGetValue(ap.targetCOM.ParentCOM.DisplayName(), out var parentAP))
        {
            parentAP.CommandID = null;
            parentAP.AcceptanceRate = null;
            parentAP.AcceptanceCheck = null;
            if (parentAP.variants == null) parentAP.variants = new List<SerializedAP>();
            parentAP.variants.Add(child);
        }
    }

    static void validateSingle(Job job, List<int> doer, List<int> receiver, HashSet<Job> verified, Dictionary<string, Dictionary<string, SerializedAP>> collection, HashSet<string> repeat )
    {
        if (verified != null)
        {
            if (verified.Contains(job))
            {
               // collection.Add($"{job.DisplayName} {job.RefID} verified", new List<SerializedAP>());
                return;
            }
            verified.Add(job);
        }

        if (job is Job_Furniture)
        {
            if (repeat.Contains(job.DisplayName))
            {
              //  collection.Add($"{job.DisplayName} {job.RefID} repeat", new List<SerializedAP>());
                return;
            }
            repeat.Add(job.DisplayName);
        }

        var chara = scr_System_CampaignManager.current.FindInstanceByID(doer[0]);
        Dictionary<string, SerializedAP> tooltips = new Dictionary<string, SerializedAP>();

        /*
        foreach (var ap in (job is Job_Furniture ? job.MakePackages(chara, true, false, true) : job.CachedPackages))
        { 
            var app = validateAP(ap, doer, receiver);
            if (app != null) tooltips.Add(app.CommandID, app);
        }*/

        if (job is Job_Furniture)
        {
            foreach (var ap in job.MakePackages(chara, true, false, true))
            {
                var app = validateAP(ap, doer, receiver);
                if (app != null)
                {
                    if (ap.targetCOM.childCOMs.Count > 0)
                    {
                        tooltips.Add(ap.targetCOM.DisplayName(), app);
                        app.CommandName = null;
                    }
                    else tooltips.Add(ap.DescriptionText(chara.RefID, false), app);
                }
            }

            foreach (var ap in job.MakePackages(chara, false, true, true))
            {
                if (ap.targetCOM.ParentCOM == null) continue;
                var app = validateAP(ap, doer, receiver);
                AddChild(ap, app, tooltips);
            }
        }
        else
        {
            foreach (var ap in job.CachedPackages)
            {
                if (ap.targetCOM != null && ap.targetCOM.ParentCOM != null) continue;
                var app = validateAP(ap, doer, receiver);
                if (app != null)
                {
                    if (ap.targetCOM.childCOMs.Count > 0)
                    {
                        if (tooltips.TryAdd(ap.targetCOM.DisplayName(), app)) app.CommandName = null;
                    }
                    else tooltips.TryAdd(ap.DescriptionText(chara.RefID, false), app);
                }
            }
            foreach (var ap in job.CachedPackages)
            {
                if (ap.targetCOM == null || ap.targetCOM.ParentCOM == null) continue;
                var app = validateAP(ap, doer, receiver);
                AddChild(ap, app, tooltips);
            }
        }

        if (tooltips.Count > 0) collection.Add($"{job.DisplayName}", tooltips);
       // else collection.Add($"{job.DisplayName}, no valid aps", new List<SerializedAP>());
    }

    static void validateExisting(Job_Furniture job, Dictionary<string, Dictionary<string, SerializedAP>> collection)
    {
        if (job == null) return;
        var tooltips = new Dictionary<string, SerializedAP>();
        foreach (var ap in job.MakePackagesJoinable(scr_System_CampaignManager.current.Player))
        {
            var app = validateAP(ap, null, null);
            if (app != null) tooltips.Add(app.CommandID, app);
        }

        if (tooltips.Count > 0) collection.Add($"{job.DisplayName}", tooltips);
    }


    static SerializedAP validateAP(ActionPackage ap, List<int> doer, List<int> receiver)
    {
        if (ap.targetCOM == null) return null;

        var app = new SerializedAP();
        app.CommandName = ap.DisplayName;
        app.CommandID = ap.targetCOM == null ? "null" : ap.targetCOM.ID;
        app.SourceJobID = ap.job.RefID;

        app.TimeCost = ap.targetCOM == null ? 1 : ap.targetCOM.TimeScale;

        if (!ap.targetCOM.ValidateJob(ap.job, out var msg))
        {
            // add message
            app.Summary = ap.GetTooltips($"validatejob fail: {msg}");
           // tooltips.Add();
            return app;
        }


        if (doer != null && receiver != null) ap.ResetRequest(doer, receiver, doer.Count > 0 ? doer[0] : -1, true);
        if (!ap.Validate())
        {
            if (ap.COMVariantID < -1) return null;
            // validation failure
            ap.tooltip.RemoveAll(x => x == "" || x.Length < 1);
            app.Summary = ap.GetTooltips(LocalizeDictionary.QueryThenParse("ui_ap_onHoverTooltip_comInvalid")).Replace("$tooltips$", String.Join("\n", ap.tooltip));
            return null;

        }
        else if (ap.ComTags.Contains("sleep") && !scr_System_CampaignManager.current.Player.shouldSleep && !scr_System_CampaignManager.current.DebugMode)
        {
            app.Summary = ap.GetTooltips(LocalizeDictionary.QueryThenParse("ui_ap_onHoverTooltip_comInvalid")).Replace("$tooltips$", LocalizeDictionary.QueryThenParse("ui_ap_onHoverTooltip_cannotSleep"));
        }
        else
        {
            ap.GetSerializedAPData(app);
            //ap.get
            //var prevalidation = ap.GetSuccessRatePrevalidationString(false);
            ap.CollectMods(out var dcMods, out var bonus, out var baseDC);
            string dcResult = "";
            if (baseDC > 0)
            {
                List<string> mods = dcMods == null ? new List<string>() : dcMods.GetAllModifiers(false);
                dcResult = $"Difficulty Check D20{(mods.Count > 0 ? $" + {String.Join(" + ", mods)}" : "")} >=? {baseDC}";
            }

            //tooltips.Add($"{ap.DisplayName}: [{ap.GetTooltips(LocalizeDictionary.QueryThenParse("ui_ap_onHoverTooltip"))}{(prevalidation.Length > 0 ? $"\n{prevalidation}" : "")}{(dcResult.Length > 0 ? $"\n{dcResult}" : "")}\n]");

            //app.Summary = ap.GetTooltips(LocalizeDictionary.QueryThenParse("ui_ap_onHoverTooltip")+$"\n{String.Join("\n", ap.tooltip)}");

            if (app.AcceptanceRate != null)
            {
                app.AcceptanceCheck = null;
            }
            else if (app.AcceptanceCheck != null)
            {
                if (app.AcceptanceCheck.Length > 0) app.AcceptanceCheck = RegexStrip(app.AcceptanceCheck);
                else app.AcceptanceCheck = null;
            }


            if (app.AcceptanceMods != null)
            {
                if (app.AcceptanceMods.Length > 0) app.AcceptanceMods = RegexStrip(app.AcceptanceMods);
                else app.AcceptanceMods = null;
            }

            if (dcResult.Length > 0) app.DifficultyCheck = dcResult;
            else app.DifficultyCheck = null;
        }

        return app;
    }


    static string RegexStrip(string s)
    {
        return System.Text.RegularExpressions.Regex.Replace(s, @"<link=[^>]+>(.*?)<\/link>", "$1");
    }

    public class SerializedAP
    { 
        public string CommandName;
        public string CommandID;
        public int? SourceJobID;
        public string Summary;
        public int? TimeCost;
        public string AcceptanceCheck;
        public string AcceptanceRate;
        public string AcceptanceMods;
        public string DifficultyCheck;
        public string Doers;
        public string Receivers;

        public List<SerializedAP> variants = null;

    }

    /// <summary>
    /// Only collect player relevant info. do not check for npc-npc.
    /// </summary>
    /// <param name="targets"></param>
    /// <returns></returns>
    public static void CollectCOMInfo(Dictionary<string, Dictionary<string, Dictionary<string, SerializedAP>>> PossibleInteractions, Room_Instance currentRoom)
    {
        var mgr = scr_System_CampaignManager.current;
        if (mgr == null) return;
        List<Character_Trainable> targets = currentRoom == null ? new List<Character_Trainable>() : currentRoom.RoomChara;
        var trackedJobs = new HashSet<Job>();

        Dictionary<string, Dictionary<string, SerializedAP>> collection = new Dictionary<string, Dictionary<string, SerializedAP>> ();

        var player = new List<int>(1);
        if (mgr.Player != null)
        {
            player.Add(mgr.Player.RefID);
        }
        var target = new List<int>(1);


        // player info!!!!

        // player com
        var playerCOM = mgr.FindJobInstanceByID(mgr.jobRef_playerCOM);


        // current job
        var curr = mgr.Player.CurrentJob;
        if (curr != null && !curr.CanBeInterrupted) trackedJobs.Add(curr);

        if (!scr_System_CampaignManager.current.displaySex)
        {
            // current room jobs
            foreach (var job in mgr.CurrentRoom.Jobs)
            {
                if (job is Job_Furniture) validateExisting(job as Job_Furniture, collection);
                else continue;
                //validateExisting(job, collection);
            }
            if (collection.Count > 0)
            {
                PossibleInteractions.Add("Ongoing Commands in room:", new Dictionary<string, Dictionary<string, SerializedAP>>(collection));
            }
            collection.Clear();
        }


        HashSet<string> duplicateCheck = new HashSet<string>();

        // target jobs
        if (targets != null && targets.Count > 1 && player.Count > 0)
        {
            foreach (var r in targets)
            {
                if (r == null) continue;
                if (r == mgr.Player) continue;

                collection.Clear();
                trackedJobs.Clear();
                duplicateCheck.Clear();

                target.Clear();
                target.Add(r.RefID);

                if (r.InteractionJob != null)
                {   // player interacting with target
                    validateSingle(r.InteractionJob, player, target, trackedJobs, collection, duplicateCheck);
                }

                if (curr != null)
                {
                    validateSingle(curr, player, target, trackedJobs, collection, duplicateCheck);
                }

                if (curr != null && curr is Job_Sex_Group)
                {
                    //
                }
                else
                {

                    if (playerCOM != null)
                    {   // check npc's acceptance of playercom
                        validateSingle(playerCOM, player, target, trackedJobs, collection, duplicateCheck);
                    }

                    if (currentRoom != null && currentRoom.Jobs != null)
                    {
                        foreach (var j in currentRoom.Jobs)
                        {
                            validateSingle(j, player, target, trackedJobs, collection, duplicateCheck);
                        }
                    }

                }

                if (collection.Count > 0)
                {
                    PossibleInteractions.Add($"Possible command with {r.FirstName}", new Dictionary<string, Dictionary<string, SerializedAP>>(collection));
                }
            }
        }

        if (player.Count > 0)
        {
            collection.Clear();
            target.Clear();
            trackedJobs.Clear();
            duplicateCheck.Clear();

            if (curr == null || curr.CanBeInterrupted)
            {
                if (playerCOM != null)
                {   // check npc's acceptance of playercom
                    validateSingle(playerCOM, player, target, trackedJobs, collection, duplicateCheck);
                }

                if (currentRoom != null && currentRoom.Jobs != null)
                {
                    foreach (var j in currentRoom.Jobs)
                    {
                        validateSingle(j, player, target, trackedJobs, collection, duplicateCheck);
                    }
                }
            }
            
            if (curr != null)
            {
                validateSingle(curr, player, target, trackedJobs, collection, duplicateCheck);
            }

            if (collection.Count > 0)
            {
                PossibleInteractions.Add($"Possible command alone", new Dictionary<string, Dictionary<string, SerializedAP>>(collection));
            }
        }
    }
}

