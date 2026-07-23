using Microsoft.Extensions.ObjectPool;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.EventSystems;


public static class UtilityEX
{                                               //  F      E      D      C      B      A      S    
    public static float[] Ranking_V_depth = {  0f, 5.0f,  7.0f,  9.0f, 11.0f, 13.0f, 15.0f };
                                  //length      //  F      E      D      C      B      A      S  
    public static float[] Ranking_P_depth = {  0f, 6.0f,  9.0f, 12.0f, 15.0f, 18.0f, 21.0f };
    //Default C   //canal+rectum -> colon       //  F      E      D      C      B      A      S  
    public static float[] Ranking_A_depth = { 0f, 10.0f, 12.0f, 14.0f, 16.0f, 18.0f, 20.0f };
                        //need deepthroat       //  F      E      D      C      B      A      S  
    public static float[] Ranking_M_depth = {  0f, 5.0f,  6.0f,  7.0f,  8.0f,  9.0f, 10.0f };

    public static float Ranking_V_depth_Step = 2.0f;
    public static float Ranking_A_depth_Step = 2.0f;
    public static float Ranking_M_depth_Step = 1.0f;
    public static float Ranking_P_depth_Step = 3.0f;

    // female 165 male 175

    //birth 20cm?                              //  F      E      D      C      B      A      S    
    public static float[] Ranking_V_size = {  0f, 3.0f,  6.0f,  9.0f, 12.0f, 15.0f, 18.0f };
                                //circumf      //  F      E      D      C      B      A      S  
    public static float[] Ranking_P_size = {  0f, 6.0f,  8.0f, 10.0f, 12.0f, 14.0f, 16.0f };
                              //default F      //  F      E      D      C      B      A      S  
    public static float[] Ranking_A_size = {  0f, 2.0f,  4.0f,  6.0f,  8.0f, 10.0f, 12.0f };
                              //default F      //  F      E      D      C      B      A      S  
    public static float[] Ranking_M_size = {  0f, 3.0f,  4.5f,  6.0f,  7.5f,  9.0f, 10.5f };

    public static float Ranking_V_size_Step = 3.0f;
    public static float Ranking_A_size_Step = 2.0f;
    public static float Ranking_M_size_Step = 1.5f;
    public static float Ranking_P_size_Step = 2.0f;

    // cum 3.25+-1.75 ml
    public static bool CTRL { get
        {
            return Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        } }


    public static Color ColorFromHex(string hex)
    {
        // Remove the # if it exists
        hex = hex.StartsWith("#") ? hex.Substring(1) : hex;

        // Ensure we have 8 characters (RRGGBBAA)
        // If you only provide 6 (RRGGBB), we'll assume Alpha is 255 (opaque)
        if (hex.Length == 6) hex += "FF";

        if (hex.Length != 8)
        {
            throw new ArgumentException("Hex color must be in RRGGBBAA format.");
        }

        // Convert hex pairs to bytes
        byte r = Convert.ToByte(hex.Substring(0, 2), 16);
        byte g = Convert.ToByte(hex.Substring(2, 2), 16);
        byte b = Convert.ToByte(hex.Substring(4, 2), 16);
        byte a = Convert.ToByte(hex.Substring(6, 2), 16);

        return new Color(r, g, b, a);
    }


    public static Color32 UI_SelfColor = new Color32(45, 54, 255, 80);
    public static Color32 UI_HostileColor = new Color32(255, 0, 52, 80);

    public static I_IsJobGiver GetActiveFactionFrom(List<Character_Trainable> cs)
    {
        Dictionary<I_IsJobGiver, int> factions = new Dictionary<I_IsJobGiver, int>();
        foreach (var c in cs)
        {
            I_IsJobGiver key = c.FactionManager.CurrentActiveParty != null ? c.FactionManager.CurrentActiveParty : c.FactionManager.CurrentlyActiveFaction;
            if (key == null) continue;
            if (factions.ContainsKey(key)) factions[key] += 1;
            else factions[key] = 1;
        }
        if (factions.Any()) return Utility.GetMaxWeightInDict(factions);
        else return null;
    }

