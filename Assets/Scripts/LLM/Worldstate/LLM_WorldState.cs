using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using static LLMUtils;



public class LLM_WorldState
{
    public class FactionStorage
    {
        public string factionID;
        public string factionDisplayName;
        public List<string> factionMembers = null;
        public Dictionary<string, Dictionary<string, RoomStorage>> FloorDescriptions = null;// <floorName, <roomRefID, roomDescription>> with each room name and present chara;
        public Dictionary<string, string> Lorebook = new Dictionary<string, string>();

        public bool isFullDetail = false;

        public List<string> management = null;
        public List<string> financials = null; // -> query for faction's billing status, debt, income, next billing dates etc
        public List<string> inventory = null; // 

        /// <summary>
        /// Externally injected data;
        /// </summary>
        public int TravelDistanceMinuteFromCurrentRoom = 0;

        public FactionStorage()
        {

        }
        public FactionStorage (Manageable faction, int TravelDistanceMinute, bool fullLoad = false)
        {
            isFullDetail = fullLoad;
            factionID = faction.FactionID;
            factionDisplayName = faction.FactionDisplayName;
            this.TravelDistanceMinuteFromCurrentRoom = TravelDistanceMinute;

            if (isFullDetail)
            {
                // -- Faction Members -- //
                factionMembers = new List<string>();
                foreach (var c in faction.ManagedChara)
                {
                    if (c == null) continue;

                    var CurrentLocation = "unknown";
                    var CurrentlyDoing = c.GetJobDescription();
                    var room = scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID);
                    if (room != null) CurrentLocation = $"{(room.parentFloor != null ? $"{room.parentFloor.displayName}, " : "")}{room.DisplayName}";

                    factionMembers.Add($"{c.FullName}, {faction.GetCharaSocialStandingName(c)} {(faction.isCharaManager(c) ? "isManager" : "")}, currently at {CurrentLocation} doing {CurrentlyDoing}");
                }

                // -- Management -- //
                management = new List<string>();
                if (faction.isPlayerFaction)
                {
                    var report = faction.DailyReport;
                    if (!report.initialized) report.Initialize();
                    management.AddRange(report.manageLogs);
                    management.AddRange(report.tradeLogs);
                    management.AddRange(report.tradeWarnings);
                }
                else
                {
                    management.Add("not player managed faction, data not necessary");
                }

                // -- Financials -- //
                financials = FactionUtility.GetObligationReport(faction, out var error);

                // -- Floors -- //
                FloorDescriptions = new Dictionary<string, Dictionary<string, RoomStorage>>();
                foreach (var floor in faction.ManagedFloors)
                {
                    var dic = new Dictionary<string, RoomStorage>();
                    foreach (var room in floor.rooms)
                    {
                        var key = $"{room.DisplayNameShort} {room.RefID}";
                        if (!dic.ContainsKey(key)) dic.Add(key, new RoomStorage(room, false));

                        if (room.parentFloor != null && room.parentFloor.MapTemplate != null)
                        {
                            foreach (var kvp in room.parentFloor.MapTemplate.Lorebooks) Lorebook.Add(kvp.Key, kvp.Value);
                        }
                    }
                    FloorDescriptions.Add(floor.displayName, dic);
                }

                // -- Inventory -- //
                inventory = new List<string>();
                if (faction.isPlayerFaction) foreach(var i in faction.Inventory.ContentsPrintable) inventory.Add(i.Print());
                else inventory.Add("faction is not player managed faction, inventory data is not necessary");                
            }
        }
    }

    public class RoomStorage
    {
        public Dictionary<string, CharaStorage> CharactersInRoom = null; // <refID, description>
        public Dictionary<string, string> CharactersInRoomShort = null; // <refID, description>
        public bool isFullDetail;
        public int RoomRefID;
        public string RoomInfo;
        //public List<string> furnituresInRoom = new List<string>();

        public RoomStorage()
        {

        }
        public RoomStorage(Room_Instance room, bool fullLoad = false)
        {
            // A character can be physically present in the current room without being a managed member of
            // its owning faction (e.g. an unaffiliated guest/visitor NPC) - the loop above only covers
            // faction.ManagedChara, so anyone else in the room was otherwise visible only by first name in
            // CurrentRoomInfo's "Chara in room" list, with no RefID anywhere in world info at all. Serialize
            // everyone physically in the room regardless of faction ownership. Indexer assignment since
            // faction.ManagedChara may already have added some of them above.
            if (room != null)
            {
                RoomRefID = room.RefID;
                isFullDetail = fullLoad;

                List<string> aps = new List<string>();
                foreach (var ap in scr_System_CampaignManager.current.GetRegisteredAPByRoom(room.RefID, false))
                {
                    if (ap.job.isPlayerRelatedJob) continue;
                    if (ap.isTemporaryAP) continue;
                    aps.Add(ap.DescriptionText());
                }

                RoomInfo = $"{room.DisplayableFurnitureNames}";
                if (room.RoomCleanliness() != Room_Instance.CleaningStatus.None) RoomInfo += $"\nRoom Cleanliness: {room.RoomCleanliness()}";
                if (room.Inventory.Contents.Count > 0) RoomInfo += $"\nRoom Items: {room.Inventory.PrintContent()}";
                if (aps.Count > 0) RoomInfo += $"\nOngoing command in room: {String.Join(" | ", aps)}";

                foreach (var c in room.RoomChara)
                {
                    if (c == scr_System_CampaignManager.current.Player) continue;
                    if (fullLoad)
                    {
                        if (CharactersInRoom == null) CharactersInRoom = new Dictionary<string, CharaStorage>();
                        if (!CharactersInRoom.ContainsKey(c.FullName)) CharactersInRoom[c.FullName] = new CharaStorage(c, false);
                    }
                    else
                    {
                        if (CharactersInRoomShort == null) CharactersInRoomShort = new Dictionary<string, string>();
                        if (!CharactersInRoomShort.ContainsKey(c.FullName)) CharactersInRoomShort[c.FullName] = $"refID {c.RefID}, currently doing: {c.GetJobDescription()}";
                    }
                }


            }
        }
    }

    public class CharaStorage
    {
        public string FirstName;
        public int RefID;
        public string Description;
        public bool isFullDetail = false;
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
        public FactionMemberStorage HomeFaction = null;
        public List<FactionMemberStorage> WorkFactions = new List<FactionMemberStorage>();

        // TODO //
        public List<string> Traits = null; // fill only for full load: collect character trait name
        public List<string> Skills = null; // fill only for full load: collect skill name application and level

        public class RelationshipStorage
        {
            public Dictionary<string, int> Scores = new Dictionary<string, int>();
            public string CurrentRelationships = "";

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

                    CurrentRelationships = rel.relationText.Replace("$name$", $"{rel.TargetName}" + (rel.Target.isTemporaryActor && rel.Target.Title.Length > 0 ? $"({rel.Target.Title})" : "")).Replace("$relation$", relName.Count > 0 ? String.Join(",", relName) : "no relation");
                }
            }
        }

        public class MemoryStorage
        {
            public string timestamp;
            public string summary;
            public List<string> details = new List<string>();
            //public string memoryEffects;

            public MemoryStorage()
            {

            }
            public MemoryStorage(Memory_Entry mem)
            {
                timestamp = $"{mem.PrintShortTimeStartToEnd}";
                summary = mem.ToString();
                details = new List<string>(mem.MemInstanceDescriptions);
                //memoryEffects = $"Statmod: Acceptance check{mem.CachedScore.ToString("+0;-#")} Mood{mem.MoodSum} Stress{mem.StressSum} Lust{mem.LustSum}";
            }
        }

        public CharaStorage()
        {

        }

        public class FactionMemberStorage
        {
            public string factionID;
            public string factionPositionName;
            public bool isManager = false;

            public FactionMemberStorage()
            {

            }
            public FactionMemberStorage(Character_Trainable c, Manageable m)
            {
                factionID = m.FactionID;
                isManager = m.isCharaManager(c);
                factionPositionName = Utility.GetFactionDetail( m, c);
            }
        }

        /// <summary>
        /// By default call fullload false
        /// </summary>
        /// <param name="c"></param>
        /// <param name="faction"></param>
        /// <param name="fullLoad"></param>
        public CharaStorage(Character_Trainable c, bool fullLoad = false)
        {
            FirstName = c.FirstName;

            isFullDetail = fullLoad;

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
            if (room != null) CurrentLocation = $"{(room.parentFloor != null ? $"{room.parentFloor.displayName}, " : "")}{room.DisplayName}";
            NextHourPlan = ((nextHourJob == null || nextHourJob.Name == "") ? LocalizeDictionary.QueryThenParse("chara_currentjob_free") : nextHourJob.Name + (nextHourFaction != null ? $"({nextHourFaction.FactionDisplayName})" : ""));

            // -- Status -- //
            Status = new List<string>();
            if (c.Stats != null)
            {
                if (c.Stats.Mood != null) Status.Add($"{c.Stats.Mood.SeverityDisplayName}\n{c.Stats.Mood.ModTooltip}");
                if (c.Stats.Stress != null) Status.Add($"{c.Stats.Stress.SeverityDisplayName}\n{c.Stats.Stress.ModTooltip}");
                if (c.Stats.Lust != null) Status.Add($"{c.Stats.Lust.SeverityDisplayName}\n{c.Stats.Lust.ModTooltip}");

                if (c.Relationships != null) Status.Add($"{c.Relationships.GetAttitudeString()}");

                foreach (var status in c.Stats.statusInstancesEx)
                {
                    if (status.BaseRef.noDisplay) continue;
                    if (!status.Displayable) continue;
                    Status.Add($"{status.SeverityDisplayName}");
                }
                foreach (var status in c.Stats.StatusInstances)
                {
                    if (status.BaseRef.noDisplay) continue;
                    if (!status.Displayable) continue;
                    Status.Add($"{status.SeverityDisplayName}");
                }
            }

            equipments = new List<string>();
            Relationships = new Dictionary<string, RelationshipStorage>();

            // -- Factions -- //
            if (c.FactionManager.HomeFactions != null && c.FactionManager.HomeFactions.Count > 0)
            {
                HomeFaction = new FactionMemberStorage(c, c.FactionManager.HomeFactions[0]);
            }

            foreach(var m in c.FactionManager.WorkFactions)
            {
                WorkFactions.Add(new FactionMemberStorage(c, m));
            }


            if (fullLoad)
            {
                LorebookEntry = c.CharacterCard;
                //schedule = new Dictionary<string, string>();

                // -- Memory -- //
                Memories = new List<MemoryStorage>();
                if (c.Memory.Entries != null)
                {
                    foreach (var i in c.Memory.Entries)
                    {
                        var newmm = new MemoryStorage(i);
                        Memories.Add(newmm);
                    }
                }

                // -- Relationships -- //
                foreach (var i in c.Relationships.Relationships)
                {
                    if (i.Owner == i.Target) continue;
                    Relationships.Add($"{i.Owner.FirstName}'s attitude toward {i.Target.FirstName}", new RelationshipStorage(i));
                }
                // foreach (var i in c.Relationships.GenericRelationship) Relationships.Add($"attitude towards {LocalizeDictionary.QueryThenParse(i.Key)}", new RelationshipStorage(i.Value, true));

                // -- Equipments -- //
                foreach (var equipref in c.Body.EquippedItemRefs)
                {
                    var equip = scr_System_CampaignManager.current.FindItemInstanceByID(equipref);
                    var equiptooltip = equip.Base.Tooltip == "no_tooltip" ? "" : $": {equip.Base.Tooltip}";
                    equipments.Add($"{equip.DisplayName}, refID {equip.RefID}, {equiptooltip}");
                }
                foreach (var kwd in c.Body.BodyDescription)
                {
                    equipments.Add(kwd);
                }
                // -- PortraitManager -- //
                if (c.PortraitManager != null)
                {
                    c.PortraitManager.CollectAllTags(ValidPortraitTags, ValidPortraitTags_target);
                }
            }
            else
            {
                // -- Relationships -- //
                var playerrel = c.Relationships.FindRelationshipWith(scr_System_CampaignManager.current.Player);
                if ( playerrel != null && playerrel.Owner != playerrel.Target)
                {
                    Relationships.Add($"attitude toward {playerrel.Target.FirstName}", new RelationshipStorage(playerrel));
                }
                // -- Equipments -- //
                foreach (var equipref in c.Body.EquippedItemRefs)
                {
                    var equip = scr_System_CampaignManager.current.FindItemInstanceByID(equipref);
                    equipments.Add($"{equip.DisplayName}");
                }
                foreach (var kwd in c.Body.BodyDescription)
                {
                    equipments.Add(kwd);
                }
            }
        }
    }

    public RoomStorage CurrentRoomInfo = null;
    public FactionStorage CurrentLocationFaction = null;
    public Dictionary<string, string> Lorebook = new Dictionary<string, string>();

    // -- TODO -- //
    public Dictionary<string, FactionStorage> AllPathableFactionsFromCurrentRoom = new Dictionary<string, FactionStorage>(); // full load only, load every faction that are pathable from player location and with approx time cost

    public class PossibleInteractions
    {
        public List<int> DoerRefs;
        public List<int> ReceiverRefs;
        public int MasterRef = -1;
        public Dictionary<string, Dictionary<string, SerializedAP>> possibleInteractions = new Dictionary<string, Dictionary<string, SerializedAP>>();
        public Dictionary<string, SerializedAP> FurnitureInteractions = new Dictionary<string, SerializedAP>();


        [JsonIgnore] public List<Character_Trainable> Doers = new List<Character_Trainable>();
        [JsonIgnore] public List<Character_Trainable> Receivers = new List<Character_Trainable>();
        [JsonIgnore] public Character_Trainable Master = null;

        // serialized actor summary strings, " - " when empty
        [JsonIgnore] public string InteractionCallName
        {
            get
            {
                List<string> doerNames = new List<string>();
                List<string> receiverNames = new List<string>();
                foreach (var i in Doers) doerNames.Add(ActorName(i));
                foreach (var i in Receivers) receiverNames.Add(ActorName(i));

                if (receiverNames.Count > 0)
                {
                    return $"possible interactions {String.Join(",", doerNames)} can do with {String.Join(",", receiverNames)}" + (Master == null ? "" : $" under order of {Master.FirstName}");
                }
                else
                {
                    return $"possible interactions {String.Join(",", doerNames)} can do" + (Master == null ? "" : $" under order of {Master.FirstName}");
                }
            }
        }

        static string ActorName(Character_Trainable c)
        {
            return c == null ? " - " : $"{c.FirstName}(refID {c.RefID})";
        }

        public PossibleInteractions()
        {

        }
        public PossibleInteractions(List<Character_Trainable> doers, List<Character_Trainable> receivers, Character_Trainable master = null)
        {
            if (doers != null) this.Doers = doers;
            if (receivers != null) this.Receivers = receivers;
            this.Master = master;

            this.DoerRefs = new List<int>(this.Doers.Select(c => c.RefID));
            this.ReceiverRefs = new List<int>(this.Receivers.Select(c => c.RefID));
            this.MasterRef = master == null ? -1 : master.RefID;
        }
        public PossibleInteractions(Character_Trainable doer, Character_Trainable receiver, Character_Trainable master = null)
        {
            this.Doers = doer == null ? new List<Character_Trainable>() : new List<Character_Trainable>() { doer };
            this.Receivers = receiver == null ? new List<Character_Trainable>() : new List<Character_Trainable>() { receiver };
            this.Master = master;

            this.DoerRefs = new List<int>(this.Doers.Select(c => c.RefID));
            this.ReceiverRefs = new List<int>(this.Receivers.Select(c => c.RefID));
            this.MasterRef = master == null ? -1 : master.RefID;

        }
    }

    /// <summary>
    /// TODO: build packages with doer reciever master and store into PossibleInteractions
    /// </summary>
    /// <param name="doer"></param>
    /// <param name="receiver"></param>
    /// <param name="master"></param>
    public void LoadInteractionData(Character_Trainable doer, Character_Trainable receiver = null, Character_Trainable master = null)
    {
        var interac = new PossibleInteractions(doer, receiver, master);
        LLMUtils.CollectCOMInfo(interac);
        LoadedPossibleInteractions[interac.InteractionCallName] = interac;
        // 
    }
    public void LoadInteractionData(Character_Trainable doer, List<Character_Trainable> receivers, Character_Trainable master = null)
    {
        var interac = new PossibleInteractions(new List<Character_Trainable>() { doer }, receivers, master);
        LLMUtils.CollectCOMInfo(interac);
        LoadedPossibleInteractions[interac.InteractionCallName] = interac;
        // 
    }

    public Dictionary<string, PossibleInteractions> LoadedPossibleInteractions = new Dictionary<string, PossibleInteractions>(); // <targetName, <commandID, tooltips>>
    public LLM_WorldState(bool loadFull = false)
    {
        // -- Current Room -- //
        var currentRoom = scr_System_CampaignManager.current.CurrentRoom;
        CurrentRoomInfo = new RoomStorage(currentRoom, true);
        // -- Current Time -- //
        var currentTime = scr_System_Time.current.getCurrentTime();
        string dayofWeek = LocalizeDictionary.QueryThenParse("ui_calendar_dayOfWeek_" + currentTime.DayOfWeek);
        Lorebook.Add("Current World Time Hour", $"{currentTime.ToShortDateString()}, {currentTime.ToShortTimeString()}, {dayofWeek}");

        if (loadFull)
        {
            // -- Current Faction -- //
            if (currentRoom != null && currentRoom.FactionOwner != null) CurrentLocationFaction = new FactionStorage(currentRoom.FactionOwner.FactionOwnerRoot, 0);
            // -- World Info -- //
            if (scr_System_CampaignManager.current.CurrentCampaign != null)
            {
                Lorebook.Add($"Current Campaign: [{scr_System_CampaignManager.current.CurrentCampaign.DisplayName}]", $"\nCampaign Info:[\n {scr_System_CampaignManager.current.CurrentCampaign.Tooltip}\n]");
                foreach (var kvp in scr_System_CampaignManager.current.CurrentCampaign.Lorebooks) Lorebook.Add(kvp.Key, kvp.Value);
            }
            // -- Campaign Start Time -- //
            var startTime = scr_System_Time.current.getStartTime();
            var dayCount = currentTime - startTime;
            Lorebook.Add("Time Since Campaign Start", $"{currentTime.Year - startTime.Year} year, {dayCount.Days + 1} days");

            foreach(var faction in scr_System_CampaignManager.current.Factions)
            {
                // find path. if pathable from current room, then write
                if (currentRoom == null || faction.MainExit == null) continue;
                if (scr_System_CampaignManager.current.Map.FindPathFromRoom(currentRoom, faction.MainExit, out var path))
                {
                    float cost = 0f;
                    foreach (var i in path) cost += i.Tag.Cost;

                    AllPathableFactionsFromCurrentRoom.TryAdd(faction.FactionDisplayName, new FactionStorage(faction, (int)cost, false));
                }
                else continue;

            }
        }

        // -- Timestopped -- //
        Lorebook.Add("isTimeStopped", $"{scr_System_Time.current.TimeStop}");

    }

}