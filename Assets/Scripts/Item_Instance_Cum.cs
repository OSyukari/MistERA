using Newtonsoft.Json;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Item_Instance_Cum : Item_Instance
{
    public int ownerRefID = -1;
    private Character_Trainable ownerRef = null;
    public bool experienceTicked = false;
    [JsonIgnore] public Character_Trainable Owner
    {
        get { 
            // check if owner is deleted
            
            if (ownerRef == null && ownerRefID != -1) ownerRef = scr_System_CampaignManager.current.FindInstanceByID(ownerRefID);
            if (ownerRef != null && ownerRef.Deleted)
            {
                ownerRefID = -1;
                ownerRef = null;
            }
            return ownerRef;
        }
    }

    [JsonIgnore]
    public string FatherName
    { get
        {
            if (Owner != null) return Owner.FirstName;
            else if (race != null) return race.DisplayName;
            else return "unknown";
        } }

    public bool Merge(Item_Instance item)
    {
        if (!(item is Item_Instance_Cum)) return false;
        var cum = item as Item_Instance_Cum;
        if (cum.raceID != raceID) return false;
        if (cum.baseID != baseID) return false;
        if (cum.templateID != templateID) return false;
        if (cum.nameOverwrite != nameOverwrite) return false;

        this.CumAmount += cum.CumAmount;
        return true;
    }

    Humanoid_Race _race = null;
    [JsonIgnore] public Humanoid_Race race
    {
        get
        {
            if (_race == null && raceID != "")
            {
                _race = scr_System_Serializer.current.MasterList.humanoid_Races.GetByID(raceID);
            }
            return _race;
        }
    }

    public string raceID = "";
    public string baseID = "";
    public string templateID = "";


    /// <summary>
    /// Since we don't go through world manager instantiation, cum do not have refID
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="nameOverwrite"></param>
    public Item_Instance_Cum(Character_Trainable owner, string nameOverwrite) : base("consumable_cum", nameOverwrite)
    {
        ownerRef = owner;

        if (owner != null)
        {
            if (this.nameOverwrite == "")
            {
                this.nameOverwrite = LocalizeDictionary.QueryThenParse("Item_Instance_Cum_nameOverwrite").Replace("$name$", owner.FirstName);
            }
            this.ownerRefID = owner.RefID;
            this.raceID = owner.Race.ID;
            this.baseID = owner.BaseID;
            this.templateID = owner.baseTemplateID;
        }
    }

    public Item_Instance_Cum(string raceid, string baseid, string templateid, string nameOverwrite) : base("consumable_cum", nameOverwrite)
    {
        this.raceID = raceid;
        this.baseID = baseid;
        this.templateID = templateid;
        if (this.nameOverwrite == "")
        {
            var ovrname = LocalizeDictionary.QueryThenParse(this.templateID,
                LocalizeDictionary.QueryThenParse(this.baseID,
                    LocalizeDictionary.QueryThenParse(this.raceID, "unknown")));
            this.nameOverwrite = LocalizeDictionary.QueryThenParse("Item_Instance_Cum_nameOverwrite").Replace("$name$", ovrname);
        }
    }

    /// <summary>
    /// USED FOR SERIALIZER DO NOT CALL THIS MANUALLY
    /// </summary>
    public Item_Instance_Cum() : base() { }

    public override string Print()
    {
        if (this._cache_printfull == "") this._cache_printfull = LocalizeDictionary.QueryThenParse("item_cum_print_amount");
        return this._cache_printfull.Replace("$item$", DisplayName).Replace("$count$", CumAmount.ToString($"N1"));
    }

    [JsonIgnore]
    public float CumAmount 
    {   get
        {
            return GetComp_Ingestible().amount;
        }
        set
        {
            GetComp_Ingestible().amount = (float)value;
        }
    }

}
