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
    public List<COM> list = new List<COM>();
    [JsonIgnore] public List<COM> LIST { get { return ID_Dictionary.Values.ToList(); } }
    public void MergeWith(I_IndexMergeable list) {
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
            if (list[i].comTags.Contains("do_not_use")) {
                list.RemoveAt(i);
                continue;
            }
            
            else if (list[i].comTags.Contains("sex") && !(list[i] is COM_Sex))
            {
                var serializedParent = JsonConvert.SerializeObject(list[i], UtilityEX.SerializerSettings);
                list[i] = JsonConvert.DeserializeObject<COM_Sex>(serializedParent, UtilityEX.SerializerSettings);
            }
            else if ( list[i] is COM_Character_Insert || list[i] is COM_Character_Remove 
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

    public COM GetByID(string ID) { 
        
        var returnVal = ID_Dictionary.ContainsKey(ID) ? ID_Dictionary[ID] : null;
        if ( returnVal != null && returnVal.comTags.Contains("food_meal") && !(returnVal is COM_TakeMeal))
        {
            Debug.LogError($"getting mealcom {returnVal.ID} ismeal? {returnVal is COM_TakeMeal}");
        }
        return returnVal; }

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

    public void AppendList(Index_COM list)
    {
        this.list.AddRange(list.list);
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
                list.Add(i);
                ID_Dictionary.Add(i.ID, i);
            }
        }

        Debug.Log($"COM late initialize Generated RecipeCOMs count {generatedCOMs.Count}, conflict count {conflictCOMs.Count}\n{String.Join(" | ",generatedCOMs)}\n{String.Join(" | ", conflictCOMs)}");
        //list.AddRange(newCOMs);

        foreach(var com in list)
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

        foreach(var co in coms)
        {
            names.Add(co.ID);
        }
        //Debug.Log($"com getbytags {String.Join("|", tags)} return {String.Join("|", names)}");
        return coms;
    }

    public void RemoveElemByTag(string tag)
    {
        this.list.RemoveAll(x=>x.comTags.Contains(tag));
    }

    public void RemoveNSFW()
    {
        for(int i = list.Count - 1; i >= 0; i--)
        {
            var c = list[i];
            if (c.comTags.Contains("touch"))
            {
                 if( !c.comTags.Contains("safe"))  list.RemoveAt(i);
                 else c.HideWhenInvalid = true;
            }
        }

    }
}

[System.Serializable]
public class COM: I_SerializationCallbackReceiver, hasCategory
{

    /// <summary>
    /// Called on GenerateCOM  loadBaseItem:TRUE
    /// </summary>
    /// <param name="baseCOM"></param>
    /// <param name="item"></param>
    public virtual void InitializeChildCOM(COM baseCOM, Item_Base item)
    {
        // ignore if not child com

        ParentCOM = baseCOM;
    }
    /// <summary>
    /// Called on GenerateCOM  loadBaseItem:FALSE
    /// </summary>
    /// <param name="baseCOM"></param>
    /// <param name="item"></param>
    public virtual void InitializeChildCOM(COM baseCOM, Item_Instance item)
    {
        // ignore if not child com

        ParentCOM = baseCOM;
    }


    [JsonIgnore]
    public List<COM> childCOMs = new List<COM>();
    public void NotifyChild(COM child)
    {
        if (child == null) return;
        childCOMs.Add(child);
        childCOMs = Utility.Distinct(childCOMs);
    }

    public bool hidden = false;

    [JsonIgnore] public bool isHiddenChild = false;
    [JsonIgnore] public bool isHiddenParent
    {
        get
        {
            return this.childCOMs.Count > 0 && (this.GenerateCOM == null || !this.GenerateCOM.hideChild);
        }
    }
    [JsonIgnore]
    public COM ParentCOM
    {
        get
        {
            if (_parentCOM == null && parentCOMID != "")
            {
                _parentCOM = scr_System_Serializer.current.MasterList.COMs.GetByID(parentCOMID);
            }
            return _parentCOM;
        }
        set
        {
            _parentCOM = value;
            parentCOMID = value == null ? "" : value.ID;
        }
    }
    COM _parentCOM = null;
    [JsonProperty] protected string parentCOMID = "";
    /// <summary>
    /// will skip if parentcomID is empty
    /// </summary>
    /// <param name="s"></param>
    public void AppendParentID(string s)
    {
        if (parentCOMID == "" || parentCOMID.Length < 1) return;
        parentCOMID += s;
        _parentCOM = null;
    }

    [JsonIgnore]
    public COM ParentCOM_includeSelf
    {
        get
        {
            if (ParentCOM == null) return this;
            else return ParentCOM;
        }
    }


    public List<string> categoryTags = new List<string>();
    public class Acceptance
    {
        public int baseAcceptanceValue = 0;
        public bool useDefault = true;
        public List<string> SkillBonus_Doer = new List<string>();
        public List<string> SkillBonus_Receiver = new List<string>();
    }
    public class Difficulty
    {
        public int baseD20Check = 0;
        public bool useDefault = true;

        public List<string> SkillBonus_Doer = new List<string>();
        public List<string> SkillBonus_Receiver = new List<string>();

        /// <summary>
        /// Moodlet score on DC success
        /// </summary>
        public int moodMod = 0;
        public int stressMod = 0;
        public int lustMod = 0;
    }

    public bool COMRepeat = false;
    public bool HideWhenInvalid = false;
    public string ID = "";
    [JsonIgnore] public virtual string tooltipID { get
        {
            return ID;
        } }

    public GenerateCOMWithItemTag GenerateCOM = null;

    public class GenerateCOMWithItemTag
    {
        public string itemTag = "";
        public COM targetCOMClass = null;
        public bool hideChild = true;
    }

    public GenerateAPWithTemplate GenerateAP = null;
    public class GenerateAPWithTemplate
    {
        public string itemTag = "";
        public ActionPackage targetAPClass = null;
        public bool hideChild = true;
    }

    public string tooltip = "";
    public string displayName = "";
    public bool VariantDoNotReadRequirement = false;

    public bool ExitJobOnExecution = false;

