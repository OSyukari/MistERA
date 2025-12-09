using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

public class scr_Menu_CharaSelect : scr_Menu
{

    public RectTransform prefab_Canvas_CharacterEditor;
    protected scr_Canvas_CharacterEditor charEditor;


    Dictionary<Character_SerializableBase, scr_presetSingle> dictionary_presets = new Dictionary<Character_SerializableBase, scr_presetSingle>();

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

        foreach(var preset in scr_System_Serializer.current.MasterList.Character_Bases.baseCharacters)
        {
            if (preset.playable) BuildSinglePreset(preset);
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
                    button.Initialize(this, button_alwaysValid);
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

    public RectTransform panel_EditPreset;

    public void OpenCharacterEditor(Character_Trainable c = null)
    {
        //Debug.LogError("OPENCHARAEDITOR WITH dATA REMOVED");
        if (c == null) c = new Character_Trainable();// ScriptableObject.CreateInstance(typeof(Character_Trainable)) as Character_Trainable;
        charEditor = scr_System_SceneManager.current.LoadCanvasIntoScene(prefab_Canvas_CharacterEditor, this.GetComponent<RectTransform>()).GetComponent<scr_Canvas_CharacterEditor>();
        charEditor.InitializeWithArgument(c);
    }

    public scr_presetSingle prefab_presetSingle;

    private void BuildSinglePreset(Character_SerializableBase file)
    {
       // Debug.Log("BuildSinglePreset fileinfo [" + file + "]");

        if (buttonsByID == null || validatorsByID == null)
        {
            Debug.LogError("one of the lists are null");
        }

        if (dictionary_presets.ContainsKey(file)) return;

        var fab = Instantiate(prefab_presetSingle);
        dictionary_presets.Add(file, fab);

        fab.SelfRect.SetParent(panel_EditPreset, false);

        fab.name.optionID = AssertUniqueHash(fab.name.GetHashCode());
        fab.name.Initialize(this, new ButtonValidator_selectPreset(this, file));
        fab.name.SetText(file.baseID);
        buttonsByID.Add(fab.name.optionID, fab.name);
        validatorsByID.Add(fab.name.optionID, fab.name.Validator);


        fab.edit.optionID = AssertUniqueHash(fab.edit.GetHashCode());
        fab.edit.Initialize(this, new ButtonValidator_editPreset(this, file));
        fab.edit.SetText(LocalizeDictionary.QueryThenParse("ui_intro_charaSelect_edit"));
        buttonsByID.Add(fab.edit.optionID, fab.edit);
        validatorsByID.Add(fab.edit.optionID, fab.edit.Validator);

        fab.delete.optionID = AssertUniqueHash(fab.delete.GetHashCode());
        fab.delete.Initialize(this, new ButtonValidator_deletePreset(this, file));
        fab.delete.SetText(LocalizeDictionary.QueryThenParse("ui_intro_charaSelect_delete"));
        buttonsByID.Add(fab.delete.optionID, fab.delete);
        validatorsByID.Add(fab.delete.optionID, fab.delete.Validator);
    }

    //ContentSizeFitter fitter;
    //Vector3 worldPointInRectangle;

    public void DeleteChara(Character_SerializableBase baseID)
    {
        if (!dictionary_presets.ContainsKey(baseID)) return;
        var target = dictionary_presets[baseID];
        dictionary_presets.Remove(baseID);

        buttonsByID.Remove(target.name.optionID);
        validatorsByID.Remove(target.name.optionID);

        buttonsByID.Remove(target.edit.optionID);
        validatorsByID.Remove(target.edit.optionID);

        buttonsByID.Remove(target.delete.optionID);
        validatorsByID.Remove(target.delete.optionID);

        target.gameObject.SetActive(false);
        Destroy(target.gameObject);

        scr_System_Serializer.current.MasterList.Character_Bases.DeleteChara(baseID);

        File.Delete($"{scr_System_Serializer.PresetPath}/{baseID.baseID}");
    }


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
        new scr_Menu_CharaSelect parent;
        Character_SerializableBase filename;
        public ButtonValidator_selectPreset(scr_Menu parent, Character_SerializableBase filename) : base(parent)
        {
            this.parent = parent as scr_Menu_CharaSelect;
            this.filename = filename;
        }

       // public override bool Clickable { get { return true; } }

        public override bool IsButtonValid()
        {
            // check filter condition
            var c = scr_System_Serializer.current.MasterList.Character_Bases.GetChara(filename.baseID);
            if (c == null) return false;


            if (!parent.canSelectPreset) return false;
            
            bool returnVal = true;

            if (parent.filterByOrigin != "" && c.Origin.ID != parent.filterByOrigin) returnVal = false;
                
            return returnVal;

        }

        public void OnClickButton()
        {
            //Debug.Log("character selected : [" + filename + "] character ["+c.FirstName+"] parent fallback ");

            var c = scr_System_Serializer.current.MasterList.Character_Bases.GetChara(filename.baseID);
            if (c == null) return;

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
        new scr_Menu_CharaSelect parent;
        Character_SerializableBase filename;
        public ButtonValidator_editPreset(scr_Menu parent, Character_SerializableBase filename) : base(parent)
        {
            this.parent = parent as scr_Menu_CharaSelect;
            this.filename = filename;
        }

        public override bool IsButtonValid()
        {
            var c = scr_System_Serializer.current.MasterList.Character_Bases.GetChara(filename.baseID);
            return c != null;
        }

        public void OnClickButton()
        {
            //Debug.Log("Attempt deserealize JSON [" + filename + "]");
            // Character_Trainable c = scr_System_Serializer.current.LoadPresetJSON(filename);
            var c = scr_System_Serializer.current.MasterList.Character_Bases.GetChara(filename.baseID);
            if (c == null) return;
            parent.OpenCharacterEditor(c);
        }
    }

    public class ButtonValidator_deletePreset : ButtonValidator, I_ButtonClickable
    {
        // public override bool Clickable { get { return true; } }
        Character_SerializableBase filename;
        new scr_Menu_CharaSelect parent;
        public ButtonValidator_deletePreset(scr_Menu parent, Character_SerializableBase filename) : base(parent)
        {
            this.parent = parent as scr_Menu_CharaSelect;
            this.filename = filename;
        }

        public override bool IsButtonValid()
        {
            if (!File.Exists($"{scr_System_Serializer.PresetPath}/{filename.baseID}")) return false;
            if (filename.baseID == "Default Chara.json") return false;
            return true;
        }

        public void OnClickButton()
        {
            parent.DeleteChara(filename);
        }
    }

}
