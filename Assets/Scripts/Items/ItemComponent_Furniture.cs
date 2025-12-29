using System;
using System.Collections.Generic;
using Newtonsoft.Json;


public class ItemComponentTemplate_Furniture : I_ItemComponentTemplate_Comp
{
    public float furnitureSize = 0f;
    public List<Furniture_COMGiver> givesJob = new List<Furniture_COMGiver>();
    public bool noDisplay = false;
    [JsonIgnore] public bool isJobGiver { get { return this.givesJob.Count > 0; } }

    public class Furniture_COMGiver
    {
        [JsonProperty] private List<string> comID = new List<string>();
        [JsonProperty] private List<string> comTags = new List<string>();
    }
    public ItemComponent_Base Instantiate(Item_Base itemBase)
    {
        return new ItemComponent_Furniture(itemBase);
    }

    public bool TryValidate(out string errorMsg)
    {
        errorMsg = "";
        return true;
    }
}


public class ItemComponent_Furniture : ItemComponent_Base
{
    [JsonIgnore] public override string CompType { get { return "ItemComponent_Furniture"; } }
    string _tooltip = null;
    [JsonIgnore]
    public override string Tooltip
    {
        get
        {

            return "";
        }
    }
    ItemComponentTemplate_Furniture _comp = null;
    public ItemComponentTemplate_Furniture Comp
    {
        get
        {
            if (_comp == null) _comp = CompTemplate.Comp_Furniture;
            return _comp;
        }
    }
    public ItemComponent_Furniture()
    {

    }
    public ItemComponent_Furniture(Item_Base itemBase)
    {
        this.parent = itemBase;
        this.parentID = itemBase.ID;
    }

    public override bool canMergeWith(ItemComponent_Base other)
    {
        return false;
    }

}