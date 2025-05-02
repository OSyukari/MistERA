using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Xml;
using System.Runtime.Serialization;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Runtime.CompilerServices;
using System.Buffers;
using Newtonsoft.Json.Linq;
using static Character_Trainable;
using System.IO;

public enum Humanoid_GenderAppearance
{
    Male,
    Female,
    Ambiguous
}

public enum Character_BodyType
{
    Default,
    BHUNP,
    CBBE_3BA
}



public class JSON_SO_Converter<T> : CustomCreationConverter<T> where T : ScriptableObject
{   // Reference: https://discussions.unity.com/t/how-to-use-json-net-to-deserialize-into-a-scriptable-object/778840/20
    // this converter is used in global json deserialize setting, so no need to call this individually.
    // all subsequent converters should be used in the same way as this one
    public override T Create(Type objectType)
    {
        if (typeof(T).IsAssignableFrom(objectType)) return (T)ScriptableObject.CreateInstance(objectType);
        return null;
    }
}

[System.Serializable]
public class Character_Trainable_SerializableTemplate_Index : I_IndexMergeable
{
    public List<Character_Trainable_SerializableTemplate> list = new List<Character_Trainable_SerializableTemplate>();

    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Character_Trainable_SerializableTemplate_Index;
        if (l == null) return;
        else if (l.list == null) return;
        else
        {
            this.list.AddRange(l.list);
        }
    }

    public CharaTemplate GetByCharaFilePath(string path)
    {
        var findresult = this.list.Find(x => x.FileLocation == path);
        if (findresult != null) return findresult.Template;
        else
        {   // try re-serialize from filepath, mainly for player chara where they're serialized via preset system and not chara system
            if (!File.Exists(path)) 
            {
                return null;
            }
            Character_Trainable_SerializableTemplate template = JsonConvert.DeserializeObject<Character_Trainable_SerializableTemplate>(File.ReadAllText(path), Utility.SerializerSettings);
            if(template == null) return null;
            this.list.Add(template);
            return template.Template;

        }
    }
    public CharaTemplate GetByCharaBaseID(string baseID)
    {
        var findresult = this.list.Find(x => x.baseID == baseID);
        return findresult == null ? null : findresult.Template;
    }
}

[System.Serializable]
public class Character_Trainable_SerializableTemplate
{
    public string FileLocation = "";
    public string baseID = "";
    public CharaTemplate Template = null;
}



[System.Serializable]
public class Character_Trainable : ScriptableObject, I_Disposable
{
    [SerializeField]
    [JsonProperty]
    protected int furnitureLockJobRef = -1;

    Job_Furniture furnitureLockJobCache = null;
    [JsonIgnore] protected Job_Furniture furniturLockJob
    {
        get
        {
            if (furnitureLockJobCache == null && furnitureLockJobRef != -1) furnitureLockJobCache = scr_System_CampaignManager.current.FindJobInstanceByID(furnitureLockJobRef) as Job_Furniture;
            return furnitureLockJobCache;
        }
    }
    [JsonIgnore] public Job_Furniture.JobContainer_Chara Jail
    {
        get
        {
            if (this.furniturLockJob == null) return null;
            var v = this.furniturLockJob.Container as Job_Furniture.JobContainer_Chara;
            return v;
        }
    }

    protected FurnitureInstance furnitureLockInstance
    {
        get
        {
            if (furniturLockJob == null) return null;
            return furniturLockJob.ParentInstance;
        }
    }

    [JsonIgnore] public bool Climaxing { get { return Stats.Climaxing != null && Stats.Climaxing.Severity >= 1; } }

    [JsonIgnore] public bool isRestrained { get { 
            return furnitureLockInstance != null;
        } }
    [JsonIgnore]
    public bool isImprisoned
    {
        get
        {
            return FactionManager.CurrentlyActiveFaction != null && FactionManager.CurrentlyActiveFaction.isPrisoner(this.RefID);
        }
    }
    [JsonIgnore] public bool canMove { get { return canAct && !isRestrained && !isImprisoned; } }
    public void LockFurnitureJob(Job_Furniture i)
    {
        //Debug.Log("LockFurnitureJob [" + FirstName + "] in [" + i.ParentInstance.DisplayName + "]");
        furnitureLockJobCache = i;
        furnitureLockJobRef = i.RefID;
        scr_System_CampaignManager.current.party.RemoveFromParty(this);
        //ChangeCurrentJob(i);
    }

    public void UnlockFurnitureJob()
    {
        furnitureLockJobCache = null ;
        furnitureLockJobRef = -1;
        ChangeCurrentJob(null);

    }

    // temporary inventory for unequipped items
    public List<int> inventory_ref;

    [JsonIgnore] public bool CanActInTimeStop { get { return this.RefID == 0; } }
    [JsonIgnore] public bool isTimeStopped { get { return scr_System_Time.current.TimeStop && !CanActInTimeStop; } }

    //public float portrait_offset_x = 0f;
    //public float portrait_offset_y = 0f;
    //public float portrait_offset_size = 1f;

    /// <summary>
    /// This field is empty in chara data, if chara data is re-deserealized then this field need to be manually copied
    /// </summary>
    public string FileLocation = "";
    [SerializeField][JsonProperty] protected string baseID = "";
    [JsonIgnore] public string BaseID { get { return baseID; } set { this.baseID = value; } }
    [SerializeField][JsonProperty] protected int referenceID = -1;
    [JsonIgnore] public int RefID { get { return referenceID; } }
    //[SerializeField] public string portraitPath = "";
    //[SerializeField] public string defaultIcon = "";
    //[SerializeField] public string defaultPortrait = "";


    [JsonIgnore] private Humanoid_Womb womb = null;
    [JsonIgnore] public Humanoid_Womb Womb {
        get { return womb; }
        set { womb = value; }
    }


    public void InitializeWithRefID(int refID)
    {
        this.referenceID = refID;

        this.Appearance = Template.Appearance;
        //Debug.Log("Setting Appearance to " + this.Appearance);

        if (this.Body == null) Body = new Character_Body(this);
        else Body.ReEstablishParent(this);
        Body.AddMissing();


        this.Memory = new MemoryManager(this);
        this.Stats.InitializeWithID(refID, Template.stat_STR, Template.stat_CON, Template.stat_PSY, Template.stat_WIL);

        //this.sexLogManager = new SexLogManager(refID);
        scr_System_Time.current.Observer_globalTime += Observer_GlobalMinute;
        scr_System_Time.current.Observer_globalTime_5min += Observer_GlobalMinute5;
        scr_System_Time.current.Observer_globalTime_Hours += Observer_GlobalHour;
        scr_System_Time.current.Observer_globalTime_Day += Observer_GlobalDay;
        scr_System_Time.current.Observer_globalTime_Day += Observer_DebugDailyRefresh;

        scr_UpdateHandler.current.Observer_PreUpdateTime += PreUpdateTime;
        scr_UpdateHandler.current.Observer_PostUpdateTime_2 += PostUpdateTime2;
        scr_UpdateHandler.current.Observer_PostUpdateTime_3 += PostUpdateTime3;
        inventory_ref = new List<int>();
        RestoreAll(true);

        this.interactionJobPointer = new Job_CharaCOM(refID);
        this.interactionJobRef = scr_System_CampaignManager.current.Register(InteractionJob);

        this.Relationships = new RelationshipManager(this);
        this.PortraitManager.RebuildInternal(this);
    }

