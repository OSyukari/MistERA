using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Unity;
using System.IO;
using UnityEngine.UI;
using TMPro;
using Newtonsoft.Json;
using System.Linq;
using Spine;
using Spine.Unity;
using UnityEditor;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using Cysharp.Threading.Tasks;
using System.Text;

public interface SpineAssetHandler
{
    public void LoadSpineJSON(string materialTexturePath, string atlasJSON_path, string skeletonJSON_path, out Texture2D spineLoader_Texture, out TextAsset spineLoader_atlasJSON, out TextAsset spineLoader_skeletonJSON);
}

[System.Serializable]
public class scr_System_CentralControl_Serializable
{
    public UserPrefs UserPref;
    public DebugLogSettings DebugLogPref;
}

[System.Serializable]
public class DebugLogSettings
{
    public bool Debug_Logging_Job = false;
    public bool Debug_Logging_ActionPackage = false;
    public bool Debug_Logging_ActorJob = false;
    public bool Debug_Logging_ActorExperienceGain = false;
    public bool Debug_Logging_MinuteAllActorsUpdate = false;
    public bool Debug_Logging_Unimplemented_KojoCOM = false;
    public bool Debug_Logging_Unimplemented_KojoEvent = false;
    public bool Debug_Logging_KojoEvents = false;
    public bool Debug_Logging_UpdateTimeCost = false;
}

public class scr_System_CentralControl : MonoBehaviour
{
    // Singleton
    public static scr_System_CentralControl current;
    [SerializeField] protected UserPrefs prefsCache = null;
    [SerializeField] protected DebugLogSettings _logPrefs = null;
    public DebugLogSettings LogPrefs { get
        {
            if(_logPrefs == null) _logPrefs = new DebugLogSettings();
            return _logPrefs;
        } }
    public UserPrefs pref { get
        {
            if (prefsCache == null) prefsCache = new UserPrefs();
            return prefsCache;
        } }
    private void Awake()
    {
        if (current == null)
        {
            current = this;
        }
        else
        {
            Destroy(gameObject);
        }
        DontDestroyOnLoad(gameObject);

        //pref = new UserPrefs();
        //Initialize();
        // too early, other System script hasnt run yet
        //Debug.Log("Persistent datat path: " + application.dataPath);
        string filePath = Application.dataPath + "/UserPrefs.json";
        FileInfo file = new System.IO.FileInfo(filePath);
        if (!File.Exists(filePath))
        {
            var prefFile = GetSerializable();
            string s = JsonConvert.SerializeObject(prefFile, Formatting.Indented, Utility.SerializerSettings);
            file.Directory.Create();
            File.WriteAllText(file.FullName, s);
        }
        else
        {
            scr_System_CentralControl_Serializable s = JsonConvert.DeserializeObject<scr_System_CentralControl_Serializable>(File.ReadAllText(file.FullName), Utility.SerializerSettings);
            LoadSerializable(s);
        }
        
    }

    public string Language { get { return "default"; } }
    public void SaveUserPref()
    {
        string filePath = Application.dataPath + "/UserPrefs.json";
        FileInfo file = new System.IO.FileInfo(filePath);
        var prefFile = GetSerializable();
        string s = JsonConvert.SerializeObject(prefFile, Formatting.Indented, Utility.SerializerSettings);
        file.Directory.Create();
        File.WriteAllText(file.FullName, s);
    }

    public System.Random random = new();


    protected void LoadSerializable(scr_System_CentralControl_Serializable obj)
    {
        this._logPrefs = obj.DebugLogPref;
        this.prefsCache = obj.UserPref;
    }

    protected scr_System_CentralControl_Serializable GetSerializable()
    {
        var obj = new scr_System_CentralControl_Serializable();
        obj.UserPref = pref;
        obj.DebugLogPref = LogPrefs;
        return obj;
    }

    public void LoadSpineJSON(string materialTexturePath, string atlasJSON_path, string skeletonJSON_path, out Texture2D spineLoader_Texture, out TextAsset spineLoader_atlasJSON, out TextAsset spineLoader_skeletonJSON)
    {
        spineLoader_atlasJSON = Resources.Load<TextAsset>(atlasJSON_path);
        spineLoader_skeletonJSON = Resources.Load<TextAsset>(skeletonJSON_path);
        spineLoader_Texture = LoadCachedTexture(materialTexturePath);
        spineLoader_Texture.name = Path.GetFileNameWithoutExtension(materialTexturePath);
    }

    public TextAsset LoadResourcesTextAssets(string materialTexturePath)
    {
        return Resources.Load<TextAsset>( materialTexturePath);
    }

    /// <summary>
    /// Get the Skeleton asset version.
    /// </summary>
    /// <param name="skelPath"></param>
    /// <returns>Skeleton asset version.</returns>
    public static string GetSkelVersion(string skelPath)
    {
        TextAsset ta = scr_System_CentralControl.current.LoadResourcesTextAssets(skelPath);
        if (ta.text.Contains("4.0.")) return "4.0";
        else if (ta.text.Contains("4.1.")) return "4.1";
        else if (ta.text.Contains("4.2.")) return "4.2";
        else return "";
    }

    public SpineLoader spine40, spine41, spine42;

    public SpineLoader GetSpineLoader(string skelPath)
    {
        //Debug.Log("Getting spine loader version " + GetSkelVersion(skelPath));
        switch (GetSkelVersion(skelPath))
        {
            case "4.0": return Instantiate(spine40);
            case "4.1": return Instantiate(spine41);
            case "4.2": return Instantiate(spine42);
            default: return null;
        }
    }


    protected void Start()
    {
        scr_System_Time.current.Observer_globalTime_Hours += OnHourUpdate;
        scr_System_Time.current.Observer_globalTime_Day += OnDayUpdate;
        scr_System_SceneManager.current.Initialize();   // load menu_intro

    }


