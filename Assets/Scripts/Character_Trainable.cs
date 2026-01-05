using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

public enum Humanoid_GenderAppearance
{
    Male,
    Female,
    Ambiguous,
    Inhuman
}

public enum Character_BodyType
{
    Default,
    BHUNP,
    CBBE_3BA
}


public class Character_Trainable : ScriptableObject, I_Disposable
{
    public bool isTemporaryActor = false;


    [JsonProperty]
    protected int furnitureLockJobRef = -1;

    [JsonIgnore] public int FurnitureLockRef { get { return furnitureLockJobRef; } }

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

    /// <summary>
    /// Return true if character is locked inside a furniture
    /// </summary>
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
    [JsonIgnore]
    public bool cannotRefuse
    {
        get
        {
            if (CurrentJob != null && CurrentJob is Job_Sex_Group && (CurrentJob as Job_Sex_Group).isActorGettingRaped(this.RefID)) return true;
            return false;
        }
    }
    [JsonIgnore] public bool canMove { get { return canAct && !isRestrained && !isImprisoned; } }
    [JsonIgnore] public bool canLeave { get { return canMove && (CurrentJob == null || CurrentJob.CanBeInterrupted); } }
    [JsonIgnore] public Manageable.HourlySchedule currentHourSchedule { get {
            return GetJobPost(scr_System_Time.current.getCurrentTime().Hour);
            //return jobpost == null ? null : jobpost.getRandCOM; 
        } }


    /// <summary>
    /// If self is player, check all packages in job and find if has active <br/>
    /// For NPC, since their AI is limited to work on job only when schedules says so, use schedule and match currentjob commands;
    /// </summary>
    [JsonIgnore] public bool isWorkingOnJob { get
        {
            if (this ==  scr_System_CampaignManager.current.Player)
            {
                return CurrentJob != null && CurrentJob.GetExistingPackages(this, false, false, false).FindAll(x => x.ComTags.Contains("job")).Count > 0;
            }
            else
            {
                var allcoms = CurrentJob == null ? new List<COM>() : CurrentJob.allusableCOMs;
                var schedule = currentHourSchedule;
                var scheduleCOMs = schedule == null ? new List<COM>() : schedule.COMs;
                return scheduleCOMs.Count > 0 && allcoms.Count > 0 && Utility.ListContainsLoose(scheduleCOMs, allcoms);
            }
        } }

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
    //public List<int> inventory_ref = new List<int>();
    public CharacterInventory Inventory = new CharacterInventory();

    [JsonIgnore] public bool CanActInTimeStop { get { return this.RefID == 0; } }
    public bool MovedInTimeStop = false;
    [JsonIgnore] public bool isTimeStopped { get { return scr_System_Time.current.TimeStopStrict && !CanActInTimeStop; } }
    [JsonIgnore] public bool isTimeStoppedLoose { get { return scr_System_Time.current.TimeStop && !CanActInTimeStop; } }

    /// <summary>
    /// This field is empty in chara data, if chara data is re-deserealized then this field need to be manually copied
    /// </summary>
    [JsonProperty] protected string baseID = "";
    [JsonIgnore] public string BaseID { get { return baseID; } set { this.baseID = value; } }
    [JsonProperty] protected int referenceID = -1;
    [JsonIgnore] public int RefID { get { return referenceID; } }

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

        this.Body.Height = Template.Height;
        this.Body.Weight = Template.Weight;

        Body.AddMissing();


        this.Memory = new MemoryManager(this);
        this.Stats.InitializeWithID(this, Template.stat_STR, Template.stat_CON, Template.stat_PSY, Template.stat_WIL);

        //this.sexLogManager = new SexLogManager(refID);
        ReEstablishObservers();
        RestoreAll(true);

        this.interactionJobPointer = new Job_CharaCOM(refID);
        this.interactionJobRef = scr_System_CampaignManager.current.Register(InteractionJob);

