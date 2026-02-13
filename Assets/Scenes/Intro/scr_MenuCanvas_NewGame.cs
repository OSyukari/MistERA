using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System;

public class scr_MenuCanvas_NewGame : scr_Menu
{
    public TextMeshProUGUI campaign_name;
    public TextMeshProUGUI campaign_descriptions;

    public RectTransform prefab_menuCanvas_CharaSelect;
    protected scr_Menu_CharaSelect charaSelect;

    public TextMeshProUGUI campaign_options;
    //public TextMeshProUGUI campaign_options_descriptions;
    public override void Initialize()
    {
        base.Initialize();


        InitializeWithArgument();
    }

    protected override void Awake()
    {
        base.Awake();
        button_alwaysValid = new ButtonValidator_AlwaysTrue(this);
        playerName = playerSelect.GetComponent<scr_SelectableText>();
        playerNameButton = playerSelect.GetComponent<scr_HoverableText>();
        companionName = companionSelect.GetComponent<scr_SelectableText>();
    }

    
    public Character_Trainable c = null;
    public TextMeshProUGUI playerSelect;
    private scr_SelectableText playerName;
    private scr_HoverableText playerNameButton;



    public void SetPlayerChar(Character_Trainable c)
    {
        if (c == null)
        {
            c = null;
            playerName.SetText(LocalizeDictionary.QueryThenParse(playerNameButton.replaceText));
        }
        else
        {
            this.c = c;
            playerName.SetText(c.FullName);
        }

        if (!(c != null && c.StartingGift.ID == "charOriginGift_companion" && companion != null && companion.Origin.ID == c.Origin.ID))
        {
            SetComanionChar(null);
        }
        GetButton_ConfirmCampaignStart.Reset();
        ValidateAll();

    }

    Character_Trainable companion = null;
    public TextMeshProUGUI companionSelect;
    private scr_SelectableText companionName;

    public void SetComanionChar(Character_Trainable c)
    {
        if (c == null)
        {
            companion = null;
            companionName.SetText(" - ");
        }
        else
        {
            this.companion = c;
            companionName.SetText(companion.FirstName + " " + ((companion.MiddleName.Length > 0) ? companion.MiddleName + " " : "") + companion.LastName);
        }

    }

    
    public void InitializeWithArgument()
    {
        if (!initialized) Initialize();

        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {
            //Debug.Log("Button " + button + " " + button.optionID);
            switch (button.optionID)
            {
                case 101:   // campaign left
                    button.Initialize(this, new ButtonValidator_selectCampaign(this, false)); break;
                case 102:   // campaign right
                    button.Initialize(this, new ButtonValidator_selectCampaign(this, true)); break;
                case 111:   // c.option left
                    button.Initialize(this, new ButtonValidator_selectCampaignOption(this, false)); break;
                case 112:   // c.option right
                    button.Initialize(this, new ButtonValidator_selectCampaignOption(this, true)); break;
                case 200:   // select player
                    button.Initialize(this, new ButtonValidator_selectPlayer(this));
                    break;
                case 201:   // select extra
                    button.Initialize(this, new ButtonValidator_selectPlayerCompanion(this));
                    break;
                case 1000:   // confirm and start
                    button_confirmCampaignStart = new ButtonValidator_confirmCampaignStart(this, button.optionID);
                    button.Initialize(this, button_confirmCampaignStart);
                    break;
                case 9999:  //exit without saving
                    button.Initialize(this, button_alwaysValid); break;
                case -1:
                    break;
                default:
                    button.Initialize(this, button_alwaysValid); break;
            }

            if (button.optionID != -1)
            {
                buttonsByID.Add(button.optionID, button);
                validatorsByID.Add(button.optionID, button.Validator);
            }

        }

        ValidateAll();
        this.SetCurrentCampaign(scr_System_Serializer.current.MasterList.CampaignSettings.GetByID("campaign_starfarers"));
        this.SetPlayerChar(null);

    }



