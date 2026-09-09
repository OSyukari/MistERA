using NUnit;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using static scr_Canvas_Management;

public class initscript_production : MonoBehaviour
{
    public scr_Canvas_Management parent;
    public RectTransform selfTab;
    public RectTransform inventoryList;


    Manageable prev = null;
    public void Initialize(Manageable m)
    {
        if (prev == m || m == null) return;

        color_enable = scr_System_CentralControl.current.DisplaySetting.TextColor_neutral.Color;
        color_disable = scr_System_CentralControl.current.DisplaySetting.TextColor_disabled.Color;

        ClearAllOrderLists();

        RefreshPOList();
        RefreshTAList();
        RefreshSalesList();
        printedItems.Clear();


        Utility.DestroyAllChildrenFrom(inventoryList);
        foreach (var entry in parent.CurrentFaction.Inventory.ContentsPrintable)
        {
            var text = Instantiate(parent.prefab_text_link).GetComponent<scr_HoverableText>();
            text.SetText(entry.Print());
            text.SetExternalTooltip($"{(scr_System_CampaignManager.current.DebugMode ? $"{entry.RefID}\n" : "")}{entry.Tooltip}");
            text.transform.SetParent(inventoryList.transform, false);
            printedItems.Add(entry, text);
        }
        
        prev = m;
    }

    Color32 color_enable, color_disable;

    Dictionary<Item_Instance, scr_HoverableText> printedItems = new Dictionary<Item_Instance, scr_HoverableText>();
    SalesManager.ItemMatch _currentSalesOrder = null;
    public SalesManager.ItemMatch CurrentSalesOrder
    {
        get
        {
            return _currentSalesOrder;
        }
        set
        {
            if (_currentSalesOrder != value)
            {
                _currentSalesOrder = value;
                RefreshItemHighlights();
            }
        }
    }



    void RefreshItemHighlights()
    {
        foreach(var kvp in printedItems)
        {
            kvp.Value.SetColor(CurrentSalesOrder == null || CurrentSalesOrder.ApplicableTo(kvp.Key) ? color_enable : color_disable);
        }
    }

    public void RefreshSalesList()
    {
        foreach(var order in parent.CurrentFaction.SalesManager.salesOrder)
        {
            if (!loadSales_Removal.ContainsKey(order))
            {
                if (order.isVirtualGood) MakeSOButtonVirtual(order);
                else MakeSOButton(order);
            }
        }
    }

    public void RefreshPOList()
    {
        //Debug.LogError("REFRESHING PO LIST!");
        foreach (var order in parent.CurrentFaction.ProductionOrders) if (!loadOrders_Removal.ContainsKey(order)) MakePOButton(order);

        //foreach(var order in loadedOrders_daily) if (!currentFaction.ProductionOrdersDaily.Contains(order)) DeletePOButton(order)
    }

    // List<Manageable.ProductionOrder> loadedOrders = null;
    Dictionary<Manageable.ProductionOrder, button_ManageProductionOrder_RemoveCount> loadOrders_Removal = new Dictionary<Manageable.ProductionOrder, button_ManageProductionOrder_RemoveCount>();

    Dictionary<Manageable.TradeOrder, button_ManageTradeOrder_RemoveCount> loadTrades_Removal = new Dictionary<Manageable.TradeOrder, button_ManageTradeOrder_RemoveCount>();

    Dictionary<SalesManager.ItemMatch, button_ManageSalesOrder_RemoveCount> loadSales_Removal = new Dictionary<SalesManager.ItemMatch, button_ManageSalesOrder_RemoveCount>();
    public void RefreshTAList()
    {
        foreach (var trade in parent.CurrentFaction.TradeOrders) if (!loadTrades_Removal.ContainsKey(trade)) MakeTOButton(trade);
    }

