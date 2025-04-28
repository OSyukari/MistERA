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
public interface I_IndexHasTooltip
{
    public void RegisterAllTooltip();
}

public interface I_NeedLateInitialize
{
    public void LateInitialize();
}

public class scr_System_Serializer : MonoBehaviour
{
    protected string DataPath_Local = "/Data/Defs/";
    protected string DataPath_Persistent;
    private Dictionary<Type, string> DataPath;

    protected Dictionary<string, string> TooltipLibrary;

    public masterList masterList;
    public MasterList MasterList;

    private string GetDataPathbyType_JSON(Type type)
    {
        return DataPath[type]+".json";
    }
    private string GetDataPathbyType_XML(Type type)
    {
        return DataPath[type]+".xml";
    }
    private string GetDataPathbyType(Type type)
    {
        return DataPath[type];
    }
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

        ID_Library = new Dictionary<string, object>();
        TooltipLibrary = new Dictionary<string, string>();

        sensitivityClass_statusID_LookUp = new Dictionary<string, string>();

        DataPath = new Dictionary<Type, string>();
        DataPath.Add(typeof(masterList), Application.dataPath + DataPath_Local);

        DataPath.Add(typeof(Character_BaseID_Index), Application.dataPath + "/Data/Characters/");


#if UNITY_EDITOR


#endif

    }

    private void Start()
    {
        // before master dictionary load, central control player pref must exist to check language
        //_masterDictionary = LoadDictionary();
        masterList = LoadDefs();
        MasterList = LoadDefs(true);

        LoadCharactersFoldersJSON();

        masterList.Initialize();
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
    public Traits_Group_Index index_TraitsAll { get { return masterList.Traits_Groups as Traits_Group_Index; } }
    //public Skills_Index index_SkillsAll { get { return masterList.Skills as Skills_Index; } }
    public Stats_Derived_Base_Index index_StatsDerived { get { return masterList.Stats_Derived_Bases as Stats_Derived_Base_Index; }  }

    Index_CampaignSetting index_CampaignSetting { get { return MasterList.CampaignSettings as Index_CampaignSetting; } }
    public Character_Base_Index index_Characters_Bases { get { return masterList.Character_Bases as Character_Base_Index; } }
    public Index_BodyPartBase index_BodyPartBase { get { return masterList.BodyPartBases as Index_BodyPartBase; } }
    public Index_Item_Base index_Item_Base { get { return masterList.Items as Index_Item_Base; } }
    public Index_COM index_COM { get { return MasterList.COMs as Index_COM; } }
    public Index_Floor_Base index_Floor_Base { get { return masterList.Floors as Index_Floor_Base; } }
    public Index_MapPlan index_MapPlan { get { return masterList.MapPlans as Index_MapPlan; } }
    public Index_FurnitureBase index_FurnitureBase { get { return masterList.Furnitures as Index_FurnitureBase; } }
    public Index_Status index_Status { get { return masterList.Status as Index_Status; } }
    public Index_StatusEx index_StatusEX { get { return masterList.StatusEXs as Index_StatusEx; } }
    public Index_Experiences index_Experiences { get { return MasterList.Experiences; } }
    public Index_CharaSkills index_Skills { get { return MasterList.Skills; } }
    public Stats_Derived_Extended_Index index_StatsExtended { get { return masterList.StatEXs as Stats_Derived_Extended_Index; } }
    Humanoid_RaceTemplateAddon_Index index_HRTemplateAddon;

    Dictionary<string, object> ID_Library;
    public void RegisterIDtoLib(string id, object o) {
        if (id.Length > 0)
        {

            if (ID_Library.ContainsKey(id))
            {
                if (id != "trait_neutral") Debug.Log("Serializer Error : library already contains id [" + id + "]");
            }
            else
            {
                //Debug.Log("registering ID [" + id + "]");
                ID_Library.Add(id, o);
            }
        }
    }

    public void RemoveIDfromLib(string id) {
        if (id.Length > 0)
        {
           ID_Library.Remove(id);
        }
    }

    public void SaveAll()
    {
        SaveDataXML(index_TraitsAll.GetType(), index_TraitsAll);
    }

    public void SaveDataJSON(Type type, object obj)
    {
        string s = JsonUtility.ToJson(obj);
        System.IO.File.WriteAllText(GetDataPathbyType_JSON(type), s);
    }

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

    public object LoadDataJSON(Type type, string path)
    {
        return JsonConvert.DeserializeObject(File.ReadAllText(path+".json"),type);
    }


    public void LoadCharactersFoldersJSON()
    {

        var newIndex = new Character_Base_Index();
        var newIndex2 = new Character_Trainable_SerializableTemplate_Index();
        string path = GetDataPathbyType(typeof(Character_BaseID_Index));
        if (Directory.Exists(path))
        {
            DirectoryInfo d = new DirectoryInfo(path);
            foreach (var file in d.GetFiles("*.json"))
            {
                var i = JsonConvert.DeserializeObject<Character_Trainable>(File.ReadAllText(file.FullName), Utility.SerializerSettings);
                if (i == null || i.BaseID == null) continue;
                newIndex.baseCharacters.Add(i);
                i.FileLocation = file.FullName;

                Character_Trainable_SerializableTemplate ii = JsonConvert.DeserializeObject<Character_Trainable_SerializableTemplate>(File.ReadAllText(file.FullName), Utility.SerializerSettings);
                ii.FileLocation = file.FullName;
                newIndex2.list.Add(ii);
            }
            foreach (var folder in d.GetDirectories())
            {
                foreach (var file in folder.GetFiles("*.json"))
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
        }

        Debug.Log("Adding "+newIndex.baseCharacters.Count+" characters to masterlist");
        masterList.Character_Bases = newIndex;
        MasterList.CharacterTemplates = newIndex2;
    }

    /*
    public Index_COM LoadCOMFolderJSON()
    {
        string path = GetDataPathbyType(typeof(Index_COM));
        Index_COM i = new Index_COM();
        if (Directory.Exists(path))
        {
            DirectoryInfo d = new DirectoryInfo(path);

            foreach (var folder in d.GetDirectories())
            {
                foreach (var file in folder.GetFiles("*.json"))
                {
                    Debug.Log("reading standalone COM file : " + file.FullName);
                    //COM c = LoadDataJSON(typeof(COM), file.FullName) as COM;
                    COM com = JsonUtility.FromJson(File.ReadAllText(file.FullName), typeof(COM)) as COM;
                    //COM com = JsonConvert.DeserializeObject<COM>(File.ReadAllText(file.FullName), new JsonSerializerSettings { MaxDepth = 20 });
                    if (com != null)
                    {
                        //com.OnAfterDeserialize();
                        i.list.Add(com);
                    }
                }
            }
            i.OnAfterDeserialize();

            foreach (var file in d.GetFiles("*.json"))
            {   // all files must be index_com files
                Debug.Log("reading index COM file : " + file.FullName);

                i.AppendList(LoadDataJSON(typeof(Index_COM), file.FullName) as Index_COM);
            }
        }
        else
        {
            Debug.Log("reading personality file ERROR directory not exist");
        }


        return i;
    }*/

    public masterList LoadDefs()
    {
        string path = GetDataPathbyType(typeof(masterList));
        masterList i = new masterList();
        i.InitializeLists();
        if (Directory.Exists(path))
        {
            DirectoryInfo d = new DirectoryInfo(path);
            foreach (var file in d.GetFiles("*.json", SearchOption.AllDirectories))
            {   // all files must be index_com files
                masterList l = JsonUtility.FromJson(File.ReadAllText(file.FullName), typeof(masterList)) as masterList;
                if (l != null) i.MergeWith(l);
            }
        }
        return i;
    }

    public MasterList LoadDefs(bool val = true)
    {
        string path = GetDataPathbyType(typeof(masterList));
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
        string path = GetDataPathbyType(typeof(masterList));
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

    /// <summary>
    /// takes a copy of the objectm and serialize it into the desired object
    /// </summary>
    /// <param name="type"></param>
    /// <param name="path"></param>
    /// <param name="reference"></param>
    public void SaveDataXML(Type type, object reference, string path = null)
    {
        XmlSerializer serializer = new XmlSerializer(type);
        FileStream stream;
        if (path != null)
        {
            stream = new FileStream(path, FileMode.Create);
        }
        else
        {
            stream = new FileStream(GetDataPathbyType_XML(type), FileMode.Create);
        }
        serializer.Serialize(stream, reference);
        stream.Close();
    }

    /// <summary>
    /// return the deserialized index object
    /// </summary>
    /// <param name="type"></param>
    /// <param name="path"></param>
    /// <returns></returns>
    public object LoadDataXML(Type type, string path = null)
    {
        object data;
        XmlSerializer serializer = new XmlSerializer(type);
        FileStream stream;
        if (path != null)
        {
            stream = new FileStream(path, FileMode.Open);
        }
        else
        {
            stream = new FileStream(GetDataPathbyType(type), FileMode.Open);
        }
        data = serializer.Deserialize(stream);
        stream.Close();
        return data;
    }

    public List<ItemComponentTemplate_Craftable_Recipe> CraftingRecipe = new List<ItemComponentTemplate_Craftable_Recipe>();

    public void AddCraftingRecipe(List<ItemComponentTemplate_Craftable_Recipe> recipeList)
    {
        foreach (var c in recipeList)
        {
            if (!CraftingRecipe.Contains(c))
            {
                CraftingRecipe.Add(c);
                scr_System_tooltipDictionary.current.AddEntry(c.RecipeUID, c.Tooltip);
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
        if (ID_Library.ContainsKey(name_or_id)) return ID_Library[name_or_id] as Traits;
        else return null;
    }
    public scr_Traits_Group GetByNameOrID_TraitsGroup(string name_or_id)
    {
        if (ID_Library.ContainsKey(name_or_id)) return ID_Library[name_or_id] as scr_Traits_Group;
        else return null;
    }

    public Skills_Full GetByNameOrID_Skills(string name_or_id)
    {
        if (ID_Library.ContainsKey(name_or_id)) return ID_Library[name_or_id] as Skills_Full;
        else return null;
    }

    public Stats_Derived_Base GetByNameOrID_StatsDerivedBase(string name_or_id)
    {
        if (ID_Library.ContainsKey(name_or_id)) return ID_Library[name_or_id] as Stats_Derived_Base;
        else return null;
    }

    public Item_Base GetByNameOrID_Item_Base(string name_or_id){
        if (ID_Library.ContainsKey(name_or_id)) return ID_Library[name_or_id] as Item_Base;
        else return null;
    }

    public BodyPart_Base GetByNameOrID_BodyPart_Base(string name_or_id)
    {
        if (ID_Library.ContainsKey(name_or_id)) return ID_Library[name_or_id] as BodyPart_Base;
        else return null;
    }

    public COM GetByNameOrID_COM(string name_or_id)
    {
        if (ID_Library.ContainsKey(name_or_id)) return ID_Library[name_or_id] as COM;
        else return null;
    }

    public BodyInternal_Base GetByNameOrID_BodyInternal_Base(string name_or_id)
    {
        if (ID_Library.ContainsKey(name_or_id)) return ID_Library[name_or_id] as BodyInternal_Base;
        else return null;
    }

    public CharaSkill GetByNameOrID(string name_or_id)
    {
        if (ID_Library.ContainsKey(name_or_id)) return ID_Library[name_or_id] as CharaSkill;
        else return null;
    }
    public Floor_Base GetByNameOrID_Floor_Base(string name_or_id)
    {
        if (ID_Library.ContainsKey(name_or_id)) return ID_Library[name_or_id] as Floor_Base;
        else return null;
    }
    public MapPlan GetByNameOrID_MapPlan(string name_or_id)
    {
        if (ID_Library.ContainsKey(name_or_id)) return ID_Library[name_or_id] as MapPlan;
        else return null;
    }

    public FurnitureBase GetByNameOrID_FurnitureBase(string name_or_id)
    {
        if (ID_Library.ContainsKey(name_or_id)) return ID_Library[name_or_id] as FurnitureBase;
        else return null;
    }

    public Status_Base GetByNameOrID_Status_Base(string name_or_id)
    {
        if (name_or_id == null || name_or_id == "") return null;
        if (ID_Library.ContainsKey(name_or_id)) return ID_Library[name_or_id] as Status_Base;
        else return null;
    }

    public StatusEx_Base GetByNameOrID_StatusEx_Base(string name_or_id)
    {
        if (name_or_id == null || name_or_id == "") return null;
        if (ID_Library.ContainsKey(name_or_id)) return ID_Library[name_or_id] as StatusEx_Base;
        else return null;
    }

    public Stats_Derived_Extended GetByNameOrID_StatusEx(string name_or_id)
    {
        if (name_or_id == null || name_or_id == "") return null;
        if (ID_Library.ContainsKey(name_or_id)) return ID_Library[name_or_id] as Stats_Derived_Extended;
        else return null;
    }

    public Sexperience_Base GetByNameOrID_ExperienceBase(string name_or_id)
    {
        if (ID_Library.ContainsKey(name_or_id)) return ID_Library[name_or_id] as Sexperience_Base;
        else return null;
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
public class SerializedcustomVerb
{
    public string verbClassString;
    public string name;
    public VerbBase verbBase
    {
        get
        {
            if (v == null) v = Activator.CreateInstance(Type.GetType(verbClassString)) as VerbBase;
            return v;
        }
    }
    public VerbBase v = null;
}

[System.Serializable]
public class Skills_Index : I_IndexHasID, I_IndexHasTooltip, I_IndexMergeable
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

    public void RegisterAllID()
    {
        Debug.Log("Skills_Index : registering ID with list length [" + list.Count + "]");

        foreach (Skills_Full o in this.list)
        {
            scr_System_Serializer.current.RegisterIDtoLib(o.ID, o);
        }
    }
    public void RegisterAllTooltip()
    {
        foreach (Skills_Full o in this.list)
        {
            scr_System_tooltipDictionary.current.AddEntry(o.ID, o.tooltip);
        }
    }
}


[System.Serializable]
public class Humanoid_RaceTemplateAddon_Index
{
    public List<Humanoid_RaceTemplateAddon> list = new List<Humanoid_RaceTemplateAddon>();
}

[System.Serializable]
public class Traits_Group_Index : I_IndexHasID, I_IndexHasTooltip, I_NeedLateInitialize, I_IndexMergeable
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
    public List<List<scr_Traits_Group>> traits_All
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
    public void RegisterAllID()
    {
        Debug.Log("Traits_Group_Index : registering ID with list length [" + traits_All.Count + "]");

        foreach (List<scr_Traits_Group> o in traits_All)
        {
            foreach (scr_Traits_Group s in o)
            {
                scr_System_Serializer.current.RegisterIDtoLib(s.ID, s);
                foreach (Traits t in s.entries)
                {
                    scr_System_Serializer.current.RegisterIDtoLib(t.ID, t);
                }

            }
        }
    }
    public void RegisterAllTooltip()
    {
        foreach (List<scr_Traits_Group> o in traits_All)
        {
            foreach (scr_Traits_Group s in o)
            {
                scr_System_tooltipDictionary.current.AddEntry(s.ID, s.tooltip);
                foreach (Traits t in s.entries)
                {
                    scr_System_tooltipDictionary.current.AddEntry(t.ID, t.tooltip);
                }

            }
        }
    }
}

public interface I_CharacterStatBase
{

}

