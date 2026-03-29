using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
public class scr_System_CampaignManager_Serializable
{
    public Dictionary<int, Job> Jobs;
    public int deterministicThreshold;
    public bool deterministicRolls;
    public bool debugMode;
    public Dictionary<string, Manageable> Factions;
    public List<int> DeletedRefIDs;
    public int refIDCounter;
    public Map_Instance Map;
    public Party Party;
    public int CurrentRoomRef;
    public CombatManager Combat;
    public Dictionary<int, Character_Trainable> Characters;
    public Dictionary<int, Item_Instance> Items;
    public string campaignSettingID;
    public Dictionary<int, ExpeditionInstance> ExpeditionInstances;
    public int debugRoomRef, statisRoomRef, tempRoomRef;
    public Dictionary<string, string> LLMResponseStorage;
    public List<int> specialUpdateJobs;
    // LogsManager? dont need serializing, logs are throwaway lines anyway
}


[Serializable]

public class scr_System_CampaignManager : MonoBehaviour
{

    public scr_CharPortraitBox CurrentTargetEX_Box = null;

    public MenuHandler CanvasAnchor = null;


    [NonSerialized] public Action<PortraitManager.CharaPortrait> Observer_UpdateCurrentTargetAnchor;
    public Action Observer_CurrentTargetClick;
    public void NotifyCurrentTargetClick()
    {
        this.Observer_CurrentTargetClick?.Invoke();
    }
    public void UpdateCurrentTargetAnchor(PortraitManager.CharaPortrait p)
    {
        Observer_UpdateCurrentTargetAnchor?.Invoke(p);
    }
    // Singleton
    [NonSerialized] public static scr_System_CampaignManager current;
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
        registeredPackagesByRoom = new Dictionary<int, List<ActionPackage>>();


