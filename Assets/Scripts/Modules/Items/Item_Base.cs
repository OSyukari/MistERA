using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using Newtonsoft.Json;


[System.Serializable]
public class Index_Item_Base : I_IndexHasID, I_NeedLateInitialize, I_IndexMergeable, I_SerializationCallbackReceiver, I_RemoveElemByTag, I_RemoveNSFW
{
    [JsonProperty] protected List<Item_Base> list = new List<Item_Base>();
    [JsonIgnore] public List<Item_Base> List { get { return this.ID_Dictionary.Values.ToList(); } }
    public void MergeWith(I_IndexMergeable list){
        var l = list as Index_Item_Base;
        if (l == null) return;
        else if (l.list == null) return;
        else
        {
            this.list.AddRange(l.list);
        }
    }

    Dictionary<string, Item_Base> ID_Dictionary = new Dictionary<string, Item_Base>();
    public void RegisterAllID(List<string> s)
    {
        s.Add("Index_Item_Base : registering ID with list length [" + list.Count + "]");

        foreach (Item_Base o in this.list)
        {
            if (o.Tags.Contains("do_not_use")) continue;
            ID_Dictionary.Add(o.ID, o);
        }
    }
    public Item_Base GetByID(string id) { return ID_Dictionary.ContainsKey(id) ? ID_Dictionary[id] : null; }

    /// <summary>
    /// this should run after all calls to appendlist
    /// </summary>
    public void LateInitialize()
    {
        // generate packaged item def for each package_able food item
        List<Item_Base> newItems = new List<Item_Base>();

        foreach(var item in this.list)
        {

            if (item.isValid)

            /* Packaging items, not used right now */
            // if item can be packaged
            if (item.canBePackaged)
            {
                //continue;

                var serializedParent = JsonConvert.SerializeObject(item, Masterlist_Items.Instance.SerializerSettings);
                Item_Base newItem = JsonConvert.DeserializeObject<Item_Base>(serializedParent, Masterlist_Items.Instance.SerializerSettings);
                newItem.canBePackaged = false;
                newItem.id = item.ID + "_packaged";
                newItem.tooltip = "Packaged " + newItem.displayName + ", unpack to get the original item.";
                newItem.displayName = "(pacakged)" + newItem.displayName;
                if (newItem.Tags.Contains("food_meal"))
                {
                    newItem.Tags.Remove("food_meal");
                    newItem.Tags.Add("food_meal_packaged");
                }

                for (int i = newItem.itemComps_Template.Count - 1; i >= 0; i--)
                {
                    // update every recipe output item to new item
                    if (newItem.itemComps_Template[i].compType == "ItemComponent_Craftable")
                    {
                        foreach (var recipe in newItem.itemComps_Template[i].comp_Craftable.recipes)
                        {
                            if (recipe.outputItemBaseID == item.ID) recipe.outputItemBaseID = newItem.ID;
                        }
                        //
                        Masterlist_Items.Instance.AddCraftingRecipe(newItem.itemComps_Template[i].comp_Craftable.recipes);
                    }
                    else if (newItem.itemComps_Template[i].compType == "ItemComponent_Degradable")
                    {
                        newItem.itemComps_Template.RemoveAt(i);
                        continue;
                    }

                }

                newItems.Add(newItem);
            }

        }

        foreach (var i in newItems)
        {
            list.Add(i);
            ID_Dictionary.Add(i.id, i);
            //for (int ii = i.itemComps_Template.Count - 1; ii >= 0; ii--)
        }

      //  list = list.Where(x => !x.Tags.Contains("do_not_use")).ToList();
    }

    public void OnAfterDeserialize()
    {
        for(int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i].Tags.Contains("do_not_use")) list.RemoveAt(i);
            else list[i].OnAfterDeserialize();
        }
    }

    public void RemoveElemByTag(string tag)
    {
        this.list.RemoveAll(x => x.Tags.Contains(tag));
    }
    public void RemoveNSFW()
    {
        this.list.RemoveAll(x => x.Equippable && x.GetCompTemplateByID("ItemComponent_Equippable").comp_Equippable.equipLayer == BodyEquipLayer.Skin);
    }
}



[System.Serializable]
public class Item_Base
{
    // serialize interface ? no
    public string id = "";
    [JsonIgnore] public string ID { get { return id; } }

    public string displayName = "";
    [JsonIgnore] public string DisplayName { get { return LocalizeDictionary.QueryThenParse(id, displayName); } }

