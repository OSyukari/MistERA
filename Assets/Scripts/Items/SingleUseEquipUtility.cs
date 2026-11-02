using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// One-time-use wearables (ItemComponentTemplate_Equippable.singleUse - condoms): put on from an inventory into the
/// wearer's own inventory and really equipped; taken off = destroyed, its used variant discarded (the floor, or for a
/// filled condom the player took off themselves, where they choose - OnCondomDiscard).
/// </summary>
public static class SingleUseEquipUtility
{
    /// <summary>
    /// Takes one baseID item out of source, equips it on wearer (forced, so it replaces whatever sits in its slot) and
    /// keeps it in wearer's own inventory (CharacterInventory keeps a worn item unmerged). False and nothing moved when
    /// none is available or it cannot be worn (e.g. no penis). A worn one of the same baseID (a filled condom) comes
    /// off first, so it is used up and discarded instead of displaced; takenOffBySelf: the wearer is the one doing it.
    /// </summary>
    public static bool TryEquipFrom(I_IsJobGiver source, string baseID, Character_Trainable wearer, bool takenOffBySelf = true)
    {
        if (source?.Inventory == null || wearer == null || string.IsNullOrEmpty(baseID)) return false;
        var item = source.Inventory.RemoveItem(baseID, wearer);
        if (item == null) return false;
        var worn = FindWorn(wearer, baseID);
        if (worn != null) wearer.UnequipItem(worn.RefID, takenOffBySelf: takenOffBySelf);
        // equip before adding: a worn item is never merged into a stack of the same item in the wearer's inventory
        if (!wearer.EquipItem(item.RefID, true))
        {
            source.Inventory.AddItem(item);
            return false;
        }
        wearer.Inventory.AddItem(item);
        return true;
    }

    /// <summary>The worn singleUse item with this base ID (e.g. "condom"), or null.</summary>
    public static Item_Instance FindWorn(Character_Trainable wearer, string baseID)
    {
        if (wearer?.Inventory == null) return null;
        foreach (var item in wearer.Inventory.Contents)
        {
            if (item == null || item.BaseID != baseID) continue;
            if (wearer.Body.IsEquipped(item.RefID)) return item;
        }
        return null;
    }

    /// <summary>The item worn on this organ that catches what it shoots (ItemComponent_Contents - a condom), or null.</summary>
    public static Item_Instance GetWornContainer(BodyInternal_Instance organ)
    {
        if (organ?.Owner == null) return null;
        foreach (var refID in organ.EquippedRefIDs)
        {
            var item = organ.Owner.Body.ResolveEquip(refID);
            if (item?.Comp_Contents != null) return item;
        }
        return null;
    }

    /// <summary>The organ wears an empty condom - protected. A filled one is not protection.</summary>
    public static bool HasEmptyWornContainer(BodyInternal_Instance organ)
    {
        var item = GetWornContainer(organ);
        return item != null && item.Comp_Contents.IsEmpty;
    }

    /// <summary>The organ wears a condom holding something - it has to come off before that organ penetrates.</summary>
    public static bool HasFilledWornContainer(BodyInternal_Instance organ)
    {
        var item = GetWornContainer(organ);
        return item != null && !item.Comp_Contents.IsEmpty;
    }

    /// <summary>Takes a filled condom off organ (the wearer does it; the line goes to m when given). False when none.</summary>
    public static bool TakeOffFilled(BodyInternal_Instance organ, MessageCollect m = null, Room_Instance room = null)
    {
        var item = GetWornContainer(organ);
        if (item == null || item.Comp_Contents.IsEmpty) return false;
        var wearer = organ.Owner;
        if (m != null)
        {
            var desc = new DescriptionCollector(LocalizeDictionary.QueryThenParse("ui_singleUse_discardFilled")
                .Replace("$name$", wearer.FirstName).Replace("$item$", item.DisplayName), new List<int>() { wearer.RefID });
            m.AddMessage_Before(desc, room);
        }
        wearer.UnequipItem(item.RefID);
        return true;
    }

    /// <summary>Takes every worn condom (ItemComponent_Contents holder) off c - leaving a sex scene.</summary>
    public static void TakeOffWornContainers(Character_Trainable c)
    {
        if (c?.Body == null) return;
        foreach (var organ in c.Body.Internals.ToArray())
        {
            var item = GetWornContainer(organ);
            if (item != null) c.UnequipItem(item.RefID);
        }
    }

