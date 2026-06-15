using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.IO;
using System.Collections;
using System.Linq;


public enum NameCulture
{
    none,
    japanese,
    english,
    goblin
}

[System.Serializable]
public class Index_EncounterGen : I_IndexHasID, I_IndexMergeable
{
    public List<TeamTemplate> list = new List<TeamTemplate>();

    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Index_EncounterGen;
        if (l == null) return;
        else if (l.list == null) return;
        else
        {
            this.list.AddRange(l.list);
        }
    }
    public TeamTemplate GetByID(string id) { return ID_Dictionary.ContainsKey(id) ? ID_Dictionary[id] : null; }
    Dictionary<string, TeamTemplate> ID_Dictionary = new Dictionary<string, TeamTemplate>();
    public void RegisterAllID(List<string> s)
    {
        s.Add("Index_CombatActions : registering ID with list length [" + list.Count + "]");

        foreach (TeamTemplate o in this.list)
        {
            if (string.IsNullOrEmpty(o.ID)) continue;
            if (!ID_Dictionary.TryAdd(o.ID, o)) Debug.Log($"failed to add Index_EncounterGen id [{o.ID}] due to duplicate");
        }
    }
}

[System.Serializable]
public class Index_CharGenTemplates : I_IndexHasID, I_IndexMergeable, I_NeedLateInitialize
{
    public List<CharaTemplateGenerator> list = new List<CharaTemplateGenerator>();
    public Dictionary<NameCulture, NameGenerator> names = new Dictionary<NameCulture, NameGenerator>();

    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Index_CharGenTemplates;
        if (l == null) return;
        else if (l.list == null) return;
        else
        {
            this.list.AddRange(l.list);
            foreach(var kvp in l.names)
            {
                if (this.names.ContainsKey(kvp.Key)) this.names[kvp.Key].MergeWith(kvp.Value);
                else this.names.Add(kvp.Key, kvp.Value);
            }
        }
    }
    public CharaTemplateGenerator GetByID(string id) { return ID_Dictionary.ContainsKey(id) ? ID_Dictionary[id] : null; }
    Dictionary<string, CharaTemplateGenerator> ID_Dictionary = new Dictionary<string, CharaTemplateGenerator>();
    public void RegisterAllID(List<string> s)
    {
        s.Add("Index_CombatActions : registering ID with list length [" + list.Count + "]");

        foreach (CharaTemplateGenerator o in this.list)
        {
            if (string.IsNullOrEmpty(o.ID)) continue;
            if (!ID_Dictionary.TryAdd(o.ID, o)) Debug.Log($"failed to add Index_CharGenTemplates id [{o.ID}] due to duplicate");
        }

    }

    public void LateInitialize()
    {
        foreach(var i in this.list)
        {
            if (i.Template == null) continue;
            scr_System_Serializer.current.MasterList.CharacterTemplates.RegisterGeneratorTemplate(i.ID, i);
        }
    }

    public void GenerateNamesFor(Character_Trainable c, Humanoid_GenderAppearance gender,  NameCulture firstname, NameCulture middleName,  NameCulture lastname, string displayFormat = "")
    {
        var fst = firstname == NameCulture.none || !names.ContainsKey(firstname) ? null : names[firstname];
        //var mdl = middleName == NameCulture.none || !names.ContainsKey(middleName) ? null : names[middleName];
        var lst = lastname == NameCulture.none || !names.ContainsKey(lastname) ? null : names[lastname];

        if (fst != null)
        {
            var list_fst = gender == Humanoid_GenderAppearance.Female ? fst.firstname_female : fst.firstname_male;
            c.FirstName = Utility.GetRandomElement(list_fst);
        }
        if (lst != null)
        {
            var list_lst = gender == Humanoid_GenderAppearance.Female ? lst.lastname_female : lst.lastname_male;
            c.LastName = Utility.GetRandomElement(list_lst);
        }
        else
        {
            c.LastName = "";
        }


        if (displayFormat != "") c.nameDisplayFormat = displayFormat;

    }
}

