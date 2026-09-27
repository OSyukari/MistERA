using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using UnityEngine;

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

    List<string> _portraitTags_Self = null;
    List<string> _portraitTags_Target = null;
    /// <summary>
    /// In-game clock time ("HH:mm") this block's content takes place, LLM-provided per the schema.
    /// Copied by the model from its context's time anchors (worldInfo current time, the [HH:mm]
    /// prefixes on tool-result/batch-report messages) rather than invented. Read by
    /// scr_panel_logs.DrawBlockHeader for the block header label and its cumulative
    /// "every captured game message with time <= this block's time" tooltip. Null/empty/garbage on
    /// old or nonconforming responses - consumers must TryParseClock it.
    /// </summary>
    public string time = null;

    /// <summary>
    /// Execution-data link, resolved by LLMAgentSession.AttachExecutionData at finalize time
    /// (agent mode only): the execution_callback_id echoed in an execute_actions tool result,
    /// attached by the model to the block narrating that execution (-1/omitted = not tied to any
    /// execution). Not a timestamp - an opaque session-scoped id; the in-game clock can stall or
    /// collide across executions, so only the id is a valid join key. Once resolved, the record's
    /// self tags are MERGED into portraitTags and executionTargetTags holds the record's partner
    /// tags, so both getters below skip their live PortraitManager fallbacks.
    /// </summary>
    public int execution_callback_id = -1;

    /// <summary>
    /// True once an execution record's tags were merged into this block (see AttachExecutionData) -
    /// suppresses the live-tag fallback in SelfPortraitTag/TargetPortraitTag, since the merged tags
    /// describe the moment the linked actions executed, not display time.
    /// </summary>
    [JsonIgnore]
    public bool executionDataResolved = false;

    /// <summary>Partner (target) tags captured at execution time by AttachExecutionData; null on
    /// unlinked blocks and old responses (live fallback applies).</summary>
    [JsonIgnore]
    public List<string> executionTargetTags = null;


    [JsonIgnore]
    public List<string> SelfPortraitTag {
        get {
            if (portraitRefID == -1) return portraitTags;
            if (_portraitTags_Self == null)
            {
                // execution-resolved blocks already carry the record's action tags inside
                // portraitTags (merged by AttachExecutionData) - only unresolved ones add the
                // live lookups below
                _portraitTags_Self = new List<string>(portraitTags);
                if (!executionDataResolved)
                {
                    var c = scr_System_CampaignManager.current.FindInstanceByID(portraitRefID);
                    if (c != null && c.PortraitManager != null)
                    {
                        _portraitTags_Self.AddRange( c.PortraitManager.GetOwnerActionTagsByPriority());
                    }
                }
            }
            return _portraitTags_Self;
        } }
    [JsonIgnore]
    public List<string> TargetPortraitTag {
        get
        {
            if (portraitRefID == -1) return new List<string>();
            if (_portraitTags_Target == null)
            {
                _portraitTags_Target = new List<string>();
                if (executionTargetTags != null)
                {
                    _portraitTags_Target.AddRange(executionTargetTags);
                }
                else
                {
                    var c = scr_System_CampaignManager.current.FindInstanceByID(portraitRefID);
                    if (c != null && c.PortraitManager != null)
                    {
                        _portraitTags_Target.AddRange(c.PortraitManager.GetOwnerActionTargetTagsByPriority());
                    }
                }
            }
            return _portraitTags_Target;
        } }
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

    /// <summary>
    /// Agent mode sets this: a None command_result stays None so the game performs the real
    /// acceptance/DC rolls. Single-shot mode leaves it false - the model narrated the outcome up
    /// front, so an unset result is defaulted to Accept. Must be set before the first
    /// GetActionPackages call (parsing mutates the APJSONs in place), and carried over by
    /// scr_System_CampaignManager.BuildAPBatch.
    /// </summary>
    [JsonIgnore] public bool gameRollsUnsetResults = false;

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

                if (!gameRollsUnsetResults && tempAP.command_result == Memory_Response.None) tempAP.command_result = Memory_Response.Accept;

                var job = scr_System_CampaignManager.current.FindJobInstanceByID(tempAP.SourceJobID);
                if (job == null) continue;

                var doers = new List<int>();
                if (tempAP.doer_RefID != -1) doers.Add(tempAP.doer_RefID);

                var receivers = new List<int>();
                if (tempAP.receiver_RefID != -1) receivers.Add(tempAP.receiver_RefID);

                // -1 (default/inactive) keeps the original behavior: the doer is their own master.
                // A master_RefID that differs from the doer marks the action as ordered by that
                // master rather than the doer's own intent.
                var masterRef = tempAP.master_RefID >= 0 ? tempAP.master_RefID : tempAP.doer_RefID;

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
    public int master_RefID = -1;
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
        if (ap.targetCOM == null || ap.targetCOM.ParentCOM == null) return;
        AddChild(ap.targetCOM.ParentCOM.DisplayName(), child, tooltips);
    }

    /// <summary>
    /// Attaches a validated child AP as a variant under its parent entry's key (the parent COM's
    /// display name). Keyed overload also serves generator COMs whose per-item packages keep the
    /// generator COM itself as targetCOM (ParentCOM == null) - see validateJob.
    /// </summary>
    static void AddChild(string parentKey, SerializedAP child, Dictionary<string, SerializedAP> tooltips)
    {
        if (child == null) return;
        child.SourceJobID = null;
        //child.Summary = null;
        child.TimeCost = null;
        if (child.AcceptanceRate != null) child.AcceptanceCheck = null;
        child.Doers = null;
        child.Receivers = null;

        if (tooltips.TryGetValue(parentKey, out var parentAP))
        {
            parentAP.CommandID = null;
            parentAP.AcceptanceRate = null;
            parentAP.AcceptanceCheck = null;
            if (parentAP.variants == null) parentAP.variants = new List<SerializedAP>();
            parentAP.variants.Add(child);
        }
    }

    static void validateSingle(Job job, List<int> doer, List<int> receiver, HashSet<Job> verified, Dictionary<string, Dictionary<string, SerializedAP>> collection, HashSet<string> repeat, int masterRef = -1, bool showInvalidAP = false)
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

        var tooltips = validateJob(job, doer, receiver, masterRef, showInvalidAP);
        // distinct jobs can share a display name (e.g. two participants on two same-named furnitures)
        if (tooltips.Count > 0 && !collection.TryAdd(job.DisplayName, tooltips)) collection.TryAdd($"{job.DisplayName} (jobRefID {job.RefID})", tooltips);
    }

    /// <summary>
    /// The shared validateSingle/validateFurniture body: enumerates a job's ActionPackages and
    /// validates each against the doer/receiver/master combo, keyed by command description text.
    /// Folder-parent COMs (childCOMs or a GenerateAP/GenerateCOM item generator) get one entry
    /// keyed by display name with their children attached as variants, enumerated per parent via
    /// MakePackages(chara, false, true, true, parentCOM) - the same recipe as the UI's
    /// LoadChildCOMPanel - so static childCOMs, runtime item-generated child COMs and per-instance
    /// GenerateAP packages all show up, invalid ones included (with their reason in AcceptanceRate).
    /// </summary>
    static Dictionary<string, SerializedAP> validateJob(Job job, List<int> doer, List<int> receiver, int masterRef, bool showInvalidAP = false)
    {
        // chara only drives the job's COM filtering; the packages themselves are constructed with the
        // full doer/receiver/master set - the same way execute_actions builds them (COM.MakePackage),
        // with the same defaults: master falls back to the first doer, the player is never both
        // doer and receiver, and -1 is not a receiver.
        var chara = scr_System_CampaignManager.current.FindInstanceByID(doer[0]);
        var doers = new List<int>(doer);
        var receivers = receiver == null ? new List<int>() : new List<int>(receiver);
        receivers.RemoveAll(r => r == -1 || (r == 0 && doers.Contains(0)));
        var master = masterRef >= 0 ? masterRef : doers[0];

        Dictionary<string, SerializedAP> tooltips = new Dictionary<string, SerializedAP>();
        List<COM> parentCOMs = new List<COM>();

        foreach (var ap in job.MakePackages(chara, doers, receivers, master, true, false, true))
        {
            var app = validateAP(ap, showInvalidAP);
            if (app == null) continue;

            if (ap.targetCOM.childCOMs.Count > 0 || ap.targetCOM.GenerateAP != null)
            {
                if (tooltips.TryAdd(ap.targetCOM.DisplayName(), app))
                {
                    app.CommandName = null;
                    parentCOMs.Add(ap.targetCOM);
                }
            }
            else tooltips.TryAdd(ap.DisplayName, app);
        }

        // orphan children: a child whose parent folder isn't in this job's COM list (e.g. Job_Sex_Group
        // excludes its folder parents) would otherwise never be reached - list it top-level, UI style
        foreach (var com in job.allusableCOMs)
        {
            if (com.ParentCOM == null || com.isHiddenChild || job.allusableCOMs.Contains(com.ParentCOM)) continue;
            foreach (var ap in job.MakePackages(chara, doers, receivers, master, false, true, true, com))
            {
                var app = validateAP(ap, showInvalidAP);
                if (app != null) tooltips.TryAdd(ap.DisplayName, app);
            }
        }

        // children per parent, LoadChildCOMPanel style: the filter keeps the parent's static
        // childCOMs plus the parent itself (whose GenerateAP branch spawns one package per matching
        // inventory item); allowInvalid keeps currently-invalid children visible with their reason.
        foreach (var parentCOM in parentCOMs)
        {
            bool anyChild = false;
            foreach (var ap in job.MakePackages(chara, doers, receivers, master, false, true, true, parentCOM))
            {
                var app = validateAP(ap, showInvalidAP);
                if (app == null) continue;
                AddChild(parentCOM.DisplayName(), app, tooltips);
                anyChild = true;
            }
            // a folder is never executable itself - with no child left to show it's just noise
            // (e.g. item generators like ingestItem with no matching item in reach)
            if (!anyChild) tooltips.Remove(parentCOM.DisplayName());
        }

        return tooltips;
    }

    /// <summary>
    /// Furniture variant of validateSingle that merges into a flat command-keyed dictionary instead
    /// of a per-furniture nested one: identical commands offered by multiple furnitures in the room
    /// collapse into a single entry whose PossibleJobSources lists every furniture job offering it
    /// (SourceJobID is dropped from the merged entry).
    /// </summary>
    static void validateFurniture(Job job, List<int> doer, List<int> receiver, int masterRef, Dictionary<string, SerializedAP> merged, HashSet<Job> verified, HashSet<string> repeat, bool showInvalidAP = false)
    {
        if (verified != null)
        {
            if (verified.Contains(job)) return;
            verified.Add(job);
        }

        if (job is Job_Furniture)
        {
            if (repeat.Contains(job.DisplayName)) return;
            repeat.Add(job.DisplayName);
        }

        var tooltips = validateJob(job, doer, receiver, masterRef, showInvalidAP);
        foreach (var kvp in tooltips)
        {
            if (merged.TryGetValue(kvp.Key, out var existing))
            {
                existing.PossibleJobSources.Add(new SerializedAP.JobSource(job));
            }
            else
            {
                var app = kvp.Value;
                app.SourceJobID = null;
                app.PossibleJobSources = new List<SerializedAP.JobSource>() { new SerializedAP.JobSource(job) };
                merged.Add(kvp.Key, app);
            }
        }
    }

    static void validateExisting(Job_Furniture job, Dictionary<string, Dictionary<string, SerializedAP>> collection)
    {
        if (job == null) return;
        var tooltips = new Dictionary<string, SerializedAP>();
        foreach (var ap in job.MakePackagesJoinable(scr_System_CampaignManager.current.Player))
        {
            var app = validateAP(ap);
            if (app != null) tooltips.TryAdd(app.CommandID, app);
        }

        if (tooltips.Count > 0) collection.Add($"{job.DisplayName}", tooltips);
    }

    /// <summary>
    /// Serializes an already-constructed package (actors must be set by whoever built it - see
    /// validateJob / Job.MakePackages(c, doers, receivers, master, ...)). Never re-targets the package,
    /// so live packages (joinables) are read without being mutated.
    /// </summary>
    static SerializedAP validateAP(ActionPackage ap, bool showInvalid = false)
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
            app.AcceptanceRate = ap.GetTooltips($"validatejob fail: {msg}");
           // tooltips.Add();
            return app;
        }


        bool valid = ap.Validate();

        // sleep is NPC sandbox behavior - never offered to the LLM simulation, whatever its validity
        if (ap.targetCOM.comTags.Contains("sleep") || ap.ComTags.Contains("sleep"))
        {
            app.AcceptanceRate = "invalid: sleep is handled by NPC sandbox behavior and cannot be commanded by the LLM simulation";
            return app;
        }

        if (!valid)
        {
            if (ap.COMVariantID < -1) return null;
            if (!showInvalid) return null;
            if (ap.targetCOM != null && ap.targetCOM.HideWhenInvalid) return null;
            // validation failure - keep the command visible instead of hiding it: parent categories
            // with variants (e.g. service commands requiring undress) must not vanish wholesale.
            // The reason goes into AcceptanceRate because AddChild strips Summary from variants.
            ap.tooltip.RemoveAll(x => x == "" || x.Length < 1);
            //app.Summary = ap.GetTooltips(LocalizeDictionary.QueryThenParse("ui_ap_onHoverTooltip_comInvalid")).Replace("$tooltips$", String.Join("\n", ap.tooltip));
            var invalidReason = RegexStrip(String.Join("; ", ap.tooltip));
            app.AcceptanceRate = invalidReason.Length > 0 ? $"invalid: {invalidReason}" : "invalid";
            return app;
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
        public class JobSource
        {
            public string SourceJobName;
            public int SourceJobRefID;
            //public string notes;

            public JobSource()
            {

            }
            public JobSource(Job j)
            {
                this.SourceJobRefID = j.RefID;
                this.SourceJobName = j.DisplayName;
            }
        }

        public void AddJobSource(Job j)
        {
            if (PossibleJobSources != null)
            {
                PossibleJobSources.Add(new JobSource(j));
            }
            else if (SourceJobID != null && SourceJobID.Value != -1)
            {
                PossibleJobSources = new List<JobSource>();
                PossibleJobSources.Add(new JobSource( j));
                PossibleJobSources.Add(new JobSource(scr_System_CampaignManager.current.FindJobInstanceByID(SourceJobID.Value)));
                SourceJobID = null;
            }
            else
            {
                SourceJobID = j.RefID;
            }
        }

        public List<JobSource> PossibleJobSources = null;

        public string CommandName;
        public string CommandID;
        public int? SourceJobID;
        //public string Summary;
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
    /// Collects the possible commands for one specific PossibleInteractions context (strictly
    /// one-way doer -&gt; receiver, under info.Master's order). All participants must share one room.
    /// Fills info.possibleInteractions (job-name-keyed: the 1:1 receiver InteractionJob, playerCOM
    /// commands whenever the player is the sole doer, plus participants' current jobs) and
    /// info.FurnitureInteractions (flat command-keyed entries whose PossibleJobSources list every
    /// room furniture job offering the command, so identical commands from multiple furnitures
    /// merge into one entry).
    /// </summary>
    public static void CollectCOMInfo(LLM_WorldState.PossibleInteractions info)
    {
        var mgr = scr_System_CampaignManager.current;
        if (mgr == null || info == null || info.Doers == null || info.Doers.Count == 0) return;

        var player = mgr.Player;
        var masterRef = info.Master != null ? info.Master.RefID : -1;

        var receivers = new List<int>();
        if (info.Receivers != null)
        {
            foreach (var r in info.Receivers)
            {
                if (r != null) receivers.Add(r.RefID);
            }
        }

        Dictionary<string, Dictionary<string, SerializedAP>> collection = new Dictionary<string, Dictionary<string, SerializedAP>>();

        var trackedJobs = new HashSet<Job>();
        var trackedJobsLocked = new HashSet<Job>();
        HashSet<string> duplicateCheck = new HashSet<string>();

        // shared job-identity dedup across all validateSingle calls below - validateSingle ends with
        // collection.Add(job.DisplayName), so validating the same job twice (e.g. the receiver's
        // InteractionJob also being a participant's CurrentJob) would throw on a duplicate key
        var verifiedJobs = new HashSet<Job>();

        foreach (var i in info.Doers) if (i.CurrentJob != null) (i.CurrentJob.CanBeInterrupted ? trackedJobs : trackedJobsLocked).Add(i.CurrentJob);
        foreach (var i in info.Receivers) if (i.CurrentJob != null) (i.CurrentJob.CanBeInterrupted ? trackedJobs : trackedJobsLocked).Add(i.CurrentJob);

        Room_Instance room = null;
        bool roomError = false;
        List<string> roomLocs = new List<string>();

        foreach (var i in info.Doers)
        {
            roomLocs.Add($"{i.FirstName} is in {i.CurrentRoom?.DisplayName}");
            if (room == null) room = i.CurrentRoom;
            else if (room != i.CurrentRoom) roomError = true;
        }
        foreach (var i in info.Receivers)
        {
            roomLocs.Add($"{i.FirstName} is in {i.CurrentRoom?.DisplayName}");
            if (room == null) room = i.CurrentRoom;
            else if (room != i.CurrentRoom) roomError = true;
        }

        if (roomError || room == null)
        {
            info.possibleInteractions.Add($"no valid interactions. Either room {room?.DisplayName} is null, actors are empty, or actors not in same room:\n{String.Join("\n", roomLocs)}", new Dictionary<string, SerializedAP>());
            return;
        }

        var receiverSingle = info.Receivers != null && info.Receivers.Count == 1 ? info.Receivers[0] : null;
        var doerSingle = info.Doers != null && info.Doers.Count == 1 ? info.Doers[0] : null;
        // player com only exists for the player doer
        var playerCOM = doerSingle != null && doerSingle == player ? mgr.FindJobInstanceByID(mgr.jobRef_playerCOM) : null;

        // -- Interaction Job, 1:1 only -- //
        if (doerSingle != null && receiverSingle != null)
        {
            var job = receiverSingle.InteractionJob;
            if (job != null) validateSingle(job, info.DoerRefs, info.ReceiverRefs, verifiedJobs, collection, duplicateCheck, masterRef, true);
        }
        else if (info.Receivers.Count > 0)
        {
            collection.Add($"cannot query for individual interaction job on [{String.Join(" ", info.ReceiverRefs)}], doer count must be 1 (currently {info.Doers.Count}) and receiver count must be 1 (currently {info.Receivers.Count})", new Dictionary<string, SerializedAP>());
        }

        // -- Player special commands: player as the sole doer, with any receivers (none = solo) -- //
        if (playerCOM != null)
        {   // with receivers this also checks the npcs' acceptance of the playercom
            validateSingle(playerCOM, info.DoerRefs, info.ReceiverRefs, verifiedJobs, collection, duplicateCheck);
        }
        // -- foreach tracked Jobs
        if (trackedJobsLocked.Count != 0)
        {
            // then locked only
            foreach(var curr in trackedJobsLocked) validateSingle(curr, info.DoerRefs, info.ReceiverRefs, verifiedJobs, collection, duplicateCheck, masterRef, true);

            info.FurnitureInteractions.Add("cannot interact with room furniture due to participant locked in special jobs", new SerializedAP() { AcceptanceRate = "cannot interact with room furniture due to participant locked in special jobs" });
        }
        else
        {
            // not locked
            foreach (var curr in trackedJobs) validateSingle(curr, info.DoerRefs, info.ReceiverRefs, verifiedJobs, collection, duplicateCheck, masterRef);

            if (room != null && room.Jobs != null)
            {
                foreach (var j in room.Jobs)
                {
                    validateFurniture(j, info.DoerRefs, info.ReceiverRefs, masterRef, info.FurnitureInteractions, verifiedJobs, duplicateCheck);
                }
            }
        }

        foreach (var kvp in collection)
        {
            info.possibleInteractions.TryAdd(kvp.Key, kvp.Value);
        }

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

