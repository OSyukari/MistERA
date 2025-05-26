using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using static UnityEngine.GraphicsBuffer;
using UnityEngine.SocialPlatforms;
using System;
using System.Linq;
using static Character_Personality.ResponseEntry.Variant;

[System.Serializable]
public class Character_Personality_Index : I_IndexHasID, I_IndexMergeable, I_NeedLateInitialize
{
    public List<Character_Personality> list = new List<Character_Personality>();

    Dictionary<string, Character_Personality> ID_Dictionary = new Dictionary<string, Character_Personality>();
    public void RegisterAllID()
    {
        Debug.Log("Character_Personality_Index : registering ID with list length [" + list.Count+ "]") ;

        foreach (Character_Personality o in this.list)
        {
            //Debug.Log("Character_Origin_Index : registering origin ["+o.ID+"] ");
            ID_Dictionary.Add(o.ID, o);
        }
    }
    
    public void MergeWith(I_IndexMergeable list){
        var l = list as Character_Personality_Index;
        if (l == null) return;
        else if (l.list == null) return;
        else
        {
            this.list.AddRange(l.list);
        }
    }

    public Character_Personality GetByID(string id) { return ID_Dictionary.ContainsKey(id) ? ID_Dictionary[id] : null; }

    public void LateInitialize()
    {
        foreach(var p in this.list)
        {
            p.CacheEntries();
        }
    }
}


[System.Serializable]
public enum KojoEventCalls
{
    Greeting,    // calls when PC enters room and meet Chara,
                 // include intro, firstperday, enterRoom (bedroom/toilet/shower/duringTrain), 

    Following,  // calls on every action if NPC is following someone, use this if both are entering places require personal handling (ex, shower)
    //Reaction,    // calls every round when PC is in room with Chara and not called greeting
                // include sleeping/timestop
    Timestop_End,
    RelationshipChange,
    FirstExperience,
    Climax,
    Creampie,
    Pregnancy
}


[System.Serializable]
public class Character_Personality
{
    // ID
    [SerializeField][JsonProperty] private string id;
    [JsonIgnore] public string ID { get { return id; } }

    // displayName
    [SerializeField][JsonProperty] private string displayName;
    [JsonIgnore] public string DisplayName { get { return displayName; } }

    // Fallback Reference 
    [SerializeField][JsonProperty] private string fallbackID = "";
    private Character_Personality fallbackRef = null;
    private Character_Personality Fallback { get {
        if (fallbackRef == null && fallbackID != "" && fallbackID != this.id) fallbackRef = scr_System_Serializer.current.MasterList.Character_Personalities.GetByID(fallbackID);
        return fallbackRef; } }

    
    // Responses
    [SerializeField][JsonProperty] private List<ResponseEntry> entries_list;
    Dictionary<string, ResponseEntry> entries = new Dictionary<string, ResponseEntry>();

    public Character_Personality()
    {
        
    }

    public void CacheEntries()
    {
        if (entries_list == null) return;
        foreach(var entry in entries_list)
        {
            if (entries.ContainsKey(entry.ID)) continue;
            entries.Add(entry.ID, entry);
        }
    }

    public string GetKOJOMessage(string eventID, List<EvaluationPackage> selfEPs, List<EvaluationPackage> targetEPs, RelationshipManager.Character_Relationship rel)
    {
        if (scr_System_CentralControl.current.LogPrefs.Debug_Logging_KojoEvents) Debug.Log("Try GetKOJOMessage evID["+eventID+"] [" + rel.Owner.FirstName + "->" + rel.Target.FirstName + "], self[" + String.Join("|", selfEPs) + "] target[" + String.Join("|", targetEPs) + "]");
        if (rel.Owner.RefID == 0) return "";
        if (!entries.ContainsKey(eventID))
        {
            if (this.Fallback != null) return Fallback.GetKOJOMessage(eventID, selfEPs, targetEPs, rel);
            else if (scr_System_CentralControl.current.LogPrefs.Debug_Logging_Unimplemented_KojoEvent) return "Personality [" + this.DisplayName + "] unimplemented event response for [" + eventID + "] and for target [" + rel.Target.FirstName + "]";
            else return "";
        }
        if (!entries[eventID].Validate(rel.Owner)) return "";
        return entries[eventID].GetResponse(rel, selfEPs, targetEPs);
    }

