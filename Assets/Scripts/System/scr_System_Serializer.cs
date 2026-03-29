using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;
using Newtonsoft.Json;
using System.Text.RegularExpressions;
using QuikGraph;


public class scr_System_Serializer : MonoBehaviour
{
    //private Dictionary<Type, string> DataPath;
    string _datapath = "";

    public bool Debug_KojoIntegrityCheck = false;

    public static string DataPath
    {
        get
        {
            if (current._datapath == "") current._datapath = Directory.GetParent(Application.dataPath).FullName + "/Data";
            return current._datapath;
        }
    }

    string _savePath = "";
    public static string SavePath
    {
        get
        {
            if (current._savePath == "") current._savePath = Directory.GetParent(Application.dataPath).FullName + "/Save";
            return current._savePath;
        }
    }

    string _autosavePath = "";
    public static string AutosavePath
    {
        get
        {
            if (current._autosavePath == "") current._autosavePath = $"{SavePath}/AutoSave.json";
            return current._autosavePath;
        }
    }

    string _presetPath = "";
    public static string PresetPath
    {
        get
        {
            if (current._presetPath == "") current._presetPath = Directory.GetParent(Application.dataPath).FullName + "/Presets";
            return current._presetPath;
        }
    }


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

    public Dictionary<string, string> ShortFileAddress = new Dictionary<string, string>();
    public string GetFullPath(string p)
    {
        return ShortFileAddress.ContainsKey(p) ? ShortFileAddress[p] : p;
    }

    public List<string> GetAllImageFilesInFolder(string folderPath)
    {
        //Debug.Log($"GetAllImageFilesWithRegex {folderPath}");
        var results = new List<string>();
        foreach(var i in ShortFileAddress)
        {
            var extension = Path.GetExtension(i.Value).ToLower();
            if (extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".webp")
            {
                if (i.Key.Contains($"{folderPath}/"))
                {
                    //Debug.Log($"GetAllImageFilesInFolder {folderPath} Match with {i.Key}");
                    results.Add(i.Key);
                }
            }
        }
        return results;
    }

    private void BuildAddressables(bool safeMode)
    {
       // var settings = AddressableAssetSettingsDefaultObject.Settings;

        List<string> skippedFiles = new List<string>();
        List<string> loadedFiles = new List<string>();
        string path = DataPath;
        if (!Directory.Exists(path)) { Debug.LogError($"Error in LoadDefs, path [{path}] do not exist"); return; }

        DirectoryInfo d = new DirectoryInfo(path);
        int appDataLen = d.Parent.Parent.FullName.Length + 1;
        var safe = "safeOnly";

        foreach (var file in d.GetFiles("*.*", SearchOption.AllDirectories))
        {   // all files must be index_com files
            if (file.Extension == ".meta" || file.Extension == ".json") continue;

            bool skipped = false;
            if (safeMode)
            {
                foreach (var s in nsfwKeywords)
                {
                    if (file.Name.Contains(s, StringComparison.InvariantCultureIgnoreCase) || file.DirectoryName.Contains(s, StringComparison.InvariantCultureIgnoreCase))
                    {
                        skipped = true;
                        skippedFiles.Add($"Skipping file {file.Name} due to safeMode toggle");
                        break;
                    }
                }
            }
            else
            {
                if (file.Name.Contains(safe, StringComparison.InvariantCultureIgnoreCase) || file.DirectoryName.Contains(safe, StringComparison.InvariantCultureIgnoreCase))
                {
                    skippedFiles.Add($"Skipping file {file.Name} due to not in safeMode");
                    continue;
                }
            }
            if (skipped) continue;
            //string guidPath = file.FullName.Remove(0, appDataLen).Replace("\\","/");

            //Debug.Log($"Reading json file {file.Name} into {filepath}, guid {guidPath}");
            /*
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(guidPath);
            if (asset == null)
            {
                Debug.LogError($"alert asset do not exist at {guidPath}");
                continue;
            }
            
            var guid = AssetDatabase.AssetPathToGUID(guidPath);
            if (string.IsNullOrEmpty(guid))
            {
                Debug.LogError($"alert null guid for {guidPath}");
                continue;
            }*/

            ShortFileAddress.Add($"{file.Directory.Name}/{file.Name}", file.FullName);
            ShortFileAddress.Add($"{file.Directory.Parent.Name}/{file.Directory.Name}/{file.Name}", file.FullName);

            //var entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
            //entry.address = filepath;
            //if (file.FullName.Contains("arin")) loadedFiles.Add($"Reading json file {file.FullName} into {file.Directory.Name}/{file.Name}");
            //Debug.Log($"reading directory {file.Name} in {file.FullName} in {file.DirectoryName} in {file.DirectoryName}");
        }
       // AssetDatabase.SaveAssets();
        Debug.Log($"BuildAddressables Complete! \n-- Loaded Files Count {loadedFiles.Count} --\n{String.Join("\n", loadedFiles)}\n-- Skipped Files Count {skippedFiles.Count} --\n{String.Join("\n", skippedFiles)}");

    }


