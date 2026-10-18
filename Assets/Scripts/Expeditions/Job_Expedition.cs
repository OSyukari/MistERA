using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
using System.Linq;

public class ExpeditionMessageEntry
{
    public string EventDescription = "";
    public List<string> Tooltips = new List<string>();
    public List<string> Tags = new List<string>();
    public List<int> Characters = new List<int>();

    [JsonIgnore]
    public string FullDescription
    {
        get
        {
            var names = new List<string>();
            foreach (var i in Characters) names.Add(scr_System_CampaignManager.current.FindInstanceByID(i).CallName);
            var newstring = EventDescription.Replace("$names$", String.Join(", ", names));
            return newstring;

        }
    }

    public SerializableEventPackage unresolved = null;
    public string resolveEventName = "";
    public string resolveMessage = "";
    public List<string> resolveTooltips = new List<string>();

    public void NotifyCharaExit(int refID)
    {
        if (unresolved == null || unresolved.isResolved) return;
        // the self release keeps its victims while they are off the job (asleep/eating) - releasing needs no job, and
        // NotifyCharaReturn would only restore the "party" key, losing "victims". Off-time events were banked while
        // their targets were off the job already, and keep them the same way.
        if (unresolved.isSelfRelease || unresolved.keepTargetsOffJob) return;
        if (Characters.Remove(refID))
        {
            //Debug.LogError($"NotifyCharaExit {scr_System_CampaignManager.current.FindInstanceByID(refID).CallName} in {FullDescription} original {EventDescription}");
            unresolved.NotifyCharaExit(refID);
        }
    }
    public bool NotifyCharaReturn(int refID)
    {
        if (unresolved == null || unresolved.isResolved) return false;
        if (!Characters.Contains(refID))
        {
            Characters.Add(refID);
        }
        //Debug.LogError($"NotifyCharaReturn {refID}");
        return true;
    }
}


public class SerializableEventPackage
{
/* 
 * this package will remain dormant until player click
 * this package will only preserve 
 */
    public string eventID = "";
    public string eventLabel = "";
    public Dictionary<string, List<int>> Targets = new Dictionary<string, List<int>>();
    public Dictionary<string, List<string>> AppendStrings = new Dictionary<string, List<string>>();
    public bool overrideTargetScope = false;
    public List<EventScope_Target> targetScopes = new List<EventScope_Target>();
    public bool overrideTargetGen = false;
    public List<Event.GenerationParameters> targetGens = new List<Event.GenerationParameters>();
    [JsonIgnore]
    public bool isValid
    { get
        {
            return eventID != "";
        }
    }

    /// <summary>
    /// Unique only for rescue events
    /// </summary>
    public int rescueJobID = -1;

    /// <summary>
    /// Expedition.selfReleaseEventID entry of an MIA party (Job_Expedition.TickSelfRelease): its button stays blocked while
    /// any other banked entry of the job is unresolved, and its targets survive the victims leaving the job.
    /// </summary>
    public bool isSelfRelease = false;

    /// <summary>
    /// Banked by an off-time event (Job_Expedition.TickOffTimeEvents): its targets are kept while they are off the job
    /// (asleep/eating), so the entry stays clickable then. Waking them is up to the event (DisruptSleep).
    /// </summary>
    public bool keepTargetsOffJob = false;

    [JsonIgnore]
    public string DisplayName { get { return LocalizeDictionary.QueryThenParse(eventID); } }
    public void NotifyCharaExit(int refID)
    {
        foreach(var i in Targets)
        {
            i.Value.Remove(refID);
        }
    }
    public bool isResolved = false;

}


public class Job_Expedition : Job
{
    [JsonIgnore]
    public override string DisplayName
    {
        get
        {
            return LocalizeDictionary.QueryThenParse($"ui_management_expeditionJob_{this.status}")
                    .Replace("$expName$", this.Expedition == null ? "-" : Expedition.Base.DisplayName);
        }
    }
    public override List<string> JobTypeTag(Character_Trainable c)
    {
        var results = new List<string>();
        if (!this.actorRefID.Contains(c.RefID)) return results;
        results.Add("expedition");
        return results;
    }
    [JsonIgnore]
    public string RemainingTime
    {
        get
        {
            if (this.RemainingMinutes < 0) return "";
            else return LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_remainingTime")
                    .Replace("$time$", $"{((double)this.RemainingMinutes / 60.0).ToString("F1")}");
        }
    }
    [JsonIgnore]
    public string RemainingProgress
    {
        get
        {
            if (Expedition == null || Expedition.ExploreRate < 0 || Expedition.Base.MaxExplorationRate <= 0) return "";
            
            return LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_remainingProgress")
                    .Replace("$rate$", ((double)(Expedition.Base.MaxExplorationRate - Expedition.ExploreRate) / Expedition.Base.MaxExplorationRate).ToString("P1"));
        }
    }
    [JsonIgnore]
    public string DisplayName_EndJob
    {
        get
        {
            if (this.status == ExpeditionStatus.returning)
            {
                if (canReturn) LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_resolving");
                else return LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_requireManualresolve");
            }
            if (isActive) return LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_abort");
            return LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_cancel");

        }
    }

