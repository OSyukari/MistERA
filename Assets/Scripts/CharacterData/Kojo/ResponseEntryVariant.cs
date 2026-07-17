using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UnityEngine;

public partial class ResponseEntry
{

    public enum ResponseDisplayType
    {
        Hidden,// no display
        AlwaysDisplay,// display regardless of location, unimplemented
        DisplayWhenVisible, // as long as visible, display
        DisplayWhenRelated // if target is player or job include player
    }

    public class Variant
    {
        public bool requireCOMStrongVariant = false;
        public ResponseDisplayType displayType = ResponseDisplayType.DisplayWhenRelated;
        public bool requireSelfDoer = false;
        public bool requireSelfReceiver = false;

        public bool requireSelfOnly = false;

       // public bool requireSelfAPDoer = false;
       // public bool requireSelfAPReceiver = false;


        public bool requireMaster = false;
        /// <summary>
        /// Valid when self is master or when no master
        /// </summary>
        public bool requireSelfMaster = false;
        /// <summary>
        /// Valid when self is not master or when no master
        /// </summary>
        public bool requireSelfNotMaster = false;

        public List<string> tags = new List<string>();
        public List<string> extraPortraitTags = new List<string>();
        public bool useActiveTags = false;
        public bool forbidPortaitDisplay = false;
        public Requirement requirement = new Requirement();
        public List<string> responses = new List<string>();
        public List<Results> results = new List<Results>();
        public bool keepLooking = false;
        public string selfEventCall = "";
        public bool hideWhenNotFocused = true;  // Do not display message if Owner is not CurrentTarget (not focused) or Target is not Player nor CurrentTarget (not focused)

        public List<Variant> variants = new List<Variant>();


