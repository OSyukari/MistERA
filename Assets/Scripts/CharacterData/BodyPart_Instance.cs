using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
using System.Linq;


[System.Serializable]
public class BodyPart_Instance
{
    [SerializeField][JsonProperty] protected string baseID = "";

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

    Dictionary<Item_Instance, List<CombatAction>> _combatActions = null;

    [JsonIgnore]
    public Dictionary<Item_Instance, List<CombatAction>> CombatActions { get
        {
            if (_combatActions == null)
            {
                _combatActions = new Dictionary<Item_Instance, List<CombatAction>>();
                if (this.equipLayers.Contains(BodyEquipLayer.Outer))
                {
                    foreach (var slot in this.availableSlots)
                    {
                        var refID = this.GetEquip(BodyEquipLayer.Outer, slot);
                        if (refID == -1) continue;
                        var item = scr_System_CampaignManager.current.FindItemInstanceByID(refID);
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

        foreach (var i in internals) i.ReEstablishParent(c);
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
            if (!inter.Initialize(s, this.Owner)) continue;
            else if ((inter.hasTag("vagina") || inter.hasTag("womb") || inter.hasTag("urethra")) && !Owner.Template.isFemale) { }
            //else if (inter.hasTag("anus") && Owner.Template.Size_A.ID == "trait_Size_A_none") { }
            else if (inter.hasTag("penis") && !Owner.Template.isMale) { }
            else if (inter.hasTag("clit") && Owner.Template.isMale) { }
            else if (inter.hasTag("breast") && !Owner.Template.isFemale) { }
            else
            {
                this.internals.Add(inter);
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
            }
        }
    }

    private int ownerRefID = -1;
    private Character_Trainable owner = null;
    private Character_Trainable Owner
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

    [JsonIgnore] public List<int> EquippedRefIDs { get
        {
            var v = contentsIndex.Values.ToList();
            v.RemoveAll(x => x == -1);
            return v;
        } }

    List<Item_Instance> _equippedItems;
    [JsonIgnore] 
    public List<Item_Instance> EquippedItems { get
        {
            if (_equippedItems == null)
            {
                _equippedItems = new List<Item_Instance>();
                foreach (var i in EquippedRefIDs) _equippedItems.Add(scr_System_CampaignManager.current.FindItemInstanceByID(i));
            }
            return _equippedItems;
        } }

    //List<Item_Instance> equippedItems;
    [SerializeField][JsonProperty] Dictionary<string, int> contentsIndex = new Dictionary<string, int>();


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
            if (contentsIndex.ContainsKey(Tuple))
            {
                if (contentsIndex[Tuple] == -1)
                {
                    contentsIndex[Tuple] = item.RefID;
                    // equip success
                    ClearEquipCache();
                    equipped = true;
                    slots.RemoveAt(i);
                }
                else if (contentsIndex[Tuple] != -1 && contentsIndex[Tuple] != item.RefID && forceEquip == true)
                {
                    var returnVal = contentsIndex[Tuple];
                    contentsIndex[Tuple] = item.RefID;

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

    protected void ClearEquipCache()
    {
        _equippedItems = null;
        _combatActions = null;
    }

    public bool UnequipItem(int itemRefID)
    {
        bool returnVal = false;
        List<string> removeList = new List<string>();
        /// CODE START
        if (contentsIndex.ContainsValue(itemRefID))
        {
            foreach (var pair in contentsIndex)
            {
                if (pair.Value == itemRefID)
                {
                    removeList.Add(pair.Key);
                    //contentsIndex[pair.Key] = -1;
                    //equippedRefIDs.Remove(itemRefID);
                    returnVal = true;
                }
            }
        }

        foreach(var i in removeList)
        {
            contentsIndex[i] = -1;
            //returnVal = true;
        }


        if (returnVal)
        {
            ClearEquipCache();
            return returnVal;
        }

        foreach (BodyInternal_Instance i in this.internals)
        {
            returnVal = i.UnequipItem(itemRefID) || returnVal;
        }
        /// CODE END
        return returnVal;
    }

    public bool HasEquipByFilter(BodyEquipLayer layer, int revealingScoreFilter = -1)
    {
        if (!this.equipLayers.Contains(layer)) return false;
        foreach(BodyPartEquipSlot slot in this.availableSlots)
        {
            var equip = this.GetEquip(layer, slot);
            if (equip > -1 && (int)scr_System_CampaignManager.current.FindItemInstanceByID(equip).GetComp_Equippable().revealing >= revealingScoreFilter) return true;
        }
        return false;
    }

    public bool TryGetEquip(out int value, BodyEquipLayer i, BodyPartEquipSlot j)
    {
        value = GetEquip(i, j);
        return value != -1;
    }

    public int GetEquip(BodyEquipLayer i, BodyPartEquipSlot j)
    {

        string Tuple = i.ToString() + "||" + j.ToString();
        if (contentsIndex.ContainsKey(Tuple)) return contentsIndex[Tuple];
        else return -1;
    }

    public Item_Instance GetRandArmor(BodyPartEquipSlot slot)
    {
        int itemID = -1;
        if (TryGetEquip(out itemID, BodyEquipLayer.Outer, slot))
        {
            var item = scr_System_CampaignManager.current.FindItemInstanceByID(itemID);
            if (item.Comp_Defense != null) return item;
        }
        if (TryGetEquip(out itemID, BodyEquipLayer.Inner, slot))
        {
            var item = scr_System_CampaignManager.current.FindItemInstanceByID(itemID);
            if (item.Comp_Defense != null) return item;
        }
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
            if (contentsIndex.ContainsKey(Tuple) && contentsIndex[Tuple] > 0)
                score += (int)(scr_System_CampaignManager.current.FindItemInstanceByID(contentsIndex[Tuple]).GetComp("ItemComponent_Equippable") as ItemComponent_Equippable).revealing;

        }
        return score;
    }
}