    /// <summary>
    /// A singleUse item just came off (Character_Trainable.UnequipItem): it is used up - removed from the wearer's
    /// inventory and destroyed, its used version discarded: holding something (ItemComponent_Contents, a condom's
    /// semen) -> its filledItemID with the contents moved over (DiscardFilled), else usedItemID on the room floor.
    /// wornOrgan: the organ it was worn on; takenOffBySelf: the wearer took it off (not a partner).
    /// </summary>
    public static void ConsumeTakenOff(Character_Trainable wearer, Item_Instance item, ItemComponent_Equippable comp,
        BodyInternal_Instance wornOrgan = null, bool takenOffBySelf = true)
    {
        if (wearer == null || item == null) return;
        wearer.Inventory.Remove(item);

        Item_Instance used = null;
        bool filled = false;
        var contents = item.Comp_Contents;
        if (contents != null && !contents.IsEmpty && !string.IsNullOrEmpty(contents.FilledItemID))
        {
            used = WorldManager.Instantiate(contents.FilledItemID);
            var usedContents = used?.Comp_Contents;
            if (usedContents != null)
            {
                // before the holder is unregistered, which would take its contents with it
                contents.MoveContentsTo(usedContents);
                filled = true;
            }
            else Debug.LogWarning($"SingleUseEquipUtility: [{contents.FilledItemID}] has no ItemComponent_Contents, contents of [{item.DisplayName}] discarded");
        }
        else if (comp != null && !string.IsNullOrEmpty(comp.usedItemID)) used = WorldManager.Instantiate(comp.usedItemID);

        scr_System_CampaignManager.current.Unregister(item);
        if (used == null) return;
        if (filled) DiscardFilled(wearer, used, wornOrgan, takenOffBySelf);
        else DropOnFloor(wearer, used);
    }

    /// <summary>Event that lets a player wearer choose where a filled condom goes (Data/Events/OnCondomDiscard_sex.json).</summary>
    public const string DiscardEventID = "OnCondomDiscard";
    /// <summary>StoredOptions key of the code-built "on (partner)'s (body part)" options.</summary>
    public const string DiscardOptionsKey = "discardOptions";

    /// <summary>
    /// A filled condom just came off. Only the player taking their own off chooses: when the organ it was on touched
    /// anyone (LastInteactedRefs), it is held in the player's inventory and OnCondomDiscard is queued with an option per
    /// partner's body part (half the semen lands there as a body trace, the condom with the rest goes on the floor).
    /// Anyone else, or no partner: straight to the floor.
    /// </summary>
    static void DiscardFilled(Character_Trainable wearer, Item_Instance used, BodyInternal_Instance wornOrgan, bool takenOffBySelf)
    {
        var player = scr_System_CampaignManager.current.Player;
        var partners = new List<Character_Trainable>();
        if (takenOffBySelf && wearer == player && wornOrgan != null)
            foreach (var touched in wornOrgan.LastInteactedRefs)
                if (touched?.Owner != null && touched.Owner != wearer && !touched.Owner.Deleted && !partners.Contains(touched.Owner)) partners.Add(touched.Owner);
        if (partners.Count < 1)
        {
            DropOnFloor(wearer, used);
            return;
        }

        // held by the player until an option is picked - the event keeps only this reference
        wearer.Inventory.AddItem(used);
        System.Action discard = () =>
        {
            if (!wearer.Inventory.Contents.Contains(used)) return; // already moved / used meanwhile
            wearer.Inventory.Remove(used);
            DropOnFloor(wearer, used);
        };

        var ev = new EventInstance(wearer, DiscardEventID, "");
        ev.FunctionCalls.Add("discard", new List<System.Action>() { discard });
        ev.AppendStrings.Add("item", new List<string>() { used.DisplayName });
        var options = new List<Event.EventEntry.Options>();
        foreach (var partner in partners)
        {
            foreach (var part in partner.Body.Body)
            {
                if (part == null) continue;
                var target = part;
                options.Add(new Event.EventEntry.Options()
                {
                    option = LocalizeDictionary.QueryThenParse("event_OnCondomDiscard_onPart")
                        .Replace("$name$", partner.FirstName).Replace("$part$", part.DisplayName),
                    boundTarget = partner,
                    // parts that can't hold traces are shown disabled, not hidden
                    disabledReasonKey = part.AllowTraces ? "" : "event_OnCondomDiscard_noTrace",
                    onSelect = _ =>
                    {
                        PourOntoPart(used, target);
                        discard();
                    },
                });
            }
        }
        ev.StoredOptions[DiscardOptionsKey] = options;
        if (!scr_UpdateHandler.current.EventHandler.StartEvent(ev, false)) discard();
    }

    /// <summary>
    /// Half of each semen in the filled condom lands on the part as a cum trace (only the source's name / race is kept,
    /// Plan_BodyTraces.md 5.4); the other half stays in the condom.
    /// </summary>
    static void PourOntoPart(Item_Instance filled, BodyPart_Instance part)
    {
        var contents = filled?.Comp_Contents;
        if (contents == null || part == null || !part.AllowTraces) return;
        foreach (var item in contents.Contents)
        {
            if (!(item is Item_Instance_Cum cum) || cum.CumAmount <= 0) continue;
            float half = cum.CumAmount / 2f;
            // a random trace slot of the part, may spread to a connected part
            if (TraceUtility.Land(part, null, TraceUtility.CumTraceID, half, TraceSource.FromCum(cum)) != null) cum.CumAmount -= half;
        }
    }

    /// <summary>Puts item on the floor of c's room (destroyed when there is no room).</summary>
    public static void DropOnFloor(Character_Trainable c, Item_Instance item)
    {
        if (item == null) return;
        var room = c == null ? null : scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID);
        if (room == null || !room.AddItem(item))
        {
            Debug.LogWarning($"SingleUseEquipUtility: no room floor for [{item.DisplayName}] of {c?.FirstName}, discarded");
            scr_System_CampaignManager.current.Unregister(item);
        }
    }
}
