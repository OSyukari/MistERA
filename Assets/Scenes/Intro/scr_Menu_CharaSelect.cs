using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.IO;
using System.Reflection;
using System;

public class scr_Menu_CharaSelect : scr_Menu
{

    public RectTransform prefab_Canvas_CharacterEditor;
    protected scr_Canvas_CharacterEditor charEditor;


    Dictionary<string, int> dictionary_presetID_path = new Dictionary<string, int>();
    private int presetID = 101;
    protected int PresetID
    {
        get
        {
            presetID += 1;
            return presetID - 1;
        }
    }

    bool canSelectPreset { get
        {
            if (responseCatcher != null && responseHandler != null) return true;
            else return false;
        } }

    public scr_MenuCanvas_NewGame responseCatcher;
    Action<Character_Trainable> responseHandler;

    string filterByOrigin = "";

    public void InitializeWithArguments(scr_MenuCanvas_NewGame responseCatcher, Action<Character_Trainable> responseHandler, string filterByOrigin = "")
    {
        if (!initialized) Initialize();
        //Debug.Log("Initialize Menu CharaSelect with arguments");
        this.responseCatcher = responseCatcher;
        this.responseHandler = responseHandler;

        this.filterByOrigin = filterByOrigin;


        ValidateAll();
        // Initialize()
    }

    public override void ValidateAll()
    {
        RefreshPresets();

        base.ValidateAll();
    }

    protected void RefreshPresets()
    {
        if (!initialized) Initialize();

       // Debug.Log("RefreshPresets! Listing All Validators [" + String.Join("|", validatorsByID.Keys) + "]");

        if (Directory.Exists(Utility.GetSavePath_Preset()))
        {
            DirectoryInfo d = new DirectoryInfo(Utility.GetSavePath_Preset());
            foreach (var file in d.GetFiles("*.json"))
            {
                BuildSinglePreset(file);
            }
        }
    }

    protected override void Awake()
    {
        base.Awake();
        button_alwaysValid = new ButtonValidator_AlwaysTrue(this);

    }

    public override void Initialize()
    {
        base.Initialize();

        // 

        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {

            switch (button.optionID)
            {
                case 99:
                    button.Initialize(this, button_alwaysValid);
                    break;
                case 100:   // new char button
                    button.Initialize(this, new ButtonValidator_AlwaysFalse(this));
                    break;
                default:
                    break;
            }
            if (button.optionID != -1)
            {
                buttonsByID.Add(button.optionID, button);
                validatorsByID.Add(button.optionID, button.Validator);
            }

        }

    }

    public RectTransform panel_EditPreset_list;
    public RectTransform panel_EditPreset_delete;
    public RectTransform panel_EditPreset_edit;

    public void OpenCharacterEditor(Character_Trainable c = null)
    {
        Debug.LogError("OPENCHARAEDITOR WITH dATA REMOVED");
        //if (c == null) c = ScriptableObject.CreateInstance(typeof(Character_Trainable)) as Character_Trainable;
        //charEditor = scr_System_SceneManager.current.LoadCanvasIntoScene(prefab_Canvas_CharacterEditor, this.GetComponent<RectTransform>()).GetComponent<scr_Canvas_CharacterEditor>();
        //charEditor.InitializeWithArgument(c);
    }

    private void BuildSinglePreset(FileInfo file)
    {
       // Debug.Log("BuildSinglePreset fileinfo [" + file + "]");
        if (dictionary_presetID_path.ContainsKey(file.Name))
        {
           // Debug.Log("already contain key");
        }
        else
        {
            if (buttonsByID == null || validatorsByID == null)
            {
                Debug.LogError("one of the lists are null");
            }

            int i = PresetID;
            dictionary_presetID_path.Add(file.Name, i);

            RectTransform text = Instantiate(prefab_text_linkbutton);
            text.SetParent(panel_EditPreset_list, false);
            scr_SelectableText button = text.GetComponent<scr_SelectableText>();
            button.optionID = i;
            button.Initialize(this, new ButtonValidator_selectPreset(this, file.Name));
            button.SetText(file.Name);

            buttonsByID.Add(button.optionID, button);
            validatorsByID.Add(button.optionID, button.Validator);

            //Debug.Log("ButtonCreated "+button.optionID+", isButton? " + (button.Validator is I_ButtonClickable) + " isButtonInDict? " + (validatorsByID[button.optionID] is I_ButtonClickable));
            //button.Validate();

            int j = PresetID;
            RectTransform text2 = Instantiate(prefab_text_linkbutton);
            text2.SetParent(panel_EditPreset_edit, false);
            scr_SelectableText button2 = text2.GetComponent<scr_SelectableText>();
            button2.optionID = j;
            button2.Initialize(this, new ButtonValidator_editPreset(this, file.Name));
            button2.SetText("Edit");

            buttonsByID.Add(button2.optionID, button2);
            validatorsByID.Add(button2.optionID, button2.Validator);
            //button2.Validate();

            int k = PresetID;
            RectTransform text3 = Instantiate(prefab_text_linkbutton);
            text3.SetParent(panel_EditPreset_delete, false);
            scr_SelectableText button3 = text3.GetComponent<scr_SelectableText>();
            button3.optionID = k;
            button3.Initialize(this, new ButtonValidator_deletePreset(this, file.Name));
            button3.SetText("Delete");

            buttonsByID.Add(button3.optionID, button3);
            validatorsByID.Add(button3.optionID, button3.Validator);
            //button3.Validate();

            //Debug.Log("Build complete");
        }
    }

