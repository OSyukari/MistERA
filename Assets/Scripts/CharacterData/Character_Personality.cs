using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

public class Character_Personality_Index : I_IndexHasID, I_IndexMergeable, I_NeedLateInitialize, I_RemoveElemByTag
{
    public List<Character_Personality> list = new List<Character_Personality>();
    public List<Character_Personality_LooseEntry> looselist = new List<Character_Personality_LooseEntry>();

    Dictionary<string, Character_Personality> ID_Dictionary = new Dictionary<string, Character_Personality>();
    public void RegisterAllID(List<string> messages)
    {
        messages.Add("Character_Personality_Index : registering ID with list length [" + list.Count+ "]") ;

        foreach (Character_Personality o in this.list)
        {
            //Debug.Log("Character_Origin_Index : registering origin ["+o.ID+"] ");
            if (!ID_Dictionary.ContainsKey(o.ID)) ID_Dictionary[o.ID] = o;
            else Debug.LogError($"error registering personality {o.ID} failed");
        }

        foreach(var i in looselist)
        {
            if (!ID_Dictionary.ContainsKey(i.ID)) Debug.LogError($"error registering personality {i.ID} loose entry failed");
            else
            {
                ID_Dictionary[i.ID].AddEntry(i.Entry);
            }
        }
    }
    
