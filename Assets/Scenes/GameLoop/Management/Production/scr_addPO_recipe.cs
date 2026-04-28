using TMPro;
using UnityEngine;

public class scr_addPO_recipe : MonoBehaviour
{
    
    public void LoadRecipe(ItemComponentTemplate_Craftable_Recipe recipe)
    {
        this.recipe = recipe;
        this.itemName.SetText(recipe.DisplayName);
        this.itemName.SetExternalTooltip(recipe.Tooltip);
        this.workType.SetText(LocalizeDictionary.QueryThenParse("tag_"+ recipe.jobKeyword));
    }

    protected ItemComponentTemplate_Craftable_Recipe recipe;
    public scr_HoverableText itemName, workType;
    public scr_SelectableText Button;
}