    [SerializeField][JsonProperty] protected SkillManager _Skills = null;
    [JsonIgnore] public SkillManager Skills{
        get
        {
            if (_Skills == null) _Skills = new SkillManager(this); 
            return _Skills;
        }
    }


    public bool canAct
    {
        get
        {
            return !this.Stats.isConsciousnessUnconscious && !isTimeStopped;
        }
    }
    public bool hasStatKeyword(string statKeyword)
    {
        if (statKeyword == "") return true;
        if (this.Race.removeStatsKeyword.Contains(statKeyword) ||
            this.RaceTemplate.removeStatsKeyword.Contains(statKeyword))
            return false;

        // check race prop
        if (this.Race.addStatsKeyword.Contains(statKeyword)
            || this.RaceTemplate.addStatsKeyword.Contains(statKeyword))
            return true;

        return false;
    }

    private void PreUpdateTime()
    {
        this._cachedJobDescription = "";
        Body.ClearLastInteractedRefs();
    }

    public bool CompareStatValue(string statID, string operand, string value)
    {
        switch (statID)
        {
            case "hasPenisPiercing":
                return false;
            case "canAct":
                return Utility.CompareValue(canAct, operand, value);
            case "isInEstrus":  // check chara status depending on menstruation cycle, or if chara is drugged
                Debug.Log(FirstName + " Comparevalue isInEstrus [" + false + "] [" + operand + "] [" + value + "]");
                return Utility.CompareValue(false, operand, value);
            case "climaxed":    // check if chara has climaxed in current postupdatetime
               // Debug.LogError(FirstName + " Comparevalue climaxed [" + this.Climaxing + "] [" + operand + "] [" + value + "]");
                return Utility.CompareValue(this.Climaxing, operand, value); ;
            case "currentClimaxCount": // check chara consecutive climax count. how ? 
                //Debug.LogError("Checking ConsecutiveClimaxCount on " + FirstName + " value is " + this.Status.ConsecutiveClimaxCount);
                Debug.Log(FirstName + " Comparevalue currentClimaxCount [" + this.Stats.ConsecutiveClimaxCount + "] [" + operand + "] [" + value + "]");
                return Utility.CompareValue(this.Stats.ConsecutiveClimaxCount, operand, value); ;
            case "isUnconscious": // check chara sleeping or unconscious
                Debug.Log(FirstName + " Comparevalue isUnconscious [" + this.Stats.isConsciousnessUnconscious + "] [" + operand + "] [" + value + "]");
                return Utility.CompareValue(this.Stats.isConsciousnessUnconscious, operand, value);
            case "isTimestopped": // check if chara can act in timestop and if currently timestopped
               // bool isTimestopped = scr_System_Time.current.timeStop && !this.CanActInTimeStop;
                //Debug.LogError(FirstName+" Comparevalue isTimestopped [" + isTimeStopped + "] [" + operand + "] [" + value + "]");
                return Utility.CompareValue(isTimeStopped, operand, value);

            case "isCumReady":  // check if chara is currently over cum threshold
                Debug.Log(FirstName + " Comparevalue isCumReady [" + (Stats.SexStimulation.Severity >= Stats.CumThreshold) + "] [" + operand + "] [" + value + "]");
                return Utility.CompareValue(Stats.SexStimulation.Severity >= Stats.CumThreshold, operand, value);

            case "isFatigued":  // check if chara can act but currently low on stamina
                return false;

            default:return false;
        }
    }


    private void PostUpdateTime2()
    {
        this.Body.CheckClimax();
    }

    private void PostUpdateTime3()
    {
        this.Skills.FinalizeExperience();
    }

    private void Observer_GlobalMinute5(TimeSpan t)
    {
        this.Body.UpdateTimeMinute(t);
        this.Memory.Tick(t);
    }
    private void Observer_GlobalMinute(TimeSpan t)
    {
        if (isTimeStopped) t = TimeSpan.FromMinutes(0);
        else t = TimeSpan.FromMinutes(1);
        this.Stats.UpdateTimeMinute(t);
    }
    private void Observer_GlobalHour(TimeSpan t)
    {
        //Debug.Log("Character Observer_GlobalHour for [" + FirstName + "]");
        if (Stats.GetStatusSeverityByStringMatch("chara_status_sleeping") > 0)
        {
            lastSleepHour = scr_System_Time.current.getCurrentTime().Hour;

            // Recover chara based on sleep efficiency ?
        }
        this.Body.UpdateTimeHour(t);
    }
    private void Observer_GlobalDay(int updateOrder)
    {
        if (updateOrder != 2) return;
        if (Womb != null)
        {
            Womb.dayTick_Cycle();
        }
        
        if (Memory != null) Memory.DailyClear();

        // check food and sleep need
        if (FactionManager != null) FactionManager.DailyNeedConsumption();

        // not enough sleep
        if (hasStatKeyword("sleep") && RefID != 0 && Stats.SleepHours > 0 && lastSleepHour < 0) Stats.AddOrModStatus("chara_status_sleep_deprived", 1440, 1440);
        lastSleepHour = Stats.GetStatusSeverityByStringMatch("chara_status_sleeping") > 0 ? 0 : -1;

        List<string> updateMessage = new List<string>();
        this.Skills.UpdateAllSkills(updateMessage);
        if (updateMessage.Count > 0)
        {
            foreach (var i in FactionManager.HomeFactions) i.AddDailyReportEntry(String.Join("\n", updateMessage));
        }
        this.Relationships.DailyRefresh();
    }

    // Recovery
    public void FullRest(int recoveryStrength = -1)
    {
        var contextKey = new List<string>() { "fullrest" };
        var strMod = Stats.Strength.GetStatMod(contextKey);
        var conMod = Stats.Constitution.GetStatMod(contextKey);
        var willMod = Stats.Willpower.GetStatMod(contextKey);
        var psyMod = Stats.Psyche.GetStatMod(contextKey);

        if (Stats.Stamina != null) Stats.Stamina.Restore(recoveryStrength > 0 ? 10 * recoveryStrength : Stats.Stamina.MaxValue);
        if (Stats.Energy != null) Stats.Energy.Restore(recoveryStrength > 0 ? 10 * recoveryStrength : Stats.Energy.MaxValue);
        if (Stats.HP != null) Stats.HP.Restore(recoveryStrength > 0 ? recoveryStrength : conMod);
        if (Stats.MP != null) Stats.MP.Restore(recoveryStrength > 0 ? recoveryStrength : psyMod);

    }

    [SerializeField][JsonProperty] protected int lastSleepHour = -1;
    private void Observer_DebugDailyRefresh(int updateOrder)
    {
        if (updateOrder != 0) return;

    }

    

    public int GetStatusSeverity(string s)
    {
        return (int)Stats.GetStatusSeverityByStringMatch(s);
    }

    [JsonIgnore] public List<int> EquippedItemRefs
    {
        get
        {
            //Debug.LogError("EQUIPPEDREFS BEFORE BODY");
            if (Body == null) return new List<int>();
            return Body.EquippedItemRefs;
        }
    }

