using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;

/// <summary>
/// This is just an interface to share data with character_serializable and extract templatedata
/// </summary>
public abstract class CharaSerializableTemplate_Base
{
    public string baseID = "";

}

/// <summary>
/// This is just an interface to share data with character_serializable and extract templatedata
/// </summary>
public class CharaSerializableTemplate_Safe : CharaSerializableTemplate_Base
{
    public CharaSafeTemplate Template = null;
}
/// <summary>
/// This is just an interface to share data with character_serializable and extract templatedata
/// </summary>
public class CharaSerializableTemplate_Trainable : CharaSerializableTemplate_Base
{
    public CharaTrainableTemplate Template = null;
}


public abstract class CharaTemplate
{
    [JsonIgnore]
    public virtual string GetCharacterCard
    {
        get
        {
            return null;
        }
    }


    [JsonIgnore] public string refID = "";

    public List<Skills> Skills = new List<Skills>();
    public int Height = 162;//cm
    public int Weight = 70; //kg
    public Humanoid_GenderAppearance Appearance = Humanoid_GenderAppearance.Female;
    public int stat_STR = 10, stat_CON = 10, stat_PSY = 10, stat_WIL = 10;
    public string personalityID = "personality_base";
    public string characterComment = "";
    public List<string> traits = new List<string>();
    public List<string> actorKeyword = new List<string>();
    [JsonIgnore]
    public virtual bool isMale
    {
        get;
    }

    [JsonIgnore]
    public virtual bool isFemale
    {
        get;
    }

    public List<RelationshipManager.presetRelationship> initialRelationship = new List<RelationshipManager.presetRelationship>();
    public List<presetInventory> initialInventory = new List<presetInventory>();
    public List<presetInventory> overrideInventory = new List<presetInventory>();

    public List<string> basicExperience = new List<string>();
    public List<string> initialExperiences = new List<string>();

    public abstract void Load(CharaTemplate t);

    public abstract void SetGender(Humanoid_GenderAppearance gender);


    [JsonIgnore] public abstract Traits Sensitivity_B { get; set; }
    [JsonIgnore] public abstract Traits Sensitivity_M { get; set; }
    [JsonIgnore] public abstract Traits Sensitivity_C { get; set; }
    [JsonIgnore] public abstract Traits Sensitivity_V { get; set; }
    [JsonIgnore] public abstract Traits Sensitivity_A { get; set; }

    [JsonIgnore] public abstract Traits Size_B { get; set; }
    [JsonIgnore] public abstract Traits Size_P { get; set; }
    [JsonIgnore] public abstract Traits Size_V { get; set; }
    [JsonIgnore] public abstract Traits Size_A { get; set; }
}

public class presetInventory
{
    public string ID = "";
    public string nameOverwrite = "";
}

public class CharaSafeTemplate : CharaTemplate
{

    public CharaSafeTemplate() { }


    [JsonIgnore]
    public override bool isMale
    {
        get
        {
            return Appearance == Humanoid_GenderAppearance.Male;
        }
    }

    [JsonIgnore]
    public override bool isFemale
    {
        get
        {
            return Appearance == Humanoid_GenderAppearance.Female;
        }
    }

    public override void Load(CharaTemplate t)
    {
        this.Appearance = t.Appearance;
        this.Skills = new List<Skills>(t.Skills);
        this.Height = t.Height;
        this.Weight = t.Weight;
        this.stat_STR = t.stat_STR;
        this.stat_CON = t.stat_CON;
        this.stat_PSY = t.stat_PSY;
        this.stat_WIL = t.stat_WIL;
        this.personalityID = t.personalityID;
        this.initialInventory = new List<presetInventory>(t.initialInventory);
        this.initialRelationship = new List<RelationshipManager.presetRelationship>(t.initialRelationship);
        this.initialExperiences = new List<string>(t.initialExperiences);
        this.basicExperience = t.basicExperience;
    }

    public override void SetGender(Humanoid_GenderAppearance gender)
    {
        Appearance = gender;
    }

    [JsonIgnore] public override Traits Sensitivity_B { get { return null; } set { return; } }
    [JsonIgnore] public override Traits Sensitivity_M { get { return null; } set { return; } }
    [JsonIgnore] public override Traits Sensitivity_C { get { return null; } set { return; } }
    [JsonIgnore] public override Traits Sensitivity_V { get { return null; } set { return; } }
    [JsonIgnore] public override Traits Sensitivity_A { get { return null; } set { return; } }

    [JsonIgnore] public override Traits Size_B { get { return null; } set { return; } }
    [JsonIgnore] public override Traits Size_P { get { return null; } set { return; } }
    [JsonIgnore] public override Traits Size_V { get { return null; } set { return; } }
    [JsonIgnore] public override Traits Size_A { get { return null; } set { return; } }
}

[System.Serializable]
public class CharaTrainableTemplate : CharaTemplate
{ 

    public CharaTrainableTemplate() { }

    public Character_BodyType BodyType = Character_BodyType.Default; 
    [JsonIgnore]
    public override string GetCharacterCard
    {
        get
        {
            return CharacterCard;
        }
    }
    public string CharacterCard = "";

    [JsonProperty] public string sensitivity_B = "trait_Sensitivity_B_default";
    [JsonProperty] public string sensitivity_M = "trait_Sensitivity_M_default";
    [JsonProperty] public string sensitivity_C = "trait_Sensitivity_C_default";
    [JsonProperty] public string sensitivity_V = "trait_Sensitivity_V_default";
    [JsonProperty] public string sensitivity_A = "trait_Sensitivity_A_default";