    private void OnDayUpdate(int dt)
    {
        // if loaded texture count less than 30 then dont do anything
        var order = textureUseCounter.OrderByDescending(x => x.Value).ToList();
        if (order.Count >= 30 && order[order.Count - 1].Value < 0)
        {
            // remove extra texture 
            for (int i = order.Count - 1; i >= 0; i--)
            {
                if (textureUseCounter[order[i].Key] <= 0) UnloadTextureCache(order[i].Key);
                if (textureUseCounter.Count <= 30) break;
            }
        }
        order.Clear();        
    }
    private void OnHourUpdate(TimeSpan t)
    {
        var keys = textureUseCounter.Keys.ToList();
        for (int i = keys.Count - 1; i >= 0; i--)
        {
            textureUseCounter[keys[i]] -= 1;
           // if (textureUseCounter[keys[i]] <= 0) UnloadTextureCache(keys[i]);
        }
    }

    public int PortraitCacheHour = 48;

    protected void AddSpriteCache(string path)
    {
        //Debug.Log("AddSpriteCache " + path);
        AddTextureCache(path);
        if (!Sprites.ContainsKey(path)) Sprites.Add(path, LoadSprite(SpriteTextures[path]));
    }
    protected void AddTextureCache(string path)
    {
        //Debug.Log("AddTextureCache " + path);
        if (!textureUseCounter.ContainsKey(path)) textureUseCounter.Add(path, PortraitCacheHour);
        if (!SpriteTextures.ContainsKey(path)) SpriteTextures.Add(path, LoadTexture(path));
    }

    public Character_Trainable LoadCharaData(string path)
    {
        var chara = JsonConvert.DeserializeObject<Character_Trainable>(File.ReadAllText(path), Utility.SerializerSettings);
        if(chara != null) chara.FileLocation = path;
        return chara;
    }

    protected void UnloadTextureCache(string path)
    {
        Debug.Log("UnloadTextureCache " + path);

        if (Sprites.ContainsKey(path))
        {
            Sprite spr = Sprites[path];
            Sprites.Remove(path);
            Destroy(spr);
        }  

        if (SpriteTextures.ContainsKey(path))
        {
            Texture2D tex = SpriteTextures[path];
            SpriteTextures.Remove(path);
            Destroy(tex);
        }

        if (textureUseCounter.ContainsKey(path)) textureUseCounter.Remove(path);
    }

    Dictionary<string, int> textureUseCounter = new Dictionary<string, int>();
    Dictionary<string, Texture2D> SpriteTextures = new Dictionary<string, Texture2D>();
    Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();

