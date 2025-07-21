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
using Cysharp.Threading.Tasks;
using System.Text;
using System.Threading.Tasks;


[System.Serializable]
public class scr_System_CentralControl_Serializable
{
    public ContentSettings ContentSettings;
    public DisplaySettings DisplaySettings;
    public DebugLogSettings DebugLogPref;
}

[System.Serializable]
public class DebugLogSettings
{
    public bool DLog_Jobs = false;
    public bool DLog_ExpGain = false;
    public bool DLog_Update = false;
    public bool DLog_CurrentRoomJob = false;
    public bool DLog_UnimplementedKojo = false;
    public bool DLog_KojoEvents = false;
    public bool DLog_Pathing = false;
    public bool DLog_Equipping = false;
    public bool DLog_Events = false;
    public bool DLog_Inventory = false;
    public bool DLog_Memory = false;
    public bool DLog_JoinAP = false;
    public bool DLog_Relationships = false;
    public bool DLog_APConflict = false;
    public bool DLog_Status = false;
    public bool DLog_Sex = false;
    public bool DLog_AP = false;
    public bool DLog_UIChange = false;
    public bool DLog_Interrupt = false;
}

public class scr_System_CentralControl : MonoBehaviour
{



    // Singleton
    public static scr_System_CentralControl current;
    [SerializeField] protected ContentSettings _content = null;
    [SerializeField] protected DebugLogSettings _logPrefs = null;
    public DebugLogSettings LogPrefs { get
        {
            if(_logPrefs == null) _logPrefs = new DebugLogSettings();
            return _logPrefs;
        } }

    private bool cachedSafeMode = false;
    public bool _SafeMode = false;
    [JsonIgnore]
    public bool isSafeMode { get
        {
#if UNITY_EDITOR
            return _SafeMode;
#else
            if (!cachedSafeMode)
            {
                cachedSafeMode = true;
                var numbers = Application.version.Split('-');
                _SafeMode = numbers[0].Contains(".s", StringComparison.InvariantCultureIgnoreCase);
            }
            return _SafeMode;
#endif
        }
    }
    public ContentSettings ContentSetting { get
        {
            if (isSafeMode) return null;
            if (_content == null && !isSafeMode) _content = new ContentSettings();
            return _content;
        } }

    [SerializeField] protected DisplaySettings _display = null;
    public DisplaySettings DisplaySetting { get
        {
            if (_display == null) _display = new DisplaySettings();
            return (_display);
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

    public bool DisplayNSFW { get { return !isSafeMode && ContentSetting.SexMode > Sex_Mode.disabled && ContentSetting.SexPresenceMode > Sex_Presence_Mode.minimal; } }


   // public Material spineSkel_40, spineSkel_41, spineSkel_42;

    public string Language { 

        get { return DisplaySetting.Language; }
        set
        {
            if (LocalizeDictionary.Instance.Index.Entries.ContainsKey(value))
            {
                DisplaySetting.Language = value;
                LocalizeDictionary.Instance.Index.cachedLang = value;
                scr_System_CentralControl.current.SaveUserPref();
            }
            else
            {
                Debug.LogError($"Error dictionary does not contain target language key {value}");
            }
        }
    }
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
        this._content = obj.ContentSettings;
        this._display = obj.DisplaySettings;
    }

    protected scr_System_CentralControl_Serializable GetSerializable()
    {
        var obj = new scr_System_CentralControl_Serializable();
        obj.ContentSettings = isSafeMode ? null : ContentSetting;
        obj.DisplaySettings = DisplaySetting;
        obj.DebugLogPref = LogPrefs;
        return obj;
    }

    public SpineLoader spine40, spine41, spine42;

    public SpineLoader GetSpineLoader(string version)
    {
        //Debug.Log("Getting spine loader version " + GetSkelVersion(skelPath));
        switch (version)
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

    }

    // Called by Serializer
    public void NotifyLoadComplete()
    {
        LocalizeDictionary.Instance.Index.cachedLang = DisplaySetting.Language;
        scr_System_SceneManager.current.Initialize();   // load menu_intro
    }

    private void OnDayUpdate(int dt)
    {
        // if loaded texture count less than 30 then dont do 100
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

    public Sprite GetSprite(Texture2D tex)
    {
        if (!this.texSprites.ContainsKey(tex)) this.texSprites.Add(tex, Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0, 0), 100.0f));
        this.textureUseCounter[tex] = PortraitCacheHour;
        return texSprites[tex];
    }


    public Character_Trainable LoadCharaData(string path)
    {
        var chara = JsonConvert.DeserializeObject<Character_Trainable>(File.ReadAllText(path), Utility.SerializerSettings);
        if (chara != null && chara.BaseID == "") chara.BaseID = path;
        return chara;
    }

