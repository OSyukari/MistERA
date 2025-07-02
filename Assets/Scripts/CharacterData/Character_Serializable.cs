using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.IO;
using System.Collections;
using System.Linq;

[System.Serializable]
public abstract class Character_SerializableBase
{
    public string baseID = "";
    public string firstName = "Jane", middleName = "", lastName = "Doe", nameDisplayFormat = "chara_fullname_firstToLast";
    public string origin = "charOrigin_EmissaryoftheTower", race = "humanRace_human", raceTemplate = "humanRaceAddon_Magician", startingGift = "charOriginGift_none";
    public bool playable = false;
    public PortraitManager Portrait = null;

    public virtual void PurgeNonExistingData()
    {
        //Debug.Log("CALLING VIRTUAL METHOD PurgeNonExistingData");
    }
}

[System.Serializable]
public class Character_SerializableSafe : Character_SerializableBase
{
    public CharaSafeTemplate Template = null;

    public override void PurgeNonExistingData()
    {
        //Debug.Log($"CALLING override METHOD PurgeNonExistingData Character_SerializableSafe with entries {(Template == null ? "null" : Template.initialInventory.Count)}");
        if (Template == null) return;
        for (int i = Template.initialInventory.Count - 1; i >= 0; i--)
        {
            var inv = Template.initialInventory[i];
            if (Masterlist_Items.GetByID(inv.ID) == null)
            {
                Template.initialInventory.RemoveAt(i);
            }

        }
    }
}

[System.Serializable]
public class Character_SerializableTrainable : Character_SerializableBase
{
    public CharaTrainableTemplate Template = null;
    public override void PurgeNonExistingData()
    {
        //Debug.Log($"CALLING override METHOD PurgeNonExistingData Character_SerializableTrainable with entries {(Template == null ? "null" : Template.initialInventory.Count)}");
        for (int i = Template.initialInventory.Count - 1; i >= 0; i--)
        {
            var inv = Template.initialInventory[i];
            if (Masterlist_Items.GetByID(inv.ID) == null)
            {
               // Debug.Log($"Removing inventoryEntry {(inv.ID)}");
                Template.initialInventory.RemoveAt(i);
            }
        }
    }

}