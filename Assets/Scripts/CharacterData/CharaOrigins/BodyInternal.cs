using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;


public class BodyInternal_Base
{
    [JsonProperty] private string id = "";
    [JsonIgnore] public string ID { get { return id; } }


    [JsonProperty] private string tooltip = "";
    [JsonIgnore] public string Tooltip { get { return tooltip; } }


    [JsonProperty] private string displayName = "";
    [JsonIgnore] public string DisplayName { get { return LocalizeDictionary.QueryThenParse(displayName); } }

    public List<string> internalID = new List<string>();

    public List<BodyPartEquipSlot> AvailableSlots = new List<BodyPartEquipSlot>();
    public List<BodyEquipLayer> equipLayers = new List<BodyEquipLayer>();

    public List<string> childID = new List<string>();

    public int sortOrder = 99;
    public List<string> tags = new List<string>();

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

    public string memory_ingest_liquid = "";
    public string memory_ingest_solid = "";

    public string sensitivityClassString = "";
    public string traitClassString = "";
    public string maxSensitivityStatString = "";

    public string exposedKojoID = "";

    public bool needLubrication = false;

    public float sizeRatio = 0, stretch_ratio = 1, aroused_stretchMod = 0;

    public float depthRatio = 0, aroused_depthMod = 1;

    public float volumeRatio = 0, visibleExpansionRatio = 1, maxExpansionRatio = 1;
}

public enum BodyInternal_Base_WombType
{
    Human,
    Feline
}

/// <summary>
/// Numbers initialized at default human values
/// </summary>
public class ReproductionTemplate
{
    public string baseID = "";
    public BodyInternal_Base_WombType Type = BodyInternal_Base_WombType.Human;
    public int pubertyThreshold = 4380;
    public float pubertyVariation = 0.1f;
    public int cycleThreshold = 28;
    public float cycleVariation = 0.1f;
    public int menstrualDays = 5;
    public int follicularDays = 7;
    public int ovulationDays = 2;
    public int ovulationPowerAverage = 1500000;
    public int ovulationPowerVariation = 300000;
    public int ovulationQuantityAverage = 730;
    public int ovulationQuantityVariation = 200;

    public float fertility = 1.0f;
    public int climaxOvulationThreshold = 100;
    public float fertilizationChance = 0.25f;
    public int   ovumLifespanMinutes = 1440;   // 24 hours (human default)
    public bool hasEstrus = false;
}