using System;
using UnityEngine;
using UnityEngine.EventSystems;

public class scr_Menu_AddSalesOrder : scr_Menu, IPointerClickHandler
{
    public Manageable sourceFaction;
    public RectTransform itemList;
    public scr_addSalesItem prefab_item;

    public void InitializeWithArgument(Manageable sourceFaction, Action onExit)
    {
        this.onSelfExit = onExit;
        if (!initialized) Initialize();

        this.sourceFaction = sourceFaction;

        BuildItemList();
        ValidateAll();
    }

    private void BuildItemList()
    {
        Utility.DestroyAllChildrenFrom(itemList);

        foreach (var item in sourceFaction.Inventory.Contents)
        {
            if (!item.canBeSold) continue;
            if (sourceFaction.Currency == item.Base) continue;
            MakeItemButton(item);
        }
    }

    private void MakeItemButton(Item_Instance item)
    {
        int itemHash = AssertUniqueHash(item.GetHashCode());
        scr_addSalesItem box = Instantiate(prefab_item);
        box.LoadItem(item, sourceFaction);
        RegisterButton(itemHash, box.Button, new Button_SelectSalesItem(this, item));
        box.GetComponent<RectTransform>().SetParent(itemList, false);
    }

    private void RegisterButton(int optionID, scr_SelectableText button, ButtonValidator validator)
    {
        if (!buttonsByID.ContainsKey(optionID))
        {
            button.Initialize(this, validator);
            button.optionID = optionID;
            buttonsByID.Add(button.optionID, button);
            validatorsByID.Add(button.optionID, button.Validator);
        }
    }

    protected override void Awake()
    {
        base.Awake();
        this.m_Canvas.overrideSorting = true;
        this.sourceFaction = null;
        button_alwaysValid = new ButtonValidator_AlwaysTrue(this);
    }

    public override void Initialize()
    {
        base.Initialize();

        foreach (scr_SelectableText button in GetComponentsInChildren<scr_SelectableText>(true))
        {
            switch (button.optionID)
            {
                case 9999: // exit
                    button.Initialize(this, button_alwaysValid); break;
                default:
                    button.Initialize(this, button_alwaysValid); break;
            }
            if (button.optionID != -1)
            {
                buttonsByID.Add(button.optionID, button);
                validatorsByID.Add(button.optionID, button.Validator);
            }
        }
    }

    public override void ValidateAll()
    {
        base.ValidateAll();
    }

    public override void Notify(int optionID)
    {
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
                case 9999: scr_System_SceneManager.current.UnloadLastCanvasFromScene(); break;
                default: break;
            }
        }
        ValidateAll();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        scr_System_CampaignManager.current.NotifyUpdate();
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // if click outside box
        if ((eventData.rawPointerPress.GetComponent<scr_Canvas_Management>() != null) || (eventData.button == PointerEventData.InputButton.Right && UtilityEX.isClickBelowDragThreshold(eventData)))
        {
            scr_System_SceneManager.current.UnloadLastCanvasFromScene();
        }
    }

    public class Button_SelectSalesItem : ButtonValidator, I_ButtonClickable
    {
        new scr_Menu_AddSalesOrder parent;
        Item_Instance item;
        public Button_SelectSalesItem(scr_Menu_AddSalesOrder parent, Item_Instance item) : base(parent)
        {
            this.parent = parent;
            this.item = item;
        }

        public override bool IsButtonValid()
        {
            if (parent.sourceFaction.SalesManager.IsSellingItem(item)) return false;
            if (!parent.sourceFaction.SalesManager.CanSellItem(item)) return false;
            return true;
        }

        public void OnClickButton()
        {
            parent.sourceFaction.SalesManager.AddSalesOrder(item);
        }
    }
}
