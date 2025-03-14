using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using TMPro;
using UnityEngine.EventSystems;
using System;
using Newtonsoft.Json;

public class scr_Canvas_Helper : scr_Menu, IPointerClickHandler
{
    public scr_HoverableText textArea;
    public scr_SelectableText prefab_notifierText;
    public RectTransform entryList;

    protected override void Awake()
    {
        base.Awake();
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
                case -9999: // exit
                    button.Initialize(this, button_alwaysValid); break;
                default: break;
            }
            if (button.optionID != -1)
            {
                buttonsByID.Add(button.optionID, button);
                validatorsByID.Add(button.optionID, button.Validator);
            }

        }

        BuildAllEntries();
        ValidateAll();
    }

    public List<string> HelperEntries;
    protected void BuildAllEntries()
    {
        for(int i = 0; i < HelperEntries.Count; i++)
        {
            var button = Instantiate(prefab_notifierText);
            button.optionID = i;
            button.Initialize(this, new ButtonValidator_HelperEntry(this, button));
            button.SetText(scr_System_Serializer.current.Dictionary.QueryThenParse(HelperEntries[i]));
            button.GetComponent<scr_PointerEnterNotifier>().Initialize(this, button.optionID);
            button.transform.SetParent(entryList, false);

            buttonsByID.Add(button.optionID, button);
            validatorsByID.Add(button.optionID, button.Validator);
        }
    }
    public void LoadHelperText(int i)
    {
        textArea.SetText(scr_System_Serializer.current.Dictionary.QueryThenParse(HelperEntries[i] + "_content"));
    }
    public void OnPointerClick(PointerEventData eventData)
    {
        // if click outside box
        if (eventData.rawPointerPress.GetComponent<scr_Canvas_Helper>() != null) scr_System_SceneManager.current.UnloadLastCanvasFromScene();
        // inside box
        else if (eventData.button == PointerEventData.InputButton.Right && Utility.isClickBelowDragThreshold(eventData)) scr_System_SceneManager.current.UnloadLastCanvasFromScene();
    }

    public int CurrentHelperEntryIndex = -1;

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
                case -9999: scr_System_SceneManager.current.UnloadLastCanvasFromScene(); break;
                default: break;
            }
        }
        ValidateAll();
    }

    protected class ButtonValidator_HelperEntry : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Helper parent;
        scr_SelectableText text;
        public ButtonValidator_HelperEntry(scr_Canvas_Helper parent, scr_SelectableText text) : base(parent)
        {
            this.parent = parent;
            this.text = text;
        }

        public override bool IsButtonValid()
        {
            if (parent.CurrentHelperEntryIndex == text.optionID) text.Toggle(true, true);
            else text.Toggle(true, false);

            return true;
        }

        public void OnClickButton()
        {
            this.text.Toggle(true, true);
            parent.CurrentHelperEntryIndex = text.optionID;
            parent.LoadHelperText(text.optionID);
        }
    }
}
