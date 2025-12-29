using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using System.Linq;

[System.Serializable]
public class ItemComponentTemplate_Defense
{
    [JsonIgnore]
    public bool isBreakable
    {
        get { return this.integrity > -1; }
    }
    public int integrity = -1;


    [System.Serializable]
    public class Defense
    {
        public List<DamageType> applyToDamageTypes = new List<DamageType>();
        public int damageReductionValue = 0;

        public Defense() { }
        public Defense(DamageType dtype, int damageReductionValue)
        {
            this.applyToDamageTypes.Add(dtype);
            this.damageReductionValue = damageReductionValue;
        }
    }

    public List<Defense> armorLayers = new List<Defense>();
}



/// <summary>
/// Serializer overrideable
/// but only override with meal, dont override with raw food
/// dont do that.
/// </summary>
[System.Serializable]
public class ItemComponent_Defense : ItemComponent_Base
{
    [JsonIgnore] public override string CompType { get { return "ItemComponent_Defense"; } }
    string _tooltip = null;
    [JsonIgnore]
    public override string Tooltip
    {
        get
        {
            if (_tooltip == null)
            {
                var template = this.CompTemplate.comp_Defense;
                List<string> s = new List<string>();
                foreach (ItemComponentTemplate_Defense.Defense i in template.armorLayers) s.Add($"{i.damageReductionValue}/{String.Join("", i.applyToDamageTypes)}");
                _tooltip = LocalizeDictionary.QueryThenParse("ItemComponent_Defense_tooltip")
                    .Replace("$layers$", String.Join(" | ", s))
                    .Replace("$pierce$", template.isBreakable ? LocalizeDictionary.QueryThenParse("ItemComponent_Defense_tooltip_integrity").Replace("$value$", $"{template.integrity}")
                                                                : LocalizeDictionary.QueryThenParse("ItemComponent_Defense_tooltip_integrity_max"));
            }
            return _tooltip;
        }
    }

    public ItemComponent_Defense()
    {

    }
    public ItemComponent_Defense(Item_Base itemBase)
    {
        this.parent = itemBase;
        this.parentID = itemBase.ID;
    }

    public override bool canMergeWith(ItemComponent_Base other)
    {
        return base.canMergeWith(other) && (other is ItemComponent_Defense) && (this.CompTemplate.comp_Defense == (other as ItemComponent_Defense).CompTemplate.comp_Defense);
    }

    [JsonIgnore] public override bool Serializable { get { return true; } }
    [JsonIgnore] public override bool Stackable { get { return true; } }
    [JsonIgnore] public int Integrity { get { return this.CompTemplate.comp_Defense.integrity; } }
    [JsonIgnore] public List<ItemComponentTemplate_Defense.Defense> ArmorLayers { get { return this.CompTemplate.comp_Defense.armorLayers; } }

}