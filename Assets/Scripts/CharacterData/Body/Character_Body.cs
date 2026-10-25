using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;


public class Character_Body
{
    public int Height = 162;
    public int Weight = 70;

    List<string> _cached_bodyDescriptions = new List<string>();
    List<string> _cached_internalTags = new List<string>();
    bool _cached_internals_description = false;
    public void NotifyInternalsContentChange()
    {
        _cached_internals_description = false;
    }
    public void NotifyStimulated()
    {
        Stimulated = true;
    }

    [JsonIgnore]
    public List<string> BodyDescription
    {
        get
        {
            if (!_cached_internals_description && GetBodyDescription()) return _cached_bodyDescriptions;
            else return _cached_bodyDescriptions;
        }
    }
    [JsonIgnore]
    public List<string> BodyRevealingDesriptors { get
        {
            if (!_cached_internals_description && GetBodyDescription()) return _cached_internalTags;
            else return _cached_internalTags;
        } }
    protected bool GetBodyDescription()
    {
        _cached_internals_description = true;
        _cached_bodyDescriptions.Clear();// = "";
        _cached_internalTags.Clear();
        var tempList = new SortedDictionary<int,string>();
        var tempList2 = new Dictionary<string, List<string>>();
        foreach(var part in this.Body)
        {
            if (part.internals.Count < 1) continue;
            foreach(var i in part.internals)
            {
                if (i.Base.exposedKojoID == "") continue;
                if (i.GetRevealingScore(BodyEquipLayer.None) > 0) continue;
                tempList.Add(i.sortOrder, i.Base.exposedKojoID);
                if (!tempList2.ContainsKey(i.Base.exposedKojoID)) tempList2.Add(i.Base.exposedKojoID, new List<string>());
                if (i.isExtremelyExpanded) tempList2[i.Base.exposedKojoID].Add("Expansion_Extreme");
                else if (i.isVisiblyExpanded) tempList2[i.Base.exposedKojoID].Add("Expansion");
            }
        }
        var string_tempList = tempList.Values.Distinct().ToList();
        var relation = Owner.Relationships.FindRelationshipWith(Owner);
        foreach(var kojoID in string_tempList)
        {
            var result = Owner.Relationships.Personality.GetKOJOMessage(kojoID, relation, tempList2[kojoID].Distinct().ToList(), null);
            if (result == null) continue;
            var dump = new List<string>();
            result.DumpMessage(dump);
            if (dump.Count < 1) continue;
            _cached_bodyDescriptions.AddRange(dump);
            _cached_internalTags.Add(LocalizeDictionary.QueryThenParse(kojoID));
        }

        _cached_internalTags = _cached_internalTags.Distinct().ToList();
        return true;
    }


    List<CombatAction> _alwaysValidActions = null;
    [JsonIgnore]
    public List<CombatAction> AlwaysValidActions
    { get
        {
            if (_alwaysValidActions == null)
            {
                _alwaysValidActions = new List<CombatAction>();
                foreach (var entry in scr_System_Serializer.current.MasterList.CombatActions.AllActions)
                {
                    if (entry.itemRequirement != null && entry.itemRequirement.isActive) continue;
                    if (_alwaysValidActions.Contains(entry)) continue;
                    _alwaysValidActions.Add(entry);
                }
                //Debug.Log($"Initializing alwaysvalidactions, dict count {scr_System_Serializer.current.MasterList.CombatActions.AllActions.Count} self count {_alwaysValidActions.Count}");
            }
            return _alwaysValidActions;
        } }

    [JsonIgnore]
    public Dictionary<I_CombatItem, List<CombatAction>> CombatActions
    { get
        {
            var list = new Dictionary<I_CombatItem, List<CombatAction>>();
            foreach(var i in this.Body)
            {
                foreach(var kvp in i.CombatActions)
                {
                    if (list.ContainsKey(kvp.Key)) continue;
                    list.Add(kvp.Key, kvp.Value);
                }
            }
            return list;
        } }

    public void UpdateTimeMinute(TimeSpan t)
    {
       // _cached_internals_description = false;
        foreach (BodyInternal_Instance i in Internals) i.UpdateTimeMinute(t);
    }
    public void UpdateTimeHour(TimeSpan t)
    {
        /*
        foreach(var inte in this.Internals)
        {
            if (inte.canContain && inte.Contains.Count > 0 && inte.canOverflowOut && inte.overflowOutTag != "ext")
            {
                var target = GetRandomInternalWithTag(inte.overflowOutTag);

                if (target != null)
                {
                    inte.TransferContentTo(target);
                }
            }
        }*/
        foreach (BodyInternal_Instance i in Internals) i.UpdateTimeHour();
    }
    public void UpdateTimeDay(TimeSpan t)
    {

    }
    //-------------------------
    protected int ownerRefID = -1;
    private Character_Trainable owner = null;
    [JsonIgnore] public Character_Trainable Owner
    {
        get
        {
            if (owner == null && ownerRefID > -1)
            {
                owner = scr_System_CampaignManager.current.FindInstanceByID(ownerRefID);
            }
            return owner;
        }
    }
    //----------------------
    [JsonProperty] private List<BodyPart_Instance> body = null;
    [JsonIgnore] public List<BodyPart_Instance> Body {
        get {
            if (body == null || body.Count < 1) AddMissing();
            return body;
        } }
    //--------------------------
    [JsonIgnore] private List<BodyInternal_Instance> internals = null;
    [JsonIgnore] public List<BodyInternal_Instance> Internals
    {
        get
        {
            if (internals == null || internals.Count < 1)
            {
                internals = new List<BodyInternal_Instance>();
                foreach (BodyPart_Instance b in Body) internals.AddRange(b.internals);
            }
            return internals;
        }
    }
    //----------------------------




    public Character_Body()
    {

    }
    public Character_Body(Character_Trainable c)
    {
        this.ReEstablishParent(c);
    }

    public void ReEstablishParent(Character_Trainable c)
    {
        this.owner = c;
        this.ownerRefID = c.RefID;

        if (this.body != null) foreach (var i in this.body) i.ReEstablishParent(c);

        // runs on creation and on load, before stats refresh their traits
        FillMissingBodyTraits(c, c.Appearance);
        EnsureHairstyle(c);
    }

    //---hairstyle----
    // per-character copy of the template's default hairstyle. the template is only seed data.
    [JsonProperty] private Hairstyle_Instance hairstyle = null;

    /// <summary> the character's own hairstyle (in-game changes go here) </summary>
    [JsonIgnore] public Hairstyle_Instance Hairstyle { get { return hairstyle; } }

