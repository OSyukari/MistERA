using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

/// <summary>
/// Per-character outfits, one per schedule key (faction membertype / recreation activity - see OutfitUtility).
/// <br/>Default clothes = every clothing item of the inventory. An outfit's pieces are virtual: unregistered
/// Item_Instance objects built from {baseID, nameOverwrite} with negative ids (-2, -3...), which the body slots
/// store like refIDs and Character_Body.ResolveEquip hands back. Nothing of an outfit ever enters the item registry
/// or an inventory, so outfits cost no item instances and can't be traded away.
/// <br/>NPCs change at changing furniture (com_furniture_changeOutfit, TryFindOutfitChangeNode) whenever
/// NeedsOutfitChange. The player is never auto-changed.
/// </summary>
public class Character_Wardrobe
{

    [JsonIgnore]
    public Hairstyle_Instance Hairstyle
    {
        get
        {
            return ActiveOutfit != null && ActiveOutfit.hairstyleOverride != null ? ActiveOutfit.hairstyleOverride : Owner.Body.Hairstyle;
        }
    }
    [JsonIgnore]
    public Traits EyeColor
    {
        get
        {
            var trait = ActiveOutfit != null ? ActiveOutfit.GetTraitWithParentID(OutfitUtility.trait_EyeColor) : null;
            if (trait != null) return trait;
            else return Owner.Stats.GetTraitByGroupID(OutfitUtility.trait_EyeColor);
        }
    }
    [JsonIgnore]
    public Traits HairColor
    {
        get
        {
            var trait = ActiveOutfit != null ? ActiveOutfit.GetTraitWithParentID(OutfitUtility.trait_HairColor) : null;
            if (trait != null) return trait;
            else return Owner.Stats.GetTraitByGroupID(OutfitUtility.trait_HairColor);
        }
    }
    [JsonIgnore]
    public Traits SkinColor
    {
        get
        {
            var trait = ActiveOutfit != null ? ActiveOutfit.GetTraitWithParentID(OutfitUtility.trait_SkinColor) : null;
            if (trait != null) return trait;
            else return Owner.Stats.GetTraitByGroupID(OutfitUtility.trait_SkinColor);
        }
    }
    [JsonIgnore]
    public Traits BodyType
    {
        get
        {
            var trait = ActiveOutfit != null ? ActiveOutfit.GetTraitWithParentID(OutfitUtility.trait_BodyType) : null;
            if (trait != null) return trait;
            else return Owner.Stats.GetTraitByGroupID(OutfitUtility.trait_BodyType);
        }
    }


    [JsonIgnore] Character_Trainable owner = null;
    [JsonIgnore] public Character_Trainable Owner { get { return owner; } }

    /// <summary> every schedule key the character has had (joined factions / booked activities), in order - for the wardrobe UI </summary>
    [JsonProperty] List<string> knownKeys = new List<string>();
    [JsonProperty] List<OutfitSet> outfits = new List<OutfitSet>();
    /// <summary> keys a data outfit (template preset / MemberType.outfit / RecreationActivity.outfit) was already offered for - never offered again, so a deleted outfit stays deleted </summary>
    [JsonProperty] List<string> seededKeys = new List<string>();

    /// <summary> key of the outfit currently put on ("" = default clothes) </summary>
    [JsonProperty] string activeKey = "";
    /// <summary> the active outfit was edited after it was put on: change again to apply the edit </summary>
    [JsonProperty] bool activeOutdated = false;
    /// <summary> virtual slot id -> piece, copied when the outfit was put on (stable even if the outfit is edited) </summary>
    [JsonProperty] Dictionary<int, OutfitPiece> activePieces = new Dictionary<int, OutfitPiece>();
    /// <summary> which variants of the active outfit were put on (OutfitSet.Resolve signature) - a different result now = change again </summary>
    [JsonProperty] string activeVariantSignature = "";
    /// <summary> override layers of the active outfit as put on (variants included); null = saves from before variants: read the outfit's own </summary>
    [JsonProperty] List<BodyEquipLayer> activeOverrideLayers = null;

    [JsonIgnore] Dictionary<int, Item_Instance> virtualItems = new Dictionary<int, Item_Instance>();

