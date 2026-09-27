using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Agent-mode tool: fetch a faction's data by faction ID. Thin wrapper over
/// LLM_WorldState.FactionStorage's cheap-vs-full-detail constructor split
/// (LLM/Worldstate/FactionStorage.cs). The cheap load returns only the most context-relevant
/// data (identity + travel time from the player's current room); the full load adds the member
/// list, territory map, lorebook, and (for player-managed factions) management/financial/
/// inventory reports. Omitting factionID inspects the faction owning the player's current room,
/// same fallback the old get_faction_map tool used.
/// </summary>
public class Tool_GetFactionDetail : ILLMTool
{
    public string Name => "get_faction_detail";

    class Args
    {
        /// <summary>Omit/null to inspect the faction that owns the player's current room.</summary>
        public string factionID = null;
        public bool fullDetail = false;
    }

    public LLMToolDefinition GetDefinition()
    {
        var schema = new LLMFormatSchema();
        schema.properties["factionID"] = new LLMFormatSchema.Type_Simple("string", "ID of the faction to inspect. Omit to inspect the faction that owns the player's current room.");
        schema.properties["fullDetail"] = new LLMFormatSchema.Type_Simple("boolean", "If false (default), returns only the most context-relevant data: the faction's ID, display name, and travel time from the player's current room. If true, additionally returns every managed member with social standing and current location/activity, the full territory map (every managed floor/room with details), lorebook entries, financials (billing/debt/income), and - for player-managed factions - management logs and inventory. Use full detail when you need the layout, staffing, or books of the faction; the default is much cheaper and usually enough to decide whether a closer look is worth it.");
        return new LLMToolDefinition(Name, "Fetch a faction's data by faction ID. By default loads only the faction's identity and travel time from the player's current room; set fullDetail=true to also load the member list, territory map, lorebook, financials, and (player-managed factions) management logs and inventory.", schema);
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

        var storage = new LLM_WorldState.FactionStorage(faction, args.fullDetail);

        var json = JsonConvert.SerializeObject(storage, UtilityEX.SerializerSettingsLLM);
        done?.Invoke(new LLMToolResult { callId = call.callId, toolName = Name, contentJson = json });
    }
}