    public List<COM_Variant> variants = new List<COM_Variant>();
    //public List<string> conflictCOMIDs = new List<string>();
    [JsonIgnore] public bool isInteraction { get { return comTags.Contains("interaction") || comTags.Contains("sex"); } }
    [JsonIgnore] public bool isSpecialInteraction { get
        {
            return comTags.Contains("initSex") || comTags.Contains("endSex");
        }
    }
    [JsonIgnore]
    public bool requiresPrivacy
    {
        get
        {
            return comTags.Contains("privacy");
        }
    }

    public Acceptance AcceptanceCheck = new Acceptance();
    public Difficulty DifficultyCheck = new Difficulty();
    [JsonIgnore] public int baseD20Check { get { return DifficultyCheck.baseD20Check; } }
    [JsonIgnore] public int baseAcceptanceValue { get { return AcceptanceCheck.baseAcceptanceValue; } }

    //public int moodModValue = 0, stressModValue = 0, lustModValue = 0;

    public List<string> comTags = new List<string>();
    public List<string> conflictTags = new List<string>();

    [JsonIgnore] public bool isSexCOM { get { return comTags.Contains("sex"); } }
    [JsonIgnore] public bool isUnsafe { get { return comTags.Contains("unsafe"); } }
    [JsonIgnore] public bool isTouchCOM { get { return !isSexCOM && comTags.Contains("touch"); } }

    [JsonProperty] protected int timeScale = 0;
    [JsonIgnore] public int TimeScale { get { return timeScale; } }

    //public List<DerivedStatMod> doerStatMod = new List<DerivedStatMod>(), receiverStatMod = new List<DerivedStatMod>();

    public COM_Requirements requirements = new COM_Requirements();

    public COM_Descriptions description_begin = new COM_Descriptions();
    public COM_Descriptions description_remove = new COM_Descriptions();
    public COM_Descriptions description_ongoing = new COM_Descriptions();
    public COM_Descriptions description_after = new COM_Descriptions();
    public COM_Descriptions description_onComplete = new COM_Descriptions();

    [JsonIgnore]
    public int MaxActorCount
    {
        get
        {
            var i = Math.Max(0, this.requirements.requirement.doerCount);
            var j = Math.Max(0, this.requirements.requirement.receiverCount);
            foreach(var v in this.variants)
            {
                i = Math.Max(i, v.requirements.requirement.doerCount);
                j = Math.Max(j, v.requirements.requirement.receiverCount);
            }

            return i + j;
        }
    }

    [JsonIgnore]
    public bool AllowMaxActorMod
    {
        get
        {
            var i = Math.Max(0, this.requirements.requirement.doerCount);
            var j = Math.Max(0, this.requirements.requirement.receiverCount);
            foreach (var v in this.variants)
            {
                i = Math.Max(i, v.requirements.requirement.doerCount);
                j = Math.Max(j, v.requirements.requirement.receiverCount);
            }

            return i > 1 || j > 1;
        }
    }
    public virtual string GetDescription_Begin(EvaluationPackage evp, int variantID)
    {
        //Debug.LogError("GetDescription_Begin with variantID " + variantID);
        if (variantID == -1) return description_begin.GetText(ref evp);
        else if (variantID >= variants.Count) return "GetDescription_Begin ERROR variantID out of bound";
        else return variants[variantID].GetDescription_Begin(this, evp);
    }
    public virtual string GetDescription_Begin(ActionPackage ap, int variantID)
    {
        //Debug.LogError("GetDescription_Begin with variantID " + variantID);
        if (variantID == -1) return description_begin.GetText(ap);
        else if (variantID >= variants.Count) return "GetDescription_Begin ERROR variantID out of bound";
        else return variants[variantID].GetDescription_Begin(this, ap);
    }

    public virtual string GetDescription_Remove(EvaluationPackage evp, int variantID)
    {
        if (variantID == -1) return description_remove.GetText(ref evp);
        else if (variantID >= variants.Count) return "GetDescription_Remove ERROR variantID out of bound";
        else return variants[variantID].GetDescription_Remove(this, evp);
    }

    public virtual string GetDescription_Ongoing(EvaluationPackage evp, int variantID)
    {
        if (variantID == -1) return description_ongoing.GetText(ref evp);
        else if (variantID >= variants.Count) return "GetDescription_Ongoing ERROR variantID out of bound";
        else return variants[variantID].GetDescription_Ongoing(this, evp);
    }
    public virtual string GetDescription_Ongoing(ActionPackage ap, int variantID)
    {
        if (variantID == -1) return description_ongoing.GetText(ap);
        else if (variantID >= variants.Count) return "GetDescription_Ongoing ERROR variantID out of bound";
        else return variants[variantID].GetDescription_Ongoing(this, ap);
    }
    public virtual string GetDescription_After(EvaluationPackage evp, int variantID)
    {
        if (variantID == -1) return description_after.GetText(ref evp);
        else if (variantID >= variants.Count) return "GetDescription_After ERROR variantID out of bound";
        else return variants[variantID].GetDescription_After(this, evp);
    }

    public virtual string GetDescription_After(ActionPackage ap, int variantID)
    {
        if (variantID == -1) return description_after.GetText(ap);
        else if (variantID >= variants.Count) return "GetDescription_After ERROR variantID out of bound";
        else return variants[variantID].GetDescription_After(this, ap);
    }

    public virtual string GetDescription_OnComplete(EvaluationPackage evp, int variantID)
    {
        if (variantID == -1) return description_onComplete.GetText(ref evp);
        else if (variantID >= variants.Count) return "GetDescription_OnComplete ERROR variantID out of bound";
        else return variants[variantID].GetDescription_OnComplete(this, evp);
    }

    public virtual string GetDescription_OnComplete(ActionPackage ap, int variantID)
    {
        if (variantID == -1) return description_onComplete.GetText(ap);
        else if (variantID >= variants.Count) return "GetDescription_OnComplete ERROR variantID out of bound";
        else return variants[variantID].GetDescription_OnComplete(this, ap);
    }

    //public Validator_Costs costs;
    public COM_Results results_immediate = new COM_Results();

    //public Validator_Costs costs;
    public COM_Results results = new COM_Results();

