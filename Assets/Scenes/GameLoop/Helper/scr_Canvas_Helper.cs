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
    public TMP_Text textAreaTMP;
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
    public List<string> HelperEntries_Era;
    protected void BuildAllEntries()
    {
        for(int i = 0; i < HelperEntries.Count; i++)
        {
            var button = Instantiate(prefab_notifierText);
            button.optionID = AssertUniqueHash(button.GetHashCode());
            button.Initialize(this, new ButtonValidator_HelperEntry(this, button, ref HelperEntries, i));
            button.SetText(LocalizeDictionary.QueryThenParse(HelperEntries[i]));
            button.GetComponent<scr_PointerEnterNotifier>().Initialize(this, button.optionID);
            button.transform.SetParent(entryList, false);

            buttonsByID.Add(button.optionID, button);
            validatorsByID.Add(button.optionID, button.Validator);

            if (this.CurrentHelperEntryIndex == null) this.CurrentHelperEntryIndex = button.Validator as I_ButtonClickable;
        }

        if (!scr_System_CentralControl.current.isSafeMode)
        {
            for (int i = 0; i < HelperEntries_Era.Count; i++)
            {
                var button = Instantiate(prefab_notifierText);
                button.optionID = AssertUniqueHash(button.GetHashCode());
                button.Initialize(this, new ButtonValidator_HelperEntry(this, button, ref HelperEntries_Era, i));
                button.SetText(LocalizeDictionary.QueryThenParse(HelperEntries_Era[i]));
                button.GetComponent<scr_PointerEnterNotifier>().Initialize(this, button.optionID);
                button.transform.SetParent(entryList, false);

                buttonsByID.Add(button.optionID, button);
                validatorsByID.Add(button.optionID, button.Validator);
            }

        }

    }

    protected override void Start()
    {
        base.Start();

        if (HelperEntries.Count > 0)
        {
            textAreaTMP.text = LocalizeDictionary.QueryThenParse(HelperEntries[0] + "_content");// this.CurrentHelperEntryIndex.OnClickButton();
        }

    }

    protected override void OnEnable()
    {
        if (!initialized) Initialize();
# if UNITY_EDITOR
        Debug.Log($"OnEnable load helper {(this.HelperEntries.Count > 0 ? HelperEntries[0] : "null")}");
#endif
        if (this.HelperEntries.Count > 0) this.LoadHelperText(HelperEntries[0]);
    }

    public I_ButtonClickable CurrentHelperEntryIndex = null;
    public void LoadHelperText(string list)
    {
        textArea.SetText(LocalizeDictionary.QueryThenParse(list + "_content"));
    }
    public void OnPointerClick(PointerEventData eventData)
    {
        // if click outside box
        if (eventData.rawPointerPress.GetComponent<scr_Canvas_Helper>() != null) scr_System_SceneManager.current.UnloadLastCanvasFromScene();
        // inside box
        else if (eventData.button == PointerEventData.InputButton.Right && Utility.isClickBelowDragThreshold(eventData)) scr_System_SceneManager.current.UnloadLastCanvasFromScene();
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
        List<string> list;
        int index;

        public ButtonValidator_HelperEntry(scr_Canvas_Helper parent, scr_SelectableText text, ref List<string> list, int index) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.index = index;
            this.list = list;
        }

        public override bool IsButtonValid()
        {
            if (parent.CurrentHelperEntryIndex == this) text.Toggle(true, true);
            else text.Toggle(true, false);

            return true;
        }

        public void OnClickButton()
        {
            this.text.Toggle(true, true);
            parent.CurrentHelperEntryIndex = this;
            parent.LoadHelperText(list[index]);
        }
    }
}
