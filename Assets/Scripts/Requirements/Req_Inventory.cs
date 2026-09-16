using System.Collections.Generic;

public class Requirement_Inventory
{
    public string inventoryItemBaseID = "";
    public bool isValid { get { return inventoryItemBaseID != ""; } }

    /// <summary>
    /// Which actor role(s) to search for the item, checked in this fixed order (Receiver, then Doer,
    /// then Master) and short-circuiting on the first role that resolves a faction - a role with no
    /// actors (e.g. no master) is simply skipped, not treated as a failure. Each character's accessible
    /// factions differ (Job_CharaCOM resolves per-actor), so multiple actors in the same role are each
    /// checked independently rather than pooled into one faction list.
    /// </summary>
    public bool checkReceiver = true;
    public bool checkDoer = true;
    public bool checkMaster = true;

    /// <summary>
    /// Searches job.GetValidInventoryFactions(actor) (job-type-specific: a single explicit faction for
    /// scheduled/furniture jobs, that actor's own accessible factions for Job_CharaCOM) and returns the
    /// first one that actually has the item, so execution can reuse the same result instead of
    /// re-deriving it independently. Only meaningful once an ActionPackage has resolved actors - checked
    /// per actor role (Receiver/Doer/Master) rather than against the job alone, since e.g. Job_CharaCOM
    /// has no single "the character" to resolve factions for.
    /// </summary>
    public Manageable ResolveFaction(Job job, List<Character_Trainable> doers, List<Character_Trainable> receivers, Character_Trainable master)
    {
        if (job == null) return null;

        if (checkReceiver && receivers != null)
        {
            foreach (var actor in receivers)
            {
                var faction = ResolveFactionForActor(job, actor);
                if (faction != null) return faction;
            }
        }

        if (checkDoer && doers != null)
        {
            foreach (var actor in doers)
            {
                var faction = ResolveFactionForActor(job, actor);
                if (faction != null) return faction;
            }
        }

        if (checkMaster && master != null)
        {
            var faction = ResolveFactionForActor(job, master);
            if (faction != null) return faction;
        }

        return null;
    }

    private Manageable ResolveFactionForActor(Job job, Character_Trainable actor)
    {
        if (actor == null) return null;
        foreach (var faction in job.GetValidInventoryFactions(actor))
            if (faction is Manageable mm && faction.Inventory != null && faction.Inventory.GetItemCount(inventoryItemBaseID) >= 1)
                return mm;
        return null;
    }

    public bool Validate(Job job, List<Character_Trainable> doers, List<Character_Trainable> receivers, Character_Trainable master, out string tooltip)
    {
        if (ResolveFaction(job, doers, receivers, master) != null)
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
