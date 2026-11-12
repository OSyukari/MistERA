using System.Collections.Generic;
using UnityEngine;

/// <summary>Reconcile authored floors and standalone rooms at setup/load without replaying campaign initialization.</summary>
public static class FactionFloorMigration
{
    public static void Reconcile(Manageable faction, MapPlan plan)
    {
        // Do not reinterpret custom/replaced factions using a different world's template.
        if (plan == null || faction.mapPlanID != plan.ID || plan.isLocationless) return;

        var campaign = scr_System_CampaignManager.current;
        // ManagedFloors alone includes shared tenant floors. Only this plan's physical host
        // may reuse its instances; other factions can legitimately use the same floor base.
        var floors = new List<Floor_Instance>();
        var instanceIDs = new HashSet<int>();
        foreach (var floor in campaign.Map.Floors)
            if (floor.mapTemplateID == plan.ID && floor.FindPhysicalHost() == faction)
            {
                floors.Add(floor);
                instanceIDs.Add(floor.mapTemplateInstanceID);
            }

        int instanceID = floors.Count == 0 ? -1 : floors[0].mapTemplateInstanceID;
        if (instanceID < 0 && floors.Count > 0) instanceID = floors[0].refID;
        var matched = new HashSet<Floor_Instance>();
        foreach (var authored in plan.floors)
        {
            // Match occurrences, not just IDs: a plan may intentionally repeat a floor base.
            Floor_Instance existing = null;
            var sources = new List<Floor_Instance>();
            for (int i = 0; i < floors.Count; i++)
            {
                var candidate = floors[i];
                if (matched.Contains(candidate)) continue;
                if (candidate.floorPlanID == authored.ID && existing == null) existing = candidate;
                else if (!string.IsNullOrEmpty(authored.migrateFromFloorID) && authored.migrateFromFloorID != authored.ID
                    && candidate.floorPlanID == authored.migrateFromFloorID) sources.Add(candidate);
            }
            if (existing != null)
            {
                matched.Add(existing);
                if (sources.Count > 0 && !existing.migrationWarningReported)
                {
                    existing.migrationWarningReported = true;
                    Debug.LogWarning($"Faction [{faction.ID}] has both migration source [{authored.migrateFromFloorID}] and target [{authored.ID}]; using the existing target for authored-floor matching. Both floors and their furniture are retained; automatic merging and exit repair are not supported.");
                }
                continue;
            }

            // Existing targets can still be matched when an earlier append left multiple
            // map instances behind, but no source may be chosen or new floor added then.
            if (instanceIDs.Count > 1)
            {
                if (!floors[0].migrationWarningReported)
                {
                    floors[0].migrationWarningReported = true;
                    Debug.LogWarning($"Faction [{faction.ID}] has ambiguous map instances; missing-floor migration skipped. Existing floors retained.");
                }
                continue;
            }

            var floorBase = scr_System_Serializer.current.GetByNameOrID_Floor_Base(authored.ID);
            if (floorBase == null || !floorBase.isValid)
            {
                Debug.LogWarning($"Faction [{faction.ID}] missing authored floor [{authored.ID}] is invalid; migration skipped for this floor.");
                continue;
            }

            if (sources.Count > 0)
            {
                var source = sources[0];
                if (sources.Count != 1 || !source.RetargetTemplateIdentity(floorBase))
                {
                    if (!source.migrationWarningReported)
                    {
                        source.migrationWarningReported = true;
                        Debug.LogWarning($"Faction [{faction.ID}] cannot safely retarget source [{authored.migrateFromFloorID}] to [{authored.ID}]: source is ambiguous or room identities are incompatible. Existing floors retained; replacement not appended.");
                    }
                    continue;
                }
                matched.Add(source);
                // Registries are live at these setup/load call sites. Use the same authored
                // room-change path as load reconciliation, without replaying initializers.
                source.PreparePendingRoomAdditions();
                source.ApplyPendingRoomChanges();
                Debug.Log($"Faction [{faction.ID}] retargeted floor [{source.refID}] from [{authored.migrateFromFloorID}] to [{authored.ID}] in place.");
                continue;
            }

            // Only the new rooms' furniture/jobs are initialized. In particular, do not run
            // MapPlan initializers or floor.Additional (which can move/spawn saved actors).
            var floor = campaign.Register(new Floor_Instance(floorBase, authored.nameOverwrite));
            if (instanceID < 0) instanceID = floor.refID;
            floor.RegisterMapTemplate(plan.ID, instanceID);
            faction.AddToFaction(floor, isRenting: plan.isRentingFloor, landlordFactionID: plan.landlordFactionID);
            Debug.Log($"Faction [{faction.ID}] restored missing authored floor [{authored.ID}] as [{floor.refID}].");
        }

        if (plan.rooms != null)
            foreach (var definition in plan.rooms)
            {
                var templateFloor = definition == null || string.IsNullOrEmpty(definition.floorID)
                    ? null : scr_System_Serializer.current.GetByNameOrID_Floor_Base(definition.floorID);
                var rb = templateFloor?.rooms?.Find(r => r != null && r.ID == definition.roomID);
                if (rb == null)
                {
                    Debug.LogWarning($"Faction [{faction.ID}] authored standalone room [{definition?.floorID}/{definition?.roomID}] is invalid; migration skipped.");
                    continue;
                }

                Room_Instance existing = null;
                bool ambiguous = false;
                var sourceTemplate = string.IsNullOrEmpty(definition.migrateFromFloorID)
                    ? null : scr_System_Serializer.current.GetByNameOrID_Floor_Base(definition.migrateFromFloorID);
                var sourceRoom = sourceTemplate?.GetRoom(definition.roomID);
                foreach (var candidate in faction.ManagedRooms.Values)
                {
                    if (candidate == null || candidate.FactionOwner != faction
                        || candidate.TemplateRoomID != definition.roomID) continue;
                    // Saved floor/room IDs still identify a source whose template (and Base) was removed.
                    bool isSource = !string.IsNullOrEmpty(definition.migrateFromFloorID)
                        && ((sourceRoom != null && candidate.Base == sourceRoom)
                            || (!candidate.isStandalone && candidate.parentFloor?.floorPlanID == definition.migrateFromFloorID));
                    if (candidate.Base != rb && !isSource) continue;
                    if (existing != null) { ambiguous = true; break; }
                    existing = candidate;
                }
                if (ambiguous)
                {
                    Debug.LogWarning($"Faction [{faction.ID}] has ambiguous owned standalone room [{definition.floorID}/{definition.roomID}]; existing rooms retained.");
                    continue;
                }
                if (existing != null)
                {
                    // Attached standalone rooms have a district parentFloor; never convert that floor.
                    if (existing.isStandalone)
                    {
                        if (existing.Base != rb) existing.ChangeTemplateFloorID(definition.floorID);
                        continue;
                    }
                    if (string.IsNullOrEmpty(definition.migrateFromFloorID)) continue;
                    var oldFloor = existing.parentFloor;
                    if (oldFloor == null || oldFloor.floorPlanID != definition.migrateFromFloorID
                        || oldFloor.mapTemplateID != plan.ID || oldFloor.FindPhysicalHost() != faction
                        || oldFloor.rooms.Count != 1 || oldFloor.rooms[0] != existing)
                    {
                        Debug.LogWarning($"Faction [{faction.ID}] cannot safely convert owned room [{existing.RefID}] from floor [{definition.migrateFromFloorID}]: source is not this plan's single-room physical host. Existing room retained; replacement not appended.");
                        continue;
                    }
                    if (!campaign.Map.ConvertToStandaloneRoom(existing, oldFloor))
                    {
                        Debug.LogWarning($"Faction [{faction.ID}] could not convert room [{existing.RefID}] to standalone; existing room retained; replacement not appended.");
                        continue;
                    }
                    existing.ChangeTemplateFloorID(definition.floorID);
                    // Conversion removes oldFloor from Map. Its queued removals must not be applied:
                    // Map.ApplyPendingRoomChanges takes a fresh floor list, excluding this old floor.
                    Debug.Log($"Faction [{faction.ID}] converted room [{existing.RefID}] from floor [{definition.migrateFromFloorID}] to standalone in place.");
                    continue;
                }

                // Restore only missing premises; saved characters and ownership are not replayed.
                var room = new Room_Instance(templateFloor, rb) { isStandalone = true };
                campaign.Register(room);
                faction.AddToFaction(room, isRenting: false);
                Debug.Log($"Faction [{faction.ID}] restored missing authored standalone room [{definition.floorID}/{definition.roomID}] as [{room.RefID}].");
            }

        // These rooms are live in Map already, so evacuation can resolve the new rally point.
        if (plan.mainExit != null && !string.IsNullOrEmpty(plan.mainExit.roomID))
            faction.SetMainExit(plan.mainExit);
        faction.InvalidateMainExitCache();
    }
}
