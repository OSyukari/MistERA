using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

using System.Xml;
using System.Xml.Serialization;
using System.IO;
using System.Runtime.Serialization;

using Newtonsoft.Json;
using NUnit.Framework;


public interface I_IndexHasID
{
    public void RegisterAllID();
}

public interface I_NeedLateInitialize
{
    public void LateInitialize();
}

public class scr_System_Serializer : MonoBehaviour
{
    protected string DataPath_Local = "/Data/";
    //private Dictionary<Type, string> DataPath;


    public MasterList MasterList;

    // Singleton
    public static scr_System_Serializer current;

    protected Dictionary_Master _masterDictionary = new Dictionary_Master();
    public Dictionary_Index Dictionary { get { return MasterList.Dictionary; }  }
    private void Awake()
    {
        // scr_System_tooltipDictionary must first initialize

        if (current == null)
        {
            current = this;
        }
        else
        {
            Destroy(gameObject);
        }
        DontDestroyOnLoad(gameObject);

        //ID_Library = new Dictionary<string, object>();
        sensitivityClass_statusID_LookUp = new Dictionary<string, string>();
    }

    private void Start()
    {
        // before master dictionary load, central control player pref must exist to check language
        //_masterDictionary = LoadDictionary();
        MasterList = LoadDefs(true);

        LoadCharactersFoldersJSON();

        MasterList.Initialize();

    }


    private Dictionary<string, string> sensitivityClass_statusID_LookUp;
    public string GetSensitivityStatus(string s)
    {
        if (s == "") return "";
        if (sensitivityClass_statusID_LookUp.ContainsKey(s)) return sensitivityClass_statusID_LookUp[s];
        return "";
    }
    public void AddSensitivityStatus(string s, string s2)
    {
        if (s != "" && s2 != "" && !sensitivityClass_statusID_LookUp.ContainsKey(s)) sensitivityClass_statusID_LookUp.Add(s,s2);
    }


    
    Humanoid_Race_Index index_HumanoidRace { get { return MasterList.humanoid_Races as Humanoid_Race_Index; } }
    Humanoid_RaceTemplate_Index index_HRaceTemplate { get { return MasterList.humanoid_RaceTemplates as Humanoid_RaceTemplate_Index; } }
    public Traits_Group_Index index_TraitsAll { get { return MasterList.Traits_Groups as Traits_Group_Index; } }
    //public Skills_Index index_SkillsAll { get { return masterList.Skills as Skills_Index; } }
    public Stats_Derived_Base_Index index_StatsDerived { get { return MasterList.Stats_Derived_Bases as Stats_Derived_Base_Index; }  }

    Index_CampaignSetting index_CampaignSetting { get { return MasterList.CampaignSettings as Index_CampaignSetting; } }
    public Character_Base_Index index_Characters_Bases { get { return MasterList.Character_Bases as Character_Base_Index; } }
    public Index_BodyPartBase index_BodyPartBase { get { return MasterList.BodyPartBases as Index_BodyPartBase; } }
    public Index_Item_Base index_Item_Base { get { return MasterList.Items as Index_Item_Base; } }
    public Index_COM index_COM { get { return MasterList.COMs as Index_COM; } }
    public Index_Floor_Base index_Floor_Base { get { return MasterList.Floors as Index_Floor_Base; } }
    public Index_MapPlan index_MapPlan { get { return MasterList.MapPlans as Index_MapPlan; } }
    public Index_FurnitureBase index_FurnitureBase { get { return MasterList.Furnitures as Index_FurnitureBase; } }
    public Index_Status index_Status { get { return MasterList.Status as Index_Status; } }
    public Index_StatusEx index_StatusEX { get { return MasterList.StatusEXs as Index_StatusEx; } }
    public Index_Experiences index_Experiences { get { return MasterList.Experiences; } }
    public Index_CharaSkills index_Skills { get { return MasterList.Skills; } }
    public Stats_Derived_Extended_Index index_StatsExtended { get { return MasterList.StatEXs as Stats_Derived_Extended_Index; } }
    Humanoid_RaceTemplateAddon_Index index_HRTemplateAddon;

    //Dictionary<string, object> ID_Library;

