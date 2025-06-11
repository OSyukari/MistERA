using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using TMPro;
using UnityEngine.EventSystems;
using System;
using Newtonsoft.Json;

public class scr_Canvas_Console : scr_Menu, IPointerClickHandler
{

    public CanvasGroup selfCanvas;
    public int consoleCount;

    public scr_HoverableText targetName, targetCurrentJob;
    public scr_HoverableText currentRoom;
    public scr_HoverableText blacklist;

    protected void Update()
    {
        if (Input.GetKeyDown(KeyCode.BackQuote))
        {
            ToggleDisplay();
        }

        if ((Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.DownArrow)) && scr_System_CentralControl.current.allusedConsoleCommands.Count > 0)
        {
            if (Input.GetKeyDown(KeyCode.UpArrow))
            {
                consoleCount = Math.Min(consoleCount + 1, scr_System_CentralControl.current.allusedConsoleCommands.Count);
                if(consoleCount > 0) consoleInput.text = scr_System_CentralControl.current.allusedConsoleCommands[consoleCount - 1];
            }else if (Input.GetKeyDown(KeyCode.DownArrow))
            {
                consoleCount = Math.Max(consoleCount - 1, 1);
                if (consoleCount > 0) consoleInput.text = scr_System_CentralControl.current.allusedConsoleCommands[consoleCount - 1];
            }
        }
    }

    protected void ToggleDisplay(bool turnOff = false)
    {
        if (turnOff || selfCanvas.interactable)
        {
            selfCanvas.interactable = false;
            selfCanvas.alpha = 0;
            selfCanvas.blocksRaycasts = false;
            consoleInput.text = string.Empty;
            scr_System_CampaignManager.current.UpdateScene();
        }
        else
        {
            selfCanvas.interactable = true;
            selfCanvas.alpha = 1;
            selfCanvas.blocksRaycasts = true;
            consoleInput.text = string.Empty;
            ActivateUI();
        }
    }

    protected override void Awake()
    {
        base.Awake();
    }

    public override void Initialize()
    {
        base.Initialize();


        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {
            // Debug.Log("Button " + button + " " + button.optionID);
            switch (button.optionID)
            {
                default: break;
            }
            if (button.optionID != -1)
            {
                buttonsByID.Add(button.optionID, button);
                validatorsByID.Add(button.optionID, button.Validator);
            }

        }
       // consoleCount = scr_System_CentralControl.current.allusedConsoleCommands.Count;
        // https://discussions.unity.com/t/submit-inputfield-when-enter-is-clicked/124549/8
    }

    protected void ActivateUI()
    {
        consoleCount = 0;
        consoleInput.ActivateInputField();
        CurrentTarget = scr_System_CampaignManager.current.CurrentTarget;
        var room = scr_System_CampaignManager.current.CurrentRoom;
        targetName.SetText("CurrentTarget: " + (CurrentTarget == null ? "null" : CurrentTarget.RefID + " " + CurrentTarget.FullName));
        targetCurrentJob.SetText("CurrentJob: " + (CurrentTarget.CurrentJob == null ? "null" : CurrentTarget.CurrentJob.RefID + " " + CurrentTarget.CurrentJob.DisplayName + " " + (CurrentTarget.CurrentJob.ParentRoom == null ? "nullRoom" : CurrentTarget.CurrentJob.ParentRoom.RefID + " " + CurrentTarget.CurrentJob.ParentRoom.DisplayName)));
        currentRoom.SetText("CurrentRoom: " + (room == null ? "null" : room.RefID + " " + room.DisplayName));

        blacklist.SetText($"Blacklist: {CurrentTarget.Memory.PrintBlacklist()}");
    }

    [SerializeField] public Character_Trainable CurrentTarget = null;

    protected override void Start()
    {
        base.Start();
    }

    public void ParseConsoleCommand(string command)
    {
        Utility.ParseConsoleCommand(command);
        ToggleDisplay(true);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        //if (consoleInput != null) consoleInput.onEndEdit.RemoveListener(ParseConsoleCommand);
    }

    protected void OnDisable()
    {
       // if (consoleInput != null) consoleInput.onEndEdit.RemoveListener(ParseConsoleCommand);
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

    public void OnPointerClick(PointerEventData eventData)
    {
        // if click outside box
        if (eventData.button == PointerEventData.InputButton.Right && Utility.isClickBelowDragThreshold(eventData)) ToggleDisplay(true);
    }


   public InputField consoleInput;
}
