using Cysharp.Threading.Tasks.Triggers;
using System;
using UnityEngine;
using UnityEngine.EventSystems;

public class scr_Menu_AddTrade : scr_Menu, IPointerClickHandler
{
    public Manageable sourceFaction;
    public RectTransform recipeList;
    public scr_addTrade prefab_trade;

    // TODO
    public RectTransform factionList;
    public prefab_factionRect prefab_factionRect;
    public Manageable currentTargetFaction;
    // 

    public void InitializeWithArgument(Manageable sourceFaction, Action onExit)
    {
        this.onSelfExit = onExit;
        if (!initialized) Initialize();

        this.sourceFaction = sourceFaction;
        Utility.DestroyAllChildrenFrom( recipeList);

        BuildFaction(sourceFaction, sourceFaction);


        foreach(var connect in sourceFaction.TradePartnerFactions)
        {
            BuildFaction(connect, sourceFaction);
        }
        ValidateAll();
    }

    void BuildFaction(Manageable faction, Manageable source)
    {
        if (faction.salesInventory.Inventory.Count < 1) return;

        if (currentTargetFaction == null) currentTargetFaction = faction;

        var factionRect = Instantiate(prefab_factionRect);
        factionRect.selfRect.SetParent(recipeList, false);

        // make button
        var txt = Instantiate(prefab_text_linkbutton);
        var btn = txt.GetComponent<scr_SelectableText>();
        btn.SetTextPreInit(faction.FactionDisplayName);
        btn.SelfRect.SetParent(factionList, false);
        var hash = AssertUniqueHash(faction.GetHashCode());
        RegisterButton(hash, btn, new ButtonValidator_ToggleFaction(this, btn, faction, factionRect.selfRect));

        foreach (var entry in faction.salesInventory.Inventory)
        {
            // TODO INSTANTIATE BUTTON
            MakeRecipeButton(entry.Value, faction, source, factionRect.selfRect);
        }

    }



    public void NotifyAddTrade(ItemEntry entry, Manageable targetFaction)
    {
        var cost = new ItemEntry(targetFaction.Currency.id, "", targetFaction.GetPrice(entry, sourceFaction != targetFaction), false);
        sourceFaction.AddTradeOrder(entry, cost, targetFaction, 0);
        scr_System_SceneManager.current.UnloadLastCanvasFromScene();
    }

    private void MakeRecipeButton(ItemEntry entry, Manageable source, Manageable target, RectTransform parent)
    {
        int recipeHash = AssertUniqueHash((entry.itemID+"|"+entry.itemCount.ToString()).GetHashCode());
        scr_addTrade box = Instantiate(prefab_trade);
        box.LoadItemEntry(entry, source, target, true);
        // reversed source and target
        RegisterButton(recipeHash, box.Button, new Button_SelectTrade(this, entry, source, box.Button));
        box.GetComponent<RectTransform>().SetParent(parent, false);
    }

    private void RegisterButton(int optionID, scr_SelectableText button, ButtonValidator validator)
    {
        if (!buttonsByID.ContainsKey(optionID))
        {
            button.Initialize(this, validator);
            button.optionID = optionID;
            buttonsByID.Add(button.optionID, button);
            validatorsByID.Add(button.optionID, button.Validator);
            //button.Validate();
            // return true;
        }
        // else return false;
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
            // Debug.Log("Button " + button + " " + button.optionID);
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
        // build all presetLis

    }

    public override void ValidateAll()
    {
        base.ValidateAll();
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
                case 9999: scr_System_SceneManager.current.UnloadLastCanvasFromScene(); break;
                default: break;
            }
        }
        ValidateAll();
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        /*
        while (list_Jobs.transform.childCount > 0)
        {
            DestroyImmediate(list_Jobs.transform.GetChild(0).gameObject);
        }*/
        //Debug.LogError("CANVAS MANAGEMENT ONDESTROY");
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

    public class ButtonValidator_ToggleFaction : ButtonValidator, I_ButtonClickable
    {
        new scr_Menu_AddTrade parent;
        scr_SelectableText button;
        Manageable faction;
        RectTransform rect;

        public ButtonValidator_ToggleFaction(scr_Menu_AddTrade parent, scr_SelectableText button, Manageable faction, RectTransform rect) : base(parent)
        {
            this.parent = parent;
            this.button = button;
            this.faction = faction;
            this.rect = rect;

            button.isButtonToggle = true;
        }

        public override bool IsButtonValid()
        {
            if (!button.gameObject.activeInHierarchy) return false;
            button.Toggle(true, parent.currentTargetFaction == faction);
            rect.gameObject.SetActive(parent.currentTargetFaction == faction);
            return true;
        }

        public void OnClickButton()
        {
            parent.currentTargetFaction = faction;

        }

    }

    public class Button_SelectTrade : ButtonValidator, I_ButtonClickable
    {
        new scr_Menu_AddTrade parent;
        ItemEntry entry;
        Manageable targetFaction;
        scr_SelectableText button;
        public Button_SelectTrade(scr_Menu_AddTrade parent, ItemEntry entry, Manageable targetFaction, scr_SelectableText button) : base(parent)
        {
            this.parent = parent;
            this.entry = entry;
            this.targetFaction = targetFaction;
            this.button = button;
        }

        public override bool IsButtonValid()
        {
            return parent.sourceFaction != null && this.targetFaction != null && this.entry != null;
        }

        public void OnClickButton()
        {
            parent.NotifyAddTrade(entry, targetFaction);
        }
    }
}
