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

    /// <summary>Authored template every "use [item]" folder is copied from (COM_ItemFolder).</summary>
    public const string ItemFolderTemplateID = "com_useItem";

    /// <summary>
    /// Every command naming an item in COM.itemFolder goes under that item's folder (template ID + "_" + item ID),
    /// created from the template on first use; the NotifyChild pass after this fills the folders' childCOMs.
    /// </summary>
    void BuildItemFolders()
    {
        var members = new List<COM>();
        foreach (var com in ID_Dictionary.Values)
            if (!string.IsNullOrEmpty(com.itemFolder)) members.Add(com);
        if (members.Count == 0) return;

        var template = GetByID(ItemFolderTemplateID) as COM_ItemFolder;
        if (template == null)
        {
            Debug.LogError($"Index_COM: item folder template [{ItemFolderTemplateID}] missing, {members.Count} commands stay without folder");
            return;
        }
        var serializedTemplate = JsonConvert.SerializeObject(template, UtilityEX.SerializerSettings);

        for (int i = 0; i < members.Count; i++)
        {
            var com = members[i];
            var item = Masterlist_Items.GetByID(com.itemFolder);
            if (item == null)
            {
                // e.g. the item was removed with NSFW content off while its commands stayed - nothing to use
                Debug.Log($"Index_COM: {com.ID} itemFolder [{com.itemFolder}] is not an item, hiding the command");
                com.hidden = true;
                continue;
            }
            var folderID = ItemFolderTemplateID + "_" + item.ID;
            var folder = GetByID(folderID);
            if (folder == null)
            {
                var newFolder = JsonConvert.DeserializeObject<COM_ItemFolder>(serializedTemplate, UtilityEX.SerializerSettings);
                newFolder.InitializeFolder(item);
                newFolder.OnAfterDeserialize();
                list_generated.Add(newFolder);
                ID_Dictionary.Add(newFolder.ID, newFolder);
                folder = newFolder;
            }
            com.ParentCOM = folder;
            com.isHiddenChild = true;
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

                        if (!newCOM1.isValid)
                        {
                            // Not an error: this item just doesn't qualify for this particular generator
                            // (e.g. an oral-only consumable failing COM_IngestItem's anus/vagina route
                            // check) - itemTag matching is necessary but not sufficient for validity.
                        }
                        else if (newCOMs.Find(x => x.ID == newCOM1.ID) != null)
                        {
                            Debug.LogError($"already contain mealcom with id {newCOM1.ID}");
                        }
                        else
                        {
                            newCOMs.Add(newCOM1);
                        }
                    }
                }
            }
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


            // upgrades run only inside a sex scene (Job_Sex_Group), so they get no _noSex clone
            if (list[i].comTags.Contains("sex") && !list[i].IsUpgrade && (list[i].comTags.Contains("service") || list[i].comTags.Contains("nosexvariant")))
            {
                var serializedParent = JsonConvert.SerializeObject(list[i], UtilityEX.SerializerSettings);
                var newCOM = JsonConvert.DeserializeObject<COM_Sex>(serializedParent, UtilityEX.SerializerSettings);
                newCOM.comTags.Remove("sex");
                // losing "sex" would drop the clone to Medium intimacy (permissions.json rules); a sexual act stays High outside a scene
                if (!newCOM.comTags.Contains("permission_intimacy_low") && !newCOM.comTags.Contains("permission_intimacy_medium") && !newCOM.comTags.Contains("permission_intimacy_high"))
                    newCOM.comTags.Add("permission_intimacy_high");
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

        BuildItemFolders();

        foreach (var com in ID_Dictionary.Values)
        {
            if (com.ParentCOM != null) com.ParentCOM.NotifyChild(com);
            if (com.IsUpgrade)
            {
                var upgradeParent = com.UpgradeParent;
                if (upgradeParent != null) upgradeParent.NotifyUpgrade(com);
                else Debug.LogError($"Index_COM: {com.ID} upgradeFrom [{com.upgradeFrom}] is not a command");
            }
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