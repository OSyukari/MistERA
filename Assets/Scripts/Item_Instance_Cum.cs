using Newtonsoft.Json;
using System.Collections;
using System.Collections.Generic;
using UnityEditorInternal.Profiling.Memory.Experimental;
using UnityEngine;

public class Item_Instance_Cum : Item_Instance
{
    public int ownerRefID;
    private Character_Trainable ownerRef = null;
    public bool experienceTicked = false;
    [JsonIgnore] public Character_Trainable Owner
    {
        get { if (ownerRef == null) ownerRef = scr_System_CampaignManager.current.FindInstanceByID(ownerRefID);
            return ownerRef;
        }
    }
    /// <summary>
    /// Since we don't go through world manager instantiation, cum do not have refID
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="nameOverwrite"></param>
    public Item_Instance_Cum(Character_Trainable owner, string nameOverwrite) : base("consumable_cum", nameOverwrite)
    {
        ownerRef = owner;
        this.ownerRefID = owner.RefID;
    }

    /// <summary>
    /// USED FOR SERIALIZER DO NOT CALL THIS MANUALLY
    /// </summary>
    public Item_Instance_Cum() : base() { }

    [JsonIgnore]
    public double CumAmount { get
        {
            return GetComp_Ingestible().amount;
        } }

}