        currentRoomRef = -1;
        LogManager = new MessageLogManager();
    }

    protected void Start()
    {

        scr_System_SceneManager.current.Observer_OnPageUnload += OnSceneUnload;
    }

    protected void OnSceneUnload()
    {
        if (actions_sceneUnload != null)
        {
           // Debug.Log($"OnSceneUnload, keyword [{actions_sceneUnload.Name}]");
            scr_UpdateHandler.current.EventHandler.StartEvent(actions_sceneUnload, false);
            scr_System_CampaignManager.current.FreeUpdate(-1, actions_sceneUnload.Name);
        }
        if (onSceneUnload_Action != null)
        {
            onSceneUnload_Action.Invoke();
        }
        onSceneUnload_Action = null;
        actions_sceneUnload = null;
    }

    Action onSceneUnload_Action = null;
    Action onHideAnchor_Action = null;
    EventInstance actions_sceneUnload = null;
    EventInstance actions_viewChange = null;
    EventInstance actions_hideAnchor = null;
    public void RegisterSceneUnloadActionCallback(Action a)
    {
        onSceneUnload_Action += a;
    }
    public void RegisterSceneUnloadEventCallback(EventInstance a, bool immediate = false)
    {
        actions_sceneUnload = a;
        if (immediate) OnSceneUnload();
    }
    public void RegisterCanvasAnchorHideEventCallback(EventInstance a, bool immediate = false)
    {
        actions_hideAnchor = a;
        if (immediate) OnSceneUnload();
    }
    public void RegisterCanvasAnchorHideActionCallback(Action a)
    {
        onHideAnchor_Action += a;
    }
    public void HideCanvasAnchor()
    {
        if (this.CanvasAnchor != null) this.CanvasAnchor.PanelAnchor.gameObject.SetActive(false);
        if (actions_hideAnchor != null)
        {
            // Debug.Log($"OnSceneUnload, keyword [{actions_sceneUnload.Name}]");
            scr_UpdateHandler.current.EventHandler.StartEvent(actions_hideAnchor, false);
            scr_System_CampaignManager.current.FreeUpdate(-1, actions_hideAnchor.Name);
        }
        if (onHideAnchor_Action != null)
        {
            onHideAnchor_Action.Invoke();
        }
        onHideAnchor_Action = null; 
        actions_hideAnchor = null;
    }
    public void EnableCanvasAnchor()
    {
        if (this.CanvasAnchor != null) this.CanvasAnchor.PanelAnchor.gameObject.SetActive(true);
    }
    Dictionary<string, ExpeditionInstance> _uniqueExpeditionInstances = null;

    protected void RebuildExpeditionsList()
    {
        _uniqueExpeditionInstances = new Dictionary<string, ExpeditionInstance>();

        foreach (var exp in Index_ExpeditionInstances)
        {
            if (exp.Value.Base.isUnique) _uniqueExpeditionInstances.Add(exp.Value.Base.ExpeditionID, exp.Value);
        }
    }

    /// <summary>
    /// Called when a entry is purged
    /// </summary>
    public void NotifyExpeditionEntryPurge(string id)
    {
        if (_uniqueExpeditionInstances != null && _uniqueExpeditionInstances.TryGetValue(id, out var value))
        {
            Debug.Log($"Removing unique expInstance {id}");
            _uniqueExpeditionInstances.Remove(id);
        }

    }

    public ExpeditionInstance FindExpeditionByID(int id)
    {
        if (_uniqueExpeditionInstances == null) RebuildExpeditionsList();
        if (Index_ExpeditionInstances.TryGetValue(id, out var expinst))
        {
            //Debug.Log($"findExpeditionbyID {id}");
            return expinst;
        }
        //Debug.Log($"cannot findExpeditionbyID {id}");
        return null;
    }
    public ExpeditionInstance CreateExpedition(string id)
    {
        if (_uniqueExpeditionInstances == null) RebuildExpeditionsList();
        if (_uniqueExpeditionInstances.TryGetValue(id, out var expinst)) return expinst;
        // create new
        var baseExp = Expeditions.ExpeditionEntry.GetByID(id);
        if (baseExp == null)
        {
            Debug.LogError($"CreateExpedition: cannot find ExpeditionEntry by ID {id}");
            return null;
        }
        var newstuff = Register(new ExpeditionInstance(baseExp));

        //Index_ExpeditionInstances.Add(newstuff.RefID, newstuff); -> already registered
        if (baseExp.isUnique) _uniqueExpeditionInstances.Add(baseExp.ExpeditionID, newstuff);

        return newstuff;
    }


    public void RegisterViewChangeEventCallback(EventInstance b)
    {
        actions_viewChange = b;
    }

    /// <summary>
    /// This inventory's content is not serialized into save data
    /// on save, every refid inside is transferred into deletedrefs
    /// </summary>
    public FactionInventory Recycler = new FactionInventory();

    /// <summary>
    /// This will skip temporary aps
    /// </summary>
    /// <param name="roomID"></param>
    /// <param name="getExecutedAPs"></param>
    /// <returns></returns>
    public List<ActionPackage> GetRegisteredAPByRoom(int roomID, bool getExecutedAPs = true)
    {
        //LocalizeDictionary.Instance.Set();
        // LocalizeDictionary
        var length = Math.Max(registeredPackagesByRoom.ContainsKey(roomID) ? registeredPackagesByRoom[roomID].Count : 0, executedPackagesByRoom.Count);

        var returnVal = new List<ActionPackage>(length);
        if (registeredPackagesByRoom.ContainsKey(roomID))
        {
            foreach (var p in registeredPackagesByRoom[roomID]) if (!p.isTemporaryAP) returnVal.Add(p);
        }
        if (getExecutedAPs)
        {
            foreach (var i in executedPackagesByRoom) if (i.Value == roomID) returnVal.Add(i.Key);
        }
        return returnVal;
    }
    public scr_System_CampaignManager_Serializable GetSerializable()
    {
        var obj = new scr_System_CampaignManager_Serializable();

        PreSaveCleanup();

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
        obj.Combat = this.Combat;
        obj.Characters = this.Index_referenceID;
        obj.campaignSettingID = CurrentCampaignID;
        obj.ExpeditionInstances = this.Index_ExpeditionInstances;
        obj.specialUpdateJobs = this.specialUpdateJobs;

        obj.statisRoomRef = this.statisRoomID;
        obj.tempRoomRef = this.tempRoomID;
        obj.debugRoomRef = this.debugRoomRef;
        obj.LLMResponseStorage = this.LLMResponseStorage;

        return obj;
    }

    protected void PreSaveCleanup()
    {
        var listC = new List<int>( Map.CharaInRoom(TemporaryRoom.RefID));
        if (listC.Count > 0) Debug.Log($"PreSaveCleanup tempRefs [{String.Join("|",listC)}]");
        foreach(var i in listC)
        {
            var chara = FindInstanceByID(i);
            if (chara == null) continue;
            if (chara.CurrentJob == null) Unregister(chara);
        }
        PartyCleanup();
        ExpeditionInstancesCleanup();
    }

    protected void PartyCleanup()
    {

    }

    public event Action<bool> Observer_GameReload;

    public event Action Observer_LoadStart;
    public event Action<float, string> Observer_LoadProgress;
    public event Action Observer_LoadComplete;

    public void LoadSerializable(scr_System_CampaignManager_Serializable obj)
    {
        Observer_GameReload?.Invoke(true);

        foreach (var i in Index_ItemReferenceID.Values) i.DisposeInternal();
        Index_ItemReferenceID.Clear();

        foreach (var i in Index_JobReferenceID.Values) i.DisposeInternal();
        Index_JobReferenceID.Clear();

        foreach (var i in Index_referenceID.Values) i.DisposeInternal();
        Index_referenceID.Clear();

        registeredPackagesByRoom.Clear();

        foreach (var i in Factions) i.DisposeInternal();
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
        organizations = obj.Factions;
        Index_ItemReferenceID = obj.Items;
        Index_referenceID = obj.Characters;
        foreach (var i in Index_JobReferenceID) i.Value.OnAfterDeserialize();
        foreach (var i in Index_ItemReferenceID) i.Value.OnAfterDeserialize();
        // factions require job to already exist
        // also requires Items to exist cuz inventory refresh
        foreach (var i in Factions) i.OnAfterDeserialize();

        // now rebuild full map data
        map.SerializationRebuilt();
        
        if (obj.Combat != null) this.Combat = obj.Combat;

        foreach (var i in Index_referenceID) i.Value.OnAfterDeserialize();

        this.party = obj.Party;
        this.party.Clear();

        this.Index_ExpeditionInstances = obj.ExpeditionInstances;
        this.specialUpdateJobs = obj.specialUpdateJobs != null ? obj.specialUpdateJobs : new List<int>();

        this._uniqueExpeditionInstances = null;

        this.statisRoomID = obj.statisRoomRef;
        this.tempRoomID = obj.tempRoomRef;
        this.StasisRoom = null;
        this._tempRoom = null;
        this.debugRoomRef = obj.debugRoomRef;
        this.LLMResponseStorage = obj.LLMResponseStorage;

        currentRoomRef = obj.CurrentRoomRef;
        currentTarget = 0;
        viewMode = ViewMode.View_Room;

        foreach (var i in Index_referenceID) i.Value.PostReloadUpdate();

        var ri = CurrentRoom;
        if (!ri.RoomChara.Contains(Player))
        {
            Debug.Log("error room does not contain player ref");
            ri.AddChara(Player);//.Add(scr_System_CampaignManager.current.Player);
            //ri.AddChara(scr_System_CampaignManager.current.Player);
        }

        this.playerAPLogs.Clear();


        // this.Map.RefreshRoomMoodlets();
        if (this.COMmanager != null)
        {
            this.COMmanager.inputfield_llm.text = "";
            this.COMmanager.inputfield_llm.DeactivateInputField();
        }

        ClearLogs(false,true);
       // StartCoroutine(CachePortraitCoroutine());

        NotifyUpdate();
    }


    protected void ExpeditionInstancesCleanup()
    {
        //Debug.LogError($"ExpeditionInstancesCleanup");
        foreach (var ex in this.Index_ExpeditionInstances)
        {
            ex.Value.ResetUsage();// = 0;
        }
        foreach(var m in Factions)
        {
            foreach(var p in m.SubFactions)
            {
                if (!p.hasExpeditionSet) continue;
                p.Job.Expedition.NotifyUsage();
            }
        }
        var list = this.Index_ExpeditionInstances.Keys.ToList();
        foreach(var k in list)
        {
            if (this.Index_ExpeditionInstances[k].CanDelete)
            {

                Debug.LogError($"Cleanup: Removing ExpeditionInstance {this.Index_ExpeditionInstances[k].Base.DisplayName}");
                this.Index_ExpeditionInstances.Remove(k);
            }
        }
        RebuildExpeditionsList();
    }

    private MessageLogManager LogManager;

    public List<MessageLog> Logs { get { return LogManager.Logs; } }

    public Dictionary<string, string> LLMResponseStorage = new Dictionary<string, string>();

    public string AddLLMEntry(string s)
    {
        if (LLMResponseStorage == null) LLMResponseStorage = new Dictionary<string, string>();
        var key = $"%%{DateTime.Now.Ticks}%%";
        LLMResponseStorage.Add(key, s);
        return key;
    }

    public bool TryGetCustomEntry(string s, out string value)
    {
        if (LLMResponseStorage == null) LLMResponseStorage = new Dictionary<string, string>();
        if (LLMResponseStorage.TryGetValue(s, out value)) return true;
        else return false;
    }

    public List<ActionPackage> playerAPLogs = new List<ActionPackage>();
    public void LogPlayerPackage(ActionPackage ap)
    {
        ap.nameOverwrite = ap.DisplayName;
        if (playerAPLogs.Count >= 5)
        {
            playerAPLogs.RemoveAt(0);
        }
        playerAPLogs.Add(ap);
    }


    /// <summary>
    /// RefID -1 no display
    /// RefID -2 
    /// </summary>
    /// <param name="refID"></param>
    /// <param name="s"></param>
    /// <param name="animate"></param>
    public void AddLog(bool visible, Room_Instance recording, int refID, string s, bool animate = false, bool rightAlign = false, string tooltip = "")
    {
        if (s.Length < 1) return;
        if (s == "\n")
        {
            //Debug.LogError("Detect addlog \\ n");
            return;
        }
        if (s == "\n\n")
        {
            // Debug.LogError("Detect addlog \\ n 2");
            return;
        }
        if (s == "\n\n\n")
        {
            // Debug.LogError("Detect addlog \\ n 3");
            return;
        }
        if (s.Contains("<align=\"right\"></align>"))
        {
            Debug.LogError($"empty string dtected, full string [{s}]");
            return;
        }
        var chara = refID >= 0 ? FindInstanceByID(refID) : null;

        if (visible)
        {
            var lg = LogManager.AddLog(chara == null ? null : chara.PortraitManager, s, tooltip, false, rightAlign);
            Observer_MessageLogs?.Invoke(lg, animate);
        }
        if (recording != null)
        {
            recording.NotifyKojoCollect(new DescriptionCollector(s, chara == null ? new List<int>() : new List<int>() { chara.RefID }));
        }
        //ChangeCurrentViewMode(ViewMode.View_Logs);
    }


    /// <summary>
    /// RefID -1 no display
    /// RefID -2 
    /// </summary>
    /// <param name="refID"></param>
    /// <param name="s"></param>
    /// <param name="animate"></param>
    public void AddLog(int refID, string s, bool animate = false, bool rightAlign = false, string tooltip = "")
    {
        if (s.Length < 1) return;
        if (s == "\n")
        {
            //Debug.LogError("Detect addlog \\ n");
            return;
        }
        if (s == "\n\n")
        {
           // Debug.LogError("Detect addlog \\ n 2");
            return;
        }
        if (s == "\n\n\n")
        {
           // Debug.LogError("Detect addlog \\ n 3");
            return;
        }
        if (s.Contains("<align=\"right\"></align>"))
        {
            Debug.LogError($"empty string dtected, full string [{s}]");
            return;
        }
        var chara = refID >= 0 ? FindInstanceByID(refID) : null;
        var lg = LogManager.AddLog(chara == null ? null : chara.PortraitManager, s, tooltip, false, rightAlign);
        Observer_MessageLogs?.Invoke(lg, animate);
        //ChangeCurrentViewMode(ViewMode.View_Logs);
    }

    public bool IsLogDisplayChara(Character_Trainable c)
    {
        if (c.RefID == 0) return true;
        return false;
    }

    public void AddLog(MessageCollect_KojoEntry m, bool animate = false, bool rightAlign = false, string tooltip = "")
    {
        if (m == null) return;
        if (m.message != null && m.message.Length > 0)
        {
            var chara = FindInstanceByID(m.portraitRefID);
            Message_Text msg = new Message_Text(chara, m.portraitTags, m.message, rightAlign, tooltip);
            Observer_MessageLogs?.Invoke(LogManager.AddLog(msg), animate);
        }
        foreach(var next in m.nexts) AddLog(next, animate, rightAlign);
    }

    public void AddLogSingle(MessageCollect_KojoEntry m)
    {
        if (m == null) return;
        if (m.message != null && m.message.Length > 0 && scr_System_CampaignManager.current.isCharaVisibleToPlayer(m.portraitRefID))
        {
            var chara = FindInstanceByID(m.portraitRefID);
            var rightAlign = chara != null && chara != Player;
            Message_Text msg = new Message_Text(chara, m.portraitTags, m.message, rightAlign);
            Observer_MessageLogs?.Invoke(LogManager.AddLog(msg), true);
            m.message = null;
        }
    }

    public bool shortenLogsPrint = true;

    public void NotifyEventEnd()
    {
        this.Map.NotifyEventEnd();
        foreach(var c in Index_referenceID.Values)
        {
            c.Stats.RefreshAttitude();
        }
        //Debug.Log("NotifyEventEnd");
    }

    /// <summary>
    /// For now, line does not register parent instance, as there is no need, it does not catch a response
    /// </summary>
    /// <param name="parent"></param>
    /// <param name="line"></param>
    /// <param name="animate">whether on add line invoke a ui update</param>
    public void AddLog_Line(EventInstance instance, Event.EventEntry.EventEntry_Line line, bool rA, bool animate = true)
    {
        // here we need to process line into translated
        //Debug.Log($"Event Addline {line}");
        MessageLog log = null;
        var content = UtilityEX.ParseEventEntry(instance, line.line);
        if (content.Length < 1) return;
        //Debug.Log($"AddLog_Line parsed content: {content}");
        if (line.portraitRefKey == "self" && instance.Self != null && instance.Self.RefID != 0)
        {
            var msglog = new Message_Text(instance.Self, line.portraitTagsOverride, content, rA, "", default, instance);
            //msglog.AddMessage(content, rA);
            log = LogManager.AddLog(msglog);
        }
        else if (instance.Targets.TryGetValue(line.portraitRefKey, out var targetrefs))
        {
            var msglog = new Message_Text(targetrefs, line.portraitTagsOverride, null, "", default, instance);
            msglog.AddMessage(content, rA);
            log = LogManager.AddLog(msglog);
        }
        else
        {
            log = LogManager.AddLog(null, rA ? $"<align=\"right\">{content}</align>" : content, "", false, false);
        }
        Observer_MessageLogs?.Invoke(log, false);
    }
    /// <summary>
    /// 
    /// </summary>
    /// <param name="parent"></param>
    /// <param name="question"></param>
    /// <param name="animate">whether on add line invoke a ui update</param>
    public void AddLog_Question(EventInstance parent, Event.EventEntry.EventEntry_Question question, bool animate = true) 
    {
        //scr_UpdateHandler.current.FlushCollectedLogs(true, false);
        MessageLog log = null;
        if (question.portraitRefKey == "self")
        {
            if (scr_System_CentralControl.current.LogPrefs.DLog_Portraits) Debug.Log($"AddLog_Question Self {parent.Self.RefID}");
            log = LogManager.AddLog(new Message_Question(parent.Self == null ? null : parent.Self.PortraitManager,question.portraitTagsOverride,  parent, question));
        }
        else if (parent.Targets.TryGetValue(question.portraitRefKey, out var targetrefs))
        {
            log = LogManager.AddLog(new Message_Question(targetrefs, question.portraitTagsOverride, parent, question));
        }
        else
        {
            Debug.Log("null AddLog_Question portraitref");
            PortraitManager portraitRef = null;
            log = LogManager.AddLog(new Message_Question(portraitRef, question.portraitTagsOverride, parent, question));
        }
        Observer_MessageLogs?.Invoke(log, !log.DisplaPortrait);
    }

    public void AddLog_LLM(LLMRequest request)
    {
        PortraitManager portraitRef = null;
        MessageLog log = LogManager.AddLog(new Message_LLMQuery(portraitRef, new List<string>(), request));
        
        Observer_MessageLogs?.Invoke(log, !log.DisplaPortrait);
    }

    public event Action<MessageLog, bool> Observer_MessageLogs;


    public event Action<PortraitManager, List<string>> Observer_LogsCharaChange;
    public void Log_TrySetChara(PortraitManager refID, bool isAnimating)
    {
        bool debug = true || scr_System_CentralControl.current.LogPrefs.DLog_Portraits;
        if (debug) Debug.Log($"Log_TrySetChara {(refID == null ? "null" : refID.Owner.FirstName )} {isAnimating}");
        if (LogManager.SetLogChara(refID, isAnimating))
        {
            if (debug) Debug.Log("Log_TrySetChara true " + refID.Owner.CallName);
            Observer_LogsCharaChange?.Invoke(refID, new List<string>());
        }
    }
    public PortraitManager Log_TrySetChara(List<Character_Trainable> list, List<string> keywords)
    {
        LogManager.SetLogChara(list, true, out var selected);
        if (selected != null)
        {
            //if (scr_System_CentralControl.current.LogPrefs.DLog_Portraits) Debug.Log("Log_TrySetChara true " + selected.Owner.CallName);
            //Debug.Log($"Invoking logs change chara on {selected.Owner.CallName} with tags {String.Join("|", keywords)}");
            Observer_LogsCharaChange?.Invoke(selected, keywords);
        }
        return selected;
    }
    public void Log_TryClearChar(bool isAnimating)
    {
        LogManager.ClearLogChara(isAnimating);
    }

    [SerializeField] Dictionary<int, Job> Index_JobReferenceID = new Dictionary<int, Job>();

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
            return Player.CurrentJob != null && Player.CurrentJob is Job_Sex_Group;
        }
    }


    [NonSerialized] public bool ColdLoad = true;

    public void UpdateScene()
    {
        NotifyUpdate();
    }

    public void RegisterExecutedAP(ActionPackage ap)
    {
        Map.dirtyCharaAPRef.Add(ap);
        executedPackagesByRoom.Add(ap, ap.RoomKey);
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
        if (p == null) Debug.LogError("campaignmanager registerAP null");
        if (!registeredPackagesByRoom.ContainsKey(p.RoomKey)) registeredPackagesByRoom.Add(p.RoomKey, new List<ActionPackage>() { p });
        else
        {
            // first validate package conflict
            for (int i = registeredPackagesByRoom[p.RoomKey].Count - 1; i >= 0; i--)
            {
                var currentP = registeredPackagesByRoom[p.RoomKey][i];
                if (!ignoreConflict && currentP.Duration > 0 && UtilityEX.DetectConflict(currentP, p))
                {
                    Debug.Log("CM registerAP detect conflict former[" + currentP.COMVariantID + $"]{currentP.PackagePriority} [" + p.COMVariantID + $"]{p.PackagePriority} avoidConflict?[" + (avoidConflict) + "]");
                    if (avoidConflict)
                    {
                        if (scr_System_CentralControl.current.LogPrefs.DLog_APConflict) Debug.Log("CM registerAP detect conflict former[" + currentP.COMVariantID + "] [" + p.COMVariantID + "] avoidConflict?[" + (avoidConflict) + "], skipping");
                        return;
                    }
                    if (currentP.PackagePriority > p.PackagePriority)
                    {
                        p.isPaused = true;
                        //p.NotifyInterrupted();
                        if (scr_System_CentralControl.current.LogPrefs.DLog_APConflict) Debug.Log("CM RegisterAP: Package [" + p.DisplayName + " " + p.PackagePriority + "] is not getting registered (set to paused) due to not having at least equal priority than [" + currentP.DisplayName + " " + currentP.PackagePriority + "]");
                        return;
                    }
                    else
                    {
                        if (scr_System_CentralControl.current.LogPrefs.DLog_APConflict) Debug.LogError($"CM RegisterAP: [{currentP.DisplayName} {currentP.PackagePriority}] (remain {currentP.Duration}) is being set to paused due to conflict with [{p.DisplayName} {p.PackagePriority}] and failed priority check.");
                        p.job.LogMessage_Begin_Replace(registeredPackagesByRoom[p.RoomKey][i], p);

                        registeredPackagesByRoom[p.RoomKey][i].isPaused = true;
                        //registeredPackagesByRoom[p.RoomKey][i].NotifyInterrupted();
                        registeredPackagesByRoom[p.RoomKey].RemoveAt(i);

                    }
                }
            }
            if (p.isPaused)
            {
                var resume = true;
                foreach (var actor in p.Actors)
                {
                    if (actor.CurrentJob != null && actor.CurrentJob != p.job && !actor.CurrentJob.CanBeInterrupted)
                    {
                        resume = false;
                        if (scr_System_CentralControl.current.LogPrefs.DLog_APConflict) Debug.Log($"CM RegisterAP: resuming package [{p.DisplayName}] ? {resume} due to {actor.FirstName} currentjob {actor.CurrentJob.RefID} != {p.job.RefID}");
                        return;
                    }
                }
                if (scr_System_CentralControl.current.LogPrefs.DLog_APConflict) Debug.Log($"CM RegisterAP: resuming package [{p.DisplayName}] ? {resume}");
                p.isPaused = false;
            }
            registeredPackagesByRoom[p.RoomKey].Add(p);
        }
        Map.dirtyCharaAPRef.Add(p);
    }
    public bool Unregister(ActionPackage p)
    {
        bool debug = scr_System_CentralControl.current.LogPrefs.DLog_Jobs;
        if (debug || p is ActionPackage_LLM) Debug.Log("Unregistering AP " + p.DisplayName);
        if (!registeredPackagesByRoom.ContainsKey(p.RoomKey)) return false;
        //if (p.targetCOM.comTags.Contains("character_trainable") && p.Duration > -1) return;
        var possibleTargets = registeredPackagesByRoom[p.RoomKey].FindAll(x => UtilityEX.ArePackagesEqual(x, p));
        if (possibleTargets.Count > 0)
        {
            if (debug) Debug.Log("Unregistering AP " + p.DisplayName + ", found " + possibleTargets.Count + " matching AP");
            foreach (var i in possibleTargets) { if (registeredPackagesByRoom.ContainsKey(i.RoomKey)) registeredPackagesByRoom[i.RoomKey].Remove(i); }
        }
        else
        {
            if (debug) Debug.Log("Unregistering AP " + p.DisplayName + ", did not find any matching package");
        }
        return possibleTargets.Count > 0;
        //if (registeredPackagesByRoom[p.RoomKey].Contains(p)) registeredPackagesByRoom[p.RoomKey].Remove(p);
    }


    public Room_Instance GetCharaRoomInstance(int charaRefID)
    {
        return Map.FindRoomByChara(charaRefID);
    }



    public List<int> specialUpdateJobs = new List<int>();
    List<Room_Instance> specialUpdateRooms = new List<Room_Instance>();
    /// <summary>
    /// Clear executed APs, and resolve filming
    /// </summary>
    public void ClearExecutedAPs()
    {
        executedPackagesByRoom.Clear();
        foreach(var i in specialUpdateJobs)
        {
            if (Index_JobReferenceID.TryGetValue(i, out var job)) job.LastUpdate();
        }
        for (int i = specialUpdateRooms.Count - 1; i >= 0; i--)
        {
            var room = specialUpdateRooms[i];
            room.ClearRecords();
            if (!room.HasRecording) specialUpdateRooms.RemoveAt(i);
        }

    }

    public void NotifyRoomSpecialUpdate(Room_Instance r)
    {
        if (!specialUpdateRooms.Contains(r)) specialUpdateRooms.Add(r);
    }

    public void UpdateAllRoom()
    {
        Map.UpdateAllRoom();
    }

    public bool isCharaVisibleToPlayer(int charaRefID)
    {
        return CurrentRoom.RoomCharaRefs.Contains(charaRefID);
        //return Map.IsBothCharaInSameRoom(charaRefID, Player.RefID);// Map.FindRoomByChara(charaRefID) == Map.FindRoomByChara();//.RefID == currentRoomRef;
    }

    public void NotifyUpdateHandlerExist()
    {

        scr_UpdateHandler.current.Observer_PreUpdateTime += Job_PreUpdateTime;
        scr_UpdateHandler.current.Observer_PostUpdateTime_1 += Job_PostUpdateTime_getLogsBegin;
        scr_UpdateHandler.current.Observer_PostUpdateTime_3 += Job_PostUpdateTime;
    }

    public bool DoFullUpdate(int charaRef)
    {
        return true;
        if (charaRef == 0) return true;
        //if (Map.IsCharaInInactiveRooms(charaRef)) return false;
        return FullUpdate || Map.IsCharaInActiveFloors(charaRef);
    }

    /// <summary>
    /// Character will take 1 to 2 minutes to decide and start execute the new job.
    /// </summary>
    public void UpdateAllCharaJob()
    {
        DateTime currentTime = scr_System_Time.current.getCurrentTime();
        int currentHour = currentTime.Hour;
        List<string> s = scr_System_CentralControl.current.LogPrefs.DLog_Update ? new List<string>() : null;
        if (s != null) s.Add("UpdateAllChara [" + currentTime.ToString() + "]");
        var list = this.Index_referenceID.Values.ToList();

        if(true)
        {

            foreach(var chara in list)
            {
                if (chara.RefID > 0 && DoFullUpdate(chara.RefID))
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
        var status = scr_System_CampaignManager.current.Player.Stats.GetStatusByStringMatch("chara_status_sleeping");

        if (Player.isSleeping && status != null && status.duration > 0 && status.Severity > 0)
        {
            updateTime = status.duration;
            returnVal = true;
        }
        else
        {
            foreach (var p in GetExistingPackages(scr_System_CampaignManager.current.Player, checkUnexecuted, true, true))
            {
                //if (p.Duration < 1) continue;
                //if (!checkUnexecuted && !p.Ticked) continue;
                // if (p.actorRefs.Contains(0) || p.masterRef == 0)
                //{
                //s += p.DisplayName + "[" + p.Duration + "] ";
                if (p.Duration < 1) continue;
                returnVal = true;
                updateTime = Math.Max(updateTime, p.Duration);
                //}
            }
        }



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

    public static List<ActionPackage> GetExistingPackages2 (Character_Trainable c, List<ActionPackage> list, bool checkUnexecuted, bool checkExecuted, bool checkMaster, bool checkDeleted = false)
    {
        List<ActionPackage> results = new List<ActionPackage>();
        foreach (var p in list)
        {
            if (!checkDeleted && p.Duration < 0) continue;
            else if (!p.actorRefs.Contains(c.RefID) && p.masterRef != c.RefID) continue;
            else if (results.Contains(p)) continue;
            else if (!p.Ticked && !checkUnexecuted) continue;
            else if (p.Duration == 0 && !checkExecuted) continue;
            else if (p.Duration < 0 && !checkDeleted) continue;
            else results.Add(p);
        }
        return results;
    }


    public bool ShowCharaLog(int refID)
    {
        if (refID < 0) return false;
        

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
        if (scr_UpdateHandler.current.Lock)
        {
            Debug.LogError($"Error ToggleTimeStop, system currently locked in update");
            return;
        }
        scr_System_Time.current.ToggleTimeStop();

        string s = "";
        if (!scr_System_Time.current.NotTimetop)
        {
            s = "TIMESTOP!";
            foreach(var c in this.Index_referenceID)
            {
                c.Value.TimestopStart();
            }
            // updatehandler flush logs
        }
        else
        {
            s = "TIMESTOP ended.";
            foreach (var c in this.Index_referenceID)
            {
                c.Value.TimestopEnd();
            }
            // updatehandler flush logs
        }
        //scr_UpdateHandler.current.Message.FlushCollectLogsCallback();
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

    public void FreeUpdate(int addLogRefID = -1, string addText = "", bool silent = false, bool flushLogsOnly = false)
    {
        //FreeUpdateAsync();
        ClearLogs(flushLogsOnly);
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

    [NonSerialized] public static int ThrottledUpdate = 3;
    public static bool FullUpdate
    {
        get
        {
            return scr_System_Time.current.getCurrentTime().Minute % 3 == 0;
        }
    }

    public void FreeUpdateOneStep(ref int totalUpdateTime, ref int updateTime)
    {
        List<ActionPackage> detachedAPs = new List<ActionPackage>();

        //if (registeredPackagesByRoom.Count < 1) Debug.LogError("CampaignManager FreeUpdate called but no package in list");

        int updateDuration = 1;

        foreach (var kvpair_list in registeredPackagesByRoom)
        {
            var floor = Map.GetFloorByRoomRefID(kvpair_list.Key);

            if (floor != null && ( true || kvpair_list.Value.Find(x=>x.actorRefs.Contains(0)) != null ))
            {
                // normal loop
                updateDuration = 1;
            }
            else if (FullUpdate)
            {
                updateDuration = 3;
            }
            else
            {
                continue;
            }


            List<ActionPackage> roomEffects = new List<ActionPackage>();
            List<int> allActorsInRoom = CharaRefsInRoom(kvpair_list.Key);
            List<int> freeActors = new List<int>(allActorsInRoom);
            var list = kvpair_list.Value;

            for (int i = list.Count - 1; i >= 0; i--)
            {
               // list.RemoveAt(list.Count);// -> used to launch a CTD error
                //Debug.Log("list count " + i + " " + String.Join("|", list));
                if (i >= list.Count) continue;  // list might get modified
                ActionPackage p = list[i];
                int roomKey = p.RoomKey;
                int duration = p.Duration;
                if (p is ActionPackage_LLM)
                {
                    Debug.Log("ticking llm package");
                }
                if (p.isPaused) continue;
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

                        if (p != null && roomKey != -1 && !executedPackagesByRoom.ContainsKey(p)) executedPackagesByRoom.Add(p, roomKey);
                        kvpair_list.Value.Remove(p);// (p);
                            
                        //kvpair_list.Value.RemoveAt(i);

                    }
                    else if (p is ActionPackage_PathTo && p.RoomKey != kvpair_list.Key)
                    {
                        // check if movement ap changed room
                        
                        kvpair_list.Value.Remove(p);// (p);
                        detachedAPs.Add(p);
                        
                    }
                    else if (!p.isTemporaryAP)
                    {
                        Debug.LogError($"package ticked and duration reset to {p.Duration}");
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

    public void NotifyEndJob(Job j)
    {
        Unregister(j);
    }
    

    public bool isPackageExecuted(ActionPackage p)
    {
        return this.executedPackagesByRoom.ContainsKey(p);
    }

    public void Unregister(Job j)
    {
        Index_JobReferenceID.Remove(j.RefID);
        specialUpdateJobs.Remove(j.RefID);
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

        if (j.RequireAdditionalLastUpdate) specialUpdateJobs.Add(j.RefID);
        //Debug.Log("Registering job " + j.RefID);
        //index_JobReferenceIDCache = null;
        //Index_JobReferenceID.Add(j.RefID, j);

        //if (j.actorRefID.Contains(0)) NotifyPlayerJobChange(j.RefID, j);

        return j.RefID;
    }

    Dictionary<int, ExpeditionInstance> Index_ExpeditionInstances = new Dictionary<int, ExpeditionInstance>();

    public void Unregister(ExpeditionInstance i)
    {
        if (this.Index_ExpeditionInstances.ContainsKey(i.RefID))
        {
            this.Index_ExpeditionInstances.Remove(i.RefID);
        }
        RebuildExpeditionsList();
    }

    public ExpeditionInstance Register(ExpeditionInstance i, int forceRefID = -1)
    {
        if (forceRefID > -1 && !Index_ExpeditionInstances.ContainsKey(forceRefID))
        {
            // do nothing
        }
        else
        {
            if (forceRefID != -1) Debug.LogError("Registering Job with forceRefID " + forceRefID + " Indexjobref already contain forceRef");
            forceRefID = GetRefID;
        }

        Index_ExpeditionInstances.Add(forceRefID, i);
        i.Register(forceRefID);
        return i;
    }

    private ViewMode viewMode;
    public ViewMode CurrentViewMode { get { return viewMode; } }
    public event Action<ViewMode,bool> Observer_CurrentViewMode;

    public void ChangeCurrentViewMode(ViewMode vm, bool lockView = false)
    {
        if (actions_viewChange != null)
        {
            var act = actions_viewChange;
            actions_viewChange = null;
            scr_UpdateHandler.current.EventHandler.StartEvent(act, false);
            scr_System_CampaignManager.current.FreeUpdate(-1, act.Name);
            return;
        }

        if (scr_System_CentralControl.current.LogPrefs.DLog_UIChange && vm == ViewMode.View_Room) Debug.Log("changevm");
        // if update lock, allow only setting to logs
        if (scr_UpdateHandler.current.Animating && vm != ViewMode.View_Logs)
        {
            Debug.LogError($"ChangeCurrentViewMode to {vm}, error, still animating");
            return;
        }
        else if (!scr_UpdateHandler.current.Updating && Combat.PlayerCombatInstance != null)
        {
            viewMode = ViewMode.View_Combat;
            Observer_CurrentViewMode?.Invoke(viewMode, lockView);
        }
        else if (viewMode == ViewMode.View_Logs && vm == ViewMode.View_Room && ExistPlayerPackage(out int a, out int b, false)) scr_UpdateHandler.current.StartUpdate(true);

        else if (scr_UpdateHandler.current.Lock && vm != ViewMode.View_Logs)
        {
            Debug.Log($"ChangeCurrentViewMode failed, currently locked");
            return;
        }
        else
        {
            if (viewMode != vm) viewMode = vm;
            Observer_CurrentViewMode?.Invoke(vm, lockView);
            Observer_UpdateNotice?.Invoke(false);
            if (viewMode == ViewMode.View_Room)
            {
                scr_UpdateHandler.current.Animating = false;
                EnableCanvasAnchor();
            }
        }
    }

    public event Action<int, Job> Observer_PlayerJob;
    public void NotifyPlayerJobChange(int jobRefID, Job job)
    {
        //Debug.LogError("ONPLAYERJOBCHANGE OBSERVER CALLED");
        Observer_PlayerJob?.Invoke(jobRefID, job);
    }

    private int currentTarget = -1;
    public event Action<int, bool> Observer_CurrentTarget;
    public event Action<int, bool> Observer_CurrentTargetEX;

    public event Action<bool, bool> Observer_LogsClear;

    public void ChangeCurrentTarget(int refID = 0, bool forceUpdate = false)
    {
        if (Index_referenceID.ContainsKey(refID))
        {
            if (currentTarget != refID || forceUpdate)
            {
                currentTarget = refID;
                Observer_CurrentTarget?.Invoke(currentTarget, forceUpdate);
            }
        }
    }
    public void ChangeCurrentTarget_Training(int refID = 0)
    {
        ChangeCurrentTarget(refID);
        if (displaySex && COMmanager != null)
        {
            var playerref = Player.RefID;
            COMmanager.SexComDoers.Clear();
            COMmanager.SexComDoers.Add(playerref);
            COMmanager.SexComReceivers.Clear();
            if (refID != playerref && refID >= 0 && Player.CurrentJob.actorRefID.Contains(refID)) COMmanager.SexComReceivers.Add(refID);
            Observer_UpdateNotice?.Invoke(false);
        }
    }

    public void Job_PreUpdateTime()
    {
        int currentMinute = scr_System_Time.current.getCurrentTime().Minute;

        foreach (var job in this.Index_JobReferenceID.Values)
        {
            job.PreUpdateTime(currentMinute);
        }
    }


    public void Job_PostUpdateTime_getLogsBegin()
    {
        //System.Threading.Tasks.Parallel.ForEach(this.Index_JobReferenceID.Values, job => job.PostUpdateTime_getLogsBegin());
        return;
        foreach (var job in this.Index_JobReferenceID.Values)
        {
            job.PostUpdateTime_getLogsBegin();
        }
    }


    /// <summary>
    /// Called on PostUpdateTime3
    /// </summary>
    public void Job_PostUpdateTime()
    {
        System.Threading.Tasks.Parallel.ForEach(this.Index_JobReferenceID.Values, job => job.PostUpdateTime());
    }

    public void ResetAllActorJobs()
    {
        foreach(var c in this.Index_referenceID.Values)
        {
            c.ChangeCurrentJob(null);// = null;
        }
        this.registeredPackagesByRoom.Clear();
        foreach(var j in this.Index_JobReferenceID.Values)
        {
            j.Clear();
        }
        this.Map.Clear();
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
            foreach(var chara in CharaInCurrentRoom)
            {
                if(chara.CurrentJob != null) i.Add(chara.CurrentJob.RefID);
                if (chara.InteractionJob != null) i.Add(chara.InteractionJob.RefID);
            }
            if(Player.CurrentJob != null) i.Add(Player.CurrentJob.RefID);

            i = i.Distinct().ToList();
            return i;
        } }

    public bool ConsoleTargetEX = false;

    public Character_Trainable CurrentTarget { get { if (currentTarget >= 0) return this.FindInstanceByID(currentTarget);
            else return this.FindInstanceByID(0);
        } }

    [SerializeField] Character_Trainable _currentTargetEX = null;
    public Character_Trainable CurrentTargetEX { 
        get { return _currentTargetEX; }
        set
        {
            if (value == null) _currentTargetEX = CurrentTarget;
            else _currentTargetEX = value;
           // Debug.Log($"Invoking CurrentTargetEX {_currentTargetEX.RefID}");
            Observer_CurrentTargetEX?.Invoke(_currentTargetEX.RefID, false);
        }
    }
    /// <summary>
    /// call for currentTargetEX to reload
    /// </summary>
    public void NotifyCurrentTargetEXReset()
    {
        //Observer_CurrentTarget?.Invoke(currentTarget, true);
        Observer_CurrentTargetEX?.Invoke(_currentTargetEX.RefID, true);
    }
    public void NotifyCurrentTargetReset()
    {
        Observer_CurrentTarget?.Invoke(currentTarget, true);
    }

    public PortraitManager.CharaPortrait CurrentTargetEXPortrait = null;


    int currentRoomRef = -1;
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
        ChangeCurrentTarget(CurrentTargetRef, true);
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

    public List<Character_Trainable> CharaInRoom(int refID)
    {
        return Map.GetRoomByRef(refID).RoomChara;
    }
    public List<int> CharaRefsInRoom(int refID)
    {
        return Map.GetRoomByRef(refID).RoomCharaRefs;
    }

    [JsonProperty] protected Dictionary<string, string> relationshipRecords = new Dictionary<string, string>();

    public List<Character_Trainable> CharaInCurrentRoom { get {
            return Map.GetRoomByRef(currentRoomRef).RoomChara;
        } }
    public List<int> CharaRefInCurrentRoom
    {
        get
        {
            return Map.GetRoomByRef(currentRoomRef).RoomCharaRefs;
        }
    }

    public Party party = new Party();

    Map_Instance map;
    public Map_Instance Map { get { return map; } }


    int debugRoomRef = 0;
    string CurrentCampaignID = "";
    public Room_Instance debugRoom;

    protected int statisRoomID = -1;
    Room_Instance _statisRoom = null;

    /// <summary>
    /// Room that houses persistent characters (such as combat dummies)
    /// </summary>
    public Room_Instance StasisRoom { get
        {
            if (_statisRoom == null) {
                if (statisRoomID == -1)
                {
                    _statisRoom = new Room_Instance(null, null);// Register();
                    statisRoomID = Register(_statisRoom);//.RefID;
                }
                else
                {
                    _statisRoom = scr_System_CampaignManager.current.Map.GetRoomByRef(statisRoomID);
                }
            } 
            return _statisRoom;
        }set
        {
            _statisRoom = null;
        }
    }

    protected int tempRoomID = -1;
    Room_Instance _tempRoom = null;

    /// <summary>
    /// Room that houses temporary characters (such as combat encounter generated enemies)
    /// </summary>
    public Room_Instance TemporaryRoom
    {
        get
        {
            if (_tempRoom == null)
            {
                if (tempRoomID == -1)
                {
                    _tempRoom = new Room_Instance(null, null);// Register();
                    tempRoomID = Register(_tempRoom);//.RefID;
                }
                else
                {
                    _tempRoom = scr_System_CampaignManager.current.Map.GetRoomByRef(tempRoomID);
                }
            }
            return _tempRoom;
        }
    }

    CampaignSettings _currentCampaign = null;
    public CampaignSettings CurrentCampaign
    {
        get
        {
            if (_currentCampaign == null && CurrentCampaignID != "")
            {
                _currentCampaign = scr_System_Serializer.current.MasterList.CampaignSettings.GetByID(CurrentCampaignID);
            }
            return _currentCampaign;
        }
        set
        {
            _currentCampaign = value;
            CurrentCampaignID = value == null ? "" : _currentCampaign.ID;
        }
    }

    public void StartCampaign(CampaignSettings camp, CampaignSettings_ExtraOptions camp_ex, Character_Trainable main, Character_Trainable sub = null)
    {
        // Debug.Log("3d8 " + Utility.Dice(1, 8) + " " + Utility.Dice(1, 8) + " " + Utility.Dice(1, 8));

        viewMode = ViewMode.View_Room;

        this.CurrentCampaignID = camp.ID;

        map = new Map_Instance();

        debugRoom = new Room_Instance(null, null);
        currentRoomRef = Register(debugRoom, debugRoomRef);

        refIDCounter = 1000000;

        main = InstantiateCharacter(main, debugRoom,0);
        //main = Register(main);
        if (sub != null) sub = InstantiateCharacter(sub, debugRoom);

        scr_System_Time.current.initializeTime();

        var hostile = FindorAddHomeFactionByID("AlwaysHostile");

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
                    PortraitManager.CharaPortrait cm = JsonConvert.DeserializeObject<PortraitManager.CharaPortrait>(ini.initArguments[0], UtilityEX.SerializerSettings);
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
                        FindInstanceByID(0).FactionManager.SetHomeFaction(f.ID, ini.initArguments[2] == "true" ? Manageable_GuestStatus.Manager : Manageable_GuestStatus.Member);
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
                    c.FactionManager.SetHomeFaction(g.ID, Manageable_GuestStatus.Member);

                    if (ini.initArguments.Count >=5 && Enum.TryParse<Manageable_GuestStatus>( ini.initArguments[4], out var guestStatus))
                    {
                        c.FactionManager.SetTempHomeFaction(f.ID, guestStatus, false);
                    }
                    else
                    {
                        c.FactionManager.SetTempHomeFaction(f.ID);
                    }

                }
                else if (ini.initClass == "campaign_init_map_extra")
                {
                    map.AddMapTemplate(ini.initArguments[0], ini.initArguments[1].ToString(),true);
                    // FindInstanceByID(0).baseID = ini.initArguments[0];
                }
#if UNITY_EDITOR
                else if (ini.initClass == "campaign_init_map_extra_debug")
                {
                    for(int i = 0; i < 1; i ++)
                    {
                        map.AddMapTemplate(ini.initArguments[0], $"{ini.initArguments[1]}_{i}", true);
                    }
                }
#endif
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
                        Debug.Log($"Instantiating inventory {f1.FactionDisplayName} {f2.DisplayName} {ini.initArguments[2]} {f4}");
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
        Map.UpdateRoomForceGreeting();
        ColdLoad = false;

        UpdateScene();
        scr_System_Time.current.UpdateTime(0, 0, 0, 0, true);
    }

    protected IEnumerator CachePortraitCoroutine()
    {
        var chars = new List<Character_Trainable>();
        foreach (var chara in this.Index_referenceID.Values)
        {
            if (chara.FactionManager.HasPlayerFaction) chars.Add(chara);
        }
        int total = chars.Count;
        for (int i = 0; i < total; i++)
        {
            Observer_LoadProgress?.Invoke((float)i / Mathf.Max(total, 1), chars[i].CallName);
            yield return chars[i].PortraitManager.CacheInternal(chars[i]);
        }
        Observer_LoadProgress?.Invoke(1f, "");
        Debug.Log("CachePortraitComplete");
    }

    public void CacheCharaPortrait(Character_Trainable c)
    {
        StartCoroutine(c.PortraitManager.CacheInternal(c));
    }


    /// <summary>
    /// Starts a new game and fires load events for the loading screen to listen to.
    /// Call this instead of StartCampaign directly when a loading screen is present.
    /// </summary>
    public void StartNewGame(
        CampaignSettings camp,
        CampaignSettings_ExtraOptions camp_ex,
        Character_Trainable main,
        Character_Trainable sub)
    {
        scr_System_SceneManager.current.LoadScene(GlobalValues.GameScene);
        StartCoroutine(InitializeNewGame(camp, camp_ex, main, sub));
        scr_System_SceneManager.current.UnloadScene(GlobalValues.IntroScene);
    }

    /// <summary>
    /// Loads a save file and fires load events for the loading screen to listen to.
    /// Call this instead of scr_UpdateHandler.LoadSaveFile directly when a loading screen is present.
    /// </summary>
    public void StartLoadSave(SaveFileHolder saveHolder)
    {
        StartCoroutine(InitializeLoadSave(saveHolder));
    }

    private IEnumerator InitializeNewGame(
        CampaignSettings camp,
        CampaignSettings_ExtraOptions camp_ex,
        Character_Trainable main,
        Character_Trainable sub)
    {
        Observer_LoadStart?.Invoke();
        yield return null;
        StartCampaign(camp, camp_ex, main, sub);
        yield return null;
        yield return CachePortraitCoroutine();
        Observer_LoadComplete?.Invoke();
    }

    private IEnumerator InitializeLoadSave(SaveFileHolder saveHolder)
    {
        Observer_LoadStart?.Invoke();
        yield return null;
        scr_UpdateHandler.current.LoadSaveFile(saveHolder, true);
        yield return null;
        yield return CachePortraitCoroutine();
        Observer_LoadComplete?.Invoke();
    }



    [NonSerialized] public int jobRef_playerCOM = 2;
    [NonSerialized] public int jobRef_followPlayerCOM = 1;

    Dictionary<int, Character_Trainable> Index_referenceID;

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

    public List<Item_Instance> GetAllEquippedItemsFrom(Character_Trainable c)
    {
        var list = c.EquippedItemRefs;
        var results = new List<Item_Instance>(list.Count);
        foreach(var i in list) if (Index_ItemReferenceID.TryGetValue(i, out var item)) results.Add(item);
        return results;
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

    public void UnregisterRoom(int refID)
    {
        var room = this.Map.UnregisterRoom(refID);
        if (room == null) return;
        if (room != null && this.registeredPackagesByRoom.TryGetValue(refID, out var packages))
        {
            Debug.Log($"UnregisterRoom {room.DisplayName} with {packages.Count} APs");
            registeredPackagesByRoom.Remove(refID);
        }

        foreach (var chara in this.Index_referenceID.Values)
        {
            chara.NotifyRoomUnregister(room);
        }
    }

    public void Unregister(Character_Trainable c)
    {
        var room = Map.FindRoomByChara(c.RefID);
        if (room != null) room.RemoveChara(c);
        this.Index_referenceID.Remove(c.RefID);
        // first, notify everyone that this should be purged
        foreach (var chara in this.Index_referenceID.Values)
        {
            chara.NotifyCharaUnregister(c);
        }

        deletedRefIDs.Add(c.RefID);
        Unregister(c.InteractionJob);
        Map.charaRoomRef.Remove(c.RefID);
        Debug.Log($"Unregistering chara {c.FirstName}");
        foreach(var m in c.FactionManager.Factions)
        {
            m.RemoveFromFaction(c);
        }
        c.DisposeInternal();
    }

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

    public int Register(Room_Instance r, int forceRefID = -1)
    {
        if (forceRefID == -1) forceRefID = GetRefID;
        if (map.HasRoomWithRef(forceRefID)) Debug.LogError("CampaignManager Registering room with force refID [" + forceRefID + "]already taken!");
        else
        {
            r.Register(forceRefID);
            map.AddRoom(r);
        }
        return r.RefID;
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

        
        List<Character_Trainable> charaInDebug = new List<Character_Trainable>(Map.GetRoomByRef(0).RoomChara) ;

        if(charaInDebug.Count < 1)
        {
            Debug.Log("Player already initialized into another room, init location failed");
        }
        foreach (var i in charaInDebug)
        {
            MoveCharacterTo(i, room);
            Debug.Log("CampaignManager: MoveCharaToRoom character refID [" + i + "] registered to room [" + room.DisplayName + "]");

        }
        if(charaInDebug.Any(x=>x.RefID == 0)) currentRoomRef = room.RefID;
        
        //NotifyUpdate();
        UpdateScene();
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="flushOnly">if true, do not clear logs. else, clear logs.</param>
    public void ClearLogs(bool flushOnly = false, bool clearAll = false)
    {
        if (!flushOnly) LogManager.Clear();
        Observer_LogsClear?.Invoke(flushOnly, clearAll);
    }


    private void UpdateCurrentTarget()
    {
        if (currentTarget == 0) return;
        if (currentTarget > 0 && CharaRefInCurrentRoom.Contains(currentTarget)) return;
        ChangeCurrentTarget(0);
    }

    public bool isPlayerPartyMember(int i)
    {
        return i == 0 || PlayerPartyMembers.Contains(i);
    }
    public List<int> PlayerPartyMembers { get { 
            return party.MemberRefIDs; } }
    public void MoveCharacterTo(Character_Trainable charaRef, int targetRoomRef)
    {
        MoveCharacterTo(charaRef, Map.GetRoomByRef(targetRoomRef));
    }
    public void MoveCharacterTo(int charaRef, int targetRoomRef)
    {
        MoveCharacterTo(FindInstanceByID(charaRef), Map.GetRoomByRef(targetRoomRef));
    }
    public void MoveCharacterTo(Character_Trainable charaRef, Room_Instance targetRoomRef)
    {
        bool playerMoved = false;
        bool updateScene = false;
        bool updateTarget = false;
        if (CharaRefInCurrentRoom.Contains(charaRef.RefID)) updateScene = true;
        else if (charaRef.RefID == 0) updateScene = true;
        else if (targetRoomRef.RefID == currentRoomRef) updateScene = true;

        if (charaRef.RefID == CurrentTargetRef || charaRef.RefID == 0) updateTarget = true;
        //if (charaLocation[charaRef] == currentRoomRef) updateTarget = true;
        //else if (targetRoomRef == currentRoomRef && currentTarget < 1) updateTarget = true;

        Map.MoveCharaTo(charaRef, targetRoomRef);

        if (charaRef.RefID == 0)
        {
            playerMoved = true;
            // if player is moved, do additional update

            foreach (int i in PlayerPartyMembers)
            {
                // do we break team if timestop ?
                //charaLocation[i] = targetRoomRef; // move party members
                var teammate = FindInstanceByID(i);
                MoveCharacterTo(teammate, targetRoomRef);

                if (teammate.CurrentJob != null)
                {
#if UNITY_EDITOR
                    Debug.Log($"Teammate {teammate.FirstName} still has ongoing job on moving room, force exiting");
#endif
                    teammate.ChangeCurrentJob(null);
                }
            }
            ChangeCurrentRoom(Map.GetRoomByRef(targetRoomRef.RefID));
            //UpdateScene();
        }
        

        if (updateScene || playerMoved) UpdateScene();
        if (updateTarget) UpdateCurrentTarget();
    }

    Dictionary<string, Manageable> organizations = new Dictionary<string, Manageable>();
    [JsonIgnore] public List<Manageable> Factions { get { return organizations.Values.ToList(); } }
    public Manageable FindorAddHomeFactionByID(string id)
    {
        if (organizations.TryGetValue(id, out var target))
        {
            return target;
        }
        else
        {
            var newstuff = new Manageable_HomeFaction(id);
            organizations.Add(id, newstuff);
            return newstuff;
        }
    }

    public Manageable FindFactionByID(string id)
    {
        if (organizations.TryGetValue(id, out var target))
        {
            return target;
        }
        else return null;
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

        MoveCharacterTo(c, room);
        //Debug.LogError("Adding charaRef "+c.RefID +" to roomRef "+room.RefID);
        //Debug.Log("CampaignManager: Instantiate character baseID [" + c.baseID + "] with refID [" + c.RefID + "] registered to room ["+room.RefID+"]");

        foreach(presetInventory p in c.Template.initialInventory)
        {
            Item_Instance i = WorldManager.Instantiate(p.ID, p.nameOverwrite);
            //Debug.Log("Instantiating chara equipping itemref " + i.RefID);
            if (i == null) continue;
            c.Inventory.AddItem(i);
            if (i.GetComp_Equippable() != null && c.EquipItem(i.RefID, false))
            {   // add to inv
                c.Inventory.Remove(i);
            }
        }
        foreach (presetInventory p in c.Template.overrideInventory)
        {
            Item_Instance i = WorldManager.Instantiate(p.ID, p.nameOverwrite);
            //Debug.Log("Instantiating chara equipping itemref " + i.RefID);
            if (i == null) continue;
            c.Inventory.AddItem(i);
            if (i.GetComp_Equippable() != null && c.EquipItem(i.RefID, false))
            {   // add to inv
                c.Inventory.Remove(i);
            }
            

        }
        return c;
    }

    protected Character_Trainable GetCharaTemplate(string ID)
    {
        var genTemplate = scr_System_Serializer.current.MasterList.CharGenTemplates.GetByID(ID);
        if (genTemplate != null && genTemplate.TargetBaseID != "")
        {
            //Debug.Log($"CampaignManager: Instantiate request for [{ID}] found genTemplate, generating instead [{genTemplate.TargetBaseID}]");
            var original_template = GetCharaTemplate(genTemplate.TargetBaseID);

            var str = JsonConvert.SerializeObject(original_template, UtilityEX.SerializerSettings);
            var template = JsonConvert.DeserializeObject<Character_Trainable>(str, UtilityEX.SerializerSettings);

            // template.BaseID = ID;
            if (genTemplate.title != "") template.Title = genTemplate.title;
            template.Template.overrideInventory = genTemplate.inventoryOverride;
            if (genTemplate.useNameGen)
            {
                scr_System_Serializer.current.MasterList.CharGenTemplates.GenerateNamesFor(template, genTemplate.Appearance, genTemplate.nameGen_firstName, genTemplate.nameGen_middleName, genTemplate.nameGen_lastName, genTemplate.nameDisplayFormat);
            }
            template.Template.SetGender(genTemplate.Appearance);
            template.Template.stat_STR = (int)Utility.RandVariation(genTemplate.str_base == 0 ? template.Template.stat_STR : genTemplate.str_base, genTemplate.str_var);
            template.Template.stat_CON = (int)Utility.RandVariation(genTemplate.con_base == 0 ? template.Template.stat_CON : genTemplate.con_base, genTemplate.con_var);
            template.Template.stat_PSY = (int)Utility.RandVariation(genTemplate.psy_base == 0 ? template.Template.stat_PSY : genTemplate.psy_base, genTemplate.psy_var);
            template.Template.stat_WIL = (int)Utility.RandVariation(genTemplate.wil_base == 0 ? template.Template.stat_WIL : genTemplate.wil_base, genTemplate.wil_var);

            if (genTemplate.setHeight > 0) template.Template.Height = genTemplate.setHeight;
            if (genTemplate.heightVariation > 0) template.Template.Height = (int)Utility.RandVariation(template.Template.Height, genTemplate.heightVariation);

            if (genTemplate.setWeight > 0) template.Template.Weight = genTemplate.setWeight;
            if (genTemplate.weightVariation > 0) template.Template.Weight = (int)Utility.RandVariation(template.Template.Weight, genTemplate.weightVariation);

            return template;
            // operate on template
            //return template
        }
        return scr_System_Serializer.current.index_Characters_Bases.GetChara(ID);
    }

    public Character_Trainable InstantiateCharacter_FromBaseID(string ID, Room_Instance room, int forceRefID = -1)
    {
        //Debug.Log($"CampaignManager: Instantiate request for ID [{ID}]");
        Character_Trainable chara = GetCharaTemplate(ID);

        if (chara != null && map.HasRoomWithRef(room.RefID))
        {
            return InstantiateCharacter(chara, room, forceRefID);
        }
        else
        {
            string s = "InstantiateCharacter error";
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

    private bool debugMode = false;
    public bool DebugMode { get { return debugMode; } set { debugMode = value; } }

    protected int deterministicThreshold = 65;
    public int DeterministicThreshold { get { return deterministicThreshold; } }
    private bool deterministicRolls = true;
    public bool DeterministicRolls { get { return deterministicRolls; } set { deterministicRolls = value; } }

    [NonSerialized][JsonIgnore] public string QuickSaveFilePath = "";

    public scr_panel_COMmanager COMmanager;
    public void Unregister(Item_Instance i)
    {
        if (Index_ItemReferenceID.ContainsKey(i.RefID))
        {
            if (scr_System_CentralControl.current.LogPrefs.DLog_Inventory) Debug.Log($"Item {i.DisplayName} unregistered " + i.RefID);
            Index_ItemReferenceID.Remove(i.RefID);
            deletedRefIDs.Add(i.UnregisterItem());
        }
    }

    public CombatManager Combat = new CombatManager();

    public bool isInCombat(int charaRef)
    {
        return Combat.isCharaInCombat(charaRef);
    }

    public void EndOngoingCombat(int charRef)
    {
        Combat.EndOngoingCombatWith(charRef);
    }

    public menu_combatSim prefab_Simulation;
    public void QueueCombatSimulation(Character_Trainable self, List<int> teammates)
    {

        EventInstance simEvent = new EventInstance(self, "CombatSimulation", "");

        List<Character_Trainable> team = new List<Character_Trainable>();
        simEvent.Targets.Add("teammates", team);
        foreach (var i in teammates) if (i != self.RefID) team.Add(scr_System_CampaignManager.current.FindInstanceByID(i));

        var teamWhole = new List<int>(teammates);
        teamWhole.Add(self.RefID);

        simEvent.FunctionCalls.Add("startCombat", new List<Action>() { () => StartCombatSimulation(teamWhole) });

        scr_UpdateHandler.current.EventHandler.StartEvent(simEvent, false);

    }
    protected void StartCombatSimulation(List<int> self, Action successCallback = null, Action failCallback = null)
    {
        // open up combat selection UI, and ui call startcombat proper
        menu_combatSim detail = scr_System_SceneManager.current.LoadCanvasIntoScene(prefab_Simulation.GetComponent<RectTransform>(), CanvasAnchor == null ? null : CanvasAnchor.PanelAnchor_AlwaysEnable).GetComponent<menu_combatSim>();
        detail.InitializeWithArgument(self, successCallback, failCallback);
    }


    public scr_menu_mealadditives prefab_Meals;

    public void QueueMealAdditive(Manageable f)
    {
        // open up combat selection UI, and ui call startcombat proper
        scr_menu_mealadditives detail = scr_System_SceneManager.current.LoadCanvasIntoScene(prefab_Meals.GetComponent<RectTransform>(), CanvasAnchor == null ? null : CanvasAnchor.PanelAnchor_AlwaysEnable).GetComponent<scr_menu_mealadditives>();
        detail.InitializeWithArgument(f);
    }


    public void StartCombat(TeamComposition teamA, TeamComposition teamB, string victoryEvID, string drawEvID, string defeatEvID, EventInstance source = null, bool forcePlayerInstance = false)
    {
        Combat.StartCombat(teamA, teamB, victoryEvID, drawEvID, defeatEvID, source, forcePlayerInstance);
    }

    public menu_Trade prefab_FactionExchange;

    public void StartFactionExchange(I_IsJobGiver fa, I_IsJobGiver fb, bool allowChara, bool allowHostile, bool allowKill, bool allowTransfer, string rescueEventID)
    {
        //Combat.StartCombat(teamA, teamB, victoryEvID, drawEvID, defeatEvID, source, forcePlayerInstance);
        menu_Trade trade = scr_System_SceneManager.current.LoadCanvasIntoScene(prefab_FactionExchange.GetComponent<RectTransform>(), CanvasAnchor == null ? null : CanvasAnchor.PanelAnchor_AlwaysEnable).GetComponent<menu_Trade>();
        trade.InitializeWithArgument(fa, fb, allowChara, allowHostile, allowKill, allowTransfer, rescueEventID);
    }
}

public static class WorldManager
{

    public static Item_Instance Instantiate(ItemEntry entry)
    {
        return Instantiate(entry.itemID, entry.itemCountOverride ? "" : entry.itemNameOverwrite, entry.itemCount);
    }
    public static Item_Instance Instantiate(string parentID, string nameOverwrite = "", int innerCount = 1)
    {
        var baseItem = scr_System_Serializer.current.GetByNameOrID_Item_Base(parentID);
        if (baseItem == null)
        {
            if (!scr_System_CentralControl.current.isSafeMode) Debug.LogError($"worldmanager instantiate error cannot find baseitem {parentID}");
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
        var targetFaction = factionOverride != "" ? factionOverride : map.initializeFaction;

        foreach (MapPlan.MapPlan_Floor fpi in map.floors)
        {
            Floor_Instance fp = WorldManager.Instantiate( fpi,disablePlayerInit, disableCharaInstantiation);

            if (fp != null && fp.refID > 0)
            {
                list.Add(fp.refID, fp);
                if (initialRefID == -1) initialRefID = fp.refID;
                fp.RegisterMapTemplate(map.ID, initialRefID);
            }
            else
            {
#if UNITY_EDITOR
                Debug.LogError($"Instantiate MapPlan Error, Cannot find Floor {fpi.ID}");
#endif
                continue;
            }
        }

        // at this stage, all character should have been initialized
        if (targetFaction != "")
        {
            // get target faction
            Manageable org = scr_System_CampaignManager.current.FindorAddHomeFactionByID(targetFaction);

            // add floor to faction and set all chara in map as faction member and set private room ownership
            foreach (var f in list.Values)
            {
                org.AddToFaction(f, true, map.setPrivateRoomOwner);
            }
            
            foreach (var ini in map.initializers)
            {
                switch (ini.initClass)
                {
                    case "campaign_init_productionOrder":
                        //Manageable f = FindorAddHomeFactionByID(ini.initArguments[0]);
                        ItemComponentTemplate_Craftable_Recipe r = Masterlist_Items.Instance.GetRecipeByID(ini.initArguments[1]);
                        Manageable.ProductionOrderType type = (Manageable.ProductionOrderType)Enum.Parse(typeof(Manageable.ProductionOrderType), ini.initArguments[2]);
                        if (int.TryParse(ini.initArguments[3], out int count)) org.AddProductionOrder(r, count, type);
                        break;
                    /*
                case "campaign_init_factionVisitor":
                    Manageable f = FindorAddHomeFactionByID(ini.initArguments[0]);
                    Manageable g = FindorAddHomeFactionByID(ini.initArguments[3]);
                    Room_Instance ri = f.ManagedRooms.Values.ToList().Find(x => x.Base.ID == ini.initArguments[1]);
                    if (ri == null) continue;
                    Character_Trainable c = InstantiateCharacter_FromBaseID(ini.initArguments[2], ri);
                    c.FactionManager.SetHomeFaction(g.ID);
                    c.FactionManager.SetTempHomeFaction(f.ID);
                    break;*/
                    case "campaign_init_factionInventory":
                        var f1 = org;
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
                        break;
                    default: break;
                }                
            }


            if (map.workHours != null && map.workHours.Count > 0) foreach (var i in map.workHours) org.InitWorkHours(i);

            if (map.managerBaseIDs != null && map.managerBaseIDs.Count > 0)
            {
                foreach (string id in map.managerBaseIDs)
                {
                    if (id == "PLAYER")
                    {
                        org.AddToFaction(scr_System_CampaignManager.current.Player, Manageable_GuestStatus.Manager);
                        if (scr_System_CampaignManager.current.Player.FactionManager.Faction_Home == null) scr_System_CampaignManager.current.Player.FactionManager.SetHomeFaction(org.ID, Manageable_GuestStatus.Manager);
                        else scr_System_CampaignManager.current.Player.FactionManager.AddWorkFaction(org.ID, true);
                    }
                    else foreach (var i in org.ManagedChara) if (i.BaseID == id) org.AddToFaction(i, Manageable_GuestStatus.Manager);
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
            org.priceMult = map.priceMult;
            org.explorationKeywords = map.explorationKeywords;
            org.mealHours = map.mealHours;
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
                if (disablePlayerInit) break;
                if (init.map_init_playerLocation != null && init.map_init_playerLocation.roomID != "")
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
#if UNITY_EDITOR
                        Debug.LogError("map_init_placeChara, error room not found.");
#endif
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