using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using System;

public class scr_panel_logs : scr_Menu, IPointerClickHandler, IScrollHandler
{
    /// <summary>
    /// This panel's own show/hide group. Instead of toggling the GameObject active (which would stop
    /// the panel processing messages), visibility is driven purely by alpha/interactable/blocksRaycasts:
    /// the panel is shown only when the logs view is active AND this instance is the active display mode.
    /// </summary>
    public CanvasGroup displayGroup;

    /// <summary>Whether the logs view is currently the shown view mode (vs room/map/combat).</summary>
    private bool inLogsView = false;

    private void RefreshVisibility()
    {
        if (displayGroup == null) return;
        bool visible = inLogsView;
        displayGroup.alpha = visible ? 1 : 0;
        displayGroup.interactable = visible;
        displayGroup.blocksRaycasts = visible;
    }

    public CanvasGroup cg_ERA, cg_AVG;
    public RectTransform rect_ERA, rect_AVG;


    public void OnScroll(PointerEventData eventData)
    {
        if (currentMode == LogsDisplayMode.AVG && eventData.scrollDelta.y > 0) SetDisplayMode(LogsDisplayMode.ERA);
        else if (currentMode == LogsDisplayMode.ERA && eventData.scrollDelta.y < 0) SetDisplayMode(LogsDisplayMode.AVG);
    }

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
        inLogsView = vm == ViewMode.View_Logs;
        if (inLogsView) this.lockView = lockView;
        // hide/show via CanvasGroup instead of toggling the GameObject, so the panel keeps processing
        // messages (and stays in sync with its sibling) even while the logs view isn't shown
        RefreshVisibility();
    }

    bool firstLine = true;

    protected override void OnEnable()
    {
        base.OnEnable();
        firstLine = true;
        SetDisplayMode(LogsDisplayMode.ERA);
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
        if (scr_System_CentralControl.current.LogPrefs.DLog_LogsMenu) Debug.Log($"OnLogsadd, waiting? {waiting} displayPortrait? {msg.DisplaPortrait} waitForPortrait? {msg.WaitForPortrait} portraitRef {(msg.Display.PortraitRef == null ? "null" : msg.Display.PortraitRef.Owner.CallName)} multiple? {msg.Display.MultipleChara.Count} animate? {animate} count? {todo.Count} firstline? {firstLine} immediate? {immediate}");
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

    LogsDisplayMode currentMode = LogsDisplayMode.ERA;

    protected void SetDisplayMode(LogsDisplayMode mode)
    {
        if (mode == LogsDisplayMode.Dontcare)
        {
            //
        }
        else
        {
            this.currentMode = mode;
        }
        
        if (this.currentMode == LogsDisplayMode.Dontcare) this.currentMode = LogsDisplayMode.ERA;

        SetCG(cg_ERA, this.currentMode == LogsDisplayMode.ERA);
        SetCG(cg_AVG, this.currentMode == LogsDisplayMode.AVG);
    }

    void SetCG(CanvasGroup group, bool active)
    {
        group.alpha = active ? 1 : 0;
        group.blocksRaycasts = active;
        group.interactable = active;
    }


    private void ClearLogs(bool clearAll = false)
    {
        var clearLogsvalue = scr_System_CentralControl.current.DisplaySetting.clearLogs.value || clearAll ? 0 : scr_System_CentralControl.current.DisplaySetting.MaxLogCount;
        //if (scr_System_CentralControl.current.DisplaySetting.clearLogs.value)
        
        while (rect_ERA.transform.childCount > clearLogsvalue)
        {
            DestroyImmediate(rect_ERA.transform.GetChild(0).gameObject);
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

    /// <summary>
    /// AVG only ever shows the single most-recent Text/Question/InputField message - clear whatever's
    /// there right before drawing a new one into it (not unconditionally every tick, which would destroy
    /// an in-progress box mid-animation of the same still-current message).
    /// </summary>
    private void ClearAVGList()
    {
        while (rect_AVG.transform.childCount > 0) DestroyImmediate(rect_AVG.transform.GetChild(0).gameObject);
    }

    private void AnimateOneStep()
    {
        if (scr_System_CentralControl.current.LogPrefs.DLog_LogsMenu) Debug.Log($"Animateonestep, firstline {firstLine} waiting? {waiting}");
        animationLock = true;
        while (rect_ERA.transform.childCount > scr_System_CentralControl.current.DisplaySetting.MaxLogCount)
        {
            DestroyImmediate(rect_ERA.transform.GetChild(0).gameObject);
        }

        //Debug.Log("loglist anchored position is " + LogsList.anchoredPosition.x + "|" + LogsList.anchoredPosition.y);
        rect_ERA.anchoredPosition = new Vector2(0, 0);

        bool drawnNew = false;

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

            // re-establish the authoritative display mode before this message draws, undoing any
            // scrollwheel-driven temporary override; then apply this message's own mode if it has one
            SetDisplayMode(current.Display.displayMode);
            scr_System_CampaignManager.current.InvokeMessageDisplay(current.Display);
            bool skipImage = skipping;

            if (current is Message_Text)
            {
                ClearAVGList();

                RectTransform msgbox_ERA = Instantiate(prefab_LogEntry);
                RectTransform msgbox_AVG = Instantiate(prefab_LogEntry);
                //if (current.PortraitRef == -1000) msgbox = Instantiate(prefab_SeparationEntry);

                msgbox_ERA.SetParent(rect_ERA, false);
                msgbox_AVG.SetParent(rect_AVG, false);

                waiting = (current as Message_Text).Draw(skipImage, msgbox_ERA.GetComponent<scr_MessageLogBox>(), this.prefab_LogLine, msgbox_AVG.GetComponent<scr_MessageLogBox>(), this.prefab_LogLine) || waiting;
                drawnNew = true;
            }
            else if (current is Message_Question)
            {
                ClearAVGList();

                var questionERA = Instantiate(prefab_question);
                questionERA.transform.SetParent(rect_ERA, false);
                var questionAVG = Instantiate(prefab_question);
                questionAVG.transform.SetParent(rect_AVG, false);
                (current as Message_Question).Draw(skipImage, this.m_Canvas, questionERA, questionAVG, this);
                drawnNew = true;
            }
            else if (current is Message_InputField)
            {
                ClearAVGList();

                var inputERA = Instantiate(prefab_inputField);
                inputERA.transform.SetParent(rect_ERA, false);
                var inputAVG = Instantiate(prefab_inputField);
                inputAVG.transform.SetParent(rect_AVG, false);
                (current as Message_InputField).Draw(skipImage, this.m_Canvas, inputERA, inputAVG, this);
                drawnNew = true;
            }
            else if (current is Message_LLMQuery)
            {
                var query = Instantiate(prefab_llm);
                query.transform.SetParent(rect_ERA, false);
                (current as Message_LLMQuery).Draw(skipImage, this.m_Canvas, query, this);
                drawnNew = true;
            }
            else if (current is Message_Question_Record)
            {
                var question = Instantiate(prefab_question);
                question.transform.SetParent(rect_ERA, false);
                (current as Message_Question_Record).Draw(skipImage, this.m_Canvas, question, this);
                drawnNew = true;
            }

        }
        else if (current.canAnimate())
        {
            current.Animate();
        }

        if (current.displayed && drawnNew && firstLine && !current.autoAnimate) firstLine = false;

        if (current.displayed && !current.canAnimate())
        {
            todo.RemoveAt(0);

            // auto advance check
            var next = todo.Count > 0 ? todo[0] : null;
            if (next != null && next.autoAnimate)
            {
                AnimateOneStep();
            }
            else
            {
                UpdateAnimatingStatus();
            }
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

    public RectTransform prefab_LogEntry, prefab_SeparationEntry;
    public scr_HoverableText prefab_LogLine;
    public scr_menu_question prefab_question;
    public scr_menu_inputField prefab_inputField;
    public scr_menu_LLMQuery prefab_llm;


    private void OnDisable()
    {
        SetCG(cg_ERA, false);
        SetCG(cg_AVG, false);
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

        // start hidden via CanvasGroup (not GameObject SetActive) so the panel keeps running
        RefreshVisibility();
    }

    protected void OnEvent(EventStatus status, bool forceLogging)
    {
#if UNITY_EDITOR
        if (scr_System_CentralControl.current.LogPrefs.DLog_LogsMenu) Debug.Log($"OnEvent {status}, waiting? {(status == EventStatus.waiting)} firstline {firstLine}");
#endif
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
                SingleUpdate(eventData.button == PointerEventData.InputButton.Right && currentMode == LogsDisplayMode.ERA);
            }
        }
        else
        {
            UpdateAnimatingStatus();
            if (rect_ERA.anchoredPosition != new Vector2(0, 0)) rect_ERA.anchoredPosition = new Vector2(0, 0);
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