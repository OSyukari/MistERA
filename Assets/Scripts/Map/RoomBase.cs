using System.Collections.Generic;

public enum RoomActivityState { AlwaysActive, DayOnly, NightOnly }
public enum RoomGenderPreference { DontCare, Male, Female }

public class Room_Base
{
    public string ID = "";
    public string displayName = "";
    public float offsetX = 0f;
    public float offsetY = 0f;
    /// <summary>
    /// If true, this room has no placement on its floor's image - offsetX/offsetY are ignored and no map
    /// button/path line point is drawn for it (canvas_RoomDisplay, initscript_roomEdit). Purely visual: the room
    /// still appears in the room list and is fully connected/pathable via connects like any other room.
    /// </summary>
    public bool hideOnMap = false;
    /// <summary>Allows everyone to enter, overriding residence locks and all room membership requirements.</summary>
    public bool allowPublicAccess = false;
    /// <summary>
    /// If true, only characters belonging to this room's owning faction in any way (home, temporary home or
    /// work - see Character_Factions.Factions; no active-faction requirement) can path into it - see
    /// Room_Instance.CanBeAccessedBy, enforced by Map.Findpath(chara, room) and Map.FilterValidPathsOptimized.
    /// Only restricts rooms as a destination; a path may still pass through.
    /// </summary>
    public bool requireFactionMembership = false;
    /// <summary>Private residence inside a workplace: only home/temporary-home members may enter, not coworkers or venue guests.</summary>
    public bool requireHomeMembership = false;
    /// <summary>
    /// Residential district amenity (gym, lounge, residents' corridors): like requireFactionMembership, but residents of
    /// the district's generated homes may enter too (ResidenceUtility.HasRoomAccess / IsResidentOf).
    /// </summary>
    public bool residentsOnly = false;
    public List<Door_Base> connects = new List<Door_Base>();
    public List<string> furnitureIDs = new List<string>();
    public bool noCleaning = false;
    public string roomImagePath = "";
    public string roomImagePath_Night = "";
    public string roomImagePath_Inactive_Night = "";
    public string roomImagePath_Inactive = "";

    /// <summary>
    /// If set, this room is owned by the named faction instead of whatever faction the containing floor
    /// is attached to (e.g. a mall's shared directory floor where each room belongs to a different shop).
    /// The target faction is found-or-created on demand (scr_System_CampaignManager.FindorAddHomeFactionByID)
    /// at room-attachment time - see Manageable.AddToFaction(Floor_Instance,...). Purely a room→faction
    /// ownership assignment; unrelated to (and safe regardless of ordering against) the physical floor/room
    /// connectivity graphs (Map_Instance.BuildPath / Floor_Instance.BuildPath), which are built solely from
    /// Room_Base.connects / Map_Instance.factionFloorDoorConnections and never reference FactionOwner.
    /// </summary>
    public string subfactionOwnerOverwrite = "";

    public RoomActivityState activityState = RoomActivityState.AlwaysActive;

    /// <summary> optional image prompt tags of this room (scene prompts, see Room_Instance.PromptTags) </summary>
    public List<string> promptTags = new List<string>();
}