    public string GetKOJOMessage(string eventID,Character_Trainable owner,  List<string> selfTags, List<EvaluationPackage> allEPs)
    {
        if (scr_System_CentralControl.current.LogPrefs.Debug_Logging_KojoEvents) Debug.Log("Try GetKOJOMessage from ["+owner.FirstName+"] evID[" + eventID + "] [missing relation, checking with all ep actors] self[" + String.Join("|", selfTags) + "] target[" + String.Join("|", allEPs) + "]");
        if (!entries.ContainsKey(eventID))
        {
            if (this.Fallback != null) return Fallback.GetKOJOMessage(eventID, owner, selfTags, allEPs);
            else if (scr_System_CentralControl.current.LogPrefs.Debug_Logging_Unimplemented_KojoEvent) return "Personality [" + this.DisplayName + "] unimplemented event response for [" + eventID + "]";
            else return "";
        }
        if (!entries[eventID].Validate(owner)) return "";
        return entries[eventID].GetResponse(owner, selfTags, allEPs);
    }


    public string GetKOJOMessage(bool isDoer, EvaluationPackage ep, RelationshipManager.Character_Relationship relation)
    {
        string comID = ep.targetCOM.ID;
        if (comID.Contains("_noSex")) comID = comID.Substring(0, comID.Length - 6);
        if (!entries.ContainsKey(comID))
        {
            if(this.Fallback != null) return Fallback.GetKOJOMessage(isDoer, ep, relation);
            else if (scr_System_CentralControl.current.LogPrefs.Debug_Logging_Unimplemented_KojoCOM) return "Personality [" + this.DisplayName + "] unimplemented COM response for [" + comID + "] and for target [" + (relation == null ? "null" : relation.Target.FirstName) + "]";
            else return "";
        }

        return entries[comID].GetResponse(relation, isDoer ? ep.DoerSelfTag : ep.ReceiverSelfTag, isDoer ? ep.ReceiverTargetTag : ep.DoerTargetTag, ep);
    }

    public string GetKOJOMessage(string eventID, RelationshipManager.Character_Relationship rel)
    {
        if (!entries.ContainsKey(eventID))
        {
            
            if (this.Fallback != null) return Fallback.GetKOJOMessage(eventID,rel);
            else if (scr_System_CentralControl.current.LogPrefs.Debug_Logging_Unimplemented_KojoEvent) return "Personality [" + this.DisplayName + "] unimplemented event response for [" + eventID + "] and for target [" + rel.Target.FirstName + "]";
            else return "";
        }

        var xx = rel.Owner;
        var yy = rel.Target;
        if (xx == null || yy == null) return "";

        Utility.GetEPsFrom(xx, yy, out List<EvaluationPackage> xxEPs, out List<EvaluationPackage> yyEPs);

        return entries[eventID].GetResponse(rel, xxEPs, yyEPs);
    }

    /// <summary>
    /// //////////////////////////////////////////////////////////////////
    /// </summary>
    [System.Serializable]
    public class ResponseEntry
    {
        /// <summary>
        /// ID could either be comID or eventID
        /// </summary>
        public string ID = "";
        public List<Variant> variants = new List<Variant>();
        public bool interruptSelfJob = false;
        public bool interruptTargetJob = false;
        public bool debugLogging = false;

        public bool Validate(Character_Trainable owner, Character_Trainable target = null)
        {
            if (interruptSelfJob && owner.CurrentJob != null && (owner.CurrentJob is Job_Sex_Group )) return false;
            if (interruptTargetJob && target != null && target.CurrentJob != null && (target.CurrentJob is Job_Sex_Group )) return false;
            return true;
        }

        public string GetResponse(RelationshipManager.Character_Relationship rel, List<string> selfTags, List<string> targetTags, EvaluationPackage ep = null)
        {
            if (!Validate(rel.Owner, rel.Target)) return "";

            if (debugLogging || scr_System_CentralControl.current.LogPrefs.Debug_Logging_KojoEvents) Debug.Log("Validating kojoResponse ["+ID+"] req [" + rel.Owner.FirstName + "->" + rel.Target.FirstName + "], self[" + String.Join("|", selfTags) + "] target[" + String.Join("|", targetTags) + "]");



            List<string> responses = new List<string>();
            foreach (var i in variants)
            {
                if (i.isValid(rel, selfTags, targetTags, ep))
                {
                    responses.Add(i.GetRandomResponse(rel, selfTags, targetTags, ep));
                    if (!i.keepLooking) break;
                }
            }
            if (scr_System_CentralControl.current.LogPrefs.Debug_Logging_KojoEvents) Debug.Log("kojoResponse [" + String.Join("\n", responses) + "] ");
            return String.Join("\n", responses);
        }

