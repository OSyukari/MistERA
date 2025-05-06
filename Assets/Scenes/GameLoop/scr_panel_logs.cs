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

    protected override void OnEnable()
    {
        base.OnEnable();
        LogsList.gameObject.SetActive(true);
        if (canAnimate) AnimateOneStep();
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
        if (animate) while (canAnimate) AnimateOneStep();
        else if (Input.GetMouseButton(1) && canAnimate) AnimateOneStep();
    }


    protected void UpdateAnimatingStatus()
    {
        scr_UpdateHandler.current.Animating = canAnimate;
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
        while (LogsList.transform.childCount > scr_System_CentralControl.current.pref.MaxLogCount)
        {
            DestroyImmediate(LogsList.transform.GetChild(0).gameObject);
        }

        UpdateAnimatingStatus();

        //Debug.Log("loglist anchored position is " + LogsList.anchoredPosition.x + "|" + LogsList.anchoredPosition.y);
        LogsList.anchoredPosition = new Vector2(0, 0);

        var current = todo.Count > 0 ? todo[0] : null;
        if(current == null)
        {
            scr_System_CampaignManager.current.Log_TryClearChar(true);
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
                (current as Message_Question).Draw(question);
            }
        }
        else if (current.canAnimate())
        {
            current.Animate();
        }

        if (!current.canAnimate()) todo.RemoveAt(0);
    }


    private IEnumerator AnimateAll()
    {
        while (canAnimate)
        {
            AnimateOneStep();
      //      yield return new WaitForSecondsRealtime(0.001f);
        }
        if (this.gameObject.activeInHierarchy)
        {
            yield return new WaitForSecondsRealtime(0.5f);
            scr_System_CampaignManager.current.ChangeCurrentViewMode(ViewMode.View_Room);
        }
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
        if (flushOnly) StartCoroutine(AnimateAll());
        else this.ClearLogs();
    }


    private void SingleUpdate(bool skipAll)
    {
        if (skipAll) StartCoroutine(AnimateAll());
        else if (canAnimate) AnimateOneStep();
        else scr_System_CampaignManager.current.ChangeCurrentViewMode(ViewMode.View_Room);
    }

    protected override void Awake()
    {
        base.Awake();

        scr_System_CampaignManager.current.Observer_CurrentViewMode += OnViewModeChange;
        scr_System_CampaignManager.current.Observer_LogsClear += OnLogsClear;

        button_alwaysValid = new ButtonValidator_AlwaysTrue(this);

        scr_System_CampaignManager.current.Observer_MessageLogs += OnLogAdd;
        scr_UpdateHandler.current.Observer_LogsSingleStepUpdate += SingleUpdate;

        todo = new List<MessageLog>();
       // msg = new List<string>();
       // msgLog = new List<List<string>>();

        this.gameObject.SetActive(false);
    }

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

        if (!canAnimate && !lockView && (clickID == -1 || clickID == -2) && Utility.isClickBelowDragThreshold(eventData))
        {   // exclude middle mouse
            if (LogsList.anchoredPosition != new Vector2(0, 0))
            {
                LogsList.anchoredPosition = new Vector2(0, 0);
            }
            else
            {
                scr_System_CampaignManager.current.ChangeCurrentViewMode(ViewMode.View_Room);
            }
        }
        else if (eventData.button == PointerEventData.InputButton.Right)
        {
            StartCoroutine(AnimateAll());
        }
        else
        {
            AnimateOneStep();
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