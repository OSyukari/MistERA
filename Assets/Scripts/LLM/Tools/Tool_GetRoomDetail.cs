using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Agent-mode tool: fetch furniture/occupants/ownership for a room, plus (for the player's current
/// room only) the available interactive commands - reuses the exact same building blocks
/// LLM_WorldState's constructor already uses for its CurrentRoomInfo string and PossibleInteractions
/// dump (LLMUtils.cs), just exposed as an on-demand, callable query instead of an always-included
/// upfront prompt section.
/// </summary>
public class Tool_GetRoomDetail : ILLMTool
{
    public string Name => "get_room_detail";

    class Args
    {
        /// <summary>Omit/null to inspect the player's current room.</summary>
        public int? roomRef = null;
    }

    class Result
    {
        public string roomName;
        public string furniture;
        public string cleanliness;
        public string items;
        public List<string> charactersPresent = new List<string>();
        public string ongoingCommands;
        public string ownerFaction;
        public List<string> roomOwners;
        public bool isPrison;
        public bool isPrivate;

        /// <summary>
        /// Only populated when the resolved room is the player's current room - available commands
        /// are computed relative to the player's own position/interactions (LLMUtils.CollectCOMInfo),
        /// so they aren't meaningful for an arbitrary room elsewhere in the faction.
        /// </summary>
        public Dictionary<string, Dictionary<string, Dictionary<string, LLMUtils.SerializedAP>>> possibleCommands = null;
    }

    public LLMToolDefinition GetDefinition()
    {
        var schema = new LLMFormatSchema();
        schema.properties["roomRef"] = new LLMFormatSchema.Type_Simple("integer", "RefID of the room to inspect. Omit to inspect the player's current room.");
        return new LLMToolDefinition(Name, "Fetch furniture, occupants, cleanliness, and ownership for a room. When the room is the player's current room, also returns the commands currently available there.", schema);
    }

    public IEnumerator Execute(LLMToolCallRequest call, Action<LLMToolResult> done)
    {
        Args args;
        try { args = JsonConvert.DeserializeObject<Args>(call.rawArgumentsJson) ?? new Args(); }
        catch { done?.Invoke(LLMToolResult.Error(call, "malformed arguments")); yield break; }

        var mgr = scr_System_CampaignManager.current;
        var room = args.roomRef.HasValue ? mgr.Map.GetRoomByRef(args.roomRef.Value) : mgr.CurrentRoom;
        if (room == null)
        {
            done?.Invoke(LLMToolResult.Error(call, args.roomRef.HasValue
                ? $"no room with RefID {args.roomRef.Value}"
                : "player has no current room"));
            yield break;
        }

        var result = new Result
        {
            roomName = room.DisplayName,
            furniture = room.DisplayableFurnitureNames,
            cleanliness = room.RoomCleanliness().ToString(),
            items = room.Inventory.Contents.Count > 0 ? room.Inventory.PrintContent() : "no item",
            isPrison = room.isRoomPrison,
            isPrivate = room.isRoomPrivate
        };
        foreach (var c in room.RoomChara) result.charactersPresent.Add(c.FirstName);

        if (room.FactionOwner != null)
        {
            result.ownerFaction = room.FactionOwner.FactionDisplayName;
            result.roomOwners = room.OwnerNames;
        }

        bool isCurrentRoom = room == mgr.CurrentRoom;
        if (isCurrentRoom)
        {
            var aps = new List<string>();
            foreach (var ap in mgr.GetRegisteredAPByRoom(room.RefID, false))
            {
                if (ap.job.isPlayerRelatedJob) continue;
                if (ap.isTemporaryAP) continue;
                aps.Add(ap.DescriptionText());
            }
            result.ongoingCommands = aps.Count > 0 ? String.Join("\n", aps) : "no ongoing";

            result.possibleCommands = new Dictionary<string, Dictionary<string, Dictionary<string, LLMUtils.SerializedAP>>>();
            LLMUtils.CollectCOMInfo(result.possibleCommands, room);
        }

        var json = JsonConvert.SerializeObject(result, UtilityEX.SerializerSettingsLLM);
        done?.Invoke(new LLMToolResult { callId = call.callId, toolName = Name, contentJson = json });
    }
}
