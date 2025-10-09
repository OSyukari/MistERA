using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Unity;
using UnityEngine.UI;
public class scr_resolveEv : MonoBehaviour
{
    public TMP_Text timeStamp;
    public scr_SelectableText button;
    public scr_HoverableText preText, postText;
    public Image image;
    public RectTransform SelfRect, preEvRect, EvRect, postEvRect;
    private void Awake()
    {
        image.color = scr_System_CentralControl.current.DisplaySetting.BackgroundColor_Transparent.Color;
    }

}
