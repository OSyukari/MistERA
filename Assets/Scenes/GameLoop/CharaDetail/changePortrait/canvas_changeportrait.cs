using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class scr_canvas_changeportrait : scr_Menu, IPointerClickHandler
{

    public RectTransform portraitList;
    public scr_HoverableText title;

    int chara_refID = -1;
    Character_Trainable _chara;
    public Character_Trainable chara { get
        {
            if (chara_refID < 0) return null;
            else if (_chara == null )
            {
                _chara = scr_System_CampaignManager.current.FindInstanceByID(chara_refID);
            }
            return _chara;
        } set
        {
            _chara = value;
            chara_refID = _chara.RefID;
        }
    }

    public Action onDestroyCallback = null;

    public void InitializeWithArgument(int refID, Action ondestroy )
    {
        onDestroyCallback = ondestroy;

        chara_refID = refID;
        CurrentTemplate = chara == null ? null : chara.PortraitManager.portraitTemplate;
        _originalTemplate = chara == null ? null : chara.PortraitManager.portraitTemplate;

        if (!initialized) Initialize();
        //pictureRect.onValueChanged.AddListener(OnPictureRectChange);

        title.SetText(LocalizeDictionary.QueryThenParse("charaPortrait_change_title").Replace("$name$", chara.FullName));

        foreach (var template in scr_System_Serializer.current.MasterList.Character_Bases.baseCharacters)
        {
            if (template.Portrait == null || template.Portrait.portraitPriorityList.Count < 1) continue;
            //if (template.full)
            int hash = AssertUniqueHash(template.GetHashCode());

            if (!ButtonsByID.ContainsKey(hash))
            {
                var rect = Instantiate(prefab_text_linkbutton);
                var box = rect.GetComponent<scr_SelectableText>();
                box.transform.SetParent(portraitList, false);
                box.Initialize(this, new ButtonValidator_SelectPortrait(this, box, template));
                box.optionID = hash;
                box.isButtonToggle = true;
                box.SetText($"{template.FullName} {template.baseID}");

                buttonsByID.Add(box.optionID, box);
                validatorsByID.Add(box.optionID, box.Validator);
            }
        }

        ValidateAll();
    }

    protected override void Awake()
    {
        base.Awake();
        this.m_Canvas.overrideSorting = true;
        button_alwaysValid = new ButtonValidator_AlwaysTrue(this);
        selfRect = GetComponent<RectTransform>();
    }

    public override void Initialize()
    {
        base.Initialize();

        bool safe = scr_System_CentralControl.current.isSafeMode;

        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {
           // Debug.Log("Button " + button + " " + button.optionID);
            switch (button.optionID)
            {
                case -9999: // confirm exit
                    button.Initialize(this, button_alwaysValid); break;
                case 9999: // abort exit
                    button.Initialize(this, button_alwaysValid); break;
                default:
                    button.Initialize(this, button_alwaysValid); break;
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
                case -9999: // confirm exit
                    chara.PortraitManager.SetTemplate(CurrentTemplate);
                    onDestroyCallback?.Invoke();
                    scr_System_SceneManager.current.UnloadLastCanvasFromScene();
                    break;
                case 9999: // abort exit
                    onDestroyCallback?.Invoke();
                    scr_System_SceneManager.current.UnloadLastCanvasFromScene();
                    break;
                default: break;
            }
        }
        ValidateAll();
    }


   // public TextMeshProUGUI picture_AA;
    RectTransform selfRect;

    protected override void OnDestroy()
    {
        //scr_System_CampaignManager.current.CurrentTargetEX = null;
        base.OnDestroy();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        bool unload = false;
        // if click outside box
        if (eventData.rawPointerPress.GetComponent<scr_Menu_CharaDetail>() != null) unload = true;
        // inside box
        else if (eventData.button == PointerEventData.InputButton.Right && UtilityEX.isClickBelowDragThreshold(eventData)) unload = true;
        //Debug.Log("scr_Menu_CharaDetail: OnPointerClick! Data["+eventData.pointerPress+"] rawData["+ eventData.rawPointerPress + "]");

        if (unload)
        {

            scr_System_SceneManager.current.UnloadLastCanvasFromScene();
        }
    }

    Character_SerializableBase _originalTemplate = null;
    Character_SerializableBase _currentTemplate = null;
    public Character_SerializableBase CurrentTemplate
    {
        get
        {
            return _currentTemplate == null ? _originalTemplate : _currentTemplate;
        }
        set
        {
            _currentTemplate = value;
            // update
            if (scr_System_CampaignManager.current.CurrentTargetEX_Box != null) scr_System_CampaignManager.current.CurrentTargetEX_Box.InitializeWithArgument(_currentTemplate == null ? _originalTemplate : _currentTemplate);
        }
    }



    public class ButtonValidator_SelectPortrait : ButtonValidator, I_ButtonClickable
    {
        new scr_canvas_changeportrait parent;
        scr_SelectableText text;
        Character_SerializableBase template;
        public ButtonValidator_SelectPortrait(scr_canvas_changeportrait parent, scr_SelectableText text, Character_SerializableBase template) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.template = template;
        }

        public override bool IsButtonValid()
        {
            if (this.template == null)
            {
                return false;

            }else if (this.template.Portrait == null || this.template.Portrait.portraitPriorityList.Count < 1)
            {
                return false;
            }
            text.Toggle(true, parent.CurrentTemplate == this.template);
            return true;
        }

        public void OnClickButton()
        {
            if (parent.CurrentTemplate == this.template) parent.CurrentTemplate = null;
            else parent.CurrentTemplate = this.template;
        }
    }

    public override void ValidateAll()
    {
        base.ValidateAll();
    }
}
