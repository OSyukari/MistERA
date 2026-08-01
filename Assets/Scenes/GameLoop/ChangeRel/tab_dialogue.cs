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

    public void ChangeTab(scr_menu_changeRel menu)
    {
        Debug.Log("tab_dialogue ChangeTab execute");

        menu.title.SetText(LocalizeDictionary.QueryThenParse("menu_dialogue_title")
            .Replace("$source$", menu.CurrentRel.Target.FirstName)
            .Replace("$target$", menu.CurrentTarget.FirstName));//

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
            menu.RegisterBtn(btn, new Button_DialogueEvent(menu, btn, ev, instance));
            btn.SetText(LocalizeDictionary.QueryThenParse(ev.ID));
        }

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
        EventInstance instance;

        public Button_DialogueEvent(scr_menu_changeRel parent, scr_SelectableText text, Event ev, EventInstance instance) : base(parent)
        {
            this.text = text;
            this.instance = instance;
            this.noValidate = true;
        }

        public override bool IsButtonValid()
        {
            return true;
        }

        public void OnClickButton()
        {
            scr_UpdateHandler.current.EventHandler.StartEvent(instance, false);

            scr_System_CampaignManager.current.RegisterSceneUnloadActionCallback(() => { scr_UpdateHandler.current.EventHandler.Run();
                scr_System_CampaignManager.current.ChangeCurrentViewMode(ViewMode.View_Logs);
            });
            parent.Notify(9999);
        }

        public override void Destroy()
        {
            this.text = null;
            this.parent = null;
        }
    }
}
