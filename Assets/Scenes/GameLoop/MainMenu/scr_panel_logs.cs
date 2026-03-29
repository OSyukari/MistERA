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
        SingleUpdate(false);
    }

    /// <summary>
    /// when logs updated, log is always displayed.
    /// </summary>
    /// <param name="msg"></param>
    /// <param name="animate"></param>
    private void OnLogAdd(MessageLog msg, bool animate)
    {
        var immediate = animate && todo.Count < 1;
        todo.Add(msg);
        UpdateAnimatingStatus();
        //Debug.Log($"onLogsAdd firstline? {firstLine} or animate? {animate} canAnimate? {canAnimate}");
        if (scr_System_CentralControl.current.LogPrefs.DLog_LogsMenu) Debug.Log($"OnLogsadd, waiting? {waiting} displayPortrait? {msg.DisplaPortrait} waitForPortrait? {msg.WaitForPortrait} portraitRef {(msg.PortraitRef == null ? "null" : msg.PortraitRef.Owner.CallName)} multiple? {msg.multipleChara.Count} animate? {animate} count? {todo.Count} firstline? {firstLine} immediate? {immediate}");
        if (firstLine) SingleUpdate(false);
        else if (waiting && msg.WaitForPortrait) return;
        else if (immediate) SingleUpdate(false);
    }

    private void SingleUpdate(bool skipAll)
    {
        if (canAnimate && !animationLock)
        {
            if (skipAll || Input.GetMouseButton(1)) AnimateAll();
            else AnimateOneStep();
        }
    }

    protected void UpdateAnimatingStatus()
    {
        scr_UpdateHandler.current.Animating = canAnimate;
        //Debug.Log($"update animating status {scr_UpdateHandler.current.Animating} lock {scr_UpdateHandler.current.Lock} updating {scr_UpdateHandler.current.Updating} event {scr_UpdateHandler.current.EventHandler.Active}");
    }

    List<MessageLog> todo;

    private void ClearLogs(bool clearAll = false)
    {
        var clearLogsvalue = scr_System_CentralControl.current.DisplaySetting.clearLogs.value || clearAll ? 0 : scr_System_CentralControl.current.DisplaySetting.MaxLogCount;
        //if (scr_System_CentralControl.current.DisplaySetting.clearLogs.value)
        
        while (LogsList.transform.childCount > clearLogsvalue)
        {
            DestroyImmediate(LogsList.transform.GetChild(0).gameObject);
        }

        // destroy all
        //trackedLogs.Clear();
        todo.Clear();
        scr_System_CampaignManager.current.Logs.Clear();
    }

    //List<List<string>> msgLog;
    //List<string> msg;
    MessageLog last = null;

    private RectTransform currentMsgLog, currentMsg;
    private void AnimateOneStep()
    {
        if (scr_System_CentralControl.current.LogPrefs.DLog_LogsMenu) Debug.Log($"Animateonestep, firstline {firstLine} waiting? {waiting}");
        animationLock = true;
        while (LogsList.transform.childCount > scr_System_CentralControl.current.DisplaySetting.MaxLogCount)
        {
            DestroyImmediate(LogsList.transform.GetChild(0).gameObject);
        }

        //Debug.Log("loglist anchored position is " + LogsList.anchoredPosition.x + "|" + LogsList.anchoredPosition.y);
        LogsList.anchoredPosition = new Vector2(0, 0);

        var current = todo.Count > 0 ? todo[0] : null;
        if (current == null)
        {
            scr_System_CampaignManager.current.Log_TryClearChar(true);
            animationLock = false;
            return;
        }
        else if (!current.displayed)
        {
            if (skipping) last = current;

            if (current is Message_Text)
            {
                RectTransform msgbox = Instantiate(prefab_LogEntry);
                //if (current.PortraitRef == -1000) msgbox = Instantiate(prefab_SeparationEntry);

                msgbox.SetParent(LogsList, false);
                waiting = (current as Message_Text).Draw(skipping, msgbox.GetComponent<scr_MessageLogBox>(), this.prefab_LogLine) || waiting;
                // if (waiting) Debug.Log("waiting!");
                firstLine = false;
            }
            else if (current is Message_Question)
            {
                var question = Instantiate(prefab_question);
                question.transform.SetParent(LogsList, false);
                (current as Message_Question).Draw(skipping, this.m_Canvas, question, this);
                firstLine = true;
            }
            else if (current is Message_LLMQuery)
            {
                var query = Instantiate(prefab_llm);
                query.transform.SetParent(LogsList, false);
                (current as Message_LLMQuery).Draw(skipping, this.m_Canvas, query, this);
                firstLine = true;
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

    bool waiting = false;
    bool animationLock = false;
    bool skipping = false;
    private void AnimateAll()
    {
        animationLock = true;
        last = null;
        skipping = true;
        int prevCount = -1;
        while (canAnimate)
        {
            if (todo.Count == prevCount) break; // stuck (e.g. LLM query still animating), avoid infinite loop
            prevCount = todo.Count;
            AnimateOneStep();
        }
        skipping = false;
        animationLock = false;
        if (last != null) last.ForceDraw();
        last = null;
    }

    public bool canAnimate { get { return todo.Count > 0; } }

    public RectTransform LogsList;
    public RectTransform prefab_LogEntry, prefab_SeparationEntry;
    public scr_HoverableText prefab_LogLine;
    public scr_menu_question prefab_question;
    public scr_menu_LLMQuery prefab_llm;


    private void OnDisable()
    {
        LogsList.gameObject.SetActive(false);
       // AnimateAll();
    }

    private void OnLogsClear(bool flushOnly, bool clearAll)
    {
        if (flushOnly) AnimateAll();
        else this.ClearLogs(clearAll);
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
        if (!this.gameObject.activeInHierarchy) this.gameObject.SetActive(true);
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

        waiting = false;
        if (scr_UpdateHandler.current.Updating || scr_UpdateHandler.current.EventHandler.Active || canAnimate)
        {
            if (scr_UpdateHandler.current.EventHandler.Active && !scr_UpdateHandler.current.EventHandler.Waiting && !canAnimate)
            {
                if (scr_System_CentralControl.current.LogPrefs.DLog_LogsMenu) Debug.Log($"Pre! OnPointerClick updating[{scr_UpdateHandler.current.Updating}] waiting[{scr_UpdateHandler.current.EventHandler.Waiting}] evActive[{scr_UpdateHandler.current.EventHandler.Active}] canAnimate[{canAnimate}]");
                scr_UpdateHandler.current.EventHandler.Run();
            }
            if (!canAnimate && scr_System_CentralControl.current.LogPrefs.DLog_LogsMenu) Debug.Log($"OnPointerClick updating[{scr_UpdateHandler.current.Updating}] waiting[{scr_UpdateHandler.current.EventHandler.Waiting}] evActive[{scr_UpdateHandler.current.EventHandler.Active}] canAnimate[{canAnimate}]");
            Observer_OnClick?.Invoke(eventData);
            if (canAnimate && !animationLock)
            {
                SingleUpdate(eventData.button == PointerEventData.InputButton.Right);
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