    /// <summary> hairstyle as currently seen: the active outfit's hairstyle override, else the own hairstyle </summary>
    [JsonIgnore] public Hairstyle_Instance EffectiveHairstyle
    {
        get
        {
            var outfit = Owner?.Wardrobe?.ActiveOutfit;
            if (outfit != null && outfit.hairstyleOverride != null && outfit.hairstyleOverride.GetBase() != null) return outfit.hairstyleOverride;
            return hairstyle;
        }
    }

    /// <summary>
    /// Seed the hairstyle on creation / old saves: the template's default, else a random default hairstyle.
    /// An existing hairstyle is only repaired against the current definitions, never replaced.
    /// </summary>
    protected void EnsureHairstyle(Character_Trainable c)
    {
        if (hairstyle != null)
        {
            hairstyle.Revalidate();
            return;
        }
        var seed = c?.Template?.defaultHairstyle;
        if (seed != null && seed.GetBase() != null)
        {
            hairstyle = seed.Clone();
            hairstyle.Revalidate();
        }
        else
        {
            if (seed != null && !string.IsNullOrEmpty(seed.HairstyleID)) Debug.LogWarning($"{c?.FirstName}: template default hairstyle [{seed.HairstyleID}] not found, using a random default");
            hairstyle = HairstyleUtility.GenerateDefault();
        }
    }

    /// <summary> replace the own hairstyle (a copy is stored) </summary>
    public void SetHairstyle(Hairstyle_Instance h)
    {
        if (h == null || h.GetBase() == null) return;
        hairstyle = h.Clone();
    }

    /// <summary>
    /// Body trait as currently seen: the active outfit's trait override of that group (display only, traits_BODY groups),
    /// else the body's own trait. Stats and organs always use GetBodyTrait.
    /// </summary>
    public Traits GetEffectiveBodyTrait(string groupID)
    {
        var outfit = Owner?.Wardrobe?.ActiveOutfit;
        if (outfit != null && outfit.bodyTraitOverrides != null)
        {
            foreach (var id in outfit.bodyTraitOverrides)
            {
                var t = BodyTraitUtility.Resolve(id);
                if (t == null || t.Parent == null || t.Parent.ID != groupID) continue;
                if (t.Parent.Type != Trait_Type.Body) continue;
                if (Owner != null && !t.IsValidFor(Owner)) continue;
                return t;
            }
        }
        return GetBodyTrait(groupID);
    }

    //---body traits (traits_BODY / traits_BODY_SEX)----
    // per-character copy of the template's body traits, stored by trait id. the template is only seed data.
    [JsonProperty] private List<string> bodyTraits = new List<string>();
    [JsonIgnore] private List<Traits> _cached_bodyTraits = null;
    [JsonIgnore] private Dictionary<string, Traits> _cached_bodyTraitsByGroup = null;

    void ClearBodyTraitCache()
    {
        _cached_bodyTraits = null;
        _cached_bodyTraitsByGroup = null;
    }

    /// <summary>
    /// Resolved body traits. Ids that can't be resolved (e.g. sexual traits in safe mode) are skipped but kept in storage.
    /// </summary>
    [JsonIgnore] public List<Traits> BodyTraits
    {
        get
        {
            if (_cached_bodyTraits == null)
            {
                _cached_bodyTraits = new List<Traits>();
                _cached_bodyTraitsByGroup = new Dictionary<string, Traits>();
                foreach (var id in bodyTraits)
                {
                    var t = BodyTraitUtility.Resolve(id);
                    if (t == null || t.Parent == null) continue;
                    _cached_bodyTraits.Add(t);
                    _cached_bodyTraitsByGroup.TryAdd(t.Parent.ID, t);
                }
            }
            return _cached_bodyTraits;
        }
    }

    public Traits GetBodyTrait(string groupID)
    {
        if (_cached_bodyTraitsByGroup == null) _ = BodyTraits;
        return _cached_bodyTraitsByGroup.TryGetValue(groupID, out var t) ? t : null;
    }
    public bool HasBodyTrait(string traitID) { return bodyTraits.Contains(traitID); }

    [JsonIgnore] public Traits Size_B { get { return GetBodyTrait(BodyTraitUtility.Group_Size_B); } }
    [JsonIgnore] public Traits Size_P { get { return GetBodyTrait(BodyTraitUtility.Group_Size_P); } }
    [JsonIgnore] public Traits Size_V { get { return GetBodyTrait(BodyTraitUtility.Group_Size_V); } }
    [JsonIgnore] public Traits Size_A { get { return GetBodyTrait(BodyTraitUtility.Group_Size_A); } }
    [JsonIgnore] public Traits Sensitivity_B { get { return GetBodyTrait(BodyTraitUtility.Group_Sensitivity_B); } }
    [JsonIgnore] public Traits Sensitivity_M { get { return GetBodyTrait(BodyTraitUtility.Group_Sensitivity_M); } }
    [JsonIgnore] public Traits Sensitivity_C { get { return GetBodyTrait(BodyTraitUtility.Group_Sensitivity_C); } }
    [JsonIgnore] public Traits Sensitivity_V { get { return GetBodyTrait(BodyTraitUtility.Group_Sensitivity_V); } }
    [JsonIgnore] public Traits Sensitivity_A { get { return GetBodyTrait(BodyTraitUtility.Group_Sensitivity_A); } }

    /// <summary>
    /// organ gating: female organs (vagina/womb/urethra/breast). safe mode has no part traits, keep gender appearance
    /// </summary>
    [JsonIgnore] public bool HasFemaleParts
    {
        get
        {
            if (scr_System_CentralControl.current.isSafeMode) return Owner != null && Owner.Template != null && Owner.Template.isFemale;
            var t = Size_V;
            return t != null && t.ID != "trait_Size_V_none";
        }
    }
    /// <summary>
    /// organ gating: male organs (penis). safe mode has no part traits, keep gender appearance
    /// </summary>
    [JsonIgnore] public bool HasMaleParts
    {
        get
        {
            if (scr_System_CentralControl.current.isSafeMode) return Owner != null && Owner.Template != null && Owner.Template.isMale;
            var t = Size_P;
            return t != null && t.ID != "trait_Size_P_none";
        }
    }