    public Character_Wardrobe() { }

    /// <summary> creation and load: link the owner, seed the template's preset outfits for keys not present yet </summary>
    public void ReEstablishParent(Character_Trainable c)
    {
        owner = c;
        if (knownKeys == null) knownKeys = new List<string>();
        if (outfits == null) outfits = new List<OutfitSet>();
        if (activePieces == null) activePieces = new Dictionary<int, OutfitPiece>();
        if (seededKeys == null) seededKeys = new List<string>();
        outfits.RemoveAll(x => x == null || string.IsNullOrEmpty(x.key));

        var seed = c?.Template?.outfits;
        if (seed == null) return;
        foreach (var o in seed)
        {
            if (o == null) continue;
            SeedOutfit(o.key, o);
        }
    }

    /// <summary>
    /// Offer a data outfit for key (template preset, MemberType.outfit, RecreationActivity.outfit): copied in once per
    /// key, unless the wardrobe already has an outfit for it (a character's own preset beats a membertype's).
    /// </summary>
    void SeedOutfit(string key, OutfitSet source)
    {
        if (string.IsNullOrEmpty(key) || source == null) return;
        RegisterKey(key);
        if (seededKeys.Contains(key)) return;
        seededKeys.Add(key);
        if (GetOutfit(key) != null) return;
        var copy = source.Clone();
        copy.key = key;
        outfits.Add(copy);
    }

    // ---------------- keys / outfits ---------------- //

    [JsonIgnore] public IReadOnlyList<string> KnownKeys { get { return knownKeys; } }
    [JsonIgnore] public IReadOnlyList<OutfitSet> Outfits { get { return outfits; } }

    public void RegisterKey(string key)
    {
        if (string.IsNullOrEmpty(key) || knownKeys.Contains(key)) return;
        knownKeys.Add(key);
    }

    /// <summary>
    /// The character holds memberType in jobGiver (Manageable.AddToFaction, a booking acting under it, and lazily while
    /// resolving the scheduled outfit): register its key and copy in the type's outfit (MemberType.outfit) once. A
    /// recreation membership (MemberType.recreation) registers every one of its activities too. Returns the key.
    /// </summary>
    public string RegisterMemberType(I_IsJobGiver jobGiver, MemberType memberType)
    {
        if (jobGiver == null || memberType == null) return "";
        var key = OutfitUtility.MemberTypeKey(jobGiver.FactionID, memberType);
        RegisterKey(key);
        var source = memberType.OutfitKeyType?.outfit;
        if (source != null) SeedOutfit(key, source);
        var activities = memberType.recreation?.activities;
        if (activities != null) foreach (var a in activities) if (a != null) RegisterActivity(jobGiver.FactionID, a);
        return key;
    }

    /// <summary>
    /// After loading (Character_Trainable.PostReloadUpdate): register the keys of every faction membership and booking the
    /// character has now - saves from before the keys were tracked only knew the outfits' keys.
    /// </summary>
    public void RegisterCurrentKeys()
    {
        var f = owner?.FactionManager;
        if (f == null) return;
        foreach (var faction in f.Factions)
        {
            if (faction != null) RegisterMemberType(faction, faction.GetMemberType(owner));
        }
        foreach (var b in f.RecreationBookings)
        {
            if (b == null || b.Faction == null) continue;
            if (b.MemberType != null) RegisterMemberType(b.Faction, b.MemberType);
            RegisterActivity(b.Faction.FactionID, RecreationUtility.ResolveActivity(f, b));
        }
    }

    /// <summary> the character books / visits activity at factionID: register its key and copy in its outfit once. Returns the key. </summary>
    public string RegisterActivity(string factionID, RecreationActivity activity)
    {
        var key = OutfitUtility.ActivityKey(factionID, activity);
        RegisterKey(key);
        if (activity?.outfit != null) SeedOutfit(key, activity.outfit);
        return key;
    }

