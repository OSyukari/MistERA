using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using UnityEngine;

[System.Serializable]
public class Character_Personality_Index : I_IndexHasID, I_IndexMergeable, I_NeedLateInitialize, I_RemoveElemByTag, I_RemoveNSFW
{
    public List<Character_Personality> list = new List<Character_Personality>();
    public List<Character_Personality_LooseEntry> looselist = new List<Character_Personality_LooseEntry>();
    public List<DressCode> dressCodes = new List<DressCode>();
    public List<DressCodeRule> defaultDressCodes = new List<DressCodeRule>();

    Dictionary<string, Character_Personality> ID_Dictionary = new Dictionary<string, Character_Personality>();
    Dictionary<string, DressCode> DressCode_Dictionary = new Dictionary<string, DressCode>();
    public void RegisterAllID(List<string> messages)
    {
        messages.Add("Character_Personality_Index : registering ID with list length [" + list.Count+ "]") ;

        foreach (Character_Personality o in this.list)
        {
            if (string.IsNullOrEmpty(o.ID)) continue;
            if (!ID_Dictionary.TryAdd(o.ID, o))  Debug.Log($"error registering personality {o.ID} failed");
        }

        foreach(var i in looselist)
        {
            if (!ID_Dictionary.ContainsKey(i.ID)) Debug.LogError($"error registering personality {i.ID} loose entry failed");
            else
            {
                ID_Dictionary[i.ID].AddEntry(i.Entry);
            }
        }

        DressCode_Dictionary.Clear();
        if (dressCodes != null)
        {
            foreach (var dc in dressCodes)
            {
                if (dc == null || string.IsNullOrEmpty(dc.ID)) continue;
                DressCode_Dictionary[dc.ID] = dc;
            }
        }

        // Deduplicate defaultDressCodes by ID (later entry overrides earlier entry so mods can override defaults).
        if (defaultDressCodes != null && defaultDressCodes.Count > 1)
        {
            var seen = new Dictionary<string, int>();
            for (int i = 0; i < defaultDressCodes.Count; i++)
            {
                var rule = defaultDressCodes[i];
                if (rule == null || string.IsNullOrEmpty(rule.ID)) continue;
                if (seen.TryGetValue(rule.ID, out int prev)) defaultDressCodes[prev] = null;
                seen[rule.ID] = i;
            }
            defaultDressCodes.RemoveAll(r => r == null);
        }
    }
    
    public void MergeWith(I_IndexMergeable list){
        var l = list as Character_Personality_Index;
        if (l == null) return;
        else
        {
            if (l.list != null) this.list.AddRange(l.list);
            if (l.looselist != null) this.looselist.AddRange(l.looselist);
            if (l.dressCodes != null) this.dressCodes.AddRange(l.dressCodes);
            if (l.defaultDressCodes != null) this.defaultDressCodes.AddRange(l.defaultDressCodes);
        }
    }

    public Character_Personality GetByID(string id) { return ID_Dictionary.ContainsKey(id) ? ID_Dictionary[id] : null; }

    public DressCode GetDressCodeByID(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        return DressCode_Dictionary.TryGetValue(id, out var dc) ? dc : null;
    }

    public DressCode ResolveDefaultDressCode(List<string> actorTags)
    {
        var best = DressCodeRule.SelectBest(defaultDressCodes, actorTags);
        if (best == null) return DressCode.Empty;
        var dc = best.ResolveDressCode();
        if (dc != null) return dc;
        if (best.requirements != null)
        {
            return new DressCode
            {
                ID = best.ID,
                allOf = new List<OutfitRequirements> { best.requirements }
            };
        }
        return DressCode.Empty;
    }

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
        public List<string> tags = new List<string>();

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

    // Authoring-only guidance for this personality; deliberately not inherited through fallbackID.
    public List<string> authoringInstructions = new List<string>();

    // Fallback Reference 
    [JsonProperty] private string fallbackID = "";
    private Character_Personality fallbackRef = null;
    private Character_Personality Fallback { get {
        if (fallbackRef == null && fallbackID != "" && fallbackID != this.id) fallbackRef = scr_System_Serializer.current.MasterList.Character_Personalities.GetByID(fallbackID);
        return fallbackRef; } }
    [JsonIgnore] public Character_Personality FallbackPersonality { get { return Fallback; } }

    /// <summary>Optional dress code ID in Character_Personalities.dressCodes. Empty inherits from Fallback, then falls back to defaultDressCodes.</summary>
    public string dressCodeID = "";

    [JsonIgnore] public string EffectiveDressCodeID
    {
        get
        {
            if (!string.IsNullOrEmpty(dressCodeID)) return dressCodeID;
            var visited = new HashSet<Character_Personality>();
            for (var p = this; p != null && visited.Add(p); p = p.Fallback)
                if (!string.IsNullOrEmpty(p.dressCodeID)) return p.dressCodeID;
            return "";
        }
    }

