using System;
using UnityEngine;

public class scr_actionHolder : MonoBehaviour
{
    public RectTransform selfRect;
    public RectTransform innerObject;
    public RectTransform messageList;
    public scr_SelectableText toggleVisibility;
    public RectTransform titles;

    // filled post creation
    public DateTime source_timestamp;
    public MessageCollect source;
    public ActionPackageRecords ap = null;
    public I_Records rec = null;

    // this data only lives in the editor ui
    bool active = true;

    public bool Activate
    {
        get
        {
            return active;
        }
        set
        {
            active = value;
            innerObject.gameObject.SetActive(active);
        }
    }
}
