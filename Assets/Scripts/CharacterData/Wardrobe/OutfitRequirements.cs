using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Data dress code. All fields are ANDed; alternatives, when supplied, require one matching branch.</summary>
[System.Serializable]
public class OutfitRequirements
{
    public List<string> anyTags = new List<string>();
    public List<string> allTags = new List<string>();
    public List<string> excludeTags = new List<string>();
    public List<string> baseIDs = new List<string>();
    public Dictionary<BodyPartEquipSlot, Revealing> coverage = new Dictionary<BodyPartEquipSlot, Revealing>();
    /// <summary>Each applicable slot must be occupied/covered by a garment with any of these tags.</summary>
    public Dictionary<BodyPartEquipSlot, List<string>> slotTags = new Dictionary<BodyPartEquipSlot, List<string>>();
    /// <summary>Required garment tags on the slots this character's personality requires covered (e.g. swimwear).</summary>
    public List<string> personalitySlotTags = new List<string>();
    public List<OutfitRequirements> alternatives = new List<OutfitRequirements>();
    public List<OutfitRequirements> allOf = new List<OutfitRequirements>();
    /// <summary>false = additional rules on top of the character's base dress code; true = replaces the base dress code.</summary>
    public bool replaceBaseDressCode = false;
    /// <summary>Optional actor/appearance/gender conditional dress code rules; highest-priority matching rule applies.</summary>
    public List<DressCodeRule> conditionalRules = new List<DressCodeRule>();

    public bool HasDirectConstraints =>
        (anyTags?.Count > 0) || (allTags?.Count > 0) || (excludeTags?.Count > 0) || (baseIDs?.Count > 0)
        || (coverage?.Count > 0) || (slotTags?.Count > 0) || (personalitySlotTags?.Count > 0)
        || (alternatives?.Count > 0) || (allOf?.Count > 0);

    public static OutfitRequirements Combine(OutfitRequirements first, OutfitRequirements second)
    {
        if (first == null) return second;
        if (second == null || ReferenceEquals(first, second)) return first;
        return new OutfitRequirements { allOf = new List<OutfitRequirements> { first, second } };
    }

    /// <summary>
    /// Resolve any conditionalRules for the character's static dress-code tags (actor keywords + appearance_* + gender_*).
    /// Returns the effective OutfitRequirements (may be null if no direct constraints and no matched rule) and whether
    /// this requirement (or its matched rule) replaces the character's base dress code.
    /// </summary>
    public OutfitRequirements ResolveForCharacter(List<string> actorTags, out bool replacesBase)
    {
        replacesBase = replaceBaseDressCode;
        var matched = DressCodeRule.SelectBest(conditionalRules, actorTags);
        if (matched == null) return HasDirectConstraints ? this : null;
        replacesBase |= matched.replaceBaseDressCode;
        var ruleReqs = matched.ResolveRequirements();
        if (!HasDirectConstraints) return ruleReqs;
        return Combine(this, ruleReqs);
    }

    public bool Check(OutfitEquipmentPreview equipment, out string reason,
        IReadOnlyDictionary<BodyPartEquipSlot, Revealing> personalityCoverage = null, bool explain = true)
    {
        if (allOf != null) foreach (var requirement in allOf)
            if (requirement != null && !requirement.Check(equipment, out reason, personalityCoverage, explain)) return false;
        reason = "";
        var items = equipment.Items;
        if (anyTags?.Count > 0 && !items.Any(i => anyTags.Any(t => i.Tags.Contains(t))))
        { if (explain) reason = "Requires clothing: " + string.Join(", ", anyTags); return false; }
        if (allTags != null) foreach (var tag in allTags)
            if (!items.Any(i => i.Tags.Contains(tag))) { if (explain) reason = "Requires clothing: " + tag; return false; }
        if (excludeTags != null) foreach (var tag in excludeTags)
            if (items.Any(i => i.Tags.Contains(tag))) { if (explain) reason = "Clothing is not allowed: " + tag; return false; }
        if (baseIDs?.Count > 0 && !items.Any(i => baseIDs.Contains(i.BaseID)))
        { if (explain) reason = "Requires one of: " + string.Join(", ", baseIDs.Select(id => LocalizeDictionary.QueryThenParse(id))); return false; }
        if (!equipment.CheckCoverage(coverage, out reason, explain)) return false;
        if (slotTags != null) foreach (var rule in slotTags)
            if (!equipment.CheckSlotTags(rule.Key, rule.Value))
            { if (explain) reason = rule.Key + " requires clothing: " + string.Join(", ", rule.Value); return false; }
        if (personalitySlotTags?.Count > 0 && personalityCoverage != null) foreach (var rule in personalityCoverage)
            if ((int)rule.Value > 0 && !equipment.CheckSlotTags(rule.Key, personalitySlotTags))
            { if (explain) reason = rule.Key + " requires clothing: " + string.Join(", ", personalitySlotTags); return false; }
        if (alternatives?.Count > 0 && !alternatives.Any(a => a != null && a.Check(equipment, out _, personalityCoverage, explain: false)))
        { if (explain) reason = "No dress code alternative is satisfied."; return false; }
        reason = "";
        return true;
    }
}