    /// <summary>
    /// Drop stored traits that fail their requirements (race keywords / appearance), then fill every body trait
    /// group the body has no entry for: valid template value first, then the valid group default.
    /// Valid existing entries (including in-game changes) are never overwritten. A fresh body also takes the
    /// template's singular body traits. Groups invalid for the character are not filled.
    /// </summary>
    protected void FillMissingBodyTraits(Character_Trainable c, Humanoid_GenderAppearance app)
    {
        if (c == null) return;
        var raceKeywords = new List<string>();
        Trait_Requirement.GetRaceKeywords(c, raceKeywords);

        var groups = new List<scr_Traits_Group>();
        BodyTraitUtility.GetAllBodyGroups(groups);

        // unresolvable ids of groups that aren't loaded (sexual traits in safe mode) are kept;
        // unresolvable ids of a loaded group were removed from the data, drop them so the group gets refilled
        int removed = bodyTraits.RemoveAll(id =>
        {
            var t = BodyTraitUtility.Resolve(id);
            if (t == null)
            {
                if (!groups.Exists(g => id.StartsWith(g.ID + "_"))) return false;
                Debug.Log($"Removing body trait [{id}] from {c.FirstName}: trait no longer exists");
                return true;
            }
            if (t.IsValidFor(raceKeywords, app)) return false;
            Debug.Log($"Removing body trait [{id}] from {c.FirstName}: requirements not met (appearance {app}, race keywords [{string.Join(",", raceKeywords)}])");
            return true;
        });
        bool changed = removed > 0;

        if (groups.Count > 0)
        {
            var seed = new List<Traits>();
            if (c.Template != null) c.Template.GetSeedBodyTraits(seed);
            seed.RemoveAll(t => !t.IsValidFor(raceKeywords, app));

            bool fresh = bodyTraits.Count < 1;
            foreach (var g in groups)
            {
                if (!g.IsValidFor(raceKeywords, app)) continue;
                if (!BodyTraitUtility.IsExclusiveGroup(g))
                {
                    if (!fresh) continue;
                    foreach (var t in seed)
                    {
                        if (t.Parent != g || bodyTraits.Contains(t.ID)) continue;
                        bodyTraits.Add(t.ID);
                        changed = true;
                    }
                    continue;
                }
                if (bodyTraits.Exists(x => BodyTraitUtility.IsInGroup(x, g.ID))) continue;

                var trait = seed.Find(x => x.Parent == g);
                if (trait == null) trait = BodyTraitUtility.GetDefaultValid(g, app, raceKeywords);
                if (trait == null) continue;
                bodyTraits.Add(trait.ID);
                changed = true;
            }
        }
        if (changed) ClearBodyTraitCache();
    }

    /// <summary>
    /// Re-check stored traits after the character's race / race template / appearance changed:
    /// invalid ones are removed and refilled, then stats are refreshed.
    /// </summary>
    public void RevalidateBodyTraits(bool refreshStats = true)
    {
        if (Owner == null) return;
        FillMissingBodyTraits(Owner, Owner.Appearance);
        _cached_internals_description = false;
        if (refreshStats) RefreshOwnerTraitStats();
    }

    /// <summary>
    /// Discard the body's traits and copy them again from the template (character editor / debug).
    /// Caller refreshes stats afterwards.
    /// </summary>
    public void ResetBodyTraitsFromTemplate()
    {
        if (Owner == null) return;
        bodyTraits.Clear();
        ClearBodyTraitCache();
        FillMissingBodyTraits(Owner, Owner.Template != null ? Owner.Template.Appearance : Owner.Appearance);
        _cached_internals_description = false;
    }

    /// <summary>
    /// The in-game mutation API for body traits (hair dye, breast growth, sensitivity training...).
    /// Replaces the same-group entry and refreshes the owner's stats. Returns false if the trait was refused
    /// (not a body trait, or requirements not met by the owner).
    /// </summary>
    public bool SetBodyTrait(Traits t, bool refreshStats = true)
    {
        if (t == null || t.Parent == null) return false;
        if (!t.Parent.isBodyTrait)
        {
            Debug.LogError($"SetBodyTrait [{t.ID}] on {Owner?.FirstName}: group [{t.Parent.ID}] is not a body trait group");
            return false;
        }
        if (Owner != null && !t.IsValidFor(Owner))
        {
            Debug.LogWarning($"SetBodyTrait [{t.ID}] on {Owner.FirstName}: requirements not met");
            return false;
        }
        var old = GetBodyTrait(t.Parent.ID);
        if (old == t) return true;
        // organs are only built in AddMissing, so a part appearing/disappearing doesn't add/remove organs yet
        if (t.Parent.ID.StartsWith("trait_Size_") && old != null && (old.ID.EndsWith("_none") != t.ID.EndsWith("_none")))
            Debug.LogWarning($"SetBodyTrait {Owner?.FirstName}: [{old.ID}] -> [{t.ID}] changes part presence, organs are not rebuilt");

        BodyTraitUtility.SetInList(bodyTraits, t);
        ClearBodyTraitCache();
        _cached_internals_description = false;
        if (refreshStats) RefreshOwnerTraitStats();
        return true;
    }

    /// <summary>
    /// Remove a body trait. Only meant for singular groups; sorted/unsorted groups always hold one entry.
    /// </summary>
    public void RemoveBodyTrait(Traits t, bool refreshStats = true)
    {
        if (t == null) return;
        if (!bodyTraits.Remove(t.ID)) return;
        ClearBodyTraitCache();
        _cached_internals_description = false;
        if (refreshStats) RefreshOwnerTraitStats();
    }

    void RefreshOwnerTraitStats()
    {
        if (Owner == null || Owner.Stats == null) return;
        Owner.Stats.RefreshTraits();
        Owner.Stats.RefreshAllStats(true);
    }

    public void ClearLastInteractedRefs()
    {
        foreach (var organ in Internals) organ.ClearLastInteractedRefs();
        if (!Owner.isTimeStoppedLoose)
        {
            Climax = false;
            Cum = false;
            Stimulated = false;
        }
    }

    public void AddMissing()
    {
        // Debug.Log("Body_AddMissing : baseID [" + Owner.Race.bodyPartRoot + "] refID [" + Owner.RefID + "]");
        if (Owner == null)
        {
            Debug.Log("CHARACTER BODY ADDMISSING OWNER NULL");
            return;
        }
        if (body == null || body.Count < 1 || body.Find(x => x.Base.ID == Owner.Race.bodyPartRoot) == null)
        {

            body = new List<BodyPart_Instance>();
            var bRoot = new BodyPart_Instance();
            bRoot.Initialize(Owner.Race.bodyPartRoot, Owner);

            body.Add(bRoot);
        }
        //Debug.Log("Body_AddMissing : rootNode DisplayName [" + Body.Find(x => x.baseID == Owner.Race.bodyPartRoot).DisplayName + "]");


        List<string> current = new List<string>();
        foreach (BodyPart_Instance i in Body) current.Add(i.Base.ID);

        List<string> newbody = scr_System_Serializer.current.GetByNameOrID_BodyPart_Base(Owner.Race.bodyPartRoot).GetAllChildsID();
        foreach (string s in current) newbody.Remove(s);

        foreach (string s in newbody)
        {
            //if (body.Find(x => x.Base.ID == s) != null) continue;
           // Debug.LogError($"{Owner.FirstName} body addmissing on {s}");
            BodyPart_Instance b = new BodyPart_Instance();
            b.Initialize(s, Owner);
            /*
            if (this.Size_V.ID == "trait_Size_V_none" && b.hasTag("vagina")) { continue; }
            else if (this.Size_A.ID == "trait_Size_A_none" && b.hasTag("anus")) { continue; }
            else if (this.Size_P.ID == "trait_Size_P_none" && b.hasTag("penis")) { continue; }
            else if (this.Size_B.ID == "trait_Size_B_none" && b.hasTag("breast")) { continue; }
            else Body.Add(b);
            */
            body.Add(b);
        }

        body.Sort((x, y) => x.sortOrder.CompareTo(y.sortOrder));


        string sb = "Character " + Owner.FirstName + " Body_AddMissing\n";
        foreach (BodyPart_Instance i in Body)
        {
            sb += i.DisplayName + " || " + i.Tooltip + " [";
            foreach (BodyInternal_Instance j in i.internals)
            {
                sb += " " + j.DisplayName + " ";
            }
            sb += "]\n";
        }
        //Debug.Log(sb);

        foreach (var i in body) i.ReEstablishParent(Owner);
        ClearBodyCaches();
    }

