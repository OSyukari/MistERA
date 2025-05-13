using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using Spine;
using Newtonsoft.Json.Linq;

[System.Serializable]
public class Index_CharaSkills : I_IndexMergeable, I_IndexHasID
{
    public List<CharaSkill> list = new List<CharaSkill>();

    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Index_CharaSkills;
        if (l == null) return;
        else if (l.list == null || l.list.Count < 1) return;
        else this.list.AddRange(l.list);
    }
    public CharaSkill GetByID(string id) { return ID_Dictionary.ContainsKey(id) ? ID_Dictionary[id] : null; }
    Dictionary<string, CharaSkill> ID_Dictionary = new Dictionary<string, CharaSkill>();
    public void RegisterAllID()
    {
        foreach (CharaSkill o in this.list)
        {
            ID_Dictionary.Add(o.ID, o);
        }
        Debug.Log("Index_CharaSkills initialized with " + list.Count + " elements");
    }
}

[System.Serializable]
public enum LogicalOperand
{
    none,
    gte,
    lte,
    eq,
    neq,
    gt,
    lt
}

[System.Serializable]
public class SkillManager
{

    [SerializeField][JsonProperty] protected Dictionary<string, int> experienceLogs = new Dictionary<string, int>();
    [SerializeField][JsonProperty] protected Dictionary<string, int> experienceLogs_currentRound = new Dictionary<string, int>();
    public List<string> ExperiencesToString()
    {
        var newDict = experienceLogs.Concat(debug_experienceLogs).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => (g.Count() > 1 ? g.First().Value + g.Last().Value : g.Last().Value));
        var list = new List<string>();
        foreach (var i in newDict)
        {
            var baseSkill = scr_System_Serializer.current.GetByNameOrID_ExperienceBase(i.Key);
            string append = baseSkill == null ? "" : baseSkill.DisplayAmountString;

            list.Add(scr_System_Serializer.current.Dictionary.QueryThenParse(i.Key) + ": " + i.Value + append);
        }
        return list;
    }

    public void ResetExperienceByID(string id)
    {
        if (experienceLogs_currentRound.Count > 0) FinalizeExperience();
        if (experienceLogs.ContainsKey(id)) experienceLogs[id] = 0;
    }

    public void ModExperienceByID(string id, int value)
    {
        if (experienceLogs_currentRound.Count > 0) FinalizeExperience();
        if (experienceLogs.ContainsKey(id)) experienceLogs[id] = Math.Max(0, experienceLogs[id] + value);
    }

    /// <summary>
    /// Called by Character_trainable postupdatetime3
    /// </summary>
    public void FinalizeExperience()
    {
        experienceLogs = experienceLogs.Concat(experienceLogs_currentRound).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => (g.Count() > 1 ? g.First().Value + g.Last().Value : g.Last().Value));
        experienceLogs_currentRound.Clear();
    }

    [JsonIgnore] public Dictionary<string, int> debug_experienceLogs = new Dictionary<string, int>();

    public int GetExperienceLevel(string expID)
    {
        if (!this.experienceLogs.ContainsKey(expID))
        {
            return 0 + (debug_experienceLogs.ContainsKey(expID) ? debug_experienceLogs[expID] : 0);
        }
        return this.experienceLogs[expID] + (debug_experienceLogs.ContainsKey(expID) ? debug_experienceLogs[expID] : 0);
    }

    public void CheckExperienceGain(List<string> ownerTags, List<string> comTags, float amount, bool isDoer, ExperienceLog m = null)
    {
        ownerTags = ownerTags.Distinct().ToList();
        comTags = comTags.Distinct().ToList();
        if (scr_System_CentralControl.current.LogPrefs.Debug_Logging_ActorExperienceGain) Debug.Log("CheckExperienceGain owner[" + String.Join("|", ownerTags) + "] com[" + String.Join("|", comTags) + "] amount[" + amount + "] isdoer[" + isDoer + "] m[" + (m == null ? "null" : "exist") + "]");
        foreach (var i in scr_System_Serializer.current.index_Experiences.List)
        {
            if (i.ExcludeCOMTags != null && i.ExcludeCOMTags.Count > 0)
            {
                if (comTags != null && comTags.Count > 0 && Utility.ListContainsLoose(i.ExcludeCOMTags, comTags)) continue;
            }
            if (i.ExcludeOwnerTags != null && i.ExcludeOwnerTags.Count > 0)
            {
                if (ownerTags != null && ownerTags.Count > 0 && Utility.ListContainsLoose(i.ExcludeOwnerTags, ownerTags)) continue;
            }
            if ((ownerTags == null || ownerTags.Count < 1) && i.RequiredOwnerTags != null && i.RequiredOwnerTags.Count > 0)
            {
                //Debug.Log("Checking " + i.ExperienceID + ", missing requiredOwnerTags");
                continue;
            }
            if ((comTags == null || comTags.Count < 1) && i.RequiredCOMTags != null && i.RequiredCOMTags.Count > 0)
            {
                //Debug.Log("Checking " + i.ExperienceID + ", missing RequiredCOMTags");
                continue;
            }
            if (isDoer && !i.ApplyToDoer)
            {
                // Debug.Log("Checking " + i.ExperienceID + ", not applying to doer");
                continue;
            }
            if (!isDoer && !i.ApplyToReceiver)
            {
                // Debug.Log("Checking " + i.ExperienceID + ", not applying to receiver");
                continue;
            }
            if (!Utility.ListContainsStrict(ownerTags, i.RequiredOwnerTags))
            {
                // Debug.Log("Checking " + i.ExperienceID + ", failed containstrict RequiredOwnerTags");
                continue;
            }
            if (!Utility.ListContainsStrict(comTags, i.RequiredCOMTags))
            {
                // Debug.Log("Checking " + i.ExperienceID + ", failed containstrict RequiredCOMTags");
                continue;
            }

            AddExperience(i, amount, m);
        }
    }

    protected void AddExperience(ExperienceClass i, float amount, ExperienceLog m = null)
    {
        //Debug.Log("AddExperience " + i.ExperienceID);
        if (!experienceLogs_currentRound.ContainsKey(i.ExperienceID)) experienceLogs_currentRound.Add(i.ExperienceID, 0);
        if (i.CountTotal)
        {
            experienceLogs_currentRound[i.ExperienceID] += (int)amount;
            if (m != null) m.AddExperience(Owner.RefID, i.ExperienceID, (int)amount);
            else scr_UpdateHandler.current.exp.AddExperience(Owner.RefID, i.ExperienceID, (int)amount);
        }
        else
        {
            experienceLogs_currentRound[i.ExperienceID] += 1;
            if (m != null) m.AddExperience(Owner.RefID, i.ExperienceID, 1);
            else scr_UpdateHandler.current.exp.AddExperience(Owner.RefID, i.ExperienceID, 1);
        }
    }

    public void CheckExperienceGain(List<string> ownerTags, float amount, ExperienceLog m = null)
    {   // for anything that ignores 
        foreach (var i in scr_System_Serializer.current.index_Experiences.List)
        {
            if ((ownerTags == null || ownerTags.Count < 1) && i.RequiredOwnerTags != null && i.RequiredOwnerTags.Count > 0) continue;
            if (i.RequiredCOMTags != null && i.RequiredCOMTags.Count > 0) continue;

            if (!Utility.ListContainsStrict(i.RequiredOwnerTags, ownerTags)) continue;

            AddExperience(i, amount, m);
        }
    }


    protected int ownerRefID = -1;
    private Character_Trainable owner = null;
    [JsonIgnore]
    public Character_Trainable Owner
    {
        get
        {
            if (owner == null && ownerRefID > -1)
            {
                owner = scr_System_CampaignManager.current.FindInstanceByID(ownerRefID);
            }
            return owner;
        }
    }

    public void ReEstablishParent(Character_Trainable owner)
    {
        this.owner = owner;
        this.ownerRefID = owner.RefID;
        RefreshSkillsList();
    }

    [SerializeField][JsonProperty] Dictionary<string, SkillInstance> skills = new Dictionary<string, SkillInstance>();
    [JsonIgnore] public List<SkillInstance> Skills { get
        {
            if (skills == null) return new List<SkillInstance>();
            return skills.Values.ToList();
        } }

    public SkillManager() { }
    public SkillManager(Character_Trainable c)
    {
        this.ReEstablishParent(c);
    }

    public void UpdateAllSkills(List<string> messages = null)
    {
        foreach (var i in Skills)
        {
            while (i.Upgrade(messages))
            {
                // do something or do nothing
            }
        }
    }

    protected void RefreshSkillsList()
    {
        foreach(var sk in scr_System_Serializer.current.index_Skills.list)
        {
            if (!sk.ValidateChara(Owner)) { }// Debug.LogError("Refresh skill entry " + sk.ID + " failed validation on " + Owner.FirstName); 
            else if (!this.skills.ContainsKey(sk.ID)) this.skills.Add(sk.ID, sk.Instantiate());
        }

        foreach(var sk in this.skills.Values) sk.ReEstablishParent(Owner);
    }
}


