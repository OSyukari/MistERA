using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;

public class Character_Trainable_SerializableTemplate_Index : I_IndexMergeable, I_IndexHasID, I_RemoveNonExisting
{

    public List<CharaSerializableTemplate_Base> list = new List<CharaSerializableTemplate_Base>();

    Dictionary<string, CharaSerializableTemplate_Base> ID_Dictionary = new Dictionary<string, CharaSerializableTemplate_Base>();
    Dictionary<string, CharaTemplateGenerator> Gen_Dictionary = new Dictionary<string, CharaTemplateGenerator>();
    public CharaSerializableTemplate_Base GetByID(string id) { return ID_Dictionary.ContainsKey(id) ? ID_Dictionary[id] : null; }

    public void RegisterGeneratorTemplate(string id, CharaTemplateGenerator gen)
    {
        this.Gen_Dictionary.Add(id, gen);
    }
    public void RegisterAllID(List<string> s)
    {
        s.Add("Index_CombatActions : registering ID with list length [" + list.Count + "]");

        foreach (var o in this.list)
        {
            if (string.IsNullOrEmpty(o.baseID)) continue;
            if (!ID_Dictionary.TryAdd(o.baseID, o)) Debug.Log($"failed to add Character_Trainable_SerializableTemplate_Index id [{o.baseID}] due to duplicate");
        }

    }

    public void DelTemplate(CharaSerializableTemplate_Base t)
    {
        list.Remove(t);
        ID_Dictionary.Remove(t.baseID);
    }
    public void SetTemplate(CharaSerializableTemplate_Base t)
    {
        if (ID_Dictionary.ContainsKey(t.baseID)) DelTemplate(ID_Dictionary[t.baseID]);
        list.Add(t);
        ID_Dictionary[t.baseID] = t;
    }

    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Character_Trainable_SerializableTemplate_Index;
        if (l == null || l.list == null) return;
        else
        {
            this.list.AddRange(l.list);
        }
    }

    public CharaTemplate GetByCharaBaseID(string baseID)
    {
        if (Gen_Dictionary.ContainsKey(baseID))
        {
            return Gen_Dictionary[baseID].Get;
        }
        else
        {
            var findresult = GetByID(baseID);
            return findresult == null ? null : findresult.GetTemplate;
        }
    }

    public void RemoveNonExisting()
    {
        foreach(var temp in list)
        {
            temp.PurgeNonExistingData();
        }
    }

    // Returns the serializable wrapper for a given baseID so callers can read
    // parentTemplateID and experienceInitializerIDs without going through the
    // resolved CharaTemplate. Falls through to the generator's targetBaseID when
    // the ID belongs to a CharaTemplateGenerator rather than a direct wrapper.
    public CharaSerializableTemplate_Base GetWrapperForBaseID(string id)
    {
        var wrapper = GetByID(id);
        if (wrapper != null) return wrapper;

        if (Gen_Dictionary.TryGetValue(id, out CharaTemplateGenerator gen))
        {
            return gen.targetBaseIDs != null && gen.targetBaseIDs.Count > 0
                ? GetByID(gen.targetBaseIDs[0])
                : null;
        }
        return null;
    }
}

[System.Serializable]
public abstract class CharaSerializableTemplate_Base
{
    public string baseID = "";
    public virtual CharaTemplate GetTemplate {get;set ;}
    public virtual void PurgeNonExistingData()
    {
       // Debug.Log("CALLING VIRTUAL METHOD PurgeNonExistingData");
    }

    // Optional parent template ID for experience initializer inheritance.
    // When set, the parent's resolved initializer IDs are prepended before this template's own.
    public string parentTemplateID = "";
    // IDs referencing ExperienceInitializer entries in MasterList.ExperienceInitializers.
    public List<string> experienceInitializerIDs = new List<string>();
}