/// <summary>Named dress code stored in Character_Personalities.dressCodes. Holds full OutfitRequirements directly.</summary>
[System.Serializable]
public class DressCode : OutfitRequirements
{
    public string ID = "";
    public static readonly DressCode Empty = new DressCode { ID = "dresscode_none" };
}

/// <summary>
/// Conditional rule matching a character's static dress-code tags (ActorKeywords + appearance_* + gender_*).
/// Used both in Character_Personalities.defaultDressCodes and in activity/memberType OutfitRequirements.conditionalRules.
/// </summary>
[System.Serializable]
public class DressCodeRule
{
    public string ID = "";
    public int priority = 0;
    public List<string> requireTags_all = new List<string>();
    public List<string> requireTags_any = new List<string>();
    public List<string> excludeTags_any = new List<string>();
    /// <summary>ID of a DressCode in Character_Personalities.dressCodes (optional if inline requirements are provided).</summary>
    public string dressCodeID = "";
    /// <summary>Optional inline requirements (combined with dressCodeID if both are set).</summary>
    public OutfitRequirements requirements = null;
    /// <summary>For activity/memberType overrides: false = additional rules on top of base dress code; true = replaces base dress code.</summary>
    public bool replaceBaseDressCode = false;

    public bool Matches(List<string> tags)
    {
        if (requireTags_any != null && requireTags_any.Count > 0 && (tags == null || !requireTags_any.Exists(tags.Contains))) return false;
        if (requireTags_all != null && requireTags_all.Count > 0 && (tags == null || !requireTags_all.TrueForAll(tags.Contains))) return false;
        if (excludeTags_any != null && tags != null && excludeTags_any.Exists(tags.Contains)) return false;
        return true;
    }

    public DressCode ResolveDressCode()
    {
        if (string.IsNullOrEmpty(dressCodeID)) return null;
        return scr_System_Serializer.current?.MasterList?.Character_Personalities?.GetDressCodeByID(dressCodeID);
    }

    public OutfitRequirements ResolveRequirements()
    {
        return OutfitRequirements.Combine(ResolveDressCode(), requirements);
    }

    /// <summary>Returns the highest-priority matching rule (later entry wins ties), or null if none match.</summary>
    public static DressCodeRule SelectBest(List<DressCodeRule> rules, List<string> tags)
    {
        if (rules == null || rules.Count == 0) return null;
        DressCodeRule best = null;
        for (int i = 0; i < rules.Count; i++)
        {
            var rule = rules[i];
            if (rule == null || !rule.Matches(tags)) continue;
            if (best == null || rule.priority >= best.priority) best = rule;
        }
        return best;
    }
}

/// <summary>
/// Detached slot projection. Never equips, clones a character, registers items, or fires live equipment callbacks.
/// Edited outfits reject conflicting pieces rather than silently dropping one as the legacy data-outfit path does.
/// </summary>
public class OutfitEquipmentPreview
{
    sealed class Holder
    {
        public readonly Dictionary<(BodyEquipLayer layer, BodyPartEquipSlot slot), Item_Instance> main = new Dictionary<(BodyEquipLayer, BodyPartEquipSlot), Item_Instance>();
        public readonly Dictionary<(BodyEquipLayer layer, BodyPartEquipSlot slot), Item_Instance> covers = new Dictionary<(BodyEquipLayer, BodyPartEquipSlot), Item_Instance>();
        public bool external;
        public Holder parent;
        public List<BodyPartEquipSlot> underSlots;
    }
    readonly Character_Trainable character;
    readonly List<Holder> holders = new List<Holder>();
    readonly Dictionary<Item_Instance, int> equipped = new Dictionary<Item_Instance, int>();
    readonly List<(BodyEquipLayer, BodyPartEquipSlot)> slotKeys = new List<(BodyEquipLayer, BodyPartEquipSlot)>();
    readonly List<BodyPartEquipSlot> remainingCovers = new List<BodyPartEquipSlot>();
    readonly HashSet<BodyPartEquipSlot> blockedCovers = new HashSet<BodyPartEquipSlot>();
    readonly List<(Dictionary<(BodyEquipLayer, BodyPartEquipSlot), Item_Instance> table,
        (BodyEquipLayer, BodyPartEquipSlot) key, Item_Instance item)> originalEquipment
        = new List<(Dictionary<(BodyEquipLayer, BodyPartEquipSlot), Item_Instance>, (BodyEquipLayer, BodyPartEquipSlot), Item_Instance)>();
    readonly List<Item_Instance> originalItems = new List<Item_Instance>();
    readonly List<Item_Instance> outfitItems = new List<Item_Instance>();
    readonly List<Item_Instance> pendingRedress = new List<Item_Instance>();
    readonly List<Item_Instance> projectedTakenOff = new List<Item_Instance>();
    public IEnumerable<Item_Instance> Items => equipped.Keys;

