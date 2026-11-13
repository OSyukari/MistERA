using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class tab_dialogue : MonoBehaviour
{
    public CanvasGroup SelfCanvasGroup;
    bool initialize = false;

    public scr_SelectableText prefab_button;
    public RectTransform comRect, eventRect;
    /// <summary>OnDialogue_Permissions boxes; falls back to eventRect when not assigned in the scene.</summary>
    public RectTransform permissionRect;
    /// <summary>One per OnDialogue_Permissions event: request button + current permission state.</summary>
    public scr_permissionBox prefab_permissionBox;

    public void ChangeTab(scr_menu_changeRel menu)
    {
        //Debug.Log("tab_dialogue ChangeTab execute");

        menu.title.SetText(LocalizeDictionary.QueryThenParse("menu_dialogue_title")
            .Replace("$source$", menu.CurrentRel.Target.FirstName)
            .Replace("$target$", menu.CurrentTarget.FullName));//

        if (initialize) return;
        initialize = true;

        Job_PlayerCOM job = scr_System_CampaignManager.current.FindJobInstanceByID(scr_System_CampaignManager.current.jobRef_playerCOM) as Job_PlayerCOM;
        if (job == null) return;

        foreach (COM c in job.allusableCOMs)
        {
            if (c.hidden) continue;
            if (!c.comTags.Contains("dialogueOnly")) continue;

            var btn = Instantiate(prefab_button);
            btn.SelfRect.SetParent(comRect, false);
            menu.RegisterBtn(btn, new Button_PlayerCOM(menu, btn, c.ID));
        }

        Character_Trainable player = scr_System_CampaignManager.current.Player;
        foreach (var ev in scr_System_Serializer.current.MasterList.Events.list)
        {
            if (ev.trigger != EventTrigger.OnDialogue_Options) continue;

            var instance = new EventInstance(player, ev.ID, "", forbidGeneration: true);
            if (!instance.isValid) continue;

            var btn = Instantiate(prefab_button);
            btn.SelfRect.SetParent(eventRect, false);
            menu.RegisterBtn(btn, new Button_DialogueEvent(menu, btn, ev));
            btn.SetText(LocalizeDictionary.QueryThenParse(ev.ID));
        }

        // unlike OnDialogue_Options, every permission event gets a box: invalid ones stay visible and disabled,
        // their validator explains why (Button_PermissionEvent) - except a permission that does not concern the target's
        // body, which the validator hides. Event ID = permission ID.
        RectTransform permissionParent = permissionRect != null ? permissionRect : eventRect;
        foreach (var ev in scr_System_Serializer.current.MasterList.Events.list)
        {
            if (ev.trigger != EventTrigger.OnDialogue_Permissions) continue;
            if (prefab_permissionBox == null)
            {
                Debug.LogError("tab_dialogue: prefab_permissionBox not assigned, OnDialogue_Permissions events not shown");
                break;
            }

            var box = Instantiate(prefab_permissionBox);
            box.transform.SetParent(permissionParent, false);
            menu.RegisterBtn(box.button, new Button_PermissionEvent(menu, box, ev));
            box.button.SetText(LocalizeDictionary.QueryThenParse(ev.ID));
        }

    }

    /// <summary>
    /// Shared click flow of the dialogue event buttons, treated as a player-issued 1 minute talk with the current target
    /// (ActionPackage_DialogueEvent): the package is registered into the player job and interrupts the npc, then on menu
    /// close the event is queued first and the update started after, so the event plays before the minute ticks.
    /// instance must already be rebuilt and validated by the caller.
    /// </summary>
    static void LaunchDialogueEvent(scr_Menu parent, EventInstance instance, string eventID, string optionName, string logTag)
    {
        var cm = scr_System_CampaignManager.current;
        var player = cm.Player;

        scr_System_CentralControl.current.AutoSave();

        var npc = cm.CurrentTarget;
        // the talked-to character's personality may play its own event instead (first valid override, else the default)
        if (npc != null && npc != player) instance = EventUtility.ResolveDialogueOverride(instance, eventID, npc);
        var talkCOM = scr_System_Serializer.current.GetByNameOrID_COM(ActionPackage_DialogueEvent.TalkCOMID);
        if (talkCOM == null) Debug.LogError($"{logTag}: cannot find {ActionPackage_DialogueEvent.TalkCOMID}, advancing time without talk package");

        if (npc != null && npc != player && talkCOM != null)
        {
            var currentjob = player.CurrentJob;
            var playerjob = currentjob == null || currentjob.CanBeInterrupted ? cm.FindJobInstanceByID(cm.jobRef_playerCOM) : currentjob;

            var ap = new ActionPackage_DialogueEvent(playerjob, talkCOM, npc.RefID, optionName);
            player.ChangeCurrentJob(playerjob);
            if (npc.CurrentJob == null || npc.CurrentJob.CanBeInterrupted) npc.ChangeCurrentJob(playerjob);
            playerjob.AddPackage(new List<ActionPackage>() { ap }, true);
        }

        string startedID = instance.CurrentEventID;
        cm.RegisterSceneUnloadActionCallback(() => {
            if (!scr_UpdateHandler.current.EventHandler.StartEvent(instance, false)) Debug.Log($"{logTag}: event {startedID} rejected on start (cooldown / duplicate)");
            scr_System_CampaignManager.current.FreeUpdate(-1, optionName);
        });
        parent.Notify(9999);
    }

    public class Button_PlayerCOM : ButtonValidator, I_ButtonClickable
    {
        scr_SelectableText text;
        string comID;
        ActionPackage package_cache;

        Job_PlayerCOM job { get { return scr_System_CampaignManager.current.FindJobInstanceByID(scr_System_CampaignManager.current.jobRef_playerCOM) as Job_PlayerCOM; } }
        COM com { get { var j = job; return j == null ? null : j.allusableCOMs.Find(x => x.ID == comID); } }

        public Button_PlayerCOM(scr_menu_changeRel parent, scr_SelectableText text, string comID) : base(parent)
        {
            this.text = text;
            this.comID = comID;
            this.noValidate = true;
        }

        public override bool IsButtonValid()
        {
            Job_PlayerCOM j = job;
            COM c = com;

            if (j == null || c == null)
            {
                text.gameObject.SetActive(false);
                return false;
            }

            // inject participants the same way scr_panel_COMmanager's ButtonValidator_validateCOM does:
            // doer = player, receiver = the live global current target (+ party members unless the COM forbids it)
            List<int> doers = new List<int>() { 0 };
            List<int> receivers = new List<int>();
            int currentref = scr_System_CampaignManager.current.CurrentTargetRef;
            if (currentref > 0) receivers.Add(currentref);
            if (!c.requirements.requirement.forbidTeammateJoin) receivers.AddRange(scr_System_CampaignManager.current.PlayerPartyMembers);
            receivers = receivers.Distinct().ToList();
            receivers.Remove(0);

            if (package_cache == null) package_cache = c.MakePackage(j, doers, receivers, 0);
            else package_cache.ResetRequest(doers, receivers, 0);

            bool valid;
            if (!c.ValidateJob(j, out var msg))
            {
                valid = false;
                tooltip = package_cache.GetTooltips(LocalizeDictionary.QueryThenParse("ui_ap_onHoverTooltip_comInvalid")).Replace("$tooltips$", msg);
            }
            else if (!package_cache.Validate())
            {
                valid = false;
                package_cache.tooltip.RemoveAll(x => x == "" || x.Length < 1);
                tooltip = package_cache.GetTooltips(LocalizeDictionary.QueryThenParse("ui_ap_onHoverTooltip_comInvalid")).Replace("$tooltips$", String.Join("\n", package_cache.tooltip));
            }
            else
            {
                valid = true;
                tooltip = package_cache.GetTooltips(LocalizeDictionary.QueryThenParse("ui_ap_onHoverTooltip"));

                if (package_cache.RequestRate * package_cache.ResponseRate == 0)
                {
                    valid = false;
                    tooltip += $"\n{LocalizeDictionary.QueryThenParse("ui_com_disabled_autofailure_tooltip")}";
                }
                else if (scr_System_CampaignManager.current.DeterministicRolls && package_cache.SuccessRate < scr_System_CampaignManager.current.DeterministicThreshold)
                {
                    valid = false;
                    tooltip += $"\n{LocalizeDictionary.QueryThenParse("ui_com_disabled_autofailure_deterministicRolls_tooltip")}";
                }
            }

            text.SetText(package_cache.DisplayName);

            // mirrors scr_panel_COMmanager's ButtonValidator_validateCOM display formula exactly:
            // HideWhenInvalid only actually hides a button when invalid AND it's not tagged "player"/"initSex"/"endSex".
            // Every COM reachable here comes from Job_PlayerCOM.allusableCOMs, which is hard-filtered to "player"-tagged
            // COMs only (see Job_PlayerCOM.UpdateAllUsableCOMs), so this always resolves true here - same as COMmanager -
            // but the full chain is kept so the behavior stays correct if that guarantee ever changes.
            bool display;
            if (valid) display = true;
            else if (!c.HideWhenInvalid) display = true;
            else if (c.comTags.Contains("player")) display = true;
            else if (c.comTags.Contains("initSex") || c.comTags.Contains("endSex")) display = true;
            else display = false;

            text.gameObject.SetActive(display);

            return valid;
        }

        public void OnClickButton()
        {
            Job_PlayerCOM j = job;
            scr_System_CentralControl.current.AutoSave();

            var ppp = package_cache.Copy();
            foreach (var actorRef in ppp.actorRefs)
            {
                scr_System_CampaignManager.current.FindInstanceByID(actorRef).ChangeCurrentJob(j, ppp.targetCOM.ID);
            }
            j.AddPackage(new List<ActionPackage>() { ppp }, true);


            scr_System_CampaignManager.current.RegisterSceneUnloadActionCallback(() => { scr_System_CampaignManager.current.FreeUpdate(-1, text.Text.text); });

            parent.Notify(9999);
        }

        public override void Destroy()
        {
            this.text = null;
            this.parent = null;
        }
    }

    public class Button_DialogueEvent : ButtonValidator, I_ButtonClickable
    {
        scr_SelectableText text;
        Event ev;

        public Button_DialogueEvent(scr_menu_changeRel parent, scr_SelectableText text, Event ev) : base(parent)
        {
            this.text = text;
            this.ev = ev;
            this.noValidate = true;
        }

        public override bool IsButtonValid()
        {
            return true;
        }

        /// <summary>See tab_dialogue.LaunchDialogueEvent.</summary>
        public void OnClickButton()
        {
            // rebuild on click instead of reusing the tab-init instance, targets may have changed since
            var instance = new EventInstance(scr_System_CampaignManager.current.Player, ev.ID, "", forbidGeneration: true);
            if (!instance.isValid)
            {
                Debug.Log($"Button_DialogueEvent: event {ev.ID} no longer valid on click, abort");
                return;
            }

            LaunchDialogueEvent(parent, instance, ev.ID, text.Text.text, "Button_DialogueEvent");
        }

        public override void Destroy()
        {
            this.text = null;
            this.ev = null;
            this.parent = null;
        }
    }

    /// <summary>
    /// OnDialogue_Permissions box (scr_permissionBox): the event ID is the permission ID, read as current target → player.
    /// Visible unless the permission does not concern the target's body (PermissionService.IsApplicable - hidden, re-checked
    /// on every ValidateAll). The state text shows whether the permission is in effect (any source), hover = its sources or the
    /// missing relationship tags. The button tooltip starts with the permission's "[id]_tooltip"; the button is
    /// disabled while the permission is already in effect (permissions are on/off), for an unknown permission ID, or when
    /// the event cannot start (EventUtility.Diagnose, EventManager.CanStart) - each reason appended. Validation is read-only
    /// and redone on every ValidateAll and on click; the button never changes permissions itself - the event's Results do.
    /// </summary>
    public class Button_PermissionEvent : ButtonValidator, I_ButtonClickable
    {
        scr_permissionBox box;
        Event ev;

        public Button_PermissionEvent(scr_menu_changeRel parent, scr_permissionBox box, Event ev) : base(parent)
        {
            this.box = box;
            this.ev = ev;
            this.noValidate = true;
        }

        /// <summary>A freshly built, startable instance, or null with the reasons why not. Also refreshes the state text.</summary>
        EventInstance BuildInstance(List<string> reasons)
        {
            var cm = scr_System_CampaignManager.current;
            var player = cm.Player;
            var target = cm.CurrentTarget;
            var definition = scr_System_Serializer.current.MasterList.PermissionDefinitions.GetByID(ev.ID);

            PermissionQueryResult query = null;
            if (definition == null) reasons.Add(LocalizeDictionary.QueryThenParse("ui_permissionBox_unknownPermission").Replace("$id$", ev.ID));
            else if (target != null && target != player) query = PermissionService.Query(target, player, ev.ID);
            RefreshState(query, target, player);

            if (query != null && !PermissionService.CanRequest(target, player, query))
                reasons.Add(PermissionService.RequestTooltip(target, player, query));

            if (query != null && query.Error.Length == 0 && query.HasPermission)
                reasons.Add(LocalizeDictionary.QueryThenParse("ui_permissionBox_alreadyGranted")
                    .Replace("$sources$", EventUtility.DescribePermissionSources(query)));

            var instance = new EventInstance(player, ev.ID, "", forbidGeneration: true);
            if (!instance.isValid)
            {
                int before = reasons.Count;
                EventUtility.Diagnose(ev, player, reasons);
                if (reasons.Count == before) reasons.Add(LocalizeDictionary.QueryThenParse("ui_eventDiag_unknown"));
                return null;
            }
            if (!scr_UpdateHandler.current.EventHandler.CanStart(instance, out var reasonKey))
            {
                reasons.Add(LocalizeDictionary.QueryThenParse(reasonKey));
                return null;
            }
            return reasons.Count == 0 ? instance : null;
        }

        /// <summary>
        /// State text and shared request explanation, including passing thresholds and new-grant lifetime.
        /// Existing explicit records retain their own validity independently of new-grant lifetime tags.
        /// </summary>
        void RefreshState(PermissionQueryResult query, Character_Trainable granter, Character_Trainable holder)
        {
            if (box.currentState == null) return;
            string stateKey;
            var lines = new List<string>();
            if (query == null || query.Error.Length != 0)
            {
                stateKey = "ui_permissionBox_state_none";
                if (query != null) lines.Add(LocalizeDictionary.QueryThenParse("ui_permissionBox_invalidPair"));
            }
            else if (query.HasPermission)
            {
                stateKey = query.Sources == PermissionEffectiveSource.TemporaryGrant ? "ui_permissionBox_state_temporary" : "ui_permissionBox_state_granted";
                lines.Add(LocalizeDictionary.QueryThenParse("ui_permissionBox_sources").Replace("$sources$", EventUtility.DescribePermissionSources(query)));
            }
            else
            {
                stateKey = "ui_permissionBox_state_notGranted";
                if (!query.Definition.requestable) lines.Add(LocalizeDictionary.QueryThenParse("ui_permissionBox_notRequestable"));
                else if (query.MissingRelationshipTags.Count > 0)
                    lines.Add(LocalizeDictionary.QueryThenParse("ui_permissionBox_missingTags").Replace("$tags$", EventUtility.DescribeRelationshipTags(query.MissingRelationshipTags)));
                else if (PermissionService.CanRequest(granter, holder, query))
                    lines.Add(LocalizeDictionary.QueryThenParse("ui_permissionBox_requestable"));
                if (query.Grants.Count > 0) lines.Add(LocalizeDictionary.QueryThenParse("ui_permissionBox_dormantGrant"));
            }
            if (query != null && query.Error.Length == 0) lines.Add(PermissionService.RequestTooltip(granter, holder, query));
            box.currentState.SetText(LocalizeDictionary.QueryThenParse(stateKey), false, "ui_permissionBox_state");
            box.currentState.SetExternalTooltip(String.Join("\n", lines));
        }

        public override bool IsButtonValid()
        {
            // a permission that does not concern the current target's body (PermissionDefinition.charaReq) is hidden, not
            // disabled: there is nothing the player could meet
            var cm = scr_System_CampaignManager.current;
            var definition = scr_System_Serializer.current.MasterList.PermissionDefinitions.GetByID(ev.ID);
            bool applicable = definition == null || cm.CurrentTarget == null || cm.CurrentTarget == cm.Player
                || PermissionService.IsApplicable(cm.CurrentTarget, definition);
            if (box.gameObject.activeSelf != applicable) box.gameObject.SetActive(applicable);
            if (!applicable)
            {
                tooltip = "";
                state = ButtonValidator_States.Invalid;
                return false;
            }

            var reasons = new List<string>();
            bool valid = BuildInstance(reasons) != null;

            // event ID = permission ID (permission_*), so this is the permission's own description key
            string tooltipKey = $"{ev.ID}_tooltip";
            string description = LocalizeDictionary.QueryThenParse(tooltipKey);
            tooltip = description == tooltipKey ? "" : description;
            if (cm.CurrentTarget != null && cm.CurrentTarget != cm.Player)
                tooltip += "\n" + PermissionService.RequestTooltip(cm.CurrentTarget, cm.Player,
                    PermissionService.Query(cm.CurrentTarget, cm.Player, ev.ID));
            if (!valid)
            {
                if (tooltip != "") tooltip += "\n\n";
                tooltip += LocalizeDictionary.QueryThenParse("ui_eventDiag_header") + "\n" + String.Join("\n", reasons);
            }

            state = valid ? ButtonValidator_States.Valid : ButtonValidator_States.Invalid;
            return valid;
        }

        public void OnClickButton()
        {
            // re-check: the box may have been drawn valid before the permission / conditions / cooldowns changed
            var reasons = new List<string>();
            var instance = BuildInstance(reasons);
            if (instance == null)
            {
                Debug.Log($"Button_PermissionEvent: event {ev.ID} no longer startable on click, abort: {String.Join(" | ", reasons)}");
                return;
            }

            LaunchDialogueEvent(parent, instance, ev.ID, box.button.Text.text, "Button_PermissionEvent");
        }

        public override void Destroy()
        {
            this.box = null;
            this.ev = null;
            this.parent = null;
        }
    }
}
