using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.IO;
using System.Collections;
using System.Linq;
using Cysharp.Threading.Tasks;


[System.Serializable]
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
            // if (o.isValid)
            ID_Dictionary.TryAdd(o.baseID, o);
        }

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
    public int Height = 163;
    public Humanoid_GenderAppearance Appearance = Humanoid_GenderAppearance.Female;
    public int stat_STR = 10, stat_CON = 10, stat_PSY = 10, stat_WIL = 10;
    public string personalityID = "personality_default";

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

    public abstract CharaTemplate Copy();

    public abstract void SetGender(Humanoid_GenderAppearance gender);
}

[System.Serializable]
public class presetInventory
{
    public string ID = "";
    public string nameOverwrite = "";
}

[System.Serializable]
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
        newInstance.stat_STR = stat_STR;
        newInstance.stat_CON = stat_CON;
        newInstance.stat_PSY = stat_PSY;
        newInstance.stat_WIL = stat_WIL;
        newInstance.personalityID = personalityID;
        newInstance.initialInventory = new List<presetInventory>(initialInventory);
        newInstance.initialRelationship = new List<RelationshipManager.presetRelationship>(initialRelationship);
        return newInstance;
    }

    public override void SetGender(Humanoid_GenderAppearance gender)
    {
        Appearance = gender;
    }
}

[System.Serializable]
public class CharaTrainableTemplate : CharaTemplate
{ 

    public CharaTrainableTemplate() { }

    public Character_BodyType BodyType = Character_BodyType.Default;

    [JsonProperty] private string sensitivity_B = "trait_Sensitivity_B_default";
    [JsonProperty] private string sensitivity_M = "trait_Sensitivity_M_default";
    [JsonProperty] private string sensitivity_C = "trait_Sensitivity_C_default";
    [JsonProperty] private string sensitivity_V = "trait_Sensitivity_V_default";
    [JsonProperty] private string sensitivity_A = "trait_Sensitivity_A_default";

    [JsonProperty] private string size_B = "trait_Size_B_none";
    [JsonProperty] private string size_P = "trait_Size_P_none";
    [JsonProperty] private string size_V = "trait_Size_V_none";
    [JsonProperty] private string size_A = "trait_Size_A_none";

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
    public Traits Sensitivity_B
    {
        get { return scr_System_Serializer.current.GetByNameOrID_Traits(sensitivity_B); }
        set { sensitivity_B = value.ID; }
    }
    [JsonIgnore]
    public Traits Sensitivity_M
    {
        get { return scr_System_Serializer.current.GetByNameOrID_Traits(sensitivity_M); }
        set { sensitivity_M = value.ID; }
    }
    [JsonIgnore]
    public Traits Sensitivity_C
    {
        get { return scr_System_Serializer.current.GetByNameOrID_Traits(sensitivity_C); }
        set { sensitivity_C = value.ID; }
    }
    [JsonIgnore]
    public Traits Sensitivity_V
    {
        get { return scr_System_Serializer.current.GetByNameOrID_Traits(sensitivity_V); }
        set { sensitivity_V = value.ID; }
    }
    [JsonIgnore]
    public Traits Sensitivity_A
    {
        get { return scr_System_Serializer.current.GetByNameOrID_Traits(sensitivity_A); }
        set { sensitivity_A = value.ID; }
    }


    [JsonIgnore]
    public Traits Size_B
    {
        get { return scr_System_Serializer.current.GetByNameOrID_Traits(size_B); }
        set { size_B = value.ID; }
    }
    [JsonIgnore]
    public Traits Size_P
    {
        get { return scr_System_Serializer.current.GetByNameOrID_Traits(size_P); }
        set { size_P = value.ID; }
    }
    [JsonIgnore]
    public Traits Size_V
    {
        get { return scr_System_Serializer.current.GetByNameOrID_Traits(size_V); }
        set { size_V = value.ID; }
    }
    [JsonIgnore]
    public Traits Size_A
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
        newInstance.stat_STR = stat_STR;
        newInstance.stat_CON = stat_CON;
        newInstance.stat_PSY = stat_PSY;
        newInstance.stat_WIL = stat_WIL;
        newInstance.personalityID = personalityID;
        newInstance.initialInventory = new List<presetInventory>(initialInventory);
        newInstance.initialRelationship = new List<RelationshipManager.presetRelationship>(initialRelationship);
        newInstance.BodyType = BodyType;



        return newInstance;
    }
}