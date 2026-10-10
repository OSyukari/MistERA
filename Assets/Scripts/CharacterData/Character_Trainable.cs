using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
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


[System.Serializable]
public class Character_Trainable : ScriptableObject, I_Disposable, I_CharaGen
{
    public bool isTemporaryActor = false;

    /// <summary>
    /// ID of the Manageable_WorkerPool this character is a fallback worker of, or empty for ordinary
    /// characters. Cleared automatically once they stop being a member of that pool (e.g. recruited).
    /// </summary>
    public string fallbackPoolID = "";

    /// <summary>
    /// Dormant characters get no per-minute ticks and UpdateAllCharaJob skips them; hour/day ticks keep
    /// running. The skipped per-minute state (statuses, cooldowns, digestion, memories) is caught up in one
    /// go by EndDormantState on waking. Only used for fallback workers parked in their pool room - see
    /// SetDormant and FallbackWorkerManager.
    /// </summary>
    [JsonProperty] protected bool isDormant = false;
    [JsonIgnore] public bool IsDormant { get { return isDormant; } }

    /// <summary>Game time SetDormant(true) was called; default (old saves) = no catch-up on waking.</summary>
    [JsonProperty] protected DateTime dormantSince = default;

    public void SetDormant(bool dormant)
    {
        if (isDormant == dormant) return;
        if (dormant)
        {
            ChangeCurrentJob(null);
            // flush this round's buffers now - PreUpdateTime/PostUpdateTime won't run again until waking
            Relationships.FinalizeAttitudeRound();
            Relationships.ClearLastInteractedRelationships();
            Relationships.ClearEPCache();
            Body.ClearLastInteractedRefs();
            Skills.FinalizeExperience();
            RemoveObservers_Minute();
            // fallback workers: drop every memory that would expire anyway, so parked workers stay small in saves
            if (fallbackPoolID != "") Memory.ClearExpiring();
            dormantSince = scr_System_Time.current.getCurrentTime();
            isDormant = true;
        }
        else
        {
            isDormant = false;
            int minutes = dormantSince == default ? 0 : (int)(scr_System_Time.current.getCurrentTime() - dormantSince).TotalMinutes;
            dormantSince = default;
            EndDormantState(minutes);
            ReEstablishObservers_Minute();
            // they didn't eat or sleep while parked - come back rested/fed
            RestoreAll();
        }
    }

    /// <summary>
    /// Catch up, in one go, the per-minute updates a dormant character skipped - mainly so time-limited data
    /// (statuses, cooldowns, stomach contents, memories) expires as if they had been ticking all along.
    /// </summary>
    protected void EndDormantState(int minutes)
    {
        if (minutes <= 0) return;
        Stats.PreUpdateTimeTick(minutes);
        var span = TimeSpan.FromMinutes(minutes);
        Stats.UpdateTimeMinute(span, span);
        Relationships.RefreshMinutes(minutes);
        Body.UpdateTimeMinute(span);
        Memory.Tick(minutes);
        Memory.DailyClear();
    }


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


    Room_Instance _currentRoom = null;
    bool cached_currentroom = false;
    [JsonIgnore] public Room_Instance CurrentRoom { get
        {
            if (_currentRoom == null && !cached_currentroom)
            {
                _currentRoom = scr_System_CampaignManager.current.Map.FindRoomByChara(this.RefID);
                cached_currentroom = true;
            }
            return _currentRoom;
        } }



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
            var party = FactionManager.CurrentActiveParty;
            if (party != null && party.isPrisoner(this.RefID)) return true;

