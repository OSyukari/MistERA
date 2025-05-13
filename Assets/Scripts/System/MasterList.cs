using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;


public interface I_IndexMergeable
{
    public void MergeWith(I_IndexMergeable list);
}


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
                list.Add(Dictionary);

                list.Add(Stats_Derived_Bases);
                list.Add(BodyPartBases);
                list.Add(Floors);
                list.Add(MapPlans);
                list.Add(StatusEXs);
                list.Add(Sexperiences);
                list.Add(StatEXs);

                list.Add(Experiences);
                list.Add(Traits_Groups);
                list.Add(Character_Origin_StartingOptions);
                list.Add(RelationshipTypes);
                list.Add(humanoid_Races);
                list.Add(humanoid_RaceTemplates);
                list.Add(CampaignSettings);
                list.Add(Skills);
                list.Add(Items);
                list.Add(Status);
                list.Add(CharacterTemplates);
                list.Add(Character_Personalities);
                list.Add(Character_Origins);    // need to be ordered later cuz require other list to be ready
                list.Add(COMs);
                list.Add(Furnitures);
                list.Add(Events);
            }
            return list;
        }
    }
    public Traits_Group_Index Traits_Groups = null;
    public Index_Sexperiences Sexperiences = null;
    public Index_BodyPartBase BodyPartBases = null;
    public Stats_Derived_Base_Index Stats_Derived_Bases = null;
    //public Character_BaseID_Index Character_BaseIDs = null;
    public Index_Floor_Base Floors = null;
    public Index_MapPlan MapPlans = null;
    public Character_Base_Index Character_Bases = null;
    public Index_StatusEx StatusEXs = null;
    public Stats_Derived_Extended_Index StatEXs = null;

    public Index_Status Status = null;
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
    public Index_COM COMs = null;
    public Index_Item_Base Items = null;
    public Dictionary_Index Dictionary = null;
    public Index_FurnitureBase Furnitures = null;
    public Index_Events Events = null;

    public void InitializeLists()
    {
        this.Experiences = new Index_Experiences();
        this.Stats_Derived_Bases = new Stats_Derived_Base_Index();
        this.Character_Bases = new Character_Base_Index();
        this.BodyPartBases = new Index_BodyPartBase();
        this.Floors = new Index_Floor_Base();
        this.MapPlans = new Index_MapPlan();
        this.StatusEXs = new Index_StatusEx();
        this.StatEXs = new Stats_Derived_Extended_Index();
        this.Sexperiences = new Index_Sexperiences();
        this.Traits_Groups = new Traits_Group_Index();
        this.Character_Origins = new Character_Origin_Index();
        this.Character_Origin_StartingOptions = new Character_Origin_startingOption_Index();
        this.RelationshipTypes = new Index_RelationshipTypes();
        this.humanoid_Races = new Humanoid_Race_Index();
        this.humanoid_RaceTemplates = new Humanoid_RaceTemplate_Index();
        this.CampaignSettings = new Index_CampaignSetting();
        this.Skills = new Index_CharaSkills();
        this.Status = new Index_Status();
        this.Items = new Index_Item_Base();
        this.CharacterTemplates = new Character_Trainable_SerializableTemplate_Index();
        this.Character_Personalities = new Character_Personality_Index();
        this.COMs = new Index_COM();
        this.Dictionary = new Dictionary_Index();
        this.Furnitures = new Index_FurnitureBase();
        this.Events = new Index_Events();
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
            if (l is I_SerializationCallbackReceiver) (l as I_SerializationCallbackReceiver).OnAfterDeserialize();
            if (l is I_IndexHasID) (l as I_IndexHasID).RegisterAllID();
        }

        foreach (object l in List)
        {
            if (l is I_NeedLateInitialize) (l as I_NeedLateInitialize).LateInitialize();
        }
    }
}

public interface I_SerializationCallbackReceiver
{
    public void OnAfterDeserialize();
}