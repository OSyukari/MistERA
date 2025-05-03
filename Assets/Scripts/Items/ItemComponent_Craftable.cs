using System.Collections.Generic;
using Newtonsoft.Json;

// ItemComponent_Craftable
[System.Serializable]
public class ItemComponentTemplate_Craftable_Recipe
{
    public string jobKeyword = "";
    public List<ItemComponentTemplate_Craftable.SkillRequirement> skillRequirements = new List<ItemComponentTemplate_Craftable.SkillRequirement>();
    public List<ItemComponentTemplate_Craftable.ItemRequirement> itemRequirements = new List<ItemComponentTemplate_Craftable.ItemRequirement>();

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
            if (_displayname == "") _displayname = scr_System_Serializer.current.GetByNameOrID_Item_Base(outputItemBaseID).DisplayName + " x" + outputAmount;
            return _displayname;
        } }
    [JsonIgnore] public string RecipeUID { get { return jobKeyword + "_" + outputItemBaseID + "_" + outputAmount; } }
    //scr_System_Serializer.current.GetByNameOrID_Item_Base(outputItemBaseID).Tooltip+ 
    [JsonIgnore] public string Tooltip { get { return "TimeCost [" + workAmount + "]minutes\nSkillRequirement []\nitemRequirement []"; } }
}

[System.Serializable]
public class ItemComponentTemplate_Craftable
{
    public List<ItemComponentTemplate_Craftable_Recipe> recipes = new List<ItemComponentTemplate_Craftable_Recipe>();

    [System.Serializable]
    public class SkillRequirement
    {
        public string skillID;
        public int minLevel;
    }

    [System.Serializable]
    public class ItemRequirement
    {
        public string baseID;
        public int amount;

        [JsonIgnore] public string Name { get
            {
                return scr_System_Serializer.current.Dictionary.QueryThenParse(baseID);
            } }
    }

}