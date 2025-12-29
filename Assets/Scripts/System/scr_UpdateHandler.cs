using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using static UnityEngine.UI.GridLayoutGroup;

public class scr_UpdateHandler : MonoBehaviour
{
    public EventManager EventHandler = new EventManager();

    public bool Lock
    {
        get
        {
            return Updating || EventHandler.Active || Animating;
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

    public bool TempLongCOMFix = true;

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

    protected void PreUpdate()
    {
        var time = scr_System_Time.current.getCurrentTime();
        Observer_PreUpdateTime?.Invoke();
        if (time.Minute == 0) Observer_PreUpdateTime_Hourly?.Invoke();
    }

    public void StartUpdate(bool init, bool silent = false, bool updateUI = false)
    {
        //if (imageScript == null) Debug.LogError("UPDATEHANDLER NO IMAGE ATTACHED");

        if (init)
        {
            firstPreUpdate = true;
            PreUpdate();
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
            
            if (EventHandler.Active && scr_System_CentralControl.current.LogPrefs.DLog_Update) Debug.LogError("Eventhandler active prior to StartCoroutine SingleUpdate");
            
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

    float waitTime = 0.001f;
    


    string cache_elapsedTime = "";
    string ElapsedTime { get { if (cache_elapsedTime == "") cache_elapsedTime = LocalizeDictionary.QueryThenParse("ui_update_elapsedTime");
        return cache_elapsedTime;} }
    private IEnumerator SingleUpdate()
    {
        //Debug.Log("Singleupdate : start");

        Updating = true;
        int loopCount = 0;
        firstLoopCounter = 2;
        FlushCollectedLogs(false, oneLoop);
        // update per room

        Job playerJob = null;
        cnManager.ChangeCurrentViewMode(ViewMode.View_Logs, true);
        //Debug.Log($"Singleupdate : eventhandler end, updatetime? {updateTime}");
        //NotifyLogsSingleUpdate();
        //var copy = updateTime;
        bool timestop = scr_System_Time.current.TimeStop;

        //WaitForSeconds wait = new WaitForSeconds(waitTime);

        while (updateTime > 0 && !EventHandler.Waiting)  // updatetime can be 0 if there is no player package
        {   // if indeed 0 updatetime, then none of the below preupdate postupdate will be called.

            if (EventHandler.Active && !EventHandler.Waiting)
            {
                EventHandler.Run(false, true);
                ExecuteEventCallbacks(CallbackResumeUpdate);
                yield return null;
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

            cnManager.ClearExecutedAPs();
            //cnManager.ClearLogs(true);
            scr_System_Time.current.NotifyTimeResumeEnd();
           // if (scr_System_Time.current.TimeResume) scr_System_Time.current.timeStop = TimestopState.normal;

            //yield return wait;
            //yield return new WaitForSecondsRealtime(waitTime);
            //yield return 
#if UNITY_EDITOR
            if (TempLongCOMFix && loopCount >= 240)
            {
                Debug.Log($"Temporarily break update loop after {loopCount}");
                break;
            }
#else
            if (loopCount >= 240)
            {
                Debug.Log($"Temporarily break update loop after {loopCount}");
                break;
            }
#endif
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
        else
        {
            cnManager.AddLog(-1000, $"<align=\"right\"><color={Utility.HexCOLOR(scr_System_CentralControl.current.DisplaySetting.TextColor_disabled.Color)}>{ElapsedTime.Replace("$count$", loopCount.ToString())}</color></align>", true, true, scr_System_Time.current.getCurrentTime().ToString());

        }
        ExecuteEventCallbacks(CallbackResumeUpdate);
        FlushCollectedLogs(true, oneLoop, true);
        //NotifyLogsSingleUpdate(CallbackResumeUpdate);
        scr_System_CampaignManager.current.NotifyEventEnd();

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
            eventCallbacks[0].Invoke();
            eventCallbacks.RemoveAt(0);
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

    public MessageCollect Message = new MessageCollect();
    public void NotifyJobDescriptions(MessageCollect m, bool shorten)
    {
       // Debug.Log("NotifyJobDescriptions");
        this.Message.Merge(m, shorten);
    }
    public void NotifyJobDescriptions_PreEvents(MessageCollect m, bool shorten)
    {
        // Debug.Log("NotifyJobDescriptions");
        if (m.messages_before.Count > 0)
        {
            Message.messages_before.AddRange(m.messages_before);
            m.messages_before.Clear();
        }
        if (m.messages_checks.Count > 0)
        {
            Message.messages_checks.AddRange(m.messages_checks);
            m.messages_checks.Clear();
        }
        if (m.messages_kojo.Count > 0)
        {
            Message.messages_kojo.AddRange(m.messages_kojo);
            m.messages_kojo.Clear();
        }
    }


    public void AppendMessageBefore(string s, bool rightalign)
    {
        if (s.Length < 1) return;
        this.Message.messages_before.Add($"<align=\"right\">{s}</align>");
    }
    public void AppendKojoMessage(MessageCollect_KojoEntry m)
    {
        //Debug.Log("AppendKojoMessage");
        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.LogError($"AppendKojoMessage: {m.message}");
        this.Message.messages_kojo.Add(m);
    }
    public void AppendKojoMessage_NonVisible(MessageCollect_KojoEntry m)
    {

    }

    public void FlushCollectedLogs_PreEvents()
    {

        if (Message.messages_checks.Count > 0) cnManager.AddLog(-1, String.Join("\n", Message.messages_checks), false);
        if (Message.messages_before.Count > 0) cnManager.AddLog(-1, String.Join("\n", Message.messages_before), false);

        foreach (var kvp in Message.messages_kojo) cnManager.AddLog(kvp);

        Message.messages_before.Clear();
        Message.messages_checks.Clear();
        Message.messages_kojo.Clear();
    }
    /// <summary>
    /// executeCallbacks == true will cause infinite loop if flushcollectedlogs is inside callbacks !!
    /// </summary>
    /// <param name="flushOut"></param>
    /// <param name="firstLoop"></param>
    /// <param name="executeCallbacks"></param>
    public void FlushCollectedLogs(bool flushOut, bool firstLoop, bool executeCallbacks = false)
    {
        if (flushOut) Message.FlushCollectLogs();
        else Message.Clear();
        if (executeCallbacks) ExecuteEventCallbacks(true);
    }

    public void AppendEndMessage(string s)
    {
        Message.messages_after.Add(s);
        if (!Updating) FlushCollectedLogs(true, false);
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
