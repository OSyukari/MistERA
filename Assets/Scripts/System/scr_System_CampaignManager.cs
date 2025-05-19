using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
using System.Linq;
using System.Runtime.CompilerServices;

[System.Serializable]
public class scr_System_CampaignManager_Serializable
{
    public Dictionary<int, Job> Jobs;
    public int deterministicThreshold;
    public bool deterministicRolls;
    public bool debugMode;
    public List<Manageable> Factions;
    public List<int> DeletedRefIDs;
    public int refIDCounter;
    public Map_Instance Map;
    public Party Party;
    public int CurrentRoomRef;
    public Dictionary<int, Character_Trainable> Characters;
    public Dictionary<int, Item_Instance> Items;

    public string campaignSettingID;
    // LogsManager? dont need serializing, logs are throwaway lines anyway
}




public class scr_System_CampaignManager : MonoBehaviour
{
    // Singleton
    public static scr_System_CampaignManager current;
    private void Awake()
    {
        if (current == null)
        {
            current = this;
        }
        else
        {
            Destroy(gameObject);
        }
        DontDestroyOnLoad(gameObject);
        Index_referenceID = new Dictionary<int, Character_Trainable>();
        Index_ItemReferenceID = new Dictionary<int, Item_Instance>();

        organizations = new List<Manageable>();

        registeredPackagesByRoom = new Dictionary<int, List<ActionPackage>>();

        currentRoomRef = -1;
        Index_JobReferenceID = new Dictionary<int, Job>();
        LogManager = new MessageLogManager();
    }

    /// <summary>
    /// This inventory's content is not serialized into save data
    /// on save, every refid inside is transferred into deletedrefs
    /// </summary>
    public Inventory Recycler = new Inventory();

    public List<ActionPackage> GetRegisteredAPByRoom(int roomID, bool getExecutedAPs = true)
    {
        //LocalizeDictionary.Instance.Set();
       // LocalizeDictionary

        var returnVal = (registeredPackagesByRoom.ContainsKey(roomID) ? registeredPackagesByRoom[roomID] : new List<ActionPackage>());
        if (getExecutedAPs)
        {
            foreach (var i in executedPackagesByRoom) if (i.Value == roomID) returnVal.Add(i.Key);
        }
        return returnVal;
    }
    public scr_System_CampaignManager_Serializable GetSerializable()
    {
        var obj = new scr_System_CampaignManager_Serializable();

        obj.Jobs = Index_JobReferenceID;
        obj.deterministicRolls = deterministicRolls;
        obj.debugMode = debugMode;
        obj.deterministicThreshold = deterministicThreshold;
        obj.Factions = organizations;
        //Debug.LogError("Factions serialize count from ["+organizations.Count+"] to ["+obj.Factions.Count+"]");
        obj.CurrentRoomRef = currentRoomRef;
        obj.refIDCounter = refIDCounter;

        obj.DeletedRefIDs = deletedRefIDs;
        if (this.Recycler.ContentRefs.Count > 0) obj.DeletedRefIDs.AddRange(this.Recycler.ContentRefs);

        obj.Items = Index_ItemReferenceID;
        obj.Party = party;
        obj.Map = map;
        obj.Characters = this.Index_referenceID;
        obj.campaignSettingID = CurrentCampaignID;

        return obj;
    }

    public void LoadSerializable(scr_System_CampaignManager_Serializable obj)
    {
        foreach (var i in Index_ItemReferenceID.Values) i.DisposeInternal();
        Index_ItemReferenceID.Clear();

        foreach (var i in Index_JobReferenceID.Values) i.DisposeInternal();
        Index_JobReferenceID.Clear();

        foreach (var i in Index_referenceID.Values) i.DisposeInternal();
        Index_referenceID.Clear();

        registeredPackagesByRoom.Clear();

        foreach (var i in organizations) i.DisposeInternal();
        organizations.Clear();

        playerPointer = null;

        deterministicRolls = obj.deterministicRolls;
        debugMode = obj.debugMode;
        deterministicThreshold = obj.deterministicThreshold;

        refIDCounter = obj.refIDCounter;
        deletedRefIDs = obj.DeletedRefIDs;

        this.CurrentCampaignID = obj.campaignSettingID;

        //this.jobs = obj.Jobs;
        //index_JobReferenceIDCache = null;

        // faction path will fail to build since factions do not exist yet
        // but map is required for jobs to serialize so we have to build them despite missing data
        map = obj.Map;
        map.SerializationRebuilt();

        //Debug.LogError("Index_JobReferenceID clear");
        //Index_JobReferenceID = obj.Jobs;
        //foreach (var i in Index_JobReferenceID.Values) i.OnAfterDeserialize();

        Index_JobReferenceID = obj.Jobs;
        foreach (var i in Index_JobReferenceID) i.Value.OnAfterDeserialize();

        Index_ItemReferenceID = obj.Items;
        foreach (var i in Index_ItemReferenceID) i.Value.OnAfterDeserialize();

        // factions require job to already exist
        // also requires Items to exist cuz inventory refresh
        organizations = obj.Factions;
        foreach (var i in organizations) i.OnAfterDeserialize();

        // now rebuild full map data
        map.SerializationRebuilt();
        

        Index_referenceID = obj.Characters;
        foreach (var i in Index_referenceID) i.Value.OnAfterDeserialize();

        party = obj.Party;

        currentRoomRef = obj.CurrentRoomRef;
        currentTarget = 0;
        viewMode = ViewMode.View_Room;

        NotifyUpdate();
    }



    private MessageLogManager LogManager;

    public List<MessageLog> Logs { get { return LogManager.Logs; } }
    /// <summary>
    /// RefID -1 no display
    /// RefID -2 
    /// </summary>
    /// <param name="refID"></param>
    /// <param name="s"></param>
    /// <param name="animate"></param>
    public void AddLog(int refID, string s, bool animate = false, bool rightAlign = false)
    {
        if (s.Length < 1) return;
        var chara = scr_System_CampaignManager.current.FindInstanceByID(refID);
        Observer_MessageLogs?.Invoke(LogManager.AddLog(chara == null ? null : chara.PortraitManager, s, animate, rightAlign), animate);
        //ChangeCurrentViewMode(ViewMode.View_Logs);
    }


    /// <summary>
    /// For now, line does not register parent instance, as there is no need, it does not catch a response
    /// </summary>
    /// <param name="parent"></param>
    /// <param name="line"></param>
    /// <param name="animate"></param>
    public void AddLog_Line(EventInstance instance, Event.EventEntry.EventEntry_Line line, bool animate = true)
    {
        // here we need to process line into translated
        Observer_MessageLogs?.Invoke(LogManager.AddLog(null, Utility.ParseEventEntry(instance, line.line), animate, false), animate);
    }

    public void AddLog_Question(EventInstance parent, Event.EventEntry.EventEntry_Question question, bool animate = true) 
    {
        Observer_MessageLogs?.Invoke(LogManager.AddLog(new Message_Question(null, parent, question)), animate);
    }

    public event Action<MessageLog, bool> Observer_MessageLogs;


