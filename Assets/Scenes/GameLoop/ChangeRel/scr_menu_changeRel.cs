using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.EventSystems;

public class scr_menu_changeRel : scr_Menu, IPointerClickHandler
{
    public scr_HoverableText title;

    public Character_Trainable CurrentTarget = null;
    public Character_Relationship CurrentRel = null;
    public Dictionary<I_IsJobGiver, RelationshipType> rel_per_faction = null;
    public Dictionary<I_IsJobGiver, bool> rel_is_a = null;
    public List<I_IsJobGiver> priorityList = null;

    public RectTransform rect_bio, rect_social, rect_personal;
    public scr_HoverableText empty_bio, empty_social;

    public scr_box_relationship relBox, relFinal;

    public scr_HoverableText mood, stress, lust, attitude;

    public void InitializeWithArgument(Character_Trainable source, Character_Trainable target)
    {
        CurrentTarget = source;
        scr_System_CampaignManager.current.CurrentTargetEX = source;

        CurrentRel = source.Relationships.FindRelationshipWith(target);

        if (!initialized) Initialize();

        RelationshipManager.Draw(CurrentRel, relBox);
        RelationshipManager.DrawFinal(CurrentRel, relFinal);

        title.SetText(LocalizeDictionary.QueryThenParse("menu_changeRel_title")
            .Replace("$source$", source.FirstName)
            .Replace("$target$", target.FirstName));//

        if (CurrentRel.Relationship_Bio != null)
        {
            var btn = Instantiate(prefab_relButton);
            btn.SelfRect.SetParent(rect_bio, false);
            var btnFull = new Button_ProposeRelationship(this, btn, CurrentRel.Relationship_Bio, !CurrentRel.isA_Bio, null, false);
            RegisterBtn(btn, btnFull);
            btn.SetText(CurrentRel.Relationship_Bio.GetDisplayName(source, !CurrentRel.isA_Bio));

            empty_bio.gameObject.SetActive(false);
        }
        else
        {
            empty_bio.gameObject.SetActive(true);
        }

        bool hasSocial = false;

        foreach (var socialKey in CurrentRel.Relationship_Social_Keys)
        {
            if (CurrentRel.tryGetSocialFaction(socialKey, out var rel, out var isa))
            {
                hasSocial = true;
                var btn = Instantiate(prefab_relButton);
                btn.SelfRect.SetParent(rect_social, false);
                var btnFull = new Button_ProposeRelationship(this, btn, rel, !isa, socialKey, false);
                RegisterBtn(btn, btnFull);
                btn.SetText( 
                    LocalizeDictionary.QueryThenParse("menu_changeRel_socialFactionWrapper")
                    .Replace("$faction$", socialKey.FactionDisplayName )
                    .Replace("$relname$", rel.GetDisplayName(source, !isa)));
            }
        }
        empty_social.gameObject.SetActive(!hasSocial);

        foreach(var personal in scr_System_Serializer.current.MasterList.RelationshipTypes.list_personal)
        {
            if (personal.isEqualRelationship)
            {
                var btn = Instantiate(prefab_relButton);
                btn.SelfRect.SetParent(rect_personal, false);
                var btnFull = new Button_ProposeRelationship(this, btn, personal, false, null);
                RegisterBtn(btn, btnFull);
                btn.SetText(personal.GetDisplayName(source, false));
            }
            else
            {
                var btnA = Instantiate(prefab_relButton);
                btnA.SelfRect.SetParent(rect_personal, false);
                var btnFullA = new Button_ProposeRelationship(this, btnA, personal, false, null);
                RegisterBtn(btnA, btnFullA);
                btnA.SetText(personal.GetDisplayName(source, false));

                var btnB = Instantiate(prefab_relButton);
                btnB.SelfRect.SetParent(rect_personal, false);
                var btnFullB = new Button_ProposeRelationship(this, btnB, personal, true, null);
                RegisterBtn(btnB, btnFullB);
                btnB.SetText(personal.GetDisplayName(source, true));
            }
        }


        if (CurrentTarget != null && CurrentTarget.Stats.Mood != null) CurrentTarget.Stats.Mood.Draw(mood);
        else mood.gameObject.SetActive(false);

        if (CurrentTarget != null && CurrentTarget.Stats.Stress != null) CurrentTarget.Stats.Stress.Draw(stress);
        else stress.gameObject.SetActive(false);

        if (CurrentTarget != null && CurrentTarget.Stats.Lust != null) CurrentTarget.Stats.Lust.Draw(lust);
        else lust.gameObject.SetActive(false);
        
        if (CurrentRel != null)  RelationshipManager.Draw_Attitude(CurrentRel, attitude);
        else attitude.gameObject.SetActive(false);

        ValidateAll();
    }

