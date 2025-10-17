using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class scr_Panel_CurrentChars : MonoBehaviour
{
    private RectTransform thisBox;
    public RectTransform prefab_iconBox;
    public Canvas canvas;


    private void Start()
    {
        //scr_System_CampaignManager.current.displayAttach(this);
        thisBox = this.GetComponent<RectTransform>();
        scr_System_CampaignManager.current.Observer_UpdateNotice += OnUpdateNotice;
        //scr_System_CampaignManager.current.Observer_CurrentRoom += OnCurrentRoomUpdate;
        scr_UpdateHandler.current.Observer_PostUpdateTime_3 += OnPostUpdateTime3;

        UpdateCharaCache();
    }

    private void OnPostUpdateTime3()
    {
        UpdateCharaCache();
    }
    private void OnCurrentRoomUpdate(int updateOrder, Room_Instance room) 
    {
        if (updateOrder != 2) return;
        UpdateCharaCache();
    }

    private void OnUpdateNotice(bool b)
    {
        UpdateCharaCache();
    }

    private void UpdateCharaCache()
    {
        for (int i = trackedRefs.Count - 1; i >= 0; i--)
        {
            if (!scr_System_CampaignManager.current.CharaRefInCurrentRoom.Contains(trackedRefs[i])) trackedRefs.RemoveAt(i);
        }
        foreach (var i in scr_System_CampaignManager.current.CharaRefInCurrentRoom) AddChara(i);
    }

    public List<int> trackedRefs = new List<int>();

    public void AddChara(int refID)
    {
        if (refID > 0 && !trackedRefs.Contains(refID))
        {
            trackedRefs.Add(refID);
            RectTransform rect = Instantiate(prefab_iconBox);
            rect.SetParent(this.GetComponent<RectTransform>(), false);
            scr_CharIconBox b = rect.GetComponent<scr_CharIconBox>();
            b.InitializeWithArgument(refID, canvas);
        }

        
    }

}