    public void MergeWith(I_IndexMergeable list){
        var l = list as Character_Personality_Index;
        if (l == null) return;
        else
        {
            if (l.list != null) this.list.AddRange(l.list);
            if (l.looselist != null) this.looselist.AddRange(l.looselist);
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

public class Character_Personality_LooseEntry
{
    public string ID = "";
    public ResponseEntry Entry = null;
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
        foreach(var i in this.entries_list)
        {
            foreach(var j in i.variants)
            {
                if (j.requirement == null)
                {
                    j.requirement = new ResponseEntry.Variant.Requirement();
                }
            }
        }
    }

    public void AddEntry(ResponseEntry entry)
    {
        this.entries_list.Add(entry);
    }

    public void CacheEntries()
    {
        if (entries_list == null) return;
        foreach(var entry in entries_list)
        {
            if (entries.ContainsKey(entry.ID)) continue;
            entries.Add(entry.ID, entry);
            if (scr_System_Serializer.current.Debug_KojoIntegrityCheck) entry.ValidateIntegrity();
        }
    }

    public MessageCollect_KojoEntry GetKOJOMessage(string eventID, List<EvaluationPackage> selfEPs, List<EvaluationPackage> targetEPs, Character_Relationship rel)
    {
        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log("Try GetKOJOMessage evID["+eventID+"] [" + rel.Owner.FirstName + "->" + rel.Target.FirstName + "], self[" + String.Join("|", selfEPs) + "] target[" + String.Join("|", targetEPs) + "]");
        if (rel.Owner.RefID == 0) return null;
        if (!entries.ContainsKey(eventID))
        {
            if (this.Fallback != null) return Fallback.GetKOJOMessage(eventID, selfEPs, targetEPs, rel);
            else if (scr_System_CentralControl.current.LogPrefs.DLog_UnimplementedKojo) Debug.Log( "Personality [" + this.DisplayName + "] unimplemented event response for [" + eventID + "] and for target [" + rel.Target.FirstName + "]");
            return null;
        }
        if (!entries[eventID].Validate(rel.Owner)) return null;

        eventID = entries[eventID].CheckRedirect(eventID);
        return entries[eventID].GetResponse(rel, selfEPs, targetEPs);
    }

    public MessageCollect_KojoEntry GetKOJOMessage(string eventID, Character_Trainable owner,  List<string> selfTags, List<EvaluationPackage> allEPs)
    {
        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents || (scr_System_CentralControl.current.LogPrefs.DLog_Interrupt && eventID == "Interrupt")) Debug.Log("Try GetKOJOMessage from ["+owner.FirstName+"] evID[" + eventID + "] [missing relation, checking with all ep actors] self[" + String.Join("|", selfTags) + "] target[" + String.Join("|", allEPs) + "]");

        if (!entries.ContainsKey(eventID))
        {
            if (this.Fallback != null) return Fallback.GetKOJOMessage(eventID, owner, selfTags, allEPs);
            else if (scr_System_CentralControl.current.LogPrefs.DLog_UnimplementedKojo) Debug.Log($"Personality [{this.DisplayName}] unimplemented event response for [{eventID}]");
            return null;
        }

        if (!entries[eventID].Validate(owner))
        {
            if (scr_System_CentralControl.current.LogPrefs.DLog_Interrupt && eventID == "Interrupt") Debug.LogError("validation failed");
            return null;
        }
        else
        {
            if (scr_System_CentralControl.current.LogPrefs.DLog_Interrupt && eventID == "Interrupt") Debug.LogError("validation success");

            eventID = entries[eventID].CheckRedirect(eventID);
            return entries[eventID].GetResponse(owner, selfTags, allEPs);
        }
    }

    public MessageCollect_KojoEntry GetKOJOMessage(bool isDoer, ActionPackage ap, Character_Relationship relation, bool checkClimax = false)
    {
        string comID = ap.targetCOM.tooltipID;
        if (comID.Contains("_noSex")) comID = comID.Substring(0, comID.Length - 6);
        if (checkClimax) comID = $"{comID}_Climax";
        if (!entries.ContainsKey(comID))
        {
            if (this.Fallback != null) return Fallback.GetKOJOMessage(isDoer, ap, relation);
            else if (scr_System_CentralControl.current.LogPrefs.DLog_UnimplementedKojo) Debug.Log("Personality [" + this.DisplayName + "] unimplemented COM response for [" + comID + "] and for target [" + (relation == null ? "null" : relation.Target.FirstName) + "]");
            return null;
        }

        comID = entries[comID].CheckRedirect(comID);
        return entries[comID].GetResponse(relation, ap.ActorSelfTags(relation.Owner.RefID), ap.ActorTargetTags(relation.Target.RefID));
    }
    public MessageCollect_KojoEntry GetKOJOMessage(bool isDoer, EvaluationPackage ep, Character_Relationship relation, bool checkClimax = false)
    {
        string comID = ep.targetCOM.tooltipID;
        if (comID.Contains("_noSex")) comID = comID.Substring(0, comID.Length - 6);
        if (checkClimax) comID = $"{comID}_Climax";
        if (!entries.ContainsKey(comID))
        {
            if(this.Fallback != null) return Fallback.GetKOJOMessage(isDoer, ep, relation);
            else if (scr_System_CentralControl.current.LogPrefs.DLog_UnimplementedKojo) Debug.Log( "Personality [" + this.DisplayName + "] unimplemented COM response for [" + comID + "] and for target [" + (relation == null ? "null" : relation.Target.FirstName) + "]");
            return null;
        }

        comID = entries[comID].CheckRedirect(comID);
        return entries[comID].GetResponse(relation, isDoer ? ep.DoerSelfTag : ep.ReceiverSelfTag, isDoer ? ep.ReceiverTargetTag : ep.DoerTargetTag, ep);
    }
    public MessageCollect_KojoEntry GetKOJOMessage_Tryjoin(ActionPackage ep, Character_Relationship relation)
    {
        string comID = ep.targetCOM.tooltipID;
        if (comID.Contains("_noSex")) comID = comID.Substring(0, comID.Length - 6);
        comID = $"{comID}_Tryjoin";
        if (!entries.ContainsKey(comID))
        {
            if (this.Fallback != null) return Fallback.GetKOJOMessage_Tryjoin(ep, relation);
            else if (scr_System_CentralControl.current.LogPrefs.DLog_UnimplementedKojo) Debug.Log("Personality [" + this.DisplayName + "] unimplemented COM response for [" + comID + "] and for target [" + (relation == null ? "null" : relation.Target.FirstName) + "]");
            return null;
        }

        comID = entries[comID].CheckRedirect(comID);
        return entries[comID].GetResponse(relation, new List<string>(), new List<string>());
    }
    public MessageCollect_KojoEntry GetKOJOMessage_Suffix(string id, string suffix, Character_Relationship relation)
    {
        string comID = id;
        if (comID.Contains("_noSex")) comID = comID.Substring(0, comID.Length - 6);
        comID = $"{comID}{suffix}";
        if (!entries.ContainsKey(comID))
        {
            if (this.Fallback != null) return Fallback.GetKOJOMessage_Suffix(id, suffix, relation);
            else if (scr_System_CentralControl.current.LogPrefs.DLog_UnimplementedKojo) Debug.Log("Personality [" + this.DisplayName + "] unimplemented COM response for [" + comID + "] and for target [" + (relation == null ? "null" : relation.Target.FirstName) + "]");
            return null;
        }

        comID = entries[comID].CheckRedirect(comID);
        return entries[comID].GetResponse(relation, new List<string>(), new List<string>());
    }
    public MessageCollect_KojoEntry GetKOJOMessage_Suffix(string suffix, bool isDoer, bool isReceiver, EvaluationPackage ep, Character_Relationship relation)
    {
        string comID = ep.targetCOM.tooltipID;
        if (comID.Contains("_noSex")) comID = comID.Substring(0, comID.Length - 6);
        comID = $"{comID}{suffix}";
        if (!entries.ContainsKey(comID))
        {
            if (this.Fallback != null) return Fallback.GetKOJOMessage_Suffix(suffix, isDoer, isReceiver, ep, relation);
            else if (scr_System_CentralControl.current.LogPrefs.DLog_UnimplementedKojo) Debug.Log("Personality [" + this.DisplayName + "] unimplemented COM response for [" + comID + "] and for target [" + (relation == null ? "null" : relation.Target.FirstName) + "]");
            return null;
        }

        comID = entries[comID].CheckRedirect(comID);
        return entries[comID].GetResponse(relation, isDoer ? ep.DoerSelfTag : isReceiver ? ep.ReceiverSelfTag : new List<string>(), isDoer ? ep.ReceiverTargetTag : isReceiver ? ep.DoerTargetTag : new List<string>(), ep);
    }
    public MessageCollect_KojoEntry GetKOJOMessage_Join(bool isDoer, EvaluationPackage ep, Character_Relationship relation)
    {
        string comID = ep.targetCOM.tooltipID;
        if (comID.Contains("_noSex")) comID = comID.Substring(0, comID.Length - 6);
        comID = $"{comID}_Join";
        if (!entries.ContainsKey(comID))
        {
            if (this.Fallback != null) return Fallback.GetKOJOMessage_Join(isDoer, ep, relation);
            else if (scr_System_CentralControl.current.LogPrefs.DLog_UnimplementedKojo) Debug.Log("Personality [" + this.DisplayName + "] unimplemented COM response for [" + comID + "] and for target [" + (relation == null ? "null" : relation.Target.FirstName) + "]");
            return null;
        }

        comID = entries[comID].CheckRedirect(comID);
        return entries[comID].GetResponse(relation, isDoer ? ep.DoerSelfTag : ep.ReceiverSelfTag, isDoer ? ep.ReceiverTargetTag : ep.DoerTargetTag, ep);
    }
    public MessageCollect_KojoEntry GetKOJOMessage_Begin(bool isDoer, EvaluationPackage ep, Character_Relationship relation)
    {
        string comID = ep.targetCOM.tooltipID;
        if (comID.Contains("_noSex")) comID = comID.Substring(0, comID.Length - 6);
        comID = $"{comID}_Begin";
        if (!entries.ContainsKey(comID))
        {
            if (this.Fallback != null) return Fallback.GetKOJOMessage_Begin(isDoer, ep, relation);
            else if (scr_System_CentralControl.current.LogPrefs.DLog_UnimplementedKojo) Debug.Log("Personality [" + this.DisplayName + "] unimplemented COM response for [" + comID + "] and for target [" + (relation == null ? "null" : relation.Target.FirstName) + "]");
            return null;
        }

        comID = entries[comID].CheckRedirect(comID);
        return entries[comID].GetResponse(relation, isDoer ? ep.DoerSelfTag : ep.ReceiverSelfTag, isDoer ? ep.ReceiverTargetTag : ep.DoerTargetTag, ep);
    }
    public MessageCollect_KojoEntry GetKOJOMessage_Ongoing(bool isDoer, EvaluationPackage ep, Character_Relationship relation)
    {
        string comID = ep.targetCOM.tooltipID;
        if (comID.Contains("_noSex")) comID = comID.Substring(0, comID.Length - 6);
        comID = $"{comID}_Ongoing";

        //Debug.Log($"GetKOJOMessage_Ongoing {comID}");

        if (!entries.ContainsKey(comID))
        {
            if (this.Fallback != null) return Fallback.GetKOJOMessage_Ongoing(isDoer, ep, relation);
            else if (scr_System_CentralControl.current.LogPrefs.DLog_UnimplementedKojo) Debug.Log("Personality [" + this.DisplayName + "] unimplemented COM response for [" + comID + "] and for target [" + (relation == null ? "null" : relation.Target.FirstName) + "]");
            return null;
        }
        comID = entries[comID].CheckRedirect(comID);
        return entries[comID].GetResponse(relation, isDoer ? ep.DoerSelfTag : ep.ReceiverSelfTag, isDoer ? ep.ReceiverTargetTag : ep.DoerTargetTag, ep);
    }
    public MessageCollect_KojoEntry GetKOJOMessage_Interrupt(bool isDoer, EvaluationPackage ep, Character_Relationship relation)
    {
        string comID = ep.targetCOM.tooltipID;
        if (comID.Contains("_noSex")) comID = comID.Substring(0, comID.Length - 6);
        comID = $"{comID}_Interrupt";

        //Debug.Log($"GetKOJOMessage_Interrupt {comID}");

        if (!entries.ContainsKey(comID))
        {
            if (this.Fallback != null) return Fallback.GetKOJOMessage_Interrupt(isDoer, ep, relation);
            else if (scr_System_CentralControl.current.LogPrefs.DLog_UnimplementedKojo) Debug.Log("Personality [" + this.DisplayName + "] unimplemented COM response for [" + comID + "] and for target [" + (relation == null ? "null" : relation.Target.FirstName) + "]");
            return null;
        }
        comID = entries[comID].CheckRedirect(comID);
        return entries[comID].GetResponse(relation, isDoer ? ep.DoerSelfTag : ep.ReceiverSelfTag, isDoer ? ep.ReceiverTargetTag : ep.DoerTargetTag, ep);
    }

    public MessageCollect_KojoEntry GetKOJOMessage(string eventID, Character_Relationship rel)
    {
        if (!entries.ContainsKey(eventID))
        {
            
            if (this.Fallback != null) return Fallback.GetKOJOMessage(eventID,rel);
            else if (scr_System_CentralControl.current.LogPrefs.DLog_UnimplementedKojo) Debug.Log( "Personality [" + this.DisplayName + "] unimplemented event response for [" + eventID + "] and for target [" + rel.Target.FirstName + "]");
            return null;
        }

        var xx = rel.Owner;
        var yy = rel.Target;
        if (xx == null || yy == null) return null;

        UtilityEX.GetEPsFrom(xx, yy, out List<EvaluationPackage> xxEPs, out List<EvaluationPackage> yyEPs);

        eventID = entries[eventID].CheckRedirect(eventID);
        return entries[eventID].GetResponse(rel, xxEPs, yyEPs);
    }

    public MessageCollect_KojoEntry GetKOJOMessage(string eventID, Character_Relationship rel, List<string> selfTags, List<string> targetTags)
    {
        if (selfTags == null) selfTags = new List<string>();
        if (targetTags == null) targetTags = new List<string>();
        if (eventID == "Descriptor") Debug.Log($"Descriptor called on {rel.Owner.CallName}");
        if (!entries.ContainsKey(eventID))
        {

            if (this.Fallback != null) return Fallback.GetKOJOMessage(eventID, rel);
            else if (scr_System_CentralControl.current.LogPrefs.DLog_UnimplementedKojo) Debug.Log( "Personality [" + this.DisplayName + "] unimplemented event response for [" + eventID + "] and for target [" + rel.Target.FirstName + "]");
            return null;
        }

        var xx = rel.Owner;
        var yy = rel.Target;
        if (xx == null || yy == null) return null;

        eventID = entries[eventID].CheckRedirect(eventID);
        return entries[eventID].GetResponse(rel, selfTags, targetTags);
    }

    /// <summary>
    /// //////////////////////////////////////////////////////////////////
    /// </summary>
    




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
    public string RedirectID = "";
    public bool requirePlayer = true;


    public string CheckRedirect(string original)
    {
        if (this.RedirectID != "") return this.RedirectID;
        else return original;
    }

    public void ValidateIntegrity()
    {
        foreach(var v in this.variants)
        {
            v.ValidateIntegrity();
        }
    }

    public bool Validate(Character_Trainable owner, Character_Trainable target = null)
    {
        if (interruptSelfJob && owner.CurrentJob != null && (owner.CurrentJob is Job_Sex_Group)) return false;
        if (interruptTargetJob && target != null && target.CurrentJob != null && (target.CurrentJob is Job_Sex_Group)) return false;
        return true;
    }

    public MessageCollect_KojoEntry GetResponse(Character_Relationship rel, List<string> selfTags, List<string> targetTags, EvaluationPackage ep = null)
    {
        if (!Validate(rel.Owner, rel.Target)) return null;

        if (debugLogging && scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log("Validating kojoResponse [" + ID + "] req [" + rel.Owner.FirstName + "->" + rel.Target.FirstName + "], self[" + String.Join("|", selfTags) + "] target[" + String.Join("|", targetTags) + "]");



        MessageCollect_KojoEntry responses = null;
        bool playerInvolved = ep != null && ep.Package != null && ep.Package.actorRefs.Contains(scr_System_CampaignManager.current.Player.RefID);
        if (!requirePlayer) playerInvolved = true;

        foreach (var i in variants)
        {
            if (i.GetRandomResponse(out var result, rel, ep,  selfTags, targetTags, playerInvolved) && result != null)
            {
                if (responses == null) responses = result;
                else responses.nexts.Add(result);
                if (!i.keepLooking) break;
            }
        }
        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents && responses != null) Debug.Log($"kojoResponse [{responses.message}]");
        return responses;
    }

    public MessageCollect_KojoEntry GetResponse(Character_Trainable owner, List<string> selfTags, List<EvaluationPackage> allEPs)
    {
        if (!Validate(owner)) return null;

        Character_Relationship relDoer, relReceiver, relSelf;
        List<int> allChara = null;
        MessageCollect_KojoEntry responses = null, responses2 = null;
        bool isValid = false;
        var newlist = new List<EvaluationPackage>(allEPs);
        foreach (var i in variants)
        {   // allEP might all have different doer receivers. Validate all in variant priority order, if any one is valid, then everyone in EP will get same treatment (cuz here we assume they all acting together

            isValid = false;
            foreach (var ep in newlist)
            {
                relSelf = owner == ep.Doer ? owner.Relationships.FindRelationshipWith(ep.Doer) : null;
                relDoer = owner == ep.Doer ? null : owner.Relationships.FindRelationshipWith(ep.Doer);
                relReceiver = owner == ep.Receiver ? null : owner.Relationships.FindRelationshipWith(ep.Receiver);
                bool playerInvolved = ep != null && ep.Package != null && ep.Package.actorRefs.Contains(scr_System_CampaignManager.current.Player.RefID);

                if (!Validate(owner, ep.Doer)) continue;
                if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log(owner.FirstName + " kojo getresponse past validate before doer/receiver validate, relOwner [" + (relDoer == null ? "null " + (ep.Doer == null ? "" : ep.Doer.FirstName) : relDoer.Owner.FirstName + "->" + relDoer.Target.FirstName) + "] relreceiver[" + (relReceiver == null ? "null " + (ep.Receiver == null ? "" : ep.Receiver.FirstName) : relReceiver.Owner.FirstName + "->" + relReceiver.Target.FirstName) + "]");
                if (relDoer != null && i.GetRandomResponse(out var v1, relDoer, ep,  selfTags, ep.DoerTargetTag, playerInvolved, true))
                {
                    isValid = true;
                    if (v1 == null) break;
                    if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log("finding epdescription " + v1 + ", replace with " + ep.Description_Ongoing);
                    v1.message = v1.message.Replace("$epDescription$", ep.Package.targetCOM.DisplayName(ep.Package.COMVariantID));
                    if (responses == null) responses = v1;
                    else
                    {
                        responses.nexts.Add(v1);
                        responses2 = v1;
                    }
                    break;
                }
                else if (relReceiver != null && i.GetRandomResponse(out var v2, relReceiver, ep, selfTags, ep.ReceiverTargetTag, playerInvolved, true))
                {
                    isValid = true;
                    if (v2 == null) break;
                    if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log("finding epdescription " + v2 + ", replace with " + ep.Description_Ongoing);
                    v2.message = v2.message.Replace("$epDescription$", ep.Package.targetCOM.DisplayName(ep.Package.COMVariantID));
                    if (responses == null) responses = v2;
                    else{
                        responses.nexts.Add(v2);
                        responses2 = v2;
                    }
                    break;
                }
                else if (relSelf != null && relDoer == null && relReceiver == null && ep.Receiver == null &&  i.GetRandomResponse(out var v3, relSelf, ep, selfTags, ep.DoerTargetTag, playerInvolved, true))
                {   // relDoer == null && relReceiver == null
                    // self referencing package, both null, ep.doer is relOwner and ep.receiver == null

                    // i.isValid(relSelf, selfTags, ep.DoerTargetTag, ep) &&

                    isValid = true;
                    //var v = i.GetRandomResponse(relSelf, ep, selfTags, ep.ReceiverTargetTag, playerInvolved, true);
                    if (v3 == null) break;
                    if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log("finding epdescription " + v3 + ", replace with " + ep.Description_Ongoing);
                    v3.message = v3.message.Replace("$epDescription$", ep.Package.targetCOM.DisplayName(ep.Package.COMVariantID));
                    if (responses == null) responses = v3;
                    else{
                        responses.nexts.Add(v3);
                        responses2 = v3;
                    }
                    break;

                }
            }

            if (isValid)
            {
                // do execution
                //Debug.LogError("KOJO CHECKINTERRUPT TRUE IS VALID: " + String.Join("|", responses));

                allChara = new List<int>();
                foreach (var ep in newlist)
                {
                    if (ep.Doer != null && !allChara.Contains(ep.Doer.RefID))
                    {
                        relDoer = owner.Relationships.FindRelationshipWith(ep.Doer);
                        foreach (var ii in i.results) ii.Execute(responses2 != null ? responses2 : responses,  relDoer, selfTags, ep.DoerTargetTag);
                        allChara.Add(ep.Doer.RefID);
                    }
                    if (ep.Receiver != null && !allChara.Contains(ep.Receiver.RefID))
                    {
                        relReceiver = owner.Relationships.FindRelationshipWith(ep.Receiver);
                        foreach (var ii in i.results) ii.Execute(responses2 != null ? responses2 : responses, relReceiver, selfTags, ep.ReceiverTargetTag);
                        allChara.Add(ep.Receiver.RefID);
                    }
                }

                if (!i.keepLooking) break;// String.Join("\n", responses);
            }
        }
        //if (!isValid) Debug.LogError($"cannot find response for {owner.FirstName} on ID {ID}, listEP_count: {allEPs.Count}");


        //var str = String.Join("\n", responses);
        return responses;
    }

    public void RemoveVariantsByTag(string tag)
    {
        this.variants.RemoveAll(x => x.tags.Contains(tag));
    }
    public MessageCollect_KojoEntry GetResponse(Character_Relationship rel, List<EvaluationPackage> selfEPs, List<EvaluationPackage> targetEPs)
    {
        if (!Validate(rel.Owner, rel.Target)) return null;

        List<string> ss1 = new List<string>();
        List<string> ss2 = new List<string>();
        foreach (var i in selfEPs) ss1.Add(i.ToString());
        foreach (var i in targetEPs) ss2.Add(i.ToString());
        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log("Validating kojo req Multiple EPs [" + rel.Owner.FirstName + "->" + rel.Target.FirstName + "], self[" + String.Join("|", ss1) + "] target[" + String.Join("|", ss2) + "]");



        var sTags = new List<string>();
        var tTags = new List<string>();

        MessageCollect_KojoEntry ss = null;


        foreach (var ep in selfEPs)
        {
            sTags = (rel.Owner.RefID == ep.DoerRef ? ep.DoerTargetTag : (rel.Owner.RefID == ep.ReceiverRef ? ep.ReceiverTargetTag : new List<string>()));
            tTags = (rel.Target.RefID == ep.DoerRef ? ep.DoerTargetTag : (rel.Target.RefID == ep.ReceiverRef ? ep.ReceiverTargetTag : new List<string>()));

            ss = GetResponse(rel, sTags, tTags, ep);
            if (ss != null) return ss;
        }

        foreach (var ep in targetEPs)
        {
            sTags = (rel.Owner.RefID == ep.DoerRef ? ep.DoerTargetTag : (rel.Owner.RefID == ep.ReceiverRef ? ep.ReceiverTargetTag : new List<string>()));
            tTags = (rel.Target.RefID == ep.DoerRef ? ep.DoerTargetTag : (rel.Target.RefID == ep.ReceiverRef ? ep.ReceiverTargetTag : new List<string>()));

            ss = GetResponse(rel, sTags, tTags, ep);
            if (ss != null) return ss;
        }

        UtilityEX.GetInteractionTagsFrom(rel.Owner, rel.Target, null, -1, ref sTags, ref tTags, ref tTags);
        return GetResponse(rel, sTags, tTags, null);
    }

    public MessageCollect_KojoEntry GetResponse(Character_Relationship rel, List<string> selfTags, List<string> targetTags)
    {
        if (!Validate(rel.Owner, rel.Target)) return null;
        return GetResponse(rel, selfTags, targetTags, null);
    }

    public class Variant
    {
        public bool requireCOMStrongVariant = false;

        public bool requireSelfDoer = false;
        public bool requireSelfReceiver = false;

        public bool requireSelfOnly = false;

        public bool requireSelfAPDoer = false;
        public bool requireSelfAPReceiver = false;


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
            foreach(var r in this.responses)
            {
                if (r.Length < 1) continue;
                var ss = r.Split(' ');
                if (ss.Length > 1) continue;
                var s2 = r.Split('_');
                if (s2.Length < 2) continue;
                var s3 = r.Split('$');
                if (s3.Length > 1) continue;
                if (LocalizeDictionary.QueryThenParse(r,"error") == "error")
                {
                    Debug.LogError($"Error kojo missing translation for {r}");
                }
            }
            foreach (var v in this.variants)
            {
                v.ValidateIntegrity();
            }
        }
        public bool GetRandomResponse(out MessageCollect_KojoEntry response, Character_Relationship rel, EvaluationPackage ep, List<string> selfTags, List<string> targetTags, bool isPlayerInvolved = false, bool skipExecute = false)
        {
            // first apply cost
            if (!isValid(rel, selfTags, targetTags, ep)) {
                response = null;
                return false;
            }

            MessageCollect_KojoEntry returnV = new MessageCollect_KojoEntry();

            if (this.variants.Count > 0) 
            {
                foreach (var variant in variants)
                {
                    if (variant.GetRandomResponse(out response, rel, ep, selfTags, targetTags, isPlayerInvolved, skipExecute))
                    {
                         if ( !variant.keepLooking) return true;
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

            var sss = selfEventCall == "" ? null : rel.Owner.Relationships.Personality.GetKOJOMessage(selfEventCall, rel, selfTags, targetTags);

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
                    returnV2 = new MessageCollect_KojoEntry();
                    if (!forbidPortaitDisplay)
                    {
                        returnV2.portraitRefID = rel.Owner.RefID;
                        returnV2.portraitTags = new List<string>(extraPortraitTags);
                        if (useActiveTags) returnV2.portraitTags.AddRange(rel.Owner.PortraitManager.GetOwnerActionTagsByPriority());
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

                if (requireSelfAPDoer && !ep.Package.doer.Contains(rel.Owner)) return false;
                if (requireSelfAPReceiver && !ep.Package.receiver.Contains(rel.Owner)) return false;

                if (requireMaster && ep.Package.Master == null) return false;

                if (requireSelfMaster && ep.Package.Master != null && ep.Package.Master != rel.Owner) return false;
                if (requireSelfNotMaster && ep.Package.Master != null && ep.Package.Master == rel.Owner) return false;
            }
            else if (ep == null)
            {
                
            }
            List<string> ttips = new List<string>();

            if (!requirement.Validate(rel, selfTags, targetTags, ep)) return false;

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
            public bool requirePermission = false;
            public List<string> selfTags = new List<string>();
            public List<string> targetTags = new List<string>();
            public bool requireSelfForced = false;
            public int variantID = -1;
            public CharaReq selfReq = null;
            public CharaReq targetReq = null;
            public string requireSelfAttitudeKey = "";
            public RequireKojoVariable requireKojoVariable = new RequireKojoVariable();
            public RequireStatusValue requireSelfStatusValue = new RequireStatusValue();
            public List<RequireStatValue> requireSelfStatValue = new List<RequireStatValue>();
            public RequireMemory requireSelfMemory = new RequireMemory();

            public bool Validate(Character_Trainable self, Character_Trainable target, List<string> selfTags, List<string> targetTags, EvaluationPackage ep, Character_Relationship rel)
            {
                if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log("Validating kojo req [" + (self == null ? "null" : self.FirstName) + "->" + (target == null ? "null" : target.FirstName) + "], self[" + String.Join("|", selfTags) + "] target[" + String.Join("|", targetTags) + "]");

                List<string> tooltips = new List<string>();

                if (selfReq != null && !CharaReqUtility.Validate(selfReq, ref tooltips, self)) return false;
                if (targetReq != null && !CharaReqUtility.Validate(targetReq, ref tooltips, target)) return false;

                if (requireSelfAttitudeKey != "")
                {
                    if (rel == null) return false;
                    var att = rel.GetCurrentAttitude();
                    if (att == null || att.MainEmotionKey != requireSelfAttitudeKey) return false;
                }

                if (ep != null)
                {
                    if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log($"validating EP reqFailure? {requireEPFailure && ep.Response > Memory_Response.Refuse} reqSuccess? {!requireEPFailure && requireEPSuccess && ep.Response < Memory_Response.Accept} ");
                    if (requireEPFailure && (ep.Package.requestAccepted || ep.Response != Memory_Response.Refuse)) return false;
                    else if (!requireEPFailure && requireEPSuccess && (!ep.Package.requestAccepted || ep.Response < Memory_Response.Accept) ) return false;
                    if (requirePermission && !ep.hasPermission) return false;

                    if (requireEPSuccessGTE != Memory_Response.None && ep.Response < requireEPSuccessGTE) return false;
                    if (requireSelfAttitudeGTE != Memory_Attitude.None && ep.GetActorAttitude(self.RefID) < requireSelfAttitudeGTE) return false;

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
                if (requireSelfForced)
                {
                    //Debug.Log($"requireSelfForced on {rel.Owner.CallName}");
                    if (rel.Owner.cannotRefuse || rel.Owner.isImprisoned)
                    {

                    } else if (ep != null && ep.Package.isForced)
                    {

                    } else return false;
                }
                //if (selfTags.Count < this.selfTags.Count) return false;
                if (this.selfTags.Count > 0 && !Utility.ListContainsStrict(selfTags, this.selfTags)) return false;
                //if (targetTags.Count < this.targetTags.Count) return false;
                if (this.targetTags.Count > 0 && !Utility.ListContainsStrict(targetTags, this.targetTags)) return false;
                if (this.variantID > -1 && (ep == null || ep.VariantID != this.variantID)) return false;
                if (this.requireKojoVariable != null && this.requireKojoVariable.isValid && rel != null && !this.requireKojoVariable.Validate(rel)) return false;
                if (this.requireSelfStatValue.Count > 0) {
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
            public bool Validate(Character_Relationship rel, List<string> selfTags, List<string> targetTags, EvaluationPackage ep)
            {
                //var v = 
                //if (!v && (rel.ownerRefID == 0 || rel.TargetID == 0)) Debug.LogError("failed validation");
                return Validate(rel.Owner, rel.Target, selfTags, targetTags, ep, rel);
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
                [JsonIgnore] public bool isValid { get {
                        if (this.statusID == "") return false;
                        if (!checkExistOnly && operand == LogicalOperand.none) return false;
                        if (checkSeverityIndex && value < 0) return false;
                        return true; } }
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
            public ModStatusValue modifyStatusValue = new ModStatusValue();
            public bool flushLog = false;
            public void Execute(MessageCollect_KojoEntry message, Character_Relationship rel, List<string> selfTags, List<string> targetTags)
            {
                if (flushLog)
                {
                    scr_System_CampaignManager.current.AddLogSingle(message);
                }
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

            public class EventInitializer
            {
                public string eventID = "";
                public string eventLabel = "";
                public bool reverseTargets = false;
                public string targetKeyword = "";

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
                public void Execute(Character_Relationship rel)
                {
                    if (isSetValue) rel.Owner.Relationships.SetKojoVariable(isDailyVariable, rel, variableID, value);
                    else rel.Owner.Relationships.ModKojoVariable(isDailyVariable, rel, variableID, value);
                }
            }
        }
    }
}