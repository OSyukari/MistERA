using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using System;

public class Character_Personality_Index : I_IndexHasID, I_IndexMergeable, I_NeedLateInitialize, I_RemoveElemByTag
{
    public List<Character_Personality> list = new List<Character_Personality>();

    Dictionary<string, Character_Personality> ID_Dictionary = new Dictionary<string, Character_Personality>();
    public void RegisterAllID(List<string> messages)
    {
        messages.Add("Character_Personality_Index : registering ID with list length [" + list.Count+ "]") ;

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
            p.LateInitialize();
            p.CacheEntries();
        }
    }

    public void RemoveElemByTag(string tag)
    {
        foreach (var i in list) i.RemoveEntriesIDContaining(tag);
    }
}


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


public class Character_Personality
{
    // ID
    [JsonProperty] private string id;
    [JsonIgnore] public string ID { get { return id; } }

    // displayName
    [JsonProperty] private string displayName;
    [JsonIgnore] public string DisplayName { get { return displayName; } }

    // Fallback Reference 
    [JsonProperty] private string fallbackID = "";
    private Character_Personality fallbackRef = null;
    private Character_Personality Fallback { get {
        if (fallbackRef == null && fallbackID != "" && fallbackID != this.id) fallbackRef = scr_System_Serializer.current.MasterList.Character_Personalities.GetByID(fallbackID);
        return fallbackRef; } }

    
    // Responses
    [JsonProperty] private List<ResponseEntry> entries_list;
    Dictionary<string, ResponseEntry> entries = new Dictionary<string, ResponseEntry>();

    public void RemoveEntriesIDContaining(string str)
    {
        this.entries_list.RemoveAll(x => x.ID.Contains(str, StringComparison.InvariantCultureIgnoreCase));
        this.entries_list.RemoveAll(x => x.tags.Contains(str));
        foreach (var i in entries_list) i.RemoveVariantsByTag(str);
    }

    public Character_Personality()
    {
        
    }


