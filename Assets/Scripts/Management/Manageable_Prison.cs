using System.Collections.Generic;
using Newtonsoft.Json;
using QuikGraph;
using UnityEngine;

/// <summary>
/// A faction's prison state - Docs/Prison/Plan_PrisonRework.md:
/// <br/>- cells (§3): prisoners held on a prison bed. Stored on the room, never as room owners. A prison bed holding a
///   prisoner is a cell and leaves the room's normal bed matching; with nobody held it is a normal bed.
/// <br/>- jail blocks (§4): this faction's jail rooms (furniture_marker_prison) grouped by the doors between them.
/// <br/>- lock overrides (§8): while locked in place, a character counts as another MemberType here.
/// </summary>
public partial class Manageable
{
    // ---------------- cells ---------------- //

    public const string CellRefusal_NoBed = "ui_prison_cell_noBed";
    public const string CellRefusal_NotPrisonBed = "ui_prison_cell_notPrisonBed";
    public const string CellRefusal_NotManaged = "ui_prison_cell_notManaged";
    public const string CellRefusal_Full = "ui_prison_cell_full";
    public const string CellRefusal_Occupied = "ui_prison_cell_occupied";

    /// <summary> roomRef -> (held charaRef -> prison bed's Job_Furniture RefID) </summary>
    [JsonProperty] Dictionary<int, Dictionary<int, int>> prisonCells = null;
    /// <summary> charaRef -> roomRef of their cell; rebuilt from prisonCells </summary>
    [JsonIgnore] Dictionary<int, int> _cellRoomByChara = null;

    Dictionary<int, int> CellRoomByChara
    {
        get
        {
            if (_cellRoomByChara == null)
            {
                _cellRoomByChara = new Dictionary<int, int>();
                if (prisonCells != null)
                    foreach (var room in prisonCells)
                        foreach (var cell in room.Value) _cellRoomByChara[cell.Key] = room.Key;
            }
            return _cellRoomByChara;
        }
    }

    /// <summary> prison bed: an ownership bed tagged prison, or any ownership bed in a jail room </summary>
    public static bool IsPrisonBed(Job_Furniture bed)
    {
        if (bed == null || !bed.AssignsOwnership) return false;
        if (bed.ParentInstance?.FurnitureBase?.Tags?.Contains(Room_Instance.FurnitureTag_Prison) == true) return true;
        return bed.ParentRoom != null && bed.ParentRoom.isRoomPrison;
    }

    /// <summary> charaRef's cell here. A ref that no longer resolves in its room counts as none (DropStaleCells clears it) </summary>
    public bool TryGetCell(int charaRef, out Job_Furniture bed)
    {
        bed = null;
        if (prisonCells == null || !CellRoomByChara.TryGetValue(charaRef, out int roomRef)) return false;
        if (!prisonCells.TryGetValue(roomRef, out var cells) || !cells.TryGetValue(charaRef, out int jobRef)) return false;
        bed = scr_System_CampaignManager.current.FindJobInstanceByID(jobRef, false) as Job_Furniture;
        if (bed == null || bed.ParentRoom == null || bed.ParentRoom.RefID != roomRef) { bed = null; return false; }
        return true;
    }

    /// <summary> the bed holds a prisoner (cell mode): out of the room's normal bed matching </summary>
    public bool IsCellBed(int roomRef, int jobRef)
    {
        if (prisonCells == null || !prisonCells.TryGetValue(roomRef, out var cells)) return false;
        foreach (var cell in cells) if (cell.Value == jobRef) return true;
        return false;
    }

    /// <summary> charaRef is held in a cell of roomRef </summary>
    public bool IsHeldInRoom(int roomRef, int charaRef)
    {
        return prisonCells != null && prisonCells.TryGetValue(roomRef, out var cells) && cells.ContainsKey(charaRef);
    }

    /// <summary> appends the prisoners held on the bed </summary>
    public void GetCellPrisoners(int roomRef, int jobRef, List<int> into)
    {
        if (prisonCells == null || !prisonCells.TryGetValue(roomRef, out var cells)) return;
        foreach (var cell in cells) if (cell.Value == jobRef) into.Add(cell.Key);
    }

    /// <summary>
    /// Why c cannot be held on bed here, as a localization key; "" = they can. A bed with normal owners is fine (they are
    /// re-matched), unless someone else lies in it right now - the matching never moves a sleeper.
    /// </summary>
    public string GetAssignCellRefusal(Character_Trainable c, Job_Furniture bed)
    {
        if (c == null || bed == null) return CellRefusal_NoBed;
        if (!IsPrisonBed(bed)) return CellRefusal_NotPrisonBed;
        var room = bed.ParentRoom;
        if (room == null || !managedRoomRefs.ContainsKey(room.RefID)) return CellRefusal_NotManaged;
        int held = 0;
        if (prisonCells != null && prisonCells.TryGetValue(room.RefID, out var cells))
            foreach (var cell in cells)
            {
                if (cell.Value != bed.RefID) continue;
                if (cell.Key == c.RefID) return "";
                held++;
            }
        if (held >= bed.Capacity) return CellRefusal_Full;
        if (held == 0)
        {
            var occupants = bed.OccupantRefs;
            for (int i = 0; i < occupants.Count; i++) if (occupants[i] != c.RefID) return CellRefusal_Occupied;
        }
        return "";
    }

