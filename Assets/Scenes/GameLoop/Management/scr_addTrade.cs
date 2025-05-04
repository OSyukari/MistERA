using TMPro;
using UnityEngine;

public class scr_addTrade : MonoBehaviour
{

    public void LoadItemEntry(Manageable.ItemEntry entry, Manageable parentFaction, Manageable targetFaction)
    {
        this.entry = entry;
        this.faction = targetFaction;
        this.itemName.SetText(entry.Print);
        this.itemName.SetExternalTooltip(entry.Tooltip);
        this.factionName.text = parentFaction == targetFaction ? " - " : faction.FactionDisplayName;
        this.pricing.text = targetFaction.GetPricingLabel(entry, parentFaction != targetFaction);
    }

    protected Manageable faction;
    protected Manageable.ItemEntry entry;
    public scr_HoverableText itemName;
    public TMP_Text factionName, pricing;
    public scr_SelectableText Button;
}
