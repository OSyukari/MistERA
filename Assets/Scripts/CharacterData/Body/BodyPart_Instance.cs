using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
using System.Linq;


public class BodyPart_Instance : I_CombatItem
{
    [JsonProperty] protected string baseID = "";

    private BodyPart_Base basePointer = null;
    [JsonIgnore] public BodyPart_Base Base
    {
        get
        {
            if (basePointer == null) basePointer = scr_System_Serializer.current.GetByNameOrID_BodyPart_Base(baseID);
            return basePointer;
        }
    }

    public BodyPart_Instance()
    {

    }

    Dictionary<I_CombatItem, List<CombatAction>> _combatActions = null;

    [JsonIgnore]
    public Dictionary<I_CombatItem, List<CombatAction>> CombatActions { get
        {
            if (_combatActions == null)
            {
                _combatActions = new Dictionary<I_CombatItem, List<CombatAction>>();

                if (this.Comp_Weapon != null)
                {
                    if (!_combatActions.ContainsKey(this)) _combatActions.Add(this, new List<CombatAction>());
                    _combatActions[this].AddRange(scr_System_Serializer.current.GetCombatActions(this.Base));
                }
                if (this.equipLayers.Contains(BodyEquipLayer.Outer))
                {
                    foreach (var slot in this.availableSlots)
                    {
                        var refID = this.GetEquip(BodyEquipLayer.Outer, slot);
                        if (refID == -1) continue;
                        var item = ResolveEquip(refID);
                        if (item == null) continue;

                        if (!_combatActions.ContainsKey(item)) _combatActions.Add(item, new List<CombatAction>());
                        _combatActions[item].AddRange(scr_System_Serializer.current.GetCombatActions(this.Base));
                    }
                }
            }
            return _combatActions;
        } }


    public void ReEstablishParent(Character_Trainable c)
    {
        this.owner = c;
        this.ownerRefID = c.RefID;

        foreach (var i in internals) i.ReEstablishParent(this);
    }
    public void Initialize(string baseID, Character_Trainable c)
    {
        this.baseID = baseID;
        ReEstablishParent(c);

        //Debug.Log("BodyPart_Instance : baseID [" + baseID+"] refID ["+ownerRefID+"]");

        basePointer = scr_System_Serializer.current.GetByNameOrID_BodyPart_Base(baseID);
        foreach (string s in Base.internalID)
        {
            if (s.Length < 1) continue;
            BodyInternal_Instance inter = new BodyInternal_Instance();
            if (!inter.Initialize(c, s, this)) continue;
            else if ((inter.hasTag("vagina") || inter.hasTag("womb") || inter.hasTag("urethra")) && !Owner.Body.HasFemaleParts) { }
            //else if (inter.hasTag("anus") && Owner.Body.Size_A.ID == "trait_Size_A_none") { }
            else if (inter.hasTag("penis") && !Owner.Body.HasMaleParts) { }
            else if (inter.hasTag("clit") && Owner.Body.HasMaleParts) { }
            else if (inter.hasTag("breast") && !Owner.Body.HasFemaleParts) { }
            else
            {
                this.internals.Add(inter);

                if (inter.hasTag("womb") && Owner.Race != null)
                {
                    if (scr_System_Serializer.current.MasterList.humanoid_Races.GetReproduction(Owner.Race.ID, out var template))
                    {
                        switch (template.wombType)
                        {
                            case BodyInternal_Base_WombType.spontaneous:
                                inter.womb = new Womb_Spontaneous(inter, template);
                                break;

                            case BodyInternal_Base_WombType.induced:
                                inter.womb = new Womb_Induced(inter, template);
                                break;
                            case BodyInternal_Base_WombType.infertile:
                                inter.womb = new Womb_Infertile(inter, null);
                                break;
                            default:
                                Debug.LogError($"error unimplemented womb type {template.wombType}");
                                inter.womb = new Womb_Infertile(inter, null);
                                break;
                        }
                    }
                    // else, has womb but no cycle
                    else
                    {
                        inter.womb = new Womb_Infertile(inter, null);
                    }
                    inter.womb.ReEstablishParent(inter);
                    Owner.RegisterWomb(inter.womb);

                }
            }
        }

        foreach (BodyEquipLayer i in equipLayers)
        {
            foreach (BodyPartEquipSlot j in availableSlots)
            {
                var key = i.ToString() + "||" + j.ToString();
                if (!contentsIndex.ContainsKey(key)) contentsIndex.Add(key, -1);
                else
                {
                    Debug.LogError($"{c.FirstName} error in initializing bodypartInstance {baseID}, duplicate equipslot key {key} in {String.Join("|",equipLayers)} {String.Join("|", availableSlots)}");
                }
                if (!coversIndex.ContainsKey(key)) coversIndex.Add(key, -1);

            }
        }
    }

