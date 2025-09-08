using System.Collections.Generic;
using UnityEngine;

public static class MapUtility
{
    public static List<COM> GetFurnitureCOMs(FurnitureBase.Furniture_COMGiver furniture)
    {
        List<COM> returnValues = new List<COM>();

        foreach (string i in furniture.comID)
        {
            var temp = scr_System_Serializer.current.GetByNameOrID_COM(i);
            if (temp != null) returnValues.Add(temp);
            else Debug.LogError($"FURNITURE COMGIVER CANNOT FIND COMMAND {i}");
        }

        if (furniture.comTags.Count > 0) returnValues.AddRange(scr_System_Serializer.current.index_COM.GetByTags(furniture.comTags));

        return returnValues;
    }

    public static List<ItemEntry> GetContent(MapPlan.SalesInventoryInit inv)
    {
        var list = new List<ItemEntry>();
        if (inv.matchByID != "") list.Add(new ItemEntry(inv.matchByID, inv.nameOverwrite, inv.itemCount, inv.countOverride));
        if (inv.matchByTags.Count > 0)
        {
            foreach (var recipe in Masterlist_Items.Instance.CraftingRecipe.Values)
            {
                var outputItem = recipe.OutputItem;
                if (outputItem == null) Debug.LogError($"sales inventory get content {recipe.outputItemBaseID} is null");
                else if (Utility.ListContainsStrict(outputItem.Tags, inv.matchByTags))
                {
                    if (inv.exceptTags.Count > 0 && Utility.ListContainsLoose(outputItem.Tags, inv.exceptTags)) continue;
                    else if (outputItem.Tags.Contains("do_not_sell")) continue;
                    list.Add(new ItemEntry(outputItem.id, "", recipe.outputAmount * inv.itemCount, inv.countOverride));
                }
            }

            foreach (var item in scr_System_Serializer.current.index_Item_Base.List)
            {
                if (item.Tags.Contains("do_not_use")) continue;
                if (item.GetCompTemplateByID("ItemComponent_Craftable") != null) continue;
                if (Utility.ListContainsStrict(item.Tags, inv.matchByTags))
                {
                    if (inv.exceptTags.Count > 0 && Utility.ListContainsLoose(item.Tags, inv.exceptTags)) continue;
                    else if (item.Tags.Contains("do_not_sell")) continue;
                    list.Add(new ItemEntry(item.id, "", inv.itemCount, inv.countOverride));
                }
            }
        }
        return list;
    }
}