        public string GetResponse(Character_Trainable owner, List<string> selfTags, List<EvaluationPackage> allEPs)
        {
            if (!Validate(owner)) return "";

            RelationshipManager.Character_Relationship relDoer, relReceiver;
            List<int> allChara = null;
            List<string> responses = new List<string>();
            bool isValid = false;
            foreach (var i in variants)
            {   // allEP might all have different doer receivers. Validate all in variant priority order, if any one is valid, then everyone in EP will get same treatment (cuz here we assume they all acting together

                isValid = false;
                foreach (var ep in allEPs)
                {
                    relDoer = owner.Relationships.FindRelationshipWith(ep.Doer);
                    relReceiver = owner.Relationships.FindRelationshipWith(ep.Receiver);

                    if (!Validate(owner, ep.Doer)) continue;
                   if(scr_System_CentralControl.current.LogPrefs.Debug_Logging_KojoEvents) Debug.Log(owner.FirstName+  " kojo getresponse past validate before doer/receiver validate, relOwner ["+(relDoer == null ? "null "+(ep.Doer == null?"":ep.Doer.FirstName):relDoer.Owner.FirstName +"->"+relDoer.Target.FirstName)+"] relreceiver["+(relReceiver == null ? "null " + (ep.Receiver == null ? "" : ep.Receiver.FirstName) : relReceiver.Owner.FirstName +"->"+ relReceiver.Target.FirstName)+"]");
                    if (relDoer != null && i.isValid(relDoer, selfTags, ep.DoerTargetTag, ep))
                    {
                        isValid = true;
                        var v = i.GetRandomResponse(relDoer, selfTags, ep.DoerTargetTag, ep, true);
                        //if (scr_System_CentralControl.current.LogPrefs.Debug_Logging_KojoEvents) Debug.Log("finding epdescription "+v+", replace with "+ep.Description_Ongoing);
                        responses.Add(v);
                        break;
                    }
                    else if (relReceiver != null && i.isValid(relReceiver, selfTags,ep.ReceiverTargetTag, ep))
                    {
                        isValid = true;
                        var v = i.GetRandomResponse(relReceiver, selfTags, ep.ReceiverTargetTag, ep, true);
                        //if (scr_System_CentralControl.current.LogPrefs.Debug_Logging_KojoEvents) Debug.Log("finding epdescription "+v+", replace with "+ep.Description_Ongoing);
                        responses.Add(v);
                        break;
                    }
                }

                if (isValid)
                {
                    // do execution
                    //Debug.LogError("KOJO CHECKINTERRUPT TRUE IS VALID: "+String.Join("|", responses));

                    allChara = new List<int>();
                    foreach(var ep in allEPs)
                    {
                        if (ep.Doer != null && !allChara.Contains(ep.Doer.RefID))
                        {
                            relDoer = owner.Relationships.FindRelationshipWith(ep.Doer);
                            foreach (var ii in i.results) ii.Execute(relDoer, selfTags, ep.DoerTargetTag, ep);
                            allChara.Add(ep.Doer.RefID);
                        }
                        if(ep.Receiver != null && !allChara.Contains(ep.Receiver.RefID))
                        {
                            relReceiver = owner.Relationships.FindRelationshipWith(ep.Receiver);
                            foreach (var ii in i.results) ii.Execute(relReceiver, selfTags, ep.ReceiverTargetTag, ep);
                            allChara.Add(ep.Receiver.RefID);
                        }
                    }
                    
                    if (!i.keepLooking) break;// String.Join("\n", responses);
                }
            }

            var str = String.Join("\n", responses);
            return str;
        }