    /// <summary>
    /// Every outfit slot to list (wardrobe UI), outfit or not: the reserved keys (always), then the known membertype /
    /// activity keys still in effect (IsCurrentKey). Keys no longer in effect (a faction left, an old post) stay stored -
    /// rejoining lists them again with their outfit; generic membertype keys and party keys are not listed.
    /// </summary>
    public List<WardrobeEntry> GetEntries()
    {
        var result = new List<WardrobeEntry>();
        foreach (var key in OutfitUtility.ReservedKeys) result.Add(new WardrobeEntry(key, GetOutfit(key)));
        foreach (var key in knownKeys)
        {
            var kind = OutfitUtility.ParseKey(key, out _, out _);
            if (kind != OutfitKeyKind.MemberType && kind != OutfitKeyKind.Activity) continue;
            if (!IsCurrentKey(key)) continue;
            result.Add(new WardrobeEntry(key, GetOutfit(key)));
        }
        return result;
    }

    /// <summary>
    /// The key belongs to what the character has now: a reserved key; a membertype key of a faction the character holds
    /// that type in (or a booking acts under); an activity key of a held recreation membership's activities, or of a
    /// booking's activity. Generic and party keys: false.
    /// </summary>
    public bool IsCurrentKey(string key)
    {
        var kind = OutfitUtility.ParseKey(key, out var factionID, out _);
        if (kind == OutfitKeyKind.Reserved) return true;
        if (kind != OutfitKeyKind.MemberType && kind != OutfitKeyKind.Activity) return false;
        var f = owner?.FactionManager;
        if (f == null) return false;

        foreach (var faction in f.Factions)
        {
            if (faction == null || faction.FactionID != factionID) continue;
            var memberType = faction.GetMemberType(owner);
            if (memberType == null) continue;
            if (kind == OutfitKeyKind.MemberType && OutfitUtility.MemberTypeKey(factionID, memberType) == key) return true;
            var activities = memberType.recreation?.activities;
            if (kind == OutfitKeyKind.Activity && activities != null && activities.Exists(a => a != null && OutfitUtility.ActivityKey(factionID, a) == key)) return true;
        }
        foreach (var b in f.RecreationBookings)
        {
            if (b == null || b.Faction == null || b.Faction.FactionID != factionID) continue;
            if (kind == OutfitKeyKind.MemberType && b.MemberType != null && OutfitUtility.MemberTypeKey(factionID, b.MemberType) == key) return true;
            if (kind == OutfitKeyKind.Activity && OutfitUtility.ActivityKey(factionID, RecreationUtility.ResolveActivity(f, b)) == key) return true;
        }
        return false;
    }

    public OutfitSet GetOutfit(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        return outfits.Find(x => x.key == key);
    }

    /// <summary> add or replace the outfit of outfit.key (a copy is stored). Editing the worn outfit applies at the next change. </summary>
    public void SetOutfit(OutfitSet outfit)
    {
        if (outfit == null || string.IsNullOrEmpty(outfit.key)) return;
        outfits.RemoveAll(x => x.key == outfit.key);
        outfits.Add(outfit.Clone());
        RegisterKey(outfit.key);
        if (outfit.key == activeKey) activeOutdated = true;
    }

    /// <summary> an outfit held elsewhere (a party's) was edited: if it is the worn one, change again to apply the edit </summary>
    public void NotifyOutfitEdited(string key)
    {
        if (!string.IsNullOrEmpty(key) && key == activeKey) activeOutdated = true;
    }

    public void RemoveOutfit(string key)
    {
        if (outfits.RemoveAll(x => x.key == key) > 0 && key == activeKey) activeOutdated = true;
    }

    // ---------------- active outfit ---------------- //

    [JsonIgnore] public string ActiveKey { get { return activeKey; } }

    /// <summary> the outfit of key is put on, as it currently is (not edited since) </summary>
    public bool IsWearing(string key) { return !string.IsNullOrEmpty(key) && key == activeKey && !activeOutdated; }

    /// <summary> the outfit currently worn (null = default clothes, or the worn outfit was removed from the wardrobe) </summary>
    [JsonIgnore] public OutfitSet ActiveOutfit { get { return ResolveOutfit(activeKey); } }

    /// <summary> outfit of any key: a party key (Manageable_Party.OutfitKey) resolves through the character's current party, others through the wardrobe </summary>
    OutfitSet ResolveOutfit(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        if (key.StartsWith("@party:"))
        {
            var party = WardrobeParty;
            return party != null && party.OutfitKey == key ? party.GetOutfit(owner) : null;
        }
        return GetOutfit(key);
    }

