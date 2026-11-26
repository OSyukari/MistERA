using System;
using System.Collections.Generic;

public enum InventoryTransferMode
{
    None,
    Consume,
    TransferToJobOwner
}

public class Requirement_Inventory
{
    public string inventoryItemBaseID = "";
    public int requiredCount = 1;
    public bool isValid { get { return inventoryItemBaseID != ""; } }

    /// <summary>
    /// When true, searches the actor's own accessible factions (home, eligible work, member locale)
    /// instead of job.GetValidInventoryFactions(actor) (which on Job_Furniture only checks job.FactionOwner).
    /// </summary>
    public bool useActorAccessibleFactions = false;

    /// <summary> What to do with requiredCount of inventoryItemBaseID when the COM executes. </summary>
    public InventoryTransferMode transferItemTo = InventoryTransferMode.None;

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

    public static List<I_IsJobGiver> GetActorAccessibleFactions(Character_Trainable actor)
    {
        var list = new List<I_IsJobGiver>();
        if (actor == null || actor.FactionManager == null) return list;

        foreach (var f in actor.FactionManager.HomeFactions)
            if (f != null && !list.Contains(f)) list.Add(f);
        foreach (var f in actor.FactionManager.WorkFactions)
        {
            if (f == null || list.Contains(f)) continue;
            bool eligible = f.isCharaManager(actor) || (f.isMember(actor.RefID) && f == actor.FactionManager.CurrentlyActiveFaction);
            if (eligible) list.Add(f);
        }
        var locale = actor.FactionManager.CurrentLocaleFaction;
        if (locale != null && !list.Contains(locale) && locale.isMember(actor.RefID)) list.Add(locale);

        return list;
    }

    /// <summary>
    /// Searches job.GetValidInventoryFactions(actor) (or GetActorAccessibleFactions when useActorAccessibleFactions is true)
    /// and returns the first faction that actually has requiredCount of the item, so execution can reuse the same result
    /// instead of re-deriving it independently.
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
        int need = Math.Max(1, requiredCount);
        var factions = useActorAccessibleFactions ? GetActorAccessibleFactions(actor) : job.GetValidInventoryFactions(actor);
        foreach (var faction in factions)
            if (faction is Manageable mm && faction.Inventory != null && faction.Inventory.GetItemCount(inventoryItemBaseID) >= need)
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
        int need = Math.Max(1, requiredCount);
        string itemText = name == null ? inventoryItemBaseID : name.DisplayName;
        if (need > 1) itemText = $"{itemText} x{need}";
        tooltip = LocalizeDictionary.QueryThenParse("ui_RequireFactionExisting_inventoryItemBaseID")
                .Replace("$faction$", "-")
                .Replace("$name$", itemText);
        return false;
    }

    public void ExecuteTransfer(Job job, List<Character_Trainable> doers, List<Character_Trainable> receivers, Character_Trainable master)
    {
        if (!isValid || transferItemTo == InventoryTransferMode.None) return;
        int count = Math.Max(1, requiredCount);
        var sourceFaction = ResolveFaction(job, doers, receivers, master);
        if (sourceFaction == null || sourceFaction.Inventory == null) return;

        if (transferItemTo == InventoryTransferMode.Consume)
        {
            sourceFaction.Inventory.RemoveOwnStock(inventoryItemBaseID, count);
        }
        else if (transferItemTo == InventoryTransferMode.TransferToJobOwner)
        {
            var dest = job?.FactionOwner;
            if (dest != null && dest.Inventory == sourceFaction.Inventory) return;
            int taken = sourceFaction.Inventory.RemoveOwnStock(inventoryItemBaseID, count);
            if (taken > 0 && dest != null && dest.Inventory != null)
                dest.Inventory.AddItem(WorldManager.Instantiate(inventoryItemBaseID, "", taken));
        }
    }
}