    protected CharaTemplate _template = null;
    [JsonIgnore] public CharaTemplate Template
    { get {
            if(_template == null)
            {
                //Debug.Log("Fetching Template data |" + this.BaseID + "|" + this.FileLocation+"|");
                if(this.BaseID != "" && this.BaseID != "PLAYER")
                {
                    _template = scr_System_Serializer.current.MasterList.CharacterTemplates.GetByCharaBaseID(BaseID);
                  //  Debug.Log("Fetching template for " + this.BaseID+", result exist? "+ (_template != null));
                }else if (this.FileLocation.Length > 0)
                {
                    _template = scr_System_Serializer.current.MasterList.CharacterTemplates.GetByCharaFilePath(FileLocation);
                   // Debug.Log("Fetching template for " + this.RefID + " with filepath "+this.FileLocation+", result exist? " + (_template != null));
                }
                else
                {
                    this._template = new CharaTemplate();
                    this._template.GenderAppearance_Set(Humanoid_GenderAppearance.Female, true, true);
                    Debug.LogError("Instantiating new Template for " + this.RefID);
                }
            }
            return _template;
    } }

    public Character_Trainable()
    {

    }
    public Character_Trainable(bool InitializeNew)
    {
        this.firstName = "Jane"; this.middleName = ""; this.lastName = "Doe";
        this.nameDisplayFormat = "chara_fullname_firstToLast";
        //Stats = new StatsManager(this);
        this.Origin = scr_System_Serializer.current.MasterList.Character_Origins.GetByID("charOrigin_EmissaryoftheTower");
        this.Race = scr_System_Serializer.current.MasterList.humanoid_Races.GetByID("humanRace_human");
        this.RaceTemplate = scr_System_Serializer.current.MasterList.humanoid_RaceTemplates.GetByID("humanRaceAddon_Magician");
        this.StartingGift = scr_System_Serializer.current.MasterList.Character_Origin_StartingOptions.GetByID(this.Origin.availableOptionsID[0]);
        this.traits = new List<string>();
        this.birthday = Utility.GetCampaignTime().AddYears(-Age);

        InitializeAllTraits();
        InitializeAllSkills();
    }
    public Character_Trainable(Character_Trainable.CharaTemplate template) : this(true)
    {
        this._template = template;
        InitializeAllTraits();
        InitializeAllSkills();
    }

    [SerializeField]
    [JsonProperty]
    protected StatsManager stats = null;

    [JsonIgnore] public StatsManager Stats { get { if (stats == null) stats = new StatsManager();
        return stats; } }

    [SerializeField][JsonProperty] protected PortraitManager Portrait = null;
    [JsonIgnore] public PortraitManager PortraitManager { get
        {
            if (this.Portrait == null)
            {
                Debug.Log("New PortraitManager instantiated for " + FirstName);
                this.Portrait = new PortraitManager(this);

            }
            return this.Portrait;
        } }
    public void LoadData(string saveData)
    {

        JsonUtility.FromJsonOverwrite(saveData, this);
    }

    public MemoryManager Memory = null;

    [SerializeField][JsonProperty] protected string firstName, middleName, lastName;
    [SerializeField][JsonProperty] protected string nameDisplayFormat;

    public void SetName(string firstName, string middleName, string lastName, string displayFormat){
        this.firstName = firstName;
        this.middleName = middleName;
        this.lastName = lastName;
        this.nameDisplayFormat = displayFormat;
    }
    [JsonIgnore] public string FirstName { get { return scr_System_Serializer.current.Dictionary.QueryThenParse(firstName, firstName); } set { firstName = value; } }
    [JsonIgnore] public string MiddleName { get { return middleName == "" ? "" : scr_System_Serializer.current.Dictionary.QueryThenParse(middleName, middleName); } set { middleName = value; } }
    [JsonIgnore] public string LastName { get { return lastName == "" ? "" : scr_System_Serializer.current.Dictionary.QueryThenParse(lastName, lastName); } set { lastName = value; } }

    [JsonIgnore] public string FullNameID { get { return baseID+"_"+referenceID; } }
    [JsonIgnore] public string FullName { get {
            //Debug.LogError(nameDisplayFormat);
            return scr_System_Serializer.current.Dictionary.QueryThenParse(nameDisplayFormat)
                .Replace("$lastName$", LastName).Replace(" $middleName$", MiddleName == "" ? "" : " "+MiddleName).Replace("$firstName$", FirstName);
        } }

    [SerializeField][JsonProperty] private string origin;
    [JsonIgnore] public Character_Origin Origin { 
        get { return scr_System_Serializer.current.MasterList.Character_Origins.GetByID(origin); } 
        set { origin = value.ID; } }

    [SerializeField][JsonProperty] protected string race = "humanRace_human";
    [JsonIgnore] public Humanoid_Race Race { 
        get { return scr_System_Serializer.current.MasterList.humanoid_Races.GetByID(race); } 
        set { race = value.ID; } }

    [SerializeField][JsonProperty] private string raceTemplate = "humanRaceAddon_Magician";
    [JsonIgnore] public Humanoid_RaceTemplate RaceTemplate { 
        get { return scr_System_Serializer.current.MasterList.humanoid_RaceTemplates.GetByID(raceTemplate); } 
        set { raceTemplate = value.ID; } }

    [SerializeField][JsonProperty] private string startingGift;
    [JsonIgnore] public Character_Origin_startingOption StartingGift { 
        get { return scr_System_Serializer.current.MasterList.Character_Origin_StartingOptions.GetByID(startingGift); } 
        set { startingGift = value.ID; } }

    [SerializeField][JsonProperty] private int currentJobRefID = -1;

    public Humanoid_GenderAppearance Appearance;

    [JsonIgnore] public bool isMale { get { return scr_System_CentralControl.current.GetGender(this).Contains(InteractionGenderType.male); } }

    [JsonIgnore] public bool isFemale { get { return scr_System_CentralControl.current.GetGender(this).Contains(InteractionGenderType.female); } }

    [JsonIgnore] public bool isAnimal { get { return this.Race.ID.Contains("beast"); } }
    [JsonIgnore] public bool isDead { get { return false; } }
    [JsonIgnore] public bool isCreature { get { return this.Race.ID.Contains("creature"); } }
    [JsonIgnore] public bool isHumanoid { get { return this.Race.ID.Contains("humanRace"); } }
    [JsonIgnore] public int CurrentJobRefID { get { return currentJobRefID; } }
    private Job currentJobPointer = null;
    [JsonIgnore] public Job CurrentJob { 
        get { 
            if (currentJobRefID == -1) return null;
            if (currentJobPointer == null) currentJobPointer = scr_System_CampaignManager.current.FindJobInstanceByID(currentJobRefID);
            /*if(currentJobPointer.ParentRoom != null && currentJobPointer.ParentRoom.RefID != scr_System_CampaignManager.current.Map.FindRoomByChara(RefID).RefID)
            {
                currentJobRefID = -1;
                currentJobPointer = null;
                Debug.Log("Resetting job pointer for chara " + FirstName + " due to no longer in same room with job");
            }*/
            return currentJobPointer; } }


