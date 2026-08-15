using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Synthetic faction representing a WorldPlan itself (ID = worldID). MainExit lazily builds and owns the
/// world's map-transit holding room (Map_Instance.CreateWorldTransitRoom) - the room a traveler is placed in
/// (never pathed through - see ActionPackage_PathTo.CheckWorldDoors) while a world-map crossing's travel time
/// elapses - so a character who somehow ends up stuck there can be pathed out like any other faction's world
/// door (Map_Instance.Findpath's cross-faction branch, GetFactionCrossingDoor/TryGetWorldMapTravelMinutes).
/// Every request for "the transit room of world X" should go through
/// scr_System_CampaignManager.FindOrAddWorldFaction(X).MainExit rather than reaching into Map_Instance
/// directly - that's the only place a Manageable_World is ever constructed.
/// </summary>
public class Manageable_World : Manageable
{
    public Manageable_World()
    {

    }

    // worldInit is the WorldPlan this faction represents - for now only its worldID is used, but future
    // per-world faction config (currency, member types, etc.) should be read off it here too, so every
    // Manageable_World creation path (FindOrAddWorldFaction is the only caller) picks it up uniformly.
    public Manageable_World(WorldPlan worldInit) : base(worldInit.worldID)
    {

    }

    /// <summary>
    /// Migration path for a save made before Manageable_World existed, where organizations[worldID] still
    /// held a plain Manageable (see FindOrAddWorldFaction) - logs loudly since this shouldn't happen for a
    /// save made with the current code, then self-heals: adopts oldFaction's existing transit room (if it
    /// already built one) as MainExit directly instead of building a second one under the same worldID.
    /// </summary>
    public Manageable_World(Manageable oldFaction) : base(oldFaction.ID)
    {
        Debug.LogError($"Manageable_World: [{oldFaction.ID}] was a {oldFaction.GetType().Name}, not Manageable_World - this save predates world-faction pathing. Migrating in place.");

        var existingRoom = scr_System_CampaignManager.current.Map.GetExistingWorldTransitRoom(this.ID);
        if (existingRoom != null)
        {
            existingRoom.FactionOwner = this;
            transitRoomCache = existingRoom;
            transitRoomRefID = existingRoom.RefID;
        }
    }

    // Persisted RefID so a reloaded Manageable_World already knows its own room without needing to consult
    // Map_Instance.worldTransitRoomRefs by worldID first - matches how every other cross-reference in this
    // codebase is stored (Room_Instance.factionOwnerRef, Manageable_Subfaction.parentID). transitRoomCache is
    // the in-memory object resolved from it (or freshly built), kept so repeated MainExit reads within a
    // session don't re-hit GetRoomByRef every time.
    [JsonProperty] int transitRoomRefID = -1;
    [JsonIgnore] Room_Instance transitRoomCache = null;

    [JsonIgnore]
    public override Room_Instance MainExit
    {
        get
        {
            if (transitRoomCache == null)
            {
                if (transitRoomRefID != -1) transitRoomCache = scr_System_CampaignManager.current.Map.GetRoomByRef(transitRoomRefID);

                if (transitRoomCache == null)
                {
                    transitRoomCache = scr_System_CampaignManager.current.Map.CreateWorldTransitRoom(this);
                    if (transitRoomCache != null) transitRoomRefID = transitRoomCache.RefID;
                }
            }
            return transitRoomCache;
        }
    }
}
