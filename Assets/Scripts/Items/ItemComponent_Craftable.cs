using System;
using System.Collections.Generic;
using Newtonsoft.Json;

// ItemComponent_Craftable
[System.Serializable]
public class ItemComponentTemplate_Craftable_Recipe
{
    public string jobKeyword = "";
    public List<ItemComponentTemplate_Craftable.SkillRequirement> skillRequirements = new List<ItemComponentTemplate_Craftable.SkillRequirement>();
    public List<Manageable.ItemEntry> itemRequirements = new List<Manageable.ItemEntry>();

    public int workAmount = 0;
    public string outputItemBaseID = "";
    public int outputAmount = 0;

    protected Item_Base _outputItemCache = null;
    [JsonIgnore] public Item_Base OutputItem { get
        {
            if (_outputItemCache == null) _outputItemCache = scr_System_Serializer.current.GetByNameOrID_Item_Base(outputItemBaseID);
            return _outputItemCache;
        } }

    string _displayname = "";
    [JsonIgnore] public string DisplayName { get { 
            if (_displayname == "") _displayname = scr_System_Serializer.current.Dictionary.QueryThenParse("tag_"+jobKeyword)+": "+ scr_System_Serializer.current.GetByNameOrID_Item_Base(outputItemBaseID).DisplayName + " x" + outputAmount;
            return _displayname;
        } }

    

    [JsonIgnore] public string RecipeUID { get { return jobKeyword + "_" + outputItemBaseID + "_" + outputAmount; } }
    //scr_System_Serializer.current.GetByNameOrID_Item_Base(outputItemBaseID).Tooltip+ 

    string _tooltip = "";

    [JsonIgnore]
    public string Tooltip
    {
        get
        {
            if (_tooltip == "")
            {
                var itemreqs = new List<string>();
                foreach (var i in itemRequirements) itemreqs.Add(i.Print);
                _tooltip = scr_System_Serializer.current.Dictionary.QueryThenParse("ui_recipe_tooltip")
                                        .Replace("$time$", workAmount.ToString())
                                        .Replace("$skillreqs$", "TODO")
                                        .Replace("$itemreqs$", itemreqs.Count > 0 ? String.Join(" ", itemreqs) : "none")
                                        .Replace("$basetooltip$", OutputItem.Tooltip);

            }
            return _tooltip;
        }
    }
}

[System.Serializable]
public class ItemComponentTemplate_Craftable
{
    public List<ItemComponentTemplate_Craftable_Recipe> recipes = new List<ItemComponentTemplate_Craftable_Recipe>();

    [System.Serializable]
    public class SkillRequirement
    {
        public string skillID = "";
        public int minLevel = 0;
    }

}