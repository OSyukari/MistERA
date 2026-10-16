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
    /// <summary> non-body traits </summary>
    public List<string> traits = new List<string>();
    /// <summary> non-sexual body traits (skin, hair...), seed data copied onto Character_Body </summary>
    public List<string> traits_body = new List<string>();
    /// <summary> seed data copied onto Character_Body.hairstyle (null = random default hairstyle) </summary>
    public Hairstyle_Instance defaultHairstyle = null;
    /// <summary> preset wardrobe outfits, seed data copied onto Character_Wardrobe (by key, missing keys only) </summary>
    public List<OutfitSet> outfits = new List<OutfitSet>();
    public List<string> actorKeyword = new List<string>();
    /// <summary> optional image prompt layer on top of the global default and race profiles (ActorPromptProfileUtility) </summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public ActorPromptProfile promptProfile = null;
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

    /// <summary>
    /// Body traits this template seeds a new body with. Every id in traits / traits_body (and traits_body_sex)
    /// is classified by its group type, so an id written in the wrong list still lands in the right place.
    /// </summary>
    public virtual void GetSeedBodyTraits(List<Traits> result)
    {
        CollectBodyTraits(traits, false, result);
        CollectBodyTraits(traits_body, true, result);
    }

    protected void CollectBodyTraits(List<string> ids, bool expectBody, List<Traits> result)
    {
        if (ids == null) return;
        foreach (var id in ids)
        {
            var t = BodyTraitUtility.Resolve(id);
            if (t == null || t.Parent == null) continue;
            if (t.Parent.isBodyTrait != expectBody) Debug.LogWarning($"template trait [{id}] is listed as {(expectBody ? "body" : "non-body")} trait but its group [{t.Parent.ID}] is {t.Parent.Type}");
            if (t.Parent.isBodyTrait) result.Add(t);
        }
    }
}

public class presetInventory
{
    public string ID = "";
    public string nameOverwrite = "";
    public List<string> promptTags = null;
    /// <summary> replaces the base item's promptTags and promptTags_slots </summary>
    public List<string> promptTags_baseOverride = null;
    /// <summary> slot name -> tags shown while that slot is visible, see Item_Base.promptTags_slots </summary>
    public Dictionary<string, List<string>> promptTags_slots = null;
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

    /// <summary> sexual body traits (part size, sensitivity), seed data copied onto Character_Body </summary>
    public List<string> traits_body_sex = new List<string>();

    // legacy read shim: pre-refactor templates stored the 9 sexual body traits as separate fields.
    // write-only, so old json (unmigrated data, players' custom characters) still loads, and is never written back.
    [JsonProperty("sensitivity_B")] private string legacy_sensitivity_B { set { SetLegacy(BodyTraitUtility.Group_Sensitivity_B, value); } }
    [JsonProperty("sensitivity_M")] private string legacy_sensitivity_M { set { SetLegacy(BodyTraitUtility.Group_Sensitivity_M, value); } }
    [JsonProperty("sensitivity_C")] private string legacy_sensitivity_C { set { SetLegacy(BodyTraitUtility.Group_Sensitivity_C, value); } }
    [JsonProperty("sensitivity_V")] private string legacy_sensitivity_V { set { SetLegacy(BodyTraitUtility.Group_Sensitivity_V, value); } }
    [JsonProperty("sensitivity_A")] private string legacy_sensitivity_A { set { SetLegacy(BodyTraitUtility.Group_Sensitivity_A, value); } }
    [JsonProperty("size_B")] private string legacy_size_B { set { SetLegacy(BodyTraitUtility.Group_Size_B, value); } }
    [JsonProperty("size_P")] private string legacy_size_P { set { SetLegacy(BodyTraitUtility.Group_Size_P, value); } }
    [JsonProperty("size_V")] private string legacy_size_V { set { SetLegacy(BodyTraitUtility.Group_Size_V, value); } }
    [JsonProperty("size_A")] private string legacy_size_A { set { SetLegacy(BodyTraitUtility.Group_Size_A, value); } }

    void SetLegacy(string groupID, string traitID)
    {
        // trait index may not be registered yet while json loads, SetInList falls back to the group prefix
        BodyTraitUtility.SetInList(traits_body_sex, traitID, groupID);
    }

    public override void GetSeedBodyTraits(List<Traits> result)
    {
        base.GetSeedBodyTraits(result);
        CollectBodyTraits(traits_body_sex, true, result);
    }

