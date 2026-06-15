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
    [JsonIgnore] public ArrayList List
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
           //     list.Add(Sexperiences);
                list.Add(StatEXs);

                list.Add(CombatActions);
                list.Add(CombatActionPresets);
                list.Add(Knowledges);
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
                list.Add(Character_RelationshipAttitudes);
                list.Add(Character_Personalities);
                list.Add(Character_Origins);    // need to be ordered later cuz require other list to be ready
                list.Add(COMs);
                list.Add(Furnitures);
                list.Add(Events);
                list.Add(Character_Bases);
                list.Add(CharGenTemplates);
                list.Add(Encounters);
                list.Add(ExplorationEvents);
                list.Add(ExplorationFeatures);
                list.Add(ExpeditionEntry);
                list.Add(FindJobNodeRoots);
                list.Add(ExperienceInitializers);
            }
            return list;
        }
    }
   // public Index_Sexperiences Sexperiences = null;
    public Index_BodyPartBase BodyPartBases = new Index_BodyPartBase();
    public Stats_Derived_Base_Index Stats_Derived_Bases = new Stats_Derived_Base_Index();
    //public Character_BaseID_Index Character_BaseIDs = null;
    public Index_Floor_Base Floors = new Index_Floor_Base();
    public Index_MapPlan MapPlans = new Index_MapPlan();
    public Character_Base_Index Character_Bases = new Character_Base_Index();
    public Index_StatusEx StatusEXs = new Index_StatusEx();
    public Stats_Derived_Extended_Index StatEXs = new Stats_Derived_Extended_Index();
    public FindJobNodeRoot_Index FindJobNodeRoots = new FindJobNodeRoot_Index();
    public Index_Status Status = new Index_Status();
    public Index_Knowledges Knowledges = new Index_Knowledges();
    public Index_Experiences Experiences = new Index_Experiences();
    public Character_Origin_Index Character_Origins = new Character_Origin_Index();
    public Character_Origin_startingOption_Index Character_Origin_StartingOptions = new Character_Origin_startingOption_Index();
    public Index_RelationshipTypes RelationshipTypes = new Index_RelationshipTypes();
    public Humanoid_Race_Index humanoid_Races = new Humanoid_Race_Index();
    public Humanoid_RaceTemplate_Index humanoid_RaceTemplates = new Humanoid_RaceTemplate_Index();
    public Index_CampaignSetting CampaignSettings = new Index_CampaignSetting();
    public Index_CharaSkills Skills = new Index_CharaSkills();
    public Character_Trainable_SerializableTemplate_Index CharacterTemplates = new Character_Trainable_SerializableTemplate_Index();
    public Character_Personality_Index Character_Personalities = new Character_Personality_Index();
    public Index_CharaRelationshipAttitudes Character_RelationshipAttitudes = new Index_CharaRelationshipAttitudes();
    public Index_COM COMs = new Index_COM();
    public Index_FurnitureBase Furnitures = new Index_FurnitureBase();

    public Index_CombatActions CombatActions = new Index_CombatActions();
    public Index_CombatActionPresets CombatActionPresets = new Index_CombatActionPresets();
    public Index_CharGenTemplates CharGenTemplates = new Index_CharGenTemplates();
    public Index_EncounterGen Encounters = new Index_EncounterGen();
    public Index_ExperienceInitializer ExperienceInitializers = new Index_ExperienceInitializer();

    public void InitializeLists(bool initCoreList = false)
    {
        if (initCoreList)
        {
            this.Events = Masterlist_Event.Instance.Events;
            this.Items = Masterlist_Items.Instance.Index;
            this.Dictionary = LocalizeDictionary.Instance.Index;
            this.humanoid_Races = CharaOrigins.Instance.Humanoid_Race_Index;
            this.Character_Origins = CharaOrigins.Instance.Origins_Index;
            this.Character_Origin_StartingOptions = CharaOrigins.Instance.StartingOption_Index;
            this.humanoid_RaceTemplates = CharaOrigins.Instance.RaceTemplateIndex;
            this.BodyPartBases = CharaOrigins.Instance.BodyPartIndex;
            this.Traits_Groups = CharaOrigins.Instance.Traits;
            this.ExplorationEvents = Expeditions.ExplorationEvents;
            this.ExplorationFeatures = Expeditions.ExplorationFeatures;
            this.ExpeditionEntry = Expeditions.ExpeditionEntry;
        }
        else
        {
            this.Events = new Index_Events();
            this.Items = new Index_Item_Base();
            this.Dictionary = new Dictionary_Index();
            this.humanoid_Races = new Humanoid_Race_Index();
            this.Character_Origins = new Character_Origin_Index();
            this.Character_Origin_StartingOptions = new Character_Origin_startingOption_Index();
            this.humanoid_RaceTemplates = new Humanoid_RaceTemplate_Index();
            this.BodyPartBases = new Index_BodyPartBase();
            this.Traits_Groups = new Traits_Group_Index();
            this.ExpeditionEntry = new Index_Expeditions();
            this.ExplorationEvents = new Index_ExpEvents();
            this.ExplorationFeatures = new Index_FeatureSet();
        }
    }

    public Index_Events Events;
    public Traits_Group_Index Traits_Groups;
    public Index_Item_Base Items;
    public Dictionary_Index Dictionary;
    public Index_Expeditions ExpeditionEntry;
    public Index_ExpEvents ExplorationEvents;
    public Index_FeatureSet ExplorationFeatures;
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
        List<string> registerMsg = new List<string>();

        foreach (object l in List)
        {
            if (l is I_SerializationCallbackReceiver) (l as I_SerializationCallbackReceiver).OnAfterDeserialize();
            if (l is I_IndexHasID) (l as I_IndexHasID).RegisterAllID(registerMsg);
        }

        foreach (object l in List)
        {
            if (l is I_NeedLateInitialize) (l as I_NeedLateInitialize).LateInitialize();
        }

        Debug.Log($"Serializer Report:\n{String.Join("\n", registerMsg)}");
    }

    public void RemoveElementWithTag(string tag)
    {
        foreach (object l in List)
        {
            if (l is I_RemoveElemByTag) (l as I_RemoveElemByTag).RemoveElemByTag(tag);
        }
    }
    public void RemoveNSFW()
    {
        foreach (object l in List)
        {
            if (l is I_RemoveNSFW) (l as I_RemoveNSFW).RemoveNSFW();
        }

        foreach(var keyword in scr_System_Serializer.current.nsfwKeywords)
        {
            RemoveElementWithTag (keyword);
        }

        Traits_Groups = null;
    }

    public void RemoveNonExisting()
    {
        foreach(object l in List)
        {
            if (l is I_RemoveNonExisting) (l as I_RemoveNonExisting).RemoveNonExisting();
        }
    }
}