    /// <summary>
    /// Only provide msg if this is visible
    /// </summary>
    /// <param name="m"></param>
    /// <param name="msg"></param>
    public void ApplyCost(EvaluationPackage m, MessageCollect msg)
    {
        //Debug.Log("VariantIDs: AP["+p.COMVariantID+"] EP["+m.VariantID+"] param["+variantID+"]");

        if (m.VariantID >= 0) this.variants[m.VariantID].ApplyCost(this, m, msg);
        //else
        //{
        //Debug.LogError("COM " + displayName + " apply cost error, both variantID < 0");
        //int validVar = GetValidVariant(m.Doer, m.Receiver);
        //if (validVar >= 0) this.variants[validVar].ApplyCost(this, m, msg);
        // 
        // }

    }
    public void ApplyResultsImmediate(Job job, ActionPackage p, EvaluationPackage evp, Memory_Attitude att, Character_Trainable target, ExperienceLog log)
    {
        //Debug.Log("ApplyResults doer[" + (evp.Doer != null ? evp.Doer.FirstName : "-") + "] receiver[" + (evp.Receiver != null ? evp.Receiver.FirstName : "-") + "]");
        results_immediate.ApplyResults(job, p, evp, target, log);
    }

    public void ApplyResults(Job job, ActionPackage p, EvaluationPackage evp, Memory_Attitude att, Character_Trainable target, ExperienceLog log)
    {
        //Debug.Log("ApplyResults doer[" + (evp.Doer != null ? evp.Doer.FirstName : "-") + "] receiver[" + (evp.Receiver != null ? evp.Receiver.FirstName : "-") + "]");
        results.ApplyResults(job, p, evp, target,log);
    }

    public bool ValidateJob(Job j, out string msg)
    {
        msg = "";
        if (this.requirements.requireContaining != null && !requirements.requireContaining.Validate(j, out msg))
        {
            msg = "require containing null";
            return false;
        }
        if (hasFactionReq && j.FactionOwner == null)
        {
            msg = "missing faction owner";
            return false;
        }
        return true;
    }

    public bool ValidateRoom(Room_Instance r, out string tooltips)
    {
        if (this.requirements.requireRoomExisting != null && !requirements.requireRoomExisting.Validate(r, out tooltips)) return false;
        foreach(var variant in this.variants) if (variant.requirements.requireRoomExisting != null && !variant.requirements.requireRoomExisting.Validate(r, out tooltips)) return false;
        tooltips = "";
        return true;
    }

    //public bool countDoerAsReceiver = false;
    /*
        public bool IsActorValid(int doerRefID, int receiverRefID = -1)
        {
            Character_Trainable doer = scr_System_CampaignManager.current.FindInstanceByID(doerRefID);
            bool returnVal = doerBodyTags.Count < 1 || doer.Body.HasBodyTag(this.doerBodyTags);
            if (receiverRefID != -1)
            {
                Character_Trainable receiver = scr_System_CampaignManager.current.FindInstanceByID(receiverRefID);
                returnVal = returnVal && (receiverBodyTags.Count < 1 || receiver.Body.HasBodyTag(this.receiverBodyTags));
            }
            return returnVal;
        }*/

  
    protected virtual bool ValidateCondition(List<string> _tooltip, List<Character_Trainable> doerRefs, List<Character_Trainable> receiverRefs, COM com, out bool hardlock, COM_Variant variant = null, int actorCountMult = 1)
    {
        bool value1 = requirements.requirement.Validate(ref _tooltip, doerRefs, receiverRefs, out hardlock, null, actorCountMult);
        bool value2 = variant == null ? true : variant.requirements.requirement.Validate(ref _tooltip, doerRefs, receiverRefs, out hardlock, com.requirements.requirement, actorCountMult);
        // if (!doerRefIDs.Contains(0)) Debug.Log("ValidateCondition com["+com.displayName+"] value["+value1+"] variant["+(variant == null? "-":variant.displayName)+"] value["+value2+"] doers["+String.Join(",",doerRefIDs)+ "] receivers[" + String.Join(",", receiverRefIDs) + "] ");
        if (!(value1 && value2))
        {
            //if (_tooltip != null) _tooltip.Add("ValidateCondition failed value1[" + value1 + "] value2[" + value2 + "]");
        }
        return value1 && value2;
    }
    protected virtual bool ValidateCondition(List<string> _tooltip, Character_Trainable doerRefs, COM com, out bool hardlock, COM_Variant variant = null)
    {
        hardlock = false;
        bool value1 = requirements.requirement.Validate(ref _tooltip, doerRefs, out hardlock, null);
        bool value2 = variant == null ? true : variant.requirements.requirement.Validate(ref _tooltip, doerRefs, out hardlock, com.requirements.requirement);
        // if (!doerRefIDs.Contains(0)) Debug.Log("ValidateCondition com["+com.displayName+"] value["+value1+"] variant["+(variant == null? "-":variant.displayName)+"] value["+value2+"] doers["+String.Join(",",doerRefIDs)+ "] receivers[" + String.Join(",", receiverRefIDs) + "] ");
        if (!(value1 && value2))
        {
           //if (_tooltip != null) _tooltip.Add("ValidateCondition failed value1[" + value1 + "] value2[" + value2 + "]");
        }
        return value1 && value2;
    }

    public bool allowInPrivateRoom = false;
    public virtual string DisplayName(Job sourceJob, List<Character_Trainable> doerRefIDs, List<Character_Trainable> receiverRefIDs = null, bool excludeRequireExisting = false, int actorCountMult = 1)
    {
        int index = GetValidVariant(sourceJob, doerRefIDs, receiverRefIDs, excludeRequireExisting, actorCountMult);
        if (index < 0) return LocalizeDictionary.QueryThenParse(this.displayName);
        else return LocalizeDictionary.QueryThenParse(variants[index].displayName);
    }

    Dictionary<int, string> _cachedDisplayNames = new Dictionary<int, string>();

    public virtual string DisplayName(int index = -1)
    {
        if (_cachedDisplayNames.TryGetValue(index, out var name)) return name;
        else
        {
            var n = index < 0 || index >= variants.Count ? LocalizeDictionary.QueryThenParse(this.displayName) : LocalizeDictionary.QueryThenParse(variants[index].displayName);
            _cachedDisplayNames.TryAdd(index, n);
            return n;
        }
    }

