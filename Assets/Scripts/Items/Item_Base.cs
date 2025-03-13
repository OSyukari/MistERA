using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using Newtonsoft.Json;

[System.Serializable]
public class Index_Item_Base : I_IndexHasID, I_IndexHasTooltip, I_NeedLateInitialize, I_IndexMergeable
{
    [SerializeField] public List<Item_Base> list = new List<Item_Base>();

    public void MergeWith(I_IndexMergeable list){
        var l = list as Index_Item_Base;
        if (l == null) return;
        else if (l.list == null) return;
        else
        {
            this.list.AddRange(l.list);
        }
    }

    public void RegisterAllID()
    {
        Debug.Log("Index_Item_Base : registering ID with list length [" + list.Count + "]");

        foreach (Item_Base o in this.list)
        {
            scr_System_Serializer.current.RegisterIDtoLib(o.ID, o);
        }
    }
    public void RegisterAllTooltip()
    {
        foreach (Item_Base o in this.list)
        {
            scr_System_tooltipDictionary.current.AddEntry(o.ID, o.tooltip);
        }
    }

    public void AppendList(Index_Item_Base list)
    {
        this.list.AddRange(list.list);
    }

    /// <summary>
    /// this should run after all calls to appendlist
    /// </summary>
    public void LateInitialize()
    {
        // generate packaged item def for each package_able food item
        List<Item_Base> newItems = new List<Item_Base>();
        foreach(var item in this.list)
        {
            // if item can be packaged
            if (!item.canBePackaged) continue;
            //continue;
            
            var serializedParent = JsonConvert.SerializeObject(item, Utility.SerializerSettings);
            Item_Base newItem = JsonConvert.DeserializeObject<Item_Base>(serializedParent, Utility.SerializerSettings);
            newItem.canBePackaged = false;
            newItem.id = item.ID + "_packaged";
            newItem.tooltip = "Packaged " + newItem.displayName + ", unpack to get the original item.";
            newItem.displayName = "(pacakged)"+ newItem.displayName;
            if (newItem.Tags.Contains("food_meal")) {
                newItem.Tags.Remove("food_meal");
                newItem.Tags.Add("food_meal_packaged");
            }
            
            for (int i = newItem.itemComps_Template.Count -1; i >= 0; i--)
            {
                // update every recipe output item to new item
                if (newItem.itemComps_Template[i].compType == "ItemComponent_Craftable")
                {
                    foreach(var recipe in newItem.itemComps_Template[i].comp_Craftable.recipes)
                    {
                        if (recipe.outputItemBaseID == item.ID) recipe.outputItemBaseID = newItem.ID;
                    }
                    //
                    scr_System_Serializer.current.AddCraftingRecipe(newItem.itemComps_Template[i].comp_Craftable.recipes);
                }
                else if (newItem.itemComps_Template[i].compType == "ItemComponent_Degradable")
                {
                    newItem.itemComps_Template.RemoveAt(i);
                    continue;
                }

            }

            newItems.Add(newItem);
        }

        foreach(var i in newItems)
        {
            scr_System_Serializer.current.RegisterIDtoLib(i.ID, i);
            scr_System_tooltipDictionary.current.AddEntry(i.ID, i.tooltip);
            list.Add(i);
            for (int ii = i.itemComps_Template.Count - 1; ii >= 0; ii--)
            {
               //if (i.itemComps_Template[ii].comp_Craftable != null) scr_System_Serializer.current.AddCraftingRecipe(i.itemComps_Template[ii].comp_Craftable.recipes);
            }
        }
    }
}



[System.Serializable]
public class Item_Base : ISerializationCallbackReceiver
{
    // serialize interface ? no
    [SerializeField] public string id = "";
    public string ID { get { return id; } }

    [SerializeField] public string displayName = "";
    public string DisplayName { get { return scr_System_Serializer.current.Dictionary.QueryThenParse(id, displayName); } }

    [SerializeField] public string tooltip = "";
    public string Tooltip { get { return tooltip; } }

    public bool noDisplay = false;
    /*
    [NonSerialized] private List<ItemComponent_Base> itemComps = new List<ItemComponent_Base>();
    public List<ItemComponent_Base> ItemComps { get { return itemComps; } }
    */
    public bool canBePackaged = false;
    [SerializeField] public int cleanlinessMod = 0;
    [SerializeField] public List<string> Tags = new List<string>();
    public ItemComponentTemplate GetCompTemplateByID(string id)
    {
        return itemComps_Template.Find(x => x.compType == id);
    }