    public static bool SHIFT { get { return Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift); } }

    public static string bugReport = "https://discord.gg/XK6vm4xPh5";

    public static JsonSerializerSettings SerializerSettings =
        new JsonSerializerSettings()
        {
            TypeNameHandling = TypeNameHandling.Auto,
            NullValueHandling = NullValueHandling.Ignore,
            Converters = new JsonConverter[] { new JSON_SO_Converter<Character_Trainable>() }
        };

    public static JsonSerializerSettings SerializerSettingsLLM =
        new JsonSerializerSettings()
        {
            TypeNameHandling = TypeNameHandling.None,
            Converters = new JsonConverter[] { new JSON_SO_Converter<Character_Trainable>() },
            NullValueHandling = NullValueHandling.Ignore
        };


    public static bool IsInLabor(Character_Trainable c)
    {
        if (scr_System_CentralControl.current.isSafeMode) return false;
        if (c == null) return false;
        if (c.ReproCycle == null) return false;
        if (!c.ReproCycle.isPregnant) return false;
        if (c.wombs == null || c.wombs.Count < 1) return false;
        foreach (var w in c.wombs)
        {
            if (w.eggs == null || w.eggs.Count < 1) continue;
            foreach (var egg in w.eggs)
            {
                if (egg.State != OvumState.Final) continue;
                return true;
            }
        }
        return false;
    }
    public static SaveFileHolder ReadSaveHolder(FileInfo file)
    {
       using (var sr = new StreamReader(file.FullName))
            using (var reader = new JsonTextReader(sr))
        {
            string _version = string.Empty, _desc = string.Empty, _lang = string.Empty, _path = file.FullName;
            bool _safe = false, _readSafe = false;



            while (reader.Read())
            {
                if (reader.TokenType == JsonToken.PropertyName)
                {
                    switch (reader.Value.ToString())
                    {
                        case "Version":
                            reader.Read();
                            _version = reader.Value?.ToString();
                            break;
                        case "SaveDescription":
                            reader.Read();
                            _desc = reader.Value?.ToString();
                            break;
                        case "Language":
                            reader.Read();
                            _lang = reader.Value?.ToString();
                            break;
                        case "SafeMode":
                            reader.Read();
                            if (reader.TokenType == JsonToken.Boolean) {
                                _safe = (bool) reader.Value;
                            }
                            else
                            {
                                _safe = true;
                            }
                            _readSafe = true;
                            break;
                    }
                    if (_version != string.Empty && _desc != string.Empty && _lang != string.Empty && _readSafe) break;
                }
            }

            var holder = new SaveFileHolder();
            holder.Version = _version;
            holder.Language = _lang;
            holder.SaveDescription = _desc;
            holder.FilePath = _path;
            holder.SafeMode = _safe;
            return holder;
        }
    }

    public static void ApplyOnConsume(BodyInternal_Instance body, List<ItemComponentTemplate_Ingestible.OnUseEffect> onUses)
    {
        if (body == null || body.Owner == null) return;
        if (onUses == null) return;
        
        foreach(var onUse in onUses)
        {
            if (!onUse.isValid) continue;

            switch (onUse.effectID)
            {
                case EffectKeyword.ModStatValue:
                    if (onUse.arguments.Count >= 2 && float.TryParse(onUse.arguments[1], out float statValue))
                    {
                        string statID = onUse.arguments[0];
                        var stat = body.Owner.Stats.GetStatEx(statID);
                        if (stat != null) stat.ModValue(statValue);
                    }
                    break;
                case EffectKeyword.ModStatValuePercent:
                    if (onUse.arguments.Count >= 2 && float.TryParse(onUse.arguments[1], out float percentile))
                    {
                        string statID = onUse.arguments[0];
                        var stat = body.Owner.Stats.GetStatEx(statID);
                        if (stat != null) stat.RestorePercent(percentile);
                    }
                    break;

            }
        }
    }
    public static void ApplyOnConsume(Character_Body body, List<ItemComponentTemplate_Ingestible.OnUseEffect> onUses)
    {
        if (body == null || body.Owner == null) return;
        if (onUses == null) return;

        foreach (var onUse in onUses)
        {
            if (!onUse.isValid) continue;

            switch (onUse.effectID)
            {
                case EffectKeyword.ModStatValue:
                    if (onUse.arguments.Count >= 2 && float.TryParse(onUse.arguments[1], out float statValue))
                    {
                        string statID = onUse.arguments[0];
                        var stat = body.Owner.Stats.GetStatEx(statID);
                        if (stat != null) stat.ModValue(statValue);
                    }
                    break;
                case EffectKeyword.ModStatValuePercent:
                    if (onUse.arguments.Count >= 2 && float.TryParse(onUse.arguments[1], out float percentile))
                    {
                        string statID = onUse.arguments[0];
                        var stat = body.Owner.Stats.GetStatEx(statID);
                        if (stat != null) stat.RestorePercent(percentile);
                    }
                    break;

            }
        }
    }




    public static void CheckExperienceGainNoStimulate(Character_Trainable a, float amount, bool isDoer, List<string> selfTags, List<string> comTags, ExperienceLog m = null)
    {
        if (a == null) return;
        a.Skills.CheckExperienceGain(selfTags, comTags, amount, isDoer, m);
    }


    public static bool MatchAPbyType(ActionPackage ap, string type)
    {
        bool returnValue = false;
        switch (type)
        {
            case "ActionPackage_PathTo": returnValue = ap is ActionPackage_PathTo;break;
            case "ActionPackage_Sex": returnValue = ap is ActionPackage_Sex; break;
            case "ActionPackage_Interaction": returnValue = ap is ActionPackage_Interaction; break;
            case "ActionPackage_Interaction_Instanced": returnValue = ap is ActionPackage_Interaction; break;
            case "ActionPackage_ProductionOrder": returnValue = ap is ActionPackage_ProductionOrder; break;
            case "ActionPackage_Redress": returnValue = ap is ActionPackage_Redress; break;
            case "ActionPackage_Undress": returnValue = ap is ActionPackage_Undress; break;
            default: returnValue = false; break;
        }
        return returnValue;
    }

    /// <summary>
    /// accepted key: hour, minute
    /// </summary>
    /// <param name="key"></param>
    /// <returns></returns>
    public static double SineSample(double cycleLen, RandomSample sample, int value)
    {
        var time = scr_System_Time.current.getCurrentTime();
        switch(sample)
        {
            case RandomSample.worldHour:
                return Math.Sin(Math.PI * time.Hour / cycleLen);
            case RandomSample.worldMinute:
                return Math.Sin(Math.PI * time.Minute / cycleLen);
            case RandomSample.elapsedHour:
                return value == 0 ? 0 : Math.Sin(Math.PI * (value / 60) / cycleLen);
            case RandomSample.elapsedMinute:
                return value == 0 ? 0 : Math.Sin(Math.PI * value / cycleLen);
            default:
                return 0;
        }
    }

    /// <summary>
    /// Deterministic pseudo-random value in [-1, 1], stable for a given (seed, bucket) pair.
    /// Used by RandomVariation_Noise so a status's random "texture" differs per-instance (via seed)
    /// and re-rolls over time (via bucket) without needing any external RNG state.
    /// </summary>
    public static float DeterministicNoise(int seed, int bucket)
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + seed;
            hash = hash * 31 + bucket;
            // xorshift-style mix so nearby seeds/buckets don't produce correlated output
            hash ^= hash << 13;
            hash ^= hash >> 17;
            hash ^= hash << 5;
            uint normalized = unchecked((uint)hash);
            return (normalized / (float)uint.MaxValue) * 2f - 1f;
        }
    }

    public static bool AreMemoryTagsMergeable(List<string> newTags, List<string> lastTags)
    {
        if (newTags.Contains("forbidMerge") || lastTags.Contains("forbidMerge")) return false;
        var returnVal = true;

        returnVal = (newTags.Contains("timestop") == lastTags.Contains("timestop")) && returnVal;
        returnVal = (newTags.Contains("sleeping") == lastTags.Contains("sleeping")) && returnVal;
        returnVal = (newTags.Contains("unconscious") == lastTags.Contains("unconscious")) && returnVal;

        returnVal = (newTags.Contains("safe") == lastTags.Contains("safe")) && returnVal;
        returnVal = (newTags.Contains("unsafe") == lastTags.Contains("unsafe")) && returnVal;
        returnVal = (newTags.Contains("sex") == lastTags.Contains("sex")) && returnVal;
        //returnVal = !(newTags.Contains("sex") && lastTags.Contains("safe")) && returnVal;

        return returnVal || newTags.Contains("mergeWithAll") || lastTags.Contains("mergeWithAll");
    }

    static Regex regex_eventKeyword = new Regex(@"\$[a-zA-Z\._]+\$");
    /// <summary>
    /// This will auto run dictionary query parse on string s<br/>
    /// - Will replace _scopetarget_.name/room<br/>
    /// - will replace _instance_appendstringkey_
    /// </summary>
    /// <param name="owner"></param>
    /// <param name="s"></param>
    /// <param name="separator"></param>
    /// <returns></returns>
    public static string ParseEventEntry(EventInstance owner, string s, string separator = ",")
    {
        var newString = LocalizeDictionary.QueryThenParse( s);
        newString = newString.Replace("$currentTime$", scr_System_Time.current.getCurrentTime().ToString());

        MatchCollection matches = regex_eventKeyword.Matches(newString);
        foreach (var match in matches.ToList())
        {
            var len = match.Value.Length;
            if (len <= 3) continue;
            var kvp = match.Value.Substring(1, len-2).Split('.');
            if (kvp.Length != 2) continue;
            //Debug.Log($"Parse event kvp {String.Join("|", kvp)}");
            Dictionary<string, int> collectDict = new Dictionary<string, int>();
            List<string> collect = new List<string>();
            bool error = false;
            if (kvp[0] == "self" && owner.Self != null)
            {
                if (CollectString(kvp[1], owner.Self, collect))
                {

                }
                else if (CollectNumber(kvp[1], owner.Self, collect))
                {

                }
                else if (CollectCount(kvp[1], owner.Self, collectDict))
                {

                }
                else if (CollectResidual(owner, kvp[0], kvp[1], collect))
                {

                }
                else  error = true;
            }
            else if (owner.Targets.ContainsKey(kvp[0]))
            {
                foreach (var c in owner.Targets[kvp[0]])
                {
                    if (CollectString(kvp[1], c, collect))
                    {

                    }
                    else if (CollectNumber(kvp[1], c, collect))
                    {

                    }
                    else if (CollectCount(kvp[1], c, collectDict))
                    {

                    }
                    else if (CollectResidual(owner, kvp[0], kvp[1], collect))
                    {

                    }
                    else error = true;
                }
            }
            else error = true;

            if (collectDict.Count > 0)
            {
                foreach (var collectkvp in collectDict) collect.Add(LocalizeDictionary.QueryThenParse($"event_targetscope_{kvp[1]}").Replace("$elem1$", collectkvp.Key).Replace("$elem2$", $"{collectkvp.Value}"));
            }
            if (!error) newString = newString.Replace(match.Value, String.Join(separator, collect));
        }

        foreach(var appendStringkey in owner.AppendStrings)
        {
           // Debug.Log($"AppendStringKey {appendStringkey.Key}, values {String.Join("|", appendStringkey.Value)}");
            if (appendStringkey.Value.Count < 1) continue;
            newString = newString.Replace($"${appendStringkey.Key}$", String.Join(",", appendStringkey.Value));
        }

        return newString;
    }

    static bool CollectResidual(EventInstance owner, string key1, string key2, List<string> collect)
    {
        if (owner.AppendStrings.TryGetValue($"{key1}.{key2}", out var value))
        {
            collect.Add(String.Join(", ",value));
            return true;
        }
        return false;
    }

    static bool CollectCount(string key, Character_Trainable c, Dictionary<string, int> collectDict)
    {
        switch (key)
        {
            case "nameCount":
                if (!collectDict.ContainsKey(c.CallName)) collectDict.Add(c.CallName, 1);
                else collectDict[c.CallName] += 1;
                return true;
        }


        return false;
    }

    static bool CollectNumber(string key, Character_Trainable c, List<string> collect)
    {

        // check kojo
        var rel = c.Relationships.FindRelationshipWith(c);
        if (c.Relationships.GetKojoVariableExist(false, rel, key))
        {
            collect.Add($"{c.Relationships.GetKojoVariable(false, rel, key)}");
            return true;
        }
        else if (c.Relationships.GetKojoVariableExist(true, rel, key))
        {
            collect.Add($"{c.Relationships.GetKojoVariable(true, rel, key)}");
            return true;
        }
        return false;
    }

    static bool CollectString(string key, Character_Trainable c, List<string> collect)
    {
        switch (key)
        {
            case "name":
                if (c != null)
                {
                    if (!collect.Contains(c.CallName)) collect.Add(c.CallName);
                }
                return true;
            case "room_name":
                var room = scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID);
                if (room != null)
                {
                    if (!collect.Contains(room.DisplayName)) collect.Add(room.DisplayName);
                }
                return true;
            default:
                return false;
        }
    }

    public static bool ArePackagesEqual(ActionPackage a, ActionPackage b)
    {
        if (a.GetType() != b.GetType()) return false;
        if (a.targetCOM != b.targetCOM) return false;
        if ((a.targetCOM != null ? a.targetCOM.ID : "") != (b.targetCOM != null ? b.targetCOM.ID : "")) return false;
        if (!Utility.ListEquals(a.DoerRefs, b.DoerRefs)) return false;
        return true;
    }

    public static int[] RollStat()
    {
        int[] stat = new int[4]; 
        stat[0] = Utility.Dice(3, 6); stat[1] = Utility.Dice(3, 6); stat[2] = Utility.Dice(3, 6); stat[3] = Utility.Dice(3, 6);
        return stat;
    }

    public static int[] RollStatRepeat(int count)
    {
        int average = 0;
        int[] stat = RollStat();
        int[] bestStat = stat;
        for (int i = 1; i < count; i++)
        {
            stat = RollStat();
            if (stat[0] + stat[1] + stat[2] + stat[3] > average) {
                average = stat[0] + stat[1] + stat[2] + stat[3];
                bestStat = stat;
                Debug.Log("Best Stat update :" + bestStat[0] + " " + bestStat[1] + " " + bestStat[2] + " " + bestStat[3]);
            }

        }
        return bestStat;
    }


    public static bool isClickBelowDragThreshold(PointerEventData eventData)
    {
        float delta = scr_System_CentralControl.current.DisplaySetting.ClickDragForgiveness;
        var a = eventData.position;
        var b = eventData.pressPosition;
        if (Mathf.Abs( a.x - b.x ) < delta && Mathf.Abs(a.y - b.y) < delta) return true;
        return false;
    }

    private static CultureInfo ciPointer = new CultureInfo("en-us");
    public static CultureInfo CultureInfo { get { return ciPointer; } }




    public static DateTime GetCampaignTime()
    {
        return new DateTime(1980, 08, 29, 6, 0, 0);
    }

    //Utility.MacroHandler(@"(1023(64)*2\n(64\e)*2\n(41\e)*4\e(1)*2)*10\e2\n");

    /// <summary>
    /// Receive Macro command, delegate 
    /// Rules: <commandID>\n
    /// example: 32\n31\n30\n
    /// Rules: (suite of commands)*x
    /// example: (32\n31\n30\n)*10
    /// Rules: multiplication\nmultiplication
    /// example: 32\n(32\n31\n30\n)*10\n(32\n31\n30\n)*10           !!!!!!!!!!!!!!!!!!!!!!!!!
    /// Do not put parenthesis inside parenthesis cuz Im lazy.
    /// </summary>
    /// <param name="s"></param>
    /// <param name="count"></param>
    public static void MacroHandler(string s, int iteration = 1){
        if (s.Length < 1) return;
        // add to history log if not repeated

        //Debug.Log("MacroHandler receive string ["+s+"] with iteration ["+iteration+"]");

        // \n \e separator
        // \n neutral, \e skip true

        // https://regex101.com
        // https://www.rexegg.com/regex-quickstart.html
        /*
        // ( number or (number) ) * number (optional \n or \e)
        0.  example (1023(64)*2\n(64\e)*2\n(41\e)*4\e(1)*2)*10\e2\n
        1.  pattern 2 accept (1023(64)*2\n(64\e)*2\n(41\e)*4\e(1)*2)*10\e
        1.1 run 10 times    1023(64)*2\n(64\e)*2\n(41\e)*4\e(1)*2
        1.1.1   pattern 1 accept 1023
        1.1.2   pattern 2 accept (64)*2\n
        1.1.2.1     run 2 times 64
        1.1.2.2     pattern 1 accept 64
        1.1.3   pattern 2 accept (64\e)*2\n
        1.1.3.1     run 2 times 64\e
        1.1.3.2     pattern 1 accept 64\e
        1.1.4   pattern 2 accept (41\e)*4\e
        1.1.4.1     run 4 times 41\e
        1.1.4.2     pattern 1 accept 41\e
        1.1.5   pattern 2 accept (1)*2
        1.1.5.1     run 2 times 1
        1.1.5.2     pattern 1 accept 1
        2.  pattern 1 accept 2\n
        */

        string pattern1 = @"^[0-9]+(\\(n|e))?";
        string pattern2 = @"^\(((\(([0-9]|\*|n|e|\\)+\)\*[0-9]+)*([0-9]|n|e|\*|\\)*)*\)\*[0-9]+(\\(n|e))*";


        // TODO
        // https://stackoverflow.com/questions/546433/regular-expression-to-match-balanced-parentheses
        //string macro_repeat = @"^\((?>\((?<c>)|\)(?<-c>)|[0-9]|\\|n|e|\*)*(?(c)(?!))\)(\*[0-9]+\\?(n|e)?)?";
       // string parenthesis_only = @"^\((?>\((?<c>)|\)(?<-c>)|[0-9]|\\|n|e|\*)*(?(c)(?!))\)";

        Regex rgShort = new Regex(pattern1);
        Regex rgLong = new Regex(pattern2);

        Match matchShort = rgShort.Match(s);
        Match matchLong = rgLong.Match(s);

        int length;

        Regex rg, repeat, body;
        MatchCollection matches;
        Match match;


        for (int i = 0; i < iteration; i++)
        {
            if (matchShort != Match.Empty)
            {
                bool skip = false;
                rg = new Regex(@"^[0-9]+");
                //Debug.Log(matchShort.Value);
                length = matchShort.Value.Length;
                if (matchShort.Value[matchShort.Value.Length - 1] == 'e') skip = true;
                //Debug.Log(rg.Match(matchShort.Value).Value);
                MacroExecute(Int32.Parse(rg.Match(matchShort.Value).Value), skip);
                MacroHandler(s.Substring(length));
            }

            if (matchLong != Match.Empty)
            {
                repeat = new Regex(@"[0-9]+");
                //Debug.Log(matchLong.Value);
                matches = repeat.Matches(matchLong.Value);
                //Debug.Log(matches[matches.Count - 1].Value);
                length = matchLong.Value.Length;
                body = new Regex(@"(\(|\)|[0-9]|\*|n|e|\\)+\)");
                match = body.Match(matchLong.Value);
                MacroHandler(match.Value.Substring(1, match.Value.Length - 2), Int32.Parse(matches[matches.Count - 1].Value));
                MacroHandler(s.Substring(length));
            }
        }
    }


    public static bool DetectConflict(ActionPackage a, ActionPackage b)
    {
        if (a is ActionPackage_Sex || b is ActionPackage_Sex)
        {
            if (a is ActionPackage_Sex) return DetectConflict2(a as ActionPackage_Sex,b);
            else return DetectConflict2(b as ActionPackage_Sex, a);
        }
        else if (a.ComTags.Contains("initSex") || b.ComTags.Contains("initSex"))
        {
            if (a.ComTags.Contains("initSex")) return DetectConflict2(a, b);
            else return DetectConflict2(b, a);
        }
        else
        {
            if (Utility.ListContainsLoose(a.actorRefs, b.actorRefs)) return true;
        }
        return false;
    }

    public static void GetActorNames(in List<int> actorRefs, out List<string> names, out List<string> namesExcept, int exceptID)
    {
        names = new List<string>();
        namesExcept = new List<string>();
        foreach(var i in actorRefs)
        {
            var c = scr_System_CampaignManager.current.FindInstanceByID(i);
            if (c == null) continue;
            names.Add(c.FirstName);
            if(i != exceptID) namesExcept.Add(c.FirstName);
        }

        //Debug.LogError($"Getactornames, |{String.Join(" ", names)}|{String.Join(" ", namesExcept)}| ");
    }


    /// <summary>
    /// DO NOT CALL THIS
    /// </summary>
    /// <param name="a"></param>
    /// <param name="package"></param>
    /// <returns></returns>
    internal static bool DetectConflict2(ActionPackage a, ActionPackage package)
    {
        //Debug.LogError("Sex AP detecting conflict");
        ActionPackage_Sex p2 = package as ActionPackage_Sex;
        Job_Sex_Group jSex =  a.job == null? null : a.job as Job_Sex_Group;
        bool log = scr_System_CentralControl.current.LogPrefs.DLog_APConflict;
        if (p2 == null || jSex == null)
        {
            if (!Utility.ListContainsLoose(a.actorRefs, package.actorRefs)) return false;
            else
            {

                if (package.ComTags.Contains("canbeignored"))
                {
                    //package.AddExtraCOMTags(new List<string>() { "ignored" });
                    package.SetIgnored();

                    if (log) Debug.Log("Detecting ActionPackage_Sex Conflict between " + a.DisplayName + " and " + package.DisplayName + " set to ignored coexist");
                    return false;
                }
                else
                {
                    if (log) Debug.Log("Detecting ActionPackage_Sex Conflict between " + a.DisplayName + " and " + package.DisplayName + " cannot be ignored");
                    return true;
                }

            }
        }
        else
        {   //detecting conflict between 2 sex packages inside a sex job (allow coexist)
            if (Utility.ListContainsLoose(a.actorRefs, package.actorRefs))
            {
                if (a.targetCOM != null && package.targetCOM != null && (Utility.ListContainsStrict(a.actorRefs, package.actorRefs) || Utility.ListContainsStrict(package.actorRefs, a.actorRefs)))
                {
                    if (a.targetCOM.conflictTags.Count > 0 && Utility.ListContainsLoose(package.targetCOM.comTags, a.targetCOM.conflictTags))
                    {
                        if (log) Debug.Log($"Detecting ActionPackage_Sex Conflict between {a.DisplayName} and {package.DisplayName} caught by condition -2\nList 1 [{String.Join(" ", a.targetCOM.conflictTags)}] [{String.Join(" ", package.targetCOM.comTags)}]");
                        return true;
                    }
                    if (package.targetCOM.conflictTags.Count > 0 && Utility.ListContainsLoose(a.targetCOM.comTags, package.targetCOM.conflictTags))
                    {
                        if (log) Debug.Log($"Detecting ActionPackage_Sex Conflict between {a.DisplayName} and {package.DisplayName} caught by condition -2\nList 1 [{String.Join(" ", a.targetCOM.comTags)}] [{String.Join(" ", package.targetCOM.conflictTags)}]");
                        return true;
                    }
                }

                if (Utility.ListContainsLoose(a.DoerRefs, package.DoerRefs) && Utility.ListContainsLoose(a.doerBodyTags, package.doerBodyTags))
                {
                    if (log) Debug.Log("Detecting ActionPackage_Sex Conflict between " + a.DisplayName + " and " + package.DisplayName + " caught by condition 1\nList 1 [" + String.Join(" ", a.DoerRefs) + " " + String.Join(" ", a.doerBodyTags) + "] List 2 [" + String.Join(" ", package.DoerRefs) + " " + String.Join(" ", package.doerBodyTags) + "]");
                    return true;
                }
                if (p2.ReceiverRefs.Count > 0 && Utility.ListContainsLoose(a.ReceiverRefs, p2.ReceiverRefs) && Utility.ListContainsLoose(a.receiverBodyTags, p2.receiverBodyTags))
                {
                    if (log) Debug.Log("Detecting ActionPackage_Sex Conflict between " + a.DisplayName + " and " + package.DisplayName + " caught by condition 2\nList 1 [" + String.Join(" ", a.receiverBodyTags) + "] List 2 [" + String.Join(" ", p2.receiverBodyTags) + "]");
                    return true;
                }

                if (Utility.ListContainsLoose(a.DoerRefs, p2.ReceiverRefs) && Utility.ListContainsLoose(a.doerBodyTags, p2.receiverBodyTags))
                {
                    if (log) Debug.Log("Detecting ActionPackage_Sex Conflict between " + a.DisplayName + " and " + package.DisplayName + " caught by condition 3\nList 1 [" + String.Join(" ", a.doerBodyTags) + "] List 2 [" + String.Join(" ", p2.receiverBodyTags) + "]");
                    return true;
                }
                if (Utility.ListContainsLoose(a.ReceiverRefs, p2.DoerRefs) && Utility.ListContainsLoose(a.receiverBodyTags, p2.doerBodyTags))
                {
                    if (log) Debug.Log("Detecting ActionPackage_Sex Conflict between " + a.DisplayName + " and " + package.DisplayName + " caught by condition 4\nList 1 [" + String.Join(" ", a.receiverBodyTags) + "] List 2 [" + String.Join(" ", p2.doerBodyTags) + "]");
                    return true;
                }
            }
                



            //if (Utility.ListContainsLoose(p2.actorRefs, actorRefs) || p2.actorRefs.Contains(receiver.RefID)) return true;
            //else return false;
            return false;
        }
    }

    public static void StringReplace(ref string s)
    {
        s = s.Replace("$color_error_begin$", $"<color={Utility.HexCOLOR(scr_System_CentralControl.current.DisplaySetting.TextColor_conflict.Color)}>")
            .Replace("$color_error_end$", "</color>");
    }


    public static void StringReplace(Character_Trainable c, ref string s)
    {

        StringReplace(ref s);
        s = s.Replace("$firstname$", c.FirstName);
    }

    public static void StringReplace(ActionPackage ap, ref string s)
    {
        StringReplace(ref s);
        // %% ____ %% refers to static entry in dictionary
        // $ ____ $ refers to local variable
        //Debug.LogError("isDoerNull["+(evp.Doer == null) + "] isReceiverNull[" + (evp.Receiver == null) + "] isMasterNull[" + (evp.Master == null) + "]");

        List<string> doers = new List<string>(), receivers = new List<string>(), actors = new List<string>();

        foreach(var ep in ap.ListEP)
        {
            if (ep.Doer != null) doers.Add(ep.Doer.CallName);
            if (ep.Receiver != null && ep.Receiver != ep.Doer) receivers.Add(ep.Receiver.CallName);
        }
        doers = Utility.Distinct(doers);
        receivers = Utility.Distinct(receivers);
        actors.AddRange(doers);
        actors.AddRange(receivers);
        actors = Utility.Distinct(actors);

        if (doers.Count > 0)
        {
            if (s.Contains("$doer.firstname$")) { s = s.Replace("$doer.firstname$", String.Join(", ", doers)); }
            if (s.Contains("$doer$")) { s = s.Replace("$doer$", String.Join(", ",doers)); }

            s = s.Replace("$doer.p$", LocalizeDictionary.QueryThenParse("bodyPart_internal_penis"));
            s = s.Replace("$doer.v$", LocalizeDictionary.QueryThenParse("bodyPart_internal_vagina"));
        }


        

        if (receivers.Count > 0)
        {
            s = s.Replace("$receiver.firstname$", String.Join(", ", receivers));

            s = s.Replace("$receiver.p$", LocalizeDictionary.QueryThenParse("bodyPart_internal_penis"));
            s = s.Replace("$receiver.v$", LocalizeDictionary.QueryThenParse("bodyPart_internal_vagina"));
        }

        if (ap.Master != null)
        {
            s = s.Replace("$master.firstname$", ap.Master.FirstName);
        }

        if (actors.Count > 0)
        {
            s = s.Replace("$actors.firstname", String.Join(", ", actors));
        }

        if (s.Contains("$comdesc$"))
        {
            Debug.LogError("STRING REPLACE COMDESC [" + s + "] with [" + (ap.targetCOM == null ? "" : ap.targetCOM.DisplayName(ap.COMVariantID)) + "]");
            s = s.Replace("$comdesc$", ap.targetCOM == null ? "" : ap.targetCOM.DisplayName(ap.COMVariantID));
        }

        s = s.Replace("$result$", LocalizeDictionary.QueryThenParse($"Memory_Response_{ap.injectResult}"));

    }

    public static void StringReplace(EvaluationPackage evp, ref string s)
    {
        //if (evp == null) return s;
        StringReplace(ref s);
        bool doerReplaced = false;
        // %% ____ %% refers to static entry in dictionary
        // $ ____ $ refers to local variable
        //Debug.LogError("isDoerNull["+(evp.Doer == null) + "] isReceiverNull[" + (evp.Receiver == null) + "] isMasterNull[" + (evp.Master == null) + "]");
        if (evp.Doer != null )
        {

            if (s.Contains("$doer.firstname$")) { s = s.Replace("$doer.firstname$", evp.Doer.FirstName); doerReplaced = true; }
            if (s.Contains("$doer$")) { s = s.Replace("$doer$", evp.Doer.FirstName); doerReplaced = true; }

            s = s.Replace("$doer.p$", LocalizeDictionary.QueryThenParse("bodyPart_internal_penis"));
            s = s.Replace("$doer.v$", LocalizeDictionary.QueryThenParse("bodyPart_internal_vagina"));

        }

        if (evp.Receiver != null)
        {
            if(doerReplaced && evp.Doer != null && evp.Doer.RefID == evp.Receiver.RefID)s = s.Replace("$receiver.firstname$", LocalizeDictionary.QueryThenParse("Self"));
            else s = s.Replace("$receiver.firstname$", evp.Receiver.FirstName);

            s = s.Replace("$receiver.p$", LocalizeDictionary.QueryThenParse("bodyPart_internal_penis"));
            s = s.Replace("$receiver.v$", LocalizeDictionary.QueryThenParse("bodyPart_internal_vagina"));
        }else if (evp.DoerTargetTag.Contains("masturbate") && evp.Doer != null)
        {
            //Debug.Log("doer masturbating");
            if (doerReplaced) s = s.Replace("$receiver.firstname$", LocalizeDictionary.QueryThenParse("Self"));
            else s = s.Replace("$receiver.firstname$", evp.Doer.FirstName);
        }
        
        if (s.Contains("$receiver$")) s = s.Replace("$receiver$", evp.Receiver == null ? "" : evp.Receiver.FirstName);
        

        if (evp.Master != null)
        {
           s = s.Replace("$master.firstname$", evp.Master.FirstName);
        }

        if (s.Contains("$comdesc$"))
        {
            Debug.LogError("STRING REPLACE COMDESC [" + s + "] with [" + (evp.targetCOM == null ? "" : evp.targetCOM.DisplayName(evp.VariantID)) + "]");
            s = s.Replace("$comdesc$", evp.targetCOM == null ? "" : evp.targetCOM.DisplayName(evp.VariantID));
        }

    }
    private static void MacroExecute(int command, bool skip = false){
        // do command
        Debug.Log("MacroExecute : [" + command + "] do skip [" + skip + "]");
    }

    public static TimeSpan ReinitStopWatch(System.Diagnostics.Stopwatch stopwatch)
    {
        stopwatch.Reset();
        stopwatch.Start();
        return stopwatch.Elapsed;
    }
    public static string LogStopwatch(System.Diagnostics.Stopwatch stopwatch, ref TimeSpan stamp)
    {
        var ns = (stopwatch.Elapsed - stamp).Ticks / TimeSpan.TicksPerMillisecond;
        stamp = stopwatch.Elapsed;
        return ns.ToString("0.0000")+"ms";
    }
      
    public static void GetEventTagsFrom(Character_Trainable a, Character_Trainable b, out List<string> ownerTags, out List<string> targetTags, out List<ActionPackage> ownerAPs)
    {
        // collect all
        ownerTags = new List<string>();
        targetTags = new List<string>();
        ownerAPs = new List<ActionPackage>();

        if (a != null){
            if (a.InteractionJob != null)   a.InteractionJob.GetActorAPTags(a.RefID, ownerTags, ownerAPs);  // get a interactionjob all self
            if (a.CurrentJob != null) a.CurrentJob.GetActorAPTags(a.RefID, ownerTags,  ownerAPs);   // get a currentjob all self
            foreach (var status in a.Stats.StatusInstances) ownerTags.AddRange(status.Tags);    // get a statsinstancekeywords
            GetActorTag(ref ownerTags, a);
            ownerTags = Utility.Distinct(ownerTags);
        }

        if (b != null)
        {
            if (b.InteractionJob != null) b.InteractionJob.GetActorAPTags(b.RefID, targetTags);  // get a interactionjob all self
            if (b.CurrentJob != null) b.CurrentJob.GetActorAPTags(b.RefID, targetTags);  // get a currentjob all self
            foreach (var status in b.Stats.StatusInstances) targetTags.AddRange(status.Tags);    // get a statsinstancekeywords
            GetActorTag(ref targetTags, b);
            targetTags = Utility.Distinct(targetTags);
        }
    }
    public static void GetEPsFrom(Character_Trainable a, Character_Trainable b, out List<EvaluationPackage> ownerEPs, out List<EvaluationPackage> targetEPs)
    {
        // collect all
        ownerEPs = new List<EvaluationPackage>();
        targetEPs = new List<EvaluationPackage>();

        if (a != null)
        {
            if (a.InteractionJob != null) a.InteractionJob.GetActorEPs(a.RefID, ownerEPs);  // get a interactionjob all self
            if (a.CurrentJob != null) a.CurrentJob.GetActorEPs(a.RefID, ownerEPs);   // get a currentjob all self
        }

        if (b != null)
        {
            if (b.InteractionJob != null) b.InteractionJob.GetActorEPs(b.RefID, targetEPs);  // get a interactionjob all self
            if (b.CurrentJob != null) b.CurrentJob.GetActorEPs(b.RefID, targetEPs);  // get a currentjob all self
        }
    }

    public static void GetEPsFrom(Character_Trainable a, out List<EvaluationPackage> EPs)
    {
        // collect all
        EPs = new List<EvaluationPackage>();

        if (a != null)
        {
            if (a.InteractionJob != null) a.InteractionJob.GetActorEPs(a.RefID, EPs);  // get a interactionjob all self
            if (a.CurrentJob != null) a.CurrentJob.GetActorEPs(a.RefID, EPs);   // get a currentjob all self
        }
    }

    public static void GetAPsFrom(Character_Trainable a, out List<ActionPackage> APs)
    {
        // collect all
        APs = new List<ActionPackage>();

        if (a != null)
        {
            if (a.InteractionJob != null) a.InteractionJob.GetActorAPs(a.RefID, APs);  // get a interactionjob all self
            if (a.CurrentJob != null) a.CurrentJob.GetActorAPs(a.RefID, APs);   // get a currentjob all self
        }

        APs = Utility.Distinct(APs);
    }

    public static void GetJobInteractionTagsFrom(Character_Trainable a, Character_Trainable b, COM targetCOM, Job job, ref List<string> ownerTags, ref List<string> extraComTags, ref List<string> extraTargetTags)
    {
        var jobSex = job as Job_Sex_Group;
        bool doer_isr = false;
        bool receiver_isr = false;
        if (a != null && b != null && targetCOM is COM_Sex)
        {
            var rel = b.Relationships.FindRelationshipWith(a);
            if (rel.HasPermission_Intimacy_High())
            {
                //
            }
            else if (jobSex != null && jobSex.isRapist(a) != jobSex.isRapist(b))
            {
                doer_isr = jobSex.isRapist(a);
                receiver_isr = jobSex.isRapist(b);
                //Debug.Log($"GetJobInteractionTagsFrom {a.FirstName} {b.FirstName} 1, {doer_isr} != {receiver_isr}");
            }
            else if (a.isRestrained != b.isRestrained)
            {
                doer_isr = !a.isRestrained;
                receiver_isr = !b.isRestrained;
                //Debug.Log($"GetJobInteractionTagsFrom {a.FirstName} {b.FirstName} 2, {doer_isr} != {receiver_isr}");
            }
            else if (a.isImprisoned != b.isImprisoned)
            {
                doer_isr = !a.isImprisoned;
                receiver_isr = !b.isImprisoned;
                //Debug.Log($"GetJobInteractionTagsFrom {a.FirstName} {b.FirstName} 3, {doer_isr} != {receiver_isr}");
            }
            else if (a.canAct != b.canAct)
            {
                doer_isr = a.canAct;
                receiver_isr = b.canAct;
                //Debug.Log($"GetJobInteractionTagsFrom {a.FirstName} {b.FirstName} 4, {doer_isr} != {receiver_isr}");
            }

            if (doer_isr != receiver_isr)
            {
                ownerTags.Add(doer_isr ? "rape" : "raped");
                extraTargetTags.Add(receiver_isr ? "rape" : "raped");
            }

        }
        ownerTags = ownerTags.Distinct().ToList();
        extraTargetTags = extraTargetTags.Distinct().ToList();
    }


    public static void GetCOMTags(Character_Trainable a, Character_Trainable b, COM com, ref List<string> extraComTags)
    {
        if (com != null)
        {
            extraComTags.AddRange(com.comTags);
        }
        if (extraComTags.Contains("initSex")) extraComTags.Add("sex");

        if (b == null || b == a)
        {
            if (com != null && com.comTags.Contains("interaction")) extraComTags.Add("NonInteraction");
            if (com != null && (com.comTags.Contains("sex") || com.comTags.Contains("service"))) extraComTags.Add("masturbate");
        }
        else
        {
            if (b.canAct && !extraComTags.Contains("interaction") && !extraComTags.Contains("NonInteraction")) extraComTags.Add("interaction");
            else if (!b.canAct)
            {
                extraComTags.Add("NonInteraction");
                extraComTags.Remove("service");
            }
            // if a cannot act then a cannot react then it doesnt make sense to add target gender experience
        }
        extraComTags = Utility.Distinct(extraComTags);
    }

    public static void GetInteractionTagsFrom(Character_Trainable a, Character_Trainable b, COM com, int variantID, ref List<string> ownerTags, ref List<string> extraComTags, ref List<string> extraTargetTags)
    {
        if (com != null)
        {
            extraComTags.AddRange(com.comTags);
            extraComTags.RemoveAll(x => com.requirements.requirement.doerBodyTags.Contains(x));
            extraComTags.RemoveAll(x => com.requirements.requirement.receiverBodyTags.Contains(x));
        }

        // Cuz we'll add the actual used body part in later when the actual part are being chosen

        foreach (var status in a.Stats.StatusInstances) ownerTags.AddRange(status.Tags);
        if(b != null ) foreach (var status in b.Stats.StatusInstances) extraTargetTags.AddRange(status.Tags);

        if (a == null) return;
        GetActorTag(ref ownerTags, a);
        if (extraComTags.Contains("initSex")) extraComTags.Add("sex");

        if (com != null && variantID >= 0 && com.comTags.Contains("sex") && com.variants[variantID].requirements.requirement.receiverCount == 0 && (b == null || b.RefID == a.RefID))
        {
            extraComTags.Add("masturbate");
            //Debug.Log("ADD MASTURBATE");
        }

        if (b == null)
        {
            if (com != null && com.comTags.Contains("interaction")) extraComTags.Add("NonInteraction");
        }
        else if (a.RefID == b.RefID)
        {
            if (com != null && com.comTags.Contains("interaction")) extraComTags.Add("NonInteraction");
            if (com != null && (com.comTags.Contains("sex") || com.comTags.Contains("service"))) extraComTags.Add("masturbate");
        }
        else
        {
            if (b.canAct && !extraComTags.Contains("interaction") && !extraComTags.Contains("NonInteraction")) extraComTags.Add("interaction");
            else if (!b.canAct) extraComTags.Add("NonInteraction");
            GetActorTag(ref extraTargetTags, b);
            // if a cannot act then a cannot react then it doesnt make sense to add target gender experience
        }
        ownerTags = Utility.Distinct(ownerTags);
        extraComTags = Utility.Distinct(extraComTags);
        extraTargetTags = Utility.Distinct(extraTargetTags);

        if (extraTargetTags.Contains("timestop") || extraTargetTags.Contains("sleeping") || extraTargetTags.Contains("unconscious")) extraComTags.Remove("service");
    }

    /// <summary>
    /// Get actor's current status (such as sleeping, timestopped, etc), plus static per-actor keyword tags
    /// (Character_Trainable.ActorKeywords: template actorKeyword + race RaceType).
    /// <br/> for current actions, call another function
    /// </summary>
    /// <param name="tags"></param>
    /// <param name="c"></param>
    public static void GetActorTag(ref List<string> tags, Character_Trainable c)
    {
        if (c == null) return;
        var result = scr_System_CentralControl.current.GetGender(c);
        foreach(var tag in result)
        {
            tags.Add(tag.ToString());
        }
        tags.AddRange(c.ActorKeywords);
        if (scr_System_Time.current.TimeResume && !c.CanActInTimeStop) tags.Add("timeResume");
        else if(c.isTimeStopped) tags.Add("timestop");
        if (c.isSleeping) {
            tags.Add("sleeping");
            tags.Add("unconscious");
        }
        else if (c.Stats.isConsciousnessUnconscious) tags.Add("unconscious");

        tags = Utility.Distinct(tags);
        //if (c.Climaxing) tags.Add("climax");  this will make it too easy to get climax exp
    }

    public static void RemoveConflictTags(ref List<string> tags)
    {
        if (tags.Contains("unsafe"))
        {
            tags.Remove("safe");
            tags.Remove("canbeignored");
        }
    }

    public class ForceJSONSerializePrivatesResolver : Newtonsoft.Json.Serialization.DefaultContractResolver
    {
        protected override IList<Newtonsoft.Json.Serialization.JsonProperty> CreateProperties(System.Type type, MemberSerialization memberSerialization)
        {
            var props = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            List<Newtonsoft.Json.Serialization.JsonProperty> jsonProps = new List<Newtonsoft.Json.Serialization.JsonProperty>();

            foreach (var prop in props)
            {
                jsonProps.Add(base.CreateProperty(prop, memberSerialization));
            }

            foreach (var field in type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance))
            {
                jsonProps.Add(base.CreateProperty(field, memberSerialization));
            }

            jsonProps.ForEach(p => { p.Writable = true; p.Readable = true; });
            return jsonProps;
        }
    }


    public static string ResourcesPath { get { return "\\Resources\\"; } }
    public static void ParseConsoleCommand(string s)
    {
        if (scr_System_CentralControl.current.allusedConsoleCommands.Contains(s)) scr_System_CentralControl.current.allusedConsoleCommands.Remove(s);
        scr_System_CentralControl.current.allusedConsoleCommands.Insert(0,s);

        bool parsedSuccessful = false;
        if (s.Length < 1) return;
        if (s == "") return;

        var parsed = s.Split(" ");
        if (parsed.Count() < 1) return;

        switch (parsed[0])
        {
            case "modstatderived": // modify derived stat's debug modifier
                if (scr_System_CampaignManager.current.CurrentTarget == null) return;
                if (parsed.Count() != 3) return;
                else
                {
                    var targetStat = scr_System_CampaignManager.current.CurrentTarget.Stats.GetDerivedStat(parsed[1]);
                    if (targetStat == null) return;
                    if (int.TryParse(parsed[2], out int numbers))
                    {
                        targetStat.Debug_ModFinalValue(numbers);
                        parsedSuccessful = true;
                    }
                }
                break;
            case "modexperience": // modify derived stat's debug modifier
                if (scr_System_CampaignManager.current.CurrentTarget == null) return;
                if (parsed.Count() != 3) return;
                else
                {
                    if (int.TryParse(parsed[2], out int numbers))
                    {
                        scr_System_CampaignManager.current.CurrentTarget.Skills.debug_experienceLogs.Add(parsed[1], numbers);
                        parsedSuccessful = true;
                    }
                }
                break;
            case "modpersonalityscore":
                if (scr_System_CampaignManager.current.CurrentTarget == null) return;
                if (parsed.Count() != 3) return;
                else
                {

                    if (!int.TryParse(parsed[2], out int numbers)) return;
                    switch (parsed[1])
                    {
                        case "pride":
                            scr_System_CampaignManager.current.CurrentTarget.Relationships.ModPride(numbers);// += numbers;
                            parsedSuccessful = true;
                            break;
                        case "corruption":
                            scr_System_CampaignManager.current.CurrentTarget.Relationships._Corruption += numbers;
                            parsedSuccessful = true;
                            break;
                        case "mood":
                            if(scr_System_CampaignManager.current.CurrentTarget.Stats.Mood != null){
                            scr_System_CampaignManager.current.CurrentTarget.Stats.Mood.DebugSeverityMod += numbers;}
                            parsedSuccessful = true;
                            break;
                        case "stress":
                            if(scr_System_CampaignManager.current.CurrentTarget.Stats.Stress != null) {
                            scr_System_CampaignManager.current.CurrentTarget.Stats.Stress.DebugSeverityMod += numbers;}
                            parsedSuccessful = true;
                            break;
                        case "lust":
                            if(scr_System_CampaignManager.current.CurrentTarget.Stats.Lust != null)
                            {
                            scr_System_CampaignManager.current.CurrentTarget.Stats.Lust.DebugSeverityMod += numbers;
                            }
                            parsedSuccessful = true;
                            break;
                    }
                }
                break;
            case "modrelationshipwith":
                if (scr_System_CampaignManager.current.CurrentTarget == null) return;
                if (parsed.Count() != 4) return;
                else
                {
                    if (!int.TryParse(parsed[1], out int targetRef)) return;
                    if (!int.TryParse(parsed[3], out int number)) return;
                    var relation = scr_System_CampaignManager.current.CurrentTarget.Relationships.FindRelationshipWith(targetRef);
                    if (relation == null) return;
                    switch (parsed[2])
                    {
                        case "trust":
                            relation.ModRelationValue(RelationshipScoreType.Trust, number);
                            parsedSuccessful = true;
                            break;
                        case "fear":
                            relation.ModRelationValue(RelationshipScoreType.Fear, number);
                            parsedSuccessful = true;
                            break;
                        case "goodwill":
                            relation.ModRelationValue(RelationshipScoreType.Goodwill, number);
                            parsedSuccessful = true;
                            break;
                        case "badwill":
                            relation.ModRelationValue(RelationshipScoreType.Badwill, number);
                            parsedSuccessful = true;
                            break;
                        case "desire":
                            relation.ModRelationValue(RelationshipScoreType.Desire, number);
                            parsedSuccessful = true;
                            break;

                    }
                }
                break;
            case "loadevent":
                if (parsed.Count() >= 3 && int.TryParse(parsed[1], out int eventTargetRef))
                {
                    var eventTarget = eventTargetRef == -1 ? null : scr_System_CampaignManager.current.FindInstanceByID(eventTargetRef);
                    string evID = parsed[2];
                    string evLbl = parsed.Count() >= 4 ? parsed[3] : "";
                    scr_UpdateHandler.current.EventHandler.StartEvent(eventTarget, evID, evLbl, true);
                }
                else
                {
                    Debug.LogError($"parse console command {parsed[0]} error");
                }
                break;
            case "ingestItem":
                if (parsed.Count() >= 3 && parsed[1] != "" && parsed[2] != "")
                {
                    var item = scr_System_Serializer.current.GetByNameOrID_Item_Base(parsed[1]);
                    if (item == null)
                    {
                        Debug.LogError($"ingestItem: cannot find item [{parsed[1]}]");
                    }
                    else if (scr_System_CampaignManager.current.CurrentTarget == null)
                    {
                        Debug.LogError($"ingestItem: require currentTarget");
                    }
                    else if(parsed[2] != "" && scr_System_CampaignManager.current.CurrentTarget.Body.HasBodyTag(new List<string>() { parsed[2] }))
                    {
                        Character_Trainable chara = scr_System_CampaignManager.current.CurrentTarget;
                        Item_Instance i = WorldManager.Instantiate(item.id, item.displayName);

                        if (chara == null)
                        {
                            Debug.LogError($"ingestItem: null character");
                        }
                        else if (i == null)
                        {
                            Debug.LogError($"ingestItem: null item");
                        }
                        else if (!chara.Body.ConsumeIngestible(i, parsed[2]))
                        {
                            scr_System_CampaignManager.current.Unregister(i);
                            Debug.LogError($"ingestItem: [{chara.FirstName}] fail to ingest [{i.DisplayName}]");
                        }
                    }
                    else
                    {
                        Debug.LogError($"ingestItem: fail to locate ingestTag [{parsed[2]}]");
                    }
                }
                else
                {
                    Debug.LogError($"parse console command {parsed[0]} error");
                }
                break;
            case "modkojovariable":
                if (parsed.Count() >= 6 && int.TryParse(parsed[1], out int kojoRelSelf) && int.TryParse(parsed[2], out int kojoRelTarget) && int.TryParse(parsed[5], out var value) && bool.TryParse(parsed[4], out bool isdaily))
                {
                    var selfChara = scr_System_CampaignManager.current.FindInstanceByID(kojoRelSelf);
                    var targetRel = selfChara == null ? null : selfChara.Relationships.FindRelationshipWith(kojoRelTarget);
                    if (targetRel != null) selfChara.Relationships.ModKojoVariable(isdaily, targetRel, parsed[3], value);
                }
                else
                {
                    Debug.LogError($"parse console command {parsed[0]} error");
                }
                break;
            case "setCurrentHour":
                if (parsed.Count() >= 2 && int.TryParse(parsed[1], out int targetHour))
                {
                    var advanceHour = (targetHour - scr_System_Time.current.getCurrentTime().Hour) % 24;
                    scr_System_Time.current.UpdateTime(0, advanceHour, 0);
                }
                else
                {
                    Debug.LogError($"parse console command {parsed[0]} error");
                }
                break;
            case "addItem":
                if (parsed.Count() >= 4 && int.TryParse(parsed[1], out int addItemToRef) && int.TryParse(parsed[3], out int addItemCount))
                {
                    var chara = scr_System_CampaignManager.current.FindInstanceByID(addItemToRef);
                    var item = Masterlist_Items.GetByID(parsed[2]);
                    if (item != null && chara != null) 
                    {
                        for(int i = 0; i < addItemCount; i++)
                        {
                            var itemInstance = WorldManager.Instantiate(parsed[2], "", addItemCount);
                            chara.Inventory.AddItem(itemInstance);
                        }
                    }
                }
                break;
            case "inspectjob":
                if (parsed.Count() >= 2 && int.TryParse(parsed[1], out int targetjobref))
                {
                    var job = scr_System_CampaignManager.current.FindJobInstanceByID(targetjobref);
                    scr_System_CentralControl.current.CurrentInspectJob = job;
                }
                break;
            case "resetAllActorJobs":

                scr_System_CampaignManager.current.ResetAllActorJobs();

                break;
            case "spawnChara":
                if (parsed.Count() >= 2)
                {
                    if (scr_System_CampaignManager.current.Player.FactionManager.CurrentlyActiveFaction == null)
                    {
                        Debug.LogError("cannot find relevant player facton to add");
                        break;
                    }
                    if (!scr_System_CampaignManager.current.Player.FactionManager.CurrentlyActiveFaction.ManagedRefs.Contains(0))
                    {
                        Debug.LogError("can only be used when player is in a player-managed faction");
                        break;
                    }

                    var c = scr_System_CampaignManager.current.InstantiateCharacter_FromBaseID(parsed[1], scr_System_CampaignManager.current.CurrentRoom);
                    if (c == null) break;
                    scr_System_CampaignManager.current.party.AddToParty(c);

                    var addTofaction = scr_System_CampaignManager.current.Player.FactionManager.CurrentlyActiveFaction;

                    c.FactionManager.SetTempHomeFaction(addTofaction.ID);
                }
                break;
            case "spawnCharaPrisoner":
                if (parsed.Count() >= 2)
                {
                    if (scr_System_CampaignManager.current.Player.FactionManager.CurrentlyActiveFaction == null)
                    {
                        Debug.LogError("cannot find relevant player facton to add");
                        break;
                    }
                    if (!scr_System_CampaignManager.current.Player.FactionManager.CurrentlyActiveFaction.ManagedRefs.Contains(0))
                    {
                        Debug.LogError("can only be used when player is in a player-managed faction");
                        break;
                    }

                    var c = scr_System_CampaignManager.current.InstantiateCharacter_FromBaseID(parsed[1], scr_System_CampaignManager.current.CurrentRoom);

                    var addTofaction = scr_System_CampaignManager.current.Player.FactionManager.CurrentlyActiveFaction;

                    c.FactionManager.SetTempHomeFaction(addTofaction.ID, Manageable_GuestStatus.Prisoner);
                }
                break;
            case "advReproCycle":
                if (parsed.Count() >= 4)
                {
                    if (int.TryParse(parsed[1], out var adv_year) && int.TryParse(parsed[2], out var adv_month) && int.TryParse(parsed[3], out var adv_day))
                    {
                        var target = scr_System_CampaignManager.current.CurrentTarget;
                        if (target == null)
                        {
                            Debug.LogError("error target null");
                            break;
                        }
                        if (!target.HasMenstrualCycle)
                        {
                            Debug.LogError("error target has no mens cycle");
                            break;
                        }

                        target.TickMenstruation(adv_year, adv_month, adv_day, true);
                    }
                }
                break;
            case "forceBirth":
                if (parsed.Count() >= 4)
                {
                    var target = scr_System_CampaignManager.current.CurrentTarget;
                    if (target == null)
                    {
                        Debug.LogError("error target null");
                        break;
                    }
                    if (target.wombs == null || target.wombs.Count < 1)
                    {
                        Debug.LogError("error target has no womb");
                        break;
                    }
                    target.TickWomb(true);
                }
                break;
            case "wombAddSpermByRef":
                if (parsed.Count() >= 4)
                {
                    var target = scr_System_CampaignManager.current.CurrentTarget;
                    if (target == null)
                    {
                        Debug.LogError("error target null");
                        break;
                    }
                    if (target.wombs.Count < 1)
                    {
                        Debug.LogError("error target has no womb");
                        break;
                    }
                    if (int.TryParse(parsed[1], out var amount) && int.TryParse(parsed[2], out var index) && int.TryParse(parsed[3], out var targetRef))
                    {
                        if (index < 0 || index >= target.wombs.Count) index = Utility.Dice(1, target.wombs.Count) - 1;
                        var targetwb = target.wombs[index];
                        var father = scr_System_CampaignManager.current.FindInstanceByID(targetRef);
                        if (father != null)
                        {
                            var pp = father.Body.GetRandomInternalWithTag("penis");
                            if (pp != null)
                            {
                                var cum = pp.Cum(amount);
                                if (cum != null) targetwb.source.Ingest(cum, null, true);
                                else Debug.LogError($"error {pp.DisplayNameFull} cannot cum");
                            }
                            else Debug.LogError($"error {father.FirstName} cannot cum");
                        }
                        else Debug.LogError($"error cannot find father ref {targetRef}");
                    }
                    else Debug.LogError("parsing argument");
                }
                break;
            case "wombAddSpermByID":
                if (parsed.Count() >= 6)
                {
                    var target = scr_System_CampaignManager.current.CurrentTarget;
                    if (target == null)
                    {
                        Debug.LogError("error target null");
                        break;
                    }
                    if (target.wombs.Count < 1)
                    {
                        Debug.LogError("error target has no womb");
                        break;
                    }
                    if (int.TryParse(parsed[1], out var amount) && int.TryParse(parsed[2], out var index))
                    {
                        if (index < 0 || index >= target.wombs.Count) index = Utility.Dice(1, target.wombs.Count) - 1;
                        var targetwb = target.wombs[index];
                        Item_Instance_Cum cum = WorldManager.Instantiate(parsed[3], parsed[4], parsed[5]);
                        cum.CumAmount = amount;
                        targetwb.source.Ingest(cum, null, true);
                    }
                    else
                    {
                        Debug.LogError("parsing argument");
                        break;
                    }
                }
                break;
            case "ovulate":
                if (parsed.Count() >= 1)
                {
                    var target = scr_System_CampaignManager.current.CurrentTarget;
                    if (target == null)
                    {
                        Debug.LogError("error target null");
                        break;
                    }
                    if (target.wombs.Count < 1)
                    {
                        Debug.LogError("error target has no womb");
                        break;
                    }
                    foreach(var womb in target.wombs)
                    {
                        womb.ovulation();
                    }
                }

                break;
        }

        if (parsedSuccessful) Debug.Log(s);
    }

    static bool isValidQuery(bool isStatEx, bool isStatDerived, Stat_Modifier modifier, Stats_Base source)
    {
        return modifier.ValidateAccess(isStatEx, isStatDerived, false, false);
    }
    static bool isValidQuery(bool isStatEx, bool isStatDerived, Stat_Modifier modifier, Stats_Derived_Base source)
    {
        return modifier.ValidateAccess(isStatEx, isStatDerived, true, false);
    }
    static bool isValidQuery(bool isStatEx, bool isStatDerived, Stat_Modifier modifier, Stats_Derived_Extended_Instance source)
    {
        return modifier.ValidateAccess(isStatEx, isStatDerived, true, true);
    }
    static bool isValidQuery(bool isStatEx, bool isStatDerived, Stat_Modifier modifier, StatusEx_Instance source)
    {
        return modifier.ValidateAccess(isStatEx, isStatDerived, true, true, true, true);
    }

    public static ObjectPool<StringBuilder> StringBuilderPool = new DefaultObjectPoolProvider().CreateStringBuilderPool();



    /// <summary>
    ///
    /// </summary>
    /// <param name="source"></param>
    /// <param name="storage"></param>
    /// <param name="list"></param>
    /// <returns></returns>
    public static float ParseStatMods(object source, StatModStorage storage, List<Stat_Modifier> list)
    {
        var statsExtList = scr_System_Serializer.current.index_StatsExtended.list;
        var statsDerivedList = scr_System_Serializer.current.index_StatsDerived.list;

        storage.Reset();
        // pre-assigne validator function
        Func<Stat_Modifier, bool> validator = null;
        // Assign the correct validator based on type
        if (source is Stats_Base sb)                            validator = (m) => isValidQuery(m._isStatEX, m._isStatDerived, m, sb);
        else if (source is Stats_Derived_Base db)               validator = (m) => isValidQuery(m._isStatEX, m._isStatDerived, m, db);
        else if (source is Stats_Derived_Extended_Instance ex)  validator = (m) => isValidQuery(m._isStatEX, m._isStatDerived, m, ex);
        else if (source is StatusEx_Instance sex)               validator = (m) => isValidQuery(m._isStatEX, m._isStatDerived, m, sex);
        else
        {
            Debug.LogError("source is of unknown type");
            return 0f;
        }


        foreach (var modifier in list)
        {
            // baseValue do not allow GetValue to prevent recursive calls
            // if (modifier.ValueType != "number") continue;
            // this filter is done in statmanager getmodifier stage

            if (!modifier.initialized)
            {
                modifier.initialized = true;
                foreach (var i in statsExtList) if (modifier.valueString == i.ID) { modifier._isStatEX = true; break; }
                foreach (var i in statsDerivedList) if (modifier.valueString == i.ID) { modifier._isStatDerived = true; break; }
            }

            if (!validator(modifier))
            {
                continue;
            }

            storage.Merge(modifier);
        }
        return storage.Value;
    }

    

    public static float StatValue(Stat_Modifier modifier, I_StatsManager Stats)
    {
        if (Stats == null && modifier.valueType != Stat_Modifier_Type.number) Debug.LogError("STATModifier.Value() ALERT: chara parameter is allowed to be null ONLY if valueType is not number");
        switch (modifier.valueType)
        {
            case Stat_Modifier_Type.getStatValue:
                return Stats.GetStatValue(modifier.valueString);
            case Stat_Modifier_Type.getStatMod:
                switch (modifier.valueString)
                {
                    case "Strength": return Stats.Strength.GetStatMod();
                    case "Constitution": return Stats.Constitution.GetStatMod();
                    case "Psyche": return Stats.Psyche.GetStatMod();
                    case "Willpower": return Stats.Willpower.GetStatMod();
                }
                break;
            case Stat_Modifier_Type.number:
                return modifier.ValueFloat;
            case Stat_Modifier_Type.getStatusValue:
                //Debug.Log("Getting status value");
                var i = Stats.GetStatusByStringMatch(modifier.valueString);
                return i == null ? 0 : i.Severity;
            default:
                Debug.LogError("StatModifier Parse error, unrecognized valuetype");
                break;

        }
        Debug.LogError("Error Getting Value in Stat_Modifier"); 
        return 0f;
    }


}