    public void SavePresetJSON(Character_Trainable obj)
    {
        string s = JsonUtility.ToJson(obj);
        if (!Directory.Exists(Utility.GetSavePath_Preset())) Directory.CreateDirectory(Application.dataPath + "/Presets/");
        System.IO.File.WriteAllText(Utility.GetSavePath_Preset()+ obj.FirstName+((obj.MiddleName.Length < 1)? " " : " "+obj.MiddleName+" ")+obj.LastName+".json", s);
    }

    public Character_Trainable LoadPresetJSON(string filename)
    {
        //Debug.Log("Loading JSON preset " + Utility.GetSavePath_Preset() + filename);
        return scr_System_CentralControl.current.LoadCharaData(Utility.GetSavePath_Preset() + filename);
    }


    public void LoadCharactersFoldersJSON()
    {

        var newIndex = new Character_Base_Index();
        var newIndex2 = new Character_Trainable_SerializableTemplate_Index();
        //string path = Application.dataPath + "/Data/Characters/";
        string path = Application.dataPath + DataPath_Local;
        if (Directory.Exists(path))
        {
            DirectoryInfo d = new DirectoryInfo(path);

            //foreach (var file in d.GetFiles("*.json"))
            foreach (var file in d.GetFiles("*.json", SearchOption.AllDirectories))
            {
                var i = JsonConvert.DeserializeObject<Character_Trainable>(File.ReadAllText(file.FullName), Utility.SerializerSettings);
                if (i == null || i.BaseID == null) continue;
                newIndex.baseCharacters.Add(i);
                i.FileLocation = file.FullName;

                Character_Trainable_SerializableTemplate ii = JsonConvert.DeserializeObject<Character_Trainable_SerializableTemplate>(File.ReadAllText(file.FullName), Utility.SerializerSettings);
                ii.FileLocation = file.FullName;
                newIndex2.list.Add(ii);
            }
        }

        Debug.Log("Adding "+newIndex.baseCharacters.Count+" characters to masterlist");
        MasterList.Character_Bases = newIndex;
        MasterList.CharacterTemplates = newIndex2;
    }

    public MasterList LoadDefs(bool val = true)
    {
        string path = Application.dataPath + DataPath_Local;
        MasterList i = new MasterList();
        i.InitializeLists();
        if (Directory.Exists(path))
        {
            DirectoryInfo d = new DirectoryInfo(path);
            foreach (var file in d.GetFiles("*.json", SearchOption.AllDirectories))
            {   // all files must be index_com files
                MasterList l = JsonConvert.DeserializeObject<MasterList>(File.ReadAllText(file.FullName),Utility.SerializerSettings);
                if (l != null)
                {
                    i.MergeWith(l);
                    Debug.Log($"Reading json file {file.FullName}");
                }
            }
        }
        return i;
    }

    public Dictionary_Master LoadDictionary(bool val = true)
    {
        string path = Application.dataPath + DataPath_Local;
        Dictionary_Master i = new Dictionary_Master();
        if (Directory.Exists(path))
        {
            DirectoryInfo d = new DirectoryInfo(path);
            foreach (var file in d.GetFiles("*.json", SearchOption.AllDirectories))
            {   // all files must be index_com files
                MasterList l = JsonConvert.DeserializeObject<MasterList>(File.ReadAllText(file.FullName), Utility.SerializerSettings);
                if (l != null)
                {
                    i.MergeWith(l);
                    Debug.Log($"Reading json file {file.FullName}");
                }
            }
        }
        return i;
    }

    public Dictionary<string, ItemComponentTemplate_Craftable_Recipe> CraftingRecipe = new Dictionary<string, ItemComponentTemplate_Craftable_Recipe>();

    public void AddCraftingRecipe(List<ItemComponentTemplate_Craftable_Recipe> recipeList)
    {
        foreach (var c in recipeList)
        {
            if (!CraftingRecipe.ContainsKey(c.RecipeUID))
            {
                CraftingRecipe.Add(c.RecipeUID, c);
            }
        }
    }

    public List<ItemComponentTemplate_Harvestable> FarmRecipe = new List<ItemComponentTemplate_Harvestable>();