        public void ValidateIntegrity()
        {
            foreach (var r in this.responses)
            {
                if (r.Length < 1) continue;
                var ss = r.Split(' ');
                if (ss.Length > 1) continue;
                var s2 = r.Split('_');
                if (s2.Length < 2) continue;
                var s3 = r.Split('$');
                if (s3.Length > 1) continue;
                if (LocalizeDictionary.QueryThenParse(r, "error") == "error")
                {
                    Debug.LogError($"Error kojo missing translation for {r}");
                }
            }
            foreach (var v in this.variants)
            {
                v.ValidateIntegrity();
            }
        }
        public bool GetRandomResponse(out MessageCollect_KojoEntry response, KojoCollector kol, bool skipExecute = false)
        {
            // first apply cost
            if (!isValid(kol))
            {
                response = null;
                return false;
            }

            MessageCollect_KojoEntry returnV = new MessageCollect_KojoEntry(-1);

            if (this.variants.Count > 0)
            {
                foreach (var variant in variants)
                {
                    if (variant.GetRandomResponse(out response, kol, skipExecute))
                    {
                        if (!variant.keepLooking) return true;
                        returnV.Merge(response);
                    }
                }
            }


            MessageCollect_KojoEntry returnV2 = null;

            string result = "";
            if (responses.Count > 0)
            {
                result = LocalizeDictionary.QueryThenParse(Utility.GetRandomElement(responses));
                result = result.Replace("$self$", kol.Owner.FirstName).Replace("$target$", kol.Target == null ? "null" : kol.Target.FirstName);
            }

            MessageCollect_KojoEntry sss = null;
            if (selfEventCall != "" && kol.Owner != null) 
            {
                var newmessage = kol.Copy();
                newmessage.eventID = selfEventCall;

                sss = newmessage.Owner.Relationships.Personality.GetKOJOMessage(newmessage);
                Debug.Log($"SelfEventCall on kol {newmessage.eventID}{newmessage.suffix}, result {(sss == null ? "null" : sss.message)}");
            }

            var target = scr_System_CampaignManager.current.CurrentTarget;
            var player = scr_System_CampaignManager.current.Player;
            if (hideWhenNotFocused && !kol.isPlayerInvolved &&
                kol.Owner != target && kol.Target != target && kol.Owner != player && kol.Target != player && !scr_UpdateHandler.current.isLastUpdate())
            {
                if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log(kol.Owner.FirstName + " kojo text skipped, no display");
            }
            else
            {
                if (result != "" && result.Replace("\n", "").Length > 0)
                {
                    returnV2 = new MessageCollect_KojoEntry(forbidPortaitDisplay ? -1 : kol.Owner.RefID);
                    if (!forbidPortaitDisplay)
                    {
                        returnV2.portraitTags.AddRange(extraPortraitTags);
                        returnV2.portraitTags.AddRange(kol.SelfTags);
                        returnV2.portraitTags.AddRange(kol.targetTags);
                        if (useActiveTags) returnV2.portraitTags.AddRange(kol.Owner.PortraitManager.GetOwnerActionTagsByPriority());
                        returnV2.portraitTags = Utility.Distinct(returnV2.portraitTags);
                    }
                    returnV2.message = result;

                }
                if (sss != null)
                {
                    if (returnV2 == null) returnV2 = sss;
                    else returnV2.nexts.Add(sss);
                }
            }
            if (!skipExecute) foreach (var i in results) i.Execute(returnV2, kol);

            returnV.Merge(returnV2);
            response = returnV;

            return true;
        }
        public bool GetRandomResponse(out MessageCollect_KojoEntry response, Character_Relationship rel, EvaluationPackage ep, List<string> selfTags, List<string> targetTags, bool isPlayerInvolved = false, bool skipExecute = false)
        {
            // first apply cost
            if (!isValid(rel, selfTags, targetTags, ep))
            {
                response = null;
                return false;
            }

            MessageCollect_KojoEntry returnV = new MessageCollect_KojoEntry(-1);

            if (this.variants.Count > 0)
            {
                foreach (var variant in variants)
                {
                    if (variant.GetRandomResponse(out response, rel, ep, selfTags, targetTags, isPlayerInvolved, skipExecute))
                    {
                        if (!variant.keepLooking) return true;
                        returnV.Merge(response);
                    }
                }
            }


            MessageCollect_KojoEntry returnV2 = null;

            string result = "";
            if (responses.Count > 0)
            {
                result = LocalizeDictionary.QueryThenParse(Utility.GetRandomElement(responses));
                result = result.Replace("$self$", rel.Owner.FirstName).Replace("$target$", rel.Target.FirstName);
            }

            MessageCollect_KojoEntry sss = null;
            if (selfEventCall != "")
            {
                Debug.Log($"SelfEventCall on {selfEventCall}");
                sss = rel.Owner.Relationships.Personality.GetKOJOMessage(selfEventCall, rel, selfTags, targetTags);

            }
            var target = scr_System_CampaignManager.current.CurrentTarget;
            var player = scr_System_CampaignManager.current.Player;
            if (hideWhenNotFocused && !isPlayerInvolved &&
                rel.Owner != target && rel.Target != target && rel.Owner != player && rel.Target != player)
            {
                if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log(rel.Owner.FirstName + " kojo text skipped, no display");
            }
            else
            {
                if (result != "" && result.Replace("\n", "").Length > 0)
                {
                    returnV2 = new MessageCollect_KojoEntry(forbidPortaitDisplay ? -1 : rel.Owner.RefID);
                    if (!forbidPortaitDisplay)
                    {
                        returnV2.portraitTags.AddRange(extraPortraitTags);
                        returnV2.portraitTags.AddRange(selfTags);
                        returnV2.portraitTags.AddRange(targetTags);
                        if (useActiveTags) returnV2.portraitTags.AddRange(rel.Owner.PortraitManager.GetOwnerActionTagsByPriority());
                        returnV2.portraitTags = Utility.Distinct(returnV2.portraitTags);
                    }
                    returnV2.message = result;

                }
                if (sss != null)
                {
                    if (returnV2 == null) returnV2 = sss;
                    else returnV2.nexts.Add(sss);
                }
            }
            if (!skipExecute) foreach (var i in results) i.Execute(returnV2, rel, selfTags, targetTags);

            returnV.Merge(returnV2);
            response = returnV;

            return true;
        }