    [SerializeField]
    [JsonProperty]
    protected List<int> activeJobRefs = new List<int>();
    public void ChangeCurrentJob(Job job = null, string targetCOMid = "", string targetCOMTag = "")
    {
        this._cachedJobDescription = "";
        //Debug.Log("Changing " + FirstName + "'s job from "+(CurrentJob == null?"null":CurrentJob.DisplayName)+" to " + (job == null ? "NULL" : String.Join(",", job.allusableCOMStrings)));
        if (this.CurrentJob != null && (job == null || CurrentJob.RefID != job.RefID)) CurrentJob.RemoveActor(RefID);

        if (job == null)
        {
            //if (this.CurrentJob != null) CurrentJob.RemoveActor(RefID);
            // reset to empty;
            currentJobRefID = -1;
            this.currentJobPointer = null;
            // can look for new job

        }
        else
        {
            currentJobRefID = job.RefID;
            currentJobPointer = job;
            job.AddActor(RefID, targetCOMid, targetCOMTag);
            // fetch new actionpackage from job
           // CurrentJob.UpdateActorPackage(this, out string ss);
        }
        if (RefID == 0) scr_System_CampaignManager.current.NotifyPlayerJobChange(job == null? -1:job.RefID, job);
    }



    public Manageable.HourlySchedule GetJobPost(int hour = -1)
    {
        if (FactionManager == null) return null;
        else
        {
            if(hour == -1) hour = scr_System_Time.current.getCurrentTime().Hour;
            return FactionManager.CurrentJobPost(hour);
        }
    }

    public string CurrentJobName(int hour = -1)
    {
        if (FactionManager == null) return "none";
        return FactionManager.CurrentJobName(hour);
    }

    public Manageable CurrentJobScheduleFaction(int hour = -1)
    {
        if (FactionManager == null) return null;
        else return FactionManager.CurrentJobScheduleFaction(hour);
    }

    [SerializeField][JsonProperty] private Character_Factions factionManager = null;
    [JsonIgnore] public  Character_Factions FactionManager { get { if (factionManager == null)
            {
                //Debug.LogError("new faction manager created");
                factionManager = new Character_Factions();
                factionManager.ReEstablishParentData(this);
            }
            return factionManager;
        } }

    

    public void InitializeFaction(Manageable m, bool isManager)
    {
        string initFactionID = (m == null ? "" : m.ID);
        this.FactionManager.SetHomeFaction(initFactionID, isManager);
    }



    public void AddTrait(string s) { traits.Add(s); }
    public void AddTrait(Traits s) { traits.Add(s.ID); }
    public void ResetTrait() { traits = new List<string>(); }
    public bool HasTrait(string s) { return traits.Contains(s); }
    public bool HasTrait(Traits t) { return traits.Contains(t.ID); }

    [JsonIgnore] public int Age { get { return 22; } }
    [SerializeField] private bool noAging = false;

    [SerializeField] private DateTime birthday;
    [JsonIgnore]public DateTime Birthday { get { return birthday; } set { birthday = value; } }

