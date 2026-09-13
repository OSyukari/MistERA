using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;


public interface I_CombatItem
{
    public List<string> ItemTags { get; }
    public string DisplayName { get; }
    public string Tooltip {  get; }

    public ItemComponent_Weapon Comp_Weapon { get; }
    public ItemComponent_Defense Comp_Defense { get; }
}

/// <summary>
/// Instantiated: World Gen, Cum
/// Destroyed: Digest, MergeItem
/// </summary>
public class Item_Instance : IDisposable, I_Disposable, I_CombatItem
{
    [JsonProperty] protected string parentID = "";   // stay consistent with parent
    [JsonIgnore] public string BaseID { get { return parentID; } }
    protected Item_Base _base = null;
    [JsonIgnore] public Item_Base Base { get {
            if (_base == null && BaseID != "") _base = scr_System_Serializer.current.GetByNameOrID_Item_Base(BaseID);
            return _base; } }

    /// <summary>
    /// Currency stacks in particular can grow huge (stack-merging in Inventory.AddItem, wallets, etc.),
    /// so this adds via long math and clamps to [0, int.MaxValue] instead of letting the int addition
    /// silently wrap around (e.g. a fortune flipping negative).
    /// </summary>
    public void ModCount(int count)
    {
        long result = (long)this.count + count;
        if (result > int.MaxValue) result = int.MaxValue;
        else if (result < 0) result = 0;
        this.count = (int)result;
    }

    //[JsonIgnore]
    //List<CombatAction> _cachedCombatActions = null;

    [JsonIgnore]
    public bool isVirtualGood
    {
        get
        {
            return this.Tags.Contains("retail_digital");
        }
    }

    [JsonIgnore]
    public List<string> ItemTags { get { return this.Tags; } }

    [JsonIgnore]
    public List<CombatAction> CombatActions 
    {
        get
        {
            return scr_System_Serializer.current.GetCombatActions(this.Base);
        }
    }

    public void SetCount(int count) { this.count = count < 0 ? 0 : count; }
    [JsonProperty] protected int count;
    [JsonIgnore] public int Count { get { return count - markTokenUsed; } }
    [JsonIgnore] public int InnerCount { get { return count; } }

    public virtual bool canStackWith(Item_Instance item)
    {
        if (this.isToken != item.isToken) return false;
        if (this.Stackable != item.Stackable) return false;
        if (this.BaseID != item.BaseID) return false;
        if (this.Count == 0 || item.Count == 0) return false;
        if (this.nameOverwrite != item.nameOverwrite) return false;

        foreach(var comp in this.compInstances)
        {
            if (comp == null) continue;
            var comp2 = item.compInstances.Find(x => x.CompType == comp.CompType);
            if (comp2 == null || !comp.canMergeWith(comp2)) return false;
        }
        return true;
    }


    [JsonProperty] protected int referenceID = -1;
    [JsonIgnore] public int RefID { get { return referenceID; } }
    [JsonIgnore] public string Tooltip { get { 
            
            List<string> compTooltips = new List<string>();
            foreach (var c in Comps)
            {
                var cc = c.Tooltip;
                if (c is ItemComponent_Weapon)
                {
                    var template = (c as ItemComponent_Weapon).Comp;
                    var execMove = template.ExecutionMove == "" ? null : Masterlist_Items.GetByID(template.ExecutionMove);
                    var execName = execMove == null ? "" : LocalizeDictionary.QueryThenParse("ItemComponent_Weapon_tooltip_execution").Replace("$name$", execMove.DisplayName);
                    cc.Replace("$execution$", execName);
                }
                if (cc.Length > 0) compTooltips.Add(cc);
            }
            return LocalizeDictionary.QueryThenParse("Item_Instance_Tooltip")
                .Replace("$tags$", $"[{String.Join("|", this.Tags)}]")
                .Replace("$parent$", Parent.Tooltip)
                .Replace("$comps$", compTooltips.Count > 0 ? $"\n\n"+String.Join("\n\n", compTooltips) : ""); } }
    [JsonIgnore] public bool Stackable { get { return Parent.Stackable && this.compInstances.Count < 1; } }
    [JsonIgnore] public int Cleanliness { get { return Parent.cleanlinessMod; } }
    [JsonIgnore] public bool isTrash { get { return Parent.Tags.Contains("trash"); } }
    [JsonIgnore] public bool Equippable { get { return Parent.Equippable; } }

