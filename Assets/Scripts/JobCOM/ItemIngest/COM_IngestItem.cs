using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

public class COM_IngestItem : COM
{
    protected Item_Base baseItem = null;

    /// <summary>
    /// Which ingest route this generated command represents ("stomach"/"anus"/"vagina").
    /// Set via GenerateCOM.targetCOMClass JSON per top-level generator COM.
    /// </summary>
    public string ingestBodyTag = "";

    [JsonIgnore] bool _isValidItem = false;
    [JsonIgnore] public override bool isValid { get { return _isValidItem; } }

    /// <summary>
    /// Exposes the resolved item so ActionPackage.cs can take-and-ingest it against a dynamically
    /// resolved faction (see requirements.requireInventory below) - mirrors COM_UseItemCOM.InnerItem.
    /// </summary>
    [JsonIgnore] public Item_Base InnerItem { get { return baseItem; } }

    public override void InitializeChildCOM(COM baseCOM, Item_Base item)
    {
        base.InitializeChildCOM(baseCOM, item);

        this.baseItem = item;

        this.ID += ("_" + baseItem.ID);
        this.comTags.AddRange(item.Tags);
        this.comTags = this.comTags.Distinct().ToList();

        // Character-owned command has no fixed employer faction - resolved dynamically per-execution
        // against job.GetValidInventoryFactions() (home/managed-work/current-location), same as
        // COM_UseItemCOM/COM_Recording, instead of a single requireFaction.
        if (this.requirements.requireInventory == null) this.requirements.requireInventory = new Requirement_Inventory();
        this.requirements.requireInventory.inventoryItemBaseID = baseItem.ID;

        var ingestTemplate = item.GetCompTemplateByID("ItemComponent_Ingestible");
        _isValidItem = item.Tags.Contains("consumable")
            && !item.Tags.Contains("food_meal")
            && (ingestTemplate?.comp_Ingestible?.ingestMethod?.Exists(m => m.bodyTags == ingestBodyTag) ?? false);
    }

    public override string GetDescription_Begin(EvaluationPackage evp, int variantID)
    {
        var s = base.GetDescription_Begin(evp, variantID);
        return Replace(s);
    }

    public override string GetVariantDescription(int variantID, bool isDoer, int charaRef, string roomName, List<int> DoerRefs, List<int> ReceiverRefs, int masterRef)
    {
        var s = base.GetVariantDescription(variantID, isDoer, charaRef, roomName, DoerRefs, ReceiverRefs, masterRef);
        return Replace(s);
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
