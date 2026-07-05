using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;

public class scr_Menu_RetailTrade : scr_Menu, IPointerClickHandler
{
    Manageable sourceFaction;
    public Manageable targetFaction;
    public RectTransform recipeList;
    public scr_retailTrade prefab_trade;
    public scr_HoverableText title2;

    public void InitializeWithArgument(Manageable sourceFaction, Manageable targetFaction, Action onExit)
    {
        this.onSelfExit = onExit;
        if (!initialized) Initialize();

        this.sourceFaction = sourceFaction;
        this.targetFaction = targetFaction;

        Utility.DestroyAllChildrenFrom( recipeList);

        foreach(var entry in targetFaction.salesInventory.Inventory)
        {
            MakeRecipeButton(entry.Value, sourceFaction, targetFaction);
        }

        title2.SetText($"{targetFaction.FactionDisplayName} -> {sourceFaction.FactionDisplayName}");

        ValidateAll();
    }

    List<scr_retailTrade> trackedBoxes = new List<scr_retailTrade>();

    private void MakeRecipeButton(ItemEntry entry, Manageable source, Manageable target)
    {
        //int recipeHash = AssertUniqueHash((entry.itemID+"|"+entry.itemCount.ToString()).GetHashCode());
        scr_retailTrade box = Instantiate(prefab_trade);
        box.LoadItemEntry(this, entry, source, target);
        RegisterButton(box.Button_Add.GetHashCode(), box.Button_Add, new Button_ModTradeCount(this, box, target, box.Button_Add, true));
        RegisterButton(box.Button_reduce.GetHashCode(), box.Button_reduce, new Button_ModTradeCount(this, box, target, box.Button_reduce, false));
        box.selfRect.SetParent(recipeList, false);

        trackedBoxes.Add(box);
    }

    private void RegisterButton(int optionID, scr_SelectableText button, ButtonValidator validator)
    {
        optionID = AssertUniqueHash(optionID);
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
                case 9998: // exit
                    button.Initialize(this, new Button_ConfirmTrade(this, button)); break;
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

    public Dictionary<ItemEntry, Manageable.TradeOrder> tradeCount = new Dictionary<ItemEntry, Manageable.TradeOrder>();

    protected void ModTradeCount(scr_retailTrade trade, ItemEntry item, bool isAdd)
    {
        if (!tradeCount.ContainsKey(item))
        {
            var cost = new ItemEntry(targetFaction.Currency.id, "", targetFaction.GetPrice(item, sourceFaction != targetFaction), false);
            var order = new Manageable.TradeOrder(sourceFaction, targetFaction, item, cost, 0, 0);
            tradeCount[item] = order;
        }

        tradeCount[item].AddCount((isAdd ? 1 : -1) * (UtilityEX.SHIFT ? 100 : UtilityEX.CTRL ? 10 : 1), item.innerStock);

        trade.UpdateCount();
    }

    public class Button_ModTradeCount : ButtonValidator, I_ButtonClickable
    {
        new scr_Menu_RetailTrade parent;
        ItemEntry entry;
        Manageable targetFaction;
        scr_retailTrade boxscript;
        scr_SelectableText button;
        bool isAdd = false;
        public Button_ModTradeCount(scr_Menu_RetailTrade parent, scr_retailTrade entry, Manageable targetFaction, scr_SelectableText button, bool isAdd) : base(parent)
        {
            this.parent = parent;
            this.boxscript = entry;
            this.entry = boxscript.entry;
            this.targetFaction = targetFaction;
            this.button = button;
            this.isAdd = isAdd;
        }

        public override bool IsButtonValid()
        {
            if (parent.sourceFaction == null || this.targetFaction == null || this.entry == null) return false;

            parent.tradeCount.TryGetValue(entry, out var tradeC);

            if (isAdd && (entry.innerStock == 0 || (tradeC != null && tradeC.Count >= entry.innerStock)))
            {
                tooltip = "out of stock";
                return false;
            }
            else if (!isAdd && (tradeC == null || tradeC.Count <= 0))
            {
                tooltip = "cannot reduce further";
                return false;
            }
            return true;
        }

        public void OnClickButton()
        {
            parent.ModTradeCount(boxscript, entry, isAdd);
        }
    }

    public void ResolveTrade()
    {
        sourceFaction.AddTradeOrder_ImmediateResolve(tradeCount);
    }

    public class Button_ConfirmTrade : ButtonValidator, I_ButtonClickable
    {
        new scr_Menu_RetailTrade parent;
        scr_SelectableText button;
        public Button_ConfirmTrade(scr_Menu_RetailTrade parent, scr_SelectableText button) : base(parent)
        {
            this.parent = parent;
            this.button = button;
                    }

        Dictionary<string, int> costMatrix = new Dictionary<string, int>();
        List<string> tooltipss = new List<string>();
        public override bool IsButtonValid()
        {
            costMatrix.Clear();
            tooltipss.Clear();

            foreach (var i in parent.tradeCount)
            {
                //if (self == target) return true;
                i.Value.AddDictionaryRecords(ref costMatrix);
            }
            bool validtrade = true;

            if (costMatrix.Count < 1)
            {
                tooltip = LocalizeDictionary.QueryThenParse("ui_management_production_retailTrade_none");
                return false;
            }

            tooltip = "";

            foreach (var kvp in costMatrix)
            {
                if (kvp.Value >= 0) continue;
                var count = parent.sourceFaction.Inventory.GetItemCount(kvp.Key);
                if (count + kvp.Value < 0)
                {
                    validtrade = false;
                    tooltipss.Add(Utility.WrapTextColor(
                        LocalizeDictionary.QueryThenParse("ui_management_production_retailTrade_missing")
                            .Replace("$name$", LocalizeDictionary.QueryThenParse(kvp.Key))
                            .Replace("$count$", $"{Math.Abs(kvp.Value)}")
                            .Replace("$owned$", $"{count}"),
                        scr_System_CentralControl.current.DisplaySetting.TextColor_conflict.Color));
                }
                else
                {
                    tooltipss.Add(LocalizeDictionary.QueryThenParse("ui_management_production_retailTrade_valid")
                            .Replace("$name$", LocalizeDictionary.QueryThenParse(kvp.Key))
                            .Replace("$count$", $"{Math.Abs(kvp.Value)}")
                            .Replace("$owned$", $"{count}"));
                }
            }
            tooltip = $"{(validtrade? "" : $"{LocalizeDictionary.QueryThenParse("ui_management_production_retailTrade_cannotparse")}\n")}{String.Join("\n", tooltipss)}";
            return validtrade;
        }

        public void OnClickButton()
        {
            parent.ResolveTrade();
            scr_System_SceneManager.current.UnloadLastCanvasFromScene();
        }
    }
}
