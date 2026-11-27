using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Crane machine prize game (com_furniture_crane_play -> event com_furniture_crane_play_event,
/// Data/Events/COM_Triggers/OnCranePlay.json, flow graph OnCranePlay_flow.svg next to it).
/// <br/>Prize pool of the machine's owner (the job's FactionOwner): every unit of its salesInventory stock (innerStock,
/// re-rolled daily by Manageable.RefreshSalesInventory) plus every count of its real inventory items carrying one of
/// requireFaction.requireCranePrize.inventoryTags (tokens skipped). A real item leaves the stock when won (a non-player
/// faction's RemoveItem would only hand out copies, so it uses RemoveOwnStock).
/// <br/>Event state lives in AppendStrings under a prefix: [prefix]_cell_[x]_[y] = unit codes ("S|salesKey" /
/// "I|baseID"), [prefix]_x / [prefix]_y = the picked column (0 left, 1 middle, 2 right) / row (0 front, 1 middle, 2 back).
/// </summary>
public static class CraneMachineUtility
{
    public const int GridSize = 3;
    /// <summary> most units put in the machine for one play; a larger pool is sampled </summary>
    public const int MaxUnits = 27;
    const string SalesCode = "S|";
    const string ItemCode = "I|";
    /// <summary> AppendStrings key Result_Event sets: the RefID of the job whose package started the event </summary>
    public const string SourceJobRefKey = "sourceJobRef";

    /// <summary> requireFaction.requireCranePrize: the command is invalid (shown with a tooltip) while the pool is empty </summary>
    public class PrizeRequirement
    {
        /// <summary> real inventory items with any of these tags count as prizes; empty = salesInventory stock only </summary>
        public List<string> inventoryTags = new List<string>();
    }

    static Manageable OwnerFaction(I_IsJobGiver owner)
    {
        if (owner == null) return null;
        return owner as Manageable ?? owner.Faction;
    }

    static bool IsPrizeItem(Item_Instance item, List<string> tags)
    {
        return item != null && !item.isToken && !item.markForDelete && item.Count > 0
            && tags != null && tags.Count > 0 && item.Tags != null && Utility.ListContainsLoose(item.Tags, tags);
    }

    /// <summary> true when owner has at least one prize unit (no allocation) </summary>
    public static bool HasPrize(I_IsJobGiver owner, List<string> inventoryTags)
    {
        var mm = OwnerFaction(owner);
        if (mm != null && mm.salesInventory != null)
        {
            foreach (var kvp in mm.salesInventory.Inventory)
                if (kvp.Value != null && kvp.Value.innerStock > 0 && kvp.Value.BaseItem != null) return true;
        }
        if (owner != null && owner.Inventory != null && inventoryTags != null && inventoryTags.Count > 0)
        {
            foreach (var item in owner.Inventory.Contents)
                if (IsPrizeItem(item, inventoryTags)) return true;
        }
        return false;
    }

    static List<string> BuildPool(I_IsJobGiver owner, List<string> inventoryTags)
    {
        var pool = new List<string>();
        var mm = OwnerFaction(owner);
        if (mm != null && mm.salesInventory != null)
        {
            foreach (var kvp in mm.salesInventory.Inventory)
            {
                if (kvp.Value == null || kvp.Value.BaseItem == null) continue;
                for (int i = 0; i < kvp.Value.innerStock; i++) pool.Add(SalesCode + kvp.Key);
            }
        }
        if (owner != null && owner.Inventory != null && inventoryTags != null && inventoryTags.Count > 0)
        {
            foreach (var item in owner.Inventory.Contents)
            {
                if (!IsPrizeItem(item, inventoryTags)) continue;
                int count = Math.Min(item.Count, MaxUnits);
                for (int i = 0; i < count; i++) pool.Add(ItemCode + item.BaseID);
            }
        }
        if (pool.Count > MaxUnits)
        {
            Utility.Shuffle(pool);
            pool.RemoveRange(MaxUnits, pool.Count - MaxUnits);
        }
        return pool;
    }

    static string UnitName(I_IsJobGiver owner, string code)
    {
        if (code.StartsWith(SalesCode))
        {
            var mm = OwnerFaction(owner);
            if (mm != null && mm.salesInventory != null && mm.salesInventory.Inventory.TryGetValue(code.Substring(SalesCode.Length), out var entry) && entry != null)
                return entry.PrintName;
            return "?";
        }
        var baseItem = scr_System_Serializer.current.index_Item_Base.GetByID(code.Substring(ItemCode.Length));
        return baseItem == null ? "?" : baseItem.DisplayName;
    }

    /// <summary> the machine's owner: the job of the package that started the event, else the actor's current job </summary>
    static I_IsJobGiver ResolveOwner(EventInstance ev, Character_Trainable actor)
    {
        if (ev.AppendStrings.TryGetValue(SourceJobRefKey, out var refs) && refs != null && refs.Count > 0 && int.TryParse(refs[0], out int jobRef))
        {
            var job = scr_System_CampaignManager.current.FindJobInstanceByID(jobRef, false);
            if (job != null && job.FactionOwner != null) return job.FactionOwner;
        }
        return actor != null && actor.CurrentJob != null ? actor.CurrentJob.FactionOwner : null;
    }

    static Character_Trainable FirstActor(EventInstance ev, string key)
    {
        if (!EventUtility.TryResolveExecTargets(ev, key, out var list) || list == null) return null;
        foreach (var c in list) if (c != null) return c;
        return null;
    }

    static string CellKey(string prefix, int x, int y) { return $"{prefix}_cell_{x}_{y}"; }

    static bool TryGetPick(EventInstance ev, string prefix, out int x, out int y)
    {
        x = y = -1;
        return ev.AppendStrings.TryGetValue(prefix + "_x", out var xs) && xs != null && xs.Count > 0 && int.TryParse(xs[0], out x)
            && ev.AppendStrings.TryGetValue(prefix + "_y", out var ys) && ys != null && ys.Count > 0 && int.TryParse(ys[0], out y)
            && x >= 0 && x < GridSize && y >= 0 && y < GridSize;
    }

    static string NamesOf(I_IsJobGiver owner, List<string> codes)
    {
        if (codes == null || codes.Count == 0) return LocalizeDictionary.QueryThenParse("crane_cell_empty");
        // grouped "name×N", in first-seen order
        var names = new List<string>();
        var counts = new List<int>();
        foreach (var code in codes)
        {
            var name = UnitName(owner, code);
            int i = names.IndexOf(name);
            if (i < 0) { names.Add(name); counts.Add(1); }
            else counts[i]++;
        }
        var parts = new List<string>();
        for (int i = 0; i < names.Count; i++) parts.Add(counts[i] > 1 ? $"{names[i]}×{counts[i]}" : names[i]);
        return String.Join(LocalizeDictionary.QueryThenParse("crane_name_separator"), parts);
    }

    /// <summary>
    /// ExecutionType.CraneSetup [actorKey, comID, prefix]: builds the owner's prize pool (comID's requireCranePrize),
    /// puts each unit in a random cell and writes [prefix]_cell_x_y, [prefix]_grid (the machine's contents, back row
    /// first) and [prefix]_count. False when the pool is empty.
    /// </summary>
    public static bool Setup(EventInstance ev, Event.EventEntry.Executor exec)
    {
        if (exec.arguments.Count < 3 || string.IsNullOrEmpty(exec.arguments[2])) return false;
        var actor = FirstActor(ev, exec.arguments[0]);
        var com = scr_System_Serializer.current.MasterList.COMs.GetByID(exec.arguments[1]);
        var owner = ResolveOwner(ev, actor);
        if (actor == null || com == null || owner == null) return false;
        var req = com.requirements.requireFaction == null ? null : com.requirements.requireFaction.requireCranePrize;
        var pool = BuildPool(owner, req == null ? null : req.inventoryTags);
        if (pool.Count == 0) return false;

        string prefix = exec.arguments[2];
        var cells = new List<string>[GridSize, GridSize];
        for (int x = 0; x < GridSize; x++)
            for (int y = 0; y < GridSize; y++) cells[x, y] = new List<string>();
        foreach (var code in pool) cells[UnityEngine.Random.Range(0, GridSize), UnityEngine.Random.Range(0, GridSize)].Add(code);

        var rows = new List<string>();
        for (int y = GridSize - 1; y >= 0; y--)
        {
            var row = LocalizeDictionary.QueryThenParse("crane_grid_row").Replace("$row$", LocalizeDictionary.QueryThenParse($"crane_row_{y}"));
            for (int x = 0; x < GridSize; x++)
            {
                ev.AppendStrings[CellKey(prefix, x, y)] = cells[x, y];
                row = row.Replace($"$c{x}$", NamesOf(owner, cells[x, y]));
            }
            rows.Add(row);
        }
        ev.AppendStrings[prefix + "_grid"] = new List<string>() { String.Join("\n", rows) };
        ev.AppendStrings[prefix + "_count"] = new List<string>() { pool.Count.ToString() };
        return true;
    }

    /// <summary>
    /// ExecutionType.CraneCellHasPrize [prefix]: sets [prefix]_cell_name to the picked cell ([prefix]_x / _y);
    /// true when that cell holds a unit.
    /// </summary>
    public static bool CellHasPrize(EventInstance ev, Event.EventEntry.Executor exec)
    {
        if (exec.arguments.Count < 1 || string.IsNullOrEmpty(exec.arguments[0])) return false;
        string prefix = exec.arguments[0];
        if (!TryGetPick(ev, prefix, out int x, out int y)) return false;
        ev.AppendStrings[prefix + "_cell_name"] = new List<string>() {
            LocalizeDictionary.QueryThenParse("crane_cell_name")
                .Replace("$row$", LocalizeDictionary.QueryThenParse($"crane_row_{y}"))
                .Replace("$col$", LocalizeDictionary.QueryThenParse($"crane_col_{x}")) };
        return ev.AppendStrings.TryGetValue(CellKey(prefix, x, y), out var units) && units != null && units.Count > 0;
    }

    /// <summary>
    /// ExecutionType.CraneGrab [actorKey, prefix, "one" | "all"]: takes one random unit (or every unit) of the picked
    /// cell out of the owner's stock - salesInventory innerStock -1, or the real item - and gives it to the actor's home
    /// faction. [prefix]_prize = the won names. False when nothing could be taken (stock changed meanwhile).
    /// </summary>
    public static bool Grab(EventInstance ev, Event.EventEntry.Executor exec)
    {
        if (exec.arguments.Count < 3 || string.IsNullOrEmpty(exec.arguments[1])) return false;
        var actor = FirstActor(ev, exec.arguments[0]);
        string prefix = exec.arguments[1];
        bool all = exec.arguments[2] == "all";
        var owner = ResolveOwner(ev, actor);
        if (actor == null || owner == null || !TryGetPick(ev, prefix, out int x, out int y)) return false;
        if (!ev.AppendStrings.TryGetValue(CellKey(prefix, x, y), out var units) || units == null || units.Count == 0) return false;
        var homes = actor.FactionManager.HomeFactions;
        var home = homes.Count > 0 ? homes[0] : null;
        if (home == null || home.Inventory == null) return false;

        var picked = new List<string>();
        if (all) picked.AddRange(units);
        else picked.Add(units[UnityEngine.Random.Range(0, units.Count)]);

        var won = new List<string>();
        foreach (var code in picked)
        {
            if (TakeUnit(owner, code, out var item))
            {
                home.Inventory.AddItem(item);
                won.Add(code);
                units.Remove(code);
            }
        }
        if (won.Count == 0) return false;
        ev.AppendStrings[prefix + "_prize"] = new List<string>() { NamesOf(owner, won) };
        return true;
    }

    static bool TakeUnit(I_IsJobGiver owner, string code, out Item_Instance item)
    {
        item = null;
        if (code.StartsWith(SalesCode))
        {
            var mm = OwnerFaction(owner);
            if (mm == null || mm.salesInventory == null) return false;
            if (!mm.salesInventory.Inventory.TryGetValue(code.Substring(SalesCode.Length), out var entry) || entry == null || entry.innerStock < 1) return false;
            item = WorldManager.Instantiate(entry);
            if (item == null) return false;
            entry.innerStock--;
            return true;
        }
        string baseID = code.Substring(ItemCode.Length);
        if (owner.Inventory == null) return false;
        if (owner.isPlayerFaction)
        {
            // the player's own stock: the real item moves
            var moved = owner.Inventory.RemoveItem(baseID, 1);
            item = moved.Count > 0 ? moved[0] : null;
            return item != null;
        }
        // a non-player inventory's RemoveItem only hands out copies - take it out of the stock for real
        if (owner.Inventory.RemoveOwnStock(baseID, 1) < 1) return false;
        item = WorldManager.Instantiate(baseID);
        return item != null;
    }
}
