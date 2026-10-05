using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.IO;
using System.Collections;


public enum NameCulture
{
    none,
    japanese,
    english,
    goblin,
    dog,
    horse
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

public interface I_CharaGen
{

}

public class CharaTemplateGenerator : I_CharaGen
{
    /// <summary>
    /// Applies only to base characters listed directly in targetBaseIDs (not inherited by child generators).
    /// false = a base is skipped while a live instance of it exists outside the stasis room.
    /// </summary>
    public bool allowDuplicateID = true;

    public string ID = "";
    public string firstName = "", middleName = "", lastName = "";

    public int setHeight = 0, heightVariation = 0;
    public int setWeight = 0, weightVariation = 0;

    public bool useNameGen = false;
    public NameCulture nameGen_firstName = NameCulture.none, nameGen_middleName = NameCulture.none, nameGen_lastName = NameCulture.none;
    public string nameDisplayFormat = "";
    public string title = "";

    public Character_Trainable GenerateChara()
    {
        if (childTemplates == null)
        {
            childTemplates = new List<I_CharaGen>();
            foreach(var ID in targetBaseIDs)
            {
                var temp = scr_System_Serializer.current.MasterList.Character_Bases.GetGeneratorByID(ID);
                if (temp != null)
                {
                    childTemplates.Add(temp);
                    continue;
                }
                var chara = scr_System_Serializer.current.index_Characters_Bases.GetChara(ID);
                if (chara != null)
                {
                    childTemplates.Add(chara);
                    continue;
                }
            }
        }

        Utility.ShuffleList(childTemplates);
        foreach(var entry in childTemplates)
        {
            if (entry is Character_Trainable)
            {
                var c = entry as Character_Trainable;
                if (c == null) continue;
                if (!allowDuplicateID && scr_System_CampaignManager.current.HasNonStasisInstanceWithBaseID(c.BaseID)) continue;
                return ApplyTemplate(c);
            }
            else if (entry is CharaTemplateGenerator)
            {
                var g = entry as CharaTemplateGenerator;
                var c = g.GenerateChara();
                if (c == null) continue;
                return ApplyTemplate(c);
            }
        }

        return null;
    }

    protected Character_Trainable ApplyTemplate(Character_Trainable original_template)
    {
        var str = JsonConvert.SerializeObject(original_template, UtilityEX.SerializerSettings);
        var template = JsonConvert.DeserializeObject<Character_Trainable>(str, UtilityEX.SerializerSettings);

        // template.BaseID = ID;
        if (title != "") template.Title = title;
        template.Template.overrideInventory = inventoryOverride;
        // Appearance left unset keeps whatever gender the picked base/child generator already has,
        // so a generator can mix male and female children without overwriting them.
        if (Appearance.HasValue) template.Template.SetGender(Appearance.Value);
        if (useNameGen)
        {
            scr_System_Serializer.current.MasterList.Character_Bases.GenerateNamesFor(template, template.Template.Appearance, nameGen_firstName, nameGen_middleName, nameGen_lastName, nameDisplayFormat);
        }
        template.Template.stat_STR = (int)Utility.RandVariation(str_base == 0 ? template.Template.stat_STR : str_base, str_var);
        template.Template.stat_CON = (int)Utility.RandVariation(con_base == 0 ? template.Template.stat_CON : con_base, con_var);
        template.Template.stat_PSY = (int)Utility.RandVariation(psy_base == 0 ? template.Template.stat_PSY : psy_base, psy_var);
        template.Template.stat_WIL = (int)Utility.RandVariation(wil_base == 0 ? template.Template.stat_WIL : wil_base, wil_var);

        if (setHeight > 0) template.Template.Height = setHeight;
        if (heightVariation > 0) template.Template.Height = (int)Utility.RandVariation(template.Template.Height, heightVariation);

        if (setWeight > 0) template.Template.Weight = setWeight;
        if (weightVariation > 0) template.Template.Weight = (int)Utility.RandVariation(template.Template.Weight, weightVariation);

        if (basicExperienceOverride.Count > 0)
        {
            Debug.Log($"setting basic experience override: {String.Join(" ", basicExperienceOverride)}");
            template.Template.basicExperience = basicExperienceOverride;
        }
        if (experienceOverride.Count > 0)
        {
            template.Template.initialExperiences.AddRange(experienceOverride);
            Debug.Log($"adding basic experience override: {String.Join(" ", experienceOverride)}");
        }

        template.baseTemplateID = this.ID;

        return template;
    }


    List<I_CharaGen> childTemplates = null;

    public List<string> targetBaseIDs = new List<string>();
    public bool allowInTraining = true;

    public int str_base = 0, str_var = 0, con_base = 0, con_var = 0, psy_base = 0, psy_var = 0, wil_base = 0, wil_var = 0;
    /// <summary>Forced gender for generated characters; null (omitted in JSON) keeps the base/child's own.</summary>
    public Humanoid_GenderAppearance? Appearance = null;
    public List<presetInventory> inventoryOverride = new List<presetInventory>();
    public List<string> basicExperienceOverride = new List<string>();
    public List<string> experienceOverride = new List<string>();

}