        protected bool isValid(KojoCollector kol)
        {
            if (requireSelfOnly && kol.targetRef != -1 && kol.targetRef != kol.selfRef) return false;
            if (requireCOMStrongVariant && (!kol.hasPackageData || !kol.isStrongP)) return false;
            if (requireSelfDoer && kol.doerRef != kol.selfRef) return false;
            if (requireSelfReceiver)
            {
                if (kol.doerRef == kol.selfRef && kol.receiverRef == -1) { }
                else if (kol.receiverRef == kol.selfRef) { }
                else return false;
            }
            /*
            if (requireSelfAPDoer)
            {
                if (!kol.hasPackageData) return false;
                if (kol.doerRef != kol.selfRef) return false;
            }
            if (requireSelfAPReceiver)
            {
                if (!kol.hasPackageData) return false;
                if (kol.doerRef == kol.selfRef && kol.receiverRef == -1) { }
                else if (kol.receiverRef == kol.selfRef) { }
                else return false;
            }*/
            if (requireMaster && kol.masterRef == -1) return false;
            if (requireSelfMaster && kol.masterRef != kol.selfRef) return false;
            if (requireSelfNotMaster && kol.masterRef == kol.selfRef) return false;

            List<string> ttips = new List<string>();

            if (!requirement.Validate(kol.Owner, kol.Target, kol.SelfTags, kol.targetTags, kol, kol.Relation, out var hadlock)) return false;

            return true;
        }


        protected bool isValid(Character_Relationship rel, List<string> selfTags, List<string> targetTags, EvaluationPackage ep)
        {
            if (rel != null)
            {
                if (requireSelfOnly && rel.Owner != rel.Target) return false;
            }

            if (ep != null && rel != null)
            {
                if (requireSelfOnly && (ep.job.Actors.Count != 1 || ep.job.Actors[0] != rel.Owner)) return false;
                if (requireCOMStrongVariant && !ep.isStrongP) return false;
                if (requireSelfDoer && ep.Doer != rel.Owner) return false;
                else if (requireSelfReceiver)
                {
                    if (ep.Receiver == null && ep.Doer != rel.Owner) return false;
                    else if (ep.Receiver != null && ep.Receiver != rel.Owner) return false;
                }

               // if (requireSelfAPDoer && !ep.Package.doer.Contains(rel.Owner)) return false;
              //  if (requireSelfAPReceiver && !ep.Package.receiver.Contains(rel.Owner)) return false;

                if (requireMaster && ep.Package.Master == null) return false;

                if (requireSelfMaster && ep.Package.Master != null && ep.Package.Master != rel.Owner) return false;
                if (requireSelfNotMaster && ep.Package.Master != null && ep.Package.Master == rel.Owner) return false;
            }
            else if (ep == null)
            {

            }
            List<string> ttips = new List<string>();

            if (!requirement.Validate(rel.Owner, rel.Target, selfTags, targetTags, ep, rel, out var hadlock)) return false;
            //if (!requirement.Validate(rel, selfTags, targetTags, ep))

            return true;
        }