    public scr_prOrderManage prefab_POEntry;
    public RectTransform list_orders, list_trades, list_retail;
    public scr_prefabTransactionManage prefab_TAEntry;
    private void MakePOButton(Manageable.ProductionOrder order)
    {
        int recipeHash = parent.AssertUniqueHashPublic(order.GetHashCode()) * 4;
        //if (loadOrders_Hash.ContainsKey(order)) return;

        scr_prOrderManage entry = Instantiate(prefab_POEntry);
        entry.itemName.SetText(order.Recipe.DisplayName);
        entry.itemName.SetExternalTooltip(order.Recipe.Tooltip);
        entry.itemCount.text = parent.CurrentFaction.Inventory.GetItemCount(order.Recipe.outputItemBaseID).ToString();
        entry.orderAmount.text = order.CountABS.ToString();
        RectTransform rect = entry.GetComponent<RectTransform>();

        //entry.expectedWorkLoad.text = (order.Recipe.workAmount).ToString();

        parent.RegisterButton(recipeHash + 1, entry.buttonPlus, new button_ManageProductionOrder_AddCount(parent, entry.orderAmount, order));
        parent.RegisterButton(recipeHash + 2, entry.button_orderType, new button_ManageProductionOrder_ChangeType(parent, entry.button_orderType, order));
        parent.RegisterButton(recipeHash + 3, entry.buttonMinus, new button_ManageProductionOrder_ReduceCount(parent, entry.orderAmount, order));
        // the following validator also responsible for displaying warning message
        var remover = new button_ManageProductionOrder_RemoveCount(parent, recipeHash, order, entry.warningMsg, entry);
        parent.RegisterButton(recipeHash, entry.btn_action, remover);

        rect.SetParent(list_orders, false);
        entry.RegisterPO(parent, parent.CurrentFaction, order);
        loadOrders_Removal.Add(order, remover);
    }


    private void MakeTOButton(Manageable.TradeOrder order)
    {
        //TODO
        int recipeHash = parent.AssertUniqueHashPublic(order.GetHashCode()) * 4;
        scr_prefabTransactionManage entry = Instantiate(prefab_TAEntry);
        entry.ItemName.SetText(order.Display);
        entry.ItemName.SetExternalTooltip(order.Tooltip);
        entry.ItemCount.text = parent.CurrentFaction.Inventory.GetItemCount(order.Entry.itemID).ToString();
        entry.FactionName.text = order.TargetFaction == parent.CurrentFaction ? " - " : order.TargetFaction.FactionDisplayName;

        RectTransform rect = entry.GetComponent<RectTransform>();

        parent.RegisterButton(recipeHash + 1, entry.ButtonPlus, new button_ManageTradeOrder_AddCount(parent, entry.OrderAmount, order, entry));
        parent.RegisterButton(recipeHash + 2, entry.Button_orderType, new button_ManageTradeOrder_ChangeType(parent, entry.Button_orderType, order));
        parent.RegisterButton(recipeHash + 3, entry.ButtonMinus, new button_ManageTradeOrder_ReduceCount(parent, entry.OrderAmount, order, entry));
        var remover = new button_ManageTradeOrder_RemoveCount(parent, recipeHash, order, entry.warningMsg, entry);
        parent.RegisterButton(recipeHash, entry.Btn_action, remover);

        rect.SetParent(list_trades, false);
        entry.RegisterTR(parent, parent.CurrentFaction, order);
        entry.UpdatePricingDisplay();
        loadTrades_Removal.Add(order, remover);
    }

