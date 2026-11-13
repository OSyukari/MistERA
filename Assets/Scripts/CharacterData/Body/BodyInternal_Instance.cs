using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;

public enum Ranking
{
    None,
    F,
    E,
    D,
    C,
    B,
    A,
    S
}

public class BodyInternal_Instance : I_TraceHolder
{
    public BodyInternal_Womb womb = null;

    //---- traces (Docs/Body/Plan_BodyTraces.md) ----
    [JsonProperty] List<TraceInstance> traces = null;
    public bool ShouldSerializetraces() { return traces != null && traces.Count > 0; }
    [JsonIgnore] public List<TraceInstance> Traces { get { if (traces == null) traces = new List<TraceInstance>(); return traces; } }
    [JsonIgnore] public bool HasTraces { get { return traces != null && traces.Count > 0; } }
    static readonly List<BodyPartEquipSlot> noSlots = new List<BodyPartEquipSlot>();
    [JsonIgnore] public List<BodyPartEquipSlot> TraceSlots { get { return Base?.traceSlots ?? noSlots; } }
    [JsonIgnore] public bool AllowTraces { get { return TraceSlots.Count > 0; } }
    [JsonIgnore] public bool CanLeaveTraces { get { return Base != null && Base.canLeaveTraces; } }
    [JsonIgnore] public List<string> LeaveTraceIDs { get { return Base?.leaveTraceIDs; } }
    [JsonIgnore] public I_TraceHolder ParentHolder { get { return Parent; } }
    [JsonIgnore] public List<BodyPartEquipSlot> OwnSlots { get { return Base?.AvailableSlots ?? noSlots; } }
    [JsonIgnore] public List<BodyPartEquipSlot> UnderSlots { get { return Base?.coverSlots ?? noSlots; } }
    [JsonIgnore] public string TraceHolderID { get { return baseID; } }
    /// <summary> an organ's own slots sit under its parent's coverSlots: the organ's coverage decides </summary>
    public bool IsSlotVisible(BodyPartEquipSlot slot) { return GetRevealingScore(BodyEquipLayer.None) < 2; }

    /// <summary> this organ's traces (visibleOnly: while it is uncovered) </summary>
    public void GetTraces(List<(I_TraceHolder holder, TraceInstance trace)> result, bool visibleOnly = false)
    {
        if (!HasTraces) return;
        foreach (var t in traces)
        {
            if (!visibleOnly || IsSlotVisible(t.slot)) result.Add((this, t));
        }
    }

    [JsonProperty] protected long firstExperience = 0, lastExperience = 0;
    [JsonProperty] protected string firstExpDesc = "", lastExpDesc = "";

    [JsonIgnore] public string FirstExperienceDesc { get { return this.firstExpDesc; } }

    public enum UnawareReason { None, Sleeping, Timestop }

    // first experience taken while owner was unaware (sleeping/unconscious/timestopped). Kept apart from
    // firstExperience (aware) - an unaware loss doesn't block the aware one, but once the aware first experience
    // is filled the unaware one stays permanently empty.
    // unawareExperience is the memory tick of the loss event, same as firstExperience - the memory entry
    // (permanent, "important") carries every related actor.
    [JsonProperty] protected long unawareExperience = 0;

    // written once when the loss happens, never modified. unawareExpDesc is the snapshot in the localization of
    // that time; the loose fields rebuild it under the current localization. Target name is stored since the
    // target actor may no longer exist.
    [JsonProperty] protected string unawareExpDesc = "", unawareDescKey = "", unawareTargetName = "", unawareTargetPartID = "", unawareComID = "";
    [JsonProperty] protected int unawareComVariant = -1;

    [JsonIgnore] public bool HasUnawareExperience { get { return this.unawareExperience != 0; } }

    /// <summary> this part can lose its virginity at all (has a first-experience text and virginityLossTags) </summary>
    [JsonIgnore] public bool CanLoseVirginity { get { return Base != null && Base.firstExperienceDesc != "" && Base.virginityLossTags.Count > 0; } }
    /// <summary> can lose it and has neither an aware nor an unaware first experience. Plain fields only (safe on a detached copy). </summary>
    [JsonIgnore] public bool IsVirgin { get { return CanLoseVirginity && this.firstExperience == 0 && this.unawareExperience == 0; } }

    /// <summary>
    /// Unaware loss desc rebuilt under current localization, falls back to the stored snapshot when the
    /// COM / target body part definition no longer exists.
    /// </summary>
    [JsonIgnore] public string UnawareExperienceDesc
    {
        get
        {
            var s = BuildUnawareDesc(unawareDescKey, unawareTargetName, unawareTargetPartID, unawareComID, unawareComVariant);
            return s ?? unawareExpDesc;
        }
    }

    string BuildUnawareDesc(string descKey, string targetName, string targetPartID, string comID, int comVariant)
    {
        if (descKey == "") return null;
        var com = comID == "" ? null : scr_System_Serializer.current.MasterList.COMs.GetByID(comID);
        if (com == null) return null;
        string target = targetName;
        if (targetPartID != "")
        {
            var partBase = scr_System_Serializer.current.GetByNameOrID_BodyInternal_Base(targetPartID);
            if (partBase == null) return null;
            target = LocalizeDictionary.QueryThenParse("bodyPart_Fulldisplayname").Replace("$name$", targetName).Replace("$part$", partBase.DisplayName);
        }
        return LocalizeDictionary.QueryThenParse(descKey).Replace("$target$", target).Replace("$command$", com.DisplayName(comVariant)).Replace("$partname$", this.DisplayName);
    }

    Memory_Entry cache_firstEXP = null, cache_lastEXP = null;
    /// <summary>Drop cached memory entries - called when entries get merged away (MemoryManager.MergeLLMSession).</summary>
    public void ClearExperienceCache() { cache_firstEXP = null; cache_lastEXP = null; }
    [JsonIgnore] public Memory_Entry FirstExperience { get
        {
            if (firstExperience == 0) return null;
            if (Base.firstExperienceDesc == "") return null;
            if (cache_firstEXP == null)
            {
                cache_firstEXP = Owner.Memory.FindEntryByDateTimeTick(firstExperience);
            }
            return cache_firstEXP;
        } 
    }

    [JsonIgnore]
    public string Image
    {
        get
        {
            if (canContain && basePointer.images_volume.Count > 0)
            {
                var fillPercentage = this.ExpandedCapacityPercentageLiquid;
                var maxcount = basePointer.images_volume.Count;
                int index = (int)Math.Clamp(fillPercentage * maxcount, 0, maxcount - 1);

                if (index != 0) return basePointer.images_volume[index];
                //return image based on fill percentage
            }
            if (basePointer.images_expansion != null && basePointer.images_expansion.Count > 0)
            {
                var solidPercentage = this.ExpandedCapacityPercentage;
                var maxcountSolid = basePointer.images_expansion.Count;
                var sklevel = ExpansionSkill == null ? 0 : ExpansionSkill.GetSkillLevel;
                int index = (int)Math.Clamp(solidPercentage * maxcountSolid, 0, maxcountSolid - 1);
                sklevel = Math.Clamp(sklevel, 0, basePointer.images_expansion.Count - 1);

                return basePointer.images_expansion[Math.Max(index, sklevel)];
            }
            else if (basePointer.images_volume.Count > 0) return basePointer.images_volume[0];
            else return "";
        }
    }

    public void UnequipByLayer(BodyEquipLayer filter, Revealing revealingScoreFilter = Revealing.Erotic)
    {
        foreach (var layer in this.equipLayers)
        {
            if (filter == BodyEquipLayer.None || layer <= filter)
            {
                foreach (BodyPartEquipSlot slot in this.availableSlots)
                {
                    var equip = this.GetEquip(layer, slot);
                    var comp = equip == -1 ? null : Owner?.Body?.ResolveEquip(equip)?.Comp_Equippable;
                    // items worn on the organ that cover nothing (condom, piercings) are not undressed - like other
                    // non-covering clothing; a condom comes off through its own command
                    if (comp != null && comp.revealing >= revealingScoreFilter && comp.coverSlot.Count > 0)
                    {
                        UnequipItem(equip);
                    }
                }
            }
            else continue;
        }
    }
    [JsonIgnore] public Memory_Entry LastExperience { get 
        {
            if (lastExperience == 0) return null;
            if (cache_lastEXP == null)
            {
                cache_lastEXP = Owner.Memory.FindEntryByDateTimeTick(lastExperience);
            }
            return cache_lastEXP;
        }
    }

    [JsonIgnore]
    public bool isPregnant
    {
        get
        {
            return womb != null && womb.isPregnant;
        }
    }

    /// <summary>
    /// Pregnant and the contents (foetus + anything else) visibly expand this organ (isVisiblyExpanded)
    /// </summary>
    [JsonIgnore]
    public bool isVisiblyPregnant
    {
        get
        {
            return isPregnant && isVisiblyExpanded;
        }
    }

    /// <summary>
    /// Owner's awareness at the moment of a sex experience, matching GetActorTag's timestop/sleeping/unconscious tags.
    /// </summary>
    public static UnawareReason GetUnawareReason(Character_Trainable c)
    {
        if (c == null) return UnawareReason.None;
        if (c.isTimeStopped) return UnawareReason.Timestop;
        if (c.isSleeping || c.Stats.isConsciousnessUnconscious) return UnawareReason.Sleeping;
        return UnawareReason.None;
    }

