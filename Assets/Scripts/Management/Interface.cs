using System;
using System.Collections.Generic;
using Newtonsoft.Json;


public interface I_IsJobGiver
{
    [JsonIgnore] public I_IsJobGiver getLocaleFaction { get; }

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
    /// <summary>
    /// Resolves the character's MemberType from the implementing faction's own charaGuestStatus
    /// dictionary (a per-character MemberType ID string), falling back to MemberType_None if unset.
    /// </summary>
    public MemberType GetMemberType(Character_Trainable c);
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

    /// <summary>
    /// rescue a character with prisoner/slave status, with the goal of adding character to the faction.
    /// even if character is/was member, if current faction is party, then character will not restore its original status, 
    /// and will be instead placed in a temporary rescued status (to prevent a "supposedly weakened" victim from joining combat
    /// on return to faction root, use canconvert to convert the rescued into new member or restore its previous membership
    /// </summary>
    /// <param name="prevStatus"></param>
    /// <param name="newstatus"></param>
    /// <returns></returns>
    public bool CanRescue(Character_Trainable c, MemberType prevStatus, out MemberType newstatus);
    /// <summary>
    /// transfer: keep the member status c currently has in the other faction (slave transfer remain slave) 
    /// (unless c was/is a member already, in that case restore c's prev status)
    /// </summary>
    /// <param name="prevStatus"></param>
    /// <param name="newstatus"></param>
    /// <returns></returns>
    public bool CanTransfer(Character_Trainable c, MemberType prevStatus, out MemberType newstatus);
    /// <summary>
    /// capture/enslave target as prisoner.
    /// cannot capture c if c is/was a member already 
    /// (status change within a faction should not be handled by this function)
    /// </summary>
    /// <param name="c"></param>
    /// <param name="prevStatus"></param>
    /// <param name="newstatus"></param>
    /// <returns></returns>
    public bool CanCapture(Character_Trainable c, MemberType prevStatus, out MemberType newstatus);
    /// <summary>
    /// liberate will not add character to current faction (temporary character will be deleted).
    /// if c is flagged important, or if the status flagged noliberate, or c is/already member, then cannot liberate
    /// </summary>
    /// <param name="c"></param>
    /// <param name="prevStatus"></param>
    /// <param name="newstatus"></param>
    /// <returns></returns>
    public bool CanLiberate(Character_Trainable c, MemberType prevStatus, out MemberType newstatus);
    /// <summary>
    /// only call in factionroot (Manageable), not by party.
    /// convert rescued into either new member or restore its previous membership status
    /// for other membership types not recognized
    /// (such as transferred ones, or other 3rd party direct joins with no specified status), 
    /// convert by finding the status with most similar parameters (such as match by ismanager, ismember, isprisoner etc)
    /// </summary>
    /// <param name="c"></param>
    /// <param name="prevStatus"></param>
    /// <param name="newstatus"></param>
    /// <returns></returns>
    public bool CanConvert(Character_Trainable c, MemberType prevStatus, out MemberType newstatus);

}
