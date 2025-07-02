using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;


public class scr_Panel_BottomBar : scr_Menu
{
    private Image image;

    //private float update;
    public bool DisableQuickLoad = true;
    public bool DisableQuickSave = true;

    protected override void Awake()
    {
        base.Awake();

        scr_System_CampaignManager.current.Observer_CurrentViewMode += OnViewModeChange;
        scr_System_CampaignManager.current.Observer_PlayerJob += OnPlayerJobChange;
        scr_System_CampaignManager.current.Observer_CurrentTarget += OnCurrentTargetChange;
        scr_System_CampaignManager.current.Observer_UpdateNotice += OnUpdateNotice;

        image = this.GetComponent<Image>();
    }
    private void OnViewModeChange(ViewMode vm, bool lockView)
    {
        ValidateAll();
    }
    private void OnUpdateNotice(bool b)
    {
        ValidateAll();
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



    private void OnPlayerJobChange(int jobRef, Job job)
    {
        ValidateAll();
    }


    private void OnCurrentTargetChange(int i)
    {
        ValidateAll();
    }

    public override void Initialize()
    {
        base.Initialize();

        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {

            switch (button.optionID)
            {
                case -2:
                    button.Initialize(this, button_alwaysValid);
                    break;
                case 10: button.Initialize(this, new ButtonValidator_InspectChara(this, button)); break;
                case 20: button.Initialize(this, new ButtonValidator_ChangeView(this, button, ViewMode.View_Logs)); break;
                case 21: button.Initialize(this, new ButtonValidator_ChangeView(this, button, ViewMode.View_Room)); break;
                case 22: button.Initialize(this, new ButtonValidator_Movement(this)); break;
                case 30: button.Initialize(this, new ButtonValidator_Management(this, button)); break;
                case 40: button.Initialize(this, new ButtonValidator_QuickSave(this)); break;
                case 42: button.Initialize(this, new ButtonValidator_Load(this, button)); break;
                case 50: button.Initialize(this, new ButtonValidator_OpenGuide(this, button)); break;
                default:
                    break;
            }
            if (button.optionID != -1)
            {
                buttonsByID.Add(button.optionID, button);
                validatorsByID.Add(button.optionID, button.Validator);
            }

        }

        // build all presetList
        ValidateAll();
    }



    public class ButtonValidator_ChangeView : ButtonValidator, I_ButtonClickable
    {
        ViewMode targetVM;
        scr_SelectableText text;
        new scr_Panel_BottomBar parent;
        string errorTooltip1;
        public ButtonValidator_ChangeView(scr_Panel_BottomBar parent, scr_SelectableText text, ViewMode targetVM) : base(parent)
        {
            this.parent = parent;
            this.targetVM = targetVM;
            this.text = text;
            errorTooltip1 = LocalizeDictionary.QueryThenParse("ui_load_tooltip_cannotLoadduringUpdate");
        }

        public override bool IsButtonValid()
        {
            tooltip = "";
            //Debug.Log("isbuttonvalid " + targetVM);
            if(scr_UpdateHandler.current.Lock)
            {
                this.tooltip = errorTooltip1;
                return false;
            }
            if (scr_System_CampaignManager.current.CurrentViewMode == targetVM)
            {
                state = ButtonValidator_States.Valid;
                this.text.Toggle(true, true);
                return true;
            }
            else if (targetVM == ViewMode.View_Logs || targetVM == ViewMode.View_Room)
            {
                state = ButtonValidator_States.Valid;
                this.text.Toggle(true, false);
                return true;
            }
            else
            {
                this.text.Toggle(true, false);

                Job job = scr_System_CampaignManager.current.Player.CurrentJob;
                if (job is Job_Sex_Group)
                {
                    tooltip += "Cannot leave room until Sex is over.";
                    state = ButtonValidator_States.Invalid;
                    return false;
                }
                else
                {
                    tooltip = "";
                    state = ButtonValidator_States.Valid;
                    return true;
                }
            }


        }

        public void OnClickButton()
        {
            if (scr_System_CampaignManager.current.CurrentViewMode != targetVM) scr_System_CampaignManager.current.ChangeCurrentViewMode(targetVM);
            else if (scr_System_CampaignManager.current.CurrentViewMode == targetVM && targetVM != ViewMode.View_Room) scr_System_CampaignManager.current.ChangeCurrentViewMode(ViewMode.View_Room);
        }
    }


    public RectTransform prefab_Canvas_charaDetail;
    public class ButtonValidator_InspectChara : ButtonValidator, I_ButtonClickable
    {
        new scr_Panel_BottomBar parent;
        scr_SelectableText text;
        public ButtonValidator_InspectChara(scr_Menu parent, scr_SelectableText text) : base(parent)
        {
            this.parent = parent as scr_Panel_BottomBar;
            this.text = text;
        }
        int charaRefID;

        public override bool IsButtonValid()
        {
            charaRefID = scr_System_CampaignManager.current.CurrentTargetRef;
            //Debug.Log("isbuttonvalid " + charaRefID + " " + scr_System_CentralControl.current.CanHaveSex(0, charaRefID));
            if (charaRefID > 0 && !scr_System_CampaignManager.current.displaySex) text.SetText("%%comManager_bottom_inspect%%");
            else
            {
                charaRefID = 0;
                text.SetText("%%comManager_bottom_inspectSelf%%");
            }
            return (charaRefID >= 0);
        }

        public void OnClickButton()
        {
            //, parent.m_Canvas.transform.GetComponent<RectTransform>()
            scr_Menu_CharaDetail detail = scr_System_SceneManager.current.LoadCanvasIntoScene(parent.prefab_Canvas_charaDetail, parent.m_Canvas.GetComponent<RectTransform>()).GetComponent<scr_Menu_CharaDetail>();
            detail.InitializeWithArgument(charaRefID);

        }
    }

    public canvas_RoomDisplay canvas_FloorDisplay;
    public class ButtonValidator_Movement : ButtonValidator, I_ButtonClickable
    {
        new scr_Panel_BottomBar parent;
        string errorTooltip1;
        public ButtonValidator_Movement(scr_Menu parent) : base(parent)
        {
            this.parent = parent as scr_Panel_BottomBar;
            errorTooltip1 = LocalizeDictionary.QueryThenParse("ui_load_tooltip_cannotLoadduringUpdate");
        }

        public override bool IsButtonValid()
        {
            tooltip = "";
            if (scr_UpdateHandler.current.Lock)
            {
                this.tooltip = errorTooltip1;
                return false;
            }
            Job job = scr_System_CampaignManager.current.Player.CurrentJob;
            if (job is Job_Sex_Group)
            {
                tooltip += "Cannot leave room until Sex is over.";
                state = ButtonValidator_States.Invalid;
                return false;
            }
            else
            {
                tooltip = "";
                state = ButtonValidator_States.Valid;
                return true;
            }
        }

        public void OnClickButton()
        {
            //, parent.m_Canvas.GetComponent<RectTransform>()
            canvas_RoomDisplay FloorDisplay = scr_System_SceneManager.current.LoadCanvasIntoScene(parent.canvas_FloorDisplay.GetComponent<RectTransform>(), parent.m_Canvas.GetComponent<RectTransform>()).GetComponent<canvas_RoomDisplay>();
            FloorDisplay.LoadFloor(scr_System_CampaignManager.current.CurrentRoom.parentFloor);
        }
    }


    public RectTransform prefab_Canvas_Management;
    public class ButtonValidator_Management : ButtonValidator, I_ButtonClickable
    {
        new scr_Panel_BottomBar parent;
        string errorTooltip1;
        public ButtonValidator_Management (scr_Menu parent, scr_SelectableText text): base(parent)
        {
            this.parent = parent as scr_Panel_BottomBar;
            this.text = text;
            errorTooltip1 = LocalizeDictionary.QueryThenParse("ui_quicksave_tooltip_cannotSaveduringUpdate");
        }

        scr_SelectableText text;

        public override bool IsButtonValid()
        {
            if (scr_UpdateHandler.current.Lock)
            {
                this.tooltip = errorTooltip1;
                return false;
            }
            if (scr_System_CampaignManager.current.Player.FactionManager.ManagerFactions.Count > 0)
            {
                return true;
            }
            else
            {
                tooltip += "player is not manager of any faction.";
                return false;
            }
        }

        public void OnClickButton()
        {
            //, parent.m_Canvas.GetComponent<RectTransform>()
            scr_Canvas_Management manage = scr_System_SceneManager.current.LoadCanvasIntoScene(parent.prefab_Canvas_Management, parent.m_Canvas.GetComponent<RectTransform>()).GetComponent<scr_Canvas_Management>();
            manage.InitializeWithArgument();
            
        }
    }

    public class ButtonValidator_QuickSave : ButtonValidator, I_ButtonClickable
    {
        new scr_Panel_BottomBar parent;
        public ButtonValidator_QuickSave(scr_Menu parent) : base(parent)
        {
            this.parent = parent as scr_Panel_BottomBar;
            errorTooltip1 = LocalizeDictionary.QueryThenParse("ui_quicksave_tooltip_cannotSaveduringUpdate");
            errorTooltip2 = LocalizeDictionary.QueryThenParse("ui_quicksave_tooltip_cannotSaveDuringSex");
        }

        scr_SelectableText text;
        string errorTooltip1, errorTooltip2;
        public override bool IsButtonValid()
        {
            this.tooltip = "";
            //return false;
            if (parent.DisableQuickSave)
            {
                this.tooltip = "quicksave disabled";
                return false;
            }
            else if (scr_UpdateHandler.current.Lock)
            {
                this.tooltip = errorTooltip1;
                return false;
            }
            else if (scr_System_CampaignManager.current.Player.CurrentJob is Job_Sex_Group)
            {
                this.tooltip = errorTooltip2;
                return false;
            }
            else
            {
                return true;
            }
        }

        public void OnClickButton()
        {
            scr_System_CentralControl.current.QuickSave();

        }
    }

    public class ButtonValidator_Load : ButtonValidator, I_ButtonClickable
    {
        new scr_Panel_BottomBar parent;
        scr_HoverableText text;
        public ButtonValidator_Load(scr_Menu parent, scr_SelectableText text) : base(parent)
        {
            this.parent = parent as scr_Panel_BottomBar;
            this.text = text.GetComponent<scr_HoverableText>();
            errorTooltip1 = LocalizeDictionary.QueryThenParse("ui_load_tooltip_cannotLoadduringUpdate");
            errorTooltip2 = LocalizeDictionary.QueryThenParse("ui_load_tooltip_cannotLoadduringSex");
        }

        string errorTooltip1, errorTooltip2;
        public override bool IsButtonValid()
        {
            this.tooltip = "";
            if (scr_UpdateHandler.current.Lock)
            {
                this.tooltip = errorTooltip1;
                return false;
            }
            else if (scr_System_CampaignManager.current.Player.CurrentJob is Job_Sex_Group)
            {
                this.tooltip = errorTooltip2;
                return false;
            }
            else
            {
                return true;
            }
        }

        public void OnClickButton()
        {
            scr_System_SceneManager.current.LoadCanvasIntoScene(parent.prefab_Canvas_LoadSave, parent.m_Canvas.GetComponent<RectTransform>());
        }
    }

    public RectTransform prefab_Canvas_LoadSave;
    public RectTransform prefab_Canvas_HelperGuide;

    public class ButtonValidator_OpenGuide : ButtonValidator, I_ButtonClickable
    {
        new scr_Panel_BottomBar parent;
        scr_HoverableText text;
        public ButtonValidator_OpenGuide(scr_Menu parent, scr_SelectableText text) : base(parent)
        {
            this.parent = parent as scr_Panel_BottomBar;
            this.text = text.GetComponent<scr_HoverableText>();
        }

        public override bool IsButtonValid()
        {

            return true;
        }

        public void OnClickButton()
        {
            scr_System_SceneManager.current.LoadCanvasIntoScene(parent.prefab_Canvas_HelperGuide, parent.m_Canvas.GetComponent<RectTransform>());
        }
    }


}