    public void TryGetJob(int currentHour, List<string> s)
    {
        if (RefID == 0)
        {
            if(CurrentJob != null && CurrentJob.hasActorCompletedJob(0)) ChangeCurrentJob(null);
            return;
        }

        bool debugLog = isImprisoned;

        string ss = FirstName + ": ";
        string jobInternalStatus;
        if (CurrentJob != null)
        {   // has job, but job cannot give a valid package
            bool hasPackage = CurrentJob.UpdateActorPackage(this, out jobInternalStatus);
            ss += jobInternalStatus;
            if (!hasPackage)
            {
                ss += $" || Job cannot give valid package, releasing from |{CurrentJob.RefID}|";
                ChangeCurrentJob(null);
            }
            // release from job
        }

        if (scr_System_CampaignManager.current.PlayerPartyMembers.Contains(RefID))
        {
            ss += "is following player";
            if(s != null) s.Add(ss);
            return;
        }
        if (CurrentJob != null)
        {
            ss += "already have a job " + CurrentJob.RefID + " with " + (CurrentJob.allusableCOMStrings.Count > 5 ? CurrentJob.allusableCOMStrings.Count + " coms" : " coms[" + String.Join(", ", CurrentJob.allusableCOMStrings) + "]") + " in room " + scr_System_CampaignManager.current.GetCharaRoomInstance(RefID).DisplayName + " descriptions: " + GetJobDescription();
            if(s != null) s.Add(ss);
            if (!CurrentJob.CanBeInterrupted ||
                CurrentJob.actorRefID.Contains(scr_System_CampaignManager.current.Player.RefID) ||
                CurrentJob.isPlayerRelatedJob)
            {
                return;
            } 
        }
        if (Climaxing)
        {
            ss += "is climaxing";
            if(s != null) s.Add(ss);
            return;
        }
        if (!canAct)
        {
            if (isTimeStopped)
            {
                ss += "is in timestop";
                if(s != null) s.Add(ss);
                return;
            }
            else if (Stats.isConsciousnessUnconscious)
            {   // unconscious return
                if (Stats.HasStatusByStringMatch("chara_status_sleeping"))
                {
                    ss += "is sleeping";
                }
                else
                {
                    ss += "is unconscious";
                }
                if(s != null) s.Add(ss);
                return;
            }
            else
            {
                ss += "chara cannot act for undefined reason";
                if(s != null) s.Add(ss);
                return;
            }
        }
        /*
        if (isRestrained)
        {
            ss += "is being restrained";
            s.Add(ss);
            return;
        }*/
        if (this.InteractionJob.isActive)
        {
            ss += this.InteractionJob.GetJobDescription(this.RefID);
            if(s != null) s.Add(ss);
            return;
        }

        List<string> factionstring = new List<string>();
        foreach (var faction in FactionManager.Factions) factionstring.Add(faction.ID + (faction.isCharaManager(this) ? "*" : ""));
        //if (chara.CurrentJobRefID)

        var jobpost = GetJobPost(currentHour);
        COM currentScheduleCOM = jobpost == null ? null : jobpost.getRandCOM;

        // if sleeping and current schedule is not sleep 
        if ((currentScheduleCOM == null || currentScheduleCOM.ID != "com_furniture_sleep") && (CurrentJob != null && CurrentJob.hasActivePackge(RefID, "com_furniture_sleep")))
        {   // try to wake up
            Job job = null;

            ss += "Changing job to " + (job == null ? "NULL" : String.Join("|", job.allusableCOMStrings) + $"|{(job == null ? "null" : job.RefID)}| in room [" + job.ParentRoom.DisplayName + "]");
            if(s != null) s.Add(ss);
            ChangeCurrentJob(job, job == null || currentScheduleCOM == null ? "" : currentScheduleCOM.ID);

            // check if can break sleep (check if still tired), if cannot break then return here
        }

        // Redress check
        if (shouldRedress)
        {
            //Debug.LogError(FirstName + " should redress");
            if (CurrentJob != null && (CurrentJob.hasActivePackge(RefID, "com_furniture_restroom_fix") || (CurrentJob.allusableCOMs.Find(x=>x.ID == "com_furniture_restroom_fix")!=null && CurrentJob.hasActivePathing(RefID))))
            {   // if current is of same type as schedule, dont do anything. 
                //Debug.LogError(FirstName + " should redress, current job has related COM");
                return;
            }
            else
            {   // current job is null, or current job is not schedule
                //Debug.LogError(FirstName + " should redress, fetching new job");
                // at this point we know the previous job can be break
                foreach (Manageable faction in FactionManager.Factions)
                {   // get closest schedule job
                    List<Job_Furniture> possibleJobs = faction.GetValidJobsByCOMID(this, "com_furniture_restroom_fix", s);
                    if (possibleJobs != null && possibleJobs.Count > 0)
                    {
                        Job job = possibleJobs[0];
                        ss += "Changing job to " + (job == null ? "NULL" : String.Join(",", job.allusableCOMStrings) + $"|{(job == null ? "null" : job.RefID)}| in room [" + job.ParentRoom.DisplayName + "]");
                        if(s != null) s.Add(ss);
                        ChangeCurrentJob(job, "com_furniture_restroom_fix");
                        return;
                    }
                }
            }
        }

        


        /// if previous almost over (time less than half and time less than 15min)
        /// if still in pathing

        if (CurrentJob != null)
        {   // if current job is not pathing and less than 15 min then keep doing it
            List<ActionPackage> p = CurrentJob.ActivePackages.FindAll(x => x.actorRefs.Contains(RefID));
            bool tryFinishJob = true;
            foreach (var package in p) if (package is ActionPackage_PathTo || package.Duration > 10 || package.targetCOM.comTags.Contains("recreation")) tryFinishJob = false;
            if (tryFinishJob) return;
        }


        if (currentScheduleCOM != null && currentScheduleCOM.ID != "com_furniture_sleep")
        {   // if current schedule has available job (exclude sleep)

            // first get command by ID, if command 
            // first check if chara is already doing related job == currentjob exist
            if (CurrentJob != null && (CurrentJob.hasActivePackge(RefID, currentScheduleCOM.ID) || (CurrentJob.allusableCOMs.Contains(currentScheduleCOM) && CurrentJob.hasActivePathing(RefID))))
            {   // if current is of same type as schedule, dont do anything. 
                return;
            }
            else
            {   // current job is null, or current job is not schedule

                // at this point we know the previous job can be break
                foreach (Manageable faction in FactionManager.Factions)
                {   // get closest schedule job
                    List<Job_Furniture> possibleJobs = faction.GetValidJobs_Jobs(this, currentHour, s);
                    if (possibleJobs != null && possibleJobs.Count > 0)
                    {
                        Job job = possibleJobs[0];
                        var targetID = ((job == null || currentScheduleCOM == null) ? "" : currentScheduleCOM.ID);
                        ss += "Changing job to faction "+ faction.FactionDisplayName+"" + (job == null ? "NULL" : String.Join(",", job.allusableCOMStrings) + $"|{(job == null ? "null" : job.RefID)}| in room [" + job.ParentRoom.DisplayName + "]");
                        if(s != null) s.Add(ss);
                        ChangeCurrentJob(job, targetID);
                        return;
                    }
                }
            }
        }

        // here, character is not doing scheduled job
        // temporarily disable eat cuz need to restrict food hours in faction management
        if (false && canEat)
        {   // try get food
            if (CurrentJob != null && CurrentJob.allusableCOMs.Find(x => x.comTags.Contains("food_meal")) != null)
            {   // if already eating (dont care if it's pathing or executing)
                return;
            }

            foreach (Manageable faction in FactionManager.HomeFactions)
            {
                List<Job_Furniture> possibleJobs = faction.GetValidJobs_Meal(this, currentHour, s);
                if (possibleJobs != null && possibleJobs.Count > 0)
                {
                    Job job = possibleJobs[0];
                    ss += "Changing job to eating " + (job == null ? "NULL" : String.Join(",", job.allusableCOMStrings) + $"|{(job == null ? "null" : job.RefID)}| in room [" + job.ParentRoom.DisplayName + "]");
                    if(s != null) s.Add(ss);
                    ChangeCurrentJob(job,"","food_meal");
                    return;
                }
            }
        }   // make eating not interruptible ?
        

        // can sleep, should sleep, hasSleepPlan
        if (shouldSleep)
        {   // if current schedule is sleep then go to sleep
           
            if (CurrentJob != null && CurrentJob.allusableCOMs.Find(x => x.comTags.Contains("sleep")) != null)
            {   // if already using a furniture that allows sleep, then its a valid one, return
                return;
            }

            foreach (Manageable faction in FactionManager.HomeFactions)
            {
                List<Job_Furniture> possibleJobs = faction.GetValidJobs_Sleep(this, currentHour, s);
                if (possibleJobs != null && possibleJobs.Count > 0)
                {
                    Job job = possibleJobs[0];
                    ss += "Changing job to sleep " + (job == null ? "NULL" : String.Join(",", job.allusableCOMStrings) + $"|{(job == null ? "null" : job.RefID)}| in room [" + job.ParentRoom.DisplayName + "]");
                    if(s != null) s.Add(ss);
                    ChangeCurrentJob(job, "com_furniture_sleep");
                    return;
                }
            }
        }


        if (shouldRest)
        {
            if (CurrentJob != null && CurrentJob.allusableCOMs.Find(x => x.comTags.Contains("rest")) != null) return;
            else
            {
                List<Job_Furniture> possibleResting = new List<Job_Furniture>();

                foreach (Manageable faction in FactionManager.HomeFactions)
                {
                    possibleResting.AddRange(faction.GetValidJobs_nonJob_byTags(this, currentHour, "rest", s));
                    break;
                }

                if (possibleResting.Count > 0)
                {
                    Job job = possibleResting[Utility.GetRandIndexFromListCount(possibleResting.Count)];
                    ss += "Changing job to resting job " + (job == null ? "NULL" : String.Join(",", job.allusableCOMStrings) + $" |{(job == null ? "null" : job.RefID)}| in room [" + job.ParentRoom.DisplayName + "]");
                    if (s != null) s.Add(ss);
                    ChangeCurrentJob(job, "", "rest");
                    return;
                }
            }
        }


        if (isAnimal)
        {        // try find interaction job (rape job)
            if (CurrentJob != null)
            {
                //Debug.LogError("Animal find job, current job is not null");
                if (CurrentJob.allusableCOMs.Find(x => x.comTags.Contains("sex")) != null)
                {
                    Debug.Log("Animal find job, current job is not null: Animal current job has sex, abort");
                    return;
                }
                else if (CurrentJob.allusableCOMs.Find(x => x.comTags.Contains("initSex")) != null)
                {
                    Debug.Log("Animal find job, current job is not null: Animal current job has initsex, abort");
                    return;
                }
            }
            //Debug.LogError("Animal looking for new target");
            List<Job_CharaCOM> possibletargets = new List<Job_CharaCOM>();

            foreach (Manageable faction in FactionManager.HomeFactions) possibletargets.AddRange(faction.GetValidCharaCOM(this, s));

            if (possibletargets.Count > 0)
            {
                Job job = possibletargets[Utility.GetRandIndexFromListCount(possibletargets.Count)];
                ss += "Changing job to having sex with " + (job == null ? "NULL" : "interaction |"+(job == null ? "null" : job.RefID)+"| in room[" + job.ParentRoom.DisplayName + "]");
                //Debug.LogError("Animal fucking ");
                if(s != null) s.Add(ss);
                ChangeCurrentJob(job, "com_interaction_initiateSex");
                return;
            }
            
        }
        else
        {
            // if still no job, set to look for recreation
            // need to find : is there location restriction ? search currently at ?
            // include search : character currently at + home faction
            if (CurrentJob != null && CurrentJob.allusableCOMs.Find(x => x.comTags.Contains("recreation")) != null)
            {
                // already set to a recreation job

                // but, job might not have active package
                return;
            }
            else
            {
                List<Job_Furniture> possibleRecreations = new List<Job_Furniture>();

                foreach (Manageable faction in FactionManager.HomeFactions)
                {
                    possibleRecreations.AddRange(faction.GetValidJobs_nonJob_byTags(this, currentHour, "recreation", s,true));
                    break;
                }

                if (possibleRecreations.Count > 0)
                {
                    Job job = possibleRecreations[Utility.GetRandIndexFromListCount(possibleRecreations.Count)];
                    ss += "Changing job to " + (job == null ? "NULL" : String.Join(",", job.allusableCOMStrings) + $"|{(job == null ? "null" : job.RefID)}| in room [" + job.ParentRoom.DisplayName + "]");
                    if(s != null) s.Add(ss);
                    ChangeCurrentJob(job,"","recreation");
                    return;
                }
            }
        }
    }