    [JsonIgnore] public DressCode ExplicitDressCode
    {
        get
        {
            var id = EffectiveDressCodeID;
            if (string.IsNullOrEmpty(id)) return null;
            return scr_System_Serializer.current?.MasterList?.Character_Personalities?.GetDressCodeByID(id);
        }
    }

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

    public List<PersonalityAcceptanceMod> AcceptanceMods = new List<PersonalityAcceptanceMod>();

    /// <summary>
    /// Filters AcceptanceMods, plus the caller-supplied memberTypeMods / memberTypeMods2 (e.g. the character's
    /// CurrentActiveMemberType and CurrentLocaleMemberType AcceptanceMods), and their nested Children recursively,
    /// down to the ones fully valid (self/target/faction/Kojo) for this self/target/EP context, writing into the
    /// caller-owned results list instead of allocating a new one. A node only has its children checked if it
    /// validates itself (pruning), and only nodes with their own Result get added — pure branch nodes are invisible
    /// in the output. Both member-type lists (and activityMods - the character's CurrentActiveActivity's, a recreation
    /// visit's) are folded into the same results.Clear()'d list in one pass so none overwrites another; callers must not
    /// call this multiple times into the same results list.
    /// </summary>
    public void CollectApplicableAcceptanceMods(Character_Trainable self, bool isDoer, Character_Trainable target, EvaluationPackage ep, ref List<string> tooltip, List<PersonalityAcceptanceMod> results, List<PersonalityAcceptanceMod> memberTypeMods = null, List<PersonalityAcceptanceMod> memberTypeMods2 = null, List<PersonalityAcceptanceMod> activityMods = null)
    {
        results.Clear();
        if (AcceptanceMods != null)
            foreach (var mod in AcceptanceMods)
                CollectAcceptanceModRecursive(mod, self, target, ep, ref tooltip, results);

        if (memberTypeMods != null)
            foreach (var mod in memberTypeMods)
                CollectAcceptanceModRecursive(mod, self, target, ep, ref tooltip, results);

        if (memberTypeMods2 != null)
            foreach (var mod in memberTypeMods2)
                CollectAcceptanceModRecursive(mod, self, target, ep, ref tooltip, results);

        if (activityMods != null)
            foreach (var mod in activityMods)
                CollectAcceptanceModRecursive(mod, self, target, ep, ref tooltip, results);
    }

    private void CollectAcceptanceModRecursive(PersonalityAcceptanceMod node, Character_Trainable self, Character_Trainable target, EvaluationPackage ep, ref List<string> tooltip, List<PersonalityAcceptanceMod> results)
    {
        if (node == null || results.Contains(node) || !node.Validate(self, target, ep, ref tooltip, out _)) return;

        if (node.Result_ActionPackage != null || node.Result_EvaluationPackage != null) results.Add(node);

        if (node.AcceptanceMods != null)
            foreach (var child in node.AcceptanceMods)
                CollectAcceptanceModRecursive(child, self, target, ep, ref tooltip, results);
    }

    /// <summary>
    /// Default dialogue event ID -> event IDs played instead when this personality's character is the one talked to
    /// (tab_dialogue.Button_DialogueEvent via EventUtility.ResolveDialogueOverride). Tried in order, the first valid one
    /// runs; none valid -> the default event. A list here replaces the fallback personality's list for that event.
    /// Template data, never saved.
    /// </summary>
    public Dictionary<string, List<string>> dialogueEventOverrides = new Dictionary<string, List<string>>();

    /// <summary>
    /// This personality's override event IDs for defaultEventID (dialogueEventOverrides), else its fallback personality's
    /// (recursively). Empty when nobody in the chain overrides it.
    /// </summary>
    public List<string> GetDialogueEventOverrides(string defaultEventID)
    {
        if (dialogueEventOverrides != null && dialogueEventOverrides.TryGetValue(defaultEventID, out var ids) && ids != null) return ids;
        if (Fallback != null) return Fallback.GetDialogueEventOverrides(defaultEventID);
        return new List<string>();
    }

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
    [JsonProperty] List<ResponseEntry> entries_list = new List<ResponseEntry>();
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

