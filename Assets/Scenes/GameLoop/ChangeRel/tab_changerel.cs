using System.Collections.Generic;
using UnityEngine;

public class tab_changerel : MonoBehaviour
{
    public CanvasGroup SelfCanvasGroup;

    public Dictionary<I_IsJobGiver, RelationshipType> rel_per_faction = null;
    public Dictionary<I_IsJobGiver, bool> rel_is_a = null;
    public RectTransform rect_bio, rect_social, rect_personal;
    public scr_HoverableText empty_bio, empty_social;
    public scr_box_relationship relBox, relFinal;
    public scr_HoverableText mood, stress, lust, attitude;
    public scr_SelectableText prefab_relButton;

    public void ChangeTab(scr_menu_changeRel menu)
    {

        menu.title.SetText(LocalizeDictionary.QueryThenParse("menu_changeRel_title")
            .Replace("$source$", menu.CurrentRel.Target.FirstName)
            .Replace("$target$", menu.CurrentTarget.FirstName));//

        if (initialize) return;
        initialize = true;

        RelationshipManager.Draw(menu.CurrentRel, relBox);
        RelationshipManager.DrawFinal(menu.CurrentRel, relFinal);

        if (menu.CurrentRel.Relationship_Bio != null)
        {
            var btn = Instantiate(prefab_relButton);
            btn.SelfRect.SetParent(rect_bio, false);
            var btnFull = new tab_changerel.Button_ProposeRelationship(menu, btn, menu.CurrentRel.Relationship_Bio, !menu.CurrentRel.isA_Bio, null, false);
            menu.RegisterBtn(btn, btnFull);
            btn.SetText(menu.CurrentRel.Relationship_Bio.GetDisplayName(menu.CurrentTarget, !menu.CurrentRel.isA_Bio));

            empty_bio.gameObject.SetActive(false);
        }
        else
        {
            empty_bio.gameObject.SetActive(true);
        }

        bool hasSocial = false;

        foreach (var socialKey in menu.CurrentRel.Relationship_Social_Keys)
        {
            if (menu.CurrentRel.tryGetSocialFaction(socialKey, out var rel, out var isa))
            {
                hasSocial = true;
                var btn = Instantiate(prefab_relButton);
                btn.SelfRect.SetParent(rect_social, false);
                var btnFull = new tab_changerel.Button_ProposeRelationship(menu, btn, rel, !isa, socialKey, false);
                menu.RegisterBtn(btn, btnFull);
                btn.SetText(
                    LocalizeDictionary.QueryThenParse("menu_changeRel_socialFactionWrapper")
                    .Replace("$faction$", socialKey.FactionDisplayName)
                    .Replace("$relname$", rel.GetDisplayName(menu.CurrentTarget, !isa)));
            }
        }
        empty_social.gameObject.SetActive(!hasSocial);

        foreach (var personal in scr_System_Serializer.current.MasterList.RelationshipTypes.list_personal)
        {
            if (personal.hide_when_safe && scr_System_CentralControl.current.isSafeMode) continue;
            if (personal.isEqualRelationship)
            {
                var btn = Instantiate(prefab_relButton);
                btn.SelfRect.SetParent(rect_personal, false);
                var btnFull = new tab_changerel.Button_ProposeRelationship(menu, btn, personal, false, null);
                menu.RegisterBtn(btn, btnFull);
                btn.SetText(personal.GetDisplayName(menu.CurrentTarget, false));
            }
            else
            {
                var btnA = Instantiate(prefab_relButton);
                btnA.SelfRect.SetParent(rect_personal, false);
                var btnFullA = new tab_changerel.Button_ProposeRelationship(menu, btnA, personal, false, null);
                menu.RegisterBtn(btnA, btnFullA);
                btnA.SetText(personal.GetDisplayName(menu.CurrentTarget, false));

                var btnB = Instantiate(prefab_relButton);
                btnB.SelfRect.SetParent(rect_personal, false);
                var btnFullB = new tab_changerel.Button_ProposeRelationship(menu, btnB, personal, true, null);
                menu.RegisterBtn(btnB, btnFullB);
                btnB.SetText(personal.GetDisplayName(menu.CurrentTarget, true));
            }
        }


        if (menu.CurrentTarget != null && menu.CurrentTarget.Stats.Mood != null) menu.CurrentTarget.Stats.Mood.Draw(mood);
        else mood.gameObject.SetActive(false);

        if (menu.CurrentTarget != null && menu.CurrentTarget.Stats.Stress != null) menu.CurrentTarget.Stats.Stress.Draw(stress);
        else stress.gameObject.SetActive(false);

        if (menu.CurrentTarget != null && menu.CurrentTarget.Stats.Lust != null) menu.CurrentTarget.Stats.Lust.Draw(lust);
        else lust.gameObject.SetActive(false);

        if (menu.CurrentRel != null) RelationshipManager.Draw_Attitude(menu.CurrentRel, attitude);
        else attitude.gameObject.SetActive(false);

    }


    bool initialize = false;


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
                scr_System_CampaignManager.current.RegisterSceneUnloadActionCallback(() => {

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