    [JsonIgnore] public bool isJobCOM { get { return !isSleepCOM && comTags.Contains("job"); } }
    [JsonIgnore] public bool isSleepCOM { get { return ID == "com_furniture_sleep"; } }
    [JsonIgnore] public bool isRecreationCOM { get { return !isSleepCOM && !isJobCOM; } }

    public int GetValidVariant(Job sourceJob, List<Character_Trainable> doerRefIDs, List<Character_Trainable> receiverRefIDs, bool excludeRequireExisting = false, int actorCountMult = 1)
    {
        List<string> s = new List<string>();
        return GetValidVariant(ref s, sourceJob, doerRefIDs, receiverRefIDs, excludeRequireExisting, actorCountMult);
    }

    public int GetValidVariant(Character_Trainable doerRefIDs, bool excludeRequireExisting = false)
    {
        int index = -1;
        //if (receiverRefIDs == null || receiverRefIDs.Count < 1) receiverRefIDs = doerRefIDs;
        if (!requirements.requireExisting.ValidateCondition(null, doerRefIDs, this))
        {
            return -1;
        }
        if (variants.Count < 1)
        {
            return -1;
        }

        if (!AllowDuringSex)
        {

        }
        else if (comTags.Contains("initSex"))
        {

        }

        if (comTags.Contains("endSex"))
        {
            Debug.Log("endSex checking doer currentjob");
            if (doerRefIDs.CurrentJob is not Job_Sex_Group) return -1;
        }

        if (!ValidateCondition(null, doerRefIDs, this, out bool hardlock))
        {
            return -1;
        }

        foreach (COM_Variant var in variants)
        {
            //s.Clear();
            if (!requirements.requireExisting.ValidateCondition(null, doerRefIDs, this, var)) continue;
            if (!ValidateCondition(null, doerRefIDs, this, out hardlock, var)) continue;
            if (excludeRequireExisting && var.requirements.requireExisting.isValid) continue;
            //if( !comTags.Contains("sex") && !comTags.Contains("touch")) Debug.Log("validate com " + displayName + " return true valid variant id "+ variants.IndexOf(var));
            index = Math.Max(index, variants.IndexOf(var));
            if (index > -1)
            {
                return index;
            }
        }
        return index;
    }

    [JsonIgnore] public bool AllowDuringSex { get { return comTags.Contains("sex") || comTags.Contains("canbeignored") || comTags.Contains("initSex") || comTags.Contains("endSex"); } }

    public int GetValidVariant(ref List<string> tooltip, Job sourceJob, List<Character_Trainable> doerRefIDs, List<Character_Trainable> receiverRefIDs, bool excludeRequireExisting = false, int actorCountMult = 1)
    {
        int index = -1;
        bool logging = tooltip != null && !scr_UpdateHandler.current.Updating;
        //if (receiverRefIDs == null || receiverRefIDs.Count < 1) receiverRefIDs = doerRefIDs;
        if (this.requirements.requireFactionExisting != null && !requirements.requireFactionExisting.Validate(sourceJob.FactionOwner, out var tooltips))
        {
            if (logging) tooltip.Add(tooltips);
            return -1;
        }
        if (!requirements.requireExisting.ValidateCondition(tooltip, doerRefIDs, receiverRefIDs, this))
        {
            return -2;
        }
        if (variants.Count < 1)
        {
            if (logging)
            {
                tooltip.Add("Command GetValidVariant invalid: no variant in command");
            }
            return -2;
        }
        if (doerRefIDs.Count < 1)
        {
            if (logging)
            {
                tooltip.Add(LocalizeDictionary.QueryThenParse("ui_ap_PreEvaluate_requireDoer"));
            }
            return -2;
        }
        if (!AllowDuringSex)
        {
            foreach (var receiver in receiverRefIDs)
            {
                if (receiver == null)
                {
                    tooltip.Add(LocalizeDictionary.QueryThenParse("ui_COM_Requirements_requireReceiver"));
                    return -2;
                }
                else if (receiver.CurrentJob is Job_Sex_Group)
                {
                    tooltip.Add(LocalizeDictionary.QueryThenParse("ui_GetValidVariant_notAllowedDuringS"));
                    return -2;
                }
            }
        }
        if (comTags.Contains("initSex"))
        {
            Job existing = null;
            bool existJoinable = false;
            var targetlist = receiverRefIDs.Count > 0 ? receiverRefIDs : doerRefIDs;
            foreach (var i in targetlist)
            {
                var targetJob = i.CurrentJob;
                if (targetJob != null && targetJob is Job_Sex_Group)
                {
                    if (existing == null) existing = targetJob;
                    else if (existing == targetJob) continue;
                    else 
                    {
                        tooltip.Add(LocalizeDictionary.QueryThenParse("ui_GetValidVariant_initS_exist"));
                        return -1;
                    }
                }
                else existJoinable = true;
            }
            if (!existJoinable)
            {
                tooltip.Add(LocalizeDictionary.QueryThenParse("ui_GetValidVariant_initS_noJoinable"));
                return -1;
            }
        }
        if (comTags.Contains("endSex"))
        {
            var targetlist = receiverRefIDs.Count > 0 ? receiverRefIDs : doerRefIDs;
            foreach (var i in targetlist)
            {
                if (!(i.CurrentJob is Job_Sex_Group))
                {
                    tooltip.Add(LocalizeDictionary.QueryThenParse("ui_GetValidVariant_endS"));
                    return -1;
                }
            }
        }
        if (!ValidateCondition(tooltip, doerRefIDs, receiverRefIDs, this, out bool hardlock, null, actorCountMult))
        {
            return hardlock ? -2 : -1;
        }

        
        // check gender combination
        if (doerRefIDs.Count > 0 && receiverRefIDs.Count > 0 &&( comTags.Contains("sex") || comTags.Contains("service") || comTags.Contains("massage") || comTags.Contains("initSex")))
        {
            if (!scr_System_CentralControl.current.CanHaveSex(doerRefIDs, receiverRefIDs, out var ttip))
            {
                tooltip.Add(LocalizeDictionary.QueryThenParse("ui_GetValidVariant_forbiddenGenderInteraction")
                                    .Replace("$interaction$", ttip));
                return -2;
            }
        }



        List<string> s = new List<string>();
        List<string> s2 = new List<string>();
        for(int i = 0; i < variants.Count; i++)
        {
            var var = variants[i];

            if (this.requirements.requireFactionExisting != null && !requirements.requireFactionExisting.Validate(sourceJob.FactionOwner, out var tooltips2))
            {
                s2.Add($"{DisplayName(i)}: {tooltips2}");
                continue;
            }
            if (!requirements.requireExisting.ValidateCondition(s, doerRefIDs, receiverRefIDs, this, var))
            {
                s2.Add($"{DisplayName(i)}: {String.Join("|", s)}");
                continue;
            }
            s.Clear();
            if (!ValidateCondition(s, doerRefIDs, receiverRefIDs, this, out hardlock, var, actorCountMult))
            {
                s2.Add($"{DisplayName(i)}: {String.Join("|", s)}");
                continue;
            }
            s.Clear();
            if (excludeRequireExisting && var.requirements.requireExisting.isValid)
            {
                s2.Add($"{DisplayName(i)}: excludeRequireExisting");
                continue;
            }
            //if( !comTags.Contains("sex") && !comTags.Contains("touch")) Debug.Log("validate com " + displayName + " return true valid variant id "+ variants.IndexOf(var));
            index = Math.Max(index, variants.IndexOf(var));
            if (index > -1) return index;
        }

        tooltip.AddRange(s2);
        return index;
    }

