using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

public class scr_PointerEnterNotifier : MonoBehaviour, IPointerEnterHandler
{
    public void OnPointerEnter(PointerEventData eventData)
    {
        if (parent != null && buttonID != -1) parent.Notify(buttonID);
    }

    public scr_Menu parent = null;
    public int buttonID = -1;

    public void Initialize(scr_Menu parent, int buttonID)
    {
        this.parent = parent;
        this.buttonID = buttonID;
    }
}