    void CaptureSlot(Dictionary<(BodyEquipLayer, BodyPartEquipSlot), Item_Instance> table,
        (BodyEquipLayer, BodyPartEquipSlot) key, Item_Instance item)
    {
        SetSlot(table, key, item);
        if (item != null) originalEquipment.Add((table, key, item));
    }

    internal void Reset()
    {
        foreach (var holder in holders)
        {
            ClearSlots(holder.main);
            ClearSlots(holder.covers);
        }
        foreach (var entry in originalEquipment) SetSlot(entry.table, entry.key, entry.item);
    }

    void ClearSlots(Dictionary<(BodyEquipLayer, BodyPartEquipSlot), Item_Instance> table)
    {
        slotKeys.Clear();
        foreach (var slot in table) if (slot.Value != null) slotKeys.Add(slot.Key);
        foreach (var key in slotKeys) SetSlot(table, key, null);
    }

    void SetSlot(Dictionary<(BodyEquipLayer, BodyPartEquipSlot), Item_Instance> table,
        (BodyEquipLayer, BodyPartEquipSlot) key, Item_Instance item)
    {
        table.TryGetValue(key, out var previous);
        table[key] = item;
        if (previous == item) return;
        if (previous != null)
        {
            int count = equipped[previous] - 1;
            if (count == 0) equipped.Remove(previous);
            else equipped[previous] = count;
        }
        if (item != null)
        {
            equipped.TryGetValue(item, out int count);
            equipped[item] = count + 1;
        }
    }

    public OutfitEquipmentPreview(Character_Trainable character)
    {
        this.character = character;
        if (character?.Body == null) return;
        foreach (var part in character.Body.Body)
        {
            var h = new Holder { external = true };
            foreach (var layer in part.equipLayers) foreach (var slot in part.availableSlots)
            {
                part.TryGetEquip(out var item, layer, slot);
                part.TryGetCover(out var cover, layer, slot);
                CaptureSlot(h.main, (layer, slot), item);
                CaptureSlot(h.covers, (layer, slot), cover);
            }
            holders.Add(h);
            foreach (var organ in part.internals)
            {
                var inner = new Holder { parent = h, underSlots = organ.Base.coverSlots?.Count > 0 ? organ.Base.coverSlots : part.availableSlots };
                foreach (var layer in organ.equipLayers) foreach (var slot in organ.availableSlots)
                {
                    organ.TryGetEquip(out var item, layer, slot);
                    CaptureSlot(inner.main, (layer, slot), item);
                }
                holders.Add(inner);
            }
        }
    }

    void Remove(Item_Instance item)
    {
        foreach (var h in holders)
        {
            Remove(h.main, item);
            Remove(h.covers, item);
        }
    }

    void Remove(Dictionary<(BodyEquipLayer, BodyPartEquipSlot), Item_Instance> table, Item_Instance item)
    {
        slotKeys.Clear();
        foreach (var slot in table) if (slot.Value == item) slotKeys.Add(slot.Key);
        foreach (var key in slotKeys) SetSlot(table, key, null);
    }

    bool PutOn(Item_Instance item, bool replace, out string reason, bool explain)
    {
        reason = "";
        var comp = item?.Comp_Equippable;
        if (comp == null || comp.equipSlot.Count == 0) { if (explain) reason = "Clothing has no equipment slots."; return false; }
        if (!comp.CanBeWornBy(character, out var restrictReason, explain))
        {
            if (explain) reason = item.DisplayName + ": " + restrictReason;
            return false;
        }
        Holder target = null;
        bool hasMatchingSlots = false;
        foreach (var holder in holders)
        {
            bool fits = true;
            bool blocked = false;
            foreach (var slot in comp.equipSlot)
            {
                if (!holder.main.TryGetValue((comp.equipLayer, slot), out var old)) { fits = false; break; }
                if (old != null && old != item && (!replace || !Character_Body.CanReplaceOutfitEquipment(old)))
                    blocked = true;
            }
            if (!fits) continue;
            hasMatchingSlots = true;
            if (!blocked) target = holder;
            if (target != null || !replace) break; // Live redress uses the first matching holder.
        }
        if (target == null)
        {
            if (explain)
                reason = item.DisplayName + (hasMatchingSlots ? " conflicts with other clothing." : " does not fit this body.");
            return false;
        }
        foreach (var slot in comp.equipSlot)
        {
            var key = (comp.equipLayer, slot);
            var old = target.main[key];
            if (old != null && old != item)
            {
                projectedTakenOff.Add(old);
                Remove(old);
            }
            SetSlot(target.main, key, item);
        }
        var remaining = remainingCovers;
        remaining.Clear();
        remaining.AddRange(comp.coverSlot);
        blockedCovers.Clear();
        foreach (var h in holders)
        {
            var table = h.external ? h.covers : h.main;
            for (int i = remaining.Count - 1; i >= 0; i--)
            {
                var key = (comp.equipLayer, remaining[i]);
                if (!table.ContainsKey(key) || (h.external && h.main[key] == item)) continue;
                var old = table[key];
                if (old == item) continue;
                if (old != null)
                {
                    if (!Character_Body.CanReplaceOutfitEquipment(old))
                    {
                        if (replace) blockedCovers.Add(remaining[i]);
                        continue;
                    }
                    if (replace) projectedTakenOff.Add(old);
                    Remove(old);
                }
                SetSlot(table, key, item);
                remaining.RemoveAt(i);
            }
        }
        foreach (var slot in remaining)
        {
            if (!blockedCovers.Contains(slot)) continue;
            if (explain) reason = item.DisplayName + " conflicts with other clothing.";
            return false;
        }
        return true;
    }