[System.Serializable]
public class NameGenerator
{
    public List<string> firstname_male = new List<string>();
    public List<string> firstname_female = new List<string>();
    public List<string> lastname_male = new List<string>();
    public List<string> lastname_female = new List<string>();

    public void MergeWith(NameGenerator ng)
    {
        this.firstname_female.AddRange(ng.firstname_female);
        this.firstname_male.AddRange(ng.firstname_male);
        this.lastname_male.AddRange(ng.lastname_male);
        this.lastname_female.AddRange(ng.lastname_female);
    }
}

public class CharaTemplateGenerator
{
    public string ID = "";
    public string firstName = "", middleName = "", lastName = "";

    public int setHeight = 0, heightVariation = 0;
    public int setWeight = 0, weightVariation = 0;

    public bool useNameGen = false;
    public NameCulture nameGen_firstName = NameCulture.none, nameGen_middleName = NameCulture.none, nameGen_lastName = NameCulture.none;
    public string nameDisplayFormat = "";
    public string title = "";

    [JsonIgnore]
    public string TargetBaseID
    { get
        {
            if (targetBaseIDs.Count > 0) return Utility.GetRandomElement(targetBaseIDs);
            return targetBaseID;
        } }

    public string targetBaseID = "";
    public List<string> targetBaseIDs = new List<string>();
    public bool allowInTraining = true;

    public int str_base = 0, str_var = 0, con_base = 0, con_var = 0, psy_base = 0, psy_var = 0, wil_base = 0, wil_var = 0;
    public Humanoid_GenderAppearance Appearance = Humanoid_GenderAppearance.Female;
    public List<presetInventory> inventoryOverride = new List<presetInventory>();
    public List<string> basicExperienceOverride = new List<string>();
    public List<string> experienceOverride = new List<string>();

    CharaTemplate _template = null;

    [JsonIgnore]
    public CharaTemplate Template
    {
        get
        {
            if (targetBaseIDs.Count > 0)
            {
                return scr_System_Serializer.current.MasterList.CharacterTemplates.GetByCharaBaseID(Utility.GetRandomElement(targetBaseIDs));
            }
            if (_template == null) _template = scr_System_Serializer.current.MasterList.CharacterTemplates.GetByCharaBaseID(targetBaseID);
            return _template;
        }
    }

    [JsonIgnore]
    public CharaTemplate Get
    {
        get
        {
            if ((targetBaseID == "" && targetBaseIDs.Count < 1) || this.Template == null) return null;
            var template = Template.Copy();
            template.stat_STR = (int)Utility.RandVariation(str_base == 0 ? template.stat_STR : str_base, str_var);
            template.stat_CON = (int)Utility.RandVariation(con_base == 0 ? template.stat_CON : con_base, con_var);
            template.stat_PSY = (int)Utility.RandVariation(psy_base == 0 ? template.stat_PSY : psy_base, psy_var);
            template.stat_WIL = (int)Utility.RandVariation(wil_base == 0 ? template.stat_WIL : wil_base, wil_var);
            template.SetGender(Appearance);
            
            if (this.setHeight > 0) template.Height = this.setHeight;
            if (this.heightVariation > 0) template.Height = (int)Utility.RandVariation(template.Height, this.heightVariation);

            if (this.setWeight > 0) template.Weight = this.setWeight;
            if (this.weightVariation > 0) template.Weight = (int)Utility.RandVariation(template.Weight, this.weightVariation);

            if (this.inventoryOverride.Count > 0) template.initialInventory.AddRange(this.inventoryOverride);
            if (this.basicExperienceOverride.Count > 0)
            {
                Debug.Log($"setting basic experience override: {String.Join(" ", this.basicExperienceOverride)}");
                template.basicExperience = this.basicExperienceOverride;
            }
            if (this.experienceOverride.Count > 0)
            {
                template.initialExperiences.AddRange(this.experienceOverride);
                Debug.Log($"adding basic experience override: {String.Join(" ", this.experienceOverride)}");
            }
            return template;
        }
    }
}