    /// <summary>
    /// The item behind a body slot id: -1 = empty, >= 0 = a registered item (owned by the inventory),
    /// &lt;= -2 = a virtual outfit piece of the owner's wardrobe (unregistered, see Character_Wardrobe).
    /// Every reader of slot ids goes through this, never the global item registry directly.
    /// </summary>
    public Item_Instance ResolveEquip(int id)
    {
        if (id == -1) return null;
        if (id >= 0) return scr_System_CampaignManager.current.FindItemInstanceByID(id);
        return Owner?.Wardrobe?.GetVirtualItem(id);
    }

    public bool IsEquipped(int id)
    {
        if (id == -1) return false;
        foreach (BodyPart_Instance part in Body)
        {
            if (part.EquippedRefIDs.Contains(id) || part.GetInternalByEquipRef(id) != null) return true;
        }
        return false;
    }

    /// <summary>
    /// True if EquipItem would put the item on without displacing anything (mirrors EquipItem's slot choice).
    /// Redress only fills such slots, so duplicate clothes never fight over one slot.
    /// </summary>
    public bool CanEquipWithoutReplacing(Item_Instance item)
    {
        if (item == null || item.GetComp_Equippable() == null) return false;
        foreach (BodyPart_Instance part in Body)
        {
            if (part.PreviewEquip(item, out bool replaces)) return !replaces;
        }
        return false;
    }

    /// <summary>
    /// True if EquipItem would put the item on but only by displacing another item (its slot is taken).
    /// False when it fits an empty slot, and when the body has no slot for it at all.
    /// </summary>
    public bool IsBlockedByOtherItem(Item_Instance item)
    {
        if (item == null || item.GetComp_Equippable() == null) return false;
        foreach (BodyPart_Instance part in Body)
        {
            if (part.PreviewEquip(item, out bool replaces)) return replaces;
        }
        return false;
    }

    public bool EquipItem(int itemRefID, int count = 1, bool forceEquip = false)
    {
        Item_Instance item = ResolveEquip(itemRefID);
        if (item == null) return false;

        var comp = item.GetComp_Equippable();
        if (comp == null) return false;
        
        //Debug.Log($"Equipping {item.DisplayName} on {Owner.FirstName} with slots [{String.Join("|", comp.equipSlot)}]");
        bool equipped = false;
        foreach (BodyPart_Instance part in Body)
        {
            if (part.EquipItem(item, forceEquip))
            {
                equipped = true;
                break;
            }
        }

        if (equipped)
        {
            var slots = new List<BodyPartEquipSlot>(comp.coverSlot);
            if (slots.Count > 0)
            {
                foreach (var part in Body)
                {
                    if (part.EquipItem(item, ref slots, forceEquip) && slots.Count < 1) break;
                }
            }
            _cached_internals_description = false;
            return true;
        }
        else
        {
            return false;
        }
        
    }

    [JsonProperty] protected bool Climax = false;
    [JsonProperty] protected bool Cum = false;
    [JsonProperty] protected bool Stimulated = false;

    public bool isClimaxing(bool maleOnly = false, bool femaleOnly = false)
    {
        if (maleOnly) return Cum;
        else if (femaleOnly) return Climax;
        else return Cum || Climax;
    }

    [JsonIgnore] public List<int> EquippedItemRefs
    {
        get
        {
            //Debug.LogError("EQUIPPEDREFS INSIDE BODY");
            List<int> items = new List<int>();
            foreach(BodyPart_Instance i in this.Body)
            {
                items.AddRange(i.EquippedRefIDs);
            }
            //Debug.LogError("EQUIPPEDREFS after BODY");
            foreach (BodyInternal_Instance i in this.Internals)
            {
                items.AddRange(i.EquippedRefIDs);
            }
            //Debug.LogError("EQUIPPEDREFS after internals");
            return items;
        }
    }

    /// <summary>
    /// if bodyTag is not specified, default first ingestmethod bodytag
    /// </summary>
    /// <param name="i"></param>
    /// <param name="bodyTag"></param>
    public bool ConsumeIngestible(Item_Instance i, string bodyTag = "", bool forcefill = false)
    {
        ItemComponent_Ingestible ingest = i.GetComp_Ingestible();
        if (ingest == null) {
            Debug.LogError("ConsumeIngestible instance [" + i.DisplayName + "] has null ingest comp");
            return false;
        }

        if (scr_System_CentralControl.current.isSafeMode)
        { // no organ can ingest, automatically do
            IngestInternal(i);
            if (i.Tags.Contains("food_meal")) Owner.NotifyFoodConsume(i);
            return true;
        }

        if (bodyTag == "" && ingest.ingestMethod.Count > 0) bodyTag = ingest.ingestMethod[0].bodyTags;

        if (bodyTag != "")
        {
            var internals = Internals.FindAll(x => x.canContain && x.hasTag(bodyTag));
            if (internals.Count < 1)
            {
                Debug.LogError("ConsumeIngestible instance [" + i.DisplayName + "] ingestmethod [" + bodyTag + "] has null internal on " + Owner.FirstName);
                return false;
            }
            else
            {
                var randInternal = Utility.GetRandomElement(internals);
                if (randInternal == null) return false;
                randInternal.Ingest(i, null, forcefill);
                if (i.Tags.Contains("food_meal")) Owner.NotifyFoodConsume(i);
                return true;
            }
        }
        else
        {
            return false;
        }
        
    }