    /// <summary>
    /// Holds c on bed (their previous cell here is released). Prisoners hold cells, never rooms: c's room ownership here
    /// goes. The bed leaves normal matching, so its normal owners move to other beds of the room or lose the room.
    /// </summary>
    public bool AssignCell(Character_Trainable c, Job_Furniture bed)
    {
        string refusal = GetAssignCellRefusal(c, bed);
        if (refusal != "")
        {
            Debug.LogWarning($"AssignCell: {c?.FirstName} cannot be held on [{bed?.RefID}] in {ID}: {refusal}");
            return false;
        }
        if (TryGetCell(c.RefID, out var current) && current == bed) return true;
        ReleaseCell(c.RefID);

        if (OwnedRoomByChara.TryGetValue(c.RefID, out var owned))
            for (int i = owned.Count - 1; i >= 0; i--) RemoveRoomOwnership(c.RefID, owned[i]);

        var room = bed.ParentRoom;
        if (prisonCells == null) prisonCells = new Dictionary<int, Dictionary<int, int>>();
        if (!prisonCells.TryGetValue(room.RefID, out var cells)) prisonCells.Add(room.RefID, cells = new Dictionary<int, int>());
        cells[c.RefID] = bed.RefID;
        _cellRoomByChara = null;

        SyncCellBed(room.RefID, bed);
        room.ReassignFurnitureOwnership();
        room.ReleaseOwnersWithoutBed();
        return true;
    }

    /// <summary> charaRef leaves their cell here; a bed left with nobody held is a normal bed again </summary>
    public void ReleaseCell(int charaRef)
    {
        if (prisonCells == null || !CellRoomByChara.TryGetValue(charaRef, out int roomRef)) return;
        if (!prisonCells.TryGetValue(roomRef, out var cells) || !cells.TryGetValue(charaRef, out int jobRef)) return;
        cells.Remove(charaRef);
        if (cells.Count == 0) prisonCells.Remove(roomRef);
        _cellRoomByChara = null;

        var room = scr_System_CampaignManager.current.Map.GetRoomByRef(roomRef);
        var bed = scr_System_CampaignManager.current.FindJobInstanceByID(jobRef, false) as Job_Furniture;
        if (room == null || bed == null || bed.ParentRoom != room) return;
        SyncCellBed(roomRef, bed);
        room.ReassignFurnitureOwnership();
    }

    /// <summary> the bed's furniture owners = its held prisoners (empty when it is a normal bed again) </summary>
    void SyncCellBed(int roomRef, Job_Furniture bed)
    {
        var held = new List<int>();
        GetCellPrisoners(roomRef, bed.RefID, held);
        bed.Ownership_Set(held);
    }

    /// <summary> Room_Instance.ReassignFurnitureOwnership: the cell beds of room keep their held prisoners as owners </summary>
    internal void SyncCellBeds(Room_Instance room)
    {
        if (room == null || prisonCells == null || !prisonCells.TryGetValue(room.RefID, out var cells) || cells.Count == 0) return;
        var held = new List<int>();
        foreach (var furniture in room.Furnitures)
        {
            var bed = furniture?.JobGiver;
            if (bed == null || !bed.AssignsOwnership) continue;
            held.Clear();
            GetCellPrisoners(room.RefID, bed.RefID, held);
            if (held.Count == 0) continue;
            var current = bed.OwnerRefs;   // called while the room is assigning: no re-entry
            bool same = current.Count == held.Count;
            for (int i = 0; same && i < held.Count; i++) same = current.Contains(held[i]);
            if (!same) bed.Ownership_Set(held);
        }
    }

    /// <summary> NotifyFurnitureChange: cells on a bed that left the room are dropped </summary>
    void DropStaleCells(Room_Instance room)
    {
        if (room == null || prisonCells == null || !prisonCells.TryGetValue(room.RefID, out var cells)) return;
        List<int> stale = null;
        foreach (var cell in cells)
        {
            var bed = scr_System_CampaignManager.current.FindJobInstanceByID(cell.Value, false) as Job_Furniture;
            if (bed != null && bed.ParentRoom == room && IsPrisonBed(bed)) continue;
            if (stale == null) stale = new List<int>();
            stale.Add(cell.Key);
        }
        if (stale == null) return;
        for (int i = 0; i < stale.Count; i++) cells.Remove(stale[i]);
        if (cells.Count == 0) prisonCells.Remove(room.RefID);
        _cellRoomByChara = null;
    }

    /// <summary> RemoveManagedRoom: its cells go with it </summary>
    void DropRoomCells(int roomRef)
    {
        if (prisonCells == null || !prisonCells.Remove(roomRef)) return;
        _cellRoomByChara = null;
    }

    // ---------------- jail blocks ---------------- //

