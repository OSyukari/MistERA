using UnityEngine;
using System.Collections.Generic;
using System;
using TMPro;
using UnityEngine.Assertions.Must;
using System.Linq;

public class initScript_Expeditions : MonoBehaviour
{
    public scr_teamBTN prefab_teamButton;
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
           // Debug.Log("Setting CurrentMode");
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

    public RectTransform teamList, miaList;

    public scr_partyBTN prefab_partyButton;
    public scr_SelectableText prefab_memberButton;
    Manageable_Party previous = null;

    bool initialized = false;

    public void Initialize(scr_Canvas_Management canvas, Manageable m, bool loadPrev = false)
    {
        this.canvas = canvas;
        bool forceReinit = initialized;
        initialized = true;
        canvas.UnloadButton(this.temporaryTeamIDs);
        Utility.DestroyAllChildrenFrom(teamList, 2);
        Utility.DestroyAllChildrenFrom(miaList, 2);

        Manageable_Party first = loadPrev ? previous : null;
        foreach (var party in m.SubFactions)
        {
            if (first == null) first = party;
            var script = Instantiate(prefab_partyButton);
            script.SelfRect.SetParent(teamList, false);
            canvas.MakeButton_Party(party, script);
            temporaryTeamIDs.Add(script.PartyButton.optionID);
        }
        if (previousFaction != m)
        {
            canvas.UnloadButton(this.temporaryCharaIDs);
            Utility.DestroyAllChildrenFrom(list_EditCharaInParty, 1);

            previousFaction = m;

            foreach (var chara in m.ManagedChara)
            {
                if (m.isVisitor(chara.RefID) || m.isPrisoner(chara.RefID)) continue;
                if (chara == scr_System_CampaignManager.current.Player) continue;
                var box = Instantiate(prefab_teamButton);
                box.selfRect.SetParent(list_EditCharaInParty, false);

                var script = box.teammateButton;
                canvas.MakeButton_PartyMembers(chara, script);
                temporaryCharaIDs.Add(script.optionID);

                var script2 = box.btn_frontline;
                canvas.MakeButton_PartyMemberComp(chara, script2, Manageable_Party.PartyComposition.frontline);
                temporaryCharaIDs.Add(script2.optionID);

                var script3 = box.btn_support;
                canvas.MakeButton_PartyMemberComp(chara, script3, Manageable_Party.PartyComposition.backline);
                temporaryCharaIDs.Add(script3.optionID);
            }
        }

        bool hasKidnap = false;

        foreach (var party in m.KidnapFactions)
        {
            hasKidnap = true;
            var script = Instantiate(prefab_partyButton);
            script.SelfRect.SetParent(miaList, false);
            //var names = new List<string>();
            //foreach (var i in party.ManagedChara_Displayables) if (!i.isTemporaryActor) names.Add(i.CallName);
            //script.partyMembers.SetText(String.Join(" ", names));
            canvas.MakeButton_Party(party, script, true);
            //script.PartyButton.SetText(party.ExpeditionName);
            temporaryTeamIDs.Add(script.PartyButton.optionID);
        }

        text_MIA_None.gameObject.SetActive(!hasKidnap);

        canvas.UnloadButton(this.temporaryExpIDs);
        Utility.DestroyAllChildrenFrom(List_AllExpeditions);
        foreach(var exp in m.GetAllValidExpeditions())
        {
            var script = Instantiate(prefab_partyButton);
            script.SelfRect.SetParent(this.List_AllExpeditions, false);
            script.partyMembers.SetText(String.Join(" ", exp.FeatureKeywords));
            canvas.MakeButton_Expedition(exp, script.PartyButton, script);
            temporaryExpIDs.Add(script.PartyButton.optionID);
        }

        if (first == null) CurrentMode = PartyEditUI.Neutral;
        else if (first != previous)
        {
            previous = first;
            CurrentMode = PartyEditUI.Neutral;
        }

        canvas.LoadParty(first, false, forceReinit);
    }

    public RectTransform List_AllExpeditions;

    List<int> temporaryTeamIDs = new List<int>();
    List<int> temporaryCharaIDs = new List<int>();
    List<int> temporaryExpIDs = new List<int>();
    List<int> temporaryExpResolves = new List<int>();

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