    [JsonIgnore] public bool isToken { get { return Parent.isTokenItem; } }
    [JsonIgnore] public bool isFoodAdditive { get { return Parent.isFoodAdditive; } }
    [JsonIgnore] public bool isFoodConsumable  { get { return Parent.isFoodConsumable; } }
    /// <summary>
    /// Print name only without count
    /// </summary>
    [JsonIgnore] public string DisplayName
    {
        get
        {
            if (nameOverwrite != "") return LocalizeDictionary.QueryThenParse(nameOverwrite, nameOverwrite);
            else return Parent.DisplayName;
        }
    }

    [JsonIgnore] public bool Displayable { get { return !this.isToken || this.Count > 0; } }

    [JsonIgnore] public bool canBeSold
    {
        get
        {
            if (this.isToken) return false;
            foreach (var c in Comps) if (!c.CanBeSold) return false;
            return true;
        }
    }

    [JsonIgnore] public float ValuePerItem
    {
        get
        {
            float value = Parent.value;
            foreach (var c in Comps) c.ValueMod(ref value);
            return value;
        }
    }

    /// <summary>
    /// Sales-volume multiplier derived from quality vs. the item's fixed ValuePerItem. Starts at
    /// neutral 1f; each component adds its own deviation (see ItemComponent_Base.AddQualityMod).
    /// Items with no quality dimension (e.g. non-recordings) stay at 1f.
    /// </summary>
    [JsonIgnore] public float QualityModifier
    {
        get
        {
            float value = 1f;
            float parentvalue = this.ValuePerItem;
            foreach (var c in Comps) c.AddQualityMod(ref value, parentvalue);
            return value;
        }
    }

    /// <summary>
    /// print name and count according to item type
    /// </summary>
    /// <returns></returns>
    public virtual string Print()
    {
        if (this._cache_printfull == "") this._cache_printfull = isCurrency ?
                          LocalizeDictionary.QueryThenParse("management_jobpost_payout_currency")
                        : LocalizeDictionary.QueryThenParse("management_jobpost_payout_item");


        return this._cache_printfull.Replace("$item$", DisplayName).Replace("$count$", CountString);
    }

    [JsonIgnore] public string CountString { get
        {
            if (isCurrency) return Count >= 10000000 ? (((int)(Count / 1000)).ToString() + "M") : (Count >= 10000 ? (((int)(Count/1000)).ToString()+"K") : (Count.ToString())); 
            else return Count.ToString();
        } }
    [JsonIgnore] public bool isCurrency { get { return this.Tags.Contains("item_money"); } }

    protected string _cache_printfull = "";

    /// <summary>
    /// Identity used to group sale listings for otherwise-identical items (see SalesManager.ItemMatch).
    /// Equal to BaseID, plus each component's GetRetailID() contribution (most contribute nothing).
    /// </summary>
    protected string _cache_retailID = "";
    [JsonIgnore] public string RetailID
    {
        get
        {
            if (this._cache_retailID == "")
            {
                var s = BaseID;
                foreach (var c in Comps) s += c.GetRetailID();
                this._cache_retailID = s;
            }
            return this._cache_retailID;
        }
    }

    public string nameOverwrite = "";
    [JsonProperty] protected List<ItemComponent_Base> compInstances = new List<ItemComponent_Base>();
    protected List<ItemComponent_Base> compInstances_nonSerialized = new List<ItemComponent_Base>();
    [JsonIgnore] public List<ItemComponent_Base> Comps { get
        {
            var v = new List<ItemComponent_Base>();
            v.AddRange(compInstances);
            v.AddRange(compInstances_nonSerialized);
            return v;
        } }

    [JsonIgnore] protected Item_Base parent = null;
    [JsonIgnore] protected Item_Base Parent
    {
        get
        {
            if (parent == null) parent = scr_System_Serializer.current.GetByNameOrID_Item_Base(BaseID);
            return parent;
        }
    }

    private List<string> _tagsCache = null;
    [JsonIgnore] public List<string> Tags
    {
        get
        {
            if (_tagsCache == null)
            {
                var v = new List<string>();
                var seen = new HashSet<string>();
                foreach (var tag in Parent.Tags) if (seen.Add(tag)) v.Add(tag);
                foreach (var c in Comps)
                {
                    var t = c.GetTags();
                    if (t == null) continue;
                    foreach (var tag in t) if (seen.Add(tag)) v.Add(tag);
                }
                _tagsCache = v;
            }
            return _tagsCache;
        }
    }

