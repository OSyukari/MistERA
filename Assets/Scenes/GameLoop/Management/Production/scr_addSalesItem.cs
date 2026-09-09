using UnityEngine;

public class scr_addSalesItem : MonoBehaviour
{
    public Item_Instance item;
    public scr_HoverableText itemName;
    public scr_HoverableText pricing, ownedCount;
    public scr_SelectableText Button;

    public void LoadItem(Item_Instance item, Manageable faction)
    {
        this.item = item;
        this.itemName.SetText(item.DisplayName);
        this.itemName.SetExternalTooltip(item.Tooltip);
        this.pricing.SetText(faction.GetPricingLabel(item, isSell: true, perItem: true));
        this.ownedCount.SetText($"{item.Count}");
    }
}