    [JsonIgnore] public bool canEat { get {
            return this.hasStatKeyword("hunger") && this.Stats.GetStatValue("stats_derived_foodConsumption") >= 1; } }
    [JsonIgnore] public bool canSleep { get {
            if (!hasSleepNeed) return false;
            if (this.Stats.Fatigue != null && this.Stats.Fatigue.Severity > 0.9) return true;
            return false; } }
    [JsonIgnore] public bool isSleeping { get
        {
            return Stats.GetStatusSeverityByStringMatch("chara_status_sleeping") > 0;
        } }
    [JsonIgnore] public bool hasSleepNeed
    {
        get
        {
            if (!this.hasStatKeyword("sleep")) return false;
            if (this.Stats.SleepHours < 1) return false;
            return true;
        }
    }

    [JsonIgnore]public bool shouldSleep { get
        {
            if (!hasSleepNeed) return false;
            if (this.FactionManager.HasSleepSchedule) 
            {
                var v = GetJobPost();
                return v != null && v.comIDs.Contains("com_furniture_sleep");
            }
            else
            {
                return canSleep;
            }
        } }

    [JsonIgnore]public bool shouldRest { get
        {
            if (Stats.Stamina != null && Stats.Stamina.ValuePercentile < 0.5) return true;
            if (Stats.Energy != null && Stats.Energy.ValuePercentile < 0.5) return true;
            return false;
        } }

    /// <summary>
    /// This check ideally should be more complex.
    /// </summary>
    [JsonIgnore] public bool isUndressed { get { return canRedress; } }
    [JsonIgnore] public bool canRedress { get
        {
            return this.inventory_ref.Count > 0;
        } }
    [JsonIgnore] public bool shouldRedress { get { return canRedress && true; } }

    [JsonIgnore] private List<string> traits = new List<string>();

    [JsonIgnore] public List<Traits> Traits
    {
        get
        {
            
            List<Traits> t = new List<Traits>();
            return t;
            foreach (string s in traits)
            {
                var ss = scr_System_Serializer.current.GetByNameOrID_Traits(s);
                if (ss != null) t.Add(ss);
            }
            return t;
        }
    }
    protected void InitializeAllTraits()
    {
        traits = new List<string>();

        foreach(List<scr_Traits_Group> list in scr_System_Serializer.current.index_TraitsAll.traits_All)
        {
            foreach (scr_Traits_Group group in list)
            {
                if (group.SortType == Trait_Group_Type.Singular)
                {
                    // dont do anything
                }
                else if (group.Type == Trait_Type.Body)  
                {
                    // exclude body untyped
                }
                else if (group.SortType == Trait_Group_Type.SortedList || group.SortType == Trait_Group_Type.UnsortedList)
                {
                    // skip this, redo trait system
                    //AddTrait(group.getNeutralinGroup());
                }
                else
                {

                }
            }
        }
    }


    //[JsonIgnore] List<Skills> Skills = null;
    protected void InitializeAllSkills()
    {
        /*
        this.Skills = new List<Skills>();
        foreach(Skills_Full skl in scr_System_Serializer.current.index_SkillsAll.list)
        {
            Skills s = skl.MakeSkill();
            s.Owner = this;
            this.Skills.Add(s);
        }*/
    }


    public SkillInstance GetSkill(string skillID)
    {
        foreach (SkillInstance s in this.Skills.Skills)
        {
            if (s.BaseRef.ID == skillID) return s;
        }
        return null;
    }
    public int GetSkillLevel(string skillID)
    {
        foreach(Skills s in this.Template.Skills)
        {
            if (s.ID == skillID) return s.GetSkillLevel();
        }

        return -1;
    }

    public void EndOngoingMemory(DateTime actorJoinTime, string ignoreMemorywithTag = "")
    {
        //Debug.Log("CHARA " + FullName + " END ONGOING MEMORY");
        if (this.Memory.Last == null) return;
        if (ignoreMemorywithTag == "" || !this.Memory.Last.Tags.Contains(ignoreMemorywithTag)) this.Memory.EndOngoingLog(actorJoinTime);
    }

    [SerializeField][JsonProperty] private int interactionJobRef = -1;
    private Job_CharaCOM interactionJobPointer = null;
    [JsonIgnore] public Job_CharaCOM InteractionJob { get { if (interactionJobPointer == null) interactionJobPointer = scr_System_CampaignManager.current.FindJobInstanceByID(interactionJobRef) as Job_CharaCOM;
            return interactionJobPointer;
        } }


    protected string _cachedJobDescription = "";
    public string GetJobDescription()
    {
        if(_cachedJobDescription == "")
        {
            if (this.InteractionJob != null && this.InteractionJob.isActive) _cachedJobDescription = this.InteractionJob.GetJobDescription(RefID);
            else if (this.CurrentJob != null) _cachedJobDescription = this.CurrentJob.GetJobDescription(RefID);
            else if (this.isSleeping) _cachedJobDescription = scr_System_Serializer.current.Dictionary.Query("chara_currentjob_sleeping");
            else if (this.Stats.isConsciousnessUnconscious) _cachedJobDescription = "chara_currentjob_unconscious";
            else _cachedJobDescription = "None";
        }

        return _cachedJobDescription;
    }

    public bool PostponeClimax()
    {
        return false;
    }



    public void RestoreAll(bool regenerateBody = false)
    {
        if (regenerateBody)
        {
            Body.AddMissing();
            List<string> l = new List<string>();
            l.Add("womb");
            if (Body.HasBodyTag(new List<string>() { "womb" }) && this.womb == null)
            {
                switch (Race.ID)
                {
                    case "humanRace_elf":
                    case "humanRace_angel":
                        womb = new Womb_Elf(RefID, noAging);
                        break;
                    case "humanRace_demon":
                    case "humanRace_beastkin_cat":
                        womb = new Womb_Furry(RefID, noAging);
                        break;
                    default:
                        womb = new Womb_Human(RefID, noAging);
                        break;

                }
                
            }
        }
        this.Stats.RestoreAll();

    }



