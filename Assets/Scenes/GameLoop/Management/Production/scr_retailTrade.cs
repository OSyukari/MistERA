using TMPro;
using UnityEngine;

public class scr_retailTrade : MonoBehaviour
{
    scr_Menu_RetailTrade menu;
    public void LoadItemEntry(scr_Menu_RetailTrade menu, ItemEntry entry, Manageable parentFaction, Manageable targetFaction)
    {
        this.entry = entry;
        this.menu = menu;
        this.faction = targetFaction;
        this.itemName.SetText(entry.Print);
        this.itemName.SetExternalTooltip(entry.Tooltip);
        //this.factionName.text = parentFaction == targetFaction ? " - " : faction.FactionDisplayName;
        this.pricing.text = targetFaction.GetPricingLabel(entry, parentFaction != targetFaction);
        this.ownedCount.SetText($"{parentFaction.Inventory.GetItemCount(entry.itemID)}");

        this.stockCount.SetText($"{entry.innerStock}");
        if (menu.tradeCount.TryGetValue(entry, out var purchase)) this.purchaseCount.SetText($"{purchase.Count}");
        else this.purchaseCount.SetText($"{0}");
    }

    public void UpdateCount()
    {
        if (menu.tradeCount.TryGetValue(entry, out var purchase)) this.purchaseCount.SetText($"{purchase.Count}");
        else this.purchaseCount.SetText($"{0}");
    }


    public RectTransform selfRect;
    protected Manageable faction;
    public ItemEntry entry;
    public scr_HoverableText itemName, ownedCount, stockCount, purchaseCount;
    public TMP_Text pricing;
    public scr_SelectableText Button_Add, Button_reduce;
}