    public event Action<PortraitManager> Observer_LogsCharaChange;
    public void Log_TrySetChara(PortraitManager refID, bool isAnimating)
    {

        if (LogManager.SetLogChara(refID, isAnimating))
        {
            //Debug.Log("Log_TrySetChara true " + refID);
            Observer_LogsCharaChange?.Invoke(refID);
        }
        else
        {
            //Debug.Log("Log_TrySetChara false " + refID);
        }
    }
    public void Log_TryClearChar(bool isAnimating)
    {
        LogManager.ClearLogChara(isAnimating);
    }

    Dictionary<int, Job> Index_JobReferenceID;

    public int GlobalTimeScale { get { return 4; } }

    //public void displayDetach(scr_Panel_CurrentChars s)
    //{
    //    if (this.display == s) display = null;
    //}

    // FACTORY METHOD
    // in actor creation get a boolean-toggle getter for getID and automate registering into dictionary
    // put dictionary into serializer
    // 

    // Initialize script call in order by CentralControl
    public void Initialize()
    {

    }

    public bool displaySex
    {
        get
        {
            return (Player.CurrentJob as Job_Sex_Group) != null;
        }
    }


    public bool ColdLoad = true;

    public void UpdateScene()
    {
        /*
        foreach (int s in CharaInCurrentRoom)
        {

            Character_Trainable o;
            if (Index_referenceID.TryGetValue(s, out o))
            {
                // depend on the type of c
                Character_Trainable c = o as Character_Trainable;
               // scr_System_CentralControl.current.AddPortraitCache(c.RefID);
                //if (display != null && c != null && c.RefID > 0 ) display.AddChara(c.RefID);
            }
        }

        foreach (int s in party.MemberRefIDs)
        {

        }
        var exceptList = new List<int>();
        foreach(var i in Player.FactionManager.HomeFactions ) exceptList.AddRange(i.ManagedRefs);
        exceptList = exceptList.Distinct().ToList();
        //scr_System_CentralControl.current.UnloadAllPortraitExcept(exceptList);
        */
        NotifyUpdate();

    }

    Dictionary<int, List<ActionPackage>> registeredPackagesByRoom;
    Dictionary<ActionPackage, int> executedPackagesByRoom = new Dictionary<ActionPackage, int>();
    /// <summary>
    /// <para>Receive Package registration from all different Jobs. <br/>
    /// Depending on the type of package, its added to 3 possible package queues : <br/>
    /// request queue (where package has not executed and might be refused)<br/>
    /// action queue (where package has begun execution) movement package also falls into this one<br/>
    /// package has internal priority setting. on add package, sort list by priority.
    /// </summary>
    /// <param name="p"></param>
    public void Register(ActionPackage p, bool avoidConflict = false, bool ignoreConflict = false)
    {
        if (!registeredPackagesByRoom.ContainsKey(p.RoomKey)) registeredPackagesByRoom.Add(p.RoomKey, new List<ActionPackage>() { p });
        else
        {
            // first validate package conflict
            for (int i = registeredPackagesByRoom[p.RoomKey].Count - 1; i >= 0; i--)
            {
                var currentP = registeredPackagesByRoom[p.RoomKey][i];
                if (!ignoreConflict && Utility.DetectConflict(currentP, p))
                {
                    Debug.Log("CM registerAP detect conflict former[" + currentP.COMVariantID + "] [" + p.COMVariantID + "] avoidConflict?[" + (avoidConflict) + "]");
                    if (avoidConflict)
                    {
                        Debug.Log("CM registerAP detect conflict former[" + currentP.COMVariantID + "] [" + p.COMVariantID + "] avoidConflict?[" + (avoidConflict) + "], skipping");
                        return;
                    }
                    if (currentP.PackagePriority > p.PackagePriority)
                    {
                        p.paused = true;
                        Debug.Log("CM RegisterAP: Package [" + p.DisplayName + " " + p.PackagePriority + "] is not getting registered (set to paused) due to not having at least equal priority than [" + currentP.DisplayName + " " + currentP.PackagePriority + "]");
                        return;
                    }
                    else
                    {
                        Debug.Log("CM RegisterAP: [" + currentP.DisplayName + " " + currentP.PackagePriority + "] is being set to paused due to conflict with [" + p.DisplayName + " " + p.PackagePriority + "] and failed priority check.");
                        p.job.LogMessage_Begin_Replace(registeredPackagesByRoom[p.RoomKey][i], p);

                        registeredPackagesByRoom[p.RoomKey][i].paused = true;
                        registeredPackagesByRoom[p.RoomKey].RemoveAt(i);
                    }
                }
            }
            if (p.paused)
            {
                Debug.Log("CM RegisterAP: resuming package [" + p.DisplayName + "]");
                p.paused = false;
            }
            registeredPackagesByRoom[p.RoomKey].Add(p);
        }
        Map.dirtyCharaAPRef.Add(p);
    }
    public void Unregister(ActionPackage p)
    {
        bool debug = scr_System_CentralControl.current.LogPrefs.Debug_Logging_ActionPackage;
        if (debug) Debug.Log("Unregistering AP " + p.DisplayName);
        if (!registeredPackagesByRoom.ContainsKey(p.RoomKey)) return;
        //if (p.targetCOM.comTags.Contains("character_trainable") && p.Duration > -1) return;
        var possibleTargets = registeredPackagesByRoom[p.RoomKey].FindAll(x => Utility.ArePackagesEqual(x, p));
        if (possibleTargets.Count > 0)
        {
            if (debug) Debug.Log("Unregistering AP " + p.DisplayName + ", found " + possibleTargets.Count + " matching AP");
            foreach (var i in possibleTargets) { if (registeredPackagesByRoom.ContainsKey(i.RoomKey)) registeredPackagesByRoom[i.RoomKey].Remove(i); }
        }
        else
        {
            if (debug) Debug.Log("Unregistering AP " + p.DisplayName + ", did not find any matching package");
        }
        //if (registeredPackagesByRoom[p.RoomKey].Contains(p)) registeredPackagesByRoom[p.RoomKey].Remove(p);
    }


    public Room_Instance GetCharaRoomInstance(int charaRefID)
    {
        return Map.FindRoomByChara(charaRefID);
    }

    public void ClearExecutedAPs()
    {
        executedPackagesByRoom.Clear();
    }

    public void UpdateAllRoom()
    {
        Map.UpdateAllRoom();
    }

    public bool isCharaVisibleToPlayer(int charaRefID)
    {
        return Map.FindRoomByChara(charaRefID).RefID == currentRoomRef;
    }

    public void NotifyUpdateHandlerExist()
    {

        scr_UpdateHandler.current.Observer_PreUpdateTime += Job_PreUpdateTime;
        scr_UpdateHandler.current.Observer_PostUpdateTime_1 += Job_PostUpdateTime_getLogsBegin;
        scr_UpdateHandler.current.Observer_PostUpdateTime_3 += Job_PostUpdateTime;
    }

    public bool DoFullUpdate(int charaRef)
    {
        return charaRef == 0 || Map.IsCharaInActiveFloors(charaRef);
    }