        this.Relationships = new RelationshipManager(this);
        this.PortraitManager.RebuildInternal(this);
    }

    [JsonProperty] protected SkillManager _Skills = null;
    [JsonIgnore] public SkillManager Skills{
        get
        {
            if (_Skills == null) _Skills = new SkillManager(this); 
            return _Skills;
        }
    }

    [JsonIgnore]
    public bool canFight
    {
        get
        {
            return this.canAct && (this.Stats.HP == null || this.Stats.HP.Value > 0);
        }
    }
    [JsonIgnore] public bool canAct
    {
        get
        {
            return !this.Stats.isConsciousnessUnconscious && !isTimeStopped;
        }
    }
    [JsonIgnore]
    public bool canActInResume
    {
        get
        {
            return !this.Stats.isConsciousnessUnconscious && (scr_System_Time.current.NotTimetop || CanActInTimeStop);
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

    protected bool queuedWakeup = false;

    private void PreUpdateTime()
    {
        if (!isTimeStoppedLoose)
        {
            queuedWakeup = false;
        }
        this._cachedJobDescription = string.Empty;
        Body.ClearLastInteractedRefs();
        this.Stats.PreUpdateTimeTick();
    }

    public bool CompareStatValue(string statID, LogicalOperand operand, string value)
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
                //Debug.Log(FirstName + " Comparevalue currentClimaxCount [" + this.Stats.ConsecutiveClimaxCount + "] [" + operand + "] [" + value + "]");
                if (int.TryParse(value, out int cliamxCount))
                {
                    return Utility.CompareValue(this.Stats.ConsecutiveClimaxCount, operand, cliamxCount); ;
                }
                else
                {
                    Debug.LogError($"failed to parse currentClimaxCount target value {value}");
                    return false;
                }
            case "isUnconscious": // check chara sleeping or unconscious
                //Debug.Log(FirstName + " Comparevalue isUnconscious [" + this.Stats.isConsciousnessUnconscious + "] [" + operand + "] [" + value + "]");
                return Utility.CompareValue(this.Stats.isConsciousnessUnconscious, operand, value);
            case "isTimestopped": // check if chara can act in timestop and if currently timestopped
               // bool isTimestopped = scr_System_Time.current.timeStop && !this.CanActInTimeStop;
                //Debug.LogError(FirstName+" Comparevalue isTimestopped [" + isTimeStopped + "] [" + operand + "] [" + value + "]");
                return Utility.CompareValue(isTimeStopped, operand, value);

            case "isCumReady":  // check if chara is currently over cum threshold
               // Debug.Log(FirstName + " Comparevalue isCumReady [" + (Stats.SexStimulation.Severity >= Stats.CumThreshold) + "] [" + operand + "] [" + value + "]");
                return Utility.CompareValue(Body.isClimaxing(true), operand, value);

            case "isFatigued":  // check if chara can act but currently low on stamina
                return false;

            default:
                Debug.LogError("Unrecognized operand " + operand);
                return false;
        }
    }

    private void PostUpdateTime2()
    {
        if (!scr_System_CentralControl.current.isSafeMode) this.Body.CheckClimax(this.InteractionJob.m);
    }

    private void PostUpdateTime3()
    {
        this.Skills.FinalizeExperience();
        this._cachedJobDescription = string.Empty;
        this.PortraitManager.ClearHandlerCache();
        if (!isTimeStoppedLoose)
        {
            MovedInTimeStop = false;
            forbidGreeting = false;
        }
    }

    private void Observer_GlobalMinute5(TimeSpan t)
    {
        this.Body.UpdateTimeMinute(t);
        this.Memory.Tick(t);
    }
    private void Observer_GlobalMinute(TimeSpan t, TimeSpan t_real)
    {
        //Debug.Log($"Chara stat update globaltime {t.TotalMinutes}");
        this.Stats.UpdateTimeMinute(t, t_real);
    }
    private void Observer_GlobalHour(TimeSpan t)
    {
        //Debug.Log("Character Observer_GlobalHour for [" + FirstName + "]");
        //if (Stats.GetStatusSeverityByStringMatch("chara_status_sleeping") > 0)
        if (Stats.isConsciousnessUnconscious)
        {
            timeSinceLastSleep = 24;

            // Recover chara based on sleep efficiency ?
        }
        else if (hasStatKeyword("sleep"))
        {
            timeSinceLastSleep -= 1;
            if (timeSinceLastSleep < 0) Stats.AddOrModStatus("chara_status_sleep_deprived", 1440, 1440);
        }
        this.Body.UpdateTimeHour(t);
        timeSinceLastEat = Math.Min(24, timeSinceLastEat + 1);
        this.Relationships.HourlyRefresh();
        //Debug.Log($"{FirstName} Observer_GlobalHour: conscious? {Stats.isConsciousnessUnconscious} sleep? {hasStatKeyword("sleep")} lastSleep {timeSinceLastSleep}, lastEat {timeSinceLastEat}");
    }

    public void NotifyFoodConsume(Item_Instance i)
    {
        //Debug.LogError($"{FirstName} notify food consumption {i.DisplayName}");
        this.timeSinceLastEat = 0;
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

        List<Manageable.DailyReportHandler.MiscMessageEntry> updateMessage = new List<Manageable.DailyReportHandler.MiscMessageEntry>();
        this.Skills.UpdateAllSkills(updateMessage);
        if (updateMessage.Count > 0)
        {
            foreach (var i in FactionManager.HomeFactions)
            {
                foreach(var m in updateMessage) i.DailyReport.AddMiscRecord(m);
            }
        }
        this.Relationships.DailyRefresh();
    }

    public void NotifyFactionChange()
    {
        this.Relationships.NotifyFactionChange();
    }

    // Recovery
    public void FullRest(int recoveryStrength = -1)
    {
        var contextKey = new List<string>() { "fullrest" };
        var strMod = Stats.Strength.GetStatMod(contextKey);
        var conMod = Stats.Constitution.GetStatMod(contextKey);
        var willMod = Stats.Willpower.GetStatMod(contextKey);
        var psyMod = Stats.Psyche.GetStatMod(contextKey);

        if (Stats.Stamina != null) Stats.Stamina.ModValue(recoveryStrength > 0 ? 10 * recoveryStrength : Stats.Stamina.MaxValue);
        if (Stats.Energy != null) Stats.Energy.ModValue(recoveryStrength > 0 ? 10 * recoveryStrength : Stats.Energy.MaxValue);
        if (Stats.HP != null) Stats.HP.ModValue(recoveryStrength > 0 ? recoveryStrength : conMod);
        if (Stats.MP != null) Stats.MP.ModValue(recoveryStrength > 0 ? recoveryStrength : psyMod);

    }

    [JsonProperty] protected int timeSinceLastSleep = 24;
    [JsonProperty] protected int timeSinceLastEat = 24;
    private void Observer_GlobalDay_0(int updateOrder)
    {
        if (updateOrder != 0) return;
        this.FactionManager.FlagForDailyNeed();
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

    protected CharaSafeTemplate _templateS = null;
    protected CharaTrainableTemplate _template = null;
    [JsonIgnore] public CharaTemplate Template
    { get {
            if(_template == null && _templateS == null)
            {
                //Debug.Log("Fetching Template data |" + this.BaseID + "|" + this.FileLocation+"|");
                if (this.BaseID != "")
                {
                    if (scr_System_CentralControl.current.isSafeMode) _templateS = scr_System_Serializer.current.MasterList.CharacterTemplates.GetByCharaBaseID(BaseID) as CharaSafeTemplate;
                    else _template = scr_System_Serializer.current.MasterList.CharacterTemplates.GetByCharaBaseID(BaseID) as CharaTrainableTemplate;
                  //  Debug.Log("Fetching template for " + this.BaseID+", result exist? "+ (_template != null));
                }
                else
                {

                    if (scr_System_CentralControl.current.isSafeMode) _templateS = new CharaSafeTemplate();
                    else
                    {
                        this._template = new CharaTrainableTemplate();
                        this._template.GenderAppearance_Set(Humanoid_GenderAppearance.Female, true, true);
                    }
                    Debug.Log("Instantiating new Template for " + this.RefID);
                }
            }
            return _template == null ? _templateS : _template;
    }
        set
        {
            if (value == null)
            {
                this._template = null;
                this._templateS = null;
            }
            else if (scr_System_CentralControl.current.isSafeMode) {
                this._templateS = value as CharaSafeTemplate;
                this._template = null;
            }
            else
            {
                this._templateS = null;
                this._template = value as CharaTrainableTemplate;
            }
        }
    }

    public Character_Trainable()
    {

    }
    public Character_Trainable(bool InitializeNew)
    {
        //Stats = new StatsManager(this);
        this.birthday = UtilityEX.GetCampaignTime().AddYears(-Age);

        InitializeAllTraits();
        InitializeAllSkills();
    }

    [JsonProperty]
    protected StatsManager stats = null;

    [JsonIgnore] public StatsManager Stats { get { if (stats == null) stats = new StatsManager();
        return stats; } }

    [JsonProperty] protected PortraitManager Portrait = null;
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

    [JsonProperty] protected string firstName = "Jane", middleName = "", lastName = "Doe", title = "";
    [JsonProperty] public string nameDisplayFormat = "chara_fullname_firstToLast";
    [JsonProperty] protected string characterComment = "";
    [JsonIgnore] public string CharacterComment 
    {
        get
        {
            if (characterComment == "") return "No Comment";
            return LocalizeDictionary.QueryThenParse(characterComment);
        }
    }

    public void SetName(string firstName, string middleName, string lastName, string displayFormat){
        this.FirstName = firstName;
        this.middleName = middleName;
        this.lastName = lastName;
        this.nameDisplayFormat = displayFormat;
    }

    bool _isFirstNameCached = false;
    string _cachedFirstName = "";
    [JsonIgnore] public string FirstName {
        get
        {
            if (!_isFirstNameCached)
            {
                _isFirstNameCached = true;
                _cachedFirstName = LocalizeDictionary.QueryThenParse(firstName, firstName);
            }
            return _cachedFirstName;
        }
       set {
            _isFirstNameCached = false;
            firstName = value;
        } 
    }

    string _callName = string.Empty;
    [JsonIgnore] public string CallName { get
        {
            if (_callName == string.Empty)
            {
                _callName = Relationships.ExistRelationship(scr_System_CampaignManager.current.Player.RefID) ? FirstName : Title == "" ? FirstName : Title;
            }
            return _callName;
        } set
        {
            _callName = string.Empty;
        }
    }

    [JsonIgnore] public string MiddleName { get { return middleName == "" ? "" : LocalizeDictionary.QueryThenParse(middleName, middleName); } set { middleName = value; } }
    [JsonIgnore] public string LastName { get { return lastName == "" ? "" : LocalizeDictionary.QueryThenParse(lastName, lastName); } set { lastName = value; } }

    [JsonIgnore] public string FullNameID { get { return baseID+"_"+referenceID; } }

    string _title = string.Empty;
    [JsonIgnore] public string Title { get {
            if (title == "") return "";
            if (_title == string.Empty)
            {
                _title = LocalizeDictionary.QueryThenParse(title, title);
            }
            return _title;
        }
        set
        {
            this.title = value;
            _title = string.Empty;
        }
    }
    [JsonIgnore]
    public string Title_Raw
    {
        get
        {

            return title;
        }
    }
    string _cachedFullName = "";

    [JsonIgnore] public string FullName { get {
            if (_cachedFullName == "") _cachedFullName = LocalizeDictionary.QueryThenParse(nameDisplayFormat)
                                                            .Replace("$lastName$", LastName)
                                                            .Replace(" $middleName$", MiddleName == "" ? "" : " " + MiddleName)
                                                            .Replace("$firstName$", FirstName);
            //Debug.LogError(nameDisplayFormat);
            return _cachedFullName;
        } }

    [JsonProperty] private string origin = "charOrigin_none";
    [JsonIgnore] public Character_Origin Origin { 
        get { return scr_System_Serializer.current.MasterList.Character_Origins.GetByID(origin); } 
        set { 
            origin = value.ID;
            if (value.forceRace_ID != "") this.Race = scr_System_Serializer.current.MasterList.humanoid_Races.GetByID(value.forceRace_ID);
            if (value.forceRaceTemplate_ID != "") this.RaceTemplate = scr_System_Serializer.current.MasterList.humanoid_RaceTemplates.GetByID(value.forceRaceTemplate_ID);
            
            this.Stats.RefreshAllStats(true);
        } }

    [JsonProperty] protected string race = "humanRace_human";
    [JsonIgnore] public Humanoid_Race Race { 
        get { return scr_System_Serializer.current.MasterList.humanoid_Races.GetByID(race); } 
        set { race = value.ID;
            this.Stats.RefreshAllStats(true);
        } }

    [JsonProperty] private string raceTemplate = "humanRaceAddon_standard";
    [JsonIgnore] public Humanoid_RaceTemplate RaceTemplate { 
        get { return scr_System_Serializer.current.MasterList.humanoid_RaceTemplates.GetByID(raceTemplate); } 
        set { raceTemplate = value.ID;
            this.Stats.RefreshAllStats(true);
        } }

    [JsonProperty] private string startingGift = "charOriginGift_none";
    [JsonIgnore] public Character_Origin_startingOption StartingGift { 
        get { return scr_System_Serializer.current.MasterList.Character_Origin_StartingOptions.GetByID(startingGift); } 
        set { startingGift = value.ID;
            this.Stats.RefreshAllStats(true);
        } }

    [JsonProperty] private int currentJobRefID = -1;

    public Humanoid_GenderAppearance Appearance;

    [JsonIgnore] public bool isMale { get { return scr_System_CentralControl.current.isSafeMode ? Appearance == Humanoid_GenderAppearance.Male : scr_System_CentralControl.current.GetGender(this).Contains(InteractionGenderType.male); } }

    [JsonIgnore] public bool isFemale { get { return scr_System_CentralControl.current.isSafeMode ? Appearance == Humanoid_GenderAppearance.Female : scr_System_CentralControl.current.GetGender(this).Contains(InteractionGenderType.female); } }

    [JsonIgnore] public bool isAnimal { get { return this.Race.ID.Contains("beast"); } }
    [JsonIgnore] public bool isDead { get { return false; } }
    [JsonIgnore] public bool isCreature { get { return this.Race.ID.Contains("creature"); } }
    [JsonIgnore] public bool isHumanoid { get { return this.Race.ID.Contains("humanRace"); } }
    [JsonIgnore] public int CurrentJobRefID { get { return currentJobRefID; } }
    private Job currentJobPointer = null;
    [JsonIgnore] public Job CurrentJob { 
        get { 
            if (currentJobRefID == -1) return null;
            else if (currentJobPointer == null) currentJobPointer = scr_System_CampaignManager.current.FindJobInstanceByID(currentJobRefID);
            return currentJobPointer; }
    }


    [JsonProperty] protected List<int> activeJobRefs = new List<int>();
    public void ChangeCurrentJob(Job job = null, string targetCOMid = "", string targetCOMTag = "")
    {

        this._cachedJobDescription = string.Empty;
        if (job != null && job == this.InteractionJob)
        {

        }
        else
        {
            if (RefID == 0 && scr_System_CentralControl.current.LogPrefs.DLog_Jobs) Debug.Log("Changing " + FirstName + "'s job from " + (CurrentJob == null ? "null" : CurrentJob.DisplayName) + " to " + (job == null ? "NULL" : String.Join(",", job.allusableCOMStrings)));
            if (this.CurrentJob != null && (job == null || CurrentJob.RefID != job.RefID)) CurrentJob.RemoveActor(RefID);

            this.currentJobPointer = job;
            this.currentJobRefID = job == null ? -1 : job.RefID;
            if (job != null) job.AddActor(RefID, targetCOMid, targetCOMTag);
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

    [JsonProperty] private Character_Factions factionManager = null;
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
        this.FactionManager.SetHomeFaction(initFactionID, isManager? Manageable_GuestStatus.Manager : Manageable_GuestStatus.Member);
    }

    [JsonProperty] protected BodyEquipLayer lastKnownLayer = BodyEquipLayer.Inner;
    public void NotifyConsciousClothingChange(BodyEquipLayer layer)
    {
        this.lastKnownLayer = layer;
    }

    [JsonIgnore]
    public bool DisplayCharaEvent
    {
        get
        {
            foreach(var m in this.FactionManager.Factions)
            {
                if (m.isPlayerFaction) return true;
            }
            return false;
        }
    }

    public void AddTrait(string s) { traits.Add(s); }
    public void AddTrait(Traits s) { traits.Add(s.ID); }
    public void ResetTrait() { traits = new List<string>(); }
    public bool HasTrait(string s) { return traits.Contains(s); }
    public bool HasTrait(Traits t) { return traits.Contains(t.ID); }

    [JsonIgnore] public int Age { get { return 22; } }
    [JsonProperty] private bool noAging = false;

    [JsonProperty] private DateTime birthday;
    [JsonIgnore] public DateTime Birthday { get { return birthday; } set { birthday = value; } }

    public void TryGetJob(int currentHour, List<string> s)
    {
        I_IsJobGiver currentJobFaction = FactionManager.CurrentActiveParty != null ? FactionManager.CurrentActiveParty : FactionManager.CurrentlyActiveFaction;
        I_IsJobGiver currentLocaleFaction = FactionManager.CurrentActiveParty != null ? FactionManager.CurrentActiveParty : FactionManager.CurrentLocaleFaction;

        bool resetJob = false;

        if (RefID == 0)
        {
            if (CurrentJob != null && CurrentJob.hasActorCompletedJob(0)) ChangeCurrentJob(null);
            return;
        }
        bool log = s != null;
        bool debugLog = isRestrained;

        string ss = FirstName + ": ";
        string jobInternalStatus;
        if (interrupted)
        {
            if (log) ss += $" | interrupted, delaying job search |";
            interrupted = false;
            if (s != null) s.Add(ss);
            return;
        }
        if (CurrentJob != null)
        {   // has job, but job cannot give a valid package
            bool hasPackage = CurrentJob.UpdateActorPackage(this, out jobInternalStatus);
            if (log) ss += jobInternalStatus;
            if (!hasPackage)
            {
                if (log) ss += $" || Job cannot give valid package, releasing from |{CurrentJob.RefID}|";
                resetJob = true;
            }
            // release from job
        }

        if (scr_System_CampaignManager.current.PlayerPartyMembers.Contains(RefID))
        {
            if (log) ss += "is following player";
            if(s != null) s.Add(ss);
            return;
        }
        if (CurrentJob != null && !resetJob)
        {
            if (log) ss += "already have a job " + CurrentJob.RefID + " with " + (CurrentJob.allusableCOMStrings.Count > 5 ? CurrentJob.allusableCOMStrings.Count + " coms" : " coms[" + String.Join(", ", CurrentJob.allusableCOMStrings) + "]") + " in room " + scr_System_CampaignManager.current.GetCharaRoomInstance(RefID).DisplayName + " descriptions: " + GetJobDescription();
            if (!CurrentJob.CanBeInterrupted ||
                CurrentJob.actorRefID.Contains(scr_System_CampaignManager.current.Player.RefID) ||
                CurrentJob.isPlayerRelatedJob)
            {

                if (s != null) s.Add(ss);
                return;
            } 
        }
        if (Climaxing)
        {
            if (log) ss += "is climaxing";
            if(s != null) s.Add(ss);
            return;
        }
        if (!canAct)
        {
            if (isTimeStopped)
            {
                if (log) ss += "is in timestop";
                if(s != null) s.Add(ss);
                return;
            }
            else if (Stats.isConsciousnessUnconscious)
            {   // unconscious return
                if (Stats.HasStatusByStringMatch("chara_status_sleeping"))
                {
                    if (log) ss += "is sleeping";
                }
                else
                {
                    if (log) ss += "is unconscious";
                }
                if(s != null) s.Add(ss);
                return;
            }
            else
            {
                if (log) ss += "chara cannot act for undefined reason";
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
            if (log) ss += this.InteractionJob.GetJobDescription(this.RefID);
            if(s != null) s.Add(ss);
            return;
        }

        List<string> factionstring = new List<string>();
        foreach (var faction in FactionManager.Factions) factionstring.Add(faction.ID + (faction.isCharaManager(this) ? "*" : ""));
        //if (chara.CurrentJobRefID)

        var jobpost = GetJobPost(currentHour);
        COM currentScheduleCOM = jobpost == null ? null : jobpost.getRandCOM;
      

        /// if previous almost over (time less than half and time less than 15min)
        /// if still in pathing
        bool tryFinishJob = false;
        if (CurrentJob != null && !resetJob)
        {   // if current job is not pathing and less than 15 min then keep doing it
            List<ActionPackage> p = CurrentJob.ActivePackages.FindAll(x => x.actorRefs.Contains(RefID));

            bool allInterrupted = p.Count > 0;
            int maxWait = 5;
            int maxInterruptWait = 0;
            foreach (var pp in p)
            {
                allInterrupted = allInterrupted && pp.isPaused;
                maxInterruptWait = Math.Max(maxInterruptWait, pp.pausedTick);
            }

            tryFinishJob = p.Count > 0 && (!allInterrupted || maxInterruptWait <= maxWait);
            foreach (var package in p)
            {
                //if (package is ActionPackage_PathTo || (package.Duration > 10 && package.targetCOM.comTags.Contains("recreation"))) tryFinishJobUrgent = false;
                if (package is ActionPackage_PathTo) tryFinishJob = false;
            }
            if (tryFinishJob && allInterrupted && maxInterruptWait <= maxWait)
            {
                if (log) ss += "| waiting for interrupt to end |";
                if (s != null) s.Add(ss);
                return;
            }
            if (allInterrupted && maxInterruptWait > maxWait)
            {
                if (log) ss += "| aborting current job due to interrupt |";
                resetJob = true;
            }
            else if (tryFinishJob) 
            {
                if (s != null) s.Add(ss);
                return;
            }
        }

        /*
        if (resetJob)
        {
            this.ChangeCurrentJob(null);
            resetJob = false;
            if (log) s.Add(", RESET called");
        }*/

        if (isTemporaryActor)
        {
            if (s != null) s.Add("isTemporaryActor, breaking");
            return;
        }

        // Redress check
        if (shouldRedress)
        {
            //Debug.LogError(FirstName + " should redress");
            if (CurrentJob != null && !resetJob && (CurrentJob.hasActivePackge(RefID, "com_furniture_restroom_fix") || (CurrentJob.allusableCOMs.Find(x => x.ID == "com_furniture_restroom_fix") != null && CurrentJob.hasActivePathing(RefID))))
            {   // if current is of same type as schedule, dont do anything. 
                //Debug.LogError(FirstName + " should redress, current job has related COM");

                if (s != null) s.Add(ss);
                return;
            }
            else if (currentLocaleFaction != null)
            {   // get closest schedule job from current location
                List<Job_Furniture> possibleJobs = currentLocaleFaction.GetValidJobsByCOMID(this, "com_furniture_restroom_fix", s);
                if (possibleJobs != null && possibleJobs.Count > 0)
                {
                    Job job = possibleJobs[0];
                    if (log) ss += "Changing job to " + (job == null ? "NULL" : String.Join(",", job.allusableCOMStrings) + $"|{(job == null ? "null" : job.RefID)}| in room [" + job.ParentRoom.DisplayName + "]");
                    ChangeCurrentJob(job, "com_furniture_restroom_fix");

                    if (s != null) s.Add(ss);
                    return;
                }
                //}
            }
        }

        // can sleep, should sleep, hasSleepPlan
        if (shouldSleep)
        {   // if current schedule is sleep then go to sleep

            if (CurrentJob != null && !resetJob && CurrentJob.allusableCOMs.Find(x => x.comTags.Contains("sleep")) != null)
            {   // if already using a furniture that allows sleep, then its a valid one, return

                if (s != null) s.Add(ss);
                return;
            }
            else if (currentJobFaction != null)
            {
                // chara should go back home to sleep
                List<Job_Furniture> possibleJobs = currentJobFaction.GetValidJobs_Sleep(this, currentHour, s);
                if (possibleJobs != null && possibleJobs.Count > 0)
                {
                    Job job = possibleJobs[0];
                    if (log) ss += "Changing job to sleep " + (job == null ? "NULL" : String.Join(",", job.allusableCOMStrings) + $"|{(job == null ? "null" : job.RefID)}| in room [" + job.ParentRoom.DisplayName + "]");
                    ChangeCurrentJob(job, "com_furniture_sleep");

                    if (s != null) s.Add(ss);
                    return;
                }
            }
        }

        // temporarily disable eat cuz need to restrict food hours in faction management
        if (canEat)
        {   // try get food
            if (CurrentJob != null && !resetJob && CurrentJob.allusableCOMs.Find(x => x.comTags.Contains("food_meal")) != null)
            {   // if already eating (dont care if it's pathing or executing)

                if (s != null) s.Add(ss);
                return;
            }
            else if (currentLocaleFaction != null)
            {
                // allow checking during work hour

                List<Job_Furniture> possibleJobs = currentLocaleFaction.GetValidJobs_Meal(this, currentHour, s);
                if (possibleJobs != null && possibleJobs.Count > 0)
                {
                    Job job = possibleJobs[0];
                    if (log) ss += "Changing job to eating " + (job == null ? "NULL" : String.Join(",", job.allusableCOMStrings) + $"|{(job == null ? "null" : job.RefID)}| in room [" + job.ParentRoom.DisplayName + "]");
                    ChangeCurrentJob(job, "", "food_meal");

                    if (s != null) s.Add(ss);
                    return;
                }
            }

            //}
        }

        if (currentJobFaction is Manageable_Party)
        {
            var party = currentJobFaction as Manageable_Party;
            if (party == null)
            {

            }
            else if ((FactionManager.isPartyLocked || party.isActive) && !party.Job.isResting && !party.skipTryGetJob(this))
            {
                
                if (FactionManager.isPartyLocked && !party.hasExpeditionSet)
                {
                    if (log) ss += $"party locked {party.FactionDisplayName} !hasExpeditionSet {(party.Job == null ? "-" : "exist")} {(party.Job == null || party.Job.Expedition == null ? "-" : "exist")}";
                    if (s != null) s.Add(ss);
                    return;
                }
                else if (this.CurrentJob == party.Job && party.Job.canReturn && party.Job.canExit(this.RefID))
                {
                    this.FactionManager.RemoveFromParty(party);
                    ChangeCurrentJob();
                    if (log) ss += "Exiting party exploration job " + party.FactionDisplayName + "" + party.Job.DisplayName;
                    if (s != null) s.Add(ss);
                    return;
                }
                else if (party.Job != null && this.CurrentJob != party.Job && !party.Job.ShouldRest(this))
                {
                    ChangeCurrentJob(party.Job);
                    if (log) ss += "Changing job to party exploration job " + party.FactionDisplayName + "" + party.Job.DisplayName;
                    if (s != null) s.Add(ss);
                    return;
                }
                else if (party.Job.hasActivePackge(this.RefID))
                {
                    // be careful actorjobcomplete list, but here not necessary as camp ignore the list
                    if (log) ss += "working on party exploration job " + party.FactionDisplayName + "" + party.Job.DisplayName;
                    if (s != null) s.Add(ss);
                    return;
                }
                else if (party.Job.ShouldRest(this))
                {
                    if (log) ss += "exploration shouldRest? TRUE ||";
                    if (s != null) s.Add(ss);
                }
                else
                {
                    // be careful actorjobcomplete list, but here not necessary as camp ignore the list
                    if (log) ss += $"working on party exploration job, inCooldown? {party.Job.HasCooldown()} or returning? {party.Job.status == Job_Expedition.ExpeditionStatus.returning}, faction {party.FactionDisplayName} {party.Job.DisplayName}";
                    if (s != null) s.Add(ss);
                    return;
                }
            }
            else if (FactionManager.isPartyLocked)
            {
                Debug.LogError($"Error party locked and hasExpeditionSet[{party.hasExpeditionSet}] !isResting[{!party.Job.isResting}] !skipTryGetJob[{!party.skipTryGetJob(this)}]");
            }
        }


        if (currentScheduleCOM != null && currentScheduleCOM.ID != "com_furniture_sleep")
        {   // if current schedule has available job (exclude sleep)

            // first get command by ID, if command 
            // first check if chara is already doing related job == currentjob exist
            if (CurrentJob != null && !resetJob && (CurrentJob.hasActivePackge(RefID, currentScheduleCOM.ID) || (CurrentJob.allusableCOMs.Contains(currentScheduleCOM) && CurrentJob.hasActivePathing(RefID))))
            {   // if current is of same type as schedule, dont do anything. 

                if (s != null) s.Add(ss);
                return;
            }
            else if (currentJobFaction != null)
            {   // current job is null, or current job is not schedule

                // at this point we know the previous job can be break
                //foreach (Manageable faction in FactionManager.Factions)
                //{   // get closest schedule job
                List<Job_Furniture> possibleJobs = currentJobFaction.GetValidJobs_Jobs(this, currentHour, ref ss, true);
                if (possibleJobs != null && possibleJobs.Count > 0)
                {
                    Job job = possibleJobs[0];
                    var targetID = ((job == null || currentScheduleCOM == null) ? "" : currentScheduleCOM.ID);
                    if (log) ss += "Changing job to faction "+ currentJobFaction.FactionDisplayName+"" + (job == null ? "NULL" : String.Join(",", job.allusableCOMStrings) + $"|{(job == null ? "null" : job.RefID)}| in room [" + job.ParentRoom.DisplayName + "]");
                    ChangeCurrentJob(job, targetID);

                    if (s != null) s.Add(ss);
                    return;
                }
               // }
            }
        }

        if (shouldRest && TryFindNonJobByTag(resetJob, "rest", currentLocaleFaction, currentHour, ref ss, log, s, new NonJobSearchWrapper(false, true, true)))
        {
            if (s != null) s.Add(ss);
            return;
        }

        if ((isAnimal || isCreature) && !scr_System_CentralControl.current.isSafeMode && isRestrained)
        {        // try find interaction job (rape job)
            if (CurrentJob != null && !resetJob)
            {
                //Debug.LogError("Animal find job, current job is not null");
                if (CurrentJob.allusableCOMs.Find(x => x.comTags.Contains("sex")) != null)
                {
                    if (log) ss += "|already in sex job|";
                    if (s != null) s.Add(ss);
                    return;
                }
                else if (CurrentJob.allusableCOMs.Find(x => x.comTags.Contains("initSex")) != null)
                {
                    if (log) ss += "|trying to initiate sex|";
                    if (s != null) s.Add(ss);
                    return;
                }
            }
            else if (Stats.Energy.ValuePercentile < 0.9 || Stats.Stamina.ValuePercentile < 0.9)
            {

            }
            else if (currentJobFaction != null)
            {
                //Debug.LogError("Animal looking for new target");
                List<Job_CharaCOM> possibletargets = new List<Job_CharaCOM>();

                //foreach (Manageable faction in FactionManager.HomeFactions)
                possibletargets.AddRange(currentJobFaction.GetValidCharaCOMByTag(this, "initSex", ref ss));

                if (possibletargets.Count > 0)
                {
                    Job_CharaCOM interactionJob = Utility.GetRandomElement(possibletargets);
                    var existingJob = interactionJob == null ? null : interactionJob.Owner.CurrentJob;
                    if (existingJob != null && existingJob is Job_Sex_Group)
                    {
                        var existingSex = existingJob as Job_Sex_Group;
                        ChangeCurrentJob(existingSex);
                        if (log) ss += $"|joining existing Sexjob on {interactionJob.Owner.CallName}|";
                        if (s != null) s.Add(ss);
                        return;
                    }
                    else
                    {

                        ChangeCurrentJob(interactionJob, "com_interaction_initiateSex");
                        if (log) ss += $"|trying to initiate sex on {interactionJob.Owner.CallName} in room {interactionJob.ParentRoom.DisplayName}";
                        if (s != null) s.Add(ss);
                        return;
                    }

                }
            }

            
        }
        
        if (Jail != null && Jail.ownerJob != null)
        {
            ChangeCurrentJob(Jail.ownerJob, "", "rest");
            return;
        }
        else if(TryFindNonJobByTag(resetJob, "recreation", currentJobFaction, currentHour, ref ss, log, s, new NonJobSearchWrapper(true, false, true)))
        {
            if (s != null) s.Add(ss);
            return;
        }
        // if still no job, set to look for recreation
        // need to find : is there location restriction ? search currently at ?
        // include search : character currently at + home faction
            
        else if (TryFindNonJobByTag(resetJob, "rest", currentLocaleFaction, currentHour, ref ss, debugLog, s, new NonJobSearchWrapper(false, true, false)))
        {
            if (s != null) s.Add(ss);
            return;
        }
        
    }

    public class NonJobSearchWrapper
    {
        public bool skipPrivate, shortestPathOnly, checkBlacklist;
        public NonJobSearchWrapper(bool skipPrivate, bool shortestPathOnly, bool checkBlacklist) 
        {
            this.skipPrivate = skipPrivate;
            this.shortestPathOnly = shortestPathOnly;
            this.checkBlacklist = checkBlacklist;
        }

        public void Search(ref List<Job_Furniture> list, I_IsJobGiver faction, Character_Trainable c, int hour, string tag, List<string> s)
        {
            list.Clear();
            list.AddRange(faction.GetValidJobs_nonJob_byTags(c, hour, tag, s, this.skipPrivate, this.shortestPathOnly, this.checkBlacklist));
        }
    }

    protected bool TryFindNonJobByTag(bool resetJob, string tag, I_IsJobGiver currentJobFaction, int currentHour, ref string ss, bool log, List<string> s, NonJobSearchWrapper search)
    {
        if (CurrentJob != null && !resetJob && CurrentJob.allusableCOMs.Find(x => x.comTags.Contains(tag)) != null)
        {
            return true;
        }
        else if (currentJobFaction != null)
        {
            List<Job_Furniture> possibleRecreations = new List<Job_Furniture>();

            //foreach (Manageable faction in FactionManager.HomeFactions)
            //{
            search.Search(ref possibleRecreations, currentJobFaction, this, currentHour, tag, s);
            //possibleRecreations.AddRange(currentJobFaction.GetValidJobs_nonJob_byTags(this, currentHour, tag, s, true, false, true));
            //    break;
            //}
            if (possibleRecreations.Count < 1) return false;

            Job job = Utility.GetRandomElement(possibleRecreations);
            if (log) ss += $"Changing job to tag [{tag}] " + (job == null ? "NULL" : String.Join(",", job.allusableCOMStrings) + $"|{(job == null ? "null" : job.RefID)}| in room [" + job.ParentRoom.DisplayName + "]");

            ChangeCurrentJob(job, "", tag);
            if (CurrentJob != job) Debug.LogError($"Error in changing job from {(CurrentJob == null ? "null" : CurrentJob.RefID)} to {(job == null ? "null" : job.RefID)}");

            return true;

        }
        return false;
    }

    [JsonIgnore] public bool canEat { get {
            return this.hasStatKeyword("hunger") && this.Stats.GetStatValue("stats_derived_foodConsumption") >= 1 && timeSinceLastEat > 3; } }
    [JsonIgnore] public bool canSleep { get {
            if (!hasSleepNeed) return false;
            if (this.Stats.GetStatusSeverityByStringMatch("chara_status_sleep_deprived") > 0) return true;
            if (this.Stats.Fatigue != null && this.Stats.Fatigue.Severity > 0.9) return true;
            return timeSinceLastSleep < (24 - 8);  } }
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

    [JsonIgnore] public bool shouldSleep { get
        {
            if (!hasSleepNeed) return false;
            if (this.FactionManager.HasSleepSchedule) 
            {
                var v = GetJobPost();
                return v != null && v.comIDs.Contains("com_furniture_sleep") && canSleep;
            }
            else
            {
                return canSleep;
            }
        } }

    [JsonIgnore] public bool shouldRest { get
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
            return this.Inventory.Contents.Any(x=>x.GetComp_Equippable() != null);
        } }
    [JsonIgnore] public bool shouldRedress { 
        get {
            return canRedress && !FactionManager.isPartyLocked;
            if (!canRedress) return false;
            foreach(var i in this.Inventory.Contents)
            {
                var comp = i.GetComp_Equippable();
                if (comp != null && comp.equipLayer <= lastKnownLayer) return true;
            }
            return false;
        } 
    }

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

    [JsonProperty] private int interactionJobRef = -1;
    private Job_CharaCOM interactionJobPointer = null;
    [JsonIgnore] public Job_CharaCOM InteractionJob { get { if (interactionJobPointer == null) interactionJobPointer = scr_System_CampaignManager.current.FindJobInstanceByID(interactionJobRef) as Job_CharaCOM;
            return interactionJobPointer;
        } }

    [JsonIgnore] public bool interrupted = false;

    /// <summary>
    /// Wipe cached description
    /// </summary>
    public void NotifyJobStateChange()
    {
        this._cachedJobDescription = string.Empty;
    }

    protected string _cachedJobDescription = string.Empty;
    public string GetJobDescription()
    {
        if(_cachedJobDescription == string.Empty)
        {
            if (scr_System_CentralControl.current.LogPrefs.DLog_Jobs) Debug.Log($"{FirstName} updating job description\n{this.isSleeping} {this.Stats.isConsciousnessUnconscious} {this.InteractionJob != null && this.InteractionJob.isActive} {this.CurrentJob != null}");
            if (this.isSleeping) _cachedJobDescription = LocalizeDictionary.QueryThenParse("chara_currentjob_sleeping");
            else if (this.Stats.isConsciousnessUnconscious) _cachedJobDescription = LocalizeDictionary.QueryThenParse("chara_currentjob_unconscious");
            else if (this.InteractionJob != null && this.InteractionJob.isActive) _cachedJobDescription = this.InteractionJob.GetJobDescription(RefID);
            else if (this.CurrentJob != null) _cachedJobDescription = this.CurrentJob.GetJobDescription(RefID);
            else _cachedJobDescription = LocalizeDictionary.QueryThenParse("chara_currentjob_none"); ;
        }
        //if (scr_System_CentralControl.current.LogPrefs.DLog_Jobs) Debug.Log($"{FirstName} getjobdescription {_cachedJobDescription}");
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

    public bool EquipItem(int itemRefID, bool forceEquip = false)
    {
        Item_Instance item = scr_System_CampaignManager.current.FindItemInstanceByID(itemRefID);

        if (item != null)
        {
            ItemComponent_Equippable comp = item.GetComp("ItemComponent_Equippable") as ItemComponent_Equippable;
            if (comp != null)
            {
                //Stats.RefreshAllStats(true);
                if ( Body.EquipItem(itemRefID, comp.equipCount, forceEquip))
                {
                    Skills.RefreshAvailableSkillChecks();
                    if (comp.statModifiers.Count > 0) this.Stats.RefreshAllStats(true);
                    this.PortraitManager.ClearHandlerCache();
                    return true;
                }
                else
                {
                    return false;
                }
            }
            else
            {
                Debug.LogError($"Equipitem {item.DisplayName} failed on {FirstName}, target item does not have equip comp");
            }
        }
        else
        {
            Debug.LogError($"Equipitem {itemRefID} failed on {FirstName}, target item cannot be found");
        }
        return false;
    }

    


    public void TimestopStart()
    {
        if (this.isTemporaryActor) return;
        // Memorize chara current status if applicable

        if (!CanActInTimeStop)
        {
            this.Memory.TimestopStart();
            // timestop start kojo
            //Debug.Log($"{FirstName} cannot act in timestop");
            var rel = Relationships.FindRelationshipWith(scr_System_CampaignManager.current.Player);
            var m = Relationships.Personality.GetKOJOMessage("OnTimestopStart", rel, new List<string>(), new List<string>());

            if (m != null)
            {
                this.InteractionJob.m.AddKojo(m);
                //Debug.Log($"timestop start {m.message}");
            }

            this.InteractionJob.NotifyDescriptionsOutOfUpdate();
        }
    }

    public bool forbidGreeting = false;

    public void TimestopEnd()
    {
        if (this.isTemporaryActor) return;

        var mem = Memory.timestopMemory;
        if (mem != null)
        {
            List<string> tags = new List<string>();
            tags.Add("timestop");
            bool skip = false;

            // Check Climax
            if (!Body.isClimaxing())
            {
                Body.CheckClimax(this.InteractionJob.m);
                if (Body.isClimaxing())
                {
                    tags.Add("climax");
                    // skip others check
                }
            }
            else
            {
                tags.Add("climaxing");
            }
            
            // Check restraint
            if (this.FurnitureLockRef != mem.furnitureLock) tags.Add(this.FurnitureLockRef == -1 ? "unlocked" : "locked");
            
            // check location
            var currentRoomRef = scr_System_CampaignManager.current.GetCharaRoomInstance(this.RefID).RefID;
            if (currentRoomRef != mem.lastLocationRef) tags.Add("location_change");

            var pleasure = Stats.SexStimulation;
            var pain = Stats.FindStatusByExactID("chara_status_pain");
            var pleasureSeverity = pleasure == null ? 0 : (int)pleasure.Severity;
            var painSeverity = pain == null ? 0 : (int)pain.Severity;

            if (pleasureSeverity > mem.pleasureSeverity) tags.Add("pleasure");
            if (painSeverity > mem.painSeverity) tags.Add("pain");

            bool checkClothes = false;
            if (!Utility.ListEquals(mem.lastEquipRefs, EquippedItemRefs))
            {
                tags.Add("undressed");
                checkClothes = true;
            }
            
            foreach (var part in this.Body.Internals)
            {
                if (checkClothes && part.Base.exposedKojoID != "")
                {
                    var score = part.Parent.GetRevealingScore(BodyEquipLayer.None);
                    if (score < 1 && score < mem.exposedBodyRefs[part.baseID])
                    {
                        tags.Add(part.Base.exposedKojoID);
                        if (!tags.Contains("nudity")) tags.Add("nudity");
                    }
                }
                if (part.canContain)
                {
                    if ((int)part.CurrentlyContained > mem.container[part.baseID]) 
                    {
                        if (part.ContainsCum && !tags.Contains("cum")) tags.Add("cum");
                        if (part.isExtremelyExpanded)
                        {
                            tags.Add($"{part.baseID}_Expansion_Extreme");
                            tags.Add("Expansion_Extreme");
                            tags.Add("Expansion");
                        }
                        else if (part.isVisiblyExpanded)
                        {
                            tags.Add($"{part.baseID}_Expansion");
                            tags.Add("Expansion");
                        }
                    }
                }
            }
            tags = Utility.Distinct(tags);


            if (tags.Count > 1)
            {
                if (!Body.isClimaxing() && Stats.isConsciousnessUnconscious) tags.Add("sleeping_noclimax");
                forbidGreeting = true;
            }

            if (tags.Count > 1) Debug.Log($"Timestop End, {FirstName} tags [{String.Join("|", tags)}]");

            var rel = Relationships.FindRelationshipWith(scr_System_CampaignManager.current.Player);
            var m = Relationships.Personality.GetKOJOMessage("OnTimestopEnd", rel, tags, new List<string>());


            if (m != null) this.InteractionJob.m.AddKojo(m);

            this.InteractionJob.NotifyDescriptionsOutOfUpdate();

        }
        Memory.TimestopEnd();
        /*
         * first, change timestop toggle to resuming
         * 
         * then, call timestopend to all chara (do everything here)
         * - check climax (if resuming then call the resuming kojo)
         * then, change timestop toggle to normal
         */
    }


    /// <summary>
    /// If not immediate, then queue event and over <br/>
    /// if immediate, then actually execute
    /// </summary>
    public void WakeUp(bool immediate)
    {
        if (!immediate) 
        {
            if (!queuedWakeup)
            {
                queuedWakeup = true;
                scr_UpdateHandler.current.EventHandler.StartEvent(this, "QueuedWakeupEvent", "", RefID == 0);
            }
        }
        else
        {
            //Debug.LogError("Wakeup immediate!");
            // IF SLEEP DEPRIVED, IT IS ALREADY ADDED PRIOR TO THIS POINT (ON CALLING WakeupPrep)        
            this.Stats.RemoveStatusByStringMatch("chara_status_sleeping");
            // Debug.Log($"Chara wake up at conscious {this.Stats.Consciousness.Severity}");
            List<Character_Relationship> accepted = new List<Character_Relationship>();
            List<Character_Relationship> refused = new List<Character_Relationship>();

            if (!this.Stats.isConsciousnessUnconscious)
            {

                var memInst = new MemInstance(new List<int>(), new List<string>(), "", -1, -1, true, Memory_Response.Accept, Memory_Attitude.None, LocalizeDictionary.QueryThenParse("ui_entry_memory_sleep_end"));
                var memEntry = this.Memory.AddEntry(memInst, new List<string>() { "forbidMerge" });

                memEntry.entryDescription = memInst.description;
                // re-check every AP
                UtilityEX.GetAPsFrom(this, out List<ActionPackage> aps);

                var wakeupEV = new EventInstance(this, "OnCharaWakeUp", "");
                var callbacks = new List<Action>();
                var appends = new List<Action>();
                wakeupEV.FunctionCalls.Add("jobCallback", callbacks);

                if (this.InteractionJob.isVisibleToPlayer) wakeupEV.FunctionCalls.Add("onWakeUp", appends);
                //scr_UpdateHandler.current.EventHandler.StartEvent(this, "OnCharaWakeUp", "", false);
                scr_UpdateHandler.current.EventHandler.StartEvent(wakeupEV, false);

                List<Job> jobLists_refuse = new List<Job>();
                List<Job> jobLists_accept = new List<Job>();

                foreach (var ap in aps)
                {
                    var result = ap.retryRequest(this, "justWokenUp");

                    if (!result)
                    {
                        if (!jobLists_refuse.Contains(ap.job)) jobLists_refuse.Add(ap.job);
                        //Debug.LogError($"Wakeup revalidating ap {ap.targetCOM.displayName} on {this.FirstName}, isDoer {ap.doer.Contains(this)} isReceiver {ap.receiver.Contains(this)}, result {result}");
                        
                        ap.ExecutePackageOutsideUpdate();   // execution
                        if (ap.job.isVisibleToPlayer) ap.job.CollectLogs(ap);

                        ap.DisablePackage();
                        scr_System_CampaignManager.current.Unregister(ap);
                        if (ap.job is Job_Sex_Group)
                        {
                            foreach(var ep in ap.ListEP)
                            {
                                var rel = ep.Relationship(this);
                                if (rel == null) continue;
                                if (refused.Contains(rel)) continue;
                                if (accepted.Contains(rel)) continue;

                                refused.Add(rel);
                            }
                        }
                        ap.job.CurrentPackages.Remove(ap);
                    }
                    else
                    {
                        if (!jobLists_accept.Contains(ap.job)) jobLists_accept.Add(ap.job);
                        // Debug.Log($"Wakeup revalidating ap {ap.targetCOM.displayName} on {this.FirstName}, isDoer {ap.doer.Contains(this)} isReceiver {ap.receiver.Contains(this)}, result {result}");
                        if (ap.job is Job_Sex_Group)
                        {
                            foreach (var ep in ap.ListEP)
                            {
                                var rel = ep.Relationship(this);
                                if (rel == null) continue;
                                if (accepted.Contains(rel)) continue;

                                accepted.Add(rel);
                                refused.Remove(rel);
                            }
                        }
                        if (ap.job.isVisibleToPlayer) ap.job.CollectLogs(ap);
                    }
                }

                foreach (var job in jobLists_accept)
                {
                    callbacks.Add(job.NotifyDescriptionsOutOfUpdate);

                    if (jobLists_refuse.Contains(job)) jobLists_refuse.Remove(job);
                }

                foreach(var job in jobLists_refuse)
                {
                    if (job is Job_Sex_Group)
                    {
                        var message = LocalizeDictionary.QueryThenParse("event_onNightAssaultFailed_jobD").Replace("$chara$", this.FirstName);
                        UtilityEX.StringReplace(ref message);
                        (job as Job_Sex_Group).FlagActorLeave(this.RefID, message);
                    }
                    callbacks.Add(job.NotifyDescriptionsOutOfUpdate);
                }

                var selfTags = new List<string>();
                foreach (var item in this.Inventory.ContentsPrintable)
                {
                    if (!item.Equippable) continue;
                    if (item.GetComp_Equippable().equipLayer != BodyEquipLayer.Skin) continue;
                    // detected item 
                    selfTags.Add("removedClothing");
                    break;
                }
                bool foundCum = false;
                foreach (var organ in this.Body.Internals)
                {
                    if (foundCum) break;
                    foreach (var content in organ.Contains)
                    {
                        if (foundCum) break;
                        if (content is Item_Instance_Cum)
                        {
                            selfTags.Add("cum");
                            foundCum = true;
                        }
                    }
                }

                if (accepted.Count >= 1)
                {
                    var randRel = Utility.GetRandomElement(accepted);
                    var message = this.Relationships.Personality.GetKOJOMessage("OnNightAssaultSuccess", randRel, selfTags, new List<string>());
                    Debug.Log($"({FirstName}) detected night assault accept count {accepted.Count}, random select {randRel.TargetName}, message {message}");
                    appends.Add(()=>scr_System_CampaignManager.current.AddLog( message));
                }
                else if (refused.Count >= 1)
                {
                    var randRel = Utility.GetRandomElement(refused);
                    var message = this.Relationships.Personality.GetKOJOMessage("OnNightAssaultFailure", randRel, selfTags, new List<string>());
                    Debug.Log($"({FirstName}) detected night assault accept count {accepted.Count}, random select {randRel.TargetName}, message {message}");
                    appends.Add(() => scr_System_CampaignManager.current.AddLog(message));
                    // add moodlet, reduce relationship -> mod relationship record are from ep. or can we directly inject into updatehandler ?
                    // directly add explog to updatehandler's log
                    var relationship = randRel.Owner.Relationships;
                    var logger = scr_System_CampaignManager.current.isCharaVisibleToPlayer(this.RefID) ? scr_UpdateHandler.current.Message.exp : null;
                    relationship.IncreaseRelationshipWith(randRel.TargetID, RelationshipScoreType.Trust, -30, logger);
                    relationship.IncreaseRelationshipWith(randRel.TargetID, RelationshipScoreType.Fear, 30, logger);


                    memInst.ResetInternal(Memory_Response.Refuse, Memory_Attitude.Hate);
                    memInst.targets = new List<int>() { randRel.TargetID };
                    memInst.description = LocalizeDictionary.QueryThenParse("memory_onNightAssaultFailed").Replace("$target$", randRel.Target.FirstName);
                    memInst.AddMoodletScore(-2, -4, 0);

                    memEntry.entryDescription = memInst.description;
                    memEntry.Duration = Stats.MemoryLength * 10;
                    memEntry.ReEstablishParent(this);

                    var memInst_2 = new MemInstance(new List<int>(), new List<string>(), "", -1, -1, true, Memory_Response.None, Memory_Attitude.None, LocalizeDictionary.QueryThenParse("memory_onNightAssaultCaught").Replace("$target$", randRel.Owner.FirstName));
                    var memEntry_2 = randRel.Target.Memory.AddEntry(memInst_2, new List<string>() { "forbidMerge" });
                    memEntry_2.entryDescription = memInst_2.description;
                }
                else
                {
                    // first check last memories if any conscious sex then let it go
                    // NO

                    var rel = this.Relationships.FindRelationshipWith(this);
                    var message = this.Relationships.Personality.GetKOJOMessage("OnWakeUp", rel, selfTags, new List<string>());
                    //Debug.Log($"({FirstName}) no night assault, message {message}");
                    // add moodlet
                    if (selfTags.Count > 0)
                    {
                        
                        memInst.ResetInternal(Memory_Response.Refuse, Memory_Attitude.Hate);
                        memInst.description = LocalizeDictionary.QueryThenParse("memory_onNightAssaultDiscover");
                        memInst.AddMoodletScore(selfTags.Count, (-selfTags.Count - 1) * 2, 0);

                        memEntry.entryDescription = memInst.description;
                        memEntry.Duration = Stats.MemoryLength * 10;
                        memEntry.ReEstablishParent(this);
                    }
                    else
                    {
                        memInst.ResetInternal(Memory_Response.Accept, Memory_Attitude.Neutral);
                    }
#if UNITY_EDITOR
                    Debug.Log($"{FirstName} wakes up naturally, trying to breaking from lingering job");
#endif
                    this.ChangeCurrentJob(null);
                }

                // if exit job, then removeactor already called endongoingmemory
                // so, if last memory is ended and uncons, then, problem!

                this.Stats.RefreshAllStats();


                /*
                 1. check underwear removal
                 2. check cum in mouth and vagina
                 3. check body stimulation level
                 */

                // if one job got all its ap refused, try leaving job
                // regardless of accept or refuse, job will have collected new kojo entries, need to manually log them outside of update loop.


                // end existing memory entry
                // check self status
            }

           // scr_UpdateHandler.current.FlushCollectedLogs(true, false);
        }
    }

    public int Sleep()
    {
        var tired = Stats.GetStatusSeverityByStringMatch("chara_status_sleep_deprived");
        var sleepHour = (int)Math.Ceiling(tired > 0 ? Math.Min(Stats.SleepHours * 60, tired) : Stats.SleepHours * 60);
        Stats.AddOrModStatus("chara_status_sleeping", Stats.SleepDepth, sleepHour);
        Stats.RemoveStatusByStringMatch("chara_status_sleep_deprived");

        var memInst2 = new MemInstance(new List<int>() { }, new List<string>(), "", -1, -1, true, Memory_Response.Accept, Memory_Attitude.None, LocalizeDictionary.QueryThenParse("ui_entry_memory_sleep_begin"));
        this.Memory.AddEntry(memInst2, new List<string>() { "forbidMerge" });

        return sleepHour;
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
        var instance = scr_System_CampaignManager.current.FindItemInstanceByID(itemRefID);
        var comp = instance.GetComp_Equippable();
        if (((int)comp.revealing >= RevealingFilter || comp.equipLayer == BodyEquipLayer.Outer) && (comp.revealing < Revealing.Armored || unequipArmor ) && (!comp.lockable || unequipLocked))
        {
            if (Body.UnequipItem(itemRefID))
            {
                Inventory.AddItem(instance);
                //inventory_ref.Add(itemRefID);
                if (comp.statModifiers.Count > 0) this.Stats.RefreshAllStats(true);
                Skills.RefreshAvailableSkillChecks();
                this.PortraitManager.ClearHandlerCache();
            }
        }
    }


    /// <summary>
    /// default undress all<br/>
    /// will undress any item.revealing >= revealingfilter
    /// </summary>
    /// <param name="layer"></param>
    public void Undress(BodyEquipLayer layer = BodyEquipLayer.None, Revealing RevealingFilter = Revealing.Erotic, bool unequipArmor = false, bool unequipLocked = false)
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
                    if (instance.TryGetEquip(out var item, layer, slot)) UnequipItem(item.RefID, (int)RevealingFilter, unequipArmor, unequipLocked);
                }

            }
        }
    }

    public bool NeedUndress(BodyEquipLayer includeLayer, Revealing includeRating)
    {
        foreach(var i in Body.EquippedItemRefs)
        {
            var item = scr_System_CampaignManager.current.FindItemInstanceByID(i);
            var comp = item.GetComp_Equippable();
            if (comp == null) continue;
            else if (comp.equipLayer <= includeLayer) continue;
            else if (comp.revealing < includeRating) continue;
            else
            {
               // Debug.LogError($"{FirstName} found undress target {item.DisplayName}, equiplayer {comp.equipLayer} < {includeLayer}, {comp.revealing} < {includeRating}");
                return true;
            }
        }
        return false;
    }

    public void UndressAll(BodyEquipLayer upToLayer, Revealing RevealingFilter = Revealing.Erotic, bool unequipArmor = false, bool unequipLocked = false)
    {
        for (var layer = BodyEquipLayer.Outer; layer > upToLayer; layer --)
        {
            Undress(layer, RevealingFilter, unequipArmor, unequipLocked);
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
            var contentrefs = new List<Item_Instance>( Inventory.Contents );
            foreach (var refere in contentrefs)
            {
                if (refere.Equippable) Reequip(refere, layer);
            }
        }
    }

    /// <summary>
    /// Reequip from own inventory ref
    /// </summary>
    /// <param name="itemRefID"></param>
    public void Reequip(Item_Instance item, BodyEquipLayer layerFilter = BodyEquipLayer.None)
    {
       // Item_Instance item = scr_System_CampaignManager.current.FindItemInstanceByID(itemRefID);
        //Debug.Log("Redressing item ref " + item.DisplayName);
        if (item != null && Inventory.Contains(item))
        {
            ItemComponent_Equippable comp = item.GetComp("ItemComponent_Equippable") as ItemComponent_Equippable;
            if (comp != null && (layerFilter == BodyEquipLayer.None || comp.equipLayer == layerFilter))
            {
                if (Body.EquipItem(item.RefID, comp.equipCount, true))
                {
                    Skills.RefreshAvailableSkillChecks();
                    if (comp.statModifiers.Count > 0) this.Stats.RefreshAllStats(true);
                    Inventory.Remove(item);
                    this.PortraitManager.ClearHandlerCache();
                }
            }
        }
    }

    /// <summary>
    /// Call when destroy
    /// </summary>
    public void DisposeInternal()
    {
        RemoveObservers();
        if (PortraitManager != null) PortraitManager.ClearInternal();
    }

    public void PostReloadUpdate()
    {
        if (this.Relationships != null) Relationships.PostReloadUpdate();
    }

    public void OnAfterDeserialize()
    {
        string s = "Loaded Chara " + FullName + "\n";
        if (this.factionManager != null) FactionManager.ReEstablishParentData(this);
        if (this.Body != null) Body.ReEstablishParent(this);
        if (this.Memory != null) Memory.ReEstablishParent(this);
        if (this.Skills != null) Skills.ReEstablishParent(this);
        if (this.Stats != null) Stats.ReEstablishParent(this);  // stats require memory
        if (this.Portrait != null) Portrait.RebuildInternal(this);
        if (this.Relationships != null) Relationships.ReEstablishParent(this);

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
        ReEstablishObservers();
    }

    protected void ReEstablishObservers()
    {
        scr_System_Time.current.Observer_globalTime += Observer_GlobalMinute;
        scr_System_Time.current.Observer_globalTime_5min += Observer_GlobalMinute5;
        scr_System_Time.current.Observer_globalTime_Hours += Observer_GlobalHour;
        scr_System_Time.current.Observer_globalTime_Day += Observer_GlobalDay;
        scr_System_Time.current.Observer_globalTime_Day += Observer_GlobalDay_0;


        scr_UpdateHandler.current.Observer_PreUpdateTime += PreUpdateTime;
        scr_UpdateHandler.current.Observer_PostUpdateTime_2 += PostUpdateTime2;
        scr_UpdateHandler.current.Observer_PostUpdateTime_3 += PostUpdateTime3;
        scr_UpdateHandler.current.Observer_PostUpdateTime_EventEnd += PostEvent;
    }

    protected void PostEvent(bool eventhandler_active)
    {

    }

    protected void RemoveObservers()
    {
        scr_System_Time.current.Observer_globalTime -= Observer_GlobalMinute;
        scr_System_Time.current.Observer_globalTime_5min -= Observer_GlobalMinute5;
        scr_System_Time.current.Observer_globalTime_Hours -= Observer_GlobalHour;
        scr_System_Time.current.Observer_globalTime_Day -= Observer_GlobalDay;
        scr_System_Time.current.Observer_globalTime_Day -= Observer_GlobalDay_0;

        scr_UpdateHandler.current.Observer_PreUpdateTime -= PreUpdateTime;
        scr_UpdateHandler.current.Observer_PostUpdateTime_2 -= PostUpdateTime2;
        scr_UpdateHandler.current.Observer_PostUpdateTime_3 -= PostUpdateTime3;
        scr_UpdateHandler.current.Observer_PostUpdateTime_EventEnd -= PostEvent;
    }

    public RelationshipManager Relationships = null;

    public void NotifyCharaUnregister(Character_Trainable c)
    {
        this.Memory.NotifyCharaUnregister(c);
        this.Relationships.NotifyCharaUnregister(c.RefID);
    }
    public void NotifyRoomUnregister(Room_Instance r)
    {
        this.Memory.NotifyRoomUnregister(r);
    }


    [JsonIgnore] public bool Debug_ForceDeepSleep = false;
}