    protected void UnloadTextureCache(Texture2D path)
    {
        if (texSprites.ContainsKey(path)) texSprites.Remove(path);
        if (textureUseCounter.ContainsKey(path)) textureUseCounter.Remove(path);
    }

    Dictionary<Texture2D, int> textureUseCounter = new Dictionary<Texture2D, int>();
    //Dictionary<string, Texture2D> SpriteTextures = new Dictionary<string, Texture2D>();
    //Dictionary<string, Sprite> Sprites = new Dictionary<string, Sprite>();
    Dictionary<Texture2D, Sprite> texSprites = new Dictionary<Texture2D, Sprite>();

    private void Initialize()
    {
        

    }


    public Color32 Color_neutral { get { return DisplaySetting.TextColor_neutral.Color; } }
    public Color32 Color_hover { get { return DisplaySetting.TextColor_hover.Color; } }

    public bool adult
    {
        get
        {
            if (!isSafeMode && ContentSetting.adultContent) return true;
            else return false;
        }
    }

    public bool gay
    {
        get
        {
            if (!isSafeMode && ContentSetting.adultContent) return true;
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
            if (isSafeMode || !ContentSetting.adultContent) return XRay_Mode.disabled;
            else return ContentSetting.adultContent_xray;
        }
    }

    public IEnumerator Wait(float i)
    {
        yield return new WaitForSeconds(i);
    }



    public bool isMale(Character_Trainable c)
    {
        if (isSafeMode) return c.Appearance == Humanoid_GenderAppearance.Male;
        if (c.Body == null) return false;

        bool hasP = c.Body.HasBodyTag("penis");
        bool hasV = c.Body.HasBodyTag("vagina");

        bool cond1 = (ContentSetting.Male_Appearance == Gender_App_Condition.male_only && c.Appearance == Humanoid_GenderAppearance.Male)
    || (ContentSetting.Male_Appearance == Gender_App_Condition.male_or_ambi && c.Appearance != Humanoid_GenderAppearance.Female)
    || (ContentSetting.Male_Appearance == Gender_App_Condition.dont_care);
        bool cond2 = (ContentSetting.Male_Penis == Gender_Condition.require && hasP)
            || (ContentSetting.Male_Penis == Gender_Condition.forbid && !hasP)
            || (ContentSetting.Male_Penis == Gender_Condition.dont_care);
        bool cond3 = (ContentSetting.Male_Vagina == Gender_Condition.require && hasV)
            || (ContentSetting.Male_Vagina == Gender_Condition.forbid && !hasV)
            || (ContentSetting.Male_Vagina == Gender_Condition.dont_care);
        return cond1 && cond2 & cond3;
    }

    public bool isMale(CharaTemplate c)
    {
        if (isSafeMode) return false;

        bool cond1 = (ContentSetting.Male_Appearance == Gender_App_Condition.male_only && c.Appearance == Humanoid_GenderAppearance.Male)
    || (ContentSetting.Male_Appearance == Gender_App_Condition.male_or_ambi && c.Appearance != Humanoid_GenderAppearance.Female)
    || (ContentSetting.Male_Appearance == Gender_App_Condition.dont_care);
        bool cond2 = (ContentSetting.Male_Penis == Gender_Condition.require && c.isMale)
            || (ContentSetting.Male_Penis == Gender_Condition.forbid && !c.isMale)
            || (ContentSetting.Male_Penis == Gender_Condition.dont_care);
        bool cond3 = (ContentSetting.Male_Vagina == Gender_Condition.require && c.isFemale)
            || (ContentSetting.Male_Vagina == Gender_Condition.forbid && !c.isFemale)
            || (ContentSetting.Male_Vagina == Gender_Condition.dont_care);
        return cond1 && cond2 & cond3;
    }

    public bool isFemale(Character_Trainable c)
    {
        if (isSafeMode) return c.Appearance == Humanoid_GenderAppearance.Female;

        if (c.Body == null) return false;
        bool hasP = c.Body.HasBodyTag("penis");
        bool hasV = c.Body.HasBodyTag("vagina");

        bool cond1 = (ContentSetting.Female_Appearance == Gender_App_Condition.female_only && c.Appearance == Humanoid_GenderAppearance.Female)
    || (ContentSetting.Female_Appearance == Gender_App_Condition.female_or_ambi && c.Appearance != Humanoid_GenderAppearance.Male)
    || (ContentSetting.Female_Appearance == Gender_App_Condition.dont_care);
        bool cond2 = (ContentSetting.Female_Penis == Gender_Condition.require && hasP)
            || (ContentSetting.Female_Penis == Gender_Condition.forbid && !hasP)
            || (ContentSetting.Female_Penis == Gender_Condition.dont_care);
        bool cond3 = (ContentSetting.Female_Vagina == Gender_Condition.require && hasV)
            || (ContentSetting.Female_Vagina == Gender_Condition.forbid && !hasV)
            || (ContentSetting.Female_Vagina == Gender_Condition.dont_care);
        return cond1 && cond2 & cond3;
    }


