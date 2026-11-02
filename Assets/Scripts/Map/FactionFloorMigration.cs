using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>Append authored floors to existing factions without replaying campaign initialization.</summary>
public static class FactionFloorMigration
{
    public static void Reconcile(Manageable faction, MapPlan plan)
    {
        // Do not reinterpret custom/replaced factions using a different world's template.
        if (plan == null || faction.mapPlanID != plan.ID || plan.floors.Count == 0) return;

        var campaign = scr_System_CampaignManager.current;
        // ManagedFloors alone includes shared tenant floors. Only this plan's physical host
        // may reuse its instances; other factions can legitimately use the same floor base.
        var floors = campaign.Map.Floors.Where(f => f.mapTemplateID == plan.ID
            && f.FindPhysicalHost() == faction).ToList();
        var instanceIDs = floors.Select(f => f.mapTemplateInstanceID).Distinct().ToList();
        if (instanceIDs.Count > 1)
        {
            Debug.LogWarning($"Faction [{faction.ID}] has ambiguous map instances; missing-floor migration skipped.");
            return;
        }

        int instanceID = floors.Count == 0 ? -1 : floors[0].mapTemplateInstanceID;
        if (instanceID < 0 && floors.Count > 0) instanceID = floors[0].refID;
        var matched = new HashSet<Floor_Instance>();
        foreach (var authored in plan.floors)
        {
            // Match occurrences, not just IDs: a plan may intentionally repeat a floor base.
            var existing = floors.Find(f => f.floorPlanID == authored.ID && !matched.Contains(f));
            if (existing != null)
            {
                matched.Add(existing);
                continue;
            }

            var floorBase = scr_System_Serializer.current.GetByNameOrID_Floor_Base(authored.ID);
            if (floorBase == null || !floorBase.isValid)
            {
                Debug.LogWarning($"Faction [{faction.ID}] missing authored floor [{authored.ID}] is invalid; migration skipped for this floor.");
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

        // These rooms are live in Map already, so evacuation can resolve the new rally point.
        if (plan.mainExit != null && !string.IsNullOrEmpty(plan.mainExit.roomID))
            faction.SetMainExit(plan.mainExit);
        faction.InvalidateMainExitCache();
    }
}