    /// <summary>
    /// reason != None: owner unaware of the act - registered as the unaware first experience instead
    /// (see unawareExperience). Returns true on either kind of first experience registration.
    /// </summary>
    public bool NotifySexExperience(bool hasPermission, EvaluationPackage.DelayedExpLogging exp, UnawareReason reason)
    {
        string targetName = exp.targetName, comName = exp.comName;
        List<string> comtags = exp.comtags, targetBodyTag = exp.targetBodytags;
        if (Owner.Memory.Last == null)
        {
            Debug.LogError($"error NotifySexExperience {Owner.FirstName} Last memory null");
            return false;
        }

        this.lastExperience = Owner.Memory.Last.FinalEndTime.Ticks;
        this.lastExpDesc = LocalizeDictionary.QueryThenParse("bodyPart_internal_lastExpFormat").Replace("$target$", targetName).Replace("$command$", comName);

        // aware first experience filled: both closed (unaware one stays permanently empty)
        if (this.firstExperience != 0 || this.Base.firstExperienceDesc == "" || this.Base.virginityLossTags.Count < 1)
        {
            return false;
        }
        // only the first unaware loss is recorded
        if (reason != UnawareReason.None && this.unawareExperience != 0) return false;

        if (targetBodyTag != null && Utility.ListContainsLoose(targetBodyTag, this.Base.virginityLossTags))
        {
            if (scr_System_CentralControl.current.LogPrefs.DLog_Training) Debug.Log($"{Owner.FirstName} match firstexperience {DisplayName} on targetBodytags {String.Join(" ", targetBodyTag)}");
            if (reason != UnawareReason.None) RegisterUnawareExperience(exp, reason);
            else
            {
                this.firstExperience = lastExperience;
                this.firstExpDesc = LocalizeDictionary.QueryThenParse(hasPermission? "bodyPart_internal_expVirginLoss_cons" : "bodyPart_internal_expVirginLoss").Replace("$target$", targetName).Replace("$command$", comName).Replace("$partname$", this.DisplayName);
            }
            return true;
        }
        else if (Utility.ListContainsLoose(comtags, this.Base.virginityLossTags))
        {
            if (scr_System_CentralControl.current.LogPrefs.DLog_Training) Debug.Log($"{Owner.FirstName} match firstexperience {DisplayName} on comtags {String.Join(" ", comtags)}");
            if (reason != UnawareReason.None) RegisterUnawareExperience(exp, reason);
            else
            {
                this.firstExperience = lastExperience;
                this.firstExpDesc = LocalizeDictionary.QueryThenParse(hasPermission ? "bodyPart_internal_expVirginLoss_cons" : "bodyPart_internal_expVirginLoss").Replace("$target$", targetName).Replace("$command$", comName).Replace("$partname$", this.DisplayName);
            }
            return true;
        }
        else
        {

            return false;
            //Debug.LogError("Checking virginity loss with tags [" + String.Join(",", ownerTags) + "][" + String.Join(",", comtags) + "] with baseTags [" + String.Join(",", this.Base.virginityLossTags) + "]");
        }
    }

    /// <summary>
    /// Dry run of NotifySexExperience's gate - whether this interaction would register a first experience
    /// (aware or unaware), without changing anything. Used to tag command kojo before the loss is registered.
    /// </summary>
    public bool WouldLoseFirstExperience(List<string> comtags, List<string> targetBodyTag, UnawareReason reason)
    {
        if (this.firstExperience != 0 || this.Base.firstExperienceDesc == "" || this.Base.virginityLossTags.Count < 1) return false;
        if (reason != UnawareReason.None && this.unawareExperience != 0) return false;
        if (targetBodyTag != null && Utility.ListContainsLoose(targetBodyTag, this.Base.virginityLossTags)) return true;
        return comtags != null && Utility.ListContainsLoose(comtags, this.Base.virginityLossTags);
    }

    void RegisterUnawareExperience(EvaluationPackage.DelayedExpLogging exp, UnawareReason reason)
    {
        this.unawareExperience = lastExperience;
        this.unawareDescKey = reason == UnawareReason.Timestop ? "bodyPart_internal_expVirginLoss_unaware_timestop" : "bodyPart_internal_expVirginLoss_unaware_sleep";
        this.unawareTargetName = exp.targetActorName;
        this.unawareTargetPartID = exp.targetPartID;
        this.unawareComID = exp.comID;
        this.unawareComVariant = exp.comVariant;
        this.unawareExpDesc = BuildUnawareDesc(unawareDescKey, unawareTargetName, unawareTargetPartID, unawareComID, unawareComVariant)
            ?? LocalizeDictionary.QueryThenParse(unawareDescKey).Replace("$target$", exp.targetName).Replace("$command$", exp.comName).Replace("$partname$", this.DisplayName);
    }

    // Called by ExperienceInitializer at generation time to mark this body part as already
    // deflowered, without a real partner or memory entry. Timestamp 1 is non-zero so virginity
    // checks pass, but is never a real DateTime tick value so FirstExperience returns null.
    public bool WriteSexExperience(bool hasPermission, string targetName, string comName, List<string> actiontags)
    {
        if (lastExperience == 0 || lastExperience == 1)
        {
            this.lastExperience = 1;
            this.lastExpDesc = LocalizeDictionary.QueryThenParse("bodyPart_internal_lastExpFormat").Replace("$target$", targetName).Replace("$command$", comName);
        }

        if (this.firstExperience != 0 || this.Base.firstExperienceDesc == "" || this.Base.virginityLossTags.Count < 1)
        {
            // do nothing
            return false;
        }
        else if (actiontags != null && Utility.ListContainsLoose(actiontags, this.Base.virginityLossTags))
        {
            if (scr_System_CentralControl.current.LogPrefs.DLog_Training) Debug.Log($"{Owner.FirstName} match firstexperience {DisplayName} on targetBodytags {String.Join(" ", actiontags)}");
            this.firstExperience = 1;
            this.firstExpDesc = LocalizeDictionary.QueryThenParse(hasPermission ? "bodyPart_internal_expVirginLoss_cons" : "bodyPart_internal_expVirginLoss").Replace("$target$", targetName).Replace("$command$", comName).Replace("$partname$", this.DisplayName);
            return true;
        }
        else return false;
    }
    // Called by ExperienceInitializer at generation time to mark this body part as already
    // deflowered, without a real partner or memory entry. Timestamp 1 is non-zero so virginity
    // checks pass, but is never a real DateTime tick value so FirstExperience returns null.
    public bool WriteSexExperience(bool hasPermission, ExperienceActor target, string comName, List<string> actiontags)
    {
        if (lastExperience == 0 || lastExperience == 1)
        {
            this.lastExperience = 1;
            this.lastExpDesc = LocalizeDictionary.QueryThenParse("bodyPart_internal_lastExpFormat").Replace("$target$", LocalizeDictionary.QueryThenParse( target.displayName)).Replace("$command$", comName).Replace("$insertion$", LocalizeDictionary.QueryThenParse(target.insertionName));
        }

        if (this.firstExperience != 0 || this.Base.firstExperienceDesc == "" || this.Base.virginityLossTags.Count < 1)
        {
            // do nothing
            return false;
        }
        else if (actiontags != null && Utility.ListContainsLoose(actiontags, this.Base.virginityLossTags))
        {
            if (scr_System_CentralControl.current.LogPrefs.DLog_Training) Debug.Log($"{Owner.FirstName} match firstexperience {DisplayName} on targetBodytags {String.Join(" ", actiontags)}");
            this.firstExperience = 1;
            this.firstExpDesc = LocalizeDictionary.QueryThenParse(hasPermission ? "bodyPart_internal_expVirginLoss_cons" : "bodyPart_internal_expVirginLoss").Replace("$target$", LocalizeDictionary.QueryThenParse(target.displayName)).Replace("$insertion$", LocalizeDictionary.QueryThenParse(target.insertionName)).Replace("$command$", comName).Replace("$partname$", this.DisplayName);
            return true;
        }
        else return false;
    }
    public void Draw_FirstExperience(scr_HoverableText box)
    {
        if (Base.firstExperienceDesc == "") box.SetText("");    // this organ does not register first experience at all


        string conscious = firstExpDesc == "" ? LocalizeDictionary.QueryThenParse(Base.firstExperienceDesc) : firstExpDesc;
        if (unawareExperience == 0) box.SetText(conscious);
        else if (firstExperience != 0) box.SetText($"{UnawareExperienceDesc}\n{conscious}");
        // unaware loss only: owner still believes they're a virgin
        else box.SetText(UnawareExperienceDesc + LocalizeDictionary.QueryThenParse("bodyPart_internal_unaware_marker"));

        var mem = FirstExperience;
        if (mem == null && unawareExperience != 0) mem = Owner.Memory.FindEntryByDateTimeTick(unawareExperience);
        if (mem != null) box.SetExternalTooltip(mem.ToString(false, true, true));
    }
    public void Draw_LastExperience(scr_HoverableText box)
    {
        if (lastExpDesc == "") box.SetText("");
        else box.SetText(lastExpDesc);
            
        if (LastExperience != null) box.SetExternalTooltip(LastExperience.ToString(false, true, true));
    }

    int expansionLevel = 0;
    int expansion = 0;
    int cumStimulation = 1;

    public void UpdateTimeHour()
    {
        // update from owner main
        //if (this.womb != null) womb.HourTick();
        if (this.canContain && Base != null) FlowCumHourly();
    }

    //---- cum flow (Docs/Body/Plan_InternalFluidFlow.md) ----
    // Only cum flows (sperm moves on its own). In = the deeper organ, Out = toward the outside; the hourly flow never
    // leaves the body - only a load pushed past max capacity spills out (PushCumOverflowOut).

    const float MinFlowAmount = 0.01f;
    // FlowCumHourly -> MoveCumTo never recurses, one buffer is enough
    static readonly List<Item_Instance_Cum> flowBuffer = new List<Item_Instance_Cum>();

    /// <summary> cum held here, ml </summary>
    [JsonIgnore] public float FlowableCum
    {
        get
        {
            float sum = 0;
            var contents = Contains;
            for (int i = 0; i < contents.Count; i++)
            {
                if (contents[i] is Item_Instance_Cum cum && cum.CumAmount > 0) sum += cum.CumAmount;
            }
            return sum;
        }
    }

    /// <summary> the organ a direction tag leads to; null for none, the outside ("ext") or an organ that holds nothing (clit) </summary>
    BodyInternal_Instance FlowTarget(string tag)
    {
        if (string.IsNullOrEmpty(tag) || tag == "ext" || Owner?.Body == null) return null;
        var target = Owner.Body.GetRandomInternalWithTag(tag);
        return target != null && target != this && target.canContain ? target : null;
    }