        public string GetResponse(RelationshipManager.Character_Relationship rel, List<EvaluationPackage> selfEPs, List<EvaluationPackage> targetEPs)
        {
            if (!Validate(rel.Owner, rel.Target)) return "";

            List<string> ss1 = new List<string>();
            List<string> ss2 = new List<string>();
            foreach (var i in selfEPs) ss1.Add(i.ToString());
            foreach (var i in targetEPs) ss2.Add(i.ToString());
            if(scr_System_CentralControl.current.LogPrefs.Debug_Logging_KojoEvents) Debug.Log("Validating kojo req Multiple EPs [" + rel.Owner.FirstName + "->" + rel.Target.FirstName + "], self["+String.Join("|", ss1) +"] target["+String.Join("|", ss2) +"]");



            var sTags = new List<string>();
            var tTags = new List<string>();

            var ss = "";


            foreach (var ep in selfEPs)
            {
                sTags = (rel.Owner.RefID == ep.DoerRef ? ep.DoerTargetTag : (rel.Owner.RefID == ep.ReceiverRef ? ep.ReceiverTargetTag : new List<string>()));
                tTags = (rel.Target.RefID == ep.DoerRef ? ep.DoerTargetTag : (rel.Target.RefID == ep.ReceiverRef ? ep.ReceiverTargetTag : new List<string>()));

                ss = GetResponse(rel, sTags, tTags, ep);
                if (ss != "") return ss;
            }

            foreach (var ep in targetEPs)
            {
                sTags = (rel.Owner.RefID == ep.DoerRef ? ep.DoerTargetTag : (rel.Owner.RefID == ep.ReceiverRef ? ep.ReceiverTargetTag : new List<string>()));
                tTags = (rel.Target.RefID == ep.DoerRef ? ep.DoerTargetTag : (rel.Target.RefID == ep.ReceiverRef ? ep.ReceiverTargetTag : new List<string>()));

                ss = GetResponse(rel, sTags, tTags, ep);
                if (ss != "") return ss;
            }

            Utility.GetInteractionTagsFrom(rel.Owner, rel.Target, null, -1, ref sTags, ref tTags, ref tTags);
            return GetResponse(rel, sTags, tTags, null);
        }


        [System.Serializable]
        public class Variant
        {
            public List<Requirement> requirements = new List<Requirement>();
            public List<string> responses = new List<string>();
            public List<Results> results = new List<Results>();
            public bool keepLooking = false;
            public string selfEventCall = "";
            public bool hideWhenNotFocused = true;  // Do not display message if Owner is not CurrentTarget (not focused) or Target is not Player nor CurrentTarget (not focused)
            public string GetRandomResponse(RelationshipManager.Character_Relationship rel, List<string> selfTags, List<string> targetTags, EvaluationPackage ep = null, bool skipExecute = false)
            {
                // first apply cost
                if(!skipExecute) foreach(var i in results) i.Execute(rel, selfTags, targetTags, ep);

                List<string> returnV = new List<string>();

                if (selfEventCall != "")
                {                    
                   // Debug.Log("selfEventCall "+selfEventCall);
                    var sss = rel.Owner.Relationships.Personality.GetKOJOMessage(selfEventCall, rel);
                    if(sss.Length > 0)  returnV.Add(sss);
                    
                }

                if (responses.Count < 1) return String.Join("\n", returnV);
                else if (hideWhenNotFocused &&
                    rel.Owner != scr_System_CampaignManager.current.CurrentTarget &&
                    rel.Owner.RefID != 0 && rel.Target.RefID != 0 &&
                    rel.Target != scr_System_CampaignManager.current.CurrentTarget &&   // not direct target
                    (ep == null || ep.Package == null || !ep.Package.actorRefs.Contains(0)) &&
                   ( rel.Owner.CurrentJob == null || !(rel.Owner.CurrentJob is Job_Sex_Group) || !rel.Owner.CurrentJob.actorRefID.Contains(0))
                    )
                {
                    if(scr_System_CentralControl.current.LogPrefs.Debug_Logging_KojoEvents) Debug.Log(rel.Owner.FirstName + " kojo text skipped, no display");
                    return String.Join("\n", returnV);
                }

                var result = scr_System_Serializer.current.Dictionary.QueryThenParse(responses[Utility.GetRandIndexFromListCount(responses.Count)]);
                result = result.Replace("$self$", rel.Owner.FirstName);
                if (ep != null) result = result.Replace("$epDescription$", ep.Description_Ongoing);
                returnV.Add(result);
                returnV.RemoveAll(x => x == "");

                return String.Join("\n", returnV);
            }

            public bool isValid(RelationshipManager.Character_Relationship rel, List<string> selfTags, List<string> targetTags, EvaluationPackage ep)
            {
                foreach(var requirement in requirements)
                {
                    if (!requirement.Validate(rel, selfTags, targetTags, ep)) return false;
                }
                return true;
            }
            public bool isValid(Character_Trainable owner, List<string> selfTags, List<string> targetTags, EvaluationPackage ep)
            {
                foreach (var requirement in requirements)
                {
                    if (!requirement.Validate(owner, null, selfTags, targetTags, ep)) return false;
                }
                return true;
            }

