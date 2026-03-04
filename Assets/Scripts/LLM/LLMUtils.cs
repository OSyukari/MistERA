using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using static LLMMessage;
using static LLMUtils;

public class LLM_Setting
{
    public bool enabled = true;
    public class ChatCompletion
    {
        public int APIType = 0;
        public string modellist = "";
        public string endpoint = "";
        public string key = "";
        public string model = "";
    }

    public ChatCompletion chatCompletionModel = new ChatCompletion();
}




public class LLMRequest
{
    public List<string> prepend = null;
    public List<LLMMessage> messages = new List<LLMMessage>();
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


    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public double? top_p = 1.0; 
    
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public int? top_k = 40;

    public ResponseFormatter response_format = null;
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public ResponseFormatter_Claude output_config = null;

    public void LoadTemplate(LLMRequest req)
    {
        this.temperature = req.temperature;
        this.max_completion_tokens = req.max_completion_tokens;
        this.max_tokens = req.max_tokens;
        this.top_p = req.top_p;
        this.top_k = req.top_k;
        foreach(var message in req.messages)
        {
            var newm = new LLMMessage(message);
            messages.Add(newm);
        }
        if (this.response_format == null) this.response_format = req.response_format;
    }

    public void ReplaceString(string a, string b)
    {
        foreach(var message in this.messages)
        {
            message.content = message.content.Replace(a, b);
        }
    }
    public void ReplaceType(string a, string b)
    {
        foreach (var message in this.messages)
        {
            message.content = message.content.Replace(a, b);
        }
    }

    public void Purge()
    {
        currentString = null;
        prepend = null;
        if (this.response_format != null) this.response_format.Purge();


    }

    public LLMRequest() { }
    public LLMRequest(bool initialize)
    {
        this.response_format = new ResponseFormatter(initialize);
    }

    public class ResponseFormatter_Claude
    {
        public ResponseFormatter_Claude_2 format;

        public class ResponseFormatter_Claude_2
        {
            public string type = "json_schema";
            public LLMFormatSchema schema;
        }

        public ResponseFormatter_Claude(ResponseFormatter format)
        {
            this.format = new ResponseFormatter_Claude_2();
            this.format.schema = format.json_schema.schema;
        }

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

public class LLMMessage
{
    public string role;
    public string content;

    public LLMMessage() { }
    public LLMMessage(LLMMessage message)
    {
        this.role = message.role;
        this.content = message.content;
    }

    MessageJSON json = null;

    public MessageJSON json_serialized = null;

    public MessageJSON GetContent()
    {
        if (json != null) return json;

        try
        {
            json = JsonConvert.DeserializeObject<MessageJSON>(content);
            json_serialized = json;
        }
        catch(Exception e)
        {
            Debug.LogError($"error failed to deserialize MessageJSON object from [{content}]");
            json = new MessageJSON();
            json.content_string = Utility.WrapTextColor($"ERROR failed to deserialize MessageJSON object, error code [{content}]", scr_System_CentralControl.current.DisplaySetting.TextColor_conflict.Color);
            return json;
        }


        try
        {
            var json1 = JsonConvert.DeserializeObject<MessageJSON_blocks>(content);
            json.content_blocks = json1.content;
            return json;
        }
        catch (Exception e)
        {
            try
            {
                var json1 = JsonConvert.DeserializeObject<MessageJSON_simple>(content);
                json.content_string = json1.content;
                return json;
            }
            catch (Exception e2)
            {
                Debug.LogError($"error failed to deserialize MessageJSON object from [{content}]");
                if (content == null) json.content_string = "null";
                else json.content_string = content;
                return json;
            }
        }
    }

    
    public class MessageParagraph
    {
        public string content_text;
        public int portraitRefID = -1;
        public List<string> portraitTags = new List<string>();
        public string CommandID;
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
public class MessageJSON
{
    public string think;
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
            return (content_blocks.Count > animatedIndex) || (content_string.Length > animatedIndex);
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