    /// <summary> the direction leads out of the body: "ext", or an organ that holds nothing (vagina -> clit) </summary>
    bool LeadsOutside(string tag)
    {
        return !string.IsNullOrEmpty(tag) && FlowTarget(tag) == null;
    }

    /// <summary> overflow out -> flow back -> seep in, each by its BodyInternal_Base share (0 = off) </summary>
    void FlowCumHourly()
    {
        float self = FlowableCum;
        if (self < MinFlowAmount) return;
        var def = Base;
        var outOrgan = def.overflowShare > 0 || def.backflowShare > 0 ? FlowTarget(def.tag_directionOut) : null;

        if (outOrgan != null && def.overflowShare > 0)
        {
            float excess = self - VolumeCapacity;
            if (excess > 0) self -= MoveCumTo(outOrgan, excess * def.overflowShare);
        }
        if (outOrgan != null && def.backflowShare > 0)
        {
            float outer = outOrgan.FlowableCum;
            if (self > outer) self -= MoveCumTo(outOrgan, (self - outer) * def.backflowShare);
        }
        if (def.seepTargetRatio > 0 && def.seepMaxStep > 0)
        {
            var inOrgan = FlowTarget(def.tag_directionIn);
            if (inOrgan != null)
            {
                float missing = self * def.seepTargetRatio - inOrgan.FlowableCum;
                if (missing > 0) MoveCumTo(inOrgan, Math.Min(missing, self * def.seepMaxStep));
            }
        }
    }

    /// <summary> moves amount ml of cum (each father's share alike) into target quietly. Returns the ml moved </summary>
    float MoveCumTo(BodyInternal_Instance target, float amount)
    {
        if (target == null || amount < MinFlowAmount) return 0;
        flowBuffer.Clear();
        float moved = TakeCum(amount, flowBuffer);
        for (int i = 0; i < flowBuffer.Count; i++) target.ReceiveFlow(flowBuffer[i]);
        flowBuffer.Clear();
        if (moved > 0) Owner?.Body?.NotifyInternalsContentChange();
        return moved;
    }

    /// <summary>
    /// Takes amount ml of cum out of this organ, the same share of every father's cum (like Deflate) so the womb's
    /// weighted father pick stays fair. A fully taken item leaves as itself, a partly taken one is split into a new
    /// registered item. Returns the ml taken.
    /// </summary>
    float TakeCum(float amount, List<Item_Instance_Cum> result)
    {
        float total = FlowableCum;
        if (amount <= 0 || total <= 0) return 0;
        float ratio = Math.Min(1f, amount / total);
        float taken = 0;
        var contents = Contains;
        for (int i = contents.Count - 1; i >= 0; i--)
        {
            if (!(contents[i] is Item_Instance_Cum cum) || cum.CumAmount <= 0) continue;
            float part = cum.CumAmount * ratio;
            if (part <= 0) continue;
            if (cum.CumAmount - part <= 0.001f)
            {
                taken += cum.CumAmount;
                ExtractContent(cum.RefID);
                result.Add(cum);
            }
            else
            {
                cum.CumAmount -= part;
                result.Add(SplitCum(cum, part));
                taken += part;
            }
        }
        return taken;
    }

    /// <summary> a registered copy of source holding amount ml, same father; already counted (no second exp / stimulation) </summary>
    static Item_Instance_Cum SplitCum(Item_Instance_Cum source, float amount)
    {
        var split = WorldManager.Instantiate(source.raceID, source.baseID, source.templateID, source.nameOverwrite);
        split.ownerRefID = source.ownerRefID;
        split.CumAmount = amount;
        split.experienceTicked = true;
        return split;
    }

    /// <summary> flowed-in cum: merges into the same father's cum here, else sits with its own digest delay. No exp, message or memory </summary>
    void ReceiveFlow(Item_Instance_Cum cum)
    {
        var contents = Contains;
        for (int i = 0; i < contents.Count; i++)
        {
            if (contents[i] is Item_Instance_Cum existing && existing != cum && existing.Merge(cum))
            {
                scr_System_CampaignManager.current.Unregister(cum);
                return;
            }
        }
        var method = cum.Comp_Ingestible?.GetIngestMethod(this.Base.tags);
        ContainedRefs_Delays[cum.RefID] = method == null ? 0 : (int)Utility.RandVariation(method.digestDelay, method.digestDelayVariation);
        contains_cache = null;
    }

    /// <summary>
    /// item left the body from this organ: leakTraceRatio of it lands as its leak trace (vagina / anus -> lower body,
    /// mouth -> face, may spread), the rest goes on the floor
    /// </summary>
    void SpillToOutside(Item_Instance item)
    {
        var ing = item?.Comp_Ingestible;
        if (ing == null) return;
        float amount = ing.amount;
        float traceAmount = amount * Math.Clamp(Base.leakTraceRatio, 0f, 1f);
        if (traceAmount > 0 && ing.LeakTraceID != "")
        {
            var source = item is Item_Instance_Cum cum ? TraceSource.FromCum(cum) : new TraceSource();
            if (TraceUtility.Land(this, null, ing.LeakTraceID, traceAmount, source) == null) traceAmount = 0;
        }

        var room = Owner == null ? null : scr_System_CampaignManager.current.Map.FindRoomByChara(Owner.RefID);
        ing.amount = amount - traceAmount;
        if (ing.amount > 0.001f && room != null) room.AddItem(item);
        else scr_System_CampaignManager.current.Unregister(item);
    }

    public void UpdateTimeMinute(TimeSpan t)
    {
        if (!this.canContain) return;

        // TotalMinutes, not Minutes: the Minutes component is 0 for whole-hour spans (EndDormantState catch-up)
        int totalMinutes = (int)t.TotalMinutes;
        List<Item_Instance> delete = new List<Item_Instance>();
        foreach (var content in Contains)
        {
            var comp = content.Comp_Ingestible;
            var methods = comp == null ? new List<ItemComponentTemplate_Ingestible.Ingestible_IngestMethod>() : comp.ingestMethod.FindAll(x => this.hasTag(x.bodyTags));

            if (methods.Count > 0)
            {
                float amount = methods[0].digestSpeed;
                int digestMinutes = totalMinutes;
                if (ContainedRefs_Delays.TryGetValue(content.RefID, out var value) && value > 0)
                {
                    int timeTick = Math.Min(totalMinutes, value);
                    ContainedRefs_Delays[content.RefID] -= timeTick;
                    // only the part of t left after the delay ran out is spent digesting
                    digestMinutes = totalMinutes - timeTick;
                }

                // never digest past what's left, so a long span can't over-apply digestion statuses
                if (amount < 0 && comp.amount > 0) digestMinutes = Math.Min(digestMinutes, (int)Math.Ceiling(comp.amount / -amount));

                if (digestMinutes > 0)
                {
                    comp.amount += amount * digestMinutes;

                    foreach (var method in methods)
                    {
                        Digest(method, digestMinutes, amount);
                    }
                }

                if (comp.amount <= 0 && (!ContainedRefs_Delays.TryGetValue(content.RefID, out var remaining) || remaining <= 0)) delete.Add(content);
            }

        }
        foreach (Item_Instance item in delete)
        {
            ContainedRefs_Delays.Remove(item.RefID);
            scr_System_CampaignManager.current.Unregister(item);
        }
        if (delete.Count > 0) contains_cache = null;



        delete.Clear();
    }

    private void Digest(ItemComponentTemplate_Ingestible.Ingestible_IngestMethod method, int minutes, float amount)
    {
        if (method.giveStatus != null && method.giveStatus.Length > 0) this.owner.Stats.AddOrModStatus(method.giveStatus, -amount * method.amountMod * minutes);
    }

    public string baseID = "";

    private BodyInternal_Base basePointer = null;
    [JsonIgnore] public BodyInternal_Base Base
    {
        get
        {
            if (basePointer == null && baseID != "") basePointer = scr_System_Serializer.current.GetByNameOrID_BodyInternal_Base(baseID);
            return basePointer;
        }
    }

    [JsonProperty] List<Tuple<int, string>> lastInteactedRefs = new List<Tuple<int, string>>();
    private List<BodyInternal_Instance> lastinteractedRefs_cache = null;
    [JsonIgnore] public List<BodyInternal_Instance> LastInteactedRefs
    { get {
            if (lastInteactedRefs == null) lastInteactedRefs = new List<Tuple<int, string>>();
            if (lastinteractedRefs_cache == null)
            {
                lastinteractedRefs_cache = new List<BodyInternal_Instance>();

                foreach(var i in lastInteactedRefs)
                {
                    lastinteractedRefs_cache.Add( scr_System_CampaignManager.current.FindInstanceByID(i.Item1).Body.GetRandomInternalWithBaseID(i.Item2));
                }
            }
            return lastinteractedRefs_cache;
        } }
    public void LogLastInteractedRef(BodyInternal_Instance instance)
    {
        if (instance == null || instance.Owner == null) return;
        if (lastInteactedRefs == null) lastInteactedRefs = new List<Tuple<int, string>>();

        if (lastInteactedRefs.Find(x=>x.Item1 == instance.Owner.RefID && x.Item2 == instance.baseID) == null) lastInteactedRefs.Add(new Tuple<int, string>(instance.Owner.RefID, instance.baseID));
        lastinteractedRefs_cache = null;
        //if (scr_System_CentralControl.current.DLOG_NSFW.log_Cum) Debug.Log($"[LogLastInteractedRef] {Owner?.FirstName}.{baseID} <- {instance.Owner.FirstName}.{instance.baseID}");
    }
    public void ClearLastInteractedRefs()
    {
        if (scr_System_CentralControl.current.DLOG_NSFW.log_Cum && lastInteactedRefs != null && lastInteactedRefs.Count > 0)
        {
            List<string> ss = new List<string>();
            foreach(var i in lastInteactedRefs)
            {
                ss.Add($"{i.Item1} {i.Item2}");
            }

            Debug.Log($"[LogLastInteractedRef] {Owner?.FirstName}.{baseID} cleared, previously contains {ss.Count}\n{String.Join("\n", ss)}");
        }
        if (lastInteactedRefs == null) lastInteactedRefs = new List<Tuple<int, string>>();
        lastInteactedRefs.Clear();
        lastinteractedRefs_cache = null;
    }

