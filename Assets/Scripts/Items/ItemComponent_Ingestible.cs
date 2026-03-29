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
    public bool isLiquid = false;
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

        public Ingestible_IngestMethod Copy()
        {
            var newinstance = new Ingestible_IngestMethod();
            newinstance.bodyTags = this.bodyTags;
            newinstance.digestSpeed = this.digestSpeed;
            newinstance.digestDelay = this.digestDelay;
            newinstance.digestDelayVariation = this.digestDelayVariation;
            newinstance.amountMod = this.amountMod;
            newinstance.giveStatus = this.giveStatus;
            return newinstance;
        }
        public Ingestible_IngestMethod Mix(Ingestible_IngestMethod targetmethod, float selfAmount, float targetAmount)
        {
            var newinstance = this.Copy();

            if (targetAmount <= 0) newinstance.amountMod = 0;
            else newinstance.amountMod = Mathf.Clamp(targetAmount * targetmethod.amountMod / selfAmount, 0, 9999);

            newinstance.giveStatus = targetmethod.giveStatus;
            return newinstance;
        }
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
    string _tooltip = null;
    [JsonIgnore] public override string Tooltip
    {
        get
        {
            if (_tooltip == null)
            {
                string s = "";
                foreach (ItemComponentTemplate_Ingestible.Ingestible_IngestMethod i in this.ingestMethod) s += String.Join(" ", i.bodyTags);
                _tooltip = LocalizeDictionary.QueryThenParse("ItemComponent_Ingestible_tooltip")
                    .Replace("$amount$", $"{amount}")
                    .Replace("$methods$", s); //$" Ingestible, amount {amount}" + " ingest methods [" + s + "]";
            }
            return _tooltip;

        }
    }

    public bool canMixWith(ItemComponentTemplate_Ingestible other)
    {
        if (other == null) return false;
        List<string> method_self = new List<string>();
        foreach (var method in this.ingestMethod) method_self.Add(method.bodyTags);

        foreach (var method in other.ingestMethod) if (method_self.Contains(method.bodyTags)) return true;
        return false;
    }

    public override bool canMergeWith(ItemComponent_Base other)
    {
        if (!(other is ItemComponent_Ingestible)) return false;
        var other2 = other as ItemComponent_Ingestible;
        return base.canMergeWith(other) && (this.amount == other2.amount) && this.ingestMethod_addons.Count < 1 && other2.ingestMethod_addons.Count < 1;
    }

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

    [JsonProperty] protected List<ItemComponentTemplate_Ingestible.Ingestible_IngestMethod> ingestMethod_addons = new List<ItemComponentTemplate_Ingestible.Ingestible_IngestMethod>();
    public void AddIngestMethod(ItemComponentTemplate_Ingestible.Ingestible_IngestMethod m)
    {
        ingestMethod_addons.Add(m);
        _ingestMethod = null;
    }

    [JsonProperty] public float amount = 0;
    [JsonIgnore] public bool isLiquid { get { return CompTemplate.comp_Ingestible.isLiquid; } }
    List<ItemComponentTemplate_Ingestible.Ingestible_IngestMethod> _ingestMethod = null;
    [JsonIgnore] public List<ItemComponentTemplate_Ingestible.Ingestible_IngestMethod> ingestMethod { get {
            if (_ingestMethod == null)
            {
                _ingestMethod = new List<ItemComponentTemplate_Ingestible.Ingestible_IngestMethod>();
                _ingestMethod.AddRange(CompTemplate.comp_Ingestible.ingestMethod);
                _ingestMethod.AddRange(ingestMethod_addons);
            }
            return _ingestMethod; } }
    //public string giveStatus { get { return CompTemplate.comp_Ingestible.giveStatus; } }
    [JsonIgnore] public List<ItemComponentTemplate_Ingestible.OnUseEffect> OnUseEffects { get { return CompTemplate.comp_Ingestible.OnUseEffects; } }


}