using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

[System.Serializable]
public class BodyInternal_Base
{
    [SerializeField] private string id = "";
    public string ID { get { return id; } }


    [SerializeField] private string tooltip = "";
    public string Tooltip { get { return tooltip; } }


    [SerializeField] private string displayName = "";
    public string DisplayName { get { return scr_System_Serializer.current.Dictionary.Parse(displayName); } }

    public List<string> internalID = new List<string>();

    public List<string> availableSlotsString = new List<string>();
    [NonSerialized] public List<BodyPartEquipSlot> availableSlots = new List<BodyPartEquipSlot>();


    public List<BodyPartEquipSlot> AvailableSlots { get { return availableSlots; } }

    public List<string> equipLayersString = new List<string>();
    [NonSerialized] public List<BodyEquipLayer> equipLayers = new List<BodyEquipLayer>();

    [SerializeField] public List<string> childID = new List<string>();

    [SerializeField] public int sortOrder = 99;
    [SerializeField] public List<string> tags = new List<string>();
    //[SerializeField]
    //public List<ItemComponent_Data> Comps = new List<ItemComponent_Data>();

    [SerializeField] public string firstExperienceDesc = "";
    [SerializeField] public List<string> virginityLossTags = new List<string>();

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

    [SerializeField] public string tag_directionIn = "";
    [SerializeField] public string tag_directionOut = "";

    public bool canOverflowIn { get { return tag_directionIn != ""; } }
    public bool canOverflowOut { get { return tag_directionOut != ""; } }


    [SerializeField] public string sensitivityClassString = "";
    [SerializeField] public string maxSensitivityStatString = "";

    public bool needLubrication = false;
    public float sizeRatio = 0;
    public float volumeRatio = 0;
    public float depthRatio = 0;

}