[System.Serializable]
public class SkillInstance
{
    [JsonIgnore] public string DisplayName { 
        get {
            if (!BaseRef.hasGenderVariant) return scr_System_Serializer.current.Dictionary.QueryThenParse(BaseRef.ID);
            else return scr_System_Serializer.current.Dictionary.QueryThenParse(BaseRef.ID+"_"+scr_System_CentralControl.current.GetGenderSimple(Owner).ToString());
    } }
    [JsonIgnore] public string DisplayNameFull { get { return DisplayName+": "+currentLevel; } }
    protected int ownerRefID = -1;
    private Character_Trainable owner = null;
    [JsonIgnore]
    public Character_Trainable Owner
    {
        get
        {
            if (owner == null && ownerRefID > -1)
            {
                owner = scr_System_CampaignManager.current.FindInstanceByID(ownerRefID);
            }
            return owner;
        }
    }

    public void ReEstablishParent(Character_Trainable owner)
    {
        this.owner = owner;
        this.ownerRefID = owner.RefID;
    }

    [SerializeField]
    [JsonProperty]
    protected string baseSkillID = "";
    protected CharaSkill _base = null;
    [JsonIgnore] public CharaSkill BaseRef { get
        {
            if (_base == null && baseSkillID != "") _base = scr_System_Serializer.current.GetByNameOrID(baseSkillID);
            return _base;
        } }