    //ContentSizeFitter fitter;
    //Vector3 worldPointInRectangle;


    public override void Notify(int optionID)
    {

        //Debug.Log("Parent Notified ! [" + optionID + "]\nListing All Validators ["+String.Join("|",validatorsByID.Keys)+"]");
        ButtonValidator validator = validatorsByID[optionID];
        I_ButtonClickable button = validator as I_ButtonClickable;
        if (button != null)
        {
            button.OnClickButton();
        }
        else
        {
            //Debug.LogError("button is not clickable");
            switch (optionID)
            {
                case 99:    //cancel
                    scr_System_SceneManager.current.UnloadLastCanvasFromScene();
                    break;
                case 100:   // new char
                    OpenCharacterEditor();
                    break;
                default:
                    //button.OnClickButton();
                    break;

            }
        }

    }

    public class ButtonValidator_selectPreset : ButtonValidator, I_ButtonClickable
    {
        string filename;
        new scr_Menu_CharaSelect parent;
        Character_Trainable c;
        public ButtonValidator_selectPreset(scr_Menu parent, string filename) : base(parent)
        {
            this.parent = parent as scr_Menu_CharaSelect;
            this.filename = filename;
            c = scr_System_Serializer.current.LoadPresetJSON(filename);
        }

       // public override bool Clickable { get { return true; } }

        public override bool IsButtonValid()
        {
            // check filter condition
            if (parent.canSelectPreset)
            {
                bool returnVal = true;
                if (!(parent.filterByOrigin != "" && c.Origin.ID == parent.filterByOrigin)) returnVal = false;

                return returnVal;
            }
            else return false;
        }

        public void OnClickButton()
        {
            //Debug.Log("character selected : [" + filename + "] character ["+c.FirstName+"] parent fallback ");

            var ra = parent.responseCatcher as scr_MenuCanvas_NewGame;
            if (ra != null) parent.responseHandler(c);
            else
            {
                Debug.LogError("PARENT RESPONSECATCHER FAILED");
            }
            scr_System_SceneManager.current.UnloadLastCanvasFromScene();
        }
    }


    public class ButtonValidator_editPreset : ButtonValidator, I_ButtonClickable
    {
        //public override bool Clickable { get { return true; } }
        string filename;
        new scr_Menu_CharaSelect parent;
        public ButtonValidator_editPreset(scr_Menu parent, string filename) : base(parent)
        {
            this.parent = parent as scr_Menu_CharaSelect;
            this.filename = filename;
        }

        public override bool IsButtonValid()
        {
            return false;

            return File.Exists(Utility.GetSavePath_Preset() + filename);
        }

        public void OnClickButton()
        {
            Debug.Log("Attempt deserealize JSON [" + filename + "]");
            Character_Trainable c = scr_System_Serializer.current.LoadPresetJSON(filename);
            parent.OpenCharacterEditor(c);
        }
    }

    public class ButtonValidator_deletePreset : ButtonValidator, I_ButtonClickable
    {
       // public override bool Clickable { get { return true; } }
        string filename;
        new scr_Menu_CharaSelect parent;
        public ButtonValidator_deletePreset(scr_Menu parent, string filename) : base(parent)
        {
            this.parent = parent as scr_Menu_CharaSelect;
            this.filename = filename;
        }

        public override bool IsButtonValid()
        {
            return false;
            return File.Exists(Utility.GetSavePath_Preset() + filename);
        }

        public void OnClickButton()
        {
            File.Delete(Utility.GetSavePath_Preset() + filename);
        }
    }

}
