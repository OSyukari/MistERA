using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Text.RegularExpressions;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

public class Dictionary_Master
{
    protected ArrayList list = null;
    public ArrayList List
    {
        get
        {
            if (list == null)
            {
                list = new ArrayList();
                list.Add(Dictionary);
            }
            return list;
        }
    }

    public Dictionary_Index Dictionary = new Dictionary_Index();

    public void MergeWith(MasterList list)
    {
        for (int i = 0; i < this.List.Count; i++)
        {
            if (list.List[i] == null) continue;
            //if (this.List[i] == null) this.List[i] = 
            I_IndexMergeable a = this.List[i] as I_IndexMergeable;
            I_IndexMergeable b = list.List[i] as I_IndexMergeable;
            if (a != null && b != null) a.MergeWith(b);
            else
            {
                Debug.LogError("Index Merge operation failed at index " + i);
            }
        }
    }
    public void Initialize()
    {

        foreach (object l in List)
        {
            if (l is I_SerializationCallbackReceiver) (l as I_SerializationCallbackReceiver).OnAfterDeserialize();
            if (l is I_IndexHasID) (l as I_IndexHasID).RegisterAllID();
        }

        foreach (object l in List)
        {
            if (l is I_NeedLateInitialize) (l as I_NeedLateInitialize).LateInitialize();
            //if (l is I_IndexHasTooltip) (l as I_IndexHasTooltip).RegisterAllTooltip();  // tooltip uses dictionary
        }
    }
}

[System.Serializable]
public class Dictionary_Index : I_IndexMergeable
{
    public SortedDictionary<string, SortedDictionary<string, string>> Entries = null;

    public Dictionary_Index() {
        Entries = new SortedDictionary<string, SortedDictionary<string, string>>();
        Entries.Add("default", new SortedDictionary<string, string>());
    
    }

    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Dictionary_Index;
        if (l == null) return;
        else if (l.Entries == null) return;
        else
        {
            foreach (var entry in l.Entries)
            {
                if (!this.Entries.ContainsKey(entry.Key)) this.Entries.Add(entry.Key, new SortedDictionary<string, string>());
                var tempdic = this.Entries[entry.Key].Concat(entry.Value).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                this.Entries[entry.Key] = new SortedDictionary<string, string>(tempdic);
            }
        }
    }

    private string cachedLang = "";
    private SortedDictionary<string, string> _currentDict = null;
    protected SortedDictionary<string, string> currentDict { get
        {
            if (cachedLang != "" && cachedLang == scr_System_CentralControl.current.Language) 
            {
                // nothing happens
            }
            else if (scr_System_CentralControl.current == null) 
            {
                Debug.LogError($"Central control null");
                cachedLang = "default";
                _currentDict = Entries["default"];
            }
            else if (Entries.ContainsKey(scr_System_CentralControl.current.Language))
            {

                    _currentDict = Entries[scr_System_CentralControl.current.Language];
     

            }
            else
            {
                Debug.LogError($"DICTIONARY MISSING LANGUAGE FOR {scr_System_CentralControl.current.Language}");
                return null;
            }
            return _currentDict;
            
            
        } }

    /// <summary>
    /// Find entry in dictionary, and return without parsing
    /// </summary>
    /// <param name="ID"></param>
    /// <returns></returns>
    public string Query(string ID)
    {
        return currentDict == null ? "ERROR Dictionary NULL" : (currentDict.ContainsKey(ID) ? currentDict[ID] : ID);
    }

    /// <summary>
    /// First find entry in dictionary, then replace string in entry
    /// </summary>
    /// <param name="ID"></param>
    /// <param name="fallback"></param>
    /// <returns></returns>
    public string QueryThenParse(string ID, string fallback = "")
    {
        string s = Query(ID);
        string p = Parse(s);
        if (p == ID && fallback != "") return fallback;
        return p;
    }

    /// <summary>
    /// Perform string replace without looking in dictionary for ID entry
    /// </summary>
    /// <param name="s"></param>
    /// <returns></returns>
    public string Parse(string s)
    {
        bool Stop = false;
        int counter = 0;
        while (!Stop && counter < 20)
        {
            counter++;
            Match match = Regex.Match(s, strReplacerPattern);
            if (match == Match.Empty) Stop = true;
            else
            {
                var original = match.Value;
                var content = Regex.Match(original, strContentPattern).Value;
                var replaced = Query(content);
                if (replaced != content) s = s.Replace(original, replaced);
            }
        }
        return s;

    }

    static string strReplacerPattern = @"[%][%]([A-Za-z]|[0-9]|[_])+[%][%]";
    static string strContentPattern = @"(?<=%%)\S+(?=%%)";
    //Regex strReplacer = new Regex(strReplacerPattern);

}

