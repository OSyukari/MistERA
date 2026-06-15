using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

[System.Serializable]
public class ItemComponentTemplate_Knowledges
{

    public List<string> knowledgeIDs = new List<string>();
    public float knowledgeRatePerMin = 0.035f;
}


[System.Serializable]
public class ItemComponent_Knowledges : ItemComponent_Base
{
    [JsonIgnore] public override string CompType { get { return "ItemComponent_Knowledges"; } }
    string _tooltip = null;



    [JsonIgnore]
    public override string Tooltip
    {
        get
        {
            if (_tooltip == null)
            {
                List<string> ss = new List<string>();
                if (Comp != null)
                {
                    foreach (var sid in Comp.knowledgeIDs)
                    {
                        ss.Add(LocalizeDictionary.QueryThenParse(sid));
                    }
                    _tooltip = $"contains knowledge on {String.Join(" ", ss)}";
                }
                else
                {
                    _tooltip = $"contains knowledge on nothing";
                }
            }
            return _tooltip;
        }
    }

    public ItemComponent_Knowledges()
    {

    }
    public ItemComponent_Knowledges(Item_Base itemBase)
    {
        this.parent = itemBase;
        this.parentID = itemBase.ID;
    }

    public override bool canMergeWith(ItemComponent_Base other)
    {
        return base.canMergeWith(other) && (other is ItemComponent_Knowledges) && (this.CompTemplate.comp_Knowledge == (other as ItemComponent_Knowledges).CompTemplate.comp_Knowledge);
    }
    [JsonIgnore] public override bool Serializable { get { return true; } }
    [JsonIgnore] public override bool Stackable { get { return true; } }

    ItemComponentTemplate_Knowledges _comp = null;
    public ItemComponentTemplate_Knowledges Comp
    {
        get
        {
            if (_comp == null) _comp = CompTemplate.comp_Knowledge;
            return _comp;
        }
    }

   
}