    /// <summary>
    /// Character will take 1 to 2 minutes to decide and start execute the new job.
    /// </summary>
    public void UpdateAllCharaJob()
    {
        DateTime currentTime = scr_System_Time.current.getCurrentTime();
        int currentHour = currentTime.Hour;
        List<string> s = scr_System_CentralControl.current.LogPrefs.Debug_Logging_MinuteAllActorsUpdate ? new List<string>() : null;
        if (s != null) s.Add("UpdateAllChara [" + currentTime.ToString() + "]");
        var list = this.Index_referenceID.Values.ToList();

        bool fullUpdate = scr_System_Time.current.getCurrentTime().Minute % 6 == 0;

        if(true)
        {

            foreach(var chara in list)
            {
                if (chara.RefID > 0 && (fullUpdate || DoFullUpdate(chara.RefID)))
                {
                    List<string> sss = s == null ? null : new List<string>();
                    chara.TryGetJob(currentHour, sss);
                    if (s != null) s.AddRange(sss);
                    //if (s != null) s.Add("Update time cost : " + (DateTime.Now - curr).TotalNanoseconds + "ms");
                }
                else
                {
                    if (s != null) s.Add(chara.FirstName + "--- Update Skipped");
                }
            }
        }

        if (s != null) Debug.Log(String.Join("\n", s));
        //Debug.Log("UpdateAllCharaJob complete after " + (DateTime.Now - currentTime).TotalNanoseconds + "ms");
    }

    /// <summary>
    /// Check all package by room, find all player related packages, take the maximum package duration from all and write into both arguments.<br/>
    /// If no player package, both arguments will have a value of 1
    /// </summary>
    /// <param name="updateTime"></param>
    /// <param name="totalUpdateTime"></param>
    /// <returns></returns>
    public bool ExistPlayerPackage(out int updateTime, out int totalUpdateTime, bool checkUnexecuted = true)
    {
        bool returnVal = false;
        updateTime = 0;

        //string s = "ExistPlayerPackage : ";

        foreach(var p in GetExistingPackages(scr_System_CampaignManager.current.Player, checkUnexecuted, false, true))
        {
            //if (p.Duration < 1) continue;
            //if (!checkUnexecuted && !p.Ticked) continue;
           // if (p.actorRefs.Contains(0) || p.masterRef == 0)
            //{
                //s += p.DisplayName + "[" + p.Duration + "] ";
                returnVal = true;
                updateTime = Math.Max(updateTime, p.Duration);
            //}
        }
        /*
        foreach (var kvpair_list in registeredPackagesByRoom)
        {
            foreach (var p in kvpair_list.Value)
            {
                if (p.Duration < 1) continue;
                if (!checkUnexecuted && !p.Ticked) continue;
                if (p.actorRefs.Contains(0) || p.masterRef == 0)
                {
                    //s += p.DisplayName + "[" + p.Duration + "] ";
                    returnVal = true;
                    updateTime = Math.Max(updateTime, p.Duration);
                }
            }
        }*/

        totalUpdateTime = updateTime;
        //Debug.Log(s);
        //Debug.Log($"EXIST PLAYER PACKAGE {updateTime} {totalUpdateTime}");
        return returnVal;
    }

    public List<ActionPackage> GetExistingPackages (Character_Trainable c, bool checkUnexecuted, bool checkExecuted, bool checkMaster)
    {
        List<ActionPackage> results = new List<ActionPackage>();
        if (c == null)
        {
            Debug.LogError("GetExistingPackages null chara");
            return results;
        }
        var room = Map.FindRoomByChara(c.RefID);

        /*
        foreach (var kvpair_list in registeredPackagesByRoom)
        {
            foreach (var p in GetExistingPackages2(c, kvpair_list.Value, checkUnexecuted, checkExecuted, checkMaster)) if (!results.Contains(p)) results.Add(p);
        }
        */
        if (room != null && registeredPackagesByRoom.ContainsKey(room.RefID)) foreach(var p in GetExistingPackages2(c, registeredPackagesByRoom[room.RefID], checkUnexecuted, checkExecuted, checkMaster)) if (!results.Contains(p)) results.Add(p);
        if (c.CurrentJob != null) foreach(var p in GetExistingPackages2(c, c.CurrentJob.ActivePackages, checkUnexecuted, checkExecuted, checkMaster)) if (!results.Contains(p)) results.Add(p);
        if (c.InteractionJob != null) foreach (var p in GetExistingPackages2(c, c.InteractionJob.ActivePackages, checkUnexecuted, checkExecuted, checkMaster)) if (!results.Contains(p)) results.Add(p);
        return results;

    }

    protected List<ActionPackage> GetExistingPackages2 (Character_Trainable c, List<ActionPackage> list, bool checkUnexecuted, bool checkExecuted, bool checkMaster)
    {
        List<ActionPackage> results = new List<ActionPackage>();
        foreach (var p in list) if ((p.actorRefs.Contains(c.RefID) || p.masterRef == c.RefID) && !results.Contains(p) && (p.Ticked || checkUnexecuted) && (p.Duration > 0 || checkExecuted)) results.Add(p);
        return results;
    }


    public bool ShowCharaLog(int refID)
    {
        if (refID < 0)
        {
            Debug.LogError("SHOWCHARALOG REFID " + refID + " ERROR");
            return false;
        }

        var room = GetCharaRoomInstance(refID);
        //Debug.LogError("SHOWCHARALOG ? room " + (room == null?"null":room.RefID) + " vs "+ (CurrentRoom == null ? "null" : CurrentRoom.RefID));
        return room != null && room.RefID == CurrentRoom.RefID;
    }

    /// <summary>
    /// Include a FreeUpdate calls within.
    /// </summary>
    public void ToggleTimeStop()
    {
        //ClearLogs();
        scr_System_Time.current.ToggleTimeStop();

        string s = "";
        if (scr_System_Time.current.TimeStop) s = "TIMESTOP!";
        else s = "TIMESTOP ended.";

        FreeUpdate(-1, s);

    }

    public enum displayAP_Reason
    {
        none,
        isPlayerCOM,
        isFailedCOM
    }

    ActionPackage displayAP = null;
    displayAP_Reason displayAP_reason = displayAP_Reason.none;
    public void SetDisplayCOM(ActionPackage ap, displayAP_Reason reason = displayAP_Reason.none)
    {
        if (ap == null)
        {
            displayAP = null;
            return;
        }
        if (displayAP_reason <= reason)
        {
            displayAP = ap;
            displayAP_reason = reason;

            if (reason == displayAP_Reason.isPlayerCOM)
            {
                //AddLog(-1, ap.DisplayName, false);
                scr_UpdateHandler.current.NotifyLogsSingleUpdate();
            }
        }
    }

    public bool isPlayerConscious { get { return !Player.Stats.isConsciousnessUnconscious; } }


    public bool IsInSameParty(Character_Trainable a, Character_Trainable b)
    {
        if (a == null || b == null) return false;
        return (a == b || (isPlayerPartyMember(a.RefID) && isPlayerPartyMember(b.RefID)));
    }