    /// <summary>
    /// The party whose wardrobe the character wears now: the locked party, else the current party while it is active
    /// (resting included), not the player's, or the character stands in its room (gathering failed / expedition just
    /// over - same rule as Character_Trainable.isScheduleSleep). Null = not in a party.
    /// </summary>
    [JsonIgnore] public Manageable_Party WardrobeParty
    {
        get
        {
            var f = owner?.FactionManager;
            if (f == null) return null;
            if (f.CurrentLockedParty != null) return f.CurrentLockedParty;
            var p = f.CurrentParty;
            if (p == null) return null;
            if (p.isActive || !p.isPlayerFaction) return p;
            if (p.Room != null && p.Room.RoomChara.Contains(owner)) return p;
            return null;
        }
    }

    /// <summary>
    /// The layer is put away for now: the worn party outfit's party is resting and the outfit doesn't keep the layer
    /// (restKeepLayers). Redress leaves such layers off until the rest is over.
    /// </summary>
    public bool IsLayerStowed(BodyEquipLayer layer)
    {
        var party = WardrobeParty;
        if (party == null || party.Job == null || !party.Job.isResting || activeKey != party.OutfitKey) return false;
        var outfit = party.GetOutfit(owner);
        if (outfit == null || outfit.restKeepLayers == null || outfit.restKeepLayers.Count == 0) return false;
        return !outfit.restKeepLayers.Contains(layer);
    }

    /// <summary> the character's party started resting (Job_Expedition): take off the layers the party outfit doesn't keep for rest </summary>
    public void OnPartyRestStart()
    {
        if (owner == null || IsPlayer || scr_System_CentralControl.current.isSafeMode) return;
        foreach (var layer in OutfitUtility.AllLayers)
        {
            if (IsLayerStowed(layer)) owner.Undress(layer, Revealing.Erotic, true, false);
        }
    }

    /// <summary> the active outfit replaces the default clothes of this layer (Redress leaves inventory clothes of it off) </summary>
    public bool OverridesLayer(BodyEquipLayer layer)
    {
        var outfit = ActiveOutfit;
        if (outfit == null) return false;
        if (activeOverrideLayers != null) return activeOverrideLayers.Contains(layer);
        return outfit.Overrides(layer);
    }

    // ---------------- variants (season / holiday) ---------------- //

    List<string> ContextTags()
    {
        var tags = new List<string>();
        OutfitUtility.GetContextTags(owner, tags);
        return tags;
    }

    /// <summary> the key an outfit counts as when worn now: a #default resolving to nothing is the plain default clothes ("") </summary>
    string EffectiveKey(OutfitSet outfit, List<string> tags)
    {
        if (outfit == null) return "";
        if (outfit.key == OutfitUtility.Key_Default && outfit.Resolve(tags, out _).IsEmpty) return "";
        return outfit.key;
    }

    /// <summary> the outfit would resolve to other variants now than when it was put on (the season / holiday changed) </summary>
    bool VariantsChanged(OutfitSet outfit)
    {
        if (outfit == null) return false;
        outfit.Resolve(ContextTags(), out var signature);
        return signature != (activeVariantSignature ?? "");
    }

    public bool IsActivePiece(int id) { return id <= -2 && activePieces.ContainsKey(id); }

    /// <summary> the virtual item of an active piece id (built lazily, also after loading); null if not an active piece </summary>
    public Item_Instance GetVirtualItem(int id)
    {
        if (virtualItems.TryGetValue(id, out var item)) return item;
        if (!activePieces.TryGetValue(id, out var piece)) return null;
        item = OutfitUtility.BuildVirtualItem(piece, id);
        if (item != null) virtualItems[id] = item;
        return item;
    }

    /// <summary> virtual items of every active piece (worn or taken off) </summary>
    [JsonIgnore] public List<Item_Instance> ActivePieceItems
    {
        get
        {
            var list = new List<Item_Instance>(activePieces.Count);
            foreach (var id in activePieces.Keys)
            {
                var item = GetVirtualItem(id);
                if (item != null) list.Add(item);
            }
            return list;
        }
    }

