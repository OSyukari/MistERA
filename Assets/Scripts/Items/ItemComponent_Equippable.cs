using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

public enum BodyEquipLayer
{
    None, Skin, Inner, Outer//, Shell
}

[System.Serializable]
public enum BodyPartEquipSlot
{
    None,
    // External
    Hand,
    Hair, Face, Eyes,
    Torso, Neck,
    Lower,
    Leg, Feet,
    // Internal
    Mouth,
    Stomach,
    Breasts, Nipples,
    Clitoris,
    Vagina,
    Womb,
    Urethra,
    Anus
}

[System.Serializable]
public enum Revealing
{
    Erotic = -1,
    SeeThrough = 0,
    ShapeReveal = 1,
    NonRevealing = 2,
    Armored = 3
}



[System.Serializable]
public class ItemComponentTemplate_Equippable : I_ItemComponentTemplate_Comp
{
    public BodyPartEquipSlot equipSlot = BodyPartEquipSlot.None;
    public List<BodyPartEquipSlot> coverSlot = new List<BodyPartEquipSlot>();
    public BodyEquipLayer equipLayer = BodyEquipLayer.None;
    public Revealing revealing = Revealing.NonRevealing;
    public bool lockable = false;
    public int equipCount = 1;
    public List<string> equipTags = new List<string>();
    public List<Stat_Modifier> stat_modifiers = new List<Stat_Modifier>();

    [System.Serializable]
    public class PartialUndress
    {
        public string displayName = "";
        public string statusID = "";
    }
    public bool TryValidate(out string errorMsg)
    {
        errorMsg = "";
        if (equipLayer == BodyEquipLayer.None) errorMsg += "ItemComponentTemplate_Equippable: component is invalid, missing equipLayer data\n";
        
        if (equipSlot == BodyPartEquipSlot.None) errorMsg += "ItemComponentTemplate_Equippable: component is invalid, missing equipSlot data\n";
        
        if (errorMsg == "") return true;
        else return false;
    }

    public ItemComponent_Base Instantiate(Item_Base itemBase)
    {
        return new ItemComponent_Equippable(itemBase);
    }
}



[System.Serializable]
public class ItemComponent_Equippable : ItemComponent_Base
{
    [JsonIgnore] public override string CompType { get { return "ItemComponent_Equippable"; } }
    [JsonIgnore] public override string Tooltip
    {
        get
        {
            return $"Equippable on {equipLayer}, requires Slot {equipSlot} {coverSlot}";
        }
    }

    //[SerializeField] new string parentID;
    /*
    protected override Item_Base Parent { get {
        if (parent == null) parent = scr_System_Serializer.current.GetByNameOrID_Item_Base(parentID);
        return parent; } }
    */
    /*
    [SerializeField] new protected string parentID;
    new protected Item_Base parent = null;
    protected override Item_Base Parent
    {
        get
        {
            if (this.parent == null) this.parent = scr_System_Serializer.current.GetByNameOrID_Item_Base(parentID);
            return this.parent;
        }
    }*/

    /*
    new protected ItemComponentTemplate compTemplate = null;
    protected override ItemComponentTemplate CompTemplate
    {
        get
        {
            if (this.compTemplate == null) this.compTemplate = Parent.GetCompTemplateByID(CompType);
            return this.compTemplate;
        }
    }*/
    public ItemComponent_Equippable()
    {

    }
    public ItemComponent_Equippable(Item_Base itemBase)
    {
        this.parent = itemBase;
        this.parentID = itemBase.ID;
    }

    public override bool canMergeWith(ItemComponent_Base other)
    {
        return false;
    }

    [JsonIgnore] public BodyPartEquipSlot equipSlot { get { return CompTemplate.comp_Equippable.equipSlot; } }
    [JsonIgnore] public List<BodyPartEquipSlot> coverSlot { get { return CompTemplate.comp_Equippable.coverSlot; } }
    [JsonIgnore] public BodyEquipLayer equipLayer { get { return CompTemplate.comp_Equippable.equipLayer; } }
    [JsonIgnore] public Revealing revealing { get { return CompTemplate.comp_Equippable.revealing; } }
    [JsonIgnore] public int equipCount { get { return CompTemplate.comp_Equippable.equipCount; } }
    [JsonIgnore] public List<string> equipTags { get { return CompTemplate.comp_Equippable.equipTags; } }
    [JsonIgnore] public List<Stat_Modifier> statModifiers { get { return CompTemplate.comp_Equippable.stat_modifiers; } }

    [JsonIgnore] public bool lockable { get { return CompTemplate.comp_Equippable.lockable; } }


    public bool hasTag(string s)
    {
        return this.equipTags.Contains(s);
    }

}