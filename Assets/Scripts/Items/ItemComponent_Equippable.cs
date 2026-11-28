using System;
using System.Collections.Generic;
using Newtonsoft.Json;

public class ItemComponentTemplate_Equippable : I_ItemComponentTemplate_Comp
{
    public List<BodyPartEquipSlot> equipSlot = new List<BodyPartEquipSlot>();
    public List<BodyPartEquipSlot> coverSlot = new List<BodyPartEquipSlot>();
    public BodyEquipLayer equipLayer = BodyEquipLayer.None;
    public Revealing revealing = Revealing.NonRevealing;
    public bool lockable = false;
    public int equipCount = 1;
    public List<string> equipTags = new List<string>();
    public List<Stat_Modifier> stat_modifiers = new List<Stat_Modifier>();

    public List<string> promptTags = new List<string>();

    /// <summary>Optional body/organ tags that must be present on the wearer's body (e.g. "breast", "vagina", "penis").</summary>
    public List<string> requireBodyTags = new List<string>();
    /// <summary>Optional body/organ tags that must not be present on the wearer's body (e.g. "penis").</summary>
    public List<string> forbidBodyTags = new List<string>();
    /// <summary>Optional static actor/appearance/gender tags that must all be present on the wearer.</summary>
    public List<string> requireActorTags = new List<string>();
    /// <summary>Optional static actor/appearance/gender tags where at least one must be present on the wearer (e.g. ["humanrace","humanoid","android"]).</summary>
    public List<string> requireActorTags_any = new List<string>();
    /// <summary>Optional static actor/appearance/gender tags that must not be present on the wearer.</summary>
    public List<string> forbidActorTags = new List<string>();
    /// <summary>Optional intended audience metadata (does not forbid equipping; shown in tooltip and used by NPCs to notice unusual outfits).</summary>
    public EquipIntendedFor intendedFor = null;

    /// <summary>
    /// One-time use (condoms): taking it off destroys the instance and leaves usedItemID on the wearer's room floor
    /// instead of keeping it (SingleUseEquipUtility).
    /// </summary>
    public bool singleUse = false;
    /// <summary>singleUse only: item base ID dropped on the floor when taken off ("" = nothing).</summary>
    public string usedItemID = "";

    [System.Serializable]
    public class PartialUndress
    {
        public string displayName = "";
        public string statusID = "";
    }
    public bool TryValidate(out string errorMsg)
    {
        errorMsg = "";
        if (equipLayer == BodyEquipLayer.None) errorMsg += "ItemComponentTemplate_Equippable: component is invalid, missing equipLayer data\n";
        
        if (equipSlot.Count < 1) errorMsg += "ItemComponentTemplate_Equippable: component is invalid, missing equipSlot data\n";
        
        if (errorMsg == "") return true;
        else return false;
    }

    public ItemComponent_Base Instantiate(Item_Base itemBase)
    {
        return new ItemComponent_Equippable(itemBase);
    }
}

/// <summary>
/// Advisory intended audience for an equippable garment (e.g. appearance: ["Female"] or ["Male"]).
/// Empty/null = unisex / unspecified. Never blocks equipping; displayed in tooltip and queryable by NPCs.
/// </summary>
[System.Serializable]
public class EquipIntendedFor
{
    public List<Humanoid_GenderAppearance> appearance = new List<Humanoid_GenderAppearance>();
    public List<string> tags = new List<string>();

    [JsonIgnore] public bool IsSpecified =>
        (appearance != null && appearance.Count > 0) || (tags != null && tags.Count > 0);

    public bool Matches(Character_Trainable c)
    {
        if (!IsSpecified || c == null) return true;
        if (appearance != null && appearance.Count > 0 && !appearance.Contains(c.Appearance))
            return false;
        if (tags != null && tags.Count > 0)
        {
            var actorTags = new List<string>();
            OutfitUtility.GetDressCodeTags(c, actorTags);
            for (int i = 0; i < tags.Count; i++)
                if (!actorTags.Contains(tags[i])) return false;
        }
        return true;
    }
}



[System.Serializable]
public class ItemComponent_Equippable : ItemComponent_Base
{
    [JsonIgnore] public override string CompType { get { return "ItemComponent_Equippable"; } }
    string _tooltip = null;
    [JsonIgnore] public override string Tooltip
    {
        get
        {
            if (_tooltip == null)
            {
                _tooltip = LocalizeDictionary.QueryThenParse("ItemComponent_Equippable_tooltip")
                    .Replace("$layer$", $"{equipLayer}")
                    .Replace("$slots$", String.Join("|", equipSlot))
                    .Replace("$covers$", coverSlot.Count > 0 ?
                        LocalizeDictionary.QueryThenParse("ItemComponent_Equippable_tooltip_covers")
                    .Replace("$layer$", $"{equipLayer}")
                    .Replace("$covers$", String.Join("|", coverSlot)) : "");
                string intendedText = BuildIntendedForTooltip();
                if (!string.IsNullOrEmpty(intendedText)) _tooltip += "\n" + intendedText;
                string restrictionText = BuildRestrictionTooltip();
                if (!string.IsNullOrEmpty(restrictionText)) _tooltip += "\n" + restrictionText;
            }
            return _tooltip;
        }
    }