    // ---------------- schedule ---------------- //

    /// <summary>
    /// The outfit the current schedule asks for, with the job giver it belongs to, by priority:
    /// <br/>1. the active recreation activity's outfit;
    /// <br/>2. the active membertype's (faction-specific key, then the generic key);
    /// <br/>3. at home (the active job giver is the priority home and the character is in its rooms): the own home outfit
    /// (#home, or #home_temp when the priority home is the temporary home - no fallback between them);
    /// <br/>else null = default clothes. Keys met here are registered and get their data outfit copied in (old saves).
    /// </summary>
    public OutfitSet GetScheduledOutfit(out I_IsJobGiver jobGiver)
    {
        jobGiver = null;
        if (owner == null || owner.FactionManager == null) return null;
        var f = owner.FactionManager;
        // in a party only the party's wardrobe counts: its outfit for the character, else the default clothes
        var party = WardrobeParty;
        if (party != null)
        {
            var partyOutfit = party.GetOutfit(owner);
            if (partyOutfit != null) jobGiver = party;
            return partyOutfit;
        }

        var giver = f.CurrentActiveJobGiver;
        if (giver == null) return null;

        var activity = f.CurrentActiveActivity;
        if (activity != null)
        {
            var o = GetOutfit(RegisterActivity(giver.FactionID, activity));
            if (o != null)
            {
                jobGiver = giver;
                return o;
            }
        }

        var memberType = f.CurrentActiveMemberType;
        if (memberType != null)
        {
            var outfit = GetOutfit(RegisterMemberType(giver, memberType));
            if (outfit == null) outfit = GetOutfit(OutfitUtility.GenericMemberTypeKey(memberType));
            if (outfit != null)
            {
                jobGiver = giver;
                return outfit;
            }
        }

        var home = PriorityHome(out bool isTemp);
        if (home != null && giver == (I_IsJobGiver)home && OutfitUtility.IsInLocaleOf(owner, home))
        {
            var homeOutfit = GetOutfit(isTemp ? OutfitUtility.Key_HomeTemp : OutfitUtility.Key_Home);
            if (homeOutfit != null)
            {
                jobGiver = home;
                return homeOutfit;
            }
        }
        return null;
    }

    /// <summary> the priority home (HomeFactions[0]); isTemp = it is the temporary home </summary>
    Manageable PriorityHome(out bool isTemp)
    {
        isTemp = false;
        var f = owner?.FactionManager;
        if (f == null || f.HomeFactions.Count < 1) return null;
        var home = f.HomeFactions[0];
        isTemp = home != null && home == f.Faction_Home_Temporary;
        return home;
    }

    /// <summary>
    /// The outfit to wear right here, right now: the scheduled outfit (none scheduled = #default, the default clothes),
    /// unless it is limitToLocaleFaction and the character is outside its job giver's rooms - then the default clothes
    /// (change back before leaving, change into it on arrival). keepCurrent = the scheduled outfit is already worn
    /// (stepping out during the shift changes nothing). Null = the plain default clothes.
    /// </summary>
    OutfitSet GetWantedOutfit(out bool keepCurrent) { return GetWantedOutfit(out keepCurrent, out _); }

    /// <param name="canPutOnHere"> the wanted outfit may be put on where the character stands (limitToLocaleFaction) </param>
    OutfitSet GetWantedOutfit(out bool keepCurrent, out bool canPutOnHere)
    {
        keepCurrent = false;
        var scheduled = GetScheduledOutfit(out var giver);
        if (scheduled == null)
        {
            scheduled = GetOutfit(OutfitUtility.Key_Default);
            giver = null;   // the default clothes belong nowhere: worn anywhere
        }
        canPutOnHere = scheduled == null || !scheduled.limitToLocaleFaction || giver == null || OutfitUtility.IsInLocaleOf(owner, giver);
        if (scheduled != null && scheduled.key == activeKey && !activeOutdated)
        {
            keepCurrent = true;
            return scheduled;
        }
        if (!canPutOnHere) return GetOutfit(OutfitUtility.Key_Default);
        return scheduled;
    }