            var locale = FactionManager.CurrentLocaleFaction;
            return locale != null && locale.GetMemberType(this).isPrisoner;
        }
    }

    /// <summary>
    /// Whether this character agrees to share their private room with the given full set of other
    /// occupants (the resulting roommate group, excluding themselves). Requirement is lower when the
    /// home faction has no other empty private room to fall back to - same gender needs at least
    /// intimacy_medium (or intimacy_low when there's no empty room to instead move into); different
    /// gender needs intimacy_high (or intimacy_medium when there's no empty room).
    /// </summary>
    public bool WouldAgreeToShareRoom(List<Character_Trainable> others)
    {
        bool hasEmptyRoom = false;
        var homeFaction = this.FactionManager.Faction_Home;
        if (homeFaction != null)
        {
            foreach (var room in homeFaction.ManagedRooms.Values)
            {
                if (room.isRoomPrivate && homeFaction.RoomOwners(room.RefID).Count == 0)
                {
                    hasEmptyRoom = true;
                    break;
                }
            }
        }

        foreach (var other in others)
        {
            if (other == this) continue;

            var rel = this.Relationships.FindRelationshipWith(other);
            bool sameGender = this.isMale == other.isMale && this.isFemale == other.isFemale;
            bool agrees = sameGender
                ? rel.HasPermission_Intimacy_Medium() || (!hasEmptyRoom && rel.HasPermission_Intimacy_Low())
                : rel.HasPermission_Intimacy_High() || (!hasEmptyRoom && rel.HasPermission_Intimacy_Medium());

            if (!agrees) return false;
        }
        return true;
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
    [JsonIgnore] public bool canMove { get { return canAct && !isRestrained && !isImprisoned && !Stats.hasStatusEXTag(StatsUtility.Stat_Tag_Immobilized) && !Stats.hasStatusTag(StatsUtility.Stat_Tag_Immobilized); } }
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
    [JsonIgnore] public string BaseID { get { return baseID; } set {

            if (this.baseTemplateID == "") this.baseTemplateID = this.baseID;
            this.baseID = value; } }
    [JsonProperty] protected int referenceID = -1;
    [JsonIgnore] public int RefID { get { return referenceID; } }

    public event Action<Room_Instance> OnMoveToRoom;
    public void NotifyMoveToRoom(Room_Instance r)
    {
        this._currentRoom = r;
        OnMoveToRoom?.Invoke(r);
    }

    public void InitializeWithRefID(int refID)
    {

        this.referenceID = refID;

        if (Template == null)
        {
            Debug.LogError("Error template null!");
        }
        this.Appearance = Template.Appearance;

        //Debug.Log("Setting Appearance to " + this.Appearance);

        if (this.Body == null) Body = new Character_Body(this);
        else Body.ReEstablishParent(this);

        this.Body.Height = Template.Height;
        this.Body.Weight = Template.Weight;

        this.Memory = new MemoryManager(this);
        this.Stats.InitializeWithID(this, Template.stat_STR, Template.stat_CON, Template.stat_PSY, Template.stat_WIL);

        Body.AddMissing();
        //this.sexLogManager = new SexLogManager(refID);
        ReEstablishObservers();
        RestoreAll(true);

        this.interactionJobPointer = new Job_CharaCOM(refID);
        this.interactionJobRef = scr_System_CampaignManager.current.Register(InteractionJob);

        this.Relationships = new RelationshipManager(this);
        this.PortraitManager.RebuildInternal(this);


        // Apply experience initializers declared in the template hierarchy.
        // Parent initializers are prepended (run first), then child's own.
        var baseExp = Template.basicExperience;
        var addonExps = Template.initialExperiences;

        List<string> expInits = new List<string>();

        if (baseExp.Count > 0)
        {
            var rand = Utility.GetRandomElement(baseExp);
            var expp = scr_System_Serializer.current.MasterList.Experiences.GetInitializerByID(rand);
            if (expp != null)
            {
                expInits.Add($"adding exp {expp.BaseID}");
                expp.Execute(this);
            }
            else
            {
                expInits.Add($"cannot find exp {rand}");
            }
        }

        foreach (var id in addonExps)
        {
            var init = scr_System_Serializer.current.MasterList.Experiences.GetInitializerByID(id);
            if (init != null)
            {
                expInits.Add($"adding exp {init.BaseID}");
                init.Execute(this);
            }
            else
            {
                expInits.Add($"cannot find exp {id}");
            }
        }

        Debug.Log($"Character {FirstName} spawned, expinits {expInits.Count}:\n{String.Join("\n", expInits)}");

        Skills.UpdateAllSkills(null);

        // fill statbars against the final maxes: exp inits and skill updates above can add
        // modifiers that change max values after the earlier RestoreAll
        RestoreAll();
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
            return (this.Stats == null || !this.Stats.isConsciousnessUnconscious) && !isTimeStopped;
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
        Relationships.FinalizeAttitudeRound();
        Body.ClearLastInteractedRefs();
        Relationships.ClearLastInteractedRelationships();
        this.Stats.PreUpdateTimeTick();
        this.PortraitManager.ClearHandlerCache();
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
                //Debug.Log(FirstName + " Comparevalue isInEstrus [" + false + "] [" + operand + "] [" + value + "]");
                return Utility.CompareValue(ReproCycle != null && ReproCycle.isEstrus, operand, value);
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
        this.Relationships.ClearEPCache();
        CheckConsciousness();
    }

    /// <summary>
    /// Cause-agnostic consciousness edge detector: whichever unconscious cause (sleep, pain, etc.)
    /// happens first starts the shared snapshot; WakeUp() is the sole consumer regardless of cause,
    /// since sleep's own trigger already no-ops correctly while a different cause still holds the
    /// character under (Memory.consciousnessMemory just stays populated until that resolves).
    /// </summary>
    private void CheckConsciousness()
    {
        if (this.isTemporaryActor) return;

        if (Stats.isConsciousnessUnconscious) Memory.ConsciousnessLost_TryStart();
        else if (Memory.consciousnessMemory != null) WakeUp(true);
    }

    private void PostUpdateTime3()
    {
        this.Skills.FinalizeExperience();
        this._cachedJobDescription = string.Empty;
        this.Memory.Tick();
        if (!isTimeStoppedLoose)
        {
            MovedInTimeStop = false;
            forbidGreeting = false;
        }
    }

    private void Observer_GlobalMinute5(TimeSpan t)
    {
        this.Body.UpdateTimeMinute(t);
        this.Relationships.RefreshMinute5();
    }
    private void Observer_GlobalMinute(TimeSpan t, TimeSpan t_real)
    {
        //Debug.Log($"Chara stat update globaltime {t.TotalMinutes}");
        this.Stats.UpdateTimeMinute(t, t_real);
        this.Relationships.RefreshMinute1();
    }
    private void Observer_GlobalHour(TimeSpan t)
    {
        //Debug.Log("Character Observer_GlobalHour for [" + FirstName + "]");
        //if (Stats.GetStatusSeverityByStringMatch("chara_status_sleeping") > 0)
        
        var party = this.FactionManager.CurrentParty;
        int currentHour = scr_System_Time.current.getCurrentTime().Hour;
        if (Stats.isConsciousnessUnconscious)
        {
            timeSinceLastSleep = 0;

            // Recover chara based on sleep efficiency ?
        }
        else if (isDormant)
        {
            // parked fallback worker - living off-screen, so no sleep deprivation builds up
            timeSinceLastSleep = 0;
        }
        /*
        else if (party != null && party.isActive && party.SleepHours.Contains(currentHour))
        {
            timeSinceLastSleep = 0;
        }*/
        else if (hasStatKeyword("sleep"))
        {
            timeSinceLastSleep += 1;
            var sleephours = Stats.SleepHours;
            if (timeSinceLastSleep > Math.Max(24, sleephours * 2))
            {
                if (sleephours > 0) Stats.AddOrModStatus("chara_status_sleep_deprived", (sleephours * 60)*1.2f, (int)((sleephours * 60)*0.6f));
            }
        }

        this.Body.UpdateTimeHour(t);
        this.TickWomb();
        this.TickLabor();
        scr_UpdateHandler.current.EventHandler.Trigger(this, EventTrigger.OnHourlyUpdate);
        timeSinceLastEat = Math.Min(24, timeSinceLastEat + 1);
        this.Relationships.HourlyRefresh();
        //Debug.Log($"{FirstName} Observer_GlobalHour: conscious? {Stats.isConsciousnessUnconscious} sleep? {hasStatKeyword("sleep")} lastSleep {timeSinceLastSleep}, lastEat {timeSinceLastEat}");

        // membertype workModules can vary by day of week (activeDays), so which hours count as
        // "occupied" for sleep purposes can change from one day to the next - rebuild privateSchedule
        // every hour (instead of once/day) so a schedule change is picked up as soon as it's within the
        // rolling 24h lookahead. UpdateSchedule no-ops on its own if the character is currently asleep.
        if (FactionManager != null)
        {
            var scheduleRefreshMsg = new List<string>();
            FactionManager.UpdateSchedule(ref scheduleRefreshMsg, false);
            // recreation bookings against the refreshed schedule / planned night / sleep state
            FactionManager.OnHourUpdate_Recreation();
        }


    }
    /// <summary>
    /// Externally set timesincelastsleep to 0
    /// </summary>
    public void CapLastSleepTime()
    {
        if (timeSinceLastSleep > 23) timeSinceLastSleep = 23;
    }

    public void NotifyFoodConsume(Item_Instance i)
    {
        //Debug.LogError($"{FirstName} notify food consumption {i.DisplayName}");
        this.timeSinceLastEat = 0;
    }

    [JsonIgnore]
    public bool HasMenstrualCycle
    {
        get
        {
            return ReproCycle != null;
        }
    }

    ReproductionTemplate _reproTemplate = null;
    [JsonIgnore] public ReproductionTemplate ReproTemplate
    {
        get
        {
            if (_reproTemplate == null && Race != null)
            {
                _reproTemplate = scr_System_Serializer.current.MasterList.humanoid_Races.GetReproduction(this.Race.ID);
            }
            return _reproTemplate;
        }
    }

    public ReproductionCycle ReproCycle = null;

    /// <summary>
    /// Advances every womb by one hour. Only a forced (debug) birth delivers here - natural births come from the
    /// "labor" event chain (Labor_Contraction -> GiveBirth), labor stage changes from TickLabor.
    /// </summary>
    public void TickWomb(bool forcebirth = false, bool forbidBirth = false)
    {
        if (this.wombs == null || this.wombs.Count < 1) return;
        bool birth = false;
        foreach (var wb in wombs)
        {
            /// notify result
            wb.HourTick(forcebirth, forbidBirth);
            if (wb.birthEV.Count > 0) birth = true;
        }
        if (birth)
        {
            NotifyBirth();
            ContinueLaborAfterBirth();
        }

        ReproductionUtility.UpdateLaborStatus(this);

        // a baby that entered early labor during this tick (FoetusTemplates.AdvStage_End resets lifespan to 0; the
        // next tick adds 60) - once per mother, however many babies started together
        foreach (var egg in ReproductionUtility.AllOvums(this))
        {
            if (egg.State != OvumState.Final || egg.lifespan != 0) continue;
            NotifyLaborStart();
            break;
        }
    }

    /// <summary>
    /// Labor_Start: the mother may be admitted to a hospital. The player decides - self = player, the mother injected
    /// as "mother" - for the player's own labor and for anyone sharing the player's permanent home faction; any other
    /// mother decides herself (self = mother; shown even out of view when her home or temp home is player-managed -
    /// ReproductionUtility.IsLaborVisibleToPlayer). The admission options are built here (FactionJoinUtility) and only
    /// loaded by the event.
    /// </summary>
    void NotifyLaborStart()
    {
        var player = scr_System_CampaignManager.current.Player;
        bool playerDecides = ReproductionUtility.IsLaborPlayerDecided(this);

        var ev = new EventInstance(playerDecides ? player : this, ReproductionUtility.event_laborStart, "");
        ev.Targets["mother"] = new List<Character_Trainable>() { this };
        if (!playerDecides && ReproductionUtility.IsLaborVisibleToPlayer(this)) ev.displayOverride = true;
        FactionJoinUtility.InsertJoinOptions(ev, ReproductionUtility.laborStart_optionsKey, this, ReproductionUtility.memberType_hospitalPatient);
        scr_UpdateHandler.current.EventHandler.StartEvent(ev, false);
    }

    /// <summary>
    /// Hourly labor stage changes. Called from Observer_GlobalHour only - not from the day loop in TickMenstruation,
    /// which bulk-ticks the womb 24 times at once.
    /// Early labor over: the whole labor waits for a C-section if any baby in labor cannot be delivered naturally;
    /// otherwise one baby enters intense labor once the mother is resting (she stays in early labor until then).
    /// The birth itself is events: the hourly Labor_IntenseStart starts the "labor" event chain.
    /// </summary>
    public void TickLabor()
    {
        if (this.wombs == null || this.wombs.Count < 1) return;
        ReproductionUtility.ClampRunningLabor(this);
        if (ReproductionUtility.IsInIntenseLabor(this)) return;
        if (ReproductionUtility.RequiresCSection(this))
        {
            // waiting for a C-section that can no longer come (no hospital available, none running): back to natural birth
            bool operating = scr_System_CampaignManager.current.GetSpecialTrackedJobs(this, j => j is Job_CSection).Count > 0;
            if (operating || ReproductionUtility.IsCSectionAvailable(this)) return;
            foreach (var egg in ReproductionUtility.AllOvums(this))
            {
                if (egg.State == OvumState.Final_RequireHelp) egg.State = OvumState.Final;
            }
            ReproductionUtility.UpdateLaborStatus(this);
        }

        Ovum ready = null;
        foreach (var egg in ReproductionUtility.AllOvums(this))
        {
            if (!egg.isEarlyLaborOver) continue;
            ready = egg;
            break;
        }
        if (ready == null) return;

        if (TrySendLaborToCSection()) return;
        if (!ReproductionUtility.CanBirthNow(this)) return;
        StartIntenseLabor(ready, ReproductionUtility.ClampActiveLabor(ready.foetus == null ? 210 : ready.foetus.duration_labor_intense, false));
        // first baby only - following siblings continue the same active labor
        AddLaborMemory(memory_laborActive, true);
    }

    /// <summary>
    /// If any baby in early labor cannot be delivered naturally and a C-section is available at all
    /// (ReproductionUtility.IsCSectionAvailable), the whole labor (every baby in early labor) waits for it
    /// (Final_RequireHelp). Returns true if it did; otherwise the labor goes on as a natural birth.
    /// </summary>
    bool TrySendLaborToCSection()
    {
        bool unsafeLabor = false;
        foreach (var egg in ReproductionUtility.AllOvums(this))
        {
            if (egg.State != OvumState.Final) continue;
            if (ReproductionUtility.CanDeliverSafely(egg.womb, egg, out _, out _)) continue;
            unsafeLabor = true;
            break;
        }
        if (!unsafeLabor) return false;
        if (!ReproductionUtility.IsCSectionAvailable(this)) return false;

        foreach (var egg in ReproductionUtility.AllOvums(this))
        {
            if (egg.State == OvumState.Final) egg.State = OvumState.Final_RequireHelp;
        }
        EndIntenseLaborState();
        ReproductionUtility.SetLaborObstructed(this, true);
        ReproductionUtility.UpdateLaborStatus(this);
        return true;
    }

    /// <summary>
    /// One baby enters its intense stage (duration minutes). State only - the "labor" event chain (started by the
    /// hourly Labor_IntenseStart, or continued by the previous sibling's birth) rolls the birth.
    /// </summary>
    void StartIntenseLabor(Ovum egg, int duration)
    {
        egg.State = OvumState.IntenseLabor;
        egg.intenseStartTime = scr_System_Time.current.getCurrentTime();
        egg.lastBirthRollTime = egg.intenseStartTime;
        egg.intenseDuration = Math.Max(1, duration);
        // active labor: a pure label replaces the early-labor progress status
        if (Stats.FindStatusByExactID(ReproductionUtility.status_labor_intense) == null) Stats.AddOrModStatus(ReproductionUtility.status_labor_intense, 100);
        ReproductionUtility.UpdateLaborStatus(this);
    }

    /// <summary>No baby in intense labor any more; the labor chain's next Labor_Contraction sees it and ends the chain.</summary>
    void EndIntenseLaborState()
    {
        Stats.RemoveStatusByExactID(ReproductionUtility.status_labor_intense);
    }

    /// <summary>
    /// Delivers babies now: allWaiting = every baby in any labor stage (C-section), else the baby in intense labor.
    /// followupEventID (default PregnancyEnd_Birth) runs through NotifyBirth. Afterwards the next sibling in labor gets
    /// a short intense stage (duration_birth_interval), unless the labor goes to a C-section. False if nobody was delivered.
    /// </summary>
    public bool GiveBirth(bool allWaiting, string followupEventID = "")
    {
        if (this.wombs == null || this.wombs.Count < 1) return false;
        bool any = false;
        foreach (var wb in wombs)
        {
            wb.birthEV.Clear();
            foreach (var egg in wb.eggs)
            {
                if (egg == null) continue;
                if (allWaiting ? !ReproductionUtility.IsLaborState(egg.State) : egg.State != OvumState.IntenseLabor) continue;
                wb.birthEV.Add(egg);
                any = true;
            }
        }
        if (!any) return false;
        NotifyBirth(followupEventID);
        ContinueLaborAfterBirth();
        return true;
    }

    /// <summary>
    /// After a birth: the next baby in early labor gets a short intense stage (multiple birth) - or, if any remaining
    /// baby cannot be delivered naturally, the rest wait for a C-section. With no birth left, intense labor state ends.
    /// </summary>
    void ContinueLaborAfterBirth()
    {
        if (ReproductionUtility.IsInIntenseLabor(this)) return;
        var next = ReproductionUtility.FindOvum(this, OvumState.Final);
        if (next != null && !TrySendLaborToCSection())
        {
            StartIntenseLabor(next, ReproductionUtility.ClampActiveLabor(next.foetus == null ? 20 : next.foetus.duration_birth_interval, true));
            return;
        }
        EndIntenseLaborState();
        // no baby left in any labor stage: removes every labor status
        ReproductionUtility.UpdateLaborStatus(this);
    }


    /// <summary>
    /// Advances the reproduction cycle by whole days. tickWomb also bulk-advances the wombs 24 hours per day - only for
    /// skipping time (debug advance); the daily update must not, since Observer_GlobalHour already ticks the womb hourly.
    /// notify: send status notices (NotifyOvulation) - daily update only, not on womb registration or debug advance.
    /// </summary>
    public void TickMenstruation(int year = 0, int month = 0, int day = 1, bool log = false, bool tickWomb = false, bool notify = false)
    {
        if (ReproCycle == null) return;
        if (ReproTemplate == null) return;

        int totalCycle = year * 365 + month * 30 + day;
        bool birth = false;
        for (int i = 0; i < totalCycle; i++)
        {
            bool stagesupressed = GetStatusSeverity(ReproductionUtility.status_pills_daily) > 0;
            bool emergencyActive = GetStatusSeverity(ReproductionUtility.status_pills_emergency) > 0;
            bool forceOvulateActive = GetStatusSeverity(ReproductionUtility.status_pills_induceovulation) > 0;


            // this variable will be replaced by a boolean getter
            // that return true if every womb is in menopause
            bool isOvumexhausted = false;

            // this shouldnt be necessary. if this is empty, then on "add womb" stage this should have been initialized
            /// if (Menstruation == null) Menstruation = new MenstruationState(ReproTemplate);

            //bool ispregnant = false;

            /*
            if (ReproTemplate.hasEstrus && Menstruation.CycleStage == MenstruationStatus.Ovulation)
            {
                // leave this blank for now, I'll add this in the future
            }*/
            if (tickWomb) for (int j = 0; j < 24; j++) TickWomb(false, log);
            

            var ispregnant = wombs != null && wombs.Any(w => w.isPregnant);
            bool wasOvulating = ReproCycle.CanOvulate;
            ReproCycle.Tick(ReproTemplate, ispregnant, stagesupressed, emergencyActive, forceOvulateActive,  isOvumexhausted);

            foreach (var wb in wombs)
            {
                /// notify result
                wb.dayTick_Cycle(ReproCycle);
            }

            // Ovulation-trigger item: forces the cycle to (re)enter the ovulate-eligible stage and
            // fires ovulation directly every day it's active, bypassing dayTick_Cycle's transition
            // guard above (which would otherwise skip re-firing on consecutive days already sitting
            // in that stage).
            if (forceOvulateActive && ReproCycle.CanOvulate)
            {
                foreach (var wb in wombs) wb.ovulation();
            }

            // menstrual cycle only - an estrus cycle's ovulate-eligible stage is heat, ovulation itself comes on climax
            if (notify && !wasOvulating && ReproCycle is Cycles_Menstruation && ReproCycle.CanOvulate) NotifyOvulation();

            TickCyclePhaseStatus();
            TickPregnancyMoodStatus();
        }

        if (log)
        {
            string debugmsg = $"{FirstName} advance cycle by {totalCycle} days, current stage {ReproCycle.CycleName(this)}";
            foreach (var v in wombs)
            {
                debugmsg += $"\n{v.debugTooltip}";
            }

            Debug.Log(debugmsg);
        }
    }

    /// <summary>
    /// Repro_OvulationStart: one-line notice that the cycle entered ovulation. Only sent when the player should know
    /// (ReproductionUtility.IsReproStatusVisibleToPlayer) and then shown wherever the player is.
    /// </summary>
    void NotifyOvulation()
    {
        if (!ReproductionUtility.IsReproStatusVisibleToPlayer(this)) return;
        var ev = new EventInstance(this, ReproductionUtility.event_ovulation, "");
        ev.displayOverride = true;
        scr_UpdateHandler.current.EventHandler.StartEvent(ev, false);
    }

    /// <summary>
    /// Per-day, cycle-type-agnostic status handling: each cycle phase may have a status configured
    /// in ReproTemplate.cycleStatusIDs (indexed by ReproCycle.CurrentStatus). On a phase transition,
    /// the previous phase's status (if any, and if different from the new phase's) is removed.
    /// The current phase's status (if any) has its severity set directly every day from
    /// ReproCycle.CurrentPhaseProgress via SetStatusSeverityRatio (0=variants[0].threshold,
    /// 1=variants[last].threshold) — whether that reads as "ticking up" or "ticking down" is entirely
    /// down to whether the status' own threshold range is e.g. 0..100 or -100..0, not anything decided
    /// here. No decay is involved, since phase lengths vary wildly across races.
    /// </summary>
    private void TickCyclePhaseStatus()
    {
        if (ReproCycle == null || ReproTemplate == null) return;

        int current = ReproCycle.CurrentStatus;
        string currID = ReproTemplate.GetCycleStatusID(current);

        if (current != ReproCycle.PreviousStatus)
        {
            string prevID = ReproTemplate.GetCycleStatusID(ReproCycle.PreviousStatus);
            if (prevID != "" && prevID != currID)
            {
                Stats.RemoveStatusByStringMatch(prevID);
                Stats.SyncPainCompanion(prevID);
            }
            ReproCycle.AdvanceStatusHistory();
        }

        if (currID == "") return;

        float progress = Mathf.Clamp01(ReproCycle.CurrentPhaseProgress(ReproTemplate));
        Stats.SetStatusSeverityRatio(currID, progress);
        Stats.SyncPainCompanion(currID);
    }

    /// <summary>
    /// Pregnancy-stage-agnostic status handling, mirroring TickCyclePhaseStatus but keyed by the
    /// oldest ovum's OvumState via ReproTemplate.pregnancyStatusIDs instead of the cycle phase.
    /// No case-specific branching: every configured slot is checked the same way, whichever one
    /// matches the current OvumState gets its severity set from Ovum.CurrentPhaseProgress via
    /// SetStatusSeverityRatio, every other configured slot gets removed if present (covers stage
    /// transitions and losing pregnancy).
    /// </summary>
    private void TickPregnancyMoodStatus()
    {
        if (ReproTemplate == null) return;

        int activeIndex = -1;
        float progress = 0f;
        if (ReproCycle != null && ReproCycle.isPregnant)
        {
            var ovum = ReproductionUtility.GetOldestOvum(this);
            if (ovum != null)
            {
                activeIndex = (int)ovum.State;
                progress = Mathf.Clamp01(ovum.CurrentPhaseProgress());
            }
        }

        for (int i = 0; i < ReproTemplate.pregnancyStatusIDs.Count; i++)
        {
            string id = ReproTemplate.pregnancyStatusIDs[i];
            if (id == "") continue;

            if (i == activeIndex) Stats.SetStatusSeverityRatio(id, progress);
            else if (Stats.HasStatusByStringMatch(id)) Stats.RemoveStatusByStringMatch(id);
        }
    }


    public static string memory_laborActive = "memory_entry_labor_active";
    public static string memory_csection = "memory_entry_labor_csection";

    /// <summary>
    /// Adds a permanent (important) labor memory with the localized text textKey ($doctor$ = doctor's name, if given).
    /// floorName: the memory names the floor she is on (e.g. 在医院开始分娩) instead of the room.
    /// </summary>
    public void AddLaborMemory(string textKey, bool floorName, Character_Trainable doctor = null)
    {
        var desc = LocalizeDictionary.QueryThenParse(textKey);
        if (doctor != null) desc = desc.Replace("$doctor$", doctor.FullName);
        var memInst = new MemInstance(new List<int>(), new List<string>(), "", -1, -1, true, Memory_Response.Accept, Memory_Attitude.Neutral, desc);
        var entry = Memory.AddEntry(memInst, new List<string>() { "forbidMerge", "important" });
        if (entry == null) return;
        entry.entryDescription = desc;
        entry.disableRoomName = false;
        if (floorName)
        {
            var room = scr_System_CampaignManager.current.Map.FindRoomByChara(RefID);
            var floor = room == null ? null : scr_System_CampaignManager.current.Map.GetFloorByRoomRefID(room.RefID);
            if (floor != null && !string.IsNullOrEmpty(floor.displayName)) entry.roomNameOverride = floor.displayName;
        }
    }

    /// <summary>Delivers every womb's birthEV and runs eventID (default PregnancyEnd_Birth) on the mother.</summary>
    public void NotifyBirth(string eventID = "")
    {
        if (string.IsNullOrEmpty(eventID)) eventID = ReproductionUtility.event_birth;
        Manageable targetf = null;
        List<string> names = new List<string>();

        if (FactionManager.CurrentActiveParty != null)
        {
            targetf = FactionManager.CurrentActiveParty.FactionOwnerRoot;
        }
        else if (FactionManager.Faction_Home_Temporary != null && FactionManager.Faction_Home_Temporary.isPrisoner(RefID))
        {
            targetf = FactionManager.Faction_Home_Temporary;
        }
        else
        {
            targetf = FactionManager.Faction_Home;
        }

        var ev = new EventInstance(this, eventID, "");

        foreach (var i in this.FactionManager.HomeFactions) ev.displayOverride = ev.displayOverride || i.isPlayerRelatedFaction;
        ev.AppendStrings.Add("babyname", new List<string>());
        List<Action> birth = new List<Action>();


        if (targetf == null)
        {
            ev.AppendStrings.Add("outcome", new List<string>() { "the baby is lost" });
        }
        else if (targetf.isPlayerRelatedFaction)
        {
            ev.AppendStrings.Add("outcome", new List<string>() { $"the baby is taken by {targetf.FactionDisplayName}" });
        }
        else
        {
            ev.AppendStrings.Add("outcome", new List<string>() { $"the baby taken by {targetf.FactionDisplayName}" });
            // fallback workers' babies are kept by their pool (handled later); other non-player factions keep none
            if (!(targetf is Manageable_WorkerPool)) targetf = null;
        }

        ev.AppendStrings.Add("roomname", new List<string>() { (CurrentRoom == null ? "somewhere" : CurrentRoom.DisplayName) });

        bool haspreg = false;
        foreach (var wb in wombs)
        {
            if (wb.birthEV.Count > 0)
            {
                foreach (var egg in wb.birthEV)
                {
                    names.Add(egg.foetusItem.DisplayName);
                    haspreg = true;
                    ev.AppendStrings["babyname"].Add(egg.foetusItem.DisplayName);
                }
                wb.GiveBirthTo(this, wb.birthEV, targetf, ev.message.exp);
            }
        }

        ev.AppendStrings.Add("count", new List<string>() { $"{names.Count}" });

        // add memory entry - two versions: labor still going (another baby in labor or waiting for a C-section; the
        // delivered babies are already out of the womb here), or this birth ended the labor
        bool laborLeft = false;
        foreach (var egg in ReproductionUtility.AllOvums(this)) if (ReproductionUtility.IsLaborState(egg.State)) { laborLeft = true; break; }
        var mem_desc = LocalizeDictionary.QueryThenParse(laborLeft ? "memory_entry_givebirth" : "memory_entry_givebirth_laborEnd")
            .Replace("$count$", $"{names.Count}");
        var memInst_2 = new MemInstance(new List<int>(), new List<string>(), "", -1, -1, true, Memory_Response.Accept, Memory_Attitude.Neutral, String.Join("\n", names));
        // important: a birth is never forgotten (no expiry, see Memory_Entry's duration)
        var memEntry_2 = Memory.AddEntry(memInst_2, new List<string>() { "forbidMerge", "important" });
        memEntry_2.entryDescription = mem_desc;
        memEntry_2.disableRoomName = false;

        // add experience
        if (targetf != null) targetf.DailyReport.AddMiscRecord(
            LocalizeDictionary.QueryThenParse("faction_report_givebirth")
                .Replace("$count$", $"{names.Count}")
                .Replace("$name$", FirstName)
                .Replace($"room", CurrentRoom == null ? LocalizeDictionary.QueryThenParse("location_unknown") : CurrentRoom.DisplayName), names);

        scr_UpdateHandler.current.EventHandler.StartEvent(ev, false);

        var ispregnant = wombs != null && wombs.Any(w => w.isPregnant);
        ReproCycle.Birth(ispregnant, ReproTemplate);
    }


    private void Observer_GlobalDay(int updateOrder)
    {
        if (updateOrder != 2) return;
        if (HasMenstrualCycle)
        {
            TickMenstruation(notify: true);

            foreach (var wb in wombs)
            {
                /// notify result
                wb.dayTick_Cycle(ReproCycle);
            }
        }
        
        if (Memory != null) Memory.DailyClear();

        // check food and sleep need
        if (FactionManager != null) FactionManager.DailyNeedConsumption();

        List<Manageable.DailyReportHandler.MiscMessageEntry> updateMessage = new List<Manageable.DailyReportHandler.MiscMessageEntry>();
        this.Skills.UpdateAllSkills(updateMessage);
        this.Relationships.DailyRefresh(updateMessage);
        if (updateMessage.Count > 0)
        {
            foreach (var i in FactionManager.HomeFactions)
            {
                foreach(var m in updateMessage) i.DailyReport.AddMiscRecord(m);
            }
        }
    }

    [JsonIgnore] public List<BodyInternal_Womb> wombs = new List<BodyInternal_Womb>();
    public void RegisterWomb(BodyInternal_Womb wb)
    {
        if (wb == null) return;
        if (scr_System_CentralControl.current.isSafeMode) return;
        if (wombs.Contains(wb)) return;
        wombs.Add(wb);

        if (ReproCycle == null)
        {
            ReproCycle = ReproductionCycleUtility.MakeCycle(ReproTemplate);
            if (ReproCycle != null)
            {
                ReproCycle.Quickstart(ReproTemplate, Age, isDefaultAge);
                TickMenstruation();
            }
        }
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

    [JsonProperty] protected int timeSinceLastSleep = 0;
    [JsonProperty] protected int timeSinceLastEat = 24;
    [JsonProperty] public int ScheduledSleepMissingMinutes = 0;
    private void Observer_GlobalDay_0(int updateOrder)
    {
        if (updateOrder != 0) return;
        this.FactionManager.FlagForDailyNeed();
    }

    /// <summary>Day update stage 3 (after factions and characters updated): recreation planning only.</summary>
    private void Observer_GlobalDay_3(int updateOrder)
    {
        if (updateOrder != 3) return;
        if (FactionManager != null) FactionManager.OnDayUpdate_Recreation();
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

    List<string> _actorKeywords = null;
    [JsonIgnore]
    public List<string> ActorKeywords
    {
        get
        {
            if (_actorKeywords == null)
            {
                _actorKeywords = new List<string>();
                if (this.Template != null) _actorKeywords.AddRange(this.Template.actorKeyword);
                if (this.Race != null) _actorKeywords.AddRange(this.Race.RaceType);
                if (this.RaceTemplate != null) _actorKeywords.AddRange(this.RaceTemplate.actorKeyword);
                Utility.DistinctInPlace(_actorKeywords);
            }
            return _actorKeywords;
        }
    }


    protected CharaSafeTemplate _templateS = null;
    protected CharaTrainableTemplate _template = null;
    [JsonIgnore] public CharaTemplate Template
    { 
        get {
            if (scr_System_CentralControl.current.isSafeMode)
            {
                if (_templateS == null)
                {
                    _templateS = scr_System_Serializer.current.MasterList.Character_Bases.GetTemplateSafeByID(BaseID);
                    if (_templateS == null && baseTemplateID != "")
                    {
                        _templateS = scr_System_Serializer.current.MasterList.Character_Bases.GetTemplateSafeByID(baseTemplateID);
                    }
                    if (_templateS == null)
                    {
                        _templateS = new CharaSafeTemplate();
                        if (BaseID != "") Debug.LogError($"Error failed to find template id {BaseID} nor baseTemplateID {baseTemplateID}, initializing new");
                    }
                }
                return _templateS;
            }
            else
            {
                if (_template == null)
                {
                    _template = scr_System_Serializer.current.MasterList.Character_Bases.GetTemplateByID(BaseID) as CharaTrainableTemplate;
                    if (_template == null && baseTemplateID != "")
                    {
                        _template = scr_System_Serializer.current.MasterList.Character_Bases.GetTemplateByID(baseTemplateID);
                    }
                    if (_template == null)
                    {
                        _template = new CharaTrainableTemplate();
                        if (BaseID != "") Debug.LogError($"Error failed to find template id {BaseID} nor baseTemplateID {baseTemplateID}, initializing new");
                    }
                }
                return _template;
            }
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

    [JsonProperty]
    protected StatsManager stats = null;

    [JsonIgnore] public StatsManager Stats { get { if (stats == null) stats = new StatsManager();
        return stats; } }

    [JsonProperty]
    protected FameTracker fame = null;

    /// <summary>
    /// Fame by source type (see FameTracker) - e.g. ErAV recordings add "av" / "leaked" fame to their main
    /// actors as copies sell.
    /// </summary>
    [JsonIgnore] public FameTracker Fame { get { if (fame == null) fame = new FameTracker();
        return fame; } }

    [JsonProperty]
    protected RankTracker ranks = null;

    /// <summary>
    /// Current level on each rank track (see RankTrack) - only changes through a promotion evaluation.
    /// </summary>
    [JsonIgnore] public RankTracker Ranks { get { if (ranks == null) ranks = new RankTracker();
        return ranks; } }

    /// <summary>
    /// Current per-character emotional-reaction state (Angry/Happy/Focused/etc). See RelationshipManager /
    /// Character_Attitude. Replaces the old per-relationship RelationshipAttitude.
    /// </summary>
    public Character_Attitude GetCurrentAttitude() { return this.Relationships.GetCurrentAttitude(); }

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
    [JsonIgnore] public string CharacterCard
    {
        get
        {
            if (this.Template != null) return this.Template.GetCharacterCard;
            else return null;
        }
    }
    public void SetName(string firstName, string middleName, string lastName, string displayFormat){
        if (firstName != "") this.FirstName = firstName;
        if (middleName != "") this.middleName = middleName;
        if (lastName != "") this.lastName = lastName;
        if (displayFormat != "") this.nameDisplayFormat = displayFormat;

        _cachedFullName = "";
        _callName = string.Empty;
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
            if (_cachedFullName == "")
            {
                if (MiddleName == "" && LastName == "") _cachedFullName = FirstName;
                else _cachedFullName = LocalizeDictionary.QueryThenParse(nameDisplayFormat)
                                                            .Replace("$lastName$", LastName)
                                                            .Replace(" $middleName$", MiddleName == "" ? "" : " " + MiddleName)
                                                            .Replace("$firstName$", FirstName);
            }
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



    /// <summary>
    /// Whether c should take/keep working jobs dispatched at faction - false only while faction is one of
    /// c's work factions AND still owes c an unpaid salary backlog: a suspended Obligation_Salary
    /// (payeeRefID == c.RefID, owed > 0 - see RecurringObligation.IsSuspended) on that faction's own
    /// TradeManager, which self-clears the moment the missed wage is paid off. Non-work factions (home,
    /// locale visits) are never gated. See TryFindScheduledJobNode's strike check.
    /// <br/>reason: when returning false, a localized, human-readable explanation (one line per suspended
    /// obligation, amount via ItemEntry.Print) - see Utility.FillFactionRect's tooltip/red-name handling.
    /// </summary>
    public bool ShouldWorkFor(I_IsJobGiver faction)
    {
        return ShouldWorkFor(faction, out _);
    }

    public bool ShouldWorkFor(I_IsJobGiver faction, out string reason)
    {
        reason = "";
        var employer = faction == null ? null : faction.Faction;
        if (employer == null || employer.TradeManager == null) return true;
        if (FactionManager == null || !FactionManager.WorkFactions.Contains(employer)) return true;

        var owedSalaries = employer.TradeManager.Obligations.OfType<Obligation_Salary>()
            .Where(x => x.payeeRefID == RefID && x.IsSuspended).ToList();
        if (owedSalaries.Count > 0)
        {
            // case for failing salary
            reason = string.Join("\n", owedSalaries.Select(x =>
                LocalizeDictionary.QueryThenParse("management_faction_work_strike_tooltip")
                    .Replace("$item$", x.owed == null ? "" : x.owed.Print)));
            return false;
        }
        return true;
    }

    public bool CanWorkFor(I_IsJobGiver faction)
    {
        return CanWorkFor(faction, out var stringss);
    }

    /// <summary>
    /// Blocks working a job at faction (C) when the faction that actually dispatched this character there
    /// (A - Character_Factions.GetWorkFactionSourceOrDefault, the explicitly tracked source or else
    /// HomeFactions[0]) owes C an unpaid membership fee (suspended Obligation_MembershipFee - see
    /// Obligation_MembershipFee.HandlePaymentEvent). Mirror-image of ShouldWorkFor's strike check (C owing
    /// this character unpaid salary); this is "A hasn't paid C for B's membership, so B can't work there,"
    /// not "B personally owes anything" - Obligation_MembershipFee is scoped to (A, C, cadence), not per
    /// character, so this can block every character A dispatched to C at once, same granularity the
    /// obligation itself already has. Filtered to THIS character's own membershipFee.cadence at C (same
    /// lookup Obligation_MembershipFee.GetRelevantFees itself uses) so an unrelated sibling's unpaid dues
    /// under a different MemberType/cadence at the same provider never blocks this character - only the
    /// obligation actually covering this character's own membership arrangement counts. Recreation
    /// memberships (Character_Factions.RecreationFactions) are gated the same way, billed to the priority home.
    /// </summary>
    public bool CanWorkFor(I_IsJobGiver faction, out string reason)
    {
        reason = "";
        var provider = faction == null ? null : faction.Faction;
        if (provider == null) return true;
        if (FactionManager == null || (!FactionManager.WorkFactions.Contains(provider) && !FactionManager.RecreationFactions.Contains(provider))) return true;

        var status = provider.GetMemberType(this);
        if (status == null || status.membershipFee == null) return true;

        var sourceFaction = FactionManager.GetWorkFactionSourceOrDefault(provider.ID);
        if (sourceFaction == null || sourceFaction.TradeManager == null) return true;

        var unpaidFees = sourceFaction.TradeManager.Obligations.OfType<Obligation_MembershipFee>()
            .Where(x => x.TargetFaction == provider && x.cadence == status.membershipFee.cadence && x.IsSuspended).ToList();
        if (unpaidFees.Count > 0)
        {
            // GetDisplayName already resolves the fee's own membershipFeeName override (e.g. "学费"),
            // falling back to the generic "会员费" wording when unset - see Obligation_MembershipFee.
            reason = string.Join("\n", unpaidFees.Select(x =>
                LocalizeDictionary.QueryThenParse("management_faction_work_feeUnpaid_tooltip")
                    .Replace("$name$", x.GetDisplayName(sourceFaction))
                    .Replace("$item$", x.owed == null ? "" : x.owed.Print)));
            return false;
        }
        return true;
    }

    public Manageable.HourlySchedule GetJobPost(int hour = -1, int daysLookahead = 0)
    {
        if (FactionManager == null) return null;
        else
        {
            if(hour == -1) hour = scr_System_Time.current.getCurrentTime().Hour;
            return FactionManager.CurrentJobPost(hour, daysLookahead);
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

    /// <summary>
    /// Whether this character wants a solo flexible visit the planner picked for them (a membership activity, a flexible
    /// offer) - ctx is null. Always yes for now.
    /// </summary>
    public bool AcceptBooking(RecreationBooking booking, RecreationInviteContext ctx)
    {
        return true;
    }

    /// <summary>
    /// Whether this character takes session into consideration at all - asked once, the first time they see it (their
    /// inbox, a faction / world list, an extended invitation - RecreationUtility.RankSessions). No = Refused: final for
    /// this session, never ranked. ctx: who invited them, their role, how they were reached, who attends / is invited.
    /// The player answers through the Recreation_Invite event instead. Always yes for now.
    /// </summary>
    public bool AcceptSession(RecreationGroup session, RecreationInviteContext ctx)
    {
        return true;
    }

    /// <summary>
    /// Whether this character hosts session - asked before it is posted (RecreationUtility.TryHostSession; ctx.invited =
    /// who would be invited). No = not posted: the visit stays solo (a mandatory activity isn't booked). Always yes for now.
    /// </summary>
    public bool AcceptHosting(RecreationGroup session, RecreationInviteContext ctx)
    {
        return true;
    }

    /// <summary>
    /// Whether this character, confirming session, extends the invitation with validator (one of the spec's
    /// InviteeValidators - RecreationUtility.ExtendInvitation). No = that validator isn't used (an inviteRequired one then
    /// keeps them from taking part, as if nobody was found). Always yes for now.
    /// </summary>
    public bool AcceptExtending(RecreationGroup session, RecreationInviteTarget validator, RecreationInviteContext ctx)
    {
        return true;
    }

    /// <summary>
    /// Which of two bookings this character prefers - &gt; 0 incoming, &lt; 0 existing, 0 no preference. The comparator of
    /// the session ranking (RecreationUtility.RankSessions - ties fall back to a fixed order: earlier start, host, venue)
    /// and of a session against a flexible visit in its way (RecreationUtility.TryConfirm: the session wins unless the
    /// visit is preferred). Must be deterministic (no random rolls) so the same situation always resolves the same way.
    /// Equal for now.
    /// </summary>
    public int CompareBookingPreference(RecreationBooking existing, RecreationInviteContext existingCtx, RecreationBooking incoming, RecreationInviteContext incomingCtx)
    {
        return 0;
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

    /// <summary>
    /// What the character is "being" right now, for name labels: taking part in the recreation visit that drives this
    /// hour (Character_Factions.GetEffectiveBooking - a session booking names its session: ui_recreation_participating),
    /// else their social standing in the currently active faction (Manageable.GetCharaSocialStandingName). "" when neither.
    /// </summary>
    [JsonIgnore] public string CurrentActiveFactionName
    {
        get
        {
            var booking = FactionManager.GetEffectiveBooking();
            string activity = booking == null ? "" : booking.DisplayName;
            if (!string.IsNullOrEmpty(activity))
                return LocalizeDictionary.QueryThenParse("ui_recreation_participating").Replace("$activity$", activity);
            var faction = FactionManager.CurrentlyActiveFaction;
            return faction == null ? "" : faction.GetCharaSocialStandingName(this);
        }
    }


    public void InitializeFaction(Manageable m, bool isManager)
    {
        string initFactionID = (m == null ? "" : m.ID);
        this.FactionManager.ReEstablishParentData(this);
        this.FactionManager.SetHomeFaction(initFactionID, isManager? FactionUtility.MemberType_Manager : FactionUtility.MemberType_Member);
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

    [JsonProperty]
    protected int? age = null;

    [JsonIgnore] public int Age { get { return isDefaultAge || age.Value <= 0 ? 22 : age.Value; } }
    [JsonIgnore] public bool isDefaultAge { get { return age is null; } }
    [JsonProperty] private bool noAging = false;

    [JsonProperty] private DateTime birthday;
    [JsonIgnore] public DateTime Birthday { get { return birthday; } set { birthday = value; } }

    public void TryGetJob(int currentHour, List<string> s)
    {
        I_IsJobGiver currentJobFaction = FactionManager.CurrentActiveParty != null ? FactionManager.CurrentActiveParty : FactionManager.CurrentlyActiveFaction;
        I_IsJobGiver currentLocaleFaction = FactionManager.CurrentActiveParty != null ? FactionManager.CurrentActiveParty : FactionManager.CurrentLocaleFaction;

        bool resetJob = true;

        if (RefID == 0)
        {
            if (CurrentJob != null && CurrentJob.hasActorCompletedJob(0)) ChangeCurrentJob(null);
            return;
        }
        if (fallbackPoolID != "" && FallbackWorkerManager.TryEnterDormancy(this))
        {
            if (s != null) s.Add(FirstName + ": fallback worker back in pool, going dormant");
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
            if (CurrentJob == null)
            {
                if (log) ss += $" || released from previous job";
                resetJob = true;
            }
            else if (!hasPackage)
            {
                if (log) ss += $" || Job cannot give valid package, releasing from |{CurrentJob.RefID}|";
                ChangeCurrentJob(null);
                resetJob = true;
            }
            else
            {
                resetJob = false;
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

        //var jobpost = GetJobPost(currentHour);
        //COM currentScheduleCOM = jobpost == null ? null : jobpost.getRandCOM;
      

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

            if (tryFinishJob)
            {   // current action would otherwise be left to finish uninterrupted - but as long as it isn't
                // itself job-tagged (work always finishes first), break out early whenever it's time to
                // start traveling for whatever's scheduled next hour, instead of coasting to completion.
                bool currentIsJobAction = p.Exists(pp => pp.ComTags.Contains("job"));
                if (!currentIsJobAction && FactionUtility.ShouldTravelForNextHourSchedule(this, currentLocaleFaction, currentHour, out var nextFaction, out var travelMinutes))
                {
                    tryFinishJob = false;
                    resetJob = true;
                    if (log) ss += $"| next hour's schedule at {nextFaction.FactionDisplayName} needs ~{travelMinutes}min world travel - interrupting current non-job action |";
                }
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

        if (s != null) s.Add(ss);

        if (isTemporaryActor)
        {
            if (s != null) s.Add("isTemporaryActor, breaking");
            return;
        }
        else if (currentJobFaction == null && currentLocaleFaction == null)
        {
            if (s != null) s.Add("no faction, breaking");
            return;
        }
        else if (resetJob && this.Relationships != null)
        {
            this.Relationships.Personality.Behavior.TryGetJob(this, currentJobFaction, currentLocaleFaction, resetJob, currentHour, s);
        }

        // Redress check
        /*
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
        }*/


        /*
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
        }*/

        // temporarily disable eat cuz need to restrict food hours in faction management
        /*
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
        }*/

        /*
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
        }*/


        /*
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
        }*/
        /*
        if (shouldRest && TryFindNonJobByTag(resetJob, "rest", currentLocaleFaction, currentHour, ref ss, log, s, new NonJobSearchWrapper(false, true, true)))
        {
            if (s != null) s.Add(ss);
            return;
        }*/
        /*
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


        }*/

        /*
        if (Jail != null && Jail.ownerJob != null)
        {
            ChangeCurrentJob(Jail.ownerJob, "", "rest");
            return;
        }*/
        /*
        else if (TryFindNonJobByTag(resetJob, "recreation", currentJobFaction, currentHour, ref ss, log, s, new NonJobSearchWrapper(true, false, true)))
        {
            if (s != null) s.Add(ss);
            return;
        }*/
        // if still no job, set to look for recreation
        // need to find : is there location restriction ? search currently at ?
        // include search : character currently at + home faction
        /*
    else if (TryFindNonJobByTag(resetJob, "rest", currentLocaleFaction, currentHour, ref ss, debugLog, s, new NonJobSearchWrapper(false, true, false)))
    {
        if (s != null) s.Add(ss);
        return;
    }*/

    }


    [JsonIgnore] public bool canEat { get {
            return this.hasStatKeyword("hunger") && this.Stats.GetStatValue("stats_derived_foodConsumption") >= 1 && timeSinceLastEat > 3; } }

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

    /// <summary>
    /// Allow sleep but respect work schedule
    /// </summary>
    bool canSleep
    {
        get
        {
            if (this.Stats.GetStatusSeverityByStringMatch("chara_status_sleep_deprived") > 0) return true;
            if (this.Stats.Fatigued) return true;
            if (HasForceSleepTag) return true;
            return (hasSleepNeed && Stats.SleepHours > 0 && timeSinceLastSleep > Stats.SleepHours / 2);
        }
    }

    /// <summary>
    /// Allow sleep regardless of current work schedules
    /// </summary>
    bool forceSleep
    {
        get
        {
            // Already unconscious (any cause) - let them lie down regardless of schedule/time,
            // otherwise an unconscious player can never issue any command, including sleep,
            // and since time only advances on player command execution, that's a permanent hardlock.
            if (this.Stats.isConsciousnessUnconscious) return true;
            var induced = this.Stats.FindStatusByExactID("chara_status_inducedSleep");
            if (induced != null && induced.SeverityDisplayable) return true;
            if (HasForceSleepTag) return true;
            return false;
        }
    }

    /// <summary>Any active status (regular or EX) whose current variant carries the forceSleep tag.</summary>
    bool HasForceSleepTag { get { return Stats.hasStatusTag(StatsUtility.Stat_Tag_ForceSleep) || Stats.hasStatusEXTag(StatsUtility.Stat_Tag_ForceSleep); } }

    [JsonIgnore] public bool shouldSleep { get
        {
            if (forceSleep) return true;
            // active labor: stay on bed rest instead of leaving for scheduled sleep (forced sleep above still applies)
            if (ReproductionUtility.IsInIntenseLabor(this)) return false;
            if (!canSleep) return false;

            // check schedule
            //var returnval = hasSleepNeed && sleephours > 0 && timeSinceLastSleep > sleephours;
            //if (!returnval && RefID > 0) Debug.LogError($"{FirstName} cannot sleep! hasSleepNeed {hasSleepNeed}, sleephours > 0 {sleephours > 0}, timeSinceLastSleep {timeSinceLastSleep} > sleephours {sleephours} = {timeSinceLastSleep > sleephours}");
            //return hasSleepNeed && sleephours > 0 && timeSinceLastSleep > sleephours;

            // Party override: while in an ongoing expedition, the party sleep window is authoritative.
            // Personal schedule is bypassed; NPC sleeps iff the party window says so (and canSleep).
            return isScheduleSleep;
        } }

    [JsonIgnore] public bool isScheduleSleep
    {
        get
        {
            var party = this.FactionManager.CurrentParty;

            if (party != null && (party.isActive || party.Room.RoomChara.Contains(this)))
            {
                int currentHour = scr_System_Time.current.getCurrentTime().Hour;
                // if (RefID > 0) Debug.Log($"{FirstName} should sleep in party? {party.SleepHours.Contains(currentHour)} = current {currentHour} in [{String.Join(" ", party.SleepHours)}]");
                return party.SleepHours.Contains(currentHour);
            }
            else if (this.FactionManager.HasSleepSchedule)
            {
                var v = GetJobPost();
                // if (RefID > 0) Debug.Log($"{FirstName} should sleep in schedule? {v != null && v.comIDs.Contains("com_furniture_sleep")}");
                return v != null && v.comIDs.Contains("com_furniture_sleep");
            }
            else return false;
        }
    }


    [JsonIgnore] public bool shouldRest { get
        {
            if (Stats.Stamina != null && Stats.Stamina.ValuePercentile < 0.5) return true;
            if (Stats.Energy != null && Stats.Energy.ValuePercentile < 0.5) return true;
            if (Stats.hasStatusEXTag(StatsUtility.Stat_Tag_ConsReduced)) return true;
            if (Stats.hasStatusTag(StatsUtility.Stat_Tag_NeedRest)) return true;
            return false;
        } }

    /// <summary>
    /// Abnormal condition compelling the character onto bed rest (any labor stage, including waiting for a C-section, or
    /// a status tagged requireBedRest such as post-birth recovery). Drives TryFindBedRestNode and gates
    /// com_furniture_rest_required and hospital admission. Not enforced on the player.
    /// </summary>
    [JsonIgnore] public bool requireBedRest { get
        {
            // not UtilityEX.IsInLabor: it skips Final_RequireHelp, which made a mother waiting for a C-section stop
            // needing bed rest - lost her hospital admission option, and TickLabor flipped her labor between waiting and
            // natural every hour without ever progressing
            if (!scr_System_CentralControl.current.isSafeMode)
                foreach (var egg in ReproductionUtility.AllOvums(this)) if (ReproductionUtility.IsLaborState(egg.State)) return true;
            if (Stats.hasStatusTag(StatsUtility.Stat_Tag_RequireBedRest)) return true;
            if (Stats.hasStatusEXTag(StatsUtility.Stat_Tag_RequireBedRest)) return true;
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

    

    public SkillInstance GetSkill(string skillID)
    {
        foreach (SkillInstance s in this.Skills.Skills)
        {
            if (s.BaseRef == null) continue;
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
        if (regenerateBody) Body.AddMissing();
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
            //var m = Relationships.Personality.GetKOJOMessage("OnTimestopStart", rel, new List<string>(), new List<string>());
            var kol = new KojoCollector(this, "OnTimestopStart");
            kol.LoadRel(rel);
            kol = Relationships.GetKOJOMessage_Suffix(kol, null);
            if (kol != null)
            {
                this.InteractionJob.m.AddKojo(kol);
                Debug.Log($"timestop start {kol.collect.message}");
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
                if (Body.CheckClimax(this.InteractionJob.m))
                {
                    tags.Add("climax");
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
            var pain = Stats.Pain;
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
            Utility.DistinctInPlace(tags);


            if (tags.Count > 1)
            {
                if (!Body.isClimaxing() && Stats.isConsciousnessUnconscious) tags.Add("sleeping_noclimax");
                forbidGreeting = true;
            }

            if (tags.Count > 1) Debug.Log($"Timestop End, {FirstName} tags [{String.Join("|", tags)}]");

            var rel = Relationships.FindRelationshipWith(scr_System_CampaignManager.current.Player);
           // var m = Relationships.Personality.GetKOJOMessage("OnTimestopEnd", rel, tags, new List<string>());



            var kol = new KojoCollector(this, "OnTimestopEnd");
            kol.LoadRel(rel);
            kol.LoadSelfTags(this, tags);
            kol = Relationships.GetKOJOMessage_Suffix(kol, null);
            if (kol != null)
            {
                this.InteractionJob.m.AddKojo(kol);
                Debug.Log($"timestop start {kol.collect.message}");
            }

            this.InteractionJob.NotifyDescriptionsOutOfUpdate();

            // If something notable happened, interrupt the NPC's current job so they recheck what to do
            if (tags.Count > 1 && CurrentJob != null && CurrentJob.CanBeInterrupted)
            {
                ChangeCurrentJob(null);
            }
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

                List<Job> jobLists_refuse = new List<Job>();
                List<Job> jobLists_accept = new List<Job>();

                List<Action> apEventCollector = new List<Action>();

                foreach (var ap in aps)
                {
                    var result = ap.retryRequest(this, "justWokenUp");

                    if (!result)
                    {
                        if (!jobLists_refuse.Contains(ap.job)) jobLists_refuse.Add(ap.job);
                        //Debug.LogError($"Wakeup revalidating ap {ap.targetCOM.displayName} on {this.FirstName}, isDoer {ap.doer.Contains(this)} isReceiver {ap.receiver.Contains(this)}, result {result}");
                        //ap.ExecutePackageOutsideUpdate();
                        ap.ExecutePackageOutsideUpdate(eventCollector: apEventCollector);
                        if (ap.job.isVisibleToPlayer || ap.isOwnRoomVisibleToPlayer) ap.job.CollectLogs(ap);

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
                            if (ap.ListEP.Count < 1) Debug.LogError("Erorr ap ep null");
                        }
                        ap.job.CurrentPackages.Remove(ap);
                    }
                    else
                    {
                        if (!jobLists_accept.Contains(ap.job)) jobLists_accept.Add(ap.job);
                        // Debug.Log($"Wakeup revalidating ap {ap.targetCOM.displayName} on {this.FirstName}, isDoer {ap.doer.Contains(this)} isReceiver {ap.receiver.Contains(this)}, result {result}");
                        if (ap.job is Job_Sex_Group)
                        {
                            //var rel = ap.GetRelationship(this);
                            foreach (var ep in ap.ListEP)
                            {
                                var rel = ep.Relationship(this);
                                if (rel == null) continue;
                                if (accepted.Contains(rel)) continue;

                                accepted.Add(rel);
                                refused.Remove(rel);
                            }
                            if (ap.ListEP.Count < 1) Debug.LogError("Erorr ap ep null");
                        }
                        if (ap.job.isVisibleToPlayer || ap.isOwnRoomVisibleToPlayer) ap.job.CollectLogs(ap);
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

                callbacks.AddRange(apEventCollector);

                var selfTags = new List<string>();
                if (Memory == null || Memory.consciousnessMemory == null)
                {
                    Debug.LogError($"{FirstName} error no consciousness memory");
                }
                else if (!Utility.ListEquals(Memory.consciousnessMemory.lastEquipRefs, EquippedItemRefs))
                {
                    selfTags.Add("removedClothing");
                }
                bool foundCum = false;
                if (Memory != null && Memory.consciousnessMemory != null)
                {
                    foreach (var organ in this.Body.Internals)
                    {
                        if (foundCum) break;
                        if (!organ.canContain) continue;
                        int baseline = Memory.consciousnessMemory.container.TryGetValue(organ.baseID, out var b) ? b : 0;
                        if ((int)organ.CurrentlyContained > baseline && organ.ContainsCum)
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
                    if (message != null && message.message == "") message.message = $"(OnNightAssaultSuccess: {FirstName})";
                    Debug.Log($"({FirstName}) detected night assault accept count {accepted.Count}, random select {randRel.TargetName}, message {message.message}");
                    // Deferred twice on purpose: this closure only runs once ExecuteCallback("onWakeUp")
                    // invokes it (after OnCharaWakeUp's Line entry has already queued its own message via
                    // AddEventCallback), and queuing AddLog here rather than calling it directly keeps our
                    // message behind that already-queued one in eventCallbacks instead of jumping ahead of it.
                    appends.Add(() => scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(message)));
                }
                else if (refused.Count >= 1)
                {
                    var randRel = Utility.GetRandomElement(refused);
                    var message = this.Relationships.Personality.GetKOJOMessage("OnNightAssaultFailure", randRel, selfTags, new List<string>());
                    if (message != null && message.message == "") message.message = $"(OnNightAssaultFailure: {FirstName})";
                    Debug.Log($"({FirstName}) detected night assault refuse count {refused.Count}, random select {randRel.TargetName}, message {message}, apEventCollector {apEventCollector.Count}");
                    appends.Add(() => scr_UpdateHandler.current.AddEventCallback(() => scr_System_CampaignManager.current.AddLog(message)));
                    // add moodlet, reduce relationship -> mod relationship record are from ep. or can we directly inject into updatehandler ?
                    // directly add explog to updatehandler's log
                    var relationship = randRel.Owner.Relationships;
                    var logger = scr_System_CampaignManager.current.isCharaVisibleToPlayer(this.RefID) ? scr_UpdateHandler.current.GetExpLogs() : null;
                    relationship.IncreaseRelationshipWith(randRel.TargetID, RelationshipScoreType.Trust, -30, logger);
                    relationship.IncreaseRelationshipWith(randRel.TargetID, RelationshipScoreType.Fear, 30, logger);


                    memInst.ResetInternal(Memory_Response.Accept, Memory_Attitude.Hate);
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
                else if (jobLists_accept.Count >= 1)
                {
                    // Still legitimately part of a validated AP (e.g. an alternating multi-receiver
                    // position that only builds one EvaluationPackage per round, so this actor simply
                    // wasn't the one it landed on this tick) - stay attached, nothing happened to them
                    // this round that warrants a reaction message either way.
                }
                else
                {
                    // first check last memories if any conscious sex then let it go
                    // NO

                    var rel = this.Relationships.FindRelationshipWith(this);
                    var message = this.Relationships.Personality.GetKOJOMessage("OnWakeUp", rel, selfTags, new List<string>());
                    if (message != null && message.message == "") message.message = $"(OnWakeUp: {FirstName})";
                    //Debug.Log($"({FirstName}) no night assault, message {message}");
                    // add moodlet
                    if (selfTags.Count > 0)
                    {
                        
                        memInst.ResetInternal(Memory_Response.Accept, Memory_Attitude.Hate);
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

                // Only now, after callbacks/appends are fully populated above, actually launch the event -
                // StartEvent forces itself to run synchronously whenever scr_UpdateHandler.current.Updating
                // is true (which it always is here), cascading through OnCharaWakeUp's Line and Branch (and
                // therefore ExecuteCallback("onWakeUp"/"jobCallback")) in this same call. Starting it any
                // earlier means those ExecuteCallback steps run against still-empty lists.
                //scr_UpdateHandler.current.EventHandler.StartEvent(this, "OnCharaWakeUp", "", false);
                wakeupEV.FunctionCalls.Add("jobCallback", callbacks);
                if (this.InteractionJob.isVisibleToPlayer) wakeupEV.FunctionCalls.Add("onWakeUp", appends);
                scr_UpdateHandler.current.EventHandler.StartEvent(wakeupEV, false);

                // if exit job, then removeactor already called endongoingmemory
                // so, if last memory is ended and uncons, then, problem!

                this.Stats.RefreshAllStats();
                Memory.ConsciousnessRegained_End();


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

    private int ComputeSleepWindowMinutes()
    {
        var now = scr_System_Time.current.getCurrentTime();
        int currentHour = now.Hour;
        int currentMinute = now.Minute;
        var party = this.FactionManager.CurrentParty;
        if (party != null && (party.isActive || party.Room.RoomChara.Contains(this)))
        {
            int minutes = 0;
            for (int i = 0; i < Stats.SleepHours; i++)
            {
                if (party.SleepHours.Contains((currentHour + i) % 24))
                    minutes += i == 0 ? (60 - currentMinute) : 60;
                else
                    break;
            }
            return minutes;
        }
        else
        {
            int minutes = 0;
            for (int i = 0; i < Stats.SleepHours; i++)
            {
                int hour = (currentHour + i) % 24;
                int daysLookahead = (currentHour + i) / 24;
                var post = GetJobPost(hour, daysLookahead);
                if (post != null && post.comIDs.Contains("com_furniture_sleep"))
                    minutes += i == 0 ? (60 - currentMinute) : 60;
                else
                    break;
            }
            return minutes;
        }
    }

    public int Sleep()
    {
        var tired = Stats.GetStatusSeverityByStringMatch("chara_status_sleep_deprived");
        int windowMinutes = ComputeSleepWindowMinutes();

        int personalNeedMinutes = tired > 0
            ? (int)(Math.Min(Stats.SleepHours * 60, tired))
            : (int)(Stats.SleepHours * 60);

        int sleepHour;
        if (scr_System_CampaignManager.current.Player == this && windowMinutes == 0 && scr_System_CampaignManager.current.DebugMode)
        {
            // Player sleeping outside their assigned sleep hour (e.g. DEBUG-triggered):
            // skip a full night instead of only covering the accrued deprivation debt,
            // so a scheduled sleep that gets interrupted can still resume in sync with the party.
            sleepHour = Stats.SleepHours * 60;
        }
        else
        {
            sleepHour = windowMinutes > 0 ? Math.Min(personalNeedMinutes, windowMinutes) : personalNeedMinutes;
        }

        // Player only: while a forceSleep status (e.g. postpartum/post-op recovery) is active, sleep straight
        // through its remaining duration instead of being capped at the usual sleep need. NPCs don't need
        // this - their sleep node keeps re-sleeping them while shouldSleep stays true.
        if (scr_System_CampaignManager.current.Player == this)
        {
            int forceSleepMinutes = 0;
            foreach (var status in Stats.StatusInstances)
            {
                if (status.duration > 0 && status.Tags.Contains(StatsUtility.Stat_Tag_ForceSleep))
                    forceSleepMinutes = Math.Max(forceSleepMinutes, status.duration);
            }
            sleepHour = Math.Max(sleepHour, forceSleepMinutes);
        }
        ScheduledSleepMissingMinutes = Math.Max(0, personalNeedMinutes - sleepHour);

        Stats.AddOrModStatus("chara_status_sleeping", Stats.SleepDepth, sleepHour);
        Stats.RemoveStatusByStringMatch("chara_status_sleep_deprived");

        var memInst2 = new MemInstance(new List<int>() { }, new List<string>(), "", -1, -1, true, Memory_Response.Accept, Memory_Attitude.None, LocalizeDictionary.QueryThenParse("ui_entry_memory_sleep_begin"));
        this.Memory.AddEntry(memInst2, new List<string>() { "forbidMerge" });

        Memory.ConsciousnessLost_TryStart();

        if (scr_System_CampaignManager.current.Player == this)
        {
            scr_System_CampaignManager.current.party.DisbandParty();
        }
        //Debug.Log($"{FirstName} sleep!");
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
        if (scr_System_CentralControl.current.isSafeMode) return false;

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
    public bool NeedUndress(COM_Requirements req, bool isdoer)
    {
        if (scr_System_CentralControl.current.isSafeMode) return false;
        var charareq = isdoer ? req.requirement.req_Doers : req.requirement.req_Receivers;

        
        if (charareq.requireUndressedTags.Count > 0)
        {
            if (Body.HasEquipByFilter(charareq.requireUndressedTags, charareq.clothingRequirement, charareq.minRevealingScore)) return true;
        }
        else if (charareq.clothingRequirement < BodyEquipLayer.Outer && NeedUndress(charareq.clothingRequirement, Revealing.Erotic)) return true;

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

    [JsonIgnore] public bool Deleted = false;

    /// <summary>
    /// Call when destroy
    /// </summary>
    public void DisposeInternal()
    {
        Deleted = true;
        RemoveObservers();
        if (PortraitManager != null) PortraitManager.ClearInternal();
    }

    public void PostReloadUpdate()
    {
        if (this.Relationships != null) Relationships.PostReloadUpdate();
        if (this.factionManager != null) factionManager.PostReloadUpdate_Recreation();
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

        if (this.Memory != null) Memory.UpdateBlacklist();

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
        ReEstablishObservers_HourDay();
        if (!isDormant) ReEstablishObservers_Minute();
    }

    protected void ReEstablishObservers()
    {
        ReEstablishObservers_HourDay();
        ReEstablishObservers_Minute();
    }

    /// <summary>Hour/day ticks - kept subscribed while dormant.</summary>
    protected void ReEstablishObservers_HourDay()
    {
        if (this.RefID == -1) return;

        scr_System_Time.current.Observer_globalTime_Hours += Observer_GlobalHour;
        scr_System_Time.current.Observer_globalTime_Day += Observer_GlobalDay;
        scr_System_Time.current.Observer_globalTime_Day += Observer_GlobalDay_0;
        scr_System_Time.current.Observer_globalTime_Day += Observer_GlobalDay_3;
    }

    /// <summary>Per-minute ticks - dropped while dormant, caught up in one go by EndDormantState.</summary>
    protected void ReEstablishObservers_Minute()
    {
        if (this.RefID == -1) return;

        scr_System_Time.current.Observer_globalTime += Observer_GlobalMinute;
        scr_System_Time.current.Observer_globalTime_5min += Observer_GlobalMinute5;

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
        RemoveObservers_HourDay();
        RemoveObservers_Minute();
    }

    protected void RemoveObservers_HourDay()
    {
        if (this.RefID == -1) return;

        scr_System_Time.current.Observer_globalTime_Hours -= Observer_GlobalHour;
        scr_System_Time.current.Observer_globalTime_Day -= Observer_GlobalDay;
        scr_System_Time.current.Observer_globalTime_Day -= Observer_GlobalDay_0;
        scr_System_Time.current.Observer_globalTime_Day -= Observer_GlobalDay_3;
    }

    protected void RemoveObservers_Minute()
    {
        if (this.RefID == -1) return;

        scr_System_Time.current.Observer_globalTime -= Observer_GlobalMinute;
        scr_System_Time.current.Observer_globalTime_5min -= Observer_GlobalMinute5;

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

    public string baseTemplateID = "";

    [JsonIgnore] public bool Debug_ForceDeepSleep = false;


    public bool DeflateInternal(EventInstance collector, string deflateStringkey, string kojoStringKey, string memoryStringKey = "", bool fullDeflate = false, bool deleteObject = true, string tagFilter = "")
    {
        // 1. fetch every applicable bodypart (matching tagFilter, and actually deflate-able for the requested mode)
        var internals = this.Body.Internals;
        List<BodyInternal_Instance> candidates = null;
        for (int i = 0; i < internals.Count; i++)
        {
            var candidate = internals[i];
            if (tagFilter != "" && !candidate.hasTag(tagFilter)) continue;
            if (fullDeflate ? !candidate.canFullyDeflate : !candidate.canDeflate) continue;
            if (candidates == null) candidates = new List<BodyInternal_Instance>();
            candidates.Add(candidate);
        }
        if (candidates == null) return false;

        bool anyDeflated = false;
        List<string> deflateMessages = null;
        List<string> memoryMessages = null;
        // merge deflated amount across bodyparts that share the same deflateEventID, per-ID, for a single combined kojo query
        Dictionary<string, float> kojoAmountByEventID = null;

        for (int ci = 0; ci < candidates.Count; ci++)
        {
            var part = candidates[ci];

            // 1.1 deflate this part; each returned item's own amount is exactly what left the body
            // (fully-removed items keep their original amount, partially-reduced items are split-off
            // instances holding just the removed delta — see BodyInternal_Instance.Deflate). What
            // happens to those items next (delete vs. drop in the room) is this caller's call, not the
            // body part's.
            var expelledItems = part.Deflate(fullDeflate);
            if (expelledItems == null || expelledItems.Count < 1) continue;

            float partDeflatedAmount = 0f;
            var itemTexts = new List<string>();
            for (int i = 0; i < expelledItems.Count; i++)
            {
                var expelled = expelledItems[i];
                var ing = expelled.GetComp_Ingestible();
                if (ing != null) partDeflatedAmount += ing.amount;
                itemTexts.Add(expelled.Print());

                if (deleteObject) scr_System_CampaignManager.current.Unregister(expelled);
                else scr_System_CampaignManager.current.Map.FindRoomByChara(this.RefID).AddItem(expelled);
            }
            if (partDeflatedAmount <= 0f) continue;
            anyDeflated = true;

            // 1.2 collect this part's deflation message: "$name$从$bodypart$排出了$items$" (zh-cn) / "$name$ expelled $items$ from $bodypart$" (en-us)
            // name is included so this reads unambiguously when multiple actors are in the same scene
            if (deflateMessages == null) deflateMessages = new List<string>();
            deflateMessages.Add(LocalizeDictionary.QueryThenParse("deflate_message")
                .Replace("$name$", this.FirstName)
                .Replace("$bodypart$", part.DisplayName)
                .Replace("$items$", String.Join(" ", itemTexts)));

            // 1.3 same message minus $name$, meant to be logged into this character's own memory
            // (a memory entry is already scoped to its owner, so restating the name would be redundant)
            if (memoryStringKey != "")
            {
                if (memoryMessages == null) memoryMessages = new List<string>();
                memoryMessages.Add(LocalizeDictionary.QueryThenParse("deflate_message_memory")
                    .Replace("$bodypart$", part.DisplayName)
                    .Replace("$items$", String.Join(" ", itemTexts)));
            }

            // 2. group this part's deflated amount under its deflateEventID (if any) for the kojo step below.
            // parts with no deflateEventID contribute their message above but are skipped for kojo collection.
            if (part.Base.deflateEventID != "")
            {
                if (kojoAmountByEventID == null) kojoAmountByEventID = new Dictionary<string, float>();
                kojoAmountByEventID.TryGetValue(part.Base.deflateEventID, out var existing);
                kojoAmountByEventID[part.Base.deflateEventID] = existing + partDeflatedAmount;
            }
        }

        if (!anyDeflated) return false;

        if (collector != null && deflateStringkey != "" && deflateMessages != null)
            collector.AppendStrings[deflateStringkey] = deflateMessages;

        if (collector != null && memoryStringKey != "" && memoryMessages != null)
        {
            // accumulate rather than overwrite: callers (e.g. a multi-part deflate event) commonly reuse the
            // same memoryStringKey across several DeflateInternal calls and only read it once, at the end
            if (collector.AppendStrings.TryGetValue(memoryStringKey, out var existingMemoryMessages)) existingMemoryMessages.AddRange(memoryMessages);
            else collector.AppendStrings[memoryStringKey] = memoryMessages;
        }

        // 2.1-2.3 kojo collect: one query per distinct deflateEventID (same-ID parts already merged above),
        // combining every group's output into a single kojoStringKey entry
        if (collector != null && kojoStringKey != "" && kojoAmountByEventID != null)
        {
            var rel = this.Relationships.FindRelationshipWith(this);
            List<string> kojoTexts = null;
            foreach (var kvp in kojoAmountByEventID)
            {
                this.Relationships.SetKojoVariable(true, rel, kvp.Key, (int)kvp.Value);
                var kojoResult = this.Relationships.Personality.GetKOJOMessage(kvp.Key, rel, null, null);
                if (kojoResult == null) continue;
                if (kojoTexts == null) kojoTexts = new List<string>();
                kojoResult.DumpMessage(kojoTexts);
            }
            if (kojoTexts != null) collector.AppendStrings[kojoStringKey] = kojoTexts;
        }

        // return true if any deflation happened
        return true;
    }

    public bool SwallowInternal(bool fullDeflate, string bodyTag = "", ExperienceLog m = null)
    {
        var internals = this.Body.Internals;
        bool any = false;
        for (int i = 0; i < internals.Count; i++)
        {
            var part = internals[i];
            if (bodyTag != "" && !part.hasTag(bodyTag)) continue;
            if (!(fullDeflate ? part.canFullyDeflate : part.canDeflate)) continue; // nothing swallowable here right now

            var target = (part.overflowOutTag == "" || part.overflowOutTag == "ext")
                ? null : this.Body.GetRandomInternalWithTag(part.overflowOutTag);
            if (target == null) continue;

            if (part.TransferContentTo(target, fullDeflate, m)) any = true;
        }
        return any;
    }
}

public class Character_BaseID_Index
{

}



public class Character_Base_Index : I_IndexMergeable, I_IndexHasID, I_RemoveNonExisting, I_RemoveNSFW
{
    public List<Character_SerializableBase> baseCharacters = new List<Character_SerializableBase>();

    Dictionary<string, CharaTrainableTemplate> templates = new Dictionary<string, CharaTrainableTemplate>();
    Dictionary<string, CharaSafeTemplate> templatesS = new Dictionary<string, CharaSafeTemplate>();

    public List<CharaTemplateGenerator> generators = new List<CharaTemplateGenerator>();

    Dictionary<string, CharaTemplateGenerator> ID_generators = new Dictionary<string, CharaTemplateGenerator>();
    
    public Dictionary<NameCulture, NameGenerator> names = new Dictionary<NameCulture, NameGenerator>();

    public CharaTrainableTemplate GetTemplateByID(string id) { 
        if (templates.TryGetValue(id, out var value))
        {
            return value;
        }
        else
        {
            //if (id != "") Debug.LogError($"Error GetTemplateByID {id}");
            return null;
        }
    }
    public CharaSafeTemplate GetTemplateSafeByID(string id)
    {
        if (templatesS.TryGetValue(id, out var value))
        {
            return value;
        }
        else
        {
            //Debug.LogError($"Error GetTemplateSafeByID {id}, entries in dict {templatesS.Count}");
            return null;
        }
    }

    public CharaTemplateGenerator GetGeneratorByID(string id)
    {
        if (ID_generators.TryGetValue(id, out var value)) return value;
        foreach(var vv in generators)
        {
            if (vv.ID == id)
            {
                ID_generators.Add(vv.ID, vv);
                return vv;
            }
        }
        return null;
    }

    public void GenerateNamesFor(Character_Trainable c, Humanoid_GenderAppearance gender, NameCulture firstname, NameCulture middleName, NameCulture lastname, string displayFormat = "")
    {
        var fst = firstname == NameCulture.none || !names.ContainsKey(firstname) ? null : names[firstname];
        //var mdl = middleName == NameCulture.none || !names.ContainsKey(middleName) ? null : names[middleName];
        var lst = lastname == NameCulture.none || !names.ContainsKey(lastname) ? null : names[lastname];

        if (fst != null)
        {
            var list_fst = gender == Humanoid_GenderAppearance.Female ? fst.firstname_female : fst.firstname_male;
            c.FirstName = Utility.GetRandomElement(list_fst);
        }
        if (lst != null)
        {
            var list_lst = gender == Humanoid_GenderAppearance.Female ? lst.lastname_female : lst.lastname_male;
            c.LastName = Utility.GetRandomElement(list_lst);
        }
        else
        {
            c.LastName = "";
        }


        if (displayFormat != "") c.nameDisplayFormat = displayFormat;

    }
    public void SetTemplate(string id, CharaTrainableTemplate t, bool log = false)
    {
        templates[id] = t;
        if (log) Debug.Log($"setting custom template [{id}], value {(t == null? "null" : "exist")}");
    }
    public void SetTemplateSafe(string id, CharaSafeTemplate t, bool log = false)
    {
        templatesS[id] = t;
        if (log) Debug.Log($"setting custom safe template [{id}], value {(t == null ? "null" : "exist")}");
    }

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

        foreach (var kvp in l.names)
        {
            if (this.names.ContainsKey(kvp.Key)) this.names[kvp.Key].MergeWith(kvp.Value);
            else this.names.Add(kvp.Key, kvp.Value);
        }

        this.generators.AddRange(l.generators);

        foreach (var kvp in l.templates) if (!templates.ContainsKey(kvp.Key)) templates[kvp.Key] = kvp.Value;
        foreach (var kvp in l.templatesS) if (!templatesS.ContainsKey(kvp.Key)) templatesS[kvp.Key] = kvp.Value;



    }

    public void LateInitialize()
    {

    }

    Dictionary <string, Character_SerializableBase> ID_Dictionary = new Dictionary<string, Character_SerializableBase>();
    public void RegisterAllID(List<string> s)
    {
        if (s != null) s.Add("Character_Base_Index : registering ID with list length [" + baseCharacters.Count + "]");
        foreach (Character_SerializableBase o in this.baseCharacters)
        {
            if (o.baseID == "") continue;
            if (!ID_Dictionary.ContainsKey(o.baseID)) ID_Dictionary[o.baseID] = o;
            else Debug.LogError($"Error registering allID in Character_Base_Index, {o.baseID} already registered");

            // templates/templatesS are lookup caches and are not serialized; rebuild them here
            // from each character's own embedded Template field, which IS serialized as part of baseCharacters.
            if (o is Character_SerializableSafe safe && safe.Template != null) SetTemplateSafe(o.baseID, safe.Template);
            else if (o is Character_SerializableTrainable trainable && trainable.Template != null) SetTemplate(o.baseID, trainable.Template);
        }

        foreach( var gen in this.generators)
        {
            if (string.IsNullOrEmpty(gen.ID)) continue;
            if (!ID_generators.TryAdd(gen.ID, gen)) Debug.Log($"failed to add ID_generators id [{gen.ID}] due to duplicate");
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
        // resolve portraits from the template instead of saving a full copy per character
        if (template.Portrait != null) chara.PortraitManager.InitFromTemplate(id);
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
            if (c.Portrait == null) continue;
            foreach(var p in c.Portrait.portraitPriorityList)
            {
                p.Variants = null;
            }
        }
    }
}