    /*
     GetSuccess
        += base DC
        
        if dice lands in success, target like the interaction
            target.getreaction(com, success)
            customizable response based on target personality and com

        if dice lands in neutral, target lukewarm
            target.getreaction(com, success)
            customizable response based on target personality and com

        if dice lands in failure, target reaction maybe ?
            target.getreaction(com, success)
            customizable response based on target personality and com

            
     if COM def can force, then if failure, let player decide push it or not
        only on interaction or sex


     */

    public void OnAfterDeserialize()
    {
        if (comTags.Contains("position_face")) conflictTags.Add("position_reverse");
        if (comTags.Contains("position_reverse")) conflictTags.Add("position_face");
        if (comTags.Contains("position_equal")) conflictTags.Add("position_inequal");
        if (comTags.Contains("position_inequal")) conflictTags.Add("position_equal");

        foreach (string s in requirements.requirement.req_Doers.BodyTags)
        {
            AddCOMTags(s);
        }
        foreach(string s in requirements.requirement.req_Receivers.BodyTags)
        {
            AddCOMTags(s);
        }
        if (this.variants == null) variants = new List<COM_Variant>();
        if (this.variants.Count < 1) variants.Add(new COM_Variant(this.displayName, this));

        foreach(COM_Variant vari in this.variants) vari.Read(this);
    }

    protected void AddCOMTags(string s)
    {
        switch (s)
        {
            case "vagina": if ((comTags.Contains("sex") || comTags.Contains("touch")) && !comTags.Contains("vagina")) comTags.Add("vagina"); break;
            case "clit": 
                break;
            case "anus": if ((comTags.Contains("sex") || comTags.Contains("touch")) && !comTags.Contains("anus")) comTags.Add("anus"); break;
            case "breast": if ((comTags.Contains("sex") || comTags.Contains("touch")) && !comTags.Contains("breast")) comTags.Add("breast"); break;
            case "nipple": if ((comTags.Contains("sex") || comTags.Contains("touch")) && !comTags.Contains("breast")) comTags.Add("breast"); break;
            case "mouth": if ((comTags.Contains("sex") || comTags.Contains("touch")) && !comTags.Contains("oral")) comTags.Add("oral"); break;
            case "penis":
                break;
            default:
                break;

        }
    }

    [System.Serializable]
    public class DerivedStatMod
    {
        [SerializeField] string statDerivedBaseString = "";
        private Stats_Derived_Base parent = null;
        public Stats_Derived_Base Parent
        {
            get
            {
                if (statDerivedBaseString == "") return null;
                if (parent == null) parent = parent = scr_System_Serializer.current.GetByNameOrID_StatsDerivedBase(statDerivedBaseString);
                return parent;
            }
        }
        int addValue = 0;
        public int Value { get { return addValue; } }

    }

    /// <summary>
    /// Used during serialization, check internal data valid or not. if invalid, dont add to DB.
    /// </summary>
    [JsonIgnore]
    
    public virtual bool isValid { get
        {
            return true;
        } }

    [JsonIgnore]
    public bool hasFactionReq
    {
        get
        {
            if (this.requirements.hasFactionReq) return true;
            else foreach (var variant in this.variants) if (variant.requirements.hasFactionReq) return true;
            return false;
        }
    }

    [JsonIgnore]
    public List<string> CategoryLabel
    {
        get
        {
            return categoryTags;
        }
    }

    public virtual string GetVariantDescription(int variantID, bool isDoer, int charaRef, string roomName, List<int> DoerRefs, List<int> ReceiverRefs, int masterRef)
    {
        return variants[variantID].GetVariantDescription(false, isDoer, charaRef, roomName, DoerRefs, ReceiverRefs, masterRef);
    }

    [System.Serializable]
    public class COM_Variant
    {
        //[NonSerialized] private int ownerIndex = -1;
        public string description_doer_1_0 = "";
        public string description_doer_n_0 = "";
        public string description_doer_1_n = "";
        public string description_doer_n_n = "";
        public string description_receiver_n_1 = "";
        public string description_receiver_n_n = "";
        public string displayName = "";

        // (-1) equals use Base.
        // (-2) equals dont use Base.
        // (0 or above equals get another)
        public int useAnothersDescription = -1;
        public bool useBaseDescription = true;

        public COM_Descriptions description_begin = new COM_Descriptions();
        public COM_Descriptions description_remove = new COM_Descriptions();
        public COM_Descriptions description_ongoing = new COM_Descriptions();
        public COM_Descriptions description_after = new COM_Descriptions();
        public COM_Descriptions description_onComplete = new COM_Descriptions();

        public COM_Requirements requirements = new COM_Requirements();
        public bool setForce = false;