    [JsonIgnore] bool IsPlayer { get { return owner != null && scr_System_CampaignManager.current != null && scr_System_CampaignManager.current.Player == owner; } }

    /// <summary> the schedule asks for different clothes than worn (NPCs only - the player is never auto-changed) </summary>
    [JsonIgnore] public bool NeedsOutfitChange
    {
        get
        {
            if (owner == null || IsPlayer) return false;
            // sleeping / about to sleep: the sleep clothes are put on at the bed (PrepareForSleep), changed out of after waking
            if (owner.isSleeping || owner.shouldSleep) return false;
            // a player party changes clothes only once under way (Job_Expedition lets members go change at the camp):
            // before departure - queued, gathering, gathering failed but still at the camp - keep what is worn
            var party = WardrobeParty;
            if (party != null && party.isPlayerFaction && party.Job != null && party.Job.status < Job_Expedition.ExpeditionStatus.active) return false;
            var wanted = GetWantedOutfit(out bool keepCurrent, out bool canPutOnHere);
            // the right outfit, but the worn clothes drifted from it (e.g. changed by hand), or the season / holiday
            // calls for other variants: put it on again - where it may be put on
            if (keepCurrent) return canPutOnHere && (IsActiveOutfitDisturbed() || VariantsChanged(wanted));
            if (EffectiveKey(wanted, ContextTags()) != activeKey) return true;
            return activeOutdated;
        }
    }

    /// <summary> com_furniture_changeOutfit result: put on what the schedule wants now (no-op if already worn) </summary>
    public void ApplyWanted()
    {
        if (owner == null) return;
        var wanted = GetWantedOutfit(out bool keepCurrent, out bool canPutOnHere);
        if (keepCurrent && (!canPutOnHere || (!IsActiveOutfitDisturbed() && !VariantsChanged(wanted))))
        {
            owner.Redress();
            return;
        }
        ApplyOutfit(wanted);
    }

    /// <summary>
    /// The active outfit is worn, but not as it should be: one of its pieces is kept off by another item in its slot
    /// (Redress only fills empty slots, so it can't fix that), or own clothes are worn again on a layer the outfit
    /// overrides. Only what ApplyOutfit itself removes counts (locked items are left alone there, so they are ignored
    /// here too) - otherwise the change would repeat forever. A piece the body has no slot for doesn't count either.
    /// </summary>
    bool IsActiveOutfitDisturbed()
    {
        var outfit = ActiveOutfit;
        if (outfit == null || owner == null || owner.Body == null) return false;
        var body = owner.Body;

        foreach (var id in activePieces.Keys)
        {
            if (body.IsEquipped(id)) continue;
            var piece = GetVirtualItem(id);
            if (piece != null && body.IsBlockedByOtherItem(piece)) return true;
        }

        foreach (var layer in new[] { BodyEquipLayer.Skin, BodyEquipLayer.Inner, BodyEquipLayer.Outer })
        {
            if (!OverridesLayer(layer)) continue;
            // same slots ApplyOutfit's Character_Trainable.Undress(layer) goes through
            foreach (BodyPart_Instance part in body.Body)
            {
                foreach (BodyPartEquipSlot slot in part.availableSlots)
                {
                    if (!part.TryGetEquip(out var worn, layer, slot) || worn.RefID <= -2) continue;
                    var comp = worn.GetComp_Equippable();
                    if (comp != null && !comp.lockable) return true;
                }
            }
        }
        return false;
    }