    /// <summary>
    /// Call after anything that changes what Tags would compute - swapping/adding an ItemComponent,
    /// or mutating a component's internal state that its GetTags() depends on (e.g. SetFurnitureBase).
    /// </summary>
    public void InvalidateTagsCache()
    {
        _tagsCache = null;
    }

    public ItemComponent_Ingestible GetComp_Ingestible() { return GetComp("ItemComponent_Ingestible") as ItemComponent_Ingestible; }

    public ItemComponent_Equippable GetComp_Equippable() { return GetComp("ItemComponent_Equippable") as ItemComponent_Equippable; }

    ItemComponent_Defense _defense = null;
    [JsonIgnore]
    public ItemComponent_Defense Comp_Defense { get {
            if (_defense == null)
            {
                _defense = GetComp("ItemComponent_Defense") as ItemComponent_Defense;
            }
            return _defense;
        } }
    ItemComponent_Weapon _weapon = null;
    [JsonIgnore]
    public ItemComponent_Weapon Comp_Weapon
    {
        get
        {
            if (_weapon == null)
            {
                _weapon = GetComp("ItemComponent_Weapon") as ItemComponent_Weapon;
            }
            return _weapon;
        }
    }
    ItemComponent_Records _records = null;
    [JsonIgnore]
    public ItemComponent_Records Comp_Records
    {
        get
        {
            if (_records == null)
            {
                _records = GetComp("ItemComponent_Records") as ItemComponent_Records;
            }
            return _records;
        }
    }
    ItemComponent_Knowledges _knowledge = null;
    [JsonIgnore]
    public ItemComponent_Knowledges Comp_Knowledge
    {
        get
        {
            if (_knowledge == null)
            {
                _knowledge = GetComp("ItemComponent_Knowledges") as ItemComponent_Knowledges;
            }
            return _knowledge;
        }
    }
    public ItemComponent_Base GetComp(string name)
    {
        return this.Comps.Find(x=>x.CompType == name);
    }

    public void RegisterItem(int uid)
    {
        this.referenceID = uid;
    }

    public int UnregisterItem()
    {
        int i = this.referenceID;
        this.referenceID = -1;
        return i;
    }

    /// <summary>
    /// USED FOR SERIALIZER DO NOT CALL THIS MANUALLY
    /// </summary>
    public Item_Instance() {
    

    }
    public Item_Instance(string parentID, string nameOverwrite, int count = 1)
    {
        this.parentID = parentID;
        foreach (var comp in Parent.itemComps_Template)
        {
            var c = comp.Instantiate(parent);
            if (c is null) continue;
            if (c.Stackable) compInstances_nonSerialized.Add(comp.Instantiate(parent));
            else compInstances.Add(comp.Instantiate(parent));
        }

        this.nameOverwrite = nameOverwrite;
        this.count = count;
    }

    [JsonProperty] public int markTokenUsed = 0;
    [JsonProperty] public bool markForDelete = false;

    public void Tick(TimeSpan t)
    {
        //if (compInstances == null) OnAfterDeserialize();
        //Debug.LogError("CompTick " + RefID);
        foreach(var comp in compInstances)
        {
            if (comp is null) continue;
             if (!comp.Tick(t))
            {
                switch (comp.CompType)
                {
                    case "ItemComponent_Degradable":
                        markForDelete = true;
                        //Debug.LogError("Item ["+this.DisplayName+"] degraded! will need to delete");
                        break;
                    default:break;
                }
            }
            
        }


    }

    public void OnAfterDeserialize()
    {
        this.compInstances.RemoveAll(x => x is null);

        foreach (var comp in Parent.itemComps_Template)
        {
            
            if (this.compInstances_nonSerialized.Find(x => x.CompType == comp.compType) != null) continue;
            if (this.compInstances.Find(x => x.CompType == comp.compType) != null) continue;


            var c = comp.Instantiate(parent);
            if (c is null) continue;
            //Debug.Log("ReAdding comp " + comp.compType + " for item ref " + this.RefID + " " + Base.DisplayName+", Stackable? "+c.Stackable);
            if (c.Stackable) compInstances_nonSerialized.Add(c);
            else compInstances.Add(c);

        }
        InvalidateTagsCache();
    }

    public void Dispose()
    {
        Debug.Log("Item Instance " + RefID + " disposed");
    }

    public virtual void DisposeInternal()
    {
        //this.compInstances = null;
        //this.parent = null;

    }
}