public class Character_BaseID_Index
{

}



public class Character_Base_Index : I_IndexMergeable, I_IndexHasID, I_RemoveNonExisting, I_RemoveNSFW
{
    public List<Character_SerializableBase> baseCharacters = new List<Character_SerializableBase>();

    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Character_Base_Index;
        if (l == null || l.baseCharacters == null) return;

        string s = "";
        foreach (var i in baseCharacters) s += i.baseID + " | ";
        s += "+++";
        foreach (var i in l.baseCharacters) s += i.baseID + " | ";
        //Debug.Log("Merging with " + s);
        this.baseCharacters.AddRange(l.baseCharacters);
        this.baseCharacters.RemoveAll(x => x.baseID == null || x.baseID.Length < 1);
        
    }

    Dictionary <string, Character_SerializableBase> ID_Dictionary = new Dictionary<string, Character_SerializableBase>();
    public void RegisterAllID(List<string> s)
    {
        s.Add("Character_Base_Index : registering ID with list length [" + baseCharacters.Count + "]");
        foreach (Character_SerializableBase o in this.baseCharacters)
        {
            if (o.baseID == "") continue;
            if (!ID_Dictionary.ContainsKey(o.baseID)) ID_Dictionary[o.baseID] = o;
            else Debug.LogError($"Error registering allID in Character_Base_Index, {o.baseID} already registered");
        }
    }


    public void DeleteChara(Character_SerializableBase c)
    {
        baseCharacters.Remove(c);
        ID_Dictionary.Remove(c.baseID);
    }

    public void SetChara(Character_SerializableBase c)
    {
        if (ID_Dictionary.ContainsKey(c.baseID))
        {
            DeleteChara(ID_Dictionary[c.baseID]);
        }
        baseCharacters.Add(c);
        ID_Dictionary[c.baseID] = c;
    }

    public Character_SerializableBase GetByID(string id) { return ID_Dictionary.ContainsKey(id) ? ID_Dictionary[id] : null; }

    /// <summary>
    /// Return a new serialized copy of [id] character with wiped template data
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public Character_Trainable GetChara(string id)
    {
        var template = GetByID(id);
        if (template == null) return null;
        var str = JsonConvert.SerializeObject(template, UtilityEX.SerializerSettings);
        var chara = JsonConvert.DeserializeObject<Character_Trainable>(str, UtilityEX.SerializerSettings);
        chara.Template = null;
        return chara;
    }

    public void RemoveNonExisting()
    {
        Debug.Log($"CALLING RemoveNonExisting on {baseCharacters.Count} instances");
        foreach (var c in baseCharacters)
        {
            c.PurgeNonExistingData();
        }
    }

    public void RemoveNSFW()
    {
        foreach(var c in baseCharacters)
        {
            foreach(var p in c.Portrait.portraitPriorityList)
            {
                p.Variants = null;
            }
        }
    }
}
