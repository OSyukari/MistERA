using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Networking;

public enum LLMStatus
{
    /// <summary>
    /// inactive
    /// </summary>
    inactive,

    /// <summary>
    /// waiting for LLM response
    /// </summary>
    active,

    waiting
}


public class scr_UpdateHandler : MonoBehaviour
{
    public EventManager EventHandler = new EventManager();

    //---------------------------------------

    Coroutine LLMRoutine = null;
    UnityWebRequest currentRequest = null;

    //public bool skipCurrentRoundClimaxCheck = false;
    /// <summary>
    /// Whether a request is currently in flight and cancellable. Deliberately keyed off LLMStatus
    /// rather than `LLMRoutine != null`: LLMStatus is set to active as the very first line of
    /// SendLLMRequest, before AddLog_LLM synchronously creates/draws the query panel (which calls
    /// ValidateAll during its own InitializeWithArgs) - whereas `LLMRoutine` isn't assigned until
    /// StartCoroutine returns, several lines later. A check against `LLMRoutine != null` would see a
    /// stale null on that first ValidateAll pass and render the cancel/regenerate button as
    /// not-yet-clickable for a request that is, in fact, already running.
    /// </summary>
    public bool CanInterruptLLMRoutine { get
        {
            return LLMStatus == LLMStatus.active;
        } }
    public void InterruptLLMRoutine()
    {
        if (LLMRoutine != null) StopCoroutine(LLMRoutine);
        LLMRoutine = null;
        if (currentRequest != null)
        {
            currentRequest.Abort();
            currentRequest.Dispose();
            currentRequest = null;
        }
        LLMStatus = LLMStatus.waiting;
        Observer_LLMStatus?.Invoke(LLMStatus);
    }
    LLMStatus _LLMStatus = LLMStatus.inactive;
    public LLMStatus LLMStatus
    {
        get
        {
            return _LLMStatus;

        }
        set
        {
            _LLMStatus = value;
            Observer_LLMStatus?.Invoke(value);
        }
    }

    public event Action<LLMStatus> Observer_LLMStatus;
    public event Action<LLMResponse> Observer_LLMResponse;
    /// <summary>
    /// Fires with the accumulated-so-far reasoning text on every reasoning-bearing streamed chunk.
    /// Also fires once with "" at the start of a request to clear any previous request's display.
    /// </summary>
    public event Action<string> Observer_LLMReasoningDelta;

    public bool LLM_Active
    {
        get
        {
            return LLMStatus > LLMStatus.inactive;
        }
    }