        public class Requirement
        {
            public string targetBaseID = "";
            public bool requireSelfAction = true;
            public bool requireTargetAction = true;
            public bool requireEPSuccess = true;
            /// <summary>
            /// requireEPFailure takes priority over requireEPSuccess, and both conditions are mutually exclusive.
            /// if require failure, then success is ignored
            /// require success only valid when not requiring failure
            /// </summary>
            public bool requireEPFailure = false;
            public Memory_Response requireEPSuccessGTE = Memory_Response.None;
            public Memory_Attitude requireSelfAttitudeGTE = Memory_Attitude.None;
            public AP_Status requireAPStatus = AP_Status.none;
            public bool requirePermission = false;
            public string requireInCooldownID = "";
            public string requireNotInCooldownID = "";
            public List<string> selfTags = new List<string>();
            public List<string> excludeSelfTags = new List<string>();
            public List<string> targetTags = new List<string>();
            public List<string> excludeTargetTags = new List<string>();
            public bool requireSelfForced = false;
            public int variantID = -1;
            public CharaReq selfReq = null;
            public bool allowRecording = false;
            public CharaReq targetReq = null;
            public string requireSelfAttitudeKey = "";
            public RequireKojoVariable requireKojoVariable = new RequireKojoVariable();
            public RequireStatusValue requireSelfStatusValue = new RequireStatusValue();
            public List<RequireStatValue> requireSelfStatValue = new List<RequireStatValue>();
            public RequireMemory requireSelfMemory = new RequireMemory();

