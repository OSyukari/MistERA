using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;

[System.Serializable]
public class BodyInternal_Base
{
    [SerializeField][JsonProperty] private string id = "";
    [JsonIgnore] public string ID { get { return id; } }


    [SerializeField][JsonProperty] private string tooltip = "";
    [JsonIgnore] public string Tooltip { get { return tooltip; } }


    [SerializeField][JsonProperty] private string displayName = "";
    [JsonIgnore] public string DisplayName { get { return LocalizeDictionary.QueryThenParse(displayName); } }

    public List<string> internalID = new List<string>();

    public List<BodyPartEquipSlot> AvailableSlots = new List<BodyPartEquipSlot>();
    public List<BodyEquipLayer> equipLayers = new List<BodyEquipLayer>();

    public List<string> childID = new List<string>();

    public int sortOrder = 99;
    public List<string> tags = new List<string>();
    //[SerializeField]
    //public List<ItemComponent_Data> Comps = new List<ItemComponent_Data>();

    public string firstExperienceDesc = "";
    public List<string> virginityLossTags = new List<string>();

    public List<string> GetAllChildsID()
    {
        List<string> value = new List<string>();
        foreach (string s in childID)
        {
            BodyPart_Base b = CharaOrigins.Instance.BodyPartIndex.GetPartByID(s);
            if (b != null) value.AddRange(b.GetAllChildsID());
        }

        value.AddRange(childID);
        return value;
    }

    public string tag_directionIn = "";
    public string tag_directionOut = "";

    [JsonIgnore] public bool canOverflowIn { get { return tag_directionIn != ""; } }
    [JsonIgnore] public bool canOverflowOut { get { return tag_directionOut != ""; } }


    public string sensitivityClassString = "";
    public string maxSensitivityStatString = "";

    public bool needLubrication = false;
    public float sizeRatio = 0;
    public float volumeRatio = 0;
    public float depthRatio = 0;

}