    [JsonProperty] public string size_B = "trait_Size_B_none";
    [JsonProperty] public string size_P = "trait_Size_P_none";
    [JsonProperty] public string size_V = "trait_Size_V_none";
    [JsonProperty] public string size_A = "trait_Size_A_none";

    public override void SetGender(Humanoid_GenderAppearance gender)
    {
        GenderAppearance_Set(gender, true, true);
    }
    public void GenderAppearance_Set(Humanoid_GenderAppearance app, bool forceDefaultGenital = false, bool forceDefaultSensitivity = false)
    {
        this.Appearance = app;
        if (forceDefaultGenital && !scr_System_CentralControl.current.isSafeMode)
        {
            switch (app)
            {
                case Humanoid_GenderAppearance.Male:
                    Size_P = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Size_P").getNeutralinGroup();
                    Size_B = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Size_B").entries[1];
                    Size_V = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Size_V").entries[0];
                    Size_A = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Size_A").getNeutralinGroup();
                    break;
                case Humanoid_GenderAppearance.Female:
                    Size_P = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Size_P").entries[0];
                    Size_B = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Size_B").getNeutralinGroup();
                    Size_V = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Size_V").getNeutralinGroup();
                    Size_A = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Size_A").getNeutralinGroup();
                    break;
                case Humanoid_GenderAppearance.Ambiguous:
                    Size_P = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Size_P").getNeutralinGroup();
                    Size_B = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Size_B").getNeutralinGroup();
                    Size_V = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Size_V").getNeutralinGroup();
                    Size_A = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Size_A").getNeutralinGroup();
                    break;
                //case Humanoid_GenderAppearance.Inhuman:
                default:
                    Size_P = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Size_P").entries[0];
                    Size_B = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Size_B").entries[0];
                    Size_V = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Size_V").entries[0];
                    Size_A = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Size_A").entries[0];
                    break;
            }
        }

        if (forceDefaultSensitivity && !scr_System_CentralControl.current.isSafeMode)
        {
            Sensitivity_B = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Sensitivity_B").getNeutralinGroup();
            Sensitivity_M = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Sensitivity_M").getNeutralinGroup();
            Sensitivity_C = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Sensitivity_C").getNeutralinGroup();
            Sensitivity_V = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Sensitivity_V").getNeutralinGroup();
            Sensitivity_A = scr_System_Serializer.current.GetByNameOrID_TraitsGroup("trait_Sensitivity_A").getNeutralinGroup();
        }
    }

    [JsonIgnore]
    public override bool isMale
    {
        get
        {
            if (this.Size_P.ID != "trait_Size_P_none") return true;
            else return false;
        }
    }

    [JsonIgnore]
    public override bool isFemale
    {
        get
        {
            if (this.Size_V.ID != "trait_Size_V_none") return true;
            else return false;
        }
    }


    [JsonIgnore]
    public override Traits Sensitivity_B
    {
        get { return scr_System_Serializer.current.GetByNameOrID_Traits(sensitivity_B); }
        set { sensitivity_B = value.ID; }
    }
    [JsonIgnore]
    public override Traits Sensitivity_M
    {
        get { return scr_System_Serializer.current.GetByNameOrID_Traits(sensitivity_M); }
        set { sensitivity_M = value.ID; }
    }
    [JsonIgnore]
    public override Traits Sensitivity_C
    {
        get { return scr_System_Serializer.current.GetByNameOrID_Traits(sensitivity_C); }
        set { sensitivity_C = value.ID; }
    }
    [JsonIgnore]
    public override Traits Sensitivity_V
    {
        get { return scr_System_Serializer.current.GetByNameOrID_Traits(sensitivity_V); }
        set { sensitivity_V = value.ID; }
    }
    [JsonIgnore]
    public override Traits Sensitivity_A
    {
        get { return scr_System_Serializer.current.GetByNameOrID_Traits(sensitivity_A); }
        set { sensitivity_A = value.ID; }
    }


    [JsonIgnore]
    public override Traits Size_B
    {
        get { return scr_System_Serializer.current.GetByNameOrID_Traits(size_B); }
        set { size_B = value.ID; }
    }
    [JsonIgnore]
    public override Traits Size_P
    {
        get { return scr_System_Serializer.current.GetByNameOrID_Traits(size_P); }
        set { size_P = value.ID; }
    }
    [JsonIgnore]
    public override Traits Size_V
    {
        get { return scr_System_Serializer.current.GetByNameOrID_Traits(size_V); }
        set { size_V = value.ID; }
    }
    [JsonIgnore]
    public override Traits Size_A
    {
        get { return scr_System_Serializer.current.GetByNameOrID_Traits(size_A); }
        set { size_A = value.ID; }
    }


    public override void Load(CharaTemplate t)
    {





       // this.BodyType = t.BodyType;

        this.Appearance = t.Appearance;
        this.Skills = new List<Skills>(t.Skills);
        this.Height = t.Height;
        this.Weight = t.Weight;
        this.stat_STR = t.stat_STR;
        this.stat_CON = t.stat_CON;
        this.stat_PSY = t.stat_PSY;
        this.stat_WIL = t.stat_WIL;
        this.personalityID = t.personalityID;
        this.initialInventory = new List<presetInventory>(t.initialInventory);
        this.initialRelationship = new List<RelationshipManager.presetRelationship>(t.initialRelationship);
        this.initialExperiences = new List<string>(t.initialExperiences);
        this.basicExperience = t.basicExperience;

    }
}