using Newtonsoft.Json;
using System.Collections.Generic;



public partial class LLM_WorldState
{
    

    public RoomStorage CurrentRoomInfo = null;
    public FactionStorage CurrentLocationFaction = null;
    public Dictionary<string, string> Lorebook = new Dictionary<string, string>();

    public Dictionary<string, FactionStorage> AllPathableFactionsFromCurrentRoom = new Dictionary<string, FactionStorage>(); // full load only, hierarchical: top-level entries are world-anchored factions pathable from the player's location; factions that belong under another (subfactions, door-connected annexes with no world of their own) are grouped into the parent's SubFactions
    public List<QuestStorage> QuestStages = null;
    

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
            if (currentRoom != null && currentRoom.FactionOwner != null) CurrentLocationFaction = new FactionStorage(currentRoom.FactionOwner.FactionOwnerRoot);
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

            // -- All Factions (hierarchical, same host-resolution rules as the map UI's draw plan in
            // canvas_RoomDisplay.InitFloorList/ResolveHost) -- //
            // Storages are created through one shared cache so a faction's host storage exists even
            // when the host is visited before (or never) reaches its own loop iteration - the host
            // chain can be several levels deep (mall shop -> mall -> world hub).
            var storages = new Dictionary<Manageable, FactionStorage>();
            FactionStorage StorageOf(Manageable f)
            {
                if (storages.TryGetValue(f, out var cached)) return cached;
                cached = new FactionStorage(f, false);
                storages[f] = cached;
                return cached;
            }

            foreach (var faction in scr_System_CampaignManager.current.Factions)
            {
                // find path. if pathable from current room, then write
                if (currentRoom == null || faction.MainExit == null) continue;
                if (!scr_System_CampaignManager.current.Map.FindPathFromRoom(currentRoom, faction.MainExit, out var path)) continue;

                var storage = StorageOf(faction);
                // never serialize the parent pointer inside the dump - nesting shows the
                // relationship already; ParentFaction is only for standalone (tool query) results
                storage.ParentFaction = null;
                var host = ResolveFactionHost(faction, new HashSet<Manageable>());
                if (host == faction)
                {
                    AllPathableFactionsFromCurrentRoom.TryAdd(faction.FactionDisplayName, storage);
                    continue;
                }

                // belongs under another faction - group it into the host's SubFactions. No
                // ParentFaction here: nesting already conveys the relationship, the parent pointer
                // is only serialized for storages returned standalone (get_faction_detail).
                var hostStorage = StorageOf(host);
                if (!hostStorage.SubFactions.Exists(s => s.factionID == faction.FactionID))
                    hostStorage.SubFactions.Add(storage);
            }
            // -- Quest Stages -- //
            QuestStages = new List<QuestStorage>();
            foreach (var quest in Masterlist_Event.Instance.Events.quests)
            {
                var result = QuestUtility.Evaluate(quest);
                if (result == null) continue;
                QuestStages.Add(new QuestStorage(result));
            }

        }

        // -- Timestopped -- //
        Lorebook.Add("isTimeStopped", $"{scr_System_Time.current.TimeStop}");

    }

    /// <summary>
    /// The faction a given faction should be grouped under in the hierarchical world-state dump.
    /// The world hub faction itself (Manageable_World, e.g. world_erav_modern_jp) is the root:
    /// every world-anchored faction (worldConnection) nests under the hub of the world it belongs
    /// to; a faction with no world of its own nests under the nearest world-anchored faction
    /// reachable through factionConnection (e.g. a door-connected film studio hangs off the office
    /// it connects to, preserving the chain: world hub -&gt; office -&gt; film studio); a subfaction
    /// (e.g. a mall shop) nests under its DIRECT parent (not the parent's own host - recursing
    /// past the parent would resolve the shop to the world hub and store it a second time next to
    /// the constructor-collected copy inside its parent's SubFactions). Returns the faction
    /// itself when it IS the hub or nothing resolves.
    /// </summary>
    static Manageable ResolveFactionHost(Manageable faction, HashSet<Manageable> visiting)
    {
        if (faction is Manageable_Subfaction sub && sub.Parent != null) return sub.Parent;

        if (faction is Manageable_World) return faction;

        if (faction.worldConnection.Count > 0 || !visiting.Add(faction))
        {
            var hub = scr_System_CampaignManager.current.FindFactionByID(faction.worldConnection[0]);
            return hub != null ? hub : faction;
        }

        foreach (var connected in faction.factionConnection)
        {
            // a world-anchored neighbor is this faction's direct host (not the neighbor's own hub,
            // which would flatten the chain) - recurse past worldless neighbors to their anchor
            if (connected.worldConnection.Count > 0) return connected;

            var anchor = ResolveFactionHost(connected, visiting);
            if (anchor != connected && !(anchor is Manageable_World)) return anchor;
        }

        return faction;
    }

}