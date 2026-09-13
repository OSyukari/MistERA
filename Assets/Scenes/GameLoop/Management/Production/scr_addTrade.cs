using TMPro;
using UnityEngine;

public class scr_addTrade : MonoBehaviour
{

    public void LoadItemEntry(ItemEntry entry, Manageable parentFaction, Manageable targetFaction, bool isParentTarget = true)
    {
        this.entry = entry;
        this.itemName.SetText(entry.Print);
        this.itemName.SetExternalTooltip(entry.Tooltip);

        if (isParentTarget)
        {
            this.faction = parentFaction;
            this.ownedCount.SetText($"{targetFaction.Inventory.GetItemCount(entry.itemID)}");
        }
        else
        {
            this.faction = targetFaction;
            this.ownedCount.SetText($"{parentFaction.Inventory.GetItemCount(entry.itemID)}");
        }
        this.factionName.text = parentFaction == targetFaction ? " - " : faction.FactionDisplayName;
        this.pricing.text = targetFaction.GetPricingLabel(entry, parentFaction != targetFaction);
    }

    protected Manageable faction;
    protected ItemEntry entry;
    public scr_HoverableText itemName, ownedCount;
    public TMP_Text factionName, pricing;
    public scr_SelectableText Button;
}
