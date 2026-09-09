using System;
using System.Collections.Generic;
using Newtonsoft.Json;


public class ItemComponentTemplate_Furniture : I_ItemComponentTemplate_Comp
{
    public string furnitureBaseID = "";

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

    [JsonIgnore] public override bool Stackable { get { return false; } }

    [JsonProperty] private string furnitureBaseID = "";
    private FurnitureBase furnitureBaseRefCache = null;
    [JsonIgnore] public FurnitureBase FurnitureBaseRef
    {
        get
        {
            if (furnitureBaseRefCache == null)
            {
                string id = furnitureBaseID != "" ? furnitureBaseID : Comp?.furnitureBaseID;
                if (!string.IsNullOrEmpty(id)) furnitureBaseRefCache = scr_System_Serializer.current.GetByNameOrID_FurnitureBase(id);
            }
            return furnitureBaseRefCache;
        }
    }

    public void SetFurnitureBase(FurnitureBase b)
    {
        this.furnitureBaseID = b.ID;
        this.furnitureBaseRefCache = b;
    }

    public override List<string> GetTags()
    {
        var tags = new List<string> { "furniture", "furniture_packed" };
        if (FurnitureBaseRef != null) tags.AddRange(FurnitureBaseRef.Tags);
        return tags;
    }

}