    private int ownerRefID = -1;
    private Character_Trainable owner = null;
    [JsonIgnore] public Character_Trainable Owner
    {
        get
        {
            if (ownerRefID > -1 && owner == null) owner = scr_System_CampaignManager.current.FindInstanceByID(ownerRefID);
            return owner;
        }
    }

    private string OwnerName
    {
        get
        {
            if (Owner != null) return Owner.FirstName;
            else return "";
        }
    }
    private string OwnerRace
    {
        get
        {
            if (Owner != null) return Owner.Race.DisplayName;
            else return "";
        }
    }

    [JsonIgnore] public string DisplayName { get { return Base.DisplayName; } }
    [JsonIgnore] public string Tooltip { get { return String.Format(Base.Tooltip, OwnerRace); } }

    public bool hasTag(string tag)
    {
        bool hastag = Base.tags.Contains(tag);
        foreach (BodyInternal_Instance i in internals)
        {
            hastag = hastag || i.hasTag(tag);
        }

        return hastag;
    }
    [JsonIgnore] public int sortOrder { get { return Base.sortOrder; } }

    public List<BodyInternal_Instance> internals = new List<BodyInternal_Instance>();

    [JsonIgnore] public List<BodyPartEquipSlot> availableSlots { get { return Base.AvailableSlots; } }
    [JsonIgnore] public List<BodyEquipLayer> equipLayers { get { return Base.equipLayers; } }

    public BodyInternal_Instance GetInternalByEquipRef(int equipRef)
    {
        foreach (BodyInternal_Instance i in internals) if (i.EquippedRefIDs.Contains(equipRef)) return i;
        return null;
    }
    public BodyInternal_Instance GetRandInternalWithTagsLoose(List<string> tags)
    {
        return Utility.GetRandomElement(internals.FindAll(x => Utility.ListContainsLoose(x.Base.tags, tags)));
    }
    [JsonIgnore] public List<int> EquippedRefIDs { get
        {
            var v = contentsIndex.Values.ToList();
            v.RemoveAll(x => x == -1);
            v = v.Distinct().ToList();
            return v;
        } }

    List<Item_Instance> _equippedItems;
    [JsonIgnore] 
    public List<Item_Instance> EquippedItems { get
        {
            if (_equippedItems == null)
            {
                _equippedItems = new List<Item_Instance>();
                foreach (var i in EquippedRefIDs)
                {
                    var item = ResolveEquip(i);
                    if (item != null) _equippedItems.Add(item);
                }
            }
            return _equippedItems;
        } }

    [JsonIgnore]
    public List<string> ItemTags
    {
        get
        {
            return this.Base.tags;
        }
    }

    ItemComponent_Weapon _weaponComp = null;
    ItemComponent_Defense _defenseComp = null;
    ItemComponentTemplate _template = null;
    protected ItemComponentTemplate template
    {
        get
        {
            if (_template == null) _template = new ItemComponentTemplate();
            return _template;
        }
    }
    [JsonIgnore]
    public ItemComponent_Weapon Comp_Weapon
    {
        get
        {
            if (this._weaponComp == null && Base.hasNaturalWeapon)
            {
                template.comp_Weapon = Base.NaturalWeapon;
                _weaponComp = new ItemComponent_Weapon();
                _weaponComp.CompTemplate = template;
            }
            return this._weaponComp;
        }
    }