    private Sprite LoadSprite(Texture2D tex)
    {
        return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0, 0), 100.0f);
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="path">Application.dataPath+"/" WILL BE PREPEND TO PATH PARAM WHEN LOADING TEXTURE FILE</param>
    /// <returns></returns>
    public Sprite LoadCachedSprite(string path)
    {
        if (path == null || path.Length < 1) return SpriteAsset.transparent;

        if (!Sprites.ContainsKey(path)) AddSpriteCache(path);

        textureUseCounter[path] = PortraitCacheHour;
        return Sprites[path];
        
    }

    public Texture2D LoadCachedTexture(string path)
    {
        if (path == null || path.Length < 1) return Texture2D.whiteTexture;

        if (!SpriteTextures.ContainsKey(path)) AddTextureCache(path);

        textureUseCounter[path] = PortraitCacheHour;
        return SpriteTextures[path];

    }



    private Texture2D LoadTexture(string FilePath)
    {

        // Load a PNG or JPG file from disk to a Texture2D
        // Returns null if load fails

        Texture2D Tex2D;
        byte[] FileData;

        var tex = Resources.Load<Texture2D>(FilePath);
        if (tex != null)
        {
            //Debug.Log("Loaded Resource texture " + FilePath);
            return tex;
        }
        else if (File.Exists(Application.dataPath + "/" + FilePath))
        {
            //Debug.Log("Loaded new texture " + Application.dataPath + "/" + FilePath);
            FileData = File.ReadAllBytes(Application.dataPath + "/" + FilePath);
            Tex2D = new Texture2D(2, 2);           // Create new "empty" texture
            if (Tex2D.LoadImage(FileData))           // Load the imagedata into the texture (size is set automatically)
                return Tex2D;                 // If data = readable -> return texture
        }

        Debug.Log("Loaded texture failed " + FilePath);
        return null;                     // Return null if load failed
        


    }

    private void Initialize()
    {
        

    }


    public Color32 Color_neutral { get { return pref.TextColor_neutral; } }
    public Color32 Color_hover { get { return pref.TextColor_hover; } }

    public bool adult
    {
        get
        {
            if (pref.adultContent) return true;
            else return false;
        }
    }

    public bool gay
    {
        get
        {
            if (pref.adultContent) return true;
            else return false;
        }
    }

    public TMP_FontAsset alphabet;
    public TMP_FontAsset chinese;

    public TMP_FontAsset Font { get { return alphabet; } }

    public XRay_Mode xray_mode
    {
        get
        {
            if (!pref.adultContent) return XRay_Mode.disabled;
            else return pref.adultContent_xray;
        }
    }

    public IEnumerator Wait(float i)
    {
        yield return new WaitForSeconds(i);
    }



    public bool isMale(Character_Trainable c)
    {
        if (c.Body == null) return false;
        bool hasP = c.Body.HasBodyTag("penis");
        bool hasV = c.Body.HasBodyTag("vagina");

        bool cond1 = (pref.Male_Appearance == Gender_App_Condition.male_only && c.Appearance == Humanoid_GenderAppearance.Male)
    || (pref.Male_Appearance == Gender_App_Condition.male_or_ambi && c.Appearance != Humanoid_GenderAppearance.Female)
    || (pref.Male_Appearance == Gender_App_Condition.dont_care);
        bool cond2 = (pref.Male_Penis == Gender_Condition.require && hasP)
            || (pref.Male_Penis == Gender_Condition.forbid && !hasP)
            || (pref.Male_Penis == Gender_Condition.dont_care);
        bool cond3 = (pref.Male_Vagina == Gender_Condition.require && hasV)
            || (pref.Male_Vagina == Gender_Condition.forbid && !hasV)
            || (pref.Male_Vagina == Gender_Condition.dont_care);
        return cond1 && cond2 & cond3;
    }

    public bool isMale(Character_Trainable.CharaTemplate c)
    {
        bool cond1 = (pref.Male_Appearance == Gender_App_Condition.male_only && c.Appearance == Humanoid_GenderAppearance.Male)
    || (pref.Male_Appearance == Gender_App_Condition.male_or_ambi && c.Appearance != Humanoid_GenderAppearance.Female)
    || (pref.Male_Appearance == Gender_App_Condition.dont_care);
        bool cond2 = (pref.Male_Penis == Gender_Condition.require && c.Size_P.ID != "trait_Size_P_none")
            || (pref.Male_Penis == Gender_Condition.forbid && c.Size_P.ID == "trait_Size_P_none")
            || (pref.Male_Penis == Gender_Condition.dont_care);
        bool cond3 = (pref.Male_Vagina == Gender_Condition.require && c.Size_V.ID != "trait_Size_V_none")
            || (pref.Male_Vagina == Gender_Condition.forbid && c.Size_V.ID == "trait_Size_V_none")
            || (pref.Male_Vagina == Gender_Condition.dont_care);
        return cond1 && cond2 & cond3;
    }

    public bool isFemale(Character_Trainable c)
    {
        if (c.Body == null) return false;
        bool hasP = c.Body.HasBodyTag("penis");
        bool hasV = c.Body.HasBodyTag("vagina");

        bool cond1 = (pref.Female_Appearance == Gender_App_Condition.female_only && c.Appearance == Humanoid_GenderAppearance.Female)
    || (pref.Female_Appearance == Gender_App_Condition.female_or_ambi && c.Appearance != Humanoid_GenderAppearance.Male)
    || (pref.Female_Appearance == Gender_App_Condition.dont_care);
        bool cond2 = (pref.Female_Penis == Gender_Condition.require && hasP)
            || (pref.Female_Penis == Gender_Condition.forbid && !hasP)
            || (pref.Female_Penis == Gender_Condition.dont_care);
        bool cond3 = (pref.Female_Vagina == Gender_Condition.require && hasV)
            || (pref.Female_Vagina == Gender_Condition.forbid && !hasV)
            || (pref.Female_Vagina == Gender_Condition.dont_care);
        return cond1 && cond2 & cond3;
    }


    public bool isFemale(Character_Trainable.CharaTemplate c)
    {
        bool cond1 = (pref.Female_Appearance == Gender_App_Condition.female_only && c.Appearance == Humanoid_GenderAppearance.Female)
    || (pref.Female_Appearance == Gender_App_Condition.female_or_ambi && c.Appearance != Humanoid_GenderAppearance.Male)
    || (pref.Female_Appearance == Gender_App_Condition.dont_care);
        bool cond2 = (pref.Female_Penis == Gender_Condition.require && c.Size_P.ID != "trait_Size_P_none")
            || (pref.Female_Penis == Gender_Condition.forbid && c.Size_P.ID == "trait_Size_P_none")
            || (pref.Female_Penis == Gender_Condition.dont_care);
        bool cond3 = (pref.Female_Vagina == Gender_Condition.require && c.Size_V.ID != "trait_Size_V_none")
            || (pref.Female_Vagina == Gender_Condition.forbid && c.Size_V.ID == "trait_Size_V_none")
            || (pref.Female_Vagina == Gender_Condition.dont_care);
        return cond1 && cond2 & cond3;
    }

    public List<InteractionGenderType> GetGender(int refID)
    {
        Character_Trainable c = scr_System_CampaignManager.current.FindInstanceByID(refID);
        if (c == null) return null;
        return GetGender(c);
    }


    public List<string> allusedConsoleCommands = new List<string>();
    public InteractionGenderType GetGenderSimple(Character_Trainable c)
    {
        List<InteractionGenderType> complexResult = GetGender(c);
        if (complexResult.Contains(InteractionGenderType.male)) return InteractionGenderType.male;
        else if (complexResult.Contains(InteractionGenderType.female)) return InteractionGenderType.female;
        else return InteractionGenderType.ambi;
    }

    public List<InteractionGenderType> GetGender(Character_Trainable c)
    {
        var result = new List<InteractionGenderType>();

        if (scr_System_CentralControl.current.pref.GenderPriority == Gender_Priority.female_first)
        {
            if (isFemale(c)) result.Add( InteractionGenderType.female);
            else if (isMale(c)) result.Add(InteractionGenderType.male);
            else result.Add(InteractionGenderType.ambi);
        }
        else
        {
            if (isMale(c)) result.Add(InteractionGenderType.male);
            else if (isFemale(c)) result.Add(InteractionGenderType.female);
            else result.Add(InteractionGenderType.ambi);
        }

        if (c.isHumanoid) result.Add(InteractionGenderType.human);
        else if (c.isAnimal) result.Add(InteractionGenderType.animal);
        else if (c.isCreature) result.Add(InteractionGenderType.creature);

        if (c.isDead) result.Add(InteractionGenderType.corpse);

        return result;
    }

    public InteractionGenderType GetGenderSimple(Character_Trainable.CharaTemplate c)
    {
        if (scr_System_CentralControl.current.pref.GenderPriority == Gender_Priority.female_first)
        {
            if (isFemale(c)) return InteractionGenderType.female;
            else if (isMale(c)) return InteractionGenderType.male;
            else return InteractionGenderType.ambi;
        }
        else
        {
            if (isMale(c)) return InteractionGenderType.male;
            else if (isFemale(c)) return InteractionGenderType.female;
            else return InteractionGenderType.ambi;
        }
    }

    public string GetGenderSymbol(int refID)
    {
        var s = GetGender(refID);
        if ( s.Contains(InteractionGenderType.female)) return "\u2640";
        else if (s.Contains(InteractionGenderType.male)) return "\u2642";
        else if (s.Contains(InteractionGenderType.ambi)) return "\u2642\u2640";
        else return "";
        
    }



    public bool CanHaveSex(List<int> doers, List<int> receivers)
    {
        List<InteractionGenderType> doersG = new List<InteractionGenderType>();
        List<InteractionGenderType> receiversG = new List<InteractionGenderType>();

        foreach(var i in doers) doersG.AddRange(GetGender(i));
        foreach(var i in receivers) receiversG.AddRange(GetGender(i));

        doersG = doersG.Distinct().ToList();
        receiversG = receiversG.Distinct().ToList();

        foreach (var i in doersG) foreach (var j in receiversG) if (!CanHaveSex(i, j)) return false;
        return true;

    }

    protected bool CanHaveSex(InteractionGenderType doerGender, InteractionGenderType receiverGender)
    {

        bool value = true;

        if (doerGender == InteractionGenderType.animal)
        {
            if (receiverGender == InteractionGenderType.male) value = scr_System_CentralControl.current.pref.creature_on_male.value;
            else if (receiverGender == InteractionGenderType.female) value = scr_System_CentralControl.current.pref.creature_on_female.value;
            else if (receiverGender == InteractionGenderType.ambi) value = scr_System_CentralControl.current.pref.creature_on_ambi.value;
            else if (receiverGender == InteractionGenderType.animal) value = scr_System_CentralControl.current.pref.creature_on_creature.value;
            else if (receiverGender == InteractionGenderType.corpse) value = scr_System_CentralControl.current.pref.creature_on_necro.value;
        }
        else if (doerGender == InteractionGenderType.male)
        {
            if (receiverGender == InteractionGenderType.male) value = scr_System_CentralControl.current.pref.male_on_male.value;
            else if (receiverGender == InteractionGenderType.female) value = scr_System_CentralControl.current.pref.male_on_female.value;
            else if (receiverGender == InteractionGenderType.ambi) value = scr_System_CentralControl.current.pref.male_on_ambi.value;
            else if (receiverGender == InteractionGenderType.animal) value = scr_System_CentralControl.current.pref.male_on_creature.value;
            else if (receiverGender == InteractionGenderType.corpse) value = scr_System_CentralControl.current.pref.male_on_necro.value;
        }
        else if (doerGender == InteractionGenderType.female)
        {
            if (receiverGender == InteractionGenderType.male) value = scr_System_CentralControl.current.pref.female_on_male.value;
            else if (receiverGender == InteractionGenderType.female)  value = scr_System_CentralControl.current.pref.female_on_female.value;
            else if (receiverGender == InteractionGenderType.ambi) value = scr_System_CentralControl.current.pref.female_on_ambi.value;
            else if (receiverGender == InteractionGenderType.animal) value = scr_System_CentralControl.current.pref.female_on_creature.value;
            else if (receiverGender == InteractionGenderType.corpse) value = scr_System_CentralControl.current.pref.female_on_necro.value;
        }
        else if (doerGender == InteractionGenderType.ambi)
        {
            if (receiverGender == InteractionGenderType.male)  value = scr_System_CentralControl.current.pref.ambi_on_male.value;
            else if (receiverGender == InteractionGenderType.female) value = scr_System_CentralControl.current.pref.ambi_on_female.value;
            else if (receiverGender == InteractionGenderType.ambi) value = scr_System_CentralControl.current.pref.ambi_on_ambi.value;
            else if (receiverGender == InteractionGenderType.animal) value = scr_System_CentralControl.current.pref.ambi_on_creature.value;
            else if (receiverGender == InteractionGenderType.corpse) value = scr_System_CentralControl.current.pref.ambi_on_necro.value;
        }

        return value;
    }

    protected bool CanHaveSex(List<InteractionGenderType> doer, List<InteractionGenderType> receiver)
    {
        foreach(var i in doer) foreach(var j in receiver) if (!CanHaveSex(i,j)) return false;
        return true;
    }

    public bool CanHaveSex(int doerRefID, int receiverRefID)
    {
        Character_Trainable doer = scr_System_CampaignManager.current.FindInstanceByID(doerRefID);
        Character_Trainable receiver = scr_System_CampaignManager.current.FindInstanceByID(receiverRefID);

        return CanHaveSex(GetGender(doer), GetGender(receiver));
    }

    public bool CanInteractWith(int doerRefID, int receiverRefID)
    {
        Character_Trainable c = scr_System_CampaignManager.current.FindInstanceByID(doerRefID);
        if ((!c.isImprisoned || scr_System_CampaignManager.current.Map.IsBothCharaInSameRoom(doerRefID, receiverRefID)) &&
            (!c.isRestrained || (c.Jail != null && c.Jail.hasContent(receiverRefID)))) return true;
        return false;
    }

    public void QuickSave()
    {
        scr_UpdateHandler.current.NotifySL(true);

        var time = DateTime.Now;
        var fileName = time.Year+"-"+time.Month.ToString("D2")+"-"+time.Day.ToString("D2") + " "+time.Hour.ToString("D2") + "H "+time.Minute.ToString("D2") +"M " + time.Second.ToString("D2") + "S";
        var save = new SaveFile(true);
        string s = JsonConvert.SerializeObject(save, Formatting.Indented, Utility.SerializerSettings);

        FileInfo file = new System.IO.FileInfo(Utility.GetSavePath_Save() + fileName + ".json");
        file.Directory.Create();
        File.WriteAllText(file.FullName, s);

        scr_System_CampaignManager.current.QuickSaveFilePath = file.FullName;

        scr_UpdateHandler.current.NotifySL(false);
        Debug.Log("Saving Complete!");
    }

    public void QuickLoad()
    {
        var path = scr_System_CampaignManager.current.QuickSaveFilePath;
        if (path == null || path.Length < 1)
        {
            Debug.LogError("QuickLoad path invalid");
            return;
        }
        SaveFile save = JsonConvert.DeserializeObject<SaveFile>(File.ReadAllText(path), Utility.SerializerSettings);

        save.LoadSave();

        Debug.Log("Loading Complete!");
    }


}