    public SkillInstance() { }
    public SkillInstance(CharaSkill baseInstance) 
    {
        this.baseSkillID = baseInstance.ID;
        this._base = baseInstance;
    }

    [SerializeField]
    [JsonProperty]
    protected int currentLevel = 0;

    [JsonIgnore] public int GetSkillLevel
    {
        get
        {
            if (BaseRef == null) return 0;
            if (!BaseRef.ValidateChara(Owner)) return 0;
            return currentLevel;
        }
    }

    public List<Stat_Modifier> GetStatMods()
    {
        if (this.GetSkillLevel < 1) return new List<Stat_Modifier>();
        return BaseRef.Levels[this.currentLevel].stat_modifiers;
    }

    [JsonIgnore]
    public bool CanUpgrade
    {
        get
        {
            TooltipCache.Clear();
            if ((this.currentLevel + 1) >= BaseRef.Levels.Count)
            {
                TooltipCache.Add("already at max level");
                return false;
            }
            return BaseRef.Levels[currentLevel + 1].ValidateChara(Owner, TooltipCache);
        }
    }

    /// <summary>
    /// This is only initialized after calling CanUpgrade, so dont do it without calling that one first
    /// </summary>
    [JsonIgnore] public List<string> TooltipCache = new List<string>();


    public bool Upgrade(List<string> messages = null)
    {
        if (!CanUpgrade) return false;

        bool refresh = this.GetStatMods().Count > 0;
        this.currentLevel += 1;
        if (messages != null) messages.Add( Owner.FirstName+"'s skill " + BaseRef.ID + " upgraded to level " + (currentLevel));
        BaseRef.NotifyUpgrade(Owner, currentLevel, messages);
        refresh =  refresh || this.GetStatMods().Count > 0;
        if (refresh)
        {
            
            List<string> s = new List<string>();
            foreach(var i in this.GetStatMods())
            {
                s.Add(i.statID+"-"+i.modKey+"-"+i.Type+"-"+i.Value(Owner));
            }
            //Debug.Log("Refreshing all stats "+String.Join("|",s));
            Owner.Stats.RefreshAllStats(true);
        }
        return true;
    }
}



[System.Serializable]
public class CharaSkill
{
    public string ID = "";
    public bool hasGenderVariant = false;
    public Require requirements = null;
    public bool allowAutoUpgrade = true;
    public List<SkillLevel> Levels = new List<SkillLevel>();
    public SkillInstance Instantiate()
    {
        var i = new SkillInstance(this);
        return i;
    }

    public bool ValidateChara(Character_Trainable c, List<string> tooltips = null)
    {
        if (requirements == null) return true;
        return requirements.Validate(c, tooltips);
    }