    public scr_resolveEv prefab_resolveEventBtn;
    public scr_HoverableText text_MIA_None;

    protected void UpdateDisplay(bool isKidnap)
    {
        teamNameButton.interactable = scr_System_CampaignManager.current.DebugMode || !isKidnap;
        teamNameButton.text = $"{party.FactionDisplayName}";// (p.FactionDisplayName);
        var names = new List<string>();
        foreach (var i in party.ManagedChara_Displayables) if (!i.isTemporaryActor) names.Add(i.FirstName);
        teammates.SetText($"{String.Join(", ", names)}");

        //p.GetAvailability(out status_tooltip);
        var tooltip = $"{party.GetAvailability(out var status_tooltip)} {party.Job.status}";
        teamStatus.SetText(status_tooltip);
        teamStatus.SetExternalTooltip(tooltip);
    }


    public void Draw(Manageable_Party p, bool isKidnap, bool forceRefresh = false)
    {
        if (party != p || p == null)
        {
            this.teamNameButton.DeactivateInputField();
            this.ExpeditionConfig.StartTime.DeactivateInputField();
            this.ExpeditionConfig.CooldownTime.DeactivateInputField();
        }
        else if (!forceRefresh)
        {
            UpdateDisplay(isKidnap);
            return;
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

            ExpeditionConfig.gameObject.SetActive(false);
        }
        else
        {
            UpdateDisplay(isKidnap);


            ExpeditionConfig.gameObject.SetActive(!p.Job.isActive && !isKidnap);
            OnEndEdit_StartTime();

            bool hasInv = false;
            if (p.Inventory != null)
            {
                foreach (var i in p.Inventory.Contents)
                {
                    hasInv = hasInv || true;
                    var v = Instantiate(canvas.prefab_text_link);
                    v.SetParent(grid_inventory, false);
                    var v2 = v.GetComponent<scr_HoverableText>();
                    v2.SetText(i.Print());
                }
            }
            if (p.Room != null && p.Room.DisplayableFurnitures.Count > 0)
            {
                hasInv = true;
                grid_gear.gameObject.SetActive(true);
                grid_gear.SetText(p.Room.DisplayableFurnitureNames_withLink);
                if (scr_System_CampaignManager.current.DebugMode)
                {
                    var allvalidcoms = new List<string>();
                    foreach(var f in p.Room.Jobs)
                    {
                        allvalidcoms.AddRange(f.allusableCOMStrings);
                    }
                    allvalidcoms = allvalidcoms.Distinct().ToList();
                    grid_gear.SetExternalTooltip($"AllvalidCOMS:\n{String.Join("|", allvalidcoms)}");
                }
            }
            else grid_gear.gameObject.SetActive(false);

            /*
            if (p.Inventory != null)
            {
                foreach (var i in p.Inventory.Contents)
                {
                    hasInv = hasInv || true;
                    var v = Instantiate(canvas.prefab_text_link);
                    v.SetParent(grid_inv_temp);
                    var v2 = v.GetComponent<scr_HoverableText>();
                    v2.SetText(i.DisplayName);
                }
            }*/
            text_inv_empty.gameObject.SetActive(!hasInv);


            canvas.UnloadButton(temporaryExpResolves);
            temporaryExpResolves.Clear();
            Utility.DestroyAllChildrenFrom(list_ExpeditionMSG);
            foreach (var msg in p.Job.ExpeditionResults)
            {
                bool first = true;
                foreach (var i in msg.Value)
                {
                    if (i.unresolved != null || i.resolveMessage != "")
                    {
                        var v = canvas.MakeEventResolveButton(i, out var box);
                        box.SelfRect.SetParent(list_ExpeditionMSG, false);
                        temporaryExpResolves.Add(v.optionID);
                        box.preText.SetText(i.FullDescription);
                        box.preText.SetExternalTooltip(String.Join("\n", i.Tooltips));
                        box.timeStamp.text = first ? msg.Key.ToString("HH:mm") : "";
                        if ((i.unresolved == null || i.unresolved.isResolved) && i.resolveMessage != "")
                        {
                            box.postEvRect.gameObject.SetActive(true);
                            box.postText.SetText(i.resolveMessage);
                        }
                        else
                        {
                            box.postEvRect.gameObject.SetActive(false);
                        }
                    }
                    else
                    {
                        var v = Instantiate(prefab_memEntry);
                        v.SelfRect.SetParent(list_ExpeditionMSG, false);
                        v.memText.SetText(i.FullDescription);
                        v.memText.SetExternalTooltip(String.Join("\n", i.Tooltips));
                        v.timeStamp.text = first ? msg.Key.ToString("HH:mm") : "";
                    }
                    first = false;
                }
            }

            bool hasActive = false;
            foreach(var ap in p.Job.ActivePackages)
            {
                if (ap.Duration <= 0) continue;
                if (ap.isTemporaryAP) continue;
                hasActive = true;
                // var app = ap as ActionPackage_Expedition;
                // if (app == null) continue;
                var v = Instantiate(prefab_memEntry);
                v.SelfRect.SetParent(list_ExpeditionMSG, false);
                v.memText.SetText(ap.DisplayName == "EMPTY" ? p.Job.DescriptionString : ap.DisplayName);
                v.timeStamp.text = "";// LocalizeDictionary.QueryThenParse("exp_event_timestamp_ongoing");// first ? msg.Key.ToShortTimeString() : "";
            }

            if (!hasActive && p.Job.isActive && p.Job.actorRefID.Count > 0)
            {
                var v = Instantiate(prefab_memEntry);
                v.SelfRect.SetParent(list_ExpeditionMSG, false);
                v.memText.SetText(p.Job.DescriptionString);
                v.timeStamp.text = "";// LocalizeDictionary.QueryThenParse("exp_event_timestamp_ongoing");// first ? msg.Key.ToShortTimeString() : "";
            }
        }
    }
    public scr_memoryBox prefab_memEntry;
    public RectTransform list_ExpeditionMSG;
    public scr_ExpeditionConfig ExpeditionConfig;



