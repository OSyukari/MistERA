using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Newtonsoft.Json;

public class ItemComponentTemplate_Recorder
{
    public string storeItemID = "";
    public int durationPerItem = 0;
    public int maxDuration = 0;
    public string resultItemID = "";

}

public class ItemComponent_Recorder : ItemComponent_Base
{
    [JsonIgnore] public override string CompType { get { return "ItemComponent_Recorder"; } }

    string _tooltip = null;
    [JsonIgnore]
    public override string Tooltip
    {
        get
        {
            if (_tooltip == null)
            {
                _tooltip = $"{(CompTemplate.Comp_Recorder.storeItemID == "" ? "Cannot store record" : $"Allows recording into {CompTemplate.Comp_Recorder.storeItemID}, duration per item {CompTemplate.Comp_Recorder.durationPerItem}, max duration {CompTemplate.Comp_Recorder.maxDuration}")}";

            }
            return _tooltip;

        }
    }

    public ItemComponent_Recorder() { }
    public ItemComponent_Recorder(Item_Base itemBase)
    {
        this.parent = itemBase;
        this.parentID = itemBase.ID;
    }



}