    [SerializeField] bool dummyLLMToggle = false;
    [JsonIgnore] public bool dummyLLM
    {
        get
        {
#if UNITY_EDITOR
            return dummyLLMToggle;
#else
    return false;
#endif
        }
    }
    public bool reserializeTemplate = false;
    /// <summary>
    /// This one function will create payload and replace stuff
    /// </summary>
    /// <param name="s"></param>
    /// <param name="updateUI"></param>
    public void SendLLMRequest(string s, bool updateUI =false)
    {
        var llm = scr_System_CentralControl.current.LLMSetting.chatCompletionModel;
        if (llm == null)
        {
            Debug.LogError("SendLLMRequest: no LLM preset selected, aborting.");
            return;
        }
        var payload = new LLMRequest();
        payload.model = llm.model;

        if (reserializeTemplate) scr_System_CentralControl.current.ResetLLMRequestTemplate();

        if (scr_System_CentralControl.current.CurrentPreset != null)
        {
            payload.LoadTemplate(scr_System_CentralControl.current.CurrentPreset);
            // slow mode config for now; fast mode will need its own entry point once wired up
            if (scr_System_CentralControl.current.LLMSlowModeConfig != null) payload.LoadTemplate(scr_System_CentralControl.current.LLMSlowModeConfig);
            // inject user
            payload.ReplaceString("<user>", scr_System_CampaignManager.current.Player.FirstName);
            var worldinfo = new LLM_WorldState();
            var worldinfostring = JsonConvert.SerializeObject(worldinfo, Formatting.Indented, UtilityEX.SerializerSettings);



            payload.ReplaceString("%%worldInfo%%", worldinfostring);
            payload.ReplaceString("%%currentRoundInput%%", s);
            payload.ReplaceString("%%currentLanguage%%", LocalizeDictionary.Instance.Index.cachedLang);
            payload.currentString = s;


            string collectionPath = Application.persistentDataPath + "/worldStateInfo.json";

            var s2 = JsonConvert.SerializeObject(worldinfo, formatting: Formatting.Indented, UtilityEX.SerializerSettingsLLM);
            if (File.Exists(collectionPath)) File.Delete(collectionPath);

            FileInfo untransDict = new System.IO.FileInfo(collectionPath);
            untransDict.Directory.Create();
            File.WriteAllText(untransDict.FullName, s2);
            Debug.Log($"creating/updating worldstateinfo collection in {collectionPath}");


        }
        else
        {
            var message = new LLMMessage_Final();
            message.role = "user";
            message.content = s;
            payload.messages.Add(message);
        }

        SendLLMRequest(payload, updateUI);
    }
    public void SendLLMRequest(LLMRequest s, bool updateUI = false)
    {
        var llm = scr_System_CentralControl.current.LLMSetting.chatCompletionModel;
        if (llm == null)
        {
            Debug.LogError("SendLLMRequest: no LLM preset selected, aborting.");
            return;
        }

        LLMStatus = LLMStatus.active;
        if (updateUI)
        {
            scr_System_CampaignManager.current.ChangeCurrentViewMode(ViewMode.View_Logs);
            scr_System_CampaignManager.current.AddLog_LLM(s);
        }

        var profile = scr_System_CentralControl.current.ResolveProviderProfile(llm);
        LLMProviderUtils.ApplyRequestShaping(s, profile);

        LLMRoutine = StartCoroutine(SendLLMRequest_Routine(s, OnLLMResponse));

    }

    void OnLLMResponse(bool success, string s)
    {
        LLMResponse response = null;

        string collectionPath = Application.persistentDataPath + "/LLMResponse.json";

        if (dummyLLM)
        {
            if (File.Exists(collectionPath))
            {
                FileInfo file = new System.IO.FileInfo(collectionPath);
                response = JsonConvert.DeserializeObject<LLMResponse>(File.ReadAllText(file.FullName), UtilityEX.SerializerSettings);


                Debug.Log($"Loading dummy Response from {collectionPath}");
            }
            else
            {
                response = new LLMResponse();
                var choice = new LLMResponse.choice();
                choice.index = 0;
                choice.message = new LLMMessage();
                choice.message.role = "assistant";
                choice.message.content = s;
                response.choices.Add(choice);

                Debug.Log($"Creating new dummy Response at {collectionPath}");
            }
        }
        else if (success)
        {
            response = JsonConvert.DeserializeObject<LLMResponse>(s);
        }
        else
        {
            response = new LLMResponse();
            var choice = new LLMResponse.choice();
            choice.index = 0;
            choice.message = new LLMMessage();
            choice.message.role = "assistant";
            choice.message.content = s;
            response.choices.Add(choice);

            Debug.Log($"Response Received! LLM failed to respond! creating file at {collectionPath}");
        }

        FinalizeLLMResponse(response);
    }

    void FinalizeLLMResponse(LLMResponse response)
    {
        string collectionPath = Application.persistentDataPath + "/LLMResponse.json";
        var s2 = JsonConvert.SerializeObject(response, formatting: Formatting.Indented, UtilityEX.SerializerSettingsLLM);
        if (File.Exists(collectionPath)) File.Delete(collectionPath);

        FileInfo untransDict = new System.IO.FileInfo(collectionPath);
        untransDict.Directory.Create();
        File.WriteAllText(untransDict.FullName, s2);

        Debug.Log($"Response Received!: creating file at {collectionPath}");

        // Settle status/routine state before notifying listeners: Observer_LLMResponse triggers the
        // UI's own ValidateAll pass (via LoadResponse -> Animate), and button validators read
        // LLMStatus/CanInterruptLLMRoutine - if those were still stale ("active") at that point,
        // buttons would render as still-busy until some unrelated later event happened to call
        // ValidateAll again.
        LLMRoutine = null;
        LLMStatus = LLMStatus.waiting;

        Observer_LLMResponse?.Invoke(response);
    }