    public string tooltip = "";
    [JsonIgnore]
    public string Tooltip
    {
        get
        {
            if (_tooltipCache == "")
            {
                var compTooltips = new List<string>();
                foreach (var comp in this.itemComps_Template) if (comp.Tooltip.Length > 0) compTooltips.Add(comp.Tooltip);
                _tooltipCache = LocalizeDictionary.QueryThenParse(id + "_tooltip", tooltip) + (compTooltips.Count > 0 ? "\n\n"+ String.Join("\n", compTooltips) : "");
            }
            return _tooltipCache;
        }
    }

    string _tooltipCache = "";



    //public bool noDisplay = false;
    public float value = 0;
    /*
    [NonSerialized] private List<ItemComponent_Base> itemComps = new List<ItemComponent_Base>();
    public List<ItemComponent_Base> ItemComps { get { return itemComps; } }
    */
    [JsonIgnore][NonSerialized] public bool canBePackaged = false;
    public int cleanlinessMod = 0;
    public List<string> Tags = new List<string>();
    public ItemComponentTemplate GetCompTemplateByID(string id)
    {
        return itemComps_Template.Find(x => x.compType == id);
    }

    /*
    public List<string> givesJobID = new List<string>();
    [JsonIgnore] public List<string> GivesJobID { get { return givesJobID; } }
    [JsonIgnore]
    public bool IsJobGiver
    {
        get
        {
            if (givesJobID.Count < 1 || (givesJobID.Count == 1 && givesJobID[0] == "")) return false;
            else return true;
        }
    }*/

    public List<ItemComponentTemplate> itemComps_Template = new List<ItemComponentTemplate>();


    [JsonIgnore] public bool isTokenItem { get { 
            return this.Tags.Contains("food_meal") && this.itemComps_Template.Find(x => x.compType == "ItemComponent_Degradable") != null; 
        } }


    public void OnAfterDeserialize()
    {

        this.stackable = true;

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
                    Masterlist_Items.Instance.AddCraftingRecipe(i.comp_Craftable.recipes); break;
                case "ItemComponent_Harvestable":
                    Masterlist_Items.Instance.AddFarmRecipe(i.comp_Harvestable); break;
                default: break;

            }

            this.stackable = i.stackable && this.stackable;
        }

    }

    [JsonIgnore] public bool Equippable { get { return itemComps_Template.Exists(x => x.compType == "ItemComponent_Equippable"); } }

    [NonSerialized] private bool stackable;
    [JsonIgnore] public bool Stackable { get { return stackable; } }

    private bool valid = true;
    [JsonIgnore] public bool isValid { get { 
            
            
            return valid; 
        } }

    [JsonIgnore]
    public bool isWeapon { get
        {
            return itemComps_Template.Exists(x => x.compType == "ItemComponent_Weapon");
        } }


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
    public string compType = "";


    public int currentGrowth = 0; //ItemComponent_Harvestable
    public int minuteCounter = 0; //ItemComponent_Degradable


}

[System.Serializable]
public enum MoveType
{
    None,
    Swing, Thrust, Impact,
    Gunshot
}

[System.Serializable]
public enum DamageType
{
    None, Untyped,
    Pierce, Blunt, Slash,
    Heat, Cold, Chemical
}

[System.Serializable]
public class AttackInstance
{
    public MoveType moveType = MoveType.None;
    public List<DamageType> damageTypes;
    public float damageAmount;
    public float tracking;

    [JsonIgnore]
    public List<string> attackSpecs = new List<string>();
}


[System.Serializable]
public class ItemRequirement
{
    public List<string> requireTags = new List<string>();

    [JsonIgnore] public bool isActive { get { return requireTags.Count > 0; } }

    public bool Validate(Item_Base item)
    {
        return Utility.ListContainsStrict(item.Tags,requireTags);
    }
    public bool Validate(List<string> tags)
    {
        return Utility.ListContainsStrict(tags, requireTags);
    }

    public bool Merge(ItemRequirement req)
    {
        if (this.requireTags.Count == 0 && req.requireTags.Count != 0) return false;
        if (this.requireTags.Count != 0 && req.requireTags.Count == 0) return false;
        if (Utility.ListContainsStrict(this.requireTags, req.requireTags)) return true;
        else if (Utility.ListContainsStrict(req.requireTags, this.requireTags))
        {
            this.requireTags.AddRange(req.requireTags);
            this.requireTags = this.requireTags.Distinct().ToList();
            return true;
        }
        return false;
    }

    [JsonIgnore]
    public string Tooltip
    {
        get
        {
            return LocalizeDictionary.QueryThenParse("ui_combatAction_tooltip_itemReq").Replace("$kwds$", String.Join("|", requireTags));
        }
    }
}