    public void AddFarmRecipe(ItemComponentTemplate_Harvestable recipe)
    {
        if (!FarmRecipe.Contains(recipe)) FarmRecipe.Add(recipe);
    }

    public Traits GetByNameOrID_Traits(string name_or_id)
    {
        return MasterList.Traits_Groups.GetTraitByID(name_or_id);
    }
    public scr_Traits_Group GetByNameOrID_TraitsGroup(string name_or_id)
    {
        return MasterList.Traits_Groups.GetGroupByID(name_or_id);
    }

    public Skills_Full GetByNameOrID_Skills(string name_or_id)
    {
        Debug.LogError("fetching skills unimplemented");
        return null;
    }

    public Stats_Derived_Base GetByNameOrID_StatsDerivedBase(string name_or_id)
    {
        return MasterList.Stats_Derived_Bases.GetByID(name_or_id);
    }

    public Item_Base GetByNameOrID_Item_Base(string name_or_id){
        return MasterList.Items.GetByID(name_or_id);
    }

    public BodyPart_Base GetByNameOrID_BodyPart_Base(string name_or_id)
    {
        return MasterList.BodyPartBases.GetPartByID(name_or_id);
    }

    public COM GetByNameOrID_COM(string name_or_id)
    {
        return MasterList.COMs.GetByID(name_or_id);
    }

    public BodyInternal_Base GetByNameOrID_BodyInternal_Base(string name_or_id)
    {
        return MasterList.BodyPartBases.GetInternalByID(name_or_id);
    }

    public CharaSkill GetByNameOrID(string name_or_id)
    {
        return MasterList.Skills.GetByID(name_or_id);
    }
    public Floor_Base GetByNameOrID_Floor_Base(string name_or_id)
    {
        return MasterList.Floors.GetByID(name_or_id);
    }
    public MapPlan GetByNameOrID_MapPlan(string name_or_id)
    {
        return MasterList.MapPlans.GetByID(name_or_id);
    }

    public FurnitureBase GetByNameOrID_FurnitureBase(string name_or_id)
    {
        return MasterList.Furnitures.GetByID(name_or_id);
    }

    public Status_Base GetByNameOrID_Status_Base(string name_or_id)
    {
        return MasterList.Status.GetByID(name_or_id);
    }

    public StatusEx_Base GetByNameOrID_StatusEx_Base(string name_or_id)
    {
        return MasterList.StatusEXs.GetByID(name_or_id);
    }

    public Stats_Derived_Extended GetByNameOrID_StatusEx(string name_or_id)
    {
        return MasterList.StatEXs.GetByID(name_or_id);
    }

    public Sexperience_Base GetByNameOrID_ExperienceBase(string name_or_id)
    {
        return MasterList.Sexperiences.GetByID(name_or_id);
    }
}


[System.Serializable]
public class Character_Trainable_Root
{
    [SerializeField]
    private List<Character_Trainable> characters;

    public Character_Trainable Character { get { return characters[0]; } }
    public Character_Trainable_Root(Character_Trainable c) 
    {
        characters = new List<Character_Trainable>();
        characters.Add(c);
    }
}


[System.Serializable]
public class Skills_Index : I_IndexHasID, I_IndexMergeable
{
    public List<Skills_Full> list = new List<Skills_Full>();

    public void MergeWith(I_IndexMergeable list){
        var l = list as Skills_Index;
        if (l == null) return;
        else if (l.list == null) return;
        else
        {
            this.list.AddRange(l.list);
        }
    }

    Dictionary<string, Skills_Full> ID_Dictionary = new Dictionary<string, Skills_Full>();
    public void RegisterAllID()
    {
        Debug.Log("Skills_Index : registering ID with list length [" + list.Count + "]");

        foreach (Skills_Full o in this.list)
        {
            ID_Dictionary.Add(o.ID, o);
        }
    }
    public Skills_Full GetByID(string id) { return ID_Dictionary.ContainsKey(id) ? ID_Dictionary[id] : null; }
}


[System.Serializable]
public class Humanoid_RaceTemplateAddon_Index
{
    public List<Humanoid_RaceTemplateAddon> list = new List<Humanoid_RaceTemplateAddon>();
}