    protected void IngestInternal(Item_Instance i, ExperienceLog m = null)
    {
        var comp = i.GetComp_Ingestible();
        if (comp != null)
        {
            string key = comp.isLiquid ? "ui_entry_memory_description_swallow" : "ui_entry_memory_description_eat";
            if (key != "")
            {
                string memstring = LocalizeDictionary.QueryThenParse(key);
                memstring = memstring.Replace("$itemname$", i.DisplayName).Replace("$amount$", comp.isLiquid ? comp.amount.ToString("F1") : i.Count.ToString());

                var memInst2 = new MemInstance(new List<int>() { Owner.RefID }, new List<string>(), "", -1, -1, false, Memory_Response.Accept, Memory_Attitude.None, memstring);
                Owner.Memory.AddEntry(memInst2, null, -1, true);
            }
        }
        UtilityEX.ApplyOnConsume(this, comp.OnUseEffects);
        scr_System_CampaignManager.current.Unregister(i);
        return;
    }

    public bool HasBodyTag(List<string> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (!HasBodyTag(list[i])) return false;
        }
        return true;
    }

    /// <summary>
    /// HasBodyTag results per tag. Body tags only come from the parts' / internals' Base data, so a result holds until the
    /// body's part list changes - call ClearBodyCaches whenever it does (AddMissing does). Not saved.
    /// </summary>
    [NonSerialized][JsonIgnore] Dictionary<string, bool> _bodyTagCache = null;

    /// <summary>Wipe the caches derived from the part list (Internals, HasBodyTag). Call after anything adds / removes /
    /// replaces body parts or their internals.</summary>
    public void ClearBodyCaches()
    {
        internals = null;
        _bodyTagCache?.Clear();
    }

    public bool HasBodyTag(string s)
    {
        if (s == null) return false;
        if (_bodyTagCache == null) _bodyTagCache = new Dictionary<string, bool>();
        else if (_bodyTagCache.TryGetValue(s, out bool cached)) return cached;

        bool has = false;
        var parts = Body;
        for (int i = 0; i < parts.Count; i++)
        {
            if (parts[i].hasTag(s)) { has = true; break; }
        }
        _bodyTagCache[s] = has;
        return has;
    }

    public void UndressAllBy(List<string> tags, BodyEquipLayer layer = BodyEquipLayer.Skin)
    {
        foreach(var tag in tags)
        {
            foreach (BodyPart_Instance b in Body)
            {
                if (b.hasTag(tag))
                {
                    b.UnequipByLayer(layer);
                }
            }
        }
    }

    public int GetRevealingScoreByTag(string tag, BodyEquipLayer layer = BodyEquipLayer.Skin)
    {
        var i = -1;
        foreach (BodyPart_Instance b in Body)
        {
            i = Math.Max(i, b.GetRevealingScoreByTag(tag, layer));
        }
        return i;
    }

    public int GetMaxRevealingScoreByTags(List<string> tags, BodyEquipLayer layer = BodyEquipLayer.Skin)
    {
        int i = 0;
        foreach (string s in tags)  i = Math.Max(i, GetRevealingScoreByTag(s, layer));
        return i;
    }

    public bool HasEquipByFilter(BodyEquipLayer layer, int revealingScoreFilter = -1)
    {
        foreach (BodyPart_Instance b in Body)
        {
            if (b.HasEquipByFilter(layer, revealingScoreFilter)) return true;
        }
        return false;
    }

    public bool HasEquipByFilter(List<string> tags, BodyEquipLayer layer, int revealingScoreFilter = -1)
    {
        foreach (BodyPart_Instance b in Body)
        {
            foreach (var tag in tags)
            {
                if (b.hasTag(tag) && b.HasEquipByFilter(layer, revealingScoreFilter)) return true;
            }
        }
        return false;
    }

    public bool UnequipItem(int itemRefID)
    {
        bool returnVal = false;
        foreach (BodyPart_Instance i in Body)
        {
            returnVal = i.UnequipItem(itemRefID) || returnVal;
            //if (returnVal) break;
        }
        _cached_internals_description = false;
        return returnVal;

    }

    public List<BodyInternal_Instance> GetInternalsWithTags(List<string> tags)
    {
        return Internals.FindAll(x => x.hasAnyTag(tags));
    }

    public BodyInternal_Instance GetRandomInternalWithTag(string tag)
    {
        List<BodyInternal_Instance> l = Internals.FindAll(x => x.hasTag(tag));
        if (l.Count == 0) return null;
        return Utility.GetRandomElement(l);
    }

    public BodyInternal_Instance GetRandomInternalWithBaseID(string baseID)
    {
        List<BodyInternal_Instance> l = Internals.FindAll(x => x.baseID == baseID);
        if (l.Count == 0) return null;
        return Utility.GetRandomElement(l);
    }
    public BodyPart_Instance GetRandomPartWithBaseID(string baseID)
    {
        return Utility.GetRandomElement(Body.FindAll(x => x.Base.ID == baseID));
    }

    protected void ConcatenateClimax(ref string originalString, string sensitivityString)
    {
        int count = 1;
        string key = sensitivityString == "" ? "" : sensitivityString.Substring(sensitivityString.Length - 1);

        if (originalString != null && originalString.Length > 0) 
        {
            var disassemble = originalString.Split("||");
            count += int.Parse(disassemble[2]);
            key += disassemble[1];
        }
        originalString = "experience_sex_strong_climax" + "||" + key + "||" + count;
    }

    [JsonIgnore] public long ClimaxOccurrenceCount { get; private set; }
    [JsonIgnore] MessageCollect eventActCollector;

    /// <summary>Event AP boundary only. Does not touch stimulation, aftermath, or normal recovery.</summary>
    public IDisposable BeginEventAct(MessageCollect collector)
    {
        return new EventActScope(this, collector);
    }

    sealed class EventActScope : IDisposable
    {
        Character_Body body;
        readonly MessageCollect previous;

        public EventActScope(Character_Body body, MessageCollect collector)
        {
            this.body = body;
            previous = body.eventActCollector;
            body.eventActCollector = collector;
            body.ResetEventClimaxBlocker();
        }

        public void Dispose()
        {
            if (body == null) return;
            body.ResetEventClimaxBlocker();
            body.eventActCollector = previous;
            body = null;
        }
    }

    internal void ResetEventClimaxBlocker()
    {
        Climax = false;
        Cum = false;
        Stimulated = false;
        Owner.Stats.Climaxing?.SeveritySet(0);
    }

    public bool CheckClimax(MessageCollect message)
    {
        return CheckClimax(message, out _);
    }

    public bool CheckClimax(MessageCollect message, out bool climaxOccurred)
    {
        // Internal checks may supply the interaction job's collector rather than the event's.
        message = eventActCollector ?? message;
        var exp = new ExperienceLog();
        try { return CheckClimaxCore(message, out climaxOccurred, exp); }
        finally
        {
            // Mechanical grants can precede a failing memory/kojo/ejaculation effect. Keep those
            // already-earned deltas in the event's partial summary, without replaying any grants.
            if (eventActCollector != null) message.exp.Accumulate(exp);
        }
    }

    bool CheckClimaxCore(MessageCollect message, out bool climaxOccurred, ExperienceLog exp)
    {
        climaxOccurred = false;
        string climaxKeywords = "";
        string cumKeywords = "";

        if (scr_System_CentralControl.current.DLOG_NSFW.log_Cum && Owner.Stats.Climaxing != null && Stimulated) Debug.Log($"CheckClimax [{Owner.FirstName}] stimulated={Stimulated} climaxing={isClimaxing()} sexStim={Owner.Stats.SexStimulation.Severity} threshold={Owner.Stats.CumThreshold}");

        //if (scr_UpdateHandler.current.skipCurrentRoundClimaxCheck) return;
        if (Owner.Stats.Climaxing == null) return false;
        if (!Stimulated) return false;
        if (isClimaxing()) return true;
        if (Owner.Stats.SexStimulation.Severity >= Owner.Stats.CumThreshold) 
        {
           // Debug.Log($"canclimax, checking timestop {scr_System_Time.current.TimeStopStrict && !Owner.CanActInTimeStop} or {Owner.Stats.Climaxing.Severity > 0}");
            // forbid climax if timestopped
            // BUT!! ALLOW CLIMAX DURING RESUME
            if (Owner.isTimeStopped) return false;
            if (Owner.Stats.Climaxing.Severity > 0) return true; // block repeat climax if still lingering

            var intensity = Owner.Stats.SexStimulation.Severity;
            List<string> climaxTags = new List<string>();
            UtilityEX.GetActorTag(ref climaxTags, Owner);

            int climaxDebuff = 0;
            // Accumulated across every climaxing part below, and the pride/partner tags further down, so all
            // of Owner's climax-experience grants (C/B/M/V/W/A_climax, raped_climax, masturbate_climax,
            // animal_climax) come from a single CheckExperienceGainNoStimulate call at the end of this method
            // instead of one call per part/per experience - each experience's own RequiredOwnerTags/
            // RequiredCOMTags decides which (if any) actually grant from the merged pool.
            List<string> allClimaxOwnerTags = new List<string>();
            List<string> allClimaxComTags = new List<string>();
            foreach (var part in this.Internals)
            {

                if (part.Sensitivity != "" && (int)Owner.Stats.GetStatusSeverityByStringMatch(part.Sensitivity) >= Owner.Stats.CumThreshold)
                {

                    ConcatenateClimax(ref climaxKeywords, part.Sensitivity);// climaxKeywords.Add("Strong Climax "+ part.Sensitivity);
                    Item_Instance cum = null;
                    // if penis then cum, if not then just climax

                    List<string> selfTag = new List<string>(part.Base.tags) { };
                    selfTag.AddRange(climaxTags);
                    selfTag.Add(part.Sensitivity);
                    selfTag.Add("climax");

                    if (!Climax) ClimaxOccurrenceCount++;
                    this.Climax = true;
                    // ADDLOG CLIMAX
                    float cumAmount = 0;
                    cumKeywords = "yes";
                    if (part.CumVolume > 0)
                    {
                        cumAmount = part.CumVolume * (scr_System_CampaignManager.current.DebugMode ? 5 : 1);
                        cum = part.Cum(cumAmount, exp);
                    }
                    else
                    {
                        //Debug.Log($"{Owner.FirstName} cannot cum, {part.DisplayName} volume {part.CumVolume}");
                        cum = null;
                    }

                    if (cum != null)
                    {
                        this.Cum = true;

                        // find valid container for cum

                        if (scr_System_CentralControl.current.DLOG_NSFW.log_Cum) Debug.Log($"[CumDebug] {Owner.FirstName} part={part.DisplayName} canFuck={part.canFuck} lastInteractedCount={part.LastInteactedRefs.Count} refs=[{string.Join(",", part.LastInteactedRefs.ConvertAll(x => x == null ? "NULL" : $"{x.Owner?.FirstName}.{x.baseID}"))}]");

                        List<BodyInternal_Instance> possiblecontainers = part.LastInteactedRefs.FindAll(x => x.canContain);
                        List<BodyInternal_Instance> possibleEmptyContainers = possiblecontainers.FindAll(x => !x.containsOverCapacity);

                        BodyInternal_Instance container = (possibleEmptyContainers.Count > 0 ? Utility.GetRandomElement(possibleEmptyContainers)
                                        : (possiblecontainers.Count > 0 ? Utility.GetRandomElement(possiblecontainers) : null));

                        if (scr_System_CentralControl.current.DLOG_NSFW.log_Cum) Debug.Log($"[CumDebug] {Owner.FirstName} containers={possiblecontainers.Count} emptyContainers={possibleEmptyContainers.Count} selectedContainer={container?.Owner?.FirstName}.{container?.baseID ?? "NONE (cum on ground)"}");


                        if (container != null)
                        {   // if valid bodyinternal container exist : Fucker != null && Fucker.canContain
                            // Share only across this ingest and its follow-up grant, never across actors or loads.
                            var forbiddenIngestExperienceIDs = new HashSet<string>();
                            if (!container.Ingest(cum, exp, true, forbiddenExperienceIDs: forbiddenIngestExperienceIDs))
                            {
                                scr_System_CampaignManager.current.Map.FindRoomByChara(ownerRefID).AddItem(cum);
                                // cum on ground, write message
                                exp.AppendClimaxMSG(Owner.RefID, LocalizeDictionary.QueryThenParse("experience_sex_cumtainer_none")
                                    .Replace("$amount$", $"{cum.GetComp_Ingestible().amount.ToString("N0")}"));
                            }

                            exp.PrependClimaxMSG(Owner.RefID, LocalizeDictionary.QueryThenParse("experience_sex_cumtainer_prepend"));

                            container.Parent.Owner.Body.NotifyInternalsContentChange();
                            string key = "ui_entry_memory_description_cum_";
                            // ADDLOG CUM FOR RECEIVER
                            var rel = container.Owner.Relationships.FindRelationshipWith(part.Owner);

                            var desc = LocalizeDictionary.QueryThenParse( rel.HasPermission_Family()? "ui_entry_memory_description_creampie" : "ui_entry_memory_description_creampie_nopermission");
                            desc = desc.Replace("$target$", part.Owner.FirstName)
                                        .Replace("$cum_verb$", LocalizeDictionary.QueryThenParse(key + container.Base.ID))
                                        .Replace("$amount$", cumAmount.ToString("N0"));


                            List<string> containerTags = new List<string>(container.Base.tags) { };
                            containerTags.Add("cum");
                            UtilityEX.GetActorTag(ref containerTags, container.Owner);
                            if (container.Sensitivity != "") containerTags.Add(container.Sensitivity);

                            UtilityEX.CheckExperienceGainNoStimulate(container.Owner, cumAmount, false, containerTags, new List<string>(), exp, forbiddenIngestExperienceIDs);

                            // ADDLOG CUM FOR SHOOTER
                            var desc2 = LocalizeDictionary.QueryThenParse( "ui_entry_memory_description_cumOnto");
                            desc2 = desc2.Replace("$target$", container.Owner.FirstName)
                                        .Replace("$cum_verb$", LocalizeDictionary.QueryThenParse(key + container.Base.ID))
                                        .Replace("$amount$", cumAmount.ToString("N0"));

                            if (!rel.HasPermission_Family())
                            {
                                // trust penalty only applies to a creampie proper (the container is connected
                                // to a womb) - mouth/anus/etc without family permission still cost Badwill/Fear
                                if (container.womb != null)
                                {
                                    rel.ModRelationValue(RelationshipScoreType.Trust, -cumAmount, false);
                                    exp.AddRelations(rel.Owner.RefID, rel.TargetID, RelationshipScoreType.Trust, (int)-cumAmount);
                                }

                                rel.ModRelationValue(RelationshipScoreType.Badwill, cumAmount, false);
                                exp.AddRelations(rel.Owner.RefID, rel.TargetID, RelationshipScoreType.Badwill, (int)cumAmount);

                                rel.ModRelationValue(RelationshipScoreType.Fear, cumAmount, false);
                                exp.AddRelations(rel.Owner.RefID, rel.TargetID, RelationshipScoreType.Fear, (int)cumAmount);
                            }

                            var memInst2 = new MemInstance(new List<int>() { part.Owner.RefID }, selfTag, "", -1, -1, false, Memory_Response.Accept, rel.HasPermission_Family() ? Memory_Attitude.Like : Memory_Attitude.Hate, desc);
                            container.Owner.Memory.AddEntry(memInst2, containerTags, -1, true);

                            var memInst3 = new MemInstance(new List<int>() { container.Owner.RefID }, containerTags, "", -1, -1, true, Memory_Response.Accept, Memory_Attitude.Like, desc2);
                            part.Owner.Memory.AddEntry(memInst3, selfTag, -1, true);
                        }
                        else
                        {   // cum on ground
                            scr_System_CampaignManager.current.Map.FindRoomByChara(ownerRefID).AddItem(cum);

                            // ADDLOG CUM FOR SHOOTER
                            var desc = LocalizeDictionary.QueryThenParse("ui_entry_memory_description_cum");
                            desc = desc.Replace("$amount$", cumAmount.ToString("N0"));


                            var memInst4 = new MemInstance(new List<int>() { part.Owner.RefID }, new List<string>() { "" }, "", -1, -1, true, Memory_Response.Accept, Memory_Attitude.Like, desc);
                            part.Owner.Memory.AddEntry(memInst4, selfTag, -1, true);
                        }
                        climaxDebuff -= 200;
                        part.TryModOwnerStimulationStatus(-200);


                        var desc1 = LocalizeDictionary.QueryThenParse("ui_entry_memory_description_climax_keyworded").Replace("$part$", part.DisplayName);


                        var memInst5 = new MemInstance(new List<int>() { part.Owner.RefID }, new List<string>(), "", -1, -1, true, Memory_Response.Accept, Memory_Attitude.Like, desc1);
                        part.Owner.Memory.AddEntry(memInst5, selfTag, -1, true);

                        allClimaxOwnerTags.AddRange(selfTag);

                    }
                    else
                    {
                        climaxDebuff -= 50;
                        part.TryModOwnerStimulationStatus(-40);

                        var desc1 = LocalizeDictionary.QueryThenParse("ui_entry_memory_description_climax_keyworded").Replace("$part$", part.DisplayName);

                        var memInst6 = new MemInstance(new List<int>() { part.Owner.RefID }, new List<string>(), "", -1, -1, true, Memory_Response.Accept, Memory_Attitude.Like, desc1);
                        part.Owner.Memory.AddEntry(memInst6, selfTag, -1, true);

                        allClimaxOwnerTags.AddRange(selfTag);
                    }
                }
            }

            if (Climax) Owner.Stats.Climaxing.SeverityAdd(Cum ? 2 : 7);

            if (climaxDebuff != 0 && climaxKeywords.Length > 0)
            {
                //if (!Cum) climaxDebuff = Math.Clamp(climaxDebuff, -200, 0);
                if (scr_System_CentralControl.current.LogPrefs.DLog_Status) Debug.Log($"Adding climax status on {Owner.FirstName} debuffstrength {climaxDebuff}");

                //Owner.Stats.AddOrModStatus("chara_status_lust_hidden", climaxDebuff/10);

                Owner.Stats.AddOrModStatus("chara_status_sexual_climax_after", climaxDebuff);
                float satisfiedBonus = 0;
                if (Owner.Stats.Lust_Hidden != null)
                {
                    satisfiedBonus += Math.Min(climaxDebuff * 0.5f, Owner.Stats.Lust_Hidden.Severity);
                    Owner.Stats.AddOrModStatus(Owner.Stats.Lust_Hidden.ID, climaxDebuff * 0.5f);
                }

                var disassemble = climaxKeywords.Split("||");
                climaxKeywords = LocalizeDictionary.QueryThenParse(disassemble[0])
                    .Replace("$sensitivity$", disassemble[1])
                    .Replace("$count$", LocalizeDictionary.QueryThenParse(disassemble[0] + "_" + disassemble[2]));

                if (cumKeywords.Length > 0)
                {
                    exp.PrependClimaxMSG(Owner.RefID, $"{Owner.FirstName}: {climaxKeywords}/$append$");
                }
                else
                {
                    exp.PrependClimaxMSG(Owner.RefID, $"{Owner.FirstName}: {climaxKeywords}");
                }
                //string s = Owner.FirstName + ":" + climaxKeywords + (cumKeywords.Length > 0 ? "/" + cumKeywords : "");

                //Debug.LogError("Merge Climax Message");

                UtilityEX.GetAPsFrom(this.owner, out var listAP);
                var listEP = new List<EvaluationPackage>();
                foreach (var ap in listAP) listEP.AddRange(ap.ListEP);

                bool logged = false;

                if (Owner.CurrentJob != null && Owner.CurrentJob is Job_Sex_Group sexJob)
                {
                    List<int> relevantActorRefs = Owner.CurrentJob.GetLastInteractedActorRefs(Owner.RefID);
                    Utility.ShuffleList(relevantActorRefs);

                    // consent check for the trust bonus below: scene has no designated non-consenting party,
                    // and most of Owner's currently active interactions actually carried permission
                    bool sceneHasConsent = sexJob.Rapist.Count == 0;
                    int consideredEP = 0, permittedEP = 0;
                    foreach (var epc in listEP)
                    {
                        if (epc.Doer != Owner && epc.Receiver != Owner) continue;
                        consideredEP++;
                        if (epc.HasPermission) permittedEP++;
                    }
                    bool mostHadPermission = consideredEP == 0 || permittedEP * 2 >= consideredEP;
                    float trustGain = sceneHasConsent && mostHadPermission ? intensity * 0.01f : 0f;

                    // pride penalty for a non-consensual climax, routed through the standard personality-driven
                    // CheckPrideChange pathway (same "raped"/"forced" tags CheckRelationshipChange's Fear bump
                    // already reads) rather than a direct mod, so tier/mult curves apply same as every other
                    // pride event. amount = intensity * 0.1 lands on the documented "100 intensity -> -1 pride"
                    // at the same tier-distance=-1/mult=1 baseline the existing imprisoned/restrained entries assume.
                    var prideTags = new List<string>();
                    foreach (var epc in listEP)
                    {
                        if (epc.Doer == Owner || epc.Receiver == Owner) prideTags.AddRange(epc.GetActorEPTags(Owner.RefID));
                    }
                    Utility.DistinctInPlace(prideTags);
                    if (prideTags.Contains("raped") || prideTags.Contains("forced"))
                    {
                        Owner.Relationships.CheckPrideChange(prideTags, prideTags, intensity * 0.001f, exp);
                    }

                    // partner-side tags (counterpart's own ActorKeywords + the COM's own comTags, e.g. "animal"/
                    // "unsafe") - mirrors prideTags above but via GetActorEPTargetTags instead of GetActorEPTags,
                    // since this needs to describe who Owner was WITH, not Owner's own state. Feeds
                    // experience_sex_animal_climax's RequiredCOMTags via the merged call below.
                    var partnerTags = new List<string>();
                    foreach (var epc in listEP)
                    {
                        if (epc.Doer == Owner || epc.Receiver == Owner) partnerTags.AddRange(epc.GetActorEPTargetTags(Owner.RefID));
                    }
                    Utility.DistinctInPlace(partnerTags);

                    allClimaxOwnerTags.AddRange(prideTags);
                    allClimaxComTags.AddRange(partnerTags);

                    foreach (var c in relevantActorRefs)
                    {
                        var target = scr_System_CampaignManager.current.FindInstanceByID(c);
                        var rel = target == null ? null : Owner.Relationships.FindRelationshipWith(target);
                        if (rel != null)
                        {
                            if (!logged)
                            {
                                var kol = new KojoCollector(Owner, "OnClimax_target");
                                kol.LoadRel(rel);
                                kol = Owner.Relationships.GetKOJOMessage_Suffix(kol, null);
                                if (kol != null)
                                {
                                    kol.SetVisibleToAll();
                                    message.AddMessage_After(kol, Owner.CurrentRoom);//.Add();
                                    logged = true;
                                }
                            }
                            float value = Mathf.Abs(climaxDebuff * 0.1f) + Mathf.Abs(satisfiedBonus);
                            rel.ModRelationValue(RelationshipScoreType.Goodwill, value, false);
                            rel.ModRelationValue(RelationshipScoreType.Desire, value, false);
                            exp.AddRelations(rel.Owner.RefID, rel.TargetID, RelationshipScoreType.Goodwill, (int)value);
                            exp.AddRelations(rel.Owner.RefID, rel.TargetID, RelationshipScoreType.Desire, (int)value);
                            if (satisfiedBonus > 0) exp.AddMessage(rel.Owner.RefID, $"{Owner.FirstName}'s desire is getting satisfied ({(satisfiedBonus).ToString("+0;-#")})");

                            if (trustGain != 0)
                            {
                                rel.ModRelationValue(RelationshipScoreType.Trust, trustGain, false);
                                exp.AddRelations(rel.Owner.RefID, rel.TargetID, RelationshipScoreType.Trust, (int)trustGain);
                            }
                        }

                    }
                }

                // single grant covering every climax-experience for Owner (C/B/M/V/W/A_climax from the per-part
                // loop above, raped_climax/masturbate_climax/animal_climax from prideTags/partnerTags above, if
                // Owner was in a Job_Sex_Group) - RequiredOwnerTags/RequiredCOMTags on each experience decides
                // which of them actually grant from this one merged pool.
                UtilityEX.CheckExperienceGainNoStimulate(Owner, 1, false, Utility.Distinct(allClimaxOwnerTags), Utility.Distinct(allClimaxComTags), exp);

                List<string> wbmessages = new List<string>();
                foreach (var wb in this.Owner.wombs)
                {
                    wb.NotifyClimax(intensity, Owner.ReproCycle, wbmessages);
                }
                if (wbmessages.Count > 0)
                {
                    foreach (var m in wbmessages)
                    {
                        message.AddMessage_After(new DescriptionCollector(m.Replace("$name$", Owner.FirstName)), Owner.CurrentRoom);
                    }
                }

                //scr_UpdateHandler.current.NotifyClimax(Owner.RefID, s, exp);
                //exp.AddClimaxMSG(Owner.RefID, s);
                //message.exp.MergeWith(exp, false);
                List<int> actors = new List<int>();
                if (Owner.CurrentJob != null) actors.AddRange(Owner.CurrentJob.actorRefID);
                actors.AddRange(Owner.InteractionJob.actorRefID);
                Utility.DistinctInPlace(actors);

                if (message.accumulateExperience)
                {
                    message.exp.Accumulate(exp);
                    message.exp.AddRelevantChara(actors);
                    exp.Clear();
                }
                else
                {
                    exp.Finalize(out var desc);
                    if (desc != null)
                    {
                        desc.relevantActors = actors;
                        message.messages_exp.Add(desc);
                        if (Owner.CurrentRoom != null) Owner.CurrentRoom.NotifyDescCollect(desc, MessageCollect_Type.exp);
                    }
                }

                if (!logged)
                {
                    //exp.AddMessage(Owner.RefID, "kojo message here");
                    var kol = new KojoCollector(Owner, "OnClimax_single");
                    kol.LoadSelfTags(Owner, null);
                    kol = Owner.Relationships.GetKOJOMessage_Suffix(kol, null);
                    if (kol != null)
                    {
                        kol.SetVisibleToAll();
                        message.AddMessage_After(kol, Owner.CurrentRoom);//.Add();
                                                                         // logged = true;
                    }

                    //message.messages_kojo_after.Add(Owner.Relationships.Personality.GetKOJOMessage("OnClimax_single", this.Owner, tags, listEP));
                }

                climaxOccurred = true;
                return true;

            }
            else return false;

        }
        else return false;
    }

}