    [JsonIgnore]
    public ItemComponent_Defense Comp_Defense
    {
        get
        {
            if (this._defenseComp == null && Base.hasNaturalDefense)
            {
                template.comp_Defense = Base.NaturalDefense;
                _defenseComp = new ItemComponent_Defense();
                _defenseComp.CompTemplate = template;
            }
            return this._defenseComp;
        }
    }

    DefenseStats _defense = null;
    [JsonIgnore]
    public DefenseStats Defense
    {
        get
        {
            if (this._defense == null && Comp_Defense != null)
            {
                this._defense = new DefenseStats();
                this._defense.Set(LocalizeDictionary.QueryThenParse("natural_armor_part").Replace("$part$", this.DisplayName), Comp_Defense);
            }
            return this._defense;
        }
    }

    //List<Item_Instance> equippedItems;
    [JsonProperty] Dictionary<string, int> coversIndex = new Dictionary<string, int>();
    [JsonProperty] Dictionary<string, int> contentsIndex = new Dictionary<string, int>();


    /// <summary>
    /// Return -1 fail, return 0 success return 1+ swapped gear
    /// </summary>
    /// <param name="itemRefID"></param>
    /// <param name="forceEquip"></param>
    /// <returns></returns>
    public bool EquipItem(Item_Instance item, ref List<BodyPartEquipSlot> slots, bool forceEquip = false)
    {
        var comp = item.GetComp_Equippable();
        bool equipped = false;
       // Debug.Log($"Equipping {String.Join(" ", slots)} + {comp.equipLayer} on {this.DisplayName}, allIndex [{String.Join(",", contentsIndex.Keys)}]");

        for (int i = slots.Count - 1; i >= 0; i--)
        {
            var slot = slots[i];
            string Tuple = comp.equipLayer.ToString() + "||" + slot.ToString();

            /// CODE START
            if (coversIndex.ContainsKey(Tuple))
            {
                //cannot equip same item into its main slot and cover slot -> both shoes equipped on same slot
                if (contentsIndex.ContainsKey(Tuple) && contentsIndex[Tuple] == item.RefID) continue;
                else if (coversIndex[Tuple] == -1)
                {
                    coversIndex[Tuple] = item.RefID;
                    // equip success
                    ClearEquipCache();
                    equipped = true;
                    slots.RemoveAt(i);
                }
                else if (coversIndex[Tuple] != -1 && coversIndex[Tuple] != item.RefID && forceEquip == true)
                {
                    var returnVal = coversIndex[Tuple];
                    coversIndex[Tuple] = item.RefID;

                    Owner.UnequipItem(returnVal);
                    ClearEquipCache();
                    // replace equipped
                    equipped = true;
                    slots.RemoveAt(i);
                }
            }
        }

        foreach (BodyInternal_Instance i in this.internals)
        {
            equipped = i.EquipItem(item, ref slots, forceEquip) || equipped;
        }
        if (equipped) ClearEquipCache();
        return equipped;
    }

