using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;



public class scr_menu_changeRel : scr_Menu, IPointerClickHandler
{
    public scr_HoverableText title, greetingMSG;
    public tab_changerel script_changerel;
    public tab_dialogue script_dialogue;

    public Character_Trainable CurrentTarget = null;
    public Character_Relationship CurrentRel = null;
   // public Dictionary<I_IsJobGiver, RelationshipType> rel_per_faction = null;
   // public Dictionary<I_IsJobGiver, bool> rel_is_a = null;
    public List<I_IsJobGiver> priorityList = null;


    public CanvasGroup currentTab = null;
   // public RectTransform rect_bio, rect_social, rect_personal;
   //  public scr_HoverableText empty_bio, empty_social;

    // public scr_box_relationship relBox, relFinal;

    // public scr_HoverableText mood, stress, lust, attitude;

    public void InitializeWithArgument(Character_Trainable source, Character_Trainable target)
    {
        CurrentTarget = source;
        scr_System_CampaignManager.current.CurrentTargetEX = source;

        CurrentRel = source.Relationships.FindRelationshipWith(target);

        if (!initialized) Initialize();

        ValidateAll();
    }


    protected override void OnEnable()
    {
        base.OnEnable();
    }

    protected override void Start()
    {
        base.Start();
        if (this.gameObject.activeInHierarchy) Notify(1001);
    }

    public void RegisterBtn(scr_SelectableText button, ButtonValidator validator)
    {
        int optionID = AssertUniqueHash(button.GetHashCode());

        if (!buttonsByID.ContainsKey(optionID))
        {
            button.Initialize(this, validator);
            button.optionID = optionID;
            buttonsByID.Add(button.optionID, button);
            validatorsByID.Add(button.optionID, button.Validator);
        }
        else
        {
            Debug.LogError($"scr_menu_changeRel registerbtn hash collision on {optionID}");
        }
    }
    protected override void Awake()
    {
        base.Awake();
        this.m_Canvas.overrideSorting = true;
        button_alwaysValid = new ButtonValidator_AlwaysTrue(this);
    }

    public override void Initialize()
    {
        base.Initialize();

        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {
            // Debug.Log("Button " + button + " " + button.optionID);
            switch (button.optionID)
            {
                case 9999: // exit
                    button.Initialize(this, button_alwaysValid); break;
                case 1001: // tab dialogue
                    button.Initialize(this, new button_tab_dialogue(this, button, script_dialogue)); break;
                case 1002: // change rel
                    button.Initialize(this, new button_tab_changerel(this, button, script_changerel)); break;
                default: break;
            }
            if (button.optionID != -1)
            {
                buttonsByID.Add(button.optionID, button);
                validatorsByID.Add(button.optionID, button.Validator);
            }

        }

    }
    public override void ValidateAll()
    {
        base.ValidateAll();
    }
    public void OnPointerClick(PointerEventData eventData)
    {
        // if click outside box
        if ((eventData.rawPointerPress.GetComponent<scr_Canvas_Management>() != null) || (eventData.button == PointerEventData.InputButton.Right && UtilityEX.isClickBelowDragThreshold(eventData)))
        {
            scr_System_SceneManager.current.UnloadLastCanvasFromScene();
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
                case 9999:
                    scr_System_SceneManager.current.UnloadLastCanvasFromScene(); break;
                default: break;
            }
        }
        ValidateAll();
    }
    protected override void OnDestroy()
    {
        scr_System_CampaignManager.current.RegisterSceneUnloadActionCallback(() => { scr_System_CampaignManager.current.NotifyUpdate(); });
        base.OnDestroy();
    }

    class button_tab_changerel : ButtonValidator, I_ButtonClickable
    {
        new scr_menu_changeRel parent;
        tab_changerel target;
        scr_SelectableText text;

        public button_tab_changerel(scr_menu_changeRel parent, scr_SelectableText text, tab_changerel target) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.target = target;
        }

        public override bool IsButtonValid()
        {
            bool isCurrent = parent.currentTab == target.SelfCanvasGroup;
            target.SelfCanvasGroup.gameObject.SetActive(isCurrent);
            text.Toggle(true, isCurrent);
            return true;
        }

        public void OnClickButton()
        {
            parent.currentTab = target.SelfCanvasGroup;
            target.ChangeTab(parent);
        }
    }

    class button_tab_dialogue : ButtonValidator, I_ButtonClickable
    {
        new scr_menu_changeRel parent;
        tab_dialogue target;
        scr_SelectableText text;

        public button_tab_dialogue(scr_menu_changeRel parent, scr_SelectableText text, tab_dialogue target) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.target = target;
        }

        public override bool IsButtonValid()
        {
            bool isCurrent = parent.currentTab == target.SelfCanvasGroup;
            target.SelfCanvasGroup.gameObject.SetActive(isCurrent);
            text.Toggle(true, isCurrent);
            return true;
        }

        public void OnClickButton()
        {
            parent.currentTab = target.SelfCanvasGroup;
            target.ChangeTab(parent);
        }
    }
}
