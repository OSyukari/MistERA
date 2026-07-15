using System;
using System.Collections.Generic;
using Newtonsoft.Json;


public interface I_IsJobGiver
{
    public List<Job_Furniture> GetValidJobs_Jobs(Character_Trainable chara, int currentHour, ref string s);
    [JsonIgnore]
    public string FactionDisplayName { get; }
    public List<Job_CharaCOM> GetValidCharaCOMByTag(Character_Trainable chara, string tag, ref string ss, bool restrainedOnly = true);
    public List<Job_Furniture> GetValidJobs_Heuristics(
        Func<Job_Furniture, Character_Trainable, Dictionary<int, float>, float> heuristic,
        int maxCount,
        Character_Trainable chara,
        int currentHour,
        PathingRoomFilter filter,
        string comIDOverride = "",
        string tagoverride = "",
        List<string> s = null,
        List<int> restrictRoomList = null);

    public void NotifyFurnitureChange(Room_Instance room);
    public List<int> RoomOwners(int roomRef);
    [JsonIgnore] public List<Floor_Instance> ManagedFloors { get; }
    [JsonIgnore] public List<Manageable> ConnectedFactions { get; }
    [JsonIgnore] public Room_Instance MainExit { get; }
    [JsonIgnore] public FactionInventory Inventory { get; }
    [JsonIgnore] public List<Character_Trainable> Managers { get; }
    [JsonIgnore] public List<Character_Trainable> ManagedChara { get; }
    [JsonIgnore] public bool isPlayerFaction { get; }
    [JsonIgnore] public bool isPlayerRelatedFaction { get; }
    public Manageable_GuestStatus GetStatus(Character_Trainable c);
    [JsonIgnore] public Job_MoveLocation FactionRallyJob { get; }
    [JsonIgnore] public bool isMealHour { get; }
    public bool isMealHourAt(int hour);
    /// <summary>
    /// Return true if character is manager/member/hidden. <br/>
    /// Require strict ordering in membershipstatus
    /// </summary>
    /// <param name="charaRef"></param>
    /// <returns></returns>
    public bool isMember(int charaRef);
    [JsonIgnore] public Manageable FactionOwnerRoot { get; }
    public List<int> GetOwnedRooms(Character_Trainable c);

    [JsonIgnore] public Manageable Faction { get; }
}
