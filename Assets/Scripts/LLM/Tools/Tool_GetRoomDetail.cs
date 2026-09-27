using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Agent-mode tool: fetch a room's data via LLM_WorldState.RoomStorage (LLM/Worldstate/
/// RoomStorage.cs) - furniture/cleanliness/items/ongoing commands via RoomInfo, ownership via
/// OwnerFaction/RoomNameShort (prison/private-with-owners/plain, from DisplayNameShort), and
/// occupants via the cheap-vs-full-detail constructor split (full load embeds a per-character
/// CharaStorage for everyone present; cheap load is just name + RefID + current activity).
/// </summary>
public class Tool_GetRoomDetail : ILLMTool
{
    public string Name => "get_room_detail";

    class Args
    {
        /// <summary>Omit/null to inspect the player's current room.</summary>
        public int? roomRef = null;
        public bool fullDetail = false;
    }

    public LLMToolDefinition GetDefinition()
    {
        var schema = new LLMFormatSchema();
        schema.properties["roomRef"] = new LLMFormatSchema.Type_Simple("integer", "RefID of the room to inspect. Omit to inspect the player's current room.");
        schema.properties["fullDetail"] = new LLMFormatSchema.Type_Simple("boolean", "If false (default), occupants are listed with just full name, RefID, and current activity. If true, each occupant additionally gets a per-character detail entry (identity, statuses, attitude toward the player, equipment, and more) - use it when you need to know who exactly is in the room, not just that they are there.");
        return new LLMToolDefinition(Name, "Fetch a room's data: furniture, cleanliness, items, ongoing commands, ownership, and occupants.", schema);
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

        var storage = new LLM_WorldState.RoomStorage(room, args.fullDetail);

        var json = JsonConvert.SerializeObject(storage, UtilityEX.SerializerSettingsLLM);
        done?.Invoke(new LLMToolResult { callId = call.callId, toolName = Name, contentJson = json });
    }
}