[System.Serializable]
public class Traits_Group_Index : I_IndexHasID, I_NeedLateInitialize, I_IndexMergeable
{

    public void MergeWith(I_IndexMergeable list){
        var l = list as Traits_Group_Index;
        if (l == null) return;
        else if (l.traits_All.Count < 1) return;
        else
        {
            this.traits_STR.AddRange(l.traits_STR);
            this.traits_STR_SEX.AddRange(l.traits_STR_SEX);
            this.traits_CON.AddRange(l.traits_CON);
            this.traits_CON_SEX.AddRange(l.traits_CON_SEX);
            this.traits_PSY.AddRange(l.traits_PSY);
            this.traits_PSY_SEX.AddRange(l.traits_PSY_SEX);
            this.traits_WIL.AddRange(l.traits_WIL);
            this.traits_WIL_SEX.AddRange(l.traits_WIL_SEX);
            this.traits_BODY.AddRange(l.traits_BODY);




//            this.list.AddRange(l.list);

            if (this.traitsall == null)this.traitsall = new List<List<scr_Traits_Group>>(); 
            this.traitsall.AddRange(l.traits_All);
        }
    }
    public List<scr_Traits_Group> traits_STR = new List<scr_Traits_Group>();
    public List<scr_Traits_Group> traits_CON = new List<scr_Traits_Group>();
    public List<scr_Traits_Group> traits_PSY = new List<scr_Traits_Group>();
    public List<scr_Traits_Group> traits_WIL = new List<scr_Traits_Group>();
    public List<scr_Traits_Group> traits_STR_SEX = new List<scr_Traits_Group>();
    public List<scr_Traits_Group> traits_CON_SEX = new List<scr_Traits_Group>();
    public List<scr_Traits_Group> traits_PSY_SEX = new List<scr_Traits_Group>();
    public List<scr_Traits_Group> traits_WIL_SEX = new List<scr_Traits_Group>();
    public List<scr_Traits_Group> traits_BODY = new List<scr_Traits_Group>();

    private List<List<scr_Traits_Group>> traitsall = null;
    [JsonIgnore] public List<List<scr_Traits_Group>> traits_All
    {
        get
        {
            if (traitsall == null)
            {
                traitsall = new List<List<scr_Traits_Group>>();
                traitsall.Add(traits_STR);
                traitsall.Add(traits_CON);
                traitsall.Add(traits_PSY);
                traitsall.Add(traits_WIL);
                traitsall.Add(traits_STR_SEX);
                traitsall.Add(traits_CON_SEX);
                traitsall.Add(traits_PSY_SEX);
                traitsall.Add(traits_WIL_SEX);
                traitsall.Add(traits_BODY);
                return traitsall;
            }
            else
            {
                return traitsall;
            }
        }
    }
    public void LateInitialize()
    {
        foreach (List<scr_Traits_Group> o in traitsall)
        {
            foreach (scr_Traits_Group s in o)
            {
                foreach (Traits t in s.entries)
                {
                    t.Type = s.Type;
                    t.ParentID = s.ID;
                }

            }
        }
    }

    Dictionary<string, scr_Traits_Group> ID_Dictionary1 = new Dictionary<string, scr_Traits_Group>();
    Dictionary<string, Traits> ID_Dictionary2 = new Dictionary<string, Traits>();

    public scr_Traits_Group GetGroupByID(string id) { return ID_Dictionary1.ContainsKey(id) ? ID_Dictionary1[id] : null; }
    public Traits GetTraitByID(string id) { return ID_Dictionary2.ContainsKey(id) ? ID_Dictionary2[id] : null; }

    public void RegisterAllID()
    {
        Debug.Log("Traits_Group_Index : registering ID with list length [" + traits_All.Count + "]");

        foreach (List<scr_Traits_Group> o in traits_All)
        {
            foreach (scr_Traits_Group s in o)
            {
                ID_Dictionary1.Add(s.ID, s);
                foreach (Traits t in s.entries)
                {
                    ID_Dictionary2.Add(t.ID, t);
                }

            }
        }
    }
}

public interface I_CharacterStatBase
{

}

