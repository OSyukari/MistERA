using UnityEngine;
using System.Collections.Generic;
using System;
using TMPro;
using UnityEngine.Assertions.Must;

public class initScript_Expeditions : MonoBehaviour
{
    public enum PartyEditUI
    {
        Neutral,
        MembersEdit,
        ExpeditionEdit
    }
    
    PartyEditUI _currentMode = PartyEditUI.Neutral;

    public PartyEditUI CurrentMode
    { 
        get
        {
            return _currentMode;
        }
        set 
        { 
            _currentMode = value;
            bool _neutral = _currentMode == PartyEditUI.Neutral;
            bool _editM = _currentMode == PartyEditUI.MembersEdit;
            bool _editExp = _currentMode == PartyEditUI.ExpeditionEdit;
            if (canvas != null && canvas.Tab_Expeditions.gameObject.activeInHierarchy)
            {
                foreach (var i in ui_neutral) i.gameObject.SetActive(_neutral);
                foreach (var i in ui_editMembers) i.gameObject.SetActive(_editM);
                foreach (var i in ui_editExpeditions) i.gameObject.SetActive(_editExp);
            }
        }
    }

    public List<RectTransform> ui_neutral;
    public List<RectTransform> ui_editMembers;
    public List<RectTransform> ui_editExpeditions;

    public RectTransform teamList;

    public scr_partyBTN prefab_partyButton;
    public scr_SelectableText prefab_memberButton;
    Manageable_Party previous = null;
    public void Initialize(scr_Canvas_Management canvas, Manageable m, bool loadPrev = false)
    {
        this.canvas = canvas;

        canvas.UnloadButton(this.temporaryTeamIDs);
        Utility.DestroyAllChildrenFrom(teamList, 1);

        Manageable_Party first = loadPrev ? previous : null;
        foreach (var party in m.SubFactions)
        {
            if (first == null) first = party;
            var script = Instantiate(prefab_partyButton);
            script.SelfRect.SetParent(teamList, false);
            var names = new List<string>();
            foreach (var i in party.ManagedChara) names.Add(i.FirstName);
            script.partyMembers.SetText(String.Join(" ", names));
            canvas.MakeButton_Party(party, script.PartyButton);
            temporaryTeamIDs.Add(script.PartyButton.optionID);
        }
        if (previousFaction != m)
        {
            canvas.UnloadButton(this.temporaryCharaIDs);
            Utility.DestroyAllChildrenFrom(list_EditCharaInParty, 1);

            previousFaction = m;

            foreach (var chara in m.ManagedChara)
            {
                var script = Instantiate(prefab_memberButton);
                script.SelfRect.SetParent(list_EditCharaInParty, false);
                canvas.MakeButton_PartyMembers(chara, script);
                temporaryCharaIDs.Add(script.optionID);
            }

        }

        canvas.UnloadButton(this.temporaryExpIDs);
        Utility.DestroyAllChildrenFrom(List_AllExpeditions);
        foreach(var exp in Expeditions.ExpeditionEntry.list)
        {
            var script = Instantiate(prefab_partyButton);
            script.SelfRect.SetParent(this.List_AllExpeditions, false);
            script.partyMembers.SetText(String.Join(" ", exp.FeatureKeywords));
            canvas.MakeButton_Expedition(exp, script.PartyButton);
            temporaryExpIDs.Add(script.PartyButton.optionID);
        }

        if (first == null) CurrentMode = PartyEditUI.Neutral;
        else if (first != previous)
        {
            previous = first;
            CurrentMode = PartyEditUI.Neutral;
        }

        canvas.LoadParty(first);
    }

    public RectTransform List_AllExpeditions;

    List<int> temporaryTeamIDs = new List<int>();
    List<int> temporaryCharaIDs = new List<int>();
    List<int> temporaryExpIDs = new List<int>();

    public TMP_InputField teamNameButton;
    public TMP_Text teammates;
    public scr_HoverableText teamStatus;
    public scr_inputFieldLink prefab_inputfield;
    public RectTransform list_EditCharaInParty;

    public DateTime StaticTime = new DateTime(1990, 01, 01);

    Manageable previousFaction = null;
    scr_Canvas_Management canvas = null;
    Manageable_Party party;

    public scr_HoverableText grid_gear;
    public RectTransform grid_inventory, grid_inv_temp, text_inv_empty;