[System.Serializable]
public class CharaSerializableTemplate_Safe : CharaSerializableTemplate_Base
{
    public CharaSafeTemplate Template = null;
    public override CharaTemplate GetTemplate 
    { 
        get {
            var randTemplate = this.Generator == null ? null : this.Generator.Get;
            if (randTemplate != null) return randTemplate as CharaSafeTemplate;
            else return Template; } 
        set {
            if (this.Generator != null)
            {
                Debug.LogError("ERROR CANNOT SET RANDOM GENERATOR TEMPLATE");
            }
            else
            {
                Template = value as CharaSafeTemplate;
            }
        } 
    }
    public CharaTemplateGenerator Generator = null;
    public override void PurgeNonExistingData()
    {
      //  Debug.Log($"CALLING override METHOD PurgeNonExistingData Character_SerializableSafe with entries {(Template == null ? "null" : Template.initialInventory.Count)}");
        if (Template == null) return;
        for (int i = Template.initialInventory.Count - 1; i >= 0; i--)
        {
            var inv = Template.initialInventory[i];
            if (Masterlist_Items.GetByID(inv.ID) == null)
            {
             //   Debug.Log($"Removing inventoryEntry {(inv.ID)}");
                Template.initialInventory.RemoveAt(i);
            }

        }
    }
}
[System.Serializable]
public class CharaSerializableTemplate_Trainable : CharaSerializableTemplate_Base
{
    public CharaTrainableTemplate Template = null;
    public override CharaTemplate GetTemplate
    {
        get
        {
            var randTemplate = this.Generator == null ? null : this.Generator.Get;
            if (randTemplate != null) return randTemplate as CharaTrainableTemplate;
            else return Template;
        }
        set
        {
            if (this.Generator != null)
            {
                Debug.LogError("ERROR CANNOT SET RANDOM GENERATOR TEMPLATE");
            }
            else
            {
                Template = value as CharaTrainableTemplate;
            }
        }
    }
    public CharaTemplateGenerator Generator = null;
    public override void PurgeNonExistingData()
    {
      //  Debug.Log($"CALLING override METHOD PurgeNonExistingData Character_SerializableSafe with entries {(Template == null ? "null" : Template.initialInventory.Count)}");
        if (Template == null) return;
        for (int i = Template.initialInventory.Count - 1; i >= 0; i--)
        {
            var inv = Template.initialInventory[i];
            if (Masterlist_Items.GetByID(inv.ID) == null)
            {
            //    Debug.Log($"Removing inventoryEntry {(inv.ID)}");
                Template.initialInventory.RemoveAt(i);
            }

        }
    }

}


[System.Serializable]
public abstract class CharaTemplate
{
    public List<Skills> Skills = new List<Skills>();
    public int Height = 162;//cm
    public double HWMultiplier = 0.43;
    public int Weight = 70; //kg
    public Humanoid_GenderAppearance Appearance = Humanoid_GenderAppearance.Female;
    public int stat_STR = 10, stat_CON = 10, stat_PSY = 10, stat_WIL = 10;
    public string personalityID = "personality_default";
    public string characterComment = "";
    public string CharacterCard = "";
    public List<string> traits = new List<string>();
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

    public abstract CharaTemplate Copy();

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

    public override CharaTemplate Copy()
    {
        var newInstance = new CharaSafeTemplate();
        newInstance.Appearance = Appearance;
        newInstance.Skills = new List<Skills>();
        if (this.Skills != null) newInstance.Skills.AddRange(this.Skills);
        newInstance.Height = Height;
        newInstance.Weight = Weight;
        newInstance.stat_STR = stat_STR;
        newInstance.stat_CON = stat_CON;
        newInstance.stat_PSY = stat_PSY;
        newInstance.stat_WIL = stat_WIL;
        newInstance.personalityID = personalityID;
        newInstance.initialInventory = new List<presetInventory>(initialInventory);
        newInstance.initialRelationship = new List<RelationshipManager.presetRelationship>(initialRelationship);
        newInstance.initialExperiences = new List<string>(initialExperiences);
        newInstance.basicExperience = basicExperience;
        return newInstance;
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


    public override CharaTemplate Copy()
    {
        var newInstance = new CharaTrainableTemplate();
        newInstance.Appearance = Appearance;
        newInstance.Skills = new List<Skills>();
        if (this.Skills != null) newInstance.Skills.AddRange(this.Skills);
        newInstance.Height = Height;
        newInstance.Weight = Weight;
        newInstance.stat_STR = stat_STR;
        newInstance.stat_CON = stat_CON;
        newInstance.stat_PSY = stat_PSY;
        newInstance.stat_WIL = stat_WIL;
        newInstance.personalityID = personalityID;
        newInstance.initialInventory = new List<presetInventory>(initialInventory);
        newInstance.initialRelationship = new List<RelationshipManager.presetRelationship>(initialRelationship);

        newInstance.basicExperience = basicExperience;
        newInstance.initialExperiences = new List<string>(initialExperiences);
        newInstance.BodyType = BodyType;



        return newInstance;
    }
}