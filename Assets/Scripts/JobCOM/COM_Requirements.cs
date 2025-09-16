using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Linq;
using Newtonsoft.Json;

[System.Serializable]
public class COM_Requirements
{

    public BodyEquipLayer clothingRequirement = BodyEquipLayer.Outer;
    /// <summary>
    /// when flagged as true, do not establish doer-receiver relationship, instead make multiple doer-null relationship
    /// </summary>
    [JsonIgnore] public bool TreatReceiverAsDoer { get { return requirement.TreatReceiverAsDoer; } }

    /// <summary>
    /// when flagged as true, AND when there is doer-null relationship, make it into doer-doer.
    /// <br/> does nothing when there is doer-receiver relationship
    /// </summary>
    [JsonIgnore] public bool TreatDoerAsReceiver { get { return requirement.TreatDoerAsReceiver; } }

    /// <summary>
    /// when flagged as true, add party member to receivers. Happens before TreatReceiverAsDoer
    /// </summary>
    [JsonIgnore] public bool AddPartyMemberAsReceiver { get { return requirement.AddPartyMemberAsReceiver; } }

    /// <summary>
    /// when flagged as true, add party member to receivers. Happens before TreatDoerAsReceiver
    /// </summary>
    [JsonIgnore] public bool AddPartyMemberAsDoer { get { return requirement.AddPartyMemberAsDoer; } }

    [JsonIgnore]
    public bool hasFactionReq
    {
        get
        {
            if (this.requireFactionExisting != null && requireFactionExisting.isValid) return true;
            return false;
        }
    }

    public void Read(COM_Requirements req)
    {
        this.requirement.Read(req.requirement);
        this.requireRoomExisting.Read(req.requireRoomExisting);
    }

    public Requirement requirement = new Requirement();

    [System.Serializable]
    public class Requirement
    {
        public int doerCount = -1;
        public int receiverCount = -1;

        public CharaReq req_Doers = new CharaReq();
        public CharaReq req_Receivers = new CharaReq();

        /// <summary>
        /// indicates that this command has no receiver. </br>
        /// when making evaluatin packages, make one for every doer and every receiver
        /// </summary>
        /// 


        [JsonProperty] protected bool treatReceiverAsDoer = false;

        [JsonIgnore] public bool TreatReceiverAsDoer { get { return treatReceiverAsDoer; } }

        [JsonProperty] protected bool treatDoerAsReceiver = false;
        [JsonIgnore] public bool TreatDoerAsReceiver { get { return treatDoerAsReceiver; } }

        [JsonIgnore] public bool AddPartyMemberAsReceiver { get { return req_Receivers.addPartyMembers; } }
        [JsonIgnore] public bool AddPartyMemberAsDoer { get { return req_Doers.addPartyMembers; } }

        [JsonIgnore] public List<string> doerBodyTags { get { return req_Doers.BodyTags; } }
        [JsonIgnore] public List<string> receiverBodyTags { get { return req_Receivers.BodyTags; } }

        public void Read(Requirement req)
        {
            if (this.doerCount == -1 && req.doerCount != -1) this.doerCount = req.doerCount;
            if (this.receiverCount == -1 && req.receiverCount != -1) this.receiverCount = req.receiverCount;
            req_Doers.Read(req.req_Doers);
            req_Receivers.Read(req.req_Receivers);
            treatDoerAsReceiver = treatDoerAsReceiver || req.treatDoerAsReceiver;
            treatReceiverAsDoer = treatReceiverAsDoer || req.treatReceiverAsDoer;
        }

