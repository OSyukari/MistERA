using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;

[System.Serializable]
public class Index_BodyPartBase : I_IndexHasID, I_IndexMergeable
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

    Dictionary<string, BodyPart_Base> ID_Dictionary1 = new Dictionary<string, BodyPart_Base>();
    Dictionary<string, BodyInternal_Base> ID_Dictionary2 = new Dictionary<string, BodyInternal_Base>();
    public void RegisterAllID()
    {
        Debug.Log("Index_BodyPartBase : registering ID with list length [" + BodyPart_Base.Count+ "]+["+ BodyInternal_Base .Count+ "]") ;

        foreach (BodyPart_Base o in this.BodyPart_Base)
        {
            //Debug.Log("Character_Origin_Index : registering origin ["+o.ID+"] ");
            ID_Dictionary1.Add(o.ID, o);
        }

        foreach (BodyInternal_Base o in this.BodyInternal_Base)
        {
            //Debug.Log("Character_Origin_Index : registering origin ["+o.ID+"] ");
            ID_Dictionary2.Add(o.ID, o);
        }

        var keys = ID_Dictionary1.Keys;
        foreach(var key in keys)
        {
            if (!ID_Dictionary1[key].isValid)
            {
                 Debug.LogError("BodyPart_Base [" + key + "] not valid: all its childs' sortOrder must be strictly bigger than its own sortOrder.");
                ID_Dictionary1.Remove(key);
            }
        }
    }
    public BodyPart_Base GetPartByID(string id) { return ID_Dictionary1.ContainsKey(id) ? ID_Dictionary1[id] : null; }
    public BodyInternal_Base GetInternalByID(string id) { return ID_Dictionary2.ContainsKey(id) ? ID_Dictionary2[id] : null; }
}

[System.Serializable]
public class BodyPart_Base
{
    [SerializeField][JsonProperty] private string id = "";
    [JsonIgnore] public string ID { get { return id; } }


    [SerializeField][JsonProperty] private string tooltip = "";
    [JsonIgnore] public string Tooltip { get { return tooltip; } }


    [SerializeField][JsonProperty] private string displayName = "";
    [JsonIgnore] public string DisplayName { get { return scr_System_Serializer.current.Dictionary.QueryThenParse(displayName); } }

    public List<string> internalID = new List<string>();

    public List<BodyPartEquipSlot> AvailableSlots = new List<BodyPartEquipSlot>();

    //public List<string> equipLayersString = new List<string>() { "Skin", "Inner", "Outer" };//, "Shell"
    public List<BodyEquipLayer> equipLayers = new List<BodyEquipLayer>() { BodyEquipLayer.Skin, BodyEquipLayer.Inner, BodyEquipLayer.Outer };

    public List<string> childID = new List<string>();

    public int sortOrder = 99;
    public List<string> tags = new List<string>();
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
    [JsonIgnore] public bool isValid
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