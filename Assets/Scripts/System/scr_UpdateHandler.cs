using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

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

    public event Action Observer_PreUpdateTime;
    public event Action Observer_PostUpdateTime_1;
    public event Action Observer_PostUpdateTime_2;
    public event Action Observer_PostUpdateTime_3;
    public event Action<bool> Observer_PostUpdateTime_EventEnd;
    //public event Action Observer_PostUpdateTime_4;
    public event Action<bool> Observer_LogsSingleStepUpdate;
    public event Action<EventStatus, bool> Observer_EventStatus;

    public void StartUpdate(bool init, bool silent = false, bool updateUI = false)
    {
        //if (imageScript == null) Debug.LogError("UPDATEHANDLER NO IMAGE ATTACHED");

        if (init)
        {
            firstPreUpdate = true;
            Observer_PreUpdateTime?.Invoke();
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
            if (EventHandler.Active)
            {
                if (scr_System_CentralControl.current.LogPrefs.DLog_Update) Debug.LogError("Eventhandler active prior to StartCoroutine SingleUpdate");
            }
            else
            {

                if(scr_System_CentralControl.current.LogPrefs.DLog_Update) Debug.LogError($"UpdateHandler PlayerPackage Update duration {updateTime} {totalUpdateTime}");

                StartCoroutine(SingleUpdate());
            }
        }
        else if (!Updating && init)
        {
            if (EventHandler.Active)
            {
                if (scr_System_CentralControl.current.LogPrefs.DLog_Update) Debug.LogError("Eventhandler active prior to StartCoroutine SingleUpdate");
            }
            else
            {
                updateTime = 1;
                totalUpdateTime = 1;
                if (scr_System_CentralControl.current.LogPrefs.DLog_Update) Debug.LogError($"UpdateHandler ForceUpdate duration {updateTime}  {totalUpdateTime}");
                StartCoroutine(SingleUpdate());
            }

        }
        else if (updateUI) NotifyLogsSingleUpdate();
    }

    public bool DoDisplayCOM(ActionPackage p)
    {
        if (p == null) return false;

        // if player related, return true
        if (p.actorRefs != null && p.actorRefs.Contains(0)) return true;

        // log update is initiated by Job and there is already a player visibility check prior to this.
        //if (p.job != null && p.job.ParentRoom != null && p.job.ParentRoom.RefID != scr_System_CampaignManager.current.CurrentRoom.RefID) return false;

        if (!scr_System_CampaignManager.current.DebugMode && p.targetCOM != null && p.targetCOM.TimeScale * 2 < totalUpdateTime) return false;

        return true;
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

    protected List<string> checkResults = new List<string>();
    List<string> currentRoundClimax = new List<string>();
    public ExperienceLog exp = new ExperienceLog();

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

        WaitForSeconds wait = new WaitForSeconds(waitTime);

        while (updateTime > 0 && !EventHandler.Waiting)  // updatetime can be 0 if there is no player package
        {   // if indeed 0 updatetime, then none of the below preupdate postupdate will be called.

            if (EventHandler.Active && !EventHandler.Waiting)
            {
                ExecuteEventCallbacks(CallbackResumeUpdate);
                yield return null;
            }
            else if (EventHandler.Active && EventHandler.Waiting) break;
            else if (firstLoopCounter != 2)
            {
                scr_System_CampaignManager.current.NotifyEventEnd();
            }

            halted = false;
            loopCount++;
            //var time = Clock ? Utility.ReinitStopWatch(stopWatch) : TimeSpan.Zero;
            //var time2 = time;
            //foreach (Manageable faction in organizations) faction.Manage();
            if (firstLoopCounter > 0) firstLoopCounter --;
            FlushCollectedLogs(true, oneLoop);
            oneLoop = false;

           // if (firstPreUpdate) firstPreUpdate = false;
            //else
            Observer_PreUpdateTime?.Invoke();

            //if (Clock) Debug.Log("Observer_PreUpdateTime complete " + Utility.LogStopwatch(stopWatch, ref time2));

            cnManager.FreeUpdateOneStep(ref totalUpdateTime, ref updateTime);

            updateTime -= 1;

            scr_System_Time.current.UpdateTime(0, 0, timestop ? 0 : 1, 0, true);   // if timestop then the value dont really matter

            if (checkResults.Count > 0) cnManager.AddLog(-1, String.Join("\n", checkResults), false);
            checkResults.Clear();

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
            if (EventHandler.Active) EventHandler.Run(false, true);


            scr_System_Time.current.NotifyTimeResumeEnd();
           // if (scr_System_Time.current.TimeResume) scr_System_Time.current.timeStop = TimestopState.normal;

            yield return wait;
            //yield return new WaitForSecondsRealtime(waitTime);
            //yield return 
#if UNITY_EDITOR
            if (TempLongCOMFix && loopCount >= 180)
            {
                Debug.Log($"Temporarily break update loop after {loopCount}");
                break;
            }
#else
            if (loopCount >= 180)
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
        if (false && playerJob != null) cnManager.AddLog(-1, playerJob.MessagesBefore, halted);

        cnManager.AddLog(-1, String.Join("\n", message_begin), halted);
        cnManager.AddLog(-1, String.Join("\n", message_ongoing), halted);

        message_begin.Clear();
        message_ongoing.Clear();

        foreach(var kvp in kojoMsgDictionary)
        {
            cnManager.AddLog(kvp.Key, kvp.Value, halted);
        }

        kojoMsgDictionary.Clear();

        // all climax ? need to filter out who worth displaying
        cnManager.AddLog(-1, String.Join("\n", currentRoundClimax), halted);

        currentRoundClimax.Clear();
        // after job message
        if (false && playerJob != null) cnManager.AddLog(-1, playerJob.MessagesAfter, halted);
        cnManager.AddLog(-1, String.Join("\n", message_end), halted);

        message_end.Clear();

        // all exp inc message
        foreach(var i in scr_System_CampaignManager.current.ActiveJobsRefsInCurrentRoom)
        {
            var job = scr_System_CampaignManager.current.FindJobInstanceByID(i);
            //Debug.Log("Merging with ActiveJob "+job.RefID+" " + job.DisplayName);
            this.exp.MergeWith(job.exp);
        }
        cnManager.AddLog(-1, exp.PrintContent(), halted);
        exp.Clear();

        if (timeStop) totalUpdateTime = 0;


        cnManager.AddLog(-1000, $"<align=\"right\"><color={Utility.HexCOLOR(scr_System_CentralControl.current.DisplaySetting.TextColor_disabled.Color)}>{ElapsedTime.Replace("$count$", loopCount.ToString())}</color></align>", false, true, scr_System_Time.current.getCurrentTime().ToString());

        /*
        List<string> names = new List<string>();
        foreach (var charaRef in cnManager.CharaInCurrentRoom)
        {
            if (charaRef == 0 || cnManager.PlayerPartyMembers.Contains(charaRef)) continue;
            Character_Trainable c = cnManager.FindInstanceByID(charaRef);
            if (c == null) continue;
            names.Add("<align=\"right\">"+LocalizeDictionary.QueryThenParse("chara_currently_doing").Replace("$chara$",c.FirstName).Replace("$currentjob$", c.GetJobDescription()) +"</align>");
            //cnManager.AddLog(charaRef, c.FirstName+ " is in room" + room.DisplayName+  ", currently " + c.GetJobDescription(), true);
        }
        if (names.Count > 0) cnManager.AddLog(-1, String.Join("\n", names) , false, true);
        //yield return null;
        */

        //if (playerJob == null || playerJob is Job_Sex_Group) { }
        //else
        //{   // release player from previous job registry to avoid lingering COM display
        //    scr_System_CampaignManager.current.Player.ChangeCurrentJob(null);
        // }
        Updating = false;

        if (EventHandler.Active)
        {

#if UNITY_EDITOR
            if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.LogError($"EVENTHANDLER ACTIVE, running... is Updating? {Updating}");
#endif
            EventHandler.Run(false, true);
        }

        ExecuteEventCallbacks(CallbackResumeUpdate);
        NotifyLogsSingleUpdate(CallbackResumeUpdate);
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
    }
    protected List<Action> eventCallbacks = new List<Action>();
    protected void ExecuteEventCallbacks(bool autoResumeUpdate)
    {
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
        scr_System_CampaignManager.current.NotifyEventEnd();
        FlushCollectedLogs(true, true, false);
        var bo = cnManager.ExistPlayerPackage(out var a, out var b, true);
        if (autoResumeUpdate)
        {
            if (log) Debug.LogError($"execute event callbacks, autoresumeupdate {autoResumeUpdate}, {!EventHandler.Active} {halted} {bo}");
            if (!EventHandler.Active && halted && bo) StartUpdate(false);
        }
        this.CallbackResumeUpdate = false;
    }



    List<string> message_begin = new List<string>(), message_end = new List<string>();
    List<string> message_ongoing = new List<string>();
    public void NotifyJobDescriptions(List<string> begin, List<string> ongoing, List<string> end, Dictionary<int, string> kojo)
    {
        // accumate result, on flush addlog to CNManager
        if (begin != null && begin.Count > 0) message_begin.AddRange(begin);
        if (ongoing != null && ongoing.Count > 0) message_ongoing.AddRange(ongoing);
        if (end != null && end.Count > 0) message_end.AddRange(end);
        if (kojo != null)
        {
            foreach(var kvp in kojo)
            {
                if (!this.kojoMsgDictionary.ContainsKey(kvp.Key)) this.kojoMsgDictionary[kvp.Key] = kvp.Value;
                else this.kojoMsgDictionary[kvp.Key] += "\n" + kvp.Value;
            }
        }
    }

    public Dictionary<int, string> kojoMsgDictionary = new Dictionary<int, string>();
    public void AppendKojoMessage(int charaRefID, string s)
    {
        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.LogError($"AppendKojoMessage: {s}");
        if (!kojoMsgDictionary.ContainsKey(charaRefID)) kojoMsgDictionary.Add(charaRefID, s);
        else kojoMsgDictionary[charaRefID] += "\n" + s;
    }

    /// <summary>
    /// executeCallbacks == true will cause infinite loop if flushcollectedlogs is inside callbacks !!
    /// </summary>
    /// <param name="flushOut"></param>
    /// <param name="firstLoop"></param>
    /// <param name="executeCallbacks"></param>
    public void FlushCollectedLogs(bool flushOut, bool firstLoop, bool executeCallbacks = false)
    {
        if (flushOut)
        {
            if (checkResults.Count > 0) cnManager.AddLog(-1, String.Join("\n", checkResults), false);
            cnManager.AddLog(-1, exp.PrintContent(), true);
            if (message_begin.Count > 0) cnManager.AddLog(-1, String.Join("\n", message_begin), true);
            if (firstLoop && message_ongoing.Count > 0) cnManager.AddLog(-1, String.Join("\n", message_ongoing), true);

            foreach(var kvp in kojoMsgDictionary)
            {
                bool rA = kvp.Key == 0 || kvp.Key == scr_System_CampaignManager.current.CurrentTargetRef;
                cnManager.AddLog(kvp.Key, kvp.Value, true, !rA);
            }
            if (currentRoundClimax.Count > 0) cnManager.AddLog(-1, String.Join("\n", currentRoundClimax), true);
            if (message_end.Count > 0) cnManager.AddLog(-1, String.Join("\n", message_end), true);
        }
        exp.Clear();
        checkResults.Clear();
        currentRoundClimax.Clear();
        message_begin.Clear();
        message_ongoing.Clear(); 
        kojoMsgDictionary.Clear();
        message_end.Clear();

        if (executeCallbacks) ExecuteEventCallbacks(true);
    }

    public void NotifyCheckResult(string resultString)
    {
        if (resultString.Length < 1) return;
        if (!Updating) Debug.LogError($"Updatehandler: receiver checkresult [{resultString}] but not currently updating");
        checkResults.Add(resultString);
        //Observer_LogsSingleStepUpdate?.Invoke(false);
    }

    public void NotifyClimax(int targetRefID, string s, ExperienceLog exp)
    {
        if (!scr_System_CampaignManager.current.ShowCharaLog(targetRefID)) return;
        currentRoundClimax.Add(s);
        this.exp.MergeWith(exp);
    }

    public void NotifyLogsSingleUpdate(bool skipAll = false)
    {
        Observer_LogsSingleStepUpdate?.Invoke(skipAll);
    }


    public int PlayerQuery(Action<scr_Menu> action)
    {
        return 0;
    }
}