    public bool IsDisplayCOM(ActionPackage ap)
    {
        bool returnVal = false;


        if (displayAP == ap) returnVal = true;
        else if (displayAP == null && ap != null) returnVal = false;
        else if (displayAP != null && ap == null) returnVal = false;
        // now both ap are not null
        else if (displayAP.targetCOM == ap.targetCOM
            && displayAP.job == ap.job
            && displayAP.actorRefs == ap.actorRefs) returnVal = true;

        //Debug.Log("ISDISPLAYCOM ? " + (displayAP == null ? "nullAP" : displayAP.DisplayName) + " COMPARE WITH " + (ap == null ? "nullAP" : ap.DisplayName) + " result "+ returnVal);

        return returnVal;
    }

    //Job displayJob = null;

    public void FreeUpdate(int addLogRefID = -1, string addText = "", bool silent = false)
    {
        //FreeUpdateAsync();
        ClearLogs();
        if (!silent && addText != "") AddLog(addLogRefID, addText, true);
        scr_UpdateHandler.current.StartUpdate(true, silent);
        //StartCoroutine(FreeUpdateCoroutine());

        // coroutine will catch all job that need detailed display
        // coroutine will catch a single COM (if any, or all) that needs check display
        /*
         COM success check display
        - all COM will go through success check.
        - if all success, only display last COM (player issued)
        - if any failed, display the failed one and break
        - anyway, there will only be ONE COM displaying name and check success result
         */


    }

    public void FreeUpdateOneStep(ref int totalUpdateTime, ref int updateTime)
    {
        List<ActionPackage> detachedAPs = new List<ActionPackage>();

        if (registeredPackagesByRoom.Count < 1) Debug.LogError("CampaignManager FreeUpdate called but no package in list");
        else
        {
            string s = "CampaignManager FreeUpdate\n";
            foreach (var list in registeredPackagesByRoom.Values)
            {
                foreach (var element in list) s += element.DisplayName + " ";
                s += "\n";
            }
            //Debug.Log(s);
        }

        bool fullUpdate = scr_System_Time.current.getCurrentTime().Minute % 3 == 0;
        int updateDuration = 1;

        foreach (var kvpair_list in registeredPackagesByRoom)
        {

            
            var floor = Map.GetFloorByRoomRefID(kvpair_list.Key);



            if (floor != null && ( Map.ActiveFloorRefIDs.Contains(floor.refID) || kvpair_list.Value.Find(x=>x.actorRefs.Contains(0)) != null ))
            {
                // normal loop
                updateDuration = 1;
            }
            else if (fullUpdate)
            {
                updateDuration = 3;
            }
            else
            {
                continue;
            }


            List<ActionPackage> roomEffects = new List<ActionPackage>();
            List<int> allActorsInRoom = CharaInRoom(kvpair_list.Key);
            List<int> freeActors = new List<int>(allActorsInRoom);
            var list = kvpair_list.Value;

            for (int i = list.Count - 1; i >= 0; i--)
            {
                //Debug.Log("list count " + i + " " + String.Join("|", list));
                if (i >= list.Count) continue;  // list might get modified
                ActionPackage p = list[i];
                int roomKey = p.RoomKey;
                int duration = p.Duration;

                if (p.Tick(ref freeActors, updateDuration))
                {
                    // check package has room-wide effect
                    // if yes, then add to list
                    // add to roomEffects

                    // finally remove from list
                    if (p.Duration <= 0)
                    {
                        if (duration > 1 && p.actorRefs.Contains(0))
                        {   // player package has been refused, reevaluate
                            totalUpdateTime -= (updateTime - 1);
                            updateTime = 0;
                        }

                        if(p != null && roomKey != -1 && !executedPackagesByRoom.ContainsKey(p)) executedPackagesByRoom.Add(p, roomKey);
                        kvpair_list.Value.Remove(p);// (p);
                            
                        
                        //kvpair_list.Value.RemoveAt(i);

                    }
                    else if (p is ActionPackage_PathTo && p.RoomKey != kvpair_list.Key)
                    {
                        // check if movement ap changed room
                        
                        kvpair_list.Value.Remove(p);// (p);
                        detachedAPs.Add(p);
                        
                    }
                }
            }

            foreach (var p in roomEffects)
            {
                // if effect requiret not focus, send in allactorsinroom
                // if not, send in freeactors
            }

            
        }

        foreach(var p in detachedAPs)
        {
            var roomKey = p.RoomKey;
            if (!registeredPackagesByRoom.ContainsKey(roomKey)) registeredPackagesByRoom.Add(roomKey, new List<ActionPackage>() { p });
            else registeredPackagesByRoom[roomKey].Add(p);
        }

    }


    

    

    public int Register(Job j, int forceRefID = -1)
    {

        if (forceRefID > -1 && !Index_JobReferenceID.ContainsKey(forceRefID))
        {
            // do nothing
        }
        else
        {
            if (forceRefID != -1) Debug.LogError("Registering Job with forceRefID " + forceRefID + " Indexjobref already contain forceRef");
            forceRefID = GetRefID;
        }


        Index_JobReferenceID.Add(forceRefID, j);
        j.Register(forceRefID);
        //Debug.Log("Registering job " + j.RefID);
        //index_JobReferenceIDCache = null;
        //Index_JobReferenceID.Add(j.RefID, j);

        //if (j.actorRefID.Contains(0)) NotifyPlayerJobChange(j.RefID, j);

        return j.RefID;
    }

    private ViewMode viewMode;
    public ViewMode CurrentViewMode { get { return viewMode; } }
    public event Action<ViewMode,bool> Observer_CurrentViewMode;

    public void ChangeCurrentViewMode(ViewMode vm, bool lockView = false)
    {
        // if update lock, allow only setting to logs
        if (scr_UpdateHandler.current.Animating && vm != ViewMode.View_Logs)
        {
            Debug.LogError($"ChangeCurrentViewMode to {vm}, error, still animating");
            return;
        }
        else if (viewMode == ViewMode.View_Logs && vm == ViewMode.View_Room && ExistPlayerPackage(out int a, out int b)) scr_UpdateHandler.current.StartUpdate(false);
        else
        {
            if (viewMode != vm) viewMode = vm;
            Observer_CurrentViewMode?.Invoke(vm, lockView);
            if (viewMode == ViewMode.View_Room) scr_UpdateHandler.current.Animating = false;
        }
    }

    public event Action<int, Job> Observer_PlayerJob;
    public void NotifyPlayerJobChange(int jobRefID, Job job)
    {
        //Debug.LogError("ONPLAYERJOBCHANGE OBSERVER CALLED");
        Observer_PlayerJob?.Invoke(jobRefID, job);
    }

    private int currentTarget = -1;
    public event Action<int> Observer_CurrentTarget;

    public event Action<bool> Observer_LogsClear;

    public void ChangeCurrentTarget(int refID = 0)
    {
        if (Index_referenceID.ContainsKey(refID))
        {
            currentTarget = (int)refID;
            Observer_CurrentTarget?.Invoke(currentTarget);
        }
    }