    public void Draw(Manageable_Party p)
    {
        if (party != p)
        {
            this.teamNameButton.DeactivateInputField();
            this.ExpeditionConfig.StartTime.DeactivateInputField();
            this.ExpeditionConfig.CooldownTime.DeactivateInputField();
        }
        party = p;
        previous = p;

        Utility.DestroyAllChildrenFrom(grid_inventory);
        Utility.DestroyAllChildrenFrom(grid_inv_temp);

        if (party == null)
        {
            teamNameButton.text = "-";
            teammates.SetText("-");
            teamNameButton.interactable = false;
            teamStatus.SetText("-");

            text_inv_empty.gameObject.SetActive(true); 
            grid_gear.gameObject.SetActive(false);
        }
        else
        {
            teamNameButton.interactable = true;
            teamNameButton.text = $"{p.FactionDisplayName}";// (p.FactionDisplayName);
            var names = new List<string>();
            foreach (var i in p.ManagedChara) names.Add(i.FirstName);
            teammates.SetText($"{String.Join(", ", names)}");
            string status_tooltip;
            teamStatus.SetText($"{p.GetAvailability(out status_tooltip)} {p.Job.status}");
            teamStatus.SetExternalTooltip($"{status_tooltip}\n{p.Job.statusTooltip}");

            OnEndEdit_StartTime();

            bool hasInv = false;
            if (p.Inventory != null)
            {
                foreach (var i in p.Inventory.Contents)
                {
                    hasInv = hasInv || true;
                    var v = Instantiate(canvas.prefab_text_link);
                    v.SetParent(grid_inventory);
                    var v2 = v.GetComponent<scr_HoverableText>();
                    v2.SetText(i.DisplayName);
                }
            }
            if (p.Room != null && p.Room.DisplayableFurnitures.Count > 0)
            {
                hasInv = true;
                grid_gear.gameObject.SetActive(true);
                grid_gear.SetText(p.Room.DisplayableFurnitureNames_withLink);
            }
            else grid_gear.gameObject.SetActive(false);
            
            if (p.TempInventory != null)
            {
                foreach (var i in p.TempInventory.Contents)
                {
                    hasInv = hasInv || true;
                    var v = Instantiate(canvas.prefab_text_link);
                    v.SetParent(grid_inv_temp);
                    var v2 = v.GetComponent<scr_HoverableText>();
                    v2.SetText(i.DisplayName);
                }
            }
            text_inv_empty.gameObject.SetActive(!hasInv);

            if (p.isActive)
            {
                ExpeditionConfig.gameObject.SetActive(false);
                list_ExpeditionMSG.gameObject.SetActive(true) ;

                Utility.DestroyAllChildrenFrom(list_ExpeditionMSG);
                foreach(var msg in p.Job.ExpeditionResults)
                {
                    bool first = true;
                    foreach(var i in msg.Value)
                    {
                        var v = Instantiate(prefab_memEntry);
                        v.SelfRect.SetParent(list_ExpeditionMSG, false);
                        v.memText.SetText(i.FullDescription);
                        v.memText.SetExternalTooltip(String.Join("\n",i.Tooltips));
                        v.timeStamp.text = first ? msg.Key.ToShortTimeString() : "";
                        first = false;
                    }
                }

                foreach(var ap in p.Job.ActivePackages)
                {
                    if (ap.Duration <= 0) continue;
                    if (ap.isTemporaryAP) continue;
                   // var app = ap as ActionPackage_Expedition;
                   // if (app == null) continue;
                    var v = Instantiate(prefab_memEntry);
                    v.SelfRect.SetParent(list_ExpeditionMSG, false);
                    v.memText.SetText(ap.DisplayName);
                    v.timeStamp.text = "";// LocalizeDictionary.QueryThenParse("exp_event_timestamp_ongoing");// first ? msg.Key.ToShortTimeString() : "";
                }
            }
            else
            {
                ExpeditionConfig.gameObject.SetActive(true);
                list_ExpeditionMSG.gameObject.SetActive(false);
            }
        }
    }
    public scr_memoryBox prefab_memEntry;
    public RectTransform list_ExpeditionMSG;
    public scr_ExpeditionConfig ExpeditionConfig;



    public void OnValueChanged_PartyName(string s)
    {
        Debug.Log("OnValueChanged");
        if (party == null) return;
        
        party.FactionDisplayName = teamNameButton.text;
        if (canvas != null) canvas.UpdatePartyNames();
    }

