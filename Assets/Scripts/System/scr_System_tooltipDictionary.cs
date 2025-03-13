using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

// Name is optional
// ID is mandatory

public class scr_System_tooltipDictionary : MonoBehaviour
{
    // Singleton
    public static scr_System_tooltipDictionary current;
    private void Awake()
    {
        if (current == null)
        {
            current = this;
        }
        else
        {
            Destroy(gameObject);
        }
        DontDestroyOnLoad(gameObject);

        ////////////////
        Dictionary_Unsorted dictionary_Unsorted = JsonUtility.FromJson<Dictionary_Unsorted>(json_dict_unsorted.text);
        dict = new Dictionary<string, string>();

        foreach (Dict_Entry entry in dictionary_Unsorted.list)
        {
            //Debug.Log("Found entry in Dictionary: " + entry.entryName + " " + entry.entryContent);
            AddEntry(entry.ID, entry.tooltip);
        }
#if UNITY_EDITOR
        //Debug.Log("scr_System_tooltipDictionary : Initialization Complete with [" + dict.Count + "] entries.");
#endif
        dictionary_Unsorted = null;
    }

    public TextAsset json_dict_unsorted;    // UNITY EDITOR EXTERNAL PROPERTY VALUE
    private Dictionary<string, string> dict;

    public string FindEntry(string name_or_id)
    {
        if (dict.ContainsKey(name_or_id))
        {
            return dict[name_or_id] as string;
        }
        else
        {
            return null;
        }
    }

    public bool AddEntry(string name_or_id, string content)
    {
        if (dict.ContainsKey(name_or_id))
        {
            if (!(name_or_id == "trait_neutral"))
            {
                Debug.Log("System_Dictionary addEntry : found duplicate name_or_id [" + name_or_id + "]");
            }

            return false;
        }
        else
        {
            dict.Add(name_or_id, content);
            return true;
        }
    }

}

[System.Serializable]
public class Dictionary_Unsorted
{
    public List<Dict_Entry> list = new List<Dict_Entry>();
}

[System.Serializable]
public class Dict_Entry {
    public string ID = "";
    public string tooltip = "";
}