    public void Job_PreUpdateTime()
    {
        int currentMinute = scr_System_Time.current.getCurrentTime().Minute;

        foreach (var job in this.Index_JobReferenceID.Values)
        {
            job.PreUpdateTime(currentMinute);
        }

/*
        foreach (var list in registeredPackagesByRoom.Values)
        {
            list.Sort(delegate (ActionPackage a, ActionPackage b)
            {
                if (a.PackagePriority == b.PackagePriority) return 0;
                else if (a.PackagePriority > b.PackagePriority) return 1;
                else if (a.PackagePriority < b.PackagePriority) return -1;
                else return 0;
            });
        }*/
    }


    public void Job_PostUpdateTime_getLogsBegin()
    {
        System.Threading.Tasks.Parallel.ForEach(this.Index_JobReferenceID.Values, job => job.PostUpdateTime_getLogsBegin());
    }


    public void Job_PostUpdateTime()
    {
        System.Threading.Tasks.Parallel.ForEach(this.Index_JobReferenceID.Values, job => job.PostUpdateTime());
    }

    public bool DisplayPortrait(int refID)
    {
        if (refID <= 0) return false;
        return true;
    }

    private Character_Trainable playerPointer = null;
    public Character_Trainable Player { get {
            if (playerPointer == null) playerPointer = FindInstanceByID(0);
            return playerPointer;
        } }

    public int CurrentTargetRef { get {
            if (currentTarget >= 0) return currentTarget;
            else return 0; } }

    public List<int> ActiveJobsRefsInCurrentRoom { get
        {

            var i = new List<int>();
            foreach(var refID in CharaInCurrentRoom)
            {
                var chara = scr_System_CampaignManager.current.FindInstanceByID(refID);
                if(chara.CurrentJob != null) i.Add(chara.CurrentJob.RefID);
                if (chara.InteractionJob != null) i.Add(chara.InteractionJob.RefID);
            }
            if(Player.CurrentJob != null) i.Add(Player.CurrentJob.RefID);

            i = i.Distinct().ToList();
            return i;
        } }

    public Character_Trainable CurrentTarget { get { if (currentTarget >= 0) return this.FindInstanceByID(currentTarget);
            else return this.FindInstanceByID(0);
        } }


    [SerializeField][JsonProperty] int currentRoomRef = -1;
    public event Action<int, Room_Instance> Observer_CurrentRoom;
    public void ChangeCurrentRoom(Room_Instance room, bool forceReinitTarget = false)
    {
        currentRoomRef = room.RefID;
        //Map.MoveCharaTo(0, room.RefID);
        //if (CurrentTargetRef > 0 && scr_System_CampaignManager.current.charaLocation[CurrentTargetRef] != room.RefID) ChangeCurrentTarget(0);
        Observer_CurrentRoom?.Invoke(0,room);
        Observer_CurrentRoom?.Invoke(1,room);
        Observer_CurrentRoom?.Invoke(2,room);
    }

    public Room_Instance CurrentRoom { get {
            if (currentRoomRef < 0) return null;
            return map.GetRoomByRef(currentRoomRef);
        } }

    public event Action<bool> Observer_playerParty;
    public void NotifyPlayerPartyChange(bool value = true)
    {
        Observer_playerParty?.Invoke(value);
    }

    public void NotifyUpdate(bool value = false)
    {
        //Debug.Log("CAMPAIGNMANAGER NOTIFY UPDATE");
        Observer_UpdateNotice?.Invoke(value);
        ChangeCurrentRoom(CurrentRoom);
        Observer_CurrentTarget?.Invoke(CurrentTargetRef);
    }
    public event Action<bool> Observer_UpdateNotice;

    //public Job_PlayerCOM PlayerCOM { get { return FindJobInstanceByID(0) as Job_PlayerCOM; } }
    public Floor_Instance PlayerFloor
    {
        get
        {
            return this.Map.GetFloorByRoomRefID(currentRoomRef);
        }
    }

    public List<int> CharaInRoom(int refID)
    {
        return Map.CharaInRoom(refID);
    }

    [SerializeField] [JsonProperty] protected Dictionary<string, string> relationshipRecords = new Dictionary<string, string>();

    public List<int> CharaInCurrentRoom { get {
            return Map.CharaInRoom(currentRoomRef);
        } }

    public Party party;

    Map_Instance map;
    public Map_Instance Map { get { return map; } }