    [JsonIgnore] public BodyPart_Instance Parent = null;

    public BodyInternal_Instance()
    {

    }

    public void ReEstablishParent(BodyPart_Instance c)
    {
        this.owner = c.Owner;
        this.ownerRefID = c.Owner.RefID;

        this.Parent = c;
        if (HasTraces) TraceUtility.FixSlots(this);

        if (this.womb != null)
        {
            womb.ReEstablishParent(this);
            Owner.RegisterWomb(this.womb);
        }
    }
    public bool Initialize(Character_Trainable cc, string baseID, BodyPart_Instance c)
    {
        ReEstablishParent(c);

        this.baseID = $"{cc.BaseID}_{baseID}";
        basePointer = scr_System_Serializer.current.GetByNameOrID_BodyInternal_Base(this.baseID);
        if (basePointer == null)
        {
            this.baseID = baseID;
            basePointer = scr_System_Serializer.current.GetByNameOrID_BodyInternal_Base(this.baseID);
        }

        if (Base == null) return false;

        if (Base.depthRatio != 0)
        {
            orifice_depth = (float)Owner.Body.Height * Base.depthRatio;
        }

        if (Base.sizeRatio != 0)
        {
            orifice_size = Mathf.Sqrt((float)(Owner.Body.Height * Owner.Body.Weight)) * Base.sizeRatio;// Base.sizeRatio == 0 ? 0 : Utility.RandVariation(Owner.Body.Height, 0.1f) * Base.sizeRatio;
        }

        if (Base.volumeRatio != 0)
        {
            volume_capacity = (float)Owner.Body.Height/100f * (float)Owner.Body.Weight * Base.volumeRatio;
            //Debug.Log($"Generating intenral volume, height {Owner.Body.Height} ratio {Base.volumeHeightRatio} mass {Base.volumeMassRatio} BMI {Owner.Body.BMI}, result {volume_capacity}");
        }

        contentsIndex.Clear();

        foreach (BodyEquipLayer i in equipLayers)
        {
            foreach (BodyPartEquipSlot j in availableSlots)
            {
                contentsIndex.Add(i.ToString()+"||"+j.ToString(), -1);
            }
        }

        return true;
    }

    [JsonIgnore] public Ranking Rank_Depth
    {
        get
        {
            //int counter = 0;
            Ranking previous = Ranking.None;
            // 3 6 9 12 15 18 21
            if (hasTag("vagina"))
            {
                foreach (float f in UtilityEX.Ranking_V_depth)
                {
                    if (orifice_depth >= f) previous = (Ranking)Array.IndexOf(UtilityEX.Ranking_V_depth, f);
                    else return previous;
                }
                return previous;
            }
            else if (hasTag("penis"))
            {
                foreach (float f in UtilityEX.Ranking_P_depth)
                {
                    if (orifice_depth >= f) previous = (Ranking)Array.IndexOf(UtilityEX.Ranking_P_depth, f);
                    else return previous;
                }
                return previous;
            }
            else if (hasTag("anus"))
            {
                foreach (float f in UtilityEX.Ranking_A_depth)
                {
                    if (orifice_depth >= f) previous = (Ranking)Array.IndexOf(UtilityEX.Ranking_A_depth, f);
                    else return previous;
                }
                return previous;
            }
            else if (hasTag("mouth"))
            {
                foreach (float f in UtilityEX.Ranking_M_depth)
                {
                    if (orifice_depth >= f) previous = (Ranking)Array.IndexOf(UtilityEX.Ranking_M_depth, f);
                    else return previous;
                }
                return previous;
            }
            else
            {
                return Ranking.None;
            }
        }
    }

    [JsonIgnore] public Ranking Rank_Size
    {
        get
        {
            //int counter = 0;
            Ranking previous = Ranking.None;
            // 3 6 9 12 15 18 21
            if (hasTag("vagina"))
            {
                foreach (float f in UtilityEX.Ranking_V_size)
                {
                    if (orifice_size >= f) previous = (Ranking)Array.IndexOf(UtilityEX.Ranking_V_size, f);
                    else return previous;
                }
                return previous;
            }
            else if (hasTag("penis"))
            {
                foreach (float f in UtilityEX.Ranking_P_size)
                {
                    if (orifice_size >= f) previous = (Ranking)Array.IndexOf(UtilityEX.Ranking_P_size, f);
                    else return previous;
                }
                return previous;
            }
            else if (hasTag("anus"))
            {
                foreach (float f in UtilityEX.Ranking_A_size)
                {
                    if (orifice_size >= f) previous = (Ranking)Array.IndexOf(UtilityEX.Ranking_A_size, f);
                    else return previous;
                }
                return previous;
            }
            else if (hasTag("mouth"))
            {
                foreach (float f in UtilityEX.Ranking_M_size)
                {
                    if (orifice_size >= f) previous = (Ranking)Array.IndexOf(UtilityEX.Ranking_M_size, f);
                    else return previous;
                }
                return previous;
            }
            else
            {
                return Ranking.None;
            }
        }
    }



    [JsonIgnore] public bool needLubrication { get { return Base.needLubrication; } }

    /// <summary>Owner-derived lubrication points; missing or ineligible definitions contribute zero.</summary>
    [JsonIgnore] public float DerivedLubrication
    {
        get
        {
            var definition = Base;
            if (definition == null || string.IsNullOrWhiteSpace(definition.lubricationStatID)) return 0;
            var stat = Owner?.Stats?.GetDerivedStat(definition.lubricationStatID);
            if (stat == null) return 0;
            float value = stat.FinalValue();
            return float.IsNaN(value) || float.IsInfinity(value) ? 0 : Math.Max(0, value);
        }
    }

    /// <summary>Lubrication points from all liquid currently in this organ, measured in ml.</summary>
    [JsonIgnore] public float ContainedLiquidLubricationBonus
    {
        get
        {
            var definition = Base;
            if (definition == null) return 0;
            float maximum = definition.liquidLubricationBonusMax;
            float halfAmount = definition.liquidLubricationHalfAmountMl;
            if (maximum <= 0 || float.IsNaN(maximum) || float.IsInfinity(maximum)
                || halfAmount <= 0 || float.IsNaN(halfAmount) || float.IsInfinity(halfAmount)) return 0;
            float amount = CurrentlyContainedLiquid;
            if (amount <= 0 || float.IsNaN(amount) || float.IsInfinity(amount)) return 0;
            float bonus = (float)(maximum / (1.0 + (double)halfAmount / amount));
            return float.IsNaN(bonus) || float.IsInfinity(bonus) ? 0 : Math.Max(0, bonus);
        }
    }

    /// <summary>Body-local lubrication points, evaluated live without storing liquid or queuing changes.</summary>
    [JsonIgnore] public float EffectiveLubrication
    {
        get
        {
            return (float)Math.Min(float.MaxValue, (double)DerivedLubrication + ContainedLiquidLubricationBonus);
        }
    }

    /// <summary>
    /// Signed sex-DC modifier while this organ is penetrated, from EffectiveLubrication and its definition's lubricationDC
    /// curve. False when the organ needs no lubrication or has no authored curve.
    /// </summary>
    public bool TryGetLubricationDCModifier(out int value)
    {
        value = 0;
        if (!needLubrication) return false;
        var def = Base;
        if (def.lubricationDCMin > def.lubricationDCMax || (def.lubricationDCScale == 0 && def.lubricationDCOffset == 0)
            || float.IsNaN(def.lubricationDCScale) || float.IsInfinity(def.lubricationDCScale)
            || float.IsNaN(def.lubricationDCOffset) || float.IsInfinity(def.lubricationDCOffset)) return false;
        float lubrication = EffectiveLubrication;
        if (float.IsNaN(lubrication) || float.IsInfinity(lubrication)) return false;
        value = (int)Math.Truncate(Math.Clamp((double)lubrication * def.lubricationDCScale + def.lubricationDCOffset, def.lubricationDCMin, def.lubricationDCMax));
        return true;
    }

    /// <summary> Lubricated enough that penetration takes no lubrication penalty (or needs no lubrication). </summary>
    [JsonIgnore] public bool IsSufficientlyLubricated { get { return !TryGetLubricationDCModifier(out int value) || value >= 0; } }


    protected SkillInstance _sensitivitySkill = null;
    [JsonIgnore] public SkillInstance SensitivitySkill
    {
        get
        {
            if (_sensitivitySkill == null && this.Base.sensitivityClassString != "")
            {
                _sensitivitySkill = Owner.GetSkill("skill_sensitivity_" + this.Base.sensitivityClassString);
            }
            return _sensitivitySkill;
        }
    }

    protected SkillInstance _expansionSkill = null;
    [JsonIgnore]
    public SkillInstance ExpansionSkill
    {
        get
        {
            if (_expansionSkill == null && this.canBePenetrated && this.Base.sensitivityClassString != "")
            {
                _expansionSkill = Owner.GetSkill("skill_expansion_" + this.Base.sensitivityClassString);
            }
            return _expansionSkill;
        }
    }

    protected Status_Instance GetOwnerStimulationStatus(string s)
    {
        switch (Base.sensitivityClassString)
        {
            case "C": return Owner.Stats.Sex_C;
            case "V": return Owner.Stats.Sex_V;
            case "A": return Owner.Stats.Sex_A;
            case "B": return Owner.Stats.Sex_B;
            case "W": return Owner.Stats.Sex_W;
            case "M": return Owner.Stats.Sex_M;
            default: return null;
        }
    }

    bool _cached_status = false;
    Status_Instance _status_cache = null;

