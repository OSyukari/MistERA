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
    public void InitializeWithArgs(EventEntry.EventEntry_Question query)
    {
        this.Text.SetText(query.question);

        foreach (var option in query.options)
        {
            var button = Instantiate(prefab_text_linkbutton).GetComponent<scr_SelectableText>();
            RegisterButton(option.GetHashCode(), button, new Button_OptionBtn(this, button, option));
            preferredLen = Math.Max(preferredLen, button.GetComponent<TMP_Text>().preferredWidth);
        }
        Grid.cellSize = new Vector2(Grid.cellSize.x, (float)Math.Min(self.rect.width * 0.9, preferredLen));
    }

    public override void Initialize()
    {
        base.Initialize();

        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {
            // Debug.Log("Button " + button + " " + button.optionID);
            switch (button.optionID)
            {
                default:
                    button.Initialize(this, button_alwaysValid); break;
            }
            if (button.optionID != -1)
            {
                buttonsByID.Add(button.optionID, button);
                validatorsByID.Add(button.optionID, button.Validator);
            }

        }
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
        this.m_Canvas.overrideSorting = true;
        //this.sourceFaction = null;
        button_alwaysValid = new ButtonValidator_AlwaysTrue(this);
    }

    public class Button_OptionBtn : ButtonValidator, I_ButtonClickable
    {
        new scr_menu_question parent;
        scr_SelectableText button;
        EventEntry.EventEntry_Question.Options option;

        /// <summary>
        /// Attach a custom validator, isbuttonvalid check validator, onclick apply validator
        /// </summary>
        /// <param name="parent"></param>
        public Button_OptionBtn(scr_menu_question parent, scr_SelectableText button, EventEntry.EventEntry_Question.Options option) :base(parent)
        {
            this.parent = parent;
            this.button = button;
            this.option = option;

            this.button.SetText(option.option);
        }

        public override bool IsButtonValid()
        {
            return parent.Active && option.isValid();
        }

        public void OnClickButton()
        {
            parent.Active = false;
            option.Execute();
        }
    }
}