        public bool Validate(ref List<string> _tooltip, List<int> doerRefIDs, List<int> receiverRefIDs, Requirement extraCondition = null)
        {
            //int doercount = (extraCondition == null ? doerCount : (doerCount != -1 ? doerCount : extraCondition.doerCount));
            //int receivercount = (extraCondition == null ? receiverCount : (receiverCount != -1 ? receiverCount : extraCondition.receiverCount));
            var actorCount = new List<int>();
            actorCount.AddRange(doerRefIDs);
            if (TreatReceiverAsDoer) actorCount.AddRange(receiverRefIDs);
            actorCount = actorCount.Distinct().ToList();
            actorCount.RemoveAll(x=>x < 0);

            if (doerCount == -1) { }
            else if (doerCount == 0 && doerRefIDs.Count == 0 && (!treatReceiverAsDoer || receiverRefIDs.Count == 0)) { }
            else if (doerCount > 0 && actorCount.Count == doerCount) { }
            else if (doerCount > 9) { }
            else
            {
                _tooltip.Add("Command invalid: command doer number below requirement : treatRasD["+treatReceiverAsDoer+"] dCount["+doerRefIDs.Count+"] rCount["+receiverRefIDs.Count+"] finalEval["+ (treatReceiverAsDoer && ((doerRefIDs.Count + receiverRefIDs.Count) <= doerCount)) + "]");
                return false;
            }

            if (receiverCount == 0 && receiverRefIDs.Count != 0)
            {
                _tooltip.Add("Command invalid: command can only be done alone");
                return false;
            }

            if (receiverCount > 0 && receiverRefIDs.Count == 0)
            {
                _tooltip.Add("Command invalid: command cannot be done alone");
                return false;
            }

            if (receiverCount > 0 && receiverRefIDs.Count > receiverCount)
            {
                _tooltip.Add("Command invalid: too many receivers");
                return false;
            }

            //            Debug.Log("Command (Variant) Requirement Validating [" + String.Join(",", doerRefIDs) + "] and [" + String.Join(",", receiverRefIDs) + "]");

            if (!CharaReqUtility.Validate( req_Doers,ref _tooltip, doerRefIDs)) 
            {
                _tooltip.Add("doer failed doer req validation");
                return false;
            }
            if (treatDoerAsReceiver && !CharaReqUtility.Validate(req_Receivers,ref _tooltip, doerRefIDs))
            {
                _tooltip.Add("doer failed receiver req validation");
                return false;
            } 
            if (receiverCount > 0 && treatReceiverAsDoer && !CharaReqUtility.Validate(req_Doers,ref _tooltip, receiverRefIDs))
            {
                _tooltip.Add("receiver failed doer req validation");
                return false;
            } 
            if (receiverCount == 0) 
            {   
                bool value = CharaReqUtility.Validate(req_Receivers, ref _tooltip, doerRefIDs);
                if (!value) _tooltip.Add("doer failed receiver == 0 req validation");
                return value;
            }
            else{
                bool value = CharaReqUtility.Validate(req_Receivers, ref _tooltip, receiverRefIDs);
                if (!value) _tooltip.Add("receiver failed receiver req validation");
                return value;
            } 
        }

        
    }


    public RequireExisting requireExisting = new RequireExisting();
    [System.Serializable]
    public class RequireExisting
    {
        public List<string> comTags = new List<string>();

        public List<string> doerBodyTags = new List<string>();
        public List<string> receiverBodyTags = new List<string>();

        public bool OverPenetration = false;
        public string requireCOMID = "";
        public bool isValid
        {
            get { return comTags.Count > 0 || doerBodyTags.Count > 0 || receiverBodyTags.Count > 0 || OverPenetration || requireCOMID != ""; }
        }