    public void TryModOwnerStimulationStatus(float value)
    {
        if (!_cached_status)
        {
            _cached_status = true;
            _status_cache = GetOwnerStimulationStatus(this.Base.sensitivityClassString);
        }
        if (_status_cache == null) return;
        Owner.Stats.AddOrModStatus(_status_cache.ID, value);
    }

    [JsonIgnore]public int MaxSensitivity
    {
        get
        {
            if (Base.maxSensitivityStatString == "") return 0;
            //Debug.Log("Fetching " + Base.maxSensitivityStatString+", does it exist "+(scr_System_Serializer.current.GetByNameOrID_StatsDerivedBase(Base.maxSensitivityStatString) != null));
            var stat = Owner.Stats.GetDerivedStat(Base.maxSensitivityStatString);
            if (stat == null) return 0;
            return (int)(stat.FinalValue() * 100);
        }
    }

    [JsonProperty] private float orifice_depth = 0;
    [JsonIgnore] public float Depth
    {
        get { return orifice_depth; }
    }
    [JsonIgnore]
    public float CurrentDepth
    {
        get
        {
            if (this.orifice_depth == 0 || Owner.Stats.SexStimulation == null) return this.orifice_depth;
            float mod = Owner.Stats.SexStimulation.Severity * 0.02f;
            return mod * MaxDepth + (1 - mod) * Depth;
        }
    }
    [JsonIgnore] public float MaxDepth
    {
        get { return orifice_depth * Base.aroused_depthMod; }
    }

    [JsonProperty] private float orifice_size = 0;
    [JsonIgnore] public float Size
    {
        get { if (orifice_size == 0) return 0;
            if (this.ExpansionSkill == null) return orifice_size;
            return orifice_size * ( 1 + this.ExpansionSkill.GetSkillLevel * 0.1f); }
    }
    [JsonIgnore]
    public float MaxSize
    {
        get { return Size * (Base.stretch_ratio + Base.aroused_stretchMod + (this.SensitivitySkill != null ? this.SensitivitySkill.GetSkillLevel * 0.05f : 0f)); }
    }

    [JsonIgnore]
    public float CurrentSize
    {
        get
        {
            if (Size == 0) return 0;

            float mod = (Owner?.Stats?.SexStimulation?.Severity ?? 0) * 0.02f;
            if (needLubrication)
            {
                float lubrication = EffectiveLubrication;
                if (lubrication <= 66) mod *= (0.015f * lubrication);
            }

            return MaxSize * mod + Size * (1 - mod);
           // if (hasTag("arousalExpansion")) modifier += Math.Max(0, Math.Max(Owner.Stats.Lust != null ? Owner.Stats.Lust.Severity : 0, Owner.Stats.Stress != null ? Owner.Stats.Stress.Severity : 0) * 0.2f);

        }
    }

    [JsonIgnore] public bool canDigest
    {
        get
        {
            return hasTag("stomach") || hasTag("anus");
        }
    }

    [JsonIgnore] public bool canBeFucked
    {
        get
        {
            return orifice_depth > 0 && orifice_size > 0;
        }
    }

    [JsonIgnore] public bool canBeStimulated
    {
        get
        {
            return Sensitivity != "";
        }
    }

    [JsonIgnore] public bool canBePenetrated
    {
        get
        {
            return canBeFucked && hasTag("mouth") || hasTag("vagina") || hasTag("womb") || hasTag("urethra") || hasTag("anus") || hasTag("nipple");
        }
    }

    [JsonIgnore] public bool canFuck
    {
        get
        {
            return hasTag("penis") || hasTag("penetration");
        }
    }

    [JsonIgnore] public bool canContain
    {
        get
        {
            return volume_capacity > 0 && Base.tags.Contains("internal");
        }
    }

    [JsonIgnore] public bool canOverflowIn { get { return Base.canOverflowIn; } }
    [JsonIgnore] public bool canOverflowOut { get { return Base.canOverflowOut; } }

    [JsonIgnore] public string overflowOutTag { get { return Base.tag_directionOut; } }
    [JsonIgnore] public string overflowInTag { get { return Base.tag_directionIn; } }

    protected int ownerRefID = -1;
    private Character_Trainable owner = null;
    [JsonIgnore] public Character_Trainable Owner
    {
        get
        {
            if ( owner == null)
            {
                if (ownerRefID < 0) return null;
                owner = scr_System_CampaignManager.current.FindInstanceByID(ownerRefID);
            }
            return owner;
        }
    }

    // contains
    protected List<Item_Instance> contains_cache = null;
    [JsonIgnore] public List<Item_Instance> Contains
    {
        get
        {
            if (contains_cache == null)
            {
                contains_cache = new List<Item_Instance>();
                foreach(var kvp in ContainedRefs_Delays) contains_cache.Add(scr_System_CampaignManager.current.FindItemInstanceByID(kvp.Key));
            }
            return contains_cache;
        }
    }

    public bool HasContent(int i)
    {
        return ContainedRefs_Delays.ContainsKey(i);
    }

    public bool ExtractContent(int i)
    {
        if (ContainedRefs_Delays.TryGetValue(i, out var item))
        {
            ContainedRefs_Delays.Remove(i);
            contains_cache = null;
            return true;
        }
        else return false;
    }

    public bool HasContent(Item_Instance i)
    {
        if (i == null) return false;
        return ContainedRefs_Delays.ContainsKey(i.RefID);
    }

    [JsonIgnore]
    public bool ContainsCum { get
        {
            if (this.Contains == null) return false;
            foreach (var i in this.Contains) if (i is Item_Instance_Cum && i.Comp_Ingestible.amount > 0) return true;
            return false;
        } }

    [JsonProperty] Dictionary<int, int> ContainedRefs_Delays = new Dictionary<int, int>();
    public float volume_capacity = 0;
    [JsonIgnore] public bool containsOverCapacity
    {
        get
        {
            return CurrentlyContained >= MaxCapacity;
        }
    }

    [JsonIgnore]
    public float VolumeCapacity { get
        {
            return this.canContain ? volume_capacity : 0;
        } }

    [JsonIgnore]
    public float CumVolume
    { get
        {
            return this.canCum && this.volume_capacity > 0 ? this.volume_capacity : 0;
        } }

    [JsonIgnore]
    public bool canCum
    {
        get
        {
            return hasTag("penis");
        }
    }

    [JsonIgnore]
    public bool isVisiblyExpanded
    {
        get
        {
            return this.canContain && CurrentlyContained >= VolumeCapacity;
        }
    }
    [JsonIgnore]
    public bool isExtremelyExpanded
    { get
        {
            return this.canContain && CurrentlyContained >= VisiblyExpandedCapacity;
        } }

    [JsonIgnore]
    public bool canDeflate
    {
        get
        {
            if (!this.canContain || !isVisiblyExpanded) return false;
            foreach (var item in Contains)
            {
                var ing = item.Comp_Ingestible;
                if (ing != null && ing.CanBeExpelled) return true;
            }
            return false;
        }
    }
    [JsonIgnore]
    public bool canFullyDeflate
    {
        get
        {
            if (!this.canContain) return false;
            foreach (var item in Contains)
            {
                var ing = item.Comp_Ingestible;
                if (ing != null && ing.CanBeExpelled) return true;
            }
            return false;
        }
    }

    /// <summary>Read-only check for a positive removal using the same mode and arithmetic as Deflate.</summary>
    public bool WouldDeflate(bool fullDeflate)
    {
        if (fullDeflate ? !canFullyDeflate : !canDeflate) return false;
        var contents = Contains;
        float expellableTotal = 0f, nonExpellableTotal = 0f;
        foreach (var item in contents)
        {
            var ing = item.Comp_Ingestible;
            if (ing == null) continue;
            if (ing.CanBeExpelled) expellableTotal += ing.amount;
            else nonExpellableTotal += ing.amount;
        }
        if (expellableTotal <= 0f) return false;

        float targetExpellableTotal = fullDeflate ? 0f : Math.Max(0f, VolumeCapacity - nonExpellableTotal);
        float ratio = targetExpellableTotal / expellableTotal;
        foreach (var item in contents)
        {
            var ing = item.Comp_Ingestible;
            if (ing != null && ing.CanBeExpelled && ing.amount - ing.amount * ratio > 0f) return true;
        }
        return false;
    }

    /// <summary>
    /// Proportionally removes expellable content from this part, preserving the pre-expulsion ratio between
    /// expellable items and leaving non-expellable content untouched (fullDeflate forces the expellable target
    /// to 0, otherwise the target is whatever room the visibly-expanded threshold leaves after non-expellable
    /// content). Returns every Item_Instance that left the body: fully-removed items are returned as-is (their
    /// own amount already equals what left); partially-reduced items are split off into a fresh instance holding
    /// exactly the removed amount, so its Print()/amount always reflects what was expelled, never the item's
    /// remaining or original total. Does NOT decide what happens to the returned items (delete vs. drop in
    /// room, etc.) — that's the caller's call.
    /// </summary>
    public List<Item_Instance> Deflate(bool fullDeflate)
    {
        var contents = this.Contains;

        float expellableTotal = 0f, nonExpellableTotal = 0f;
        for (int i = 0; i < contents.Count; i++)
        {
            var ing = contents[i].Comp_Ingestible;
            if (ing == null) continue;
            if (ing.CanBeExpelled) expellableTotal += ing.amount;
            else nonExpellableTotal += ing.amount;
        }
        if (expellableTotal <= 0f) return null;

        float targetExpellableTotal = fullDeflate ? 0f : Math.Max(0f, this.VolumeCapacity - nonExpellableTotal);
        float ratio = targetExpellableTotal / expellableTotal;

        List<Item_Instance> expelled = null;
        for (int i = 0; i < contents.Count; i++)
        {
            var item = contents[i];
            var ing = item.Comp_Ingestible;
            if (ing == null || !ing.CanBeExpelled) continue;

            float newAmount = ing.amount * ratio;
            float removed = ing.amount - newAmount;
            if (removed <= 0f) continue;

            if (expelled == null) expelled = new List<Item_Instance>();

            if (newAmount <= 0.001f)
            {
                // fully removed: the item itself leaves the body, its amount already equals what was removed
                this.ExtractContent(item.RefID);
                expelled.Add(item);
            }
            else
            {
                // partially reduced: the item stays put, a fresh instance is split off holding just the removed amount
                ing.amount = newAmount;
                var splitOff = SplitIngestible(item, removed);
                if (splitOff != null) expelled.Add(splitOff);
            }
        }

        return expelled;
    }

