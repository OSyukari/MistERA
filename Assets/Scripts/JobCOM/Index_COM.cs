using Newtonsoft.Json;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Jobs;
using UnityEngine;



[System.Serializable]
public class Index_COM : I_IndexHasID, I_SerializationCallbackReceiver, I_NeedLateInitialize, I_IndexMergeable, I_RemoveElemByTag, I_RemoveNSFW
{
    [JsonProperty] protected List<COM> list = new List<COM>();
    protected List<COM> list_generated = new List<COM>();
    [JsonIgnore] public List<COM> LIST { get { return ID_Dictionary.Values.ToList(); } }
    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Index_COM;
        if (l == null) return;
        else if (l.list == null) return;
        else
        {
            this.list.AddRange(l.list);
        }
    }

    public void OnAfterDeserialize()
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            if (list[i].comTags.Contains("do_not_use"))
            {
                list.RemoveAt(i);
                continue;
            }

            else if (list[i].comTags.Contains("sex") && !(list[i] is COM_Sex))
            {
                var serializedParent = JsonConvert.SerializeObject(list[i], UtilityEX.SerializerSettings);
                list[i] = JsonConvert.DeserializeObject<COM_Sex>(serializedParent, UtilityEX.SerializerSettings);
            }
            else if (list[i] is COM_Character_Insert || list[i] is COM_Character_Remove
                //  || list[i] is COM_TakeMeal
                // || list[i] is COM_FarmRecipe 
                || list[i].ID.Contains("_noSex", StringComparison.InvariantCultureIgnoreCase))
            {
                list.RemoveAt(i);
                continue;
            }

            list[i].OnAfterDeserialize();
        }
    }

    public COM GetByID(string ID)
    {

        var returnVal = ID_Dictionary.ContainsKey(ID) ? ID_Dictionary[ID] : null;
        if (returnVal != null && returnVal.comTags.Contains("food_meal") && !(returnVal is COM_TakeMeal))
        {
            Debug.LogError($"getting mealcom {returnVal.ID} ismeal? {returnVal is COM_TakeMeal}");
        }
        return returnVal;
    }

    Dictionary<string, COM> ID_Dictionary = new Dictionary<string, COM>();
    public void RegisterAllID(List<string> messages)
    {

        messages.Add("Index_COM : registering ID with list length [" + list.Count + "]");
        foreach (COM s in list)
        {
            if (string.IsNullOrEmpty(s.ID)) continue;
            if (!ID_Dictionary.TryAdd(s.ID, s)) Debug.Log($"failed to add Index_COM id [{s.ID}] due to duplicate");
        }

    }

    public void LateInitialize()
    {
        List<COM> newCOMs = new List<COM>();
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var com = list[i];

            if (com.GenerateCOM != null && com.GenerateCOM.itemTag != "" && com.GenerateCOM.targetCOMClass != null)
            {
                var type = com.GenerateCOM.targetCOMClass.GetType();
                var serializedParent = JsonConvert.SerializeObject(com.GenerateCOM.targetCOMClass, UtilityEX.SerializerSettings);
                bool hideChild = com.GenerateCOM.hideChild;
                foreach (Item_Base item in Masterlist_Items.Instance.Index.List)
                {
                    if (item.Tags.Contains(com.GenerateCOM.itemTag))
                    {
                        COM newCOM1 = JsonConvert.DeserializeObject(serializedParent.Replace("$itemID$", item.ID), type, UtilityEX.SerializerSettings) as COM;
                        newCOM1.InitializeChildCOM(com, item);

                        if (hideChild) newCOM1.isHiddenChild = true;

                        if (newCOM1.isValid && newCOMs.Find(x => x.ID == newCOM1.ID) == null) newCOMs.Add(newCOM1);
                        else Debug.LogError($"already contain mealcom with id {newCOM1.ID}");
                    }
                }
            }
            /*if (com.ID == "com_furniture_getmeal")
            {
                foreach (Item_Base item in Masterlist_Items.Instance.Index.List)
                {
                    if (item.Tags.Contains("food_meal"))
                    {
                        var serializedParent = JsonConvert.SerializeObject(com, UtilityEX.SerializerSettings);
                        COM_TakeMeal newCOM1 = JsonConvert.DeserializeObject<COM_TakeMeal>(serializedParent, UtilityEX.SerializerSettings);
                        newCOM1.Initialize(com, item);

                        if (newCOMs.Find(x => x.ID == newCOM1.ID) == null) newCOMs.Add(newCOM1);
                        else Debug.LogError($"already contain mealcom with id {newCOM1.ID}");
                    }
                }
            }*/
            else if (com.requirements.requireContaining != null && com.requirements.requireContaining.allowPlanting != null)
            {
                // make restraint furniture stuff
                if (com.requirements.requireContaining.allowPlanting.Contains("character_trainable"))
                {

                    var serializedParent = JsonConvert.SerializeObject(list[i], UtilityEX.SerializerSettings);
                    COM_Character_Insert newCOM1 = JsonConvert.DeserializeObject<COM_Character_Insert>(serializedParent, UtilityEX.SerializerSettings);
                    COM_Character_Remove newCOM2 = JsonConvert.DeserializeObject<COM_Character_Remove>(serializedParent, UtilityEX.SerializerSettings);
                    // COM_FarmRecipe rcp = new COM_FarmRecipe(list[i], recipe);
                    newCOM1.Initialize();
                    newCOM2.Initialize();
                    //newCOM3.Initialize();


                    if (newCOMs.Find(x => x.ID == newCOM1.ID) == null) newCOMs.Add(newCOM1);
                    if (newCOMs.Find(x => x.ID == newCOM2.ID) == null) newCOMs.Add(newCOM2);
                    //if (newCOMs.Find(x => x.ID == newCOM3.ID) == null) newCOMs.Add(newCOM3);
                }
                /*
                else if (list[i].requirements.requireContaining.allowPlanting.Count == 1 && list[i].requirements.requireContaining.allowPlanting[0] == "")
                {   // this is a special case just to handle/initialize com_job_farm_remove

                    //Debug.LogError($"FarmRecipe fallthrough on {list[i].ID}, allow planting 1 and nullID");
                    // Debug.Log("initializing remove plant com [" + list[i].ID + "]");
                    var serializedParent = JsonConvert.SerializeObject(list[i], UtilityEX.SerializerSettings);
                    COM_FarmRecipe newCOM = JsonConvert.DeserializeObject<COM_FarmRecipe>(serializedParent, UtilityEX.SerializerSettings);
                    newCOM.InitializeRecipe(null);
                    list[i] = newCOM;
                    // COM_FarmRecipe rcp = new COM_FarmRecipe(list[i], recipe);
                }
                else
                { // make farm recipe stuff
                    Debug.LogError($"FarmRecipe fallthrough on {list[i].ID}");
                    
                    foreach (var recipe in Masterlist_Items.Instance.FarmRecipe)
                    {
                        if (list[i].requirements.requireContaining.allowPlanting.Contains(recipe.growType))
                        {
                            // make new variant with recipe
                            var serializedParent = JsonConvert.SerializeObject(list[i], UtilityEX.SerializerSettings);
                            COM_FarmRecipe newCOM = JsonConvert.DeserializeObject<COM_FarmRecipe>(serializedParent, UtilityEX.SerializerSettings);
                            newCOM.InitializeRecipe(recipe);

                            // COM_FarmRecipe rcp = new COM_FarmRecipe(list[i], recipe);

                            if (newCOMs.Find(x => x.ID == newCOM.ID) == null) newCOMs.Add(newCOM);
                        }
                    }
                }*/
            }
            if (list[i].comTags.Contains("service") || list[i].comTags.Contains("sex") || (list[i].comTags.Contains("touch") && !list[i].comTags.Contains("safe")))
            {
                list[i].comTags.Add("unsafe");
            }


            if (list[i].comTags.Contains("sex") && (list[i].comTags.Contains("service") || list[i].comTags.Contains("nosexvariant")))
            {
                var serializedParent = JsonConvert.SerializeObject(list[i], UtilityEX.SerializerSettings);
                var newCOM = JsonConvert.DeserializeObject<COM_Sex>(serializedParent, UtilityEX.SerializerSettings);
                newCOM.comTags.Remove("sex");
                newCOM.requirements.requirement.req_Receivers.requireAbsentJobwithCOMTag.Add("sex");
                newCOM.ID += "_noSex";
                newCOM.AppendParentID("_noSex");
                newCOMs.Add(newCOM);
            }
        }

        List<string> generatedCOMs = new List<string>();
        List<string> conflictCOMs = new List<string>();

        foreach (var i in newCOMs)
        {
            if (ID_Dictionary.ContainsKey(i.ID))
            {
                conflictCOMs.Add(i.ID);
            }
            else
            {
                generatedCOMs.Add(i.ID);
                i.OnAfterDeserialize();
                list_generated.Add(i);
                ID_Dictionary.Add(i.ID, i);
            }
        }

        Debug.Log($"COM late initialize Generated RecipeCOMs count {generatedCOMs.Count}, conflict count {conflictCOMs.Count}\n{String.Join(" | ", generatedCOMs)}\n{String.Join(" | ", conflictCOMs)}");
        //list.AddRange(newCOMs);

        foreach (var com in ID_Dictionary.Values)
        {
            if (com.ParentCOM != null) com.ParentCOM.NotifyChild(com);
        }

        /*
        foreach (var i in list)
        {
            if (i.comTags.Contains("sex")) continue;
            if (i.comTags.Contains("interaction") && i.comTags.Contains("canbeignored")) continue;
            if (i.comTags.Contains("initSex") || i.comTags.Contains("endSex")) continue;
            if (!i.requirements.requirement.req_Receivers.requireAbsentJobwithCOMTag.Contains("sex")) i.requirements.requirement.req_Receivers.requireAbsentJobwithCOMTag.Add("sex");
        }*/
    }

    public List<COM> GetByTags(List<string> tags)
    {
        List<COM> coms = LIST.FindAll(x => Utility.ListContainsStrict(x.comTags, tags));
        List<string> names = new List<string>();

        foreach (var co in coms)
        {
            names.Add(co.ID);
        }
        //Debug.Log($"com getbytags {String.Join("|", tags)} return {String.Join("|", names)}");
        return coms;
    }

    public void RemoveElemByTag(string tag)
    {
        this.list.RemoveAll(x => x.comTags.Contains(tag));
    }

    public void RemoveNSFW()
    {
        for (int i = list.Count - 1; i >= 0; i--)
        {
            var c = list[i];
            if (c.comTags.Contains("touch"))
            {
                if (!c.comTags.Contains("safe")) list.RemoveAt(i);
                else c.HideWhenInvalid = true;
            }
        }

    }
}