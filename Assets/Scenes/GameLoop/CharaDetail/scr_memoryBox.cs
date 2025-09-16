using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Unity;
using UnityEngine.UI;

public class scr_memoryBox : MonoBehaviour
{
    public TMP_Text timeStamp;
    public scr_HoverableText memText;
    public Image image;
    public RectTransform SelfRect;
    private void Awake()
    {
        image.color = scr_System_CentralControl.current.DisplaySetting.BackgroundColor_Transparent.Color;
    }

}