    public bool isFemale(CharaTemplate c)
    {
        if (isSafeMode) return false;
        // template calls should never be executed when safemode -> this whole menu shouldnt show at all

        bool cond1 = (ContentSetting.Female_Appearance == Gender_App_Condition.female_only && c.Appearance == Humanoid_GenderAppearance.Female)
    || (ContentSetting.Female_Appearance == Gender_App_Condition.female_or_ambi && c.Appearance != Humanoid_GenderAppearance.Male)
    || (ContentSetting.Female_Appearance == Gender_App_Condition.dont_care);
        bool cond2 = (ContentSetting.Female_Penis == Gender_Condition.require && c.isMale)
            || (ContentSetting.Female_Penis == Gender_Condition.forbid && !c.isMale)
            || (ContentSetting.Female_Penis == Gender_Condition.dont_care);
        bool cond3 = (ContentSetting.Female_Vagina == Gender_Condition.require && c.isFemale)
            || (ContentSetting.Female_Vagina == Gender_Condition.forbid && !c.isFemale)
            || (ContentSetting.Female_Vagina == Gender_Condition.dont_care);
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
        if (scr_System_CentralControl.current.isSafeMode) return InteractionGenderType.none;

        List<InteractionGenderType> complexResult = GetGender(c);
        if (complexResult.Contains(InteractionGenderType.male)) return InteractionGenderType.male;
        else if (complexResult.Contains(InteractionGenderType.female)) return InteractionGenderType.female;
        else return InteractionGenderType.ambi;
    }

