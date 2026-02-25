using UnityEngine;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UnityEngine.UI;
using TMPro;
using UnityEngine.EventSystems;

public class scr_menu_question : scr_Menu
{

    public scr_HoverableText Text;
    public GridLayoutGroup Grid;

    public CanvasGroup SelfGroup;
    public Image SelfImage;

    float preferredLen = 0;


    public Message_Question InnerQuestion = null;

    scr_panel_logs logs;
    bool _active = true;
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
            }
        }
    }
    public void InitializeWithArgs(Canvas mainCanvas, EventInstance instance, Event.EventEntry.EventEntry_Question query, scr_panel_logs logs)
    {
        if (!initialized) Initialize();
        this.logs = logs;
        SetCanvas(mainCanvas, true);
        this.Text.SetText(UtilityEX.ParseEventEntry(instance, query.question));
        SelfImage.color = scr_System_CentralControl.current.DisplaySetting.BackgroundColor_Transparent.Color;
        foreach (var option in query.options)
        {
            var button = Instantiate(prefab_text_linkbutton).GetComponent<scr_SelectableText>();
            RegisterButton(option.GetHashCode(), button, new Button_OptionBtn(this, button, instance, option));
            preferredLen = Math.Max(preferredLen, button.GetComponent<TMP_Text>().preferredWidth);
            button.SetText(UtilityEX.ParseEventEntry(instance, option.option));
            if (option.isDefaultCancel && defaultCancel == null) defaultCancel = button.Validator as Button_OptionBtn;
        }
       // Debug.Log($"Initializing question menu, grid size {Grid.cellSize.ToString()} rectTransformSizedelta {self.sizeDelta} deltaX {self.sizeDelta.x} rectwidth {self.rect.width} gridflexwidth {Grid.flexibleWidth} rectlocalscale");
        //Grid.cellSize = new Vector2(Grid.cellSize.x, (float)Math.Min(self.rect.width * 0.9, preferredLen));
        ValidateAll();
        scr_UpdateHandler.current.InvokeEventStatus(EventStatus.waiting, true);
        

        if (defaultCancel != null && logs != null) logs.Observer_OnClick += OnClick;
    }

    public override void Initialize()
    {
        base.Initialize();
    }

    public override void ValidateAll()
    {
        base.ValidateAll();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (this.defaultCancel != null && logs != null) logs.Observer_OnClick -= OnClick;
    }

    protected void OnClick(PointerEventData pointer)
    {
        if (defaultCancel == null || logs == null) return;
        if (pointer.button != PointerEventData.InputButton.Right) return;
        if (!defaultCancel.IsButtonValid()) return;
        Notify(defaultCancel.button.optionID);
    }

    Button_OptionBtn defaultCancel = null;

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

    private void RegisterButton(int optionID, scr_SelectableText button, ButtonValidator validator)
    {
        optionID = AssertUniqueHash(optionID);
        button.transform.SetParent(this.Grid.transform, false);

        button.Initialize(this, validator);
        button.optionID = optionID;
        buttonsByID.Add(button.optionID, button);
        validatorsByID.Add(button.optionID, button.Validator);
        button.Validate();
    }

    protected override void Awake()
    {
        base.Awake();
        //this.sourceFaction = null;
        button_alwaysValid = new ButtonValidator_AlwaysTrue(this);
    }

    public class Button_OptionBtn : ButtonValidator, I_ButtonClickable
    {
        new scr_menu_question parent;
        public scr_SelectableText button;
        Event.EventEntry.EventEntry_Question.Options option;
        EventInstance instance;
        bool selected = false;
        /// <summary>
        /// Attach a custom validator, isbuttonvalid check validator, onclick apply validator
        /// </summary>
        /// <param name="parent"></param>
        public Button_OptionBtn(scr_menu_question parent, scr_SelectableText button, EventInstance instance, Event.EventEntry.EventEntry_Question.Options option) :base(parent)
        {
            this.parent = parent;
            this.button = button;
            this.instance = instance;
            this.option = option;

            var tooltip1 = option.option + "_tooltip";
            var tooltip2 = LocalizeDictionary.QueryThenParse(tooltip1, tooltip1);
            if (tooltip2 != tooltip1) this.tooltip += tooltip2;

            if (option.tooltip != "")
            {
                if (instance.AppendStrings.ContainsKey(option.tooltip)) tooltip += (this.tooltip.Length > 0 ? "\n" : "") + String.Join("\n", instance.AppendStrings[option.tooltip]);
                else tooltip += (this.tooltip.Length > 0 ? "\n" : "") + option.tooltip;
            }

            if (option.isDefaultCancel) this.tooltip += (this.tooltip.Length > 0 ? "\n":"") + LocalizeDictionary.QueryThenParse("event_isDefaultCancel_tooltip");
            this.tooltip = UtilityEX.ParseEventEntry(instance, this.tooltip);
        }

        public override bool IsButtonValid()
        {
            return selected || (parent.Active && EventUtility.isValid( option,instance));
        }

        public void OnClickButton()
        {
            if (!selected)
            {
                parent.Active = false;
                selected = true;

                EventUtility.Execute(instance, option, true);// option.Execute(instance, true);
            }
        }
    }
}