        public string GetDescription_Begin(COM ownerCOM, EvaluationPackage evp)
        {
            //Debug.LogError("GetDescription_Begin Variant isOwnerNull?["+ (ownerCOM == null )+ "] useAnotherDesc?["+ useAnothersDescription + "]");
            //if (ownerCOM == null) Debug.LogError("ownerCOM is null");
            List<string> s = new List<string>();
            s.Add(description_begin.GetText(ref evp));
            // prevent infinite loop
            if (useAnothersDescription > -1 && useAnothersDescription != ownerCOM.variants.IndexOf(this)) s.Add( ownerCOM.GetDescription_Begin(evp, useAnothersDescription));
            if (useBaseDescription) s.Add(ownerCOM.GetDescription_Begin(evp, -1));

            if (s.Count > 1 && s.Find(x => x == "$DEFAULT$") != null) s.RemoveAll(x => x == "$DEFAULT$");
            s.RemoveAll(x => x.Length < 1);
            string s2 = String.Join("\n", s);
            //s2 = Utility.StringReplace(ref evp, s2); 
            //Debug.LogError($"GetDescription_Begin: {s2} 2");
            return s2;
        }
        public string GetDescription_Begin(COM ownerCOM, ActionPackage ap)
        {
            //Debug.LogError("GetDescription_Begin Variant isOwnerNull?["+ (ownerCOM == null )+ "] useAnotherDesc?["+ useAnothersDescription + "]");
            //if (ownerCOM == null) Debug.LogError("ownerCOM is null");
            List<string> s = new List<string>();
            s.Add(description_begin.GetText(ap));
            // prevent infinite loop
            if (useAnothersDescription > -1 && useAnothersDescription != ownerCOM.variants.IndexOf(this)) s.Add(ownerCOM.GetDescription_Begin(ap, useAnothersDescription));
            if (useBaseDescription) s.Add(ownerCOM.GetDescription_Begin(ap, -1));

            if (s.Count > 1 && s.Find(x => x == "$DEFAULT$") != null) s.RemoveAll(x => x == "$DEFAULT$");
            s.RemoveAll(x => x.Length < 1);
            string s2 = String.Join("\n", s);
            //s2 = Utility.StringReplace(ref evp, s2);
            //Debug.LogError($"GetDescription_Begin: {s2} 1");
            return s2;
        }

        public string GetDescription_Remove(COM ownerCOM, EvaluationPackage evp)
        {
            
            List<string> s = new List<string>();
            s.Add(description_remove.GetText(ref evp));
            // prevent infinite loop
            if (useAnothersDescription > -1 && useAnothersDescription != ownerCOM.variants.IndexOf(this)) s.Add(ownerCOM.GetDescription_Remove(evp, useAnothersDescription));
            if (useBaseDescription) s.Add(ownerCOM.GetDescription_Remove(evp, -1));

            if (s.Count > 1 && s.Find(x => x == "$DEFAULT$") != null) s.RemoveAll(x => x == "$DEFAULT$");

            string s2 = String.Join("\n", s);
            //s2 = Utility.StringReplace(ref evp, s2);
            return s2;
        }

        public string GetDescription_Ongoing(COM ownerCOM, EvaluationPackage evp)
        {
            List<string> s = new List<string>();
            s.Add(description_ongoing.GetText(ref evp));
            // prevent infinite loop
            if (useAnothersDescription > -1 && useAnothersDescription != ownerCOM.variants.IndexOf(this)) s.Add(ownerCOM.GetDescription_Ongoing(evp, useAnothersDescription));
            if (evp == null || ownerCOM == null) Debug.LogError($"evp null? {evp == null} ownercom null? {ownerCOM == null}");
            if (useBaseDescription) s.Add(ownerCOM.GetDescription_Ongoing(evp, -1));

            if (s.Count > 1 && s.Find(x => x == "$DEFAULT$") != null) s.RemoveAll(x => x == "$DEFAULT$");
            s.RemoveAll(x => x.Length < 1);
            string s2 = String.Join("\n", s);
            //s2 = Utility.StringReplace(ref evp, s2);
            return s2;
        }
        public string GetDescription_Ongoing(COM ownerCOM, ActionPackage ap)
        {
            List<string> s = new List<string>();
            s.Add(description_ongoing.GetText(ap));
            // prevent infinite loop
            if (useAnothersDescription > -1 && useAnothersDescription != ownerCOM.variants.IndexOf(this)) s.Add(ownerCOM.GetDescription_Ongoing(ap, useAnothersDescription));
            if (ap == null || ownerCOM == null) Debug.LogError($"ap null? {ap == null} ownercom null? {ownerCOM == null}");
            if (useBaseDescription) s.Add(ownerCOM.GetDescription_Ongoing(ap, -1));

            if (s.Count > 1 && s.Find(x => x == "$DEFAULT$") != null) s.RemoveAll(x => x == "$DEFAULT$");
            s.RemoveAll(x => x.Length < 1);
            string s2 = String.Join("\n", s);
            //s2 = Utility.StringReplace(ref evp, s2);
            return s2;
        }

        public string GetDescription_After(COM ownerCOM, ActionPackage ap)
        {
            List<string> s = new List<string>();
            s.Add(description_after.GetText(ap));
            // prevent infinite loop
            if (useAnothersDescription > -1 && useAnothersDescription != ownerCOM.variants.IndexOf(this)) s.Add(ownerCOM.GetDescription_After(ap, useAnothersDescription));
            if (useBaseDescription) s.Add(ownerCOM.GetDescription_After(ap, -1));

            if (s.Count > 1 && s.Find(x => x == "$DEFAULT$") != null) s.RemoveAll(x => x == "$DEFAULT$");
            s.RemoveAll(x => x.Length < 1);
            string s2 = String.Join("\n", s);
            //s2 = Utility.StringReplace(ref evp, s2);
            return s2;
        }

        public string GetDescription_After(COM ownerCOM, EvaluationPackage evp)
        {
            List<string> s = new List<string>();
            s.Add(description_after.GetText(ref evp));
            // prevent infinite loop
            if (useAnothersDescription > -1 && useAnothersDescription != ownerCOM.variants.IndexOf(this)) s.Add(ownerCOM.GetDescription_After(evp, useAnothersDescription));
            if (useBaseDescription) s.Add(ownerCOM.GetDescription_After(evp, -1));

            if (s.Count > 1 && s.Find(x => x == "$DEFAULT$") != null) s.RemoveAll(x => x == "$DEFAULT$");
            s.RemoveAll(x => x.Length < 1);
            string s2 = String.Join("\n", s);
            //s2 = Utility.StringReplace(ref evp, s2);
            return s2;
        }

