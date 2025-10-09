using UnityEngine;
using UnityEngine.UI;
using System;

public class scr_itemEntry : MonoBehaviour
{
    public scr_SelectableText btn_TransferSingle, btn_TransferAll, btn_CancelSingle, btn_CancelAll;
    public scr_HoverableText itemName, transferCountUI;
    public HorizontalLayoutGroup layout;
    public RectTransform selfRect;
    public Image selfImage;

    public bool isTeamA;
    public Item_Instance innerInstance = null;
    public menu_Trade parent;
    public Color32 neutral, toggled;

    public void Resolve()
    {
        if (Transfer > 0 && toInv != null)
        {
            if (equippedTo != null)
            {
                equippedTo.UnequipItem(innerInstance.RefID, -1, true, true);
                toInv.AddItem(innerInstance);
            }
            else if (fromInv != null)
            {
                var newInst = fromInv.Split(innerInstance, Transfer);
                toInv.AddItem(newInst);
            }
        }
    }

    protected Inventory fromInv = null, toInv = null;
    protected Character_Trainable equippedTo = null;
    public void InitItem(menu_Trade canvas, Item_Instance entry, bool isTeamA, Inventory toInv, Inventory fromInv = null, Character_Trainable equippedTo = null)
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

        itemName.SetText(innerInstance.Print());
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
            return this.innerInstance != null && this.transferCount < innerInstance.InnerCount;
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

            transferCount = Math.Clamp(value, 0, innerInstance.InnerCount);
            UpdateUI();
        }
    }

    protected void UpdateUI()
    {

        if ( Transfer < 1)
        {
            transferCountUI.SetText( "" );
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
        scr_itemEntry entry;
        public Button_tranferSingle(menu_Trade parent, scr_SelectableText text, scr_itemEntry entry) : base(parent)
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
        scr_itemEntry entry;
        public Button_tranferAll(menu_Trade parent, scr_SelectableText text, scr_itemEntry entry) : base(parent)
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
            entry.Transfer = entry.innerInstance.InnerCount;
        }
    }
    public class Button_cancelSingle : ButtonValidator, I_ButtonClickable
    {
        new menu_Trade parent;
        scr_SelectableText text;
        scr_itemEntry entry;
        public Button_cancelSingle(menu_Trade parent, scr_SelectableText text, scr_itemEntry entry) : base(parent)
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
        scr_itemEntry entry;
        public Button_cancelTransfer(menu_Trade parent, scr_SelectableText text, scr_itemEntry entry) : base(parent)
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
