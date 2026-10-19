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
    /// <summary> the parent part's slots whose coverage decides this organ's visibility (e.g. mouth -> Face). Empty = all parent slots </summary>
    public List<BodyPartEquipSlot> coverSlots = new List<BodyPartEquipSlot>();

    /// <summary> image prompt tags added while this organ is visible (see LLMUtils.CollectActorTags) </summary>
    public List<string> promptTags = new List<string>();
    /// <summary> optional destinations for visible promptTags and ordinary requested trait/skill tags; always tags stay flat </summary>
    public List<string> promptGroups = new List<string>();
    /// <summary> image prompt tags added regardless of visibility </summary>
    public List<string> promptTags_always = new List<string>();
    /// <summary> trait group ids (trait_Size_B, trait_Sensitivity_B...): the owner's current trait adds its promptTags_always always, promptTags while visible </summary>
    public List<string> requestTraits = new List<string>();
    /// <summary> skill ids (skill_expansion_V, skill_sensitivity_V...): the owner's current level adds its promptTags_always always, promptTags while visible </summary>
    public List<string> requestSkills = new List<string>();
    /// <summary> optional image prompt layer of every body that has this organ, after its part's (ActorPromptProfileUtility) </summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public ActorPromptProfile promptProfile = null;

    public List<string> childID = new List<string>();

    public int sortOrder = 99;
    public List<string> tags = new List<string>();

    public string firstExperienceDesc = "";
    public List<string> virginityLossTags = new List<string>();

    public List<string> images_expansion = new List<string>();
    public List<string> images_volume = new List<string>();

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
    public string deflateEventID = "";

    public bool needLubrication = false;

    public float sizeRatio = 0, stretch_ratio = 1, aroused_stretchMod = 0;

    public float depthRatio = 0, aroused_depthMod = 1;

    public float volumeRatio = 0, visibleExpansionRatio = 1, maxExpansionRatio = 1;
}