    /// <summary>
    /// If equip midway fail will unequip self
    /// </summary>
    /// <param name="item"></param>
    /// <param name="forceEquip"></param>
    /// <returns></returns>
    public bool EquipItem(Item_Instance item, bool forceEquip = false)
    {
        var comp = item.GetComp_Equippable();
        bool equipped = false;
        bool unequip = false;
        // Debug.Log($"Equipping {String.Join(" ", slots)} + {comp.equipLayer} on {this.DisplayName}, allIndex [{String.Join(",", contentsIndex.Keys)}]");

        // prevalidate
        foreach(var slot in comp.equipSlot)
        {
            string Tuple = comp.equipLayer.ToString() + "||" + slot.ToString();

            /// CODE START
            if (contentsIndex.ContainsKey(Tuple))
            {
                if (contentsIndex[Tuple] == -1)
                {
                    contentsIndex[Tuple] = item.RefID;
                    // equip success
                    ClearEquipCache();
                    equipped = true;
                }
                else if (contentsIndex[Tuple] == item.RefID)
                {
                    // do nothing
                }
                else
                {
                    var returnVal = contentsIndex[Tuple];
                    contentsIndex[Tuple] = item.RefID;

                    Owner.UnequipItem(returnVal);
                    ClearEquipCache();
                    // replace equipped
                    equipped = true;
                }
            }
            else
            {
                if (equipped) unequip = true;
                equipped = false;
            }
        }
        
        if (unequip)
        {
            UnequipItem(item.RefID);
            equipped = false;
        }

        if (!equipped)
        {
            foreach (BodyInternal_Instance i in this.internals)
            {
                equipped = i.EquipItem(item, forceEquip);
                if (equipped) break;
            }
        }


        if (equipped) ClearEquipCache();
        return equipped;
    }

    /// <summary>
    /// Mirrors EquipItem(item) without changing anything: true if this part (or one of its organs) would take the item;
    /// replaces = that would displace another item.
    /// </summary>
    public bool PreviewEquip(Item_Instance item, out bool replaces)
    {
        replaces = false;
        var comp = item.GetComp_Equippable();
        if (comp == null) return false;
        bool equipped = false, unequip = false, displaces = false;
        foreach (var slot in comp.equipSlot)
        {
            string Tuple = comp.equipLayer.ToString() + "||" + slot.ToString();
            if (contentsIndex.TryGetValue(Tuple, out var current))
            {
                if (current == -1) equipped = true;
                else if (current != item.RefID)
                {
                    equipped = true;
                    displaces = true;
                }
            }
            else
            {
                if (equipped) unequip = true;
                equipped = false;
            }
        }
        if (unequip) equipped = false;
        if (equipped)
        {
            replaces = displaces;
            return true;
        }
        foreach (BodyInternal_Instance i in this.internals)
        {
            if (i.PreviewEquip(item, out replaces)) return true;
        }
        replaces = false;
        return false;
    }

    /// <summary> the item behind a slot id - through the owner's body (registered items and virtual outfit pieces) </summary>
    Item_Instance ResolveEquip(int id)
    {
        if (id == -1) return null;
        if (Owner != null && Owner.Body != null) return Owner.Body.ResolveEquip(id);
        return id >= 0 ? scr_System_CampaignManager.current.FindItemInstanceByID(id) : null;
    }

    protected void ClearEquipCache()
    {
        _equippedItems = null;
        _combatActions = null;
    }

    public bool UnequipItem(int itemRefID)
    {
        bool returnVal = false;
        /// CODE START
        if (contentsIndex.ContainsValue(itemRefID))
        {
            var list = contentsIndex.Keys.ToList();
            foreach (var key in list)
            {
                if (contentsIndex[key] == itemRefID)
                {
                    returnVal = true;
                    contentsIndex[key] = -1;
                }
            }
        }

        if (coversIndex.ContainsValue(itemRefID))
        {
            var list = coversIndex.Keys.ToList();
            foreach (var key in list)
            {
                if (coversIndex[key] == itemRefID)
                {
                    returnVal = true;
                    coversIndex[key] = -1;
                }
            }
        }

        if (returnVal)
        {
            ClearEquipCache();
            return returnVal;
        }

        foreach (BodyInternal_Instance i in this.internals) returnVal = i.UnequipItem(itemRefID) || returnVal;
        return returnVal;
    }

    public void UnequipByLayer(BodyEquipLayer filter, Revealing revealingScoreFilter = Revealing.Erotic)
    {
        foreach(var layer in this.equipLayers)
        {
            if (layer < filter) continue;
            {
                foreach (BodyPartEquipSlot slot in this.availableSlots)
                {
                    var equip = this.GetEquip(layer, slot);
                    var comp = ResolveEquip(equip)?.GetComp_Equippable();
                    if (comp != null && comp.revealing >= revealingScoreFilter)
                    {
                        Owner.UnequipItem(equip);
                    }
                }
                foreach (var organ in this.internals) organ.UnequipByLayer(layer, revealingScoreFilter);
            }
        }
    }

