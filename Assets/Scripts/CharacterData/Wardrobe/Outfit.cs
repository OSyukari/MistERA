using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// One piece of an outfit: only the item's identity. The worn piece is a virtual Item_Instance built from it
/// (Character_Wardrobe.GetVirtualItem) - never instantiated through WorldManager, never registered or traded.
/// </summary>

public class OutfitPiece
{
    public string baseID = "";
    public string nameOverwrite = "";

    public OutfitPiece() { }
    public OutfitPiece(string baseID, string nameOverwrite = "")
    {
        this.baseID = baseID ?? "";
        this.nameOverwrite = nameOverwrite ?? "";
    }


    string _name = null;
    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            if (_name == null)
            {
                _name = nameOverwrite != "" ? LocalizeDictionary.QueryThenParse(nameOverwrite) : LocalizeDictionary.QueryThenParse(baseID);
            }
            return _name;
        }
    }

    public OutfitPiece Clone() { return new OutfitPiece(baseID, nameOverwrite); }

    /// <summary> copy an item's identity into a piece; null if the item can't be an outfit piece (see OutfitUtility.CanBePiece) </summary>
    public static OutfitPiece FromItem(Item_Instance item)
    {
        if (item == null) return null;
        var piece = new OutfitPiece(item.BaseID, item.nameOverwrite);
        return OutfitUtility.CanBePiece(piece, out _) ? piece : null;
    }
}

/// <summary>
/// An outfit worn for one schedule key (OutfitUtility.MemberTypeKey / ActivityKey). Putting it on takes off the
/// worn items of every overridden layer, then wears all pieces; the rest of the default clothes (inventory) stay on.
/// </summary>

public class OutfitSet
{
    public string key = "";

    string _cache_displayname = null;
    [JsonIgnore]
    public string DisplayName
    {
        get
        {
            if (_cache_displayname == null) _cache_displayname = OutfitUtility.KeyDisplayName(key);
            return _cache_displayname;
        }
    }


    public List<OutfitPiece> pieces = new List<OutfitPiece>();

    public bool overrideSkin = false;
    public bool overrideInner = false;
    public bool overrideOuter = false;

    /// <summary>
    /// true: only put on while standing in a room of the job giver the key belongs to (changing at the workplace);
    /// elsewhere the character changes back to the default clothes instead. false: put on anywhere.
    /// </summary>
    public bool limitToLocaleFaction = true;

    /// <summary>
    /// Layers of this outfit kept on to sleep; the other layers come off (Skin included). Empty = the outfit isn't slept
    /// in: going to sleep changes into the sleep outfit (#sleep / #sleep_temp) or strips to the skin layer as usual.
    /// E.g. a hospital gown ["Skin","Inner","Outer"], an expedition outfit ["Skin","Inner"].
    /// </summary>
    public List<BodyEquipLayer> sleepKeepLayers = new List<BodyEquipLayer>();

    /// <summary>
    /// Layers of this outfit kept on while its party rests (expedition resting: meal / night hours, awake); the other
    /// layers come off until the rest is over. Empty = no special handling (everything stays on). Only party outfits rest.
    /// </summary>
    public List<BodyEquipLayer> restKeepLayers = new List<BodyEquipLayer>();

    /// <summary> optional hairstyle shown while the outfit is worn (the own hairstyle is untouched) </summary>
    public Hairstyle_Instance hairstyleOverride = null;
    /// <summary> optional traits_BODY trait ids shown while the outfit is worn (display only, see Character_Body.GetEffectiveBodyTrait) </summary>
    public List<string> bodyTraitOverrides = new List<string>();
    List<Traits> _cache_bodyTraitsOverride = null;
    [JsonIgnore]
    public List<Traits> BodyTraitsOverride
    {
        get
        {
            if (_cache_bodyTraitsOverride == null)
            {
                _cache_bodyTraitsOverride = new List<Traits>();
                foreach(var i in bodyTraitOverrides)
                {
                    var trait = scr_System_Serializer.current.MasterList.Traits_Groups.GetTraitByID(i);
                    if (trait != null) _cache_bodyTraitsOverride.Add(trait);
                }
            }
            return _cache_bodyTraitsOverride;
        }
    }

