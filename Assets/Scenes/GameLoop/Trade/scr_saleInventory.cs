using System;
using UnityEngine;
using UnityEngine.UI;
using static UnityEngine.EventSystems.EventTrigger;
using static UnityEngine.GraphicsBuffer;


public class scr_saleInventory : MonoBehaviour
{
    public scr_SelectableText btn_TransferSingle, btn_TransferAll, btn_CancelSingle, btn_CancelAll;

    public scr_HoverableText itemName, transferCountUI;
    public HorizontalLayoutGroup layout;
    public RectTransform selfRect;
    public Image selfImage;

    public bool isTeamA;
    public ItemEntry innerInstance = null;
    public menu_Trade parent;
    public Color32 neutral, toggled;

    public void Resolve()
    {
        var Entry = innerInstance;
        var cc = Transfer;

        if (Transfer > 0 && toInv != null && Entry.itemID != "")
        {
            var FactionOwner = isTeamA ? parent.a : parent.b;
            var TargetFaction = isTeamA ? parent.b : parent.a;

            FactionInventory recycler = scr_System_CampaignManager.current.Recycler;
            FactionInventory self = FactionOwner.isPlayerFaction ? FactionOwner.Inventory : recycler;
            FactionInventory target = TargetFaction.isPlayerFaction && TargetFaction != FactionOwner ? TargetFaction.Inventory : recycler;

            var entry_count = Entry.itemCount * cc;
            if (target == recycler) self.AddItem(WorldManager.Instantiate(Entry.itemID, Entry.itemCountOverride ? "" : Entry.itemNameOverwrite, entry_count));
            else self.AddItem(target.RemoveItem(Entry.itemID, entry_count));
            if (self != recycler && FactionOwner.Faction != null) FactionOwner.Faction.DailyReport.AddTradeRecord(Entry.itemID, entry_count);
            if (target != recycler && target != self && TargetFaction.Faction != null) TargetFaction.Faction.DailyReport.AddTradeRecord(Entry.itemID, -entry_count);
            
        }
    }


    protected Inventory fromInv = null, toInv = null;
    protected Character_Trainable equippedTo = null;
    public void InitItem(menu_Trade canvas, ItemEntry entry, bool isTeamA, Inventory toInv, Inventory fromInv = null, Character_Trainable equippedTo = null)
    {
        this.toInv = toInv;
        this.fromInv = fromInv;
        this.equippedTo = equippedTo;
        this.parent = canvas;
        this.isTeamA = isTeamA;
        this.innerInstance = entry;

        parent.RegisterBtn(this.btn_TransferSingle, new Button_tranferSingle(parent, this.btn_TransferSingle, this));
        parent.RegisterBtn(this.btn_TransferAll, new Button_tranferAll(parent, this.btn_TransferAll, this));
        parent.RegisterBtn(this.btn_CancelSingle, new Button_cancelSingle(parent, this.btn_CancelSingle, this));
        parent.RegisterBtn(this.btn_CancelAll, new Button_cancelTransfer(parent, this.btn_CancelAll, this));

        itemName.SetText(innerInstance.Print);
        if (isTeamA)
        {
            layout.reverseArrangement = true;
            layout.childAlignment = TextAnchor.MiddleRight;
            btn_TransferSingle.SetText(">");
            btn_TransferAll.SetText(">>");
            btn_CancelSingle.SetText("<");
            btn_CancelAll.SetText("<<");
        }
        else
        {
            layout.reverseArrangement = false;
            layout.childAlignment = TextAnchor.MiddleLeft;
            btn_TransferSingle.SetText("<");
            btn_TransferAll.SetText("<<");
            btn_CancelSingle.SetText(">");
            btn_CancelAll.SetText(">>");
        }

        neutral = scr_System_CentralControl.current.DisplaySetting.TextColor_neutral.Color;
        toggled = scr_System_CentralControl.current.DisplaySetting.TextColor_toggle.Color;
        UpdateUI();
    }

    protected int transferCount = 0;
    public bool canTransfer
    {
        get
        {
            return this.innerInstance != null && this.transferCount < innerInstance.innerStock;
        }
    }
    public int Transfer
    {
        get
        {
            return transferCount;
        }
        set
        {

            transferCount = Math.Clamp(value, 0, innerInstance.innerStock);
            UpdateUI();
        }
    }

    protected void UpdateUI()
    {

        if (Transfer < 1)
        {
            transferCountUI.SetText("");
            transferCountUI.SetColor(neutral);
            itemName.SetColor(neutral);

        }
        else
        {
            transferCountUI.SetText($"{Transfer}");
            transferCountUI.SetColor(toggled);
            itemName.SetColor(toggled);
        }

    }


    public class Button_tranferSingle : ButtonValidator, I_ButtonClickable
    {
        new menu_Trade parent;
        scr_SelectableText text;
        scr_saleInventory entry;
        public Button_tranferSingle(menu_Trade parent, scr_SelectableText text, scr_saleInventory entry) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.entry = entry;
        }
        public override bool IsButtonValid()
        {
            if (!text.gameObject.activeInHierarchy) return false;
            if (entry == null) return false;
            return entry.canTransfer;
        }
        public void OnClickButton()
        {
            // finalize combat and run
            entry.Transfer += 1;
        }
    }
    public class Button_tranferAll : ButtonValidator, I_ButtonClickable
    {
        new menu_Trade parent;
        scr_SelectableText text;
        scr_saleInventory entry;
        public Button_tranferAll(menu_Trade parent, scr_SelectableText text, scr_saleInventory entry) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.entry = entry;
        }
        public override bool IsButtonValid()
        {
            if (!text.gameObject.activeInHierarchy) return false;
            if (entry == null) return false;
            return entry.canTransfer;
        }
        public void OnClickButton()
        {
            // finalize combat and run
            entry.Transfer = entry.innerInstance.innerStock;
        }
    }
    public class Button_cancelSingle : ButtonValidator, I_ButtonClickable
    {
        new menu_Trade parent;
        scr_SelectableText text;
        scr_saleInventory entry;
        public Button_cancelSingle(menu_Trade parent, scr_SelectableText text, scr_saleInventory entry) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.entry = entry;
        }
        public override bool IsButtonValid()
        {
            if (!text.gameObject.activeInHierarchy) return false;
            if (entry == null) return false;
            return entry.Transfer > 0;
        }
        public void OnClickButton()
        {
            // finalize combat and run
            entry.Transfer -= 1;
        }
    }
    public class Button_cancelTransfer : ButtonValidator, I_ButtonClickable
    {
        new menu_Trade parent;
        scr_SelectableText text;
        scr_saleInventory entry;
        public Button_cancelTransfer(menu_Trade parent, scr_SelectableText text, scr_saleInventory entry) : base(parent)
        {
            this.parent = parent;
            this.text = text;
            this.entry = entry;
        }
        public override bool IsButtonValid()
        {
            if (!text.gameObject.activeInHierarchy) return false;
            if (entry == null) return false;
            return entry.Transfer > 0;
        }
        public void OnClickButton()
        {
            // finalize combat and run
            entry.Transfer = 0;
        }
    }
}