            /*
            public bool isValid(RelationshipManager.Character_Relationship rel, List<EvaluationPackage> selfTags, List<EvaluationPackage> targetTags)
            {
                foreach (var requirement in requirements)
                {
                    if (!requirement.Validate(rel, selfTags, targetTags)) return false;
                }
                return true;
            }*/

            [System.Serializable]
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
                public List<string> selfTags = new List<string>();
                public List<string> targetTags = new List<string>();
                public int variantID = -1;
                public RequireKojoVariable requireKojoVariable = new RequireKojoVariable();
                public RequireStatusValue requireSelfStatusValue = new RequireStatusValue();


                public bool Validate(RelationshipManager.Character_Relationship rel, List<EvaluationPackage> selfEPs, List<EvaluationPackage> targetEPs)
                {
                    List<string> ss1 = new List<string>();
                    List<string> ss2 = new List<string>();
                    foreach(var i in selfEPs) ss1.Add(i.ToString());
                    foreach(var i in targetEPs) ss2.Add(i.ToString());
                    if (scr_System_CentralControl.current.LogPrefs.Debug_Logging_KojoEvents) Debug.Log("Validating kojo req Multiple EPs [" + rel.Owner.FirstName + "->" + rel.Target.FirstName + "], self["+String.Join("|", ss1) +"] target["+String.Join("|", ss2) +"]");

                    var sTags = new List<string>();
                    var tTags = new List<string>();


                    foreach (var ep in selfEPs)
                    {
                        sTags = (rel.Owner.RefID == ep.DoerRef ? ep.DoerSelfTag : (rel.Owner.RefID == ep.ReceiverRef ? ep.ReceiverSelfTag : new List<string>()));
                        tTags = (rel.Target.RefID == ep.DoerRef ? ep.DoerTargetTag : (rel.Target.RefID == ep.ReceiverRef ? ep.ReceiverTargetTag : new List<string>()));
                        if (Validate(rel, sTags, tTags, ep)) return true;
                    }

                    foreach (var ep in targetEPs)
                    {
                        sTags = (rel.Owner.RefID == ep.DoerRef ? ep.DoerSelfTag : (rel.Owner.RefID == ep.ReceiverRef ? ep.ReceiverSelfTag : new List<string>()));
                        tTags = (rel.Target.RefID == ep.DoerRef ? ep.DoerTargetTag : (rel.Target.RefID == ep.ReceiverRef ? ep.ReceiverTargetTag : new List<string>()));
                        if (Validate(rel, sTags, tTags, ep)) return true;
                    }

                    Utility.GetInteractionTagsFrom(rel.Owner, rel.Target, null, -1, ref sTags, ref tTags, ref tTags);
                    return Validate(rel, sTags, tTags, null);
                }

                public bool Validate(Character_Trainable self, Character_Trainable target, List<string> selfTags, List<string> targetTags, EvaluationPackage ep, RelationshipManager.Character_Relationship rel = null)
                {
                    if (scr_System_CentralControl.current.LogPrefs.Debug_Logging_KojoEvents) Debug.Log("Validating kojo req [" + (self == null?"null":self.FirstName) + "->" + (target == null ? "null":target.FirstName) + "], self[" + String.Join("|", selfTags) + "] target[" + String.Join("|", targetTags) + "]");

                    if (ep != null)
                    {
                        if (requireEPFailure && ep.Response > Memory_Response.Refuse) return false;
                        else if (!requireEPFailure && requireEPSuccess && ep.Response < Memory_Response.Accept) return false;
                    }
                    if (this.targetBaseID != "")
                    {
                        if (target == null) return false;
                        else if (targetBaseID == "PLAYER" && target != scr_System_CampaignManager.current.Player) return false;
                        else if (targetBaseID != "PLAYER" && target.BaseID != this.targetBaseID) return false;
                    }    
                    if (requireSelfAction && (self == null || !self.canAct)) return false;
                    if (requireTargetAction && (target == null || !target.canAct)) return false;
                    //if (selfTags.Count < this.selfTags.Count) return false;
                    if (this.selfTags.Count > 0 && !Utility.ListContainsStrict(selfTags, this.selfTags)) return false;
                    //if (targetTags.Count < this.targetTags.Count) return false;
                    if (this.targetTags.Count > 0 && !Utility.ListContainsStrict(targetTags, this.targetTags)) return false;
                    if (this.variantID > -1 && (ep == null || ep.VariantID != this.variantID)) return false;
                    if (this.requireKojoVariable != null && this.requireKojoVariable.isValid && rel != null && !this.requireKojoVariable.Validate(rel)) return false;
                    if (this.requireSelfStatusValue != null && this.requireSelfStatusValue.isValid && rel != null && !this.requireSelfStatusValue.Validate(rel.Owner)) return false;
                    return true;

                }
                public bool Validate(RelationshipManager.Character_Relationship rel, List<string> selfTags, List<string> targetTags, EvaluationPackage ep)
                {
                    return Validate(rel.Owner, rel.Target, selfTags, targetTags, ep, rel); 
                }