    public Traits GetTraitWithParentID(string ID)
    {
        foreach(var i in BodyTraitsOverride)
        {
            if (i.Parent.ID == ID) return i;
        }
        return null;
    }

    /// <summary>
    /// Tag-conditional additions (season / holiday - OutfitUtility.GetContextTags), checked in order: a variant without
    /// group applies whenever it matches; within one group only the first matching variant applies, so a last variant
    /// without conditions is the group's "else". See Resolve.
    /// </summary>
    public List<OutfitVariant> variants = new List<OutfitVariant>();

    public bool Overrides(BodyEquipLayer layer)
    {
        switch (layer)
        {
            case BodyEquipLayer.Skin: return overrideSkin;
            case BodyEquipLayer.Inner: return overrideInner;
            case BodyEquipLayer.Outer: return overrideOuter;
            default: return false;
        }
    }

    /// <summary>
    /// What this outfit is for these context tags: a copy without variants - base pieces plus the matching variants'
    /// pieces, override layers OR-ed in. signature = the matched variants' indexes ("" = none), to tell when the result
    /// changes (Character_Wardrobe re-applies the outfit then).
    /// </summary>
    public OutfitSet Resolve(List<string> tags, out string signature)
    {
        var result = Clone();
        result.variants.Clear();
        var matched = new List<int>();
        var usedGroups = new HashSet<string>();
        if (variants != null)
        {
            for (int i = 0; i < variants.Count; i++)
            {
                var v = variants[i];
                if (v == null) continue;
                bool grouped = !string.IsNullOrEmpty(v.group);
                if (grouped && usedGroups.Contains(v.group)) continue;
                if (!v.Matches(tags)) continue;
                if (grouped) usedGroups.Add(v.group);
                matched.Add(i);
                if (v.pieces != null) foreach (var p in v.pieces) if (p != null) result.pieces.Add(p.Clone());
                result.overrideSkin |= v.overrideSkin;
                result.overrideInner |= v.overrideInner;
                result.overrideOuter |= v.overrideOuter;
            }
        }
        signature = string.Join(",", matched);
        return result;
    }

    /// <summary> nothing to put on or take off: no pieces, no override layers (a #default whose variants don't match) </summary>
    [Newtonsoft.Json.JsonIgnore] public bool IsEmpty
    {
        get { return (pieces == null || pieces.Count == 0) && !overrideSkin && !overrideInner && !overrideOuter; }
    }

    public OutfitSet Clone()
    {
        var copy = new OutfitSet();
        copy.key = key;
        if (pieces != null) foreach (var p in pieces) if (p != null) copy.pieces.Add(p.Clone());
        copy.overrideSkin = overrideSkin;
        copy.overrideInner = overrideInner;
        copy.overrideOuter = overrideOuter;
        copy.limitToLocaleFaction = limitToLocaleFaction;
        if (sleepKeepLayers != null) copy.sleepKeepLayers.AddRange(sleepKeepLayers);
        if (restKeepLayers != null) copy.restKeepLayers.AddRange(restKeepLayers);
        copy.hairstyleOverride = hairstyleOverride?.Clone();
        if (bodyTraitOverrides != null) copy.bodyTraitOverrides.AddRange(bodyTraitOverrides);
        if (variants != null) foreach (var v in variants) if (v != null) copy.variants.Add(v.Clone());
        return copy;
    }
}

/// <summary>
/// Pieces (and optionally override layers) an outfit adds while the context tags match - e.g. a coat and gloves in
/// "winter". Conditions only read a tag list (OutfitUtility.GetContextTags); all empty = always matches.
/// </summary>
[System.Serializable]
public class OutfitVariant
{
    /// <summary> optional: within a group only the first matching variant applies (a conditionless last one = "else") </summary>
    public string group = "";
    public List<string> requireTags_any = new List<string>();
    public List<string> requireTags_all = new List<string>();
    public List<string> excludeTags_any = new List<string>();

    public List<OutfitPiece> pieces = new List<OutfitPiece>();