    public void LateInitialize()
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
        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log("Try GetKOJOMessage evID["+eventID+"] [" + rel.Owner.FirstName + "->" + rel.Target.FirstName + "], self[" + String.Join("|", selfEPs) + "] target[" + String.Join("|", targetEPs) + "]");
        if (rel.Owner.RefID == 0) return "";
        if (!entries.ContainsKey(eventID))
        {
            if (this.Fallback != null) return Fallback.GetKOJOMessage(eventID, selfEPs, targetEPs, rel);
            else if (scr_System_CentralControl.current.LogPrefs.DLog_UnimplementedKojo) return "Personality [" + this.DisplayName + "] unimplemented event response for [" + eventID + "] and for target [" + rel.Target.FirstName + "]";
            else return "";
        }
        if (!entries[eventID].Validate(rel.Owner)) return "";
        return entries[eventID].GetResponse(rel, selfEPs, targetEPs);
    }

    public string GetKOJOMessage(string eventID,Character_Trainable owner,  List<string> selfTags, List<EvaluationPackage> allEPs)
    {
        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents || (scr_System_CentralControl.current.LogPrefs.DLog_Interrupt && eventID == "Interrupt")) Debug.Log("Try GetKOJOMessage from ["+owner.FirstName+"] evID[" + eventID + "] [missing relation, checking with all ep actors] self[" + String.Join("|", selfTags) + "] target[" + String.Join("|", allEPs) + "]");

        if (!entries.ContainsKey(eventID))
        {
            if (this.Fallback != null) return Fallback.GetKOJOMessage(eventID, owner, selfTags, allEPs);
            else if (scr_System_CentralControl.current.LogPrefs.DLog_UnimplementedKojo) return "Personality [" + this.DisplayName + "] unimplemented event response for [" + eventID + "]";
            else return "";
        }

        if (!entries[eventID].Validate(owner))
        {
            if (scr_System_CentralControl.current.LogPrefs.DLog_Interrupt && eventID == "Interrupt") Debug.LogError("validation failed");
            return "";
        }
        else
        {
            if (scr_System_CentralControl.current.LogPrefs.DLog_Interrupt && eventID == "Interrupt") Debug.LogError("validation success");
            return entries[eventID].GetResponse(owner, selfTags, allEPs);
        }
    }


    public string GetKOJOMessage(bool isDoer, EvaluationPackage ep, RelationshipManager.Character_Relationship relation)
    {
        string comID = ep.targetCOM.ID;
        if (comID.Contains("_noSex")) comID = comID.Substring(0, comID.Length - 6);
        if (!entries.ContainsKey(comID))
        {
            if(this.Fallback != null) return Fallback.GetKOJOMessage(isDoer, ep, relation);
            else if (scr_System_CentralControl.current.LogPrefs.DLog_UnimplementedKojo) return "Personality [" + this.DisplayName + "] unimplemented COM response for [" + comID + "] and for target [" + (relation == null ? "null" : relation.Target.FirstName) + "]";
            else return "";
        }

        return entries[comID].GetResponse(relation, isDoer ? ep.DoerSelfTag : ep.ReceiverSelfTag, isDoer ? ep.ReceiverTargetTag : ep.DoerTargetTag, ep);
    }

    public string GetKOJOMessage(string eventID, RelationshipManager.Character_Relationship rel)
    {
        if (!entries.ContainsKey(eventID))
        {
            
            if (this.Fallback != null) return Fallback.GetKOJOMessage(eventID,rel);
            else if (scr_System_CentralControl.current.LogPrefs.DLog_UnimplementedKojo) return "Personality [" + this.DisplayName + "] unimplemented event response for [" + eventID + "] and for target [" + rel.Target.FirstName + "]";
            else return "";
        }

        var xx = rel.Owner;
        var yy = rel.Target;
        if (xx == null || yy == null) return "";

        UtilityEX.GetEPsFrom(xx, yy, out List<EvaluationPackage> xxEPs, out List<EvaluationPackage> yyEPs);

        return entries[eventID].GetResponse(rel, xxEPs, yyEPs);
    }

    public string GetKOJOMessage(string eventID, RelationshipManager.Character_Relationship rel, List<string> selfTags, List<string> targetTags)
    {
        if (!entries.ContainsKey(eventID))
        {

            if (this.Fallback != null) return Fallback.GetKOJOMessage(eventID, rel);
            else if (scr_System_CentralControl.current.LogPrefs.DLog_UnimplementedKojo) return "Personality [" + this.DisplayName + "] unimplemented event response for [" + eventID + "] and for target [" + rel.Target.FirstName + "]";
            else return "";
        }

        var xx = rel.Owner;
        var yy = rel.Target;
        if (xx == null || yy == null) return "";

        return entries[eventID].GetResponse(rel, selfTags, targetTags);
    }

    /// <summary>
    /// //////////////////////////////////////////////////////////////////
    /// </summary>
    public class ResponseEntry
    {
        /// <summary>
        /// ID could either be comID or eventID
        /// </summary>
        public string ID = "";
        public List<string> tags = new List<string>();
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

            if (debugLogging && scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log("Validating kojoResponse ["+ID+"] req [" + rel.Owner.FirstName + "->" + rel.Target.FirstName + "], self[" + String.Join("|", selfTags) + "] target[" + String.Join("|", targetTags) + "]");



            List<string> responses = new List<string>();
            foreach (var i in variants)
            {
                if (i.isValid(rel, selfTags, targetTags, ep))
                {
                    bool playerInvolved = ep != null && ep.Package != null && ep.Package.actorRefs.Contains(scr_System_CampaignManager.current.Player.RefID);
                    responses.Add(i.GetRandomResponse(rel, selfTags, targetTags, playerInvolved));
                    if (!i.keepLooking) break;
                }
            }
            if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log("kojoResponse [" + String.Join("\n", responses) + "] ");
            return String.Join("\n", responses);
        }

        public string GetResponse(Character_Trainable owner, List<string> selfTags, List<EvaluationPackage> allEPs)
        {
            if (!Validate(owner)) return "";

            RelationshipManager.Character_Relationship relDoer, relReceiver, relSelf;
            List<int> allChara = null;
            List<string> responses = new List<string>();
            bool isValid = false;
            foreach (var i in variants)
            {   // allEP might all have different doer receivers. Validate all in variant priority order, if any one is valid, then everyone in EP will get same treatment (cuz here we assume they all acting together

                isValid = false;
                foreach (var ep in allEPs)
                {
                    relSelf = owner == ep.Doer ? owner.Relationships.FindRelationshipWith(ep.Doer) : null;
                    relDoer = owner == ep.Doer ? null : owner.Relationships.FindRelationshipWith(ep.Doer);
                    relReceiver = owner == ep.Receiver ? null : owner.Relationships.FindRelationshipWith(ep.Receiver);
                    bool playerInvolved = ep != null && ep.Package != null && ep.Package.actorRefs.Contains(scr_System_CampaignManager.current.Player.RefID);

                    if (!Validate(owner, ep.Doer)) continue;
                    if(scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log(owner.FirstName+  " kojo getresponse past validate before doer/receiver validate, relOwner ["+(relDoer == null ? "null "+(ep.Doer == null?"":ep.Doer.FirstName):relDoer.Owner.FirstName +"->"+relDoer.Target.FirstName)+"] relreceiver["+(relReceiver == null ? "null " + (ep.Receiver == null ? "" : ep.Receiver.FirstName) : relReceiver.Owner.FirstName +"->"+ relReceiver.Target.FirstName)+"]");
                    if (relDoer != null && i.isValid(relDoer, selfTags, ep.DoerTargetTag, ep))
                    {
                        isValid = true;
                        var v = i.GetRandomResponse(relDoer, selfTags, ep.DoerTargetTag, playerInvolved, true);
                        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log("finding epdescription " + v + ", replace with " + ep.Description_Ongoing);
                        v = v.Replace("$epDescription$", ep.Package.targetCOM.DisplayName(ep.Package.COMVariantID));
                        responses.Add(v);
                        break;
                    }
                    else if (relReceiver != null && i.isValid(relReceiver, selfTags, ep.ReceiverTargetTag, ep))
                    {
                        isValid = true;
                        var v = i.GetRandomResponse(relReceiver, selfTags, ep.ReceiverTargetTag, playerInvolved, true);
                        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log("finding epdescription " + v + ", replace with " + ep.Description_Ongoing);
                        v = v.Replace("$epDescription$", ep.Package.targetCOM.DisplayName(ep.Package.COMVariantID));
                        responses.Add(v);
                        break;
                    }
                    else if (relSelf != null && relDoer == null && relReceiver == null && ep.Receiver == null && i.isValid(relSelf, selfTags, ep.DoerTargetTag, ep))
                    {   // relDoer == null && relReceiver == null
                        // self referencing package, both null, ep.doer is relOwner and ep.receiver == null

                        isValid = true;
                        var v = i.GetRandomResponse(relSelf, selfTags, ep.ReceiverTargetTag, playerInvolved, true);
                        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log("finding epdescription " + v + ", replace with " + ep.Description_Ongoing);
                        v = v.Replace("$epDescription$", ep.Package.targetCOM.DisplayName(ep.Package.COMVariantID));
                        responses.Add(v);
                        break;

                    }
                }

                if (isValid)
                {
                    // do execution
                    if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.LogError("KOJO CHECKINTERRUPT TRUE IS VALID: "+String.Join("|", responses));

                    allChara = new List<int>();
                    foreach(var ep in allEPs)
                    {
                        if (ep.Doer != null && !allChara.Contains(ep.Doer.RefID))
                        {
                            relDoer = owner.Relationships.FindRelationshipWith(ep.Doer);
                            foreach (var ii in i.results) ii.Execute(relDoer, selfTags, ep.DoerTargetTag);
                            allChara.Add(ep.Doer.RefID);
                        }
                        if(ep.Receiver != null && !allChara.Contains(ep.Receiver.RefID))
                        {
                            relReceiver = owner.Relationships.FindRelationshipWith(ep.Receiver);
                            foreach (var ii in i.results) ii.Execute(relReceiver, selfTags, ep.ReceiverTargetTag);
                            allChara.Add(ep.Receiver.RefID);
                        }
                    }
                    
                    if (!i.keepLooking) break;// String.Join("\n", responses);
                }
            }
            //if (!isValid) Debug.LogError($"cannot find response for {owner.FirstName} on ID {ID}, listEP_count: {allEPs.Count}");
                

            var str = String.Join("\n", responses);
            return str;
        }

        public void RemoveVariantsByTag(string tag)
        {
            this.variants.RemoveAll(x=>x.tags.Contains(tag));
        }
        public string GetResponse(RelationshipManager.Character_Relationship rel, List<EvaluationPackage> selfEPs, List<EvaluationPackage> targetEPs)
        {
            if (!Validate(rel.Owner, rel.Target)) return "";

            List<string> ss1 = new List<string>();
            List<string> ss2 = new List<string>();
            foreach (var i in selfEPs) ss1.Add(i.ToString());
            foreach (var i in targetEPs) ss2.Add(i.ToString());
            if(scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log("Validating kojo req Multiple EPs [" + rel.Owner.FirstName + "->" + rel.Target.FirstName + "], self["+String.Join("|", ss1) +"] target["+String.Join("|", ss2) +"]");



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

            UtilityEX.GetInteractionTagsFrom(rel.Owner, rel.Target, null, -1, ref sTags, ref tTags, ref tTags);
            return GetResponse(rel, sTags, tTags, null);
        }

        public string GetResponse(RelationshipManager.Character_Relationship rel, List<string> selfTags, List<string> targetTags )
        {
            if (!Validate(rel.Owner, rel.Target)) return "";
            return GetResponse(rel, selfTags, targetTags, null);
        }

        public class Variant
        {
            public List<string> tags = new List<string>();
            public List<Requirement> requirements = new List<Requirement>();
            public List<string> responses = new List<string>();
            public List<Results> results = new List<Results>();
            public bool keepLooking = false;
            public string selfEventCall = "";
            public bool hideWhenNotFocused = true;  // Do not display message if Owner is not CurrentTarget (not focused) or Target is not Player nor CurrentTarget (not focused)
            public string GetRandomResponse(RelationshipManager.Character_Relationship rel, List<string> selfTags, List<string> targetTags, bool isPlayerInvolved = false, bool skipExecute = false)
            {
                // first apply cost
                if(!skipExecute) foreach(var i in results) i.Execute(rel, selfTags, targetTags);




                List<string> returnV = new List<string>();
                string result = "";
                if (responses.Count > 0)
                {
                    result = LocalizeDictionary.QueryThenParse(Utility.GetRandomElement(responses));
                    result = result.Replace("$self$", rel.Owner.FirstName).Replace("$target$", rel.Target.FirstName);
                }

                string sss = "";
                if (selfEventCall != "")
                {                    
                   // Debug.Log("selfEventCall "+selfEventCall);
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
                    if (result != "") returnV.Add(result);
                    returnV.RemoveAll(x => x == "");
                    if (sss != "") returnV.Add(sss);
                }

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
                public Memory_Response requireEPSuccessGTE = Memory_Response.None;
                public List<string> selfTags = new List<string>();
                public List<string> targetTags = new List<string>();
                public int variantID = -1;
                public RequireKojoVariable requireKojoVariable = new RequireKojoVariable();
                public RequireStatusValue requireSelfStatusValue = new RequireStatusValue();
                public RequireStatValue requireSelfStatValue = new RequireStatValue();
                public RequireMemory requireSelfMemory = new RequireMemory();


                public bool Validate(RelationshipManager.Character_Relationship rel, List<EvaluationPackage> selfEPs, List<EvaluationPackage> targetEPs)
                {
                    List<string> ss1 = new List<string>();
                    List<string> ss2 = new List<string>();
                    foreach(var i in selfEPs) ss1.Add(i.ToString());
                    foreach(var i in targetEPs) ss2.Add(i.ToString());
                    if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log("Validating kojo req Multiple EPs [" + rel.Owner.FirstName + "->" + rel.Target.FirstName + "], self["+String.Join("|", ss1) +"] target["+String.Join("|", ss2) +"]");

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

                    UtilityEX.GetInteractionTagsFrom(rel.Owner, rel.Target, null, -1, ref sTags, ref tTags, ref tTags);
                    return Validate(rel, sTags, tTags, null);
                }

                public bool Validate(Character_Trainable self, Character_Trainable target, List<string> selfTags, List<string> targetTags, EvaluationPackage ep, RelationshipManager.Character_Relationship rel = null)
                {
                    if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log("Validating kojo req [" + (self == null?"null":self.FirstName) + "->" + (target == null ? "null":target.FirstName) + "], self[" + String.Join("|", selfTags) + "] target[" + String.Join("|", targetTags) + "]");

                    if (ep != null)
                    {
                        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log($"validating EP reqFailure? {requireEPFailure && ep.Response > Memory_Response.Refuse} reqSuccess? {!requireEPFailure && requireEPSuccess && ep.Response < Memory_Response.Accept} ");
                        if (requireEPFailure && ep.Response != Memory_Response.Refuse) return false;
                        else if (!requireEPFailure && requireEPSuccess && ep.Response < Memory_Response.Accept) return false;

                        var selfAttitude = ep.Response;
                        if (selfAttitude == Memory_Response.None && requireEPSuccessGTE != Memory_Response.None)
                        {
                            Debug.LogError("SelfAttitude None in Validating kojo req [" + (self == null ? "null" : self.FirstName) + "->" + (target == null ? "null" : target.FirstName) + "], self[" + String.Join("|", selfTags) + "] target[" + String.Join("|", targetTags) + "]");
                            return false;
                        }
                        else if (requireEPSuccessGTE != Memory_Response.None && selfAttitude < requireEPSuccessGTE) return false;
                    }
                    if (this.targetBaseID != "")
                    {
                        if (target == null) return false;
                        else if (targetBaseID == "PLAYER" && target != scr_System_CampaignManager.current.Player) return false;
                        else if (targetBaseID != "PLAYER" && target.BaseID != this.targetBaseID) return false;
                    }
                    if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.LogError($"validating requireSelfAction {requireSelfAction} {self.canAct} {self.Stats.isConsciousnessUnconscious}");
                    if (requireSelfAction && (self == null || !self.canAct)) return false;
                    if (requireTargetAction && (target == null || !target.canAct)) return false;
                    //if (selfTags.Count < this.selfTags.Count) return false;
                    if (this.selfTags.Count > 0 && !Utility.ListContainsStrict(selfTags, this.selfTags)) return false;
                    //if (targetTags.Count < this.targetTags.Count) return false;
                    if (this.targetTags.Count > 0 && !Utility.ListContainsStrict(targetTags, this.targetTags)) return false;
                    if (this.variantID > -1 && (ep == null || ep.VariantID != this.variantID)) return false;
                    if (this.requireKojoVariable != null && this.requireKojoVariable.isValid && rel != null && !this.requireKojoVariable.Validate(rel)) return false;
                    if (this.requireSelfStatValue != null && this.requireSelfStatValue.isValid && rel != null && !this.requireSelfStatValue.Validate(rel.Owner)) return false;
                    if (this.requireSelfStatusValue != null && this.requireSelfStatusValue.isValid && rel != null && !this.requireSelfStatusValue.Validate(rel.Owner)) return false;
                    if (this.requireSelfMemory != null && this.requireSelfMemory.isValid && rel != null && !this.requireSelfMemory.Validate(rel.Owner)) return false;
                    return true;

                }
                public bool Validate(RelationshipManager.Character_Relationship rel, List<string> selfTags, List<string> targetTags, EvaluationPackage ep)
                {
                    //var v = 
                    //if (!v && (rel.ownerRefID == 0 || rel.TargetID == 0)) Debug.LogError("failed validation");
                    return Validate(rel.Owner, rel.Target, selfTags, targetTags, ep, rel);
                }

                [System.Serializable]
                public class RequireMemory
                {
                    public int minuteRollback = 0;
                    public List<string> selfTags = new List<string>();
                    public List<string> targetTags  = new List<string>();
                    [JsonIgnore] public bool isValid { get { return minuteRollback > 0 && (selfTags.Count > 0 || targetTags.Count > 0); } }
                    public bool Validate(Character_Trainable chara)
                    {
                        List<Memory_Entry> list = chara.Memory.GetAllMemoryMatch(selfTags, targetTags, minuteRollback);
                        return list.Count > 0;
                    }

                }

                [System.Serializable]
                public class RequireStatValue
                {
                    public string statID = "";
                    public LogicalOperand operand = LogicalOperand.none;
                    public string value = "";
                    [JsonIgnore] public bool isValid { get { return this.statID != "" && operand != LogicalOperand.none && value != ""; } }
                    public bool Validate(Character_Trainable chara)
                    {
                        return chara.CompareStatValue(statID, operand, value);
                    }

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
                public void Execute(RelationshipManager.Character_Relationship rel, List<string> selfTags, List<string> targetTags)
                {
                    if (modifyKojoVariables != null && modifyKojoVariables.isValid) modifyKojoVariables.Execute(rel);
                    if (this.launchEvent != null && this.launchEvent.isValid) launchEvent.Execute(rel);
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
                    public bool reverseTargets = false;
                    public string targetKeyword = "";

                    [JsonIgnore] public bool isValid { get { return this.eventID != ""; } }

                    public void Execute(RelationshipManager.Character_Relationship rel)
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

                        if (newEvent != null) scr_UpdateHandler.current.EventHandler.StartEvent(newEvent, false);
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


