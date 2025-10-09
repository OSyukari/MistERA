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
        capture
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
            // enslave
            var targetFaction = this.isTeamA ? this.parent.b : this.parent.a;
            if (targetFaction is Manageable_Party)
            {
                this.innerChara.FactionManager.RemoveFromParty(null, true);
                if(this.innerChara.FactionManager.AddToParty(targetFaction as Manageable_Party, TreatmentResult == Treatment.capture ? Manageable_GuestStatus.Prisoner : Manageable_GuestStatus.Member, true))
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
                if (this.innerChara.FactionManager.Faction_Home == null) this.innerChara.FactionManager.SetHomeFaction((targetFaction as Manageable).ID, TreatmentResult == Treatment.capture ? Manageable_GuestStatus.Prisoner : Manageable_GuestStatus.Member);
                else this.innerChara.FactionManager.SetTempHomeFaction((targetFaction as Manageable).ID, TreatmentResult == Treatment.capture ? Manageable_GuestStatus.Prisoner : Manageable_GuestStatus.Member);
            }
            else
            {
                Debug.LogError($"Chara Resolve Trade failed, undefined faction type");
            }
            scr_System_CampaignManager.current.MoveCharacterTo(innerChara.RefID, targetFaction.MainExit.RefID);
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

        canvas.RegisterBtn(this.btn_capture, new Button_SetTreatment(canvas, this.btn_capture, this, Treatment.capture));
        canvas.RegisterBtn(this.btn_transfer, new Button_SetTreatment(canvas, this.btn_transfer, this, Treatment.transfer));
        canvas.RegisterBtn(this.btn_nothing, new Button_SetTreatment(canvas, this.btn_nothing, this, Treatment.none));
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
            scr_Menu_CharaDetail detail = scr_System_SceneManager.current.LoadCanvasIntoScene(parent.prefab_Canvas_CharaDetail.GetComponent<RectTransform>(), parent.GetComponent<RectTransform>()).GetComponent<scr_Menu_CharaDetail>();
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

            if (target == Treatment.none)
            {
                innerText = $"trade_chara_Treatment_{target}";
            }
            else if (target == Treatment.transfer)
            {
                // cannot transfer self teammmate
                // cannot transfer enemy (only capture is allowed)
                // cannot transfer non-enemy that is not flagged allowtransfer
                if (entry.isTeamA || parent.isHostile || !parent.allowTransfer) deactivateSelf = true;
                else
                {
                    innerText = $"trade_chara_Treatment_{target}";
                    // variants: transfer ally, rescue target
                }

            }
            else if (target == Treatment.capture)
            {
                if (entry.isTeamA || !parent.allowHostile) deactivateSelf = true;
                else if (!entry.innerChara.isHumanoid) deactivateSelf = true;
                else
                {
                    innerText = $"trade_chara_Treatment_{target}";
                    // variants: hostile capture, non-hostile kidnap/enslave
                }
            }
            else
            {

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