    public bool HasEquipByFilter(BodyEquipLayer layerFilter, int revealingScoreFilter = -1)
    {
        foreach(var layer in this.equipLayers)
        {
            if (layer <= layerFilter && layerFilter != BodyEquipLayer.None) continue;
            foreach (BodyPartEquipSlot slot in this.availableSlots)
            {
                var comp = ResolveEquip(this.GetEquip(layer, slot))?.GetComp_Equippable();
                if (comp != null && (int)comp.revealing >= revealingScoreFilter) return true;
            }
        }
        return false;
    }

    public bool TryGetEquip(out Item_Instance value, BodyEquipLayer i, BodyPartEquipSlot j)
    {
        value = null;
        var v = GetEquip(i, j);
        if (v != -1) value = ResolveEquip(v);
        return value != null;
    }
    public bool TryGetCover(out Item_Instance value, BodyEquipLayer i, BodyPartEquipSlot j)
    {
        value = null;
        var v = GetCover(i, j);
        if (v != -1) value = ResolveEquip(v);
        return value != null;
    }
    protected int GetCover(BodyEquipLayer i, BodyPartEquipSlot j)
    {
        string Tuple = i.ToString() + "||" + j.ToString();
        if (coversIndex.ContainsKey(Tuple)) return coversIndex[Tuple];
        else return -1;
    }
    protected int GetEquip(BodyEquipLayer i, BodyPartEquipSlot j)
    {

        string Tuple = i.ToString() + "||" + j.ToString();
        if (contentsIndex.ContainsKey(Tuple)) return contentsIndex[Tuple];
        else return -1;
    }

    public Item_Instance GetRandArmor(BodyPartEquipSlot slot)
    {
        Item_Instance item;
        if (TryGetEquip(out item, BodyEquipLayer.Outer, slot) && item.Comp_Defense != null) return item;
        if (TryGetCover(out item, BodyEquipLayer.Outer, slot) && item.Comp_Defense != null) return item;
        if (TryGetEquip(out item, BodyEquipLayer.Inner, slot) && item.Comp_Defense != null) return item;
        if (TryGetCover(out item, BodyEquipLayer.Inner, slot) && item.Comp_Defense != null) return item;
        return null;
    }

    public int GetRevealingScore(BodyEquipLayer layer)
    {
        int score = 0;
        switch (layer)
        {
            case BodyEquipLayer.None:
                score += SumRevealingBy(BodyEquipLayer.Skin);
                goto case BodyEquipLayer.Skin;
            case BodyEquipLayer.Skin:
                score += SumRevealingBy(BodyEquipLayer.Inner);
                goto case BodyEquipLayer.Inner;
            case BodyEquipLayer.Inner:
                score += SumRevealingBy(BodyEquipLayer.Outer);
                goto case BodyEquipLayer.Outer;
            case BodyEquipLayer.Outer:
            /*    score += SumRevealingBy(BodyEquipLayer.Shell);
                goto case BodyEquipLayer.Shell;
            case BodyEquipLayer.Shell:*/
                break;
        }
        return score;
    }

    private int SumRevealingBy(BodyEquipLayer layer)
    {
        int score = 0;
        foreach (BodyPartEquipSlot slot in this.availableSlots)
        {

            string Tuple = layer.ToString() + "||" + slot.ToString();
            if (contentsIndex.TryGetValue(Tuple, out var content) && content != -1)
            {
                var comp = ResolveEquip(content)?.GetComp_Equippable();
                if (comp != null) score += (int)comp.revealing;
            }
            if (coversIndex.TryGetValue(Tuple, out var cover) && cover != -1)
            {
                var comp = ResolveEquip(cover)?.GetComp_Equippable();
                if (comp != null) score += (int)comp.revealing;
            }
        }
        return score;
    }
}
