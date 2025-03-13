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

    [JsonIgnore] public virtual bool Stackable { get { return true; } }

    public void ReEstablishParent(string parentID, Item_Base parent)
    {
        this.parentID = parentID;
        this.parent = parent;
    }

    [SerializeField][JsonProperty] protected string parentID;
    [JsonIgnore] protected Item_Base parent = null;
    [JsonIgnore] protected virtual Item_Base Parent
    {
        get
        {
            if (parent == null) parent = scr_System_Serializer.current.GetByNameOrID_Item_Base(parentID);
            return parent;
        }
    }

    [JsonIgnore] protected ItemComponentTemplate compTemplate = null;
    [JsonIgnore] protected virtual ItemComponentTemplate CompTemplate
    {
        get
        {
            if (compTemplate == null) compTemplate = Parent.GetCompTemplateByID(CompType);
            return compTemplate;
        }
    }

    [JsonIgnore] public virtual bool Serializable { get { return false; } }

    public virtual bool Tick(TimeSpan t)
    {
        return true;
    }
}