    public scr_prefabretail_box prefab_salesEntry, prefab_salesEntry_virtual;
    private void MakeSOButton(SalesManager.ItemMatch order)
    {
        int recipeHash = parent.AssertUniqueHashPublic(order.GetHashCode()) * 5;
        scr_prefabretail_box entry = Instantiate(prefab_salesEntry);
        entry.ItemName.SetText(order.Display);

        entry.ItemCount.text = order.CountInInventoryString(parent.CurrentFaction.Inventory);
        entry.OrderAmount.text = order.OrderCountString;

        RectTransform rect = entry.GetComponent<RectTransform>();

        parent.RegisterButton(recipeHash + 1, entry.ButtonPlus, new button_ManageSalesOrder_AddCount(parent, entry.OrderAmount, order));
        parent.RegisterButton(recipeHash + 2, entry.Button_orderType, new button_ManageSalesOrder_ChangeType(parent, entry.Button_orderType, order, parent.CurrentFaction.Inventory));
        parent.RegisterButton(recipeHash + 3, entry.ButtonMinus, new button_ManageSalesOrder_ReduceCount(parent, entry.OrderAmount, order));
        //parent.RegisterButton(recipeHash + 4, entry.btn_disable, new button_ManageSalesOrder_ToggleDisable(parent, entry.btn_disable, order, entry));
        var remover = new button_ManageSalesOrder_RemoveCount(parent, recipeHash, order, entry);
        parent.RegisterButton(recipeHash, entry.Btn_action, remover);

        rect.SetParent(list_retail, false);
        entry.RegisterSO(parent, parent.CurrentFaction, order);
        entry.UpdatePricingDisplay();
        loadSales_Removal.Add(order, remover);
    }
    private void MakeSOButtonVirtual(SalesManager.ItemMatch order)
    {
        int recipeHash = parent.AssertUniqueHashPublic(order.GetHashCode()) * 5;
        scr_prefabretail_box entry = Instantiate(prefab_salesEntry_virtual);
        entry.ItemName.SetText(order.Display);

        entry.ItemCount.text = order.CountInInventoryString(parent.CurrentFaction.Inventory);
        //entry.OrderAmount.text = order.OrderCountString;

        RectTransform rect = entry.GetComponent<RectTransform>();

        //parent.RegisterButton(recipeHash + 1, entry.ButtonPlus, new button_ManageSalesOrder_AddCount(parent, entry.OrderAmount, order));
        parent.RegisterButton(recipeHash + 2, entry.Button_orderType, new button_ManageSalesOrder_ChangeType(parent, entry.Button_orderType, order, parent.CurrentFaction.Inventory));
        //parent.RegisterButton(recipeHash + 3, entry.ButtonMinus, new button_ManageSalesOrder_ReduceCount(parent, entry.OrderAmount, order));
        //parent.RegisterButton(recipeHash + 4, entry.btn_disable, new button_ManageSalesOrder_ToggleDisable(parent, entry.btn_disable, order, entry));
        var remover = new button_ManageSalesOrder_RemoveCount(parent, recipeHash, order, entry);
        parent.RegisterButton(recipeHash, entry.Btn_action, remover);

        rect.SetParent(list_retail, false);
        entry.RegisterSO(parent, parent.CurrentFaction, order);
        entry.UpdatePricingDisplay();
        loadSales_Removal.Add(order, remover);
    }
    public void DestroySOMButton(SalesManager.ItemMatch so, int recipeHash)
    {
        parent.DestroyCOMButton(recipeHash);
        parent.DestroyCOMButton(recipeHash + 1);
        parent.DestroyCOMButton(recipeHash + 2);
        parent.DestroyCOMButton(recipeHash + 3);
        //parent.DestroyCOMButton(recipeHash + 4);
        loadSales_Removal.Remove(so);
    }
    // RefreshPOList/RefreshTAList/RefreshSalesList only add rows for orders not yet tracked, so
    // switching to a different faction leaves the previous faction's rows (and their button
    // registrations) behind. Clear all three lists out before rebuilding for the newly selected faction.
    private void ClearAllOrderLists()
    {
        foreach (var order in new List<Manageable.ProductionOrder>(loadOrders_Removal.Keys))
            DestroyPOMButton(order, loadOrders_Removal[order].ButtonID);
        Utility.DestroyAllChildrenFrom(list_orders);

        foreach (var trade in new List<Manageable.TradeOrder>(loadTrades_Removal.Keys))
            DestroyTOMButton(trade, loadTrades_Removal[trade].ButtonID);
        Utility.DestroyAllChildrenFrom(list_trades);

        foreach (var so in new List<SalesManager.ItemMatch>(loadSales_Removal.Keys))
            DestroySOMButton(so, loadSales_Removal[so].ButtonID);
        Utility.DestroyAllChildrenFrom(list_retail);
    }




