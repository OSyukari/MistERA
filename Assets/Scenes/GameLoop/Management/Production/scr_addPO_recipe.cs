using TMPro;
using UnityEngine;

public class scr_addPO_recipe : MonoBehaviour
{
    
    public void LoadRecipe(Manageable parentFaction, ItemComponentTemplate_Craftable_Recipe recipe)
    {
        this.recipe = recipe;
        this.itemName.SetText(recipe.DisplayName);
        this.itemName.SetExternalTooltip(recipe.Tooltip);
        this.workType.SetText(LocalizeDictionary.QueryThenParse("tag_"+ recipe.jobKeyword));
        this.ownedCount.SetText($"{parentFaction.Inventory.GetItemCount(recipe.outputItemBaseID)}");
    }

    protected ItemComponentTemplate_Craftable_Recipe recipe;
    public scr_HoverableText itemName, workType, ownedCount;
    public scr_SelectableText Button;
}
