using System.Collections.Generic;

/// <summary>
/// Command IDs replaced by generated commands, for old saves (Docs/Interactions/Plan_GeneratedCommands.md §4.7). Filled at
/// load of the definitions by the generators' legacyIDs (Index_COM); applied once while a save at or before
/// RenamedUpToBuild loads, by the classes that store command IDs, in their ReEstablishParent (where SaveFile.LoadingVersion
/// is set). The stored string is rewritten, so the save holds the new ID from its next save on.
/// </summary>
public static class COM_LegacyIDs
{
    /// <summary>
    /// Saves at or before this build hold the old restraint IDs (com_furniture_cryosleep_ADD ...). Set to the last build
    /// before the per-furniture restraint commands shipped.
    /// </summary>
    public const int RenamedUpToBuild = 20261116;

    struct Replacement
    {
        public string prefix;       // + furniture ID = the furniture's own command
        public string first;        // without a furniture: the first generated command of that action
    }

    static readonly Dictionary<string, Replacement> table = new Dictionary<string, Replacement>();

    /// <summary> oldID is now `prefix + furnitureID`; first = the command used when the holder has no furniture. </summary>
    public static void Register(string oldID, string prefix, string first)
    {
        if (string.IsNullOrEmpty(oldID)) return;
        table[oldID] = new Replacement { prefix = prefix, first = first };
    }

    /// <summary>
    /// Rewrites id when it is a replaced command and a save at or before RenamedUpToBuild is loading: the command of
    /// furnitureID when given and generated, else the action's first generated command. True when id changed.
    /// </summary>
    public static bool Migrate(ref string id, string furnitureID = null)
    {
        if (string.IsNullOrEmpty(id) || table.Count == 0 || !table.TryGetValue(id, out var r)) return false;
        if (!SaveFile.LoadingSaveAtOrBefore(RenamedUpToBuild)) return false;
        string replacement = r.first;
        if (!string.IsNullOrEmpty(furnitureID))
        {
            var own = r.prefix + furnitureID;
            if (scr_System_Serializer.current.MasterList.COMs.GetByID(own) != null) replacement = own;
        }
        if (string.IsNullOrEmpty(replacement)) return false;
        id = replacement;
        return true;
    }
}