    string CurrentCampaignID;
    Room_Instance debugRoom;
    public void StartCampaign(CampaignSettings camp, CampaignSettings_ExtraOptions camp_ex, Character_Trainable main, Character_Trainable sub = null)
    {
        // Debug.Log("3d8 " + Utility.Dice(1, 8) + " " + Utility.Dice(1, 8) + " " + Utility.Dice(1, 8));

        viewMode = ViewMode.View_Room;

        this.CurrentCampaignID = camp.ID;

        scr_System_SceneManager.current.LoadScene(GlobalValues.GameScene);

        map = new Map_Instance();

        debugRoom = Register(new Room_Instance(null, null), 0);
        currentRoomRef = debugRoom.RefID;

        refIDCounter = 1000000;

        main = InstantiateCharacter(main, debugRoom,0);
        //main = Register(main);
        if (sub != null) sub = InstantiateCharacter(sub, debugRoom);

        party = new Party();
        //party.AddToParty(main);


        //currentRoom.Add(main.RefID);
        //charaLocation.Add(main.RefID, debugRoom.RefID);
        //if (sub != null) currentRoom.Add(sub.RefID);
        //if (sub != null) charaLocation.Add(sub.RefID, debugRoom.RefID);




        scr_System_Time.current.initializeTime();

        if (camp_ex != null)
        {
            foreach (CampaignSettings_Initializer ini in camp_ex.initializers)
            {
                
                if (ini.initClass == "campaign_init_partymembers")
                {
                    Character_Trainable player = Player;
                    Manageable playerFaction = (player.FactionManager.HomeFactions != null && player.FactionManager.HomeFactions.Count > 0 ? player.FactionManager.HomeFactions[player.FactionManager.HomeFactions.Count - 1] : null);
                    foreach (string s in ini.initArguments)
                    {
                        var playerRoom = Map.FindRoomByChara(player.RefID);
                        Character_Trainable c = InstantiateCharacter_FromBaseID(s, playerRoom);
                        if (c == null && c.RefID < 1) continue;
                        
                        //party.AddToParty(c);
                        //if (playerRoom != null) charaLocation.Add(c.RefID, playerRoom.RefID);
                        if (playerFaction != null) c.InitializeFaction(playerFaction, false);
                        if (playerFaction != null && playerRoom != null) playerFaction.AddRoomOwnership(c.RefID, playerRoom.RefID);
                        
                    }
                }
                else if (ini.initClass == "campaign_init_playerNameIDs")
                {
                    var target = FindInstanceByID(0);
                    target.BaseID = ini.initArguments[0];
                    target.SetName(ini.initArguments[1],ini.initArguments[2],ini.initArguments[3],ini.initArguments[4]);
                }
                else if (ini.initClass == "campaign_init_playerPortrait")
                {
                    //Debug.Log(ini.initArguments[0]);
                    PortraitManager.CharaPortrait cm = JsonConvert.DeserializeObject<PortraitManager.CharaPortrait>(ini.initArguments[0], Utility.SerializerSettings);
                    Player.PortraitManager.Prepend(cm);

                }
                else if (ini.initClass == "campaign_init_map_root")
                {
                    map.AddMapTemplate(ini.initArguments[0], ini.initArguments[1]);
                    // FindInstanceByID(0).baseID = ini.initArguments[0];
                }
                else if (ini.initClass == "campaign_init_homefaction")
                {
                    Manageable f = FindorAddHomeFactionByID(ini.initArguments[0]);
                    if (ini.initArguments[1] == "true"){
                        FindInstanceByID(0).FactionManager.SetHomeFaction(f.ID, (ini.initArguments[2] == "true"));
                    }
                }
                else if (ini.initClass == "campaign_init_productionOrder")
                {
                    Manageable f = FindorAddHomeFactionByID(ini.initArguments[0]);
                    ItemComponentTemplate_Craftable_Recipe r = Masterlist_Items.Instance.GetRecipeByID(ini.initArguments[1]);
                    Manageable.ProductionOrderType type = (Manageable.ProductionOrderType) Enum.Parse(typeof(Manageable.ProductionOrderType), ini.initArguments[2]);
                    if (int.TryParse(ini.initArguments[3], out int count)) f.AddProductionOrder(r, count, type);
                    
                }
                else if (ini.initClass == "campaign_init_factionVisitor")
                {
                    Manageable f = FindorAddHomeFactionByID(ini.initArguments[0]);
                    Manageable g = FindorAddHomeFactionByID(ini.initArguments[3]);
                    Room_Instance ri = f.ManagedRooms.Values.ToList().Find(x=>x.Base.ID ==  ini.initArguments[1]);
                    if (ri == null) continue;
                    Character_Trainable c = InstantiateCharacter_FromBaseID(ini.initArguments[2], ri);
                    c.FactionManager.SetHomeFaction(g.ID);
                    c.FactionManager.SetTempHomeFaction(f.ID);
                }
                else if (ini.initClass == "campaign_init_map_extra")
                {
                    map.AddMapTemplate(ini.initArguments[0], ini.initArguments[1].ToString());
                    // FindInstanceByID(0).baseID = ini.initArguments[0];
                }
                else if (ini.initClass == "campaign_init_factionConnect")
                {
                    var f1 = scr_System_CampaignManager.current.FindFactionByID(ini.initArguments[0]);
                    var f2 = scr_System_CampaignManager.current.FindFactionByID(ini.initArguments[1]);
                    scr_System_CampaignManager.current.Map.ConnectFactions(f1, f2);
                    // FindInstanceByID(0).baseID = ini.initArguments[0];
                }
                else if (ini.initClass == "campaign_init_factionInventory")
                {
                    var f1 = scr_System_CampaignManager.current.FindFactionByID(ini.initArguments[0]);
                    var f2 = scr_System_Serializer.current.GetByNameOrID_Item_Base(ini.initArguments[1]);
                    if (f1 != null && f2 != null && int.TryParse(ini.initArguments[3], out int f4))
                    {
                        //Debug.Log($"Instantiating inventory {f1.FactionDisplayName} {f2.DisplayName} {ini.initArguments[2]} {f4}");
                        f1.Inventory.AddItem(WorldManager.Instantiate(f2.id, ini.initArguments[2], f4));
                    }
                    else
                    {
                        Debug.LogError($"Error instantiating inventory, {(f1 == null ? ini.initArguments[0] + " missing" : "")} {(f2 == null ? ini.initArguments[1] + " missing" : "")}");
                    }
                }
            }

            //
            // map.playerInitLocation
            // move everyone in  currentRoom into initlocation room
        }




        //Debug.Log("CampaignManager: StartCampaign Complete\n"+ party.DebugInfo());


        Register(new Job_PlayerCOM(), jobRef_playerCOM);
        Register(new Job_FollowPlayer(), jobRef_followPlayerCOM);

        //if (sub != null) party.AddToParty(sub);

        /*
        Manageable m = FindorAddHomeFactionByID("Utnapishtim");
        foreach(var recipe in scr_System_Serializer.current.CraftingRecipe)
        {
            m.AddProductionOrder(recipe, 11, Manageable.ProductionOrderType.craftUntilCount);
        }
        */
        //Observer_UpdateNotice?.Invoke(false);
        Map.SerializationRebuilt();

        ColdLoad = false;
        UpdateScene();
        scr_System_Time.current.UpdateTime(0, 0, 0, 0, true);
    }

    public int jobRef_playerCOM = 2;
    public int jobRef_followPlayerCOM = 1;

    Dictionary<int, Character_Trainable> Index_referenceID;
    public List<Character_Trainable> InstancedCharacters { get { return Index_referenceID.Values.ToList(); } }

    /*
    List<Item_Instance> items;
    private Dictionary<int, Item_Instance> index_ItemReferenceIDCache = null;
    Dictionary<int, Item_Instance> Index_ItemReferenceID
    {
        get
        {
            if (index_ItemReferenceIDCache == null)
            {
                index_ItemReferenceIDCache= new Dictionary<int, Item_Instance>();
                foreach(var i in items) index_ItemReferenceIDCache.Add(i.RefID, i);
            }
            return index_ItemReferenceIDCache;
        }
    }*/
    Dictionary<int, Item_Instance> Index_ItemReferenceID;
    public List<Item_Instance> InstancedItems { get { return Index_ItemReferenceID.Values.ToList(); } }

    public Character_Trainable HasInstanceCharaWithBaseID(string baseID)
    {
        var tempList = Index_referenceID.Values.ToList();
        return tempList.Find(x => x.BaseID == baseID);
    }
    public Character_Trainable FindInstanceByID(int id)
    {
        if (Index_referenceID.ContainsKey(id)) return Index_referenceID[id] as Character_Trainable;
        else return null;
    }

    public List<int> FindRefIDByBaseID(string baseID)
    {
        List<int> list = new List<int>();
        foreach (KeyValuePair<int, Character_Trainable> entry in Index_referenceID) {
            if ((entry.Value as Character_Trainable).BaseID == baseID) list.Add(entry.Key);
        }
        if (list.Count > 0) return list;
        else return null;
    }

    public Item_Instance FindItemInstanceByID(int id)
    {
        if (Index_ItemReferenceID.ContainsKey(id)) return Index_ItemReferenceID[id] as Item_Instance;
        else return null;
    }

    public Job FindJobInstanceByID(int id)
    {
        if (id < 0) return null;
        if (Index_JobReferenceID.ContainsKey(id)) return Index_JobReferenceID[id];
        else
        {
            Debug.LogError("CampaignManager FindJobInstanceByID " + id + " cannot find key");
            return null;
        }
    }

    private int refIDCounter;
    protected int GetRefID { get {
            refIDCounter++;
            return refIDCounter - 1; } }

    [SerializeField] protected List<int> deletedRefIDs;