    /// <summary>
    /// Going to sleep (Character_Trainable.Sleep, at the bed, before the consciousness snapshot so waking up doesn't
    /// read it as clothes removed while asleep). NPCs only.
    /// <br/>- The worn outfit keeps layers for sleep (sleepKeepLayers): take off only the other layers.
    /// <br/>- Else, outside a party: change into the own sleep outfit (#sleep, or #sleep_temp at the temporary home - no
    /// fallback between them).
    /// <br/>- Else strip the inner and outer layers.
    /// <br/>Taken-off clothes come back through Redress after waking. Safe mode never strips.
    /// </summary>
    public void PrepareForSleep()
    {
        if (owner == null || owner.Body == null || IsPlayer) return;
        var active = ActiveOutfit;
        if (active != null && active.sleepKeepLayers != null && active.sleepKeepLayers.Count > 0)
        {
            if (scr_System_CentralControl.current.isSafeMode) return;
            foreach (var layer in new[] { BodyEquipLayer.Outer, BodyEquipLayer.Inner, BodyEquipLayer.Skin })
            {
                if (!active.sleepKeepLayers.Contains(layer)) owner.Undress(layer, Revealing.Erotic, true, false);
            }
            return;
        }

        // in a party only the party's wardrobe counts: no own sleep clothes there
        if (WardrobeParty == null)
        {
            PriorityHome(out bool isTemp);
            var sleepOutfit = GetOutfit(isTemp ? OutfitUtility.Key_SleepTemp : OutfitUtility.Key_Sleep);
            if (sleepOutfit != null)
            {
                if (sleepOutfit.key != activeKey || activeOutdated || VariantsChanged(sleepOutfit)) ApplyOutfit(sleepOutfit);
                return;
            }
        }
        if (scr_System_CentralControl.current.isSafeMode) return;
        owner.Undress(BodyEquipLayer.Outer, Revealing.Erotic, true, false);
        owner.Undress(BodyEquipLayer.Inner, Revealing.Erotic, true, false);
    }

    /// <summary>
    /// Change into target (null = default clothes): take the current outfit's pieces off, resolve target for today's
    /// context tags (variants - OutfitSet.Resolve), take off the worn items of every layer it overrides (armor too,
    /// locked items stay), wear its pieces, then Redress fills the remaining empty slots from the inventory - for null
    /// that is the whole default outfit. A #default resolving to nothing counts as null.
    /// </summary>
    public void ApplyOutfit(OutfitSet target)
    {
        if (owner == null || owner.Body == null) return;
        var body = owner.Body;

        foreach (var id in activePieces.Keys.ToList())
        {
            if (body.IsEquipped(id)) owner.UnequipItem(id, -1, true, true);
        }
        activePieces.Clear();
        virtualItems.Clear();
        activeKey = "";
        activeOutdated = false;
        activeVariantSignature = "";
        activeOverrideLayers = null;
        // the old pieces are gone; the default clothes taken off below stay noted, so a later revert puts exactly them back
        owner.PruneTakenOff();

        OutfitSet worn = null;
        string signature = "";
        if (target != null)
        {
            worn = target.Resolve(ContextTags(), out signature);
            if (target.key == OutfitUtility.Key_Default && worn.IsEmpty) worn = null;
        }

        if (worn != null)
        {
            activeOverrideLayers = new List<BodyEquipLayer>();
            foreach (var layer in new[] { BodyEquipLayer.Outer, BodyEquipLayer.Inner, BodyEquipLayer.Skin })
            {
                if (!worn.Overrides(layer)) continue;
                activeOverrideLayers.Add(layer);
                owner.Undress(layer, Revealing.Erotic, true, false);
            }

            activeKey = target.key;
            activeVariantSignature = signature;
            if (worn.pieces != null)
            {
                for (int i = 0; i < worn.pieces.Count; i++)
                {
                    var piece = worn.pieces[i];
                    if (piece == null) continue;
                    int id = -2 - i;
                    activePieces[id] = piece.Clone();
                    if (GetVirtualItem(id) == null)
                    {
                        activePieces.Remove(id);
                        continue;
                    }
                    owner.EquipItem(id, true);
                }
            }

            // a piece that didn't stay on - pushed off by a later piece of the same outfit (two pieces for one slot, a
            // data mistake) or no slot for it on this body - is dropped: kept, it would count as kept off by another
            // item (IsActiveOutfitDisturbed) and the outfit would be put on again and again
            foreach (var id in activePieces.Keys.ToList())
            {
                if (body.IsEquipped(id)) continue;
                Debug.LogWarning($"Character_Wardrobe {owner.FirstName}: outfit [{activeKey}] piece [{activePieces[id].baseID}] could not stay on, dropped (another piece of the outfit uses its slot?)");
                activePieces.Remove(id);
                virtualItems.Remove(id);
            }
        }

        owner.Redress();
        owner.PortraitManager.ClearHandlerCache();
    }
}
