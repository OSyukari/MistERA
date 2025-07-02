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
            else if (inter.hasTag("breast") && !Owner.Template.isMale) { }
            else
            {
                this.internals.Add(inter);
            }
        }

        contentsIndex = new Dictionary<string, int>();

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

    //List<Item_Instance> equippedItems;
    [SerializeField][JsonProperty] Dictionary<string, int> contentsIndex;

    /// <summary>
    /// Return -1 fail, return 0 success return 1+ swapped gear
    /// </summary>
    /// <param name="itemRefID"></param>
    /// <param name="forceEquip"></param>
    /// <returns></returns>
    public int EquipItem(int itemRefID, bool forceEquip = false)
    {
        Item_Instance item = scr_System_CampaignManager.current.FindItemInstanceByID(itemRefID);

        if (item != null)
        {
            ItemComponent_Equippable comp = item.GetComp("ItemComponent_Equippable") as ItemComponent_Equippable;
            if (comp != null)
            {

                string Tuple = comp.equipLayer.ToString() + "||" + comp.equipSlot.ToString();
                /// CODE START
                if (contentsIndex.ContainsKey(Tuple))
                {
                    if (contentsIndex[Tuple] == -1)
                    {
                        contentsIndex[Tuple] = itemRefID;
                        // equip success
#if UNITY_EDITOR
                        if (scr_System_CentralControl.current.LogPrefs.DLog_Equipping) Debug.Log($"{Owner.FirstName} successfully equipped {item.DisplayName} at {DisplayName}");
#endif
                        return 0;
                    }
                    else if (contentsIndex[Tuple] != -1 && forceEquip == true)
                    {
                        int returnval = contentsIndex[Tuple];
                        contentsIndex[Tuple] = itemRefID;

                        Owner.UnequipItem(returnval);
                        var unequipped = scr_System_CampaignManager.current.FindItemInstanceByID(returnval);
                        // replace equipped
#if UNITY_EDITOR
                        if (scr_System_CentralControl.current.LogPrefs.DLog_Equipping) Debug.Log($"{Owner.FirstName} successfully equipped {item.DisplayName} at {DisplayName}, unequipping item {unequipped.DisplayName}");
#endif
                        return returnval;
                    }
                }
                else
                {
#if UNITY_EDITOR
                    if (scr_System_CentralControl.current.LogPrefs.DLog_Equipping) Debug.LogError($"bodypart {DisplayName} does not contain index {Tuple} in {String.Join(",", contentsIndex.Keys)}, checking childrens");
#endif
                    // equip failed ask next
                    foreach (BodyInternal_Instance i in this.internals)
                    {
#if UNITY_EDITOR
                        if (scr_System_CentralControl.current.LogPrefs.DLog_Equipping) Debug.LogError($"trying equip item on children {i.DisplayName}");
#endif
                        int j = i.EquipItem(itemRefID, forceEquip);
                        if (j > -1) return j;
                    }
                }
                /// CODE END
            }
            else
            {
                Debug.LogError($"{Owner.FirstName} failed equipping {item.DisplayName}, cannot find item equippable comp");
            }
        }
        else
        {
            Debug.LogError($"{Owner.FirstName} failed equipping {itemRefID}, cannot find item by ref");
        }
        return -1;
    }

    public List<int> EquipItemDirect(int itemRefID, BodyPartEquipSlot slot, BodyEquipLayer layer)
    {
        List<int> list = new List<int>();
        int returnval = -1;

        string Tuple = layer.ToString() + "||" + slot.ToString();
        if (contentsIndex.ContainsKey(Tuple))
        {
            if (contentsIndex[Tuple] == -1)
            {
                contentsIndex[Tuple] = itemRefID;
            }
            else
            {
                returnval = contentsIndex[Tuple];
                contentsIndex[Tuple] = itemRefID;

                Owner.UnequipItem(returnval);


                list.Add(returnval);
            }

        }
        foreach (BodyInternal_Instance i in this.internals)
        {
            //list.Add(i.EquipItemDirect(itemRefID, slot, layer));
        }

        return list;
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

        if (returnVal) return returnVal;

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

    public int GetEquip(BodyEquipLayer i, BodyPartEquipSlot j)
    {

        string Tuple = i.ToString() + "||" + j.ToString();
        if (contentsIndex.ContainsKey(Tuple)) return contentsIndex[Tuple];
        else return -1;
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