        public string GetDescription_OnComplete(COM ownerCOM, ActionPackage ap)
        {
            List<string> s = new List<string>();
            s.Add(description_onComplete.GetText(ap));
            // prevent infinite loop
            if (useAnothersDescription > -1 && useAnothersDescription != ownerCOM.variants.IndexOf(this)) s.Add(ownerCOM.GetDescription_OnComplete(ap, useAnothersDescription));
            if (useBaseDescription) s.Add(ownerCOM.GetDescription_OnComplete(ap, -1));

            if (s.Count > 1 && s.Find(x => x == "$DEFAULT$") != null) s.RemoveAll(x => x == "$DEFAULT$");
            s.RemoveAll(x => x.Length < 1);
            if (s.Count < 1)
            {
                s.Add($"({ap.targetCOM.DisplayName(ap.COMVariantID)}: {LocalizeDictionary.QueryThenParse($"Memory_Response_{( ap.injectResult != Memory_Response.None ? ap.injectResult : "Complete" )}") })");
            }
            string s2 = String.Join("\n", s);
            return s2;
        }

        public string GetDescription_OnComplete(COM ownerCOM, EvaluationPackage evp)
        {
            List<string> s = new List<string>();
            s.Add(description_onComplete.GetText(ref evp));
            // prevent infinite loop
            if (useAnothersDescription > -1 && useAnothersDescription != ownerCOM.variants.IndexOf(this)) s.Add(ownerCOM.GetDescription_OnComplete(evp, useAnothersDescription));
            if (useBaseDescription) s.Add(ownerCOM.GetDescription_OnComplete(evp, -1));

            if (s.Count > 1 && s.Find(x => x == "$DEFAULT$") != null) s.RemoveAll(x => x == "$DEFAULT$");
            s.RemoveAll(x => x.Length < 1);
            if (s.Count < 1) s.Add($"({evp.targetCOM.DisplayName(evp.Package.COMVariantID)}: {evp.Response})");
            string s2 = String.Join("\n", s);
            return s2;
        }

        public string GetVariantDescription(bool withSelfDescription, bool isDoer, int charaRef, string roomName, List<int> doers, List<int> receivers, int masterRef)
        {
            string baseDesc = "";

            if (this.requirements.requirement.TreatReceiverAsDoer)
            {
                doers.AddRange(receivers);
                receivers.Clear();
                isDoer = doers.Contains(charaRef);
               // Debug.Log($"COM {this.displayName} treat receiver as doer, setting list |{String.Join(" ", doers)}|{String.Join(" ", receivers)}| ");
            }

            bool isReceiverActive = this.requirements.requirement.req_Receivers.requireAction;

            if (isDoer && doers.Count == 1)
            {
                if (receivers.Count < 1) baseDesc = description_doer_1_0 != "" ? description_doer_1_0 : "comDescription_1_0_doer";
                else baseDesc = description_doer_1_n != "" ? description_doer_1_n : isReceiverActive ? "comDescription_1_n_doer_active" : "comDescription_1_n_doer_passive";
            }
            else if (isDoer && doers.Count > 1)
            {
                if (receivers.Count < 1) baseDesc = description_doer_n_0 != "" ? description_doer_n_0 : "comDescription_n_0_doer";
                else  baseDesc = description_doer_n_n != "" ? description_doer_n_n : isReceiverActive ? "comDescription_n_n_doer_active" : "comDescription_n_n_doer_passive";
            }
            else if (!isDoer && receivers.Count > 0)
            {
                if (receivers.Count < 2) baseDesc = description_receiver_n_1 != "" ? description_receiver_n_1 : isReceiverActive ? "comDescription_n_1_receiver_active": "comDescription_n_1_receiver_passive";
                else baseDesc = description_receiver_n_n != "" ? description_receiver_n_n : isReceiverActive ? "comDescription_n_n_receiver_active": "comDescription_n_n_receiver_passive";
            }

            UtilityEX.GetActorNames(doers, out List<string> names_doers, out List<string> names_doers_other, charaRef);
            UtilityEX.GetActorNames(receivers, out List<string> names_receiver, out List<string> names_receiver_other, charaRef);

            baseDesc = LocalizeDictionary.QueryThenParse(baseDesc)
                .Replace("$comdesc$", LocalizeDictionary.QueryThenParse(this.displayName))
                //.Replace("$room$",roomName)
                .Replace("$other_doers$", String.Join(",", names_doers_other))
                .Replace("$doers$", String.Join(",", names_doers))
                .Replace("$receivers$", String.Join(",", names_receiver))
                .Replace("$other_receivers$", String.Join(",", names_receiver_other));

            /*
            if (withSelfDescription)
            {
                baseDesc = baseDesc.Replace("$self$", scr_System_CampaignManager.current.FindInstanceByID(charaRef).FirstName);
            }else baseDesc = baseDesc.Replace("$self$", "");
            */
            return baseDesc;
        }