    public void OnValueChanged_PartyName(string s)
    {
       // Debug.Log("OnValueChanged");
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
            if (!parent.currentParty.isPlayerFaction) return false;
            var booleanvalue = parent.currentParty.AllowPassNight;
            text.Toggle(true, booleanvalue);

            expDuration.SetText(timeString
                .Replace("$time$", $"{parent.currentParty.BaseDuration}")
                .Replace("$final$", $"{parent.currentParty.FinalDuration}"));

            return !parent.currentParty.isActive;
        }
        public void OnClickButton()
        {
            var booleanvalue = parent.currentParty.AllowPassNight;
            parent.currentParty.AllowPassNight = !booleanvalue;
        }
    }

    public class ButtonValidator_EditCamp : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Management parent;
        scr_SelectableText text;
        public ButtonValidator_EditCamp(scr_Canvas_Management parent, scr_SelectableText text) : base(parent)
        {
            this.parent = parent;
            this.text = text;
        }
        public override bool IsButtonValid()
        {
            if (!text.gameObject.activeInHierarchy) return false;
            else if (parent.currentParty == null)
            {
                this.tooltip = $"RoomJobs: null";
                return false;
            }
            if (!parent.currentParty.isPlayerFaction) return false;

            List<string> roomJobs = new List<string>();
            foreach (var i in parent.currentParty.Room.Jobs) roomJobs.Add($"{i.DisplayName}:{String.Join("|", i.allusableCOMStrings)}");

            List<string> managedRooms = new List<string>();
            foreach (var i in parent.currentParty.ManagedRooms) managedRooms.Add($"{i.Key}:{String.Join("|",i.Value)}");

            List<string> nonJobPosts = new List<string>();
            foreach (var i in parent.currentParty.NonjobPosts)
            {
                List<string> names = new List<string>();
                foreach (var j in i.Value) names.Add(j.DisplayName);
                nonJobPosts.Add($"{i.Key.DisplayName()}:{String.Join("|", names)}");
            }

this.tooltip = $"RoomJobs:{(parent.currentParty == null ? "null" : String.Join("\n", roomJobs))}\n\nManagedRooms:\n{String.Join("\n",managedRooms)}\n\nNonJobPosts:\n{String.Join("\n", nonJobPosts)}"; 



            return false;
        }

        public void OnClickButton()
        {
        }
    }
    public class ButtonValidator_PrioritizeResting : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Management parent;
        scr_SelectableText text;
        public ButtonValidator_PrioritizeResting(scr_Canvas_Management parent, scr_SelectableText text) : base(parent)
        {
            this.parent = parent;
            this.text = text;
        }
        public override bool IsButtonValid()
        {
            //if (text == null) Debug.LogError("text null");
            if (!text.gameObject.activeInHierarchy || parent.currentParty == null)
            {
                return false;
            }
            if (!parent.currentParty.isPlayerFaction) return false;

            var isRecurring = parent.currentParty.PrioritizeResting;
            text.Toggle(true, isRecurring);

            return !parent.currentParty.isActive;
        }

        public void OnClickButton()
        {
            parent.currentParty.PrioritizeResting = !parent.currentParty.PrioritizeResting;
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
            if (!parent.currentParty.isPlayerFaction) return false;

            var isRecurring = parent.currentParty.IsRecurring;
            text.Toggle(true, isRecurring);
            recurringCooldown.interactable = !parent.currentParty.isActive && isRecurring;
            act?.Invoke();

            return !parent.currentParty.isActive;
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

        if (!canvas.currentParty.IsRecurring) ExpeditionConfig.CooldownTime.text = "-";
        else
        {
            var body = LocalizeDictionary.QueryThenParse("ui_management_expeditionConfig_recurringCooldown")
                    .Replace("$count$", $"{canvas.currentParty.RecurringCooldown}");
            ExpeditionConfig.CooldownTime.text = ExpeditionConfig.CooldownTime.interactable ? $"[{body}]" : body;
        }
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
        if (canvas == null || canvas.currentParty == null || !ExpeditionConfig.gameObject.activeInHierarchy) return;
        var currentExp = canvas.currentParty.Job.Expedition;
        if (currentExp != null && currentExp.Base.HasStartHour)
        {
            ExpeditionConfig.StartTime.interactable = false;
            ExpeditionConfig.StartTime.text = LocalizeDictionary.QueryThenParse("ui_management_expeditionConfig_startTimeForced")
                .Replace("$time$", $"{StaticTime.AddHours( canvas.currentParty.FinalStartHour).ToShortTimeString()}");
        }
        else
        {
            ExpeditionConfig.StartTime.interactable = !canvas.currentParty.isActive;
            var body = LocalizeDictionary.QueryThenParse("ui_management_expeditionConfig_startTime")
                .Replace("$time$", $"{StaticTime.AddHours(canvas.currentParty.FinalStartHour).ToShortTimeString()}");
            ExpeditionConfig.StartTime.text = ExpeditionConfig.StartTime.interactable ? $"[{body}]" : body;
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
            onClick = null;
            var p = parent.currentParty;
            
            //if (text == null) Debug.LogError("text null");
            if (!text.gameObject.activeInHierarchy) return false;
            if (p == null)
            {
                this.text.SetText(LocalizeDictionary.QueryThenParse("ui_management_expeditions_setExpTarget_none"));
                return false;
            }
            if (!p.isPlayerFaction)
            {
                this.text.SetText("non player party, cannot resolve");
                return false;
            }
            if (!parent.currentParty.isActive)
            {
                if (p.Job.ExpeditionActive)
                {
                    this.text.SetText(LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_cancel"));
                    onClick = CancelExp;
                    return true;
                }
                else
                {
                    this.text.SetText(LocalizeDictionary.QueryThenParse("ui_management_expeditions_startExp"));
                    onClick = StartExp;
                    return parent.currentParty.CanStartExpedition;
                }
            }
            else if (parent.currentParty.Job.hasUnresolvedResult)
            {
                this.text.SetText(LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_requireManualresolve"));
                return false;
            }
            else if (parent.currentParty.Job.status != Job_Expedition.ExpeditionStatus.returning)
            {
                onClick = AbortExp;
                this.text.SetText(LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_abort"));
                return true;
            }
            else
            {
                this.text.SetText(LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_resolving"));
                return false;
            }
        }

        Action onClick = null;

        protected void StartExp()
        {
            parent.currentParty.Job.ExpeditionActive = true;
        }

        protected void AbortExp()
        {
            parent.currentParty.Job.status = Job_Expedition.ExpeditionStatus.returning;
        }

        protected void CancelExp()
        {
            parent.currentParty.Job.ExpeditionActive = false;
        }

        public void OnClickButton()
        {
            if (onClick != null)
            {
                onClick();
                parent.currentParty.Job.UpdateStatus(-1,-1,false);
                parent.LoadParty(parent.currentParty);// (charaRefID);
            }
        }
    }

    public class ButtonValidator_ResolveEvent : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Management parent;
        scr_SelectableText text;
        ExpeditionMessageEntry m;
        string extraText;

        public ButtonValidator_ResolveEvent(scr_Canvas_Management parent, scr_SelectableText text, ExpeditionMessageEntry m, string extraText) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.m = m;
            this.extraText = extraText;
        }

        public override bool IsButtonValid()
        {
            text.SetText(extraText);
            state = ButtonValidator_States.Valid;
            //if (text == null) Debug.LogError("text null");
            if (!text.gameObject.activeInHierarchy) return false;
            if (parent.currentParty == null || parent.currentParty.Job == null || !parent.currentParty.Job.isActive) return false;
            if (!parent.currentParty.isPlayerFaction) return false;
            if (m.unresolved == null || m.unresolved.isResolved) return false;
            if (m.Characters.Count < 1)
            {
                state = ButtonValidator_States.Conflict;
                text.SetText(LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_cannotResolve"));
                return false;
            }
            return true;
        }


        public void OnEVResolve()
        {

        }

        public void OnClickButton()
        {
            //scr_System_CampaignManager.current.ChangeCurrentViewMode(ViewMode.View_Logs);
            var package = m.unresolved;
            var ev = EventUtility.StartEvent(parent.currentParty.Job, package);
            //scr_System_CampaignManager.current.FreeUpdate();
            var callbacks1 = new List<Action>();
            package.isResolved = true;

            ev.FunctionCalls.Add("ResolveEvent", callbacks1);
            callbacks1.Add(() => {
                m.resolveEventName = package.DisplayName;
                m.resolveMessage = ev.DumpCurrentLine;
                package.isResolved = true;
                m.unresolved = null;
            });

            var callbacks2 = new List<Action>();
            ev.FunctionCalls.Add("RetryEvent", callbacks2);
            callbacks2.Add(() => {
                package.isResolved = false;
            });

            var callbacks3 = new List<Action>();
            ev.FunctionCalls.Add("AddEventMessagePostResolve", callbacks3);
            callbacks3.Add(() => {
                if (ev.AppendStrings.ContainsKey("AddEventMessagePostResolve"))
                {
                    m.resolveMessage = $"{m.resolveMessage}\n{String.Join("\n", ev.AppendStrings["AddEventMessagePostResolve"])}";
                }
            });

            scr_System_CampaignManager.current.RegisterCanvasAnchorHideEventCallback(ev);
            scr_System_CampaignManager.current.HideCanvasAnchor();

        }
    }

    public class ButtonValidator_partyMemberTeamComp : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Management parent;
        scr_SelectableText text;
        Character_Trainable c;
        Manageable_Party.PartyComposition targetComp;
        public ButtonValidator_partyMemberTeamComp(scr_Canvas_Management parent, scr_SelectableText text, Character_Trainable c, Manageable_Party.PartyComposition comp) : base(parent)
        {
            this.text = text;
            this.parent = parent;
            this.c = c;
            this.targetComp = comp;
            text.isButtonToggle = true;
        }

        bool isJoin = false;

        public override bool IsButtonValid()
        {
            //if (text == null) Debug.LogError("text null");
            if (!text.gameObject.activeInHierarchy) return false;
            if (parent.currentParty == null) return false;
            if (!parent.currentParty.isPlayerFaction) return false;
            if (!parent.currentParty.ManagedRefs.Contains(c.RefID)) return false;

            
            text.Toggle(true, parent.currentParty.GetTeamComp(c.RefID) == targetComp);
            return true;
        }

        public void OnClickButton()
        {
            parent.currentParty.SetTeamComp(c.RefID, targetComp);
        }
    }

    public scr_HoverableText SelextExpTooltip;
    public class ButtonValidator_partyEditExpeditions : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Management parent;
        scr_SelectableText text;
        scr_HoverableText extraTooltip;
        public ButtonValidator_partyEditExpeditions(scr_Canvas_Management parent, scr_SelectableText text, scr_HoverableText tooltip) : base(parent)
        {
            this.text = text;
            this.parent = parent;
            this.extraTooltip = tooltip;
            this.extraTooltip.SetText("");
        }

        public override bool IsButtonValid()
        {
            //if (text == null) Debug.LogError("text null");
            if (!text.gameObject.activeInHierarchy) return false;
            if (parent.currentParty == null || parent.CurrentFaction == null || parent.currentParty.Job == null)
            {
                text.SetText(LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_").Replace("$expName$", "-"));
                return false;
            }
            text.SetText(parent.currentParty.Job.DisplayName);
            bool isplayer = parent.currentParty.isPlayerFaction;
            if (!isplayer || parent.currentParty.isActive)
            {
                var stringkey = isplayer && (parent.currentParty.Job.status == Job_Expedition.ExpeditionStatus.active || parent.currentParty.Job.status == Job_Expedition.ExpeditionStatus.returning) ? "ui_management_expeditionJob_remaining_active_ongoing" : "ui_management_expeditionJob_remaining_active";
                var str = LocalizeDictionary.QueryThenParse(stringkey)
                        .Replace("$time$", parent.currentParty.Job.RemainingTime)
                        .Replace("$progress$", parent.currentParty.Job.RemainingProgress)
                        .Replace("$names$", parent.currentParty.Job.ActorCount < 1 ? " - " : String.Join(" ", parent.currentParty.Job.ActorNames));
                
                if (isplayer && parent.currentParty.isActive)
                {
                    var settings = new List<string>();
                    if (parent.currentParty.PrioritizeResting) settings.Add(LocalizeDictionary.QueryThenParse("ui_management_expeditionConfig_prioritizeResting"));
                    if (settings.Count > 0)
                    {
                        str += $"\n{LocalizeDictionary.QueryThenParse("ui_management_expeditionConfig").Replace("$settings$", String.Join(" ", settings))}";
                    }
                }

                extraTooltip.SetText(str);
            }
            else
            {
                extraTooltip.SetText(LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_remaining_inactive"));
            }

            return parent.currentParty.isPlayerFaction && !parent.currentParty.isActive;
        }

        public void OnClickButton()
        {
            parent.Script_Expeditions.CurrentMode = initScript_Expeditions.PartyEditUI.ExpeditionEdit;
        }
    }

    public class ButtonValidator_selectExp : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Management parent;
        scr_SelectableText text;
        ExpeditionInstance exp;
        scr_partyBTN box;
        public ButtonValidator_selectExp(scr_Canvas_Management parent, scr_SelectableText text, ExpeditionInstance exp, scr_partyBTN box) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.exp = exp;
            text.isButtonToggle = true;
            text.AttachOnHoverEnter(OnHover);
            this.box = box;
        }
        public override bool IsButtonValid()
        {
            //if (text == null) Debug.LogError("text null");
            //box.SelfRect.gameObject.SetActive(parent.CurrentFaction != null && (parent.CurrentFaction.explorationKeywords.Count < 1 || (exp.Keywords.Count > 0 && Utility.ListContainsStrict(parent.CurrentFaction.explorationKeywords, exp.Keywords))));
            //Debug.Log($"ButtonValidator_selectExp isvalid [{String.Join("|", parent.CurrentFaction.explorationKeywords)}] [{String.Join("|", exp.keywords)}]");
            if (!text.gameObject.activeInHierarchy) return false;
            if (!parent.currentParty.isPlayerFaction) return false;
            else if (parent.currentParty == null) return false;
            else if (this.exp == null) return false;

            this.text.Toggle(true, this.exp == parent.currentParty.Job.Expedition);
            return true;
        }

        public void OnHover()
        {
            if (!text.gameObject.activeInHierarchy) return;
            parent.expSelectPage.LoadExp(this.exp);
        }

        public void OnClickButton()
        {
            parent.currentParty.SetExpedition(this.exp);
            parent.Script_Expeditions.CurrentMode = initScript_Expeditions.PartyEditUI.Neutral;
            parent.currentParty.Job.UpdateStatus(-1,-1,false);
            parent.LoadParty(parent.currentParty);// (charaRefID);
            //parent.ValidateAll();
        }
    }
}
