using UnityEngine;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;
using System.Linq;

public class scr_menu_LLMQuery : scr_Menu
{

    public TMP_Text messageText;

    public CanvasGroup SelfGroup;
    public Image SelfImage;

    public TMP_Text responseText;

    public bool _active = true;

    public bool Active 
    { 
        get
        {
            return _active;
        }
        set
        {
            _active = value;
            if (!_active)
            {
                SelfImage.color = scr_System_CentralControl.current.DisplaySetting.TextColor_transparent;
                SelfGroup.blocksRaycasts = false;
                SelfGroup.interactable = false;
                scr_UpdateHandler.current.Observer_LLMResponse -= OnResponse;
            }
        }
    }
    public LLMRequest request;
    public void InitializeWithArgs(Canvas mainCanvas, LLMRequest request, scr_panel_logs logs)
    {
        if (!initialized) Initialize();
        this.request = request;
        SetCanvas(mainCanvas, true);
        this.messageText.text = request.currentString;
        SelfImage.color = scr_System_CentralControl.current.DisplaySetting.BackgroundColor_Transparent.Color;

       // Debug.Log($"Initializing question menu, grid size {Grid.cellSize.ToString()} rectTransformSizedelta {self.sizeDelta} deltaX {self.sizeDelta.x} rectwidth {self.rect.width} gridflexwidth {Grid.flexibleWidth} rectlocalscale");
        //Grid.cellSize = new Vector2(Grid.cellSize.x, (float)Math.Min(self.rect.width * 0.9, preferredLen));
        ValidateAll();

        scr_UpdateHandler.current.Observer_LLMResponse += OnResponse;
        scr_UpdateHandler.current.Observer_LLMStatus += OnUpdate;



    }

    void OnUpdate(LLMStatus status)
    {
        ValidateAll();
    }

    void OnResponse(LLMResponse response)
    {
        Debug.Log("OnResponse!");
        this.currentList.Add(response);
        LoadResponse();
    }

    public override void Initialize()
    {
        base.Initialize();

        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {
            switch (button.optionID)
            {
                case -1: break;
                case 1000:
                    button.Initialize(this, new Button_LoadPrev(this, button));
                    break;
                case 1001:
                    button.Initialize(this, new Button_DiscardAll(this, button));
                    break;
                case 1002:
                    button.Initialize(this, new Button_Confirm(this, button));
                    break;
                case 1003:
                    button.Initialize(this, new Button_Regenerate(this, button));
                    break;
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
    }

    public override void ValidateAll()
    {
        base.ValidateAll();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
    }

    protected void OnClick(PointerEventData pointer)
    {

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


    protected override void Awake()
    {
        base.Awake();
        //this.sourceFaction = null;
        button_alwaysValid = new ButtonValidator_AlwaysTrue(this);
    }

    public class Button_Confirm : ButtonValidator, I_ButtonClickable
    {
        new scr_menu_LLMQuery parent;
        public scr_SelectableText button;
        public Button_Confirm(scr_menu_LLMQuery parent, scr_SelectableText button):base(parent)
        {
            this.parent = parent;
            this.button = button;
        }
        public override bool IsButtonValid()
        {
            if (scr_UpdateHandler.current.LLMStatus == LLMStatus.active) return false;
            return parent.Active && parent.HasValidResponse;
        }
        public void OnClickButton()
        {
            parent.ExecuteResponse();
        }
    }
    public bool HasValidResponse
    {
        get
        {
            return CurrentResponse != null;
        }
    }
    public void ExecuteResponse()
    {
        this.Active = false;
    }

    public void Regenerate()
    {
        scr_UpdateHandler.current.SendLLMRequest(this.request);
    }

    int currentIndex = -1;
    List<LLMResponse> currentList = new List<LLMResponse>();

    LLMResponse CurrentResponse = null;
    public bool HasNext
    {
        get
        {
            return currentIndex > -1 && currentList.Count > currentIndex + 1;
        }
    }
    public bool HasPrev
    {
        get
        {
            return currentIndex > -1;
        }
    }

    public void LoadResponse(bool prev = false)
    {
        if (currentList.Count < 1) return;
        currentIndex += (prev ? -1 : 1);
        if (currentIndex < 0) currentIndex = currentList.Count - 1;
        else if (currentIndex >= currentList.Count) currentIndex = 0;

        CurrentResponse = currentList[currentIndex];
        if (CurrentResponse.choices.Count > 0)
        {
            responseText.text = CurrentResponse.choices[0].message.content;
        }
        else
        {
            responseText.text = "error no message in response";
        }
    }


    public void CancelResponse()
    {
        scr_UpdateHandler.current.InterruptLLMRoutine();
    }

    private void OnEnable()
    {
       // ValidateAll();
    }

    public class Button_Regenerate : ButtonValidator, I_ButtonClickable
    {
        new scr_menu_LLMQuery parent;
        public scr_SelectableText button;
        public Button_Regenerate(scr_menu_LLMQuery parent, scr_SelectableText button) : base(parent)
        {
            this.parent = parent;
            this.button = button;
        }
        public override bool IsButtonValid()
        {
            Debug.Log("IsButtonValid");
            if (!parent.Active) return false;
            if (scr_UpdateHandler.current.LLMStatus == LLMStatus.active) 
            {
                button.SetText("cancel update");
            }
            else if (parent.HasNext)
            {
                button.SetText("->");
            }
            else
            {
                button.SetText("generate");
            }
            return true;
        }
        public void OnClickButton()
        {
            if (scr_UpdateHandler.current.LLMStatus == LLMStatus.active) parent.CancelResponse();
            else if (parent.HasNext) parent.LoadResponse();
            else parent.Regenerate();
        }
    }


    public class Button_LoadPrev : ButtonValidator, I_ButtonClickable
    {
        new scr_menu_LLMQuery parent;
        public scr_SelectableText button;
        public Button_LoadPrev(scr_menu_LLMQuery parent, scr_SelectableText button) : base(parent)
        {
            this.parent = parent;
            this.button = button;
        }
        public override bool IsButtonValid()
        {
            if (!parent.Active) return false;
            if (scr_UpdateHandler.current.LLMStatus == LLMStatus.active) return false;
            if (parent.currentList.Count < 2) return false;
            return parent.HasPrev;
        }
        public void OnClickButton()
        {
            parent.LoadResponse(true);
        }
    }
    public class Button_DiscardAll : ButtonValidator, I_ButtonClickable
    {
        new scr_menu_LLMQuery parent;
        public scr_SelectableText button;
        public Button_DiscardAll(scr_menu_LLMQuery parent, scr_SelectableText button) : base(parent)
        {
            this.parent = parent;
            this.button = button;
        }
        public override bool IsButtonValid()
        {
            if (!parent.Active) return false;
            if (scr_UpdateHandler.current.LLMStatus == LLMStatus.active) return false;
            return true;
        }
        public void OnClickButton()
        {
            parent.Active = false;
            scr_UpdateHandler.current.LLMStatus = LLMStatus.inactive;
            scr_System_CampaignManager.current.AddLog(-1, "LLM generation aborted, click again to return to main menu", true);
        }
    }
}

