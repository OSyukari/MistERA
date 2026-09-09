using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;


public static class ItemUtility
{
    /// <summary>
    /// Has different calculation logic based on concrete or virtual item.
    /// </summary>
    /// <param name="items"></param>
    /// <returns></returns>
    public static float AveragePricing(List<Item_Instance> items)
    {
        float c = 0;
        float value = 0f;
        foreach (var item in items)
        {

            value += item.ValuePerItem * (item.isVirtualGood ? 1 : item.Count);
            c += (item.isVirtualGood ? 1 : item.Count);

        }
        if (c <= 0) return 0f;
        return value / c;
    }

    /// <summary>
    /// AveragePricing across every inventory item this order applies to, converted into
    /// faction's sales currency (see Manageable.GetPrice) and formatted for UI display.
    /// </summary>
    public static string AveragePricingString(SalesManager.ItemMatch order, Manageable faction)
    {
        if (order == null || faction == null || faction.Currency == null) return " - ";
        var allItems = faction.Inventory.Contents.FindAll(order.ApplicableTo);
        if (allItems.Count == 1) return faction.GetPricingLabel(allItems[0], true, order.isVirtualGood);

        var price = AveragePricing(faction.Inventory.Contents.FindAll(order.ApplicableTo)) / faction.Currency.value * (float)faction.priceMult;

        return faction.GetPricingLabel(price, true);
    }
}
