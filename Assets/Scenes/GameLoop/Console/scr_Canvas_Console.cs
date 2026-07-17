using Newtonsoft.Json;
using System;
using System.Buffers.Text;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class scr_Canvas_Console : scr_Menu, IPointerClickHandler
{

    public CanvasGroup selfCanvas, parentCanvas;
    public int consoleCount;

    public scr_HoverableText targetName, baseID, targetCurrentJob, targetparty;
    public scr_HoverableText currentRoom;
    public scr_HoverableText blacklist;

    public scr_HoverableText portrait_neutral, portrait_active, portrait_combat;

    protected void Update()
    {
        if (scr_System_CentralControl.current.isSafeMode) return;
        if (Input.GetKeyDown(KeyCode.BackQuote) )
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
            consoleInput.caretPosition = consoleInput.text.Length;
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

           // parentCanvas.interactable = false;
            //parentCanvas.alpha = 0;
           // parentCanvas.blocksRaycasts = false;
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

        foreach(var str in validConsoleCommands) MakeCommandButton(str);

        if (!scr_System_CentralControl.current.isSafeMode)
        {
            foreach (var str in devConsoleCommands) MakeCommandButton(str);
        }

        ValidateAll();

        // consoleCount = scr_System_CentralControl.current.allusedConsoleCommands.Count;
        // https://discussions.unity.com/t/submit-inputfield-when-enter-is-clicked/124549/8
    }


    public RectTransform ConsoleButtonList;
    public consoleEntry consolebtnprefab;
    protected void MakeCommandButton(string s)
    {
        var btn = Instantiate(consolebtnprefab);
        btn.selfRect.SetParent(ConsoleButtonList);

        btn.selfButton.Initialize(this, new ButtonValidator_ConsoleButton(this, btn.selfButton, s));
        btn.selfButton.SetText(s);
        btn.selfButton.optionID = AssertUniqueHash(btn.selfButton.GetHashCode());
        buttonsByID.Add(btn.selfButton.optionID, btn.selfButton);
        validatorsByID.Add(btn.selfButton.optionID, btn.selfButton.Validator);

    }

    string[] validConsoleCommands = new string[]
    {
        "modstatderived",
        "resetAllActorJobs",
        "spawnChara",
        "addItem",
        "modkojovariable",
        "loadevent",
        "modexperience",
        "modrelationshipwith",
        "modpersonalityscore"
    };

    string[] devConsoleCommands = new string[]
    {
        "advReproCycle",
        "wombAddSpermByRef",
        "wombAddSpermByID",
        "ovulate",
        "forceBirth"
    };

    protected void ActivateUI()
    {
        consoleCount = 0;
        consoleInput.ActivateInputField();
        CurrentTarget = scr_System_CampaignManager.current.ConsoleTargetEX && scr_System_CampaignManager.current.CurrentTargetEX != null ? scr_System_CampaignManager.current.CurrentTargetEX : scr_System_CampaignManager.current.CurrentTarget;
        var room = scr_System_CampaignManager.current.Map.FindRoomByChara(CurrentTarget.RefID);// scr_System_CampaignManager.current.CurrentRoom;
        targetName.SetText("CurrentTarget: " + (CurrentTarget == null ? "null" : $"{CurrentTarget.RefID} {CurrentTarget.FullName}, isImprisoned {CurrentTarget.isImprisoned} isRestrained {CurrentTarget.isRestrained}"));
        
        baseID.SetText($"BaseID [{(CurrentTarget == null ? "null" :CurrentTarget.BaseID)}]");
        targetCurrentJob.SetText("CurrentJob: " + (CurrentTarget.CurrentJob == null ? "null" : CurrentTarget.CurrentJob.RefID + " " + CurrentTarget.CurrentJob.DisplayName + " " + (CurrentTarget.CurrentJob.ParentRoom == null ? "nullRoom" : CurrentTarget.CurrentJob.ParentRoom.RefID + " " + CurrentTarget.CurrentJob.ParentRoom.DisplayName)));

        targetparty.SetText($"CurrentParty {(CurrentTarget.FactionManager.CurrentParty == null ? "null" : CurrentTarget.FactionManager.CurrentParty.Job.RefID)}, LockedParty {(CurrentTarget.FactionManager.CurrentLockedParty == null ? "null" : CurrentTarget.FactionManager.CurrentLockedParty.Job.RefID)}, CurrentActive {(CurrentTarget.FactionManager.CurrentActiveParty == null ? "null" : CurrentTarget.FactionManager.CurrentActiveParty.Job.RefID)} isLocked {(CurrentTarget.FactionManager.isPartyLocked)}");

        currentRoom.SetText($"CurrentRoom: {(room == null ? "null" : $"{room.RefID} {room.DisplayName}, isPrison? {room.isRoomPrison} isPrivate? {room.isRoomPrivate}")}");

        portrait_neutral.SetText($"Neutral Portrait Tags [{(CurrentTarget == null ? "null" : String.Join(" ", CurrentTarget.PortraitManager.tags_neutral))}]");
        portrait_active.SetText($"Activity Portrait Tags [{(CurrentTarget == null ? "null" : String.Join(" ", CurrentTarget.PortraitManager.tags_active))}]");
        portrait_combat.SetText($"Combat Portrait Tags [{(CurrentTarget == null ? "null" : String.Join(" ", CurrentTarget.PortraitManager.tags_combat))}]");

        blacklist.SetText($"Blacklist: {CurrentTarget.Memory.PrintBlacklist()}");

        /*
        foreach(var c in ConsoleButtonList.GetComponentsInChildren<scr_HoverableText>())
        {
            c.SetText(c.replaceText);
        }*/

    }



    [SerializeField] public Character_Trainable CurrentTarget = null;

    protected override void Start()
    {
        base.Start();

        ToggleDisplay(true);
    }

    public void ParseConsoleCommand(string command)
    {
        UtilityEX.ParseConsoleCommand(command);
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
        if (eventData.button == PointerEventData.InputButton.Right && UtilityEX.isClickBelowDragThreshold(eventData)) ToggleDisplay(true);
    }


    public InputField consoleInput;


    public class ButtonValidator_ConsoleButton : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Console parent;
        scr_SelectableText text;
        string command;
        public ButtonValidator_ConsoleButton(scr_Menu parent, scr_SelectableText text, string command) : base(parent)
        {
            this.parent = parent as scr_Canvas_Console;
            this.text = text;
            this.command = command;

            this.tooltip = LocalizeDictionary.QueryThenParse($"console_{command}_tooltip");
        }
        public override bool IsButtonValid()
        {
            return true;
        }
        public void OnClickButton()
        {
            parent.consoleInput.text = command;
            parent.consoleInput.caretPosition = parent.consoleInput.text.Length;
        }
    }

}