    public override void OnAfterDeserialize()
    {
        base.OnAfterDeserialize();
    }

    [JsonIgnore] public override bool CanBeInterrupted { get { return true; } }

    public SortedDictionary<DateTime, List<ExpeditionMessageEntry>> ExpeditionResults = new SortedDictionary<DateTime, List<ExpeditionMessageEntry>>();

    protected void TryMerge(DateTime time, ExpeditionMessageEntry ExpeditionResult)
    {
        if (ExpeditionResults.ContainsKey(time))
        {
            bool merged = false;
            foreach (var i in ExpeditionResults[time])
            {
                if (i.EventDescription == ExpeditionResult.EventDescription)
                {
                    i.Tags.AddRange(ExpeditionResult.Tags);
                    i.Tags = i.Tags.Distinct().ToList();
                    i.Characters.AddRange(ExpeditionResult.Characters);
                    i.Characters = i.Characters.Distinct().ToList();
                    merged = true;
                }
            }
            if (!merged) ExpeditionResults[time].Add( ExpeditionResult );
        }
        else
        {
            ExpeditionResults.Add(scr_System_Time.current.getCurrentTime(), new List<ExpeditionMessageEntry>() { ExpeditionResult });
        }
    }

    public override void AddActor(int charaRef, string priorityCOMID = "", string priorityCOMTag = "")
    {
        base.AddActor(charaRef, priorityCOMID, priorityCOMTag);

        if (isActive && hasUnresolvedResult)
        {
            foreach (var i in ExpeditionResults)
            {
                foreach (var result in i.Value)
                {
                    if (!result.NotifyCharaReturn(charaRef)) continue;
                    if (result.unresolved.Targets.TryGetValue("party", out var partyList))
                    {
                        if (!partyList.Contains(charaRef))   partyList.Add(charaRef);
                    }
                    else result.unresolved.Targets.Add("party", new List<int> { charaRef });

                    string key = "";

                    switch (FactionOwner_Party.GetTeamComp(charaRef))
                    {
                        case Manageable_Party.PartyComposition.frontline:
                            key = "teamA_frontline";
                            break;
                        case Manageable_Party.PartyComposition.backline:
                            key = "teamA_backline";
                            break;
                        default: break;
                    }
                    
                    if (key != "")
                    {
                        if (result.unresolved.Targets.TryGetValue(key, out var formation))
                        {
                            if (!formation.Contains(charaRef)) formation.Add(charaRef);
                        }
                        else result.unresolved.Targets.Add(key, new List<int> { charaRef });
                    }
                }
            }
        }

        UpdateStatus(-1, -1, false);
    }
    public void DumpLogInto(Job_Expedition other)
    {
        // re-kidnapped into the same party: nothing to move (and adding into the dictionary being iterated would throw)
        if (other == null || other == this) return;
        foreach(var i in this.ExpeditionResults)
        {
            // both logs can hold the same timestamp (e.g. this party's MIA notice and the new party's capture line)
            if (other.ExpeditionResults.TryGetValue(i.Key, out var existing)) existing.AddRange(i.Value);
            else other.ExpeditionResults.Add(i.Key, i.Value);
        }
        this.ExpeditionResults.Clear();
    }

    public ExpeditionMessageEntry AddResult(string s, List<string> tags, List<Character_Trainable> chara, bool registerMemory = false)
    {
        var charaRefs = new List<int>();
        foreach (var c in chara)
        {
            charaRefs.Add(c.RefID);
        }

        return AddResult(s, tags, charaRefs, registerMemory);
    }

    public ExpeditionMessageEntry AddResult(string s, List<string> tags, List<int> chara, bool registerMemory = false)
    {
        var result = new ExpeditionMessageEntry();
        result.EventDescription = s;
        result.Tags = tags;
        result.Characters = chara;

        TryMerge(scr_System_Time.current.getCurrentTime(), result);
        return result;
    }

    public enum ExpeditionStatus
    {
        inactive,
        queued,
        gathering,
        active,
        resting,
        returning
    }

    [JsonIgnore]
    public List<string> ActorNames
    {
        get
        {

                var _cachedNames = new List<string>();
                foreach (var i in this.actorRefID) _cachedNames.Add(scr_System_CampaignManager.current.FindInstanceByID(i).CallName);

            return _cachedNames;
        }
    }
    [JsonIgnore]
    public int ActorCount
    { get
        {
            return this.actorRefID.Count;
        } }


    public ExpeditionStatus status = ExpeditionStatus.inactive;

    public string statusTooltip = "";