    IEnumerator SendLLMRequest_Routine(LLMRequest payload, Action<bool, string> onResponseReceived)
    {
        Observer_LLMStatus?.Invoke(LLMStatus);
        Observer_LLMReasoningDelta?.Invoke("");

        var llm = scr_System_CentralControl.current.LLMSetting.chatCompletionModel;
        if (llm == null)
        {
            Debug.LogError("SendLLMRequest_Routine: no LLM preset selected, aborting.");
            onResponseReceived?.Invoke(false, "no LLM preset selected");
            yield break;
        }
        var endpoint = llm.endpoint;
        var apiKey = llm.key;

        payload.Purge();


        string jsonPayload = JsonConvert.SerializeObject(payload, Formatting.Indented, UtilityEX.SerializerSettingsLLM);
        string collectionPath = Application.persistentDataPath + "/LLMRequest.json";
        if (File.Exists(collectionPath)) File.Delete(collectionPath);
        FileInfo untransDict = new System.IO.FileInfo(collectionPath);
        untransDict.Directory.Create();
        File.WriteAllText(untransDict.FullName, jsonPayload);
        Debug.Log($"Request Created!: creating file at {collectionPath}");


        if (dummyLLM)
        {
            yield return new WaitForSecondsRealtime(3);

            onResponseReceived?.Invoke(true, $"dummytext received {DateTime.Now}");
        }
        else
        {
            Debug.Log($"Sending request to endpoint {endpoint}");

            var profile = scr_System_CentralControl.current.ResolveProviderProfile(llm);

            using (UnityWebRequest request = new UnityWebRequest(endpoint, "POST"))
            {
                currentRequest = request;

                byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonPayload);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);

                bool streaming = profile != null && profile.supportsStreaming;
                LLMStreamDownloadHandler streamHandler = null;
                if (streaming)
                {
                    streamHandler = new LLMStreamDownloadHandler(profile, text => Observer_LLMReasoningDelta?.Invoke(text));
                    request.downloadHandler = streamHandler;
                }
                else
                {
                    request.downloadHandler = new DownloadHandlerBuffer();
                }

                request.SetRequestHeader("Content-Type", "application/json");
                LLMProviderUtils.ApplyAuthHeaders(request, profile, apiKey);

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.Success)
                {
                    if (streaming) FinalizeLLMResponse(streamHandler.BuildFinalResponse());
                    else onResponseReceived?.Invoke(true, request.downloadHandler.text);
                }
                else
                {
                    Debug.LogError($"LLM Request Error: {request.error}");
                    onResponseReceived?.Invoke(false, request.error);
                }