    protected void RegisterBtn(scr_SelectableText button, ButtonValidator validator)
    {
        int optionID = AssertUniqueHash(button.GetHashCode());

        if (!buttonsByID.ContainsKey(optionID))
        {
            button.Initialize(this, validator);
            button.optionID = optionID;
            buttonsByID.Add(button.optionID, button);
            validatorsByID.Add(button.optionID, button.Validator);
        }
        else
        {
            Debug.LogError($"scr_menu_changeRel registerbtn hash collision on {optionID}");
        }
    }
    protected override void Awake()
    {
        base.Awake();
        this.m_Canvas.overrideSorting = true;
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
    public void OnPointerClick(PointerEventData eventData)
    {
        // if click outside box
        if ((eventData.rawPointerPress.GetComponent<scr_Canvas_Management>() != null) || (eventData.button == PointerEventData.InputButton.Right && UtilityEX.isClickBelowDragThreshold(eventData)))
        {
            scr_System_SceneManager.current.UnloadLastCanvasFromScene();
        }
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
                    scr_System_SceneManager.current.UnloadLastCanvasFromScene(); break;
                default: break;
            }
        }
        ValidateAll();
    }
    protected override void OnDestroy()
    {
        base.OnDestroy();
        scr_System_CampaignManager.current.NotifyUpdate();
    }

    public scr_SelectableText prefab_relButton;

    public class Button_ProposeRelationship : ButtonValidator, I_ButtonClickable
    {

        new scr_menu_changeRel parent;
        scr_SelectableText button;
        RelationshipType rel;
        bool validateB;
        I_IsJobGiver sourceFaction = null;
        bool isCurrent = false;
        bool canChange;
        Color32 alert;
        string tooltipCache = "";
        public Button_ProposeRelationship(scr_menu_changeRel parent, scr_SelectableText button, RelationshipType rel, bool validateB, I_IsJobGiver sourceFaction = null, bool canChange = true) : base(parent)
        {
            this.parent = parent;
            this.button = button;
            this.canChange = canChange;
            this.rel = rel;
            this.validateB = validateB;
            this.sourceFaction = sourceFaction;
            alert = scr_System_CentralControl.current.DisplaySetting.TextColor_conflict.Color;
            tooltipCache = rel.Tooltip;

        }

        public override bool IsButtonValid()
        {
            tooltip = tooltipCache;
            if (rel == null || parent.CurrentTarget == null || parent.CurrentRel == null) return false;
            isCurrent = false;
            if (parent.CurrentRel.HasRelationship(sourceFaction, rel, !validateB))
            {
                button.Toggle(true, true);
                tooltip += $"\n\n{LocalizeDictionary.QueryThenParse("menu_changeRel_tooltip_currentActive")}";
                isCurrent = true;
                return true;
            }
            if (!canChange)
            {
                tooltip += "\n\ncannot change!";
                return true;
            }
            if (!rel.allowPlayerProposition)
            {
                tooltip += $"\n\n{Utility.WrapTextColor(LocalizeDictionary.QueryThenParse("menu_changeRel_tooltip_forbidPlayer"), alert)}";
                return false;
            }
            if (parent.CurrentRel.RelationshipCooldown > 0)
            {
                tooltip += $"\n\n{Utility.WrapTextColor(LocalizeDictionary.QueryThenParse("menu_changeRel_tooltip_cooldown"), alert)}";
                return false;
            }
            if (parent.CurrentTarget.Stats.isConsciousnessUnconscious)
            {
                tooltip += $"\n\n{parent.CurrentTarget.FirstName} is unconscious";
                return false;
            }
            if (rel.isValid(parent.CurrentRel, validateB))
            {
                tooltip += $"\n\n{LocalizeDictionary.QueryThenParse("menu_changeRel_tooltip_satistisfied")}";
                return true;
            }
            else
            {
                tooltip += $"\n\n{Utility.WrapTextColor(LocalizeDictionary.QueryThenParse("menu_changeRel_tooltip_invalid"), alert)}";
                return false;
            }
        }

        public void OnClickButton()
        {
            if (isCurrent) return;
            if (!canChange) return;
            if (sourceFaction == null)
            {
                scr_System_CampaignManager.current.RegisterSceneUnloadActionCallback( () => {

                    scr_System_CampaignManager.current.ChangeCurrentViewMode(ViewMode.View_Logs);
                    parent.CurrentRel.SetPersonalRelationship(rel, !validateB, true, true);
                });
                //scr_System_CampaignManager.current.HideCanvasAnchor();
                //scr_System_CampaignManager.current.RegisterSceneUnloadActionCallback(
                    
                scr_System_SceneManager.current.UnloadLastCanvasFromScene();
            }
            
        }
    }
}