    //public bool canBeFucked() { return (womb == null ? false : true) || (anus == null ? false : true) || (mouth == null ? false : true); }
    // instead check individual organ before

    public Character_Body Body = null;


    public BodyPart_Instance GetPartByEquipRef(int equiRef)
    {
        foreach(var part in Body.Body)
        {
            if (part.EquippedRefIDs.Contains(equiRef) || part.GetInternalByEquipRef(equiRef) != null) return part;
        }
        return null;
    }

    public List<int> EquipItem(int itemRefID, bool forceEquip = false)
    {
        Item_Instance item = scr_System_CampaignManager.current.FindItemInstanceByID(itemRefID);

        if (item != null)
        {
            ItemComponent_Equippable comp = item.GetComp("ItemComponent_Equippable") as ItemComponent_Equippable;
            if (comp != null)
            {
                Stats.RefreshAllStats(true);
                return Body.EquipItem(itemRefID, comp.equipCount, forceEquip); 
            }
        }
        return null;
    }

    /// <summary>
    /// Conditions:<br/>
    /// - item above revealing filter, or is outer 
    /// - revealing less than armor, or allow unequip armor
    /// - not locked, or unequip lock
    /// </summary>
    /// <param name="itemRefID"></param>
    /// <param name="RevealingFilter"></param>
    /// <param name="unequipArmor"></param>
    /// <param name="unequipLocked"></param>
    public void UnequipItem(int itemRefID, int RevealingFilter = -1, bool unequipArmor = false, bool unequipLocked = false)
    {
        var comp = scr_System_CampaignManager.current.FindItemInstanceByID(itemRefID).GetComp_Equippable();
        if (((int)comp.revealing >= RevealingFilter || comp.equipLayer == BodyEquipLayer.Outer) && (comp.revealing < Revealing.Armored || unequipArmor ) && (!comp.lockable || unequipLocked))
        {
            if (Body.UnequipItem(itemRefID))
            {
                inventory_ref.Add(itemRefID);
            }
            Stats.RefreshAllStats(true);
        }
    }


    /// <summary>
    /// default undress all
    /// </summary>
    /// <param name="layer"></param>
    public void Undress(BodyEquipLayer layer = BodyEquipLayer.None, int RevealingFilter = -1, bool unequipArmor = false, bool unequipLocked = false)
    {
        if (layer == BodyEquipLayer.None)
        {
            //Undress(BodyEquipLayer.Shell, RevealingFilter, unequipArmor, unequipLocked);
            Undress(BodyEquipLayer.Outer, RevealingFilter, unequipArmor, unequipLocked);
            Undress(BodyEquipLayer.Inner, RevealingFilter, unequipArmor, unequipLocked);
            Undress(BodyEquipLayer.Skin, RevealingFilter, unequipArmor, unequipLocked);
        }
        else
        {
            foreach (BodyPart_Instance instance in Body.Body)
            {
                foreach (BodyPartEquipSlot slot in instance.availableSlots)
                {
                    int i = instance.GetEquip(layer, slot);
                    if (i <= 0) continue;
                    UnequipItem(i, RevealingFilter, unequipArmor, unequipLocked);
                }

            }
        }
        
    }

    public void Redress(BodyEquipLayer layer = BodyEquipLayer.None)
    {
        if (layer == BodyEquipLayer.None)
        {
            Redress(BodyEquipLayer.Skin);
            Redress(BodyEquipLayer.Inner);
            Redress(BodyEquipLayer.Outer);
            //Redress(BodyEquipLayer.Shell);
        }
        else
        {
            for(int counter = inventory_ref.Count - 1; counter >= 0; counter--)
            {
                Reequip(inventory_ref[counter], layer);
            }
        }
    }

    /// <summary>
    /// Reequip from own inventory ref
    /// </summary>
    /// <param name="itemRefID"></param>
    public void Reequip(int itemRefID, BodyEquipLayer layerFilter = BodyEquipLayer.None)
    {
        Item_Instance item = scr_System_CampaignManager.current.FindItemInstanceByID(itemRefID);
        //Debug.Log("Redressing item ref " + item.DisplayName);
        if (item != null && inventory_ref.Contains(itemRefID))
        {
            ItemComponent_Equippable comp = item.GetComp("ItemComponent_Equippable") as ItemComponent_Equippable;
            if (comp != null && (layerFilter == BodyEquipLayer.None || comp.equipLayer == layerFilter))
            {
                Body.EquipItem(item.RefID, comp.equipCount, true);
                inventory_ref.Remove(itemRefID);
            }
        }
    }

    public void DisposeInternal()
    {

    }


    public void OnBeforeSerialize()
    {

    }

    public void OnAfterDeserialize()
    {
        string s = "Loaded Chara " + FullName + "\n";
        if (this.factionManager != null) FactionManager.ReEstablishParentData(this);
        if (this.Body != null) Body.ReEstablishParent(this);
        if (this.Memory != null) Memory.ReEstablishParent(this);
        if (this.Relationships != null) Relationships.ReEstablishParent(this);
        if (this.Skills != null) Skills.ReEstablishParent(this);
        if (this.Stats != null) Stats.ReEstablishParent(this);  // stats require memory
        if (this.Portrait != null) Portrait.RebuildInternal(this);

        bool value = true;
        if (CurrentJob != null)
        {
            s += "CurrentJob " + CurrentJob.GetJobDescription(this.referenceID);

            if (!CurrentJob.actorRefID.Contains(this.referenceID))
            {
                CurrentJob.AddActor(this.referenceID);
                s += " MISSING ACTORREF, READDED";
            }
            s += ", hasActivePackage? [" + CurrentJob.hasActivePackge(this.referenceID) + "] hasActivePathing? ["+CurrentJob.hasActivePathing(this.referenceID)+"] isManagedActor ["+CurrentJob.actorRefID.Contains(this.referenceID)+"]";
            value = CurrentJob.actorRefID.Contains(this.referenceID) && value;
        }
        else
        {
            s += "Has no current job";
        }
        //if (value) Debug.Log(s);
        //else Debug.LogError(s);
        scr_System_Time.current.Observer_globalTime += Observer_GlobalMinute;
        scr_System_Time.current.Observer_globalTime_Hours += Observer_GlobalHour;
        scr_System_Time.current.Observer_globalTime_Day += Observer_GlobalDay;
        scr_System_Time.current.Observer_globalTime_Day += Observer_DebugDailyRefresh;

        scr_UpdateHandler.current.Observer_PreUpdateTime += PreUpdateTime;
        scr_UpdateHandler.current.Observer_PostUpdateTime_2 += PostUpdateTime2;
        scr_UpdateHandler.current.Observer_PostUpdateTime_3 += PostUpdateTime3;
    }


    public RelationshipManager Relationships = null;

    [JsonIgnore] public int Height
    {
        get
        {
            if (Template != null) return Template.Height;
            return 160;
        }
    }