    public bool Apply(Character_Trainable character, OutfitSet resolved, out string reason, bool explain = true)
    {
        outfitItems.Clear();
        projectedTakenOff.Clear();
        originalItems.Clear();
        originalItems.AddRange(equipped.Keys);
        foreach (var old in originalItems)
            if (old.RefID <= -2 || (resolved.Overrides(old.Comp_Equippable.equipLayer)
                && Character_Trainable.CanUnequip(old.Comp_Equippable, unequipArmor: true)
                && holders.Any(h => h.external && h.main.Values.Contains(old)))) Remove(old);
        if (resolved.pieces != null) foreach (var piece in resolved.pieces)
        {
            var item = OutfitUtility.CreatePiece(piece, out reason);
            if (item == null) return false;
            if (!PutOn(item, true, out reason, explain)) return false;
            outfitItems.Add(item);
        }
        // Redress fills empty slots only, in layer order, outside overridden layers.
        character.BuildRedressCandidates(BodyEquipLayer.None, pendingRedress, resolved, projectedTakenOff);
        foreach (var layer in OutfitUtility.AllLayers)
        {
            if (resolved.Overrides(layer)) continue;
            foreach (var item in pendingRedress)
                if (item.Comp_Equippable.equipLayer == layer && !equipped.ContainsKey(item)) PutOn(item, false, out _, explain: false);
        }
        if (outfitItems.Any(item => !equipped.ContainsKey(item)))
        { reason = "Inventory clothing displaces an outfit piece. Replace that layer or remove the conflicting piece."; return false; }
        reason = "";
        return true;
    }

    public bool CheckCoverage(IReadOnlyDictionary<BodyPartEquipSlot, Revealing> requirements, out string reason, bool explain = true)
    {
        if (requirements != null) foreach (var rule in requirements)
        {
            foreach (var h in holders)
            {
                if (!h.main.Keys.Any(k => k.slot == rule.Key)) continue;
                int score = h.parent == null ? Coverage(h, rule.Key) : h.underSlots.Where(s => h.parent.main.Keys.Any(k => k.slot == s))
                    .Select(s => Coverage(h.parent, s)).DefaultIfEmpty(0).Min();
                if (score < (int)rule.Value)
                { reason = explain ? rule.Key + " requires " + rule.Value + " coverage (current: " + score + ")." : ""; return false; }
            }
        }
        reason = "";
        return true;
    }

    public bool CheckSlotTags(BodyPartEquipSlot slot, List<string> tags)
    {
        foreach (var h in holders)
        {
            if (!h.main.Keys.Any(k => k.slot == slot)) continue;
            if (h.parent != null)
            {
                if (h.underSlots.Any(s => h.parent.main.Keys.Any(k => k.slot == s) && !HasSlotTags(h.parent, s, tags))) return false;
            }
            else if (!HasSlotTags(h, slot, tags)) return false;
        }
        return true;
    }

    static bool HasSlotTags(Holder holder, BodyPartEquipSlot slot, List<string> tags) => holder.main.Concat(holder.covers)
        .Any(kv => kv.Key.slot == slot && kv.Value != null && tags != null && tags.Any(t => kv.Value.Tags.Contains(t)));

    static int Coverage(Holder holder, BodyPartEquipSlot slot)
    {
        int score = 0;
        foreach (var layer in OutfitUtility.AllLayers)
        {
            holder.main.TryGetValue((layer, slot), out var item);
            holder.covers.TryGetValue((layer, slot), out var cover);
            score += BodyPart_Instance.SlotRevealingScore(item, cover);
        }
        return score;
    }
}
