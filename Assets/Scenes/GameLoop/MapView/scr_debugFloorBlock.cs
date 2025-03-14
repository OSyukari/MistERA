using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class scr_debugFloorBlock : MonoBehaviour
{
    public TMP_Text floorID;
    public RectTransform Debug_RoomList;
 
    public void Clear()
    {
        while (Debug_RoomList.transform.childCount > 0)
        {
            DestroyImmediate(Debug_RoomList.transform.GetChild(0).gameObject);
        }
    }
}