    public class ButtonValidator_AllowPassNightToggle : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Management parent;
        scr_SelectableText text;
        scr_HoverableText expDuration;
        string timeString;
        public ButtonValidator_AllowPassNightToggle(scr_Canvas_Management parent, scr_SelectableText text, scr_HoverableText expDuration) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.expDuration = expDuration;
            timeString = LocalizeDictionary.QueryThenParse("ui_management_expeditionConfig_duration");
        }
        public override bool IsButtonValid()
        {
            if (!text.gameObject.activeInHierarchy || parent.currentParty == null)
            {
                return false;
            }
            var booleanvalue = parent.currentParty.AllowPassNight;
            text.Toggle(true, booleanvalue);

            expDuration.SetText(timeString
                .Replace("$time$", $"{parent.currentParty.BaseDuration}")
                .Replace("$final$", $"{parent.currentParty.FinalDuration}"));

            return true;
        }
        public void OnClickButton()
        {
            var booleanvalue = parent.currentParty.AllowPassNight;
            parent.currentParty.AllowPassNight = !booleanvalue;
        }
    }

    public class ButtonValidator_RecurringToggle : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Management parent;
        scr_SelectableText text;
        TMP_InputField recurringCooldown;
        Action act;
        public ButtonValidator_RecurringToggle(scr_Canvas_Management parent, scr_SelectableText text, TMP_InputField recurringCooldown, Action RefeshText) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.recurringCooldown = recurringCooldown;
            this.act = RefeshText;
        }
        public override bool IsButtonValid()
        {
            //if (text == null) Debug.LogError("text null");
            if (!text.gameObject.activeInHierarchy || parent.currentParty == null) 
            {
                recurringCooldown.text = "-";
                return false;
            }
            var isRecurring = parent.currentParty.IsRecurring;
            text.Toggle(true, isRecurring);
            recurringCooldown.interactable = isRecurring;
            if (isRecurring) act?.Invoke();
            else  recurringCooldown.text = " - ";

            return true;
        }

        public void OnClickButton()
        {
            parent.currentParty.IsRecurring = !parent.currentParty.IsRecurring;
        }
    }

    public void OnValueChanged_Recurring()
    {
        if (canvas == null || canvas.currentParty == null) return;
        if (int.TryParse(ExpeditionConfig.CooldownTime.text, out var value))
        {
            canvas.currentParty.RecurringCooldown = value;
        }
    }
    public void OnSelect_Recurring()
    {
        if (canvas == null || canvas.currentParty == null) return;
        ExpeditionConfig.CooldownTime.text = $"{canvas.currentParty.RecurringCooldown}";
    }
    public void OnEndEdit_Recurring()
    {
        if (canvas == null || canvas.currentParty == null) return;
        ExpeditionConfig.CooldownTime.text = LocalizeDictionary.QueryThenParse("ui_management_expeditionConfig_recurringCooldown")
            .Replace("$count$", $"{canvas.currentParty.RecurringCooldown}");
    }

    public void OnValueChanged_StartTime()
    {
        if (canvas == null || canvas.currentParty == null) return;
        if (double.TryParse(ExpeditionConfig.StartTime.text, out var value))
        {
            canvas.currentParty.StartHour = TimeSpan.FromHours(value % 24).Hours;
        }
    }
    public void OnSelect_StartTime()
    {
        if (canvas == null || canvas.currentParty == null) return;
        ExpeditionConfig.StartTime.text = $"{canvas.currentParty.StartHour}";

    }
    public void OnEndEdit_StartTime()
    {
        if (canvas == null || canvas.currentParty == null) return;
        var currentExp = canvas.currentParty.Job.Expedition;
        if (currentExp != null && currentExp.HasStartHour)
        {
            ExpeditionConfig.StartTime.interactable = false;
            ExpeditionConfig.StartTime.text = LocalizeDictionary.QueryThenParse("ui_management_expeditionConfig_startTimeForced")
                .Replace("$time$", $"{StaticTime.AddHours( canvas.currentParty.FinalStartHour).ToShortTimeString()}");
        }
        else
        {
            ExpeditionConfig.StartTime.interactable = true;
            ExpeditionConfig.StartTime.text = LocalizeDictionary.QueryThenParse("ui_management_expeditionConfig_startTime")
            .Replace("$time$", $"{StaticTime.AddHours(canvas.currentParty.FinalStartHour).ToShortTimeString()}");
        }
    }

    public class ButtonValidator_StartExp : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Management parent;
        scr_SelectableText text;
        public ButtonValidator_StartExp(scr_Canvas_Management parent, scr_SelectableText text) : base(parent)
        {
            this.parent = parent;
            this.text = text;
        }
        public override bool IsButtonValid()
        {
            //if (text == null) Debug.LogError("text null");
            if (!text.gameObject.activeInHierarchy) return false;
            if (parent.currentParty == null)
            {
                this.text.SetText(LocalizeDictionary.QueryThenParse("ui_management_expeditions_setExpTarget_none"));
                return false;
            }
            if (!parent.currentParty.isActive)
            {
                this.text.SetText(LocalizeDictionary.QueryThenParse("ui_management_expeditions_startExp"));
                return parent.currentParty.CanStartExpedition;
            }
            else if (parent.currentParty.CanResolveExpedition)
            {
                this.text.SetText(LocalizeDictionary.QueryThenParse("ui_management_expeditions_resolveExp"));
                return true;
            }
            else
            {
                this.text.SetText(LocalizeDictionary.QueryThenParse("ui_management_expeditions_ongoingExp"));
                return false;
            }
        }

        public void OnClickButton()
        {
            parent.currentParty.StartExpedition();
            parent.LoadParty(parent.currentParty);// (charaRefID);
        }
    }
}
