using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System;

public class scr_Panel_CurrentCharList : scr_Menu
{ 
    protected override void OnDestroy()
    {
        base.OnDestroy();

    }

    protected override void Awake()
    {
        base.Awake();
        scr_System_CampaignManager.current.Observer_CurrentTarget += ReadCurrentChar;
        scr_System_CampaignManager.current.Observer_CurrentRoom += OnRoomChange;
        scr_System_CampaignManager.current.Observer_playerParty += OnPlayerPartyChange;
        scr_System_CampaignManager.current.Observer_PlayerJob += OnPlayerJobChange;

        scr_UpdateHandler.current.Observer_PostUpdateTime_3 += OnPostUpdateTime3;
        scr_System_CampaignManager.current.Observer_UpdateNotice += OnUpdateNotice;

        
    }

    protected override void Start()
    {
        currentTargetBox.InitializeWithArgument(-1);
        if(!initialized) Initialize();
        UpdateChara();
    }

    private void OnUpdateNotice(bool b)
    {
        UpdateChara();
    }

    private void OnPostUpdateTime3()
    {
        UpdateChara();
    }
    private void OnRoomChange(int updateOrder, Room_Instance room)
    {
        if (updateOrder != 2) return;
        UpdateChara();
    }

    private void UpdateChara()
    {
        var room = scr_System_CampaignManager.current.CurrentRoom;
        List<int> removeList = new List<int>();
        foreach (int charaRef in buttonsByID.Keys)
        {
            var room2 = scr_System_CampaignManager.current.Map.FindRoomByChara(charaRef);
            if ( room2 == null || room2.RefID != room.RefID)
            {
                removeList.Add(charaRef);
            }
        }
        foreach (int i in removeList)
        {
            RemoveChara(i);
        }

        foreach(var chara in scr_System_CampaignManager.current.CharaRefInCurrentRoom)
        {
            AddChara(chara);
        }
    }

    private void OnPlayerPartyChange(bool value) { ValidateAll(); }
    private void OnPlayerJobChange(int jobRefID, Job job){ValidateAll();}

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

    private void ReadCurrentChar(int id)
    {
        ValidateAll();
    }

    public RectTransform prefab_nameBox, listBox;
    public void AddChara(int refID)
    {
        //Debug.Log("display : adding character " + refID + " " + scr_System_CampaignManager.current.FindInstanceByID(refID).FirstName);

        if (refID > 0 && !buttonsByID.ContainsKey(refID))
        {
            RectTransform rect = Instantiate(prefab_nameBox);
            rect.SetParent(listBox, false);
            scr_SelectableText b = rect.GetComponent<scr_SelectableText>();

            b.isButtonToggle = true;
//            b.forbidNotify = true;

            b.Initialize(this, new ButtonValidator_selectChara(this, refID, b));

            //b.SetText(scr_System_CampaignManager.current.FindInstanceByID(refID).FirstName);

            b.optionID = refID;

            buttonsByID.Add(b.optionID, b);
            validatorsByID.Add(refID, b.Validator);

            b.Validate();
        }
    }

    public void RemoveChara(int refID)
    {
        if (refID != -1 && buttonsByID.ContainsKey(refID))
        {
            scr_SelectableText s = buttonsByID[refID];
            buttonsByID.Remove(refID);
            s.gameObject.SetActive(false);
            Destroy(s.gameObject);
            s = null;

            ButtonValidator b = validatorsByID[refID];
            validatorsByID.Remove(refID);
            b = null;
        }
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

    public class ButtonValidator_selectChara : ButtonValidator, I_ButtonClickable
    {

        int refID = -1;
        scr_SelectableText text;
        new scr_Panel_CurrentCharList parent;
        RectTransform transform;
        string firstName;
        public ButtonValidator_selectChara(scr_Menu parent, int refID, scr_SelectableText text) : base(parent)
        {
            this.refID = refID;
            this.text = text;
            this.parent = parent as scr_Panel_CurrentCharList;
            transform = text.GetComponent<RectTransform>();
            firstName = scr_System_CampaignManager.current.FindInstanceByID(refID).FirstName;
        }

        public override bool IsButtonValid()
        {
            if (scr_System_CampaignManager.current.party.HasMember(refID)) text.SetText(firstName+"*");
            else text.SetText(firstName);
            if (scr_System_CampaignManager.current.CurrentTargetRef == refID)
            {
                text.Toggle(true, true);
                //transform.SetSiblingIndex(0);
            }
            else text.Toggle(true, false);

          //  if (scr_System_CampaignManager.current.displaySex){
          //      tooltip += "Cannot change target via this button during Training";
          //      return false;
        //    }else{
            return true;
            

        }

        public void OnClickButton()
        {
            if (this.refID > 0)
            {

                if (scr_System_CampaignManager.current.CurrentTargetRef != refID)
                {
                    scr_System_CampaignManager.current.ChangeCurrentTarget(refID);
                }
                else if (!scr_System_CampaignManager.current.displaySex)
                {
                    scr_System_CampaignManager.current.ChangeCurrentTarget(0);
                }
                scr_System_CampaignManager.current.ChangeCurrentViewMode(ViewMode.View_Room);
            }
        }
    }

    public scr_CharPortraitBox currentTargetBox;
}