[System.Serializable]
public class SaveFileHolder
{
    public string Version;
    public string SaveDescription;

    public string FilePath = "";

    [JsonIgnore] public SaveFile InnerFile{get{
        return JsonConvert.DeserializeObject<SaveFile>(File.ReadAllText(FilePath), Utility.SerializerSettings);
    }}

    [JsonIgnore] public bool isValid{get{ return FilePath != ""; }}

}

[System.Serializable]

public class SaveFile
{
    public string Version;
    public string SaveDescription;
    public scr_System_Time_Serializable Time;
    public scr_System_CampaignManager_Serializable Campaign;
    public Index_RelationshipTypes RelationshipTypes = null;

    public SaveFile() { }
    public SaveFile(bool createNew)
    {
        var playerRoom = scr_System_CampaignManager.current.Map.FindRoomByChara(0);
        var playerFloor = scr_System_CampaignManager.current.Map.GetFloorByRoomRefID(playerRoom.RefID);

        this.Time = scr_System_Time.current.GetSerializable();
        this.Campaign = scr_System_CampaignManager.current.GetSerializable();
        this.Version = Application.version;
        this.SaveDescription = scr_System_Serializer.current.Dictionary.QueryThenParse("ui_load_fileDescription")
                .Replace("$days$", Time.ElapesedTime.Days.ToString())
                .Replace("$hours$", Time.currentDate.TimeOfDay.Hours.ToString("D2"))
                .Replace("$minutes$", Time.currentDate.TimeOfDay.Minutes.ToString("D2"))
                .Replace("$playerName$", scr_System_CampaignManager.current.Player.FullName)
                .Replace("$floor$", playerFloor.displayName)
                .Replace("$room$", playerRoom.DisplayName);
    }
    public void LoadSave()
    {   // external call to updatehandler notifySL
        scr_System_Time.current.LoadSerializable(Time);
        scr_System_CampaignManager.current.LoadSerializable(Campaign);
    }

}


