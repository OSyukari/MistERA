using TMPro;
using UnityEngine;

public class scr_addTrade : MonoBehaviour
{

    public void LoadItemEntry(ItemEntry entry, Manageable parentFaction, Manageable targetFaction)
    {
        this.entry = entry;
        this.faction = targetFaction;
        this.itemName.SetText(entry.Print);
        this.itemName.SetExternalTooltip(entry.Tooltip);
        this.factionName.text = parentFaction == targetFaction ? " - " : faction.FactionDisplayName;
        this.pricing.text = targetFaction.GetPricingLabel(entry, parentFaction != targetFaction);
        this.ownedCount.SetText($"{parentFaction.Inventory.GetItemCount(entry.itemID)}");
    }

    protected Manageable faction;
    protected ItemEntry entry;
    public scr_HoverableText itemName, ownedCount;
    public TMP_Text factionName, pricing;
    public scr_SelectableText Button;
}
