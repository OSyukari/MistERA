using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

[System.Serializable]
public class ItemComponentTemplate_Ingestible
{
    public List<Ingestible_IngestMethod> ingestMethod = new List<Ingestible_IngestMethod>();
    public float amount = 0;
    // public string giveStatus;

    [System.Serializable]
    public class Ingestible_IngestMethod
    {
        public string bodyTags = "";
        public float digestSpeed = 0;
        public int digestDelay = 0;
        public int digestDelayVariation = 0;
        public float amountMod = 0;
        public string giveStatus = "";
    }

    public List<OnUseEffect> OnUseEffects = new List<OnUseEffect>();

    [System.Serializable]
    public class OnUseEffect
    {
        public EffectKeyword effectID = EffectKeyword.None;
        public List<string> arguments = new List<string>();
        [JsonIgnore]
        public bool isValid
        {
            get
            {
                return this.effectID != EffectKeyword.None;
            }
        }
    }
}



/// <summary>
/// Serializer overrideable
/// but only override with meal, dont override with raw food
/// dont do that.
/// </summary>
[System.Serializable]
public class ItemComponent_Ingestible : ItemComponent_Base
{
    [JsonIgnore] public override string CompType { get { return "ItemComponent_Ingestible"; } }
    [JsonIgnore] public override string Tooltip
    {
        get
        {
            string s = "";
            foreach (ItemComponentTemplate_Ingestible.Ingestible_IngestMethod i in this.ingestMethod) s += String.Join(" ", i.bodyTags);
            return $"Ingestible, amount {amount}" + " ingest methods [" + s + "]";
        }
    }

    public override bool canMergeWith(ItemComponent_Base other)
    {
        return base.canMergeWith(other) && (other is ItemComponent_Ingestible) && (this.amount == (other as ItemComponent_Ingestible).amount);
    }

    //[SerializeField] new string parentID;
    [JsonIgnore] public override bool Serializable { get { return true; } }
    [JsonIgnore] public override bool Stackable { get { return false; } }

    public ItemComponent_Ingestible()
    {

    }
    public ItemComponent_Ingestible(Item_Base itemBase)
    {
        this.parent = itemBase;
        this.parentID = itemBase.ID;
        this.amount = CompTemplate.comp_Ingestible.amount;
    }
    [SerializeField][JsonProperty] public float amount = 0;
    [JsonIgnore] public List<ItemComponentTemplate_Ingestible.Ingestible_IngestMethod> ingestMethod { get { return CompTemplate.comp_Ingestible.ingestMethod; } }
    //public string giveStatus { get { return CompTemplate.comp_Ingestible.giveStatus; } }
    [JsonIgnore] public List<ItemComponentTemplate_Ingestible.OnUseEffect> OnUseEffects { get { return CompTemplate.comp_Ingestible.OnUseEffects; } }


}