[System.Serializable]
public class UserPrefs{

    public int MaxLogCount = 50;
    public int ClickDragForgiveness = 100;

    //public bool verboseLogging = true;

    public Color32 BackgroundColor_Opaque = new Color32(49,77,121,255);
    public Color32 BackgroundColor_Transparent = new Color32(0, 0, 0, 174);

    public Color32 TextColor_neutral = new Color32(255, 255, 255, 255);
    public Color32 TextColor_hover = new Color32(255, 235, 4, 255);
    public Color32 TextColor_toggle = new Color32(0, 255, 255, 255);
    public Color32 TextColor_disabled = new Color32(128,128,128, 255);
    public Color32 TextColor_conflict = new Color32(255, 0, 0, 255);
    public string HexColor_conflict = $"#{255:X2}{0:X2}{0:X2}{255:X2}";
    public Color32 TextColor_maxed = new Color32(120, 0, 128, 255);

    public Color32 TextColor_transparent = new Color32(0, 0, 0, 0);


    public EnumSetting sex_mode = new EnumSetting((int)Sex_Mode.enabled, typeof(Sex_Mode));
    [JsonIgnore] public Sex_Mode SexMode { get { return (Sex_Mode)sex_mode.enumValue; } }

    [JsonIgnore] public bool adultContent { get { return SexMode != Sex_Mode.disabled; } }

    public EnumSetting sex_presence_mode = new EnumSetting((int)Sex_Presence_Mode.all, typeof(Sex_Presence_Mode));
    [JsonIgnore] public Sex_Presence_Mode SexPresenceMode{ get { return (Sex_Presence_Mode)sex_presence_mode.enumValue; } }

    // First check female, if false then check male, if false again ambi
    public EnumSetting gender_priority = new EnumSetting((int)Gender_Priority.female_first, typeof(Gender_Priority));
    [JsonIgnore] public Gender_Priority GenderPriority { get { return (Gender_Priority)gender_priority.enumValue; } }

    // Male check
    public EnumSetting male_appearance = new EnumSetting((int)Gender_App_Condition.male_only, typeof(Gender_App_Condition));
    public EnumSetting male_penis = new EnumSetting((int)Gender_Condition.require, typeof(Gender_Condition));
    public EnumSetting male_vagina = new EnumSetting((int)Gender_Condition.dont_care, typeof(Gender_Condition));
    [JsonIgnore] public Gender_App_Condition Male_Appearance { get { return (Gender_App_Condition)male_appearance.enumValue; } }
    [JsonIgnore] public Gender_Condition Male_Penis { get { return (Gender_Condition)male_penis.enumValue; } }
    [JsonIgnore] public Gender_Condition Male_Vagina { get { return (Gender_Condition)male_vagina.enumValue; } }

    // Female check
    public EnumSetting female_appearance = new EnumSetting((int)Gender_App_Condition.female_only, typeof(Gender_App_Condition));
    public EnumSetting female_penis = new EnumSetting((int)Gender_Condition.dont_care, typeof(Gender_Condition));
    public EnumSetting female_vagina = new EnumSetting((int)Gender_Condition.require, typeof(Gender_Condition));
    [JsonIgnore] public Gender_App_Condition Female_Appearance { get { return (Gender_App_Condition)female_appearance.enumValue; } }
    [JsonIgnore] public Gender_Condition Female_Penis { get { return (Gender_Condition)female_penis.enumValue; } }
    [JsonIgnore] public Gender_Condition Female_Vagina { get { return (Gender_Condition)female_vagina.enumValue; } }

    // Sex filter Male
    public BoolSetting male_on_female = new BoolSetting(true, "male_on_female");
    public BoolSetting male_on_male = new BoolSetting(false, "male_on_male");
    public BoolSetting male_on_ambi = new BoolSetting(true, "male_on_ambi");

    // Sex filter Female
    public BoolSetting female_on_female = new BoolSetting(true, "female_on_female");
    public BoolSetting female_on_male = new BoolSetting(true, "female_on_male");
    public BoolSetting female_on_ambi = new BoolSetting(true, "female_on_ambi");

    // Sex filter Ambi
    public BoolSetting ambi_on_female = new BoolSetting(true, "ambi_on_female");
    public BoolSetting ambi_on_male = new BoolSetting(false, "ambi_on_male");
    public BoolSetting ambi_on_ambi = new BoolSetting(true, "ambi_on_ambi");

