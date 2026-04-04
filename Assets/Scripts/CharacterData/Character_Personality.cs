using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class Character_Personality_Index : I_IndexHasID, I_IndexMergeable, I_NeedLateInitialize, I_RemoveElemByTag, I_RemoveNSFW
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

    public void RemoveNSFW()
    {
        foreach(var i in list)
        {
            i.RemoveNSFWEntry();
        }
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

[System.Serializable]
public class PrideMod
{
    public class ModSingle
    {
        public List<string> selfTags = new List<string>();
        public List<string> comTags = new List<string>();

        public double mult = 1;
        public double threshold = 0;
    }

    public List<ModSingle> mods = new List<ModSingle>();
    public bool Match(List<string> self, List<string> com)
    {
        foreach(var mod in mods)
        {
            if (Utility.ListContainsStrict(self, mod.selfTags) && Utility.ListContainsStrict(com, mod.comTags))
            {
                return true;
            }
        }
        return false;
    }
    public float Match(List<string> self, List<string> com, int prideLevelMult, float amount)
    {
        foreach (var mod in mods)
        {
            if (Utility.ListContainsStrict(self, mod.selfTags) && Utility.ListContainsStrict(com, mod.comTags))
            {
                if (Math.Abs(amount) < mod.threshold) continue;
                float am = amount * (float)prideLevelMult * (float)mod.mult;
                if (Math.Abs(am) >= mod.threshold) return am;
            }
        }
        return 0;
    }
}

[System.Serializable]
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

    [JsonProperty] string behaviorID = "";

    FindJobNodeRoot _behavior = null;
    [JsonIgnore]
    public FindJobNodeRoot Behavior { get
        {
            if (_behavior == null)
            {
                if (behaviorID != "") _behavior = scr_System_Serializer.current.MasterList.FindJobNodeRoots.GetByID(behaviorID);
                else if (Fallback != null) _behavior = Fallback.Behavior;
            }
            return _behavior;
        } }

    public double maxPrideValue = 100;

    public Dictionary<PrideLevel, PrideMod> pride_increase = new Dictionary<PrideLevel, PrideMod>();
    public Dictionary<PrideLevel, PrideMod> pride_decrease = new Dictionary<PrideLevel, PrideMod>();
    /*
    public PrideMod pride_high_inc = null;
    public PrideMod pride_high_dec = null;
    public PrideMod pride_low_inc = null;
    public PrideMod pride_low_dec = null;
    public PrideMod pride_none_inc = null;
    public PrideMod pride_none_dec = null;
    */
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

    public void RemoveNSFWEntry()
    {
        for (int j = this.entries_list.Count - 1; j >= 0; j--)
        {
            var i = this.entries_list[j];
            if (i.tags.Count > 0 && Utility.ListContainsLoose(scr_System_Serializer.current.nsfwKeywords, i.tags))
            {
                this.entries_list.RemoveAt(j);
            }
        }
        this.pride_decrease.Clear();
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
           // if (this.Fallback != null) return Fallback.GetKOJOMessage(eventID, selfEPs, targetEPs, rel);
           // else if (scr_System_CentralControl.current.LogPrefs.DLog_UnimplementedKojo) Debug.Log( "Personality [" + this.DisplayName + "] unimplemented event response for [" + eventID + "] and for target [" + rel.Target.FirstName + "]");
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


    public MessageCollect_KojoEntry GetKOJOMessage(KojoCollector kol)
    { if (kol.Owner.RefID == 0) return null;
        var key = $"{kol.eventID}{kol.suffix}";

        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log($"Character_Personality GetKOJOMessage evID[{kol.eventID}{kol.suffix}] [{(kol.Owner.FirstName)}{(kol.Target == null ? "" : $" -> {kol.Target.FirstName}")}]\nSelftags: {String.Join(" ", kol.SelfTags)}\nTargetTags: {String.Join(" ", kol.targetTags)}");

        if (!entries.ContainsKey(key))
        {
            // command fallback
            var com = scr_System_Serializer.current.MasterList.COMs.GetByID(kol.eventID);
            if (com != null && com.ParentCOM != null)
            {
                var backup = kol.eventID;
                kol.eventID = com.ParentCOM.ID;
                var parentkey = $"{kol.eventID}{kol.suffix}";
                if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log($"Kojomessage miss on com {key}, hasparent, checking has [{parentkey}] {entries.ContainsKey(parentkey)}");

                if (entries.ContainsKey(parentkey))
                {
                    parentkey = entries[parentkey].CheckRedirect(parentkey);
                    return entries[parentkey].GetResponse(kol);
                }
                else
                {
                    kol.eventID = backup;
                }
            }

            if (this.Fallback != null) return Fallback.GetKOJOMessage(kol);
            else if (scr_System_CentralControl.current.LogPrefs.DLog_UnimplementedKojo) Debug.Log("Personality [" + this.DisplayName + "] unimplemented event response for [" + kol.eventID + "] and for target [" + kol.Target.FirstName + "]");
            return null;
        }
        if (!entries[key].Validate(kol.Owner))
        {
            if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log($"Character_Personality GetKOJOMessage evID[{kol.eventID}{kol.suffix}] [{(kol.Owner.FirstName)}{(kol.Target == null ? "" : $" -> {kol.Target.FirstName}")}] self validation failed");
            return null;
        }

        var eventID = entries[key].CheckRedirect(key);

        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log($"Character_Personality GetKOJOMessage evID[{kol.eventID}{kol.suffix}] [{(kol.Owner.FirstName)}{(kol.Target == null ? "" : $" -> {kol.Target.FirstName}")}] self validation success, proceeding to redirectID {eventID}");

        return entries[eventID].GetResponse(kol);
    }

    /// <summary>
    /// New API replacing GetKOJOMessage(string, Character_Trainable, List&lt;string&gt;, List&lt;EvaluationPackage&gt;).
    /// Caller sets kol.eventID and kol.selfTags, then passes allEPs.
    /// Iterates EPs to resolve the appropriate target, loads EP data into a kol copy, and delegates to GetKOJOMessage(KojoCollector).
    /// </summary>
    public MessageCollect_KojoEntry GetKOJOMessage(KojoCollector kol, List<EvaluationPackage> allEPs)
    {
        if (kol.Owner.RefID == 0) return null;

        foreach (var ep in allEPs)
        {
            // Owner observing Doer (owner is not the Doer)
            if (ep.Doer != null && ep.Doer != kol.Owner)
            {
                var attempt = kol.Copy();
                attempt.LoadEP(ep, ep.Doer);
                var result = GetKOJOMessage(attempt);
                if (result != null) return result;
            }

            // Owner observing Receiver (owner is not the Receiver)
            if (ep.Receiver != null && ep.Receiver != kol.Owner)
            {
                var attempt = kol.Copy();
                attempt.LoadEP(ep, ep.Receiver);
                var result = GetKOJOMessage(attempt);
                if (result != null) return result;
            }

            // Self-referencing: owner IS the Doer with no Receiver
            if (ep.Doer == kol.Owner && ep.Receiver == null)
            {
                var attempt = kol.Copy();
                attempt.LoadEP(ep, null);
                var result = GetKOJOMessage(attempt);
                if (result != null) return result;
            }
        }

        return null;
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
