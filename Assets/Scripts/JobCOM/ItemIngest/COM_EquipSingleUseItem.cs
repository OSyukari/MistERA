using System.Collections.Generic;
using Newtonsoft.Json;

/// <summary>
/// Puts a one-time-use wearable (ItemComponentTemplate_Equippable.singleUse - condom) on, or takes it off, as a normal
/// command (usually ActionPackage_Sex, so doing it to / for someone else is rolled and stimulates like the hand / oral act
/// it mirrors). Each command has one fixed targeting, listed under the item's "use" folder (COM.itemFolder):
/// selfOnly - the doer on themselves, receivers ignored; otherwise the wearer is the receiver (on target) or, with
/// wearerIsDoer, the doer while the receiver does it (by target). remove takes the worn item off (valid only while worn -
/// it is used up, its used variant left on the floor); otherwise one is put on (valid only while no unused one is worn -
/// a filled one comes off first - and one is accessible - the inventory check runs here instead of
/// requirements.requireInventory, which would also block removing). The equip itself happens after execution
/// (ActionPackage, ApplyExecution); dcGated commands only equip on a successful DC - a failed attempt equips and uses up
/// nothing, so it can be tried again.
/// </summary>
public class COM_EquipSingleUseItem : COM
{
    /// <summary>Item base ID put on / taken off (e.g. "condom").</summary>
    public string itemID = "";
    /// <summary>Takes the worn item off instead of putting one on.</summary>
    public bool remove = false;
    /// <summary>The doer wears it and the receiver does it (by target). Self commands (selfOnly) wear it on the doer anyway.</summary>
    public bool wearerIsDoer = false;
    /// <summary>Putting it on needs this pair's DC to succeed (e.g. with the mouth).</summary>
    public bool dcGated = false;

    [JsonIgnore] Requirement_Inventory _itemRequirement = null;
    /// <summary>Where a new item comes from: the same per-actor faction search as requireInventory.</summary>
    [JsonIgnore] public Requirement_Inventory ItemRequirement
    {
        get
        {
            if (_itemRequirement == null || _itemRequirement.inventoryItemBaseID != itemID)
                _itemRequirement = new Requirement_Inventory { inventoryItemBaseID = itemID };
            return _itemRequirement;
        }
    }

    /// <summary>The wearer of one pair: the doer (self / by target), else the receiver.</summary>
    public Character_Trainable WearerOf(Character_Trainable doer, Character_Trainable receiver)
    {
        return wearerIsDoer || receiver == null ? doer : receiver;
    }

    protected override bool ValidateCondition(List<string> _tooltip, List<Character_Trainable> doerRefs, List<Character_Trainable> receiverRefs, COM com, out bool hardlock, COM_Variant variant = null, int actorCountMult = 1, I_IsJobGiver jobFaction = null, ActionPackage p = null)
    {
        if (!base.ValidateCondition(_tooltip, doerRefs, receiverRefs, com, out hardlock, variant, actorCountMult, jobFaction, p)) return false;
        var wearers = wearerIsDoer || receiverRefs == null || receiverRefs.Count == 0 ? doerRefs : receiverRefs;
        return ValidateItemState(_tooltip, variant, wearers, doerRefs, receiverRefs, p);
    }

    protected override bool ValidateCondition(List<string> _tooltip, Character_Trainable doerRefs, COM com, out bool hardlock, COM_Variant variant = null, I_IsJobGiver jobFaction = null, ActionPackage p = null)
    {
        if (!base.ValidateCondition(_tooltip, doerRefs, com, out hardlock, variant, jobFaction, p)) return false;
        var doers = new List<Character_Trainable> { doerRefs };
        return ValidateItemState(_tooltip, variant, doers, doers, null, p);
    }

    /// <summary>
    /// Remove: every wearer wears the item. Put on: none wears an unused one, and (when a package gives the job) one is
    /// accessible - display-only checks without a package skip the inventory part.
    /// </summary>
    bool ValidateItemState(List<string> tooltip, COM_Variant variant, List<Character_Trainable> wearers,
        List<Character_Trainable> doers, List<Character_Trainable> receivers, ActionPackage p)
    {
        if (variant == null || wearers == null) return true;
        foreach (var wearer in wearers)
        {
            if (wearer == null) continue;
            var worn = SingleUseEquipUtility.FindWorn(wearer, itemID);
            if (remove && worn == null)
            {
                tooltip?.Add(LocalizeDictionary.QueryThenParse("ui_singleUse_notWorn").Replace("$name$", wearer.FirstName));
                return false;
            }
            // a filled one does not block putting a new one on (it comes off first, TryEquipFrom)
            if (!remove && IsWornUnused(worn))
            {
                tooltip?.Add(LocalizeDictionary.QueryThenParse("ui_singleUse_alreadyWorn").Replace("$name$", wearer.FirstName));
                return false;
            }
        }
        if (!remove && p != null && p.job != null && !ItemRequirement.Validate(p.job, doers, receivers, p.Master, out var inventoryTooltip))
        {
            tooltip?.Add(inventoryTooltip);
            return false;
        }
        return true;
    }

    /// <summary>
    /// After execution (ActionPackage): per accepted pair, take the wearer's item off (remove) or put one on from source -
    /// dcGated only when the pair's DC succeeded. Nothing happens for refused pairs.
    /// </summary>
    /// <returns>True when at least one item was equipped or removed, so the package can stop repeating.</returns>
    public bool ApplyExecution(List<EvaluationPackage> packages, int variantIndex, I_IsJobGiver source,
        MessageCollect messages, Room_Instance recordingRoom)
    {
        bool completed = false;
        foreach (var ep in packages)
        {
            if (ep.Response < Memory_Response.Accept) continue;
            var wearer = WearerOf(ep.Doer, ep.Receiver);
            if (wearer == null) continue;
            var worn = SingleUseEquipUtility.FindWorn(wearer, itemID);
            // only the wearer taking their own off may choose where a filled one goes; a partner's goes to the floor
            bool bySelf = wearer == ep.Doer && (ep.Receiver == null || ep.Receiver == ep.Doer);
            bool succeeded = false;
            if (remove)
            {
                if (worn != null)
                {
                    wearer.UnequipItem(worn.RefID, takenOffBySelf: bySelf);
                    succeeded = !wearer.Body.IsEquipped(worn.RefID);
                }
            }
            // a failed attempt (e.g. with the mouth) equips and uses up nothing
            else if (!IsWornUnused(worn) && (!dcGated || ep.Response >= Memory_Response.Success))
                succeeded = SingleUseEquipUtility.TryEquipFrom(source, itemID, wearer, bySelf);

            completed |= succeeded;
            if (messages != null && variantIndex >= 0 && variantIndex < variants.Count)
            {
                var variant = variants[variantIndex];
                var description = succeeded ? variant.description_execution_success : variant.description_execution_failure;
                var pair = ep;
                var text = description?.GetText(ref pair);
                if (string.IsNullOrEmpty(text)) continue;
                text = text.Replace("$DEFAULT$", "");
                UtilityEX.StringReplace(ep, ref text);
                text = Replace(text);
                if (text.Length > 0)
                {
                    var result = new DescriptionCollector(text, ep.ActorRefs) { message_excludeRelated = text };
                    messages.AddMessage_After(result, recordingRoom);
                }
            }
        }
        return completed;
    }

    /// <summary>Worn and still unused (no ItemComponent_Contents, or nothing inside).</summary>
    static bool IsWornUnused(Item_Instance worn)
    {
        if (worn == null) return false;
        var contents = worn.Comp_Contents;
        return contents == null || contents.IsEmpty;
    }
}