    public Character_Trainable Register(Character_Trainable c, int forceRefID = -1)
    {
        if (c.RefID == -1)
        {
            if (forceRefID > -1)
            {
                Index_referenceID.Add(forceRefID, c);
                c.InitializeWithRefID(forceRefID);
            }
            else
            {
                int refID = GetRefID;
                Index_referenceID.Add(refID, c);
                c.InitializeWithRefID(refID);
            }

        }
        else
        {
            if (Index_referenceID.ContainsKey(c.RefID))
            {
                Debug.LogError("CampaignManager: Registering character baseID [" + c.FullName + "] with refID [" + c.RefID + "] duplicate refID with [" + Index_referenceID[c.RefID].FullName +"]");
                return c;
            }
            else{
                Index_referenceID.Add(c.RefID, c);
            }

        }

        //Debug.Log("CampaignManager: Registering character baseID ["+c.baseID+"] with refID [" + c.RefID + "]");
        return c;
    }

    public Item_Instance Register(Item_Instance c)
    {
        if (c.RefID == -1) c.RegisterItem(GetRefID);
        Index_ItemReferenceID.Add(c.RefID, c);
        //Debug.Log("CampaignManager: Registering item baseID [" + c.BaseID + "] with refID [" + c.RefID + "]");
        return c;
    }

    /// <summary>
    /// key - charaRefID, value - roomRefID
    /// </summary>
    //public Dictionary<int, int> charaLocation { get { return Map.charaRoomRef; } }





    public Room_Instance Register(Room_Instance r, int forceRefID = -1)
    {
        if (forceRefID == -1) forceRefID = GetRefID;
        if (map.HasRoomWithRef(forceRefID)) Debug.LogError("CampaignManager Registering room with force refID [" + forceRefID + "]already taken!");
        else
        {
            r.Register(forceRefID);
            map.AddRoom(r);
        }
        return r;
    }

    public Floor_Instance Register(Floor_Instance r, int forceRefID = -1)
    {
        if (forceRefID == -1) forceRefID = GetRefID;
        if (map.HasFloorWithRef(forceRefID)) Debug.LogError("CampaignManager Registering floor with force refID [" + forceRefID + "]already taken!");
        else
        {
            r.refID = forceRefID;
            map.AddRegisteredFloor(r.refID, r);
        }
        return r;
    }



    public void MoveAllCharaFromDebugToRoom(Room_Instance room)
    {
        //Debug.LogError("moveallchara from debug to room " + (room != null ? room.RefID : "null"));

        List<int> charaInDebug = Map.CharaInRoom(0);

        if(charaInDebug.Count < 1)
        {
            Debug.Log("Player already initialized into another room, init location failed");
        }
        foreach (int i in charaInDebug)
        {
            Map.MoveCharaTo(i, room.RefID);
            Debug.Log("CampaignManager: MoveCharaToRoom character refID [" + i + "] registered to room [" + room.DisplayName + "]");

        }
        if(charaInDebug.Contains(0)) currentRoomRef = room.RefID;
        
        //NotifyUpdate();
        UpdateScene();
    }

    public void ClearLogs(bool flushOnly = false)
    {
        if (!flushOnly) LogManager.Clear();
        Observer_LogsClear?.Invoke(flushOnly);
    }


    private void UpdateCurrentTarget()
    {

        if (currentTarget > 0 && CharaInCurrentRoom.Contains(currentTarget)) return;
        var charaInRoom = CharaInCurrentRoom;
        charaInRoom.Remove(0);


        ChangeCurrentTarget(charaInRoom.Count > 0 ? charaInRoom[0] : 0);
    }

    public bool isPlayerPartyMember(int i)
    {
        return i == 0 || PlayerPartyMembers.Contains(i);
    }
    public List<int> PlayerPartyMembers { get { return party.MemberRefIDs; } }

    public void MoveCharacterTo(int charaRef, int targetRoomRef)
    {
        bool playerMoved = false;
        bool updateScene = false;
        bool updateTarget = false;
        if (CharaInCurrentRoom.Contains(charaRef)) updateScene = true;
        else if (charaRef == 0) updateScene = true;
        else if (targetRoomRef == currentRoomRef) updateScene = true;

        if (charaRef == CurrentTargetRef || charaRef == 0) updateTarget = true;
        //if (charaLocation[charaRef] == currentRoomRef) updateTarget = true;
        //else if (targetRoomRef == currentRoomRef && currentTarget < 1) updateTarget = true;

        Map.MoveCharaTo(charaRef, targetRoomRef);

        if (charaRef == 0)
        {
            playerMoved = true;
            // if player is moved, do additional update

            foreach (int i in PlayerPartyMembers)
            {
                // do we break team if timestop ?
                //charaLocation[i] = targetRoomRef; // move party members
                MoveCharacterTo(i, targetRoomRef);
            }
            ChangeCurrentRoom(Map.GetRoomByRef(targetRoomRef));
            //UpdateScene();
        }
        

        if (updateScene || playerMoved) UpdateScene();
        if (updateTarget) UpdateCurrentTarget();
    }

    List<Manageable> organizations;
    [JsonIgnore] public List<Manageable> Factions { get { return (organizations == null) ? new List<Manageable>() : organizations; } }
    public Manageable FindorAddHomeFactionByID(string id)
    {
        Manageable target = organizations.Find(x => x.ID == id);
        if (target == null) 
        {
            target = new Manageable_HomeFaction(id);
            organizations.Add(target);
        }
        return target;
    }

    public Manageable FindFactionByID(string id)
    {
        Manageable target = organizations.Find(x => x.ID == id) as Manageable;
        return target;
    }

    protected Character_Trainable InstantiateCharacter(Character_Trainable c, Room_Instance room, int forceRefID = -1){

        if (c == null) return c;

        if (forceRefID > -1)
        {
            c = Register(c,forceRefID);
        }
        else
        {
            c = Register(c);
        }

        Map.MoveCharaTo(c.RefID, room.RefID);
        //Debug.LogError("Adding charaRef "+c.RefID +" to roomRef "+room.RefID);
        //Debug.Log("CampaignManager: Instantiate character baseID [" + c.baseID + "] with refID [" + c.RefID + "] registered to room ["+room.RefID+"]");

        foreach(Character_Trainable.CharaTemplate.presetInventory p in c.Template.initialInventory)
        {
            Item_Instance i = WorldManager.Instantiate(p.ID, p.nameOverwrite);
            //Debug.Log("Instantiating chara equipping itemref " + i.RefID);
            if (!c.EquipItem(i.RefID, false)) Debug.LogError($"error equipping {i.DisplayName} on {c.FirstName}");
        }
        return c;
    }

    public Character_Trainable InstantiateCharacter(string jsonFilePath, Room_Instance room, int forceRefID = -1){

        Character_Trainable c = scr_System_CentralControl.current.LoadCharaData(jsonFilePath);

        return InstantiateCharacter(c, room, forceRefID);

    }

