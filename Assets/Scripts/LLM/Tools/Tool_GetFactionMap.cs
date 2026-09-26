using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Agent-mode tool: walk a faction's full territory (every managed floor/room/furniture) and every
/// managed character's current location/activity. Reuses the same public accessors
/// (Manageable.ManagedFloors/ManagedChara, Floor_Instance.rooms, Map.FindRoomByChara,
/// Character_Trainable.GetJobDescription) that LLM_WorldState's constructor already walks inline for
/// the current-room-only case (LLMUtils.cs:876-922) - exposed here as a standalone, faction-wide,
/// callable query instead. Deliberately not extracted as a shared static off LLM_WorldState itself,
/// to avoid touching that already-shipped, currently-relied-on code path.
/// </summary>
public class Tool_GetFactionMap : ILLMTool
{
    public string Name => "get_faction_map";

    class Args
    {
        /// <summary>Omit/null to inspect the faction that owns the player's current room.</summary>
        public string factionID = null;
    }

    class RoomEntry
    {
        public string roomName;
        public string furniture;
    }

    class CharaEntry
    {
        public string firstName;
        public int refID;
        public string currentLocation;
        public string currentlyDoing;
    }

    class Result
    {
        public string factionName;
        public Dictionary<string, List<RoomEntry>> floors = new Dictionary<string, List<RoomEntry>>();
        public List<CharaEntry> characters = new List<CharaEntry>();
    }

    public LLMToolDefinition GetDefinition()
    {
        var schema = new LLMFormatSchema();
        schema.properties["factionID"] = new LLMFormatSchema.Type_Simple("string", "ID of the faction to inspect. Omit to inspect the faction that owns the player's current room.");
        return new LLMToolDefinition(Name, "Fetch a faction's full territory map (every managed floor/room/furniture) and every managed character's current location and activity.", schema);
    }

    public IEnumerator Execute(LLMToolCallRequest call, Action<LLMToolResult> done)
    {
        Args args;
        try { args = JsonConvert.DeserializeObject<Args>(call.rawArgumentsJson) ?? new Args(); }
        catch { done?.Invoke(LLMToolResult.Error(call, "malformed arguments")); yield break; }

        Manageable faction;
        if (!string.IsNullOrEmpty(args.factionID))
        {
            faction = scr_System_CampaignManager.current.FindFactionByID(args.factionID);
        }
        else
        {
            faction = scr_System_CampaignManager.current.CurrentRoom?.FactionOwner as Manageable;
        }

        if (faction == null)
        {
            done?.Invoke(LLMToolResult.Error(call, string.IsNullOrEmpty(args.factionID)
                ? "player's current room has no owning faction"
                : $"no faction with ID '{args.factionID}'"));
            yield break;
        }

        var result = new Result { factionName = faction.FactionDisplayName };

        foreach (var floor in faction.ManagedFloors)
        {
            var rooms = new List<RoomEntry>();
            foreach (var room in floor.rooms)
            {
                rooms.Add(new RoomEntry { roomName = room.DisplayName, furniture = room.DisplayableFurnitureNames });
            }
            result.floors[floor.displayName] = rooms;
        }

        foreach (var c in faction.ManagedChara)
        {
            var room = scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID);
            result.characters.Add(new CharaEntry
            {
                firstName = c.FirstName,
                refID = c.RefID,
                currentLocation = room != null ? $"{(room.parentFloor != null ? $"{room.parentFloor.displayName}, " : "")}{room.DisplayName}" : "unknown",
                currentlyDoing = c.GetJobDescription()
            });
        }

        var json = JsonConvert.SerializeObject(result, UtilityEX.SerializerSettingsLLM);
        done?.Invoke(new LLMToolResult { callId = call.callId, toolName = Name, contentJson = json });
    }
}