    [SerializeField] public List<string> givesJobID = new List<string>();
    public List<string> GivesJobID { get { return givesJobID; } }
    public bool IsJobGiver
    {
        get
        {
            if (givesJobID.Count < 1 || (givesJobID.Count == 1 && givesJobID[0] == "")) return false;
            else return true;
        }
    }

    [SerializeField] public List<ItemComponentTemplate> itemComps_Template = new List<ItemComponentTemplate>();


    public bool isTokenItem = false;

    void ISerializationCallbackReceiver.OnBeforeSerialize()
    {

    }

    void ISerializationCallbackReceiver.OnAfterDeserialize()
    {

        this.stackable = true;

        // determine token
        // can set istokenitem true and forget about this part
        if (this.Tags.Contains("food_meal") && this.itemComps_Template.Find(x=>x.compType == "ItemComponent_Degradable") != null) this.isTokenItem = true;

        foreach(ItemComponentTemplate i in itemComps_Template)
        {

            if (itemComps_Template.FindAll(x=>x.compType == i.compType).Count > 1)
            {
                valid = false;
                Debug.LogError("Error serializing Item [" + id + "][" + displayName + "], item has multiple ItemComp ["+i.compType+"] of same type.");
            }

            switch (i.compType)
            {
                case "ItemComponent_Equippable":
                    //itemComps.Add(new ItemComponent_Equippable(i.equipSlot, i.coverSlot, i.equipLayer, i.revealing, i.equipCount));
                    if (i.comp_Equippable == null)
                    {
                        valid = false;
                        Debug.LogError("Error serializing Item [" + id + "][" + displayName + "], item has ItemComponent_Equippable but missing comp parameters.");
                    }
                    break;
                case "ItemComponent_Armor":
                    //itemComps.Add(new ItemComponent_Armor(i.health, i.hardness));
                    break;
                case "ItemComponent_Degradable":
                    //itemComps.Add(new ItemComponent_Degradable(i.minutesTillDestroy, i.daysTillDestroy)); break;
                    break;
                case "ItemComponent_Ingestible":
                    //itemComps.Add(new ItemComponent_Ingestible(i.Ingestible.ingestMethod, i.Ingestible.amount, i.Ingestible.modifiers));
                    if (i.comp_Ingestible == null)
                    {
                        valid = false;
                        Debug.LogError("Error serializing Item [" + id + "][" + displayName + "], item has ItemComponent_Ingestible but missing comp parameters.");
                    }
                        break;
                case "ItemComponent_Craftable":
                    scr_System_Serializer.current.AddCraftingRecipe(i.comp_Craftable.recipes); break;
                case "ItemComponent_Harvestable":
                    scr_System_Serializer.current.AddFarmRecipe(i.comp_Harvestable); break;
                default: break;

            }

            this.stackable = i.stackable && this.stackable;
        }

    }

    public bool Equippable { get { return itemComps_Template.Exists(x => x.compType == "ItemComponent_Equippable"); } }

    [NonSerialized] private bool stackable;
    public bool Stackable { get { return stackable; } }

    private bool valid = true;
    public bool isValid { get { 
            
            
            return valid; 
        } }
    /*
    [SerializeField] float beauty = 0f;
    public float Beauty { get { return beauty; } }
    public bool IsBeautyAffecting
    {get{
        if (beauty == 0f) return false;
        else return true; 
    }
    }*/


    // SortingOption : item type tag

    // Comp_Harvestable : harvest what, use what skill, requirement, maxamount, regrowth
    //  jobgiver harvestable

    // Comp_Food : 
    //  contribute to how much food point, contribute to which food type

    // Comp_Rottable (how long)

    // Comp_InventoryItem : 
    // Weight (kg)

    // Comp_MeleeWeapon
    //  required stat, damage, weapon type

    // Comp_RangeWeapon :
    // weapon type, use ammo, range, required stat, damage



    // Comp_Equippable

    // Comp_Usable

    // operating thought: exist in settlement, exist in inventory, exist in room, interactingwith
}


[System.Serializable]
public class ItemComponent_SerializedData
{
    public string compType;


    public int currentGrowth; //ItemComponent_Harvestable
    public int minuteCounter; //ItemComponent_Degradable


}


public enum DamageType
{
    Untyped,
    Pierce, Blunt, Slash,
    Heat, Cold, Chemical
}