    string BuildIntendedForTooltip()
    {
        if (intendedFor == null || !intendedFor.IsSpecified) return "";
        var labels = new List<string>();
        if (intendedFor.appearance != null)
        {
            for (int i = 0; i < intendedFor.appearance.Count; i++)
            {
                string key = "ItemComponent_Equippable_intendedFor_" + intendedFor.appearance[i];
                string localized = LocalizeDictionary.QueryThenParse(key);
                labels.Add(localized == key ? intendedFor.appearance[i].ToString() : localized);
            }
        }
        if (intendedFor.tags != null)
        {
            for (int i = 0; i < intendedFor.tags.Count; i++)
            {
                string tag = intendedFor.tags[i];
                string key = "equip_restriction_tag_" + tag;
                string localized = LocalizeDictionary.QueryThenParse(key);
                labels.Add(localized == key ? tag : localized);
            }
        }
        return String.Join(" / ", labels);
    }

    string BuildRestrictionTooltip()
    {
        if (!HasEquipRestrictions) return "";
        var parts = new List<string>();
        if (requireBodyTags.Count > 0)
            parts.Add(LocalizeDictionary.QueryThenParse("ItemComponent_Equippable_tooltip_requireBody")
                .Replace("$tags$", FormatRestrictionTags(requireBodyTags)));
        if (forbidBodyTags.Count > 0)
            parts.Add(LocalizeDictionary.QueryThenParse("ItemComponent_Equippable_tooltip_forbidBody")
                .Replace("$tags$", FormatRestrictionTags(forbidBodyTags)));
        if (requireActorTags.Count > 0)
            parts.Add(LocalizeDictionary.QueryThenParse("ItemComponent_Equippable_tooltip_requireActor")
                .Replace("$tags$", FormatRestrictionTags(requireActorTags)));
        if (requireActorTags_any.Count > 0)
            parts.Add(LocalizeDictionary.QueryThenParse("ItemComponent_Equippable_tooltip_requireActorAny")
                .Replace("$tags$", FormatRestrictionTags(requireActorTags_any)));
        if (forbidActorTags.Count > 0)
            parts.Add(LocalizeDictionary.QueryThenParse("ItemComponent_Equippable_tooltip_forbidActor")
                .Replace("$tags$", FormatRestrictionTags(forbidActorTags)));
        return String.Join("\n", parts);
    }

    static string FormatRestrictionTags(List<string> tags)
    {
        var names = new List<string>(tags.Count);
        for (int i = 0; i < tags.Count; i++)
        {
            string tag = tags[i];
            string key = "equip_restriction_tag_" + tag;
            string localized = LocalizeDictionary.QueryThenParse(key);
            names.Add(localized == key ? tag : localized);
        }
        return String.Join(", ", names);
    }
    public ItemComponent_Equippable()
    {

    }
    public ItemComponent_Equippable(Item_Base itemBase)
    {
        this.parent = itemBase;
        this.parentID = itemBase.ID;
    }

    public override bool canMergeWith(ItemComponent_Base other)
    {
        return false;
    }

    [JsonIgnore] public List<BodyPartEquipSlot> equipSlot { get { return CompTemplate.comp_Equippable.equipSlot; } }
    [JsonIgnore] public List<BodyPartEquipSlot> coverSlot { get { return CompTemplate.comp_Equippable.coverSlot; } }
    [JsonIgnore] public BodyEquipLayer equipLayer { get { return CompTemplate.comp_Equippable.equipLayer; } }
    [JsonIgnore] public Revealing revealing { get { return CompTemplate.comp_Equippable.revealing; } }
    [JsonIgnore] public int equipCount { get { return CompTemplate.comp_Equippable.equipCount; } }
    [JsonIgnore] public List<string> equipTags { get { return CompTemplate.comp_Equippable.equipTags; } }
    [JsonIgnore] public List<Stat_Modifier> statModifiers { get { return CompTemplate.comp_Equippable.stat_modifiers; } }

    [JsonIgnore] public bool lockable { get { return CompTemplate.comp_Equippable.lockable; } }
    [JsonIgnore] public bool singleUse { get { return CompTemplate.comp_Equippable.singleUse; } }
    [JsonIgnore] public string usedItemID { get { return CompTemplate.comp_Equippable.usedItemID; } }

    [JsonIgnore] public List<string> promptTags { get { return CompTemplate.comp_Equippable.promptTags; } }
    [JsonIgnore] public List<string> requireBodyTags { get { return CompTemplate.comp_Equippable.requireBodyTags; } }
    [JsonIgnore] public List<string> forbidBodyTags { get { return CompTemplate.comp_Equippable.forbidBodyTags; } }
    [JsonIgnore] public List<string> requireActorTags { get { return CompTemplate.comp_Equippable.requireActorTags; } }
    [JsonIgnore] public List<string> requireActorTags_any { get { return CompTemplate.comp_Equippable.requireActorTags_any; } }
    [JsonIgnore] public List<string> forbidActorTags { get { return CompTemplate.comp_Equippable.forbidActorTags; } }
    [JsonIgnore] public EquipIntendedFor intendedFor { get { return CompTemplate.comp_Equippable.intendedFor; } }

