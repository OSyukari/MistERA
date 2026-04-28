using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Newtonsoft.Json;
using UnityEngine;

[System.Serializable]
public class COM_Requirements
{

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

        public bool forbidTeammateJoin = false;

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
        public bool Validate(ref List<string> _tooltip, Character_Trainable doerRefIDs, out bool hardlock, Requirement extraCondition = null)
        {
            //int doercount = (extraCondition == null ? doerCount : (doerCount != -1 ? doerCount : extraCondition.doerCount));
            //int receivercount = (extraCondition == null ? receiverCount : (receiverCount != -1 ? receiverCount : extraCondition.receiverCount));
            /*
            var actorCount = new List<int>();
            actorCount.AddRange(doerRefIDs);
            if (TreatReceiverAsDoer) actorCount.AddRange(receiverRefIDs);
            actorCount.RemoveAll(x=>x < 0);
            */
            hardlock = false;
            bool logging = _tooltip != null && ( !scr_UpdateHandler.current.Updating || scr_System_CentralControl.current.LogPrefs.DLog_JoinAP);
            var actorCount = 1;

            if (doerCount == -1) { }
            else if (doerCount == 0) { }
            else if (doerCount > 0 && actorCount == doerCount) { }
            else if (doerCount > 9) { }
            else
            {
                //if (logging) _tooltip.Add("Command invalid: command doer number below requirement : treatRasD[" + treatReceiverAsDoer + "] dCount[" + 1 + "] rCount[" + 0 + "] finalEval[" + (treatReceiverAsDoer && ((doerRefIDs.Count + receiverRefIDs.Count) <= doerCount)) + "]");
                return false;
            }

            if (receiverCount > 0)
            {
                if (logging) _tooltip.Add(LocalizeDictionary.QueryThenParse("ui_COM_Requirements_requireReceiver"));
                hardlock = true;
                return false;
            }

            // Debug.Log("Command (Variant) Requirement Validating [" + String.Join(",", doerRefIDs) + "] and [" + String.Join(",", receiverRefIDs) + "]");

            if (!CharaReqUtility.Validate(req_Doers, ref _tooltip, doerRefIDs, out hardlock))
            {
                //if (logging) _tooltip.Add("doer failed doer req validation");
                return false;
            }
            if (treatDoerAsReceiver && !CharaReqUtility.Validate(req_Receivers, ref _tooltip, doerRefIDs, out hardlock))
            {
                return false;
            }
            if (receiverCount == 0)
            {
                return CharaReqUtility.Validate(req_Receivers, ref _tooltip, doerRefIDs, out hardlock);
            }
            else
            {
                return true;
            }
        }
        public bool Validate(ref List<string> _tooltip, List<Character_Trainable> doerRefIDs, List<Character_Trainable> receiverRefIDs, out bool hardlock, Requirement extraCondition = null, int actorCountMult = 1)
        {
            //int doercount = (extraCondition == null ? doerCount : (doerCount != -1 ? doerCount : extraCondition.doerCount));
            //int receivercount = (extraCondition == null ? receiverCount : (receiverCount != -1 ? receiverCount : extraCondition.receiverCount));
            hardlock = false;
            var actorSet = new HashSet<int>();
            bool logging = !scr_UpdateHandler.current.Updating;
            foreach (var id in doerRefIDs) { actorSet.Add(id.RefID); }
            if (TreatReceiverAsDoer) foreach (var id in receiverRefIDs) { actorSet.Add(id.RefID); }
            
            int actorCount = actorSet.Count;

            /*
            var actorCount = new List<int>();
            actorCount.AddRange(doerRefIDs);
            if (TreatReceiverAsDoer) actorCount.AddRange(receiverRefIDs);
            actorCount.RemoveAll(x=>x < 0);
            */

            if (doerCount == -1) { }
            else if (doerCount == 0 && doerRefIDs.Count == 0 && (!treatReceiverAsDoer || receiverRefIDs.Count == 0)) { }
            else if (doerCount == 1) 
            {
                if (actorCount == doerCount) { }
                else
                {
                    if (logging) _tooltip.Add(LocalizeDictionary.QueryThenParse("ui_COM_Requirements_doerCountInvalid")
                    .Replace("$count$", $"{actorCount}")
                    .Replace("$req$", $"{doerCount}"));
                    return false;
                }
            }
            else if (doerCount > 1 && doerCount < 9)
            {
                if (actorCountMult < 1 || actorCount <= (doerCount * actorCountMult)) { }
                else
                {
                    if (logging) _tooltip.Add(LocalizeDictionary.QueryThenParse("ui_COM_Requirements_doerCountInvalid")
                    .Replace("$count$", $"{actorCount}")
                    .Replace("$req$", $"{doerCount}*{actorCountMult}"));
                    return false;
                }
            }
            else if (doerCount > 9) { }


            if (receiverCount == 0 && receiverRefIDs.Count != 0)
            {
                if (logging) _tooltip.Add(LocalizeDictionary.QueryThenParse("ui_COM_Requirements_requireNoReceiver"));
                return false;
            }

            if (receiverCount > 0 && receiverRefIDs.Count == 0)
            {
                if (logging) _tooltip.Add(LocalizeDictionary.QueryThenParse("ui_COM_Requirements_requireReceiver"));
                return false;
            }

            if ((receiverCount == 1 && receiverRefIDs.Count > receiverCount) || (actorCountMult > 0 && receiverCount > 1 && receiverRefIDs.Count > receiverCount * actorCountMult))
            {
                if (logging) _tooltip.Add(LocalizeDictionary.QueryThenParse("ui_COM_Requirements_toomanyReceiver")
                                            .Replace("$target$", $"{receiverRefIDs.Count}")
                                            .Replace("$count$",$"{receiverCount}*{actorCountMult}"));
                return false;
            }

            //            Debug.Log("Command (Variant) Requirement Validating [" + String.Join(",", doerRefIDs) + "] and [" + String.Join(",", receiverRefIDs) + "]");

            if (!CharaReqUtility.Validate(req_Doers, ref _tooltip, doerRefIDs, out hardlock))
            {
                //if (logging) _tooltip.Add("doer failed doer req validation");
                return false;
            }
            if (treatDoerAsReceiver && !CharaReqUtility.Validate(req_Receivers, ref _tooltip, doerRefIDs, out hardlock))
            {
                //if (logging) _tooltip.Add("doer failed receiver req validation");
                return false;
            }
            if (receiverCount > 0 && treatReceiverAsDoer && !CharaReqUtility.Validate(req_Doers, ref _tooltip, receiverRefIDs, out hardlock))
            {
               // if (logging) _tooltip.Add("receiver failed doer req validation");
                return false;
            }
            if (receiverCount == 0)
            {
                bool value = CharaReqUtility.Validate(req_Receivers, ref _tooltip, doerRefIDs, out hardlock);
                //if (!value && logging) _tooltip.Add("doer failed receiver == 0 req validation");
                return value;
            }
            else
            {
                bool value = CharaReqUtility.Validate(req_Receivers, ref _tooltip, receiverRefIDs, out hardlock);
                //if (!value && logging) _tooltip.Add("receiver failed receiver req validation");
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

        public bool ValidateCondition(List<string> tooltip, List<Character_Trainable> doerRefIDs, List<Character_Trainable> receiverRefIDs, COM com, COM.COM_Variant variant = null)
        {
            bool logging = tooltip != null && !scr_UpdateHandler.current.Updating;
            int actorRef = doerRefIDs.Count > 0 ? doerRefIDs[0].RefID : (receiverRefIDs.Count > 0 ? receiverRefIDs[0].RefID : -1);

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

            if (jsex == null)
            {
                if (logging) tooltip.Add("Command invalid: missing pre-req job type");
                return false;
            }
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
                if (logging) tooltip.Add(LocalizeDictionary.QueryThenParse("ui_RequireExisting_missingPrereq")+"(1)");
                return false;
            }

            for (int i = existing.Count - 1; i >= 0; i--)
            {
                if (existing[i] == null)
                {
                   // tooltip.Add("remove by 1");
                    existing.RemoveAt(i);
                    continue;
                }

                if (comID != "" && ((existing[i].targetCOM == null || existing[i].targetCOM.ID != comID)))
                {
                   // tooltip.Add("remove by 2");
                    existing.RemoveAt(i);
                    continue;
                }

                //if (existing[i] == null) continue;
                if (!Utility.ListEquals(existing[i].doer, doerRefIDs))
                {
                   // tooltip.Add("remove by 3");
                    existing.RemoveAt(i);
                    continue;
                }
                if (!Utility.ListEquals(existing[i].receiver, receiverRefIDs))
                {
                   // tooltip.Add("remove by 4");
                    existing.RemoveAt(i);
                    continue;
                }
                if (comtgs.Count > 0 && comtgs.Except(existing[i].targetCOM.comTags).Count() != 0)
                {
                   // tooltip.Add("remove by 5");
                    existing.RemoveAt(i);
                    continue;
                }
                if (doerBTag.Count > 0 && doerBTag.Except(existing[i].doerBodyTags).Count() != 0)
                {
                   // tooltip.Add("remove by 6");
                    existing.RemoveAt(i);
                    continue;
                }
                if (receiverBTag.Count > 0 && receiverBTag.Except(existing[i].receiverBodyTags).Count() != 0)
                {
                   // tooltip.Add("remove by 7");
                    existing.RemoveAt(i);
                    continue;
                }
                List<string> s = new List<string>();
                if (overpen && !(existing[i].targetCOM as COM_Sex).ValidateActorLength(ref s, existing[i].DoerRefs, existing[i].ReceiverRefs))
                {
                   // tooltip.Add($"remove by 8, doerRefs {String.Join("|", existing[i].DoerRefs)}, receiveRefs {String.Join("|", existing[i].ReceiverRefs)}");
                    existing.RemoveAt(i);
                    continue;
                }
            }

            if (existing.Count < 1)
            {
                if (logging) tooltip.Add(LocalizeDictionary.QueryThenParse("ui_RequireExisting_missingPrereq")+"(2)");
                return false;
            }
            else return true;

        }

        public bool ValidateCondition(List<string> tooltip, Character_Trainable doerRefIDs, COM com, COM.COM_Variant variant = null)
        {
            bool logging = tooltip != null && !scr_UpdateHandler.current.Updating;

            int actorRef = doerRefIDs.RefID;

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

            if (jsex == null)
            {
                if (logging) tooltip.Add("Command invalid: missing pre-req job type");
                return false;
            }

            // from here on, we know its sex job
            List<ActionPackage_Sex> existing = new List<ActionPackage_Sex>();
            foreach (var package in jsex.CurrentPackages)
            {
                if (package is ActionPackage_Sex)
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
                if (logging) tooltip.Add(LocalizeDictionary.QueryThenParse("ui_RequireExisting_missingPrereq") + "(1)");
                return false;
            }

            for (int i = existing.Count - 1; i >= 0; i--)
            {
                if (existing[i] == null)
                {
                    //tooltip.Add("remove by 1");
                    existing.RemoveAt(i);
                    continue;
                }

                if (comID != "" && ((existing[i].targetCOM == null || existing[i].targetCOM.ID != comID)))
                {
                    //tooltip.Add("remove by 2");
                    existing.RemoveAt(i);
                    continue;
                }

                //if (existing[i] == null) continue;
                if (existing[i].doer.Count != 1 || existing[i].doer[0] != doerRefIDs)
                {
                   // tooltip.Add("remove by 3");
                    existing.RemoveAt(i);
                    continue;
                }
                if (comtgs.Count > 0 && comtgs.Except(existing[i].targetCOM.comTags).Count() != 0)
                {
                   // tooltip.Add("remove by 5");
                    existing.RemoveAt(i);
                    continue;
                }
                if (doerBTag.Count > 0 && doerBTag.Except(existing[i].doerBodyTags).Count() != 0)
                {
                   // tooltip.Add("remove by 6");
                    existing.RemoveAt(i);
                    continue;
                }
                if (receiverBTag.Count > 0 && receiverBTag.Except(existing[i].receiverBodyTags).Count() != 0)
                {
                  //  tooltip.Add("remove by 7");
                    existing.RemoveAt(i);
                    continue;
                }
                List<string> s = new List<string>();
                if (overpen && !(existing[i].targetCOM as COM_Sex).ValidateActorLength(ref s, existing[i].DoerRefs, existing[i].ReceiverRefs))
                {
                   // tooltip.Add($"remove by 8, doerRefs {String.Join("|", existing[i].DoerRefs)}, receiveRefs {String.Join("|", existing[i].ReceiverRefs)}");
                    existing.RemoveAt(i);
                    continue;
                }
            }

            if (existing.Count < 1)
            {
                if (logging) tooltip.Add(LocalizeDictionary.QueryThenParse("ui_RequireExisting_missingPrereq") + "(2)");
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

        public bool requireHasRecording = false;
        public bool requireNotRecording = false;


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
            this.requireHasRecording = this.requireHasRecording || req.requireHasRecording;
            this.requireNotRecording = this.requireNotRecording || req.requireNotRecording;
        }

        public bool Validate(Room_Instance targetRoom, out string tooltip)
        {
            if (this.requiresFurniture != null) foreach (var req in requiresFurniture) if (!req.Validate(targetRoom, out tooltip)) return false;
            if (this.requiresItem != null) foreach (var req in requiresItem) if (!req.Validate(targetRoom, out tooltip)) return false;
            if (requireHasRecording && !targetRoom.HasRecording)
            {
                tooltip = "require active recording in room";
                return false;
            }
            if (requireNotRecording && targetRoom.HasRecording)
            {
                tooltip = "require no active recording in room";
                return false;
            }
            tooltip = "";
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
            public bool Validate(Room_Instance targetRoom, out string tooltip)
            {
                tooltip = "";
                if (minimumCount == 0) return true;
                if (furnitureBaseID == "") return true;

                int i = 0;
                foreach (FurnitureInstance fi in targetRoom.Furnitures)
                {
                    if (fi.FurnitureBase.ID == furnitureBaseID) i++;
                }
                if( i >= minimumCount) return true;
                else
                {
                    var furn = scr_System_Serializer.current.MasterList.Furnitures.GetByID(furnitureBaseID);
                    var name = furn == null ? furnitureBaseID : furn.DisplayName;
                    tooltip = LocalizeDictionary.QueryThenParse("ui_ap_PreEvaluate_RequireRoomExisting_itemBaseID")
                            .Replace("$name$", name)
                            .Replace("$requirement$", $"{i}")
                            .Replace("$count$", $"{minimumCount}");
                    return false;
                }
            }

        }

        [System.Serializable]
        public class RequireRoomExisting_ItemBase
        {
            public string itemBaseID = "";
            public string itemTag = "";
            public int minimumCount = 0;

            public bool Validate(Room_Instance targetRoom, out string tooltip)
            {
                tooltip = "";
                if (minimumCount == 0) return true;
                if (itemBaseID == "" && itemTag == "") return true;

                if (itemBaseID != "")
                {
                    int i = targetRoom.HasItem_BaseID_Count(itemBaseID);
                    //Debug.LogError("RequireRoomExisting_ItemBase Validating itemBaseID[" + itemBaseID + "] itemTag[" + itemTag + "] minCount[" + minimumCount + "] actualCount["+i+"]");
                    if (i < minimumCount) 
                    {
                        var item = Masterlist_Items.GetByID(itemBaseID);
                        var name = item == null ? itemBaseID : item.DisplayName;
                        tooltip = LocalizeDictionary.QueryThenParse("ui_ap_PreEvaluate_RequireRoomExisting_itemBaseID")
                                .Replace("$name$", name)
                                .Replace("$requirement$", $"{minimumCount}")
                                .Replace("$count$", $"{i}");
                        return false;
                    }
                }

                if (itemTag != "")
                {
                    int i = targetRoom.HasItem_Tag_Count(itemTag);
                    //Debug.LogError("RequireRoomExisting_ItemBase Validating itemBaseID[" + itemBaseID + "] itemTag[" + itemTag + "] minCount[" + minimumCount + "] actualCount[" + i + "]");
                    if (i < minimumCount) 
                    {
                        tooltip = LocalizeDictionary.QueryThenParse("ui_ap_PreEvaluate_RequireRoomExisting_itemTag")
                            .Replace("$tags$", itemTag)
                            .Replace("$requirement$", $"{minimumCount}")
                            .Replace("$count$", $"{i}");
                        return false;
                    }
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
        public bool requireCanPrepMeal = false;
        public bool Validate(I_IsJobGiver m, out string tooltip)
        {
            tooltip = "";
            var mm = m as Manageable;
            if (jobKeyword != "" && (mm == null || !mm.ExistOngoingProductionOrder(jobKeyword)))
            {
                tooltip = LocalizeDictionary.QueryThenParse("ui_RequireFactionExisting_jobKeyword")
                        .Replace("$faction$", mm == null ? "-" : mm.FactionDisplayName)
                        .Replace("$keywords$", jobKeyword);
                return false;
            }
            if (!allowInNonPlayerFaction && !m.isPlayerFaction)
            {
                tooltip = LocalizeDictionary.QueryThenParse("ui_RequireFactionExisting_disallowInNonPlayerFaction")
                        .Replace("$faction$", mm == null ? "-" : mm.FactionDisplayName);
                return false;
            }
            if (!allowInPlayerFaction && m.isPlayerFaction)
            {
                tooltip = LocalizeDictionary.QueryThenParse("ui_RequireFactionExisting_disallowInPlayerFaction")
                        .Replace("$faction$", mm == null ? "-" : mm.FactionDisplayName);
                return false;
            }

            if (inventoryItemBaseID != "" && (m == null || m.Inventory == null || m.Inventory.GetItemCount(inventoryItemBaseID) < 1))
            {
                var name = scr_System_Serializer.current.index_Item_Base.GetByID(inventoryItemBaseID);
                var replace = name == null ? inventoryItemBaseID : name.DisplayName;
                tooltip = LocalizeDictionary.QueryThenParse("ui_RequireFactionExisting_inventoryItemBaseID")
                        .Replace("$faction$", mm == null ? "-" : mm.FactionDisplayName)
                        .Replace("$name$", replace);
                return false;
            }
            if (requireCanPrepMeal)
            {
                var nextHour = Math.Clamp(scr_System_Time.current.getCurrentTime().Hour + 1, 0, 23);
                if (!m.isPlayerFaction)
                {
                    tooltip = "non player faction, can always prep";
                }
                else if (m.isMealHourAt(nextHour))
                {
                    tooltip = "next hour is already meal hour";
                    return false;
                }
                else
                {
                    bool existFood = false;
                    foreach (var item in m.Inventory.Contents)
                    {
                        if (item.isFoodConsumable)
                        {
                            existFood = true;
                            break;
                        }
                    }
                    if (!existFood)
                    {
                        tooltip = "no consumable item in faction inventory";
                        return false;
                    }
                }
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

        public bool Validate(Job j, out string tooltip)
        {
            tooltip = "";
            if (j is Job_Furniture) return Validate(j as Job_Furniture, out tooltip);
            else return true;

        }

        public bool Validate(Job_Furniture targetJob, out string tooltip)
        {
            tooltip = "";
            if (targetJob == null)
            {
                tooltip = "source job null";
                return false;
            }
            if (this.requireContentAbsent && !targetJob.CanContain)
            {
                tooltip = LocalizeDictionary.QueryThenParse("ui_RequireContaining_requireContentAbsent")
                    .Replace("$contents$", targetJob.Container == null ? "-" : targetJob.Container.DisplayName);
                return false;
            }
            if (this.requireContentExist && (targetJob.Container == null || !targetJob.Container.HasContent))
            {
                tooltip = LocalizeDictionary.QueryThenParse("ui_RequireContaining_requireContentExist")
                    .Replace("$contents$", targetJob.Container == null ? "-" : targetJob.Container.DisplayName);
                return false;
            }
            if (requireCanMaintain && (targetJob.Container == null || !targetJob.Container.RequireMaintenance))
            {
                tooltip = LocalizeDictionary.QueryThenParse("ui_RequireContaining_requireCanMaintain")
                    .Replace("$contents$", targetJob.Container == null ? "-" : targetJob.Container.DisplayName);
                return false;
            }

            return true;
        }

        public bool isValid { get { return requireContentAbsent || requireContentExist || allowPlanting != null; } }
    }
}


