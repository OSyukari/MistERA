using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;

using Newtonsoft.Json;

public class scr_System_Serializer : MonoBehaviour
{
    protected string DataPath_Local = "/Data/";
    //private Dictionary<Type, string> DataPath;


    public MasterList MasterList;

    // Singleton
    public static scr_System_Serializer current;

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
        LoadDefs(true);
        // parkour every file

        LoadCharactersFoldersJSON();

        MasterList.Initialize();

        // error checking

        /*
        foreach (var i in MasterList.COMs.list)
        {
            if (i.comTags.Contains("food_meal"))
            {
                if (!(MasterList.COMs.GetByID(i.ID) is COM_TakeMeal)) Debug.LogError($"foodcom error target isfood in list {i is COM_TakeMeal} isfood in dict {MasterList.COMs.GetByID(i.ID) is COM_TakeMeal}");
            }
        }*/

        /*
        foreach (var com in MasterList.COMs.LIST)
        {
            if (com is COM_TakeMeal || com is COM_Sex || com is COM_Character_Insert || com is COM_Character_Remove || com is COM_FarmRecipe)
            {
                Debug.Log($"SPECIAL COM {com.ID}");
            }
        }*/
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
    public Traits_Group_Index index_TraitsAll { get { return MasterList.Traits_Groups as Traits_Group_Index; } }
    //public Skills_Index index_SkillsAll { get { return masterList.Skills as Skills_Index; } }
    public Stats_Derived_Base_Index index_StatsDerived { get { return MasterList.Stats_Derived_Bases as Stats_Derived_Base_Index; }  }

    public Character_Base_Index index_Characters_Bases { get { return MasterList.Character_Bases as Character_Base_Index; } }
    public Index_Item_Base index_Item_Base { get { return MasterList.Items as Index_Item_Base; } }
    public Index_COM index_COM { get { return MasterList.COMs as Index_COM; } }
    public Index_Status index_Status { get { return MasterList.Status as Index_Status; } }
    public Index_StatusEx index_StatusEX { get { return MasterList.StatusEXs as Index_StatusEx; } }
    public Index_Experiences index_Experiences { get { return MasterList.Experiences; } }
    public Index_CharaSkills index_Skills { get { return MasterList.Skills; } }
    public Stats_Derived_Extended_Index index_StatsExtended { get { return MasterList.StatEXs as Stats_Derived_Extended_Index; } }

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

        //Debug.Log("Adding "+newIndex.baseCharacters.Count+" characters to masterlist");
        MasterList.Character_Bases = newIndex;
        MasterList.CharacterTemplates = newIndex2;
    }

    protected void LoadDefs(bool val = true)
    {
        string path = Application.dataPath + DataPath_Local;
        MasterList = new MasterList();
        MasterList.InitializeLists(true);
        if (Directory.Exists(path))
        {
            DirectoryInfo d = new DirectoryInfo(path);
            foreach (var file in d.GetFiles("*.json", SearchOption.AllDirectories))
            {   // all files must be index_com files
                MasterList l = JsonConvert.DeserializeObject<MasterList>(File.ReadAllText(file.FullName),Utility.SerializerSettings);
                if (l != null)
                {
                    MasterList.MergeWith(l);
                    //Debug.Log($"Reading json file {file.FullName}");
                }
            }
        }
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

    public Event GetEventByID(string name_or_id)
    {
        return MasterList.Events.GetByID(name_or_id);
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