    public override void Notify(int optionID)
    {
        // reset conflict validators
        GetButton_ConfirmCampaignStart.Reset();

        //Debug.Log("Parent Notified ! [" + optionID + "]");
        //
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
                    scr_System_SceneManager.current.UnloadLastCanvasFromScene(); 
                    break;
                case 1000:
                    if (currentCampaign.ID == "campaign_chalicedivers") companion = null;
                    //if (currentCampaign.ID == "campaign_starfarers") currentCampaign_option = null;
                    scr_System_CampaignManager.current.StartCampaign(currentCampaign,currentCampaign_option, c, companion);
                    scr_System_SceneManager.current.UnloadScene(GlobalValues.IntroScene);
                    break;
                default:
                    break;
            }
        }

        ValidateAll();
    }

    CampaignSettings currentCampaign;
    CampaignSettings_ExtraOptions currentCampaign_option;

    protected void SetCurrentCampaign(CampaignSettings c)
    {
        currentCampaign = c;
        campaign_name.text = c.DisplayName;

        if (currentCampaign.extraOptions.Count > 0 && currentCampaign.extraOptions[0].ID != "")
        {
            SetCurrentCampaignOption(currentCampaign.extraOptions[0]);
        }
        else
        {
            this.currentCampaign_option = null;
            campaign_options.text = "-";
            campaign_descriptions.text = c.Tooltip;
        }
        // reset campaign options
        // set campaign text
    }

    protected void SetCurrentCampaignOption(CampaignSettings_ExtraOptions ex)
    {
        this.currentCampaign_option = ex;
        campaign_options.text = ex.DisplayName;

        campaign_descriptions.text = currentCampaign.Tooltip + "\n\n" + ex.Tooltip;
    }

    public void OpenCharaSelect(Action<Character_Trainable> a, string filterByOrigin = "")
    {
        charaSelect = scr_System_SceneManager.current.LoadCanvasIntoScene(prefab_menuCanvas_CharaSelect, this.GetComponent<RectTransform>()).GetComponent<scr_Menu_CharaSelect>();
        charaSelect.InitializeWithArguments(this, a, filterByOrigin);
    }


    class ButtonValidator_selectCampaign : ButtonValidator, I_ButtonClickable
    {
        protected new scr_MenuCanvas_NewGame parent;
        bool right = true;
        public ButtonValidator_selectCampaign(scr_Menu parent, bool right = true) : base(parent)
        {
            this.parent = parent as scr_MenuCanvas_NewGame;
            this.right = right;
            // this.buttonID = buttonID;
            //this.text = targetText.GetComponent<TMP_Text>();
        }

        public override bool IsButtonValid()
        {
            if (right) return scr_System_Serializer.current.MasterList.CampaignSettings.GetItemAfter(parent.currentCampaign) != parent.currentCampaign;
            else return scr_System_Serializer.current.MasterList.CampaignSettings.GetItemBefore(parent.currentCampaign) != parent.currentCampaign;

        }

        public void OnClickButton()
        {
            if (right)
            {
                parent.SetCurrentCampaign(scr_System_Serializer.current.MasterList.CampaignSettings.GetItemAfter(parent.currentCampaign));
            }
            else
            {
                parent.SetCurrentCampaign(scr_System_Serializer.current.MasterList.CampaignSettings.GetItemBefore(parent.currentCampaign));
            }

        }
    }



    class ButtonValidator_selectCampaignOption : ButtonValidator, I_ButtonClickable
    {
        protected new scr_MenuCanvas_NewGame parent;
        bool right = true;
        private int buttonID;
        protected TMP_Text text;
        public ButtonValidator_selectCampaignOption(scr_Menu parent, bool right = true) : base(parent)
        {
            this.parent = parent as scr_MenuCanvas_NewGame;
            this.right = right;
        }

        public override bool IsButtonValid()
        {
            if (parent.currentCampaign_option == null) return false;

            if (right)
            {
                return parent.currentCampaign.GetNextOption(parent.currentCampaign_option) != parent.currentCampaign_option;
            }
            else
            {
                return parent.currentCampaign.GetPreviousOption(parent.currentCampaign_option)!= parent.currentCampaign_option;
            }
        }

        public void OnClickButton()
        {
            if (right)
            {
                parent.SetCurrentCampaignOption(parent.currentCampaign.GetNextOption(parent.currentCampaign_option));
            }
            else
            {
                parent.SetCurrentCampaignOption(parent.currentCampaign.GetPreviousOption(parent.currentCampaign_option));
            }
        }
    }

    class ButtonValidator_selectPlayer : ButtonValidator, I_ButtonClickable
    {
        protected new scr_MenuCanvas_NewGame parent;
        public ButtonValidator_selectPlayer(scr_Menu parent) : base(parent)
        {
            this.parent = parent as scr_MenuCanvas_NewGame;
        }

        public override bool IsButtonValid()
        {
            return true;
        }

        public void OnClickButton()
        {


            Action<Character_Trainable> act = delegate (Character_Trainable s) { parent.SetPlayerChar(s); };
            parent.OpenCharaSelect(act, parent.currentCampaign.requireOriginID);
        }
    }

    class ButtonValidator_selectPlayerCompanion : ButtonValidator, I_ButtonClickable
    {
        protected new scr_MenuCanvas_NewGame parent;
        public ButtonValidator_selectPlayerCompanion(scr_Menu parent) : base(parent)
        {
            this.parent = parent as scr_MenuCanvas_NewGame;
        }

        public override bool IsButtonValid()
        {
            if (parent.c == null) return false;
            if (parent.currentCampaign.ID == "campaign_chalicedivers") return false;
            else return parent.c.StartingGift.ID == "charOriginGift_companion";
        }

        public void OnClickButton()
        {
            
            Action<Character_Trainable> act = delegate (Character_Trainable s) { parent.SetComanionChar(s); };
            parent.OpenCharaSelect(act, parent.currentCampaign.requireOriginID);
        }
    }


    ButtonValidator_confirmCampaignStart GetButton_ConfirmCampaignStart { get { return button_confirmCampaignStart; } }
    ButtonValidator_confirmCampaignStart button_confirmCampaignStart;

    class ButtonValidator_confirmCampaignStart : ButtonValidator, I_ConflictCatcher
    {
        protected new scr_MenuCanvas_NewGame parent;
        private int id;
        public ButtonValidator_confirmCampaignStart(scr_Menu parent, int id) : base(parent)
        {
            this.parent = parent as scr_MenuCanvas_NewGame;
            this.id = id;
        }

        public override bool IsButtonValid()
        {

            if (parent.c == null)
            {
                AddTooltip("No Player Character Selection\n");
                this.state = ButtonValidator_States.Invalid;
                return false;
            }
            else if (this.tooltip == "" || this.tooltip.Length < 1 )
            {
                this.state = ButtonValidator_States.Valid;
                return true;
            }
            else
            {
                this.state = ButtonValidator_States.Invalid;
                return false;
            }
        }

        private void AddTooltip(string tooltip)
        {
            if (!this.tooltip.Contains(tooltip))
            {
                if (this.tooltip == "") this.tooltip += tooltip;
                else this.tooltip += "\n" + tooltip;
            }
        }



        public void NotifyConflict(string tooltip)
        {
            if (!this.tooltip.Contains(tooltip))
            {
                if (this.tooltip == "") this.tooltip += tooltip;
                else this.tooltip += "\n" + tooltip;
                parent.GetButtonByID(id).Validate();
            }

        }

        public void Reset()
        {
            this.state = ButtonValidator_States.Valid;
            tooltip = "";
        }


    }

}
