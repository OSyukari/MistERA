using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.IO;
using System;
using System.Linq;

public abstract class scr_Menu : MonoBehaviour
{

    public List<Image> background_solid;
    public List<Image> background_transparent;

    public RectTransform prefab_text_standard;
    public RectTransform prefab_text_link;
    public RectTransform prefab_text_linkbutton;

    public RectTransform prefab_box_vertical;
    public RectTransform prefab_box_horizontal;

    public Canvas m_Canvas;
    protected Camera m_Camera;
    public delegate void Action();
    public Action onSelfExit = null;

    protected virtual void OnEnable()
    {
        if (!initialized) Initialize();
    }

    protected ButtonValidator_AlwaysTrue button_alwaysValid;
    [HideInInspector] public scr_Canvas_tooltipHandler tooltip = null;
    public abstract void Notify(int optionID);

    protected virtual void Start()
    {
        if (!initialized) Initialize();
    }

    // Attach all observers here and init data structure without validation
    protected virtual void Awake(){

        if (m_Canvas != null)
        {
            SetCanvas(m_Canvas, false);
        }

        tooltip = this.GetComponent<scr_Canvas_tooltipHandler>();

        this.buttonsByID = new Dictionary<int, scr_SelectableText>();
        validatorsByID = new Dictionary<int, ButtonValidator>();

        foreach(Image i in background_solid) i.color = scr_System_CentralControl.current.DisplaySetting.BackgroundColor_Opaque.Color;
        foreach (Image i in background_transparent) i.color = scr_System_CentralControl.current.DisplaySetting.BackgroundColor_Transparent.Color;

    }

    public void SetCanvas(Canvas c, bool overrideSorting)
    {
        if (c == null) return;

        this.m_Canvas = c;
        if (m_Canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            m_Camera = null;
        else
            m_Camera = Camera.main;

        this.m_Canvas.overrideSorting = overrideSorting;
    }

    /// <summary>
    /// IS THIS SAFE ?!!!!!!
    /// </summary>
    /// <param name="hash"></param>
    /// <returns></returns>
    protected int AssertUniqueHash(int hash)
    {
        int counter = 0;
        string s = "";
        if (hash == 0) hash += 1;
        while (counter < 100 && (buttonsByID.ContainsKey(hash) || validatorsByID.ContainsKey(hash)))
        {
            s += "hash collision on hash " + hash + ", rehashing into ";
            hash = (hash*2).GetHashCode();
            s += hash + "\n";
            counter++;
        }
        if (counter == 100) Debug.LogError(s);
        return hash;
    }


    /// <summary>
    /// Catch all button in sub-menu and attach validator by ID
    /// Auto called. If need manual initialization, move initialization code.
    /// </summary>
    public virtual void Initialize()
    {
        if (initialized)
        {
            Debug.LogError("scrMenu error uncaught repeat calls on Initialize()");
            return;
        }
        initialized = true;
    }

    protected bool initialized = false;

    protected Dictionary<int, scr_SelectableText> buttonsByID;
    protected Dictionary<int, scr_SelectableText> ButtonsByID { get { return buttonsByID; } }

    protected Dictionary<int, ButtonValidator> validatorsByID;
    protected Dictionary<int, ButtonValidator> ValidatorsByID { get { return validatorsByID; } }
    public virtual void ValidateAll()
    {
        if (ButtonsByID != null)
        {
            foreach (scr_SelectableText button in ButtonsByID.Values)
            {
                button.Validate();
            }

        }

    }

    public virtual void SendResponse(object o)
    {

    }



    protected virtual void OnDestroy()
    {
        if (onSelfExit != null) onSelfExit();
        Destroy(tooltip);
    }
    public scr_SelectableText GetButtonByID(int id)
    {
        return ButtonsByID[id];
    }



}

public class scr_Menu_Intro : scr_Menu
{

    public RectTransform prefab_Canvas_CharacterEditor;
    //protected scr_Canvas_CharacterEditor charEditor;

    public RectTransform prefab_menuCanvas_NewGame;
    //protected scr_MenuCanvas_NewGame newGame;

    public RectTransform prefab_menuCanvas_CharaSelect;
    //protected scr_Menu_CharaSelect charaSelect;

    public RectTransform prefab_menuCanvas_UserPrefs;

    Dictionary<string, int> dictionary_presetID_path;