    static Item_Instance SplitIngestible(Item_Instance source, float amount)
    {
        if (source is Item_Instance_Cum cum)
        {
            var newCum = new Item_Instance_Cum(cum.raceID, cum.baseID, cum.templateID, cum.nameOverwrite);
            newCum.CumAmount = amount;
            return newCum;
        }

        var instance = WorldManager.Instantiate(source.BaseID, source.nameOverwrite, 1);
        var ing = instance?.Comp_Ingestible;
        if (ing == null) return null;
        instance.extraTooltip = source.extraTooltip;
        ing.amount = amount;
        return instance;
    }

    [JsonIgnore]
    public float VisiblyExpandedCapacity
    {
        get
        {
            return VolumeCapacity > 0 ? this.VolumeCapacity * Base.visibleExpansionRatio : 0;
        }
    }
    [JsonIgnore] public float MaxCapacity
    {
        get
        {
            var basecap = VolumeCapacity > 0 ? this.VolumeCapacity * Base.maxExpansionRatio : 0;
            if (isPregnant)
            {
                basecap += womb.GetVolumeMod();
            }
            return basecap;
        }
    }

    [JsonIgnore]
    public float CurrentlyContained
    {
        get
        {
            float sum = 0;
            foreach (var i in Contains) sum += i.Comp_Ingestible.amount;
            return sum;
        }
    }

    [JsonIgnore]
    public float RemainingCapacity
    {
        get
        {
            return VolumeCapacity - CurrentlyContained;
        }
    }
    [JsonIgnore]
    public float RemainingExpandingCapacity
    {
        get
        {
            return VisiblyExpandedCapacity - CurrentlyContained;
        }
    }
    [JsonIgnore]
    public float RemainingMaxCapacity
    {
        get
        {
            return MaxCapacity - CurrentlyContained;
        }
    }

    [JsonIgnore]
    public float MaxCapacityPercentage
    {
        get
        {
            return CurrentlyContained / MaxCapacity;
        }
    }

    [JsonIgnore]
    public float CurrentlyContainedLiquid
    {
        get
        {
            float sum = 0;
            foreach (var i in Contains)
            {
                var ingestible = i?.Comp_Ingestible;
                if (ingestible == null || !ingestible.isLiquid) continue;
                float amount = ingestible.amount;
                if (amount <= 0 || float.IsNaN(amount) || float.IsInfinity(amount)) continue;
                sum = amount > float.MaxValue - sum ? float.MaxValue : sum + amount;
            }
            return sum;
        }
    }

    [JsonIgnore]
    public float MaxCapacityPercentageLiquid
    {
        get
        {
            return CurrentlyContainedLiquid / MaxCapacity;
        }
    }
    [JsonIgnore]
    public float ExpandedCapacityPercentage
    {
        get
        {
            return CurrentlyContained / VisiblyExpandedCapacity;
        }
    }
    [JsonIgnore]
    public float ExpandedCapacityPercentageLiquid
    {
        get
        {
            return CurrentlyContainedLiquid / VisiblyExpandedCapacity;
        }
    }

    [JsonIgnore] public string Sensitivity
    {
        get
        {
            return scr_System_Serializer.current.GetSensitivityStatus(this.Base.sensitivityClassString);
        }
    }
    [JsonIgnore]
    public string traitID
    {
        get
        {
            return this.Base.traitClassString != "" ? this.Base.traitClassString : this.Base.sensitivityClassString;
        }
    }


    [JsonIgnore]
    public Traits SizeTrait
    {
        get
        {
            switch (this.traitID)
            {
                case "B":
                case "P":
                    if (this.Owner == null || this.Owner.Body == null) return null;
                    return this.Owner.Body.GetBodyTrait("trait_Size_" + this.traitID);
                default:return null;
            }
        }
    }

    protected void AddExperience(float amount, List<string> extra = null, ExperienceLog m = null, ISet<string> forbiddenExperienceIDs = null)
    {
        var tags = new List<string>();
        tags.AddRange(this.Base.tags);
        if (extra != null) tags.AddRange(extra);
        if (Owner.Body.isClimaxing(false, true)) tags.Add("climax");
        if (this.Sensitivity != "") tags.Add(this.Sensitivity);
        Owner.Skills.CheckExperienceGain(tags, amount, m, forbiddenExperienceIDs);
    }

    /*
    public bool Ingest_(Item_Instance item, ExperienceLog m = null, bool forceFill = false, List<BodyInternal_Instance> fillHistory = null)
    {
        if (!this.canContain) return false;
        var comp = item.Comp_Ingestible;

        bool isCum = item is Item_Instance_Cum;
        if (isCum)
        {
            var com = item as Item_Instance_Cum;
            if (!com.experienceTicked)
            {
                com.experienceTicked = true;
                int exp = Math.Abs((int)(comp.amount / 10)) + 1;

                AddExperience(comp.amount, new List<string>() { "cum" }, m);

                if (cumStimulation != 0)
                {
                    string s = Sensitivity;
                    if (s != "") Owner.Stats.AddOrModStatus(s, comp.amount * cumStimulation);
                }
            }
        }


        if (!isCum || forceFill)
        {
            IngestInternal(item, m);
            return true;
        }
        else if(RemainingExpandingCapacity >= comp.amount || comp.amount < 1)
        {
            IngestInternal(item, m);
            return true;
        }
        else
        {
            Debug.Log($"{Owner.CallName} {this.DisplayName} ingest {comp.amount} ml, RemainingExpandingCapacity {RemainingExpandingCapacity}, full capacity ingest");

            if (fillHistory == null) fillHistory = new List<BodyInternal_Instance>() { this };
            else fillHistory.Add(this);

            BodyInternal_Instance directionOut = (Base.tag_directionOut == "" || Base.tag_directionOut == "ext") ? null : Owner.Body.GetRandomInternalWithTag(Base.tag_directionOut);
            BodyInternal_Instance directionIn = (Base.tag_directionIn == "" || Base.tag_directionIn == "ext") ? null : Owner.Body.GetRandomInternalWithTag(Base.tag_directionIn);

            if (directionIn != null && directionIn != this && directionIn.canContain && !fillHistory.Contains(directionIn) && directionIn.Ingest(item, m, forceFill, fillHistory)) return true;
            else if (RemainingMaxCapacity >= comp.amount)
            {
                IngestInternal(item, m);
                return true;
            }
            else
            {
                if (RemainingMaxCapacity >= 1)
                {
                    var newcap = Math.Min(RemainingMaxCapacity, comp.amount);
                    if (item is Item_Instance_Cum)
                    {
                        var cum = item as Item_Instance_Cum;
                        Item_Instance_Cum newitem = WorldManager.Instantiate(cum.Owner, cum.nameOverwrite);
                        newitem.CumAmount = newcap;
                        IngestInternal(newitem, m);
                    }
                    else
                    {
                        Item_Instance newitem = WorldManager.Instantiate(item.BaseID, item.nameOverwrite, item.Count);
                        newitem.Comp_Ingestible.amount = newcap;
                        IngestInternal(newitem, m);
                    }
                    comp.amount -= newcap;
                }

                if (directionOut != null && directionOut != this && directionOut.canContain && !fillHistory.Contains(directionOut) && directionOut.Ingest(item, m, forceFill, fillHistory)) return true;
                else return false;
            }
            
            if (RemainingExpandingCapacity >= 1)
            {
                var newcap = Math.Min(RemainingExpandingCapacity, comp.amount);
                if (item is Item_Instance_Cum)
                {
                    var cum = item as Item_Instance_Cum;
                    Item_Instance_Cum newitem = WorldManager.Instantiate(cum.Owner, cum.nameOverwrite);
                    newitem.Comp_Ingestible.amount = newcap;
                    IngestInternal(newitem, m);
                }
                else
                {
                    Item_Instance newitem = WorldManager.Instantiate(item.BaseID, item.nameOverwrite, item.Count);
                    newitem.Comp_Ingestible.amount = newcap;
                    IngestInternal(newitem, m);
                }
                comp.amount -= newcap;
                return false;
            }
        }

    }*/

    // ── New ingest logic (standalone for testing, replaces Ingest once verified) ──────────────
    // Non-cum: always ingests. Cum phases:
    //   1. Fits in expanding capacity → ingest here.
    //   2. Try directionIn (same expanding-capacity rule, recursive).
    //   3. directionIn exhausted → fits in max capacity → ingest here.
    //   4. Over max capacity → ingest here, push existing cum out via directionOut.
    public bool Ingest(Item_Instance item, ExperienceLog m = null, bool forceFill = false, List<BodyInternal_Instance> fillHistory = null, bool ignoreClimaxAppend = false, ISet<string> forbiddenExperienceIDs = null)
    {
        if (!this.canContain) return false;
        var comp = item.Comp_Ingestible;

        bool isCum = item is Item_Instance_Cum;
        if (isCum)
        {
            var com = item as Item_Instance_Cum;
            if (!com.experienceTicked)
            {
                com.experienceTicked = true;
                AddExperience(comp.amount, new List<string>() { "cum" }, m, forbiddenExperienceIDs);
                if (cumStimulation != 0)
                {
                    string s = Sensitivity;
                    if (s != "") Owner.Stats.AddOrModStatus(s, comp.amount * cumStimulation);
                }
            }
        }

        // Non-cum and forced fills bypass all capacity checks.
        if (!isCum)
        {
            IngestInternal(item, ignoreClimaxAppend ? null : m);
            return true;
        }

        // ── Cum routing ──────────────────────────────────────────────────────────
        if (fillHistory == null) fillHistory = new List<BodyInternal_Instance>() { this };
        else if (!fillHistory.Contains(this)) fillHistory.Add(this);

        // Phase 1: fits within expanding capacity.
        if (RemainingExpandingCapacity >= comp.amount || comp.amount < 1)
        {
            IngestInternal(item, ignoreClimaxAppend ? null : m);
            return true;
        }

        // Phase 2: push inward, same expanding-capacity rule.
        BodyInternal_Instance dirIn = (Base.tag_directionIn == "" || Base.tag_directionIn == "ext")
            ? null : Owner.Body.GetRandomInternalWithTag(Base.tag_directionIn);

        if (dirIn != null && dirIn != this && dirIn.canContain && !fillHistory.Contains(dirIn))
        {
            if (dirIn.Ingest(item, m, false, fillHistory, ignoreClimaxAppend: ignoreClimaxAppend, forbiddenExperienceIDs: forbiddenExperienceIDs)) return true;
        }

        // Phase 3: directionIn chain exhausted — try max capacity.
        if (RemainingMaxCapacity >= comp.amount)
        {
            IngestInternal(item, ignoreClimaxAppend ? null : m);
            return true;
        }

        // Phase 4: over max capacity — push existing cum out first, then ingest new load.
        float overflow4 = comp.amount - RemainingMaxCapacity;
        Debug.Log($"[Ingest] {Owner.CallName} {DisplayName}: over max capacity ({CurrentlyContained:F1}/{MaxCapacity:F1}), pushing {overflow4:F1}ml of existing cum out.");
        PushCumOverflowOut(overflow4, m, fillHistory);
        IngestInternal(item, ignoreClimaxAppend ? null : m);
        return true;
    }

