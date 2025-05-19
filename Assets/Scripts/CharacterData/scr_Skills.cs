using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;


public enum Skill_KeyAttribute
{
    Strength,
    Constitution,
    Psyche,
    Willpower,
    None
}

[System.Serializable]
public class Skills
{

    [SerializeField] protected string id;
    public string ID { get { return id; } }

    [JsonIgnore] public string DisplayName { get { return fullData.DisplayName; } }

    // need manual injection
    private Character_Trainable owner = null;
    [JsonIgnore] public Character_Trainable Owner
    {
        get { return owner; }
        set { owner = value; }
    }

    public void Initialize(string id)
    {
        this.id = id;
    }

    [SerializeField]
    private int experience = 0;

    private Skills_Full fullDataStorage = null;
    private Skills_Full fullData
    {
        get
        {
            if (fullDataStorage != null) return fullDataStorage;
            else
            {
                fullDataStorage = scr_System_Serializer.current.GetByNameOrID_Skills(id);
                return fullDataStorage;
            }
        }
    }

    public Skill_KeyAttribute KeyAttribute
    {
        get { return fullData.keyAttribute; }
    }

    /// <summary>
    /// Dont need to input anything
    /// </summary>
    /// <param name="exp"></param>
    /// <returns></returns>
    public int GetSkillLevel(int exp = 0)
    {

        int i = 0;
        if (fullData.keyAttribute == Skill_KeyAttribute.Strength) i = owner.Stats.Strength.GetStatMod();
        else if (fullData.keyAttribute == Skill_KeyAttribute.Constitution) i = owner.Stats.Constitution.GetStatMod();
        else if (fullData.keyAttribute == Skill_KeyAttribute.Psyche) i = owner.Stats.Psyche.GetStatMod();
        else if (fullData.keyAttribute == Skill_KeyAttribute.Willpower) i = owner.Stats.Willpower.GetStatMod();

        i = (i / 2 - 5);
        return fullData.GetSkillLevel(this.experience) + i;
    }
}

[System.Serializable]
public class Skills_Full: Skills
{
    [SerializeField] private string displayName;
    [JsonIgnore] new public string DisplayName { get { return displayName; } }

    [SerializeField]
    public string tooltip;

    [SerializeField]
    public Skill_KeyAttribute keyAttribute = Skill_KeyAttribute.None;

    [SerializeField]
    private int[] expThreshold = new int[10];

    public Skills MakeSkill()
    {
        Skills s = new Skills();
        s.Initialize(this.id);
        return s;
    }

    new public int GetSkillLevel(int exp = 0) 
    { 
        for(int i = 0; i < expThreshold.Length; i++)
        {
            if (expThreshold[i] <= exp) continue;
            else return i - 1;
        }
        return expThreshold.Length;
    }
}

