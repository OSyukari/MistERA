using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using System;
using System.Linq;

public class scr_panel_PlayerInfo : MonoBehaviour
{
    public RectTransform parentBox;

    public scr_HoverableText hp, mp, st, en;
    public scr_HoverableText fullname;

    private void Start()
    {
        //update = 0.0f;
        scr_System_CampaignManager.current.Observer_UpdateNotice += RefreshUpdateNotice;
        scr_UpdateHandler.current.Observer_PostUpdateTime_3 += PostUpdate;
        //Refresh();

        while (StatusBox.transform.childCount > 0)
        {
            DestroyImmediate(StatusBox.transform.GetChild(0).gameObject);
        }

        Refresh();
    }

    Character_Trainable Chara { get { 
            var chara = scr_System_CampaignManager.current.FindInstanceByID(0);
            return chara;
        } }

    //private float update;

    /*
    private void Update()
    {
        update += Time.deltaTime;
        if (update > 1.0f) 
        {
            update = 0.0f;
            Refresh();
        }

    }*/


    private void RefreshUpdateNotice(bool v)
    {
        Refresh();
    }

    private void PostUpdate()
    {
        Refresh();
    }


    public void Refresh()
    {
        //if (scr_UpdateHandler.current.Updating && !scr_UpdateHandler.current.isLastUpdate()) return;
        var chara = Chara;
        if (chara != null)
        {
            fullname.SetText(chara.FullName);

            chara.Stats.HP.Draw(hp);
            chara.Stats.MP.Draw(mp);
            chara.Stats.Stamina.Draw(st);
            chara.Stats.Energy.Draw(en);

            RefreshStatusBox();
        }
        else
        {
            fullname.SetText("");

            hp.SetText("");
            mp.SetText("");
            st.SetText("");
            en.SetText("");
        }
        parentBox.ForceUpdateRectTransforms();
    }

    public RectTransform StatusBox;
    public RectTransform prefab_text_link;

    Dictionary<Status_Instance, scr_HoverableText> trackedStatus = new Dictionary<Status_Instance, scr_HoverableText>();
    List<Status_Instance> _scratch_toRemove = new List<Status_Instance>();
    HashSet<Status_Instance> _scratch_displayableSet = new HashSet<Status_Instance>();

    private void RefreshStatusBox()
    {
        var displayables = Chara.Stats.StatusInstances;

        _scratch_displayableSet.Clear();
        for (int i = 0; i < displayables.Count; i++)
        {
            if (displayables[i].Displayable) _scratch_displayableSet.Add(displayables[i]);
        }

        _scratch_toRemove.Clear();
        foreach (var key in trackedStatus.Keys)
        {
            if (!_scratch_displayableSet.Contains(key)) _scratch_toRemove.Add(key);
        }

        for (int i = 0; i < _scratch_toRemove.Count; i++)
        {
            var r = _scratch_toRemove[i];
            var obj = trackedStatus[r];
            trackedStatus.Remove(r);
            DestroyImmediate(obj.gameObject);
        }

        foreach (var a in _scratch_displayableSet)
        {
            if (trackedStatus.ContainsKey(a)) continue;
            RectTransform box = Instantiate(prefab_text_link);
            box.SetParent(StatusBox, false);
            trackedStatus.Add(a, box.GetComponent<scr_HoverableText>());
        }

        foreach(var si in trackedStatus)
        {
            UI_Utility.Draw(si.Key, si.Value);
        }
        /*

        while (StatusBox.transform.childCount > 0)
        {
            DestroyImmediate(StatusBox.transform.GetChild(0).gameObject);
        }
        foreach (var si in Chara.Stats.StatusInstances_Displayable)
        {
            if (si.SeverityDisplayName != "")
            {
                RectTransform box = Instantiate(prefab_text_link);
                box.SetParent(StatusBox, false);
                UI_Utility.Draw(si, box.GetComponent<scr_HoverableText>());
            }
        }*/
    }

}
