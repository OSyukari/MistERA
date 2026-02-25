using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public class LLM_Setting
{
    public class ChatCompletion
    {
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

    public string model;
    public float temperature = 0.7f;
    public int max_tokens = 512;
    public int max_completion_tokens = 512;
    public bool stream = false;
    public double top_p = 1.0;
    public double top_k = 40;

    public void LoadTemplate(LLMRequest req)
    {
        foreach(var message in req.messages)
        {
            var newm = new LLMMessage(message);
            messages.Add(newm);
        }
    }

    public void ReplaceString(string a, string b)
    {
        foreach(var message in this.messages)
        {
            message.content = message.content.Replace(a, b);
        }
    }

    public void Purge()
    {
        currentString = null;
        prepend = null;
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
}

public class LLMResponse
{
    public string id;
    public string created;
    public string model;
    public List<choice> choices = new List<choice>();
    public usages usage;

    public class choice
    {
        public int index;
        public LLMMessage message;
        public string finish_reason;
    }
    public class usages
    {
        public int prompt_tokens;
        public int completion_tokens;
        public int total_tokens;
    }
}


public class LLM_WorldState
{
    public class CharaStorage
    {
        public string Fullname;
        public string Description;
        public List<string> Status = null;
        public List<MemoryStorage> Memories = null;
        public string CurrentlyDoing;
        public string CurrentLocation;
        public string NextHourPlan;
        public Dictionary<string, RelationshipStorage> Relationships = null;
        public List<string> equipments = null;
        public Dictionary<string, string> schedule = null;

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
            int nextHour = scr_System_Time.current.getCurrentTime().Hour + 1;
            if (nextHour >= 24) nextHour -= 24;
            var nextHourJob = c.FactionManager.CurrentJobPost(nextHour);

            Fullname = c.FullName;
            Description = $"{c.Race.DisplayName} {c.RaceTemplate.DisplayName} {c.FactionManager.CurrentlyActiveFactionStatus}";
            if (scr_System_CampaignManager.current.Player == c) Description += ", IS PLAYER CHARACTER";
            CurrentlyDoing = c.GetJobDescription();
            var room = scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID);
            if (room != null) CurrentLocation = $"{(room.parentFloor != null ? $"{room.parentFloor.displayName}, " : "" )}{room.DisplayName}";
            NextHourPlan = ((nextHourJob == null || nextHourJob.Name == "") ? LocalizeDictionary.QueryThenParse("chara_currentjob_free") : nextHourJob.Name + (faction != null ? "(" + c.FactionManager.CurrentJobScheduleFaction(nextHour).FactionDisplayName + ")" : ""));

            if (fullLoad)
            {
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
                    equipments.Add($"{equip.DisplayName}{(equip.Base.Tooltip == "NONE" ? "" : equip.Base.Tooltip)}");
                }

                for(int i = 0; i < 24; i++)
                {
                    var name = c.GetJobPost(i).Name;
                    schedule.Add($"{i}H", name == "" ? "free time" : name);
                }
            
            }
        }
    }

    public Dictionary<string, List<string>> FloorDescriptions = new Dictionary<string, List<string>>();// <floorName, <roomRefID, roomDescription>> with each room name and present chara;
    public Dictionary<string, string> Lorebook = new Dictionary<string, string>();
    public Dictionary<int, CharaStorage> Characters = new Dictionary<int, CharaStorage>(); // <refID, description>
    public Dictionary<string, Dictionary<string, string>> PossibleInteractions = new Dictionary<string, Dictionary<string, string>>(); // <targetName, <commandID, tooltips>>

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

                    }
                    else
                    {
                        dic.Add($"{room.DisplayName}");
                    }
                }
                FloorDescriptions.Add(floor.displayName, dic);
            }


            foreach(var c in faction.ManagedChara)
            {

               if (currentRoom.RoomChara.Contains(c))
                {// more detailed desc
                    Characters.Add(c.RefID, new CharaStorage(c, faction, true));
                }
                else
                {
                    Characters.Add(c.RefID, new CharaStorage(c, faction, false));
                }

            }
        }


        // collect world info
        if (scr_System_CampaignManager.current.CurrentCampaign != null)
        {
            Lorebook.Add($"Current Campaign: [{scr_System_CampaignManager.current.CurrentCampaign.DisplayName}]",$"\nCampaign Info:[\n {scr_System_CampaignManager.current.CurrentCampaign.Tooltip}\n]");
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


        LLMUtils.CollectCOMInfo(PossibleInteractions, currentRoom);

    }

}


public static class LLMUtils
{
    static void validateSingle(Job job, List<int> doer, List<int> receiver, HashSet<Job> verified, Dictionary<string,string> collection, HashSet<string> repeat )
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
        List<string> tooltips = new List<string>();

