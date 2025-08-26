using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using System.Linq;

[System.Serializable]
public class ItemComponentTemplate_Weapon
{
    /// <summary>
    /// Used in defense comp calculation
    /// </summary>
    public int Mass = 0;
    /// <summary>
    /// Provide speedmod and is indicator of how suitable it is as a weapon
    /// </summary>
    public int Balance = 0;
    public Dictionary<MoveType, DamageTypeValidator> DamageTypes = new Dictionary<MoveType, DamageTypeValidator>();
    [System.Serializable]
    public class DamageTypeValidator
    {
        public List<DamageType> targetDamageTypes = new List<DamageType>();
        public float convertRatio = 1f;


        public void Convert(AttackInstance atk)
        {
            atk.damageTypes = targetDamageTypes;
            atk.damageAmount *= convertRatio;
        }
    }
}



/// <summary>
/// Serializer overrideable
/// but only override with meal, dont override with raw food
/// dont do that.
/// </summary>
[System.Serializable]
public class ItemComponent_Weapon : ItemComponent_Base
{
    [JsonIgnore] public override string CompType { get { return "ItemComponent_Weapon"; } }
    [JsonIgnore]
    public override string Tooltip
    {
        get
        {
            var template = this.CompTemplate.comp_Weapon;
            List<string> s = new List<string>();
            return $"is weapon.";
        }
    }

    public ItemComponent_Weapon()
    {

    }
    public ItemComponent_Weapon(Item_Base itemBase)
    {
        this.parent = itemBase;
        this.parentID = itemBase.ID;
    }

    public override bool canMergeWith(ItemComponent_Base other)
    {
        return base.canMergeWith(other) && (other is ItemComponent_Weapon) && (this.CompTemplate.comp_Weapon == (other as ItemComponent_Weapon).CompTemplate.comp_Weapon);
    }

    //[SerializeField] new string parentID;
    [JsonIgnore] public override bool Serializable { get { return true; } }
    [JsonIgnore] public override bool Stackable { get { return true; } }

    ItemComponentTemplate_Weapon _comp = null;
    public ItemComponentTemplate_Weapon Comp { get
        {
            if (_comp == null ) _comp = CompTemplate.comp_Weapon;
            return _comp;
        } }



    public bool DealDamage(AttackInstance atk, out string tooltip)
    {
        if (Comp.DamageTypes.TryGetValue(atk.moveType, out var comp))
        {
            var s = atk.damageAmount;
            comp.Convert(atk);
            tooltip = LocalizeDictionary.QueryThenParse("ActionResult_tooltip_damageConversion")
                .Replace("$str$", $"{s}")
                .Replace("$mult$", $"{comp.convertRatio}")
                .Replace("$final$", $"{atk.damageAmount}")
                .Replace("$types$", String.Join("|", atk.damageTypes));
            return true;
        }
        else
        {
            tooltip = LocalizeDictionary.QueryThenParse("ActionResult_tooltip_unsupportedWeapon");
            atk.damageTypes = new List<DamageType>();
            return false;
        }
    }


    ItemComponentTemplate_Defense _defense;
    ItemComponentTemplate _template;
    ItemComponent_Defense _defenseComp;
    [JsonIgnore]
    public ItemComponent_Defense Defense
    {
        get
        {
            if (_defenseComp == null && this.Comp.Mass > 0)
            {
                _defense = new ItemComponentTemplate_Defense();
                _defense.armorLayers.Add(new ItemComponentTemplate_Defense.Defense(DamageType.Slash, Comp.Mass));
                _defense.armorLayers.Add(new ItemComponentTemplate_Defense.Defense(DamageType.Pierce, (int)Math.Ceiling((float)Comp.Mass / 2)));
                _defense.armorLayers.Add(new ItemComponentTemplate_Defense.Defense(DamageType.Blunt, (int)Math.Ceiling((double)Comp.Mass / 5)));
                _template = new ItemComponentTemplate();
                _template.comp_Defense = _defense;
                _defenseComp = new ItemComponent_Defense();
                _defenseComp.CompTemplate = _template;
            }
            return _defenseComp;
        }
    }
}