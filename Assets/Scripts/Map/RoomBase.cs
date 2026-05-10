using System.Collections.Generic;

public enum RoomActivityState { AlwaysActive, DayOnly, NightOnly }

public class Room_Base
{
    public string ID = "";
    public string displayName = "";
    public float offsetX = 0f;
    public float offsetY = 0f;
    public List<Door_Base> connects = new List<Door_Base>();
    public List<string> furnitureIDs = new List<string>();
    public bool noCleaning = false;
    public string roomImagePath = "";
    public RoomActivityState activityState = RoomActivityState.AlwaysActive;
}
