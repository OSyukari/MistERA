using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using System;

public class scr_panel_logs : scr_Menu, IPointerClickHandler
{

    public override void Notify(int optionID)
    {
        //Debug.Log("Parent Notified ! [" + optionID + "]");
        ButtonValidator validator = validatorsByID[optionID];
        I_ButtonClickable button = validator as I_ButtonClickable;
        if (button != null)
        {
            button.OnClickButton();
        }
        else
        {
            switch (optionID)
            {
                default: break;
            }
        }
        ValidateAll();
    }

    public bool lockView = false;

    private void OnViewModeChange(ViewMode vm, bool lockView)
    {
        if (vm == ViewMode.View_Logs)
        {
            if (!this.gameObject.activeInHierarchy) this.gameObject.SetActive(true);
            this.lockView = lockView;
        }
    }

    bool firstLine = true;

    protected override void OnEnable()
    {
        base.OnEnable();
        LogsList.gameObject.SetActive(true);
        firstLine = true;
        if (firstLine && canAnimate) AnimateOneStep();
        //if (canAnimate) AnimateOneStep();
        // foreach log in logs
        // if not in tracked list, build into list
        /*        foreach (var log in scr_System_CampaignManager.current.Logs)
                {
                    if (!trackedLogs.Contains(log)) MakeLogEntry(log);
                }
        */
        ValidateAll();
    }

    /// <summary>
    /// when logs updated, log is always displayed.
    /// </summary>
    /// <param name="msg"></param>
    /// <param name="animate"></param>
    private void OnLogAdd(MessageLog msg, bool animate)
    {
        todo.Add(msg);
        UpdateAnimatingStatus();
        //Debug.Log($"onLogsAdd firstline? {firstLine} or animate? {animate} canAnimate? {canAnimate}");
        if (firstLine || animate) SingleUpdate(false);
    }

    private void SingleUpdate(bool skipAll)
    {
        if (canAnimate)
        {
            if (skipAll || Input.GetMouseButton(1)) AnimateAll();
            else AnimateOneStep();
        }
        else
        {
            scr_System_CampaignManager.current.ChangeCurrentViewMode(ViewMode.View_Room);
        }
    }

    protected void UpdateAnimatingStatus()
    {
        scr_UpdateHandler.current.Animating = canAnimate;
        //Debug.Log($"update animating status {scr_UpdateHandler.current.Animating} lock {scr_UpdateHandler.current.Lock} updating {scr_UpdateHandler.current.Updating} event {scr_UpdateHandler.current.EventHandler.Active}");
    }

    List<MessageLog> todo;

    private void ClearLogs()
    {
        // destroy all
        while (LogsList.transform.childCount > 0)
        {
            DestroyImmediate(LogsList.transform.GetChild(0).gameObject);
        }
        //trackedLogs.Clear();
        todo.Clear();
        scr_System_CampaignManager.current.Logs.Clear();
    }

    //List<List<string>> msgLog;
    //List<string> msg;

    private RectTransform currentMsgLog, currentMsg;
    private void AnimateOneStep()
    {
        // Debug.Log($"Animateonestep, firstline {firstLine}");
        animationLock = true;
        firstLine = false;
        while (LogsList.transform.childCount > scr_System_CentralControl.current.pref.MaxLogCount)
        {
            DestroyImmediate(LogsList.transform.GetChild(0).gameObject);
        }

        //Debug.Log("loglist anchored position is " + LogsList.anchoredPosition.x + "|" + LogsList.anchoredPosition.y);
        LogsList.anchoredPosition = new Vector2(0, 0);

        var current = todo.Count > 0 ? todo[0] : null;
        if(current == null)
        {
            scr_System_CampaignManager.current.Log_TryClearChar(true);
            animationLock = false;
            return;
        }
        else if (!current.displayed)
        {
            if (current is Message_Text)
            {
                RectTransform msgbox;
                if (current.PortraitRef == -1000) msgbox = Instantiate(prefab_SeparationEntry);
                else msgbox = Instantiate(prefab_LogEntry);

                msgbox.SetParent(LogsList, false);
                (current as Message_Text).Draw(msgbox.GetComponent<scr_MessageLogBox>(), this.prefab_LogLine);
            }
            else if (current is Message_Question)
            {
                var question = Instantiate(prefab_question);
                question.transform.SetParent(LogsList, false);
                (current as Message_Question).Draw(this.m_Canvas, question, this);
            }
        }
        else if (current.canAnimate())
        {
            current.Animate();
        }

        if (current.displayed && !current.canAnimate())
        {
            todo.RemoveAt(0);
            UpdateAnimatingStatus();
        }


        animationLock = false;
    }

