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

    /// <summary>
    /// One generated command (Docs/Interactions/Plan_GeneratedCommands.md §4.4): the template json read as type, linked to
    /// source for text and kojo. The caller runs the kind's own initialization (item child, ...) and registers it.
    /// </summary>
    static COM Generate(COM source, string templateJson, Type type, bool ownText, bool ownKojo)
    {
        var generated = JsonConvert.DeserializeObject(templateJson, type, UtilityEX.SerializerSettings) as COM;
        generated.SetGeneratedFrom(source, ownText, ownKojo);
        return generated;
    }

    /// <summary>
    /// The GenerateForChildren generators of declaring for source - source is declaring itself (self = true, only
    /// includeSelf generators) or one of its children. A child of the declaring folder keeps the generated folder as its
    /// parent (`parent + idSuffix`, generated from the folder by includeSelf).
    /// </summary>
    static void GenerateDeclared(COM declaring, COM source, List<COM> into, bool self)
    {
        var defs = declaring.GenerateForChildren;
        if (defs == null) return;
        for (int d = 0; d < defs.Count; d++)
        {
            var def = defs[d];
            if (def == null || string.IsNullOrEmpty(def.idSuffix)) continue;
            if (self && !def.includeSelf) continue;
            if (def.skipUpgrades && source.IsUpgrade) continue;

            var generated = Generate(source, JsonConvert.SerializeObject(source, UtilityEX.SerializerSettings), source.GetType(), def.useOwnText, def.useOwnKojo);
            generated.ID = source.ID + def.idSuffix;
            if (!self && def.includeSelf) generated.AppendParentID(def.idSuffix);
            Transform(def.transform, generated);
            into.Add(generated);
        }
    }

    /// <summary>
    /// The GenerateForFurniture generators of declaring (Plan_GeneratedCommands.md §4.6): a copy of declaring with the
    /// def's template laid over it, read as comClass and set up by its InitializeGenerated - first once without furniture
    /// to learn its tags, then for every furniture whose job givers offer it (giver comID names declaring, or all giver
    /// comTags are on the command - MapUtility.GetFurnitureCOMs). ID `declaring + idSuffix + "_" + furnitureID`.
    /// </summary>
    static void GenerateForFurnitureDeclared(COM declaring, List<COM> into)
    {
        var defs = declaring.GenerateForFurniture;
        if (defs == null) return;
        var furnitures = scr_System_Serializer.current.MasterList.Furnitures.list;
        for (int d = 0; d < defs.Count; d++)
        {
            var def = defs[d];
            if (def == null) continue;
            // idSuffix may be "" (the declaring command is the action itself: com_furniture_cryosleep_ADD → _ADD_<furniture>)
            if (def.idSuffix == null) def.idSuffix = "";
            var type = string.IsNullOrEmpty(def.comClass) ? declaring.GetType() : Type.GetType(def.comClass);
            if (type == null || !typeof(COM).IsAssignableFrom(type))
            {
                Debug.LogError($"Index_COM: {declaring.ID} GenerateForFurniture [{def.idSuffix}] comClass [{def.comClass}] is not a command class");
                continue;
            }
            var root = Newtonsoft.Json.Linq.JObject.Parse(JsonConvert.SerializeObject(declaring, UtilityEX.SerializerSettings));
            root.Remove("$type");
            if (def.template != null) MergeTemplate(root, def.template);
            var json = root.ToString(Formatting.None);

            var probe = MakeFurnitureChild(declaring, def, type, json, "", d);
            string first = null;
            for (int f = 0; f < furnitures.Count; f++)
            {
                var furniture = furnitures[f];
                if (furniture == null || string.IsNullOrEmpty(furniture.ID) || !Offers(furniture, declaring, probe)) continue;
                var generated = MakeFurnitureChild(declaring, def, type, json, furniture.ID, d);
                if (into.Find(x => x.ID == generated.ID) != null) continue;
                into.Add(generated);
                if (first == null) first = generated.ID;
            }
            if (first == null) { Debug.Log($"Index_COM: {declaring.ID} GenerateForFurniture [{def.idSuffix}]: no furniture offers it"); continue; }
            if (def.legacyIDs != null)
                for (int l = 0; l < def.legacyIDs.Count; l++) COM_LegacyIDs.Register(def.legacyIDs[l], declaring.ID + def.idSuffix + "_", first);
        }
    }

    static COM MakeFurnitureChild(COM declaring, COM.GenerateForFurnitureDef def, Type type, string json, string furnitureID, int defIndex)
    {
        var generated = Generate(declaring, json.Replace("$furnitureID$", furnitureID), type, def.useOwnText, def.useOwnKojo);
        generated.ID = declaring.ID + def.idSuffix + (furnitureID == "" ? "" : "_" + furnitureID);
        generated.targetFurnitureID = furnitureID;
        generated.generatorKey = declaring.ID + def.idSuffix + "@furniture";
        generated.generatorTemplatePath = $"GenerateForFurniture[{defIndex}].template";
        generated.InitializeGenerated();
        return generated;
    }

    /// <summary> Whether a furniture's job givers offer the command (same test as MapUtility.GetFurnitureCOMs: comID, or all tags). </summary>
    static bool Offers(FurnitureBase furniture, COM declaring, COM command)
    {
        if (furniture.givesJob == null) return false;
        for (int g = 0; g < furniture.givesJob.Count; g++)
        {
            var giver = furniture.givesJob[g];
            if (giver == null) continue;
            if (giver.comID != null && giver.comID.Contains(declaring.ID)) return true;
            if (giver.comTags != null && giver.comTags.Count > 0 && Utility.ListContainsStrict(command.comTags, giver.comTags)) return true;
        }
        return false;
    }

    /// <summary>
    /// A generator template over a copy of the declaring command: "variants" merge element by element (the template's
    /// fields win, nested lists are replaced), every other field is replaced as a whole (so text blocks never mix).
    /// </summary>
    static void MergeTemplate(Newtonsoft.Json.Linq.JObject target, Newtonsoft.Json.Linq.JObject template)
    {
        foreach (var prop in template.Properties())
        {
            if (prop.Name == "variants" && prop.Value is Newtonsoft.Json.Linq.JArray tv && target["variants"] is Newtonsoft.Json.Linq.JArray bv)
            {
                for (int v = 0; v < tv.Count; v++)
                {
                    if (v >= bv.Count) { bv.Add(tv[v].DeepClone()); continue; }
                    if (bv[v] is Newtonsoft.Json.Linq.JObject bo && tv[v] is Newtonsoft.Json.Linq.JObject to)
                        bo.Merge(to, new Newtonsoft.Json.Linq.JsonMergeSettings { MergeArrayHandling = Newtonsoft.Json.Linq.MergeArrayHandling.Replace });
                    else bv[v] = tv[v].DeepClone();
                }
                continue;
            }
            target[prop.Name] = prop.Value.DeepClone();
        }
    }

    /// <summary> Kind-specific edits a declared generator names (GenerateForChildrenDef.transform). </summary>
    static void Transform(string transform, COM generated)
    {
        switch (transform)
        {
            case "":
            case null:
                return;
            case "noSex":
                // offered outside a sex scene: not a sex act, and the receiver must not be in one
                generated.comTags.Remove("sex");
                // losing "sex" would drop the clone to Medium intimacy (permissions.json rules); a sexual act stays High outside a scene
                if (!generated.comTags.Contains("permission_intimacy_low") && !generated.comTags.Contains("permission_intimacy_medium") && !generated.comTags.Contains("permission_intimacy_high"))
                    generated.comTags.Add("permission_intimacy_high");
                generated.requirements.requirement.req_Receivers.requireAbsentJobwithCOMTag.Add("sex");
                return;
            default:
                Debug.LogError($"Index_COM: unknown generator transform [{transform}] on {generated.ID}");
                return;
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
                        var itemJson = serializedParent;
                        // this item's own text etc. laid over the template (Docs/TextAuthoring/Plan_ItemVariantText.md)
                        if (com.GenerateCOM.itemOverrides != null && com.GenerateCOM.itemOverrides.TryGetValue(item.ID, out var itemOverride) && itemOverride != null)
                        {
                            var root = Newtonsoft.Json.Linq.JObject.Parse(serializedParent);
                            MergeTemplate(root, itemOverride);
                            itemJson = root.ToString(Formatting.None);
                        }
                        COM newCOM1 = Generate(com, itemJson.Replace("$itemID$", item.ID), type, com.GenerateCOM.useOwnText, com.GenerateCOM.useOwnKojo);
                        newCOM1.generatorKey = com.ID + "@item";
                        newCOM1.generatorTemplatePath = "GenerateCOM.targetCOMClass";
                        newCOM1.generatorItemID = item.ID;
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
                // restraint furniture (character_trainable): declared with GenerateForFurniture on the template command
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
            // one generated command per furniture that offers it - e.g. the restraint add / remove commands
            GenerateForFurnitureDeclared(com, newCOMs);

            if (list[i].comTags.Contains("service") || list[i].comTags.Contains("sex") || (list[i].comTags.Contains("touch") && !list[i].comTags.Contains("safe")))
            {
                list[i].comTags.Add("unsafe");
            }


            // generators declared on this command (includeSelf) and on its folder parent (every child) - e.g. the noSex
            // copies of the service acts, declared on com_sex_service_base
            GenerateDeclared(list[i], list[i], newCOMs, true);
            var folder = list[i].ParentCOM;
            if (folder != null && folder != list[i]) GenerateDeclared(folder, list[i], newCOMs, false);
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