            try
            {
                json = JsonConvert.DeserializeObject<MessageJSON>(text);
                json_serialized = json;
            }
            catch (Exception e)
            {
                Debug.LogError($"error failed to deserialize MessageJSON object from [{text}]");
                return null;
            }


            try
            {
                var json1 = JsonConvert.DeserializeObject<MessageJSON_blocks>(text);
                json.content_blocks = json1.content;
                return json;
            }
            catch (Exception e)
            {
                try
                {
                    var json1 = JsonConvert.DeserializeObject<MessageJSON_simple>(text);
                    json.content_string = json1.content;
                    return json;
                }
                catch (Exception e2)
                {
                    Debug.LogError($"error failed to deserialize MessageJSON object from [{text}]");
                    if (text == null) json.content_string = "null";
                    else json.content_string = text;
                    return json;
                }
            }
        }
    }
    public class usages
    {
        public int prompt_tokens;
        public int completion_tokens;
        public int total_tokens;
        public int input_tokens;
        public int output_tokens;
    }

    [JsonIgnore]
    public MessageJSON JSON
    {
        get
        {
            if (choices.Count > 0) return choices[0].JSON;
            else if (content.Count > 0) return content[0].JSON;
            else return null;
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
        public Dictionary<string, string> schedule = null;
        public string LorebookEntry = null;
        public List<string> ValidPortraitTags = new List<string>();

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

                    CurrentAttitude = $"{rel.GetCurrentAttitude().DisplayName}";

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

            RefID = c.RefID;
            Description = $"{c.Race.DisplayName} {c.RaceTemplate.DisplayName} {c.FactionManager.CurrentlyActiveFactionStatus}";
            if (scr_System_CampaignManager.current.Player == c) Description += ", IS PLAYER CHARACTER";
            CurrentlyDoing = c.GetJobDescription();
            var room = scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID);
            if (room != null) CurrentLocation = $"{(room.parentFloor != null ? $"{room.parentFloor.displayName}, " : "" )}{room.DisplayName}";
            NextHourPlan = ((nextHourJob == null || nextHourJob.Name == "") ? LocalizeDictionary.QueryThenParse("chara_currentjob_free") : nextHourJob.Name + (faction != null ? "(" + c.FactionManager.CurrentJobScheduleFaction(nextHour).FactionDisplayName + ")" : ""));

            if (fullLoad)
            {
                LorebookEntry = c.CharacterCard;
                Relationships = new Dictionary<string, RelationshipStorage>();
                equipments = new List<string>();
                schedule = new Dictionary<string, string>();
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

                for(int i = 0; i < 24; i++)
                {
                    var name = c.GetJobPost(i).Name;
                    if(name != "") schedule.Add($"{i}H", name);
                }

                if (c.PortraitManager != null)
                {
                    c.PortraitManager.CollectAllTags(ref this.ValidPortraitTags);
                }
            
            }
        }
    }

    public Dictionary<string, List<string>> FloorDescriptions = new Dictionary<string, List<string>>();// <floorName, <roomRefID, roomDescription>> with each room name and present chara;
    public Dictionary<string, string> Lorebook = new Dictionary<string, string>();
    public Dictionary<string, CharaStorage> Characters = new Dictionary<string, CharaStorage>(); // <refID, description>
    public Dictionary<string, Dictionary<string, List<SerializedAP>>> PossibleInteractions = new Dictionary<string, Dictionary<string, List<SerializedAP>>>(); // <targetName, <commandID, tooltips>>

