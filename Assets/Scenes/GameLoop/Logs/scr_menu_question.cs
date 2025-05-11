using UnityEngine;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UnityEngine.UI;
using TMPro;

public class scr_menu_question : scr_Menu
{

    public scr_HoverableText Text;
    public GridLayoutGroup Grid;
    float preferredLen = 0;

    RectTransform self;


    public bool Active = true;
    public void InitializeWithArgs(Canvas mainCanvas, EventInstance instance, Event.EventEntry.EventEntry_Question query)
    {
        // Initialize();
        SetCanvas(mainCanvas, true);
        this.Text.SetText(query.question);

        foreach (var option in query.options)
        {
            var button = Instantiate(prefab_text_linkbutton).GetComponent<scr_SelectableText>();
            RegisterButton(option.GetHashCode(), button, new Button_OptionBtn(this, button, instance, option));
            preferredLen = Math.Max(preferredLen, button.GetComponent<TMP_Text>().preferredWidth);
            button.SetText(option.option);
        }
        Debug.Log($"Initializing question menu, grid size {Grid.cellSize.ToString()} rectsizedelta rectwidth sizedelta gridflexwidth{Grid.flexibleWidth} rectlocalscale");
        //Grid.cellSize = new Vector2(Grid.cellSize.x, (float)Math.Min(self.rect.width * 0.9, preferredLen));
        ValidateAll();
    }

    public override void Initialize()
    {
        base.Initialize();

        /*
        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {
            // Debug.Log("Button " + button + " " + button.optionID);
            switch (button.optionID)
            {
                default:  break;
            }
            if (button.optionID != -1)
            {
                buttonsByID.Add(button.optionID, button);
                validatorsByID.Add(button.optionID, button.Validator);
            }

        }*/
        // build all presetLis

    }

    public override void ValidateAll()
    {
        base.ValidateAll();
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
        scr_SelectableText button;
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
        }

        public override bool IsButtonValid()
        {
            return selected || (parent.Active && option.isValid());
        }

        public void OnClickButton()
        {
            if (!selected)
            {
                parent.Active = false;
                selected = true;
                option.Execute(instance);
            }
        }
    }
}