    [System.Serializable]
    public class CharaTemplate
    {
        public Humanoid_GenderAppearance Appearance = Humanoid_GenderAppearance.Female;
        public Character_BodyType BodyType = Character_BodyType.Default;
        public int stat_STR = 10, stat_CON = 10, stat_PSY = 10, stat_WIL = 10;

        public string personalityID = "personality_default";

        [SerializeField][JsonProperty] private string sensitivity_B = "trait_Sensitivity_B_default";
        [SerializeField][JsonProperty] private string sensitivity_M = "trait_Sensitivity_M_default";
        [SerializeField][JsonProperty] private string sensitivity_C = "trait_Sensitivity_C_default";
        [SerializeField][JsonProperty] private string sensitivity_V = "trait_Sensitivity_V_default";
        [SerializeField][JsonProperty] private string sensitivity_A = "trait_Sensitivity_A_default";

        [SerializeField][JsonProperty] private string size_B = "trait_Size_B_none";
        [SerializeField][JsonProperty] private string size_P = "trait_Size_P_none";
        [SerializeField][JsonProperty] private string size_V = "trait_Size_V_none";
        [SerializeField][JsonProperty] private string size_A = "trait_Size_A_none";

        public List<RelationshipManager.presetRelationship> initialRelationship = new List<RelationshipManager.presetRelationship>();
        public List<presetInventory> initialInventory = new List<presetInventory>();

        public List<Skills> Skills;

        public int Height = 163;

        public CharaTemplate() {  }

        public void GenderAppearance_Set(Humanoid_GenderAppearance app, bool forceDefaultGenital = false, bool forceDefaultSensitivity = false)
        {
            this.Appearance = app;
            if (forceDefaultGenital)
            {
                switch (app)
                {
                    case Humanoid_GenderAppearance.Male:
                        Size_P = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Size_P").getNeutralinGroup();
                        Size_B = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Size_B").entries[1];
                        Size_V = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Size_V").entries[0];
                        Size_A = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Size_A").getNeutralinGroup();
                        break;
                    case Humanoid_GenderAppearance.Female:
                        Size_P = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Size_P").entries[0];
                        Size_B = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Size_B").getNeutralinGroup();
                        Size_V = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Size_V").getNeutralinGroup();
                        Size_A = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Size_A").getNeutralinGroup();
                        break;
                    case Humanoid_GenderAppearance.Ambiguous:
                        Size_P = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Size_P").getNeutralinGroup();
                        Size_B = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Size_B").getNeutralinGroup();
                        Size_V = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Size_V").getNeutralinGroup();
                        Size_A = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Size_A").getNeutralinGroup();
                        break;
                    //case Humanoid_GenderAppearance.Inhuman:
                    default:
                        Size_P = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Size_P").entries[0];
                        Size_B = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Size_B").entries[0];
                        Size_V = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Size_V").entries[0];
                        Size_A = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Size_A").entries[0];
                        break;
                }
            }

            if (forceDefaultSensitivity)
            {
                Sensitivity_B = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Sensitivity_B").getNeutralinGroup();
                Sensitivity_M = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Sensitivity_M").getNeutralinGroup();
                Sensitivity_C = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Sensitivity_C").getNeutralinGroup();
                Sensitivity_V = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Sensitivity_V").getNeutralinGroup();
                Sensitivity_A = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Sensitivity_A").getNeutralinGroup();
            }


        }

        [System.Serializable]
        public class presetInventory
        {
            public string ID;
            public string nameOverwrite = "";
        }

        [JsonIgnore]
        public bool isMale
        {
            get
            {
                if (this.Size_P.ID != "trait_Size_P_none") return true;
                else return false;
            }
        }

        [JsonIgnore]
        public bool isFemale
        {
            get
            {
                if (this.Size_V.ID != "trait_Size_V_none") return true;
                else return false;
            }
        }


        [JsonIgnore]
        public Traits Sensitivity_B
        {
            get { return scr_System_Serializer.current.GetByNameOrID_Traits(sensitivity_B); }
            set { sensitivity_B = value.ID; }
        }
        [JsonIgnore]
        public Traits Sensitivity_M
        {
            get { return scr_System_Serializer.current.GetByNameOrID_Traits(sensitivity_M); }
            set { sensitivity_M = value.ID; }
        }
        [JsonIgnore]
        public Traits Sensitivity_C
        {
            get { return scr_System_Serializer.current.GetByNameOrID_Traits(sensitivity_C); }
            set { sensitivity_C = value.ID; }
        }
        [JsonIgnore]
        public Traits Sensitivity_V
        {
            get { return scr_System_Serializer.current.GetByNameOrID_Traits(sensitivity_V); }
            set { sensitivity_V = value.ID; }
        }
        [JsonIgnore]
        public Traits Sensitivity_A
        {
            get { return scr_System_Serializer.current.GetByNameOrID_Traits(sensitivity_A); }
            set { sensitivity_A = value.ID; }
        }


        [JsonIgnore]
        public Traits Size_B
        {
            get { return scr_System_Serializer.current.GetByNameOrID_Traits(size_B); }
            set { size_B = value.ID; }
        }
        [JsonIgnore]
        public Traits Size_P
        {
            get { return scr_System_Serializer.current.GetByNameOrID_Traits(size_P); }
            set { size_P = value.ID; }
        }
        [JsonIgnore]
        public Traits Size_V
        {
            get { return scr_System_Serializer.current.GetByNameOrID_Traits(size_V); }
            set { size_V = value.ID; }
        }
        [JsonIgnore]
        public Traits Size_A
        {
            get { return scr_System_Serializer.current.GetByNameOrID_Traits(size_A); }
            set { size_A = value.ID; }
        }
    }

}




public class Character_Trainable_Template : Character_Trainable
{

}



public class Character_Human : Character_Trainable {
    /*
     
     
     */

}

public class Character_Elf : Character_Trainable { }

public class Character_ : Character_Trainable { }

[System.Serializable]
public class Character_BaseID_Index
{

}



[System.Serializable]
public class Character_Base_Index : I_IndexMergeable, I_IndexHasID
{
    public List<Character_Trainable> baseCharacters = new List<Character_Trainable>();

    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Character_Base_Index;
        if (l == null) return;
        else if (l.baseCharacters == null) return;
        else
        {
            string s = "";
            foreach (var i in baseCharacters) s += i.BaseID + " | ";
            s += "+++";
            foreach (var i in l.baseCharacters) s += i.BaseID + " | ";
            Debug.Log("Merging with " + s);
            this.baseCharacters.AddRange(l.baseCharacters);
        }
    }

    public Character_Trainable GetTemplateFromBaseID(string ID)
    {
        //Debug.Log("Character_Base_Index : finding ID ["+ID+"] in list length [" + baseCharacters.Count + "]");
        foreach(var i in this.baseCharacters)
        {
            if (i.BaseID == ID) return i;
        }
        Debug.LogError("Cannot find id " + ID);
        return null;
    }

    public void RegisterAllID()
    {
        //Debug.Log("Character_Base_Index : registering ID with list length [" + baseCharacters.Count + "]");
        foreach (Character_Trainable o in this.baseCharacters)
        {
            scr_System_Serializer.current.RegisterIDtoLib(o.BaseID, o);
        }
    }
}