        public bool ValidateCondition(ref List<string> tooltip, List<int> doerRefIDs, List<int> receiverRefIDs, COM com, COM.COM_Variant variant = null)
        {

            int actorRef = doerRefIDs.Count > 0 ? doerRefIDs.First(x => x > -1) : (receiverRefIDs.Count > 0 ? receiverRefIDs.First(x => x > -1) : -1);

            List<string> comtgs = (variant != null && variant.requirements.requireExisting.isValid && variant.requirements.requireExisting.comTags.Count > 0 ?
                variant.requirements.requireExisting.comTags : com.requirements.requireExisting.comTags);

            List<string> doerBTag = (variant != null && variant.requirements.requireExisting.isValid && variant.requirements.requireExisting.doerBodyTags.Count > 0 ?
                variant.requirements.requireExisting.doerBodyTags : com.requirements.requireExisting.doerBodyTags);

            List<string> receiverBTag = (variant != null && variant.requirements.requireExisting.isValid && variant.requirements.requireExisting.receiverBodyTags.Count > 0 ?
                variant.requirements.requireExisting.receiverBodyTags : com.requirements.requireExisting.receiverBodyTags);

            bool overpen = (variant != null && variant.requirements.requireExisting.isValid ?
                (variant.requirements.requireExisting.OverPenetration || com.requirements.requireExisting.OverPenetration) : com.requirements.requireExisting.OverPenetration);

            string comID = (variant != null && variant.requirements.requireExisting.isValid && variant.requirements.requireExisting.requireCOMID != "" ? variant.requirements.requireExisting.requireCOMID : this.requireCOMID);

            if (comtgs.Count < 1 && doerBTag.Count < 1 && receiverBTag.Count < 1 && OverPenetration == false && comID == "") return true;



            Job_Sex_Group jsex = actorRef != -1 ? scr_System_CampaignManager.current.FindInstanceByID(actorRef).CurrentJob as Job_Sex_Group : null;

            if (jsex == null) return false;

            // from here on, we know its sex job
            List<ActionPackage_Sex> existing = new List<ActionPackage_Sex>();
            foreach (var package in jsex.CurrentPackages)
            {
                if(package is ActionPackage_Sex)
                {
                    if (package.targetCOM == null) continue;
                    if (comID != "" && package.targetCOM.ID != comID) continue;
                    existing.Add(package as ActionPackage_Sex);
                }
                else
                {
                    continue;
                }
            }

            if (existing.Count < 1)
            {
                tooltip.Add("Command invalid: missing pre-req command");
                return false;
            }

            for (int i = existing.Count - 1; i >= 0; i--)
            {
                if (existing[i] == null)
                {
                    existing.RemoveAt(i);
                    continue;
                }

                if (comID != "" && ((existing[i].targetCOM == null || existing[i].targetCOM.ID != comID)))
                {
                    existing.RemoveAt(i);
                    continue;
                }

                //if (existing[i] == null) continue;
                if (!Utility.ListEquals(existing[i].DoerRefs, doerRefIDs))
                {
                    existing.RemoveAt(i);
                    continue;
                }
                if (!Utility.ListEquals(existing[i].ReceiverRefs, receiverRefIDs))
                {
                    existing.RemoveAt(i);
                    continue;
                }
                if (comtgs.Count > 0 && comtgs.Except(existing[i].targetCOM.comTags).Count() != 0)
                {
                    existing.RemoveAt(i);
                    continue;
                }
                if (doerBTag.Count > 0 && doerBTag.Except(existing[i].doerBodyTags).Count() != 0)
                {
                    existing.RemoveAt(i);
                    continue;
                }
                if (receiverBTag.Count > 0 && receiverBTag.Except(existing[i].receiverBodyTags).Count() != 0)
                {
                    existing.RemoveAt(i);
                    continue;
                }
                List<string> s = new List<string>();
                if (overpen && !(existing[i].targetCOM as COM_Sex).ValidateActorLength(ref s, existing[i].DoerRefs, existing[i].ReceiverRefs))
                {
                    existing.RemoveAt(i);
                    continue;
                }
            }

            if (existing.Count < 1)
            {
                tooltip.Add("Command invalid: missing pre-req command");
                return false;
            }
            else return true;

        }
    }

    public RequireRoomExisting requireRoomExisting = new RequireRoomExisting();
    /// <summary>
    /// This validator will only work if the command is attached to a Job_Furniture, as only Job_Furniture will call to this validator.
    /// </summary>
    [System.Serializable]
    public class RequireRoomExisting
    {
        // com is valid if there is iteminstance of allowed itembase in room
        // - drink cum from condom, check if there is filled condom in room
        // - cleaning job: check if there is dirt (item instance check is dirt) in room

        public List<RequireRoomExisting_FurnitureBase> requiresFurniture = null;
        public List<RequireRoomExisting_ItemBase> requiresItem = null;

        public void Read(RequireRoomExisting req)
        {
            if (req.requiresFurniture != null)
            {
                if (this.requiresFurniture == null) this.requiresFurniture = new List<RequireRoomExisting_FurnitureBase>();
                this.requiresFurniture.AddRange(req.requiresFurniture);
            }
            if (req.requiresItem != null)
            {
                if (this.requiresItem == null) this.requiresItem = new List<RequireRoomExisting_ItemBase>();
                this.requiresItem.AddRange(requiresItem);
            }
        }

        public bool Validate(Room_Instance targetRoom)
        {
            if (this.requiresFurniture != null) foreach (var req in requiresFurniture) if (!req.Validate(targetRoom)) return false;
            if (this.requiresItem != null) foreach (var req in requiresItem) if (!req.Validate(targetRoom)) return false;
            return true;
        }

        // how do i handle 

