using UnityEngine;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Collections;
using System;
using System.Linq;
using Newtonsoft.Json;

public class LocalizeDictionary : MonoBehaviour
{
    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
        DontDestroyOnLoad(gameObject);
    }
    public static LocalizeDictionary Instance { get; private set; }

    public SortedDictionary<string, SortedDictionary<string, string>> Entries = null;

    public Dictionary_Index Index = new Dictionary_Index();
}


[System.Serializable]
public class Dictionary_Index : I_IndexMergeable
{


    public SortedDictionary<string, SortedDictionary<string, string>> Entries = null;

    public Dictionary_Index()
    {
        Entries = new SortedDictionary<string, SortedDictionary<string, string>>();
        Entries.Add("default", new SortedDictionary<string, string>());

    }
    public void SetLang(string lang)
    {
        this.cachedLang = lang;
    }
    public string cachedLang = "default";

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

    private SortedDictionary<string, string> _currentDict = null;
    protected SortedDictionary<string, string> currentDict
    {
        get
        {
            if (Entries.ContainsKey(cachedLang)) return Entries[cachedLang];
            else return null;
            /*
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
            */

        }
    }

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

