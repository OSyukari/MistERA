using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Old saves (Docs/Prison/Plan_PrisonRework.md §11), run once while a save at or before MigratedUpToBuild loads:
/// <br/>- jail rooms were rooms holding prison furniture; now the marker makes one. Rooms of non-player factions holding
///   prison furniture get furniture_marker_prison (player rooms don't: the marker can't be removed by the player).
/// <br/>- "imprison" locked the character into the prison furniture and gave them its room. Now: unlocked, that room
///   ownership dropped; a prisoner of the furniture's faction is held on it as their cell. A non-prisoner is just unlocked.
/// </summary>
public static class PrisonMigration
{
    /// <summary> saves at or before this build hold the old room-based prisons </summary>
    public const int MigratedUpToBuild = 20261120;

    public static void Run(IEnumerable<Manageable> factions, IEnumerable<Character_Trainable> characters)
    {
        if (!SaveFile.LoadingSaveAtOrBefore(MigratedUpToBuild)) return;
        var map = scr_System_CampaignManager.current.Map;

        foreach (var faction in factions)
        {
            if (faction == null) continue;
            bool playerFaction = faction.isPlayerRelatedFaction;
            foreach (var roomRef in new List<int>(faction.ManagedRooms.Keys))
            {
                var room = map.GetRoomByRef(roomRef);
                if (room == null || room.isRoomPrison) continue;
                // a player room only when its map data now places the marker (e.g. Utnapishtim's isolation rooms)
                bool wanted = playerFaction
                    ? room.Base?.furnitureIDs?.Contains(Room_Instance.FurnitureID_PrisonMarker) == true
                    : room.HasPrisonFurniture() || room.Base?.furnitureIDs?.Contains(Room_Instance.FurnitureID_PrisonMarker) == true;
                if (!wanted) continue;
                room.AddFurniture(Room_Instance.FurnitureID_PrisonMarker);
                Debug.Log($"PrisonMigration: jail marker added to {room.DisplayNameShort} ({faction.ID})");
            }
        }

        var locked = new List<Character_Trainable>();
        foreach (var c in characters) if (c != null && c.FurnitureLockRef >= 0) locked.Add(c);
        foreach (var c in locked)
        {
            var job = scr_System_CampaignManager.current.FindJobInstanceByID(c.FurnitureLockRef, false) as Job_Furniture;
            if (job == null || job.ParentInstance?.FurnitureBase?.Tags?.Contains(Room_Instance.FurnitureTag_Prison) != true) continue;
            (job.Container as Job_Furniture.JobContainer_Chara)?.Extract(c.RefID);
            c.UnlockFurnitureJob();
            var faction = job.FactionOwner as Manageable;
            if (faction == null || job.ParentRoom == null) continue;
            faction.RemoveRoomOwnership(c.RefID, job.ParentRoom.RefID);
            if (PrisonUtility.IsPrisonerOf(c, faction)) faction.AssignCell(c, job);
            Debug.Log($"PrisonMigration: {c.FirstName} unlocked from {job.DisplayName}{(faction.TryGetCell(c.RefID, out _) ? ", held there as a cell" : "")}");
        }

        // every other prisoner (e.g. placed by npcInit) gets a free cell of their faction, if there is one
        foreach (var faction in factions)
        {
            if (faction == null) continue;
            foreach (var c in new List<Character_Trainable>(faction.ManagedChara))
                if (PrisonUtility.TryAutoAssignCell(faction, c)) Debug.Log($"PrisonMigration: {c.FirstName} given a cell in {faction.ID}");
        }
    }
}