        foreach (var ap in (job is Job_Furniture ? job.MakePackages(chara, true) : job.CachedPackages))
        { 
            tooltips.Clear();
            validateAP(ap, tooltips, doer, receiver);
            if (tooltips.Count > 0) collection.Add($"{job.DisplayName}: {ap.DisplayName}", String.Join("\n", tooltips));
        }
    }

    static void validateExisting(Job job, Dictionary<string, string> collection)
    {
        List<string> tooltips = new List<string>();
        foreach (var ap in job.MakePackages(scr_System_CampaignManager.current.Player))
        {
            tooltips.Clear();
            validateAP(ap, tooltips, null, null);
            if (tooltips.Count > 0) collection.Add($"Available Command in {job.DisplayName}", String.Join("\n", tooltips));
        }
    }


    static void validateAP(ActionPackage ap, List<string> tooltips, List<int> doer, List<int> receiver)
    {
        if (ap.targetCOM == null) return;
        if (!ap.targetCOM.ValidateJob(ap.job, out var msg))
        {
            // add message
            return;
        }
        if (doer != null && receiver != null) ap.ResetRequest(doer, receiver, doer.Count > 0 ? doer[0] : -1, true);
        if (!ap.Validate())
        {
            // validation failure
            ap.tooltip.RemoveAll(x => x == "" || x.Length < 1);
            tooltips.Add($"{ap.DisplayName}: [{ap.GetTooltips(LocalizeDictionary.QueryThenParse("ui_ap_onHoverTooltip_comInvalid")).Replace("$tooltips$", String.Join("\n", ap.tooltip))}\n]");

        }
        else if (ap.ComTags.Contains("sleep") && !scr_System_CampaignManager.current.Player.shouldSleep)
        {
            tooltips.Add($"{ap.DisplayName}: [{ap.GetTooltips(LocalizeDictionary.QueryThenParse("ui_ap_onHoverTooltip_comInvalid")).Replace("$tooltips$", LocalizeDictionary.QueryThenParse("ui_ap_onHoverTooltip_cannotSleep"))}\n]");
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

            tooltips.Add($"{ap.DisplayName}: [{ap.GetTooltips(LocalizeDictionary.QueryThenParse("ui_ap_onHoverTooltip"))}{(prevalidation.Length > 0 ? $"\n{prevalidation}" : "")}{(dcResult.Length > 0 ? $"\n{dcResult}" : "")}\n]");
        }
    }

    /// <summary>
    /// Only collect player relevant info. do not check for npc-npc.
    /// </summary>
    /// <param name="targets"></param>
    /// <returns></returns>
    public static void CollectCOMInfo(Dictionary<string, Dictionary<string, string>> PossibleInteractions, Room_Instance currentRoom)
    {
        var mgr = scr_System_CampaignManager.current;
        if (mgr == null) return;
        List<Character_Trainable> targets = currentRoom == null ? new List<Character_Trainable>() : currentRoom.RoomChara;
        var trackedJobs = new HashSet<Job>();

        Dictionary<string, string> collection = new Dictionary<string, string>();

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

        // current room jobs
        foreach (var job in mgr.CurrentRoom.Jobs)
        {
            validateExisting(job, collection);
        }
        if (collection.Count > 0)
        {
            PossibleInteractions.Add("Existing Commands in room:", new Dictionary<string, string>(collection));
        }
        collection.Clear();

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

                if (playerCOM != null)
                {   // check npc's acceptance of playercom
                    validateSingle(playerCOM, player, target, trackedJobs, collection, duplicateCheck);
                }

                if (r.InteractionJob != null)
                {   // player interacting with target
                    validateSingle(r.InteractionJob, player, target, trackedJobs, collection, duplicateCheck);
                }

                if (currentRoom != null && currentRoom.Jobs != null)
                {
                    foreach (var j in currentRoom.Jobs)
                    {
                        validateSingle(j, player, target, trackedJobs, collection, duplicateCheck);
                    }
                }

                if (curr != null)
                {
                    validateSingle(curr, player, target, trackedJobs, collection, duplicateCheck);
                }

                if (collection.Count > 0)
                {
                    PossibleInteractions.Add($"Possible command with {r.FirstName}", new Dictionary<string, string>(collection));
                }
            }
        }

        if (player.Count > 0)
        {
            collection.Clear();
            target.Clear();
            trackedJobs.Clear();
            duplicateCheck.Clear();

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
            if (curr != null)
            {
                validateSingle(curr, player, target, trackedJobs, collection, duplicateCheck);
            }

            if (collection.Count > 0)
            {
                PossibleInteractions.Add($"Possible command alone", new Dictionary<string, string>(collection));
            }
        }
    }
}