    bool animationLock = false;
    private void AnimateAll()
    {
        animationLock = true;
        while (canAnimate)
        {
            AnimateOneStep();
      //      yield return new WaitForSecondsRealtime(0.001f);
        }
        animationLock = false;
        /*
        if (!scr_UpdateHandler.current.Lock && this.gameObject.activeInHierarchy)
        {
            yield return new WaitForSecondsRealtime(0.5f);
            scr_System_CampaignManager.current.ChangeCurrentViewMode(ViewMode.View_Room);
        }*/
    }

    public bool canAnimate { get { return todo.Count > 0; } }

    public RectTransform LogsList;
    public RectTransform prefab_LogEntry, prefab_SeparationEntry;
    public TMP_Text prefab_LogLine;
    public scr_menu_question prefab_question;


    private void OnDisable()
    {
        LogsList.gameObject.SetActive(false);
       // AnimateAll();
    }

    private void OnLogsClear(bool flushOnly)
    {
        if (flushOnly) AnimateAll();
        else this.ClearLogs();
    }

    protected override void Awake()
    {
        base.Awake();

        scr_System_CampaignManager.current.Observer_CurrentViewMode += OnViewModeChange;
        scr_System_CampaignManager.current.Observer_LogsClear += OnLogsClear;

        button_alwaysValid = new ButtonValidator_AlwaysTrue(this);

        scr_System_CampaignManager.current.Observer_MessageLogs += OnLogAdd;
        scr_UpdateHandler.current.Observer_LogsSingleStepUpdate += SingleUpdate;
        scr_UpdateHandler.current.Observer_EventStatus += OnEvent;

        todo = new List<MessageLog>();
       // msg = new List<string>();
       // msgLog = new List<List<string>>();

        this.gameObject.SetActive(false);
    }

    protected void OnEvent(EventStatus status, bool forceLogging)
    {
#if UNITY_EDITOR
        if (scr_System_CentralControl.current.LogPrefs.DLog_Events) Debug.Log($"OnEvent {status}, waiting? {(status == EventStatus.waiting)} firstline {firstLine}");
#endif
        if (!this.gameObject.activeInHierarchy && status != EventStatus.idle) this.gameObject.SetActive(true);
        if (forceLogging) this.firstLine = true;
    }

    public Action<PointerEventData> Observer_OnClick;

    public override void Initialize()
    {
        base.Initialize();

        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {

            switch (button.optionID)
            {
                case -1: break;
                default:
                    button.Initialize(this, button_alwaysValid);
                    break;
            }
            if (button.optionID != -1)
            {
                buttonsByID.Add(button.optionID, button);
                validatorsByID.Add(button.optionID, button.Validator);
            }

        }


        ValidateAll();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        int clickID = eventData.pointerId;

        if (scr_UpdateHandler.current.Updating || scr_UpdateHandler.current.EventHandler.Active || canAnimate)
        {
            Debug.Log($"OnPointerClick updating[{scr_UpdateHandler.current.Updating}] evActive[{scr_UpdateHandler.current.EventHandler.Active}] canAnimate[{canAnimate}]");
            Observer_OnClick?.Invoke(eventData);
            if (canAnimate && !animationLock)
            {
                if (eventData.button == PointerEventData.InputButton.Right || Input.GetMouseButton(1)) AnimateAll();
                else AnimateOneStep();
            }
        }
        else
        {
            UpdateAnimatingStatus();
            if (LogsList.anchoredPosition != new Vector2(0, 0)) LogsList.anchoredPosition = new Vector2(0, 0);
            else scr_System_CampaignManager.current.ChangeCurrentViewMode(ViewMode.View_Room);
        }
    }
}

public struct MessageBlock
{
    public int portraitRef;
    public List<MessageLine> lines;
}
public struct MessageLine
{
    public bool rightAlign;
    public string messages;
}