    public void NotifyUpgrade(Character_Trainable c, int currentLevel, List<string> messages = null)
    {
        if (requirements == null) return;
        requirements.NotifyUpgrade(c, messages);

        this.Levels[currentLevel].NotifyUpgrade(c, messages);
    }

    [System.Serializable]
    public abstract class Require
    {
        public List<RequirementEntry> entries = new List<RequirementEntry>();
        public abstract bool Validate(Character_Trainable c, List<string> tooltips = null);
        public abstract void NotifyUpgrade(Character_Trainable c, List<string> messages = null);
    }

    [System.Serializable]
    public class RequireOne : Require
    {
        public override void NotifyUpgrade(Character_Trainable c, List<string> messages = null)
        {
            if (entries == null || entries.Count < 1) return;
            foreach (var i in entries) if (i.NotifyUpgrade(c)) return;
        }

        public override bool Validate(Character_Trainable c, List<string> tooltips = null)
        {
           // Debug.LogError("CharaSkill RequireOne null?" + (entries == null || entries.Count < 1));
            if (entries == null || entries.Count < 1) return true;
            if (tooltips != null) tooltips.Add("Require one of the following:\n");
            foreach (var i in entries) if (i.Validate(c)) return true;
            return false;
        }
    }

    [System.Serializable]
    public class RequireAll : Require
    {
        public override void NotifyUpgrade(Character_Trainable c, List<string> messages = null)
        {
            if (entries == null || entries.Count < 1) return;
            foreach (var i in entries) i.NotifyUpgrade(c, messages);
        }

        public override bool Validate(Character_Trainable c, List<string> tooltips = null)
        {
           // Debug.LogError("CharaSkill RequireAll null?" + (entries == null || entries.Count < 1));
            if (entries == null || entries.Count < 1) return true;
            if (tooltips != null) tooltips.Add("Require all of the following:\n");
            foreach (var i in entries) if (!i.Validate(c, tooltips)) return false;
            return true;
        }
    }


    [System.Serializable]
    public class RequirementEntry
    {
        public InteractionGenderType requireGender = InteractionGenderType.none;
        public List<string> requireBodyTag = new List<string>();
        public List<string> requireStatKeyword = new List<string>();
        public List<RequireExperience> requireExperiences = new List<RequireExperience> ();
        public RequireSumExperience requireSumExperiences = new RequireSumExperience ();

        public bool NotifyUpgrade(Character_Trainable c, List<string> messages = null)
        {
            if(!this.Validate(c, null)) return false;
            foreach(var i in this.requireExperiences) i.NotifyUpgrade(c, messages);
            return true;
        }
        public bool Validate(Character_Trainable c, List<string> tooltips = null)
        {
            var returnValue = true;
            List<string> c1 = new List<string> ();
            List<string> c2 = new List<string> ();
            //Debug.Log("CharaSkill RequirementEntry nulldata? ["+(requireBodyTag != null && requireBodyTag.Count > 0) +"] ["+(requireStatKeyword != null && requireStatKeyword.Count > 0) +"] ["+(requireExperiences != null && requireExperiences.Count > 0) +"]");
            if (requireBodyTag != null && requireBodyTag.Count > 0)
            {
                c2.Clear();
                foreach (var i in requireBodyTag)
                {
                    if (!c.Body.HasBodyTag(i))
                    {
                        c2.Add("<color=" + scr_System_CentralControl.current.pref.HexColor_conflict + ">" + i + "</color>");
                        returnValue = false;
                    }
                    else c2.Add(i);
                    
                }
                c1.Add("require BodyTags ["+ String.Join("|", c2)+"]");
            }
            if (requireStatKeyword != null && requireStatKeyword.Count > 0)
            {
                c2.Clear();
                foreach (var i in requireStatKeyword)
                {
                    if (!c.hasStatKeyword(i))
                    {
                        c2.Add("<color=" + scr_System_CentralControl.current.pref.HexColor_conflict + ">" + i + "</color>");
                        returnValue = false;
                    }
                    else c2.Add(i);
                }
                c1.Add("require statkeyword [" + String.Join("|", c2) + "]");
            }
            if (requireExperiences != null &&  requireExperiences.Count > 0)
            {
                c2.Clear();
                foreach (var i in requireExperiences)
                {
                    if (!i.isValid) continue;

                    if (!Utility.CompareValue(c.Skills.GetExperienceLevel(i.experienceID), i.operand, i.value))
                    {
                        c2.Add("<color=" + scr_System_CentralControl.current.pref.HexColor_conflict + ">" + i.Tooltip + "</color>");
                        returnValue = false;
                    }
                    else c2.Add(i.Tooltip);
                }
                c1.Add("require Experiences [" + String.Join("|", c2) + "]");
            }

            if (requireGender != InteractionGenderType.none)
            {
                c2.Clear();
                if (!scr_System_CentralControl.current.GetGender(c).Contains(requireGender))
                {
                    c2.Add("<color=" + scr_System_CentralControl.current.pref.HexColor_conflict + ">" + requireGender.ToString() + "</color>");
                    returnValue= false;
                }
                else c2.Add(requireGender.ToString());
                c1.Add("require Gender [" + String.Join("|", c2) + "]");
            }

            if (requireSumExperiences.isValid)
            {
                c2.Clear();
                int sumValue = 0;
                foreach (var i in requireSumExperiences.experienceIDs) sumValue += c.Skills.GetExperienceLevel(i);
                if (!Utility.CompareValue(sumValue, requireSumExperiences.operand, requireSumExperiences.value))
                {
                    c2.Add("<color=" + scr_System_CentralControl.current.pref.HexColor_conflict + ">" + requireSumExperiences.Tooltip + "</color> (currently "+sumValue+")");
                    returnValue = false;
                }
                else c2.Add(requireSumExperiences.Tooltip+" (currently "+sumValue+")");
                c1.Add("require Sum Experiences [" + String.Join("|", c2) + "]");
            }

            if (tooltips != null && c1.Count > 0) tooltips.Add(String.Join(" | ",c1));
            return returnValue;
        }

