using System.Collections.Generic;
using UnityEngine;
using System;
using Newtonsoft.Json;
using System.Linq;

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
                var serializedParent = JsonConvert.SerializeObject(list[i], Utility.SerializerSettings);
                list[i] = JsonConvert.DeserializeObject<COM_Sex>(serializedParent, Utility.SerializerSettings);
            }
            else if (list[i] is COM_TakeMeal || list[i] is COM_Character_Insert || list[i] is COM_Character_Remove || list[i] is COM_FarmRecipe || list[i].ID.Contains("_noSex", StringComparison.InvariantCultureIgnoreCase))
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
            //Debug.Log("serializing com id "+s.ID);
            if (ID_Dictionary.ContainsKey(s.ID)) ID_Dictionary[s.ID] = s;
            else ID_Dictionary.Add(s.ID, s);
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
            if (list[i].ID == "com_furniture_getmeal")
            {
                foreach (Item_Base item in Masterlist_Items.Instance.Index.List)
                {
                    if (item.Tags.Contains("food_meal"))
                    {
                        var serializedParent = JsonConvert.SerializeObject(list[i], Utility.SerializerSettings);
                        COM_TakeMeal newCOM1 = JsonConvert.DeserializeObject<COM_TakeMeal>(serializedParent, Utility.SerializerSettings);
                        newCOM1.Initialize(list[i], item);

                        if (newCOMs.Find(x => x.ID == newCOM1.ID) == null) newCOMs.Add(newCOM1);
                        else Debug.LogError($"already contain mealcom with id {newCOM1.ID}");
                    }
                }
            }
            else if (list[i].requirements.requireContaining != null && list[i].requirements.requireContaining.allowPlanting != null)
            {
                // make restraint furniture stuff
                if (list[i].requirements.requireContaining.allowPlanting.Contains("character_trainable"))
                {

                    var serializedParent = JsonConvert.SerializeObject(list[i], Utility.SerializerSettings);
                    COM_Character_Insert newCOM1 = JsonConvert.DeserializeObject<COM_Character_Insert>(serializedParent, Utility.SerializerSettings);
                    COM_Character_Remove newCOM2 = JsonConvert.DeserializeObject<COM_Character_Remove>(serializedParent, Utility.SerializerSettings);
                    // COM_FarmRecipe rcp = new COM_FarmRecipe(list[i], recipe);
                    newCOM1.Initialize();
                    newCOM2.Initialize();
                    //newCOM3.Initialize();


                    if (newCOMs.Find(x => x.ID == newCOM1.ID) == null) newCOMs.Add(newCOM1);
                    if (newCOMs.Find(x => x.ID == newCOM2.ID) == null) newCOMs.Add(newCOM2);
                    //if (newCOMs.Find(x => x.ID == newCOM3.ID) == null) newCOMs.Add(newCOM3);
                }
                else if (list[i].requirements.requireContaining.allowPlanting.Count == 1 && list[i].requirements.requireContaining.allowPlanting[0] == "")
                {   // this is a special case just to handle/initialize com_job_farm_remove

                   // Debug.Log("initializing remove plant com [" + list[i].ID + "]");
                    var serializedParent = JsonConvert.SerializeObject(list[i], Utility.SerializerSettings);
                    COM_FarmRecipe newCOM = JsonConvert.DeserializeObject<COM_FarmRecipe>(serializedParent, Utility.SerializerSettings);
                    newCOM.InitializeRecipe(null);
                    list[i] = newCOM;
                    // COM_FarmRecipe rcp = new COM_FarmRecipe(list[i], recipe);
                }
                else
                { // make farm recipe stuff
                    foreach (var recipe in Masterlist_Items.Instance.FarmRecipe)
                    {
                        if (list[i].requirements.requireContaining.allowPlanting.Contains(recipe.growType))
                        {
                            // make new variant with recipe
                            var serializedParent = JsonConvert.SerializeObject(list[i], Utility.SerializerSettings);
                            COM_FarmRecipe newCOM = JsonConvert.DeserializeObject<COM_FarmRecipe>(serializedParent, Utility.SerializerSettings);
                            newCOM.InitializeRecipe(recipe);

                            // COM_FarmRecipe rcp = new COM_FarmRecipe(list[i], recipe);

                            if (newCOMs.Find(x => x.ID == newCOM.ID) == null) newCOMs.Add(newCOM);
                        }
                    }
                }
            }
            if (list[i].comTags.Contains("service") || list[i].comTags.Contains("sex") || (list[i].comTags.Contains("touch") && !list[i].comTags.Contains("safe")))
            {
                list[i].comTags.Add("unsafe");
            }


            if (list[i].comTags.Contains("sex") && (list[i].comTags.Contains("service") || list[i].comTags.Contains("nosexvariant")))
            {
                var serializedParent = JsonConvert.SerializeObject(list[i], Utility.SerializerSettings);
                var newCOM = JsonConvert.DeserializeObject<COM_Sex>(serializedParent, Utility.SerializerSettings);
                newCOM.comTags.Remove("sex");
                newCOM.requirements.requirement.req_Receivers.requireAbsentJobwithCOMTag.Add("sex");
                newCOM.ID += "_noSex";
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
public class COM: I_SerializationCallbackReceiver
{

    [System.Serializable]
    public class Acceptance
    {
        public int baseAcceptanceValue = 0;
        public bool useDefault = true;
    }
    [System.Serializable]
    public class Difficulty
    {
        public int baseD20Check = 0;
        public bool useDefault = true;
    }

    public bool COMRepeat = false;
    public bool HideWhenInvalid = false;
    public string ID = "";

    public string tooltip = "";
    public string displayName = "";
    public bool VariantDoNotReadRequirement = false;

    public List<COM_Variant> variants = new List<COM_Variant>();
    //public List<string> conflictCOMIDs = new List<string>();
    [JsonIgnore] public bool isInteraction { get { return comTags.Contains("interaction") || comTags.Contains("sex"); } }

    public Acceptance AcceptanceCheck = new Acceptance();
    public Difficulty DifficultyCheck = new Difficulty();
    [JsonIgnore] public int baseD20Check { get { return DifficultyCheck.baseD20Check; } }
    [JsonIgnore] public int baseAcceptanceValue { get { return AcceptanceCheck.baseAcceptanceValue; } }

    //public int moodModValue = 0, stressModValue = 0, lustModValue = 0;

    public List<string> comTags = new List<string>();

    [JsonIgnore] public bool isSexCOM { get { return comTags.Contains("sex"); } }
    [JsonIgnore] public bool isUnsafe { get { return comTags.Contains("unsafe"); } }
    [JsonIgnore] public bool isTouchCOM { get { return !isSexCOM && comTags.Contains("touch"); } }

    [SerializeField][JsonProperty] protected int timeScale = 0;
    [JsonIgnore] public int TimeScale { get { return timeScale; } }

    //public List<DerivedStatMod> doerStatMod = new List<DerivedStatMod>(), receiverStatMod = new List<DerivedStatMod>();

    public COM_Requirements requirements = new COM_Requirements();

    public COM_Descriptions description_begin = new COM_Descriptions();
    public COM_Descriptions descriptions_remove = new COM_Descriptions();
    public COM_Descriptions description_ongoing = new COM_Descriptions();
    public COM_Descriptions description_after = new COM_Descriptions();

    public virtual string GetDescription_Begin(EvaluationPackage evp, int variantID)
    {
        //Debug.LogError("GetDescription_Begin with variantID " + variantID);
        if (variantID == -1) return description_begin.GetText(ref evp);
        else if (variantID >= variants.Count) return "GetDescription_Begin ERROR variantID out of bound";
        else return variants[variantID].GetDescription_Begin(this, evp);
    }

    public virtual string GetDescription_Remove(EvaluationPackage evp, int variantID)
    {
        if (variantID == -1) return descriptions_remove.GetText(ref evp);
        else if (variantID >= variants.Count) return "GetDescription_Remove ERROR variantID out of bound";
        else return variants[variantID].GetDescription_Remove(this, evp);
    }

    public virtual string GetDescription_Ongoing(EvaluationPackage evp, int variantID)
    {
        if (variantID == -1) return description_ongoing.GetText(ref evp);
        else if (variantID >= variants.Count) return "GetDescription_Ongoing ERROR variantID out of bound";
        else return variants[variantID].GetDescription_Ongoing(this, evp);
    }

    public virtual string GetDescription_After(EvaluationPackage evp, int variantID)
    {
        if (variantID == -1) return description_after.GetText(ref evp);
        else if (variantID >= variants.Count) return "GetDescription_After ERROR variantID out of bound";
        else return variants[variantID].GetDescription_After(this, evp);
    }


    //public Validator_Costs costs;
    public COM_Results results = new COM_Results();

    public void ApplyCost(EvaluationPackage m)
    {
        //Debug.Log("VariantIDs: AP["+p.COMVariantID+"] EP["+m.VariantID+"] param["+variantID+"]");

        if (m.VariantID >= 0) this.variants[m.VariantID].ApplyCost(this, m);
        else
        {
            Debug.LogError("COM " + displayName + " apply cost error, both variantID < 0");
            int validVar = GetValidVariant(m.Doer, m.Receiver);
            if (validVar >= 0) this.variants[validVar].ApplyCost(this, m);
           // 
        }
    }

    public void ApplyResults(Job job, ActionPackage p, EvaluationPackage evp, Memory_Attitude att, Character_Trainable target)
    {
        //Debug.Log("ApplyResults doer[" + (evp.Doer != null ? evp.Doer.FirstName : "-") + "] receiver[" + (evp.Receiver != null ? evp.Receiver.FirstName : "-") + "]");
        results.ApplyResults(job, p, evp, target);
    }

    public bool ValidateJob(Job j, out string msg)
    {
        msg = "";
        if (this.requirements.requireContaining != null && !requirements.requireContaining.Validate(j))
        {
            msg = "missing require containing item";
            return false;
        }
        if (hasFactionReq && j.FactionOwner == null)
        {
            msg = "missing faction owner";
            return false;
        }
        if (j.FactionOwner != null && !ValidateFaction(j.FactionOwner))
        {
            return false;
        }
        if (j is Job_Furniture && !(j as Job_Furniture).CanCOMAcceptMoreActor(this))
        {
            msg = "furniture cannot accept more actor";
            return false;
        }
        return true;
    }

    public bool ValidateRoom(Room_Instance r)
    {
        if (this.requirements.requireRoomExisting != null && !requirements.requireRoomExisting.Validate(r)) return false;
        foreach(var variant in this.variants) if (variant.requirements.requireRoomExisting != null && !variant.requirements.requireRoomExisting.Validate(r)) return false;

        return true;
    }

    protected bool ValidateFaction(Manageable m)
    {
        if (this.requirements.requireFactionExisting != null && !requirements.requireFactionExisting.Validate(m)) return false;
        foreach (var variant in this.variants) if (variant.requirements.requireFactionExisting != null && !variant.requirements.requireFactionExisting.Validate(m)) return false;

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

    

    

    public virtual bool ValidateCondition(ref List<string> _tooltip, List<int> doerRefIDs, List<int> receiverRefIDs, COM com, COM_Variant variant = null)
    {
        bool value1 = requirements.requirement.Validate(ref _tooltip, doerRefIDs, receiverRefIDs, null);
        bool value2 = variant == null ? true : variant.requirements.requirement.Validate(ref _tooltip, doerRefIDs, receiverRefIDs, com.requirements.requirement);
       // if (!doerRefIDs.Contains(0)) Debug.Log("ValidateCondition com["+com.displayName+"] value["+value1+"] variant["+(variant == null? "-":variant.displayName)+"] value["+value2+"] doers["+String.Join(",",doerRefIDs)+ "] receivers[" + String.Join(",", receiverRefIDs) + "] ");
        if (!(value1 && value2))
        {
            _tooltip.Add("ValidateCondition failed value1[" + value1 + "] value2[" + value2 + "]");
        } 
        return value1 && value2;
    }

    public virtual string DisplayName(List<int> doerRefIDs, List<int> receiverRefIDs = null, bool excludeRequireExisting = false)
    {
        int index = GetValidVariant(doerRefIDs, receiverRefIDs, excludeRequireExisting);
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

    public int GetValidVariant(List<Character_Trainable> doers, List<Character_Trainable> receivers, bool excludeRequireExisting = false)
    {
        List<int> doerRefIDs = new List<int>();
        foreach (var c in doers) doerRefIDs.Add(c.RefID);

        List< int > receiverRefIDs = new List<int>();
        foreach (var c in receivers) receiverRefIDs.Add(c.RefID);

        return GetValidVariant(doerRefIDs, receiverRefIDs, excludeRequireExisting);
    }

    public int GetValidVariant(List<int> doerRefIDs, List<int> receiverRefIDs, bool excludeRequireExisting = false)
    {
        List<string> s = new List<string>();
        return GetValidVariant(ref s, doerRefIDs, receiverRefIDs, excludeRequireExisting);
    }

    public int GetValidVariant(Character_Trainable doer, Character_Trainable receiver)
    {
        List<int> doerRefIDs = new List<int>();
        if (doer != null) doerRefIDs.Add(doer.RefID);

        List<int> receiverRefIDs = new List<int>();
        if (receiver != null) receiverRefIDs.Add(receiver.RefID);

        return GetValidVariant(doerRefIDs, receiverRefIDs);
    }

    [JsonIgnore] public bool AllowDuringSex { get { return comTags.Contains("sex") || comTags.Contains("canbeignored") || comTags.Contains("initSex") || comTags.Contains("endSex"); } }

    public int GetValidVariant(ref List<string> tooltip, List<int> doerRefIDs, List<int> receiverRefIDs, bool excludeRequireExisting = false)
    {
        int index = -1;
        //if (receiverRefIDs == null || receiverRefIDs.Count < 1) receiverRefIDs = doerRefIDs;
        List<string> s2 = new List<string>();
        if (!requirements.requireExisting.ValidateCondition(ref s2, doerRefIDs, receiverRefIDs, this) && s2.Count > 0)
        {
            tooltip.Add("Command GetValidVariant invalid: missing required pre-existing command"); 
            tooltip.AddRange(s2);
            return -1;
        }
        if (variants.Count < 1)
        {
            tooltip.Add("Command GetValidVariant invalid: no variant in command"); 
            return -1;
        }
        if (doerRefIDs.Count < 1)
        {
            tooltip.Add("Command GetValidVariant invalid: no participant"); 
            return -1;
        }
        if (!AllowDuringSex)
        {
            foreach (var i in receiverRefIDs)
            {
                var receiver = scr_System_CampaignManager.current.FindInstanceByID(i);
                if (receiver == null) return -1;
                else if (receiver.CurrentJob is Job_Sex_Group)
                {
                    tooltip.Add("Command GetValidVariant invalid: not allowed during sex");
                    return -1;
                }
            }
        }
        else if (comTags.Contains("initSex"))
        {
            foreach (var i in receiverRefIDs)
            {
                var targetJob = scr_System_CampaignManager.current.FindInstanceByID(i).CurrentJob;
                if (targetJob is Job_Sex_Group && Utility.ListContainsStrict(targetJob.actorRefID,doerRefIDs))
                {
                    tooltip.Add("Command GetValidVariant invalid: both actors already in sex");
                    return -1;
                }
            }
        }
        else if (comTags.Contains("endSex"))
        {
            foreach (var i in receiverRefIDs)
            {
                if (!(scr_System_CampaignManager.current.FindInstanceByID(i).CurrentJob is Job_Sex_Group))
                {
                    tooltip.Add("Command GetValidVariant invalid: no sex act can be ended");
                    return -1;
                }
            }
        }
        if (!ValidateCondition(ref tooltip, doerRefIDs, receiverRefIDs, this))
        {
            tooltip.Add("Command GetValidVariant invalid: requirement not met");
            return -1;
        }

        
        // check gender combination
        if (doerRefIDs.Count > 0 && receiverRefIDs.Count > 0 &&( comTags.Contains("sex") || comTags.Contains("service") || comTags.Contains("massage") || comTags.Contains("initSex")))
        {
            if (!scr_System_CentralControl.current.CanHaveSex(doerRefIDs, receiverRefIDs))
            {
                tooltip.Add("gender interaction forbidden by user");
                return -1;
            }
        }



        List<string> s = new List<string>();
        foreach (COM_Variant var in variants)
        {
            //s.Clear();
            if (!requirements.requireExisting.ValidateCondition(ref s, doerRefIDs, receiverRefIDs, this, var)) continue;
            if (!ValidateCondition(ref s, doerRefIDs, receiverRefIDs, this, var)) continue;
            if (excludeRequireExisting && var.requirements.requireExisting.isValid) continue;
            //if( !comTags.Contains("sex") && !comTags.Contains("touch")) Debug.Log("validate com " + displayName + " return true valid variant id "+ variants.IndexOf(var));
            index = Math.Max(index, variants.IndexOf(var));
            if (index > -1)
            {
                tooltip.AddRange(s);
                return index;
            }
        }
        if (s.Count > 0) tooltip.AddRange(s);
        return index;
    }

    public int GetValidVariant(int doerRefID, int receiverRefID = -1)
    {
        if (receiverRefID == -1) return GetValidVariant(new List<int>() { doerRefID }, new List<int>());
        else return GetValidVariant(new List<int>() { doerRefID }, new List<int>() { receiverRefID });
    }


    public virtual bool ValidateActors(ref List<string> tooltip, List<int> doerRefIDs, List<int> receiverRefIDs = null)
    {
        //if (!comTags.Contains("sex") && !comTags.Contains("touch")) Debug.Log("validate com " + displayName + " doers[" + String.Join(" ", doerRefIDs) + "] receivers[" +String.Join(" ",receiverRefIDs)+"]");
        if ( GetValidVariant(ref tooltip, doerRefIDs, receiverRefIDs) > -1) return true;
        return false;
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
        foreach(string s in requirements.requirement.req_Doers.BodyTags)
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
        [SerializeField] int addValue = 0;
        public int Value { get { return addValue; } }

    }

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
        public COM_Descriptions descriptions_remove = new COM_Descriptions();
        public COM_Descriptions description_ongoing = new COM_Descriptions();
        public COM_Descriptions description_after = new COM_Descriptions();

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
            return s2;
        }

        public string GetDescription_Remove(COM ownerCOM, EvaluationPackage evp)
        {
            
            List<string> s = new List<string>();
            s.Add(descriptions_remove.GetText(ref evp));
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

            Utility.GetActorNames(doers, out List<string> names_doers, out List<string> names_doers_other, charaRef);
            Utility.GetActorNames(receivers, out List<string> names_receiver, out List<string> names_receiver_other, charaRef);

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

        public void ApplyCost(COM parent, EvaluationPackage m)
        {

            if (m.Doer != null) requirements.requirement.req_Doers.ApplyCost(m, m.Doer, parent, true);
            if (requirements.TreatReceiverAsDoer && m.Receiver != null) requirements.requirement.req_Doers.ApplyCost(m, m.Receiver, parent, true);

            if (m.Receiver != null) requirements.requirement.req_Receivers.ApplyCost(m, m.Receiver, parent, false);
            if (requirements.TreatDoerAsReceiver && m.Doer != null) requirements.requirement.req_Receivers.ApplyCost(m, m.Doer, parent, false);

        }
        public void Read(COM c) {
            //if (c.VariantDoNotReadRequirement) Debug.LogError("reading variant req despite forbidden");
            if(c == null) Debug.LogError("reading variant req owner null");
            //ownerCOM = c;
            if (!c.VariantDoNotReadRequirement) this.requirements.Read(c.requirements);
            //if (!(useBaseDsc == 1)) Debug.LogError("owner ["+ownerCOM.displayName+"] COMVARIANT READ useBaseDsc?[ " + (useBaseDsc == 1) + "] useAnotherDesc?[" + (useAnothersDesc > -1) + "]");
        }

    }

    public string PreEvaluate(List<int> doerRefIDs, List<int> receiverRefIDs)
    {
        List<Character_Trainable> doers = new List<Character_Trainable>();
        List<Character_Trainable> receivers = new List<Character_Trainable>();
        foreach (int i in doerRefIDs) doers.Add(scr_System_CampaignManager.current.FindInstanceByID(i));
        foreach (int i in receiverRefIDs) receivers.Add(scr_System_CampaignManager.current.FindInstanceByID(i));

        return PreEvaluate(doers, receivers);
    }

    public virtual string PreEvaluate(List<Character_Trainable> doerRefIDs, List<Character_Trainable> receiverRefIDs)
    {
        return "";
    }


    public string ActionPackageClass = "";
    public ActionPackage MakePackage(Job job, List<int> doers, List<int> receivers, int masterRef, Manageable.ProductionOrder pOrder = null)
    {
        ActionPackage returnValue = null;
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

            default:
                break;

        }
        if (returnValue == null) Debug.LogError("Error making package for com " + ID);
        return returnValue;
    }

   
}