    /// <summary>
    /// THIS IS NOT BEING USED AT ALL RIGHT ???
    /// </summary>
    /// <param name="order"></param>
    public void DestroyPOMButton(Manageable.ProductionOrder po, int recipeHash)
    {
        //// ??????
        parent.DestroyCOMButton(recipeHash);
        parent.DestroyCOMButton(recipeHash + 1);
        parent.DestroyCOMButton(recipeHash + 2);
        parent.DestroyCOMButton(recipeHash + 3);
        loadOrders_Removal.Remove(po);
    }

    public void DestroyTOMButton(Manageable.TradeOrder to, int recipeHash)
    {
        //// ??????
        parent.DestroyCOMButton(recipeHash);
        parent.DestroyCOMButton(recipeHash + 1);
        parent.DestroyCOMButton(recipeHash + 2);
        parent.DestroyCOMButton(recipeHash + 3);
        loadTrades_Removal.Remove(to);
    }



    public class button_ManageProductionOrder_RemoveCount : ButtonValidator, I_ButtonClickable
    {

        new scr_Canvas_Management parent;
        Manageable.ProductionOrder order;
        TMP_Text warning;
        scr_prOrderManage parentRect;

        Color32 conflictColor;
        string alert_hours, alert_items;
        int buttonID;
        public int ButtonID { get { return buttonID; } }
        public button_ManageProductionOrder_RemoveCount(scr_Canvas_Management parent, int buttonID, Manageable.ProductionOrder order, TMP_Text warning, scr_prOrderManage parentRect) : base(parent)
        {
            this.parent = parent;
            this.order = order;
            this.buttonID = buttonID;
            this.parentRect = parentRect;
            this.warning = warning;
            // this.tooltip = "Delete This Order\nThis functionality is currently disabled";
            this.conflictColor = scr_System_CentralControl.current.DisplaySetting.TextColor_conflict.Color;
            alert_hours = LocalizeDictionary.QueryThenParse("ui_management_production_missingHours");
            alert_items = LocalizeDictionary.QueryThenParse("ui_management_production_missingResource");
        }

        public override bool IsButtonValid()
        {
            // modify warning message
            this.warning.text = "";
            var texts = new List<string>();
            if (this.order.Count > 0)
            {
                foreach (var i in order.Recipe.itemRequirements) if (!parent.CurrentFaction.resourceWarnings.ContainsKey(i.itemID) || parent.CurrentFaction.resourceWarnings[i.itemID] < 0) texts.Add(alert_items.Replace("$itemname$", i.Print));
                if (!parent.CurrentFaction.productionWarnings.ContainsKey(order.Recipe.jobKeyword) || parent.CurrentFaction.productionWarnings[order.Recipe.jobKeyword] < 0) texts.Add(alert_hours.Replace("$comname$", "tag_" + order.Recipe.jobKeyword));
                this.warning.text = texts.Count > 0 ? Utility.WrapTextColor(String.Join(" ", texts), conflictColor) : "";
            }

            if (!parent.CurrentFaction.HasProductionOrder(order))
            {
                tooltip = "This Production Order no longer exists.";
                return false;
            }
            return true;
        }

        public void OnClickButton()
        {
            parent.CurrentFaction.RemoveProductionOrder(order);
            parentRect.gameObject.SetActive(false);
            parent.initscript_production.DestroyPOMButton(order, buttonID);
            DestroyImmediate(parentRect.gameObject);
            //text.text = order.Count.ToString();
        }
    }

    public class Button_LoadCanvas_AddPO : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Management parent;
        public Button_LoadCanvas_AddPO(scr_Canvas_Management parent) : base(parent)
        {
            this.parent = parent;
        }

