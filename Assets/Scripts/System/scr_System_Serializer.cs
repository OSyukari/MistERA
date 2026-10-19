using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;
using Newtonsoft.Json;


public class scr_System_Serializer : MonoBehaviour
{
    //private Dictionary<Type, string> DataPath;
    string _datapath = "";

    public bool Debug_KojoIntegrityCheck = false;

    public bool DisableFontFallback = false;

    public static string DataPath
    {
        get
        {
            if (current._datapath == "") current._datapath = Directory.GetParent(Application.dataPath).FullName + "/Data";
            return current._datapath;
        }
    }

    string _modsPath = "";
    public static string ModsPath
    {
        get
        {
            if (current._modsPath == "") current._modsPath = Directory.GetParent(Application.dataPath).FullName + "/Mods";
            return current._modsPath;
        }
    }

    public static string AssetPacksPath { get { return $"{ModsPath}/Assetpacks"; } }

    // filled at boot by AssetPackLoader.Scan, in load order
    public List<AssetPackInfo> AssetPacks = new List<AssetPackInfo>();
    // filled at boot by ModLoader.Scan, in load order (valid mods only, enabled or not)
    public List<ModInfo> Mods = new List<ModInfo>();

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

    string _checkpointPath = "";
    /// <summary>
    /// Disposable checkpoint saves for the agentic LLM tool-calling loop (see LLMCheckpointStore) - a
    /// subdirectory of SavePath, which the player-facing save browser (scr_Canvas_LoadSave.
    /// BuildSaveButtons) never scans since it only lists "*.json" directly inside SavePath itself,
    /// non-recursively. No exclusion logic needed on the UI side, just this directory separation.
    /// </summary>
    public static string CheckpointPath
    {
        get
        {
            if (current._checkpointPath == "") current._checkpointPath = $"{SavePath}/Checkpoints";
            return current._checkpointPath;
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

    /// <summary>
    /// Global portrait template (GlobalPortraitTemplate.Initialize at boot): handler list as JSON with $foldername$, null = disabled
    /// </summary>
    [System.NonSerialized] public string GlobalPortraitTemplateJson = null;
    /// <summary>
    /// Global portrait template expanded and resolved per character folder (GlobalPortraitTemplate.GetHandlers), filled on first use
    /// </summary>
    [System.NonSerialized] public Dictionary<string, List<PortraitManager.CharaPortrait>> GlobalPortraitByFolder = new Dictionary<string, List<PortraitManager.CharaPortrait>>(StringComparer.OrdinalIgnoreCase);
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

    static bool IsImageExtension(string path)
    {
        var extension = Path.GetExtension(path).ToLower();
        return extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".webp";
    }

    /// <summary>
    /// True if key is a registered short address (see BuildAddressables) of an image file.
    /// </summary>
    public bool ImageFileExists(string key)
    {
        return ShortFileAddress.TryGetValue(key, out var fullPath) && IsImageExtension(fullPath);
    }

    /// <summary>
    /// Every image short address that fully matches pattern (anchored, case-insensitive).
    /// One file is registered under several keys (name, dir/name, parent/dir/name); only the first matching key per file is returned.
    /// Returns an empty list if pattern is not a valid regex.
    /// </summary>
    public List<string> GetAllImageFilesByRegex(string pattern)
    {
        var results = new List<string>();
        System.Text.RegularExpressions.Regex regex;
        try
        {
            regex = new System.Text.RegularExpressions.Regex($"^(?:{pattern})$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        }
        catch (ArgumentException e)
        {
            Debug.LogWarning($"GetAllImageFilesByRegex invalid regex [{pattern}]: {e.Message}");
            return results;
        }
        var seenFiles = new HashSet<string>();
        foreach (var i in ShortFileAddress)
        {
            if (!IsImageExtension(i.Value)) continue;
            if (!regex.IsMatch(i.Key)) continue;
            if (seenFiles.Add(i.Value)) results.Add(i.Key);
        }
        return results;
    }

    private void BuildAddressables(bool safeMode)
    {
       // var settings = AddressableAssetSettingsDefaultObject.Settings;

        List<string> skippedFiles = new List<string>();
        List<string> loadedFiles = new List<string>();
        List<string> errors = new List<string>();
        string path = DataPath;
        if (!Directory.Exists(path)) { Debug.LogError($"Error in LoadDefs, path [{path}] do not exist"); return; }

        DirectoryInfo d = new DirectoryInfo(path);

        foreach (var file in d.GetFiles("*.*", SearchOption.AllDirectories))
        {   // all files must be index_com files
            RegisterAddressableFile(file, safeMode, "Data", skippedFiles, errors);
        }

        // data mods: non-json files registered as if inside Data, after Data, in load order (About/ and Patches/ excluded)
        List<string> packSummary = new List<string>();
        foreach (var mod in Mods)
        {
            if (!mod.enabled) continue;
            int count = 0;
            foreach (var file in new DirectoryInfo(mod.folderPath).GetFiles("*.*", SearchOption.AllDirectories))
            {
                if (!mod.IsDataFile(file)) continue;
                if (RegisterAddressableFile(file, safeMode, mod.id, skippedFiles, errors)) count++;
            }
            packSummary.Add($"[mod {mod.id}] {mod.DisplayName}: {count} files");
        }

        // asset packs: registered after Data and mods, in load order. Conflicting keys are logged and ignored (first registration wins)
        foreach (var pack in AssetPacks)
        {
            if (!pack.enabled)
            {
                packSummary.Add($"[{pack.id}] disabled");
                continue;
            }
            int count = 0;
            foreach (var file in new DirectoryInfo(pack.folderPath).GetFiles("*.*", SearchOption.AllDirectories))
            {
                if (AssetPackLoader.IsInAbout(pack, file)) continue;
                if (RegisterAddressableFile(file, safeMode, pack.id, skippedFiles, errors)) count++;
            }
            packSummary.Add($"[{pack.id}] {pack.DisplayName}: {count} files");
        }

       // AssetDatabase.SaveAssets();
        Debug.Log($"BuildAddressables Complete! \n-- Mods Count {Mods.Count}, Asset Packs Count {AssetPacks.Count} --\n{String.Join("\n", packSummary)}\n-- Loaded Files Count {loadedFiles.Count} --\n{String.Join("\n", loadedFiles)}\n-- Skipped Files Count {skippedFiles.Count} --\n{String.Join("\n", skippedFiles)}\n-- ERRORS COUNT {errors.Count} --\n{String.Join("\n", errors)}");

    }

    /// <summary>
    /// Registers file under its short addresses (name, dir/name, parent/dir/name). Returns false if the file was filtered out.
    /// source is only used for conflict messages (Data or asset pack id).
    /// </summary>
    private bool RegisterAddressableFile(FileInfo file, bool safeMode, string source, List<string> skippedFiles, List<string> errors)
    {
            const string safe = "safeOnly";
            if (file.Extension == ".meta" || file.Extension == ".json") return false;

            if (safeMode)
            {
                foreach (var s in nsfwKeywords)
                {
                    if (file.Name.Contains(s, StringComparison.InvariantCultureIgnoreCase) || file.DirectoryName.Contains(s, StringComparison.InvariantCultureIgnoreCase))
                    {
                        skippedFiles.Add($"Skipping file {file.Name} due to safeMode toggle");
                        return false;
                    }
                }
            }
            else
            {
                if (file.Name.Contains(safe, StringComparison.InvariantCultureIgnoreCase) || file.DirectoryName.Contains(safe, StringComparison.InvariantCultureIgnoreCase))
                {
                    skippedFiles.Add($"Skipping file {file.Name} due to not in safeMode");
                    return false;
                }
            }
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
            if (!ShortFileAddress.TryAdd($"{file.Name}", file.FullName))
            {
                // silent fail is okay
            }
            if (!ShortFileAddress.TryAdd($"{file.Directory.Name}/{file.Name}", file.FullName))
            {
                errors.Add($"conflict key {file.Directory.Name}/{file.Name} from [{source}], ignored");
            }
            if (!ShortFileAddress.TryAdd($"{file.Directory.Parent.Name}/{file.Directory.Name}/{file.Name}", file.FullName))
            {
                errors.Add($"conflict key {file.Directory.Parent.Name}/{file.Directory.Name}/{file.Name} from [{source}], ignored");
            }

            //var entry = settings.CreateOrMoveEntry(guid, settings.DefaultGroup);
            //entry.address = filepath;
            //if (file.FullName.Contains("arin")) loadedFiles.Add($"Reading json file {file.FullName} into {file.Directory.Name}/{file.Name}");
            //Debug.Log($"reading directory {file.Name} in {file.FullName} in {file.DirectoryName} in {file.DirectoryName}");
            return true;
    }


    /// <summary>Registers a newly written file with the same short addresses as boot registration. Never publishes in safe mode.</summary>
    public bool RegisterRuntimeFile(string fullPath)
    {
        if (scr_System_CentralControl.current != null && scr_System_CentralControl.current.isSafeMode) return false;
        var file = new FileInfo(fullPath);
        if (!file.Exists || file.Directory?.Parent == null
            || file.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
            || file.Extension.Equals(".meta", StringComparison.OrdinalIgnoreCase)) return false;
        var key = $"{file.Directory.Parent.Name}/{file.Directory.Name}/{file.Name}";
        if (ShortFileAddress.TryGetValue(key, out var existing)
            && !string.Equals(existing, file.FullName, StringComparison.OrdinalIgnoreCase)) return false;

        var errors = new List<string>();
        if (!RegisterAddressableFile(file, false, "runtime", new List<string>(), errors)) return false;
        foreach (var error in errors) Debug.LogWarning($"RegisterRuntimeFile: {error}");
        return ShortFileAddress.TryGetValue(key, out var registered)
            && string.Equals(registered, file.FullName, StringComparison.OrdinalIgnoreCase);
    }

    protected void CustomCalls()
    {

    }

    public event Action Observer_LoadStart;
    public event Action<float, string> Observer_LoadProgress;
    public event Action Observer_LoadComplete;

    private void Start()
    {
        StartCoroutine(BootSequence());
    }

    private IEnumerator BootSequence()
    {
        Observer_LoadStart?.Invoke();
        Observer_LoadProgress?.Invoke(0.00f, "Loading data...");

        CustomCalls();

        yield return null;
        // user prefs are loaded by now (CentralControl.Start), so pack toggles are readable
        AssetPacks = AssetPackLoader.Scan(AssetPacksPath);
        Mods = ModLoader.Scan(ModsPath, Path.GetFileName(AssetPacksPath));
        // pass 1: collect patches of enabled mods; they are applied to def json text in LoadDefs / LoadCharactersFoldersJSON
        bool bootSafeMode = scr_System_CentralControl.current.isSafeMode;
        ModLoader.LoadPatches(Mods, file => IsDefFileFiltered(file, bootSafeMode, out _));
        BuildAddressables(scr_System_CentralControl.current.isSafeMode);
        GeneratedPortraitRegistry.Initialize(this);

        Observer_LoadProgress?.Invoke(0.10f, "Loading definitions...");
        yield return null;
        // before master dictionary load, central control player pref must exist to check language
        LoadDefs(scr_System_CentralControl.current.isSafeMode);

        Observer_LoadProgress?.Invoke(0.35f, "Loading characters...");
        yield return null;
        // parkour every file
        LoadCharactersFoldersJSON();
        LoadCharactersPresetsJSON();
        ModLoader.ReportAndClear();

#if UNITY_EDITOR
        Observer_LoadProgress?.Invoke(0.50f, "Updating localization tables...");
        yield return null;
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

        // source string tracking: compare current zh-cn against snapshot
        string snapshotPath = Application.dataPath + "/dict_zh_cn_snapshot.json";
        string reportPath = Application.dataPath + "/dict_source_updated.json";

        if (File.Exists(snapshotPath))
        {
            var snapshot = JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(snapshotPath));
            var changedEntries = new Dictionary<string, Dictionary<string, string>>();

            foreach (var kvp in baseDict)
            {
                if (!snapshot.ContainsKey(kvp.Key))
                    changedEntries[kvp.Key] = new Dictionary<string, string> { { "old", null }, { "new", kvp.Value } };
                else if (snapshot[kvp.Key] != kvp.Value)
                    changedEntries[kvp.Key] = new Dictionary<string, string> { { "old", snapshot[kvp.Key] }, { "new", kvp.Value } };
            }

            File.WriteAllText(reportPath, JsonConvert.SerializeObject(changedEntries, Formatting.Indented));

            if (changedEntries.Count > 0)
                Debug.Log($"[SourceTracking] {changedEntries.Count} zh-cn source string(s) changed since snapshot. Other languages may need updating. See dict_source_updated.json");
            else
                Debug.Log("[SourceTracking] No source string changes since snapshot.");
        }
#endif
        Observer_LoadProgress?.Invoke(0.55f, "Finalizing definitions...");
        yield return null;
        MasterList.Initialize();
        GlobalPortraitTemplate.Initialize(this);

        Observer_LoadProgress?.Invoke(0.60f, "Preparing fonts...");
        scr_System_FontManager.Observer_FontProgress += OnFontProgress;
        try
        {
            string currentLang = scr_System_CentralControl.current.Language;
            string currentFontSelection = scr_System_CentralControl.current.DisplaySetting.FontSelection
                .TryGetValue(currentLang, out var fs) ? fs : "";
            yield return scr_System_FontManager.EnsureFontsCoroutine(
                scr_System_CentralControl.current.DefaultFallbackFont, currentLang, currentFontSelection);
        }
        finally
        {
            scr_System_FontManager.Observer_FontProgress -= OnFontProgress;
        }

#if UNITY_EDITOR
        Observer_LoadProgress?.Invoke(0.95f, "Writing debug dumps...");
        yield return null;
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
        else
        {
            string safeListpath = Application.dataPath + " /MasterList.json";
            var s = JsonConvert.SerializeObject(MasterList, formatting: Formatting.Indented, UtilityEX.SerializerSettings);
            if (File.Exists(safeListpath)) File.Delete(safeListpath);

            FileInfo safeFile = new System.IO.FileInfo(safeListpath);
            safeFile.Directory.Create();
            File.WriteAllText(safeFile.FullName, s);
            Debug.Log($"creating/updating Masterlist in {safeListpath}");

        }
#endif

        Observer_LoadProgress?.Invoke(1.00f, "Ready");
        scr_System_CentralControl.current.NotifyLoadComplete();
        Observer_LoadComplete?.Invoke();
    }

    // Font phase occupies the [0.60, 0.95) slice of overall boot progress.
    private void OnFontProgress(string description, int index, int total)
    {
        float t = total > 0 ? (float)index / total : 1f;
        Observer_LoadProgress?.Invoke(0.60f + t * 0.35f, description);
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

            MasterList.Character_Bases.SetChara(chara);

           // var template2 = new CharaSafeTemplate();
            //var template = new CharaSerializableTemplate_Safe();
            //template.baseID = filepath.Name;
            //template.Template = obj.Template as CharaSafeTemplate;
            MasterList.Character_Bases.SetTemplateSafe(filepath.Name, obj.Template as CharaSafeTemplate);
        }
        else
        {
            var chara = JsonConvert.DeserializeObject<Character_SerializableTrainable>(serialized_chara, UtilityEX.SerializerSettings);
            chara.Template = obj.Template as CharaTrainableTemplate;
            chara.playable = true;
            chara.baseID = filepath.Name;
            serialized_chara = JsonConvert.SerializeObject(chara, UtilityEX.SerializerSettings);

            scr_System_Serializer.current.MasterList.Character_Bases.SetChara(chara);

            //var template = new chara
            //var template = new CharaSerializableTemplate_Trainable();
            //template.baseID = filepath.Name;
            //template.Template = obj.Template as CharaTrainableTemplate;
            //scr_System_Serializer.current.MasterList.Character_Bases.SetTemplate(template);
            MasterList.Character_Bases.SetTemplate(filepath.Name, obj.Template as CharaTrainableTemplate);
        }


        filepath.Directory.Create();
        File.WriteAllText(filepath.FullName, serialized_chara);

    }

    protected void LoadCharactersFoldersJSON()
    {
        List<string> skippedFiles = new List<string>();
        List<string> loadedFiles = new List<string>();

        var newIndex = MasterList.Character_Bases;
        string path = DataPath;

        bool safeMode = scr_System_CentralControl.current.isSafeMode;
        if (Directory.Exists(path))
        {
            // Data + enabled mods; text is patched once (ModLoader cache) and shared by both passes
            // a mod character re-declaring an existing baseID keeps the existing one (replace via patches instead)
            bool ModClash(ModInfo mod, string baseID, FileInfo file)
            {
                if (mod == null || !newIndex.baseCharacters.Exists(c => c.baseID == baseID)) return false;
                Debug.LogWarning($"Mod [{mod.id}] character [{baseID}] in {file.Name} already exists, ignored");
                skippedFiles.Add($"Skipping file {file.Name} from mod [{mod.id}] due to duplicate baseID [{baseID}]");
                return true;
            }

            foreach (var (file, relativePath, mod) in GetDefJsonFiles())
            {
                string text = ModLoader.ReadPatched(file, relativePath);
                if (safeMode)
                {
                    var i = JsonConvert.DeserializeObject<Character_SerializableSafe>(text, UtilityEX.SerializerSettings);
                    if (i == null || i.baseID == null || i.baseID.Length < 1) continue;

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
                    if (ModClash(mod, i.baseID, file)) continue;

                    newIndex.baseCharacters.Add(i);

                    var ii = JsonConvert.DeserializeObject<CharaSerializableTemplate_Safe>(text, UtilityEX.SerializerSettings);
                    if (ii.Template != null) newIndex.SetTemplateSafe(string.IsNullOrEmpty(ii.baseID) ? file.Name : ii.baseID, ii.Template);
                }
                else
                {
                    var i = JsonConvert.DeserializeObject<Character_SerializableTrainable>(text, UtilityEX.SerializerSettings);
                    if (i == null || i.baseID == null || i.baseID.Length < 1) continue;
                    if (ModClash(mod, i.baseID, file)) continue;
                    newIndex.baseCharacters.Add(i);

                    var ii = JsonConvert.DeserializeObject<CharaSerializableTemplate_Trainable>(text, UtilityEX.SerializerSettings);
                    //newIndex.SetTemplate(ii);
                    if (ii.Template != null) newIndex.SetTemplate(string.IsNullOrEmpty(ii.baseID) ? file.Name : ii.baseID, ii.Template);
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
                    //if (ii.baseID == "" || ii.baseID.Length < 1) ii.baseID = file.Name;
                    //newIndex.SetTemplate(ii);
                    if (ii.Template != null) newIndex.SetTemplateSafe(string.IsNullOrEmpty(ii.baseID) ? file.Name : ii.baseID, ii.Template);
                }
                else
                {
                    var i = JsonConvert.DeserializeObject<Character_SerializableTrainable>(File.ReadAllText(file.FullName), UtilityEX.SerializerSettings);
                    if (i == null) continue;
                    if (i.baseID == "" || i.baseID.Length < 1) i.baseID = file.Name;
                    newIndex.baseCharacters.Add(i);

                    var ii = JsonConvert.DeserializeObject<CharaSerializableTemplate_Trainable>(File.ReadAllText(file.FullName), UtilityEX.SerializerSettings);
                    //if (ii.baseID == "" || ii.baseID.Length < 1) ii.baseID = file.Name;
                    //newIndex.SetTemplate(ii);
                    if (ii.Template != null) newIndex.SetTemplate(string.IsNullOrEmpty(ii.baseID) ? file.Name : ii.baseID, ii.Template);
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

        foreach (var (file, relativePath, _) in GetDefJsonFiles())
        {   // all files must be index_com files
            if (IsDefFileFiltered(file, safeMode, out var reason))
            {
                skippedFiles.Add($"Skipping file {file.Name} due to {reason}");
                continue;
            }
            //Debug.Log($"reading directory {file.Name} in {file.FullName} in {file.DirectoryName} in {file.DirectoryName}");
            MasterList l;
            try
            {
                l = JsonConvert.DeserializeObject<MasterList>(ModLoader.ReadPatched(file, relativePath), UtilityEX.SerializerSettings);
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to parse masterlist file [{file.FullName}], skipping. Reason: {e.Message}");
                skippedFiles.Add($"Skipping file {file.Name} due to parse failure: {e.Message}");
                continue;
            }
            if (l != null)
            {
                MasterList.MergeWith(l);
                loadedFiles.Add($"Reading json file {file.FullName}");
            }
        }
        Debug.Log($"LoadDefs Complete! \n-- Loaded Files Count {loadedFiles.Count} --\n{String.Join("\n", loadedFiles)}\n-- Skipped Files Count {skippedFiles.Count} --\n{String.Join("\n", skippedFiles)}");
    }

    /// <summary>
    /// Every def json file: Data first, then each enabled mod in load order (About/ and Patches/ excluded).
    /// relativePath is relative to Data or to the mod folder, '/' separated (used by the patch "file" filter). mod is null for Data.
    /// </summary>
    private List<(FileInfo file, string relativePath, ModInfo mod)> GetDefJsonFiles()
    {
        var results = new List<(FileInfo, string, ModInfo)>();
        if (Directory.Exists(DataPath))
        {
            foreach (var file in new DirectoryInfo(DataPath).GetFiles("*.json", SearchOption.AllDirectories))
                results.Add((file, ModLoader.RelativePath(DataPath, file), null));
        }
        foreach (var mod in Mods)
        {
            if (!mod.enabled) continue;
            foreach (var file in new DirectoryInfo(mod.folderPath).GetFiles("*.json", SearchOption.AllDirectories))
            {
                if (!mod.IsDataFile(file)) continue;
                results.Add((file, ModLoader.RelativePath(mod.folderPath, file), mod));
            }
        }
        return results;
    }

    /// <summary>
    /// Safe mode / safeOnly filter used for def json files (and mod patch files).
    /// </summary>
    private bool IsDefFileFiltered(FileInfo file, bool safeMode, out string reason)
    {
        const string safe = "safeOnly";
        if (safeMode)
        {
            foreach (var s in nsfwKeywords)
            {
                if (file.Name.Contains(s, StringComparison.InvariantCultureIgnoreCase) || file.FullName.Contains(s, StringComparison.InvariantCultureIgnoreCase))
                {
                    reason = "safeMode toggle";
                    return true;
                }
            }
        }
        else if (file.Name.Contains(safe, StringComparison.InvariantCultureIgnoreCase) || file.FullName.Contains(safe, StringComparison.InvariantCultureIgnoreCase))
        {
            reason = "not in safeMode";
            return true;
        }
        reason = null;
        return false;
    }
  
    [NonSerialized]
    public List<string> nsfwKeywords = new List<string>() { "nsfw", "unsafe", "sex", "initSex", "endSex", "massage", "do_not_use", "forbidCopy" };

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

    public Hairstyle_Base GetByNameOrID_Hairstyle(string name_or_id)
    {
        return MasterList.BodyPartBases.GetHairstyleByID(name_or_id);
    }

    public Hairstyle_SlotOption GetByNameOrID_HairstyleOption(string name_or_id)
    {
        return MasterList.BodyPartBases.GetHairstyleOptionByID(name_or_id);
    }

    public CharaSkill GetByNameOrID(string name_or_id)
    {
        return MasterList.Skills.GetByID(name_or_id);
    }
    public Floor_Base GetByNameOrID_Floor_Base(string name_or_id)
    {
        return MasterList.MapPlans.GetByID_FloorBase(name_or_id);
    }
    public MapPlan GetByNameOrID_MapPlan(string name_or_id)
    {
        return MasterList.MapPlans.GetByID_MapPlan(name_or_id);
    }
    public WorldPlan GetByNameOrID_WorldPlan(string name_or_id)
    {
        return MasterList.MapPlans.GetByID_WorldPlan(name_or_id);
    }
    public SalesClienteleDef GetByNameOrID_SalesClienteleDef(string name_or_id)
    {
        return MasterList.MapPlans.GetByID_SalesClienteleDef(name_or_id);
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

