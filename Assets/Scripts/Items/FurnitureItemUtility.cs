public static class FurnitureItemUtility
{
    public const string PackedFurnitureItemID = "item_furniture_packed";

    public static bool CanPack(FurnitureInstance inst, out string reason)
    {
        reason = "";
        if (inst == null || inst.FurnitureBase == null) { reason = "invalid furniture instance"; return false; }
        if (inst.FurnitureBase.noDisplay) { reason = "marker furniture cannot be packed"; return false; }
        if (inst.JobGiver != null && inst.JobGiver.IsInUse) { reason = "furniture is in use"; return false; }
        return true;
    }

    public static bool TryPack(FurnitureInstance inst, out Item_Instance createdItem, out string reason)
    {
        createdItem = null;
        if (!CanPack(inst, out reason)) return false;
        var room = inst.ParentRoom;
        var faction = room == null ? null : room.FactionOwner;
        if (faction == null) { reason = "room has no faction owner"; return false; }

        var item = WorldManager.Instantiate(PackedFurnitureItemID);
        var comp = item.GetComp("ItemComponent_Furniture") as ItemComponent_Furniture;
        comp.SetFurnitureBase(inst.FurnitureBase);
        item.InvalidateTagsCache();
        item.nameOverwrite = LocalizeDictionary.QueryThenParse("item_furniture_packed_nameOverwrite").Replace("$name$", inst.FurnitureBase.DisplayName);

        faction.Inventory.AddItem(item);
        room.RemoveFurniture(inst);

        createdItem = item;
        return true;
    }

    public static bool CanUnpack(Item_Instance item, out string reason)
    {
        reason = "";
        var comp = item == null ? null : item.GetComp("ItemComponent_Furniture") as ItemComponent_Furniture;
        if (comp == null || comp.FurnitureBaseRef == null) { reason = "not a packed furniture item"; return false; }
        if (comp.FurnitureBaseRef.noDisplay) { reason = "marker furniture cannot be unpacked"; return false; }
        return true;
    }

    public static bool TryUnpack(Item_Instance item, Room_Instance targetRoom, out FurnitureInstance createdInstance, out string reason)
    {
        createdInstance = null;
        if (!CanUnpack(item, out reason)) return false;
        var faction = targetRoom == null ? null : targetRoom.FactionOwner;
        if (faction == null || !faction.Inventory.Contains(item)) { reason = "item not in this room's faction inventory"; return false; }

        var furnitureBase = (item.GetComp("ItemComponent_Furniture") as ItemComponent_Furniture).FurnitureBaseRef;
        faction.Inventory.Remove(item);
        scr_System_CampaignManager.current.Unregister(item);
        createdInstance = targetRoom.AddFurniture(furnitureBase);
        return true;
    }
}
