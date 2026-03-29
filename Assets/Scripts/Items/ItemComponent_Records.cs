using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;


[System.Serializable]
public class ItemComponentTemplate_Records
{
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

    public override bool canMergeWith(ItemComponent_Base other)
    {
        if (!(other is ItemComponent_Records)) return false;
        var other2 = other as ItemComponent_Records;
        return base.canMergeWith(other) && this.records == null && other2.records == null;
    }

    [JsonIgnore] public override bool Serializable { get { return true; } }
    [JsonIgnore] public override bool Stackable { get { return false; } }

    public KojoRecording records = null;

    public void LoadRecords(KojoRecording recording)
    {
        this.records = recording;
    }
}