        [System.Serializable]
        public class RequireExperience
        {
            public string experienceID = "";
            public LogicalOperand operand = LogicalOperand.none;
            public int value = 0;
            [JsonIgnore] public bool isValid { get { return experienceID != "" && operand != LogicalOperand.none; } }
            [JsonIgnore] public string Tooltip { get
                {
                    return scr_System_Serializer.current.Dictionary.QueryThenParse(experienceID)+" values must be "+ scr_System_Serializer.current.Dictionary.QueryThenParse(operand.ToString())+" "+value;
                } }

            public bool WipeExperienceOnLevelUp = false;
            public bool DeduceExperienceOnLevelUp = false;
            public void NotifyUpgrade(Character_Trainable c, List<string> messages = null)
            {
                if (this.WipeExperienceOnLevelUp)
                {
                    c.Skills.ResetExperienceByID(experienceID);
                    if (messages != null) messages.Add(experienceID + " is getting wiped on level-up");
                }else if (this.DeduceExperienceOnLevelUp)
                {
                    c.Skills.ModExperienceByID(experienceID, -value);
                    if (messages != null) messages.Add(experienceID + " reduced by "+value+" on level-up");
                }
            }
        }

        [System.Serializable]
        public class RequireSumExperience
        {
            public List<string> experienceIDs = new List<string>();
            public LogicalOperand operand = LogicalOperand.none;
            public int value = 0;
            [JsonIgnore] public bool isValid { get { return experienceIDs != null && experienceIDs.Count > 0 && operand != LogicalOperand.none; } }
            [JsonIgnore] public string Tooltip
            {
                get
                {
                    //Debug.Log("expIDs " + String.Join("|", experienceIDs));
                    List<string> allNames = new List<string>();
                    foreach (var i in experienceIDs) allNames.Add(scr_System_Serializer.current.Dictionary.QueryThenParse(i));
                    //Debug.Log("expIDs " + String.Join("|", allNames));
                    return String.Join(",",allNames) + " sum must be " + scr_System_Serializer.current.Dictionary.QueryThenParse(operand.ToString()) + " " + value;
                }
            }
        }
    }

    [System.Serializable]
    public class SkillLevel
    {
        public Require requirements = null;
        public List<Stat_Modifier> stat_modifiers = new List<Stat_Modifier>();
        public string DisplayName = "";
        public bool ValidateChara(Character_Trainable c, List<string> tooltips = null)
        {
            //Debug.Log("Validating Chara " + c.FirstName + ", requirement null? " + (requirements == null));
            if (requirements == null) return true;
            return requirements.Validate(c, tooltips);
        }

        public void NotifyUpgrade(Character_Trainable c, List<string> messages = null)
        {
            if (requirements == null) return;
            requirements.NotifyUpgrade(c, messages);
        }

    }
}