    protected void CustomCalls()
    {

    }

    bool initialized = false;
    private void Update()
    {
        if (!initialized)
        {
            initialized = true;

            CustomCalls();

            BuildAddressables(scr_System_CentralControl.current.isSafeMode);

            // before master dictionary load, central control player pref must exist to check language
            LoadDefs(scr_System_CentralControl.current.isSafeMode);

            // parkour every file
            LoadCharactersFoldersJSON();
            LoadCharactersPresetsJSON();

#if UNITY_EDITOR
            if (scr_System_CentralControl.current.isSafeMode)
            {   // update safelist
                SafeList.InitializeLists();

                SafeList.MergeWith(MasterList);

                MasterList = SafeList;
                CharaOrigins.Instance.Humanoid_Race_Index = SafeList.humanoid_Races;
                CharaOrigins.Instance.Origins_Index = SafeList.Character_Origins;
                CharaOrigins.Instance.StartingOption_Index = SafeList.Character_Origin_StartingOptions;
                CharaOrigins.Instance.RaceTemplateIndex = SafeList.humanoid_RaceTemplates;
                CharaOrigins.Instance.BodyPartIndex = SafeList.BodyPartBases;
                CharaOrigins.Instance.Traits = SafeList.Traits_Groups;
                Masterlist_Items.Instance.Index = SafeList.Items;
                LocalizeDictionary.Instance.Index = SafeList.Dictionary;

                Expeditions.Instance.ResetMasterlist();

                MasterList.RemoveNSFW();
            }

            // update untranslated list
            Dictionary_Index untranslated = new Dictionary_Index();
            var baseDict = LocalizeDictionary.Instance.Index.Entries["zh-cn"];
            foreach (var language in LocalizeDictionary.Instance.Index.Languages)
            {
                if (language == "zh-cn") continue;
                var currentLib = LocalizeDictionary.Instance.Index.Entries[language];
                var currentTarget = untranslated.Entries[language];
                foreach (var key in baseDict.Keys)
                {
                    if (!currentLib.ContainsKey(key)) currentTarget.Add(key, baseDict[key]);
                }
            }

            string untransDictPath = Application.dataPath + "/untranslatedDict.json";

            var s2 = JsonConvert.SerializeObject(untranslated, formatting: Formatting.Indented, UtilityEX.SerializerSettings);
            if (File.Exists(untransDictPath)) File.Delete(untransDictPath);

            FileInfo untransDict = new System.IO.FileInfo(untransDictPath);
            untransDict.Directory.Create();
            File.WriteAllText(untransDict.FullName, s2);
            Debug.Log($"creating/updating untranslated Dictionary in {untransDictPath}");
#endif
            MasterList.Initialize();

#if UNITY_EDITOR

            if (scr_System_CentralControl.current.isSafeMode)
            {
                MasterList.RemoveNonExisting();

                string safeListpath = Application.dataPath + " /safeMasterList.json";
                var s = JsonConvert.SerializeObject(SafeList, formatting: Formatting.Indented, UtilityEX.SerializerSettings);
                if (File.Exists(safeListpath)) File.Delete(safeListpath);

                FileInfo safeFile = new System.IO.FileInfo(safeListpath);
                safeFile.Directory.Create();
                File.WriteAllText(safeFile.FullName, s);
                Debug.Log($"creating/updating safeList in {safeListpath}");
            }

#endif

            scr_System_CentralControl.current.NotifyLoadComplete();
        }
    }

