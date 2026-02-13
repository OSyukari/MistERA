using System;
using System.Collections.Generic;
using Newtonsoft.Json;


public interface I_IsJobGiver
{
    public List<Job_Furniture> GetValidJobs_Meal(Character_Trainable chara, int currentHour, List<string> s = null);
    public List<Job_Furniture> GetValidJobs_Jobs(Character_Trainable chara, int currentHour, ref string s, bool checkBlacklist = false);
    [JsonIgnore]
    public string FactionDisplayName { get; }
    public List<Job_CharaCOM> GetValidCharaCOMByTag(Character_Trainable chara, string tag, ref string ss, bool restrainedOnly = true);
    public List<Job_Furniture> GetValidJobsByCOMID(Character_Trainable chara, string comID, List<string> s = null, bool allowJobPostSearch = true, bool allowNonJobPostSearch = true, List<int> restrictRoomList = null);
    public List<Job_Furniture> GetValidJobs_Sleep(Character_Trainable chara, int currentHour, List<string> s = null);
    public List<Job_Furniture> GetValidJobs_nonJob_byTags(Character_Trainable chara, int currentHour, string tag, List<string> s = null, bool skipPrivate = false, bool shortestPathOnly = true, bool checkBlacklist = false, List<int> restrictRoomList = null);

    public List<Job_Furniture> GetValidJobs_byTags(Character_Trainable chara, int currentHour, string tag, List<string> s = null, bool skipPrivate = false, bool shortestPathOnly = true, bool checkBlacklist = false, List<int> restrictRoomList = null); public void NotifyFurnitureChange(Room_Instance room);
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

    [JsonIgnore]
    public bool isMealHour { get; }
    /// <summary>
    /// Return true if character is manager/member/hidden. <br/>
    /// Require strict ordering in membershipstatus
    /// </summary>
    /// <param name="charaRef"></param>
    /// <returns></returns>
    public bool isMember(int charaRef);
    [JsonIgnore] public Manageable FactionOwnerRoot { get; }
    public List<int> GetOwnedRooms(Character_Trainable c);
}