        public void OnChildExit()
        {
            //Debug.LogError("UNIMPLEMENTED");
            this.parent.initscript_production.RefreshPOList();
        }

        public override bool IsButtonValid()
        {
            return parent.canvas_AddPO != null;
        }
        public void OnClickButton()
        {
            scr_menu_AddProductionOrder cvs = scr_System_SceneManager.current.LoadCanvasIntoScene(parent, parent.canvas_AddPO).GetComponent<scr_menu_AddProductionOrder>();
            cvs.InitializeWithArgument(this.parent.CurrentFaction, OnChildExit);
        }
    }

    public class Button_LoadCanvas_AddTR : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Management parent;
        public Button_LoadCanvas_AddTR(scr_Canvas_Management parent) : base(parent)
        {
            this.parent = parent;
        }

        public override bool IsButtonValid()
        {
            return parent.canvas_AddTR != null;
        }

        protected void OnChildExit()
        {
            //Debug.LogError("UNIMPLEMENTED");
            this.parent.initscript_production.RefreshTAList();
        }

        public void OnClickButton()
        {
            scr_Menu_AddTrade cvs = scr_System_SceneManager.current.LoadCanvasIntoScene(parent, parent.canvas_AddTR).GetComponent<scr_Menu_AddTrade>();
            cvs.InitializeWithArgument(this.parent.CurrentFaction, OnChildExit);
        }
    }

    public class Button_LoadCanvas_AddSO : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Management parent;
        public Button_LoadCanvas_AddSO(scr_Canvas_Management parent) : base(parent)
        {
            this.parent = parent;
        }

        public override bool IsButtonValid()
        {
            return parent.canvas_AddSO != null;
        }

        protected void OnChildExit()
        {
            this.parent.initscript_production.RefreshSalesList();
        }

        public void OnClickButton()
        {
            scr_Menu_AddSalesOrder cvs = scr_System_SceneManager.current.LoadCanvasIntoScene(parent, parent.canvas_AddSO).GetComponent<scr_Menu_AddSalesOrder>();
            cvs.InitializeWithArgument(this.parent.CurrentFaction, OnChildExit);
        }
    }

    public class Button_LoadCanvas_AddTransfer : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Management parent;
        public Button_LoadCanvas_AddTransfer(scr_Canvas_Management parent) : base(parent)
        {
            this.parent = parent;
            notarget = LocalizeDictionary.QueryThenParse("ui_management_production_addTransfer_nofaction");
        }
        string notarget;
        public override bool IsButtonValid()
        {
            if (scr_System_CampaignManager.current.Player.FactionManager.ManagerFactions.Count < 2)
            {
                tooltip = notarget;
                return false;
            }
            return parent.canvas_AddTransfer != null;
        }

        protected void OnChildExit()
        {
            this.parent.initscript_production.RefreshTAList();
        }

        public void OnClickButton()
        {
            scr_AddTransfer cvs = scr_System_SceneManager.current.LoadCanvasIntoScene(parent, parent.canvas_AddTransfer).GetComponent<scr_AddTransfer>();
            cvs.InitializeWithArgument(this.parent.CurrentFaction, OnChildExit);
        }
    }



    public class button_ManageTradeOrder_AddCount : ButtonValidator, I_ButtonClickable
    {

        new scr_Canvas_Management parent;
        Manageable.TradeOrder order;
        TMP_Text text;
        scr_prefabTransactionManage entry;
        public button_ManageTradeOrder_AddCount(scr_Canvas_Management parent, TMP_Text text, Manageable.TradeOrder order, scr_prefabTransactionManage entry) : base(parent)
        {
            this.parent = parent;
            this.order = order;
            this.text = text;
            this.entry = entry;
        }

        public override bool IsButtonValid()
        {
            if (!parent.CurrentFaction.HasTradeOrder(order))
            {
                tooltip = "This Trade Order no longer exists.";
                return false;
            }
            text.text = order.CountABS.ToString();
            return true;
        }

        public void OnClickButton()
        {
            if (UtilityEX.SHIFT && UtilityEX.CTRL) order.AddCount(1000);
            else if (UtilityEX.SHIFT) order.AddCount(100);
            else if (UtilityEX.CTRL) order.AddCount(10);
            else order.AddCount(1);
            entry.UpdatePricingDisplay();
            //text.text = order.Count.ToString();
            //expectedWork.text = ((int)Math.Ceiling(order.Count * order.Recipe.workAmount / 60f)).ToString();
        }
    }

    public class button_ManageTradeOrder_ChangeType : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Management parent;
        Manageable.TradeOrder order;
        scr_SelectableText text;
        public button_ManageTradeOrder_ChangeType(scr_Canvas_Management parent, scr_SelectableText text, Manageable.TradeOrder order) : base(parent)
        {
            this.parent = parent;
            this.order = order;
            this.text = text;
        }
        public override bool IsButtonValid()
        {
            this.text.SetText(LocalizeDictionary.QueryThenParse(order.orderType.ToString()));
            return true;
        }
        public void OnClickButton()
        {
            order.orderType = 1 - order.orderType;
        }
    }

    public class button_ManageTradeOrder_ReduceCount : ButtonValidator, I_ButtonClickable
    {

        new scr_Canvas_Management parent;
        Manageable.TradeOrder order;
        TMP_Text text;
        scr_prefabTransactionManage entry;
        public button_ManageTradeOrder_ReduceCount(scr_Canvas_Management parent, TMP_Text text, Manageable.TradeOrder order, scr_prefabTransactionManage entry) : base(parent)
        {
            this.parent = parent;
            this.order = order;
            this.text = text;
            this.entry = entry;
        }

        public override bool IsButtonValid()
        {
            if (!parent.CurrentFaction.HasTradeOrder(order))
            {
                tooltip = "This Production Order no longer exists.";
                return false;
            }
            text.text = order.CountABS.ToString();
            if (order.CountABS > 0) return true;
            else return false;
        }

        public void OnClickButton()
        {
            //parent.currentFaction.AddProductionOrder(order.Recipe, -1);
            if (UtilityEX.SHIFT && UtilityEX.CTRL) order.AddCount(-1000);
            else if (UtilityEX.SHIFT) order.AddCount(-100);
            else if (UtilityEX.CTRL) order.AddCount(-10);
            else order.AddCount(-1);
            entry.UpdatePricingDisplay();

            //expectedWork.text = ((int) Math.Ceiling( order.Count * order.Recipe.workAmount / 60f)).ToString();
        }
    }


    public class button_ManageTradeOrder_RemoveCount : ButtonValidator, I_ButtonClickable
    {

        new scr_Canvas_Management parent;
        Manageable.TradeOrder order;
        TMP_Text warning;
        scr_prefabTransactionManage parentRect;

        Color32 conflictColor;
        string alert_hours, alert_items;
        int buttonID;
        public int ButtonID { get { return buttonID; } }
        public button_ManageTradeOrder_RemoveCount(scr_Canvas_Management parent, int buttonID, Manageable.TradeOrder order, TMP_Text warning, scr_prefabTransactionManage parentRect) : base(parent)
        {
            this.parent = parent;
            this.order = order;
            this.buttonID = buttonID;
            this.parentRect = parentRect;
            this.warning = warning;
            // this.tooltip = "Delete This Order\nThis functionality is currently disabled";
            this.conflictColor = scr_System_CentralControl.current.DisplaySetting.TextColor_conflict.Color;
            alert_hours = LocalizeDictionary.QueryThenParse("ui_management_production_missingHours");
            alert_items = LocalizeDictionary.QueryThenParse("ui_management_production_missingResource");
        }

        public override bool IsButtonValid()
        {
            // modify warning message
            this.warning.text = "";
            var texts = new List<string>();
            if (this.order.Count > 0)
            {
                // reversed orders pay out Entry instead of Cost (see TradeOrder.ProcessOrder) - check
                // whichever one this faction is actually on the hook for.
                var paidItem = order.reversed ? order.Entry : order.Cost;
                if (paidItem.itemID != "") if (!parent.CurrentFaction.resourceWarnings.ContainsKey(paidItem.itemID) || parent.CurrentFaction.resourceWarnings[paidItem.itemID] < 0) texts.Add(alert_items.Replace("$itemname$", paidItem.PrintName));
                //if (!parent.CurrentFaction.productionWarnings.ContainsKey(order.Recipe.jobKeyword) || parent.CurrentFaction.productionWarnings[order.Recipe.jobKeyword] < 0) texts.Add(alert_hours.Replace("$comname$", order.Recipe.jobKeyword));
                this.warning.text = texts.Count > 0 ? Utility.WrapTextColor(String.Join(" ", texts), conflictColor) : "";
            }

            if (!parent.CurrentFaction.HasTradeOrder(order))
            {
                tooltip = "This Production Order no longer exists.";
                return false;
            }
            return true;
        }

        public void OnClickButton()
        {
            parent.CurrentFaction.RemoveTradeOrder(order);
            parentRect.gameObject.SetActive(false);
            parent.initscript_production.DestroyTOMButton(order, buttonID);
            DestroyImmediate(parentRect.gameObject);
            //text.text = order.Count.ToString();
        }
    }


    public class button_ManageSalesOrder_AddCount : ButtonValidator, I_ButtonClickable
    {

        new scr_Canvas_Management parent;
        SalesManager.ItemMatch order;
        TMP_Text text;
        bool isVirtual = false;
        public button_ManageSalesOrder_AddCount(scr_Canvas_Management parent, TMP_Text text, SalesManager.ItemMatch order) : base(parent)
        {
            this.parent = parent;
            this.order = order;
            this.text = text;
            this.isVirtual = order.isVirtualGood;
            if (isVirtual) text.gameObject.SetActive(false);
        }

        public override bool IsButtonValid()
        {
            if (isVirtual)
            {
                tooltip = "digital sale does not need limit";
                return false;
            }
            if (!parent.CurrentFaction.HasSalesOrder(order))
            {
                tooltip = "This Sales Order no longer exists.";
                return false;
            }
            text.text = order.orderCount.ToString();
            if (order.isDisabled) return false;
            return true;
        }

        public void OnClickButton()
        {
            if (UtilityEX.SHIFT && UtilityEX.CTRL) order.orderCount += 1000;
            else if (UtilityEX.SHIFT) order.orderCount += 100;
            else if (UtilityEX.CTRL) order.orderCount += 10;
            else order.orderCount += 1;
        }
    }

    public class button_ManageSalesOrder_ChangeType : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Management parent;
        SalesManager.ItemMatch order;
        scr_SelectableText text;
        bool isVirtual = false;
        Inventory inv;
        public button_ManageSalesOrder_ChangeType(scr_Canvas_Management parent, scr_SelectableText text, SalesManager.ItemMatch order, Inventory inv) : base(parent)
        {
            this.parent = parent;
            this.order = order;
            this.text = text;
            this.isVirtual = order.isVirtualGood;
            this.text.useDisabledColorWhenUntoggled = true;
            this.text.isButtonToggle = true;
            this.inv = inv;
        }

        bool init = false;
        bool disabled = false;

        public override bool IsButtonValid()
        {
            if (isVirtual)
            {
                if (!init)
                {
                    if (order.CountInInventory(inv) < 1)
                    {
                        disabled = true;
                        tooltip = LocalizeDictionary.QueryThenParse("ui_management_sales_amount_nonexist_tooltip");
                    }
                    init = true;
                }
                if (disabled)
                {
                    text.Toggle(true, false);
                    return false;
                }
                else
                {
                    text.Toggle(true, !order.isDisabled);
                }
            }
            else
            {
                this.text.SetText(LocalizeDictionary.QueryThenParse(order.orderType.ToString()));
            }
            return true;
        }
        public void OnClickButton()
        {
            if (isVirtual)
            {
                order.isDisabled = !order.isDisabled;
            }
            else
            {
                order.orderType = 1 - order.orderType;
            }
        }
    }

    public class button_ManageSalesOrder_ReduceCount : ButtonValidator, I_ButtonClickable
    {

        new scr_Canvas_Management parent;
        SalesManager.ItemMatch order;
        TMP_Text text;
        bool isVirtual = false;
        public button_ManageSalesOrder_ReduceCount(scr_Canvas_Management parent, TMP_Text text, SalesManager.ItemMatch order) : base(parent)
        {
            this.parent = parent;
            this.order = order;
            this.text = text;
            this.isVirtual = order.isVirtualGood;
            if (isVirtual) text.gameObject.SetActive(false);
        }

        public override bool IsButtonValid()
        {
            if (isVirtual)
            {
                tooltip = "digital sale does not need limit";
                return false;
            }
            if (!parent.CurrentFaction.HasSalesOrder(order))
            {
                tooltip = "This Sales Order no longer exists.";
                return false;
            }
            text.text = order.orderCount.ToString();
            if (order.isDisabled) return false;
            return order.orderCount > 0;
        }

        public void OnClickButton()
        {
            if (UtilityEX.SHIFT && UtilityEX.CTRL) order.orderCount = Math.Max(0, order.orderCount - 1000);
            else if (UtilityEX.SHIFT) order.orderCount = Math.Max(0, order.orderCount - 100);
            else if (UtilityEX.CTRL) order.orderCount = Math.Max(0, order.orderCount - 10);
            else order.orderCount = Math.Max(0, order.orderCount - 1);
        }
    }

    public class button_ManageSalesOrder_ToggleDisable : ButtonValidator, I_ButtonClickable
    {
        new scr_Canvas_Management parent;
        SalesManager.ItemMatch order;
        scr_SelectableText text;
        scr_prefabretail_box entry;
        public button_ManageSalesOrder_ToggleDisable(scr_Canvas_Management parent, scr_SelectableText text, SalesManager.ItemMatch order, scr_prefabretail_box entry) : base(parent)
        {
            this.parent = parent;
            this.order = order;
            this.text = text;
            this.entry = entry;
        }
        public override bool IsButtonValid()
        {
            this.text.SetText(LocalizeDictionary.QueryThenParse(order.isDisabled ? "ui_management_production_sales_disabled" : "ui_management_production_sales_enabled", order.isDisabled ? "Disabled" : "Enabled"));
            entry.CanvasGroup.alpha = order.isDisabled ? 0.5f : 1f;
            return true;
        }
        public void OnClickButton()
        {
        }
    }

    public class button_ManageSalesOrder_RemoveCount : ButtonValidator, I_ButtonClickable
    {

        new scr_Canvas_Management parent;
        SalesManager.ItemMatch order;
        int buttonID;
        scr_prefabretail_box parentRect;
        public int ButtonID { get { return buttonID; } }
        public button_ManageSalesOrder_RemoveCount(scr_Canvas_Management parent, int buttonID, SalesManager.ItemMatch order, scr_prefabretail_box parentRect) : base(parent)
        {
            this.parent = parent;
            this.order = order;
            this.buttonID = buttonID;
            this.parentRect = parentRect;
        }

        public override bool IsButtonValid()
        {
            if (!parent.CurrentFaction.HasSalesOrder(order))
            {
                tooltip = "This Sales Order no longer exists.";
                return false;
            }
            return true;
        }

        public void OnClickButton()
        {
            parent.CurrentFaction.RemoveSalesOrder(order);
            parentRect.gameObject.SetActive(false);
            parent.initscript_production.DestroySOMButton(order, buttonID);
            DestroyImmediate(parentRect.gameObject);
        }
    }


}
