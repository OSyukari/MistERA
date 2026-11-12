using System.Collections.Generic;
using Newtonsoft.Json;

public class FurnitureBase
{
    public string ID = "";
    public string bedTypeID = "";
    [JsonIgnore] public string BedTypeID => !string.IsNullOrEmpty(bedTypeID) ? bedTypeID
        : ID != null && ID.Contains("furniture_bed") ? BedPreferenceUtility.HumanType : "";
    [JsonIgnore] public bool IsClassifiedBed => !string.IsNullOrEmpty(BedTypeID);
    [JsonIgnore] BedPriority? cachedBedPriority;
    [JsonIgnore] public BedPriority BedPriority
    {
        get
        {
            if (!cachedBedPriority.HasValue) cachedBedPriority = BedPreferenceUtility.GetPriority(BedTypeID);
            return cachedBedPriority.Value;
        }
    }
    public string displayName = "";
    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            return LocalizeDictionary.QueryThenParse(ID, displayName);
        }
    }
    // recipe
    public float furnitureSize = 0f;
    public float value = 0f;
    public List<string> Tags = new List<string>();
    public List<Furniture_COMGiver> givesJob = new List<Furniture_COMGiver>();
    public bool noDisplay = false;
    /// <summary> soft character container settings (beds, sofas, room dividers) - null = none. See FurnitureOccupancyDef </summary>
    public FurnitureOccupancyDef occupancy = null;
    [JsonIgnore] public bool isJobGiver { get { return this.givesJob.Count > 0; } }
    [JsonIgnore] public bool HasOccupancy { get { return occupancy != null; } }

    [JsonIgnore]
    public bool isValid
    {
        get
        {
            if (this.ID != "") return true;
            return false;
        }
    }

    public void OnAfterDeserialize()
    {
        cachedBedPriority = null;
    }

    public void OnBeforeSerialize()
    {

    }

    public class Furniture_COMGiver
    {
        public List<string> comID = new List<string>();
        public List<string> comTags = new List<string>();
    }

}

/// <summary>
/// Soft character container of a furniture (Docs/Furniture/Plan_FurnitureOccupancy.md). Characters are kept in it
/// without being registered on its job: a sleeper lying in a bed, or a character inside a room divider (stall).
/// Runtime state lives on Job_Furniture (occupants / owners), the character keeps a ref back (furnitureLocationRef).
/// </summary>
public class FurnitureOccupancyDef
{
    /// <summary> -1 = (int)furnitureSize. Only gates the find-job path; a direct entry is never blocked </summary>
    public int capacity = -1;
    /// <summary> keeps a character who falls asleep here / is put down here (bed, sofa) </summary>
    public bool holdsUnconscious = false;
    /// <summary> has an inside, entered and left explicitly (stall, private bathroom, jail cell) </summary>
    public bool isRoomDivider = false;
    /// <summary> divider only: characters inside and outside do not see each other </summary>
    public bool blocksVisibility = false;
    /// <summary> divider only: may host its own sex job </summary>
    public bool allowSexJob = false;
    /// <summary> receives owners from the room's ownership (beds) </summary>
    public bool assignOwnership = false;
}
