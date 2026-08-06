public class Requirement_Inventory
{
    public string inventoryItemBaseID = "";
    public bool isValid { get { return inventoryItemBaseID != ""; } }

    /// <summary>
    /// Searches job.GetValidInventoryFactions() (job-type-specific: a single explicit faction for
    /// scheduled/furniture jobs, the character's accessible factions for Job_CharaCOM) and returns the
    /// first one that actually has the item, so execution can reuse the same result instead of
    /// re-deriving it independently.
    /// </summary>
    public Manageable ResolveFaction(Job job)
    {
        if (job == null) return null;
        foreach (var faction in job.GetValidInventoryFactions())
            if (faction is Manageable mm && faction.Inventory != null && faction.Inventory.GetItemCount(inventoryItemBaseID) >= 1)
                return mm;
        return null;
    }

    public bool Validate(Job job, out string tooltip)
    {
        if (ResolveFaction(job) != null)
        {
            tooltip = "";
            return true;
        }
        var name = scr_System_Serializer.current.index_Item_Base.GetByID(inventoryItemBaseID);
        tooltip = LocalizeDictionary.QueryThenParse("ui_RequireFactionExisting_inventoryItemBaseID")
                .Replace("$faction$", "-")
                .Replace("$name$", name == null ? inventoryItemBaseID : name.DisplayName);
        return false;
    }
}