    public scr_HoverableText version;
    protected override void Awake()
    {
        base.Awake();
        dictionary_presetID_path = new Dictionary<string, int>();
        button_alwaysValid = new ButtonValidator_AlwaysTrue(this);
    }
    public RectTransform prefab_Canvas_LoadSave;
    public scr_HoverableText currentLanguage;
    public override void Initialize()
    {
        base.Initialize();


        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {

            switch (button.optionID)
            {
                case 1:
                    button.Initialize(this, new ButtonValidator_Load(this, button));
                    break;
                case 2:
                    button.Initialize(this, new ButtonValidator_AlwaysTrue(this));
                    break;
                case 3:
                    button.Initialize(this, new ButtonValidator_AlwaysFalse(this));
                    break;
                case 5:
                    button.Initialize(this, new ButtonValidator_HoverMessage(this, button));break;
                case 10:
                    button.Initialize(this, new ButtonValidator_ToggleLanguage(this, button, true)); break;
                case 11:
                    button.Initialize(this, new ButtonValidator_ToggleLanguage(this, button, false)); break;
                case -1:break;
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
        // build all presetList


        ValidateAll();

    }

    public override void ValidateAll()
    {
        base.ValidateAll();

        if (Reload)
        {
            scr_System_SceneManager.current.ReloadScene(GlobalValues.IntroScene);
        }
    }


    protected override void Start()
    {
        base.Start();

        version.SetText(Application.version);

        var lang = LocalizeDictionary.QueryThenParse("language_is");
        var lang2 = LocalizeDictionary.QueryThenParse(scr_System_CentralControl.current.Language);
        Debug.Log($"query {lang} {lang2}");
        this.currentLanguage.SetText(lang.Replace("$key$", lang2));
    }
    public class ButtonValidator_Load : ButtonValidator, I_ButtonClickable
    {
        new scr_Menu_Intro parent;
        scr_HoverableText text;
        public ButtonValidator_Load(scr_Menu_Intro parent, scr_SelectableText text) : base(parent)
        {
            this.parent = parent;
            this.text = text.GetComponent<scr_HoverableText>();
            errorMSG = LocalizeDictionary.QueryThenParse("tooltip_cannotLoadSave");
        }
        string errorMSG;
        public override bool IsButtonValid()
        {
            this.tooltip = "";
            if (!Directory.Exists(scr_System_Serializer.SavePath))
            {
                this.tooltip = errorMSG;
                return false;
            }
            else
            {
                return true;
            }
        }

        public void OnClickButton()
        {
            scr_System_SceneManager.current.LoadCanvasIntoScene(parent.prefab_Canvas_LoadSave, parent.m_Canvas.GetComponent<RectTransform>());
        }
    }

    public void OpenCharaSelect()
    {
        scr_System_SceneManager.current.LoadCanvasIntoScene(prefab_menuCanvas_CharaSelect, this.GetComponent<RectTransform>()).GetComponent<scr_Menu_CharaSelect>();
    }

    public void OpenNewGame()
    {
        scr_System_SceneManager.current.LoadCanvasIntoScene(prefab_menuCanvas_NewGame, this.GetComponent<RectTransform>()).GetComponent<scr_MenuCanvas_NewGame>();
    }

    public void OpenUserPref()
    {
        scr_System_SceneManager.current.LoadCanvasIntoScene(prefab_menuCanvas_UserPrefs, this.GetComponent<RectTransform>()).GetComponent<scr_MenuCanvas_NewGame>();
    }

    //ContentSizeFitter fitter;
    //Vector3 worldPointInRectangle;

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
                case 0:
                    OpenNewGame();
                    break;
                case 1:
                    break;
                case 2:
                    OpenUserPref();
                    break;
                case 3: // Character Editor
                    //panel_EditPreset.gameObject.SetActive(true);  //setactive is disruptive
                    //RebuildPresetList();
                    OpenCharaSelect();                    
                    break;
                case 4: // Quit
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
                    break;
                default:
                    break;

            }
        }
        ValidateAll();
    }

    public class ButtonValidator_HasSaveFiletoLoad : ButtonValidator
    {
        public ButtonValidator_HasSaveFiletoLoad(scr_Menu parent) : base(parent)
        {
        }

        public override bool IsButtonValid()
        {
            bool hasSaveFile = false;
            if (hasSaveFile)
            {
                state = ButtonValidator_States.Valid;
                tooltip = "";
                return true;
            }
            else
            {
                //Debug.Log(Utility.getSaveRootPath());

                // D:/Unity/EraDivers/Assets
                state = ButtonValidator_States.Invalid;
                tooltip = LocalizeDictionary.QueryThenParse("tooltip_cannotLoadSave");
                return false;
            }
        }
    }

    public bool Reload = false;

    public class ButtonValidator_HoverMessage : ButtonValidator, I_ButtonClickable
    {
        new scr_Menu_Intro parent;
        scr_SelectableText button;
        string copied = LocalizeDictionary.QueryThenParse("intro_message_4");
        public ButtonValidator_HoverMessage(scr_Menu_Intro parent, scr_SelectableText button) : base(parent)
        {
            this.parent = parent;
            this.button = button;
            if (scr_System_CentralControl.current.isSafeMode) this.tooltip = LocalizeDictionary.QueryThenParse("intro_message_3_safe");
            else this.tooltip = LocalizeDictionary.QueryThenParse("intro_message_3").Replace("$link$", Utility.bugReport);
        }

        public override bool IsButtonValid()
        {
            return true;
        }

        public void OnClickButton()
        {
            if (!scr_System_CentralControl.current.isSafeMode)
            {
                GUIUtility.systemCopyBuffer = Utility.bugReport;
                button.SetText(copied);
            }
        }
    }

    class ButtonValidator_ToggleLanguage: ButtonValidator, I_ButtonClickable
    {
        new scr_Menu_Intro parent;
        scr_SelectableText button;
        bool toggleLeft;
        public ButtonValidator_ToggleLanguage(scr_Menu_Intro parent, scr_SelectableText button, bool toggleLeft = false) : base(parent)
        {
            this.parent = parent;
            this.button = button;
            this.toggleLeft = toggleLeft;
            keys = LocalizeDictionary.Instance.Index.Languages;
        }

        bool initialized = false;
        List<string> keys;

        public override bool IsButtonValid()
        {
            return keys.Count > 1;
        }

        public void OnClickButton()
        {
            string current = LocalizeDictionary.Instance.Index.cachedLang;
            int next = 0;
            if (toggleLeft)
            {
                next = keys.IndexOf(current) - 1;
                if (next < 0) next = keys.Count - 1;
            }
            else
            {
                next = keys.IndexOf(current) + 1;
                if (next >= keys.Count) next = 0;
            }

            scr_System_CentralControl.current.Language = keys[next];
            //centertext.SetText(titleText.Replace("$key$", LocalizeDictionary.QueryThenParse(scr_System_CentralControl.current.Language)));
            parent.Reload = true;

        }
    }
}