    // Extracts amountToPush of existing cum from this organ (each father's share alike) and routes it outward via
    // directionOut; a direction leading out of the body (ext, or vagina -> clit) spills it (SpillToOutside: trace +
    // floor). Called BEFORE ingesting the new item so the new load is never displaced. Non-cum contents are never touched.
    private void PushCumOverflowOut(float amountToPush, ExperienceLog m, List<BodyInternal_Instance> fillHistory)
    {
        if (amountToPush <= 0f) return;

        BodyInternal_Instance dirOut = FlowTarget(Base.tag_directionOut);
        bool outside = dirOut == null && LeadsOutside(Base.tag_directionOut);
        if (dirOut == null && !outside) return;
        if (dirOut != null && fillHistory != null && fillHistory.Contains(dirOut)) return;

        // not the static flow buffer: IngestAtMaxCapacity can come back here for the next organ out
        var pushed = new List<Item_Instance_Cum>();
        if (TakeCum(amountToPush, pushed) <= 0f) return;

        if (outside)
        {
            for (int i = 0; i < pushed.Count; i++) SpillToOutside(pushed[i]);
            return;
        }

        if (fillHistory == null) fillHistory = new List<BodyInternal_Instance>();
        fillHistory.Add(dirOut);
        for (int i = 0; i < pushed.Count; i++) dirOut.IngestAtMaxCapacity(pushed[i], m, fillHistory);
    }

    /// <summary>
    /// Pushes the cum above MaxCapacity out through directionOut, organ by organ, until it spills out of the body
    /// (womb -> vagina -> floor + trace). Ingest alone only pushes out cum that was already here, so one load bigger than
    /// the organ stays inside whole; call this after it when the overflow should leave (console wombAddSperm* allowExpel).
    /// </summary>
    public void SettleOverflow(ExperienceLog m = null, List<BodyInternal_Instance> fillHistory = null)
    {
        if (!this.canContain) return;
        float over = CurrentlyContained - MaxCapacity;
        if (over <= 0.001f) return;
        if (fillHistory == null) fillHistory = new List<BodyInternal_Instance>();
        if (!fillHistory.Contains(this)) fillHistory.Add(this);

        var dirOut = FlowTarget(Base.tag_directionOut);
        bool alreadyVisited = dirOut != null && fillHistory.Contains(dirOut);
        PushCumOverflowOut(over, m, fillHistory);
        if (dirOut != null && !alreadyVisited) dirOut.SettleOverflow(m, fillHistory);
        Owner?.Body?.NotifyInternalsContentChange();
    }

    // Used by PushCumOverflowOut to route pushed cum through the directionOut chain.
    // Accepts up to MaxCapacity; if still over, cascades further out.
    // Does not re-tick experience — pushed items are already accounted for.
    private void IngestAtMaxCapacity(Item_Instance item, ExperienceLog m, List<BodyInternal_Instance> fillHistory)
    {
        if (!this.canContain) return;
        if (!fillHistory.Contains(this)) fillHistory.Add(this);

        var comp = item.Comp_Ingestible;
        if (RemainingMaxCapacity >= comp.amount)
        {
            IngestInternal(item, m);
            return;
        }

        // Still over max — push existing cum out first, then accept.
        float overflow = comp.amount - RemainingMaxCapacity;
        PushCumOverflowOut(overflow, m, fillHistory);
        IngestInternal(item, m);
    }
    // ────────────────────────────────────────────────────────────────────────────

    protected void IngestInternal(Item_Instance i, ExperienceLog m = null)
    {
        var comp = i.Comp_Ingestible;

        if (comp != null && m != null && i is Item_Instance_Cum)
        {
            var cum = (i as Item_Instance_Cum);
            if (cum != null && cum.Owner != null)
            {
                m.AppendClimaxMSG(cum.Owner.RefID,
                    LocalizeDictionary.QueryThenParse("experience_sex_cumtainer_single")
                        .Replace("$partname$", this.DisplayNameFull)
                        .Replace("$amount$", $"{comp.amount.ToString("N0")}"));
            }
            else if (cum != null)
            {
                m.AppendClimaxMSG(owner.RefID,
                    LocalizeDictionary.QueryThenParse("experience_sex_cumtainer_ownerless")
                        .Replace("$partname$", this.DisplayNameFull)
                        .Replace("$amount$", $"{comp.amount.ToString("N0")}")
                        .Replace("$sourcename$", cum.DisplayName));
            }
        }

        UtilityEX.ApplyOnConsume(this, comp.OnUseEffects);
        var method = comp == null ? null : comp.GetIngestMethod(this.Base.tags);
        if (comp != null)
        {
            string key = method != null && method.ingestionMsgString != "" ? method.ingestionMsgString :
                    comp.isLiquid ? Base.memory_ingest_liquid : Base.memory_ingest_solid;
            if (key != "")
            {
                string memstring = LocalizeDictionary.QueryThenParse(key);
                memstring = memstring.Replace("$itemname$", i.DisplayName).Replace("$amount$", comp.isLiquid ? comp.amount.ToString("F1") : i.Count.ToString());

                var memInst2 = new MemInstance(new List<int>() { Owner.RefID }, new List<string>(), "", -1, -1, false, Memory_Response.Accept, Memory_Attitude.None, memstring);
                Owner.Memory.AddEntry(memInst2, null, -1, true);
            }
        }

        if (i is Item_Instance_Cum)
        {
            var cum = i as Item_Instance_Cum;
            foreach (var kvpair in Contains)
            {
                if (kvpair is Item_Instance_Cum && (kvpair as Item_Instance_Cum).Merge(cum))
                {
                    return;
                }
            }
        }
        else if (comp != null && comp.isLiquid)
        {
            // Liquids of the same kind combine into one stomach entry instead of each sitting as its
            // own separately-timed ContainedRefs_Delays entry and digesting in parallel. canMergeComp
            // (unlike canStackWith) doesn't require identical amount/addons, since the whole point here
            // is combining two differently-digested/possibly-doctored amounts - MergeLiquid handles
            // rescaling addons and summing amount correctly.
            foreach (var kvpair in Contains)
            {
                if (kvpair.canMergeComp(i))
                {
                    kvpair.Comp_Ingestible.MergeLiquid(comp);
                    scr_System_CampaignManager.current.Unregister(i);
                    return;
                }
            }
        }
        else if (i.Stackable)
        {
           // Debug.Log("Tryingest item ["+i.DisplayName+"] stackable!");
            foreach(var kvpair in Contains)
            {
                if (kvpair.BaseID == i.BaseID && kvpair.DisplayName == i.DisplayName)
                {
                    //consumed = true;
                    kvpair.Comp_Ingestible.amount += comp.amount;
                    scr_System_CampaignManager.current.Unregister(i);
                    return;
                }
            }
        }

       // if (!contain.ContainsKey(kvp.Key)) DigestDelays.Add(kvp.Key, Utility.RandVariation(method.digestDelay, method.digestDelayVariation));
        this.ContainedRefs_Delays.Add(i.RefID, method == null ? 0 : (int)Utility.RandVariation(method.digestDelay, method.digestDelayVariation));
        contains_cache = null;
    }

    private string OwnerName
    {
        get
        {
            if (Owner != null) return Owner.FirstName;
            else return "";
        }
    }
    private string OwnerRace
    {
        get
        {
            if (Owner != null) return Owner.Race.DisplayName;
            else return "";
        }
    }

    [JsonIgnore] public string DisplayName { get { return Base.DisplayName; } }

    string _displayNameFull = string.Empty;
    [JsonIgnore] public string DisplayNameFull { get { 
            if (_displayNameFull == string.Empty)
            {
                _displayNameFull = LocalizeDictionary.QueryThenParse("bodyPart_Fulldisplayname")
                    .Replace("$name$", OwnerName)
                    .Replace("$part$", DisplayName);
            }
            return _displayNameFull; } }

    [JsonIgnore] public string Tooltip
    {
        get
        {
            return String.Format(Base.Tooltip, OwnerRace);
        }
    }

    public bool hasTag(string tag)
    {
        return Base.tags.Contains(tag);
    }


    public bool hasAnyTag(List<string> tag)
    {
        return Utility.ListContainsLoose(Base.tags, tag);// Base.tags.Contains(tag);
    }
    [JsonIgnore] public int sortOrder
    {
        get { return Base.sortOrder; }
    }