    [JsonIgnore] public EnumSetting _creature_mode = new EnumSetting((int)Creature_Mode.all_male, typeof(Creature_Mode));
    [JsonIgnore] public Creature_Mode CreatureMode { get { return (Creature_Mode)_creature_mode.enumValue; } }

    // Sex filter creature
    [JsonIgnore] public BoolSetting creature_on_female = new BoolSetting(true, "creature_on_female");
    [JsonIgnore] public BoolSetting creature_on_male = new BoolSetting(false, "creature_on_male");
    [JsonIgnore] public BoolSetting creature_on_ambi = new BoolSetting(true, "creature_on_ambi");
    [JsonIgnore] public BoolSetting creature_on_creature = new BoolSetting(false, "creature_on_creature");

    [JsonIgnore] public BoolSetting male_on_creature = new BoolSetting(false, "male_on_creature");
    [JsonIgnore] public BoolSetting female_on_creature = new BoolSetting(true, "female_on_creature");
    [JsonIgnore] public BoolSetting ambi_on_creature = new BoolSetting(true, "ambi_on_creature");

    [JsonIgnore] public EnumSetting _necro_mode = new EnumSetting((int)Necro_Mode.nonhuman, typeof(Necro_Mode));
    [JsonIgnore] public Necro_Mode NecroMode { get { return (Necro_Mode)_necro_mode.enumValue; } }

    [JsonIgnore] public BoolSetting male_on_necro =  new BoolSetting( true, "male_on_necro");
    [JsonIgnore] public BoolSetting female_on_necro =   new BoolSetting( false, "female_on_necro");
    [JsonIgnore] public BoolSetting ambi_on_necro =   new BoolSetting( true, "ambi_on_necro");
    [JsonIgnore] public BoolSetting creature_on_necro =  new BoolSetting( true, "creature_on_necro");

    [JsonIgnore] public EnumSetting _dismember_mode = new EnumSetting((int)Dismember_Mode.corpse_only, typeof(Dismember_Mode));
    [JsonIgnore] public EnumSetting _cannibal_mode = new EnumSetting((int)Cannibal_Mode.non_humanoid_only, typeof(Cannibal_Mode));

    [JsonIgnore] public Dismember_Mode DismemberMode { get { return (Dismember_Mode)_dismember_mode.enumValue; } }
    [JsonIgnore] public Cannibal_Mode CannibalMode { get { return (Cannibal_Mode)_cannibal_mode.enumValue; } }


    public EnumSetting _adultContent_xray = new EnumSetting((int)XRay_Mode.widget_first, typeof(XRay_Mode));
    [JsonIgnore] public XRay_Mode adultContent_xray { get { return (XRay_Mode)_adultContent_xray.enumValue; } }

    public BoolSetting displayPlayerPortraitInLogs =  new BoolSetting( true, "displayPlayerPortraitInLogs"); 
}

[System.Serializable]
public class BoolSetting
{
    public bool value;
    public string name;
    public BoolSetting(bool value, string name)
    {
        this.value = value;
        this.name = name;
    }

    public void Toggle()
    {
        this.value = !this.value;
    }
    public string DisplayName()
    {
        return name;
    }
}


[System.Serializable]
public class EnumSetting
{
    public int enumValue;
    public Type enumType;
    public EnumSetting(int initialValue, Type type) 
    { 
        this.enumValue = initialValue; 
        this.enumType = type;
    }
    public bool HasPrev()
    {
        var Arr = Enum.GetValues(enumType);
        return (enumValue - 1 >= 0);
    }
    public bool HasNext()
    {
        var Arr = Enum.GetValues(enumType);
        return (enumValue + 1 < Arr.Length);
    }

    public void Prev()
    {
        var Arr = Enum.GetValues(enumType);
        this.enumValue = (enumValue - 1 >= 0 ? enumValue - 1 : Arr.Length - 1);
    }
    public void Next()
    {
        var Arr = Enum.GetValues(enumType);
        this.enumValue = (enumValue + 1 < Arr.Length ? enumValue + 1 : 0);
    }

    public string DisplayName()
    {
        var Arr = Enum.GetValues(enumType);
        return enumType.ToString()+"."+Arr.GetValue(enumValue).ToString();
    }
}

[System.Serializable]
public enum InteractionGenderType
{
    none,
    male,
    female,
    ambi,
    human,
    animal,
    creature,
    corpse,
    insect
}

public enum ViewMode
{
    View_Logs,
    View_Room,
    View_Map
}

public enum Gender_Priority
{
    male_first,
    female_first
}

public enum Creature_Mode
{
    disabled,
    all_male,
    all_female,
    use_both
}

public enum Necro_Mode 
{
    disabled,
    nonhuman,
    allowall
}

public enum Sex_Presence_Mode 
{
    minimal, 
    indirect,
    all
}

public enum Gender_App_Condition
{
    female_only,
    female_or_ambi,
    dont_care,
    male_or_ambi,
    male_only
}



/*
static class UserPref_EnumMethods
{
    public static Creature_Mode Next(this Creature_Mode e)
    {
        int index = (int)e;
        if (Enum.IsDefined(typeof(Creature_Mode), index + 1)) return (Creature_Mode)Enum.ToObject(typeof(Creature_Mode), index + 1);
        else return (Creature_Mode)Enum.ToObject(typeof(Creature_Mode), 0);
    }

    public static object Next(this System.Enum e)
    {
        return null;
    }
}
*/

public enum Gender_Condition
{
    forbid,
    dont_care,
    require
}


public enum Cannibal_Mode
{
    disabled,
    non_humanoid_only,
    no_restriction
}

public enum Dismember_Mode
{
    disabled,
    corpse_only,
    dead_and_living
}

public enum Sex_Mode
{
    disabled,
    enabled,
    hardcore
}

public enum XRay_Mode
{
    disabled,
    widget_only,
    widget_first,
    internal_first,
    internal_only
}