        [System.Serializable]
        public class RequireRoomExisting_FurnitureBase
        {
            // com is valid if there is furnitureinstance of allowed furniturebase in room
            // - job com "manage cultivation" check if there is plantation in room
            // - job com carniculture check if theres carni plant in room
            public string furnitureBaseID = "";
            public int minimumCount = 0;
            public bool Validate(Room_Instance targetRoom)
            {
                if (minimumCount == 0) return true;
                if (furnitureBaseID == "") return true;

                int i = 0;
                foreach (FurnitureInstance fi in targetRoom.Furnitures)
                {
                    if (fi.FurnitureBase.ID == furnitureBaseID) i++;
                }
                return i >= minimumCount;
            }

        }

        [System.Serializable]
        public class RequireRoomExisting_ItemBase
        {
            public string itemBaseID = "";
            public string itemTag = "";
            public int minimumCount = 0;

            public bool Validate(Room_Instance targetRoom)
            {
                if (minimumCount == 0) return true;
                if (itemBaseID == "" && itemTag == "") return true;



                if (itemBaseID != "")
                {
                    int i = targetRoom.HasItem_BaseID_Count(itemBaseID);
                    //Debug.LogError("RequireRoomExisting_ItemBase Validating itemBaseID[" + itemBaseID + "] itemTag[" + itemTag + "] minCount[" + minimumCount + "] actualCount["+i+"]");
                    if (i >= minimumCount) return true;
                    else return false;
                }

                if (itemTag != "")
                {
                    int i = targetRoom.HasItem_Tag_Count(itemTag);
                    //Debug.LogError("RequireRoomExisting_ItemBase Validating itemBaseID[" + itemBaseID + "] itemTag[" + itemTag + "] minCount[" + minimumCount + "] actualCount[" + i + "]");
                    if (i >= minimumCount) return true;
                    else return false;
                }

                return true;
            }


        }


    }

    public RequireFactionExisting requireFactionExisting = null;
    [System.Serializable]
    public class RequireFactionExisting
    {
        // com open production recipe tab. player only. Fetch player inventory -> party inventory -> settlement inventory, set production order of corresponding inventory
        // furniture allow open this menu, same menu as when open from management menu
        // furniture menu if selected and satisfy, immediately go to production (single)
        // management menu add production order, anyone can satisfy order in production furniture (job)

        // com fulfill production order (isJob), require a existing production order of specified tag (from settlement or from party)

        // NPC assigned by production order tag.

        public bool allowInNonPlayerFaction = true;
        public bool allowInPlayerFaction = true;
        public string jobKeyword = "";
        public string inventoryItemBaseID = "";

        public bool Validate(I_IsJobGiver m)
        {
            var mm = m as Manageable;
            if (jobKeyword != "" && (mm == null || !mm.ExistOngoingProductionOrder(jobKeyword))) return false;

            if (!allowInNonPlayerFaction && !m.isPlayerFaction) return false;
            if (!allowInPlayerFaction && m.isPlayerFaction) return false;

            if (inventoryItemBaseID != "" && (m == null || m.Inventory == null || m.Inventory.GetItemCount(inventoryItemBaseID) < 1))
            {
                //Debug.LogError($"validate inventory for {(m == null ? "null" : m.FactionDisplayName)} error, does not contain item {inventoryItemBaseID} or low count {m.Inventory.GetItemCount(inventoryItemBaseID)}");
                return false;
            }
            return true;
        }

        public bool isValid { get { return jobKeyword != "" || inventoryItemBaseID != ""; } }

    }

    public RequireContaining requireContaining = null;
    [System.Serializable]
    public class RequireContaining
    {
        public bool requireContentAbsent = false;
        public bool requireContentExist = false;
        public List<string> allowPlanting = null;
        public bool requireCanMaintain = false;

        public bool Validate(Job j)
        {
            if (j is Job_Furniture) return Validate(j as Job_Furniture);
            else return true;

        }

        public bool Validate(Job_Furniture targetJob)
        {
            if (targetJob == null) return false;
            if (this.requireContentAbsent && !targetJob.CanContain) return false;
            if (this.requireContentExist && (targetJob.Container == null || !targetJob.Container.HasContent)) return false;
            if (requireCanMaintain && (targetJob.Container == null || !targetJob.Container.RequireMaintenance)) return false;

            return true;
        }

        public bool isValid { get { return requireContentAbsent || requireContentExist || allowPlanting != null; } }
    }
}


