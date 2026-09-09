using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class scr_prefabretail_box : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    //public scr_SelectableText btn_disable;
    public scr_HoverableText ItemName;
    public TMP_Text ItemCount, OrderAmount;
    public scr_HoverableText pricing;
    public scr_SelectableText Button_orderType, ButtonMinus, ButtonPlus, Btn_action;
    public TMP_Text warningMsg;

    public Manageable targetFaction;
    public SalesManager.ItemMatch order = null;
    public CanvasGroup CanvasGroup;

    scr_Canvas_Management parent;

    string _cache_tooltip = null;

    public void UpdatePricingDisplay()
    {
        pricing.SetText(ItemUtility.AveragePricingString(order, targetFaction));
        // pricing set extra tooltip

        if (_cache_tooltip == null)
        {
            _cache_tooltip = LocalizeDictionary.QueryThenParse("ui_management_production_Trade_pricing_tooltip_each");
        }

        float expectedSalesMult = targetFaction != null ? targetFaction.SalesManager.GetExpectedSalesMultiplier(order) : 1f;
        pricing.SetExternalTooltip($"{_cache_tooltip}\nexpected sales mult x{expectedSalesMult:0.00}");
    }

    public void RegisterSO(scr_Canvas_Management canvas, Manageable faction, SalesManager.ItemMatch orderSO)
    {
        targetFaction = faction;
        this.order = orderSO;
        this.parent = canvas;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (parent == null) return;
        parent.initscript_production.CurrentSalesOrder = this.order;
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        if (parent == null) return;
        if (parent.initscript_production.CurrentSalesOrder == this.order) parent.initscript_production.CurrentSalesOrder = null;
    }
}