                [System.Serializable]
                public class RequireStatusValue
                {
                    public string statusID = "";
                    public bool checkExistOnly = false;
                    public LogicalOperand operand = LogicalOperand.none;
                    public float value = 0;
                    [JsonIgnore] public bool isValid { get { return this.statusID != "" && (checkExistOnly || operand != LogicalOperand.none); } }
                    public bool Validate(Character_Trainable chara)
                    {
                        var status = chara.Stats.GetStatusByStringMatch(statusID);
                        if (checkExistOnly) return status != null;
                        else return status != null && Utility.CompareValue(chara.Stats.GetStatusByStringMatch(statusID).Severity, operand, value);
                    }
                }

                [System.Serializable]
                public class RequireKojoVariable
                {
                    public bool isDailyVariable = false;
                    public string variableID = "";
                    public bool checkExistOnly = false;
                    public LogicalOperand operand = LogicalOperand.none;
                    public int value = 0;

                    [JsonIgnore] public bool isValid { get { return this.variableID != "" && (checkExistOnly || operand != LogicalOperand.none); } }
                    public bool Validate(RelationshipManager.Character_Relationship rel)
                    {
                        if (checkExistOnly) return (rel.Owner.Relationships.GetKojoVariableExist(isDailyVariable, rel, variableID) == (value != 0));
                        else return Utility.CompareValue(rel.Owner.Relationships.GetKojoVariable(isDailyVariable, rel, variableID), operand, value);

                    }
                }
            }

            [System.Serializable]
            public class Results
            {   // mainly use to manipulate kojo variables

                public ModKojoVariable modifyKojoVariables = new ModKojoVariable();
                public EventInitializer launchEvent = new EventInitializer();
                public ModStatusValue modifyStatusValue = new ModStatusValue();
                public void Execute(RelationshipManager.Character_Relationship rel, List<string> selfTags, List<string> targetTags, EvaluationPackage ep = null)
                {
                    if (modifyKojoVariables != null && modifyKojoVariables.isValid) modifyKojoVariables.Execute(rel);
                    if (this.launchEvent != null && this.launchEvent.isValid) launchEvent.Execute(rel.Owner);
                    if (modifyStatusValue != null && modifyStatusValue.isValid) modifyStatusValue.Execute(rel.Owner);
                }

                [System.Serializable]
                public class ModStatusValue
                {
                    public string statusID = "";
                    public float value = 0;
                    [JsonIgnore] public bool isValid { get { return this.statusID != "" && value != 0; } }
                    public void Execute(Character_Trainable chara)
                    {
                        chara.Stats.AddOrModStatus(statusID, value);
                    }

                }

                [System.Serializable]
                public class EventInitializer
                {
                    public string eventID = "";
                    public string eventLabel = "";

                    [JsonIgnore] public bool isValid { get { return this.eventID != ""; } }

                    public void Execute(Character_Trainable chara)
                    {
                        scr_UpdateHandler.current.EventHandler.StartEvent(chara, eventID, eventLabel, false);
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
                    public void Execute(RelationshipManager.Character_Relationship rel)
                    {
                        if (isSetValue) rel.Owner.Relationships.SetKojoVariable(isDailyVariable, rel, variableID, value);
                        else rel.Owner.Relationships.ModKojoVariable(isDailyVariable, rel, variableID, value);
                    }
                }

            }
        }
    }




    [System.Serializable]
    public class Response
    {



        [SerializeField] private string id;
        public string ID { get { return id; } }
        //[SerializeField] private Personality_Response value;
        //public Personality_Response Value { get { return value; } }


        [SerializeField] private string text;

        public string Text { get { return text; } }

        //https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/tokens/interpolated
        // store response string, return to personality
        // handle variable replacement in personality?


        [SerializeField] private string tooltip;
        [SerializeField] private string displayName;
        [SerializeField] private List<string> nullyfying_IDs;
        [SerializeField] private List<string> applicable_IDs;
    }



}


