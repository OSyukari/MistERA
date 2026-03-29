using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class scr_menu_mealadditives : scr_Menu, IPointerClickHandler
{
    public Manageable faction;

    Dictionary<string, object> trackedRefs = new Dictionary<string, object>();

    public scr_HoverableText title;
    public CanvasGroup targetingRect;


    scr_add_instance _currentlyEditing = null;
    public scr_add_instance CurrentlyEditing 
    {
        get
        {
            return _currentlyEditing;
        }
        set
        {
            if (value == null)
            {
                _currentlyEditing = null;
                targetingRect.alpha = 0;
                targetingRect.interactable = false;
                targetingRect.blocksRaycasts = false;
            }
            else
            {
                _currentlyEditing = value;
                targetingRect.alpha = 1;
                targetingRect.interactable = true;
                targetingRect.blocksRaycasts = true;
            }
        }
    }


    public scr_SelectableText prefab_targetBTN;
    public void InitializeWithArgument(Manageable targetFaction)
    {
        if (!initialized) Initialize();

        this.faction = targetFaction;
        Utility.DestroyAllChildrenFrom(list_additives);

        title.SetText(LocalizeDictionary.QueryThenParse(title.replaceText).Replace("$faction$", targetFaction.FactionDisplayName));

        // for each existing additive command in faction meal manager, draw
        foreach(var itembase in scr_System_Serializer.current.MasterList.Items.List)
        {
            if (!itembase.Tags.Contains("additive")) continue;
            DrawAdditives(itembase);
        }

        foreach(var chara in targetFaction.ManagedChara)
        {
            var button = Instantiate(prefab_targetBTN);
            button.SelfRect.SetParent(list_targets, false);
            var buttonscript = button.GetComponent<scr_SelectableText>();
            RegisterButton(chara.RefID, buttonscript, new ButtonValidator_ToggleTargetRef(this, buttonscript, chara));
            button.SetText(chara.FirstName);
        }

        // for each meal in faction inv, add a notice on "what additive will be added"

        // for each valid additives in faction inventory, draw add button

        // merge existing additives ?
        CurrentlyEditing = null;
        // check if existing overlap. if overlap, forbid add button and write to tell player modify existing
        ValidateAll();
    }
    public int RegisterButton(int optionID, scr_SelectableText button, ButtonValidator validator, bool assertUnique = true)
    {
        if (assertUnique) optionID = AssertUniqueHash(optionID);
        if (!buttonsByID.ContainsKey(optionID))
        {
            button.Initialize(this, validator);
            button.optionID = optionID;
            buttonsByID.Add(button.optionID, button);
            validatorsByID.Add(button.optionID, button.Validator);
            // button.Validate();
            // return true;
        }
        // else return false;
        return optionID;
    }

    public void UnregisterButton(int optionID)
    {
        buttonsByID.Remove(optionID);
        validatorsByID.Remove(optionID);
    }


    protected override void Awake()
    {
        base.Awake();
        this.m_Canvas.overrideSorting = true;
        button_alwaysValid = new ButtonValidator_AlwaysTrue(this);
        faction = null;
    }
    public override void Initialize()
    {
        base.Initialize();

        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {
            // Debug.Log("Button " + button + " " + button.optionID);
            switch (button.optionID)
            {
                case 9980: // exit
                    button.Initialize(this, new ButtonValidator_ToggleTargetEnum(this, button, AdditiveEntry.AdditiveEntryType.All)); break;
                case 9981: // exit
                    button.Initialize(this, new ButtonValidator_ToggleTargetEnum(this, button, AdditiveEntry.AdditiveEntryType.AllIncludePlayer)); break;
                case 9982: // exit
                    button.Initialize(this, new ButtonValidator_ToggleTargetEnum(this, button, AdditiveEntry.AdditiveEntryType.Custom)); break;
                case 9989: // exit
                    button.Initialize(this, new ButtonValidator_FinishEditTarget(this)); break;
                case 9998: // exit
                    button.Initialize(this, new ButtonValidator_ActivateAll(this, button)); break;
                case 9999: // exit
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
                case 9999:
                    var nexthour = Math.Clamp(scr_System_Time.current.getCurrentTime().Hour + 1, 0, 23);
                    if (faction.isPlayerFaction && !faction.mealHours.Contains(nexthour)) faction.mealHours.Add(nexthour);
                    scr_System_SceneManager.current.UnloadLastCanvasFromScene(); break;
                default: break;
            }
        }
        ValidateAll();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        /*
        while (list_Jobs.transform.childCount > 0)
        {
            DestroyImmediate(list_Jobs.transform.GetChild(0).gameObject);
        }*/
        //Debug.LogError("CANVAS MANAGEMENT ONDESTROY");
        scr_System_CampaignManager.current.NotifyUpdate();

    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // if click outside box
        return;
        if ((eventData.rawPointerPress.GetComponent<scr_Canvas_Management>() != null) || (eventData.button == PointerEventData.InputButton.Right && UtilityEX.isClickBelowDragThreshold(eventData)))
        {
            scr_System_SceneManager.current.UnloadLastCanvasFromScene();
        }
    }

    public List<scr_prefab_additive> boxes = new List<scr_prefab_additive>();

    public void DrawAdditives(Item_Base item)
    {
        // instantiate prefab and write item
        var box = Instantiate(prefab_additive);
        box.selfRect.SetParent(list_additives, false);
        box.selfImage.color = scr_System_CentralControl.current.DisplaySetting.BackgroundColor_Transparent.Color;

        box.Load(this, faction, item);
        boxes.Add(box);
        
        // add button

        // in item's valid additives list add each possible meal that can be added

        // in additives list draw all active additives
    }

    public class ButtonValidator_ActivateAll : ButtonValidator, I_ButtonClickable
    {
        new scr_menu_mealadditives parent;
        scr_SelectableText button;
        public ButtonValidator_ActivateAll(scr_menu_mealadditives parent, scr_SelectableText button) : base(parent)
        {
            this.parent = parent;
            this.button = button;
        }

        public override bool IsButtonValid()
        {
            foreach(var box in parent.boxes)
            {
                foreach(var kvp in box.trackedButtons.Keys)
                {
                    return true;
                }
            }
            return false;
        }

        public void OnClickButton()
        {
            foreach (var box in parent.boxes)
            {
                foreach (var boxx in box.boxes)
                {
                    boxx.Entry.remainingTicks = 120;
                    boxx.RefreshData();
                }
            }
        }
    }
    public RectTransform list_additives;
    public RectTransform list_targets;
    public scr_prefab_additive prefab_additive;


    public class ButtonValidator_ToggleTargetEnum : ButtonValidator, I_ButtonClickable
    {

        new scr_menu_mealadditives parent;
        scr_SelectableText button; 
        AdditiveEntry.AdditiveEntryType type;
        public ButtonValidator_ToggleTargetEnum(scr_menu_mealadditives parent, scr_SelectableText button, AdditiveEntry.AdditiveEntryType type) : base(parent)
        {
            this.parent = parent;
            this.button = button;
            this.type = type;
        }

        public override bool IsButtonValid()
        {
            if (parent.CurrentlyEditing == null || parent.CurrentlyEditing.Entry == null) return false;
            if (!button.gameObject.activeInHierarchy) return false;
            button.Toggle(true, parent.CurrentlyEditing.Entry.targetingType == type);
            return true;
        }

        public void OnClickButton()
        {
            parent.CurrentlyEditing.Entry.targetingType = type;
            parent.CurrentlyEditing.RefreshData();
        }
    }


    public class ButtonValidator_ToggleTargetRef : ButtonValidator, I_ButtonClickable
    {

        new scr_menu_mealadditives parent;
        scr_SelectableText button;
        Character_Trainable c;
        public ButtonValidator_ToggleTargetRef(scr_menu_mealadditives parent, scr_SelectableText button, Character_Trainable c) : base(parent)
        {
            this.parent = parent;
            this.button = button;
            this.c = c;
        }

        public override bool IsButtonValid()
        {
            if (parent.CurrentlyEditing == null || parent.CurrentlyEditing.Entry == null) return false;
            if (!button.gameObject.activeInHierarchy) return false;
            var entry = parent.CurrentlyEditing.Entry;
            if (entry.targetingType == AdditiveEntry.AdditiveEntryType.AllIncludePlayer) button.Toggle(true, true);
            else if (entry.targetingType == AdditiveEntry.AdditiveEntryType.All) button.Toggle(true, scr_System_CampaignManager.current.Player != c);
            else button.Toggle(true,  parent.CurrentlyEditing.Entry.targetCharaRefs.Contains(c.RefID));
            return true;
        }

        public void OnClickButton()
        {
            bool remove = parent.CurrentlyEditing.Entry.targetCharaRefs.Contains(c.RefID);
            parent.CurrentlyEditing.Entry.targetingType = AdditiveEntry.AdditiveEntryType.Custom;
            if (remove) parent.CurrentlyEditing.Entry.targetCharaRefs.Remove(c.RefID);
            else parent.CurrentlyEditing.Entry.targetCharaRefs.Add(c.RefID);

            parent.CurrentlyEditing.RefreshData();
        }
    }

    public class ButtonValidator_FinishEditTarget : ButtonValidator, I_ButtonClickable
    {

        new scr_menu_mealadditives parent;
        public ButtonValidator_FinishEditTarget(scr_menu_mealadditives parent) : base(parent)
        {
            this.parent = parent;
        }

        public override bool IsButtonValid()
        {
            if (parent.CurrentlyEditing == null || parent.CurrentlyEditing.Entry == null) return false;
            return true;
        }

        public void OnClickButton()
        {
            parent.CurrentlyEditing = null;
        }
    }
}