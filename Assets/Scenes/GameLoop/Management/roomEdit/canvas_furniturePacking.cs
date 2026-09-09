using System.Collections.Generic;
using UnityEngine;




public class canvas_furniturePacking : scr_Menu
{

    public RectTransform factionOwnerInventory;
    public RectTransform roomFurnitures;

    public itemEntryScript prefab_itemEntry;
    public itemEntryScript prefab_furnitureEntry;

    Room_Instance room;
    List<Button_PackFurniture> packEntries = new List<Button_PackFurniture>();
    List<Button_UnpackFurniture> unpackEntries = new List<Button_UnpackFurniture>();


    protected override void Awake()
    {
        base.Awake();
        this.m_Canvas.overrideSorting = true;
        button_alwaysValid = new ButtonValidator_AlwaysTrue(this);

    }
    public void InitializeWithArgument(Room_Instance room)
    {
        this.room = room;
        cache_isPlayerManager = null;
        if (!initialized) Initialize();


        Utility.DestroyAllChildrenFrom(roomFurnitures);
        Utility.DestroyAllChildrenFrom(factionOwnerInventory);
        packEntries.Clear();
        unpackEntries.Clear();

        if (room != null)
        {
            var faction = room.FactionOwner;
            bool isManager = isPlayerManager;

            foreach (var inst in room.Furnitures)
            {
                if (inst.noDisplay) continue;
                AddFurnitureEntry(inst, isManager);
            }

            if (faction != null && faction.Inventory != null)
            {
                foreach (var item in faction.Inventory.Contents)
                {
                    if (item.GetComp("ItemComponent_Furniture") == null) continue;
                    AddItemEntry(item, isManager);
                }
            }
        }

        ValidateAll();
    }

    private void AddFurnitureEntry(FurnitureInstance inst, bool isManager)
    {
        itemEntryScript box = Instantiate(prefab_furnitureEntry);
        box.itemName.SetText(inst.DisplayName);
        box.itemName.SetExternalTooltip(LocalizeDictionary.QueryThenParse(inst.FurnitureBase.ID + "_tooltip", ""));
        box.selfRect.SetParent(roomFurnitures, false);

        box.actionButton.isButtonToggle = true;

        var validator = new Button_PackFurniture(this, inst, box.actionButton, isManager);
        RegisterButton(inst.GetHashCode(), box.actionButton, validator);
        packEntries.Add(validator);
    }

    private void AddItemEntry(Item_Instance item, bool isManager)
    {
        itemEntryScript box = Instantiate(prefab_itemEntry);
        box.itemName.SetText(item.DisplayName);
        box.itemName.SetExternalTooltip(item.Tooltip);
        box.selfRect.SetParent(factionOwnerInventory, false);

        box.actionButton.isButtonToggle = true;

        var validator = new Button_UnpackFurniture(this, item, box.actionButton, isManager);
        RegisterButton(item.GetHashCode(), box.actionButton, validator);
        unpackEntries.Add(validator);
    }

    private void RegisterButton(int hash, scr_SelectableText button, ButtonValidator validator)
    {
        int id = AssertUniqueHash(hash);
        button.Initialize(this, validator);
        button.optionID = id;
        buttonsByID.Add(id, button);
        validatorsByID.Add(id, validator);
    }

    private void ApplyAll()
    {

        foreach (var v in packEntries)
        {
            if (!v.button.IsToggled) continue;
            if (!FurnitureItemUtility.TryPack(v.inst, out _, out var reason)) Debug.LogError($"pack furniture failed: {reason}");
        }

        foreach (var v in unpackEntries)
        {
            if (!v.button.IsToggled) continue;
            if (!FurnitureItemUtility.TryUnpack(v.item, room, out _, out var reason)) Debug.LogError($"unpack furniture failed: {reason}");
        }
        deactivate = true;
        scr_System_CampaignManager.current.NotifyUpdate();
    }


