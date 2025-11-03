using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class scr_charaEntry : MonoBehaviour
{
    public scr_HoverableText charaName;
    public scr_SelectableText btn_capture, btn_transfer, btn_nothing, btn_inspectChara;
    public RectTransform rect_charaTreatment, rect_charaBody, rect_charaInventory;
    public RectTransform selfRect;
    public Image selfImage;

    public bool isTeamA;
    public Character_Trainable innerChara = null;
    public menu_Trade parent;
    public Color32 neutral, toggled;

    public scr_itemEntry prefab_item;

    Treatment _treatment = Treatment.none;
    public Treatment TreatmentResult
    {
        get
        {
            return _treatment;
        }
        set
        {
            _treatment= value;
            UpdateUI();
        }
    }
    public enum Treatment
    {
        none,
        transfer,
        capture,
        rescue,
        liberate
    }

    public bool allowDismember = false;
    public bool allowInventory = false;

    List<scr_itemEntry> managedLists = new List<scr_itemEntry>();

    public void Resolve()
    {
        if (this.TreatmentResult == Treatment.none)
        {
            foreach (var i in managedLists) i.Resolve();
        }
        else
        {

            var targetFaction = this.isTeamA ? this.parent.b : this.parent.a;

            var prevStatus = innerChara.FactionManager.CurrentActiveParty.GetStatus(innerChara);
            var newStats = targetFaction.FactionOwnerRoot.GetStatus(innerChara.RefID);

            switch (this.TreatmentResult)
            {
                case Treatment.rescue:
                    if (newStats == Manageable_GuestStatus.None) newStats = Manageable_GuestStatus.Member;
                    // else maintain existing status
                    break;
                case Treatment.transfer: newStats = prevStatus; break;
                case Treatment.capture: newStats = Manageable_GuestStatus.Prisoner; break;
                default:
                    newStats = Manageable_GuestStatus.None; break;
            }

            //Debug.LogError($"Chara Resolve Trade treatment {prevStatus} {newStats} {this.TreatmentResult}");
            if (TreatmentResult == Treatment.liberate && parent.liberateEventID != "")
            {
                if (newStats == Manageable_GuestStatus.None)
                {
                    var ev = new EventInstance(innerChara, parent.liberateEventID, "");
                    ev.Targets.Add("party", isTeamA ? this.parent.b.ManagedChara : this.parent.a.ManagedChara);
                    ev.Targets.Add("rescued", new List<Character_Trainable>() { innerChara });
                    ev.displayOverride = innerChara.DisplayCharaEvent || isTeamA ? parent.b.isPlayerFaction : parent.a.isPlayerFaction;// (innerChara.DisplayCharaEvent || (isTeamA ? parent.a.isPlayerFaction || parent.b.isPlayerFaction));
                    //Debug.Log($"displayOverride? {ev.displayOverride}");
                    scr_UpdateHandler.current.EventHandler.StartEvent(ev, false);
                }
                else
                {
                    Debug.LogError($"Liberating character {innerChara.CallName}, is legit? {newStats == Manageable_GuestStatus.None}");
                }
                return;
            }
            else if (newStats == Manageable_GuestStatus.None)
            {
                Debug.LogError($"Chara Resolve Trade failed, undefined faction type");
                return;
            }
            else if (targetFaction is Manageable_Party)
            {

                scr_System_CampaignManager.current.MoveCharacterTo(innerChara, targetFaction.MainExit);
                this.innerChara.FactionManager.RemoveFromParty(null, true, true);

                if (this.innerChara.FactionManager.AddToPartyAsTemp(targetFaction, newStats == Manageable_GuestStatus.Prisoner ? Manageable_GuestStatus.Prisoner : Manageable_GuestStatus.Visitor, newStats))
                {
                    // do nothing
                }
                else
                {
                    Debug.LogError($"Chara Resolve Trade failed, add to party unsuccessful");
                }
            }
            else if (targetFaction is Manageable)
            {
                scr_System_CampaignManager.current.MoveCharacterTo(innerChara, targetFaction.MainExit);
                this.innerChara.FactionManager.RemoveFromParty(null, true, true);

                if (this.innerChara.FactionManager.Faction_Home == null) this.innerChara.FactionManager.SetHomeFaction((targetFaction as Manageable).ID, newStats);
                else this.innerChara.FactionManager.SetTempHomeFaction((targetFaction as Manageable).ID, newStats);
            }
            else
            {
                Debug.LogError($"Chara Resolve Trade failed, undefined faction type");
            }
            this.innerChara.ChangeCurrentJob();
        }
    }

    public void InitChara(menu_Trade canvas, Character_Trainable c, bool isTeamA)
    {
        managedLists.Clear();

        this.parent = canvas;
        this.isTeamA = isTeamA;
        this.innerChara = c;

        var targetInv = isTeamA ? canvas.b.Inventory : canvas.a.Inventory;

        bool displayFullName = c.CallName != c.FirstName;
        if (displayFullName) charaName.SetText($"{c.CallName} {c.FirstName}");
        else charaName.SetText(c.CallName);

        if (isTeamA)
        {
            rect_charaTreatment.gameObject.SetActive(false);
            rect_charaBody.gameObject.SetActive(false);
            rect_charaInventory.gameObject.SetActive(false);
            return;
        }

        this.allowDismember = canvas.allowDismember(c, isTeamA);
        this.allowInventory = canvas.allowInventory(isTeamA);

        if (parent.a.FactionOwnerRoot.isManagedChara(innerChara.RefID))
        {
            canvas.RegisterBtn(this.btn_nothing, new Button_SetTreatment(canvas, this.btn_nothing, this, Treatment.rescue));
            _treatment = Treatment.rescue;
        }
        else
        {
            canvas.RegisterBtn(this.btn_nothing, new Button_SetTreatment(canvas, this.btn_nothing, this, Treatment.none));
            _treatment = Treatment.none;
        }

        canvas.RegisterBtn(this.btn_capture, new Button_SetTreatment(canvas, this.btn_capture, this, Treatment.capture));
        canvas.RegisterBtn(this.btn_transfer, new Button_SetTreatment(canvas, this.btn_transfer, this, Treatment.transfer));
        canvas.RegisterBtn(this.btn_inspectChara, new Button_InspectChara(canvas, this.btn_inspectChara, c));


        foreach(var itemInst in c.Inventory.Contents)
        {
            var entry = Instantiate(prefab_item);
            entry.selfRect.SetParent(this.rect_charaInventory, false);
            entry.InitItem(canvas, itemInst, isTeamA, targetInv, c.Inventory);
            managedLists.Add(entry);
        }

        foreach(var item in c.EquippedItemRefs)
        {
            var itemInst = scr_System_CampaignManager.current.FindItemInstanceByID(item);
            if (itemInst == null) continue;
            var entry = Instantiate(prefab_item);
            entry.selfRect.SetParent(this.rect_charaInventory, false);
            entry.InitItem(canvas, itemInst, isTeamA, targetInv, null, c);
            managedLists.Add(entry);
        }

        neutral = scr_System_CentralControl.current.DisplaySetting.TextColor_neutral.Color;
        toggled = scr_System_CentralControl.current.DisplaySetting.TextColor_toggle.Color;
        UpdateUI();
    }
    protected void UpdateUI()
    {
        bool showInv = TreatmentResult == Treatment.none;
        rect_charaInventory.gameObject.SetActive(showInv);
        rect_charaBody.gameObject.SetActive(showInv);

        charaName.SetColor(showInv ? neutral : toggled);
    }

    public class Button_InspectChara : ButtonValidator, I_ButtonClickable
    {
        new menu_Trade parent;
        scr_SelectableText text;
        Character_Trainable c;
        public Button_InspectChara(menu_Trade parent, scr_SelectableText text, Character_Trainable c) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.c = c;
        }
        public override bool IsButtonValid()
        {
            if (!text.gameObject.activeInHierarchy) return false;
            if (c == null) return false;
            return true;
        }
        public void OnClickButton()
        {
            scr_Menu_CharaDetail detail = scr_System_SceneManager.current.LoadCanvasIntoScene(parent, parent.prefab_Canvas_CharaDetail).GetComponent<scr_Menu_CharaDetail>();
            detail.InitializeWithArgument(c.RefID);
        }
    }


    public class Button_SetTreatment : ButtonValidator, I_ButtonClickable
    {
        new menu_Trade parent;
        scr_SelectableText text;
        scr_charaEntry entry;
        Treatment target;
        bool deactivateSelf = false;

        string innerText = "";
        public Button_SetTreatment(menu_Trade parent, scr_SelectableText text, scr_charaEntry entry, Treatment target) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.entry = entry;
            this.target = target;

            bool isRescue = false;
            bool canbeLiberated = false;

            if (entry.isTeamA || !parent.allowTransfer) deactivateSelf = true;
            else if (parent.a.FactionOwnerRoot.isManagedChara(entry.innerChara.RefID))
            {// if chara in A and not in B -> rescue
                isRescue = true;
            }
            else if (parent.b.FactionOwnerRoot.isPrisoner(entry.innerChara.RefID) && parent.liberateEventID != "")
            {// not in faction, can be liberate
                canbeLiberated = true;
            }
            // if chara not in A, capture or rescue(transfer)

            if (deactivateSelf)
            {
                // do nothing
            }
            else if (!parent.allowTransfer) deactivateSelf = true;
            else if (target == Treatment.transfer)
            {
                if (isRescue || !canbeLiberated) deactivateSelf = true;
                else if (!parent.isHostile)
                {
                    // non hostile faction allow transfer -> direct transfer
                    innerText = $"trade_chara_Treatment_{target}";
                }
                else if (canbeLiberated)
                {
                    // hostile faction allow transfer and is prisoner -> rescue
                    this.target = Treatment.liberate;
                    innerText = $"trade_chara_Treatment_{this.target}";
                }
            }
            else if (target == Treatment.capture)
            {
                if (!parent.allowHostile || isRescue) deactivateSelf = true;
                else if (!entry.innerChara.isHumanoid) deactivateSelf = true;
                else
                {
                    innerText = $"trade_chara_Treatment_{target}";
                    // variants: hostile capture, non-hostile kidnap/enslave
                }
            }
            else
            {
                innerText = $"trade_chara_Treatment_{target}";
            }

            if (deactivateSelf) this.text.gameObject.SetActive (false);
        }
        public override bool IsButtonValid()
        {
            if (deactivateSelf) return false;
            if (!text.gameObject.activeInHierarchy) return false;
            if (entry == null) return false;
            text.SetText(innerText);
            text.Toggle(true, target == entry.TreatmentResult);
            return true;
        }
        public void OnClickButton()
        {
            entry.TreatmentResult = target;
        }
    }
}