    private void Start()
    {
        // build addressable
        
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
        string serialized_chara = JsonConvert.SerializeObject(obj, UtilityEX.SerializerSettings);
        FileInfo filepath = new System.IO.FileInfo($"{PresetPath}/{obj.FirstName}_{(obj.MiddleName.Length < 1 ? "" : $"{obj.MiddleName}_")}{obj.LastName}.json");

        if (scr_System_CentralControl.current.isSafeMode)
        {
            var chara = JsonConvert.DeserializeObject<Character_SerializableSafe>(serialized_chara, UtilityEX.SerializerSettings);
            chara.Template = obj.Template as CharaSafeTemplate;
            chara.playable = true;
            chara.baseID = filepath.Name;
            serialized_chara = JsonConvert.SerializeObject(chara, UtilityEX.SerializerSettings);

            scr_System_Serializer.current.MasterList.Character_Bases.SetChara(chara);

            var template = new CharaSerializableTemplate_Safe();
            template.baseID = filepath.Name;
            template.Template = obj.Template as CharaSafeTemplate;
            scr_System_Serializer.current.MasterList.CharacterTemplates.SetTemplate(template);
        }
        else
        {
            var chara = JsonConvert.DeserializeObject<Character_SerializableTrainable>(serialized_chara, UtilityEX.SerializerSettings);
            chara.Template = obj.Template as CharaTrainableTemplate;
            chara.playable = true;
            chara.baseID = filepath.Name;
            serialized_chara = JsonConvert.SerializeObject(chara, UtilityEX.SerializerSettings);

            scr_System_Serializer.current.MasterList.Character_Bases.SetChara(chara);

            var template = new CharaSerializableTemplate_Trainable();
            template.baseID = filepath.Name;
            template.Template = obj.Template as CharaTrainableTemplate;
            scr_System_Serializer.current.MasterList.CharacterTemplates.SetTemplate(template);
        }


        filepath.Directory.Create();
        File.WriteAllText(filepath.FullName, serialized_chara);

    }

    protected void LoadCharactersFoldersJSON()
    {
        List<string> skippedFiles = new List<string>();
        List<string> loadedFiles = new List<string>();

        var newIndex = MasterList.Character_Bases;
        var newIndex2 = MasterList.CharacterTemplates;
        string path = DataPath;

        bool safeMode = scr_System_CentralControl.current.isSafeMode;
        if (Directory.Exists(path))
        {
            DirectoryInfo d = new DirectoryInfo(path);

            //foreach (var file in d.GetFiles("*.json"))
            foreach (var file in d.GetFiles("*.json", SearchOption.AllDirectories))
            {
                if (safeMode)
                {
                    bool skipped = false;
                    foreach (var s in nsfwKeywords)
                    {
                        if (file.Name.Contains(s, StringComparison.InvariantCultureIgnoreCase) || file.DirectoryName.Contains(s, StringComparison.InvariantCultureIgnoreCase))
                        {
                            skipped = true;
                            skippedFiles.Add($"Skipping file {file.Name} due to safeMode toggle");
                            break;
                        }
                    }
                    if (skipped) continue;

                    var i = JsonConvert.DeserializeObject<Character_SerializableSafe>(File.ReadAllText(file.FullName), UtilityEX.SerializerSettings);
                    if (i == null || i.baseID == null || i.baseID.Length < 1) continue;
                    newIndex.baseCharacters.Add(i);

                    var ii = JsonConvert.DeserializeObject<CharaSerializableTemplate_Safe>(File.ReadAllText(file.FullName), UtilityEX.SerializerSettings);
                    newIndex2.list.Add(ii);
                }
                else
                {
                    var i = JsonConvert.DeserializeObject<Character_SerializableTrainable>(File.ReadAllText(file.FullName), UtilityEX.SerializerSettings);
                    if (i == null || i.baseID == null || i.baseID.Length < 1) continue;
                    newIndex.baseCharacters.Add(i);

                    var ii = JsonConvert.DeserializeObject<CharaSerializableTemplate_Trainable>(File.ReadAllText(file.FullName), UtilityEX.SerializerSettings);
                    newIndex2.list.Add(ii);
                }
                loadedFiles.Add($"Reading json file {file.FullName}");

            }
        }

        Debug.Log($"LoadCharactersFoldersJSON Complete! \n-- Loaded Files Count {loadedFiles.Count} --\n{String.Join("\n", loadedFiles)}\n-- Skipped Files Count {skippedFiles.Count} --\n{String.Join("\n", skippedFiles)}");
        //Debug.Log("Adding "+newIndex.baseCharacters.Count+" characters to masterlist");
    }

