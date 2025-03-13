using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

[System.Serializable]
public class Index_BodyPartBase : I_IndexHasID, I_IndexHasTooltip, I_NeedLateInitialize, I_IndexMergeable
{
    public List<BodyPart_Base> BodyPart_Base = new List<BodyPart_Base>();
    public List<BodyInternal_Base> BodyInternal_Base = new List<BodyInternal_Base>();

    public void MergeWith(I_IndexMergeable list){
        var l = list as Index_BodyPartBase;
        if (l == null) return;
        else
        {
            this.BodyPart_Base.AddRange(l.BodyPart_Base);
            this.BodyInternal_Base.AddRange(l.BodyInternal_Base);
        }
    }

    public void RegisterAllID()
    {
        Debug.Log("Index_BodyPartBase : registering ID with list length [" + BodyPart_Base.Count+ "]+["+ BodyInternal_Base .Count+ "]") ;

        foreach (BodyPart_Base o in this.BodyPart_Base)
        {
            //Debug.Log("Character_Origin_Index : registering origin ["+o.ID+"] ");
            scr_System_Serializer.current.RegisterIDtoLib(o.ID, o);
        }
        foreach (BodyInternal_Base o in this.BodyInternal_Base)
        {
            //Debug.Log("Character_Origin_Index : registering origin ["+o.ID+"] ");
            scr_System_Serializer.current.RegisterIDtoLib(o.ID, o);
        }

       
    }

    public void RegisterAllTooltip()
    {
        foreach (BodyPart_Base o in this.BodyPart_Base)
        {
            scr_System_tooltipDictionary.current.AddEntry(o.ID, o.Tooltip);
        }
        foreach (BodyInternal_Base o in this.BodyInternal_Base)
        {
            scr_System_tooltipDictionary.current.AddEntry(o.ID, o.Tooltip);
        }
    }

    void I_NeedLateInitialize.LateInitialize()
    {
        foreach (BodyPart_Base b in BodyPart_Base)
        {
            foreach (string s in b.availableSlotsString)
            {
                BodyPartEquipSlot slot = BodyPartEquipSlot.None;
                Enum.TryParse(s, out slot);
                if (slot != BodyPartEquipSlot.None) b.availableSlots.Add(slot);
            }
            foreach (string s in b.equipLayersString)
            {
                BodyEquipLayer layer = BodyEquipLayer.None;
                Enum.TryParse(s, out layer);
                if (layer != BodyEquipLayer.None) b.equipLayers.Add(layer);

            }

            // ERROR CHECKING START
            if (!b.isValid)
            {
                Debug.LogError("BodyPart_Base [" + b.ID + "] not valid: all its childs' sortOrder must be strictly bigger than its own sortOrder.");
                scr_System_Serializer.current.RemoveIDfromLib(b.ID);
                // prevent it from being used.
            }

            // ERROR CHECKING END
        }
        foreach (BodyInternal_Base b in BodyInternal_Base)
        {
            foreach (string s in b.availableSlotsString)
            {
                BodyPartEquipSlot slot = BodyPartEquipSlot.None;
                Enum.TryParse(s, out slot);
                if (slot != BodyPartEquipSlot.None) b.availableSlots.Add(slot);
            }
            foreach (string s in b.equipLayersString)
            {
                BodyEquipLayer layer = BodyEquipLayer.None;
                Enum.TryParse(s, out layer);
                if (layer != BodyEquipLayer.None) b.equipLayers.Add(layer);

            }
        }
    }
}

[System.Serializable]
public class BodyPart_Base
{
    [SerializeField] private string id = "";
    public string ID { get { return id; } }


    [SerializeField] private string tooltip = "";
    public string Tooltip { get { return tooltip; } }


    [SerializeField] private string displayName = "";
    public string DisplayName { get { return scr_System_Serializer.current.Dictionary.QueryThenParse(displayName); } }

    public List<string> internalID = new List<string>();

    public List<string> availableSlotsString = new List<string>();
    [NonSerialized] public List<BodyPartEquipSlot> availableSlots = new List<BodyPartEquipSlot>();


    public List<BodyPartEquipSlot> AvailableSlots { get { return availableSlots; } }

    public List<string> equipLayersString = new List<string>() { "Skin", "Inner", "Outer" };//, "Shell"
    [NonSerialized] public List<BodyEquipLayer> equipLayers = new List<BodyEquipLayer>();

    [SerializeField] public List<string> childID = new List<string>();

    [SerializeField] public int sortOrder = 99;
    [SerializeField] public List<string> tags = new List<string>();
    //[SerializeField]
    //public List<ItemComponent_Data> Comps = new List<ItemComponent_Data>();

    public List<string> GetAllChildsID()
    {
        List<string> value = new List<string>();
        foreach (string s in childID)
        {
            BodyPart_Base b = scr_System_Serializer.current.GetByNameOrID_BodyPart_Base(s);
            if (b != null) value.AddRange(b.GetAllChildsID());
        }

        value.AddRange(childID);
        return value;
    }

    /// <summary>
    /// Dumb isValid check, child sortOrder must be strictly bigger than parent
    /// </summary>
    public bool isValid
    {
        get
        {
            foreach (string s in childID)
            {
                if (scr_System_Serializer.current.GetByNameOrID_BodyPart_Base(s).sortOrder <= this.sortOrder) return false;
            }
            return true;
        }
    }

}