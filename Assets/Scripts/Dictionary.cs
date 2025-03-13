using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.Text.RegularExpressions;

[System.Serializable]
public class Dictionary_Master 
{
    Dictionary<string, Dictionary_Index> m_Dictionary;
    public Dictionary_Master()
    {
        m_Dictionary = new Dictionary<string, Dictionary_Index>();
    }

    /// <summary>
    /// Find entry in dictionary, and return without parsing
    /// </summary>
    /// <param name="ID"></param>
    /// <returns></returns>
    public string Query(string ID)
    {
        Dictionary_Index current = m_Dictionary["default"];
        string result = current.Query(ID);
        return result == null ? ID : result;
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

    public void AddToDict(Dictionary_Index dict)
    {
        if (m_Dictionary.ContainsKey(dict.language)) m_Dictionary[dict.language].entries.AddRange(dict.entries);
        else m_Dictionary.Add(dict.language, dict);
    }
}

[System.Serializable]
public class Dictionary_Index
{
    public string language = "default";
    public List<Dictionary_Entry> entries;

    public string Query(string ID)
    {
        var result = entries.Find(x => x.ID == ID);
        if (result != null) return result.Entry;
        else
        {
            //Debug.Log("Unimplemented Dictionary Entry for [" + ID + "] in dictionary [" + language + "]");
            return ID;
        }
    }

    [System.Serializable]
    public class Dictionary_Entry
    {
        public string ID;
        public string Entry;
    }

}