    public override void SetGender(Humanoid_GenderAppearance gender)
    {
        GenderAppearance_Set(gender, true, true);
    }
    public void GenderAppearance_Set(Humanoid_GenderAppearance app, bool forceDefaultGenital = false, bool forceDefaultSensitivity = false)
    {
        this.Appearance = app;
        if (forceDefaultGenital && !scr_System_CentralControl.current.isSafeMode)
        {
            Size_P = BodyTraitUtility.GetDefault(BodyTraitUtility.Group_Size_P, app);
            Size_B = BodyTraitUtility.GetDefault(BodyTraitUtility.Group_Size_B, app);
            Size_V = BodyTraitUtility.GetDefault(BodyTraitUtility.Group_Size_V, app);
            Size_A = BodyTraitUtility.GetDefault(BodyTraitUtility.Group_Size_A, app);
        }

        if (forceDefaultSensitivity && !scr_System_CentralControl.current.isSafeMode)
        {
            Sensitivity_B = BodyTraitUtility.GetDefault(BodyTraitUtility.Group_Sensitivity_B, app);
            Sensitivity_M = BodyTraitUtility.GetDefault(BodyTraitUtility.Group_Sensitivity_M, app);
            Sensitivity_C = BodyTraitUtility.GetDefault(BodyTraitUtility.Group_Sensitivity_C, app);
            Sensitivity_V = BodyTraitUtility.GetDefault(BodyTraitUtility.Group_Sensitivity_V, app);
            Sensitivity_A = BodyTraitUtility.GetDefault(BodyTraitUtility.Group_Sensitivity_A, app);
        }
    }

    /// <summary>
    /// Template seed value for a sexual body trait group; falls back to the gender default (same as the body does)
    /// </summary>
    protected Traits GetSexTrait(string groupID)
    {
        var t = BodyTraitUtility.FindInGroup(traits_body_sex, groupID);
        if (t != null) return t;
        return BodyTraitUtility.GetDefault(groupID, Appearance);
    }
    protected void SetSexTrait(Traits value)
    {
        if (value == null) return;
        BodyTraitUtility.SetInList(traits_body_sex, value);
    }

    [JsonIgnore]
    public override bool isMale
    {
        get
        {
            var t = Size_P;
            return t != null && t.ID != "trait_Size_P_none";
        }
    }

    [JsonIgnore]
    public override bool isFemale
    {
        get
        {
            var t = Size_V;
            return t != null && t.ID != "trait_Size_V_none";
        }
    }


    /// <summary> optional; null = age hidden/defaulted, omitted when serialized </summary>
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public int? Age = null;
    [JsonIgnore] public override Traits Sensitivity_B { get { return GetSexTrait(BodyTraitUtility.Group_Sensitivity_B); } set { SetSexTrait(value); } }
    [JsonIgnore] public override Traits Sensitivity_M { get { return GetSexTrait(BodyTraitUtility.Group_Sensitivity_M); } set { SetSexTrait(value); } }
    [JsonIgnore] public override Traits Sensitivity_C { get { return GetSexTrait(BodyTraitUtility.Group_Sensitivity_C); } set { SetSexTrait(value); } }
    [JsonIgnore] public override Traits Sensitivity_V { get { return GetSexTrait(BodyTraitUtility.Group_Sensitivity_V); } set { SetSexTrait(value); } }
    [JsonIgnore] public override Traits Sensitivity_A { get { return GetSexTrait(BodyTraitUtility.Group_Sensitivity_A); } set { SetSexTrait(value); } }

    [JsonIgnore] public override Traits Size_B { get { return GetSexTrait(BodyTraitUtility.Group_Size_B); } set { SetSexTrait(value); } }
    [JsonIgnore] public override Traits Size_P { get { return GetSexTrait(BodyTraitUtility.Group_Size_P); } set { SetSexTrait(value); } }
    [JsonIgnore] public override Traits Size_V { get { return GetSexTrait(BodyTraitUtility.Group_Size_V); } set { SetSexTrait(value); } }
    [JsonIgnore] public override Traits Size_A { get { return GetSexTrait(BodyTraitUtility.Group_Size_A); } set { SetSexTrait(value); } }


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
        this.Age = (t as CharaTrainableTemplate)?.Age;
    }
}