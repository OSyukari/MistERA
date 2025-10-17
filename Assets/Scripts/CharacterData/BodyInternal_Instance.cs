using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using Newtonsoft.Json;

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

    private Sexperience experiences = new Sexperience();

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

    public bool NotifySexExperience(string targetName, string comName, List<string> comtags, List<string> targetBodyTag)
    {
        this.lastExperience = Owner.Memory.Last.EndTime.Ticks;
        this.lastExpDesc = LocalizeDictionary.QueryThenParse("bodyPart_internal_lastExpFormat").Replace("$target$", targetName).Replace("$command$", comName);

        if (this.firstExperience != 0 || this.Base.firstExperienceDesc == "" || this.Base.virginityLossTags.Count < 1)
        {
            return false;
        }

        if (targetBodyTag != null && Utility.ListContainsLoose(targetBodyTag, this.Base.virginityLossTags))
        {
            Debug.Log($"{Owner.FirstName} match firstexperience {DisplayName} on targetBodytags {String.Join(" ", targetBodyTag)}");
            this.firstExperience = lastExperience;
            this.firstExpDesc = LocalizeDictionary.QueryThenParse("bodyPart_internal_expVirginLoss").Replace("$target$", targetName).Replace("$command$", comName).Replace("$partname$", this.DisplayName);
            return true;
        }
        else if (Utility.ListContainsLoose(comtags, this.Base.virginityLossTags))
        {
            Debug.Log($"{Owner.FirstName} match firstexperience {DisplayName} on comtags {String.Join(" ", comtags)}");
            this.firstExperience = lastExperience;
            this.firstExpDesc = LocalizeDictionary.QueryThenParse("bodyPart_internal_expVirginLoss").Replace("$target$", targetName).Replace("$command$", comName).Replace("$partname$", this.DisplayName);
            return true;
        }
        else
        {
           
            return false;
            //Debug.LogError("Checking virginity loss with tags [" + String.Join(",", ownerTags) + "][" + String.Join(",", comtags) + "] with baseTags [" + String.Join(",", this.Base.virginityLossTags) + "]");
        }
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

    public void UpdateTimeMinute(TimeSpan t)
    {
        if (!this.canContain) return;

        List<Item_Instance> delete = new List<Item_Instance>();
        foreach (var content in Contains)
        {
            ItemComponentTemplate_Ingestible.Ingestible_IngestMethod method = content.GetComp_Ingestible().ingestMethod.Find(x => this.hasTag(x.bodyTags));
            if (method != null)
            {
                //if (!contain.ContainsKey(kvp.Key)) DigestDelays.Add(kvp.Key, Utility.RandVariation(method.digestDelay, method.digestDelayVariation));
                if (ContainedRefs_Delays[content.RefID] > 0)
                {
                    int timeTick = Math.Min((int) t.Minutes, ContainedRefs_Delays[content.RefID]);
                    ContainedRefs_Delays[content.RefID] -= timeTick;
                    if (timeTick < t.Minutes)
                    {
                        Digest(method, t - TimeSpan.FromMinutes(t.Minutes - timeTick), content, content.GetComp_Ingestible(), delete);
                    }
                }
                else
                {
                    Digest(method, t, content, content.GetComp_Ingestible(), delete);
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

    private void Digest(ItemComponentTemplate_Ingestible.Ingestible_IngestMethod method, TimeSpan t, Item_Instance i, ItemComponent_Ingestible comp, List<Item_Instance> delete)
    {
        float amount = method.digestSpeed;

        comp.amount += amount * t.Minutes;
        if (method.giveStatus != null && method.giveStatus.Length > 0) this.owner.Stats.AddOrModStatus(method.giveStatus, -amount * method.amountMod * t.Minutes);
        if (comp.amount <= 0) delete.Add(i);
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
    }
    public void ClearLastInteractedRefs()
    {
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
    }
    public bool Initialize(string baseID, BodyPart_Instance c)
    {
        this.baseID = baseID;
        ReEstablishParent(c);

        basePointer = scr_System_Serializer.current.GetByNameOrID_BodyInternal_Base(baseID);
        if (basePointer == null) return false;
        this.experiences = new Sexperience();

        orifice_depth = basePointer.depthRatio == 0 ? 0 : Utility.RandVariation(Owner.Height, 0.1f) * basePointer.depthRatio;
        orifice_size = basePointer.sizeRatio == 0 ? 0 : Utility.RandVariation(Owner.Height, 0.1f) * basePointer.sizeRatio;
        volume_capacity = basePointer.volumeRatio == 0 ? 0 : Utility.RandVariation(Owner.Height, 0.1f) * basePointer.volumeRatio;

        contentsIndex = new Dictionary<string, int>();
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


    [JsonIgnore] public float CurrentDepth
    {
        get
        {

            float modifier = 1;
            if (hasTag("arousalExpansion"))
            {
                modifier += Math.Max(0, Math.Max( Owner.Stats.Lust != null ? Owner.Stats.Lust.Severity:0, Owner.Stats.Stress != null ? Owner.Stats.Stress.Severity : 0) * 0.2f);
                modifier += Math.Max(0, (Owner.Stats.SexStimulation.Severity - 50) )* 0.01f;
            }
            if (this.canBePenetrated && this.ExpansionSkill != null)
            {
                modifier += this.ExpansionSkill.GetSkillLevel * 0.1f;
            }
            return Depth * modifier;
        }
    }

    [JsonIgnore] public bool needLubrication { get { return Base.needLubrication; } }

    [JsonIgnore] public float CurrentSize
    {
        get
        {
            float baseSize = Size;

            float modifier = 1 + (!canBePenetrated ? 0 : needLubrication ? (Owner.Stats.Lubrication.Severity -30) * 0.01f : 0f);
            if (hasTag("arousalExpansion")) modifier += Math.Max(0, Math.Max(Owner.Stats.Lust != null ? Owner.Stats.Lust.Severity:0, Owner.Stats.Stress != null ? Owner.Stats.Stress.Severity : 0) * 0.2f);
            if (this.canBePenetrated && this.ExpansionSkill != null)
            {
                modifier += this.ExpansionSkill.GetSkillLevel * 0.2f;
                baseSize += this.ExpansionSkill.GetSkillLevel * 0.5f;
            }

            return baseSize * modifier ;
        }
    }

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

    [JsonIgnore]
    public float CurrentSizeExtended
    {
        get
        {
            return CurrentSize * (1 + (this.SensitivitySkill != null ? this.SensitivitySkill.GetSkillLevel * 0.05f : 0f));
        }
    }

    [JsonIgnore] public float CurrentMaxSize
    {
        get
        {
            if (hasTag("anus")) return CurrentSizeExtended * 1.5f;
            else if (hasTag("vagina")) return CurrentSizeExtended * 2;
            else if (hasTag("womb")) return CurrentSizeExtended * 20;

            return CurrentSizeExtended;
        }
    }

    [JsonIgnore]public int MaxSensitivity
    {
        get
        {
            //if (Trait_Sensitivity != null) return Math.Max(basePointer.defaultSensitivity + Trait_Sensitivity.trait_score, 0);
            if (Base.maxSensitivityStatString == "") return 0;
            //Debug.Log("Fetching " + Base.maxSensitivityStatString+", does it exist "+(scr_System_Serializer.current.GetByNameOrID_StatsDerivedBase(Base.maxSensitivityStatString) != null));

            return (int)Owner.Stats.GetDerivedStat(Base.maxSensitivityStatString).FinalValue();
        }
    }

    [JsonProperty] private float orifice_depth;
    [JsonIgnore] public float Depth
    {
        get { return orifice_depth; }
    }

    [JsonProperty] private float orifice_size;
    [JsonIgnore] public float Size
    {
        get { return orifice_size; }
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
            return Depth > 0 && Size > 0;
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
            return volume_capacity > 0.1;
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
    [JsonProperty] Dictionary<int, int> ContainedRefs_Delays = new Dictionary<int, int>();
    public float volume_capacity;
    [JsonIgnore] public bool containsOverCapacity
    {
        get
        {
            float sum = 0;
            foreach (var i in Contains) sum += i.GetComp_Ingestible().amount;
            return sum > volume_capacity;
        }
    }

    [JsonIgnore] public string Sensitivity
    {
        get
        {
            return scr_System_Serializer.current.GetSensitivityStatus(this.Base.sensitivityClassString);
        }
    }

    protected void AddExperience(float amount, List<string> extra = null, ExperienceLog m = null)
    {
        var tags = new List<string>();
        tags.AddRange(this.Base.tags);
        if (extra != null) tags.AddRange(extra);
        if (Owner.Body.isClimaxing(false)) tags.Add("climax");
        if (this.Sensitivity != "") tags.Add(this.Sensitivity);
        Owner.Skills.CheckExperienceGain(tags, amount, m);
    }

    public void Ingest(Item_Instance i, ExperienceLog m = null)
    {
        var comp = i.GetComp_Ingestible();
        //bool consumed = false;
        if (i as Item_Instance_Cum != null)
        {
            float amount = comp.amount;
            int exp = Math.Abs((int)(comp.amount / 10)) + 1;

            AddExperience(amount, new List<string>() { "cum"}, m);

            if (cumStimulation != 0)
            {
                string s = Sensitivity;
                if (s != "") Owner.Stats.AddOrModStatus(s, amount * cumStimulation);
            }
        }

        UtilityEX.ApplyOnConsume(this, comp.OnUseEffects);

        if (i.Stackable)
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

        ItemComponentTemplate_Ingestible.Ingestible_IngestMethod method = comp.ingestMethod.Find(x => this.hasTag(x.bodyTags));

        //if (!contain.ContainsKey(kvp.Key)) DigestDelays.Add(kvp.Key, Utility.RandVariation(method.digestDelay, method.digestDelayVariation));
        this.ContainedRefs_Delays.Add(i.RefID, (int)Utility.RandVariation(method.digestDelay, method.digestDelayVariation));
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

    [JsonIgnore] public string DisplayNameFull { get { return OwnerName + "'s "+DisplayName; } }

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
            Item_Instance cum = WorldManager.Instantiate(this.owner, owner.FirstName + "'s cum");
            cum.GetComp_Ingestible().amount = amount;

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
    [JsonProperty] Dictionary<string, int> contentsIndex;

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

    public void Stimulate(ref List<string> ownerTags, ref int pleasureTotal, ref float pleasure, ref float pain)
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

            int i = Math.Min((int)pleasure, MaxSensitivity - (int)Owner.Stats.GetStatusSeverityByStringMatch(sensitivity));
            Owner.Stats.AddOrModStatus(sensitivity, pleasure,-1, MaxSensitivity);
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