    /// <summary> added to the outfit's own override layers while this variant applies </summary>
    public bool overrideSkin = false;
    public bool overrideInner = false;
    public bool overrideOuter = false;

    public bool Matches(List<string> tags)
    {
        if (requireTags_any != null && requireTags_any.Count > 0 && (tags == null || !requireTags_any.Exists(tags.Contains))) return false;
        if (requireTags_all != null && requireTags_all.Count > 0 && (tags == null || !requireTags_all.TrueForAll(tags.Contains))) return false;
        if (excludeTags_any != null && tags != null && excludeTags_any.Exists(tags.Contains)) return false;
        return true;
    }

    public OutfitVariant Clone()
    {
        var copy = new OutfitVariant();
        copy.group = group;
        if (requireTags_any != null) copy.requireTags_any.AddRange(requireTags_any);
        if (requireTags_all != null) copy.requireTags_all.AddRange(requireTags_all);
        if (excludeTags_any != null) copy.excludeTags_any.AddRange(excludeTags_any);
        if (pieces != null) foreach (var p in pieces) if (p != null) copy.pieces.Add(p.Clone());
        copy.overrideSkin = overrideSkin;
        copy.overrideInner = overrideInner;
        copy.overrideOuter = overrideOuter;
        return copy;
    }
}

/// <summary> what a wardrobe key stands for (OutfitUtility.ParseKey) </summary>
public enum OutfitKeyKind
{
    None,
    /// <summary> character-own reserved key: #default, #home, #home_temp, #sleep, #sleep_temp </summary>
    Reserved,
    /// <summary> "factionID|keyTypeID" - OutfitUtility.MemberTypeKey </summary>
    MemberType,
    /// <summary> "factionID:activityID" - OutfitUtility.ActivityKey </summary>
    Activity,
    /// <summary> "keyTypeID" - OutfitUtility.GenericMemberTypeKey (template presets only) </summary>
    Generic,
    /// <summary> "@party:jobRef" - Manageable_Party.OutfitKey (the party's wardrobe, never the character's) </summary>
    Party,
}

/// <summary>
/// One outfit slot of a wardrobe as listed (Character_Wardrobe.GetEntries): the key, what it stands for, and its outfit
/// (null = none set - the default clothes are worn for it).
/// </summary>
public class WardrobeEntry
{
    public readonly string key;
    public readonly OutfitKeyKind kind;
    /// <summary> MemberType / Activity keys: the faction the key belongs to, else "" </summary>
    public readonly string factionID;
    /// <summary> MemberType: the key type's ID; Activity: the activity ID; Reserved / Generic: the key itself </summary>
    public readonly string id;
    public readonly OutfitSet outfit;

    public WardrobeEntry(string key, OutfitSet outfit)
    {
        this.key = key ?? "";
        this.outfit = outfit;
        kind = OutfitUtility.ParseKey(this.key, out factionID, out id);
    }

    [JsonIgnore] public bool HasOutfit { get { return outfit != null; } }
    [JsonIgnore] public string DisplayName { get { return OutfitUtility.KeyDisplayName(key); } }
}

public static class OutfitUtility
{
    public const string trait_BodyType = "trait_BodyType";
    public const string trait_SkinColor = "trait_SkinColor";
    public const string trait_HairColor = "trait_HairColor";
    public const string trait_EyeColor = "trait_EyeColor";

    // character-own outfits (reserved keys): worn at the priority home / to sleep there. "_temp" = the priority home is
    // the temporary home; an empty _temp slot does not fall back to the regular one.
    public const string Key_Home = "#home";
    public const string Key_HomeTemp = "#home_temp";
    public const string Key_Sleep = "#sleep";
    public const string Key_SleepTemp = "#sleep_temp";
    /// <summary>
    /// worn wherever the default clothes (inventory) are: usually no pieces / overrides, only variants (a winter coat...).
    /// Resolving to nothing (IsEmpty) counts as the plain default clothes - no change trip for it.
    /// </summary>
    public const string Key_Default = "#default";

    /// <summary> every reserved key, in wardrobe UI order </summary>
    public static readonly string[] ReservedKeys = { Key_Default, Key_Home, Key_HomeTemp, Key_Sleep, Key_SleepTemp };