            public bool Validate(Character_Trainable self, Character_Trainable target, List<string> selfTags, List<string> targetTags, I_ResultStorage ep, Character_Relationship rel, out bool hardlock)
            {

                hardlock = false;
                //if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents_validations) Debug.Log("Validating kojo req [" + (self == null ? "null" : self.FirstName) + "->" + (target == null ? "null" : target.FirstName) + $"], self[{String.Join("|", selfTags)}" + "] target[" + String.Join("|", targetTags) + "]");

                List<string> tooltips = new List<string>();

                if (selfReq != null && !CharaReqUtility.Validate(selfReq, ref tooltips, self, out hardlock))
                {
                    if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents_validations) Debug.Log($"validation failed, CharaReqUtility.Validate selfReq failure");
                    return false;
                }
                if (targetReq != null && !CharaReqUtility.Validate(targetReq, ref tooltips, target, out hardlock))
                {
                    if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents_validations) Debug.Log($"validation failed, CharaReqUtility.Validate targetReq failure");
                    return false;
                }
                if (requireSelfAttitudeKey != "")
                {
                    if (rel == null)
                    {
                        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents_validations) Debug.Log($"validation failed, requireSelfAttitudeKey {requireSelfAttitudeKey}, rel null");
                        return false;
                    }
                    var att = rel.GetCurrentAttitude();
                    if (att == null || !att.tags.Contains(requireSelfAttitudeKey))
                    {
                        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents_validations) Debug.Log($"validation failed, requireSelfAttitudeKey {requireSelfAttitudeKey}, att {(att == null ? "null" : $"tags [{String.Join(" ", att.tags)}] not contain key")}");
                        return false;
                    }
                }
                if (requireInCooldownID != "" && !self.Relationships.BehaviorInCooldown(requireInCooldownID))
                {
                    if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents_validations) Debug.Log($"validation failed, requireInCooldownID {requireInCooldownID}");
                    return false;
                }
                if (requireNotInCooldownID != "" && self.Relationships.BehaviorInCooldown(requireNotInCooldownID))
                {
                    if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents_validations) Debug.Log($"validation failed, requireNotInCooldownID {requireNotInCooldownID}");
                    return false;
                }


                if (ep != null)
                {
                    if (!allowRecording && ep.isRecording)
                    {
                        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents_validations) Debug.Log($"validation failed, allowRecording {allowRecording} ep isrecording? {ep.isRecording}");
                        return false;
                    }
                    //if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents_validations) Debug.Log($"validating EP reqFailure? {requireEPFailure && ep.Response > Memory_Response.Refuse} reqSuccess? {!requireEPFailure && requireEPSuccess && ep.Response < Memory_Response.Accept} ");
                    if (requireEPFailure && (ep.requestAccepted || ep.Response != Memory_Response.Refuse))
                    {
                        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents_validations) Debug.Log($"validation failed, requireEPFailure, accepted? {ep.requestAccepted}, not refuse? {ep.Response != Memory_Response.Refuse}");
                        return false;
                    }
                    else if (!requireEPFailure && requireEPSuccess && (!ep.requestAccepted || ep.Response < Memory_Response.Accept))
                    {
                        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents_validations) Debug.Log($"validation failed, requireEPSuccess, refused? {!ep.requestAccepted}, refuse? {ep.Response < Memory_Response.Accept}");
                        return false;
                    }
                    if (requirePermission && !ep.HasPermission)
                    {
                        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents_validations) Debug.Log($"validation failed, requirePermission, hasnopermission? {!ep.HasPermission}");
                        return false;
                    }

                    if (requireEPSuccessGTE != Memory_Response.None && ep.Response < requireEPSuccessGTE)
                    {
                        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents_validations) Debug.Log($"validation failed, requireEPSuccessGTE {requireEPSuccessGTE}, ep {ep.Response}, {ep.Response < requireEPSuccessGTE}");
                        return false;
                    }
                    if (requireSelfAttitudeGTE != Memory_Attitude.None && ep.GetActorAttitude(self.RefID) < requireSelfAttitudeGTE)
                    {
                        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents_validations) Debug.Log($"validation failed, requireSelfAttitudeGTE {requireSelfAttitudeGTE}, ep {ep.GetActorAttitude(self.RefID)} {ep.GetActorAttitude(self.RefID) < requireSelfAttitudeGTE}");
                        return false;
                    }
                    if (requireAPStatus != AP_Status.none && ep.APStatus != requireAPStatus)
                    {
                        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents_validations) Debug.Log($"validation failed, requireAPStatus {requireAPStatus}, ep {ep.APStatus}");
                        return false;
                    }

                }
                if (this.targetBaseID != "")
                {
                    if (target == null)
                    {
                        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents_validations) Debug.Log($"validation failed, require baseID {targetBaseID}, null target");
                        return false;
                    }
                    else if (targetBaseID == "PLAYER" && target != scr_System_CampaignManager.current.Player)
                    {
                        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents_validations) Debug.Log($"validation failed, require baseID {targetBaseID}, target not player");
                        return false;
                    }
                    else if (targetBaseID != "PLAYER" && target.BaseID != this.targetBaseID)
                    {
                        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents_validations) Debug.Log($"validation failed, require baseID {targetBaseID}, targetID {target.BaseID}");
                        return false;
                    }
                }
                //if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents_validations) Debug.LogError($"validating requireSelfAction {requireSelfAction} {self.canAct} {self.Stats.isConsciousnessUnconscious}");
                if (requireSelfAction && (self == null || !self.canAct))
                {
                    if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents_validations) Debug.Log($"validation failed, requireSelfAction, self {(self == null ? "null" : $"{self.FirstName} cannotAct? {!self.canAct}")}");
                    return false;
                }
                if (requireTargetAction && target != null && !target.canAct)
                {
                    if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents_validations) Debug.Log($"validation failed, requireTargetAction, target {(target == null ? "null" : $"{target.FirstName} cannotAct? {!target.canAct}")}");
                    return false;
                }
                if (requireSelfForced)
                {
                    //Debug.Log($"requireSelfForced on {rel.Owner.CallName}");
                    if (self.cannotRefuse || self.isImprisoned)
                    {

                    }
                    else if (ep != null && ep.isForced)
                    {

                    }
                    else
                    {
                        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents_validations) Debug.Log($"validation failed, requireSelfForced, self cannotrefuse? {self.cannotRefuse}, imprisoned? {self.isImprisoned}, ep {(ep == null ? "null" :  "isforced? {ep.isForced}")}");
                        return false;
                    }
                }
                //if (selfTags.Count < this.selfTags.Count) return false;
                if (this.selfTags.Count > 0 && !Utility.ListContainsStrict(selfTags, this.selfTags)) return false;
                //if (targetTags.Count < this.targetTags.Count) return false;
                if (this.excludeSelfTags.Count > 0 && Utility.ListContainsLoose(selfTags, this.excludeSelfTags)) return false;
                if (this.targetTags.Count > 0 && !Utility.ListContainsStrict(targetTags, this.targetTags)) return false;
                if (this.excludeTargetTags.Count > 0 && Utility.ListContainsLoose(targetTags, this.excludeTargetTags)) return false;
                if (this.variantID > -1 && (ep == null || ep.VariantID != this.variantID)) return false;
                if (this.requireKojoVariable != null && this.requireKojoVariable.isValid && rel != null && !this.requireKojoVariable.Validate(rel)) return false;
                if (this.requireSelfStatValue.Count > 0)
                {
                    foreach (var i in this.requireSelfStatValue)
                    {
                        //Debug.Log($"{self.FirstName} RequireStatValue isvalid? {i.isValid} {i.statID} {i.operand} {i.value} == {self.CompareStatValue(i.statID, i.operand, i.value)}");
                        if (!i.isValid) continue;
                        if (!i.Validate(self)) return false;
                    }
                }
                if (this.requireSelfStatusValue != null && this.requireSelfStatusValue.isValid && rel != null && !this.requireSelfStatusValue.Validate(rel.Owner)) return false;
                if (this.requireSelfMemory != null && this.requireSelfMemory.isValid && rel != null && !this.requireSelfMemory.Validate(rel.Owner)) return false;
                return true;

            }

            public class RequireMemory
            {
                public int minuteRollback = 0;
                public List<string> selfTags = new List<string>();
                public List<string> targetTags = new List<string>();
                [JsonIgnore] public bool isValid { get { return minuteRollback > 0 && (selfTags.Count > 0 || targetTags.Count > 0); } }
                public bool Validate(Character_Trainable chara)
                {
                    List<Memory_Entry> list = chara.Memory.GetAllMemoryMatch(selfTags, targetTags, minuteRollback);
                    return list.Count > 0;
                }

            }

            public class RequireStatValue
            {
                public string statID = "";
                public LogicalOperand operand = LogicalOperand.none;
                public string value = "";
                [JsonIgnore] public bool isValid { get { return this.statID != "" && operand != LogicalOperand.none && value != ""; } }
                public bool Validate(Character_Trainable chara)
                {
                    if (chara == null) return false;
                    //Debug.Log($"{chara.FirstName} RequireStatValue {statID} {operand} {value} == {chara.CompareStatValue(statID, operand, value)}");
                    return chara.CompareStatValue(statID, operand, value);
                }
            }

            public class RequireStatusValue
            {
                public string statusID = "";
                public bool checkExistOnly = false;
                public bool checkSeverityIndex = false;
                public LogicalOperand operand = LogicalOperand.none;
                public float value = 0;
                [JsonIgnore]
                public bool isValid
                {
                    get
                    {
                        if (this.statusID == "") return false;
                        if (!checkExistOnly && operand == LogicalOperand.none) return false;
                        if (checkSeverityIndex && value < 0) return false;
                        return true;
                    }
                }
                public bool Validate(Character_Trainable chara)
                {
                    var status = chara.Stats.GetStatusByStringMatch(statusID);
                    if (checkExistOnly) return status != null;
                    if (status == null) return false;
                    if (checkSeverityIndex) return Utility.CompareValue(status.SeverityIndex, operand, value);
                    else return Utility.CompareValue(status.Severity, operand, value);
                }
            }

            public class RequireKojoVariable
            {
                public bool isDailyVariable = false;
                public string variableID = "";
                public bool checkExistOnly = false;
                public LogicalOperand operand = LogicalOperand.none;
                public int value = 0;

                [JsonIgnore] public bool isValid { get { return this.variableID != "" && (checkExistOnly || operand != LogicalOperand.none); } }
                public bool Validate(Character_Relationship rel)
                {
                    if (checkExistOnly) return (rel.Owner.Relationships.GetKojoVariableExist(isDailyVariable, rel, variableID) == (value != 0));
                    else return Utility.CompareValue(rel.Owner.Relationships.GetKojoVariable(isDailyVariable, rel, variableID), operand, value);

                }
            }
        }

        public class Results
        {   // mainly use to manipulate kojo variables

            public ModKojoVariable modifyKojoVariables = new ModKojoVariable();
            public EventInitializer launchEvent = new EventInitializer();
            public Result_Character.ModStatusValue modifyStatusValue = new Result_Character.ModStatusValue();
            public string addCooldownID = "";
            public int addCooldownDuration = 0;
            public bool flushLog = false;
            public void Execute(MessageCollect_KojoEntry message, KojoCollector rel)
            {
                if (flushLog)
                {
                    scr_System_CampaignManager.current.AddLog(message);
                }
                if (addCooldownID != "" && addCooldownDuration > 0 && rel != null && rel.Owner != null)
                {
                    rel.Owner.Relationships.BehaviorCooldown(addCooldownID, 0, addCooldownDuration);
                }
                if (modifyKojoVariables != null && modifyKojoVariables.isValid) modifyKojoVariables.Execute(rel.Relation);
                if (this.launchEvent != null && this.launchEvent.isValid)
                {
                    launchEvent.Execute(rel.Relation);
                }
                if (modifyStatusValue != null && modifyStatusValue.isValid) modifyStatusValue.Execute(rel.Owner, null);
            }
            public void Execute(MessageCollect_KojoEntry message, Character_Relationship rel, List<string> selfTags, List<string> targetTags)
            {
                if (flushLog)
                {
                    scr_System_CampaignManager.current.AddLog(message);
                }
                if (modifyKojoVariables != null && modifyKojoVariables.isValid) modifyKojoVariables.Execute(rel);
                if (this.launchEvent != null && this.launchEvent.isValid)
                {
                    launchEvent.Execute(rel);
                }
                if (modifyStatusValue != null && modifyStatusValue.isValid) modifyStatusValue.Execute(rel.Owner, null);
            }


            public class EventInitializer
            {
                public string eventID = "";
                public string eventLabel = "";
                public bool reverseTargets = false;
                public string targetKeyword = "";
                public bool startImmediate = false;
                [JsonIgnore] public bool isValid { get { return this.eventID != ""; } }

                public void Execute(Character_Relationship rel)
                {
                    //Debug.LogError($"Startevent execute {reverseTargets} {rel.Owner.FirstName} {rel.Target.FirstName} {eventID} {eventLabel}");
                    EventInstance newEvent = null;
                    if (reverseTargets)
                    {
                        newEvent = new EventInstance(rel.Target, eventID, eventLabel);
                        if (targetKeyword != "") newEvent.Targets.Add(targetKeyword, new List<Character_Trainable>() { rel.Owner });
                    }
                    else
                    {
                        newEvent = new EventInstance(rel.Owner, eventID, eventLabel);
                        if (targetKeyword != "") newEvent.Targets.Add(targetKeyword, new List<Character_Trainable>() { rel.Target });
                    }

                    if (newEvent != null)
                    {
                        if (startImmediate) scr_UpdateHandler.current.EventHandler.StartEvent(newEvent, false);
                        else scr_UpdateHandler.current.AddEventCallback(() => scr_UpdateHandler.current.EventHandler.StartEvent(newEvent, false));
                    }
                }
            }

            [System.Serializable]
            public class ModKojoVariable
            {
                public bool isDailyVariable = false;
                public string variableID = "";
                public bool isSetValue = false;
                public int value = 0;

                [JsonIgnore] public bool isValid { get { return this.variableID != ""; } }
                public void Execute(Character_Relationship rel)
                {
                    if (isSetValue) rel.Owner.Relationships.SetKojoVariable(isDailyVariable, rel, variableID, value);
                    else rel.Owner.Relationships.ModKojoVariable(isDailyVariable, rel, variableID, value);
                }
            }
        }
    }
}