    public void UpdateStatus(int currentHour = -1, int currentMinute = -1, bool tickPackage = true)
    {
        if (currentHour == -1 || currentMinute == -1)
        {
            var cur = scr_System_Time.current.getCurrentTime();
            currentHour = cur.Hour;
            currentMinute = cur.Minute;
        }

        if (tickPackage) this.packageCooldown = Math.Max(this.packageCooldown - 1, 0);
        if (!this.FactionOwner_Party.isPlayerFaction)
        {
            TickSelfRelease(tickPackage);
            TickOffTimeEvents(tickPackage);
            return;
        }
        bool begin = false;
        if (!ExpeditionActive || Expedition == null)
        {
            status = ExpeditionStatus.inactive;
            statusTooltip = "party is inactive";
        }
        else if (status == ExpeditionStatus.inactive)
        {
            status = ExpeditionStatus.queued;
            statusTooltip = "party expedition queued";
        }

        if (status != ExpeditionStatus.inactive)
        {
            if (currentHour == startHour)
            {
                if (status == ExpeditionStatus.queued && FactionOwner_Party.TryStartExpedition())
                {
                    AddResult(LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_start"), new List<string>(), new List<int>());
                    properExitRefs.Clear();
                    status = ExpeditionStatus.gathering;
                    statusTooltip = "gathering expedition members";
                    begin = true;
                }
            }
            else if (status == ExpeditionStatus.gathering)
            {
                status = ExpeditionStatus.queued;
                statusTooltip = "gathering expedition members failed, retry tomorrow";
                AddResult(LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_cancel_missing"), new List<string>(), new List<int> {  });
            }
        }

        if (status == ExpeditionStatus.gathering)
        {
            bool wait = false;
            foreach(var c in FactionOwner.ManagedChara)
            {
                if (scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID) != this.FactionOwner_Party.MainExit) wait = true;
                else if (ShouldRest(c)) wait = true;
            }
            if (!wait)
            {

                status = ExpeditionStatus.active;
                AddResult(LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_start_final"), new List<string>(), new List<int>());
                StartCooldown();
                foreach (var cref in this.actorRefID)
                {
                    var c = scr_System_CampaignManager.current.FindInstanceByID(cref);
                    var memstr = LocalizeDictionary.QueryThenParse("exp_event_departure_memory").Replace("$loc$", this.Expedition.Base.DisplayName);
                    var newMem = new MemInstance(new List<int>(), new List<string>(), "", -1, -1, true, Memory_Response.Accept, Memory_Attitude.Neutral, memstr);

                    var entry = c.Memory.AddEntry(newMem, new List<string>() { "forbidMerge" });
                    //entry.entryDescription = memstr;
                    entry.disableRoomName = true;
                }
            }
        }


        if (begin)
        {
            bool success = true;
            foreach (var i in this.FactionOwner.ManagedChara)
            {
                success = success && i.FactionManager.AddToParty(this.FactionOwner, FactionUtility.MemberType_Member, false);
                if (!success) break;
            }
            if (!success)
            {
                foreach (var i in this.FactionOwner.ManagedChara) i.FactionManager.RemoveFromParty(this.FactionOwner);
                status = ExpeditionStatus.queued;
            }
        }
        else if (this.status == ExpeditionStatus.active || this.status == ExpeditionStatus.resting)
        {
            var previous = this.status;
            if (this.FactionOwner_Party.OwnerFaction.mealHours.Contains(currentHour)
                    || ((this.FactionOwner_Party.AllowPassNight || this.FactionOwner_Party.BaseDuration > 23)
                            && (this.FactionOwner_Party.SleepHours.Contains(currentHour) || !this.FactionOwner_Party.OwnerFaction.IsActiveHour(currentHour)))) this.status = ExpeditionStatus.resting;
            else this.status = ExpeditionStatus.active;

            // rest begins: members put away the party-gear layers not kept for rest (OutfitSet.restKeepLayers); Redress
            // leaves them off while resting and puts them back once the party is active again
            if (previous != ExpeditionStatus.resting && this.status == ExpeditionStatus.resting)
            {
                foreach (var c in this.FactionOwner.ManagedChara) c?.Wardrobe?.OnPartyRestStart();
            }
        }

        if (this.status == ExpeditionStatus.active)
        {
            if (RemainingMinutes != -1)
            {
                if (RemainingMinutes > 0 && tickPackage) RemainingMinutes = Math.Max(0, RemainingMinutes - 1);
                if (RemainingMinutes == 0) this.status = ExpeditionStatus.returning;
            }
            if (this.Expedition.ExploreRate == 0)
            {
                this.status = ExpeditionStatus.returning;
            }

            if (this.actorRefID.Count < 1 && scr_System_CampaignManager.current.CharaInRoom(this.ParentRoom.RefID).Count < 1)
            {
                this.status = ExpeditionStatus.returning;
                this.AddResult(LocalizeDictionary.QueryThenParse("ui_management_expedition_jobInterruptMIA"), new List<string>(), new List<Character_Trainable>());
                this.NotifyEventFailure();
            }
        }

        TickOffTimeEvents(tickPackage);

        if (this.status == ExpeditionStatus.returning)
        {
            for(var i = packages_previous.Count - 1; i >= 0; i--)
            {
                var p = packages_previous[i];
                if (p.isTemporaryAP) continue;
                scr_System_CampaignManager.current.Unregister(p);
                this.packages_previous.RemoveAt(i);
            }

            if (properExitRefs.Count > 0 || this.actorRefID.Count < 1)
            {
                var charaList = this.FactionOwner_Party.Room.RoomChara;
                var remaining = new List<int>();
                if (charaList.Count > 0)
                {
                    foreach (var c in charaList)
                    {
                        if (c.isTemporaryActor && (c.CurrentJob == null || c.CurrentJobRefID == -1) && !c.FactionManager.HasPlayerFaction)
                        {
                            // Debug.Log($"Checking Charalist {c.CallName} is removable");

                        }
                        else
                        {
                            remaining.Add(c.RefID);
                        }
                    }
                }

                if (remaining.Count < 1)
                {
                    properExitRefs.Clear();
                    this.ExpeditionActive = FactionOwner_Party.IsRecurring;
                    this.status = this.ExpeditionActive ? ExpeditionStatus.queued : ExpeditionStatus.inactive;
                    this.statusTooltip = "expedition concluded";
                    var result = AddResult(LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_end"), new List<string>(), new List<int>());
                    List<string> obtained = new List<string>();
                    FactionOwner_Party.Inventory.Dump(FactionOwner_Party.OwnerFaction.Inventory, obtained);
                    result.Tooltips.Add($"obtained {String.Join(", ", obtained)}");
                    FactionOwner_Party.ExpeditionEnd();
                }
                else
                {
                    Debug.Log($"Actor remain [{String.Join("|", remaining)}]");
                }
            }
        }
    }

    /// <summary> set once the self release entry was banked (reset when a new expedition is set). No new exploration
    /// events are started after that. </summary>
    [JsonProperty] protected bool selfReleaseFired = false;

    /// <summary>
    /// Non-player (MIA) parties only - their UpdateStatus stops before the countdown/returning logic. Opt-in through
    /// Expedition.selfReleaseEventID: counts DurationHour down on game time (called every minute by Manage; tickPackage
    /// false for the out-of-cycle calls) and, once progress or countdown reaches 0, banks the release event like an
    /// exploration result (isSelfRelease: clickable only after every other banked entry is resolved). The event itself
    /// moves and releases the captives. immediateRunExpeditionEvent runs it at once if nothing else is pending.
    /// </summary>
    protected void TickSelfRelease(bool tickPackage)
    {
        if (selfReleaseFired || this.Expedition == null || this.Expedition.Base == null || !this.Expedition.Base.CanSelfRelease) return;

        if (RemainingMinutes > 0 && tickPackage) RemainingMinutes = Math.Max(0, RemainingMinutes - 1);
        if (RemainingMinutes != 0 && this.Expedition.ExploreRate != 0) return;
        if (!tickPackage) return;   // fire from the regular minute tick only

        var victims = new List<Character_Trainable>();
        foreach (var c in FactionOwner_Party.ManagedChara)
        {
            if (c == null || c.isTemporaryActor || FactionOwner_Party.GetMemberType(c).isHidden) continue;
            victims.Add(c);
        }
        if (victims.Count < 1)
        {
            selfReleaseFired = true;
            return;
        }

        var expBase = this.Expedition.Base;
        var victimRefs = new List<int>();
        foreach (var c in victims) victimRefs.Add(c.RefID);

        var package = new SerializableEventPackage();
        package.eventID = expBase.selfReleaseEventID;
        package.eventLabel = expBase.selfReleaseEventLabel;
        package.isSelfRelease = true;
        package.Targets.Add("victims", new List<int>(victimRefs));

        var entry = AddResult(LocalizeDictionary.QueryThenParse("ui_management_expedition_selfReleaseText"), new List<string>(), new List<int>(victimRefs));
        entry.unresolved = package;
        selfReleaseFired = true;

        if (ExpeditionUtility.ImmediateRunEnabled && !ExpeditionUtility.HasOtherUnresolved(this, entry)) ExpeditionUtility.RunBankedEventNow(this, entry);
    }

    /// <summary>
    /// Per member: minutes left before her next off-time roll (the last off-time event's DurationMinutes). Only members
    /// currently off time have an entry.
    /// </summary>
    [JsonProperty] protected Dictionary<int, int> offTimeCooldown = new Dictionary<int, int>();

    /// <summary>
    /// Off-time events (Expedition.offTimeEventRate > 0), every party, minute tick only: each of this party's own members
    /// who is off time - asleep, or off this job while the party rests / she needs rest (non-player parties: whenever she
    /// is off this job) - rolls offTimeEventRate per minute for an event from the current day/night list
    /// (ExpeditionUtility.RandOffTimeEvent). The result is applied at once like an exploration result but costs no
    /// progress; a banked scene keeps her as target while she stays off the job. Her state is untouched - an event that
    /// needs her awake wakes her itself (DisruptSleep).
    /// </summary>
    protected void TickOffTimeEvents(bool tickPackage)
    {
        if (!tickPackage) return;
        var exp = this.Expedition;
        if (exp == null || exp.Base == null || exp.Base.offTimeEventRate <= 0f || !isActive
            || selfReleaseFired || RemainingMinutes == 0 || exp.ExploreRate == 0)
        {
            offTimeCooldown.Clear();
            return;
        }
        bool playerParty = FactionOwner_Party.isPlayerFaction;
        if (playerParty ? (status != ExpeditionStatus.active && status != ExpeditionStatus.resting) : status == ExpeditionStatus.returning)
        {
            offTimeCooldown.Clear();
            return;
        }

        var offTime = new List<Character_Trainable>();
        foreach (var c in FactionOwner_Party.ManagedChara)
        {
            if (c == null || c.isTemporaryActor || c == scr_System_CampaignManager.current.Player) continue;
            var memberType = FactionOwner_Party.GetMemberType(c);
            if (memberType == null || memberType.isHidden) continue;
            if (c.InteractionJob != null && c.InteractionJob.isActive) continue;   // already in an interaction (e.g. a scene's command)

            bool offJob = c.CurrentJob != this;
            bool isOffTime = c.isSleeping
                || (offJob && (!playerParty || status == ExpeditionStatus.resting || ShouldRest(c)));
            if (isOffTime) offTime.Add(c);
        }

        // members back on the job (or gone) lose their wait
        var stale = new List<int>();
        foreach (var refID in offTimeCooldown.Keys) if (!offTime.Exists(x => x.RefID == refID)) stale.Add(refID);
        foreach (var refID in stale) offTimeCooldown.Remove(refID);

        foreach (var c in offTime)
        {
            if (offTimeCooldown.TryGetValue(c.RefID, out var wait) && wait > 0)
            {
                offTimeCooldown[c.RefID] = wait - 1;
                continue;
            }
            if (!Utility.RandomChance(exp.Base.offTimeEventRate)) continue;

            var ev = ExpeditionUtility.RandOffTimeEvent(c, exp, FactionOwner_Party);
            if (ev == null) continue;
            if (ExpeditionUtility.ApplyResult(this, ev, new List<Character_Trainable>() { c }, this.DisplayName, true) != null)
                offTimeCooldown[c.RefID] = Math.Max(0, ev.DurationMinutes);
        }
    }

    protected void NotifyEventFailure()
    {
        foreach(var eventry in this.ExpeditionResults)
        {
            foreach(var entry in eventry.Value)
            {
                if (entry.unresolved == null) continue;
                if (entry.unresolved.isResolved) continue;
                if (entry.unresolved.rescueJobID != -1)
                {
                    entry.resolveMessage = "rescue event failure";
                    entry.unresolved.isResolved = true;
                    var job = scr_System_CampaignManager.current.FindJobInstanceByID(entry.unresolved.rescueJobID) as Job_Expedition;
                    var kexp = job == null ? null : job.Expedition;
                    if (kexp != null)
                    {
                        kexp.ModProgressRate(0.2);
                        entry.resolveMessage += " (reset)";
                    }
                }

            }
        }
    }



    /// <summary>
    /// Return Parent FactionOwner (Party) Main Exit (camp room)
    /// </summary>
    [JsonIgnore]
    public override Room_Instance ParentRoom
    {
        get
        {
            return FactionOwner_Party.MainExit;
        }
    }

    [JsonProperty] string activePartyID = "";
    [JsonProperty] string activePartyOwnerID = "";

    [JsonProperty] bool _expeditionActive = false;
    [JsonIgnore] public bool ExpeditionActive
    {
        get
        {
            return _expeditionActive;
        }
        set
        {
            
            if (value == true)
            {
                if (this.Expedition != null)
                {
                    _expeditionActive = true;
                    FactionOwner_Party.OnDayUpdate_0(); // wipe recurring
                    this.startHour = FactionOwner_Party.FinalStartHour;
                }
            }
            else
            {
                _expeditionActive = value;
            }
        }
    }


    public int startHour = -1;

    public Job_Expedition() : base() { }

    public Job_Expedition(Manageable_Party p) : base() 
    {
        this.FactionOwner = p;
    }

    public List<ActionPackage_Expedition> storedResults = new List<ActionPackage_Expedition>();

    public void StoreResult(ActionPackage_Expedition res)
    {
        storedResults.Add(res);
    }

    public int RemainingMinutes = -1;


    [JsonProperty] protected int expeditionRefID = -1;

    ExpeditionInstance _exp = null;

    public override void RemoveActor(int charaRef)
    {

        if (this.hasUnresolvedResult)
        {
            foreach (var i in this.ExpeditionResults)
            {
                foreach (var result in i.Value)
                {
                    if (result.unresolved == null) continue;
                    result.NotifyCharaExit(charaRef);
                }
            }
        }

        for (int index = packages_previous.Count - 1; index >= 0; index--)
        {
            var p = packages_previous[index];
            if (p.actorRefs.Contains(charaRef))
            {
                p.DisablePackage();
                if (! scr_System_CampaignManager.current.Unregister(p))
                {
                   // Debug.LogError($"EXPEDITION REMOVEACTOR Unregister ERROR package {p.DisplayName}");
                }
                else
                {
                    //Debug.Log($"EXPEDITION REMOVEACTOR disabling package {p.DisplayName}");
                }
                packages_previous.RemoveAt(index);
            }
        }
        for (int index = packages_current.Count - 1; index >= 0; index--)
        {
            var p = packages_current[index];
            if (p.actorRefs.Contains(charaRef))
            {
                p.DisablePackage();
                if (!scr_System_CampaignManager.current.Unregister(p))
                {
                    //Debug.LogError($"EXPEDITION REMOVEACTOR Unregister ERROR package {p.DisplayName}");
                }
                else
                {
                    //Debug.Log($"EXPEDITION REMOVEACTOR disabling package {p.DisplayName}");
                }
                packages_current.RemoveAt(index);
            }
        }
        base.RemoveActor(charaRef);
    }
    /// <summary>
    /// Please call this from Party
    /// </summary>
    public void SetExpedition(ExpeditionInstance exp)
    {

        _exp = null;
        selfReleaseFired = false;
        offTimeCooldown.Clear();
        if (exp == null) this.expeditionRefID = -1;
        else this.expeditionRefID = exp.RefID;

        //Debug.LogError($"SetExpedition! {this.expeditionRefID}");
    }

    [JsonIgnore]
    public ExpeditionInstance Expedition
    {
        get
        {
            if (_exp == null && expeditionRefID != -1)
            {
                _exp = scr_System_CampaignManager.current.FindExpeditionByID(expeditionRefID);
            }
            return _exp;
        }
    }

    [JsonIgnore]
    public string ExpeditionName
    {
        get
        {
            if (this.Expedition == null) return "no exp";
            else return this.Expedition.Base.DisplayName;
        }
    }

    [JsonIgnore]
    public bool hasUnresolvedResult
    {
        get
        {
            foreach(var i in this.ExpeditionResults.Values)
            {
                foreach (var j in i) if (j.unresolved != null) return true;
            }
            return false;
        }
    }
    [JsonIgnore] public bool canReturn { get { return isActive && this.status == ExpeditionStatus.returning && !hasUnresolvedResult; } }
    [JsonIgnore] public bool isResting { get { return isActive && this.status == ExpeditionStatus.resting; } }
    [JsonIgnore] public bool isActive { get {
            return this.Expedition != null && this.status > ExpeditionStatus.queued; } }

    [JsonIgnore]
    public Manageable_Party FactionOwner_Party { get { return FactionOwner as  Manageable_Party; } }

    [JsonIgnore]
    public string DescriptionString
    { get
        {
            var names = new List<string>();
            switch(this.status)
            {
                case ExpeditionStatus.active:
                    if (Expedition != null && Expedition.DescriptionText.Count > 0) return LocalizeDictionary.QueryThenParse(Utility.GetRandomElement(Expedition.DescriptionText)).Replace("$names$", String.Join(", ", ActorNames));
                    else return LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_team_active").Replace("$names$", String.Join(", ", ActorNames));
                case ExpeditionStatus.resting:
                    return LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_team_resting").Replace("$names$", String.Join(", ", ActorNames));
                case ExpeditionStatus.gathering:
                    return LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_start");
                case ExpeditionStatus.returning:
                    return LocalizeDictionary.QueryThenParse("ui_management_expeditionJob_team_returning").Replace("$names$", String.Join(", ", ActorNames));
                default:
                    return "?";
            }
        } }

    public bool canExit(int charaRef)
    {
        return properExitRefs.Contains(charaRef);
    }
    public List<int> properExitRefs = new List<int>();


    public Dictionary<int, DateTime> startTime = new Dictionary<int, DateTime>();

    public override bool UpdateActorPackage(Character_Trainable c, out string ss)
    {
        /*
             if character hour has com setting, try get com setting job
            else get random
         */
       // RefreshValidCOMs(true);
        ss = "(Job Expedition): ";
        // actor have job but don't have a action package registeredDisplayName.
        //Character_Trainable c = scr_System_CampaignManager.current.FindInstanceByID(actorRefID[i]);

        // Check has ongoing package
        var temp = packages_current.FindAll(x => x.actorRefs.Contains(c.RefID));
        if (temp.Count > 0)
        {
            ss += c.FirstName + " already have package |";
            foreach (var i in temp) ss += i.DisplayName + "|";
            return true;
        }

        // check has ongoing package 2
        List<ActionPackage> tempList = packages_previous.FindAll(x => x.actorRefs.Contains(c.RefID) && x.Duration > 0);
        if (tempList.Count > 0)
        {
            List<int> durations = new List<int>();
            foreach (var i in tempList) durations.Add(i.Duration);

            ss += c.FirstName + $" already have ongoing previous package {tempList.Count} [{String.Join("|",durations)}] ";
            return true;
        }
        /* Expedition ignores jobcomplete
        else if (actorJobComplete.Contains(c.RefID) || c.RefID == 0)
        {
            ss += c.FirstName + " have completed job, releasing";
            return false;
        }*/

        // pathing
        var charaRoom = scr_System_CampaignManager.current.GetCharaRoomInstance(c.RefID);
        var desiredCOMID = this.actorRefIDStorage[c.RefID].comID;
        var desiredCOM = this.allusableCOMs.Find(x => x.ID == desiredCOMID);

        var parentFaction = FactionOwner_Party;
        var parentPlusFaction = FactionOwner.FactionOwnerRoot;

        if (!isActive)
        {
            ss += "expedition not active, waiting ||";
            return false;
        }
        else if (this.status == ExpeditionStatus.returning)
        {   // if existing exploration package, break existing '

            if (hasUnresolvedResult)
            {
                ss += "expedition hasUnresolvedResult, waiting ||";
                return true;
            }
            else // canreturn
            {
                int exitRef = -1;
                if (parentPlusFaction.MainExit != null && parentPlusFaction.isManagedChara(c.RefID)) exitRef = parentPlusFaction.MainExit.RefID;

                if (exitRef == -1)
                {
                    if (!this.Expedition.Base.CanBeRescued) Debug.LogError("ERROR!!!!");

                    ss += "actor require rescue ||";

                    // require rescue
                    return true;
                }
                else if (charaRoom == parentFaction.MainExit)
                {   // return chara
                    AddResult(LocalizeDictionary.QueryThenParse("exp_event_returning"), new List<string>(), new List<int> { c.RefID });
                    ActionPackage_PathTo package = new ActionPackage_PathTo(this, c.RefID, exitRef);
                    if (!package.Validate())
                    {
                        ss += "actor pathing package creation failed ||";
                        return false;
                    }
                    ss += "actor return pathing created ||";
                    AddPackage(new List<ActionPackage>() { package });

                    var memStr = LocalizeDictionary.QueryThenParse("exp_event_return_memory").Replace("$loc$", Expedition.Base.DisplayName);
                    var newMem = new MemInstance(new List<int>(), new List<string>(), "", -1, -1, true, Memory_Response.Accept, Memory_Attitude.Neutral, memStr);

                    var entry = c.Memory.AddEntry(newMem, new List<string>() { "expeditionEnd" });
                    entry.disableRoomName = true;

                    return true;
                }
                else if (charaRoom.RefID == exitRef)
                {   // release chara
                    //Debug.LogError($"Error fail to return {c.CallName} from party {FactionOwner_Party.FullFactionDisplayName}");
                    ss += "actor at exit location, waiting for release ||";
                    properExitRefs.Add(c.RefID);
                    return true;
                }
            }
        }
        else if (parentFaction.MainExit != null && charaRoom != parentFaction.MainExit)
        {
            //Debug.Log("JobFurniture : trying to add pathing package to ["+c.FirstName+"]");
            // 1 - if actor not in job room, set go to room.
            // make movement package
            if (parentPlusFaction == null || parentPlusFaction.MainExit == null)
            {   // owner faction does not exist, teleporting
                //AddResult(LocalizeDictionary.QueryThenParse("exp_event_gathering"), new List<string>(), new List<int> { c.RefID });
                ActionPackage_TeleportTo package = new ActionPackage_TeleportTo(this, c.RefID, parentFaction.MainExit.RefID);
                if (!package.Validate())
                {
                    ss += "actor teleport package creation failed ||";
                    return false;
                }
                ss += "actor teleport departure pathing created ||";
                AddPackage(new List<ActionPackage>() { package });
                return true;
            }
            else
            {
                //AddResult(LocalizeDictionary.QueryThenParse("exp_event_gathering"), new List<string>(), new List<int> { c.RefID });
                ActionPackage_PathTo package = new ActionPackage_PathTo(this, c.RefID, parentFaction.MainExit.RefID);
                if (!package.Validate())
                {
                    ss += "actor pathing package creation failed ||";
                    return false;
                }
                ss += "actor departure pathing created ||";
                AddPackage(new List<ActionPackage>() { package });
                return true;
            }
        }
        else if (this.status == ExpeditionStatus.gathering)
        {
            ss += "waiting for team to gather ||";
            return true;
        }
        else if (NeedsPartyGearChange(c))
        {
            // under way and the party gear isn't on (yet / any more): let go, so the outfit change node sends the
            // character to the camp's changing spot; TryFindPartyExplorationJob puts them back into this job afterwards
            ss += "changing into party gear, liberating ||";
            return false;
        }
        else if (desiredCOM != null && c.NeedUndress(desiredCOM.requirements, true))
        {
            ActionPackage_Undress package = new ActionPackage_Undress(this, c.RefID, desiredCOM.requirements, true);
            if (!package.Validate())
            {
                ss += "actor undress package creation failed ||";
                return false;
            }
            ss += "actor undress created ||";
            AddPackage(new List<ActionPackage>() { package });
            return true;
        }
        else if (this.isResting)
        {
            ss += "expedition on break! ||";
            return false;
        }
        else if (ShouldRest(c))
        {
            ss += "should rest from exploration, liberating ||";
            return false;
        }
        else if (packageCooldown > 0)
        {
            ss += $"expedition active, exploring, cooldown {packageCooldown} no event ||";
            return true;
        }
        else if (FactionOwner_Party.isPlayerFaction && FactionOwner_Party.isPrisoner(c))
        {   // forbid player party captured prisoner from exploring
            ss += "is being imprisoned, cannot explore ||";
            return true;
        }
        else if (RemainingMinutes != 0 && Expedition.ExploreRate != 0 && !selfReleaseFired)
        {
            

            //Debug.Log("JobFurniture : [" + c.FirstName + "] at work location, adding job command with [" + validCOMs.Count + "] valid jobCOMs [" + String.Join(",", s) + "]");
            // 2 - if actor is in room, set COM package
            // make COM package
            var list1 = MakePackages(c,false,true);
            var list2 = MakePackagesJoinable(c);

            var pl1 = list1.Count > 0 ? Utility.GetRandomElement(list1) : null;
            var pl2 = list2.Count > 0 ? Utility.GetRandomElement(list2) : null;

            if (pl2 != null && pl2.JoinAP(c))
            {
                // do nothing
                ss += $"joining existing [{pl2.DescriptionText(c.RefID)}] among [{list2.Count}]";
                return true;
            }
            else if (pl1 != null)
            {
                AddPackage(new List<ActionPackage>() { pl1 });
                ss += $"creating package [{pl1.DescriptionText(c.RefID)}] among [{list1.Count}]";
                return true;
            }
            else
            {
                ss += "expedition actor has not valid command or has completed all commands";
                return false;
            }
        }

        ss += $"expedition no package, remaining minutes {RemainingMinutes}, explore rate {Expedition.ExploreRate}";
        return true;
        
    }

    public void StartCooldown()
    {
        if (this.Expedition == null || this.FactionOwner_Party == null) return;
        this.packageCooldown = ExpeditionUtility.Cooldown(Expedition, this.FactionOwner_Party);
        //Debug.Log($"Expedition cooldown set to: {packageCooldown}");
    }
    public bool HasCooldown()
    {
        return packageCooldown > 0;
    }
    /// <summary>
    /// An NPC member of this (unlocked) party has party gear set (Manageable_Party.GetOutfit) and the wardrobe wants a
    /// change. Party gear is changed into after departure, at the camp. Kidnapped (locked) characters get their outfit
    /// on the spot instead (EventUtility.Kidnap), the player is never auto-changed.
    /// </summary>
    bool NeedsPartyGearChange(Character_Trainable c)
    {
        if (c == null || c == scr_System_CampaignManager.current.Player || c.FactionManager.isPartyLocked) return false;
        if (this.status != ExpeditionStatus.active) return false;
        if (FactionOwner_Party == null || FactionOwner_Party.GetOutfit(c) == null) return false;
        return c.Wardrobe != null && c.Wardrobe.NeedsOutfitChange;
    }

    public bool ShouldRest(Character_Trainable c)
    {
        if (this.status == ExpeditionStatus.returning) return false;
        if (FactionOwner_Party.PrioritizeResting && c.Stats.HP.ValuePercentile < 1.0) return true;
        return c.Stats.HP.Value < 1 || c.shouldRest;
    }


    [JsonProperty] protected int packageCooldown = 0;

    /// <summary>
    /// Make a single random package
    /// </summary>
    /// <param name="c"></param>
    /// <param name="allowInvalid"></param>
    /// <returns></returns>
    public override List<ActionPackage> MakePackages(Character_Trainable c, bool allowParent, bool allowChild, bool allowInvalid = false, COM filter = null, List<string> debug = null)
    {
        //Debug.Log("JobFurniture : [" + c.FirstName + "] at work location, adding job command with [" + validCOMs.Count + "] valid jobCOMs [" + String.Join(",", s) + "]");
        // 2 - if actor is in room, set COM package
        // make COM package
        List<ActionPackage> results = new List<ActionPackage>();

        int loop = 0;
        while (loop < 10)
        {
            loop++;
            var newAP = ExpeditionUtility.RandEvent(c, Expedition, this.FactionOwner_Party);
            if (newAP == null) continue;
            if (!newAP.Actors.Contains(c)) continue;
            newAP.ReEstablishParent(this);// = this;
            if (newAP.Validate() || allowInvalid)
            {
                results.Add(newAP);
                break;
            }
        }

        return results;
    }
    protected List<ActionPackage> MakePackagesJoinable(Character_Trainable c, bool allowInvalid = false, List<string> debug = null)
    {
        List<ActionPackage> pkgs = new List<ActionPackage>();

        foreach (var pkg in this.ActivePackages)
        {
            if (!(pkg is ActionPackage_Expedition)) continue;
            if (pkg.Duration <= 1) continue;
            if (pkg.isPaused) continue;
            if (pkg.isTemporaryAP) continue;
            if (pkg.doer.Contains(c)) continue;
            //if (pkg.Duration * 2 < pkg.targetCOM.TimeScale) continue;

            ActionPackage_Expedition newpkg = pkg.Copy() as ActionPackage_Expedition;
            if (!newpkg.canJoinAP(c)) continue;
            else pkgs.Add(pkg);
        }
        return pkgs;
    }

    public override string GetJobDescription(int charaRef)
    {
        return LocalizeDictionary.QueryThenParse("chara_currentjob_expedition");
        //return base.GetJobDescription(charaRef);
    }
}