    /// <summary>
    /// What key stands for, split into its parts: factionID for membertype / activity keys (else ""), id = the key type
    /// ID / activity ID (reserved / generic: the key itself, party: the job ref).
    /// </summary>
    public static OutfitKeyKind ParseKey(string key, out string factionID, out string id)
    {
        factionID = "";
        id = key ?? "";
        if (string.IsNullOrEmpty(key)) return OutfitKeyKind.None;
        if (key.StartsWith("@party:"))
        {
            id = key.Substring("@party:".Length);
            return OutfitKeyKind.Party;
        }
        if (key.StartsWith("#")) return Array.IndexOf(ReservedKeys, key) >= 0 ? OutfitKeyKind.Reserved : OutfitKeyKind.None;
        int split = key.LastIndexOf('|');
        if (split > 0)
        {
            factionID = key.Substring(0, split);
            id = key.Substring(split + 1);
            return OutfitKeyKind.MemberType;
        }
        split = key.LastIndexOf(':');
        if (split > 0)
        {
            factionID = key.Substring(0, split);
            id = key.Substring(split + 1);
            return OutfitKeyKind.Activity;
        }
        return OutfitKeyKind.Generic;
    }

    /// <summary> display name of a wardrobe key: "faction - type / activity" for faction keys, else the localized key </summary>
    public static string KeyDisplayName(string key)
    {
        var kind = ParseKey(key, out var factionID, out var id);
        if (kind == OutfitKeyKind.MemberType || kind == OutfitKeyKind.Activity)
            return $"{LocalizeDictionary.QueryThenParse(factionID)} - {LocalizeDictionary.QueryThenParse(id)}";
        return LocalizeDictionary.QueryThenParse(id);
    }

    /// <summary>
    /// Context tags outfit variants match against: today's holiday and season tags of the character's world - the world
    /// of the faction owning the current room (a party's room: its owner faction), else of the priority home, else the
    /// first loaded world. The only place that knows where outfit tags come from.
    /// </summary>
    public static void GetContextTags(Character_Trainable c, List<string> result)
    {
        if (result == null) return;
        var campaign = scr_System_CampaignManager.current;
        if (c == null || campaign == null) return;

        string worldID = "";
        var locale = c.FactionManager?.CurrentLocaleFaction;
        Manageable faction = locale is Manageable_Party party ? party.OwnerFaction : locale?.getLocaleFaction as Manageable;
        if (faction != null) worldID = Manageable.ResolveWorldID(faction);
        if (string.IsNullOrEmpty(worldID))
        {
            var homes = c.FactionManager?.HomeFactions;
            if (homes != null && homes.Count > 0) worldID = Manageable.ResolveWorldID(homes[0]);
        }
        if (string.IsNullOrEmpty(worldID) && campaign.currentWorldPlanIDs != null && campaign.currentWorldPlanIDs.Count > 0)
            worldID = campaign.currentWorldPlanIDs[0];

        var snap = HolidaySeasonSystem.GetTracker(worldID);
        if (snap == null) return;
        result.AddRange(snap.holidayTags);
        result.AddRange(snap.seasonTags);
    }

    /// <summary>
    /// outfit key of a membertype held in a faction / party: "factionID|keyTypeID" - MemberType.OutfitKeyType: the type
    /// defining the outfit, else the direct parent (the shifts of one post share a key), else the type itself.
    /// <br/>Read from the data each time: data adding / removing a membertype outfit (an update, a mod) moves the key, and
    /// an outfit stored under the old key no longer applies to that type (it stays stored; undoing the change restores it).
    /// </summary>
    public static string MemberTypeKey(string factionID, MemberType memberType)
    {
        var keyType = memberType?.OutfitKeyType;
        if (string.IsNullOrEmpty(factionID) || keyType == null) return "";
        return factionID + "|" + keyType.ID;
    }

    /// <summary> generic outfit key of a membertype in any faction: "keyTypeID" </summary>
    public static string GenericMemberTypeKey(MemberType memberType)
    {
        var keyType = memberType?.OutfitKeyType;
        return keyType != null ? keyType.ID : "";
    }

