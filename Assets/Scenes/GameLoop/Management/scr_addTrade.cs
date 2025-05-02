using TMPro;
using UnityEngine;

public class scr_addTrade : MonoBehaviour
{

    public void LoadItemEntry(Manageable.ItemEntry entry, Manageable parentFaction, Manageable targetFaction)
    {
        this.entry = entry;
        this.faction = targetFaction;
        this.itemName.text = entry.Print;
        this.factionName.text = parentFaction == targetFaction ? " - " : faction.FactionDisplayName;
        this.pricing.text = "-";
    }

    protected Manageable faction;
    protected Manageable.ItemEntry entry;
    public TMP_Text itemName, factionName, pricing;
    public scr_SelectableText Button;
}