    public bool IsIntendedFor(Character_Trainable c) => intendedFor == null || intendedFor.Matches(c);
    public bool IsUnusualFor(Character_Trainable c) => intendedFor != null && intendedFor.IsSpecified && !intendedFor.Matches(c);

    [JsonIgnore] public bool HasEquipRestrictions =>
        (requireBodyTags != null && requireBodyTags.Count > 0)
        || (forbidBodyTags != null && forbidBodyTags.Count > 0)
        || (requireActorTags != null && requireActorTags.Count > 0)
        || (requireActorTags_any != null && requireActorTags_any.Count > 0)
        || (forbidActorTags != null && forbidActorTags.Count > 0);

    public bool CanBeWornBy(Character_Trainable c, out string reason, bool explain = true)
    {
        reason = "";
        if (!HasEquipRestrictions || c == null) return true;

        if (requireBodyTags != null)
        {
            for (int i = 0; i < requireBodyTags.Count; i++)
            {
                if (!CharacterHasBodyTag(c, requireBodyTags[i]))
                {
                    if (explain)
                        reason = LocalizeDictionary.QueryThenParse("ItemComponent_Equippable_tooltip_requireBody")
                            .Replace("$tags$", FormatRestrictionTags(new List<string> { requireBodyTags[i] }));
                    return false;
                }
            }
        }
        if (forbidBodyTags != null)
        {
            for (int i = 0; i < forbidBodyTags.Count; i++)
            {
                if (CharacterHasBodyTag(c, forbidBodyTags[i]))
                {
                    if (explain)
                        reason = LocalizeDictionary.QueryThenParse("ItemComponent_Equippable_tooltip_forbidBody")
                            .Replace("$tags$", FormatRestrictionTags(new List<string> { forbidBodyTags[i] }));
                    return false;
                }
            }
        }

        if ((requireActorTags != null && requireActorTags.Count > 0)
            || (requireActorTags_any != null && requireActorTags_any.Count > 0)
            || (forbidActorTags != null && forbidActorTags.Count > 0))
        {
            var actorTags = new List<string>();
            OutfitUtility.GetDressCodeTags(c, actorTags);
            if (requireActorTags != null)
            {
                for (int i = 0; i < requireActorTags.Count; i++)
                {
                    if (!actorTags.Contains(requireActorTags[i]))
                    {
                        if (explain)
                            reason = LocalizeDictionary.QueryThenParse("ItemComponent_Equippable_tooltip_requireActor")
                                .Replace("$tags$", FormatRestrictionTags(new List<string> { requireActorTags[i] }));
                        return false;
                    }
                }
            }
            if (requireActorTags_any != null && requireActorTags_any.Count > 0)
            {
                bool matchedAny = false;
                for (int i = 0; i < requireActorTags_any.Count; i++)
                {
                    if (actorTags.Contains(requireActorTags_any[i])) { matchedAny = true; break; }
                }
                if (!matchedAny)
                {
                    if (explain)
                        reason = LocalizeDictionary.QueryThenParse("ItemComponent_Equippable_tooltip_requireActorAny")
                            .Replace("$tags$", FormatRestrictionTags(requireActorTags_any));
                    return false;
                }
            }
            if (forbidActorTags != null)
            {
                for (int i = 0; i < forbidActorTags.Count; i++)
                {
                    if (actorTags.Contains(forbidActorTags[i]))
                    {
                        if (explain)
                            reason = LocalizeDictionary.QueryThenParse("ItemComponent_Equippable_tooltip_forbidActor")
                                .Replace("$tags$", FormatRestrictionTags(new List<string> { forbidActorTags[i] }));
                        return false;
                    }
                }
            }
        }
        return true;
    }

    static bool CharacterHasBodyTag(Character_Trainable c, string tag)
    {
        if (c == null || string.IsNullOrEmpty(tag)) return false;
        if (c.Body != null && c.Body.Body != null)
        {
            foreach (var part in c.Body.Body)
            {
                if (part == null) continue;
                if (part.Base?.tags != null && part.Base.tags.Contains(tag)) return true;
                if (part.internals != null)
                {
                    foreach (var organ in part.internals)
                    {
                        if (organ?.Base?.tags != null && organ.Base.tags.Contains(tag)) return true;
                    }
                }
            }
        }
        if (scr_System_CentralControl.current != null && scr_System_CentralControl.current.isSafeMode)
        {
            if (tag == "penis") return c.isMale;
            if (tag == "breast" || tag == "vagina" || tag == "clit") return c.isFemale;
        }
        return false;
    }

    public bool hasTag(string s)
    {
        return this.equipTags.Contains(s);
    }

}