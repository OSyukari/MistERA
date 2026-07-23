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

public class BodyInternal_Instance
{
    public BodyInternal_Womb womb = null;

    [JsonProperty] protected long firstExperience = 0, lastExperience = 0;
    [JsonProperty] protected string firstExpDesc = "", lastExpDesc = "";

    [JsonIgnore] public string FirstExperienceDesc { get { return this.firstExpDesc; } }

    Memory_Entry cache_firstEXP = null, cache_lastEXP = null;
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
                var fillPercentage = this.ExpandedCapacityPercentage;
                var maxcount = basePointer.images_volume.Count;
                int index = (int)Math.Clamp(fillPercentage * maxcount, 0, maxcount - 1);

                if (index == 0 && basePointer.images_expansion != null && basePointer.images_expansion.Count > 0)
                {
                    // use expansion image instead
                }
                else
                {
                    return basePointer.images_volume[index];
                }
                //return image based on fill percentage
            }

            var sklevel = ExpansionSkill == null ? 0 : ExpansionSkill.GetSkillLevel;
            if (basePointer.images_expansion != null && basePointer.images_expansion.Count > 0)
            {
                sklevel = Math.Clamp(sklevel, 0, basePointer.images_expansion.Count - 1);
                return basePointer.images_expansion[sklevel];
            }
            return "";
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
                    if (equip > -1 && scr_System_CampaignManager.current.FindItemInstanceByID(equip).GetComp_Equippable().revealing >= revealingScoreFilter)
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

    public bool NotifySexExperience(bool hasPermission, string targetName, string comName, List<string> comtags, List<string> targetBodyTag)
    {
        this.lastExperience = Owner.Memory.Last.EndTime.Ticks;
        this.lastExpDesc = LocalizeDictionary.QueryThenParse("bodyPart_internal_lastExpFormat").Replace("$target$", targetName).Replace("$command$", comName);

        if (this.firstExperience != 0 || this.Base.firstExperienceDesc == "" || this.Base.virginityLossTags.Count < 1)
        {
            return false;
        }

        if (targetBodyTag != null && Utility.ListContainsLoose(targetBodyTag, this.Base.virginityLossTags))
        {
            if (scr_System_CentralControl.current.LogPrefs.DLog_Training) Debug.Log($"{Owner.FirstName} match firstexperience {DisplayName} on targetBodytags {String.Join(" ", targetBodyTag)}");
            this.firstExperience = lastExperience;
            this.firstExpDesc = LocalizeDictionary.QueryThenParse(hasPermission? "bodyPart_internal_expVirginLoss_cons" : "bodyPart_internal_expVirginLoss").Replace("$target$", targetName).Replace("$command$", comName).Replace("$partname$", this.DisplayName);
            return true;
        }
        else if (Utility.ListContainsLoose(comtags, this.Base.virginityLossTags))
        {
            if (scr_System_CentralControl.current.LogPrefs.DLog_Training) Debug.Log($"{Owner.FirstName} match firstexperience {DisplayName} on comtags {String.Join(" ", comtags)}");
            this.firstExperience = lastExperience;
            this.firstExpDesc = LocalizeDictionary.QueryThenParse(hasPermission ? "bodyPart_internal_expVirginLoss_cons" : "bodyPart_internal_expVirginLoss").Replace("$target$", targetName).Replace("$command$", comName).Replace("$partname$", this.DisplayName);
            return true;
        }
        else
        {
           
            return false;
            //Debug.LogError("Checking virginity loss with tags [" + String.Join(",", ownerTags) + "][" + String.Join(",", comtags) + "] with baseTags [" + String.Join(",", this.Base.virginityLossTags) + "]");
        }
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


        if (firstExpDesc == "") box.SetText(LocalizeDictionary.QueryThenParse(Base.firstExperienceDesc)); 
        else box.SetText(firstExpDesc);

        if (FirstExperience != null) box.SetExternalTooltip(FirstExperience.ToString(false, true, true));
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
    }

    public void UpdateTimeMinute(TimeSpan t)
    {
        if (!this.canContain) return;

        List<Item_Instance> delete = new List<Item_Instance>();
        foreach (var content in Contains)
        {
            var comp = content.GetComp_Ingestible();
            var methods = comp == null ? new List<ItemComponentTemplate_Ingestible.Ingestible_IngestMethod>() : comp.ingestMethod.FindAll(x => this.hasTag(x.bodyTags));

            TimeSpan? updatetime = null;

            if (methods.Count > 0)
            {
                float amount = methods[0].digestSpeed;
                if (ContainedRefs_Delays.TryGetValue(content.RefID, out var value) && value > 0)
                {
                    int timeTick = Math.Min((int)t.Minutes, ContainedRefs_Delays[content.RefID]);
                    ContainedRefs_Delays[content.RefID] -= timeTick;
                    if (timeTick < t.Minutes)
                    {
                        updatetime = t - TimeSpan.FromMinutes(t.Minutes - timeTick);
                    }
                }
                else
                {
                    updatetime = t;
                }

                if (updatetime != null)
                {
                    comp.amount += amount * t.Minutes;

                    foreach (var method in methods)
                    {
                        Digest(method, t, amount);
                    }

                    if (comp.amount <= 0) delete.Add(content);
                }
            }

        }
        foreach (Item_Instance item in delete)
        {
            Contains.Clear();
            ContainedRefs_Delays.Remove(item.RefID);
            scr_System_CampaignManager.current.Unregister(item);
        }



        delete.Clear();
    }

    private void Digest(ItemComponentTemplate_Ingestible.Ingestible_IngestMethod method, TimeSpan t, float amount)
    {
        if (method.giveStatus != null && method.giveStatus.Length > 0) this.owner.Stats.AddOrModStatus(method.giveStatus, -amount * method.amountMod * t.Minutes);
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

            float mod = Owner.Stats.SexStimulation.Severity * 0.02f;
            if (needLubrication && Owner.Stats.Lubrication.Severity <= 66) mod *= (0.015f * Owner.Stats.Lubrication.Severity);

            return MaxSize * mod + Size * (1 - mod);
            //float modifier = 1 + Math.Max(needLubrication && ?  (needLubrication ? (Owner.Stats.Lubrication.Severity - 30) * 0.01f : 0f);
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
            foreach (var i in this.Contains) if (i is Item_Instance_Cum && i.GetComp_Ingestible().amount > 0) return true;
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
            foreach (var i in Contains) sum += i.GetComp_Ingestible().amount;
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
    public float ExpandedCapacityPercentage
    {
        get
        {
            return CurrentlyContained / VisiblyExpandedCapacity;
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
                    if (this.Owner == null || this.Owner.Template == null) return null;
                    return this.Owner.Template.Size_B;
                case "P":
                    if (this.Owner == null || this.Owner.Template == null) return null;
                    return this.Owner.Template.Size_P;
                default:return null;
            }
        }
    }

    protected void AddExperience(float amount, List<string> extra = null, ExperienceLog m = null)
    {
        var tags = new List<string>();
        tags.AddRange(this.Base.tags);
        if (extra != null) tags.AddRange(extra);
        if (Owner.Body.isClimaxing(false, true)) tags.Add("climax");
        if (this.Sensitivity != "") tags.Add(this.Sensitivity);
        Owner.Skills.CheckExperienceGain(tags, amount, m);
    }

    /*
    public bool Ingest_(Item_Instance item, ExperienceLog m = null, bool forceFill = false, List<BodyInternal_Instance> fillHistory = null)
    {
        if (!this.canContain) return false;
        var comp = item.GetComp_Ingestible();

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
                        newitem.GetComp_Ingestible().amount = newcap;
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
                    newitem.GetComp_Ingestible().amount = newcap;
                    IngestInternal(newitem, m);
                }
                else
                {
                    Item_Instance newitem = WorldManager.Instantiate(item.BaseID, item.nameOverwrite, item.Count);
                    newitem.GetComp_Ingestible().amount = newcap;
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
    public bool Ingest(Item_Instance item, ExperienceLog m = null, bool forceFill = false, List<BodyInternal_Instance> fillHistory = null)
    {
        if (!this.canContain) return false;
        var comp = item.GetComp_Ingestible();

        bool isCum = item is Item_Instance_Cum;
        if (isCum)
        {
            var com = item as Item_Instance_Cum;
            if (!com.experienceTicked)
            {
                com.experienceTicked = true;
                AddExperience(comp.amount, new List<string>() { "cum" }, m);
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
            IngestInternal(item, m);
            return true;
        }

        // ── Cum routing ──────────────────────────────────────────────────────────
        if (fillHistory == null) fillHistory = new List<BodyInternal_Instance>() { this };
        else if (!fillHistory.Contains(this)) fillHistory.Add(this);

        // Phase 1: fits within expanding capacity.
        if (RemainingExpandingCapacity >= comp.amount || comp.amount < 1)
        {
            IngestInternal(item, m);
            return true;
        }

        // Phase 2: push inward, same expanding-capacity rule.
        BodyInternal_Instance dirIn = (Base.tag_directionIn == "" || Base.tag_directionIn == "ext")
            ? null : Owner.Body.GetRandomInternalWithTag(Base.tag_directionIn);

        if (dirIn != null && dirIn != this && dirIn.canContain && !fillHistory.Contains(dirIn))
        {
            if (dirIn.Ingest(item, m, false, fillHistory)) return true;
        }

        // Phase 3: directionIn chain exhausted — try max capacity.
        if (RemainingMaxCapacity >= comp.amount)
        {
            IngestInternal(item, m);
            return true;
        }

        // Phase 4: over max capacity — push existing cum out first, then ingest new load.
        float overflow4 = comp.amount - RemainingMaxCapacity;
        Debug.Log($"[Ingest] {Owner.CallName} {DisplayName}: over max capacity ({CurrentlyContained:F1}/{MaxCapacity:F1}), pushing {overflow4:F1}ml of existing cum out.");
        PushCumOverflowOut(overflow4, m, fillHistory);
        IngestInternal(item, m);
        return true;
    }

    // Extracts amountToPush of existing cum from this organ and routes it outward via
    // directionOut. Called BEFORE ingesting the new item so the new load is never displaced.
    // Non-cum contents are never touched.
    private void PushCumOverflowOut(float amountToPush, ExperienceLog m, List<BodyInternal_Instance> fillHistory)
    {
        if (amountToPush <= 0f) return;

        BodyInternal_Instance dirOut = (Base.tag_directionOut == "" || Base.tag_directionOut == "ext")
            ? null : Owner.Body.GetRandomInternalWithTag(Base.tag_directionOut);

        if (dirOut == null || dirOut == this || !dirOut.canContain
            || (fillHistory != null && fillHistory.Contains(dirOut))) return;

        if (fillHistory == null) fillHistory = new List<BodyInternal_Instance>();
        fillHistory.Add(dirOut);

        float remaining = amountToPush;
        while (remaining > 0f)
        {
            // Only push cum — other item types are never displaced.
            Item_Instance_Cum sourceCum = null;
            foreach (var content in Contains)
            {
                if (content is Item_Instance_Cum cum && cum.CumAmount > 0f)
                {
                    sourceCum = cum;
                    break;
                }
            }
            if (sourceCum == null) break;

            float pushAmount = Math.Min(remaining, (float)sourceCum.CumAmount);

            Item_Instance_Cum overflow_item = WorldManager.Instantiate(sourceCum.Owner, sourceCum.nameOverwrite);
            overflow_item.CumAmount = pushAmount;
            overflow_item.experienceTicked = true; // already counted when it first entered

            sourceCum.CumAmount -= pushAmount;
            if (sourceCum.CumAmount <= 0f)
            {
                ContainedRefs_Delays.Remove(sourceCum.RefID);
                contains_cache = null;
                scr_System_CampaignManager.current.Unregister(sourceCum);
            }

            remaining -= pushAmount;
            dirOut.IngestAtMaxCapacity(overflow_item, m, fillHistory);
        }
    }

    // Used by PushCumOverflowOut to route pushed cum through the directionOut chain.
    // Accepts up to MaxCapacity; if still over, cascades further out.
    // Does not re-tick experience — pushed items are already accounted for.
    private void IngestAtMaxCapacity(Item_Instance item, ExperienceLog m, List<BodyInternal_Instance> fillHistory)
    {
        if (!this.canContain) return;
        if (!fillHistory.Contains(this)) fillHistory.Add(this);

        var comp = item.GetComp_Ingestible();
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
        var comp = i.GetComp_Ingestible();

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
        else if (i.Stackable)
        {
           // Debug.Log("Tryingest item ["+i.DisplayName+"] stackable!");
            foreach(var kvpair in Contains)
            {
                if (kvpair.BaseID == i.BaseID && kvpair.DisplayName == i.DisplayName)
                {
                    //consumed = true;
                    kvpair.GetComp_Ingestible().amount += comp.amount;
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

    public void TransferContentTo(BodyInternal_Instance instance)
    {

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
        var comp = item.GetComp_Equippable();
        bool equipped = false;

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

    public bool EquipItem(Item_Instance item, bool forceEquip = false)
    {
        var comp = item.GetComp_Equippable();
        bool equipped = false;
        bool unequip = false;

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