        public COM_Variant()
        {

        }
        public COM_Variant(string s, COM c)
        {
            this.displayName = s;
            //ownerCOM = c;
            //this.requirements = new COM_Requirements();
            //Debug.LogError("COMVARIANT create useBaseDsc?[ " + (useBaseDscription == 1) + "] useAnotherDesc?[" + (useAnotherVariantDescription > -1) + "]");
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="parent"></param>
        /// <param name="m"></param>
        /// <param name="msg">Only provide MessageCollect if this should be visible</param>
        public void ApplyCost(COM parent, EvaluationPackage m, MessageCollect msg)
        {

            if (m.Doer != null) CharaReqUtility.ApplyCost( requirements.requirement.req_Doers,m, m.Doer, parent, true, msg);
            if (requirements.TreatReceiverAsDoer && m.Receiver != null) CharaReqUtility.ApplyCost(requirements.requirement.req_Doers,m, m.Receiver, parent, true, msg);

            if (m.Receiver != null) CharaReqUtility.ApplyCost(requirements.requirement.req_Receivers,m, m.Receiver, parent, false, msg);
            if (requirements.TreatDoerAsReceiver && m.Doer != null) CharaReqUtility.ApplyCost(requirements.requirement.req_Receivers,m, m.Doer, parent, false, msg);

        }
        public void Read(COM c) {
            //if (c.VariantDoNotReadRequirement) Debug.LogError("reading variant req despite forbidden");
            if(c == null) Debug.LogError("reading variant req owner null");
            //ownerCOM = c;
            if (!c.VariantDoNotReadRequirement) this.requirements.Read(c.requirements);
            //if (!(useBaseDsc == 1)) Debug.LogError("owner ["+ownerCOM.displayName+"] COMVARIANT READ useBaseDsc?[ " + (useBaseDsc == 1) + "] useAnotherDesc?[" + (useAnothersDesc > -1) + "]");
        }

    }

    public string PreEvaluate(Job sourceJob, List<int> doerRefIDs, List<int> receiverRefIDs)
    {
        List<Character_Trainable> doers = new List<Character_Trainable>();
        List<Character_Trainable> receivers = new List<Character_Trainable>();
        foreach (int i in doerRefIDs) doers.Add(scr_System_CampaignManager.current.FindInstanceByID(i));
        foreach (int i in receiverRefIDs) receivers.Add(scr_System_CampaignManager.current.FindInstanceByID(i));

        return PreEvaluate(sourceJob, doers, receivers);
    }

    public virtual string PreEvaluate(Job sourceJob, List<Character_Trainable> doerRefIDs, List<Character_Trainable> receiverRefIDs)
    {
        return "";
    }

    public string fallbackCOMID = "";
    public string ActionPackageClass = "";
    /// <summary>
    /// This is not called by Furniture.
    /// </summary>
    /// <param name="job"></param>
    /// <param name="doers"></param>
    /// <param name="receivers"></param>
    /// <param name="masterRef"></param>
    /// <returns></returns>
    public ActionPackage MakePackage(Job job, List<int> doers, List<int> receivers, int masterRef)
    {
        ActionPackage returnValue = null;
        Manageable.ProductionOrder pOrder = null;
        if (!isJobCOM || (job.FactionOwner != null && job.FactionOwner.FactionOwnerRoot.GetProductionOrder(job as Job_Furniture, out var xxx, out pOrder)))
        {

        }
        switch (ActionPackageClass)
        {
            case "ActionPackage_Interaction":
                returnValue = new ActionPackage_Interaction(job, this, doers, receivers, masterRef );
                break;
            case "ActionPackage_Sex":
                returnValue = new ActionPackage_Sex(job, this, doers, receivers, masterRef);
                break;
            case "ActionPackage_ProductionOrder":
                Job_Furniture jFurn = job as Job_Furniture;
                if (jFurn == null) break;
                else if (pOrder == null)
                {
                    Debug.LogError("ActionPackage_ProductionOrder creation error, missing pOrder");
                    returnValue = new ActionPackage_Interaction(job, this, doers, receivers, masterRef);
                }
                else returnValue = new ActionPackage_ProductionOrder(pOrder, jFurn, this, doers, receivers, masterRef);
                break;
            case "ActionPackage_Talk":
                returnValue = new ActionPackage_Talk(job, this, doers, receivers, masterRef);
                break;
            default:
                break;

        }
        if (returnValue == null) Debug.LogError("Error making package for com " + ID);
        return returnValue;
    }

    public List<ActionPackage> MakePackages(Job job, List<int> doers, List<int> receivers, int masterRef)
    {
        List<ActionPackage> returnValues = new List<ActionPackage>();
        if (this.GenerateAP != null && this.GenerateAP.itemTag != null && this.GenerateAP.targetAPClass != null)
        {
            var itemUseAP = this.GenerateAP.targetAPClass as ActionPackage_ItemUse;
            if (itemUseAP == null)
            {
                // error

            }
            else
            {
                var items = job.FactionOwner.Inventory.GetItemByTag(this.GenerateAP.itemTag);
                foreach (var item in items)
                {
                    // make package
                    var newap = itemUseAP.Copy() as ActionPackage_ItemUse;
                    //newap.ItemInstance = item;
                    newap.ReInitializeCOM(job, this, doers, receivers, masterRef, true);
                    newap.LoadItem(item);
                    returnValues.Add(newap);
                }
            }

            if (returnValues.Count < 1) Debug.Log($"Error GenerateAP for com {ID}, probably missing items");
        }
        else
        {
            ActionPackage returnValue = null;
            Manageable.ProductionOrder pOrder = null;
            if (!isJobCOM || (job.FactionOwner != null && job.FactionOwner.FactionOwnerRoot.GetProductionOrder(job as Job_Furniture, out var xxx, out pOrder)))
            {

            }
            switch (ActionPackageClass)
            {
                case "ActionPackage_Interaction":
                    returnValue = new ActionPackage_Interaction(job, this, doers, receivers, masterRef);
                    if (returnValue != null) returnValues.Add(returnValue);
                    break;
                case "ActionPackage_ItemUse":


                    break;
                case "ActionPackage_Sex":
                    returnValue = new ActionPackage_Sex(job, this, doers, receivers, masterRef);
                    if (returnValue != null) returnValues.Add(returnValue);
                    break;
                case "ActionPackage_ProductionOrder":
                    Job_Furniture jFurn = job as Job_Furniture;
                    if (jFurn == null) break;
                    else if (pOrder == null)
                    {
                        Debug.LogError("ActionPackage_ProductionOrder creation error, missing pOrder");
                        returnValue = new ActionPackage_Interaction(job, this, doers, receivers, masterRef);
                    }
                    else returnValue = new ActionPackage_ProductionOrder(pOrder, jFurn, this, doers, receivers, masterRef);
                    if (returnValue != null) returnValues.Add(returnValue);
                    break;

                default:
                    break;

            }
            if (returnValues.Count < 1) Debug.LogError("Error making package for com " + ID);
        }
        
        return returnValues;
    }


   public virtual string Replace(string s)
    {
        return s;
    }
}