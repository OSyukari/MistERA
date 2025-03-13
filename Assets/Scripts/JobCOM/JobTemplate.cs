using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;

[System.Serializable]
public class JobTemplate : ISerializationCallbackReceiver
{
    [SerializeField] public string jobBaseClass;
    [SerializeField] public string ID;

    [SerializeField] public string tooltip;
    [SerializeField] public string displayname;

    // Unique Datas

    [SerializeField] public List<string> doerBodyTags;
    [SerializeField] public List<string> receiverBodyTags;


    public bool IsTemplateValid()
    {
        switch (jobBaseClass)
        {
            default:break;
        }
        return false;
    }

    public bool IsActorValid(int doerRefID, int receiverRefID = -1)
    {
        return false;
    }

    public void OnBeforeSerialize()
    {

    }

    public void OnAfterDeserialize()
    {

    }
}




