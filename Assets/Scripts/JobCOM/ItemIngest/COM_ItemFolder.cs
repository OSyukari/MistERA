using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// "Use [item]" folder: one per item, built in Index_COM.LateInitialize from the authored template (com_useItem) for
/// every item some command names in COM.itemFolder. Its children are those commands (ingest routes, condom commands),
/// so the item is picked first and the way of using it second. Never executable itself - clicking it opens the child
/// panel. Unlike a generator, it keeps its own button while it has children (isHiddenParent false).
/// </summary>
public class COM_ItemFolder : COM
{
    protected Item_Base baseItem = null;
    [JsonIgnore] public Item_Base InnerItem { get { return baseItem; } }

    [JsonIgnore] public override bool isHiddenParent { get { return false; } }
    [JsonIgnore] public override bool isValid { get { return childCOMs.Count > 0; } }

    /// <summary>Turns a copy of the template into the folder of item: ID template_itemID, shown.</summary>
    public void InitializeFolder(Item_Base item)
    {
        baseItem = item;
        ID += "_" + item.ID;
        hidden = false;
    }

    public override string DisplayName(int index = -1)
    {
        return Replace(base.DisplayName(index));
    }

    public override string DisplayName(Job sourceJob, List<Character_Trainable> doerRefIDs, List<Character_Trainable> receiverRefIDs = null, bool excludeRequireExisting = false, int actorCountMult = 1)
    {
        return Replace(base.DisplayName(sourceJob, doerRefIDs, receiverRefIDs, excludeRequireExisting, actorCountMult));
    }

    public override string Replace(string s)
    {
        if (baseItem != null) return s.Replace("$name$", baseItem.DisplayName);
        else return s;
    }
}
