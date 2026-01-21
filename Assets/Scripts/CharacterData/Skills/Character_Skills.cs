using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
using UnityEditor;

public class Index_CharaSkills : I_IndexMergeable, I_IndexHasID, I_RemoveElemByTag
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
    public void RegisterAllID(List<string> s)
    {
        foreach (CharaSkill o in this.list)
        {
            ID_Dictionary.Add(o.ID, o);
        }
        s.Add("Index_CharaSkills initialized with " + list.Count + " elements");
    }

    public void RemoveElemByTag(string tag)
    {
        this.list.RemoveAll(x => x.tags.Contains(tag));
    }
}


public interface hasCategory
{
    public List<string> CategoryLabel { get; }
}


public class CharaSkill
{
    public string ID = "";
    public List<string> tags = new List<string>();
    public List<string> categoryTag = new List<string>();
    public bool hasGenderVariant = false;
    public Require requirements = null;
    public bool allowAutoUpgrade = true;
    public List<SkillLevel> Levels = new List<SkillLevel>();
    public bool noDisplay = false;
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

    public abstract class Require
    {
        public List<RequirementEntry> entries = new List<RequirementEntry>();
        public abstract bool Validate(Character_Trainable c, List<string> tooltips = null);
        public abstract void NotifyUpgrade(Character_Trainable c, List<string> messages = null);
    }

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
            if (tooltips != null) tooltips.Add("Require one of the following:");
            foreach (var i in entries) if (i.Validate(c, tooltips)) return true;
            return false;
        }
    }

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
            if (tooltips != null) tooltips.Add("Require all of the following:");
            foreach (var i in entries) if (!i.Validate(c, tooltips)) return false;
            return true;
        }
    }


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
                    if (c.Body == null || !c.Body.HasBodyTag(i))
                    {
                        c2.Add("<color=" + scr_System_CentralControl.current.DisplaySetting.TextColor_conflict.Hex + ">" + i + "</color>");
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
                        c2.Add("<color=" + scr_System_CentralControl.current.DisplaySetting.TextColor_conflict.Hex + ">" + i + "</color>");
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
                        c2.Add("<color=" + scr_System_CentralControl.current.DisplaySetting.TextColor_conflict.Hex + ">" + i.Tooltip + "</color>");
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
                    c2.Add("<color=" + scr_System_CentralControl.current.DisplaySetting.TextColor_conflict.Hex + ">" + requireGender.ToString() + "</color>");
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
                    c2.Add("<color=" + scr_System_CentralControl.current.DisplaySetting.TextColor_conflict.Hex + ">" + requireSumExperiences.Tooltip + "</color> (currently "+sumValue+")");
                    returnValue = false;
                }
                else c2.Add(requireSumExperiences.Tooltip+" (currently "+sumValue+")");
                c1.Add("require Sum Experiences [" + String.Join("|", c2) + "]");
            }

            if (tooltips != null && c1.Count > 0) tooltips.Add(String.Join(" | ",c1));
            return returnValue;
        }

        public class RequireExperience
        {
            public string experienceID = "";
            public LogicalOperand operand = LogicalOperand.none;
            public int value = 0;
            [JsonIgnore] public bool isValid { get { return experienceID != "" && operand != LogicalOperand.none; } }
            [JsonIgnore] public string Tooltip { get
                {
                    return LocalizeDictionary.QueryThenParse(experienceID)+" values must be "+ LocalizeDictionary.QueryThenParse(operand.ToString())+" "+value;
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
                    foreach (var i in experienceIDs) allNames.Add(LocalizeDictionary.QueryThenParse(i));
                    //Debug.Log("expIDs " + String.Join("|", allNames));
                    return String.Join(",",allNames) + " sum must be " + LocalizeDictionary.QueryThenParse(operand.ToString()) + " " + value;
                }
            }
        }
    }


    public class SkillLevel
    {
        public Require requirements = null;
        public List<Stat_Modifier> stat_modifiers = new List<Stat_Modifier>();
        public string DisplayName = "";
        public List<SkillUse> validUse = new List<SkillUse>();

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

    public class SkillUse
    {
        public string skillUseTags = "";
        public List<string> requireSelfTags = new List<string>();
        public List<string> requireCOMTags = new List<string>();
        public List<string> requirePermanentTags = new List<string>();
        public bool ApplyTo(List<string> self, List<string> action)
        {
            if (skillUseTags == "") return false;
            if (!action.Contains(skillUseTags)) return false;
            if (requireSelfTags.Count < 1) { }
            else if (self == null || requireSelfTags.Count > self.Count || !Utility.ListContainsStrict(self, requireSelfTags)) return false;
            if (requireCOMTags.Count > action.Count || !Utility.ListContainsStrict(action, requireCOMTags)) return false;
            return true;
        }

        public int GetExtraMods()
        {
            return 0;
        }
    }
}