    public Item_Instance Cum(float amount = 1, ExperienceLog m = null)
    {
        if (!this.canFuck)
        {
            //Owner.Status.AddOrModStatus("chara_status_sex_climax_after", -50);
            //if (m != null) m.AddMessage(Owner.FirstName + " Climaxed!");
            return null;
        }
        else
        {
            Item_Instance_Cum cum = WorldManager.Instantiate(this.owner);
            cum.CumAmount = amount;

            // Owner.Status.AddOrModStatus("chara_status_sex_climax_after", -120);
            //if (m != null) m.AddMessage(Owner.FirstName + " Cum [" + amount + "]ml !");
            return cum;
        }
    }

    [JsonIgnore] public List<BodyPartEquipSlot> availableSlots { get { return Base.AvailableSlots; } }
    [JsonIgnore] public List<BodyEquipLayer> equipLayers { get { return Base.equipLayers; } }

    /// <summary> coverage of this organ: the parent part's Base.coverSlots (all parent slots if empty). Below 2 = visible </summary>
    public int GetRevealingScore(BodyEquipLayer layer)
    {
        if (Parent == null) return 0;
        var slots = Base.coverSlots != null && Base.coverSlots.Count > 0 ? Base.coverSlots : Parent.availableSlots;
        return Parent.GetRevealingScore(layer, slots);
    }

    /// <param name="movedTags">if given, the item tags of every item the target actually took are added to it (no duplicates)</param>
    public bool TransferContentTo(BodyInternal_Instance instance, bool fullDeflate, ExperienceLog m = null, List<string> movedTags = null)
    {
        if (instance == null || !instance.canContain) return false;
        var items = this.Deflate(fullDeflate);
        if (items == null || items.Count < 1) return false;

        bool any = false;
        foreach (var item in items)
        {
            Item_Instance_Cum cum = item is Item_Instance_Cum ? item as Item_Instance_Cum : null;

            if (cum != null) cum.experienceTicked = false;

            if (instance.Ingest(item, m, ignoreClimaxAppend : true))
            {
                any = true;
                if (movedTags != null)
                {
                    var itemTags = item.Tags;
                    for (int t = 0; t < itemTags.Count; t++) if (!movedTags.Contains(itemTags[t])) movedTags.Add(itemTags[t]);
                }
            }
            else
            {
                if (cum != null) cum.experienceTicked = true;
                this.IngestInternal(item);
            }
        }
        return any;
    }

    [JsonIgnore] public List<int> EquippedRefIDs { get { if (contentsIndex == null) return new List<int>();
            var v = contentsIndex.Values.ToList();
            v.RemoveAll(x => x == -1);
            return v;
        } }

    //List<Item_Instance> equippedItems;
    [JsonProperty] Dictionary<string, int> contentsIndex = new Dictionary<string, int>();

    /// <summary>
    /// Return -1 fail, return 0 success return 1+ swapped gear
    /// </summary>
    /// <param name="itemRefID"></param>
    /// <param name="forceEquip"></param>
    /// <returns></returns>
    public bool EquipItem(Item_Instance item, ref List<BodyPartEquipSlot> slots, bool forceEquip = false)
    {
        var comp = item.Comp_Equippable;
        bool equipped = false;
        EnsureSlotKeys();

        for (int i = slots.Count - 1; i >= 0; i--)
        {
            var slot = slots[i];
            string Tuple = comp.equipLayer.ToString() + "||" + slot.ToString();
            /// CODE START
            if (contentsIndex.ContainsKey(Tuple))
            {
                if (contentsIndex[Tuple] == -1)
                {
                    contentsIndex[Tuple] = item.RefID;
                    // equip success
                    equipped = true;
                    slots.Remove(slot);
                }
                else if (contentsIndex[Tuple] != -1 && contentsIndex[Tuple] != item.RefID && forceEquip == true)
                {
                    var returnVal = contentsIndex[Tuple];
                    contentsIndex[Tuple] = item.RefID;

                    Owner.UnequipItem(returnVal);
                    // replace equipped
                    equipped = true;
                    slots.Remove(slot);
                }
            }
            /// CODE END
        }
        return equipped;
    }

    /// <summary>
    /// Adds the equip keys (layer||slot) the organ's definition lists but this instance's saved table lacks - slots
    /// added to a definition later (e.g. Penis) then work on existing characters without re-initializing the organ.
    /// </summary>
    void EnsureSlotKeys()
    {
        if (contentsIndex == null || Base == null || equipLayers == null || availableSlots == null) return;
        foreach (BodyEquipLayer layer in equipLayers)
            foreach (BodyPartEquipSlot slot in availableSlots)
            {
                var key = layer.ToString() + "||" + slot.ToString();
                if (!contentsIndex.ContainsKey(key)) contentsIndex.Add(key, -1);
            }
    }

    /// <summary>
    /// Mirrors EquipItem(item) without changing anything: true if this organ would take the item;
    /// replaces = that would displace another item.
    /// </summary>
    public bool PreviewEquip(Item_Instance item, out bool replaces)
    {
        replaces = false;
        var comp = item.Comp_Equippable;
        if (comp == null || contentsIndex == null) return false;
        EnsureSlotKeys();
        bool equipped = false, unequip = false, displaces = false;
        foreach (var slot in comp.equipSlot)
        {
            string Tuple = comp.equipLayer.ToString() + "||" + slot.ToString();
            if (contentsIndex.TryGetValue(Tuple, out var current))
            {
                if (current == -1) equipped = true;
                else if (current != item.RefID)
                {
                    equipped = true;
                    displaces = true;
                }
            }
            else
            {
                if (equipped) unequip = true;
                equipped = false;
            }
        }
        if (unequip) equipped = false;
        replaces = equipped && displaces;
        return equipped;
    }

    public bool EquipItem(Item_Instance item, bool forceEquip = false)
    {
        var comp = item.Comp_Equippable;
        bool equipped = false;
        bool unequip = false;
        EnsureSlotKeys();

        foreach(var slot in comp.equipSlot)
        {
            string Tuple = comp.equipLayer.ToString() + "||" + slot.ToString();
            /// CODE START
            if (contentsIndex.ContainsKey(Tuple))
            {
                if (contentsIndex[Tuple] == -1)
                {
                    contentsIndex[Tuple] = item.RefID;
                    // equip success
                    equipped = true;
                }
                else if (contentsIndex[Tuple] == item.RefID)
                {
                    // do nothing
                }
                else
                {
                    var returnVal = contentsIndex[Tuple];
                    contentsIndex[Tuple] = item.RefID;

                    Owner.UnequipItem(returnVal);
                    // replace equipped
                    equipped = true;
                }
            }
            else
            {
                if (equipped) unequip = true;
                equipped = false;
            }
            /// CODE END
        }

        if (unequip)
        {
            UnequipItem(item.RefID);
            equipped = false;
        }

        return equipped;
    }

    public int EquipItemDirect(int itemRefID, BodyPartEquipSlot slot, BodyEquipLayer layer)
    {
        int returnval = -1;

        string Tuple = layer.ToString() + "||" + slot.ToString();
        if (contentsIndex.ContainsKey(Tuple))
        {
            if (contentsIndex[Tuple] == -1)
            {
                contentsIndex[Tuple] = itemRefID;
            }
            else
            {
                returnval = contentsIndex[Tuple];
                contentsIndex[Tuple] = itemRefID;

                Owner.UnequipItem(returnval);
            }
            // organ slots feed the body's EquippedItemRefs cache
            Owner?.Body?.ClearEquipCaches();
        }
        return returnval;
    }
    public bool UnequipItem(int itemRefID)
    {
        /// CODE START
        bool returnVal = false;

        List<string> removeList = new List<string>();
        if (contentsIndex.ContainsValue(itemRefID))
        {
            foreach (var pair in contentsIndex)
            {
                if (pair.Value == itemRefID)
                {
                    removeList.Add(pair.Key);
                    returnVal = true;
                    //contentsIndex[pair.Key] = -1;
                    //equippedRefIDs.Remove(itemRefID);
                }
            }
        }

        foreach (string i in removeList)
        {
            contentsIndex[i] = -1;
        }

        return returnVal;
        /// CODE END
    }

    /// <summary> the item in a slot, resolved through the owner's body (registered items and virtual outfit pieces) </summary>
    public bool TryGetEquip(out Item_Instance value, BodyEquipLayer i, BodyPartEquipSlot j)
    {
        value = null;
        var v = GetEquip(i, j);
        if (v != -1 && Owner != null && Owner.Body != null) value = Owner.Body.ResolveEquip(v);
        return value != null;
    }

    public int GetEquip(BodyEquipLayer i, BodyPartEquipSlot j)
    {
        string Tuple = i.ToString() + "||" + j.ToString();
        if (contentsIndex.ContainsKey(Tuple)) return contentsIndex[Tuple];
        else return -1;
    }

    public void Stimulate(ref List<string> ownerTags, ref int pleasureTotal, ref double pleasure, ref double pain)
    {
        string sensitivity = Sensitivity;
        bool Maso = false;
        ownerTags.AddRange(Base.tags);

        //Debug.Log("Stimulating body part " + this.DisplayName);
        if (sensitivity != "")
        {
            if (Maso) pleasure += pain;
            else pleasure -= pain * 2;

            ownerTags.Add(Sensitivity);

            //int i = Math.Min((int)pleasure, MaxSensitivity - (int)Owner.Stats.GetStatusSeverityByStringMatch(sensitivity));
            Owner.Stats.AddOrModStatus(sensitivity, (float)pleasure, -1);
            Owner.Body.NotifyStimulated();
            //Debug.Log(debug);
            pleasureTotal += (int)pleasure;
        }
        else
        {
            pleasure = 0;
            pain = 0;
        }
    }
}

public class Sexperience
{
    public Dictionary<string, float> expTables = new Dictionary<string, float>();

    // targetref (can be null)
    // target (fetch from ref) 
    // time date location (map, room, DateTime -> date, hour minute)
    // com and variantID (displayname)
    // others (sleeping? threatened?)
}

// global sexp : best sex, worst sex, most sex, 
// global sexp based on whole sex session
