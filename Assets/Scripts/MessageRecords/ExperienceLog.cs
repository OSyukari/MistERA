using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class ExperienceLog
{
    [JsonIgnore]
    public bool isPlayerLog
    {
        get
        {
            return RightAlign.ContainsKey(0)
                || StatLog.ContainsKey(0) || ExpLog.ContainsKey(0) ||
                //RelationLog.ContainsKey(0) || 
                MessageLog.ContainsKey(0) || climaxMessage.ContainsKey(0);
        }
    }
    [JsonIgnore] public bool leftAlignOverride = false;

    public bool finalized = false;

    public List<int> relevantActorRefs = new List<int>();

    public void Finalize(out DescriptionCollector desc)
    {
        desc = new DescriptionCollector();

        var msg = PrintContent_Messages();
        if (msg.Length > 0)
        {
            desc.message += $"{(desc.message.Length > 0 ? "\n" : "")}{msg}";
        }
        var climax = PrintContent_Climax();
        if (climax.Length > 0)
        {
            desc.message += $"{(desc.message.Length > 0 ? "\n" : "")}{climax}";
        }
        var stats = PrintContent_Stats();
        if (stats.Length > 0) desc.message += $"{(desc.message.Length > 0 ? "\n" : "")}{stats}";

        desc.LoadActors(this.relevantActorRefs);
        Clear();

        if (desc.message.Length < 1) desc = null;
        return;
    }

    public void AddRelevantChara(List<int> list)
    {
        foreach (var i in list) if (!relevantActorRefs.Contains(i)) relevantActorRefs.Add(i);
    }

    public bool VisibleTo(Character_Trainable c)
    {
        if (c == null) return true;
        return relevantActorRefs.Contains(c.RefID);
    }

    protected SortedDictionary<int, bool> RightAlign = new SortedDictionary<int, bool>();
    protected SortedDictionary<int, Dictionary<string, double>> StatLog = new SortedDictionary<int, Dictionary<string, double>>();
    protected SortedDictionary<int, Dictionary<string, int>> ExpLog = new SortedDictionary<int, Dictionary<string, int>>();
    //protected SortedDictionary<int, Dictionary<int, int>> RelationLog = new SortedDictionary<int, Dictionary<int, int>>();
    protected SortedDictionary<int, List<string>> MessageLog = new SortedDictionary<int, List<string>>();
    protected SortedDictionary<int, string> climaxMessage = new SortedDictionary<int, string>();

    public ExperienceLog()
    {

    }

    public void AppendClimaxMSG(int chararef, string msg)
    {
        AddChara(chararef);
        if (!climaxMessage.ContainsKey(chararef)) climaxMessage[chararef] = msg;
        else climaxMessage[chararef] += msg;
    }

    public void PrependClimaxMSG(int chararef, string msg)
    {
        AddChara(chararef);
        if (!climaxMessage.ContainsKey(chararef)) climaxMessage[chararef] = msg.Replace("/$append$", "");
        else climaxMessage[chararef] = msg.Replace("$append$", climaxMessage[chararef]);
    }

    public bool GetRightAlign(int chararef)
    {
        if (this.RightAlign.TryGetValue(chararef, out bool result)) return result;
        return true;
    }
    public void AddChara(int charaRef)
    {
        if (!this.relevantActorRefs.Contains(charaRef)) this.relevantActorRefs.Add(charaRef);
        if (!this.RightAlign.ContainsKey(charaRef)) this.RightAlign.Add(charaRef, charaRef == 0 ? false : true);
    }

    public void AddExperience(int charaRef, string expID, int count)
    {
        AddChara(charaRef);
        // Debug.Log("EVP Explog, adding experiences "+expID);
        if (!this.ExpLog.ContainsKey(charaRef)) ExpLog.Add(charaRef, new Dictionary<string, int>());

        if (ExpLog[charaRef].ContainsKey(expID)) ExpLog[charaRef][expID] += count;
        else ExpLog[charaRef].Add(expID, count);
    }

    /// <summary>
    /// Only logs relation increase from NPC toward PC
    /// </summary>
    /// <param name="charaRef"></param>
    /// <param name="relID"></param>
    /// <param name="count"></param>
    public void AddRelations(int sourceCharaRef, int targetCharaRef, RelationshipScoreType relID, int count)
    {
        AddChara(sourceCharaRef);
        AddChara(targetCharaRef);
        //Debug.Log("EVP Explog, adding relations");
        // Debug.LogError("AddRelations between ["+sourceCharaRef+"] and ["+targetCharaRef+"]");
        int playerRef = scr_System_CampaignManager.current.Player.RefID;
        if (sourceCharaRef == playerRef) return;
        if (targetCharaRef != playerRef) return;

        if (!this.StatLog.ContainsKey(sourceCharaRef)) StatLog.Add(sourceCharaRef, new Dictionary<string, double>());
        var relIDStr = $"relationship_{relID.ToString().ToLower()}";
        if (StatLog[sourceCharaRef].ContainsKey(relIDStr)) StatLog[sourceCharaRef][relIDStr] += count;
        else StatLog[sourceCharaRef].Add(relIDStr, count);
    }

    public void AddMessage(int charaRef, string message)
    {
        AddChara(charaRef);
        if (!this.MessageLog.ContainsKey(charaRef)) MessageLog.Add(charaRef, new List<string>() { message });
        else this.MessageLog[charaRef].Add(message);
    }

    public void AddStats(int charaRef, string statID, double count)
    {
        AddChara(charaRef);
        //Debug.Log("EVP Explog, adding stat");
        if (!this.StatLog.ContainsKey(charaRef)) StatLog.Add(charaRef, new Dictionary<string, double>());

        if (StatLog[charaRef].ContainsKey(statID)) StatLog[charaRef][statID] += count;
        else StatLog[charaRef].Add(statID, count);
    }

    public void MergeWith(ExperienceLog log, bool shorten)
    {
        Debug.LogError("DO NOT USE THIS");
        /*
         EXP PRINT ITSELF, THEN THECK LOG VISIBILITY, TREAT AS DESCCOLLECTOR         
         */

        leftAlignOverride = leftAlignOverride || log.leftAlignOverride;

        this.AddRelevantChara(log.relevantActorRefs);

        foreach (KeyValuePair<int, bool> kvp in log.RightAlign)
        {
            this.RightAlign[kvp.Key] = log.leftAlignOverride ? false : kvp.Value;
        }

        if (!shorten)
        {
            foreach (KeyValuePair<int, Dictionary<string, int>> kvp in log.ExpLog)
            {
                if (!this.ExpLog.ContainsKey(kvp.Key)) ExpLog.Add(kvp.Key, kvp.Value);
                else
                {

                    foreach (KeyValuePair<string, int> kkvp in kvp.Value)
                    {
                        if (!this.ExpLog[kvp.Key].ContainsKey(kkvp.Key)) this.ExpLog[kvp.Key].Add(kkvp.Key, kkvp.Value);
                        else this.ExpLog[kvp.Key][kkvp.Key] += kkvp.Value;
                    }
                }
            }

            /*
            foreach (KeyValuePair<int, Dictionary<int, int>> kvp in log.RelationLog)
            {
                if (!this.RelationLog.ContainsKey(kvp.Key)) RelationLog.Add(kvp.Key, kvp.Value);
                else
                {

                    foreach (KeyValuePair<int, int> kkvp in kvp.Value)
                    {
                        if (!this.RelationLog[kvp.Key].ContainsKey(kkvp.Key)) this.RelationLog[kvp.Key].Add(kkvp.Key, kkvp.Value);
                        else this.RelationLog[kvp.Key][kkvp.Key] += kkvp.Value;
                    }
                }
            }*/

            foreach (KeyValuePair<int, Dictionary<string, double>> kvp in log.StatLog)
            {
                if (!this.StatLog.ContainsKey(kvp.Key)) StatLog.Add(kvp.Key, kvp.Value);
                else
                {
                    foreach (KeyValuePair<string, double> kkvp in kvp.Value)
                    {
                        if (!this.StatLog[kvp.Key].ContainsKey(kkvp.Key)) this.StatLog[kvp.Key].Add(kkvp.Key, kkvp.Value);
                        else this.StatLog[kvp.Key][kkvp.Key] += kkvp.Value;
                    }
                }
            }
        }

        foreach (KeyValuePair<int, List<string>> kvp in log.MessageLog)
        {
            if (!this.MessageLog.ContainsKey(kvp.Key)) MessageLog.Add(kvp.Key, kvp.Value);
            else MessageLog[kvp.Key].AddRange(kvp.Value);
            MessageLog[kvp.Key].RemoveAll(x => x.Length < 1);
        }
        foreach (KeyValuePair<int, string> kvp in log.climaxMessage)
        {
            if (kvp.Value.Length < 1) continue;
            climaxMessage[kvp.Key] = kvp.Value;
        }
    }

    public void Clear()
    {
        leftAlignOverride = false;
        RightAlign.Clear();
        //bool clearedBeforePrint = false;
        //foreach (KeyValuePair<int, Dictionary<string, int>> kvp in ExpLog) kvp.Value.Clear();
        ExpLog.Clear();

        //if(clearedBeforePrint) Debug.LogError("EVP Explog cleared before print ? ");

        //foreach (KeyValuePair<int, Dictionary<int, int>> kvp in RelationLog) kvp.Value.Clear();
        //RelationLog.Clear();
        climaxMessage.Clear();
        //foreach (KeyValuePair<int, Dictionary<string, int>> kvp in StatLog) kvp.Value.Clear();
        StatLog.Clear();

        MessageLog.Clear();

        relevantActorRefs.Clear();
        finalized = false;
    }

    public string PrintContent_Stats()
    {

        // Debug.Log("EVP Explog, print");
        List<string> lines = new List<string>();
        List<int> keys = new List<int>();
        keys.AddRange(StatLog.Keys);
        keys.AddRange(ExpLog.Keys);
        keys = Utility.Distinct(keys);

        foreach (var key in keys)
        {
            string s = scr_System_CampaignManager.current.FindInstanceByID(key).FirstName + ": ";
            bool hasvalue = false;
            if (StatLog.TryGetValue(key, out var kvp_refID) && kvp_refID.Values.Count > 0)
            {
                foreach (var kvp in kvp_refID)
                {
                    if (kvp.Value != 0)
                    {
                        hasvalue = true;
                        s += "" + LocalizeDictionary.QueryThenParse(kvp.Key) + "" + kvp.Value.ToString("+0.#;-0.#") + " ";
                    }
                }
            }
            if (ExpLog.TryGetValue(key, out var kvp_refID2) && kvp_refID2.Values.Count > 0)
            {
                foreach (var kvp in kvp_refID2)
                {
                    if (kvp.Value != 0)
                    {
                        hasvalue = true;
                        s += "" + LocalizeDictionary.QueryThenParse(kvp.Key) + "" + kvp.Value.ToString("+0.#;-0.#") + " ";
                    }
                }
            }
            if (!hasvalue) continue;
            s = Utility.WrapTextColor(s, scr_System_CentralControl.current.DisplaySetting.TextColor_disabled.Color);
            lines.Add(s);
        }
        if (lines.Count < 1) return "";
        return $"{String.Join('\n', lines.ToArray())}";
    }
    /*
    public string PrintContent_Relations()
    {
        // Debug.Log("EVP Explog, print");
        List<string> lines = new List<string>();
        // Only player character related relationship increase is logged
        foreach (var kvp_refID in RelationLog)
        {
            if (kvp_refID.Value.Count > 0)
            {
                string s = scr_System_CampaignManager.current.FindInstanceByID(kvp_refID.Key).FirstName + ": ";
                foreach (var kvp in kvp_refID.Value) if (kvp.Value != 0 || kvp_refID.Value.Count < 2) s += "" + LocalizeDictionary.QueryThenParse("relationship_" + ((RelationshipScoreType)kvp.Key).ToString().ToLower()) + "" + kvp.Value.ToString("+0;-#") + " ";
                if (s.Length < 1) continue;

                s = Utility.WrapTextColor(s, scr_System_CentralControl.current.DisplaySetting.TextColor_disabled.Color);
                lines.Add(RightAlign[kvp_refID.Key] ? $"<align=\"right\">{s}</align>" : s);
            }
        }
        return String.Join('\n', lines.ToArray());
    }*/
    public string PrintContent_Messages()
    {
        // Debug.Log("EVP Explog, print");
        List<string> lines = new List<string>();
        List<int> keys = MessageLog.Keys.ToList();
        foreach (var key in keys)
        {
            if (MessageLog.TryGetValue(key, out var kvp_refID) && kvp_refID.Count > 0)
            {
                //string s = !leftAlignOverride && RightAlign[key] ? $"<align=\"right\">{String.Join("</align>\n<align=\"right\">", kvp_refID)}</align>" : String.Join("\n", kvp_refID);
                string s =  String.Join("\n", kvp_refID);
                if (s.Length < 1) continue;
                lines.Add(s);
            }
            //Debug.Log($"FlushLogMessage {kvp_refID.Key} {RightAlign[kvp_refID.Key]} {String.Join("||", kvp_refID.Value)}");

        }
        return String.Join('\n', lines.ToArray());
    }
    public string PrintContent_Climax()
    {
        // Debug.Log("EVP Explog, print");
        List<string> lines = new List<string>();
        List<int> keys = climaxMessage.Keys.ToList();
        foreach (var key in keys)
        {
            if (climaxMessage.TryGetValue(key, out var kvp_refID) && kvp_refID.Length > 0)
            {
                string s = String.Join("\n", kvp_refID);
                if (s.Length < 1) continue;
                lines.Add(s);
            }
            //Debug.Log($"FlushLogMessage {kvp_refID.Key} {RightAlign[kvp_refID.Key]} {String.Join("||", kvp_refID.Value)}");

        }
        return String.Join('\n', lines.ToArray());
    }

}