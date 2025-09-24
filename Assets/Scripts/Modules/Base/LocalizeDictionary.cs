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

        _cacheResult.Clear();
    }
    public static LocalizeDictionary Instance { get; private set; }

    public Dictionary_Index Index = new Dictionary_Index();

    static Dictionary<string, string> _cacheResult = new Dictionary<string, string>();

    public static string QueryThenParse(string ID, string fallback = "")
    {
        if (_cacheResult.ContainsKey(ID)) return _cacheResult[ID];
        var result = Instance.Index.QueryThenParse(ID, fallback);
        _cacheResult[ID] = result;
        return result;
    }
    public static void ClearCache()
    {
        _cacheResult.Clear();
    }
}


[System.Serializable]
public class Dictionary_Index : I_IndexMergeable
{

    [HideInInspector][JsonIgnore][NonSerialized] 
    public List<string> Languages = new List<string>()
    {
        "zh-cn",
        "en-us"
    };

    public SortedDictionary<string, SortedDictionary<string, string>> Entries = null;

    public Dictionary_Index()
    {
        Entries = new SortedDictionary<string, SortedDictionary<string, string>>();
        Entries.Add(defaultLang, new SortedDictionary<string, string>());
        foreach(var key in Languages) Entries.Add(key, new SortedDictionary<string, string>());

    }
    protected string defaultLang = "default";
    [JsonIgnore][NonSerialized] public string cachedLang = "zh-cn";

    public void MergeWith(I_IndexMergeable list)
    {
        var l = list as Dictionary_Index;
        if (l == null || l.Entries == null) return;
        
        foreach (var entry in l.Entries)
        {
            //if (!this.Entries.ContainsKey(entry.Key)) this.Entries.Add(entry.Key, new SortedDictionary<string, string>());
            var tempdic = this.Entries[entry.Key].Concat(entry.Value).ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            this.Entries[entry.Key] = new SortedDictionary<string, string>(tempdic);
        }
        
    }

    /// <summary>
    /// Find entry in dictionary, and return without parsing
    /// </summary>
    /// <param name="ID"></param>
    /// <returns></returns>
    protected string Query(string ID)
    {
        return Entries[cachedLang].ContainsKey(ID) ? Entries[cachedLang][ID] : Entries[defaultLang].ContainsKey(ID) ? Entries[defaultLang][ID]: ID;
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
    protected string Parse(string s)
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