    bool? cache_isPlayerManager = null;
    bool deactivate = false;
    public bool isPlayerManager
    {
        get
        {
            if (cache_isPlayerManager == null)
            {
                if (this.room == null || this.room.FactionOwner == null || this.room.FactionOwner.Managers == null)
                {
                    cache_isPlayerManager = false;
                }
                else
                {

                    cache_isPlayerManager =   this.room.FactionOwner.Managers.Contains(scr_System_CampaignManager.current.Player);
                }
            } return cache_isPlayerManager.Value;

        }
    }

    public override void Initialize()
    {
        base.Initialize();

        bool safe = scr_System_CentralControl.current.isSafeMode;

        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {
            // Debug.Log("Button " + button + " " + button.optionID);
            switch (button.optionID)
            {

                case 9999: // cancel / exit
                    button.Initialize(this, button_alwaysValid); break;
                case 9998: // apply / exit
                    button.Initialize(this, new Button_ApplyAll(this)); break;
                case -1: break;

                default:
                    button.Initialize(this, button_alwaysValid); break;
            }
            if (button.optionID != -1)
            {
                buttonsByID.Add(button.optionID, button);
                validatorsByID.Add(button.optionID, button.Validator);
            }

        }

        // build all presetList
        ValidateAll();


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
                    scr_System_SceneManager.current.UnloadLastCanvasFromScene();
                    break;
                default: break;
            }
        }
        ValidateAll();
    }

    public class Button_PackFurniture : ButtonValidator, I_ButtonClickable
    {
        new canvas_furniturePacking parent;
        public FurnitureInstance inst;
        public scr_SelectableText button;
        bool isManager;

        public Button_PackFurniture(canvas_furniturePacking parent, FurnitureInstance inst, scr_SelectableText button, bool isManager) : base(parent)
        {
            this.parent = parent;
            this.inst = inst;
            this.button = button;
            this.isManager = isManager;
        }

        public override bool IsButtonValid()
        {
            if (!isManager)
            {
                tooltip = "you must be a manager of this faction to pack furniture";
                return false;
            }
            if (parent.deactivate) return false;
            if (!FurnitureItemUtility.CanPack(inst, out var reason))
            {
                tooltip = reason;
                return false;
            }
            tooltip = "";
            return true;
        }

        public void OnClickButton()
        {
            button.Toggle();
        }
    }

    public class Button_UnpackFurniture : ButtonValidator, I_ButtonClickable
    {
        new canvas_furniturePacking parent;
        public Item_Instance item;
        public scr_SelectableText button;
        bool isManager;

        public Button_UnpackFurniture(canvas_furniturePacking parent, Item_Instance item, scr_SelectableText button, bool isManager) : base(parent)
        {
            this.parent = parent;
            this.item = item;
            this.button = button;
            this.isManager = isManager;
        }

        public override bool IsButtonValid()
        {
            if (!isManager)
            {
                tooltip = "you must be a manager of this faction to unpack furniture";
                return false;
            }
            if (!FurnitureItemUtility.CanUnpack(item, out var reason))
            {
                tooltip = reason;
                return false;
            }
            tooltip = "";
            return true;
        }

        public void OnClickButton()
        {
            button.Toggle();
        }
    }

    public class Button_ApplyAll : ButtonValidator, I_ButtonClickable
    {
        new canvas_furniturePacking parent;
        public Button_ApplyAll(canvas_furniturePacking parent) : base(parent)
        {
            this.parent = parent;
        }

        public override bool IsButtonValid()
        {
            if (!parent.isPlayerManager)
            { 
                tooltip = "not faction manager cannot change";
                return false;
            }
            else
            {
                tooltip = "";
                return true;
            }
        }

        public void OnClickButton()
        {
            parent.ApplyAll();
            scr_System_SceneManager.current.UnloadLastCanvasFromScene();
        }
    }
}