                currentRequest = null;
            }
        }

    }


    //---------------------------------------

    public bool Lock
    {
        get
        {
            return Updating || EventHandler.Active || Animating || LLM_Active;
        }
    }

    bool _updating = false;
    public bool Updating
    {
        set
        {
            _updating = value;
            if (imageScript != null)
            {
                if (_updating)
                {
                    imageScript.Activate();
                    //                    imageScript.selfCanvasGroup.alpha = 1;
                    _updateTime = scr_System_Time.current.getCurrentTime();
                }
                else
                {
                    imageScript.Deactivate();
                    //imageScript.selfCanvasGroup.alpha = 0;
                }
            }

        }
        get
        {
            return _updating;
        }
    }

    public bool TempLongCOMFix = false;

    bool _animating = false;
    public bool Animating
    {
        get
        {
            return _animating;
        }
        set
        {
            _animating = value;

            //if (!_animating && cnManager.ExistPlayerPackage(out int aaa, out int bbb))
            //{
            //    click(null);
            //}
        }
    }
    private void click(PointerEventData eventData)
    {

        if (Updating || imageScript == null) return;

        // differentiate left and right click and animate all ?
        // dont. if its a single long command its as fast as skip anyway.
        // if multiple commands, it usually involves moving, and we want to let player possible break movement
        Debug.LogError("click");
        StartUpdate(false, false, true);

    }
    public static scr_UpdateHandler current;

    protected scr_System_CampaignManager _cnManager = null;
    public scr_System_CampaignManager cnManager { get
        {
            if (_cnManager == null)
            {
                _cnManager = scr_System_CampaignManager.current;
                if (_cnManager != null) _cnManager.NotifyUpdateHandlerExist();
            }
            return _cnManager;
        } }
    scr_AttachToUpdateHandler imageScript = null;

    /// <summary>
    /// Only observer should be logs panel
    /// </summary>
    /// <param name="status"></param>
    /// <param name="forcelogging"></param>
    public void InvokeEventStatus(EventStatus status, bool forcelogging)
    {
        this.Observer_EventStatus?.Invoke(status, forcelogging);
    }

    public void LoadSaveFile(SaveFileHolder saveHolder, bool unloadCanvas = true){
        if (!saveHolder.isValid) Debug.LogError("LoadSave error did not re-inject file path.");
        else LoadSaveFile(saveHolder.InnerFile, unloadCanvas);
    }

    public bool CanLoadSave(SaveFileHolder save)
    {
        return save.isValid && save.SafeMode == scr_System_CentralControl.current.isSafeMode;
    }
    protected void LoadSaveFile(SaveFile save, bool unloadCanvas = true)
    {
        NotifySL(true);
        if (unloadCanvas) scr_System_SceneManager.current.UnloadLastCanvasFromScene();
        if (scr_System_CampaignManager.current.ColdLoad)
        {
            scr_System_SceneManager.current.UnloadScene(GlobalValues.IntroScene);
            scr_System_SceneManager.current.LoadScene(GlobalValues.GameScene);
        }
        this.NotifySL(true);
        save.LoadSave();



        if (scr_System_CampaignManager.current.ColdLoad)
        {
            scr_System_CampaignManager.current.ColdLoad = false;
        }
        NotifySL(false);
        scr_System_CampaignManager.current.UpdateScene();
        scr_System_CampaignManager.current.ChangeCurrentViewMode(ViewMode.View_Logs);
        scr_System_CampaignManager.current.ChangeCurrentViewMode(ViewMode.View_Room);
    }

    public void NotifySL(bool blockAction)
    {
        if (imageScript == null) return;
        if (blockAction) imageScript.Activate();
        else imageScript.Deactivate();
    }
    public void AttachUpdateImage(scr_AttachToUpdateHandler script)
    {
        imageScript = script;
        imageScript.Observer_PointerClick += click;
    }

    // Start is called before the first frame update
    void Awake()
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

    }

    int updateTime, totalUpdateTime, totalUpdateTime2;
    bool firstPreUpdate = false;
    bool timeStop;
    bool oneLoop;
    public bool halted = false;


    public event Action Observer_PreUpdateTime_Hourly;
    public event Action Observer_PreUpdateTime;
    public event Action Observer_PostUpdateTime_1;
    public event Action Observer_PostUpdateTime_2;
    public event Action Observer_PostUpdateTime_3;
    public event Action<bool> Observer_PostUpdateTime_EventEnd;
    //public event Action Observer_PostUpdateTime_4;
    public event Action<bool> Observer_LogsSingleStepUpdate;
    public event Action<EventStatus, bool> Observer_EventStatus;

    /// <summary>
    /// tickCooldown should stay true for every call representing an actual simulated minute (the
    /// SingleUpdate loop's own per-iteration call). StartUpdate's eager pre-flight call happens once
    /// per issued command, before the loop ticks any simulated time at all, so it must pass false -
    /// otherwise every command ticks cooldowns down by one extra minute beyond how much time actually
    /// elapsed (a 1-minute command ticking cooldown by 2, a 15-minute one by 16).
    /// </summary>
    protected void PreUpdate(bool tickCooldown = true)
    {
        var time = scr_System_Time.current.getCurrentTime();
        Observer_PreUpdateTime?.Invoke();
        if (tickCooldown) this.EventHandler.TickCooldown();
        if (time.Minute == 0) Observer_PreUpdateTime_Hourly?.Invoke();
    }

    public void StartUpdate(bool init, bool silent = false, bool updateUI = false)
    {
        //if (imageScript == null) Debug.LogError("UPDATEHANDLER NO IMAGE ATTACHED");

        if (init)
        {
            firstPreUpdate = true;
            PreUpdate(false);
            timeStop = scr_System_Time.current.TimeStop;
            oneLoop = true;
        }
        //FlushCollectedLogs(false, true);
        //if (imageScript != null)
        //{
        // Updatetime is used to register loop count in minutes
        // totalUpdateTime is used when loop finishes and print value.
        // tldr, totalUpdateTime is the update duration count, and we should filter command logging based on this value.
        if (!Updating && cnManager.ExistPlayerPackage(out updateTime, out totalUpdateTime))
        {
            if(scr_System_CentralControl.current.LogPrefs.DLog_Update) Debug.Log($"UpdateHandler PlayerPackage StartCoroutine SingleUpdate, Update duration {updateTime} total {totalUpdateTime}, Eventhandler Active? {EventHandler.Active}");

            StartCoroutine(SingleUpdate());
        }
        else if (!Updating && init)
        {

#if UNITY_EDITOR
            if (EventHandler.Active && scr_System_CentralControl.current.LogPrefs.DLog_Update) Debug.Log("Eventhandler active prior to StartCoroutine SingleUpdate");
#endif
            updateTime = 1;
            totalUpdateTime = 1;
            if (scr_System_CentralControl.current.LogPrefs.DLog_Update) Debug.Log($"UpdateHandler ForceUpdate duration {updateTime}  {totalUpdateTime}");
            StartCoroutine(SingleUpdate());
        }
        else if (updateUI) NotifyLogsSingleUpdate();
    }

    /// <summary>
    /// Check for when AP is not player directly involved but should be displayed regardless <br/>
    /// log update is initiated by Job and there is already a player location visibility check prior to this.
    /// </summary>
    /// <param name="p"></param>
    /// <returns></returns>
    public bool DoDisplayCOM(ActionPackage p)
    {
        if (p == null) return false;
        if (p.targetCOM != null)
        {
            if (p.ComTags.Contains("initSex") || p.ComTags.Contains("endSex")) return true;
            else if (p.targetCOM.TimeScale * 4 < totalUpdateTime) return false;
        }
        return true;
    }
    public bool isLastUpdate()
    {
        return updateTime == 0;
    }
    /// <summary>
    /// check whether the internal COM has a TimeScale that is long enough for display.
    /// <br/>
    /// Player related COM should not use this as validity test
    /// </summary>
    /// <returns></returns>
    public bool CheckCommandDurationFilter(ActionPackage p)
    {
        if (!Updating) return true;
        if (p == null || p.targetCOM == null) return true;
        return p.targetCOM.TimeScale * 2 >= totalUpdateTime;
    }

    //List<string> currentRoundClimax = new List<string>();

    protected System.Diagnostics.Stopwatch stopWatch = new System.Diagnostics.Stopwatch();
    protected bool CallbackResumeUpdate = false;
    protected int firstLoopCounter = 2;
    public bool isFirstUpdate { get { return firstLoopCounter > 0; } }

    WaitForSeconds wait = new WaitForSeconds(0.0001f);


    string cache_elapsedTime = "";
    string ElapsedTime { get { if (cache_elapsedTime == "") cache_elapsedTime = LocalizeDictionary.QueryThenParse("ui_update_elapsedTime");
        return cache_elapsedTime;} }


    DateTime _updateTime;
    [JsonIgnore]
    public DateTime UpdateTime
    {
        get
        {
            if (Updating) return _updateTime;
            else return scr_System_Time.current.getCurrentTime();
        }
    }


    private IEnumerator SingleUpdate()
    {
        //Debug.Log("Singleupdate : start");

        Updating = true;
        int loopCount = 0;
        firstLoopCounter = 2;
        float lastDisplayYield = Time.realtimeSinceStartup;

        Job playerJob = null;
        cnManager.ChangeCurrentViewMode(ViewMode.View_Logs, true);
        FlushCollectedLogs(true, oneLoop);
        // update per room
        //Debug.Log($"Singleupdate : eventhandler end, updatetime? {updateTime}");
        //NotifyLogsSingleUpdate();
        //var copy = updateTime;
        bool timestop = scr_System_Time.current.TimeStop;

        while (updateTime > 0 && !EventHandler.Waiting)  // updatetime can be 0 if there is no player package
        {   // if indeed 0 updatetime, then none of the below preupdate postupdate will be called.

            if (EventHandler.Active && !EventHandler.Waiting)
            {
                EventHandler.Run(false, true);
                ExecuteEventCallbacks(CallbackResumeUpdate);
                yield return null;
                // World time must not advance while any event is still queued/running - the loop's own
                // condition only guards against EventHandler.Waiting, so without this continue the
                // per-minute world tick below would run in the same pass as draining the event queue,
                // letting characters move/change jobs while events referencing their prior state are
                // still backlogged (e.g. a witnessed-join event firing minutes late against a character
                // who has since left and taken a different job).
                continue;
            }
            else if (EventHandler.Active && EventHandler.Waiting) break;
            else if (firstLoopCounter != 2)
            {
                scr_System_CampaignManager.current.NotifyEventEnd();
            }
            FlushCollectedLogs(true, oneLoop, true);

            halted = false;
            loopCount++;
            //var time = Clock ? Utility.ReinitStopWatch(stopWatch) : TimeSpan.Zero;
            //var time2 = time;
            //foreach (Manageable faction in organizations) faction.Manage();
            if (firstLoopCounter > 0) firstLoopCounter --;
            oneLoop = false;

            // if (firstPreUpdate) firstPreUpdate = false;
            //else
            PreUpdate();

            //if (Clock) Debug.Log("Observer_PreUpdateTime complete " + Utility.LogStopwatch(stopWatch, ref time2));

            cnManager.FreeUpdateOneStep(ref totalUpdateTime, ref updateTime);

            updateTime -= 1;

            scr_System_Time.current.UpdateTime(0, 0, timestop ? 0 : 1, 0, true);   // if timestop then the value dont really matter

            // during postupdatetime, all job will clear and re-update package, and all character will check cum.
            // separate this.
            playerJob = cnManager.Player.CurrentJob;

            Observer_PostUpdateTime_1?.Invoke();    // step where all EP makes message_before
            Observer_PostUpdateTime_2?.Invoke();    // step where all character check cum
            Observer_PostUpdateTime_3?.Invoke();    // step where all EP makes message_after, and when cleanup happens

            cnManager.UpdateAllRoom();  // parallel foreach

            cnManager.UpdateAllCharaJob();

            // Some results (e.g. a witnessed-job "try to join" launchEvent without startImmediate)
            // only queue a callback into eventCallbacks rather than starting the event outright - see
            // ResponseEntryVariant.EventInitializer.Execute(). Without draining that queue here, the
            // callback sits unprocessed while the loop moves on to the next simulated minute, and by
            // the time it eventually fires (next time an event happens to already be active, or once
            // the whole update finishes) the character(s) it was queued against may have moved on and
            // changed jobs. Flush it immediately so any event it starts is caught by the Active check
            // at the top of the next iteration, before another minute ticks.
            ExecuteEventCallbacks(CallbackResumeUpdate);

            cnManager.ClearExecutedAPs();
            //cnManager.ClearLogs(true);
            scr_System_Time.current.NotifyTimeResumeEnd();
            //skipCurrentRoundClimaxCheck = false;
            // if (scr_System_Time.current.TimeResume) scr_System_Time.current.timeStop = TimestopState.normal;
            _updateTime = scr_System_Time.current.getCurrentTime();

            //yield return wait;
            if (Time.realtimeSinceStartup - lastDisplayYield >= 0.05f)
            {
                yield return null;
                lastDisplayYield = Time.realtimeSinceStartup;
            }
            //yield return new WaitForSecondsRealtime(waitTime);
            //yield return 

            if (false && TempLongCOMFix && loopCount >= 240)
            {
                Debug.Log($"Temporarily break update loop after {loopCount}");
                break;
            }
        }

        loopCount = timestop ? 0 : loopCount;

        if (cnManager.ExistPlayerPackage(out updateTime, out totalUpdateTime2, false))
        {   // continuous update
            // ask break
            totalUpdateTime = totalUpdateTime2;
            if (scr_System_CentralControl.current.LogPrefs.DLog_Update) Debug.Log($"--- halted totalupdatetime {updateTime} {totalUpdateTime}");
            cnManager.ChangeCurrentViewMode(ViewMode.View_Logs, true);
            halted = EventHandler.Active;
        }
        else
        {
            cnManager.ChangeCurrentViewMode(ViewMode.View_Logs, EventHandler.Active);
        }

        // exiting loop. if player is involved in a job, here's when we should display it.
        //playerJob = cnManager.Player.CurrentJob;

        // begin job message
        FlushCollectedLogs(true, false);

        if (timeStop) totalUpdateTime = 0;

        Updating = false;

        if (EventHandler.Active)
        {
            EventHandler.Run(false, true);
        }
            

        
        ExecuteEventCallbacks(CallbackResumeUpdate);
        FlushCollectedLogs(true, oneLoop, true);
        //NotifyLogsSingleUpdate(CallbackResumeUpdate);
        scr_System_CampaignManager.current.NotifyEventEnd();
        var player = scr_System_CampaignManager.current.Player;
        var desc = new DescriptionCollector($"<color={Utility.HexCOLOR(scr_System_CentralControl.current.DisplaySetting.TextColor_disabled.Color)}>{ElapsedTime.Replace("$count$", loopCount.ToString())}</color>", VisibilityLevel.Roomwide);
        desc.tooltip = scr_System_Time.current.getCurrentTime().ToString();
        desc.rightAlign = true;
        desc.autoAnimate = true;
        desc.relevantActors.Add(player.RefID);

        cnManager.AddLog(desc, player, true);
    }

    public void DeferredUpdateCall(int intref, string text)
    {
        if (!Updating)
        {
            scr_System_CampaignManager.current.FreeUpdate(intref, text);
        }
    }

    public void ToggleCallbackUpdate()
    {
        this.halted = true;
        this.CallbackResumeUpdate = true;
    }

    public void AddEventCallback(Action e)
    {
        eventCallbacks.Add(e);
        //Debug.Log($"AddEventCallback count {eventCallbacks.Count}");
    }
    protected List<Action> eventCallbacks = new List<Action>();

    public void ResumeUpdate()
    {
        EventHandler.Run(false, true);
        ExecuteEventCallbacks(CallbackResumeUpdate);
    }
    protected void ExecuteEventCallbacks(bool autoResumeUpdate)
    {
        //Debug.Log($"ExecuteEventCallbacks count {eventCallbacks.Count}");
        bool log = scr_System_CentralControl.current.LogPrefs.DLog_Events;
        FlushCollectedLogs(true, true, false);
        var loopCount = 100;
        while(loopCount > 0 && eventCallbacks.Count > 0)
        {
            var vv = eventCallbacks.Count > 0 ? eventCallbacks[0] : null;
            if (vv != null)
            {
                vv.Invoke();
                eventCallbacks.Remove(vv);
            }
            loopCount--;
        }
        if (loopCount < 1) Debug.LogError("Eventcallback stack exceed 100, forced exit");

        if (log) Debug.Log($"invoking Observer_PostUpdateTime_EventEnd, stillupdating? {this.EventHandler.Active}");
        Observer_PostUpdateTime_EventEnd?.Invoke(this.EventHandler.Active);
        FlushCollectedLogs(true, true, false);
        var bo = cnManager.ExistPlayerPackage(out var a, out var b, true);
        if (autoResumeUpdate)
        {
            if (log) Debug.LogError($"execute event callbacks, autoresumeupdate {autoResumeUpdate}, {!EventHandler.Active} {halted} {bo}");
            if (!EventHandler.Active && halted && bo) StartUpdate(false);
        }
        this.CallbackResumeUpdate = false;
    }

    MessageCollect Message = new MessageCollect();
    public void NotifyJobDescriptions(MessageCollect m, bool clearOnMerge = true)
    {
       // Debug.Log("NotifyJobDescriptions");
        this.Message.Merge(m, clearOnMerge);
    }

    /*
    public void AppendKojoMessage(MessageCollect_KojoEntry m, bool visible, Room_Instance recording)
    {
        //Debug.Log("AppendKojoMessage");
        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.LogError($"AppendKojoMessage: {m.message}");
        if (!visible && recording == null) return;
        if (visible) this.Message.messages_kojo.Add(m);
        if (recording != null) recording.NotifyKojoCollect(m);
    }*/

    public void AppendKojoMessage(KojoCollector m, Room_Instance room)
    {
        //Debug.Log("AppendKojoMessage");
        var player = scr_System_CampaignManager.current.Player;
        bool visible = m.DirectlyRelated(player) && m.VisibleTo(player, room);
        bool record = room != null && room.HasRecording;

        if (record) room.NotifyKojoCollect(m);
        if (visible)
        {
            m.collect.rightAlign = m.rightAlign;
            this.Message.AddKojo(m);
           // FlushCollectedLogs_PreEvents();
           if (!Updating)
            {
                this.Message.FlushCollectLogs(player);
                Debug.Log($"AppendKojoMessage not updating, flushing");
            }
        }
    }
    public ExperienceLog GetExpLogs()
    {
        return this.Message.exp;
    }


    /// <summary>
    /// executeCallbacks == true will cause infinite loop if flushcollectedlogs is inside callbacks !!
    /// </summary>
    /// <param name="flushOut"></param>
    /// <param name="firstLoop"></param>
    /// <param name="executeCallbacks"></param>
    public void FlushCollectedLogs(bool flushOut, bool firstLoop, bool executeCallbacks = false)
    {
        if (flushOut) Message.FlushCollectLogs(scr_System_CampaignManager.current.Player);
        else Message.Clear();
        if (executeCallbacks) ExecuteEventCallbacks(true);
    }

    public void AppendMessageBefore(DescriptionCollector desc, Room_Instance room, bool allowFlush = false)
    {
        var player = scr_System_CampaignManager.current.Player;
        var visible = desc.VisibleTo(player, room);


            //Debug.Log($"AppendMessageBefore visible, [{desc.message}] [{desc.message_excludeRelated}]");
        this.Message.AddMessage_Before(desc, room);
        
        //else Debug.Log($"AppendMessageBefore not visible, [{desc.message}] [{desc.message_excludeRelated}]\n room? {(room == null ? "null" : $"{room.DisplayNameShort} {String.Join(" ", room.RoomCharaRefs)} {room.RoomChara.Contains(player)} {player.CurrentRoom == room} {scr_System_CampaignManager.current.CurrentRoom == room}")} direct? {desc.DirectlyRelated(player)} ");
        
        // message addmessage already has a addstuff inside
        //if (room != null && room.HasRecording) room.NotifyDescCollect(desc);

        if (allowFlush && visible && !Updating)
        {
            Debug.Log("Updatehandler AppendMessageAfter !Updating, flushCollectedLogs");
            FlushCollectedLogs(true, false);
        }
    }
    public void AppendMessageAfter(DescriptionCollector desc, Room_Instance room, bool allowFlush = false)
    {
        var player = scr_System_CampaignManager.current.Player;
        var visible = desc.VisibleTo(player, room);
        if (visible) this.Message.AddMessage_After(desc, room);
       // if (room != null && room.HasRecording) room.NotifyDescCollect(desc, MessageCollect_Type.after);

        if (allowFlush && visible && !Updating)
        {
            Debug.Log("Updatehandler AppendMessageAfter !Updating, flushCollectedLogs");
            FlushCollectedLogs(true, false);
        }
    }

    public void NotifyLogsSingleUpdate(bool skipAll = false)
    {
        Observer_LogsSingleStepUpdate?.Invoke(skipAll);
    }

    /*
    public void AddExperience(int charaRef, string expID, int count)
    {
        if (!scr_System_CampaignManager.current.ShowCharaLog(charaRef)) return;
        this.Message.exp.AddExperience(charaRef, expID, count);
    }

    public int PlayerQuery(Action<scr_Menu> action)
    {
        return 0;
    }*/
}
