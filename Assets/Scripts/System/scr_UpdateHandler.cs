using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

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

            if (!_animating && cnManager.ExistPlayerPackage(out int aaa, out int bbb))
            {
                click(null);
            }
        }
    }
    private void click(PointerEventData eventData)
    {

        if (Updating || imageScript == null) return;

        // differentiate left and right click and animate all ?
        // dont. if its a single long command its as fast as skip anyway.
        // if multiple commands, it usually involves moving, and we want to let player possible break movement
        bool single = true;
        if (!Updating && (cnManager.ExistPlayerPackage(out updateTime, out totalUpdateTime)) && loopCounter < 20)
        {
            StartCoroutine(SingleUpdate());

            single = false;
        }
        else
        {
            NotifyLogsSingleUpdate();
        }
        /*
        if ((cnManager.ExistPlayerPackage(out updateTime, out totalUpdateTime) || oneLoop) && loopCounter < 20)
        {   // any click start single one
            StartCoroutine(SingleUpdate());
        }
        else
        {   // no longer update, closing
            // if right click, or if right was held
            if (eventData.button == PointerEventData.InputButton.Right)
            {
                imageScript.Deactivate();
                NotifyLogsSingleUpdate(true);
            }
            else
            {
                imageScript.Deactivate();
                NotifyLogsSingleUpdate();
            }
        }*/

    }
    public static scr_UpdateHandler current;
    public scr_System_CampaignManager cnManager;
    scr_AttachToUpdateHandler imageScript = null;

    public void InvokeEventStatus(EventStatus status, bool forcelogging)
    {
        this.Observer_EventStatus?.Invoke(status, forcelogging);
    }

    public void LoadSaveFile(SaveFileHolder saveHolder, bool unloadCanvas = true){
        if (!saveHolder.isValid) Debug.LogError("LoadSave error did not re-inject file path.");
        else LoadSaveFile(saveHolder.InnerFile, unloadCanvas);
    }
    public void LoadSaveFile(SaveFile save, bool unloadCanvas = true)
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

        cnManager = scr_System_CampaignManager.current;
        if (current == null)
        {
            current = this;
        }
        else
        {
            Destroy(gameObject);
        }
        DontDestroyOnLoad(gameObject);

        if (scr_System_CampaignManager.current != null) scr_System_CampaignManager.current.NotifyUpdateHandlerExist();
    }

    int updateTime, totalUpdateTime, totalUpdateTime2;
    bool firstPreUpdate;
    bool timeStop;
    int loopCounter;
    bool oneLoop;

    public event Action Observer_PreUpdateTime;
    public event Action Observer_PostUpdateTime_1;
    public event Action Observer_PostUpdateTime_2;
    public event Action Observer_PostUpdateTime_3; 
    //public event Action Observer_PostUpdateTime_4;
    public event Action<bool> Observer_LogsSingleStepUpdate;
    public event Action<EventStatus, bool> Observer_EventStatus;

    public void StartUpdate(bool silent = false)
    {
        //if (imageScript == null) Debug.LogError("UPDATEHANDLER NO IMAGE ATTACHED");

        firstPreUpdate = true;
        if (firstPreUpdate) Observer_PreUpdateTime?.Invoke();
        timeStop = scr_System_Time.current.TimeStop;
        loopCounter = 0;
        oneLoop = true;

        //FlushCollectedLogs(false, true);
        //if (imageScript != null)
        //{
            // Updatetime is used to register loop count in minutes
            // totalUpdateTime is used when loop finishes and print value.
            // tldr, totalUpdateTime is the update duration count, and we should filter command logging based on this value.
        if (!Updating && (cnManager.ExistPlayerPackage(out updateTime, out totalUpdateTime)) && loopCounter < 20)
        { 
            StartCoroutine(SingleUpdate()); 
        }
        //}
        //else
        //{
        //    while ((cnManager.ExistPlayerPackage(out updateTime, out totalUpdateTime) || oneLoop) && loopCounter < 20) StartCoroutine(SingleUpdate());
        //}
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

    protected int firstLoopCounter = 2;
    public bool isFirstUpdate { get { return firstLoopCounter > 0; } }
    private IEnumerator SingleUpdate()
    {
        //Debug.Log("Singleupdate : start");
        var Clock = scr_System_CentralControl.current.LogPrefs.Debug_Logging_UpdateTimeCost ;
        Updating = true;
        firstLoopCounter = 2;
        FlushCollectedLogs(false, oneLoop);
        // update per room

        Job playerJob = null;
        cnManager.ChangeCurrentViewMode(ViewMode.View_Logs, true);

        if (EventHandler.Active)
        {
            Debug.Log($"Singleupdate : eventhandler active, waiting at pre updatetime {updateTime}");
            Updating = false;
            yield return new WaitUntil(() => EventHandler.Status == EventStatus.idle);
        }
        Updating = true;
        //Debug.Log($"Singleupdate : eventhandler end, updatetime? {updateTime}");
        //NotifyLogsSingleUpdate();
        while (updateTime > 0)  // updatetime can be 0 if there is no player package
        {   // if indeed 0 updatetime, then none of the below preupdate postupdate will be called.

            var time = Clock ? Utility.ReinitStopWatch(stopWatch) : TimeSpan.Zero;
            var time2 = time;
            //foreach (Manageable faction in organizations) faction.Manage();
            if (firstLoopCounter > 0) firstLoopCounter --;
            FlushCollectedLogs(true, oneLoop);
            oneLoop = false;

            if (firstPreUpdate) firstPreUpdate = false;
            else Observer_PreUpdateTime?.Invoke();
            if (Clock) Debug.Log("Observer_PreUpdateTime complete " + Utility.LogStopwatch(stopWatch, ref time2));

            cnManager.FreeUpdateOneStep(ref totalUpdateTime, ref updateTime);
            if (Clock) Debug.Log("FreeUpdateOneStep complete " + Utility.LogStopwatch(stopWatch, ref time2));

            updateTime -= 1;
            scr_System_Time.current.UpdateTime(0, 0, 1, 0, true);   // if timestop then the value dont really matter
            if(Clock) Debug.Log("UpdateTime complete " + Utility.LogStopwatch(stopWatch, ref time2));

            if (checkResults.Count > 0) cnManager.AddLog(-1, String.Join("\n", checkResults), false);

            // during postupdatetime, all job will clear and re-update package, and all character will check cum.
            // separate this.
            playerJob = cnManager.Player.CurrentJob;

            Observer_PostUpdateTime_1?.Invoke();    // step where all EP makes message_before
            // when invoking postUpdate1, totalUpdateTime already exists, so we can allow job instances to ask and use it as filter
            // even if timestop, totalUpdateTime is the actual minute logic passing, and the filter should still apply
            if(Clock)  Debug.Log("Observer_PostUpdateTime_1 complete " + Utility.LogStopwatch(stopWatch, ref time2));

            Observer_PostUpdateTime_2?.Invoke();    // step where all character check cum
            if (Clock) Debug.Log("Observer_PostUpdateTime_2 complete " + Utility.LogStopwatch(stopWatch, ref time2));

            Observer_PostUpdateTime_3?.Invoke();    // step where all EP makes message_after, and when cleanup happens
            // also, job internal package renewal
            if (Clock) Debug.Log("Observer_PostUpdateTime_3 complete " + Utility.LogStopwatch(stopWatch, ref time2));

            //Observer_PostUpdateTime_4?.Invoke();    // sex com panel refresh values
            cnManager.UpdateAllRoom();  // parallel foreach
            if (Clock) Debug.Log("UpdateAllRoom complete " + Utility.LogStopwatch(stopWatch, ref time2));

            cnManager.UpdateAllCharaJob();
            if (Clock) Debug.Log("UpdateAllCharaJob complete " + Utility.LogStopwatch(stopWatch, ref time2));

            cnManager.ClearExecutedAPs();
            //cnManager.ClearLogs(true);

            if (Clock) Debug.Log("SingleUpdate 1 loop End, total time " + Utility.LogStopwatch(stopWatch, ref time));
            stopWatch.Stop();

            if (EventHandler.Active)
            {
                Updating = false;
                Debug.Log($"Singleupdate : eventhandler active, waiting at updatetime{updateTime}");
                yield return new WaitUntil(() => EventHandler.Status == EventStatus.idle);
            }
            else yield return null;
            //yield return new WaitForSecondsRealtime(0.001f);
        }
        loopCounter++;

        

        if (cnManager.ExistPlayerPackage(out updateTime, out totalUpdateTime2, false) && loopCounter < 20)
        {   // continuous update
            // ask break

            totalUpdateTime = totalUpdateTime2;
            Debug.Log($"totalupdatetime {updateTime} {totalUpdateTime}");
            cnManager.ChangeCurrentViewMode(ViewMode.View_Logs, true);

        }
        else
        {
            cnManager.ChangeCurrentViewMode(ViewMode.View_Logs, false);
        }
     
        
    
        // exiting loop. if player is involved in a job, here's when we should display it.
        //playerJob = cnManager.Player.CurrentJob;

        // begin job message
        if (false && playerJob != null) cnManager.AddLog(-1, playerJob.MessagesBefore);
        cnManager.AddLog(-1, String.Join("\n", message_begin));
        cnManager.AddLog(-1, String.Join("\n", message_ongoing));
        
        foreach(var kvp in kojoMsgDictionary)
        {
            cnManager.AddLog(kvp.Key, kvp.Value);
        }

        // all climax ? need to filter out who worth displaying
        cnManager.AddLog(-1, String.Join("\n", currentRoundClimax));

        // after job message
        if (false && playerJob != null) cnManager.AddLog(-1, playerJob.MessagesAfter);
        cnManager.AddLog(-1, String.Join("\n", message_end));


        // all exp inc message
        foreach(var i in scr_System_CampaignManager.current.ActiveJobsRefsInCurrentRoom)
        {
            var job = scr_System_CampaignManager.current.FindJobInstanceByID(i);
            //Debug.Log("Merging with ActiveJob "+job.RefID+" " + job.DisplayName);
            this.exp.MergeWith(job.exp);
        }
        cnManager.AddLog(-1, exp.PrintContent());

        if (timeStop) totalUpdateTime = 0;


        cnManager.AddLog(-1000, "<align=\"right\">" + "(" + totalUpdateTime + " Minutes) " + scr_System_Time.current.getCurrentTime().ToString() + "</align>", false, false);

        List<string> names = new List<string>();
        foreach (var charaRef in cnManager.CharaInCurrentRoom)
        {
            if (charaRef == 0 || cnManager.PlayerPartyMembers.Contains(charaRef)) continue;
            Character_Trainable c = cnManager.FindInstanceByID(charaRef);
            if (c == null) continue;
            names.Add("<align=\"right\">"+scr_System_Serializer.current.Dictionary.Query("chara_currently_doing").Replace("$chara$",c.FirstName).Replace("$currentjob$", c.GetJobDescription()) +"</align>");
            //cnManager.AddLog(charaRef, c.FirstName+ " is in room" + room.DisplayName+  ", currently " + c.GetJobDescription(), true);
        }
        if(names.Count > 0) cnManager.AddLog(-1, String.Join("\n", names) , false, true);
        //yield return null;
        

        //NotifyLogsSingleUpdate();
        //Debug.LogError("CN MANAGER STILL HAVE PLAYER PACKAGE? " + cnManager.ExistPlayerPackage(out updateTime, out totalUpdateTime) + " LOOPCOUNTER " + loopCounter);

        if (playerJob == null || playerJob is Job_Sex_Group) { }
        else
        {   // release player from previous job registry to avoid lingering COM display
            scr_System_CampaignManager.current.Player.ChangeCurrentJob(null);
        }
        Updating = false;
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
        if (!kojoMsgDictionary.ContainsKey(charaRefID)) kojoMsgDictionary.Add(charaRefID, s);
        else kojoMsgDictionary[charaRefID] += "\n" + s;
    }
    private void FlushCollectedLogs(bool flushOut, bool firstLoop)
    {
        checkResults.Clear();
        exp.Clear();

        if (flushOut && message_begin.Count > 0) cnManager.AddLog(-1, String.Join("\n", message_begin), true);
        if (flushOut&& firstLoop && message_ongoing.Count > 0) cnManager.AddLog(-1, String.Join("\n", message_ongoing), true);
        if (flushOut)
        {
            foreach (var kvp in kojoMsgDictionary)
            {
                bool rA = kvp.Key == 0 || kvp.Key == scr_System_CampaignManager.current.CurrentTargetRef;
                cnManager.AddLog(kvp.Key, kvp.Value, true, !rA);
            }
        }
        if (flushOut && currentRoundClimax.Count > 0) cnManager.AddLog(-1, String.Join("\n", currentRoundClimax), true);
        if (flushOut && message_end.Count > 0) cnManager.AddLog(-1, String.Join("\n", message_end), true);

        currentRoundClimax.Clear();
        message_begin.Clear();
        message_ongoing.Clear(); 
        kojoMsgDictionary.Clear();
        message_end.Clear();
    }

    public void NotifyCheckResult(string resultString)
    {
        if (resultString.Length < 1) return;
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