    protected void LoadCharactersPresetsJSON()
    {

        List<string> skippedFiles = new List<string>();
        List<string> loadedFiles = new List<string>();


        var newIndex = MasterList.Character_Bases;
        var newIndex2 = MasterList.CharacterTemplates;
        string path = PresetPath;

        bool safeMode = scr_System_CentralControl.current.isSafeMode;
        if (Directory.Exists(path))
        {
            DirectoryInfo d = new DirectoryInfo(path);

            //foreach (var file in d.GetFiles("*.json"))
            foreach (var file in d.GetFiles("*.json", SearchOption.AllDirectories))
            {
                if (safeMode)
                {
                    bool skipped = false;
                    foreach (var s in nsfwKeywords)
                    {
                        if (file.Name.Contains(s, StringComparison.InvariantCultureIgnoreCase) || file.DirectoryName.Contains(s, StringComparison.InvariantCultureIgnoreCase))
                        {
                            skipped = true;
                            skippedFiles.Add($"Skipping file {file.Name} due to safeMode toggle");
                            break;
                        }
                    }
                    if (skipped) continue;

                    var i = JsonConvert.DeserializeObject<Character_SerializableSafe>(File.ReadAllText(file.FullName), UtilityEX.SerializerSettings);
                    if (i == null) continue;
                    if (i.baseID == "" || i.baseID.Length < 1) i.baseID = file.Name;
                    newIndex.baseCharacters.Add(i);

                    var ii = JsonConvert.DeserializeObject<CharaSerializableTemplate_Safe>(File.ReadAllText(file.FullName), UtilityEX.SerializerSettings);
                    if (ii.baseID == "" || ii.baseID.Length < 1) ii.baseID = file.Name;
                    newIndex2.list.Add(ii);
                }
                else
                {
                    var i = JsonConvert.DeserializeObject<Character_SerializableTrainable>(File.ReadAllText(file.FullName), UtilityEX.SerializerSettings);
                    if (i == null) continue;
                    if (i.baseID == "" || i.baseID.Length < 1) i.baseID = file.Name;
                    newIndex.baseCharacters.Add(i);

                    var ii = JsonConvert.DeserializeObject<CharaSerializableTemplate_Trainable>(File.ReadAllText(file.FullName), UtilityEX.SerializerSettings);
                    if (ii.baseID == "" || ii.baseID.Length < 1) ii.baseID = file.Name;
                    newIndex2.list.Add(ii);
                }
                loadedFiles.Add($"Reading json file {file.FullName}");

            }
        }

        Debug.Log($"LoadCharactersPresetsJSON Complete! \n-- Loaded Files Count {loadedFiles.Count} --\n{String.Join("\n", loadedFiles)}\n-- Skipped Files Count {skippedFiles.Count} --\n{String.Join("\n", skippedFiles)}");
        //Debug.Log("Adding "+newIndex.baseCharacters.Count+" characters to masterlist");
    }

    MasterList SafeList = new MasterList();

