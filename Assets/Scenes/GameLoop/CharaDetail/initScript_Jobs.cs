using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class initScript_Relations : MonoBehaviour
{
    public scr_SelectableText homeFaction, homeFactionTemp;
    public TMP_Text charaComment;
    public scr_HoverableText viewHidden;
    public scr_memoryBox prefab_MemoryEntry;
    public scr_memoryDaySplit prefab_DaySplit;
    //public RectTransform workFactionBox, workFactionsPrefab;
    public TMP_Text workFactionsNameList;
    List<RectTransform> listRelationship = new List<RectTransform>();
    public void InitializeData(Character_Trainable c, scr_Menu_CharaDetail parent)
    {
        if (c == null) return;
        listRelationship.Clear();

        homeFaction.SetText(c.FactionManager.Faction_Home == null ? " - " : c.FactionManager.Faction_Home.FactionDisplayName);
        homeFactionTemp.SetText(c.FactionManager.Faction_Home_Temporary == null ? " - " : c.FactionManager.Faction_Home_Temporary.FactionDisplayName);


        string s = "";
        foreach (var i in c.FactionManager.WorkFactions)
        {
            s += i.FactionDisplayName + "  ";
        }
        workFactionsNameList.text = s.Length > 0 ? s : "no work factions";

        bool safe = scr_System_CentralControl.current.isSafeMode;

        //listRelationship = new List<RectTransform>();
        foreach (Character_Relationship rel in c.Relationships.Relationships)
        {
            RectTransform rect = Instantiate(parent.prefab_boxRelationship);
            rect.SetParent(parent.boxRelationshipList, false);
            listRelationship.Add(rect);
            var scrbox = rect.GetComponent<scr_box_relationship>();
            RelationshipManager.Draw(rel, scrbox);

            if (safe)
            {
                scrbox.desireBox.gameObject.SetActive(false);
            }
        }

        if (c.Template != null) charaComment.SetText(LocalizeDictionary.QueryThenParse( c.Template.characterComment));
        else charaComment.SetText("");

        DateTime current = scr_System_Time.current.getCurrentTime();
        DateTime lastTime = scr_System_Time.current.getCurrentTime();
        bool first = true;
        bool shorten = false;
        string lastString = Utility.GetRelativeDayString(current, lastTime);

        List<string> _hidden = new List<string>();
        foreach(var i in c.Relationships.GenericRelationship)
        {
            _hidden.Add($"{i.Key} {i.Value.Debug_RelationshipScores}");
        }
        if (_hidden.Count > 0) viewHidden.SetExternalTooltip(String.Join("\n", _hidden));
        else viewHidden.SetExternalTooltip("none");


        for (int i = c.Memory.Entries.Count - 1; i >= 0; i--)// Memory_Entry mem in chara.MemoryManager.entries)
        {
            var currEntry = c.Memory.Entries[i];
            if (shorten || (current - currEntry.FinalEndTime).Days >= 7)
            {
                shorten = true;
            }
            var last2 = Utility.GetRelativeDayString(current, currEntry.FinalEndTime);

            if (first || lastString != last2)
            {
                first = false;
                lastTime = currEntry.FinalEndTime;
                lastString = last2;
                var rect = Instantiate(prefab_DaySplit);
                rect.selfRect.SetParent(parent.boxMemoriesList, false);
                rect.text.SetText($"{lastString}");
            }
            //if (!chara.Memory.Entries[i].isValid) continue;
            scr_memoryBox line = Instantiate(prefab_MemoryEntry);
            line.gameObject.transform.SetParent(parent.boxMemoriesList, false);
            c.Memory.Entries[i].Draw(line, shorten);
            //line.SetText(chara.MemoryManager.entries[i]);

            //if (entry.Tags.Count > 0) prefab_MemoryEntry.GetComponent<scr_HoverableText>().SetExternalTooltip("Relevant Tags: " + String.Join(" ", entry.Tags));
        }
    }

    protected void OnDestroy()
    {
        listRelationship.Clear();
    }

}