        foreach(var i in this.pride_increase)
        {
            for(int j = i.Value.mods.Count - 1; j >= 0; j--)
            {
                if (i.Value.mods[j].tags.Count > 0 && Utility.ListContainsLoose(scr_System_Serializer.current.nsfwKeywords, i.Value.mods[j].tags)) i.Value.mods.RemoveAt(j);
            }
        }
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
            entry.RefreshDisabled();
            if (scr_System_Serializer.current.Debug_KojoIntegrityCheck) entry.ValidateIntegrity();
        }
    }

    /// <summary>
    /// An upgrade command (French kiss...) speaks its own lines when it has any that fit, else its parent's (kiss) -
    /// requireCOMStrongVariant lines there apply, the upgrade being the strong round (EvaluationPackage.isStrongP).
    /// </summary>
    public MessageCollect_KojoEntry GetKOJOMessage(KojoCollector kol)
    {
        var message = GetKOJOMessage_Command(kol);
        if (kol.Owner.RefID == 0 || HasKojoText(message)) return message;

        var com = scr_System_Serializer.current.MasterList.COMs.GetByID(kol.eventID);
        if (com == null || !com.IsUpgrade) return message;
        var upgradeID = kol.eventID;
        kol.eventID = com.upgradeFrom;
        var parentMessage = GetKOJOMessage(kol);
        if (HasKojoText(parentMessage)) return parentMessage;
        kol.eventID = upgradeID;
        return message;
    }

    /// <summary>
    /// Whether this personality (or its fallback chain) authors at least one response for key, following RedirectID.
    /// Static check: variant requirements are not evaluated. An entry with only empty responses counts as missing.
    /// </summary>
    public bool HasKojoEntry(string key)
    {
        if (entries.TryGetValue(key, out var entry))
        {
            var redirect = entry.CheckRedirect(key);
            if (redirect != key && !entries.TryGetValue(redirect, out entry)) return false;
            return entry.HasResponses();
        }
        return Fallback != null && Fallback.HasKojoEntry(key);
    }

    static bool HasKojoText(MessageCollect_KojoEntry message)
    {
        if (message == null) return false;
        var texts = new List<string>();
        message.DumpMessage(texts);
        return texts.Count > 0;
    }

    /// <summary> Reused by GetKOJOMessage_Command (consumed before any recursive lookup). </summary>
    static readonly List<string> kojoKeyBuffer = new List<string>();

    MessageCollect_KojoEntry GetKOJOMessage_Command(KojoCollector kol)
    { if (kol.Owner.RefID == 0) return null;

        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log($"Character_Personality GetKOJOMessage evID[{kol.eventID}{kol.suffix}] [{(kol.Owner.FirstName)}{(kol.Target == null ? "" : $" -> {kol.Target.FirstName}")}]\nSelftags: {String.Join(" ", kol.SelfTags)}\nTargetTags: {String.Join(" ", kol.TargetTags)}");

        // a command tries its own ID, then its kojo source's (Docs/Interactions/Plan_GeneratedCommands.md §4.3);
        // anything that isn't a command (events, triggers) is the one key
        var com = scr_System_Serializer.current.MasterList.COMs.GetByID(kol.eventID);
        string key = null;
        if (com != null)
        {
            kojoKeyBuffer.Clear();
            com.GetKojoKeys(kojoKeyBuffer, kol.VariantID);
            for (int i = 0; i < kojoKeyBuffer.Count && key == null; i++)
            {
                var k = kojoKeyBuffer[i] + kol.suffix;
                if (entries.ContainsKey(k)) key = k;
            }
            kojoKeyBuffer.Clear();
        }
        else if (entries.ContainsKey(kol.eventID + kol.suffix)) key = kol.eventID + kol.suffix;

        if (key == null)
        {
            // command fallback: the folder parent (the source's, for a command that doesn't use its own kojo)
            var parent = com?.KojoFolderParent;
            // an upgrade without its own lines falls back to its parent's in GetKOJOMessage, not to its folder parent
            if (com != null && parent != null && !com.IsUpgrade)
            {
                var backup = kol.eventID;
                kol.eventID = parent.ID;
                var parentkey = $"{kol.eventID}{kol.suffix}";
                if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log($"Kojomessage miss on com {backup}{kol.suffix}, hasparent, checking has [{parentkey}] {entries.ContainsKey(parentkey)}");

                if (entries.ContainsKey(parentkey))
                {
                    parentkey = entries[parentkey].CheckRedirect(parentkey);
                    var parentMessage = entries[parentkey].GetResponse(kol);
                    if (parentMessage != null || Fallback == null) return parentMessage;
                    // no branch matched: the fallback personality answers, from the command itself
                    kol.eventID = backup;
                    return Fallback.GetKOJOMessage(kol);
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
            return Fallback != null ? Fallback.GetKOJOMessage(kol) : null;
        }

        var eventID = entries[key].CheckRedirect(key);

        if (scr_System_CentralControl.current.LogPrefs.DLog_KojoEvents) Debug.Log($"Character_Personality GetKOJOMessage evID[{kol.eventID}{kol.suffix}] [{(kol.Owner.FirstName)}{(kol.Target == null ? "" : $" -> {kol.Target.FirstName}")}] self validation success, proceeding to redirectID {eventID}");

        var message = entries[eventID].GetResponse(kol);
        // an own entry with no matching branch doesn't silence the fallback personality
        if (message == null && Fallback != null) return Fallback.GetKOJOMessage(kol);
        return message;
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

        var key = entries[eventID].CheckRedirect(eventID);
        var message = entries[key].GetResponse(rel, xxEPs, yyEPs);
        if (message == null && Fallback != null) return Fallback.GetKOJOMessage(eventID, rel);
        return message;
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


        var key = entries[eventID].CheckRedirect(eventID);
        var message = entries[key].GetResponse(rel, selfTags, targetTags);
        // same fallback call as a missing key above
        if (message == null && Fallback != null) return Fallback.GetKOJOMessage(eventID, rel);
        return message;
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
