using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// JSON template -> create Item 
// Serialize

[System.Serializable]
public abstract class ItemComponent_Base
{
    [JsonIgnore] public int parentItemInstanceRef = -1;
    [JsonIgnore] public virtual string CompType { get { return "ItemComponent_Base"; } }
    [JsonIgnore] public virtual string Tooltip
    {
        get
        {
            return $"ItemComponent Base";
        }
    }

    public virtual bool canMergeWith(ItemComponent_Base other)
    {
        if (CompType != other.CompType) return false;
        return true;
    }

    /// <summary>
    /// Contribution this component makes to its owning item's RetailID (see Item_Instance.RetailID).
    /// Most components contribute nothing.
    /// </summary>
    public virtual string GetRetailID() { return ""; }

    [JsonIgnore] public virtual bool Stackable { get { return true; } }

    /// <summary>
    /// Contribution this component makes to its owning item's canBeSold check (see Item_Instance.canBeSold).
    /// Most components allow selling.
    /// </summary>
    [JsonIgnore] public virtual bool CanBeSold { get { return true; } }

    /// <summary>
    /// Lets this component adjust its owning item's ValuePerItem (see Item_Instance.ValuePerItem).
    /// Most components leave the value unchanged.
    /// </summary>
    public virtual void ValueMod(ref float value) { }

    /// <summary>
    /// Lets this component adjust its owning item's QualityModifier (see Item_Instance.QualityModifier).
    /// value starts at 1f (neutral) and each component adds its own deviation; parentvalue is the
    /// item's fixed ValuePerItem. Most components leave the value unchanged.
    /// </summary>
    public virtual float AddQualityMod(ref float value, float parentvalue) { return value; }

    /// <summary>
    /// Contribution this component makes to its owning item's Tags (see Item_Instance.Tags).
    /// Most components contribute nothing.
    /// </summary>
    public virtual List<string> GetTags() { return null; }

    public virtual void ReEstablishParent(string parentID, Item_Base parent)
    {
        this.parentID = parentID;
        this.parent = parent;
    }

    [JsonProperty] protected string parentID;
    [JsonIgnore] protected Item_Base parent = null;
    [JsonIgnore] protected virtual Item_Base Parent
    {
        get
        {
            if (parent == null) parent = Masterlist_Items.Instance.Index.GetByID(parentID);
            return parent;
        }
    }

    [JsonIgnore] protected ItemComponentTemplate compTemplate = null;
    [JsonIgnore] public virtual ItemComponentTemplate CompTemplate
    {
        get
        {
            if (compTemplate == null) compTemplate = Parent.GetCompTemplateByID(CompType);
            return compTemplate;
        }
        set
        {
            this.compTemplate = value;
        }
    }

    [JsonIgnore] public virtual bool Serializable { get { return false; } }

    public virtual bool Tick(TimeSpan t)
    {
        return true;
    }
}