    public Character_Trainable InstantiateCharacter_FromBaseID(string ID, Room_Instance room, int forceRefID = -1)
    {
        //Debug.Log("CampaignManager: Instantiate request for ID [" + ID + "]");

        string path = ID;
        var chara = scr_System_Serializer.current.index_Characters_Bases.GetTemplateFromBaseID(ID);

        Character_Trainable template = scr_System_CentralControl.current.LoadCharaData(chara.FileLocation);

        if (template != null && map.HasRoomWithRef(room.RefID))
        {
            return InstantiateCharacter(template, room, forceRefID);
            //return InstantiateCharacter(Application.dataPath + path, room, forceRefID);
        }
        else
        {
            string s = "InstantiateCharacter error";
            if (path == null) s += "\ncharacter path null";
            if (!map.HasRoomWithRef(room.RefID)) s += "\nmap has no ref with roomRef " + room.RefID;
            Debug.LogError(s);

        }

        return null;
    }

    public bool XrayMode { get
        {
            return debug_xray;
        } }

    [NonSerialized] public bool debug_xray = false;

    private bool debugMode = true;
    public bool DebugMode { get { return debugMode; } set { debugMode = value; } }

    protected int deterministicThreshold = 65;
    public int DeterministicThreshold { get { return deterministicThreshold; } }
    private bool deterministicRolls = true;
    public bool DeterministicRolls { get { return deterministicRolls; } set { deterministicRolls = value; } }

    [NonSerialized][JsonIgnore] public string QuickSaveFilePath = "";

    public void Unregister(Item_Instance i)
    {
        if (Index_ItemReferenceID.ContainsKey(i.RefID))
        {
            Debug.Log("Item unregistered " + i.RefID);
            Index_ItemReferenceID.Remove(i.RefID);
            deletedRefIDs.Add(i.UnregisterItem());
        }
    }
}

public static class WorldManager
{
    public static Item_Instance Instantiate(string parentID, string nameOverwrite = "", int innerCount = 1)
    {
        var baseItem = scr_System_Serializer.current.GetByNameOrID_Item_Base(parentID);
        if (baseItem == null)
        {
            Debug.LogError($"worldmanager instantiate error cannot find baseitem {parentID}");
            return null;
        }
        Item_Instance i = new Item_Instance(parentID, nameOverwrite);
        i.SetCount(innerCount);
        scr_System_CampaignManager.current.Register(i);
        return i;
    }

    public static Item_Instance_Cum Instantiate(Character_Trainable owner, string nameOverwrite)
    {
        Item_Instance_Cum i = new Item_Instance_Cum(owner, nameOverwrite);
        scr_System_CampaignManager.current.Register(i);
        return i;
    }

    public static Dictionary<int, Floor_Instance> Instantiate(MapPlan map, string factionOverride = "", bool disablePlayerInit = false, bool disableCharaInstantiation = false)
    {
        Dictionary<int, Floor_Instance> list = new Dictionary<int, Floor_Instance>();
        var initialRefID = -1;
        foreach (MapPlan.MapPlan_Floor fpi in map.floors)
        {
            Floor_Instance fp = WorldManager.Instantiate( fpi,disablePlayerInit, disableCharaInstantiation);

            if (fp != null && fp.refID > 0)
            {
                list.Add(fp.refID, fp);
                if (initialRefID == -1) initialRefID = fp.refID;
                fp.RegisterMapTemplate(map.ID, initialRefID);

            }
        }

        // at this stage, all character should have been initialized
        var targetFaction = factionOverride != "" ? factionOverride : map.initializeFaction;

        if (targetFaction != "")
        {
            // get target faction
            Manageable org = scr_System_CampaignManager.current.FindorAddHomeFactionByID(targetFaction);

            // add floor to faction and set all chara in map as faction member and set private room ownership
            foreach (var f in list.Values) org.AddToFaction(f, true, map.setPrivateRoomOwner);

            if (map.workHours != null && map.workHours.Count > 0) foreach (var i in map.workHours) org.InitWorkHours(i);

            if (map.managerBaseIDs != null && map.managerBaseIDs.Count > 0)
            {
                foreach (string id in map.managerBaseIDs)
                {
                    if (id == "PLAYER")
                    {
                        org.AddToFaction(0, Manageable_GuestStatus.Manager);
                        if (scr_System_CampaignManager.current.Player.FactionManager.Faction_Home == null) scr_System_CampaignManager.current.Player.FactionManager.SetHomeFaction(org.ID, true);
                        else scr_System_CampaignManager.current.Player.FactionManager.AddWorkFaction(org.ID, true);
                    }
                    else foreach (var i in org.ManagedChara) if (i.BaseID == id) org.AddToFaction(i.RefID, Manageable_GuestStatus.Manager);
                }
            }

            // set work hours
            foreach (var module in map.workModules)
            {
                org.AddJobPost(module);
            }

            if (map.mainExit != null && map.mainExit.roomID != "")
            {
                org.SetMainExit(map.mainExit);
            }

            if (map.salesCurrency != "")
            {
                org.SetMainCurrency(map.salesCurrency);
                foreach (var itemInit in map.salesInventory)
                {
                    org.AddSalesInventory(itemInit);
                }
            }

        }

        return list;
    }

    public static Floor_Instance Instantiate(MapPlan.MapPlan_Floor floor, bool disablePlayerInit = false, bool disableCharaInstantiation = false)
    {
        Floor_Base fp = scr_System_Serializer.current.GetByNameOrID_Floor_Base(floor.ID);
        if (fp != null)
        {
            Floor_Instance f = new Floor_Instance(fp, floor.nameOverwrite);
            scr_System_CampaignManager.current.Register(f);
            // initialize with additional
            foreach (MapPlan.MapPlan_FloorInit init in floor.Additional)
            {
                Intialize(init, f, disablePlayerInit, disableCharaInstantiation);
            }

            return f;
        }
        else return null;
    }

    public static void Intialize(MapPlan.MapPlan_FloorInit init, Floor_Instance f, bool disablePlayerInit = false, bool disableCharaInstantiation = false)
    {
        switch (init.addClass)
        {
            case "map_init_playerLocation":

                if (!disablePlayerInit && init.map_init_playerLocation != null && init.map_init_playerLocation.roomID != "")
                {
                    Room_Instance r = f.FindRoom(init.map_init_playerLocation.roomID);
                    if (r != null)
                    {
                        scr_System_CampaignManager.current.MoveAllCharaFromDebugToRoom(r);
                        // TODO
                    }
                    else
                    {
                        Debug.LogError("map_init_playerLocation, error room not found.");
                    }
                }
                else
                {
                    Debug.LogError("map_init_playerLocation, error reading room init config.");
                }

                break;
            case "map_init_placeChara":
                if (!disableCharaInstantiation && init.map_init_placeChara != null && init.map_init_placeChara.roomID != "")
                {
                    Room_Instance r = f.FindRoom(init.map_init_placeChara.roomID);
                    if (r != null && init.map_init_placeChara.charaBaseID.Count > 0)
                    {
                        foreach (string s in init.map_init_placeChara.charaBaseID)
                        {
                            scr_System_CampaignManager.current.InstantiateCharacter_FromBaseID(s, r);
                        }
                    }
                    else
                    {
                        Debug.LogError("map_init_placeChara, error room not found.");
                    }
                }
                else
                {
                    Debug.LogError("map_init_placeChara, error reading chara init config.");
                }

                break;



            default: break;

        }
    }
}