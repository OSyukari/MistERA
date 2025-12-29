using UnityEngine;
using System.Collections.Generic;
using Newtonsoft.Json;

[System.Serializable]
public class Masterlist_Items : MonoBehaviour
{
    public JsonSerializerSettings SerializerSettings = new JsonSerializerSettings() { TypeNameHandling = TypeNameHandling.Auto };

    public static Masterlist_Items Instance { get; private set; }

    [SerializeField] public Index_Item_Base Index = new Index_Item_Base();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
        DontDestroyOnLoad(gameObject);
    }

    public Dictionary<string, ItemComponentTemplate_Craftable_Recipe> CraftingRecipe = new Dictionary<string, ItemComponentTemplate_Craftable_Recipe>();

    public void AddCraftingRecipe(List<ItemComponentTemplate_Craftable_Recipe> recipeList)
    {
        foreach (var c in recipeList)
        {
            if (!CraftingRecipe.ContainsKey(c.RecipeUID))
            {
                CraftingRecipe.Add(c.RecipeUID, c);
            }
        }
    }
    public List<ItemComponentTemplate_Harvestable> FarmRecipe = new List<ItemComponentTemplate_Harvestable>();

    public void AddFarmRecipe(ItemComponentTemplate_Harvestable recipe)
    {
        if (!FarmRecipe.Contains(recipe)) FarmRecipe.Add(recipe);
    }
    public ItemComponentTemplate_Craftable_Recipe GetRecipeByID(string key)
    {
        return CraftingRecipe.ContainsKey(key) ? CraftingRecipe[key] : null;
    }

    public ItemComponentTemplate_Harvestable GetHarvestByID(string key)
    {
        return FarmRecipe.Find(x => x.compHarvestible_UID == key);
    }

    public static Item_Base GetByID(string id) { return Instance.Index.GetByID(id); }
}
