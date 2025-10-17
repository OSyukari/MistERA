using System.Collections.Generic;
using UnityEngine;
using System;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine.UI;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine.EventSystems;
using System.IO;
using System.Text;
using Microsoft.Extensions.ObjectPool;

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
        new JsonSerializerSettings() { 
            TypeNameHandling = TypeNameHandling.Auto ,
            Converters = new JsonConverter[] { new JSON_SO_Converter<Character_Trainable>() }
    };


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
            if (kvp[0] == "self" && owner.Self != null)
            {
                switch(kvp[1])
                {
                    case "name": newString = newString.Replace(match.Value, owner.Self.FirstName); break;
                    case "nameCount": newString = newString.Replace(match.Value, owner.Self.FirstName); break;
                    case "room_name":
                        var room = scr_System_CampaignManager.current.Map.FindRoomByChara(owner.Self.RefID);
                        if (room != null) newString = newString.Replace(match.Value, room.DisplayName); break;
                    default: break;
                }
            }
            else if (owner.Targets.ContainsKey(kvp[0]))
            {
                bool error = false;
                List<string> collect = new List<string>();
                Dictionary<string, int> collectDict = new Dictionary<string, int>();
                foreach (var c in owner.Targets[kvp[0]])
                {
                    switch(kvp[1])
                    {
                        case "name": if (!collect.Contains(c.CallName)) collect.Add(c.CallName); break;
                        case "nameCount":
                            if (!collectDict.ContainsKey(c.CallName)) collectDict.Add(c.CallName, 1);
                            else collectDict[c.CallName] += 1;
                            break;
                        case "room_name":
                            var room = scr_System_CampaignManager.current.Map.FindRoomByChara(c.RefID);
                            if (room != null && !collect.Contains(room.DisplayName)) collect.Add(room.DisplayName); break;
                        default: error = true; break;
                    }
                }
                if (collectDict.Count > 0)
                {
                    foreach (var collectkvp in collectDict) collect.Add(LocalizeDictionary.QueryThenParse("event_targetscope_nameCount").Replace("$name$", collectkvp.Key).Replace("$count$", $"{collectkvp.Value}"));
                }
                if (!error) newString = newString.Replace(match.Value, String.Join(separator, collect));
            }
        }

        foreach(var appendStringkey in owner.AppendStrings)
        {
            if (appendStringkey.Value.Count < 1) continue;
            newString = newString.Replace($"${appendStringkey.Key}$", String.Join(",", appendStringkey.Value));
        }

        return newString;
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
        if (p2 == null || jSex == null)
        {
            if (!Utility.ListContainsLoose(a.actorRefs, package.actorRefs)) return false;
            else
            {

                if (package.ComTags.Contains("canbeignored"))
                {
                    //package.AddExtraCOMTags(new List<string>() { "ignored" });
                    package.SetIgnored();

                    Debug.Log("Detecting ActionPackage_Sex Conflict between " + a.DisplayName + " and " + package.DisplayName + " set to ignored coexist");
                    return false;
                }
                else
                {
                    Debug.Log("Detecting ActionPackage_Sex Conflict between " + a.DisplayName + " and " + package.DisplayName + " cannot be ignored");
                    return true;
                }

            }
        }
        else
        {   //detecting conflict between 2 sex packages inside a sex job (allow coexist)
            if (Utility.ListContainsLoose(a.actorRefs, package.actorRefs))
            {
                if (a.targetCOM != null && package.targetCOM != null)
                {
                    if (a.targetCOM.conflictTags.Count > 0 && Utility.ListContainsLoose(package.targetCOM.comTags, a.targetCOM.conflictTags))
                    {
                        Debug.Log($"Detecting ActionPackage_Sex Conflict between {a.DisplayName} and {package.DisplayName} caught by condition -2\nList 1 [{String.Join(" ", a.targetCOM.conflictTags)}] [{String.Join(" ", package.targetCOM.comTags)}]");
                        return true;
                    }
                    if (package.targetCOM.conflictTags.Count > 0 && Utility.ListContainsLoose(a.targetCOM.comTags, package.targetCOM.conflictTags))
                    {
                        Debug.Log($"Detecting ActionPackage_Sex Conflict between {a.DisplayName} and {package.DisplayName} caught by condition -2\nList 1 [{String.Join(" ", a.targetCOM.comTags)}] [{String.Join(" ", package.targetCOM.conflictTags)}]");
                        return true;
                    }
                }

                if (Utility.ListContainsLoose(a.DoerRefs, package.DoerRefs) && Utility.ListContainsLoose(a.doerBodyTags, package.doerBodyTags))
                {
                    Debug.Log("Detecting ActionPackage_Sex Conflict between " + a.DisplayName + " and " + package.DisplayName + " caught by condition 1\nList 1 [" + String.Join(" ", a.DoerRefs) + " " + String.Join(" ", a.doerBodyTags) + "] List 2 [" + String.Join(" ", package.DoerRefs) + " " + String.Join(" ", package.doerBodyTags) + "]");
                    return true;
                }
                if (p2.ReceiverRefs.Count > 0 && Utility.ListContainsLoose(a.ReceiverRefs, p2.ReceiverRefs) && Utility.ListContainsLoose(a.receiverBodyTags, p2.receiverBodyTags))
                {
                    Debug.Log("Detecting ActionPackage_Sex Conflict between " + a.DisplayName + " and " + package.DisplayName + " caught by condition 2\nList 1 [" + String.Join(" ", a.receiverBodyTags) + "] List 2 [" + String.Join(" ", p2.receiverBodyTags) + "]");
                    return true;
                }

                if (Utility.ListContainsLoose(a.DoerRefs, p2.ReceiverRefs) && Utility.ListContainsLoose(a.doerBodyTags, p2.receiverBodyTags))
                {
                    Debug.Log("Detecting ActionPackage_Sex Conflict between " + a.DisplayName + " and " + package.DisplayName + " caught by condition 3\nList 1 [" + String.Join(" ", a.doerBodyTags) + "] List 2 [" + String.Join(" ", p2.receiverBodyTags) + "]");
                    return true;
                }
                if (Utility.ListContainsLoose(a.ReceiverRefs, p2.DoerRefs) && Utility.ListContainsLoose(a.receiverBodyTags, p2.doerBodyTags))
                {
                    Debug.Log("Detecting ActionPackage_Sex Conflict between " + a.DisplayName + " and " + package.DisplayName + " caught by condition 4\nList 1 [" + String.Join(" ", a.receiverBodyTags) + "] List 2 [" + String.Join(" ", p2.doerBodyTags) + "]");
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

    public static void LoadSprite(Texture2D SpriteTexture, Image image)
    {
        if (SpriteTexture == null)
        {
            image.sprite = SpriteAsset.transparent;
            return;
        }
        Sprite NewSprite = Sprite.Create(SpriteTexture, new Rect(0, 0, SpriteTexture.width, SpriteTexture.height), new Vector2(0, 0), 100.0f);
        image.sprite = NewSprite;
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
            ownerTags = ownerTags.Distinct().ToList();
        }

        if (b != null)
        {
            if (b.InteractionJob != null) b.InteractionJob.GetActorAPTags(b.RefID, targetTags);  // get a interactionjob all self
            if (b.CurrentJob != null) b.CurrentJob.GetActorAPTags(b.RefID, targetTags);  // get a currentjob all self
            foreach (var status in b.Stats.StatusInstances) targetTags.AddRange(status.Tags);    // get a statsinstancekeywords
            GetActorTag(ref targetTags, b);
            targetTags = targetTags.Distinct().ToList();
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

        APs = APs.Distinct().ToList();
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


    public static void GetActorTag(ref List<string> tags, Character_Trainable c)
    {
        var result = scr_System_CentralControl.current.GetGender(c);
        foreach(var tag in result)
        {
            tags.Add(tag.ToString());
        }
        if (scr_System_Time.current.TimeResume && !c.CanActInTimeStop) tags.Add("timeResume"); 
        else if(c.isTimeStopped) tags.Add("timestop");
        if (c.isSleeping) {
            tags.Add("sleeping");
            tags.Add("unconscious");
        }
        else if (c.Stats.isConsciousnessUnconscious) tags.Add("unconscious");

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
                            scr_System_CampaignManager.current.CurrentTarget.Relationships._Pride += numbers;
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
                            if(scr_System_CampaignManager.current.CurrentTarget.Stats.Lust != null){
                            scr_System_CampaignManager.current.CurrentTarget.Stats.Lust.DebugSeverityMod += numbers;}
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

        }

        if (parsedSuccessful) Debug.Log(s);
    }

    public static bool isValidQuery(bool isStatEx, bool isStatDerived, Stat_Modifier modifier, Stats_Base source)
    {
        return modifier.ValidateAccess(isStatEx, isStatDerived, false, false);
    }
    public static bool isValidQuery(bool isStatEx, bool isStatDerived, Stat_Modifier modifier, Stats_Derived_Base source)
    {
        return modifier.ValidateAccess(isStatEx, isStatDerived, true, false);
    }
    public static bool isValidQuery(bool isStatEx, bool isStatDerived, Stat_Modifier modifier, Stats_Derived_Extended_Instance source)
    {
        return modifier.ValidateAccess(isStatEx, isStatDerived, true, true);
    }
    public static bool isValidQuery(bool isStatEx, bool isStatDerived, Stat_Modifier modifier, StatusEx_Instance source)
    {
        return modifier.ValidateAccess(isStatEx, isStatDerived, true, true, true, true);
    }



    public static float _ParseStatMods(object source, StatsManager c, Dictionary<string, StatsManager.ModStorage> storage, List<Stat_Modifier> list, List<string> stringResults = null, float valueFloor = 0f, float valueCeiling = 999f, bool capModded = false )
    {


        string sourceID = "";

        foreach (var modifier in list)
        {
            // baseValue do not allow GetValue to prevent recursive calls
            // if (modifier.ValueType != "number") continue;
            // this filter is done in statmanager getmodifier stage

            bool isStatEx = scr_System_Serializer.current.index_StatsExtended.list.Find(x => x.ID == modifier.valueString) != null;
            bool isStatDerived = scr_System_Serializer.current.index_StatsDerived.list.Find(x => x.ID == modifier.valueString) != null;

            if (source == null)
            {
                // do nothing
                Debug.LogError("source is null");
            }
            else if (source is Stats_Base)
            {
                sourceID = (source as Stats_Base).ID;
                //Debug.LogError("source is Stats_Base " + sourceID);
                if (!isValidQuery(isStatEx, isStatDerived, modifier,(Stats_Base)source)) continue;
            }
            else if (source is Stats_Derived_Base)
            {
                sourceID = (source as Stats_Derived_Base).ID;
                //Debug.LogError("source is Stats_Derived_Base " + sourceID);
                if (!isValidQuery(isStatEx, isStatDerived, modifier, (Stats_Derived_Base)source)) continue;
            }
            else if (source is Stats_Derived_Extended_Instance)
            {
                sourceID = (source as Stats_Derived_Extended_Instance).ID;
                //Debug.LogError("source is Stats_Derived_Extended_Instance " + sourceID);
                if (!isValidQuery(isStatEx, isStatDerived, modifier, (Stats_Derived_Extended_Instance)source)) continue;
            }
            else if (source is StatusEx_Instance)
            {
                sourceID = (source as StatusEx_Instance).BaseRef.statusID;
                //Debug.LogError("source is StatusEx_Instance " + sourceID);
                if (!isValidQuery(isStatEx, isStatDerived, modifier, (StatusEx_Instance)source)) continue;
            }
            else
            {
                Debug.LogError("source is else");
                continue;
            }

            if (!storage.ContainsKey(modifier.modKey))
            {
                var newEntry = new StatsManager.ModStorage(1);
                storage.Add(modifier.modKey, newEntry);
            }
            switch (modifier.type)
            {
                case Stat_Modifier.StatMod_Type.setBase:
                    storage[modifier.modKey].baseValue = UtilityEX.StatValue(modifier, c);
                    break;
                case Stat_Modifier.StatMod_Type.setMult:
                    storage[modifier.modKey].baseMult = UtilityEX.StatValue(modifier, c);
                    break;
                case Stat_Modifier.StatMod_Type.addMult:
                    storage[modifier.modKey].addMult += UtilityEX.StatValue(modifier, c);
                    break;
                case Stat_Modifier.StatMod_Type.addBase:
                    storage[modifier.modKey].addValue += UtilityEX.StatValue(modifier, c);
                    break;
            }
        }

        //ModStorage baseMod = modifiers["baseValue"];
        //ModStorage finalMod = modifiers["finalMod"];
        StatsManager.ModStorage finalMod = null;
        float mods = 0f;

        if (capModded) 
        {
            valueCeiling = 0f;
            valueFloor = 0f;
        }

        foreach (var kvp in storage)
        {
            if (kvp.Key == "finalMod")
            {
                finalMod = storage["finalMod"];
                continue;
            }
            if (stringResults != null) stringResults.Add(kvp.Key + " (" + kvp.Value.baseValue + "+" + kvp.Value.addValue + ")*(" + kvp.Value.baseMult + "+" + kvp.Value.addMult + ")");
            var value = (kvp.Value.baseValue + kvp.Value.addValue) * (kvp.Value.baseMult + kvp.Value.addMult);
            mods += value;
            if (capModded)
            {
                valueCeiling = Math.Max(value, valueCeiling);
                valueFloor = Math.Min(value, valueFloor);
            }
        }

        if (finalMod != null)
        {
            if (stringResults != null) stringResults.Add("finalMod " + "* (" + finalMod.baseMult + " + " + finalMod.addMult + ") "+  "(" + finalMod.baseValue + "+" + finalMod.addValue + ")");
            mods = mods * (finalMod == null ? 1 : (finalMod.baseMult + finalMod.addMult)) + (finalMod == null ? 0 : (finalMod.baseValue + finalMod.addValue));
        }
        else
        {
            //Debug.LogError("FinalMod not found for statID " + sourceID);
        }

        if (mods > valueCeiling) return valueCeiling;
        else if (mods < valueFloor) return valueFloor;
        else return mods;

    }

    public static bool Stat_Modifier_isStatEx(Stat_Modifier mod)
    {
        if (!mod.initialized)
        {
            mod.initialized = true;
            mod._isStatEX = scr_System_Serializer.current.index_StatsExtended.list.Any(x => x.ID == mod.valueString);
            mod._isStatDerived = scr_System_Serializer.current.index_StatsDerived.list.Any(x => x.ID == mod.valueString);
        }
        return mod._isStatEX;
    }
    public static bool Stat_Modifier_isStatDerived(Stat_Modifier mod)
    {
        if (!mod.initialized)
        {
            mod.initialized = true;
        }
        return mod._isStatDerived;
    }

    public static ObjectPool<StringBuilder> StringBuilderPool = new DefaultObjectPoolProvider().CreateStringBuilderPool(); 

    /// <summary>
    /// 
    /// </summary>
    /// <param name="source"></param>
    /// <param name="stats"></param>
    /// <param name="storage"></param>
    /// <param name="list"></param>
    /// <param name="record"></param>
    /// <param name="valueFloor"></param>
    /// <param name="valueCeiling"></param>
    /// <param name="capModded">Restrict valueFloor and valueCeiling to the max magnitude of every single mod, used for mood related.</param>
    /// <param name="allowOvercap">Allow value calculation to ge above min or max cap during calculation stage, will only take effect if capModded == false<br/>true: will only clamp value after all calculations<br/>false: will clamp after every step</param>
    /// <returns></returns>
    public static float ParseStatMods(object source, I_StatsManager stats, Dictionary<string, StatsManager.ModStorage> storage, List<Stat_Modifier> list, StatRecord record = null, float valueFloor = 0f, float valueCeiling = 999f, bool capModded = false, bool allowOvercap = true)
    {


        string sourceID = string.Empty;
        var statsExtList = scr_System_Serializer.current.index_StatsExtended.list;
        var statsDerivedList = scr_System_Serializer.current.index_StatsDerived.list;

        foreach (var modifier in list)
        {
            // baseValue do not allow GetValue to prevent recursive calls
            // if (modifier.ValueType != "number") continue;
            // this filter is done in statmanager getmodifier stage

            if (!modifier.initialized)
            {
                modifier.initialized = true;
                modifier._isStatEX = statsExtList.Any(x => x.ID == modifier.valueString);
                modifier._isStatDerived = statsDerivedList.Any(x => x.ID == modifier.valueString);
            }

            switch (source)
            {
                case null:
                    Debug.LogError("source is null");
                    continue;

                case Stats_Base statsBase:
                    sourceID = statsBase.ID;
                    if (!isValidQuery(modifier._isStatEX, modifier._isStatDerived, modifier, statsBase)) continue;
                    break;

                case Stats_Derived_Base derivedBase:
                    sourceID = derivedBase.ID;
                    if (!isValidQuery(modifier._isStatEX, modifier._isStatDerived, modifier, derivedBase)) continue;
                    break;

                case Stats_Derived_Extended_Instance extended:
                    sourceID = extended.ID;
                    if (!isValidQuery(modifier._isStatEX, modifier._isStatDerived, modifier, extended)) continue;
                    break;

                case StatusEx_Instance statusEx:
                    sourceID = statusEx.BaseRef.statusID;
                    if (!isValidQuery(modifier._isStatEX, modifier._isStatDerived, modifier, statusEx)) continue;
                    break;

                default:
                    Debug.LogError("source is of unknown type");
                    continue;
            }

            if (!storage.TryGetValue(modifier.modKey, out var entry))
            {
                entry = new StatsManager.ModStorage(1);
                storage.Add(modifier.modKey, entry);
            }

            float value = UtilityEX.StatValue(modifier, stats); // avoid calling this 4x
            switch (modifier.type)
            {
                case Stat_Modifier.StatMod_Type.setBase:
                    entry.baseValue = value;
                    break;
                case Stat_Modifier.StatMod_Type.setMult:
                    entry.baseMult = value;
                    break;
                case Stat_Modifier.StatMod_Type.addMult:
                    entry.addMult += value;
                    break;
                case Stat_Modifier.StatMod_Type.addBase:
                    entry.addValue += value;
                    break;
            }
        }

        //ModStorage baseMod = modifiers["baseValue"];
        //ModStorage finalMod = modifiers["finalMod"];
        if (capModded)
        {
            valueCeiling = 0f;
            valueFloor = 0f;
        }

        float mods = 0f;
        StatsManager.ModStorage finalMod = null;


        foreach (var kvp in storage)
        {
            if (kvp.Key == "finalMod")
            {
                finalMod = kvp.Value;
                continue;
            }

            float v = (kvp.Value.baseValue + kvp.Value.addValue) * (kvp.Value.baseMult + kvp.Value.addMult);
            mods += v;


            if (capModded)
            {
                valueCeiling = Math.Max(v, valueCeiling);
                valueFloor = Math.Min(v, valueFloor);
            }
            else if (!allowOvercap)
            {
                mods = Mathf.Clamp(mods, valueFloor, valueCeiling);
            }

            if (record != null) record.AddEntry(kvp.Key, kvp.Value);
        }


        if (finalMod != null)
        {
            if (record != null) record.AddEntry(string.Empty, finalMod);
            mods = mods * (finalMod.baseMult + finalMod.addMult) + (finalMod.baseValue + finalMod.addValue);
        }

        var final = Mathf.Clamp(mods, valueFloor, valueCeiling);
        if (record != null) record.SetValue(final);// .value = final;
        return final;
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
