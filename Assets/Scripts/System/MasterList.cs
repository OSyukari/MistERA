using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;




[System.Serializable]
public class MasterList
{
    protected ArrayList list = null;
    public ArrayList List
    {
        get
        {
            if (list == null)
            {
                list = new ArrayList();
                list.Add(Experiences);
                list.Add(Character_Origin_StartingOptions);
                list.Add(RelationshipTypes);
                list.Add(humanoid_Races);
                list.Add(humanoid_RaceTemplates);
                list.Add(CampaignSettings);
                list.Add(Skills);
                list.Add(CharacterTemplates);
                list.Add(Character_Personalities);
                list.Add(Character_Origins);    // need to be ordered later cuz require other list to be ready
            }
            return list;
        }
    }

    public Index_Experiences Experiences = null;
    public Character_Origin_Index Character_Origins = null;
    public Character_Origin_startingOption_Index Character_Origin_StartingOptions = null;
    public Index_RelationshipTypes RelationshipTypes = null;
    public Humanoid_Race_Index humanoid_Races = null;
    public Humanoid_RaceTemplate_Index humanoid_RaceTemplates = null;
    public Index_CampaignSetting CampaignSettings = null;
    public Index_CharaSkills Skills = null;
    public Character_Trainable_SerializableTemplate_Index CharacterTemplates = null;
    public Character_Personality_Index Character_Personalities = null;

    public void InitializeLists()
    {
        this.Experiences = new Index_Experiences();
        this.Character_Origins = new Character_Origin_Index();
        this.Character_Origin_StartingOptions = new Character_Origin_startingOption_Index();
        this.RelationshipTypes = new Index_RelationshipTypes();
        this.humanoid_Races = new Humanoid_Race_Index();
        this.humanoid_RaceTemplates = new Humanoid_RaceTemplate_Index();
        this.CampaignSettings = new Index_CampaignSetting();
        this.Skills = new Index_CharaSkills();
        this.CharacterTemplates = new Character_Trainable_SerializableTemplate_Index();
        this.Character_Personalities = new Character_Personality_Index();
    }

    public void MergeWith(MasterList list)
    {
        for (int i = 0; i < this.List.Count; i++)
        {
            if (list.List[i] == null) continue;
            //if (this.List[i] == null) this.List[i] = 
            I_IndexMergeable a = this.List[i] as I_IndexMergeable;
            I_IndexMergeable b = list.List[i] as I_IndexMergeable;
            if (a != null && b != null) a.MergeWith(b);
            else
            {
                Debug.LogError("Index Merge operation failed at index " + i);
            }
        }
    }
    public void Initialize()
    {

        foreach (object l in List)
        {
            if (l is I_IndexHasID) (l as I_IndexHasID).RegisterAllID();
            if (l is I_IndexHasTooltip) (l as I_IndexHasTooltip).RegisterAllTooltip();
        }

        foreach (object l in List)
        {
            if (l is I_NeedLateInitialize) (l as I_NeedLateInitialize).LateInitialize();
        }
    }
}