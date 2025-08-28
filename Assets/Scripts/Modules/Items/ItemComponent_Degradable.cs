using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class ItemComponentTemplate_Degradable
{
    public int countdown_Days = 0;
    public int countdown_Hours = 0;
    public int countdown_Minutes = 0;

    //public string relevantItemTag = "";

    [JsonIgnore] public int TotalTick { get
        {
            return (countdown_Days * 24 + countdown_Hours) * 60 + countdown_Minutes;
        } }


}

[System.Serializable]
public class ItemComponent_Degradable : ItemComponent_Base
{
    [JsonIgnore] public override string CompType { get { return "ItemComponent_Degradable"; } }

    [JsonIgnore] public override string Tooltip
    {
        get
        {
            return LocalizeDictionary.QueryThenParse("ItemComponent_Degradable_tooltip")
                .Replace("$minutes$", $"{minuteCounter}");
        }
    }
    public override bool canMergeWith(ItemComponent_Base other)
    {
        return base.canMergeWith(other) && (other is ItemComponent_Degradable) && this.minuteCounter == (other as ItemComponent_Degradable).minuteCounter;
    }
    [JsonIgnore] public override bool Stackable { get { return false; } }
    [JsonIgnore] public override bool Serializable { get { return true; } }
    public ItemComponent_Degradable(Item_Base itemBase)
    {
        this.parent = itemBase;
        this.parentID = itemBase.ID;

        minuteCounter = CompTemplate.comp_Degradable.TotalTick;
    }

    public ItemComponent_Degradable(){
        
    }
    [SerializeField][JsonProperty] private int minuteCounter = 0;    //SerializedField
    [JsonIgnore] public int MinuteCounter { get { return minuteCounter; } }
    public override bool Tick(TimeSpan t)
    {
        if (minuteCounter == 0) return false;
        // && (CompTemplate.comp_Degradable.relevantItemTag == "" || parent.Tags.Contains( CompTemplate.comp_Degradable.relevantItemTag)
        if (minuteCounter > 0) minuteCounter = Math.Max(0, minuteCounter - (int)t.TotalMinutes);
        return true;
    }

}