public static class GlobalValues { 
    public static string IntroScene = "Assets/Scenes/Intro/Menu_Intro.unity";
    public static string GameScene = "Assets/Scenes/GameLoop/Menu_Game.unity";
}

public static class DataPath
{
    public static string iconAA_default = "Images/iconAA_default.txt";
    public static string icon_default = "";
    public static string portrait_default = "";
    public static string Character_Origin_Index = "/Data/Defs/" + "/PawnDefs/Character_Origin";

    
}

public static class XraySpritePath
{
    public static string widget_ass0 = "/Images/SSE_iWant_SLWidget/apropos2/ass0.png";
    public static string widget_ass1 = "/Images/SSE_iWant_SLWidget/apropos2/ass1.png";
    public static string widget_ass2 = "/Images/SSE_iWant_SLWidget/apropos2/ass2.png";
    public static string widget_ass3 = "/Images/SSE_iWant_SLWidget/apropos2/ass3.png";
    public static string widget_ass4 = "/Images/SSE_iWant_SLWidget/apropos2/ass4.png";
    public static string widget_ass5 = "/Images/SSE_iWant_SLWidget/apropos2/ass5.png";
    public static string widget_ass6 = "/Images/SSE_iWant_SLWidget/apropos2/ass6.png";
    public static string widget_ass7 = "/Images/SSE_iWant_SLWidget/apropos2/ass7.png";
    public static string widget_ass8 = "/Images/SSE_iWant_SLWidget/apropos2/ass8.png";

    public static string widget_oral0 = "/Images/SSE_iWant_SLWidget/apropos2/oral0.png";
    public static string widget_oral1 = "/Images/SSE_iWant_SLWidget/apropos2/oral1.png";
    public static string widget_oral2 = "/Images/SSE_iWant_SLWidget/apropos2/oral2.png";
    public static string widget_oral3 = "/Images/SSE_iWant_SLWidget/apropos2/oral3.png";
    public static string widget_oral4 = "/Images/SSE_iWant_SLWidget/apropos2/oral4.png";
    public static string widget_oral5 = "/Images/SSE_iWant_SLWidget/apropos2/oral5.png";
    public static string widget_oral6 = "/Images/SSE_iWant_SLWidget/apropos2/oral6.png";
    public static string widget_oral7 = "/Images/SSE_iWant_SLWidget/apropos2/oral7.png";
    public static string widget_oral8 = "/Images/SSE_iWant_SLWidget/apropos2/oral8.png";

    public static string widget_vag0 = "/Images/SSE_iWant_SLWidget/apropos2/vag0.png";
    public static string widget_vag1 = "/Images/SSE_iWant_SLWidget/apropos2/vag1.png";
    public static string widget_vag2 = "/Images/SSE_iWant_SLWidget/apropos2/vag2.png";
    public static string widget_vag3 = "/Images/SSE_iWant_SLWidget/apropos2/vag3.png";
    public static string widget_vag4 = "/Images/SSE_iWant_SLWidget/apropos2/vag4.png";
    public static string widget_vag5 = "/Images/SSE_iWant_SLWidget/apropos2/vag5.png";
    public static string widget_vag6 = "/Images/SSE_iWant_SLWidget/apropos2/vag6.png";
    public static string widget_vag7 = "/Images/SSE_iWant_SLWidget/apropos2/vag7.png";
    public static string widget_vag8 = "/Images/SSE_iWant_SLWidget/apropos2/vag8.png";

    public static string eratw_a1 = "Images/EraTW_Xray/A_1.png";

    public static string eratw_w1 = "Images/EraTW_Xray/W.png";

    public static string eratw_v1 = "Images/EraTW_Xray/V_1.png";

    public static string rjw_ovary1 = "Images/RJW_ovu/Ovary_00.png";
    public static string rjw_ovary2 = "Images/RJW_ovu/Ovary_01.png";
    public static string rjw_ovary3 = "Images/RJW_ovu/Ovary_02.png";
    public static string rjw_egg1 = "Images/RJW_ovu/Egg.png";
    public static string rjw_eggFertilized1 = "Images/RJW_ovu/Egg_Fertilized00.png";
    public static string rjw_eggFertilized2 = "Images/RJW_ovu/Egg_Fertilized01.png";
    public static string rjw_eggFertilized3 = "Images/RJW_ovu/Egg_Fertilized02.png";
    public static string rjw_eggInseminate1 = "Images/RJW_ovu/Egg_Fertilizing00.png";
    public static string rjw_eggInseminate2 = "Images/RJW_ovu/Egg_Fertilizing01.png";
    public static string rjw_eggInseminate3 = "Images/RJW_ovu/Egg_Fertilizing02.png";
    public static string rjw_eggPlanted = "Images/RJW_ovu/Egg_Implanted00.png";
}

public static class XraySprite
{
    public static Texture2D widget_ass0 = LoadTexture(Application.dataPath+"/"+"/Images/SSE_iWant_SLWidget/apropos2/ass0.png");
    public static Texture2D widget_ass1 = LoadTexture(Application.dataPath+"/"+"/Images/SSE_iWant_SLWidget/apropos2/ass1.png");
    public static Texture2D widget_ass2 = LoadTexture(Application.dataPath+"/"+"/Images/SSE_iWant_SLWidget/apropos2/ass2.png");
    public static Texture2D widget_ass3 = LoadTexture(Application.dataPath+"/"+"/Images/SSE_iWant_SLWidget/apropos2/ass3.png");
    public static Texture2D widget_ass4 = LoadTexture(Application.dataPath+"/"+"/Images/SSE_iWant_SLWidget/apropos2/ass4.png");
    public static Texture2D widget_ass5 = LoadTexture(Application.dataPath+"/"+"/Images/SSE_iWant_SLWidget/apropos2/ass5.png");
    public static Texture2D widget_ass6 = LoadTexture(Application.dataPath+"/"+"/Images/SSE_iWant_SLWidget/apropos2/ass6.png");
    public static Texture2D widget_ass7 = LoadTexture(Application.dataPath+"/"+"/Images/SSE_iWant_SLWidget/apropos2/ass7.png");
    public static Texture2D widget_ass8 = LoadTexture(Application.dataPath+"/"+"/Images/SSE_iWant_SLWidget/apropos2/ass8.png");

