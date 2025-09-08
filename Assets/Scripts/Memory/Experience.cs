using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
/*
[System.Serializable]
public class Index_Sexperiences : I_IndexHasID, I_IndexMergeable
{
    public List<Sexperience_Base> list = new List<Sexperience_Base>();

    Dictionary<string, Sexperience_Base> ID_Dictionary = new Dictionary<string, Sexperience_Base>();
    public void RegisterAllID(List<string> messages)
    {
        messages.Add("Index_Sexperiences : registering ID with list length [" + list.Count +"]");

        foreach (Sexperience_Base o in this.list)
        {
            if (o.isValid) ID_Dictionary.Add(o.ID, o);
        }
    }
    public Sexperience_Base GetByID(string id) { return ID_Dictionary.ContainsKey(id) ? ID_Dictionary[id] : null; }
    public void MergeWith(I_IndexMergeable list){
        var l = list as Index_Sexperiences;
        if (l == null) return;
        else if (l.list == null || l.list.Count < 1) return;
        else this.list.AddRange(l.list);
        
    }
}






[System.Serializable]
public class Sexperience_Base
{
    public string ID = "";
    public string instanceClass = "";
    public bool hasClimaxVariant = true;
    public bool hasGenderVariant = false;
    public Condition Conditions = null;
    public Rank rank = null;
    public string DisplayAmountString = "";

   [JsonIgnore] public bool isValid
    {
        get
        {
            return ID != "";
            //return false;
        }
    }

    [System.Serializable]
    public class Condition
    {
        public List<string> requiredCOMTags = new List<string>();
        // public string requiredTargetGender = "";
        public string targetRaceString = "";
        public bool applyToDoer = false;
        public bool applyToReceiver = false;
        public bool excludeSelfAsTarget = true;
        public bool excludeFemaleTarget = false;
        public bool excludeMaleTarget = false;

        public bool Validate(Character_Trainable self, List<string> comTags, Character_Trainable target, bool isDoer)
        {
            if (targetRaceString != "" && !target.Race.ID.Contains(targetRaceString)) return false;

            if (requiredCOMTags.Count > 0 && !Utility.ListContainsLoose(comTags, requiredCOMTags)) return false;

            if (excludeSelfAsTarget && (target == null || target.RefID == self.RefID)) return false;

            if (applyToDoer && !isDoer) return false;
            if (applyToReceiver && isDoer) return false;

            if (excludeFemaleTarget && target.isFemale) return false;
            if (excludeMaleTarget && target.isMale) return false;

            return true;
        }
    }

    public bool Validate(Character_Trainable self, List<string> comTags, Character_Trainable target, bool isDoer)
    {
        return Conditions.Validate(self, comTags, target, isDoer);
    }

    public Sexperience_Instance Instantiate()
    {
        switch (instanceClass)
        {
            case "Sexperience_Race": return new Sexperience_Race(this);
            default: return new Sexperience_Instance(this);
        }
    }

    [System.Serializable]
    public class Rank
    {
        public string displayName = "";
        public List<int> threshold = new List<int>();
        public bool basedOnClimaxCountOnly = false;

        public int GetRank(int ii)
        {
            if (threshold.Count < 1) return 0;
            for (int i = 0; i < threshold.Count; i++)
            {
                if (ii < threshold[i]) return i;
            }
            return threshold.Count;
        }
    }


}


[System.Serializable]
public class Sexperience_Instance
{
    protected string baseID;
    //public class Experience_Instance
    protected Sexperience_Base baseRef = null;
    protected Sexperience_Base Base { get { 
            if (baseRef == null) baseRef = scr_System_Serializer.current.GetByNameOrID_ExperienceBase(baseID);
            return baseRef;
        } }

    protected int counter;
    protected int counter_climax;

    public Sexperience_Instance(Sexperience_Base b)
    {
        this.baseID = b.ID;
        this.baseRef = b;
        counter = 0;
        counter_climax = 0;
    }

    public virtual List<string> requiredCOMTags { get { return Base.Conditions.requiredCOMTags; } }

    public string DisplayName(Character_Trainable c)
    {
        if (Base.hasGenderVariant)
        {
            if (c.isFemale) return LocalizeDictionary.QueryThenParse(Base.ID + "_DisplayName_Female");
            else return LocalizeDictionary.QueryThenParse(Base.ID + "_DisplayName_Male");
        }else return LocalizeDictionary.QueryThenParse(Base.ID + "_DisplayName");
    }

    public virtual bool Validate(Character_Trainable self, List<string> comTags, Character_Trainable target, bool isDoer)
    {
        return Base.Validate(self, comTags, target, isDoer);
    }

    public string DisplayName_Climax(Character_Trainable c)
    {
        if (Base.hasGenderVariant)
        {
            if (c.isFemale) return LocalizeDictionary.QueryThenParse(Base.ID + "_Climax_DisplayName_Female");
            else return LocalizeDictionary.QueryThenParse(Base.ID + "_Climax_DisplayName_Male");
        }
        else return LocalizeDictionary.QueryThenParse(Base.ID+ "_Climax_DisplayName");
    }

    public void Add(int value, bool isClimax)
    {
        if (isClimax && Base.hasClimaxVariant) counter_climax += value;
        else counter += value;
    }

    public int Rank
    {
        get
        {
            if (Base.rank.basedOnClimaxCountOnly) return Base.rank.GetRank(counter_climax);
            else return Base.rank.GetRank(counter + counter_climax);
        }
    }

    public string RankDisplayName(Character_Trainable c)
    {

        if (Base.hasGenderVariant)
        {
            if (c.isFemale) return LocalizeDictionary.QueryThenParse(Base.ID + "_RankDisplayName_Female");
            else return LocalizeDictionary.QueryThenParse(Base.ID + "_RankDisplayName_Male");
        }
        else return LocalizeDictionary.QueryThenParse(Base.ID + "_RankDisplayName");
        
    }

}*/



[System.Serializable]
public class ExperienceClass
{
    public string ExperienceID = "";
    public string DisplayAmountString = "";
    public List<string> tags = new List<string>();
    public bool CountTotal = false;
    public bool ApplyToDoer = true;
    public bool ApplyToReceiver = true;
    public List<string> RequiredOwnerTags = new List<string>();
    public List<string> ExcludeOwnerTags = new List<string>();
    public List<string> RequiredCOMTags = new List<string>();
    public List<string> ExcludeCOMTags = new List<string>();
}

[System.Serializable]
public class Index_Experiences : I_IndexMergeable, I_IndexHasID, I_RemoveElemByTag
{
    [JsonProperty] protected List<ExperienceClass> list = new List<ExperienceClass>();
    protected System.Collections.Concurrent.ConcurrentDictionary<string, ExperienceClass> _List;
    [JsonIgnore] public List<ExperienceClass> List { get { return list; } }
    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Index_Experiences;
        if (l == null) return;
        else if (l.list == null) return;
        else
        {
            this.list.AddRange(l.list);
        }
    }

    public void RegisterAllID(List<string> messages)
    {
        messages.Add("Registering Experiences with count " + list.Count);

        var ids = new Dictionary<string, ExperienceClass>();
        foreach(var i in list) ids.Add(i.ExperienceID, i);
        _List = new System.Collections.Concurrent.ConcurrentDictionary<string, ExperienceClass>(ids);
    }

    public ExperienceClass GetByID(string id)
    {
        if(_List.TryGetValue(id, out ExperienceClass result)) return result;
        return null;
    }

    public void RemoveElemByTag(string tag)
    {
        this.list.RemoveAll(x => x.tags.Contains(tag));
    }

}

