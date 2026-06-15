using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;


[System.Serializable]
public class ItemComponentTemplate_Records
{
    public string storeItemID = "";
    public KojoRecording records = null;
}

[System.Serializable]
public class ItemComponent_Records : ItemComponent_Base
{
    [JsonIgnore] public override string CompType { get { return "ItemComponent_Records"; } }
    string _tooltip = null;
    [JsonIgnore]
    public override string Tooltip
    {
        get
        {
            if (_tooltip == null)
            {
                _tooltip = $"records holder";
            }
            return _tooltip;

        }
    }
    public ItemComponent_Records()
    {

    }
    public ItemComponent_Records(Item_Base itemBase)
    {
        this.parent = itemBase;
        this.parentID = itemBase.ID;
    }

    public override void ReEstablishParent(string parentID, Item_Base parent)
    {
        base.ReEstablishParent(parentID, parent);
        if (this.Records.RecordUID == "") this.Records.RecordUID = parentID;
    }

    public override bool canMergeWith(ItemComponent_Base other)
    {
        if (!(other is ItemComponent_Records)) return false;
        var other2 = other as ItemComponent_Records;
        return base.canMergeWith(other) && this.Records == null && other2.Records == null;
    }



    [JsonIgnore] public override bool Serializable { get { return true; } }
    [JsonIgnore] public override bool Stackable { get { return false; } }

    [JsonProperty] KojoRecording records = null;
    [JsonIgnore] public KojoRecording Records
    {
        get
        {
            if (records != null) return records;
            if (CompTemplate.Comp_Records != null && CompTemplate.Comp_Records.records != null) return CompTemplate.Comp_Records.records;
            return null;
        }
        set
        {
            records = value;
        }
    }
    [JsonIgnore] public string storeItemID
    {
        get
        {
            return CompTemplate.Comp_Records == null ? "" : CompTemplate.Comp_Records.storeItemID;
        }
    }
    public void LoadRecords(KojoRecording recording)
    {
        this.records = recording;
    }

}