    /// <summary> jail roomRef -> its block (the jail rooms reachable from it through doors between jail rooms) </summary>
    [JsonIgnore] Dictionary<int, List<int>> _jailBlockByRoom = null;
    [JsonIgnore] int _jailBlocksSerial = -1;

    /// <summary> rooms added or removed: rebuild on next read (furniture changes are caught by facilitySerial) </summary>
    internal void InvalidateJailBlocks() { _jailBlockByRoom = null; }

    /// <summary>
    /// Appends the jail block of roomRef (itself included); nothing when it is not one of this faction's jail rooms.
    /// Doors within a floor only (Floor.Graph).
    /// </summary>
    public void GetJailBlock(int roomRef, List<int> into)
    {
        EnsureJailBlocks();
        if (_jailBlockByRoom.TryGetValue(roomRef, out var block)) into.AddRange(block);
    }

    void EnsureJailBlocks()
    {
        if (_jailBlockByRoom != null && _jailBlocksSerial == facilitySerial) return;
        _jailBlockByRoom = new Dictionary<int, List<int>>();
        _jailBlocksSerial = facilitySerial;
        var map = scr_System_CampaignManager.current.Map;
        var queue = new Queue<int>();
        foreach (var roomRef in managedRoomRefs.Keys)
        {
            if (_jailBlockByRoom.ContainsKey(roomRef)) continue;
            var room = map.GetRoomByRef(roomRef);
            if (room == null || !room.isRoomPrison) continue;
            var block = new List<int>();
            _jailBlockByRoom.Add(roomRef, block);
            queue.Enqueue(roomRef);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                block.Add(current);
                var graph = map.GetRoomByRef(current)?.parentFloor?.Graph;
                if (graph == null || !graph.TryGetOutEdges(current, out var edges)) continue;
                foreach (var edge in edges)
                {
                    int next = edge.Target;
                    if (_jailBlockByRoom.ContainsKey(next) || !managedRoomRefs.ContainsKey(next)) continue;
                    var nextRoom = map.GetRoomByRef(next);
                    if (nextRoom == null || !nextRoom.isRoomPrison) continue;
                    _jailBlockByRoom.Add(next, block);
                    queue.Enqueue(next);
                }
            }
        }
    }

    // ---------------- lock overrides ---------------- //

    public class LockOverride
    {
        public string typeID = "";
        /// <summary> the lock made them a member here (temporary home); unlocking removes them </summary>
        public bool addedByLock = false;
    }

    /// <summary> charaRef -> MemberType they count as while locked in place here </summary>
    [JsonProperty] Dictionary<int, LockOverride> lockOverrides = null;

    public bool HasLockOverride(int charaRef) { return lockOverrides != null && lockOverrides.ContainsKey(charaRef); }

    bool TryGetLockOverrideType(int charaRef, out MemberType type)
    {
        type = null;
        return lockOverrides != null && lockOverrides.TryGetValue(charaRef, out var lo) && FactionUtility.TryGetMemberType(lo.typeID, out type);
    }

    /// <summary> stored MemberType ID, lock override first </summary>
    string EffectiveGuestStatusID(int charaRef, string stored)
    {
        if (lockOverrides != null && lockOverrides.TryGetValue(charaRef, out var lo) && !string.IsNullOrEmpty(lo.typeID)) return lo.typeID;
        return stored;
    }

    /// <summary>
    /// c counts as type here while locked. A non-member becomes one for the lock (this faction their temporary home,
    /// holding type); unlocking removes them again.
    /// </summary>
    public void SetLockOverride(Character_Trainable c, MemberType type)
    {
        if (c == null || type == null) return;
        type = type.ResolveJoinTarget();
        bool added = false;
        if (!isManagedChara(c.RefID))
        {
            c.FactionManager.SetTempHomeFaction(ID, type, false);
            added = true;
        }
        if (lockOverrides == null) lockOverrides = new Dictionary<int, LockOverride>();
        lockOverrides[c.RefID] = new LockOverride() { typeID = type.ID, addedByLock = added };
        OnLockOverrideChanged(c);
    }

    /// <summary> c resumes their own MemberType here; one the lock made a member leaves </summary>
    public void ClearLockOverride(Character_Trainable c)
    {
        if (c == null || lockOverrides == null || !lockOverrides.TryGetValue(c.RefID, out var lo)) return;
        lockOverrides.Remove(c.RefID);
        if (lo.addedByLock && c.FactionManager.Faction_Home_Temporary == this) c.FactionManager.SetTempHomeFaction("", null);
        else OnLockOverrideChanged(c);
    }

    /// <summary> AddToFaction with a real status: a member the lock added is a real member now - unlocking keeps them </summary>
    void KeepLockAddedMember(int charaRef)
    {
        if (lockOverrides != null && lockOverrides.TryGetValue(charaRef, out var lo)) lo.addedByLock = false;
    }

    void OnLockOverrideChanged(Character_Trainable c)
    {
        _managerRefs = null;
        NotifyBedPreferenceChanged(c.RefID);
        NotifyFactionMemberChange();
    }
}