    public List<InteractionGenderType> GetGender(Character_Trainable c)
    {
        if (scr_System_CentralControl.current.isSafeMode) return new List<InteractionGenderType>();

        var result = new List<InteractionGenderType>();

        if (scr_System_CentralControl.current.ContentSetting.GenderPriority == Gender_Priority.female_first)
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

    public InteractionGenderType GetGenderSimple(CharaTemplate c)
    {
        if (scr_System_CentralControl.current.isSafeMode) return InteractionGenderType.none;

        if (scr_System_CentralControl.current.ContentSetting.GenderPriority == Gender_Priority.female_first)
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
            if (receiverGender == InteractionGenderType.male) value = scr_System_CentralControl.current.ContentSetting.creature_on_male.value;
            else if (receiverGender == InteractionGenderType.female) value = scr_System_CentralControl.current.ContentSetting.creature_on_female.value;
            else if (receiverGender == InteractionGenderType.ambi) value = scr_System_CentralControl.current.ContentSetting.creature_on_ambi.value;
            else if (receiverGender == InteractionGenderType.animal) value = scr_System_CentralControl.current.ContentSetting.creature_on_creature.value;
            else if (receiverGender == InteractionGenderType.corpse) value = scr_System_CentralControl.current.ContentSetting.creature_on_necro.value;
        }
        else if (doerGender == InteractionGenderType.male)
        {
            if (receiverGender == InteractionGenderType.male) value = scr_System_CentralControl.current.ContentSetting.male_on_male.value;
            else if (receiverGender == InteractionGenderType.female) value = scr_System_CentralControl.current.ContentSetting.male_on_female.value;
            else if (receiverGender == InteractionGenderType.ambi) value = scr_System_CentralControl.current.ContentSetting.male_on_ambi.value;
            else if (receiverGender == InteractionGenderType.animal) value = scr_System_CentralControl.current.ContentSetting.male_on_creature.value;
            else if (receiverGender == InteractionGenderType.corpse) value = scr_System_CentralControl.current.ContentSetting.male_on_necro.value;
        }
        else if (doerGender == InteractionGenderType.female)
        {
            if (receiverGender == InteractionGenderType.male) value = scr_System_CentralControl.current.ContentSetting.female_on_male.value;
            else if (receiverGender == InteractionGenderType.female)  value = scr_System_CentralControl.current.ContentSetting.female_on_female.value;
            else if (receiverGender == InteractionGenderType.ambi) value = scr_System_CentralControl.current.ContentSetting.female_on_ambi.value;
            else if (receiverGender == InteractionGenderType.animal) value = scr_System_CentralControl.current.ContentSetting.female_on_creature.value;
            else if (receiverGender == InteractionGenderType.corpse) value = scr_System_CentralControl.current.ContentSetting.female_on_necro.value;
        }
        else if (doerGender == InteractionGenderType.ambi)
        {
            if (receiverGender == InteractionGenderType.male)  value = scr_System_CentralControl.current.ContentSetting.ambi_on_male.value;
            else if (receiverGender == InteractionGenderType.female) value = scr_System_CentralControl.current.ContentSetting.ambi_on_female.value;
            else if (receiverGender == InteractionGenderType.ambi) value = scr_System_CentralControl.current.ContentSetting.ambi_on_ambi.value;
            else if (receiverGender == InteractionGenderType.animal) value = scr_System_CentralControl.current.ContentSetting.ambi_on_creature.value;
            else if (receiverGender == InteractionGenderType.corpse) value = scr_System_CentralControl.current.ContentSetting.ambi_on_necro.value;
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

        FileInfo file = new System.IO.FileInfo($"{scr_System_Serializer.SavePath}/{fileName}.json");
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
    public string Language;
    public bool SafeMode;

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
    public string Language;
    public string SaveDescription;
    public bool SafeMode;
    public scr_System_Time_Serializable Time;
    public scr_System_CampaignManager_Serializable Campaign;

    public SaveFile() { }
    public SaveFile(bool createNew)
    {
        var playerRoom = scr_System_CampaignManager.current.Map.FindRoomByChara(0);
        var playerFloor = scr_System_CampaignManager.current.Map.GetFloorByRoomRefID(playerRoom.RefID);

        this.Time = scr_System_Time.current.GetSerializable();
        this.Campaign = scr_System_CampaignManager.current.GetSerializable();
        this.Version = Application.version;
        this.Language = LocalizeDictionary.Instance.Index.cachedLang;
        this.SaveDescription = LocalizeDictionary.QueryThenParse("ui_load_fileDescription")
                .Replace("$days$", Time.ElapesedTime.Days.ToString())
                .Replace("$hours$", Time.currentDate.TimeOfDay.Hours.ToString("D2"))
                .Replace("$minutes$", Time.currentDate.TimeOfDay.Minutes.ToString("D2"))
                .Replace("$playerName$", scr_System_CampaignManager.current.Player.FullName)
                .Replace("$floor$", playerFloor.displayName)
                .Replace("$room$", playerRoom.DisplayName);
        this.SafeMode = scr_System_CentralControl.current.isSafeMode;
    }
    public void LoadSave()
    {   // external call to updatehandler notifySL
        scr_System_Time.current.LoadSerializable(Time);
        scr_System_CampaignManager.current.LoadSerializable(Campaign);
    }

}

[System.Serializable]
public class DisplaySettings
{

    public string Language = "zh-cn";
    public ColorSetting BackgroundColor_Opaque = new ColorSetting(49, 77, 121, 255);
    public ColorSetting BackgroundColor_Transparent = new ColorSetting(0, 0, 0, 174);

    public ColorSetting TextColor_neutral = new ColorSetting(255, 255, 255, 255);
    public ColorSetting TextColor_hover = new ColorSetting(255, 235, 4, 255);
    public ColorSetting TextColor_toggle = new ColorSetting(0, 255, 255, 255);
    public ColorSetting TextColor_disabled = new ColorSetting(128, 128, 128, 255);
    public ColorSetting TextColor_conflict = new ColorSetting(255, 0, 0, 255);
    public ColorSetting TextColor_maxed = new ColorSetting(120, 0, 128, 255);

    [NonSerialized][JsonIgnore] public Color32 TextColor_transparent = new Color32(0, 0, 0, 0);

    public int MaxLogCount = 50;
    public int ClickDragForgiveness = 100;

    public BoolSetting displayPlayerPortraitInLogs = new BoolSetting(true, "displayPlayerPortraitInLogs");
}

[System.Serializable]
public class ContentSettings
{

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

}


[System.Serializable]
public class ColorSetting
{
    public byte r, g, b, a;
    public ColorSetting(byte r, byte g, byte b, byte a)
    {
        this.r = r;
        this.g = g;
        this.b = b;
        this.a = a;
    }

    public void SetColor(float r, float g, float b, float a)
    {
        this.r = Convert.ToByte((int)r);
        this.g = Convert.ToByte((int)g);
        this.b = Convert.ToByte((int)b);
        this.a = Convert.ToByte((int)a);
        initialized = false;
    }

    bool initialized = false;
    Color32 _cache = new Color32();
    [JsonIgnore] public Color32 Color { get
        {
            if (!initialized)
            {
                initialized = true;
                _cache = new Color32(r, g, b, a);
            }
            return _cache;
        }
        set
        {
            this.r = value.r;
            this.g = value.g;
            this.b = value.b;
            this.a = value.a;
            initialized = false;
        }
    }

    [JsonIgnore] public string Hex { get
        {
            return $"#{r:X2}{g:X2}{b:X2}{a:X2}";
} }
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
    View_Combat
    //View_Map
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
/*
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
}*/

public static class SpriteAsset
{
    public static Sprite transparent = Sprite.Create(null, new Rect(0, 0, 0, 0), new Vector2(0, 0));
}
