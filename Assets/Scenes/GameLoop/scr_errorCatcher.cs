using UnityEngine;
using UnityEngine.EventSystems;
using System;
using TMPro;

public class scr_errorCatcher : scr_Menu
{

    public TMP_Text errorText;
    public CanvasGroup selfGroup;
    public scr_SelectableText button_copy;

    protected override void Start()
    {
        Application.logMessageReceived += MessageReceive;
    }


    protected override void Awake()
    {
        base.Awake();
        this.m_Canvas.overrideSorting = true;
    }

    public override void Initialize()
    {
        base.Initialize();
        button_alwaysValid = new ButtonValidator_AlwaysTrue(this);

        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {
            // Debug.Log("Button " + button + " " + button.optionID);
            switch (button.optionID)
            {
                case 9998: // copy log
                    button.Initialize(this, button_alwaysValid); break;
                case 9999: // exit program
                    button.Initialize(this, button_alwaysValid); break;
                default: break;
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
                case 9998:
                    GUIUtility.systemCopyBuffer = text;
                    button_copy.SetText(LocalizeDictionary.QueryThenParse("ui_fatalError_copy_over"));
                    break;
                case 9999:
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
                    break;
                default: break;
            }
        }
        ValidateAll();
    }


    protected override void OnDestroy()
    {
        base.OnDestroy();
    }

    protected void MessageReceive(string logstring, string stacktrace, LogType type)
    {
        //Debug.Log($"MessageReceive {type}");
        if (type == LogType.Exception)
        {
            text = stacktrace;
            errorText.text = $"<color=\"red\">{stacktrace}</color>";
            Active = true;
        }
    }

    protected string text = "";

    bool _active = false;
    protected bool Active
    {
        get
        {
            return _active;
        }
        set
        {
            if (!_active && value)
            {
                _active = true;
                selfGroup.alpha = 1;
                selfGroup.interactable = true;
                selfGroup.blocksRaycasts = true;
                ValidateAll();
            }
        }
    }

}