    /// <summary> outfit key of a recreation activity: "factionID:activityID" (same as its cooldown key) </summary>
    public static string ActivityKey(string factionID, RecreationActivity activity)
    {
        if (string.IsNullOrEmpty(factionID) || activity == null || string.IsNullOrEmpty(activity.ID)) return "";
        return factionID + ":" + activity.ID;
    }

    /// <summary>
    /// A piece needs a known base item with an equippable component and no per-instance state
    /// (Item_Instance.HasInstanceState), so baseID + nameOverwrite fully describe it.
    /// </summary>
    public static bool CanBePiece(OutfitPiece piece, out string reason)
    {
        reason = "";
        if (piece == null || string.IsNullOrEmpty(piece.baseID))
        {
            reason = "no base id";
            return false;
        }
        if (scr_System_Serializer.current.GetByNameOrID_Item_Base(piece.baseID) == null)
        {
            reason = $"base item [{piece.baseID}] not found";
            return false;
        }
        var probe = new Item_Instance(piece.baseID, piece.nameOverwrite);
        if (probe.GetComp_Equippable() == null)
        {
            reason = $"[{piece.baseID}] is not equippable";
            return false;
        }
        if (probe.HasInstanceState)
        {
            reason = $"[{piece.baseID}] keeps per-instance state";
            return false;
        }
        return true;
    }

    public static readonly BodyEquipLayer[] AllLayers = { BodyEquipLayer.Skin, BodyEquipLayer.Inner, BodyEquipLayer.Outer };

    /// <summary>
    /// An outfit of what c wears right now (body part slots, every layer): overrides every layer, worn anywhere, kept on
    /// for rest and sleep. Locked items are left out - they stay on as they are (ApplyOutfit never takes them off, so a
    /// copy would only be blocked by the original); so are organ items and items that can't be pieces. Key not set.
    /// </summary>
    public static OutfitSet SnapshotWorn(Character_Trainable c)
    {
        var outfit = new OutfitSet
        {
            overrideSkin = true,
            overrideInner = true,
            overrideOuter = true,
            limitToLocaleFaction = false,
        };
        outfit.sleepKeepLayers.AddRange(AllLayers);
        outfit.restKeepLayers.AddRange(AllLayers);
        if (c == null || c.Body == null) return outfit;

        var seen = new HashSet<int>();
        foreach (BodyPart_Instance part in c.Body.Body)
        {
            foreach (var layer in AllLayers)
            {
                foreach (BodyPartEquipSlot slot in part.availableSlots)
                {
                    if (!part.TryGetEquip(out var item, layer, slot) || !seen.Add(item.RefID)) continue;
                    var comp = item.GetComp_Equippable();
                    if (comp == null || comp.lockable) continue;
                    var piece = new OutfitPiece(item.BaseID, item.nameOverwrite);
                    if (CanBePiece(piece, out _)) outfit.pieces.Add(piece);
                }
            }
        }
        return outfit;
    }

    /// <summary> unregistered item for a piece, carrying the virtual slot id as its RefID; null if the piece is invalid </summary>
    public static Item_Instance BuildVirtualItem(OutfitPiece piece, int virtualID)
    {
        if (!CanBePiece(piece, out var reason))
        {
            Debug.LogWarning($"OutfitUtility: outfit piece skipped, {reason}");
            return null;
        }
        var item = new Item_Instance(piece.baseID, piece.nameOverwrite);
        item.RegisterItem(virtualID);
        return item;
    }

    /// <summary>
    /// True if the character stands in a room of jobGiver: the room's faction owner is the job giver, or one of them
    /// is the other's locale faction (a subfaction works in its parent's rooms).
    /// </summary>
    public static bool IsInLocaleOf(Character_Trainable c, I_IsJobGiver jobGiver)
    {
        if (c == null || jobGiver == null) return false;
        var locale = c.FactionManager.CurrentLocaleFaction;
        if (locale == null) return false;
        if (locale == jobGiver) return true;
        if (locale.FactionID == jobGiver.FactionID) return true;
        if (jobGiver.getLocaleFaction == locale) return true;
        if (locale.getLocaleFaction == jobGiver) return true;
        return false;
    }
}