    public static Texture2D widget_oral0 = LoadTexture(Application.dataPath+"/"+"/Images/SSE_iWant_SLWidget/apropos2/oral0.png");
    public static Texture2D widget_oral1 = LoadTexture(Application.dataPath+"/"+"/Images/SSE_iWant_SLWidget/apropos2/oral1.png");
    public static Texture2D widget_oral2 = LoadTexture(Application.dataPath+"/"+"/Images/SSE_iWant_SLWidget/apropos2/oral2.png");
    public static Texture2D widget_oral3 = LoadTexture(Application.dataPath+"/"+"/Images/SSE_iWant_SLWidget/apropos2/oral3.png");
    public static Texture2D widget_oral4 = LoadTexture(Application.dataPath+"/"+"/Images/SSE_iWant_SLWidget/apropos2/oral4.png");
    public static Texture2D widget_oral5 = LoadTexture(Application.dataPath+"/"+"/Images/SSE_iWant_SLWidget/apropos2/oral5.png");
    public static Texture2D widget_oral6 = LoadTexture(Application.dataPath+"/"+"/Images/SSE_iWant_SLWidget/apropos2/oral6.png");
    public static Texture2D widget_oral7 = LoadTexture(Application.dataPath+"/"+"/Images/SSE_iWant_SLWidget/apropos2/oral7.png");
    public static Texture2D widget_oral8 = LoadTexture(Application.dataPath+"/"+"/Images/SSE_iWant_SLWidget/apropos2/oral8.png");

    public static Texture2D widget_vag0 = LoadTexture(Application.dataPath+"/"+"/Images/SSE_iWant_SLWidget/apropos2/vag0.png");
    public static Texture2D widget_vag1 = LoadTexture(Application.dataPath+"/"+"/Images/SSE_iWant_SLWidget/apropos2/vag1.png");
    public static Texture2D widget_vag2 = LoadTexture(Application.dataPath+"/"+"/Images/SSE_iWant_SLWidget/apropos2/vag2.png");
    public static Texture2D widget_vag3 = LoadTexture(Application.dataPath+"/"+"/Images/SSE_iWant_SLWidget/apropos2/vag3.png");
    public static Texture2D widget_vag4 = LoadTexture(Application.dataPath+"/"+"/Images/SSE_iWant_SLWidget/apropos2/vag4.png");
    public static Texture2D widget_vag5 = LoadTexture(Application.dataPath+"/"+"/Images/SSE_iWant_SLWidget/apropos2/vag5.png");
    public static Texture2D widget_vag6 = LoadTexture(Application.dataPath+"/"+"/Images/SSE_iWant_SLWidget/apropos2/vag6.png");
    public static Texture2D widget_vag7 = LoadTexture(Application.dataPath+"/"+"/Images/SSE_iWant_SLWidget/apropos2/vag7.png");
    public static Texture2D widget_vag8 = LoadTexture(Application.dataPath+"/"+"/Images/SSE_iWant_SLWidget/apropos2/vag8.png");

    public static Texture2D eratw_a1 = LoadTexture(Application.dataPath+"/"+"Images/EraTW_Xray/A_1.png");

    public static Texture2D eratw_w1 = LoadTexture(Application.dataPath+"/"+"Images/EraTW_Xray/W.png");

    public static Texture2D eratw_v1 = LoadTexture(Application.dataPath+"/"+"Images/EraTW_Xray/V_1.png");

    public static Texture2D rjw_ovary1 = LoadTexture(Application.dataPath+"/"+"Images/RJW_ovu/Ovary_00.png");
    public static Texture2D rjw_ovary2 = LoadTexture(Application.dataPath+"/"+"Images/RJW_ovu/Ovary_01.png");
    public static Texture2D rjw_ovary3 = LoadTexture(Application.dataPath+"/"+"Images/RJW_ovu/Ovary_02.png");
    public static Texture2D rjw_egg1 = LoadTexture(Application.dataPath+"/"+"Images/RJW_ovu/Egg.png");
    public static Texture2D rjw_eggFertilized1 = LoadTexture(Application.dataPath+"/"+"Images/RJW_ovu/Egg_Fertilized00.png");
    public static Texture2D rjw_eggFertilized2 = LoadTexture(Application.dataPath+"/"+"Images/RJW_ovu/Egg_Fertilized01.png");
    public static Texture2D rjw_eggFertilized3 = LoadTexture(Application.dataPath+"/"+"Images/RJW_ovu/Egg_Fertilized02.png");
    public static Texture2D rjw_eggInseminate1 = LoadTexture(Application.dataPath+"/"+"Images/RJW_ovu/Egg_Fertilizing00.png");
    public static Texture2D rjw_eggInseminate2 = LoadTexture(Application.dataPath+"/"+"Images/RJW_ovu/Egg_Fertilizing01.png");
    public static Texture2D rjw_eggInseminate3 = LoadTexture(Application.dataPath+"/"+"Images/RJW_ovu/Egg_Fertilizing02.png");
    public static Texture2D rjw_eggPlanted = LoadTexture(Application.dataPath+"/"+"Images/RJW_ovu/Egg_Implanted00.png");

    private static Texture2D LoadTexture(string FilePath)
    {

        // Load a PNG or JPG file from disk to a Texture2D
        // Returns null if load fails

        Texture2D Tex2D;
        byte[] FileData;

        if (File.Exists(FilePath))
        {
            FileData = File.ReadAllBytes(FilePath);
            Tex2D = new Texture2D(2, 2);           // Create new "empty" texture
            if (Tex2D.LoadImage(FileData))           // Load the imagedata into the texture (size is set automatically)
                return Tex2D;                 // If data = readable -> return texture
        }
        return null;                     // Return null if load failed

    }
}

public static class SpriteAsset
{
    public static Sprite transparent = Sprite.Create(null, new Rect(0, 0, 0, 0), new Vector2(0, 0));
}
