using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using System.IO;
using TMPro;
using UnityEngine.EventSystems;
using System;
using Newtonsoft.Json;

public class scr_Canvas_LoadSave : scr_Menu, IPointerClickHandler
{

    public scr_SaveRect prefab_saveRect;
    public RectTransform saveList;

    protected override void Awake()
    {
        base.Awake();

        button_alwaysValid = new ButtonValidator_AlwaysTrue(this);
    }

    private int idCounter = 1;
    protected int GetID 
    { get
        {
            idCounter++;
            return idCounter - 1;
        }
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
                default:break;
            }
            if (button.optionID != -1)
            {
                buttonsByID.Add(button.optionID, button);
                validatorsByID.Add(button.optionID, button.Validator);
            }

        }
        // build all presetLis
        idCounter = 1;
        BuildSaveButtons();
    }

    protected void BuildSaveButtons()
    {

        if (Directory.Exists(Utility.GetSavePath_Save()))
        {
            DirectoryInfo d = new DirectoryInfo(Utility.GetSavePath_Save());
            foreach (var file in d.GetFiles("*.json"))
            {
                BuildSingleButton(file);
            }
        }

        ValidateAll();
    }

    protected void BuildSingleButton(FileInfo file)
    {
        if (!file.Exists) return;

        scr_SaveRect box = Instantiate(prefab_saveRect);
        box.transform.SetParent(saveList, false);


        scr_SelectableText button1 = box.saveDescription;

        button1.optionID = GetID * 2;
        button1.Initialize(this, new ButtonValidator_LoadSave(this, file, button1));

        buttonsByID.Add(button1.optionID, button1);
        validatorsByID.Add(button1.optionID, button1.Validator);

        //Debug.Log("ButtonCreated "+button.optionID+", isButton? " + (button.Validator is I_ButtonClickable) + " isButtonInDict? " + (validatorsByID[button.optionID] is I_ButtonClickable));
        //button.Validate();

        scr_SelectableText button2 = box.saveDelete;

        button2.optionID = button1.optionID + 1;
        button2.Initialize(this, new ButtonValidator_DeleteSave(this, file, button2));

        buttonsByID.Add(button2.optionID, button2);
        validatorsByID.Add(button2.optionID, button2.Validator);
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

    public void OnPointerClick(PointerEventData eventData)
    {
        // if click outside box
        if (eventData.rawPointerPress.GetComponent<scr_Canvas_LoadSave>() != null) scr_System_SceneManager.current.UnloadLastCanvasFromScene();
        // inside box
        else if (eventData.button == PointerEventData.InputButton.Right && Utility.isClickBelowDragThreshold(eventData)) scr_System_SceneManager.current.UnloadLastCanvasFromScene();
    }

    protected class ButtonValidator_LoadSave : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_LoadSave parent;
        FileInfo file;
        scr_SelectableText text;

        SaveFileHolder s;
        public ButtonValidator_LoadSave(scr_Canvas_LoadSave parent, FileInfo file, scr_SelectableText text) : base(parent)
        {
            this.parent = parent;
            this.file = file;
            this.text = text;

            s = JsonConvert.DeserializeObject<SaveFileHolder>(File.ReadAllText(file.FullName), Utility.SerializerSettings);
            s.FilePath = file.FullName;
            this.text.SetText(s.SaveDescription.Replace("$filename$", file.Name));
        }



        public override bool IsButtonValid()
        {
            if (!File.Exists(file.FullName))
            {
                text.gameObject.SetActive(false);
                return false;
            }
            else
            {
                return true;
            }
        }

        public void OnClickButton()
        {
            // unload canvas
            scr_UpdateHandler.current.LoadSaveFile(s);
        }
    }
    protected class ButtonValidator_DeleteSave : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_LoadSave parent;
        FileInfo file;
        scr_SelectableText text;
        public ButtonValidator_DeleteSave(scr_Canvas_LoadSave parent, FileInfo file, scr_SelectableText text) : base(parent)
        {
            this.parent = parent;
            this.file = file;
            this.text = text;
        }

        public override bool IsButtonValid()
        {
            if (!File.Exists(file.FullName))
            {
                text.gameObject.SetActive(false);
                return false;
            }
            else
            {
                return true;
            }
        }

        public void OnClickButton()
        {
            File.Delete(file.FullName);
        }
    }
}