    public LLM_WorldState()
    {
        var currentRoom = scr_System_CampaignManager.current.CurrentRoom;
        var faction = currentRoom == null ? null : currentRoom.FactionOwner;

        if (faction != null)
        {
            foreach(var floor in faction.ManagedFloors)
            {
                var dic = new List<string>();
                foreach(var room in floor.rooms)
                {
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
                        dic.Add($"This is the Current Room player is in: [{room.DisplayName}]\nRoomInfo:[{room.DisplayableFurnitureNames}]\nRoom Cleanliness: {room.RoomCleanliness()}\nRoom Items:{(room.Inventory.Contents.Count > 0 ? $"[\n{room.Inventory.PrintContent()}]" : "no item")}\nChara in room:[{String.Join(", ", names)}]\nOngoing command in room:[{(aps.Count > 0 ? String.Join("\n", aps) : "no ongoing")}]");

                        if (room.parentFloor != null && room.parentFloor.MapTemplate != null)
                        {
                            foreach (var kvp in room.parentFloor.MapTemplate.Lorebooks)
                            {
                                Lorebook.Add(kvp.Key, kvp.Value);
                            }
                        }
                    }
                    else
                    {
                        if (!dic.Contains(room.DisplayName)) dic.Add($"{room.DisplayName}");
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


        // collect world info
        if (scr_System_CampaignManager.current.CurrentCampaign != null)
        {
            Lorebook.Add($"Current Campaign: [{scr_System_CampaignManager.current.CurrentCampaign.DisplayName}]",$"\nCampaign Info:[\n {scr_System_CampaignManager.current.CurrentCampaign.Tooltip}\n]");

            foreach (var kvp in scr_System_CampaignManager.current.CurrentCampaign.Lorebooks)
            {
                Lorebook.Add(kvp.Key, kvp.Value);
            }
        }

        List<string> relationshipTypes = new List<string>();
        foreach(var i in scr_System_Serializer.current.MasterList.RelationshipTypes.list_personal)
        {
            relationshipTypes.Add($"{i.DisplayName}: {i.Tooltip}");
        }
        Lorebook.Add($"All personal relationship types",$"[{String.Join("\n", relationshipTypes)}]");

        var currentTime = scr_System_Time.current.getCurrentTime();
        string dayofWeek = LocalizeDictionary.QueryThenParse("ui_calendar_dayOfWeek_" + currentTime.DayOfWeek);
        Lorebook.Add("Current World Time Hour", $"{currentTime.ToShortDateString()}, {currentTime.ToShortTimeString()}, {dayofWeek}");


        var startTime = scr_System_Time.current.getStartTime();
        var dayCount = currentTime - startTime;
        Lorebook.Add("Time Since Campaign Start", $"{currentTime.Year - startTime.Year} year, {dayCount.Days + 1} days");
        Lorebook.Add("isTimeStopped", $"{scr_System_Time.current.TimeStop}");

        LLMUtils.CollectCOMInfo(PossibleInteractions, currentRoom);

    }

}


public static class LLMUtils
{
    static void validateSingle(Job job, List<int> doer, List<int> receiver, HashSet<Job> verified, Dictionary<string, List<SerializedAP>> collection, HashSet<string> repeat )
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

        var chara = scr_System_CampaignManager.current.FindInstanceByID(doer[0]);
        List<SerializedAP> tooltips = new List<SerializedAP>();

        foreach (var ap in (job is Job_Furniture ? job.MakePackages(chara, true) : job.CachedPackages))
        { 
            validateAP(ap, tooltips, doer, receiver);
        }
        if (tooltips.Count > 0) collection.Add($"{job.DisplayName}", tooltips);
    }

    static void validateExisting(Job job, Dictionary<string, List<SerializedAP>> collection)
    {
        List<SerializedAP> tooltips = new List<SerializedAP>();
        foreach (var ap in job.MakePackages(scr_System_CampaignManager.current.Player))
        {
            validateAP(ap, tooltips, null, null);
        }
        if (tooltips.Count > 0) collection.Add($"Available Command in {job.DisplayName}", tooltips);
    }


    static void validateAP(ActionPackage ap, List<SerializedAP> tooltips, List<int> doer, List<int> receiver)
    {
        if (ap.targetCOM == null) return;
        if (!ap.targetCOM.ValidateJob(ap.job, out var msg))
        {
            // add message
            return;
        }
        var app = new SerializedAP();
        app.CommandName =  ap.DisplayName;
        app.CommandID = ap.targetCOM == null ? "null" : ap.targetCOM.ID;
        app.SourceJobID = ap.job.RefID;


        if (doer != null && receiver != null) ap.ResetRequest(doer, receiver, doer.Count > 0 ? doer[0] : -1, true);
        if (!ap.Validate())
        {
            if (ap.COMVariantID < -1) return;
            // validation failure
            ap.tooltip.RemoveAll(x => x == "" || x.Length < 1);
            app.Summary = ap.GetTooltips(LocalizeDictionary.QueryThenParse("ui_ap_onHoverTooltip_comInvalid")).Replace("$tooltips$", String.Join("\n", ap.tooltip));

        }
        else if (ap.ComTags.Contains("sleep") && !scr_System_CampaignManager.current.Player.shouldSleep && !scr_System_CampaignManager.current.DebugMode)
        {
            app.Summary = ap.GetTooltips(LocalizeDictionary.QueryThenParse("ui_ap_onHoverTooltip_comInvalid")).Replace("$tooltips$", LocalizeDictionary.QueryThenParse("ui_ap_onHoverTooltip_cannotSleep"));
        }
        else
        {
            var prevalidation = ap.GetSuccessRatePrevalidationString();
            ap.CollectMods(out var dcMods, out var bonus, out var baseDC);
            string dcResult = "";
            if (baseDC > 0)
            {
                List<string> mods = dcMods == null ? new List<string>() : dcMods.GetAllModifiers();
                dcResult = $"Difficulty Check D20{(mods.Count > 0 ? $" + {String.Join(" + ", mods)}" : "")} >=? {baseDC}";
            }

            //tooltips.Add($"{ap.DisplayName}: [{ap.GetTooltips(LocalizeDictionary.QueryThenParse("ui_ap_onHoverTooltip"))}{(prevalidation.Length > 0 ? $"\n{prevalidation}" : "")}{(dcResult.Length > 0 ? $"\n{dcResult}" : "")}\n]");

            app.Summary = ap.GetTooltips(LocalizeDictionary.QueryThenParse("ui_ap_onHoverTooltip")+$"\n{String.Join("\n", ap.tooltip)}");

            if (prevalidation.Length > 0) app.AcceptanceCheck = prevalidation;
            else app.AcceptanceCheck = null;

            if (dcResult.Length > 0) app.DifficultyCheck = dcResult;
            else app.DifficultyCheck = null;
        }

        tooltips.Add(app);
    }

    public class SerializedAP
    { 
        public string CommandName;
        public string CommandID;
        public int SourceJobID;
        public string Summary;
        public string AcceptanceCheck;
        public string DifficultyCheck;

    }

    /// <summary>
    /// Only collect player relevant info. do not check for npc-npc.
    /// </summary>
    /// <param name="targets"></param>
    /// <returns></returns>
    public static void CollectCOMInfo(Dictionary<string, Dictionary<string, List<SerializedAP>>> PossibleInteractions, Room_Instance currentRoom)
    {
        var mgr = scr_System_CampaignManager.current;
        if (mgr == null) return;
        List<Character_Trainable> targets = currentRoom == null ? new List<Character_Trainable>() : currentRoom.RoomChara;
        var trackedJobs = new HashSet<Job>();

        Dictionary<string, List<SerializedAP>> collection = new Dictionary<string, List<SerializedAP>>();

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
                validateExisting(job, collection);
            }
            if (collection.Count > 0)
            {
                PossibleInteractions.Add("Existing Commands in room:", new Dictionary<string, List<SerializedAP>>(collection));
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
                    PossibleInteractions.Add($"Possible command with {r.FirstName}", new Dictionary<string, List<SerializedAP>>(collection));
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
                PossibleInteractions.Add($"Possible command alone", new Dictionary<string, List<SerializedAP>>(collection));
            }
        }
    }
}