    protected void LoadDefs(bool safeMode)
    {
        List<string> skippedFiles = new List<string>();
        List<string> loadedFiles = new List<string>();
        string path = DataPath;
        MasterList = new MasterList();
        MasterList.InitializeLists(true);
        if (!Directory.Exists(path)) { Debug.LogError($"Error in LoadDefs, path [{path}] do not exist"); return; }

        var safe = "safeOnly";
        DirectoryInfo d = new DirectoryInfo(path);
        foreach (var file in d.GetFiles("*.json", SearchOption.AllDirectories))
        {   // all files must be index_com files
            bool skipped = false;
            if (safeMode)
            {
                foreach(var s in nsfwKeywords) 
                {
                    if (file.Name.Contains(s, StringComparison.InvariantCultureIgnoreCase) || file.DirectoryName.Contains(s, StringComparison.InvariantCultureIgnoreCase))
                    {
                        skipped = true;
                        skippedFiles.Add($"Skipping file {file.Name} due to safeMode toggle");
                        break;
                    }
                }
            }
            else
            {
                if (file.Name.Contains(safe, StringComparison.InvariantCultureIgnoreCase) || file.DirectoryName.Contains(safe, StringComparison.InvariantCultureIgnoreCase))
                {
                    skippedFiles.Add($"Skipping file {file.Name} due to not in safeMode");
                    continue;
                }
            }
            if (skipped) continue;
            //Debug.Log($"reading directory {file.Name} in {file.FullName} in {file.DirectoryName} in {file.DirectoryName}");
            MasterList l = JsonConvert.DeserializeObject<MasterList>(File.ReadAllText(file.FullName),UtilityEX.SerializerSettings);
            if (l != null)
            {
                MasterList.MergeWith(l);
                loadedFiles.Add($"Reading json file {file.FullName}");
            }
        }
        Debug.Log($"LoadDefs Complete! \n-- Loaded Files Count {loadedFiles.Count} --\n{String.Join("\n", loadedFiles)}\n-- Skipped Files Count {skippedFiles.Count} --\n{String.Join("\n", skippedFiles)}");
    }
  
    public List<string> nsfwKeywords = new List<string>() { "nsfw", "unsafe", "sex", "initSex", "endSex", "massage", "do_not_use" };

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

    public Stats_Derived_Extended GetByNameOrID_StatsEx(string name_or_id)
    {
        return MasterList.StatEXs.GetByID(name_or_id);
    }

    Dictionary<Item_Base, List<CombatAction>> _CachedCombatActions_Item = new Dictionary<Item_Base, List<CombatAction>>();
    public List<CombatAction> GetCombatActions(Item_Base item)
    {
        if (_CachedCombatActions_Item.ContainsKey(item)) return _CachedCombatActions_Item[item];

        var results = new List<CombatAction>();
        foreach (var entry in MasterList.CombatActions.list)
        {
            if (entry.itemRequirement == null) continue;
            if (!entry.itemRequirement.isActive) continue;
            if (entry.itemRequirement.Validate(item)) results.Add(entry);
        }

        _CachedCombatActions_Item.Add(item, results);
        return results;
    }

    Dictionary<BodyPart_Base, List<CombatAction>> _CachedCombatActions_Part = new Dictionary<BodyPart_Base, List<CombatAction>>();
    public List<CombatAction> GetCombatActions(BodyPart_Base part)
    {
        if (_CachedCombatActions_Part.ContainsKey(part)) return _CachedCombatActions_Part[part];

        var results = new List<CombatAction>();
        foreach (var entry in MasterList.CombatActions.list)
        {
            if (entry.itemRequirement == null) continue;
            if (!entry.itemRequirement.isActive) continue;
            if (entry.itemRequirement.Validate(part.tags)) results.Add(entry);
        }

        _CachedCombatActions_Part.Add(part, results);
        return results;
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
    public void RegisterAllID(List<string> messages)
    {
        messages.Add("Skills_Index : registering ID with list length [" + list.Count + "]");

        foreach (Skills_Full o in this.list)
        {
            ID_Dictionary.Add(o.ID, o);
        }
    }
    public Skills_Full GetByID(string id) { return ID_Dictionary.ContainsKey(id) ? ID_Dictionary[id] : null; }
}

